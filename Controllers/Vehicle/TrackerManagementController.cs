using CarCareTracker.External.Interfaces;
using CarCareTracker.Filter;
using CarCareTracker.Models;
using CarCareTracker.Models.LoggerSync;
using Microsoft.AspNetCore.Mvc;

namespace CarCareTracker.Controllers
{
    public partial class VehicleController
    {
        [TypeFilter(typeof(CollaboratorFilter))]
        [HttpGet]
        public IActionResult GetTrackerManagement(int vehicleId)
        {
            var firmware = _firmwareDataAccess.GetLatestActiveFirmware();
            var recentFiles = _receivedFileDataAccess.GetRecentFiles(vehicleId);
            var allFirmware = _firmwareDataAccess.GetAllFirmware();

            // Try to get latest battery voltage and sat count from most recent telemetry
            double? batteryVoltage = null;
            int? satCount = null;
            int? sdTotalMb = null;
            int? sdFreeMb = null;
            string runningFirmwareVersion = "—";

            var latest = _latestCache.GetLatest(vehicleId, "LOG");
            if (latest != null)
            {
                if (latest.Fields.TryGetValue("bat_v", out var batStr) &&
                    double.TryParse(batStr, out var batVal))
                {
                    batteryVoltage = batVal;
                }
                if (latest.Fields.TryGetValue("sats", out var satStr) &&
                    int.TryParse(satStr, out var satVal))
                {
                    satCount = satVal;
                }
            }


            // Get SD card info from latest STATUS record
            var latestStatus = _latestCache.GetLatest(vehicleId, "STATUS");
            if (latestStatus != null)
            {
                if (latestStatus.Fields.TryGetValue("sd_total_mb", out var sdTotalStr) &&
                    int.TryParse(sdTotalStr, out var sdTotal))
                {
                    sdTotalMb = sdTotal;
                }
                if (latestStatus.Fields.TryGetValue("sd_free_mb", out var sdFreeStr) &&
                    int.TryParse(sdFreeStr, out var sdFree))
                {
                    sdFreeMb = sdFree;
                }
                if (latestStatus.Fields.TryGetValue("firmware_version", out var fwVerStr))
                {
                    runningFirmwareVersion = fwVerStr;
                }
            }
            
            // Read firmware source version from Tracker submodule CMakeLists.txt
            string sourceVersion = "Unknown";
            var cmakePath = Path.Combine(_webEnv.ContentRootPath, "..", "Tracker", "CMakeLists.txt");
            if (System.IO.File.Exists(cmakePath))
            {
                var lines = System.IO.File.ReadAllLines(cmakePath);
                foreach (var line in lines)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"set\(PROJECT_VER\s+""(.+)""\)");
                    if (match.Success)
                    {
                        sourceVersion = match.Groups[1].Value;
                        break;
                    }
                }
            }

            var model = new TrackerHealthViewModel
            {
                VehicleId = vehicleId,
                CurrentFirmwareVersion = firmware?.Version ?? "Unknown",
                LastSyncTime = recentFiles.Any() ? recentFiles.First().ReceivedAt.ToString("g") : "Never",
                LastSyncFile = recentFiles.Any() ? recentFiles.First().Filename : "—",
                LatestBatteryVoltage = batteryVoltage,
                LatestSatCount = satCount,
                FirmwareHistory = allFirmware,
                ReceivedFiles = recentFiles,
                SourceFirmwareVersion = sourceVersion,
                SdTotalMb = sdTotalMb,
                SdFreeMb = sdFreeMb,
                RunningFirmwareVersion = runningFirmwareVersion,


            };

            return PartialView("Tracker/_TrackerManagement", model);
        }

        [HttpPost]
        public async Task<IActionResult> UploadFirmwareFromUI(string notes, IFormFile firmwareFile)
        {
            if (firmwareFile == null || firmwareFile.Length == 0)
            {
                return Json(OperationResponse.Failed(".bin file is required"));
            }

            try
            {
                var directory = Path.Combine(_webEnv.ContentRootPath, "data", "firmware");
                Directory.CreateDirectory(directory);
                // Extract version from ESP-IDF app descriptor in the .bin
                string version;
                using (var peekStream = firmwareFile.OpenReadStream())
                {
                    var header = new byte[0x40];
                    await peekStream.ReadAsync(header, 0, header.Length);
                    // Magic 0xABCD5432 at offset 0x20, version string at 0x30
                    if (header.Length >= 0x40 &&
                        header[0x20] == 0x32 && header[0x21] == 0x54 &&
                        header[0x22] == 0xCD && header[0x23] == 0xAB)
                    {
                        var verBytes = new byte[32];
                        Array.Copy(header, 0x30, verBytes, 0, 16);
                        version = System.Text.Encoding.ASCII.GetString(verBytes).TrimEnd('\0');
                    }
                    else
                    {
                        return Json(OperationResponse.Failed("Could not read version from .bin file — not a valid ESP-IDF firmware"));
                    }
                }
                var fileName = $"firmware_{version}.bin";
                var filePath = Path.Combine(directory, fileName);

                using var md5 = System.Security.Cryptography.MD5.Create();
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await firmwareFile.CopyToAsync(fileStream);
                }
                // Re-open to compute hash
                using (var readStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var hash = BitConverter.ToString(md5.ComputeHash(readStream)).Replace("-", "").ToLower();
                    var record = new FirmwareRecord
                    {
                        Version = version,
                        FilePath = fileName,
                        Md5Hash = hash,
                        FileSize = readStream.Length,
                        Notes = notes ?? string.Empty,
                        IsActive = true
                    };
                    _firmwareDataAccess.SaveFirmware(record);
                }
                return Json(OperationResponse.Succeed($"Firmware {version} uploaded"));
            }
            catch (Exception ex)
            {
                return Json(OperationResponse.Failed(ex.Message));
            }
        }
                [HttpPost]
        public async Task<IActionResult> UploadTelemetryFromUI(int vehicleId, List<IFormFile> files, List<string> relativePaths, bool detectDrives = true)
        {
            if (files == null || files.Count == 0)
            {
                return Json(OperationResponse.Failed("No files were selected"));
            }
            if (files.Count != relativePaths.Count)
            {
                return Json(OperationResponse.Failed("File/path count mismatch"));
            }

            try
            {
                var validTypes = new[] { "LOG", "IMU", "MON" };
                var received = _receivedFileDataAccess.GetReceivedFilenames(vehicleId);

                int savedCount = 0;
                int skippedCount = 0;
                bool anyLog = false;

                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    var relPath = relativePaths[i].Replace("..", "").TrimStart('/');

                    var fileType = relPath.Split('/')[0];
                    if (!validTypes.Contains(fileType))
                    {
                        continue; // not a telemetry file, ignore
                    }

                    if (received.Contains(relPath))
                    {
                        skippedCount++;
                        continue; // already synced, don't re-ingest
                    }

                    var directory = Path.Combine(_webEnv.ContentRootPath, "data", "telemetry", vehicleId.ToString());
                    var filePath = Path.Combine(directory, relPath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(fileStream);
                    }

                    _receivedFileDataAccess.MarkFileReceived(vehicleId, relPath);
                    // IMU files are saved to disk above but NOT parsed into rows — they're the
                    // raw high-frequency signal, kept for on-demand FFT later. The accel/gyro
                    // you actually plot (ax_avg, etc.) lives in the LOG files, not here.
                    if (fileType != "IMU")
                    {
                        _telemetryParserService.ParseAndStoreCsvFile(vehicleId, filePath, fileType);
                    }

                    savedCount++;
                    if (fileType == "LOG") anyLog = true;
                }

                int newDriveCount = 0;
                if (detectDrives && anyLog)
                {
                    var latestDriveTime = _driveRecordDataAccess.GetLatestDriveUnixTimeByVehicleId(vehicleId);
                    var newDrives = _telemetryParserService.DetectDrives(vehicleId, latestDriveTime);
                    if (newDrives.Any())
                    {
                        _driveRecordDataAccess.SaveDriveRecordBatch(newDrives);
                        newDriveCount = newDrives.Count;
                    }
                }

                return Json(OperationResponse.Succeed(
                    $"Uploaded {savedCount} file(s), skipped {skippedCount} already-synced. {newDriveCount} new drive(s) detected."));

            }
            catch (Exception ex)
            {
                return Json(OperationResponse.Failed(ex.Message));
            }
        }

        [HttpPost]
        public IActionResult DetectDrivesFromUI(int vehicleId)
        {
            var latestDriveTime = _driveRecordDataAccess.GetLatestDriveUnixTimeByVehicleId(vehicleId);
            var newDrives = _telemetryParserService.DetectDrives(vehicleId, latestDriveTime);
            if (newDrives.Any())
            {
                _driveRecordDataAccess.SaveDriveRecordBatch(newDrives);
            }
            return Json(OperationResponse.Succeed($"{newDrives.Count} new drive(s) detected"));
        }
    }
}


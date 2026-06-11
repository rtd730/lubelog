using CarCareTracker.External.Interfaces;
using CarCareTracker.Filter;
using CarCareTracker.Models;
using CarCareTracker.Models.LoggerSync;
using Microsoft.AspNetCore.Mvc;
using CarCareTracker.Logic; 

namespace CarCareTracker.Controllers
{
    public partial class APIController
    {
        [TypeFilter(typeof(APIKeyFilter), Arguments = new object[] { HouseholdPermission.View })]
        [HttpPost]
        [Route("/api/tracker/sync/check")]
        public IActionResult TrackerSyncCheck([FromBody] TrackerSyncCheckRequest request)
        {
            if (request == null || request.VehicleId == default)
            {
                Response.StatusCode = 400;
                return Json(OperationResponse.Failed("Must provide a valid vehicleId"));
            }
            var received = _receivedFileDataAccess.GetReceivedFilenames(request.VehicleId);
            var needed = request.Files.Where(f => !received.Contains(f)).ToList();
            // Store SD card info if reported
            if (request.SdTotalMb.HasValue || request.SdFreeMb.HasValue || !string.IsNullOrEmpty(request.FirmwareVersion))
            {
                var sdRecord = new TelemetryRecord
                {
                    VehicleId = request.VehicleId,
                    UnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Datetime = DateTime.UtcNow.ToString("o"),
                    FileType = "STATUS",
                    SourceFilename = "sync_check",
                    Fields = new Dictionary<string, string>()
                };
                if (request.SdTotalMb.HasValue)
                    sdRecord.Fields["sd_total_mb"] = request.SdTotalMb.Value.ToString();
                if (request.SdFreeMb.HasValue)
                    sdRecord.Fields["sd_free_mb"] = request.SdFreeMb.Value.ToString();
                if (!string.IsNullOrEmpty(request.FirmwareVersion))
                    sdRecord.Fields["firmware_version"] = request.FirmwareVersion;
                _telemetryDataAccess.SaveTelemetryBatch(new List<TelemetryRecord> { sdRecord });
                _latestCache.Update(sdRecord);

            }
            return Json(new { needed });
        }

        [TypeFilter(typeof(APIKeyFilter), Arguments = new object[] { HouseholdPermission.Edit })]
        [HttpPost]
        [Route("/api/tracker/sync/upload")]
        public async Task<IActionResult> TrackerSyncUpload(int vehicleId, string filename)
        {
            if (vehicleId == default || string.IsNullOrWhiteSpace(filename))
            {
                Response.StatusCode = 400;
                return Json(OperationResponse.Failed("Must provide vehicleId and filename"));
            }
            try
            {
                var directory = Path.Combine(_webEnv.ContentRootPath, "data", "telemetry", vehicleId.ToString());
                Directory.CreateDirectory(directory);
                var safeName = filename.Replace("..", "").TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var filePath = Path.Combine(directory, safeName);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await Request.Body.CopyToAsync(fileStream);
                }
                _receivedFileDataAccess.MarkFileReceived(vehicleId, filename);
                
                // Determine file type from path (e.g., "LOG/2026/file.CSV" → "LOG")
                var fileType = safeName.Split(Path.DirectorySeparatorChar, '/')[0];

                // Accept-then-parse: the bytes are safely on disk and marked received,
                // so the tracker gets its 200 immediately and moves on. The parse +
                // drive detection run on the background worker, so a backlog of uploads
                // can never block the request or blow the tracker's 30s HTTP timeout.
                // IMU is archived only — never parsed.
                if (!fileType.Equals("IMU", StringComparison.OrdinalIgnoreCase))
                {
                    _parseQueue.Enqueue(new ParseJob(vehicleId, filePath, fileType));
                }

                return Json(OperationResponse.Succeed("File Saved"));

            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(OperationResponse.Failed(ex.Message));
            }
        }


        [TypeFilter(typeof(APIKeyFilter), Arguments = new object[] { HouseholdPermission.View })]
        [HttpGet]
        [Route("/api/tracker/firmware/latest")]
        public IActionResult GetLatestFirmware()
        {
            var firmware = _firmwareDataAccess.GetLatestActiveFirmware();
            if (firmware == null)
            {
                Response.StatusCode = 204;
                return Json(null);
            }
            return Json(new { version = firmware.Version, size = firmware.FileSize, md5 = firmware.Md5Hash });
        }

        [TypeFilter(typeof(APIKeyFilter), Arguments = new object[] { HouseholdPermission.Edit })]
        [HttpPost]
        [Route("/api/tracker/firmware/upload")]
        public async Task<IActionResult> UploadFirmware(string version, string notes = "")
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                Response.StatusCode = 400;
                return Json(OperationResponse.Failed("Must provide version"));
            }
            try
            {
                var directory = Path.Combine(_webEnv.ContentRootPath, "data", "firmware");
                Directory.CreateDirectory(directory);
                var fileName = $"firmware_{version}.bin";
                var filePath = Path.Combine(directory, fileName);
                using var md5 = System.Security.Cryptography.MD5.Create();
                using var fileStream = new FileStream(filePath, FileMode.Create);
                await Request.Body.CopyToAsync(fileStream);
                fileStream.Position = 0;
                var hash = BitConverter.ToString(md5.ComputeHash(fileStream)).Replace("-", "").ToLower();
                var record = new FirmwareRecord
                {
                    Version = version,
                    FilePath = fileName,
                    Md5Hash = hash,
                    FileSize = fileStream.Length,
                    Notes = notes,
                    IsActive = true
                };
                _firmwareDataAccess.SaveFirmware(record);
                return Json(OperationResponse.Succeed("Firmware uploaded"));
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(OperationResponse.Failed(ex.Message));
            }
        }

        [TypeFilter(typeof(APIKeyFilter), Arguments = new object[] { HouseholdPermission.View })]
        [HttpGet]
        [Route("/api/tracker/firmware/download")]
        public IActionResult DownloadFirmware()
        {
            var firmware = _firmwareDataAccess.GetLatestActiveFirmware();
            if (firmware == null)
            {
                Response.StatusCode = 204;
                return Json(null);
            }
            var filePath = Path.Combine(_webEnv.ContentRootPath, "data", "firmware", firmware.FilePath);
            if (!System.IO.File.Exists(filePath))
            {
                Response.StatusCode = 404;
                return Json(OperationResponse.Failed("Firmware file not found"));
            }
            return PhysicalFile(filePath, "application/octet-stream", firmware.FilePath);
        }

    }
}

using CarCareTracker.Filter;
using CarCareTracker.Models;
using GeoTimeZone;
using Microsoft.AspNetCore.Mvc;

namespace CarCareTracker.Controllers
{
    public partial class VehicleController
    {
        [TypeFilter(typeof(CollaboratorFilter))]
        [HttpGet]
        public IActionResult GetDriveRecordsByVehicleId(int vehicleId)
        {
            var drives = _driveRecordDataAccess.GetDriveRecordsByVehicleId(vehicleId);
            
            // If cache is empty, try detecting drives from stored telemetry
            if (!drives.Any())
            {
                drives = _telemetryParserService.DetectDrives(vehicleId);
                if (drives.Any())
                {
                    _driveRecordDataAccess.SaveDriveRecordBatch(drives);
                }
            }

            var userConfig = _config.GetUserConfig(User);
            var viewModels = drives.Select(d => new DriveRecordViewModel
            {
                Id = d.Id,
                VehicleId = d.VehicleId,
                Date = ConvertToLocalTime(d.StartUnixTime, d.StartLat, d.StartLng).ToShortDateString(),
                StartTimeStr = ConvertToLocalTime(d.StartUnixTime, d.StartLat, d.StartLng).ToString("HH:mm:ss"),
                EndTimeStr = ConvertToLocalTime(d.EndUnixTime, d.EndLat, d.EndLng).ToString("HH:mm:ss"),
                DurationStr = FormatDuration(d.EndTime - d.StartTime),
                DistanceMiles = d.DistanceMiles,


                AvgSpeedMph = d.AvgSpeedMph,
                MaxSpeedMph = d.MaxSpeedMph,
                AvgRpm = d.AvgRpm,
                AvgCoolantTemp = d.AvgCoolantTemp.HasValue ? $"{d.AvgCoolantTemp.Value:F1}°" : "—",
                AvgTransTemp = d.AvgTransTemp.HasValue ? $"{d.AvgTransTemp.Value:F1}°" : "—",
                AvgOat = d.AvgOat.HasValue ? $"{d.AvgOat.Value:F1}°" : "—",
                AvgCabinTemp = d.AvgCabinTemp.HasValue ? $"{d.AvgCabinTemp.Value:F1}°" : "—",
                StartCoords = d.StartLat != 0 ? $"{d.StartLat:F4}, {d.StartLng:F4}" : "—",
                EndCoords = d.EndLat != 0 ? $"{d.EndLat:F4}, {d.EndLng:F4}" : "—",
                FuelUsed = d.FuelConsumedGallons.HasValue ? $"{d.FuelConsumedGallons.Value:F2} gal" : "—",
                AvgMpg = d.FuelConsumedGallons.HasValue && d.FuelConsumedGallons.Value > 0
                    ? $"{(d.DistanceMiles / d.FuelConsumedGallons.Value):F1}"
                    : "—",
                AvgMpgRaw = d.FuelConsumedGallons.HasValue && d.FuelConsumedGallons.Value > 0
                    ? d.DistanceMiles / d.FuelConsumedGallons.Value
                    : null,
                Notes = d.Notes,
                SourceFilename = d.SourceFilename,
                StartUnixTime = d.StartUnixTime,
                EndUnixTime = d.EndUnixTime,
                FuelConsumedGallons = d.FuelConsumedGallons
            }).ToList();

            if (userConfig.UseDescending)
            {
                viewModels = viewModels.OrderByDescending(x => DateTime.Parse(x.Date)).ToList();
            }

            var container = new DriveRecordViewModelContainer
            {
                DriveRecords = viewModels,
                HasTelemetryData = drives.Any(),
                LastParsedAt = drives.Any() 
                    ? drives.Max(d => d.EndTime).ToString("g") 
                    : "Never"
            };
            return PartialView("Drive/_Drives", container);
        }

        [HttpPost]
        public IActionResult RefreshDriveCache(int vehicleId)
        {
            if (!_userLogic.UserCanEditVehicle(GetUserID(), vehicleId, HouseholdPermission.Edit))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            // Clear old telemetry and re-parse all CSV files
            _telemetryDataAccess.DeleteTelemetryByVehicleId(vehicleId);
            var parsedCount = _telemetryParserService.ParseAllExistingFiles(vehicleId);
            // Clear and re-detect drives
            _driveRecordDataAccess.DeleteDriveRecordsByVehicleId(vehicleId);
            var drives = _telemetryParserService.DetectDrives(vehicleId);
            if (drives.Any())
            {
                _driveRecordDataAccess.SaveDriveRecordBatch(drives);
            }
            return Json(OperationResponse.Succeed($"Parsed {parsedCount} new files, detected {drives.Count} drives"));
        }



        [TypeFilter(typeof(CollaboratorFilter))]
        [HttpGet]
        public IActionResult GetDriveRecordDetails(int driveId)
        {
            var drive = _driveRecordDataAccess.GetDriveRecordById(driveId);
            if (drive == null)
            {
                return Json(OperationResponse.Failed("Drive not found"));
            }
            return PartialView("Drive/_DriveDetailModal", drive);
        }

        [HttpPost]
        public IActionResult SaveDriveNotes(int driveId, string notes)
        {
            var drive = _driveRecordDataAccess.GetDriveRecordById(driveId);
            if (drive == null)
            {
                return Json(OperationResponse.Failed("Drive not found"));
            }
            if (!_userLogic.UserCanEditVehicle(GetUserID(), drive.VehicleId, HouseholdPermission.Edit))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            drive.Notes = notes ?? string.Empty;
            _driveRecordDataAccess.SaveDriveRecordBatch(new List<DriveRecord> { drive });
            return Json(OperationResponse.Succeed("Notes saved"));
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
            {
                return $"{(int)duration.TotalHours}h {duration.Minutes:D2}m";
            }
            return $"{duration.Minutes}m {duration.Seconds:D2}s";
        }

        private static DateTime ConvertToLocalTime(long unixTime, double lat, double lng)
        {
            var utcTime = DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;
            if (lat == 0 && lng == 0)
            {
                return utcTime;
            }
            try
            {
                var tzIana = GeoTimeZone.TimeZoneLookup.GetTimeZone(lat, lng).Result;
                var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(tzIana);
                return TimeZoneInfo.ConvertTimeFromUtc(utcTime, tzInfo);
            }
            catch
            {
                return utcTime;
            }
        }

        [HttpPost]
        public IActionResult ReassignDriveToVehicle(int driveId, int newVehicleId)
        {
            var drive = _driveRecordDataAccess.GetDriveRecordById(driveId);
            if (drive == null)
            {
                return Json(OperationResponse.Failed("Drive not found"));
            }
            if (!_userLogic.UserCanEditVehicle(GetUserID(), drive.VehicleId, HouseholdPermission.Edit))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            // Move underlying telemetry rows to new vehicle
            _telemetryDataAccess.UpdateVehicleIdBySourceFilename(
                drive.VehicleId, drive.SourceFilename, newVehicleId);
            // Remove from current vehicle's drive cache and re-save
            var allDrives = _driveRecordDataAccess.GetDriveRecordsByVehicleId(drive.VehicleId);
            _driveRecordDataAccess.DeleteDriveRecordsByVehicleId(drive.VehicleId);
            var remaining = allDrives.Where(d => d.Id != driveId).ToList();
            if (remaining.Any())
            {
                _driveRecordDataAccess.SaveDriveRecordBatch(remaining);
            }
            return Json(OperationResponse.Succeed($"Drive reassigned to vehicle {newVehicleId}"));
        }
    }
}

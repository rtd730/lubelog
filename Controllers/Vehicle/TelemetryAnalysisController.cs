using CarCareTracker.External.Interfaces;
using CarCareTracker.Filter;
using CarCareTracker.Logic;
using CarCareTracker.Models;
using CarCareTracker.Models.LoggerSync;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace CarCareTracker.Controllers
{
    public partial class VehicleController
    {
        [TypeFilter(typeof(CollaboratorFilter))]
        [HttpGet]
        public IActionResult GetChartsView(int vehicleId)
        {
            return PartialView("Telemetry/_Charts", vehicleId);
        }

        [TypeFilter(typeof(CollaboratorFilter))]
        [HttpGet]
        public IActionResult GetAvailableFields(int vehicleId, string fileType = "")
        {
            List<string> rawFields;
            if (!string.IsNullOrEmpty(fileType))
            {
                rawFields = _latestCache.GetFieldNames(vehicleId, fileType);
            }
            else
            {
                // Combine field names from all file types
                var combined = new HashSet<string>();
                foreach (var ft in new[] { "LOG", "IMU", "MON" })
                {
                    foreach (var f in _latestCache.GetFieldNames(vehicleId, ft))
                        combined.Add(f);
                }
                rawFields = combined.OrderBy(f => f).ToList();
            }
            var fields = _telemetryFieldService.GetAvailableFields(rawFields, vehicleId);
            return Json(fields);
        }

        [TypeFilter(typeof(CollaboratorFilter))]
        [HttpGet]
        public IActionResult GetSourceFilesInRange(int vehicleId, string startDate = "", string endDate = "")
        {
            long startUnix = 0;
            long endUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!string.IsNullOrEmpty(startDate) &&
                DateTime.TryParse(startDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDt))
            {
                startUnix = new DateTimeOffset(startDt, TimeSpan.Zero).ToUnixTimeSeconds();
            }
            if (!string.IsNullOrEmpty(endDate) &&
                DateTime.TryParse(endDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDt))
            {
                endUnix = new DateTimeOffset(endDt.AddDays(1), TimeSpan.Zero).ToUnixTimeSeconds();
            }
            var records = _telemetryDataAccess.GetTelemetryByVehicleIdAndTimeRange(vehicleId, startUnix, endUnix);
            var files = records
                .Where(r => r.SourceFilename != "sync_check")
                .Select(r => new { fileType = r.FileType, filename = r.SourceFilename })
                .Distinct()
                .OrderBy(f => f.filename)
                .ToList();
            return Json(files);
        }

        [TypeFilter(typeof(CollaboratorFilter))]
        [HttpPost]
        public IActionResult GetTelemetryPoints([FromBody] TelemetryQueryRequest request)
        {
            if (request == null || request.VehicleId == default)
            {
                return Json(new { error = "Invalid request" });
            }

            // Parse date range to unix timestamps
            long startUnix = 0;
            long endUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!string.IsNullOrEmpty(request.StartDate) &&
                DateTime.TryParse(request.StartDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDt))
            {
                startUnix = new DateTimeOffset(startDt, TimeSpan.Zero).ToUnixTimeSeconds();
            }
            if (!string.IsNullOrEmpty(request.EndDate) &&
                DateTime.TryParse(request.EndDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDt))
            {
                endUnix = new DateTimeOffset(endDt.AddDays(1), TimeSpan.Zero).ToUnixTimeSeconds();
            }

            var records = _telemetryDataAccess.GetTelemetryDownsampled(
                request.VehicleId, startUnix, endUnix, request.FileType ?? "LOG",
                request.Downsample ? request.MaxPoints : int.MaxValue);

            // Filter by selected source files if provided
            if (request.SourceFilenames != null && request.SourceFilenames.Any())
            {
                var selected = new HashSet<string>(request.SourceFilenames);
                records = records.Where(r => selected.Contains(r.SourceFilename)).ToList();
            }

            // Apply filter if provided
            if (request.Filter != null && request.Filter.Rules.Any())
            {
                records = records.Where(r =>
                    _telemetryParserService.RowPassesFilter(r.Fields, request.Filter)).ToList();
            }

            // Need at least 2 fields for X and Y
            if (request.Fields == null || request.Fields.Count < 2)
            {
                return Json(new { error = "Select X and Y fields" });
            }

            var xField = request.Fields[0];
            var yField = request.Fields[1];
            var colorField = request.Fields.Count >= 3 ? request.Fields[2] : null;

            // Fetch gas price for fuel calculations
            decimal? gasPricePerGallon = null;
            if (request.Fields.Any(f => f.StartsWith("calc:")))
            {
                var gasRecords = _gasRecordDataAccess.GetGasRecordsByVehicleId(request.VehicleId);
                var latest = gasRecords.OrderByDescending(g => g.Date).FirstOrDefault();
                if (latest != null && latest.Gallons > 0)
                    gasPricePerGallon = latest.Cost / latest.Gallons;
            }

            // Build points array
            var points = new List<object>();
            foreach (var r in records)
            {
                var x = _telemetryFieldService.ResolveFieldValue(xField, r.Fields, gasPricePerGallon);
                var y = _telemetryFieldService.ResolveFieldValue(yField, r.Fields, gasPricePerGallon);
                if (x.HasValue && y.HasValue)
                {
                    double? c = null;
                    if (colorField != null)
                    {
                        c = _telemetryFieldService.ResolveFieldValue(colorField, r.Fields, gasPricePerGallon);
                    }
                    points.Add(new { x = x.Value, y = y.Value, label = r.Datetime, c });
                }
            }

            return Json(points);
        }

        [TypeFilter(typeof(CollaboratorFilter))]
        [HttpGet]
        public IActionResult GetFilters(int vehicleId)
        {
            var filters = _filterDefinitionDataAccess.GetFiltersByVehicleId(vehicleId);
            return Json(filters);
        }

        [HttpPost]
        public IActionResult SaveFilter([FromBody] FilterDefinition filter)
        {
            if (filter == null || filter.VehicleId == default)
            {
                return Json(OperationResponse.Failed("Invalid filter"));
            }
            if (!_userLogic.UserCanEditVehicle(GetUserID(), filter.VehicleId, HouseholdPermission.Edit))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            _filterDefinitionDataAccess.SaveFilter(filter);
            return Json(OperationResponse.Succeed("Filter saved"));
        }

        [HttpPost]
        public IActionResult DeleteFilter(int filterId)
        {
            var filter = _filterDefinitionDataAccess.GetFilterById(filterId);
            if (filter == null)
            {
                return Json(OperationResponse.Failed("Filter not found"));
            }
            if (!_userLogic.UserCanEditVehicle(GetUserID(), filter.VehicleId, HouseholdPermission.Edit))
            {
                return Json(OperationResponse.Failed("Access Denied"));
            }
            _filterDefinitionDataAccess.DeleteFilterById(filterId);
            return Json(OperationResponse.Succeed("Filter deleted"));
        }

        // Timeline actions (Phase 2)
        [TypeFilter(typeof(CollaboratorFilter))]
        [HttpGet]
        public IActionResult GetTimelineView(int vehicleId)
        {
            return PartialView("Telemetry/_Timeline", vehicleId);
        }

        [TypeFilter(typeof(CollaboratorFilter))]
        [HttpPost]
        public IActionResult GetTimelineData([FromBody] TelemetryQueryRequest request)
        {
            if (request == null || request.VehicleId == default)
            {
                return Json(new { error = "Invalid request" });
            }

            long startUnix = 0;
            long endUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!string.IsNullOrEmpty(request.StartDate) &&
                DateTime.TryParse(request.StartDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDt))
            {
                startUnix = new DateTimeOffset(startDt, TimeSpan.Zero).ToUnixTimeSeconds();
            }
            if (!string.IsNullOrEmpty(request.EndDate) &&
                DateTime.TryParse(request.EndDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDt))
            {
                endUnix = new DateTimeOffset(endDt.AddDays(1), TimeSpan.Zero).ToUnixTimeSeconds();
            }

            var records = _telemetryDataAccess.GetTelemetryDownsampled(
                request.VehicleId, startUnix, endUnix, request.FileType ?? "LOG",
                request.Downsample ? request.MaxPoints : int.MaxValue);

            // Filter by selected source files if provided
            if (request.SourceFilenames != null && request.SourceFilenames.Any())
            {
                var selected = new HashSet<string>(request.SourceFilenames);
                records = records.Where(r => selected.Contains(r.SourceFilename)).ToList();
            }

            if (request.Fields == null || !request.Fields.Any())
            {
                return Json(new { datasets = new List<object>() });
            }
            // Fetch gas price for fuel calculations
            decimal? gasPricePerGallon = null;
            if (request.Fields.Any(f => f.StartsWith("calc:")))
            {
                var gasRecords = _gasRecordDataAccess.GetGasRecordsByVehicleId(request.VehicleId);
                var latest = gasRecords.OrderByDescending(g => g.Date).FirstOrDefault();
                if (latest != null && latest.Gallons > 0)
                    gasPricePerGallon = latest.Cost / latest.Gallons;
            }

            // Build one dataset per requested field, inserting nulls for time gaps > 60s
            var datasets = new List<object>();
            foreach (var field in request.Fields)
            {
                                var data = new List<object>();
                foreach (var r in records)
                {
                var val = _telemetryFieldService.ResolveFieldValue(field, r.Fields, gasPricePerGallon);
                if (val.HasValue)
                {
                    data.Add(new { x = r.UnixTime * 1000L, y = val.Value });
                }

                }

                var fieldType = field.StartsWith("calc:")
                    ? "LOG"
                    : records.FirstOrDefault(r => r.Fields.ContainsKey(field))?.FileType ?? "LOG";

                datasets.Add(new { label = _telemetryFieldService.GetDisplayName(field), data, fileType = fieldType });

            }

            return Json(new { datasets });

        }
    }
}


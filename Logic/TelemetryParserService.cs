using CarCareTracker.External.Interfaces;
using CarCareTracker.Models;
using CarCareTracker.Models.LoggerSync;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace CarCareTracker.Logic
{
    public interface ITelemetryParserService
    {
        void ParseAndStoreCsvFile(int vehicleId, string filePath, string fileType);
        List<DriveRecord> DetectDrives(int vehicleId, long afterUnixTime = 0);
        bool RowPassesFilter(Dictionary<string, string> row, FilterDefinition filter);
        int ParseAllExistingFiles(int vehicleId);

    }
    public class TelemetryParserService : ITelemetryParserService
    {
        private readonly ITelemetryDataAccess _telemetryDataAccess;
        private readonly ILogger<TelemetryParserService> _logger;
        private readonly ITelemetryLatestCacheService _latestCache;

        private const int MinDrivePoints = 5;
        private const int MinDriveSeconds = 30;
        
        
        public TelemetryParserService(
            ITelemetryDataAccess telemetryDataAccess,
            ILogger<TelemetryParserService> logger,
            ITelemetryLatestCacheService latestCache)
        {
            _telemetryDataAccess = telemetryDataAccess;
            _logger = logger;
            _latestCache = latestCache;
        }

        // --- CSV Parsing ---

        public void ParseAndStoreCsvFile(int vehicleId, string filePath, string fileType)
        {
            try
            {
                var records = new List<TelemetryRecord>();
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    MissingFieldFound = null,
                    HeaderValidated = null,
                    BadDataFound = null,
                    AllowComments = true,
                    Comment = '#'
                };
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fileStream);
                using var csv = new CsvReader(reader, config);
                csv.Read();
                csv.ReadHeader();
                var headers = csv.HeaderRecord ?? Array.Empty<string>();
                while (csv.Read())
                {
                    var fields = new Dictionary<string, string>();
                    foreach (var header in headers)
                    {
                        var trimmedHeader = header.Trim();
                        if (string.IsNullOrEmpty(trimmedHeader)) continue;
                        var value = csv.GetField(header);
                        if (value != null)
                        {
                            fields[trimmedHeader] = value.Trim();
                        }
                    }
                    long unixTime = 0;
                    string datetime = string.Empty;
                    if (fields.TryGetValue("unixTime", out var unixStr))
                    {
                        long.TryParse(unixStr, out unixTime);
                    }
                    if (fields.TryGetValue("datetime", out var dtStr))
                    {
                        datetime = dtStr;
                    }
                    records.Add(new TelemetryRecord
                    {
                        VehicleId = vehicleId,
                        UnixTime = unixTime,
                        Datetime = datetime,
                        FileType = fileType,
                        SourceFilename = Path.GetFileName(filePath),
                        Fields = fields
                    });

                }
                if (records.Any())
                {
                    records = records.OrderBy(r => r.UnixTime).ToList();
                    _telemetryDataAccess.SaveTelemetryBatch(records);
                    _latestCache.UpdateBatch(records);
                    _logger.LogInformation(
                        "Parsed {Count} records from {File} (type: {Type}) for vehicle {VehicleId}",
                        records.Count, Path.GetFileName(filePath), fileType, vehicleId);
                }
                

            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to parse telemetry file {File} for vehicle {VehicleId}",
                    filePath, vehicleId);
            }
        }
        public int ParseAllExistingFiles(int vehicleId)
        {
            var basePath = Path.Combine("data", "telemetry", vehicleId.ToString());
            if (!Directory.Exists(basePath)) return 0;

            // Get all "FileType:Filename" keys already in DB to skip re-parsing
            var existingKeys = _telemetryDataAccess.GetExistingSourceFilenames(vehicleId);
            int parsedCount = 0;

            foreach (var fileType in new[] { "LOG", "IMU", "MON" })
            {
                var folder = Path.Combine(basePath, fileType);
                if (!Directory.Exists(folder)) continue;

                var csvFiles = Directory.GetFiles(folder, "*.CSV", SearchOption.AllDirectories);
                foreach (var csvFile in csvFiles)
                {
                    // ParseAndStoreCsvFile stores just the filename (Path.GetFileName)
                    var key = $"{fileType}:{Path.GetFileName(csvFile)}";
                    if (existingKeys.Contains(key)) continue;

                    try
                    {
                        ParseAndStoreCsvFile(vehicleId, csvFile, fileType);
                        parsedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Skipped file {File} during backfill", csvFile);
                    }
                }
            }
            return parsedCount;
        }

        // --- Drive Detection ---

                public List<DriveRecord> DetectDrives(int vehicleId, long afterUnixTime = 0)
        {
            var allRows = _telemetryDataAccess.GetTelemetryByVehicleIdAndTimeRange(
                vehicleId,
                afterUnixTime,
                long.MaxValue,
                "LOG"
            );
            if (!allRows.Any()) return new List<DriveRecord>();

            // Each LOG file is one drive — group by source filename
            var groups = allRows.GroupBy(r => r.SourceFilename);
            var drives = new List<DriveRecord>();

            foreach (var group in groups)
            {
                var segment = group.OrderBy(r => r.UnixTime).ToList();
                var drive = BuildDriveRecord(vehicleId, segment);
                if (drive != null)
                {
                    drive.SourceFilename = group.Key;
                    drives.Add(drive);
                }
            }
            return drives.OrderBy(d => d.StartUnixTime).ToList();
        }


        private DriveRecord? BuildDriveRecord(int vehicleId, List<TelemetryRecord> segment)
        {
            segment = segment.Where(r => r.UnixTime > 0).ToList();
            if (segment.Count < MinDrivePoints) return null;
            var durationSeconds = segment.Last().UnixTime - segment.First().UnixTime;
            if (durationSeconds < MinDriveSeconds) return null;

            var speeds = new List<double>();
            var rpms = new List<double>();
            var coolants = new List<double>();
            var transTemps = new List<double>();
            var oats = new List<double>();
            var cabinTemps = new List<double>();
            double totalDistanceMiles = 0;
            double? prevLat = null;
            double? prevLng = null;

            foreach (var row in segment)
            {
                double speed = GetDoubleField(row.Fields, "speed") * 0.621371; // km/h to mph
                speeds.Add(speed);
                rpms.Add(GetDoubleField(row.Fields, "rpm"));

                double lat = GetDoubleField(row.Fields, "lat");
                double lng = GetDoubleField(row.Fields, "lng");
                if (prevLat.HasValue && prevLng.HasValue && lat != 0 && lng != 0)
                {
                    totalDistanceMiles += HaversineDistanceMiles(
                        prevLat.Value, prevLng.Value, lat, lng);
                }
                if (lat != 0 && lng != 0)
                {
                    prevLat = lat;
                    prevLng = lng;
                }

                var coolant = GetNullableDoubleField(row.Fields, "coolant");
                if (coolant.HasValue) coolants.Add(coolant.Value);
                var transTemp = GetNullableDoubleField(row.Fields, "trans_temp");
                if (transTemp.HasValue) transTemps.Add(transTemp.Value);
                var oat = GetNullableDoubleField(row.Fields, "OAT_c");
                if (oat.HasValue) oats.Add(oat.Value);
                var cabin = GetNullableDoubleField(row.Fields, "CabinTemp (C)");
                if (cabin.HasValue) cabinTemps.Add(cabin.Value);
            }

            var first = segment.First();
            var last = segment.Last();
            DateTime startTime = ParseDatetime(first);
            DateTime endTime = ParseDatetime(last);

            return new DriveRecord
            {
                VehicleId = vehicleId,
                SourceFilename = string.Empty,
                StartTime = startTime,
                EndTime = endTime,
                DistanceMiles = Math.Round(totalDistanceMiles, 2),
                AvgSpeedMph = speeds.Any() ? Math.Round(speeds.Average(), 1) : 0,
                MaxSpeedMph = speeds.Any() ? Math.Round(speeds.Max(), 1) : 0,
                AvgRpm = rpms.Any() ? Math.Round(rpms.Average(), 0) : 0,
                MaxRpm = rpms.Any() ? Math.Round(rpms.Max(), 0) : 0,
                AvgCoolantTemp = coolants.Any() ? Math.Round(coolants.Average(), 1) : null,
                AvgTransTemp = transTemps.Any() ? Math.Round(transTemps.Average(), 1) : null,
                AvgOat = oats.Any() ? Math.Round(oats.Average(), 1) : null,
                AvgCabinTemp = cabinTemps.Any() ? Math.Round(cabinTemps.Average(), 1) : null,
                StartLat = GetDoubleField(first.Fields, "lat"),
                StartLng = GetDoubleField(first.Fields, "lng"),
                EndLat = GetDoubleField(last.Fields, "lat"),
                EndLng = GetDoubleField(last.Fields, "lng"),
                PointCount = segment.Count,
                StartUnixTime = first.UnixTime,
                EndUnixTime = last.UnixTime,
                FuelConsumedGallons = CalculateFuelConsumedGallons(segment)
            };
        }

        // --- Filter Evaluation ---

        public bool RowPassesFilter(Dictionary<string, string> row, FilterDefinition filter)
        {
            if (filter.Rules == null || !filter.Rules.Any()) return true;

            foreach (var rule in filter.Rules)
            {
                bool ruleResult = EvaluateRule(row, rule);
                if (filter.Logic == FilterLogic.Or && ruleResult) return true;
                if (filter.Logic == FilterLogic.And && !ruleResult) return false;
            }
            // If AND: all passed. If OR: none passed.
            return filter.Logic == FilterLogic.And;
        }

        private bool EvaluateRule(Dictionary<string, string> row, FilterRule rule)
        {
            if (!row.TryGetValue(rule.Field, out var rawValue))
            {
                // Field is absent — treat as non-matching for most operators
                return rule.Operator == FilterOperator.Ne;
            }
            if (rule.Operator == FilterOperator.Contains)
            {
                return rawValue.Contains(rule.Value, StringComparison.OrdinalIgnoreCase);
            }
            if (double.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var fieldNum)
                && double.TryParse(rule.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var ruleNum))
            {
                return rule.Operator switch
                {
                    FilterOperator.Eq => fieldNum == ruleNum,
                    FilterOperator.Ne => fieldNum != ruleNum,
                    FilterOperator.Gt => fieldNum > ruleNum,
                    FilterOperator.Gte => fieldNum >= ruleNum,
                    FilterOperator.Lt => fieldNum < ruleNum,
                    FilterOperator.Lte => fieldNum <= ruleNum,
                    _ => false
                };
            }
            // Fall back to string comparison
            int cmp = string.Compare(rawValue, rule.Value, StringComparison.OrdinalIgnoreCase);
            return rule.Operator switch
            {
                FilterOperator.Eq => cmp == 0,
                FilterOperator.Ne => cmp != 0,
                FilterOperator.Gt => cmp > 0,
                FilterOperator.Gte => cmp >= 0,
                FilterOperator.Lt => cmp < 0,
                FilterOperator.Lte => cmp <= 0,
                _ => false
            };
        }

        // --- Helpers ---

        private static double GetDoubleField(Dictionary<string, string> fields, string key)
        {
            if (fields.TryGetValue(key, out var val) &&
                double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }
            return 0;
        }

        private static double? GetNullableDoubleField(Dictionary<string, string> fields, string key)
        {
            if (fields.TryGetValue(key, out var val) &&
                double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }
            return null;
        }

        private static DateTime ParseDatetime(TelemetryRecord record)
        {
            if (!string.IsNullOrEmpty(record.Datetime) &&
                DateTime.TryParse(record.Datetime, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
            {
                return dt;
            }
            // Fall back to unix time
            return DateTimeOffset.FromUnixTimeSeconds(record.UnixTime).DateTime;
        }

        private static double HaversineDistanceMiles(
            double lat1, double lng1, double lat2, double lng2)
        {
            const double EarthRadiusMiles = 3958.8;
            double dLat = DegreesToRadians(lat2 - lat1);
            double dLng = DegreesToRadians(lng2 - lng1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(DegreesToRadians(lat1)) *
                       Math.Cos(DegreesToRadians(lat2)) *
                       Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return EarthRadiusMiles * c;
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        private static double? CalculateFuelConsumedGallons(List<TelemetryRecord> segment)
        {
            const double StoichiometricRatio = 14.7;
            const double GramsPerGallon = 2722.0;

            double totalFuelGrams = 0;
            bool hasData = false;
            double lastKnownLambda = 1;

            for (int i = 1; i < segment.Count; i++)
            {
                var prev = segment[i - 1];
                var curr = segment[i];

                double maf = GetDoubleField(curr.Fields, "maf");
                double lambda = GetDoubleField(curr.Fields, "af_lambda");

                // Update last known lambda when we get a fresh reading
                if (lambda > 0) lastKnownLambda = lambda;

                // Need MAF and at least one lambda reading so far
                if (maf <= 0 || lastKnownLambda <= 0) continue;

                double timeDelta = curr.UnixTime - prev.UnixTime;
                if (timeDelta <= 0 || timeDelta > 10) continue;

                double fuelRateGramsPerSecond = maf / (lastKnownLambda * StoichiometricRatio);

                totalFuelGrams += fuelRateGramsPerSecond * timeDelta;
                hasData = true;
            }

            if (!hasData) return null;
            return Math.Round(totalFuelGrams / GramsPerGallon, 3);
        }
    }
}

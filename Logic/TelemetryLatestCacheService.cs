using CarCareTracker.External.Interfaces;
using CarCareTracker.Models.LoggerSync;

namespace CarCareTracker.Logic
{
    public interface ITelemetryLatestCacheService
    {
        TelemetryRecord? GetLatest(int vehicleId, string fileType);
        List<string> GetFieldNames(int vehicleId, string fileType);
        void SetFieldNames(int vehicleId, string fileType, List<string> fieldNames);
        void Update(TelemetryRecord record);
        void UpdateBatch(List<TelemetryRecord> records);
        void Initialize(ITelemetryDataAccess dataAccess, List<int> vehicleIds);
    }

    public class TelemetryLatestCacheService : ITelemetryLatestCacheService
    {
        // Key: (vehicleId, fileType) -> newest record
        private readonly Dictionary<(int, string), TelemetryRecord> _cache = new();
        // Key: (vehicleId, fileType) -> set of all known field names
        private readonly Dictionary<(int, string), HashSet<string>> _fieldNameCache = new();
        private readonly object _lock = new();

        public TelemetryRecord? GetLatest(int vehicleId, string fileType)
        {
            lock (_lock)
            {
                _cache.TryGetValue((vehicleId, fileType), out var record);
                return record;
            }
        }

        public List<string> GetFieldNames(int vehicleId, string fileType)
        {
            lock (_lock)
            {
                if (_fieldNameCache.TryGetValue((vehicleId, fileType), out var names))
                    return names.OrderBy(f => f).ToList();
                return new List<string>();
            }
        }

        public void SetFieldNames(int vehicleId, string fileType, List<string> fieldNames)
        {
            lock (_lock)
            {
                _fieldNameCache[(vehicleId, fileType)] = new HashSet<string>(fieldNames);
            }
        }

        public void Update(TelemetryRecord record)
        {
            lock (_lock)
            {
                var key = (record.VehicleId, record.FileType);
                if (!_cache.TryGetValue(key, out var existing) || record.UnixTime > existing.UnixTime)
                {
                    _cache[key] = record;
                }
            }
        }

        public void UpdateBatch(List<TelemetryRecord> records)
        {
            lock (_lock)
            {
                foreach (var record in records)
                {
                    var key = (record.VehicleId, record.FileType);

                    // Update latest record
                    if (!_cache.TryGetValue(key, out var existing) || record.UnixTime > existing.UnixTime)
                    {
                        _cache[key] = record;
                    }

                    // Merge field names
                    if (!_fieldNameCache.TryGetValue(key, out var fieldSet))
                    {
                        fieldSet = new HashSet<string>();
                        _fieldNameCache[key] = fieldSet;
                    }
                    foreach (var fieldName in record.Fields.Keys)
                    {
                        fieldSet.Add(fieldName);
                    }
                }
            }
        }

        public void Initialize(ITelemetryDataAccess dataAccess, List<int> vehicleIds)
        {
            foreach (var vehicleId in vehicleIds)
            {
                foreach (var fileType in new[] { "LOG", "IMU", "MON", "STATUS" })
                {
                    var latest = dataAccess.GetLatestByFileType(vehicleId, fileType);
                    if (latest != null)
                    {
                        Update(latest);
                    }
                    var fields = dataAccess.GetDistinctFieldNames(vehicleId, fileType);
                    if (fields.Any())
                    {
                        SetFieldNames(vehicleId, fileType, fields);
                    }
                }
            }
        }
    }
}

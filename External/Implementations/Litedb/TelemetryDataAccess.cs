using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models.LoggerSync;
using LiteDB;

namespace CarCareTracker.External.Implementations
{
    public class TelemetryDataAccess : ITelemetryDataAccess
    {
        private ILiteDBHelper _liteDB { get; set; }
        private static string tableName = "telemetry";
        private static string latestTableName = "telemetry_latest";
        public TelemetryDataAccess(ILiteDBHelper liteDB)
        {
            _liteDB = liteDB;
        }
        
        public bool SaveTelemetryBatch(List<TelemetryRecord> records)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<TelemetryRecord>(tableName);
            table.EnsureIndex(x => x.VehicleId);
            table.EnsureIndex(x => x.UnixTime);
            table.EnsureIndex(x => x.FileType);
            table.EnsureIndex(x => x.SourceFilename);
            table.Upsert(records);
            UpsertLatestRecords(records);
            return true;
        }

        
        public List<TelemetryRecord> GetTelemetryByVehicleId(int vehicleId, long afterUnixTime = 0, int limit = 1000)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<TelemetryRecord>(tableName);
            return table.Query()
                .Where(x => x.VehicleId == vehicleId && x.UnixTime > afterUnixTime)
                .OrderByDescending(x => x.UnixTime)
                .Limit(limit)
                .ToList();
        }
        // Keeps a tiny "telemetry_latest" collection holding only the newest record

        private void UpsertLatestRecords(List<TelemetryRecord> records)
        {
            var db = _liteDB.GetLiteDB();
            var latestTable = db.GetCollection<TelemetryRecord>(latestTableName);
            latestTable.EnsureIndex(x => x.VehicleId);
            latestTable.EnsureIndex(x => x.FileType);

            // Group this batch by (vehicle, fileType) and find the newest in each group.
            // NOTE: this OrderByDescending runs on the in-memory batch (a plain C# list),
            // NOT on the database — so it's cheap, no LiteDB sort involved.
            foreach (var group in records.GroupBy(r => new { r.VehicleId, r.FileType }))
            {
                var newest = group.OrderByDescending(r => r.UnixTime).First();
                var existing = latestTable.FindOne(x => x.VehicleId == newest.VehicleId && x.FileType == newest.FileType);
                if (existing == null || newest.UnixTime > existing.UnixTime)
                {
                    // Replace whatever was stored for this key with the newer record,
                    // keeping exactly one row per (vehicle, fileType).
                    latestTable.DeleteMany(x => x.VehicleId == newest.VehicleId && x.FileType == newest.FileType);
                    latestTable.Insert(newest);
                }
            }
        }
        public List<TelemetryRecord> GetTelemetryByVehicleIdAndTimeRange(int vehicleId, long startUnix, long endUnix, string fileType = "")
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<TelemetryRecord>(tableName);
            var query = Query.And(
                Query.EQ(nameof(TelemetryRecord.VehicleId), vehicleId),
                Query.GTE(nameof(TelemetryRecord.UnixTime), startUnix),
                Query.LTE(nameof(TelemetryRecord.UnixTime), endUnix)
            );
            if (!string.IsNullOrEmpty(fileType))
            {
                query = Query.And(query, Query.EQ(nameof(TelemetryRecord.FileType), fileType));
            }
            return table.Find(query).ToList();
        }
                // Streams the time range via the UnixTime index and keeps only the distinct
        // (fileType, filename) pairs — memory scales with the number of FILES (hundreds),
        // never the number of rows (millions). Replaces materializing the whole range.
        public List<(string FileType, string SourceFilename)> GetDistinctSourceFiles(int vehicleId, long startUnix, long endUnix)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<TelemetryRecord>(tableName);
            var seen = new HashSet<(string FileType, string SourceFilename)>();
            foreach (var r in table.Find(Query.Between(nameof(TelemetryRecord.UnixTime), startUnix, endUnix)))
            {
                if (r.VehicleId != vehicleId) continue;
                if (r.SourceFilename == "sync_check") continue;
                seen.Add((r.FileType, r.SourceFilename));
            }
            return seen.OrderBy(f => f.SourceFilename).ToList();
        }
        public List<string> GetDistinctFieldNames(int vehicleId, string fileType = "")
        {
            var db = _liteDB.GetLiteDB();
            var latestTable = db.GetCollection<TelemetryRecord>(latestTableName);
            var fieldNames = new HashSet<string>();

            var typesToCheck = string.IsNullOrEmpty(fileType)
                ? new[] { "LOG", "IMU", "MON" }
                : new[] { fileType };

            foreach (var ft in typesToCheck)
            {
                var latest = latestTable.FindOne(x => x.VehicleId == vehicleId && x.FileType == ft);
                if (latest != null)
                {
                    foreach (var key in latest.Fields.Keys)
                        fieldNames.Add(key);
                }
            }

            return fieldNames.OrderBy(f => f).ToList();
        }
        public bool UpdateVehicleIdBySourceFilename(int oldVehicleId, string sourceFilename, int newVehicleId)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<TelemetryRecord>(tableName);
            var records = table.Find(Query.And(
                Query.EQ(nameof(TelemetryRecord.VehicleId), oldVehicleId),
                Query.EQ(nameof(TelemetryRecord.SourceFilename), sourceFilename)
            )).ToList();
            foreach (var record in records)
            {
                record.VehicleId = newVehicleId;
            }
            table.Upsert(records);
            return true;
        }
                public HashSet<string> GetExistingSourceFilenames(int vehicleId)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<TelemetryRecord>(tableName);
            var records = table.Find(Query.EQ(nameof(TelemetryRecord.VehicleId), vehicleId));
            var keys = new HashSet<string>();
            foreach (var r in records)
            {
                keys.Add($"{r.FileType}:{r.SourceFilename}");
            }
            return keys;
        }
                public bool DeleteTelemetryByVehicleId(int vehicleId)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<TelemetryRecord>(tableName);
            table.DeleteMany(x => x.VehicleId == vehicleId);
            return true;
        }
        public TelemetryRecord? GetLatestByFileType(int vehicleId, string fileType)
        {
            var db = _liteDB.GetLiteDB();
            var latestTable = db.GetCollection<TelemetryRecord>(latestTableName);
            return latestTable.FindOne(x => x.VehicleId == vehicleId && x.FileType == fileType);
        }
        public List<TelemetryRecord> GetTelemetryDownsampled(int vehicleId, long startUnix, long endUnix, string fileType, int maxPoints)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<TelemetryRecord>(tableName);
            var countQuery = Query.And(
                Query.EQ(nameof(TelemetryRecord.VehicleId), vehicleId),
                Query.GTE(nameof(TelemetryRecord.UnixTime), startUnix),
                Query.LTE(nameof(TelemetryRecord.UnixTime), endUnix)
            );
            if (!string.IsNullOrEmpty(fileType))
            {
                countQuery = Query.And(countQuery, Query.EQ(nameof(TelemetryRecord.FileType), fileType));
            }

            // Count first to compute skip factor
            var total = table.Count(countQuery);
            if (total == 0) return new List<TelemetryRecord>();

            int nth = total > maxPoints ? (int)Math.Ceiling((double)total / maxPoints) : 1;

            // Walk the UnixTime index across the range — streams rows one at a
            // time in ascending time order, so memory stays flat no matter how
            // wide the range is. NEVER use fluent .OrderBy here: it materializes
            // and sorts the entire collection in RAM (the trap that froze the box).
            var result = new List<TelemetryRecord>(Math.Min(total, maxPoints));
            int i = 0;
            foreach (var r in table.Find(Query.Between(nameof(TelemetryRecord.UnixTime), startUnix, endUnix)))
            {
                if (r.VehicleId != vehicleId) continue;
                if (!string.IsNullOrEmpty(fileType) && r.FileType != fileType) continue;

                if (i % nth == 0)
                {
                    result.Add(r);
                }
                i++;
            }

            // Cheap insurance: sort only the downsampled handful, never the raw rows.
            result.Sort((a, b) => a.UnixTime.CompareTo(b.UnixTime));

            return result;
        }

    }
    
}

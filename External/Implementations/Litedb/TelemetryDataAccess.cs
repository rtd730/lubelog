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
        public TelemetryDataAccess(ILiteDBHelper liteDB)
        {
            _liteDB = liteDB;
        }
        public bool SaveTelemetryBatch(List<TelemetryRecord> records)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<TelemetryRecord>(tableName);
            table.Upsert(records);
            db.Checkpoint();
            return true;
        }
        public List<TelemetryRecord> GetTelemetryByVehicleId(int vehicleId, long afterUnixTime = 0, int limit = 1000)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<TelemetryRecord>(tableName);
            return table.Find(Query.And(
                Query.EQ(nameof(TelemetryRecord.VehicleId), vehicleId),
                Query.GT(nameof(TelemetryRecord.UnixTime), afterUnixTime)
            )).Take(limit).ToList();
        }
    }
}

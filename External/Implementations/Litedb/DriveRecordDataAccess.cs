using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models;
using LiteDB;

namespace CarCareTracker.External.Implementations
{
    public class DriveRecordDataAccess : IDriveRecordDataAccess
    {
        private ILiteDBHelper _liteDB { get; set; }
        private static string tableName = "driverecords";
        public DriveRecordDataAccess(ILiteDBHelper liteDB)
        {
            _liteDB = liteDB;
        }
        public List<DriveRecord> GetDriveRecordsByVehicleId(int vehicleId)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<DriveRecord>(tableName);
            var records = table.Find(Query.EQ(nameof(DriveRecord.VehicleId), vehicleId));
            return records.ToList() ?? new List<DriveRecord>();
        }
        public DriveRecord GetDriveRecordById(int driveRecordId)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<DriveRecord>(tableName);
            return table.FindById(driveRecordId);
        }
        public bool SaveDriveRecordBatch(List<DriveRecord> records)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<DriveRecord>(tableName);
            table.EnsureIndex(x => x.VehicleId);
            table.EnsureIndex(x => x.StartUnixTime);
            table.Upsert(records);
            db.Checkpoint();
            return true;
        }
        public bool DeleteDriveRecordsByVehicleId(int vehicleId)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<DriveRecord>(tableName);
            table.DeleteMany(Query.EQ(nameof(DriveRecord.VehicleId), vehicleId));
            db.Checkpoint();
            return true;
        }
        public long GetLatestDriveUnixTimeByVehicleId(int vehicleId)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<DriveRecord>(tableName);
            var latest = table.Find(Query.EQ(nameof(DriveRecord.VehicleId), vehicleId))
                .OrderByDescending(x => x.StartUnixTime)
                .FirstOrDefault();
            return latest?.StartUnixTime ?? 0;
        }
    }
}

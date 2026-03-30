using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models.LoggerSync;
using LiteDB;

namespace CarCareTracker.External.Implementations
{
    public class FirmwareDataAccess : IFirmwareDataAccess
    {
        private ILiteDBHelper _liteDB { get; set; }
        private static string tableName = "firmware";
        public FirmwareDataAccess(ILiteDBHelper liteDB)
        {
            _liteDB = liteDB;
        }
        public FirmwareRecord? GetLatestActiveFirmware()
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<FirmwareRecord>(tableName);
            return table.Find(Query.EQ(nameof(FirmwareRecord.IsActive), true))
                        .OrderByDescending(f => f.UploadedAt)
                        .FirstOrDefault();
        }
        public bool SaveFirmware(FirmwareRecord record)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<FirmwareRecord>(tableName);
            table.Upsert(record);
            db.Checkpoint();
            return true;
        }
        public List<FirmwareRecord> GetAllFirmware()
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<FirmwareRecord>(tableName);
            return table.FindAll().OrderByDescending(f => f.UploadedAt).ToList();
        }
    }
}

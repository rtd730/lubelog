using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models.LoggerSync;
using LiteDB;

namespace CarCareTracker.External.Implementations
{
    public class ReceivedFileDataAccess : IReceivedFileDataAccess
    {
        private ILiteDBHelper _liteDB { get; set; }
        private static string tableName = "receivedfiles";
        public ReceivedFileDataAccess(ILiteDBHelper liteDB)
        {
            _liteDB = liteDB;
        }
        public List<string> GetReceivedFilenames(int vehicleId)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<ReceivedFileRecord>(tableName);
            return table.Find(Query.EQ(nameof(ReceivedFileRecord.VehicleId), vehicleId))
                        .Select(r => r.Filename)
                        .ToList();
        }
        public bool MarkFileReceived(int vehicleId, string filename)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<ReceivedFileRecord>(tableName);
            table.Upsert(new ReceivedFileRecord
            {
                VehicleId = vehicleId,
                Filename = filename
            });
            db.Checkpoint();
            return true;
        }
    }
}

using CarCareTracker.External.Interfaces;
using CarCareTracker.Helper;
using CarCareTracker.Models;
using LiteDB;

namespace CarCareTracker.External.Implementations
{
    public class FilterDefinitionDataAccess : IFilterDefinitionDataAccess
    {
        private ILiteDBHelper _liteDB { get; set; }
        private static string tableName = "filterdefinitions";
        public FilterDefinitionDataAccess(ILiteDBHelper liteDB)
        {
            _liteDB = liteDB;
        }
        public List<FilterDefinition> GetFiltersByVehicleId(int vehicleId)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<FilterDefinition>(tableName);
            var filters = table.Find(Query.EQ(nameof(FilterDefinition.VehicleId), vehicleId));
            return filters.ToList() ?? new List<FilterDefinition>();
        }
        public FilterDefinition GetFilterById(int filterId)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<FilterDefinition>(tableName);
            return table.FindById(filterId);
        }
        public bool SaveFilter(FilterDefinition filter)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<FilterDefinition>(tableName);
            table.Upsert(filter);
            db.Checkpoint();
            return true;
        }
        public bool DeleteFilterById(int filterId)
        {
            var db = _liteDB.GetLiteDB();
            var table = db.GetCollection<FilterDefinition>(tableName);
            table.Delete(filterId);
            db.Checkpoint();
            return true;
        }
    }
}

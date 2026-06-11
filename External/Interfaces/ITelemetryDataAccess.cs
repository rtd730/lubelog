using CarCareTracker.Models.LoggerSync;

namespace CarCareTracker.External.Interfaces
{
    public interface ITelemetryDataAccess
    {
        bool SaveTelemetryBatch(List<TelemetryRecord> records);
        List<TelemetryRecord> GetTelemetryByVehicleId(int vehicleId, long afterUnixTime = 0, int limit = 1000);
        List<TelemetryRecord> GetTelemetryByVehicleIdAndTimeRange(int vehicleId, long startUnix, long endUnix, string fileType = "");
        List<string> GetDistinctFieldNames(int vehicleId, string fileType = "");
        bool UpdateVehicleIdBySourceFilename(int oldVehicleId, string sourceFilename, int newVehicleId);
        HashSet<string> GetExistingSourceFilenames(int vehicleId);
        bool DeleteTelemetryByVehicleId(int vehicleId);
        TelemetryRecord? GetLatestByFileType(int vehicleId, string fileType);
        List<TelemetryRecord> GetTelemetryDownsampled(int vehicleId, long startUnix, long endUnix, string fileType, int maxPoints);
        List<(string FileType, string SourceFilename)> GetDistinctSourceFiles(int vehicleId, long startUnix, long endUnix);

    }
}

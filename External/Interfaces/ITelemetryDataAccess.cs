using CarCareTracker.Models.LoggerSync;

namespace CarCareTracker.External.Interfaces
{
    public interface ITelemetryDataAccess
    {
        bool SaveTelemetryBatch(List<TelemetryRecord> records);
        List<TelemetryRecord> GetTelemetryByVehicleId(int vehicleId, long afterUnixTime = 0, int limit = 1000);
    }
}

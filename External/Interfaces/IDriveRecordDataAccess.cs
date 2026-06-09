using CarCareTracker.Models;

namespace CarCareTracker.External.Interfaces
{
    public interface IDriveRecordDataAccess
    {
        List<DriveRecord> GetDriveRecordsByVehicleId(int vehicleId);
        DriveRecord GetDriveRecordById(int driveRecordId);
        bool SaveDriveRecordBatch(List<DriveRecord> records);
        bool DeleteDriveRecordsByVehicleId(int vehicleId);
        long GetLatestDriveUnixTimeByVehicleId(int vehicleId);
    }
}

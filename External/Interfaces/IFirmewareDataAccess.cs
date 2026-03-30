using CarCareTracker.Models.LoggerSync;

namespace CarCareTracker.External.Interfaces
{
    public interface IFirmwareDataAccess
    {
        FirmwareRecord? GetLatestActiveFirmware();
        bool SaveFirmware(FirmwareRecord record);
        List<FirmwareRecord> GetAllFirmware();
    }
}

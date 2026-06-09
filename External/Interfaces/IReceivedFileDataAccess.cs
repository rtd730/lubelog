using CarCareTracker.Models.LoggerSync;

namespace CarCareTracker.External.Interfaces
{
    public interface IReceivedFileDataAccess
    {
        List<string> GetReceivedFilenames(int vehicleId);
        bool MarkFileReceived(int vehicleId, string filename);
        List<ReceivedFileRecord> GetRecentFiles(int vehicleId, int limit = 50);
    }
}

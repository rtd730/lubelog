using CarCareTracker.Models.LoggerSync;

namespace CarCareTracker.Models
{
    public class TrackerHealthViewModel
    {
        public int VehicleId { get; set; }
        public string CurrentFirmwareVersion { get; set; } = string.Empty;
        public string LastSyncTime { get; set; } = string.Empty;
        public string LastSyncFile { get; set; } = string.Empty;
        public double? LatestBatteryVoltage { get; set; }
        public int? LatestSatCount { get; set; }
        public string SdCardState { get; set; } = string.Empty;
        public int? SdTotalMb { get; set; }
        public int? SdFreeMb { get; set; }
        public List<FirmwareRecord> FirmwareHistory { get; set; } = new();
        public List<ReceivedFileRecord> ReceivedFiles { get; set; } = new();
        public string FirmwareGitHubRepo { get; set; } = string.Empty;
        public string SourceFirmwareVersion { get; set; } = string.Empty;
        public string RunningFirmwareVersion { get; set; } = "—";

    }
}

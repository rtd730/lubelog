namespace CarCareTracker.Models.LoggerSync
{
    public class TrackerSyncCheckRequest
    {
        public int VehicleId { get; set; }
        public List<string> Files { get; set; } = new();
        public uint? SdTotalMb { get; set; }
        public uint? SdFreeMb { get; set; }
        public string? FirmwareVersion { get; set; }

    }
}


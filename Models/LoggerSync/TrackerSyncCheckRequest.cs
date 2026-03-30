namespace CarCareTracker.Models.LoggerSync
{
    public class TrackerSyncCheckRequest
    {
        public int VehicleId { get; set; }
        public List<string> Files { get; set; } = new();
    }
}


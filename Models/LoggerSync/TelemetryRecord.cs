namespace CarCareTracker.Models.LoggerSync
{
    public class TelemetryRecord
    {
        public int Id { get; set; }
        public int VehicleId { get; set; }
        public long UnixTime { get; set; }
        public string Datetime { get; set; } = string.Empty;
        public Dictionary<string, string> Fields { get; set; } = new();
    }
}

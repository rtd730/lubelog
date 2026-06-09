namespace CarCareTracker.Models
{
    public class TelemetryQueryRequest
    {
        public int VehicleId { get; set; }
        public List<int> DriveIds { get; set; } = new();
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public List<string> Fields { get; set; } = new();
        public FilterDefinition? Filter { get; set; }
        public string? FileType { get; set; }
        public int MaxPoints { get; set; } = 50000;
        public bool Downsample { get; set; } = true;
        public List<string> SourceFilenames { get; set; } = new();

    }
}

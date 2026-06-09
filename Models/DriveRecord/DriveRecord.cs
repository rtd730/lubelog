namespace CarCareTracker.Models
{
    public class DriveRecord
    {
        public int Id { get; set; }
        public int VehicleId { get; set; }
        public string SourceFilename { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double DistanceMiles { get; set; }
        public double AvgSpeedMph { get; set; }
        public double MaxSpeedMph { get; set; }
        public double AvgRpm { get; set; }
        public double MaxRpm { get; set; }
        public double? AvgCoolantTemp { get; set; }
        public double? AvgTransTemp { get; set; }
        public double? AvgOat { get; set; }
        public double? AvgCabinTemp { get; set; }
        public double StartLat { get; set; }
        public double StartLng { get; set; }
        public double EndLat { get; set; }
        public double EndLng { get; set; }
        public int PointCount { get; set; }
        public string Notes { get; set; } = string.Empty;
        public long StartUnixTime { get; set; }
        public long EndUnixTime { get; set; }
        public double? FuelConsumedGallons { get; set; }

    }
}

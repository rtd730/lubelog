namespace CarCareTracker.Models
{
    public class DriveRecordViewModel
    {
        public int Id { get; set; }
        public int VehicleId { get; set; }
        public string Date { get; set; } = string.Empty;
        public string StartTimeStr { get; set; } = string.Empty;
        public string EndTimeStr { get; set; } = string.Empty;
        public string DurationStr { get; set; } = string.Empty;
        public double DistanceMiles { get; set; }
        public double AvgSpeedMph { get; set; }
        public double MaxSpeedMph { get; set; }
        public double AvgRpm { get; set; }
        public string AvgCoolantTemp { get; set; } = string.Empty;
        public string AvgTransTemp { get; set; } = string.Empty;
        public string AvgOat { get; set; } = string.Empty;
        public string AvgCabinTemp { get; set; } = string.Empty;
        public string StartCoords { get; set; } = string.Empty;
        public string EndCoords { get; set; } = string.Empty;
        public string FuelUsed { get; set; } = string.Empty;
        public string AvgMpg { get; set; } = string.Empty;
        public double? AvgMpgRaw { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string SourceFilename { get; set; } = string.Empty;
        // Raw values for client-side sorting
        public long StartUnixTime { get; set; }
        public long EndUnixTime { get; set; }
        public double? FuelConsumedGallons { get; set; }
    }

    public class DriveRecordViewModelContainer
    {
        public List<DriveRecordViewModel> DriveRecords { get; set; } = new();
        public bool HasTelemetryData { get; set; }
        public string LastParsedAt { get; set; } = string.Empty;
    }
}

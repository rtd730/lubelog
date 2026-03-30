namespace CarCareTracker.Models.LoggerSync
{
    public class ReceivedFileRecord
    {
        public int Id { get; set; }
        public int VehicleId { get; set; }
        public string Filename { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    }
}

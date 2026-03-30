namespace CarCareTracker.Models.LoggerSync
{
    public class FirmwareRecord
    {
        public int Id { get; set; }
        public string Version { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Md5Hash { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public string Notes { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}

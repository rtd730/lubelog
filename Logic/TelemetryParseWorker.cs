using CarCareTracker.External.Interfaces;

namespace CarCareTracker.Logic
{
    public class TelemetryParseWorker : BackgroundService
    {
        private readonly ITelemetryParseQueue _queue;
        private readonly ITelemetryParserService _parser;
        private readonly IDriveRecordDataAccess _driveData;
        private readonly ILogger<TelemetryParseWorker> _logger;

        public TelemetryParseWorker(
            ITelemetryParseQueue queue,
            ITelemetryParserService parser,
            IDriveRecordDataAccess driveData,
            ILogger<TelemetryParseWorker> logger)
        {
            _queue = queue;
            _parser = parser;
            _driveData = driveData;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Sleeps until at least one job exists. On shutdown this THROWS
                    // OperationCanceledException (it does not return false), which
                    // the catch below treats as a normal exit.
                    await _queue.Reader.WaitToReadAsync(stoppingToken);

                    var vehiclesNeedingDrives = new HashSet<int>();

                    // Drain the burst. After the queue runs dry, wait a settle window;
                    // if more files land, keep draining. Drive detection therefore runs
                    // once per burst, not once per file.
                    while (true)
                    {
                        var parsedSomething = false;
                        while (_queue.Reader.TryRead(out var job))
                        {
                            parsedSomething = true;
                            try
                            {
                                _parser.ParseAndStoreCsvFile(job.VehicleId, job.FilePath, job.FileType);
                                if (job.FileType.Equals("LOG", StringComparison.OrdinalIgnoreCase))
                                    vehiclesNeedingDrives.Add(job.VehicleId);
                            }
                            catch (Exception ex)
                            {
                                // File stays on disk; the startup backfill retries it later.
                                _logger.LogError(ex, "Background parse failed for {File}", job.FilePath);
                            }
                        }

                        if (!parsedSomething) break;

                        // Throws OperationCanceledException on shutdown — deliberately
                        // skips drive detection below; it self-heals on the next burst.
                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    }

                    foreach (var vehicleId in vehiclesNeedingDrives)
                    {
                        try
                        {
                            var latest = _driveData.GetLatestDriveUnixTimeByVehicleId(vehicleId);
                            var newDrives = _parser.DetectDrives(vehicleId, latest);
                            if (newDrives.Any())
                                _driveData.SaveDriveRecordBatch(newDrives);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Background drive detection failed for vehicle {Vehicle}", vehicleId);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown. Unparsed files are already on disk and marked
                // received; the startup backfill re-parses them on next boot.
            }
        }
    }
}
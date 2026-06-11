using System.Threading.Channels;

namespace CarCareTracker.Logic
{
    // One unit of deferred work: "parse this file into the DB, later, off the request thread."
    public record ParseJob(int VehicleId, string FilePath, string FileType);

    public interface ITelemetryParseQueue
    {
        void Enqueue(ParseJob job);
        ChannelReader<ParseJob> Reader { get; }
    }

    public class TelemetryParseQueue : ITelemetryParseQueue
    {
        // Unbounded is safe here: the truck uploads one file per request, so the
        // queue never fills faster than the network delivers.
        private readonly Channel<ParseJob> _channel =
            Channel.CreateUnbounded<ParseJob>(new UnboundedChannelOptions
            {
                SingleReader = true,   // exactly one worker drains it
                SingleWriter = false   // many request threads may enqueue
            });

        public ChannelReader<ParseJob> Reader => _channel.Reader;
        public void Enqueue(ParseJob job) => _channel.Writer.TryWrite(job);
    }
}
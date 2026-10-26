namespace Dictate.Core;

public sealed record UsageDay(DateOnly Date, int Words, int Dictations, double Seconds);
public sealed record Usage(UsageDay[] Week, int? MedianLatency, int? WordsPerMinute)
{
    public static Usage From(IEnumerable<TranscriptRecord> records, DateTimeOffset? now = null)
    {
        var today = DateOnly.FromDateTime((now ?? DateTimeOffset.Now).LocalDateTime);
        var days = Enumerable.Range(0, 7).Select(offset => new UsageDay(today.AddDays(offset - 6), 0, 0, 0)).ToArray();
        var latencies = new Queue<int>();
        foreach (var record in records)
        {
            var day = DateOnly.FromDateTime(record.Date.LocalDateTime);
            var index = day.DayNumber - days[0].Date.DayNumber;
            if (index >= 0 && index < days.Length) days[index] = days[index] with
            {
                Words = days[index].Words + record.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length,
                Dictations = days[index].Dictations + 1, Seconds = days[index].Seconds + record.AudioSeconds
            };
            if (record.Delivery == DeliveryOutcome.Verified && record.Metrics.TryGetValue("release_to_text_ms", out var latency))
            {
                latencies.Enqueue((int)latency);
                if (latencies.Count > 50) latencies.Dequeue();
            }
        }
        var times = latencies.Order().ToArray();
        var seconds = days.Sum(day => day.Seconds);
        return new(days, times.Length == 0 ? null : times[times.Length / 2], seconds < 60 ? null : (int)Math.Round(days.Sum(day => day.Words) / seconds * 60));
    }
}

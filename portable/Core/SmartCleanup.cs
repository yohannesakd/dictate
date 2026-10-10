using System.Text.RegularExpressions;

namespace Dictate.Core;

public sealed class SmartCleanup(ICleanupClient client, TimeProvider? clock = null) : IDisposable
{
    private sealed class Pending
    {
        public required long Started { get; init; }
        public TaskCompletionSource<CleanupResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CleanupResult> Task => Completion.Task;
        public CancellationTokenSource Cancellation { get; } = new();
    }

    private readonly object sync = new();
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, Pending> requests = [];
    private readonly List<long> sent = [];
    private bool disposed;

    public async Task<CleanupResult> CleanAsync(string raw, IReadOnlyList<DictionaryEntry> entries,
        CleanupProvider provider, CancellationToken cancellation, bool speculative = false)
    {
        if (!CleanupGuard.NeedsModel(raw, entries)) return new(null, null, "not needed");
        var message = CleanupPrompt.Message(raw, entries);
        var key = provider + "\n" + message;
        Pending pending;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!requests.TryGetValue(key, out pending!))
            {
                var now = clock.GetTimestamp();
                sent.RemoveAll(time => clock.GetElapsedTime(time, now) >= TimeSpan.FromMinutes(1));
                if (sent.Count >= (speculative ? 8 : 12)) return new(null, null, "limited");
                sent.Add(now);
                var active = requests.Where(pair => !pair.Value.Task.IsCompleted).OrderBy(pair => pair.Value.Started).ToArray();
                if (active.Length >= 2)
                {
                    requests.Remove(active[0].Key);
                    active[0].Value.Cancellation.Cancel();
                }
                pending = new Pending { Started = now };
                requests[key] = pending;
                _ = RunAsync(key, pending, raw, entries.ToArray(), provider, message);
            }
        }
        try { return await pending.Task.WaitAsync(TimeSpan.FromSeconds(3), clock, cancellation).ConfigureAwait(false); }
        catch (TimeoutException) { return new(null, null, "timeout", "The model did not reply within 3 seconds."); }
    }

    private async Task<CleanupResult> RunAsync(string key, Pending pending, string raw, DictionaryEntry[] entries,
        CleanupProvider provider, string message)
    {
        // Start outside the request-table lock, including clients that complete synchronously.
        await Task.Yield();
        CleanupResult result;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(pending.Cancellation.Token, deadline.Token);
        try
        {
            var reply = await client.CompleteAsync(provider, CleanupPrompt.Instructions, message, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            reply = Regex.Replace(reply, "</?transcript>", "").Trim();
            var accepted = CleanupGuard.Accepts(reply, raw, entries);
            result = new(accepted ? reply : null, reply, accepted ? "used" : "rejected");
        }
        catch (OperationCanceledException) { result = new(null, null, "cancelled"); }
        catch (Exception error) { result = new(null, null, "error", error.Message); }
        lock (sync)
        {
            pending.Completion.SetResult(result);
            if (requests.TryGetValue(key, out var current) && ReferenceEquals(current, pending))
            {
                if (result.Text is null) requests.Remove(key);
                var cached = requests.Where(pair => pair.Value.Task.IsCompletedSuccessfully && pair.Value.Task.Result.Text is not null)
                    .OrderBy(pair => pair.Value.Started).ToArray();
                foreach (var old in cached.Take(Math.Max(0, cached.Length - 32))) requests.Remove(old.Key);
            }
            pending.Cancellation.Dispose();
        }
        return result;
    }

    public void Dispose()
    {
        lock (sync)
        {
            disposed = true;
            foreach (var request in requests.Values.Where(request => !request.Task.IsCompleted)) request.Cancellation.Cancel();
            requests.Clear();
        }
    }
}

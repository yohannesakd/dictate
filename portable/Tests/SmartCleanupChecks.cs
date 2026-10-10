using Dictate.Core;
using Xunit;

namespace Dictate.Tests;

public class SmartCleanupChecks
{
    private sealed class Client : ICleanupClient
    {
        public int Calls;
        public readonly Queue<TaskCompletionSource<string>> Replies = new();
        public readonly SemaphoreSlim Arrived = new(0);
        public Task<string> CompleteAsync(CleanupProvider provider, string instructions, string message, CancellationToken cancellation)
        {
            Interlocked.Increment(ref Calls);
            var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Replies) Replies.Enqueue(reply);
            Arrived.Release();
            return reply.Task;
        }
        public async Task<TaskCompletionSource<string>> Next()
        {
            Assert.True(await Arrived.WaitAsync(TimeSpan.FromSeconds(5)));
            lock (Replies) return Replies.Dequeue();
        }
    }

    [Fact]
    public async Task IdenticalRequestsShareWorkAndCacheAcceptedReplies()
    {
        var client = new Client();
        using var cleanup = new SmartCleanup(client);
        var first = cleanup.CleanAsync("Wait, wait now", [], CleanupProvider.Groq, CancellationToken.None);
        var reply = await client.Next();
        var second = cleanup.CleanAsync("Wait, wait now", [], CleanupProvider.Groq, CancellationToken.None);
        reply.SetResult("Wait now");
        Assert.Equal("Wait now", (await first).Text);
        Assert.Equal("Wait now", (await second).Text);
        Assert.Equal("Wait now", (await cleanup.CleanAsync("Wait, wait now", [], CleanupProvider.Groq, CancellationToken.None)).Text);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task ThirdRequestCancelsOldestAndLateReplyCannotBecomeCached()
    {
        var client = new Client();
        using var cleanup = new SmartCleanup(client);
        var first = cleanup.CleanAsync("Wait, wait first", [], CleanupProvider.Groq, CancellationToken.None);
        var old = await client.Next();
        var second = cleanup.CleanAsync("Wait, wait second", [], CleanupProvider.Groq, CancellationToken.None);
        var secondReply = await client.Next();
        var third = cleanup.CleanAsync("Wait, wait third", [], CleanupProvider.Groq, CancellationToken.None);
        var thirdReply = await client.Next();
        Assert.Equal("cancelled", (await first).Outcome);
        old.SetResult("Wait first");
        secondReply.SetResult("Wait second"); thirdReply.SetResult("Wait third");
        Assert.Equal("used", (await second).Outcome); Assert.Equal("used", (await third).Outcome);
        var retried = cleanup.CleanAsync("Wait, wait first", [], CleanupProvider.Groq, CancellationToken.None);
        (await client.Next()).SetResult("Wait first");
        Assert.Equal("used", (await retried).Outcome);
        Assert.Equal(4, client.Calls);
    }

    [Fact]
    public async Task TimeoutFallsBackButCompletedReplyCanServeNextDictation()
    {
        var client = new Client();
        using var cleanup = new SmartCleanup(client);
        var first = cleanup.CleanAsync("Wait, wait now", [], CleanupProvider.DeepSeek, CancellationToken.None);
        var reply = await client.Next();
        Assert.Equal("timeout", (await first).Outcome);
        reply.SetResult("Wait now");
        Assert.Equal("Wait now", (await cleanup.CleanAsync("Wait, wait now", [], CleanupProvider.DeepSeek, CancellationToken.None)).Text);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task UnapprovedOmissionIsRejected()
    {
        var client = new Client();
        using var cleanup = new SmartCleanup(client);
        var result = cleanup.CleanAsync("Wait, wait do not delete it", [], CleanupProvider.Groq, CancellationToken.None);
        (await client.Next()).SetResult("Wait delete it");
        Assert.Null((await result).Text);
        Assert.Equal("rejected", (await result).Outcome);
    }
}

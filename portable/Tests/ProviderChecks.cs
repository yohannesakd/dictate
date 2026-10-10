using System.Net;
using Dictate.Core;
using Xunit;

namespace Dictate.Tests;

public class ProviderChecks
{
    private sealed class Secrets : ISecretStore
    {
        public Task<string?> ReadAsync(string account, CancellationToken cancellation = default) => Task.FromResult<string?>("test-only");
        public Task WriteAsync(string account, string? value, CancellationToken cancellation = default) => Task.CompletedTask;
    }
    private sealed class Response(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
    [Theory]
    [InlineData(CleanupProvider.Groq)]
    [InlineData(CleanupProvider.DeepSeek)]
    public async Task IncompleteReplyCannotReplaceTranscript(CleanupProvider provider)
    {
        using var http = new HttpClient(new Response("""{"choices":[{"finish_reason":"length","message":{"content":"Missing words"}}]}"""));
        var client = new HttpCleanupClient(http, new Secrets());
        await Assert.ThrowsAsync<InvalidDataException>(() => client.CompleteAsync(provider, "instructions", "transcript", CancellationToken.None));
    }
}

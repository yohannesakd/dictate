using System.Net;
using System.Security.Cryptography;
using Dictate.Speech;
using Xunit;

namespace Dictate.Tests;

public class AssetDownloadChecks
{
    private sealed class Handler(Stream body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
    }

    private sealed class StalledBody : MemoryStream
    {
        public bool Started { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation = default)
        {
            Started = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            return 0;
        }
    }

    [Fact]
    public async Task AStalledResponseBodyTimesOutWithoutReplacingTheModel()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dictate-download-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "model.bin");
        try
        {
            await File.WriteAllTextAsync(path, "previous model");
            using var body = new StalledBody();
            using var client = new HttpClient(new Handler(body)) { Timeout = TimeSpan.FromMilliseconds(100) };
            var hash = Convert.ToHexString(SHA256.HashData(new byte[] { 1 }));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                AssetDownload.Download("https://model.invalid/weights", path, hash, 1, CancellationToken.None, client));
            Assert.True(body.Started);
            Assert.Equal("previous model", await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public async Task AnInvalidDownloadPreservesThePreviousModel(long limit, bool validHash)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dictate-download-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "model.bin");
        try
        {
            await File.WriteAllTextAsync(path, "previous model");
            byte[] bytes = [1, 2, 3];
            using var client = new HttpClient(new Handler(new MemoryStream(bytes)));
            var hash = Convert.ToHexString(SHA256.HashData(validHash ? bytes : new byte[] { 4 }));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                AssetDownload.Download("https://model.invalid/weights", path, hash, limit, CancellationToken.None, client));
            Assert.Equal("previous model", await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}

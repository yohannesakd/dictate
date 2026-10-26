using System.Net;
using System.Text;
using System.Text.Json;
using Dictate.Core;
using Xunit;

namespace Dictate.Tests;

public class ChatGptAuthChecks
{
    private sealed class Secrets : ISecretStore
    {
        public string? Value;
        public Task<string?> ReadAsync(string account, CancellationToken cancellation = default) => Task.FromResult(Value);
        public Task WriteAsync(string account, string? value, CancellationToken cancellation = default)
        {
            Value = value;
            return Task.CompletedTask;
        }
    }

    private sealed class Replies(bool delayFirst = false) : HttpMessageHandler
    {
        public readonly TaskCompletionSource Arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var call = Interlocked.Increment(ref Calls);
            Arrived.TrySetResult();
            if (delayFirst && call == 1) await Release.Task.WaitAsync(cancellation);
            var claims = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["https://api.openai.com/auth"] = new { chatgpt_account_id = "account-" + call }
            }))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    access_token = "fixture." + claims + ".fixture", refresh_token = "fixture-refresh", expires_in = 3600
                }))
            };
        }
    }

    private static Secrets Expired() => new()
    {
        Value = JsonSerializer.Serialize(new ChatGptAuth.Token("fixture", "fixture-refresh", DateTimeOffset.UnixEpoch, "previous-account"))
    };

    private static string Callback(ChatGptAuth.SignIn signIn) => ChatGptAuth.RedirectUri + "?state=" + signIn.State + "&code=fixture";

    [Fact]
    public async Task SignOutRemovesCredentialsAfterDelayedRefresh()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var replies = new Replies(delayFirst: true);
        using var http = new HttpClient(replies);
        var secrets = Expired();
        var auth = new ChatGptAuth(http, secrets);
        var refresh = auth.GetTokenAsync(deadline.Token);
        await replies.Arrived.Task.WaitAsync(deadline.Token);
        var signOut = auth.SignOutAsync(deadline.Token);
        replies.Release.SetResult();
        await Task.WhenAll(refresh, signOut);
        Assert.Null(secrets.Value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.GetTokenAsync(deadline.Token));
        Assert.Equal(1, replies.Calls);
    }

    [Fact]
    public async Task SignOutInvalidatesPendingBrowserSignIn()
    {
        using var replies = new Replies();
        using var http = new HttpClient(replies);
        var secrets = new Secrets();
        var auth = new ChatGptAuth(http, secrets);
        var signIn = ChatGptAuth.Begin();
        await auth.SignOutAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.CompleteAsync(signIn, Callback(signIn), CancellationToken.None));
        Assert.Null(secrets.Value);
        Assert.Equal(0, replies.Calls);
    }

    [Fact]
    public async Task NewBrowserSignInWorksAfterSignOut()
    {
        using var replies = new Replies();
        using var http = new HttpClient(replies);
        var secrets = new Secrets();
        var auth = new ChatGptAuth(http, secrets);
        await auth.SignOutAsync();
        var signIn = ChatGptAuth.Begin();
        await auth.CompleteAsync(signIn, Callback(signIn), CancellationToken.None);
        Assert.Equal("account-1", (await auth.GetTokenAsync(CancellationToken.None)).AccountId);
        Assert.Equal(1, replies.Calls);
    }

    [Fact]
    public async Task NewAccountCannotBeOverwrittenByEarlierRefresh()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var replies = new Replies(delayFirst: true);
        using var http = new HttpClient(replies);
        var secrets = Expired();
        var auth = new ChatGptAuth(http, secrets);
        var refresh = auth.GetTokenAsync(deadline.Token);
        await replies.Arrived.Task.WaitAsync(deadline.Token);
        var signIn = ChatGptAuth.Begin();
        var complete = auth.CompleteAsync(signIn, Callback(signIn), deadline.Token);
        replies.Release.SetResult();
        await Task.WhenAll(refresh, complete);
        Assert.Equal("account-2", (await auth.GetTokenAsync(deadline.Token)).AccountId);
        Assert.Equal(2, replies.Calls);
    }
}

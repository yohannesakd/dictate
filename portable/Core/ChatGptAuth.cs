using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dictate.Core;

public sealed class ChatGptAuth(HttpClient http, ISecretStore secrets)
{
    public sealed record Token(string Access, string Refresh, DateTimeOffset Expires, string AccountId);
    public sealed record SignIn(string Url, string State, string Verifier);
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    public const string RedirectUri = "http://localhost:1455/auth/callback";
    private static readonly SemaphoreSlim RefreshGate = new(1);

    public static SignIn Begin()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code", ["client_id"] = ClientId, ["redirect_uri"] = RedirectUri,
            ["scope"] = "openid profile email offline_access", ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier))),
            ["code_challenge_method"] = "S256", ["state"] = state, ["id_token_add_organizations"] = "true",
            ["codex_cli_simplified_flow"] = "true", ["originator"] = "dictate"
        };
        return new("https://auth.openai.com/oauth/authorize?" + string.Join('&', query.Select(pair => pair.Key + "=" + Uri.EscapeDataString(pair.Value))), state, verifier);
    }

    public async Task CompleteAsync(SignIn signIn, string callback, CancellationToken cancellation)
    {
        var uri = new Uri(callback);
        if (uri.GetLeftPart(UriPartial.Path) != RedirectUri) throw new InvalidOperationException("The sign-in callback address is invalid.");
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "");
        if (!query.TryGetValue("state", out var state) || state != signIn.State || !query.TryGetValue("code", out var code))
            throw new InvalidOperationException("The sign-in callback does not match this sign-in.");
        var token = await RequestAsync(new() { ["grant_type"] = "authorization_code", ["code"] = code, ["code_verifier"] = signIn.Verifier, ["redirect_uri"] = RedirectUri }, cancellation).ConfigureAwait(false);
        await secrets.WriteAsync("chatgpt-token", JsonSerializer.Serialize(token), cancellation).ConfigureAwait(false);
    }

    public async Task<Token> GetTokenAsync(CancellationToken cancellation)
    {
        await RefreshGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var stored = await secrets.ReadAsync("chatgpt-token", cancellation).ConfigureAwait(false);
            var token = stored is null ? null : JsonSerializer.Deserialize<Token>(stored);
            if (token is null) throw new InvalidOperationException("Sign in with ChatGPT in Settings.");
            if (token.Expires > DateTimeOffset.UtcNow.AddSeconds(60)) return token;
            token = await RequestAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = token.Refresh }, cancellation).ConfigureAwait(false);
            await secrets.WriteAsync("chatgpt-token", JsonSerializer.Serialize(token), cancellation).ConfigureAwait(false);
            return token;
        }
        finally { RefreshGate.Release(); }
    }

    private async Task<Token> RequestAsync(Dictionary<string, string> fields, CancellationToken cancellation)
    {
        fields["client_id"] = ClientId;
        using var response = await http.PostAsync("https://auth.openai.com/oauth/token", new FormUrlEncodedContent(fields), cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false), cancellationToken: cancellation).ConfigureAwait(false);
        var root = json.RootElement;
        var access = root.GetProperty("access_token").GetString()!;
        var payload = access.Split('.')[1].Replace('-', '+').Replace('_', '/');
        using var claims = JsonDocument.Parse(Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '=')));
        return new(access, root.GetProperty("refresh_token").GetString()!, DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetDouble()),
            claims.RootElement.GetProperty("https://api.openai.com/auth").GetProperty("chatgpt_account_id").GetString()!);
    }

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Dictate.Core;

public sealed class HttpCleanupClient(HttpClient http, ISecretStore secrets) : ICleanupClient
{
    public async Task<string> CompleteAsync(CleanupProvider provider, string instructions, string message, CancellationToken cancellation)
    {
        if (provider == CleanupProvider.ChatGPT) return await CompleteChatGptAsync(instructions, message, cancellation).ConfigureAwait(false);
        var key = await secrets.ReadAsync(provider.ToString().ToLowerInvariant() + "-api-key", cancellation).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException($"Add a {provider} API key in Settings.");
        var endpoint = provider == CleanupProvider.Groq ? "https://api.groq.com/openai/v1/chat/completions" : "https://api.deepseek.com/chat/completions";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var payload = new Dictionary<string, object>
        {
            ["model"] = CleanupPrompt.Model(provider), ["temperature"] = 0, ["max_tokens"] = 4096,
            ["messages"] = new[] { new { role = "system", content = instructions }, new { role = "user", content = message } }
        };
        if (provider == CleanupProvider.Groq) payload["reasoning_effort"] = "none";
        request.Content = JsonContent.Create(payload);
        using var response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"{provider} returned HTTP {(int)response.StatusCode}.");
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false), cancellationToken: cancellation).ConfigureAwait(false);
        var choice = json.RootElement.GetProperty("choices")[0];
        if (choice.GetProperty("finish_reason").GetString() != "stop") throw new InvalidDataException($"{provider} sent an incomplete reply.");
        return choice.GetProperty("message").GetProperty("content").GetString()?.Trim() ?? throw new InvalidDataException($"{provider} sent no text.");
    }

    private async Task<string> CompleteChatGptAsync(string instructions, string message, CancellationToken cancellation)
    {
        var auth = new ChatGptAuth(http, secrets);
        var token = await auth.GetTokenAsync(cancellation).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://chatgpt.com/backend-api/codex/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Access);
        request.Headers.Add("chatgpt-account-id", token.AccountId);
        request.Headers.Add("originator", "dictate");
        request.Headers.Add("OpenAI-Beta", "responses=experimental");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = JsonContent.Create(new
        {
            model = CleanupPrompt.Model(CleanupProvider.ChatGPT), store = false, stream = true, instructions,
            input = new[] { new { role = "user", content = new[] { new { type = "input_text", text = message } } } },
            reasoning = new { effort = "none" }, text = new { verbosity = "low" }, prompt_cache_key = "dictate"
        });
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"ChatGPT returned HTTP {(int)response.StatusCode}.");
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false));
        var output = new System.Text.StringBuilder();
        var completed = false;
        while (await reader.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:")) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") continue;
            using var json = JsonDocument.Parse(data);
            switch (json.RootElement.GetProperty("type").GetString())
            {
                case "response.output_text.delta": output.Append(json.RootElement.GetProperty("delta").GetString()); break;
                case "response.completed": completed = true; break;
                case "response.failed": case "error": throw new InvalidDataException("ChatGPT could not complete cleanup.");
            }
        }
        if (!completed) throw new InvalidDataException("ChatGPT sent an incomplete reply.");
        return output.ToString().Trim();
    }
}

using System.Text.Json;

namespace Dictate.Core;

public static class AppCommands
{
    public static string ProviderName(CleanupProvider provider) => provider switch { CleanupProvider.Groq => "groq", CleanupProvider.DeepSeek => "deep_seek", _ => "chat_gpt" };
    public static string KeyAccount(string? provider) => provider switch { "groq" => "groq-api-key", "deep_seek" => "deepseek-api-key", _ => throw new ArgumentException("Select an API-key provider.") };
    public static string Word(JsonElement args, string field)
    {
        var text = args.GetProperty(field).GetString()?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > 1024 ? throw new ArgumentException("Enter a word or short phrase.") : text;
    }
    public static DictationSettings Patch(DictationSettings settings, JsonElement patch)
    {
        foreach (var entry in patch.EnumerateObject()) settings = entry.Name switch
        {
            "smart_cleanup" => settings with { SmartCleanup = entry.Value.GetBoolean() },
            "speech_compute" => settings with { SpeechCompute = entry.Value.GetString() switch
            {
                "automatic" => SpeechComputeMode.Automatic, "cpu_only" => SpeechComputeMode.CpuOnly,
                _ => throw new ArgumentException("Select automatic or CPU-only speech recognition.")
            } },
            "provider" => settings with { Provider = entry.Value.GetString() switch { "groq" => CleanupProvider.Groq, "deep_seek" => CleanupProvider.DeepSeek, "chat_gpt" => CleanupProvider.ChatGPT, _ => throw new ArgumentException("Select a cleanup provider.") } },
            "open_at_login" => settings with { OpenAtLogin = entry.Value.GetBoolean() },
            "fillers" => settings with { Rules = settings.Rules with { Fillers = entry.Value.GetBoolean() } },
            "repeats" => settings with { Rules = settings.Rules with { Repeats = entry.Value.GetBoolean() } },
            "scratch_that" => settings with { Rules = settings.Rules with { ScratchThat = entry.Value.GetBoolean() } },
            _ => throw new ArgumentException("The setting is not supported.")
        };
        return settings;
    }
}

using System.Text.Json;
using Dictate.Core;

namespace Dictate.Desktop;

internal static class MacMigration
{
    internal static void Import(FileStore store, string legacyDirectory)
    {
        if (File.Exists(store.SettingsPath)) return;
        var settings = JsonSerializer.Deserialize<DictationSettings>(MacNative.TakeString(MacNative.LegacySettings())!, FileStore.Json)
            ?? throw new InvalidDataException("The existing Mac settings are empty.");
        var source = Path.Combine(legacyDirectory, "transcripts.jsonl");
        var records = File.Exists(source) ? ConvertHistory(File.ReadLines(source)).ToArray() : [];
        Directory.CreateDirectory(store.DirectoryPath);
        var history = Path.Combine(store.DirectoryPath, "transcripts.jsonl");
        var staging = history + ".migration";
        File.WriteAllLines(staging, records.Select(record => JsonSerializer.Serialize(record, FileStore.Json)));
        File.Move(staging, history, true);
        // Settings mark a complete import. A failed import retries from the unchanged legacy source.
        store.SaveSettingsAsync(SpeechProfiles.Normalize(settings)).GetAwaiter().GetResult();
    }

    internal static IEnumerable<TranscriptRecord> ConvertHistory(IEnumerable<string> lines)
    {
        foreach (var line in lines.Where(line => line.Length != 0))
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            string? String(string name) => root.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
            var delivery = String("delivery_outcome") switch
            {
                "verified" => DeliveryOutcome.Verified, "sent" => DeliveryOutcome.Sent,
                "targetChanged" => DeliveryOutcome.FocusChanged, "cancelled" => DeliveryOutcome.Cancelled,
                _ => DeliveryOutcome.Failed
            };
            yield return new()
            {
                Id = String("id") is { } id ? Guid.Parse(id) : Guid.NewGuid(),
                Date = DateTimeOffset.Parse(String("date")!, System.Globalization.CultureInfo.InvariantCulture),
                Raw = String("raw") ?? "", Rules = String("rules") ?? "", Text = String("text") ?? "",
                Application = String("app"), Smart = String("smart") ?? "off", Note = String("note"),
                Provider = String("provider"), Model = String("model"), Prompt = String("prompt"), ModelReply = String("model_reply"),
                SpeechEngine = null, LegacySpeechEngine = String("speech_engine") ?? "legacy-mac", SpeechModel = String("speech_model"),
                AudioSeconds = root.TryGetProperty("audio_s", out var seconds) ? seconds.GetDouble() : 0,
                Delivery = delivery, PostedUtf16Units = root.TryGetProperty("posted_utf16_units", out var posted) ? posted.GetInt32() : 0,
                DeliveryEvidence = root.EnumerateObject().Where(field => field.Name is "verification_mismatch" or "expected_utf16_units" or "observed_utf16_units"
                    or "expected_selection_location" or "expected_selection_length" or "observed_selection_location" or "observed_selection_length")
                    .ToDictionary(field => field.Name, field => field.Value.ToString())
            };
        }
    }
}

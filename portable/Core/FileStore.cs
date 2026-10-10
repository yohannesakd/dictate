using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dictate.Core;

public sealed class FileStore(string directory) : IDictationStore
{
    public static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
    private readonly SemaphoreSlim gate = new(1);
    public string DirectoryPath { get; } = directory;
    public string SettingsPath => Path.Combine(DirectoryPath, "settings.json");

    public DictationSettings LoadSettings() => File.Exists(SettingsPath)
        ? JsonSerializer.Deserialize<DictationSettings>(File.ReadAllText(SettingsPath), Json) ?? throw new InvalidDataException("Settings are empty.")
        : new();

    public async Task SaveSettingsAsync(DictationSettings settings)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var staging = SettingsPath + ".tmp";
            await File.WriteAllTextAsync(staging, JsonSerializer.Serialize(settings, Json)).ConfigureAwait(false);
            File.Move(staging, SettingsPath, true);
        }
        finally { gate.Release(); }
    }

    public async Task AppendAsync(TranscriptRecord record)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            await File.AppendAllTextAsync(Path.Combine(DirectoryPath, "transcripts.jsonl"), JsonSerializer.Serialize(record, Json) + "\n").ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public IEnumerable<TranscriptRecord> History()
    {
        return Records().TakeLast(50).Reverse().ToArray();
    }

    public Usage Usage() => global::Dictate.Core.Usage.From(Records());

    private IEnumerable<TranscriptRecord> Records()
    {
        var path = Path.Combine(DirectoryPath, "transcripts.jsonl");
        if (!File.Exists(path)) return [];
        return File.ReadLines(path).Where(line => line.Length > 0)
            .Select(line => JsonSerializer.Deserialize<TranscriptRecord>(line, Json) ?? throw new InvalidDataException("History contains an empty record."));
    }
}

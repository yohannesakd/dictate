using Dictate.Core;
using Dictate.Speech;
using Xunit;

namespace Dictate.Tests;

public class SpeechProfileChecks
{
    [Fact]
    public async Task MissingEngineUsesWhisperWithoutReplacingSavedCleanup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dictate-default-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(directory);
            var store = new FileStore(directory);
            await File.WriteAllTextAsync(store.SettingsPath, "{\"smart_cleanup\":false,\"provider\":\"deep_seek\"}");
            var loaded = store.LoadSettings();
            Assert.Equal(SpeechEngineKind.Whisper, loaded.SpeechEngine);
            Assert.False(loaded.SmartCleanup);
            Assert.Equal(CleanupProvider.DeepSeek, loaded.Provider);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DigitalSilenceCannotProduceTextOrLoadTheModel()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dictate-silence-" + Guid.NewGuid());
        using var speech = new WhisperEngine(directory);
        Assert.Empty(await speech.TranscribeAsync(new float[32000], SpeechProfiles.WhisperModel, "en", CancellationToken.None));
        Assert.False(Directory.Exists(directory));
    }

    [Theory]
    [InlineData(SpeechEngineKind.Whisper)]
    [InlineData(SpeechEngineKind.Whistle)]
    public async Task ExistingSettingsUseWhisperWithoutChangingCleanupOrHistoricalIdentity(SpeechEngineKind engine)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dictate-settings-" + Guid.NewGuid());
        try
        {
            var store = new FileStore(directory);
            var saved = new DictationSettings { SpeechEngine = engine, SpeechModel = "small", Language = "auto", SmartCleanup = false, Provider = CleanupProvider.DeepSeek,
                Rules = new() { Fillers = false }, Dictionary = [new("shay", "Sai")] };
            await store.SaveSettingsAsync(saved);
            await store.AppendAsync(new() { Raw = "old", Rules = "old", Text = "old", Smart = "off", Delivery = DeliveryOutcome.Sent,
                SpeechEngine = engine, SpeechModel = "old-model",
                DeliveryEvidence = new() { ["verification_mismatch"] = "selection" } });
            var historyPath = Path.Combine(directory, "transcripts.jsonl");
            var historyBefore = await File.ReadAllTextAsync(historyPath);
            var loaded = store.LoadSettings();
            Assert.Equal(SpeechEngineKind.Whisper, loaded.SpeechEngine);
            Assert.Equal(SpeechProfiles.WhisperModel, loaded.SpeechModel);
            Assert.Equal("en", loaded.Language);
            Assert.False(loaded.SmartCleanup);
            Assert.Equal(CleanupProvider.DeepSeek, loaded.Provider);
            Assert.False(loaded.Rules.Fillers);
            Assert.Equal(saved.Dictionary, loaded.Dictionary);
            await store.SaveSettingsAsync(loaded);
            Assert.Equal(SpeechEngineKind.Whisper, new FileStore(directory).LoadSettings().SpeechEngine);
            Assert.Equal(historyBefore, await File.ReadAllTextAsync(historyPath));
            var history = store.History().Single();
            Assert.Equal(engine, history.SpeechEngine);
            Assert.Equal("old-model", history.SpeechModel);
            Assert.Equal("selection", history.DeliveryEvidence["verification_mismatch"]);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

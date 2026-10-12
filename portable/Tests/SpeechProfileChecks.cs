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
    public async Task DigitalSilenceCannotProduceTextOrLoadEitherModel()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dictate-silence-" + Guid.NewGuid());
        using var speech = new SpeechRecognizer(directory);
        foreach (var engine in SpeechProfiles.Engines)
            Assert.Empty(await speech.TranscribeAsync(new float[32000], engine, SpeechProfiles.WhisperModel, "en", CancellationToken.None));
        Assert.False(Directory.Exists(directory));
    }

    [Theory]
    [InlineData(SpeechEngineKind.Whisper)]
    [InlineData(SpeechEngineKind.Whistle)]
    public async Task ExistingSettingsKeepEngineAndCleanupWhileSelectingEnglishProfile(SpeechEngineKind engine)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dictate-settings-" + Guid.NewGuid());
        try
        {
            var store = new FileStore(directory);
            var saved = new DictationSettings { SpeechEngine = engine, SpeechModel = "small", Language = "auto", SmartCleanup = false,
                Rules = new() { Fillers = false }, Dictionary = [new("shay", "Sai")] };
            await store.SaveSettingsAsync(saved);
            var loaded = store.LoadSettings();
            Assert.Equal(engine, loaded.SpeechEngine);
            Assert.Equal(SpeechProfiles.WhisperModel, loaded.SpeechModel);
            Assert.Equal("en", loaded.Language);
            Assert.False(loaded.SmartCleanup);
            Assert.False(loaded.Rules.Fillers);
            Assert.Equal(saved.Dictionary, loaded.Dictionary);
            await store.AppendAsync(new() { Raw = "old", Rules = "old", Text = "old", Smart = "off", Delivery = DeliveryOutcome.Sent,
                SpeechEngine = SpeechEngineKind.Whisper, SpeechModel = "small",
                DeliveryEvidence = new() { ["verification_mismatch"] = "selection" } });
            var history = store.History().Single();
            Assert.Equal("small", history.SpeechModel);
            Assert.Equal("selection", history.DeliveryEvidence["verification_mismatch"]);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

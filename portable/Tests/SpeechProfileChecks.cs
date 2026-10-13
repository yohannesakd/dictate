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
            Assert.Equal(SpeechComputeMode.Automatic, loaded.SpeechCompute);
            Assert.False(loaded.SmartCleanup);
            Assert.Equal(CleanupProvider.DeepSeek, loaded.Provider);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CpuOnlySurvivesSettingsPatchAndRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dictate-compute-" + Guid.NewGuid());
        try
        {
            var store = new FileStore(directory);
            using var patch = System.Text.Json.JsonDocument.Parse("{\"speech_compute\":\"cpu_only\"}");
            await store.SaveSettingsAsync(AppCommands.Patch(store.LoadSettings(), patch.RootElement));
            Assert.Equal(SpeechComputeMode.CpuOnly, new FileStore(directory).LoadSettings().SpeechCompute);
            using var invalid = System.Text.Json.JsonDocument.Parse("{\"speech_compute\":\"unsupported\"}");
            Assert.Throws<ArgumentException>(() => AppCommands.Patch(store.LoadSettings(), invalid.RootElement));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task MacRestoresPhononWithoutChangingHardwareChoiceOrHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dictate-phonon-" + Guid.NewGuid());
        try
        {
            var oldStore = new FileStore(directory);
            await oldStore.SaveSettingsAsync(new() { SpeechCompute = SpeechComputeMode.CpuOnly, SmartCleanup = false,
                Provider = CleanupProvider.DeepSeek, Dictionary = [new("shay", "Sai")] });
            await oldStore.AppendAsync(new() { Raw = "old", Rules = "old", Text = "old", Smart = "off",
                Delivery = DeliveryOutcome.Sent, SpeechEngine = SpeechEngineKind.Whisper });
            var history = await File.ReadAllTextAsync(Path.Combine(directory, "transcripts.jsonl"));
            var macStore = new FileStore(directory, SpeechEngineKind.Phonon);
            var loaded = macStore.LoadSettings();
            Assert.Equal(SpeechEngineKind.Phonon, loaded.SpeechEngine);
            Assert.Equal(SpeechProfiles.PhononModel, loaded.SpeechModel);
            Assert.Equal("auto", loaded.Language);
            Assert.Equal(SpeechComputeMode.CpuOnly, loaded.SpeechCompute);
            Assert.False(loaded.SmartCleanup);
            Assert.Equal(CleanupProvider.DeepSeek, loaded.Provider);
            Assert.Equal(new[] { new DictionaryEntry("shay", "Sai") }, loaded.Dictionary);
            await macStore.SaveSettingsAsync(loaded);
            Assert.Equal(SpeechEngineKind.Phonon, macStore.LoadSettings().SpeechEngine);
            Assert.Equal(history, await File.ReadAllTextAsync(Path.Combine(directory, "transcripts.jsonl")));
            Assert.Equal(SpeechEngineKind.Whisper, macStore.History().Single().SpeechEngine);
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

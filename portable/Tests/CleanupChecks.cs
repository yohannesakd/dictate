using System.Text.Json;
using Dictate.Core;
using Xunit;

namespace Dictate.Tests;

public class CleanupChecks
{
    private sealed record GuardCase(string Raw, string Reply, bool Expected, DictionaryEntry[] Dictionary);

    [Fact]
    public void MacCleanupGuardCasesRemainUnchanged()
    {
        var cases = JsonSerializer.Deserialize<GuardCase[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures/cleanup-guard.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        foreach (var item in cases)
            Assert.True(CleanupGuard.Accepts(item.Reply, item.Raw, item.Dictionary) == item.Expected, item.Raw + " => " + item.Reply);
    }

    [Theory]
    [InlineData("Send it to the ER.", "Send it to the ER.")]
    [InlineData("Do not scratch that surface.", "Do not scratch that surface.")]
    [InlineData("Send it to John, scratch that, send it to Sarah.", "Send it to Sarah.")]
    [InlineData("Um, I I need help.", "I need help.")]
    [InlineData("Can you, um, check the logs?", "Can you check the logs?")]
    [InlineData("I think. Uh, can you check?", "I think. Can you check?")]
    [InlineData("Check the logs um.", "Check the logs.")]
    [InlineData("I think that that is right.", "I think that that is right.")]
    [InlineData("No, no, no, that's not it.", "No, no, no, that's not it.")]
    public void RuleCleanupPreservesMacBehavior(string raw, string expected) => Assert.Equal(expected, TextCleanup.Clean(raw, new()));

    [Fact]
    public void DictionaryUsesLongestMatchAndDoesNotChain()
    {
        var dictionary = new PersonalDictionary([new("new york", "NY"), new("new york city", "NYC"), new("nyc", "elsewhere")]);
        Assert.Equal("Visit NYC and NY.", dictionary.Apply("Visit new york city and new york."));
        Assert.Equal("Someone's soul", new PersonalDictionary([new("some", "none")]).Apply("Someone's soul"));
    }

    [Fact]
    public void CleanupGateDoesNotSendOrdinarySpeech()
    {
        Assert.False(CleanupGuard.NeedsModel("Actually, would you like tea or coffee?", []));
        Assert.False(CleanupGuard.NeedsModel("No, no, no, that's not it.", []));
        Assert.True(CleanupGuard.NeedsModel("I want to, I want to go home.", []));
        Assert.True(CleanupGuard.NeedsModel("Ask soul to help.", [new("soul", "Sol")]));
    }
}

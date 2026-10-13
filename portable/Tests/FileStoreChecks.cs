using Dictate.Core;
using Xunit;

namespace Dictate.Tests;

public class FileStoreChecks
{
    [Fact]
    public async Task HistoryReadsCannotObserveAnIncompleteAppend()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dictate-history-" + Guid.NewGuid());
        try
        {
            var store = new FileStore(directory);
            var text = string.Concat(Enumerable.Repeat("Dictation remains complete. ", 200));
            var writer = Task.Run(async () =>
            {
                for (var i = 0; i < 40; i++)
                    await store.AppendAsync(new() { Raw = text, Rules = text, Text = text, Smart = "off", Delivery = DeliveryOutcome.Verified });
            });
            var reader = Task.Run(() =>
            {
                for (var i = 0; i < 40; i++)
                    Assert.All(store.History(), record => Assert.Equal(text, record.Text));
            });
            await Task.WhenAll(writer, reader);
            Assert.Equal(40, store.History().Count());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void UnixAppDataIsPrivateEvenWhenTheDirectoryAlreadyExists()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "dictate-private-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(directory);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            _ = new FileStore(directory);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}

using System.Text;
using Dictate.Core;

namespace Dictate.Desktop;

public sealed class LinuxDelivery : ITextDelivery
{
    public static bool IsWayland => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    public ITextTarget CaptureTarget()
    {
        var field = AtSpiField.Focused();
        if (field is not null) return new AccessibleTarget(field);
        return new UnsupportedTarget();
    }

    public static string? SelectedText()
    {
        using var field = AtSpiField.Focused();
        var state = field?.Read();
        return state is { } value && value.End > value.Start ? value.Text[value.Start..value.End] : null;
    }

    private sealed class AccessibleTarget(AtSpiField field) : ITextTarget
    {
        public string? Application => "Linux accessible field";
        public async Task<DeliveryReceipt> InsertAsync(string text, string raw, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!field.IsFocused) return new(DeliveryOutcome.FocusChanged, Error: "The focused field changed. Copy the text from History.");
            var state = field.Read();
            if (state is not { } value) return new(DeliveryOutcome.Unsupported, Error: "The field does not expose readable text. Copy the transcript from History.");
            var inserted = text;
            if (value.Start > 0 && !char.IsWhiteSpace(value.Text[value.Start - 1]) && inserted.Length > 0 && char.IsLetterOrDigit(inserted[0])) inserted = " " + inserted;
            if (value.End < value.Text.Length && char.IsLetterOrDigit(value.Text[value.End]) && inserted.Length > 0 && !char.IsWhiteSpace(inserted[^1])) inserted += " ";
            var expected = value.Text[..value.Start] + inserted + value.Text[value.End..];
            var caret = value.Start + inserted.Length;
            if (!field.IsFocused) return new(DeliveryOutcome.FocusChanged);
            if (!field.ReplaceRange(value.Text, value.Start, value.End, inserted)) return new(DeliveryOutcome.Failed, Error: "The app refused text insertion. Some text can remain. Check the field before copying from History.");
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var after = field.Read();
                if (after is { } current && current.Text == expected && current.Start == caret && current.End == caret)
                    return new(DeliveryOutcome.Verified, inserted.Length, value.Start == value.End ? new Correction(field, expected, value.Start, inserted.Length, caret) : null);
                await Task.Delay(15).ConfigureAwait(false);
            }
            return new(DeliveryOutcome.Sent, inserted.Length);
        }
    }

    private sealed class Correction(AtSpiField field, string expected, int start, int length, int caret) : ITextCorrection
    {
        public Task<bool> ReplaceAsync(string text, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var state = field.Read();
            if (!field.IsFocused || state is not { } value || value.Text != expected || value.Start != caret || value.End != caret) return Task.FromResult(false);
            var replacement = text;
            if (replacement.Length > 0 && start > 0 && !char.IsWhiteSpace(expected[start - 1]) && char.IsLetterOrDigit(replacement[0])) replacement = " " + replacement;
            if (replacement.Length > 0 && start + length < expected.Length && char.IsLetterOrDigit(expected[start + length]) && !char.IsWhiteSpace(replacement[^1])) replacement += " ";
            if (!field.Select(expected[..start].EnumerateRunes().Count(), expected[..(start + length)].EnumerateRunes().Count())) return Task.FromResult(false);
            return Task.FromResult(field.ReplaceRange(expected, start, start + length, replacement));
        }
    }

    private sealed class UnsupportedTarget : ITextTarget
    {
        public string? Application => "Linux";
        public Task<DeliveryReceipt> InsertAsync(string text, string raw, CancellationToken cancellation) => Task.FromResult(new DeliveryReceipt(DeliveryOutcome.Unsupported,
            Error: "This field does not expose accessible text. Copy the transcript from History."));
    }

}

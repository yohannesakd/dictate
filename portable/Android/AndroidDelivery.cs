using Android.Views.InputMethods;
using Dictate.Core;

namespace Dictate.Android;

public sealed class AndroidDelivery : ITextDelivery
{
    private sealed record Binding(long Generation, IInputConnection Connection, string? Application);
    private readonly object sync = new();
    private Binding? binding;
    private long generation;
    public bool HasTarget { get { lock (sync) return binding is not null; } }

    public void Bind(IInputConnection? connection, string? application)
    {
        lock (sync) { generation++; binding = connection is null ? null : new(generation, connection, application); }
    }

    public ITextTarget CaptureTarget()
    {
        lock (sync) return new Target(this, binding);
    }

    public string? SelectedText()
    {
        lock (sync) return binding?.Connection.GetSelectedText(0)?.ToString();
    }

    private bool Current(Binding target) { lock (sync) return binding?.Generation == target.Generation; }

    private sealed class Target(AndroidDelivery owner, Binding? binding) : ITextTarget
    {
        public string? Application => binding?.Application;
        public Task<DeliveryReceipt> InsertAsync(string text, string raw, CancellationToken cancellation) => MainThread.Run(() =>
        {
            if (cancellation.IsCancellationRequested) return new DeliveryReceipt(DeliveryOutcome.Cancelled);
            if (binding is null || !owner.Current(binding)) return new DeliveryReceipt(DeliveryOutcome.FocusChanged, Error: "The text field changed. Copy the text from History.");
            var before = Read(binding.Connection);
            var inserted = before is { } state ? Format(text, state.Text, state.Start, state.End) : text;
            if (!binding.Connection.CommitText(inserted, 1)) return new DeliveryReceipt(DeliveryOutcome.Failed, Error: "The app refused the text. Copy it from History.");
            if (before is not { } value) return new DeliveryReceipt(DeliveryOutcome.Sent, inserted.Length);
            var expected = value.Text[..value.Start] + inserted + value.Text[value.End..];
            var caret = value.Start + inserted.Length;
            var after = Read(binding.Connection);
            if (after is not { } result || result.Text != expected || result.Start != caret || result.End != caret)
                return new DeliveryReceipt(DeliveryOutcome.Sent, inserted.Length);
            ITextCorrection? correction = value.Start == value.End ? new Correction(owner, binding, expected, value.Start, inserted.Length, caret) : null;
            return new DeliveryReceipt(DeliveryOutcome.Verified, inserted.Length, correction);
        });
    }

    private sealed class Correction(AndroidDelivery owner, Binding binding, string expected, int start, int length, int caret) : ITextCorrection
    {
        public Task<bool> ReplaceAsync(string text, CancellationToken cancellation) => MainThread.Run(() =>
        {
            var state = Read(binding.Connection);
            if (!owner.Current(binding) || state is not { } value || value.Text != expected || value.Start != caret || value.End != caret) return false;
            cancellation.ThrowIfCancellationRequested();
            if (!binding.Connection.SetSelection(start, start + length)) return false;
            return binding.Connection.CommitText(text.Length == 0 ? "" : Format(text, expected, start, start + length), 1);
        });
    }

    private static (string Text, int Start, int End)? Read(IInputConnection connection)
    {
        using var request = new ExtractedTextRequest { HintMaxChars = 1_000_001, HintMaxLines = 10000 };
        using var value = connection.GetExtractedText(request, 0);
        var text = value?.Text?.ToString();
        if (value is null || text is null || text.Length > 1_000_000 || value.StartOffset != 0 || value.PartialStartOffset >= 0
            || value.SelectionStart < 0 || value.SelectionEnd < value.SelectionStart || value.SelectionEnd > text.Length) return null;
        return (text, value.SelectionStart, value.SelectionEnd);
    }

    private static string Format(string text, string value, int start, int end)
    {
        if (start > 0 && !char.IsWhiteSpace(value[start - 1]) && text.Length > 0 && char.IsLetterOrDigit(text[0])) text = " " + text;
        if (end < value.Length && char.IsLetterOrDigit(value[end]) && text.Length > 0 && !char.IsWhiteSpace(text[^1])) text += " ";
        return text;
    }
}

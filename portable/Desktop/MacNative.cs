using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Dictate.Core;

namespace Dictate.Desktop;

internal sealed class MacObject : SafeHandleZeroOrMinusOneIsInvalid
{
    internal MacObject(nint pointer) : base(true) => SetHandle(pointer);
    protected override bool ReleaseHandle() { MacNative.Release(handle); return true; }
}

internal static class MacNative
{
    private const string Library = "dictate-macos";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void ResultCallback(nint result, nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void LevelCallback(float level);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void NoticeCallback(nint notice);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void KeyCallback(int action);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void LoopCallback();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void DispatchCallback(nint context);
    private static readonly ResultCallback completed = (result, context) =>
    {
        var handle = GCHandle.FromIntPtr(context);
        var source = (TaskCompletionSource<JsonElement>)handle.Target!;
        try { using var json = JsonDocument.Parse(Marshal.PtrToStringUTF8(result)!); source.SetResult(json.RootElement.Clone()); }
        catch (Exception error) { source.SetException(error); }
        finally { handle.Free(); }
    };

    internal static string? TakeString(nint pointer)
    {
        if (pointer == 0) return null;
        try { return Marshal.PtrToStringUTF8(pointer); }
        finally { Free(pointer); }
    }
    internal static void Check(nint error)
    {
        if (TakeString(error) is { } message) throw new InvalidOperationException(message);
    }
    internal static async Task<JsonElement> InvokeAsync(Action<MacObject, ResultCallback, nint> start, CancellationToken cancellation)
    {
        using var operation = new MacObject(Operation());
        using var registration = cancellation.Register(() => Cancel(operation));
        var source = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = GCHandle.Alloc(source);
        try { start(operation, completed, GCHandle.ToIntPtr(context)); }
        catch { context.Free(); throw; }
        // Wait for the native receipt, including partial posting after cancellation.
        return await source.Task.ConfigureAwait(false);
    }
    internal static JsonElement Permissions(bool request)
    {
        using var json = JsonDocument.Parse(TakeString(GetPermissions(request ? 1 : 0))!);
        return json.RootElement.Clone();
    }

    [DllImport(Library, EntryPoint = "dictate_release")] internal static extern void Release(nint pointer);
    [DllImport(Library, EntryPoint = "dictate_free")] internal static extern void Free(nint pointer);
    [DllImport(Library, EntryPoint = "dictate_operation")] private static extern nint Operation();
    [DllImport(Library, EntryPoint = "dictate_cancel")] private static extern void Cancel(MacObject operation);
    [DllImport(Library, EntryPoint = "dictate_phonon")] internal static extern void Phonon([MarshalAs(UnmanagedType.LPUTF8Str)] string directory,
        float[] pcm, int count, int cpuOnly, MacObject operation, ResultCallback callback, nint context);
    [DllImport(Library, EntryPoint = "dictate_phonon_unload")] internal static extern void PhononUnload();
    [DllImport(Library, EntryPoint = "dictate_target")] internal static extern nint Target();
    [DllImport(Library, EntryPoint = "dictate_target_name")] internal static extern nint TargetName(MacObject target);
    [DllImport(Library, EntryPoint = "dictate_selected_text")] internal static extern nint SelectedText();
    [DllImport(Library, EntryPoint = "dictate_insert")] internal static extern void Insert(MacObject target, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, [MarshalAs(UnmanagedType.LPUTF8Str)] string raw, MacObject operation, ResultCallback callback, nint context);
    [DllImport(Library, EntryPoint = "dictate_correct")] internal static extern void Correct(MacObject correction, int restore, MacObject operation, ResultCallback callback, nint context);
    [DllImport(Library, EntryPoint = "dictate_audio_start")] internal static extern nint AudioStart(LevelCallback level, NoticeCallback notice);
    [DllImport(Library, EntryPoint = "dictate_audio_stop")] internal static extern nint AudioStop(out int count);
    [DllImport(Library, EntryPoint = "dictate_keyboard_start")] internal static extern int KeyboardStart(KeyCallback callback);
    [DllImport(Library, EntryPoint = "dictate_keyboard_stop")] internal static extern void KeyboardStop();
    [DllImport(Library, EntryPoint = "dictate_keyboard_active")] internal static extern void KeyboardActive(int active);
    [DllImport(Library, EntryPoint = "dictate_permissions")] private static extern nint GetPermissions(int request);
    [DllImport(Library, EntryPoint = "dictate_run_loop")] internal static extern void RunLoop(LoopCallback started);
    [DllImport(Library, EntryPoint = "dictate_stop_loop")] internal static extern void StopLoop();
    [DllImport(Library, EntryPoint = "dictate_post")] internal static extern void Post(DispatchCallback callback, nint context);
    [DllImport(Library, EntryPoint = "dictate_secret_read")] internal static extern nint SecretRead([MarshalAs(UnmanagedType.LPUTF8Str)] string account);
    [DllImport(Library, EntryPoint = "dictate_secret_write")] internal static extern nint SecretWrite([MarshalAs(UnmanagedType.LPUTF8Str)] string account, [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);
    [DllImport(Library, EntryPoint = "dictate_legacy_settings")] internal static extern nint LegacySettings();
}

public sealed class MacDelivery : ITextDelivery
{
    public ITextTarget CaptureTarget() => new Target(new MacObject(MacNative.Target()));
    public static string? SelectedText() => MacNative.TakeString(MacNative.SelectedText());
    private sealed class Target(MacObject handle) : ITextTarget
    {
        public string? Application { get; } = MacNative.TakeString(MacNative.TargetName(handle));
        public async Task<DeliveryReceipt> InsertAsync(string text, string raw, CancellationToken cancellation)
        {
            using (handle)
            {
                var result = await MacNative.InvokeAsync((operation, callback, context) => MacNative.Insert(handle, text, raw, operation, callback, context), cancellation).ConfigureAwait(false);
                var outcome = result.GetProperty("outcome").GetString();
                var evidence = result.TryGetProperty("evidence", out var details)
                    ? details.EnumerateObject().ToDictionary(field => field.Name, field => field.Value.ToString()) : [];
                var caretOnly = evidence.GetValueOrDefault("verification_mismatch") == "selection";
                var mapped = outcome switch
                {
                    "verified" => DeliveryOutcome.Verified, "sent" => DeliveryOutcome.Sent,
                    "unexpected" when caretOnly => DeliveryOutcome.Sent,
                    "targetChanged" => DeliveryOutcome.FocusChanged, "cancelled" => DeliveryOutcome.Cancelled,
                    _ => DeliveryOutcome.Failed
                };
                ITextCorrection? correction = result.TryGetProperty("correction", out var pointer)
                    ? new Correction(new MacObject((nint)pointer.GetInt64()), raw) : null;
                var error = outcome switch
                {
                    "verified" or "sent" or "cancelled" => null,
                    "targetChanged" => "The target field changed. Copy the text from History.",
                    "permissionDenied" => "Allow Accessibility access in Dictate Settings.",
                    "unchanged" => "The target field did not change. Copy the text from History.",
                    "unexpected" when caretOnly => "Dictation reached the field, but its caret position could not be verified.",
                    _ => "Text delivery was incomplete. Check the target field or copy the text from History."
                };
                return new(mapped, result.GetProperty("posted_units").GetInt32(), correction, error) { Evidence = evidence };
            }
        }
    }
    private sealed class Correction(MacObject handle, string raw) : ITextCorrection
    {
        public async Task<bool> ReplaceAsync(string text, CancellationToken cancellation)
        {
            if (text.Length != 0 && text != raw) return false;
            using (handle)
            {
                var result = await MacNative.InvokeAsync((operation, callback, context) => MacNative.Correct(handle, text.Length == 0 ? 0 : 1, operation, callback, context), cancellation).ConfigureAwait(false);
                return result.GetProperty("verified").GetBoolean();
            }
        }
    }
}

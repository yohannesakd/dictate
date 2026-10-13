using System.Runtime.InteropServices;
using System.Text.Json;
using Whisper.net;
using Whisper.net.LibraryLoader;

if (args is not [var directory, var modelPath, var device] || device is not ("NPU" or "CPU"))
    throw new ArgumentException("Supply the encoder directory, decoder file, and NPU, or CPU for verification.");
void Reply(object value) { Console.WriteLine("DICTATE_NPU " + JsonSerializer.Serialize(value)); Console.Out.Flush(); }
WhisperFactory? factory = null;
WhisperProcessor? processor = null;
try
{
    if (device == "NPU" && !OpenVinoDevices.HasNpu()) { Reply(new { available = false }); return; }
    Reply(new { available = true });
    using var input = new BinaryReader(Console.OpenStandardInput());
    while (true)
    {
        byte command;
        try { command = input.ReadByte(); }
        catch (EndOfStreamException) { break; }
        if (command == 1)
        {
            RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.OpenVino];
            factory = WhisperFactory.FromPath(modelPath, new() { UseGpu = false });
            processor = factory.CreateBuilder().WithThreads(4).WithNoContext().WithLanguage("en")
                .WithOpenVinoEncoder(Path.Combine(directory, "openvino-base.en", "ggml-base.en-encoder.xml"), device,
                    Path.Combine(directory, "openvino-cache")).Build();
            Reply(new { ready = true });
        }
        else if (command == 2 && processor is not null)
        {
            var count = input.ReadInt32();
            if (count < 1 || count > 300 * 16000) throw new InvalidDataException("Record at most five minutes of speech.");
            var bytes = input.ReadBytes(count * sizeof(float));
            if (bytes.Length != count * sizeof(float)) throw new EndOfStreamException();
            var pcm = new float[count];
            Buffer.BlockCopy(bytes, 0, pcm, 0, bytes.Length);
            var parts = new List<string>();
            await foreach (var segment in processor.ProcessAsync(pcm))
            {
                var text = segment.Text.Replace("[BLANK_AUDIO]", "", StringComparison.Ordinal).Trim();
                if (text.Length > 0) parts.Add(text);
            }
            Reply(new { text = string.Join(' ', parts) });
        }
        else throw new InvalidDataException("The NPU service command is invalid.");
    }
}
catch (Exception error) { Reply(new { error = error.Message }); }
finally { processor?.Dispose(); factory?.Dispose(); }

internal static class OpenVinoDevices
{
    [StructLayout(LayoutKind.Sequential)] private struct Devices { internal nint Names; internal nuint Count; }
    [DllImport("openvino_c", CallingConvention = CallingConvention.Cdecl)] private static extern int ov_core_create(out nint core);
    [DllImport("openvino_c", CallingConvention = CallingConvention.Cdecl)] private static extern int ov_core_get_available_devices(nint core, out Devices devices);
    [DllImport("openvino_c", CallingConvention = CallingConvention.Cdecl)] private static extern void ov_core_free(nint core);
    [DllImport("openvino_c", CallingConvention = CallingConvention.Cdecl)] private static extern void ov_available_devices_free(ref Devices devices);
    internal static bool HasNpu()
    {
        if (ov_core_create(out var core) != 0) return false;
        try
        {
            if (ov_core_get_available_devices(core, out var devices) != 0) return false;
            try
            {
                for (nuint index = 0; index < devices.Count; index++)
                {
                    var name = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(devices.Names, checked((int)index * IntPtr.Size)));
                    if (name == "NPU" || name?.StartsWith("NPU.", StringComparison.Ordinal) == true) return true;
                }
                return false;
            }
            finally { ov_available_devices_free(ref devices); }
        }
        finally { ov_core_free(core); }
    }
}

using System.Text;

namespace Dictate.Speech;

/// <summary>Reads and writes the 16 kHz mono PCM16 boundary used by native speech processes and evaluation fixtures.</summary>
public static class WaveAudio
{
    public static async Task WriteAsync(string path, float[] pcm, CancellationToken cancellation)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var writer = new BinaryWriter(file, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8); writer.Write(36 + pcm.Length * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000);
        writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(pcm.Length * 2);
        var bytes = new byte[pcm.Length * 2];
        for (var i = 0; i < pcm.Length; i++) System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), (short)Math.Clamp(Math.Round(pcm[i] * 32768), short.MinValue, short.MaxValue));
        await file.WriteAsync(bytes, cancellation).ConfigureAwait(false);
    }

    public static float[] Read(string path)
    {
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Expected RIFF audio.");
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Expected WAVE audio.");
        bool format = false;
        float[]? pcm = null;
        while (file.Position + 8 <= file.Length)
        {
            var chunk = new string(reader.ReadChars(4));
            var size = reader.ReadUInt32();
            var end = file.Position + size;
            if (end > file.Length) throw new InvalidDataException("Truncated WAV chunk.");
            if (chunk == "fmt ")
            {
                if (size < 16 || reader.ReadInt16() != 1 || reader.ReadInt16() != 1 || reader.ReadInt32() != 16000)
                    throw new InvalidDataException("Use mono PCM16 audio at 16 kHz.");
                reader.ReadInt32(); reader.ReadInt16();
                if (reader.ReadInt16() != 16) throw new InvalidDataException("Use 16-bit PCM audio.");
                format = true;
            }
            else if (chunk == "data")
            {
                if (size % 2 != 0 || size > 300 * 16000 * 2) throw new InvalidDataException("Use at most five minutes of PCM16 audio.");
                pcm = new float[size / 2];
                for (var i = 0; i < pcm.Length; i++) pcm[i] = reader.ReadInt16() / 32768f;
            }
            file.Position = end + size % 2;
        }
        return format && pcm is not null ? pcm : throw new InvalidDataException("Missing WAV format or audio.");
    }
}

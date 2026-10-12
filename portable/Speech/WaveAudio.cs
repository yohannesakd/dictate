namespace Dictate.Speech;

/// <summary>Reads 16 kHz mono PCM16 evaluation fixtures.</summary>
public static class WaveAudio
{
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

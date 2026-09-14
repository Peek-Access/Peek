namespace Peek.Core.Services;

/// <summary>
/// Generates short WAV PCM tones/chimes in-memory for UI sound cues (dock monitor
/// switching, AI narration lifecycle, ...) - trivial single/multi-frequency blips, so
/// synthesizing them avoids shipping (and AOT/trim-scanning) extra embedded audio assets
/// for something this small. Shared by every *SoundPlayer that just needs a short beep.
/// </summary>
public static class ToneSynthesizer
{
    private const int SampleRate = 44100;

    /// <summary>One sine-wave tone with a linear fade-out (avoids an audible click at the tail).</summary>
    public static byte[] GenerateTone(double hz, double durationSeconds, double amplitude = 0.35) =>
        WriteWav(GenerateSamples(hz, durationSeconds, amplitude));

    /// <summary>Concatenates several tones back-to-back into one chime (e.g. a two-note "done" cue).</summary>
    public static byte[] GenerateSequence(params (double Hz, double DurationSeconds)[] notes)
    {
        var samples = new List<short>();
        foreach (var (hz, duration) in notes)
            samples.AddRange(GenerateSamples(hz, duration, 0.35));

        return WriteWav([.. samples]);
    }

    private static short[] GenerateSamples(double hz, double durationSeconds, double amplitude)
    {
        var sampleCount = (int)(SampleRate * durationSeconds);
        var samples = new short[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (double)SampleRate;
            var envelope = 1.0 - i / (double)sampleCount;
            samples[i] = (short)(Math.Sin(2 * Math.PI * hz * t) * short.MaxValue * amplitude * envelope);
        }

        return samples;
    }

    private static byte[] WriteWav(short[] samples)
    {
        const int bitsPerSample = 16;
        const int channels = 1;
        var byteRate = SampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);
        var dataSize = samples.Length * (bitsPerSample / 8);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)channels);
        writer.Write(SampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataSize);
        foreach (var sample in samples)
            writer.Write(sample);

        return stream.ToArray();
    }
}

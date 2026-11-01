using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using VGAudio.Containers.NintendoWare;
using VGAudio.Formats.Pcm16;

namespace RRMusicPackMaker;

public sealed record SourceInfo(int SampleRate, double Duration, long? LoopStart, long? LoopEnd);

/// <summary>Stereo audio at a known sample rate with an optional loop, in samples.</summary>
public sealed class Song
{
    public required short[][] Channels { get; set; }
    public required int Rate { get; init; }
    public bool Loop { get; set; }
    public int LoopStart { get; set; }
    public int LoopEnd { get; set; }
    public int Length => Channels[0].Length;
}

public static class Tools
{
    public static string Find(string exe)
    {
        var dirs = new List<string> { AppContext.BaseDirectory };
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Smoothie", "bin"));
        foreach (var dir in dirs)
        {
            string path = Path.Combine(dir.Trim('"'), exe + ".exe");
            if (File.Exists(path))
                return path;
        }
        throw new FileNotFoundException($"{exe}.exe was not found. Install FFmpeg, or put {exe}.exe next to this program.");
    }

    public static byte[] RunBinary(string exe, params string[] args)
    {
        var info = new ProcessStartInfo(Find(exe))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {exe}.");
        var errors = process.StandardError.ReadToEndAsync();
        using var output = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(output);
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            string message = errors.Result.Trim();
            throw new InvalidDataException($"{exe} failed: {(message.Length > 300 ? message[^300..] : message)}");
        }
        return output.ToArray();
    }
}

public static class AudioIO
{
    public static SourceInfo Probe(string path)
    {
        byte[] json = Tools.RunBinary("ffprobe", "-v", "error", "-of", "json", "-show_streams", "-show_format", "-select_streams", "a:0", path);
        using var doc = JsonDocument.Parse(json);
        var streams = doc.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() == 0)
            throw new InvalidDataException("The file has no audio stream.");
        var stream = streams[0];
        int rate = int.Parse(stream.GetProperty("sample_rate").GetString() ?? "0", CultureInfo.InvariantCulture);
        double duration = 0;
        if (doc.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var d))
            double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);

        var (start, end) = ReadBrstmLoop(path);
        if (start == null)
            (start, end) = ReadWavLoop(path);
        if (start == null)
        {
            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CollectTags(format, tags);
            CollectTags(stream, tags);
            start = Tag(tags, "LOOPSTART", "LOOP_START");
            long? length = Tag(tags, "LOOPLENGTH", "LOOP_LENGTH");
            end = Tag(tags, "LOOPEND", "LOOP_END");
            if (start != null && end == null && length != null)
                end = start + length;
        }
        return new SourceInfo(rate, duration, start, end);
    }

    private static void CollectTags(JsonElement element, Dictionary<string, string> tags)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("tags", out var values))
            return;
        foreach (var tag in values.EnumerateObject())
            tags[tag.Name] = tag.Value.GetString() ?? "";
    }

    private static long? Tag(Dictionary<string, string> tags, params string[] names)
    {
        foreach (var name in names)
            if (tags.TryGetValue(name, out var value) && long.TryParse(value, out long parsed))
                return parsed;
        return null;
    }

    private static (long?, long?) ReadBrstmLoop(string path)
    {
        using var file = File.OpenRead(path);
        var header = new byte[0x100];
        if (file.Read(header, 0, header.Length) < 0x90 || Encoding.ASCII.GetString(header, 0, 4) != "RSTM")
            return (null, null);
        int head = BigInt(header, 0x10);
        int info = head + 8 + BigInt(header, head + 0x0C);
        if (header[info + 1] == 0)
            return (null, null);
        return (BigInt(header, info + 8), BigInt(header, info + 12));
    }

    private static (long?, long?) ReadWavLoop(string path)
    {
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file);
        if (file.Length < 12 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
            return (null, null);
        reader.ReadInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
            return (null, null);
        while (file.Position + 8 <= file.Length)
        {
            string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            long size = reader.ReadUInt32();
            long next = file.Position + size + (size & 1);
            if (id == "smpl" && size >= 60)
            {
                file.Position += 28;
                int loops = reader.ReadInt32();
                file.Position += 4;
                if (loops > 0)
                {
                    file.Position += 8;
                    long start = reader.ReadUInt32();
                    long end = reader.ReadUInt32();
                    return (start, end + 1);
                }
            }
            file.Position = next;
        }
        return (null, null);
    }

    private static int BigInt(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

    /// <summary>Decodes any format FFmpeg reads to 16-bit stereo at the given rate.</summary>
    public static short[][] Decode(string path, int rate)
    {
        byte[] raw = Tools.RunBinary("ffmpeg", "-v", "error", "-nostdin", "-i", path, "-vn", "-map", "0:a:0",
            "-f", "s16le", "-acodec", "pcm_s16le", "-ac", "2", "-ar", rate.ToString(CultureInfo.InvariantCulture),
            "-af", "aresample=filter_size=64:phase_shift=14:cutoff=0.97", "pipe:1");
        int samples = raw.Length / 4;
        if (samples == 0)
            throw new InvalidDataException("The file decoded to no audio.");
        var left = new short[samples];
        var right = new short[samples];
        for (int i = 0; i < samples; i++)
        {
            left[i] = (short)(raw[i * 4] | (raw[i * 4 + 1] << 8));
            right[i] = (short)(raw[i * 4 + 2] | (raw[i * 4 + 3] << 8));
        }
        return [left, right];
    }

    /// <summary>Parses samples (123456) or a time (1:23.5, 83.5s) into source samples.</summary>
    public static long? ParsePosition(string text, int sourceRate)
    {
        text = text.Trim();
        if (text.Length == 0)
            return null;
        if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long samples))
            return samples;
        if (text.EndsWith('s'))
            text = text[..^1];
        double seconds = 0;
        foreach (string part in text.Split(':'))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value < 0)
                throw new FormatException($"\"{text}\" is not a sample number or a time like 1:23.5.");
            seconds = seconds * 60 + value;
        }
        return (long)Math.Round(seconds * sourceRate);
    }

    public static string FormatTime(double seconds)
    {
        int minutes = (int)(seconds / 60);
        return $"{minutes}:{(seconds - minutes * 60).ToString("00.000", CultureInfo.InvariantCulture)}";
    }

    /// <summary>A time exact enough to keep a loop on the right sample.</summary>
    public static string FormatPrecise(double seconds)
    {
        int minutes = (int)(seconds / 60);
        return $"{minutes}:{(seconds - minutes * 60).ToString("00.000000", CultureInfo.InvariantCulture)}";
    }

    public static void WriteBrstm(Song song, string path, int channelCount, int rate)
    {
        var channels = new short[channelCount][];
        for (int i = 0; i < channelCount; i++)
            channels[i] = song.Channels[i % 2];

        Pcm16Format format = new(channels, rate);
        if (song.Loop)
            format = format.WithLoop(true, song.LoopStart, song.LoopEnd);

        string temp = path + ".tmp";
        using (var stream = File.Create(temp))
            new BrstmWriter().WriteToStream(format, stream);
        File.Move(temp, path, true);
    }

    public static (bool Loop, int Start, int End, int Rate, int Channels, int Samples) ReadBrstmSummary(string path)
    {
        using var stream = File.OpenRead(path);
        var audio = new BrstmReader().Read(stream);
        var format = audio.GetFormat<VGAudio.Formats.GcAdpcm.GcAdpcmFormat>();
        return (format.Looping, format.LoopStart, format.LoopEnd, format.SampleRate, format.ChannelCount, format.SampleCount);
    }

    /// <summary>Writes a short WAV of the loop seam: the seconds before the end, then the seconds after the start.</summary>
    public static string WriteSeamPreview(Song song, double seconds = 4)
    {
        int count = (int)(seconds * song.Rate);
        int fromEnd = Math.Max(0, song.LoopEnd - count);
        int beforeLength = song.LoopEnd - fromEnd;
        int afterLength = Math.Min(count, song.Length - song.LoopStart);
        return WriteWav(song.Channels, song.Rate, (fromEnd, beforeLength), (song.LoopStart, afterLength));
    }

    /// <summary>Writes a WAV of part of a song, for listening from a chosen point.</summary>
    public static string WriteRangePreview(short[][] channels, int rate, int start, double seconds)
    {
        int length = Math.Min((int)(seconds * rate), channels[0].Length - start);
        return WriteWav(channels, rate, (start, Math.Max(0, length)));
    }

    private static string WriteWav(short[][] channels, int rate, params (int Start, int Length)[] parts)
    {
        string path = Path.Combine(Path.GetTempPath(), $"rrmusic_preview_{Environment.ProcessId}_{Guid.NewGuid():N}.wav");
        using var writer = new BinaryWriter(File.Create(path));
        int bytes = parts.Sum(part => part.Length) * 4;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + bytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(rate);
        writer.Write(rate * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(bytes);
        foreach (var (start, length) in parts)
            for (int i = start; i < start + length; i++)
            {
                writer.Write(channels[0][i]);
                writer.Write(channels[1][i]);
            }
        return path;
    }
}

public static class Processing
{
    private const double SilenceLevel = 33; // About -60 dBFS.
    private const double EvenLoudnessRms = 0.16; // About -16 dBFS.
    private const double PeakLimit = 0.944; // About -0.5 dBFS.

    /// <summary>Turns a row's source and settings into audio ready to encode.</summary>
    public static Song Prepare(string source, int rate, LoopMode mode, string loopStart, string loopEnd, string loudness, Action<string>? log = null, double volumeDb = 0, double crossfadeSeconds = 0)
    {
        var info = AudioIO.Probe(source);
        var song = new Song { Channels = AudioIO.Decode(source, rate), Rate = rate };
        double scale = info.SampleRate > 0 ? (double)rate / info.SampleRate : 1;
        int ToTarget(long position) => (int)Math.Round(position * scale);
        int len = song.Length;

        switch (mode)
        {
            case LoopMode.NoLoop:
                break;
            case LoopMode.WholeSong:
                SetLoop(song, 0, len);
                break;
            case LoopMode.Custom:
                long? start = AudioIO.ParsePosition(loopStart, info.SampleRate);
                long? end = AudioIO.ParsePosition(loopEnd, info.SampleRate);
                SetLoop(song, start is null ? 0 : ToTarget(start.Value), end is null ? len : ToTarget(end.Value));
                break;
            case LoopMode.FindLoop:
                var found = LoopFinder.Find(song.Channels, rate);
                if (found is { } f)
                {
                    SetLoop(song, f.Start, f.End);
                    log?.Invoke($"Found loop {AudioIO.FormatTime((double)f.Start / rate)} to {AudioIO.FormatTime((double)f.End / rate)}, match {f.Score:P0}.");
                }
                else
                {
                    SetLoop(song, 0, len);
                    log?.Invoke("No clear loop found. Looping the whole song.");
                }
                break;
            default:
                if (info.LoopStart != null)
                {
                    SetLoop(song, ToTarget(info.LoopStart.Value), info.LoopEnd is null ? len : ToTarget(info.LoopEnd.Value));
                    log?.Invoke("Using the loop points stored in the file.");
                }
                else
                {
                    SetLoop(song, 0, len);
                }
                break;
        }

        TrimSilence(song);
        ApplyCrossfade(song, crossfadeSeconds);
        ApplyLoudness(song, loudness);
        ApplyVolume(song, volumeDb);
        return song;
    }

    /// <summary>
    /// Blends the end of the loop into its start, so any song loops without a hard cut.
    /// The loop gets shorter by the blend, and it restarts right after the audio it just blended in.
    /// </summary>
    public static void ApplyCrossfade(Song song, double seconds)
    {
        if (!song.Loop || seconds <= 0)
            return;
        int start = song.LoopStart;
        int end = song.LoopEnd;
        int fade = Math.Min((int)(seconds * song.Rate), (end - start) / 3);
        if (fade < 2)
            return;
        foreach (var channel in song.Channels)
            for (int i = 0; i < fade; i++)
            {
                double position = (i + 0.5) / fade * Math.PI / 2;
                double mixed = channel[end - fade + i] * Math.Cos(position) + channel[start + i] * Math.Sin(position);
                channel[end - fade + i] = (short)Math.Clamp(Math.Round(mixed), short.MinValue, short.MaxValue);
            }
        song.LoopStart = start + fade;
    }

    public static void ApplyVolume(Song song, double decibels)
    {
        if (decibels == 0)
            return;
        double gain = Math.Pow(10, decibels / 20);
        foreach (var channel in song.Channels)
            for (int i = 0; i < channel.Length; i++)
                channel[i] = (short)Math.Clamp(Math.Round(channel[i] * gain), short.MinValue, short.MaxValue);
    }

    private static void SetLoop(Song song, int start, int end)
    {
        end = Math.Clamp(end, 1, song.Length);
        start = Math.Clamp(start, 0, end - 1);
        if (end - start < song.Rate / 10)
            throw new InvalidDataException("The loop is shorter than 0.1 seconds. Check the loop points.");
        song.Loop = true;
        song.LoopStart = start;
        song.LoopEnd = end;
    }

    /// <summary>Removes silence at the start, and at the end if nothing is lost from the loop.</summary>
    internal static void TrimSilence(Song song)
    {
        int first = 0;
        int last = song.Length - 1;
        while (first < last && IsSilent(song, first))
            first++;
        while (last > first && IsSilent(song, last))
            last--;

        // A loop that starts inside the silence must keep its place, so stop at the loop start.
        if (song.Loop)
            first = Math.Min(first, song.LoopStart);

        bool loopsToEnd = !song.Loop || song.LoopEnd >= song.Length;
        int newEnd = loopsToEnd ? last + 1 : song.Length;
        if (first == 0 && newEnd == song.Length)
            return;

        if (newEnd - first < song.Rate / 10)
            return;

        song.Channels = song.Channels.Select(c => c[first..newEnd]).ToArray();
        if (song.Loop)
        {
            song.LoopStart -= first;
            song.LoopEnd = loopsToEnd ? song.Length : song.LoopEnd - first;
        }
    }

    private static bool IsSilent(Song song, int index) =>
        Math.Abs((int)song.Channels[0][index]) <= SilenceLevel && Math.Abs((int)song.Channels[1][index]) <= SilenceLevel;

    private static void ApplyLoudness(Song song, string loudness)
    {
        if (loudness == Options.LoudnessOff)
            return;
        double peak = 0;
        double sum = 0;
        foreach (var channel in song.Channels)
            foreach (short sample in channel)
            {
                double value = Math.Abs(sample / 32768.0);
                peak = Math.Max(peak, value);
                sum += value * value;
            }
        if (peak <= 0)
            return;
        double rms = Math.Sqrt(sum / (song.Length * 2));
        double gain = PeakLimit / peak;
        if (loudness == Options.LoudnessEven)
            gain = Math.Min(gain, EvenLoudnessRms / rms);

        foreach (var channel in song.Channels)
            for (int i = 0; i < channel.Length; i++)
                channel[i] = (short)Math.Clamp(Math.Round(channel[i] * gain), short.MinValue, short.MaxValue);
    }
}

/// <summary>Finds a seamless loop: the longest stretch of music that plays again later, unchanged.</summary>
public static class LoopFinder
{
    private const int AnalysisRate = 8000;
    private const int WindowSize = 2048;
    private const int HopSize = 512;
    private const int Bands = 24;
    private const double MinimumLoopSeconds = 20;
    private const double MinimumRepeatSeconds = 12;
    private const double SmoothSeconds = 1;
    private const double MatchLevel = 0.7;
    private const double AllowedGapSeconds = 2;
    private const double LongerLoopBonus = 0.25;
    private const double StartMarginSeconds = 0.25;
    private const double WalkStepSeconds = 0.5;
    private const double CheckSeconds = 4;
    private const double MinimumWaveMatch = 0.8;
    private const double CandidateGapSeconds = 1;
    private const int MaxCandidates = 12;
    private const double LooseMinimumLoopSeconds = 15;
    private const double LooseMinimumRepeatSeconds = 4;
    private const double LooseMatchLevel = 0.5;
    private const double LooseCheckSeconds = 2;
    private const double LooseMinimumWaveMatch = 0.3;
    private const int LooseMaxTried = 40;

    public readonly record struct Result(int Start, int End, double Score);

    public static Result? Find(short[][] channels, int rate)
    {
        float[] mono = Mix(channels);
        float[] low = Decimate(mono, rate);
        if (low.Length < WindowSize * 4)
            return null;

        float[][] frames = Features(low);
        double framesPerSecond = AnalysisRate / (double)HopSize;
        int minLag = (int)(MinimumLoopSeconds * framesPerSecond);
        int minRun = (int)(MinimumRepeatSeconds * framesPerSecond);
        int smooth = (int)(SmoothSeconds * framesPerSecond);
        if (frames.Length < minLag + minRun + smooth)
            return null;

        // For every possible loop length, find the longest stretch that repeats at that distance.
        int lagCount = frames.Length - minRun - smooth - minLag;
        var runs = new (int Length, int Start)[lagCount];
        Parallel.For(0, lagCount, offset => runs[offset] = LongestRepeat(frames, minLag + offset, smooth, MatchLevel));

        var candidates = Enumerable.Range(0, lagCount)
            .Where(offset => runs[offset].Length >= minRun)
            .OrderByDescending(offset => runs[offset].Length + LongerLoopBonus * (minLag + offset))
            .ToList();

        // The spectrum only says the music looks alike. The waveform must line up too, or the jump would be heard.
        double samplesPerFrame = HopSize * (rate / (double)AnalysisRate);
        int minimumGap = (int)(CandidateGapSeconds * framesPerSecond);
        var tried = new List<int>();
        foreach (int offset in candidates)
        {
            if (tried.Count == MaxCandidates)
                break;
            if (tried.Any(other => Math.Abs(other - offset) < minimumGap))
                continue;
            tried.Add(offset);

            // Tune the length in the middle of the run, the part most surely inside the repeat.
            int middle = (int)((runs[offset].Start + runs[offset].Length / 2) * samplesPerFrame);
            int lag = Refine(mono, middle, (int)((minLag + offset) * samplesPerFrame), rate, (int)samplesPerFrame + 16);
            if (middle + lag + CheckSeconds * rate >= mono.Length)
                continue;
            double match = WaveMatch(mono, middle, middle + lag, (int)(CheckSeconds * rate));
            if (match < MinimumWaveMatch)
                continue;

            // Walk back on the waveform to where the repeat begins, so the loop keeps as much music as it can.
            int step = (int)(WalkStepSeconds * rate);
            int start = middle;
            while (start - step >= 0 && WaveMatch(mono, start - step, start - step + lag, step) >= MinimumWaveMatch)
                start -= step;

            // The last step passed with most of it matching, so its first moments may be from before the repeat.
            start = Math.Min(middle, start + (int)(StartMarginSeconds * rate));
            return new Result(start, start + lag, match);
        }
        return null;
    }

    /// <summary>
    /// Finds several possible loops, best first, for a person to choose from by ear.
    /// Far looser than Find, so it also finds repeats in songs with vocals or live parts.
    /// </summary>
    public static List<Result> FindCandidates(short[][] channels, int rate, int maximum = 6)
    {
        var results = new List<Result>();
        float[] mono = Mix(channels);
        float[] low = Decimate(mono, rate);
        if (low.Length < WindowSize * 4)
            return results;

        float[][] frames = Features(low);
        double framesPerSecond = AnalysisRate / (double)HopSize;
        int minLag = (int)(LooseMinimumLoopSeconds * framesPerSecond);
        int minRun = (int)(LooseMinimumRepeatSeconds * framesPerSecond);
        int smooth = (int)(SmoothSeconds * framesPerSecond);
        int lagCount = frames.Length - minRun - smooth - minLag;
        if (lagCount <= 0)
            return results;

        var runs = new (int Length, int Start)[lagCount];
        Parallel.For(0, lagCount, offset => runs[offset] = LongestRepeat(frames, minLag + offset, smooth, LooseMatchLevel));

        double samplesPerFrame = HopSize * (rate / (double)AnalysisRate);
        int minimumGap = (int)(CandidateGapSeconds * framesPerSecond);
        var tried = new List<int>();
        var ranked = Enumerable.Range(0, lagCount)
            .Where(offset => runs[offset].Length >= minRun)
            .OrderByDescending(offset => runs[offset].Length + LongerLoopBonus * (minLag + offset));
        foreach (int offset in ranked)
        {
            if (tried.Count == LooseMaxTried)
                break;
            if (tried.Any(other => Math.Abs(other - offset) < minimumGap))
                continue;
            tried.Add(offset);

            int middle = (int)((runs[offset].Start + runs[offset].Length / 2) * samplesPerFrame);
            int lag = Refine(mono, middle, (int)((minLag + offset) * samplesPerFrame), rate, (int)samplesPerFrame + 16);
            int check = (int)(LooseCheckSeconds * rate);
            if (middle + lag + check >= mono.Length)
                continue;
            double match = WaveMatch(mono, middle, middle + lag, check);
            if (match < LooseMinimumWaveMatch)
                continue;

            int step = (int)(WalkStepSeconds * rate);
            double keep = Math.Max(LooseMinimumWaveMatch, match * 0.8);
            int start = middle;
            while (start - step >= 0 && WaveMatch(mono, start - step, start - step + lag, step) >= keep)
                start -= step;
            start = Math.Min(middle, start + (int)(StartMarginSeconds * rate));
            results.Add(new Result(start, start + lag, match));
        }

        return results
            .OrderByDescending(result => Math.Round(result.Score * 20))
            .ThenByDescending(result => result.End - result.Start)
            .Take(maximum)
            .ToList();
    }

    /// <summary>Moves a hand-placed loop end to where the music best lines up with the loop start.</summary>
    public static int SnapEnd(short[][] channels, int start, int end, int rate)
    {
        float[] mono = Mix(channels);
        int window = rate;
        if (start + window >= mono.Length)
            return end;
        return start + Refine(mono, start, end - start, rate, rate / 20);
    }

    private static (int Length, int Start) LongestRepeat(float[][] frames, int lag, int smooth, double level)
    {
        int count = frames.Length - lag;
        var sums = new double[count + 1];
        for (int i = 0; i < count; i++)
            sums[i + 1] = sums[i] + Dot(frames[i], frames[i + lag]);

        int bestLength = 0;
        int bestStart = 0;
        int runStart = -1;
        int lastMatch = -1;
        int allowedGap = (int)(AllowedGapSeconds * AnalysisRate / HopSize);
        double need = level * smooth;
        for (int i = 0; i + smooth <= count; i++)
        {
            if (sums[i + smooth] - sums[i] < need)
                continue;

            // Quiet moments match badly, so a short gap does not end the run.
            if (runStart < 0 || i - lastMatch > allowedGap)
                runStart = i;
            lastMatch = i;
            if (i - runStart + 1 > bestLength)
            {
                bestLength = i - runStart + 1;
                bestStart = runStart;
            }
        }
        return (bestLength, bestStart);
    }

    /// <summary>Fine tunes the loop length to the sample using the waveform.</summary>
    private static int Refine(float[] mono, int start, int lag, int rate, int range)
    {
        int window = rate;
        int best = lag;
        double bestScore = double.MinValue;
        object gate = new();
        Parallel.For(lag - range, lag + range + 1, candidate =>
        {
            if (start + candidate + window >= mono.Length)
                return;
            double score = 0;
            for (int i = 0; i < window; i += 2)
                score += mono[start + i] * mono[start + candidate + i];
            lock (gate)
            {
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
        });
        return best;
    }

    private static double WaveMatch(float[] mono, int a, int b, int length)
    {
        length = Math.Min(length, mono.Length - b);
        double both = 0, first = 0, second = 0;
        for (int i = 0; i < length; i++)
        {
            both += mono[a + i] * mono[b + i];
            first += mono[a + i] * mono[a + i];
            second += mono[b + i] * mono[b + i];
        }
        return first <= 0 || second <= 0 ? 0 : both / Math.Sqrt(first * second);
    }

    private static float[] Mix(short[][] channels)
    {
        var mono = new float[channels[0].Length];
        for (int i = 0; i < mono.Length; i++)
            mono[i] = (channels[0][i] + channels[1][i]) / 65536f;
        return mono;
    }

    private static float[] Decimate(float[] input, int rate)
    {
        double step = rate / (double)AnalysisRate;
        var output = new float[(int)(input.Length / step)];
        int span = Math.Max(1, (int)step);
        for (int i = 0; i < output.Length; i++)
        {
            int start = (int)(i * step);
            float sum = 0;
            int end = Math.Min(start + span, input.Length);
            for (int j = start; j < end; j++)
                sum += input[j];
            output[i] = sum / span;
        }
        return output;
    }

    private static float[][] Features(float[] audio)
    {
        int count = (audio.Length - WindowSize) / HopSize;
        var frames = new float[count][];
        var window = new double[WindowSize];
        for (int i = 0; i < WindowSize; i++)
            window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / WindowSize);

        Parallel.For(0, count, f =>
        {
            var buffer = new Complex[WindowSize];
            for (int i = 0; i < WindowSize; i++)
                buffer[i] = audio[f * HopSize + i] * window[i];
            Fft(buffer);

            var bands = new float[Bands];
            for (int bin = 1; bin < WindowSize / 2; bin++)
            {
                double position = Math.Log(bin) / Math.Log(WindowSize / 2.0) * Bands;
                int band = Math.Min(Bands - 1, (int)position);
                bands[band] += (float)buffer[bin].Magnitude;
            }

            for (int b = 0; b < Bands; b++)
                bands[b] = MathF.Log(1 + bands[b] * 50);
            frames[f] = bands;
        });

        // Remove each band's average, so a steady sound does not count as a match, then scale every frame to length 1.
        for (int b = 0; b < Bands; b++)
        {
            int band = b;
            double mean = frames.Average(frame => frame[band]);
            double spread = Math.Sqrt(frames.Average(frame => (frame[band] - mean) * (frame[band] - mean)));
            foreach (var frame in frames)
                frame[band] = spread < 1e-4 ? 0 : (float)((frame[band] - mean) / spread);
        }
        foreach (var frame in frames)
        {
            double norm = Math.Sqrt(frame.Sum(value => (double)value * value));
            if (norm > 0)
                for (int b = 0; b < Bands; b++)
                    frame[b] = (float)(frame[b] / norm);
        }
        return frames;
    }

    private static double Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
            sum += a[i] * b[i];
        return sum;
    }

    private static void Fft(Complex[] data)
    {
        int n = data.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
                (data[i], data[j]) = (data[j], data[i]);
        }
        for (int size = 2; size <= n; size <<= 1)
        {
            double angle = -2 * Math.PI / size;
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (int i = 0; i < n; i += size)
            {
                Complex w = Complex.One;
                for (int j = 0; j < size / 2; j++)
                {
                    Complex even = data[i + j];
                    Complex odd = data[i + j + size / 2] * w;
                    data[i + j] = even + odd;
                    data[i + j + size / 2] = even - odd;
                    w *= step;
                }
            }
        }
    }
}

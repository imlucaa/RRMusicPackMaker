using System.Text;
using RRMusicPackMaker;
using Xunit;

namespace RRMusicPackMaker.Tests;

public class ParsePositionTests
{
    [Theory]
    [InlineData("123456", 32000, 123456)]
    [InlineData("1:00", 32000, 1920000)]
    [InlineData("0:01.5", 32000, 48000)]
    [InlineData("2.5s", 1000, 2500)]
    [InlineData("0:01.000000", 44100, 44100)]
    public void ParsePosition_ValidText_ReturnsSamples(string text, int rate, long expected) =>
        Assert.Equal(expected, AudioIO.ParsePosition(text, rate));

    [Fact]
    public void ParsePosition_Empty_ReturnsNull() => Assert.Null(AudioIO.ParsePosition("  ", 32000));

    [Fact]
    public void ParsePosition_Garbage_Throws() => Assert.Throws<FormatException>(() => AudioIO.ParsePosition("abc", 32000));

    [Fact]
    public void FormatPrecise_RoundTrips_WithinOneSample()
    {
        const int rate = 32000;
        foreach (int sample in new[] { 0, 1, 12345, 5104657 })
        {
            long back = AudioIO.ParsePosition(AudioIO.FormatPrecise((double)sample / rate), rate)!.Value;
            Assert.InRange(back, sample - 1, sample + 1);
        }
    }
}

public class CatalogTests
{
    [Theory]
    [InlineData("n_Circuit32_n", "n_Circuit32_f")]
    [InlineData("STRM_N_WATER_N", "STRM_N_WATER_F")]
    [InlineData("r_64_battle_n", "r_64_battle_f")]
    public void FinalNameOf_SwapsSuffixKeepingCase(string normal, string final) =>
        Assert.Equal(final, Catalog.FinalNameOf(normal));

    [Fact]
    public void Build_WithoutInstall_StillListsSheetFiles()
    {
        var rows = Catalog.Build(null, out _);
        foreach (string file in new[] { "title_bg", "offline_bg", "wifi_globe_bg", "wifi_lobby_bg", "n_Circuit32_n", "n_BossMI", "o_FanfareGP1_32", "STRM_N_KOOPA_N" })
            Assert.Contains(rows, r => r.FileBase == file);
        Assert.Equal(rows.Count, rows.Select(r => r.FileBase).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

public class MatchingTests
{
    private static List<TrackRow> Rows() =>
    [
        new() { Name = "Luigi Circuit / Mario Circuit (shared file)", FileBase = "n_Circuit32_n" },
        new() { Name = "Coconut Mall", FileBase = "n_Shopping32_n" },
        new() { Name = "SNES Mario Circuit 1", FileBase = "0_N" },
        new() { Name = "SNES Mario Circuit 1 > SW2 Mario Circuit 1", FileBase = "sw2MC1_N" },
        new() { Name = "SNES Mario Circuit 1 (variant 2)", FileBase = "x1_N" },
        new() { Name = "Wii Moo Moo Meadows", FileBase = "89_N" },
        new() { Name = "Wii Moo Moo Meadows", FileBase = "b31_N" },
    ];

    private static TrackRow Row(List<TrackRow> rows, string file) => rows.Single(r => r.FileBase == file);

    [Theory]
    [InlineData("Luigi Circuit.mp3", "n_Circuit32_n")]
    [InlineData("Mario Circuit.mp3", "n_Circuit32_n")]
    [InlineData("n_shopping32_n.flac", "n_Shopping32_n")]
    [InlineData("Coconut Mall (remix).ogg", "n_Shopping32_n")]
    [InlineData("SW2 Mario Circuit 1.mp3", "sw2MC1_N")]
    [InlineData("SNES Mario Circuit 1.mp3", "0_N")]
    public void AutoMatch_FindsTheRightTrack(string file, string expected)
    {
        var rows = Rows();
        var (matched, _) = PackBuilder.AutoMatch([$@"C:\music\{file}"], rows);
        Assert.Equal(1, matched);
        Assert.True(Row(rows, expected).IsAssigned);
    }

    [Fact]
    public void AutoMatch_TwoTracksSameName_AssignsNeither()
    {
        var rows = Rows();
        var (matched, unmatched) = PackBuilder.AutoMatch([@"C:\music\Wii Moo Moo Meadows.mp3"], rows);
        Assert.Equal(0, matched);
        Assert.Single(unmatched);
        Assert.DoesNotContain(rows, r => r.IsAssigned);
    }

    [Fact]
    public void AutoMatch_TwoSongsOneTrack_KeepsTheFirst()
    {
        var rows = Rows();
        var (matched, unmatched) = PackBuilder.AutoMatch([@"C:\a\Luigi Circuit.mp3", @"C:\a\Mario Circuit.mp3"], rows);
        Assert.Equal(1, matched);
        Assert.Single(unmatched);
        Assert.EndsWith("Luigi Circuit.mp3", Row(rows, "n_Circuit32_n").Source);
    }

    [Fact]
    public void FillFromFolder_MoreTracksThanSongs_FillsAllAndUsesEverySong()
    {
        var rows = Enumerable.Range(0, 10).Select(i => new TrackRow { Name = $"T{i}", FileBase = $"{i}_N" }).ToList();
        string[] files = [@"C:\m\a.mp3", @"C:\m\b.mp3", @"C:\m\c.mp3"];
        PackBuilder.FillFromFolder(files, rows, new Random(1));
        Assert.All(rows, r => Assert.True(r.IsAssigned));
        Assert.Equal(3, rows.Select(r => r.Source).Distinct().Count());
        // Songs stay evenly used: at most one extra play of any song.
        var counts = rows.GroupBy(r => r.Source).Select(g => g.Count()).ToList();
        Assert.True(counts.Max() - counts.Min() <= 1);
    }
}

public class ProcessingTests
{
    private static short[] Tone(int count, double frequency, int rate = 32000, double level = 12000)
    {
        var data = new short[count];
        for (int i = 0; i < count; i++)
            data[i] = (short)(Math.Sin(2 * Math.PI * frequency * i / rate) * level);
        return data;
    }

    private static Song Stereo(short[] data, int start, int end) => new()
    {
        Channels = [(short[])data.Clone(), (short[])data.Clone()],
        Rate = 32000,
        Loop = true,
        LoopStart = start,
        LoopEnd = end,
    };

    [Fact]
    public void ApplyCrossfade_ClosesTheGapAtTheJump()
    {
        // A tone that changes pitch halfway has a hard click when looped as is.
        short[] data = [.. Tone(160000, 440), .. Tone(160000, 700)];
        const int start = 40000, end = 280000;
        var plain = Stereo(data, start, end);
        var smooth = Stereo(data, start, end);
        Processing.ApplyCrossfade(smooth, 0.5);

        int Jump(Song s) => Math.Abs(s.Channels[0][s.LoopEnd - 1] - s.Channels[0][s.LoopStart]);
        Assert.True(Jump(smooth) < 1500, $"jump was {Jump(smooth)}");
        Assert.Equal(start + 16000, smooth.LoopStart);
        Assert.Equal(end, smooth.LoopEnd);
        Assert.NotEqual(plain.LoopStart, smooth.LoopStart);
    }

    [Fact]
    public void ApplyCrossfade_LongerThanLoop_IsLimitedToAThird()
    {
        var song = Stereo(Tone(64000, 440), 0, 64000);
        Processing.ApplyCrossfade(song, 60);
        Assert.Equal(64000 / 3, song.LoopStart);
    }

    [Fact]
    public void ApplyCrossfade_Off_ChangesNothing()
    {
        var song = Stereo(Tone(64000, 440), 100, 60000);
        var before = (short[])song.Channels[0].Clone();
        Processing.ApplyCrossfade(song, 0);
        Assert.Equal(100, song.LoopStart);
        Assert.Equal(before, song.Channels[0]);
    }

    [Fact]
    public void ApplyVolume_SixDecibels_RoughlyDoubles()
    {
        var song = Stereo(Tone(3200, 440, level: 4000), 0, 3200);
        int before = song.Channels[0].Max(s => Math.Abs((int)s));
        Processing.ApplyVolume(song, 6);
        Assert.InRange(song.Channels[0].Max(s => Math.Abs((int)s)), before * 1.9, before * 2.1);
    }

    [Fact]
    public void TrimSilence_MovesLoopWithTheAudio()
    {
        short[] data = [.. new short[64000], .. Tone(320000, 440)];
        var song = Stereo(data, 96000, 256000);
        Processing.TrimSilence(song);
        Assert.Equal(160000, song.LoopEnd - song.LoopStart);
        Assert.True(song.LoopStart < 96000);
        Assert.Equal(data[96000], song.Channels[0][song.LoopStart]);
    }
}

public class LoopFinderTests
{
    private const int Rate = 32000;

    /// <summary>Music-like audio: random notes that change every quarter second, reproducible from a seed.</summary>
    private static short[] Notes(int seconds, int seed)
    {
        var random = new Random(seed);
        var data = new short[seconds * Rate];
        double phase = 0, frequency = 300;
        for (int i = 0; i < data.Length; i++)
        {
            if (i % (Rate / 4) == 0)
                frequency = 150 + random.Next(0, 16) * 60;
            phase += 2 * Math.PI * frequency / Rate;
            double envelope = 1 - (i % (Rate / 4)) / (double)(Rate / 4) * 0.7;
            data[i] = (short)(Math.Sin(phase) * 12000 * envelope);
        }
        return data;
    }

    private static short[][] Stereo(short[] data) => [data, data];

    [Fact]
    public void Find_IntroThenLoopPlayedTwice_FindsExactLength()
    {
        short[] intro = Notes(8, 1), loop = Notes(40, 2);
        short[] song = [.. intro, .. loop, .. loop];
        var found = LoopFinder.Find(Stereo(song), Rate);
        Assert.NotNull(found);
        Assert.Equal(loop.Length, found.Value.End - found.Value.Start);
        Assert.True(found.Value.Start >= intro.Length - Rate / 2, $"start {found.Value.Start}");
    }

    [Fact]
    public void Find_NoRepeat_FindsNothing() => Assert.Null(LoopFinder.Find(Stereo(Notes(90, 3)), Rate));

    [Fact]
    public void FindCandidates_ExactRepeat_ListsItFirst()
    {
        short[] loop = Notes(30, 4);
        short[] song = [.. Notes(6, 5), .. loop, .. loop];
        var found = LoopFinder.FindCandidates(Stereo(song), Rate);
        Assert.NotEmpty(found);
        Assert.Equal(loop.Length, found[0].End - found[0].Start);
        Assert.True(found[0].Score > 0.95);
    }

    [Fact]
    public void FindCandidates_ShortSong_ReturnsEmpty() =>
        Assert.Empty(LoopFinder.FindCandidates(Stereo(Notes(5, 6)), Rate));

    [Fact]
    public void SnapEnd_SlightlyWrongEnd_MovesToExactMatch()
    {
        short[] loop = Notes(30, 7);
        short[] song = [.. Notes(6, 8), .. loop, .. loop];
        int start = 10 * Rate;
        int rightEnd = start + loop.Length;
        Assert.Equal(rightEnd, LoopFinder.SnapEnd(Stereo(song), start, rightEnd + 700, Rate));
    }
}

public class BrstmTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rrm_test_" + Guid.NewGuid().ToString("N"));

    public BrstmTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, true);

    private static Song Song(int start, int end)
    {
        var data = new short[64000];
        for (int i = 0; i < data.Length; i++)
            data[i] = (short)(Math.Sin(i * 0.05) * 9000);
        return new Song { Channels = [data, data], Rate = 32000, Loop = true, LoopStart = start, LoopEnd = end };
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void WriteBrstm_RoundTrips_ChannelsAndRate(int channels)
    {
        string path = Path.Combine(_folder, "a.brstm");
        AudioIO.WriteBrstm(Song(0, 64000), path, channels, 32000);
        var info = AudioIO.ReadBrstmSummary(path);
        Assert.Equal(channels, info.Channels);
        Assert.Equal(32000, info.Rate);
        Assert.True(info.Loop);
    }

    [Fact]
    public void WriteBrstm_LoopStartIsBlockAligned_LengthKept()
    {
        string path = Path.Combine(_folder, "b.brstm");
        AudioIO.WriteBrstm(Song(1000, 50000), path, 2, 32000);
        var info = AudioIO.ReadBrstmSummary(path);
        Assert.Equal(0, info.Start % 0x3800);
        Assert.Equal(49000, info.End - info.Start);
    }

    [Fact]
    public void WriteBrstm_FileHeader_MatchesRetroRewindFiles()
    {
        string path = Path.Combine(_folder, "c.brstm");
        AudioIO.WriteBrstm(Song(0, 64000), path, 2, 32000);
        byte[] d = File.ReadAllBytes(path);
        Assert.Equal("RSTM", Encoding.ASCII.GetString(d, 0, 4));
        Assert.Equal(new byte[] { 0xFE, 0xFF, 0x01, 0x00 }, d[4..8]);
        Assert.Equal(32000, (d[0x64] << 8) | d[0x65]);
    }
}

public class BuildTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rrm_build_" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];
    private readonly string _wav;

    public BuildTests()
    {
        Directory.CreateDirectory(_folder);
        _wav = Path.Combine(_folder, "song.wav");
        WriteWav(_wav, 3);
    }

    public void Dispose() => Directory.Delete(_folder, true);

    private static void WriteWav(string path, int seconds)
    {
        const int rate = 32000;
        int samples = seconds * rate;
        using var w = new BinaryWriter(File.Create(path));
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + samples * 4);
        w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);
        w.Write(rate);
        w.Write(rate * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(samples * 4);
        for (int i = 0; i < samples; i++)
        {
            short value = (short)(Math.Sin(i * 0.05) * 9000);
            w.Write(value);
            w.Write(value);
        }
    }

    private TrackRow Row(string file, bool stock = false) => new()
    {
        Name = file,
        FileBase = file,
        Source = _wav,
        StockFinalBase = stock ? Catalog.FinalNameOf(file) : null,
        PulsarFinalBase = stock ? null : file + "x",
    };

    private BuildResult Run(params TrackRow[] rows)
    {
        lock (_log)
            _log.Clear();
        return PackBuilder.Build(new Options { PackName = "T" }, rows, _folder, m => { lock (_log) _log.Add(m); });
    }

    private string Music(string name) => Path.Combine(_folder, "T", "RetroRewind6", "MyStuff", name + ".brstm");

    [Fact]
    public void Build_SameSongOnManyTracks_EncodesOnceAndCopies()
    {
        var result = Run(Row("1_N"), Row("2_N"), Row("3_N"));
        Assert.Equal(3, result.Built);
        Assert.Equal(1, result.Encoded);
        Assert.Equal(File.ReadAllBytes(Music("1_N")), File.ReadAllBytes(Music("3_N")));
    }

    [Fact]
    public void Build_NothingChanged_SkipsEncoding()
    {
        var row = Row("1_N");
        Run(row);
        var time = File.GetLastWriteTimeUtc(Music("1_N"));
        var second = Run(row);
        Assert.Equal(1, second.Unchanged);
        Assert.Equal(0, second.Encoded);
        Assert.Equal(time, File.GetLastWriteTimeUtc(Music("1_N")));
    }

    [Fact]
    public void Build_VolumeChanged_EncodesAgain()
    {
        var row = Row("1_N");
        Run(row);
        row.VolumeDb = 6;
        var second = Run(row);
        Assert.Equal(0, second.Unchanged);
        Assert.Equal(1, second.Encoded);
    }

    [Fact]
    public void Build_SongFileChanged_EncodesAgain()
    {
        var row = Row("1_N");
        Run(row);
        WriteWav(_wav, 4);
        Assert.Equal(1, Run(row).Encoded);
    }

    [Fact]
    public void Build_TrackRemoved_DeletesItsFile()
    {
        var keep = Row("1_N");
        Run(keep, Row("2_N"));
        Assert.True(File.Exists(Music("2_N")));
        Run(keep);
        Assert.False(File.Exists(Music("2_N")));
        Assert.True(File.Exists(Music("1_N")));
    }

    [Fact]
    public void Build_OutputDeletedByHand_IsRebuilt()
    {
        var row = Row("1_N");
        Run(row);
        File.Delete(Music("1_N"));
        Assert.Equal(1, Run(row).Encoded);
        Assert.True(File.Exists(Music("1_N")));
    }

    [Fact]
    public void Build_MissingSong_FailsOnlyThatTrack()
    {
        var bad = Row("2_N");
        bad.Source = Path.Combine(_folder, "gone.mp3");
        var result = Run(Row("1_N"), bad);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Built);
        Assert.Equal("Failed", bad.Status);
        Assert.True(File.Exists(Music("1_N")));
    }

    [Fact]
    public void Build_StockSlot_WritesFasterFinalLapFile()
    {
        Run(Row("n_Farm_n", stock: true));
        Assert.Equal(33920, AudioIO.ReadBrstmSummary(Music("n_Farm_f")).Rate);
        Assert.Equal(32000, AudioIO.ReadBrstmSummary(Music("n_Farm_n")).Rate);
    }

    [Fact]
    public void Project_SaveLoad_KeepsNewFields()
    {
        var row = Row("1_N");
        row.VolumeDb = -3;
        row.Crossfade = 0.5;
        row.Loop = LoopMode.Custom;
        row.LoopStart = "0:01.000000";
        string path = Path.Combine(_folder, "p.json");
        Project.From(new Options { PackName = "X" }, [row]).Save(path);

        var fresh = new List<TrackRow> { new() { FileBase = "1_N" } };
        Project.Load(path).ApplyTo(fresh);
        Assert.Equal(-3, fresh[0].VolumeDb);
        Assert.Equal(0.5, fresh[0].Crossfade);
        Assert.Equal(LoopMode.Custom, fresh[0].Loop);
        Assert.Equal("0:01.000000", fresh[0].LoopStart);
    }

    [Fact]
    public void Project_OldFileWithoutNewFields_StillLoads()
    {
        string path = Path.Combine(_folder, "old.json");
        File.WriteAllText(path, """{"Options":{"PackName":"Old"},"Rows":[{"File":"1_N","Source":"a.mp3","Loop":"WholeSong"}]}""");
        var fresh = new List<TrackRow> { new() { FileBase = "1_N" } };
        Project.Load(path).ApplyTo(fresh);
        Assert.Equal(LoopMode.WholeSong, fresh[0].Loop);
        Assert.Equal(0, fresh[0].VolumeDb);
    }

    [Fact]
    public void InstallAsMod_WritesIniAndMusic_KeepsOtherFiles()
    {
        var row = Row("1_N");
        var result = Run(row);
        string mods = Path.Combine(_folder, "mods");
        string keep = Path.Combine(_folder, "la_bike-fk.szs");
        File.WriteAllText(keep, "kart");
        string folder = PackBuilder.InstallAsMod(result.MusicFolder, "T", "me", mods, [keep]);
        Assert.True(File.Exists(Path.Combine(folder, "1_N.brstm")));
        Assert.Contains("IsEnabled = True", File.ReadAllText(Path.Combine(folder, "T.ini")));
        Assert.True(File.Exists(Path.Combine(mods, "Kept Patches files", "la_bike-fk.szs")));
    }
}

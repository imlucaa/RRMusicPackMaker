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
    public void MenusAndFanfares_SayWhereTheyPlay()
    {
        var rows = Catalog.Build(null, out _).Where(row => row.Category is Catalog.CatMenu or Catalog.CatFanfare).ToList();
        Assert.Equal(32, rows.Count);
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Note), row.Name));
        Assert.Contains("time trial", rows.Single(row => row.FileBase == "o_FanfareMIwin_32").Note);
    }

    [Fact]
    public void Build_WithoutInstall_ListsEveryRetroRewindTrack()
    {
        var rows = Catalog.Build(null, out string message);

        Assert.Contains("built-in", message);
        Assert.True(rows.Count(row => row.Category == Catalog.CatRegs) == 32);
        Assert.True(rows.Count(row => row.Category == Catalog.CatRetro) > 150);
        Assert.True(rows.Count(row => row.Category == Catalog.CatCustom) > 100);
        Assert.True(rows.Count(row => row.Category == Catalog.CatBattle) > 20);
        Assert.Contains(rows, row => row.Name == "SW2 Ghost Valley 2" && row.Platform == "SW2" && row.Note.StartsWith("Version of SNES Ghost Valley 2"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\\no\\such\\folder")]
    public void Build_NoUsableFolder_AllUseTheBuiltInList(string? folder)
    {
        Catalog.Build(folder, out string message);
        Assert.Contains("built-in", message);
    }

    [Fact]
    public void Build_AFolderWithATrackList_IsReadInsteadOfTheBuiltInOne()
    {
        string folder = Path.Combine(Path.GetTempPath(), "rrm_list_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "Binaries"));
        try
        {
            // A damaged list is reported, which shows the folder's list was the one being read.
            File.WriteAllBytes(Path.Combine(folder, "Binaries", "ConfigRT.pul"), new byte[64]);
            Assert.Throws<InvalidDataException>(() => Catalog.Build(folder, out _));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void BuiltInTracks_ReadsItsOwnFormat_AndSkipsBadLines()
    {
        var list = BuiltInTracks.Parse("version\t6.13.4\r\ntrack\tConfigRT.pul\tWii X\tstem\t0\t8\nmusic\t1_N\nnonsense\n");
        Assert.Equal("6.13.4", list.Version);
        Assert.Equal(new PulsarConfig.Track("Wii X", "stem", 0, 8), Assert.Single(list.Tracks["ConfigRT.pul"]));
        Assert.Equal(["1_N"], list.MusicFiles);
    }

    [Fact]
    public void Build_WithoutInstall_StillListsSheetFiles()
    {
        var rows = Catalog.Build(null, out _);
        foreach (string file in new[] { "title_bg", "offline_bg", "wifi_globe_bg", "wifi_lobby_bg", "n_Circuit32_n", "n_BossMI", "o_FanfareGP1_32", "STRM_N_KOOPA_N" })
            Assert.Contains(rows, r => r.FileBase == file);
        Assert.Equal(rows.Count, rows.Select(r => r.FileBase).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Build_BattleArenas_AlsoWriteTheirTrackNumber()
    {
        // Retro Rewind 6.13.4: 176 retro tracks (44 cups) and 140 custom tracks (35 cups) come first, so the battle
        // arenas start at 316 and Chain Chomp Wheel is 344. Pulsar can name a track's music by that number.
        var rows = Catalog.Build(null, out _);
        var chomp = rows.Single(r => r.Category == Catalog.CatBattle && r.Name == "Wii Chain Chomp Wheel");
        Assert.Equal("casino_battle_N", chomp.FileBase);
        Assert.Equal(["344_N"], chomp.AliasBases);
        Assert.Equal(["316_N"], rows.Single(r => r.Category == Catalog.CatBattle && r.Name == "SNES Battle Course 4").AliasBases);
        // Every arena that borrows music gets its number; the ones with their own music keep one name. Same list as the site.
        Assert.Equal(
            ["316_N", "322_N", "326_N", "328_N", "335_N", "341_N", "342_N", "343_N", "344_N", "345_N"],
            rows.Where(r => r.Category == Catalog.CatBattle).SelectMany(r => r.AliasBases).Order(StringComparer.Ordinal).ToArray());
        // Race tracks keep one name.
        Assert.All(rows.Where(r => r.Category is Catalog.CatRetro or Catalog.CatCustom or Catalog.CatRegs), r => Assert.Empty(r.AliasBases));
    }
}

public class SortingTests
{
    private static TrackRow Row(string name, string category) =>
        new() { Name = name, FileBase = name, Category = category, Platform = Catalog.PlatformOf(Catalog.MainName(name)) };

    [Theory]
    [InlineData("Wii Luigi Circuit", true)]
    [InlineData("Wii Toad's Factory > SW2 Toad's Factory", false)]
    [InlineData("SNES Ghost Valley 2 > SW2 Ghost Valley 2", false)]
    [InlineData("SNES Mario Circuit 3 > SNES Mario Circuit 3 DX", true)]
    [InlineData("N64 DK's Jungle Parkway", true)]
    [InlineData("N64 DK Jungle Parkway", true)]
    [InlineData("DS Peach Garden", true)]
    [InlineData("SNES Mario Circuit 3 (variant 1)", true)]
    [InlineData("SNES Mario Circuit 1", false)]
    [InlineData("Wii U Rainbow Road", false)]
    [InlineData("GBA Luigi Circuit", false)]
    [InlineData("Phendrana Frostbite", false)]
    public void IsReg_OnlyTheOriginal32(string name, bool expected) =>
        Assert.Equal(expected, Catalog.IsReg(name));

    [Theory]
    [InlineData("Wii U Water Park", "Wii U")]
    [InlineData("Wii Coconut Mall", "Wii")]
    [InlineData("SW2 Crown City", "SW2")]
    [InlineData("SW Battle Stadium", "SW")]
    [InlineData("3DS Daisy Hills", "3DS")]
    [InlineData("Phendrana Frostbite", "")]
    [InlineData("DSi Fake Track", "")]
    public void PlatformOf_ReadsTheConsole(string name, string expected) =>
        Assert.Equal(expected, Catalog.PlatformOf(name));

    [Theory]
    [InlineData("SNES Ghost Valley 2 > SW2 Ghost Valley 2", "SW2 Ghost Valley 2")]
    [InlineData("SNES Donut Plains 1 > Beta Donut Plains 1", "Beta Donut Plains 1")]
    [InlineData("GBA Cheese Land > Wii U Cheese Land", "Wii U Cheese Land")]
    [InlineData("SNES Choco Island 1 > SNES Choco Island 1 DX", "SNES Choco Island 1 DX")]
    [InlineData("Phendrana > Night", null)]
    [InlineData("SNES Mario Circuit 3", null)]
    public void VersionName_IsTheVersionAfterTheArrow(string name, string? expected) =>
        Assert.Equal(expected, Catalog.VersionName(name));

    [Fact]
    public void Organize_GroupsInSidebarOrder()
    {
        var rows = Catalog.Organize([
            Row("title", Catalog.CatMenu),
            Row("Phendrana", Catalog.CatCustom),
            Row("GBA Luigi Circuit", Catalog.CatRetro),
            Row("Wii Coconut Mall", Catalog.CatRegs),
        ]);
        Assert.Equal([Catalog.CatRegs, Catalog.CatRetro, Catalog.CatCustom, Catalog.CatMenu], rows.Select(r => r.Category));
    }

    [Fact]
    public void Organize_InsideAGroup_ByConsoleThenName_VariantsFollowTheirTrack()
    {
        var rows = Catalog.Organize([
            Row("3DS Daisy Hills", Catalog.CatRetro),
            Row("SNES Mario Circuit 2", Catalog.CatRetro),
            Row("SNES Bowser Castle", Catalog.CatRetro),
            Row("SNES Bowser Castle > Tour Bowser Castle", Catalog.CatRetro),
            Row("GBA Peach Circuit", Catalog.CatRetro),
        ]);
        Assert.Equal(
            ["SNES Bowser Castle", "SNES Bowser Castle > Tour Bowser Castle", "SNES Mario Circuit 2", "GBA Peach Circuit", "3DS Daisy Hills"],
            rows.Select(r => r.Name));
    }

    [Fact]
    public void Organize_KeepsGameMusicInItsOwnOrder()
    {
        var rows = Catalog.Organize([Row("Zed menu", Catalog.CatMenu), Row("Alpha menu", Catalog.CatMenu)]);
        Assert.Equal("Zed menu", rows[0].Name);
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

    private static List<TrackRow> Tracks(int count) =>
        Enumerable.Range(0, count).Select(i => new TrackRow { Name = $"T{i}", FileBase = $"{i}_N" }).ToList();

    [Fact]
    public void FillFromFiles_InOrder_GoesByFileName_WithNumbersCountedAsNumbers()
    {
        var rows = Tracks(5);
        string[] files = ["C:/m/10 ten.mp3", "C:/m/2 two.mp3", "C:/m/1 one.mp3"];
        int filled = PackBuilder.FillFromFiles(files, rows, FillOrder.InOrder, false, new Random(1));
        Assert.Equal(5, filled);
        Assert.Equal(["1 one", "2 two", "10 ten", "1 one", "2 two"], rows.Select(r => r.Song));
        Assert.All(rows, r => Assert.True(r.IsAssigned));
    }

    [Fact]
    public void FillFromFiles_OnlyEmpty_LeavesTracksThatHaveASongAlone()
    {
        var rows = Tracks(4);
        rows[1].Source = "C:/m/mine.mp3";
        rows[1].Song = "mine";
        string[] files = ["C:/m/a.mp3", "C:/m/b.mp3", "C:/m/c.mp3"];
        int filled = PackBuilder.FillFromFiles(files, rows, FillOrder.InOrder, true, new Random(1));
        Assert.Equal(3, filled);
        Assert.Equal("C:/m/mine.mp3", rows[1].Source);
        Assert.Equal(["a", "mine", "b", "c"], rows.Select(r => r.Song));
    }

    [Fact]
    public void FillFromFiles_Shuffled_UsesEverySongBeforeRepeating()
    {
        var rows = Tracks(6);
        string[] files = ["C:/m/a.mp3", "C:/m/b.mp3", "C:/m/c.mp3"];
        PackBuilder.FillFromFiles(files, rows, FillOrder.Shuffled, false, new Random(3));
        Assert.Equal(3, rows.Take(3).Select(r => r.Source).Distinct().Count());
        Assert.Equal(3, rows.Skip(3).Select(r => r.Source).Distinct().Count());
    }

    [Fact]
    public void FillFromFiles_NoSongs_ChangesNothing()
    {
        var rows = Tracks(2);
        Assert.Equal(0, PackBuilder.FillFromFiles([], rows, FillOrder.InOrder, false, new Random(1)));
        Assert.All(rows, r => Assert.False(r.IsAssigned));
    }

    [Theory]
    [InlineData("2", "10", -1)]
    [InlineData("track 9", "track 10", -1)]
    [InlineData("A", "a", 0)]
    [InlineData("01", "1", 0)]
    [InlineData("b", "a", 1)]
    public void NaturalNames_SortsNumbersAsNumbers(string a, string b, int expected) =>
        Assert.Equal(expected, Math.Sign(NaturalNames.Instance.Compare(a, b)));
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

    private static double Rms(Song song) =>
        20 * Math.Log10(Math.Sqrt(song.Channels[0].Average(s => (double)s * s)) / 32768);

    private static int Peak(Song song) => song.Channels.Max(c => c.Max(s => Math.Abs((int)s)));

    /// <summary>A loud master like a modern song: steady and close to full scale.</summary>
    private static short[] Loud(int count)
    {
        var random = new Random(3);
        var data = new short[count];
        for (int i = 0; i < count; i++)
            data[i] = (short)Math.Clamp(random.Next(-26000, 26000), short.MinValue, short.MaxValue);
        return data;
    }

    [Fact]
    public void ApplyLevel_EvenLoudness_ReachesRetroRewindLevel()
    {
        var song = Stereo(Tone(64000, 440, level: 4000), 0, 64000);
        Processing.ApplyLevel(song, Options.LoudnessEven, 0);
        Assert.InRange(Rms(song), -11.6, -10.4);
    }

    [Fact]
    public void ApplyLevel_LoudMaster_IsNotTurnedDownToSixteen()
    {
        // This is the bug behind quiet music: loud songs were cut to -16 dB.
        var song = Stereo(Loud(64000), 0, 64000);
        Processing.ApplyLevel(song, Options.LoudnessEven, 0);
        Assert.True(Rms(song) > -12.5, $"level was {Rms(song):0.0} dB");
    }

    [Theory]
    [InlineData(-6)]
    [InlineData(-3)]
    [InlineData(3)]
    [InlineData(6)]
    public void ApplyLevel_SongVolume_ChangesTheLevelBySoMuch(int decibels)
    {
        var normal = Stereo(Tone(64000, 440, level: 2000), 0, 64000);
        var changed = Stereo(Tone(64000, 440, level: 2000), 0, 64000);
        Processing.ApplyLevel(normal, Options.LoudnessOff, 0);
        Processing.ApplyLevel(changed, Options.LoudnessOff, decibels);
        Assert.InRange(Rms(changed) - Rms(normal), decibels - 0.3, decibels + 0.3);
    }

    [Fact]
    public void ApplyLevel_BigBoost_LimitsPeaksInsteadOfClipping()
    {
        var song = Stereo(Loud(64000), 0, 64000);
        Processing.ApplyLevel(song, Options.LoudnessEven, 9);
        Assert.True(Peak(song) <= 32768 * 0.975, $"peak was {Peak(song)}");
        Assert.DoesNotContain(song.Channels[0], s => s == short.MaxValue || s == short.MinValue);
    }

    [Fact]
    public void ApplyLevel_Boost_IsLouderThanNoBoost()
    {
        var normal = Stereo(Loud(64000), 0, 64000);
        var boosted = Stereo(Loud(64000), 0, 64000);
        Processing.ApplyLevel(normal, Options.LoudnessEven, 0);
        Processing.ApplyLevel(boosted, Options.LoudnessEven, 6);
        Assert.True(Rms(boosted) > Rms(normal) + 1.5);
    }

    [Fact]
    public void ApplyLevel_NoChangeNeeded_LeavesAudioAlone()
    {
        var song = Stereo(Tone(3200, 440), 0, 3200);
        var before = (short[])song.Channels[0].Clone();
        Processing.ApplyLevel(song, Options.LoudnessOff, 0);
        Assert.Equal(before, song.Channels[0]);
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

    private string Music(string name) => Path.Combine(_folder, "T", "RetroRewind6", "Patches", name + ".brstm");

    private string Original(string name) => Path.Combine(_folder, "T", "My Stuff", "sound", "strm", name + ".brstm");

    [Fact]
    public void OriginalNames_All32RegsGetTheirOwnStockFile_AndMatchTheirName()
    {
        var regs = Catalog.Build(null, out _).Where(row => row.Category == Catalog.CatRegs).ToList();
        Assert.Equal(32, regs.Count);
        Assert.All(regs, row => Assert.NotNull(row.OriginalBase));
        // Luigi Circuit and Mario Circuit share n_Circuit32_n, so 32 tracks use 31 files.
        Assert.Equal(31, regs.Select(row => row.OriginalBase!.ToLowerInvariant()).Distinct().Count());
        string FileOf(string name) => regs.Single(row => row.Name == name).OriginalBase!;
        Assert.Equal("n_Farm_n", FileOf("Wii Moo Moo Meadows"));
        Assert.Equal("n_Shopping32_n", FileOf("Wii Coconut Mall"));
        Assert.Equal("r_GC_Beach32_n", FileOf("GCN Peach Beach"));
        Assert.Equal("r_64_Kuppa32_n", FileOf("N64 Bowser's Castle"));
        Assert.Equal("n_Circuit32_n", FileOf("Wii Luigi Circuit"));
        Assert.Equal("n_Circuit32_n", FileOf("Wii Mario Circuit"));
        Assert.Equal("n_Farm_f", regs.Single(row => row.Name == "Wii Moo Moo Meadows").OriginalFinalBase);
    }

    private TrackRow OriginalRow(string file, string original) => new()
    {
        Name = file,
        FileBase = file,
        Source = _wav,
        PulsarFinalBase = file + "x",
        OriginalBase = original,
        OriginalFinalBase = Catalog.FinalNameOf(original),
    };

    [Fact]
    public void Build_OriginalNamesOn_WritesTheStockFileAndItsFinalLap()
    {
        var row = OriginalRow("89_N", "n_Farm_n");
        PackBuilder.Build(new Options { PackName = "T", OriginalNames = true }, [row], _folder, _ => { });
        Assert.True(File.Exists(Music("89_N")));
        Assert.True(File.Exists(Original("n_Farm_n")));
        Assert.True(File.Exists(Original("n_Farm_f")));
        Assert.False(File.Exists(Music("n_Farm_n")));
        Assert.False(File.Exists(Music("n_Farm_f")));
    }

    [Fact]
    public void Build_OriginalNamesOff_WritesOnlyRetroRewindNames()
    {
        var row = OriginalRow("89_N", "n_Farm_n");
        PackBuilder.Build(new Options { PackName = "T" }, [row], _folder, _ => { });
        Assert.True(File.Exists(Music("89_N")));
        Assert.False(File.Exists(Original("n_Farm_n")));
        Assert.False(File.Exists(Original("n_Farm_f")));
        Assert.False(Directory.Exists(Path.Combine(_folder, "T", "My Stuff")));
    }

    [Fact]
    public void Build_OriginalNames_TurnedOnLater_AddsTheFilesWithoutLosingTheOthers()
    {
        var row = OriginalRow("89_N", "n_Farm_n");
        PackBuilder.Build(new Options { PackName = "T" }, [row], _folder, _ => { });
        PackBuilder.Build(new Options { PackName = "T", OriginalNames = true }, [row], _folder, _ => { });
        Assert.True(File.Exists(Music("89_N")));
        Assert.True(File.Exists(Original("n_Farm_n")));
        PackBuilder.Build(new Options { PackName = "T" }, [row], _folder, _ => { });
        Assert.False(File.Exists(Original("n_Farm_n")));
        Assert.False(Directory.Exists(Path.Combine(_folder, "T", "My Stuff")));
    }

    [Fact]
    public void Build_OriginalNames_SharedFile_GoesToTheFirstTrackWithASong_WithoutFailing()
    {
        var luigi = OriginalRow("88_N", "n_Circuit32_n");
        var mario = OriginalRow("92_N", "n_Circuit32_n");
        var result = PackBuilder.Build(new Options { PackName = "T", OriginalNames = true }, [luigi, mario], _folder, _ => { });
        Assert.Equal(0, result.Failed);
        Assert.True(luigi.OriginalActive);
        Assert.False(mario.OriginalActive);
        Assert.True(File.Exists(Original("n_Circuit32_n")));
        // With only Mario Circuit given a song, it gets the shared file.
        PackBuilder.Build(new Options { PackName = "T", OriginalNames = true }, [mario], _folder, _ => { });
        Assert.True(mario.OriginalActive);
    }

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

    [Theory]
    [InlineData(Catalog.CatRetro, 0, 4)]
    [InlineData(Catalog.CatRegs, 0, 4)]
    [InlineData(Catalog.CatCustom, 0, 4)]
    [InlineData(Catalog.CatBattle, 0, 4)]
    [InlineData(Catalog.CatStock, 0, 4)]
    [InlineData(Catalog.CatMenu, 0, 0)]
    [InlineData(Catalog.CatFanfare, 0, 0)]
    [InlineData(Catalog.CatRetro, 3, 7)]
    [InlineData(Catalog.CatRetro, -6, -2)]
    [InlineData(Catalog.CatRetro, 6, 9)]
    [InlineData(Catalog.CatRetro, 9, 9)]
    public void EffectiveVolume_RaceTracksGetABoost_WithACap(string category, int setting, double expected)
    {
        var row = new TrackRow { Category = category, VolumeDb = setting };
        Assert.Equal(expected, PackBuilder.EffectiveVolumeDb(new Options(), row));
    }

    [Theory]
    [InlineData(Options.LoudnessOff)]
    [InlineData(Options.LoudnessPeak)]
    public void EffectiveVolume_OtherModes_GetNoBoost(string loudness)
    {
        var row = new TrackRow { Category = Catalog.CatRetro, VolumeDb = 3 };
        Assert.Equal(3, PackBuilder.EffectiveVolumeDb(new Options { Loudness = loudness }, row));
    }

    [Fact]
    public void Build_RaceTrack_IsLouderThanAMenu_FromTheSameSong()
    {
        var race = new TrackRow { Name = "race", FileBase = "1_N", Source = _wav, Category = Catalog.CatRetro };
        var menu = new TrackRow { Name = "menu", FileBase = "title_bg", Source = _wav, Category = Catalog.CatMenu };
        Run(race, menu);
        Assert.NotEqual(File.ReadAllBytes(Music("1_N")), File.ReadAllBytes(Music("title_bg")));
    }

    [Fact]
    public void Project_SaveLoad_KeepsNewFields()
    {
        var row = Row("1_N");
        row.VolumeDb = -3;
        row.Crossfade = 0.5;
        row.Speed = 1.25;
        row.KeepPitch = true;
        row.Reverb = true;
        row.Loop = LoopMode.Custom;
        row.LoopStart = "0:01.000000";
        string path = Path.Combine(_folder, "p.json");
        Project.From(new Options { PackName = "X" }, [row]).Save(path);

        var fresh = new List<TrackRow> { new() { FileBase = "1_N" } };
        Project.Load(path).ApplyTo(fresh);
        Assert.Equal(-3, fresh[0].VolumeDb);
        Assert.Equal(0.5, fresh[0].Crossfade);
        Assert.Equal(1.25, fresh[0].Speed);
        Assert.True(fresh[0].KeepPitch);
        Assert.True(fresh[0].Reverb);
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
        Assert.Equal(1, fresh[0].Speed);
        Assert.False(fresh[0].KeepPitch);
    }

    [Fact]
    public void Build_EachSpeedIsItsOwnFile_AtTheRightLength()
    {
        var normal = Row("1_N");
        var nightcore = Row("2_N");
        nightcore.Speed = 1.25;
        var faster = Row("3_N");
        faster.Speed = 1.25;
        faster.KeepPitch = true;

        var result = Run(normal, nightcore, faster);

        Assert.Equal(3, result.Encoded);
        Assert.InRange(AudioIO.ReadBrstmSummary(Music("1_N")).Samples, 94000, 97000);
        Assert.InRange(AudioIO.ReadBrstmSummary(Music("2_N")).Samples, 75000, 78000);

        // The same pitch gets a short blend at the jump, and the encoder then pads the file to line up
        // its loop start, so what shows the speed here is the length of the loop.
        var faster3 = AudioIO.ReadBrstmSummary(Music("3_N"));
        Assert.InRange(faster3.End - faster3.Start, 74000, 78000);
        Assert.Contains(_log, line => line.Contains("Nightcore 1.25x"));
    }

    private string MadeBrstm()
    {
        var made = Run(Row("source_N"));
        return Path.Combine(made.MusicFolder, "source_N.brstm");
    }

    private TrackRow Imported(string file, string source)
    {
        string copy = Path.Combine(_folder, "imported_" + file + ".brstm");
        File.Copy(source, copy, true);
        var row = Row(file);
        row.Source = copy;
        row.KeepFile = true;
        return row;
    }

    [Fact]
    public void Build_ImportedSong_GoesInUnchanged()
    {
        var row = Imported("1_N", MadeBrstm());
        byte[] original = File.ReadAllBytes(row.Source);
        Assert.Equal(0, Run(row).Failed);
        Assert.Equal(original, File.ReadAllBytes(Music("1_N")));
        Assert.Contains(_log, line => line.Contains("kept as it was"));
    }

    [Theory]
    [InlineData("speed")]
    [InlineData("reverb")]
    [InlineData("volume")]
    [InlineData("loop")]
    public void Build_ImportedSong_ChangingASetting_EncodesItAgain(string setting)
    {
        var row = Imported("1_N", MadeBrstm());
        byte[] original = File.ReadAllBytes(row.Source);
        switch (setting)
        {
            case "speed": row.Speed = 0.8; break;
            case "reverb": row.Reverb = true; break;
            case "volume": row.VolumeDb = -3; break;
            default: row.Loop = LoopMode.WholeSong; break;
        }
        Assert.False(PackBuilder.UsesFileAsIs(row));
        Assert.Equal(0, Run(row).Failed);
        Assert.NotEqual(original, File.ReadAllBytes(Music("1_N")));
    }

    private string BrstmAt(int rate)
    {
        string path = Path.Combine(_folder, $"at{rate}.brstm");
        var tone = Enumerable.Range(0, rate).Select(i => (short)(8000 * Math.Sin(i * 0.05))).ToArray();
        AudioIO.WriteBrstm(new Song { Channels = [tone, tone], Rate = rate }, path, 2, rate);
        return path;
    }

    [Fact]
    public void Build_ImportedSongAbove44100Hz_IsEncodedAgain_SoTheWiiCanPlayIt()
    {
        string source = BrstmAt(48000);
        Assert.Equal(48000, AudioIO.ReadBrstmSummary(source).Rate);
        var row = Imported("1_N", source);
        Assert.Equal(0, Run(row).Failed);
        int rate = AudioIO.ReadBrstmSummary(Music("1_N")).Rate;
        Assert.InRange(rate, 1, PackBuilder.MaxWiiSampleRate);
    }

    [Fact]
    public void Build_ImportedSongAbove44100Hz_ForDolphin_IsKeptAsItIs()
    {
        string source = BrstmAt(48000);
        var row = Imported("1_N", source);
        PackBuilder.Build(new Options { PackName = "T", TargetWii = false }, [row], _folder, m => { lock (_log) _log.Add(m); });
        Assert.Equal(File.ReadAllBytes(row.Source), File.ReadAllBytes(Music("1_N")));
        Assert.Equal(48000, AudioIO.ReadBrstmSummary(Music("1_N")).Rate);
    }

    [Fact]
    public void Build_ImportedSongAt44100Hz_IsKeptByteForByte()
    {
        string source = BrstmAt(44100);
        var row = Imported("1_N", source);
        Assert.Equal(0, Run(row).Failed);
        Assert.Equal(File.ReadAllBytes(row.Source), File.ReadAllBytes(Music("1_N")));
    }

    [Fact]
    public void FinalRate_NeverGoesAboveWhatTheWiiPlays() =>
        Assert.Equal(PackBuilder.MaxWiiSampleRate, PackBuilder.FinalRate(44100, 1.25));

    [Fact]
    public void Build_VeryLongSong_SaysItUsesALotOfMemory()
    {
        string path = Path.Combine(_folder, "long.wav");
        WriteWav(path, 11 * 60);
        var row = Row("1_N");
        row.Source = path;
        Run(row);
        Assert.Contains(_log, line => line.Contains("minutes long"));
    }

    [Fact]
    public void Build_ImportedStockSong_KeepsItsOwnFinalLapFile()
    {
        var row = Imported("n_Farm_n", MadeBrstm());
        byte[] original = File.ReadAllBytes(row.Source);
        var stock = new TrackRow { Name = "Farm", FileBase = "n_Farm_n", StockFinalBase = "n_Farm_f", Source = row.Source, FinalSource = row.Source, KeepFile = true };
        Run(stock);
        Assert.Equal(original, File.ReadAllBytes(Music("n_Farm_n")));
        Assert.Equal(original, File.ReadAllBytes(Music("n_Farm_f")));
    }

    [Fact]
    public void Build_ImportedStockSong_WithoutAFinalLapFile_StillGetsOne()
    {
        var row = Imported("n_Farm_n", MadeBrstm());
        var stock = new TrackRow { Name = "Farm", FileBase = "n_Farm_n", StockFinalBase = "n_Farm_f", Source = row.Source, KeepFile = true };
        Assert.Equal(0, Run(stock).Failed);
        Assert.True(File.Exists(Music("n_Farm_f")));
    }

    [Fact]
    public void Project_RemembersThatASongIsKeptAsIs()
    {
        var row = Row("1_N");
        row.KeepFile = true;
        string path = Path.Combine(_folder, "keep.json");
        Project.From(new Options(), [row]).Save(path);
        var fresh = new List<TrackRow> { new() { FileBase = "1_N" } };
        Project.Load(path).ApplyTo(fresh);
        Assert.True(fresh[0].KeepFile);
    }

    [Fact]
    public void Build_TurningOnReverb_EncodesAgain()
    {
        var row = Row("1_N");
        Run(row);
        row.Reverb = true;
        Assert.Equal(0, Run(row).Unchanged);
        Assert.Contains(_log, line => line.Contains("Normal + reverb"));
    }

    [Fact]
    public void RaceBoost_CanBeTurnedOff()
    {
        var row = new TrackRow { Category = Catalog.CatRetro, VolumeDb = 3 };
        Assert.True(PackBuilder.EffectiveVolumeDb(new Options(), row) > 3);
        Assert.Equal(3, PackBuilder.EffectiveVolumeDb(new Options { RaceBoost = false }, row));
    }

    [Fact]
    public void Build_ChangingTheSpeed_EncodesAgain()
    {
        var row = Row("1_N");
        Run(row);
        Assert.Equal(1, Run(row).Unchanged);
        row.Speed = 1.5;
        var changed = Run(row);
        Assert.Equal(0, changed.Unchanged);
        Assert.InRange(AudioIO.ReadBrstmSummary(Music("1_N")).Samples, 62000, 66000);
    }

    [Fact]
    public void Build_FinalLapSong_GetsTheSameSpeed()
    {
        var row = Row("1_N");
        row.FinalSource = _wav;
        row.Speed = 1.5;
        Run(row);
        Assert.InRange(AudioIO.ReadBrstmSummary(Music("1_Nx")).Samples, 62000, 66000);
    }

    [Fact]
    public void Build_NoFinalLapFileAnywhere_LeavesTheSpeedUpToTheGame()
    {
        // Retro Rewind speeds up the normal file itself when it finds no final lap file.
        Run(Row("1_N"));
        Assert.False(File.Exists(Music("1_Nx")));
    }

    [Fact]
    public void Build_RetroRewindHasItsOwnFinalLapFile_ReplacesItWithThisSongSpedUp()
    {
        // Otherwise Retro Rewind's own final lap music plays instead of the pack's song.
        var row = Row("1_N");
        row.ExistingFinalFiles.Add("1_Nx");
        Run(row);
        Assert.Equal(33920, AudioIO.ReadBrstmSummary(Music("1_Nx")).Rate);
        Assert.Equal(32000, AudioIO.ReadBrstmSummary(Music("1_N")).Rate);
    }

    [Fact]
    public void Build_FinalLapSong_IsWrittenFaster_LikeTheGame()
    {
        var row = Row("1_N");
        row.FinalSource = _wav;
        Run(row);
        Assert.Equal(33920, AudioIO.ReadBrstmSummary(Music("1_Nx")).Rate);
    }

    [Theory]
    [InlineData(1.0, 32000)]
    [InlineData(1.2, 38400)]
    [InlineData(1.25, 40000)]
    public void Build_FinalLapSpeed_SetsHowFastTheFinalLapPlays(double speed, int rate)
    {
        var row = Row("1_N");
        row.FinalSpeed = speed;
        Run(row);
        Assert.Equal(rate, AudioIO.ReadBrstmSummary(Music("1_Nx")).Rate);
        Assert.Equal(32000, AudioIO.ReadBrstmSummary(Music("1_N")).Rate);
    }

    [Fact]
    public void Build_StockSlot_UsesTheFinalLapSpeed()
    {
        var row = Row("n_Farm_n", stock: true);
        row.FinalSpeed = 1.2;
        Run(row);
        Assert.Equal(38400, AudioIO.ReadBrstmSummary(Music("n_Farm_f")).Rate);
    }

    [Fact]
    public void Build_ChangingTheFinalLapSpeed_EncodesAgain()
    {
        var row = Row("1_N");
        Run(row);
        Assert.Equal(1, Run(row).Unchanged);
        row.FinalSpeed = 1.15;
        Assert.Equal(0, Run(row).Unchanged);
        Assert.Equal(36800, AudioIO.ReadBrstmSummary(Music("1_Nx")).Rate);
    }

    [Fact]
    public void Build_ImportedSong_WhereRetroRewindHasAFinalLapFile_GetsOneMadeFromIt()
    {
        var row = Imported("1_N", MadeBrstm());
        var track = new TrackRow { Name = "One", FileBase = "1_N", PulsarFinalBase = "1_Nx", Source = row.Source, KeepFile = true };
        track.ExistingFinalFiles.Add("1_Nx");
        Assert.Equal(0, Run(track).Failed);
        Assert.Equal(AudioIO.ReadBrstmSummary(row.Source).Rate * 1.06, AudioIO.ReadBrstmSummary(Music("1_Nx")).Rate, 0);
    }

    [Fact]
    public void Project_RemembersTheFinalLapSpeed()
    {
        var row = Row("1_N");
        row.FinalSpeed = 1.2;
        string path = Path.Combine(_folder, "final.json");
        Project.From(new Options(), [row]).Save(path);
        var fresh = new List<TrackRow> { new() { FileBase = "1_N" } };
        Project.Load(path).ApplyTo(fresh);
        Assert.Equal(1.2, fresh[0].FinalSpeed);
    }

    [Fact]
    public void Project_FromBeforeFinalLapSpeed_UsesTheGamesSpeed()
    {
        string path = Path.Combine(_folder, "old.json");
        File.WriteAllText(path, """{"Options":{},"Rows":[{"File":"1_N","Source":"a.wav"}]}""");
        var fresh = new List<TrackRow> { new() { FileBase = "1_N" } };
        Project.Load(path).ApplyTo(fresh);
        Assert.Equal(PackBuilder.GameFinalSpeed, fresh[0].FinalSpeed);
    }

    private string Backups => Path.Combine(_folder, "backups");

    [Fact]
    public void CopyToStrm_PutsSongsInTheStrmFolder_LeavesOtherFilesAlone()
    {
        var result = Run(Row("1_N"));
        string rr = Path.Combine(_folder, "rr");
        string other = Path.Combine(rr, "strm", "readme.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, "mine");
        Assert.Equal(1, PackBuilder.CopyToStrm(result.MusicFolder, rr, Backups));
        Assert.True(File.Exists(Path.Combine(rr, "strm", "1_N.brstm")));
        Assert.False(File.Exists(Path.Combine(rr, "Patches", "1_N.brstm")));
        Assert.Equal("mine", File.ReadAllText(other));
    }

    [Fact]
    public void CopyToStrm_SavesRetroRewindsOwnSongOnce_AndRemovingTheMakerRestoresIt()
    {
        var result = Run(Row("1_N"), Row("2_N"));
        string rr = Path.Combine(_folder, "rr");
        string strm = Path.Combine(rr, "strm");
        Directory.CreateDirectory(strm);
        File.WriteAllText(Path.Combine(strm, "1_N.brstm"), "retro rewind's own");

        PackBuilder.CopyToStrm(result.MusicFolder, rr, Backups);
        Assert.Equal("retro rewind's own", File.ReadAllText(Path.Combine(Backups, "1_N.brstm")));
        Assert.NotEqual("retro rewind's own", File.ReadAllText(Path.Combine(strm, "1_N.brstm")));

        // Adding again must not save the pack's song over the original.
        PackBuilder.CopyToStrm(result.MusicFolder, rr, Backups);
        Assert.Equal("retro rewind's own", File.ReadAllText(Path.Combine(Backups, "1_N.brstm")));

        int removed = PackBuilder.RemovePack("T", [], rr, result.MusicFolder, Backups);
        Assert.Equal(2, removed);
        Assert.Equal("retro rewind's own", File.ReadAllText(Path.Combine(strm, "1_N.brstm")));
        Assert.False(File.Exists(Path.Combine(strm, "2_N.brstm")));
        Assert.Empty(PackBuilder.PlacedInStrm(Backups));
    }

    [Fact]
    public void CopyToStrm_AlsoReplacesAnOldCopyInPatches_WhichWouldPlayInstead()
    {
        var result = Run(Row("1_N"));
        string rr = Path.Combine(_folder, "rr");
        Directory.CreateDirectory(Path.Combine(rr, "Patches"));
        File.WriteAllText(Path.Combine(rr, "Patches", "1_N.brstm"), "old");
        PackBuilder.CopyToStrm(result.MusicFolder, rr, Backups);
        Assert.Equal(File.ReadAllBytes(Path.Combine(rr, "strm", "1_N.brstm")), File.ReadAllBytes(Path.Combine(rr, "Patches", "1_N.brstm")));
    }

    [Fact]
    public void Build_FinalLapSong_HasItsOwnVolumeAndSpeed()
    {
        var row = Row("1_N");
        row.FinalSource = _wav;
        Run(row);
        long normal = AudioIO.ReadBrstmSummary(Music("1_Nx")).Samples;
        string before = Convert.ToHexString(File.ReadAllBytes(Music("1_Nx")));

        row.FinalVolumeDb = 6;
        row.FinalSongSpeed = 1.5;
        Assert.Equal(0, Run(row).Unchanged);
        Assert.InRange(AudioIO.ReadBrstmSummary(Music("1_Nx")).Samples, normal / 1.5 - 4000, normal / 1.5 + 4000);
        Assert.NotEqual(before, Convert.ToHexString(File.ReadAllBytes(Music("1_Nx"))));

        // The main song keeps its own speed.
        Assert.InRange(AudioIO.ReadBrstmSummary(Music("1_N")).Samples, normal - 4000, normal + 4000);
    }

    [Fact]
    public void Project_RemembersTheFinalLapSongsVolumeAndSpeed()
    {
        var row = Row("1_N");
        row.FinalVolumeDb = -4;
        row.FinalSongSpeed = 0.8;
        row.FinalSongReverb = true;
        string path = Path.Combine(_folder, "finalsong.json");
        Project.From(new Options(), [row]).Save(path);
        var fresh = new List<TrackRow> { new() { FileBase = "1_N" } };
        Project.Load(path).ApplyTo(fresh);
        Assert.Equal(-4, fresh[0].FinalVolumeDb);
        Assert.Equal(0.8, fresh[0].FinalSongSpeed);
        Assert.True(fresh[0].FinalSongReverb);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(0, -1, -1)]
    [InlineData(12, 1, 12)]
    [InlineData(-12, -1, -12)]
    [InlineData(5, -3, 2)]
    public void StepVolume_MovesByWholeDecibels_AndStopsAtTheEnds(int start, int step, int expected) =>
        Assert.Equal(expected, PackBuilder.StepVolume(start, step));

    [Fact]
    public void RenderFinalPreview_PlaysFasterAndHigher_ByTheFinalLapSpeed()
    {
        var row = Row("1_N");
        string normal = PackBuilder.RenderFinalPreview(new Options(), row);
        row.FinalSpeed = 1.25;
        string fast = PackBuilder.RenderFinalPreview(new Options(), row);

        // The same samples at a higher rate: the wav header carries the rate, so it plays shorter and higher.
        static int RateOf(string path) => BitConverter.ToInt32(File.ReadAllBytes(path), 24);
        Assert.Equal(33920, RateOf(normal));
        Assert.Equal(40000, RateOf(fast));
        Assert.InRange(new FileInfo(fast).Length, new FileInfo(normal).Length - 8, new FileInfo(normal).Length + 8);
    }

    [Fact]
    public void RenderFinalPreview_UsesTheFinalLapSong_WithItsOwnSpeed()
    {
        var row = Row("1_N");
        row.FinalSource = _wav;
        string same = PackBuilder.RenderFinalPreview(new Options(), row);
        row.FinalSongSpeed = 1.5;
        string quick = PackBuilder.RenderFinalPreview(new Options(), row);
        Assert.True(new FileInfo(quick).Length < new FileInfo(same).Length * 0.8);
    }

    [Fact]
    public void RemovePack_PutsBackEverythingPlacedInStrm_EvenWithoutTheBuiltPack()
    {
        var result = Run(Row("1_N"));
        string rr = Path.Combine(_folder, "rr");
        Directory.CreateDirectory(Path.Combine(rr, "strm"));
        File.WriteAllText(Path.Combine(rr, "strm", "1_N.brstm"), "original");
        PackBuilder.CopyToStrm(result.MusicFolder, rr, Backups);
        Directory.Delete(result.Folder, true);

        Assert.Equal(1, PackBuilder.RemovePack("T", [], rr, null, Backups));
        Assert.Equal("original", File.ReadAllText(Path.Combine(rr, "strm", "1_N.brstm")));
    }

    [Fact]
    public void ApplyFade_SilentAtTheEdges_UntouchedInTheMiddle()
    {
        var audio = new[] { Enumerable.Repeat((short)10000, 44100 * 4).ToArray(), Enumerable.Repeat((short)10000, 44100 * 4).ToArray() };
        EditTimeline.ApplyFade(audio, 44100, 1, 2);
        Assert.Equal(0, audio[0][0]);
        Assert.Equal(0, audio[1][^1]);
        Assert.Equal(10000, audio[0][44100 + 10]);
        Assert.InRange(audio[0][22050], 4900, 5100);
        Assert.InRange(audio[0][^44100], 4900, 5100);
    }

    [Fact]
    public void ApplyFade_LongerThanTheSong_StillWorks()
    {
        var audio = new[] { Enumerable.Repeat((short)10000, 1000).ToArray() };
        EditTimeline.ApplyFade(audio, 44100, 8, 8);
        Assert.Equal(0, audio[0][0]);
        Assert.Equal(0, audio[0][^1]);
    }

    [Fact]
    public void Build_PacksAreLaidOutForPatches_NotMyStuff()
    {
        var result = Run(Row("1_N"));
        Assert.EndsWith(Path.Combine("RetroRewind6", "Patches"), result.MusicFolder);
        Assert.True(File.Exists(Path.Combine(result.MusicFolder, "1_N.brstm")));
        Assert.False(Directory.Exists(Path.Combine(result.Folder, "RetroRewind6", "MyStuff")));
    }

    [Fact]
    public void Build_NeverWritesAZip()
    {
        Run(Row("1_N"));
        Assert.Empty(Directory.GetFiles(_folder, "*.zip", SearchOption.AllDirectories));
    }

    [Fact]
    public void ExportFolder_PutsTheSongsStraightInTheFolder_WithNoFoldersAroundThem()
    {
        var result = Run(Row("1_N"));
        string destination = Path.Combine(_folder, "usb");
        string target = PackBuilder.ExportFolder(result.Folder, destination);
        Assert.Equal(destination, target);
        Assert.True(File.Exists(Path.Combine(target, "1_N.brstm")));
        Assert.Empty(Directory.GetDirectories(target));
        Assert.False(File.Exists(Path.Combine(target, "README.txt")));
    }

    [Fact]
    public void ExportFolder_ReplacesSongsWithTheSameName_AndLeavesEverythingElse()
    {
        var result = Run(Row("1_N"));
        string destination = Path.Combine(_folder, "usb");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "1_N.brstm"), "old");
        File.WriteAllText(Path.Combine(destination, "other.brstm"), "mine");
        File.WriteAllText(Path.Combine(destination, "my notes.txt"), "mine");

        PackBuilder.ExportFolder(result.Folder, destination);

        Assert.NotEqual("old", File.ReadAllText(Path.Combine(destination, "1_N.brstm")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(destination, "other.brstm")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(destination, "my notes.txt")));
    }

    [Fact]
    public void ExportFolder_WithOriginalNames_PutsBothKindsOfNameInTheSameFolder()
    {
        var row = OriginalRow("89_N", "n_Farm_n");
        var result = PackBuilder.Build(new Options { PackName = "T", OriginalNames = true }, [row], _folder, _ => { });
        string target = PackBuilder.ExportFolder(result.Folder, Path.Combine(_folder, "usb"));
        Assert.True(File.Exists(Path.Combine(target, "89_N.brstm")));
        Assert.True(File.Exists(Path.Combine(target, "n_Farm_n.brstm")));
        Assert.True(File.Exists(Path.Combine(target, "n_Farm_f.brstm")));
        Assert.Empty(Directory.GetDirectories(target));
    }

    [Fact]
    public void ExportZip_HasEverythingAtTheTop_WithNoFolders()
    {
        var row = OriginalRow("89_N", "n_Farm_n");
        var result = PackBuilder.Build(new Options { PackName = "T", OriginalNames = true }, [row], _folder, _ => { });
        string zip = Path.Combine(_folder, "pack.zip");
        PackBuilder.ExportZip(result.Folder, zip);
        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(entry => entry.FullName).ToList();
        Assert.Contains("89_N.brstm", names);
        Assert.Contains("n_Farm_n.brstm", names);
        Assert.Contains("n_Farm_f.brstm", names);
        Assert.Contains("credits.csv", names);
        Assert.DoesNotContain(names, name => name.Contains('/') || name.Contains('\\'));
    }

    [Fact]
    public void ExportFolder_ToWhereThePackAlreadyIs_IsRefused()
    {
        var result = Run(Row("1_N"));
        Assert.Throws<IOException>(() => PackBuilder.ExportFolder(result.Folder, result.Folder));
    }

    [Fact]
    public void ExportFolder_InsideThePack_IsRefused()
    {
        var result = Run(Row("1_N"));
        Assert.Throws<IOException>(() => PackBuilder.ExportFolder(result.Folder, Path.Combine(result.Folder, "RetroRewind6")));
    }

    [Fact]
    public void ExportFolder_MissingSubfolders_AreCreated()
    {
        var result = Run(Row("1_N"));
        string target = PackBuilder.ExportFolder(result.Folder, Path.Combine(_folder, "a", "b", "c"));
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void Build_OldMyStuffPack_MovesToPatches_WithoutEncodingAgain()
    {
        var row = Row("1_N");
        var first = Run(row);
        string old = Path.Combine(_folder, "T", "RetroRewind6", "MyStuff");
        Directory.Move(first.MusicFolder, old);

        var second = Run(row);
        Assert.Equal(1, second.Unchanged);
        Assert.False(Directory.Exists(old));
        Assert.True(File.Exists(Music("1_N")));
    }

    /// <summary>The same steps as Wheel Wizard's PrepareModsForLaunch: returns which mod's copy of a file reaches Patches.</summary>
    private static string WinnerInWheelWizard(string mods, string fileName)
    {
        var ordered = Directory.GetDirectories(mods)
            .Select(folder =>
            {
                string title = Path.GetFileName(folder);
                string line = File.ReadAllLines(Path.Combine(folder, title + ".ini")).First(l => l.StartsWith("Priority"));
                return (Title: title, Priority: int.Parse(line[(line.IndexOf('=') + 1)..].Trim()));
            })
            .OrderBy(mod => mod.Priority)
            .Reverse();
        string winner = "";
        foreach (var mod in ordered)
            if (File.Exists(Path.Combine(mods, mod.Title, fileName)))
                winner = mod.Title;
        return winner;
    }

    private string OldMod(string mods, string title, int priority, string song)
    {
        string folder = Path.Combine(mods, title);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, title + ".ini"), $"[Mod]\r\nName = {title}\r\nAuthor = -1\r\nModID = -1\r\nIsEnabled = True\r\nPriority = {priority}\r\n");
        File.WriteAllText(Path.Combine(folder, song), "old music");
        return folder;
    }

    [Fact]
    public void InstallAsMod_OlderModHasSameSong_NewPackWins()
    {
        var result = Run(Row("title_bg"));
        string mods = Path.Combine(_folder, "mods");
        OldMod(mods, "e", 1, "la_bike-fk.szs");
        OldMod(mods, "music", 2, "title_bg.brstm");

        PackBuilder.InstallAsMod(result.MusicFolder, "T", "", mods);

        Assert.Equal("T", WinnerInWheelWizard(mods, "title_bg.brstm"));
        Assert.Equal(["music"], PackBuilder.ModsSharingSongs(result.MusicFolder, "T", mods));
    }

    [Fact]
    public void InstallAsMod_InstalledAgain_StillWinsAndKeepsOneMod()
    {
        var result = Run(Row("title_bg"));
        string mods = Path.Combine(_folder, "mods");
        OldMod(mods, "music", 2, "title_bg.brstm");
        PackBuilder.InstallAsMod(result.MusicFolder, "T", "", mods);
        PackBuilder.InstallAsMod(result.MusicFolder, "T", "", mods);
        Assert.Equal("T", WinnerInWheelWizard(mods, "title_bg.brstm"));
        Assert.Equal(2, Directory.GetDirectories(mods).Length);
    }

    [Fact]
    public void InstallAsMod_NoSharedSongs_LeavesPriorityAlone()
    {
        var result = Run(Row("1_N"));
        string mods = Path.Combine(_folder, "mods");
        OldMod(mods, "e", 1, "la_bike-fk.szs");
        string folder = PackBuilder.InstallAsMod(result.MusicFolder, "T", "", mods);
        Assert.Contains("Priority = 2", File.ReadAllText(Path.Combine(folder, "T.ini")));
    }

    [Fact]
    public void InstallAsMod_ClearsThePacksSongsOutOfTheKeptMod()
    {
        var result = Run(Row("title_bg"));
        string mods = Path.Combine(_folder, "mods");
        string kept = OldMod(mods, "Kept Patches files", 3, "title_bg.brstm");
        File.WriteAllText(Path.Combine(kept, "la_bike-fk.szs"), "kart");

        PackBuilder.InstallAsMod(result.MusicFolder, "T", "", mods);

        Assert.False(File.Exists(Path.Combine(kept, "title_bg.brstm")));
        Assert.True(File.Exists(Path.Combine(kept, "la_bike-fk.szs")));
    }

    [Fact]
    public void InstallAsMod_KeptModLeftEmpty_IsRemoved()
    {
        var result = Run(Row("title_bg"));
        string mods = Path.Combine(_folder, "mods");
        string kept = OldMod(mods, "Kept Patches files", 3, "title_bg.brstm");
        PackBuilder.InstallAsMod(result.MusicFolder, "T", "", mods);
        Assert.False(Directory.Exists(kept));
    }

    [Fact]
    public void FindUnownedPatches_PacksOwnSongs_AreNotAtRisk()
    {
        string rr = Path.Combine(_folder, "rr");
        string patches = Path.Combine(rr, "Patches");
        Directory.CreateDirectory(patches);
        File.WriteAllText(Path.Combine(patches, "title_bg.brstm"), "song");
        File.WriteAllText(Path.Combine(patches, "la_bike-fk.szs"), "kart");

        var loose = PackBuilder.FindUnownedPatches(rr, [Path.Combine(_folder, "nomods")], ["x/title_bg.brstm"]);

        Assert.Equal("la_bike-fk.szs", Path.GetFileName(Assert.Single(loose)));
    }

    [Fact]
    public void ModsFolders_TestOverride_IsTheOnlyTarget()
    {
        Environment.SetEnvironmentVariable("RRMUSIC_MODS", _folder);
        try
        {
            Assert.Equal([_folder], PackBuilder.ModsFolders());
        }
        finally
        {
            Environment.SetEnvironmentVariable("RRMUSIC_MODS", null);
        }
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

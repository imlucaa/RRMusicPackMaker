using System.Text;
using RRMusicPackMaker;
using Xunit;

namespace RRMusicPackMaker.Tests;

public class SetupCheckTests
{
    private static byte[] Settings(byte music = 0, byte customMusic = 0, byte loose = 0)
    {
        var data = new byte[0x20 + 12 + 80];
        Encoding.ASCII.GetBytes("SETT").CopyTo(data, 0x20);
        data[0x20 + 12 + 19] = customMusic;
        data[0x20 + 12 + 20] = music;
        data[0x20 + 12 + 58] = loose;
        return data;
    }

    [Fact]
    public void Evaluate_AllGood_NoWarnings() => Assert.Empty(SetupCheck.Evaluate(Settings(), "6.13.4", 100));

    [Theory]
    [InlineData(1, 0, 0, "Music setting is off")]
    [InlineData(2, 0, 0, "race music off")]
    [InlineData(0, 1, 0, "custom track music")]
    [InlineData(0, 0, 1, "Loose Archive Overrides")]
    public void Evaluate_GameSettingOff_Warns(byte music, byte custom, byte loose, string expected) =>
        Assert.Contains(expected, Assert.Single(SetupCheck.Evaluate(Settings(music, custom, loose), "6.13.4", 100)));

    [Fact]
    public void Evaluate_OtherRetroRewindVersion_SkipsPositionsThatMayHaveMoved() =>
        Assert.Empty(SetupCheck.Evaluate(Settings(music: 1), "6.14.0", 100));

    [Fact]
    public void Evaluate_NoVersionFile_SkipsGameSettings() =>
        Assert.Empty(SetupCheck.Evaluate(Settings(music: 1), null, 100));

    [Theory]
    [InlineData(60, true)]
    [InlineData(79, true)]
    [InlineData(80, false)]
    [InlineData(100, false)]
    public void Evaluate_DolphinVolume(int volume, bool warns) =>
        Assert.Equal(warns, SetupCheck.Evaluate(null, null, volume).Count == 1);

    [Fact]
    public void Evaluate_NothingReadable_NoWarnings() => Assert.Empty(SetupCheck.Evaluate(null, null, null));

    [Fact]
    public void Evaluate_TruncatedSettings_NoCrash() => Assert.Empty(SetupCheck.Evaluate(new byte[8], "6.13.4", null));

    [Fact]
    public void ReadDolphinVolume_OnlyReadsTheDspSection()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[Other]\r\nVolume = 5\r\n[DSP]\r\nDSPThread = True\r\nVolume = 60\r\n[Core]\r\nVolume = 7\r\n");
            Assert.Equal(60, SetupCheck.ReadDolphinVolume(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class UpdateCheckTests
{
    [Theory]
    [InlineData("v1.2.0", "1.1.0.0", true)]
    [InlineData("v1.1.1", "1.1.0.0", true)]
    [InlineData("v2.0", "1.1.0.0", true)]
    [InlineData("v1.1.0", "1.1.0.0", false)]
    [InlineData("1.1.0", "1.1.0.0", false)]
    [InlineData("v1.0.0", "1.1.0.0", false)]
    [InlineData("nightly", "1.1.0.0", false)]
    public void IsNewer_ComparesVersions(string tag, string current, bool expected) =>
        Assert.Equal(expected, UpdateCheck.IsNewer(tag, Version.Parse(current)));
}

public class RemovePackTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rrm_remove_" + Guid.NewGuid().ToString("N"));
    private string Mods => Path.Combine(_folder, "mods");
    private string Patches => Path.Combine(_folder, "rr", "Patches");

    public RemovePackTests()
    {
        Directory.CreateDirectory(Patches);
    }

    public void Dispose() => Directory.Delete(_folder, true);

    private void Mod(string name, params string[] files)
    {
        string folder = Path.Combine(Mods, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name + ".ini"), "[Mod]");
        foreach (string file in files)
        {
            File.WriteAllText(Path.Combine(folder, file), "x");
            File.WriteAllText(Path.Combine(Patches, file), "x");
        }
    }

    private int Remove(string? music = null) => PackBuilder.RemovePack("Pack", [Mods], Path.Combine(_folder, "rr"), music);

    [Fact]
    public void RemovePack_DeletesModAndItsPatches_IncludingTheSoundArchive()
    {
        Mod("Pack", "title_bg.brstm", SoundArchive.FileName);
        Assert.Equal(2, Remove());
        Assert.False(Directory.Exists(Path.Combine(Mods, "Pack")));
        Assert.Empty(Directory.GetFiles(Patches));
    }

    [Fact]
    public void RemovePack_LeavesOtherModsAndUnrelatedFiles()
    {
        Mod("Pack", "title_bg.brstm");
        Mod("e", "la_bike-fk.szs");
        File.WriteAllText(Path.Combine(Patches, "mine.szs"), "x");
        Remove();
        Assert.True(File.Exists(Path.Combine(Patches, "la_bike-fk.szs")));
        Assert.True(File.Exists(Path.Combine(Patches, "mine.szs")));
        Assert.True(Directory.Exists(Path.Combine(Mods, "e")));
    }

    [Fact]
    public void RemovePack_FileAnotherModNeeds_StaysInPatches()
    {
        Mod("Pack", "title_bg.brstm");
        Mod("other", "title_bg.brstm");
        Assert.Equal(0, Remove());
        Assert.True(File.Exists(Path.Combine(Patches, "title_bg.brstm")));
    }

    [Fact]
    public void RemovePack_WithoutWheelWizard_UsesTheBuiltPacksFileList()
    {
        string music = Path.Combine(_folder, "built");
        Directory.CreateDirectory(music);
        File.WriteAllText(Path.Combine(music, "1_N.brstm"), "x");
        File.WriteAllText(Path.Combine(Patches, "1_N.brstm"), "x");
        Assert.Equal(1, Remove(music));
        Assert.True(File.Exists(Path.Combine(music, "1_N.brstm")));
    }

    [Fact]
    public void RemovePack_NothingInstalled_DoesNothing() => Assert.Equal(0, Remove());
}

public class SizeTests
{
    [Fact]
    public void EstimateBytes_MatchesARealStereoFile()
    {
        // A real 121.5 second stereo pack file at 32000 Hz measured 4.44 MB.
        long bytes = PackBuilder.EstimateBytes(121.5, 32000, 2, false);
        Assert.InRange(bytes, 4_300_000, 4_600_000);
    }

    [Fact]
    public void EstimateBytes_FinalLapFile_Doubles() =>
        Assert.Equal(PackBuilder.EstimateBytes(60, 32000, 2, false) * 2, PackBuilder.EstimateBytes(60, 32000, 2, true));
}

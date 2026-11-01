using System.Text;
using RRMusicPackMaker;
using Xunit;

namespace RRMusicPackMaker.Tests;

public class SoundArchiveTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rrm_sound_" + Guid.NewGuid().ToString("N"));

    public SoundArchiveTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, true);

    private static void Put(byte[] d, int at, int value)
    {
        d[at] = (byte)(value >> 24);
        d[at + 1] = (byte)(value >> 16);
        d[at + 2] = (byte)(value >> 8);
        d[at + 3] = (byte)value;
    }

    /// <summary>A small archive with the same layout as the real one. Each sound is (name, volume, type).</summary>
    private static (byte[] Data, int[] VolumeAt) Archive(params (string Name, byte Volume, byte Type)[] sounds)
    {
        var d = new byte[4096];
        Encoding.ASCII.GetBytes("RSAR").CopyTo(d, 0);
        const int symbols = 0x40, info = 0x400;
        Put(d, 0x10, symbols);
        Put(d, 0x18, info);
        Encoding.ASCII.GetBytes("SYMB").CopyTo(d, symbols);
        Encoding.ASCII.GetBytes("INFO").CopyTo(d, info);

        int symbolsBody = symbols + 8, names = 0x20;
        Put(d, symbolsBody, names);
        Put(d, symbolsBody + names, sounds.Length);
        int text = 0x100;
        for (int i = 0; i < sounds.Length; i++)
        {
            Put(d, symbolsBody + names + 4 + i * 4, text);
            Encoding.ASCII.GetBytes(sounds[i].Name).CopyTo(d, symbolsBody + text);
            text += sounds[i].Name.Length + 1;
        }

        int infoBody = info + 8, table = 0x40;
        Put(d, infoBody + 4, table);
        Put(d, infoBody + table, sounds.Length);
        var volumeAt = new int[sounds.Length];
        int entry = 0x200;
        for (int i = 0; i < sounds.Length; i++)
        {
            Put(d, infoBody + table + 4 + i * 8 + 4, entry);
            Put(d, infoBody + entry, i);
            d[infoBody + entry + 0x14] = sounds[i].Volume;
            d[infoBody + entry + 0x16] = sounds[i].Type;
            volumeAt[i] = infoBody + entry + 0x14;
            entry += 0x44;
        }
        return (d, volumeAt);
    }

    [Fact]
    public void BoostMusic_RaisesRaceAndBattleMusicToFullScale()
    {
        var (data, at) = Archive(("STRM_N_DESERT_N", 51, 2), ("STRM_R_SFC_OBAKE_N", 104, 2), ("STRM_N_BTL_VENICE_N", 83, 2));
        Assert.Equal(3, SoundArchive.BoostMusic(data));
        Assert.All(at, position => Assert.Equal(127, data[position]));
    }

    [Fact]
    public void BoostMusic_LeavesFanfaresAndSoundEffectsAlone()
    {
        var (data, at) = Archive(("STRM_N_DESERT_N", 51, 2), ("STRM_O_GOAL1_FAN", 87, 2), ("SE_RC_ITEM", 90, 3), ("STRM_N_FAKE_SEQ", 40, 1));
        SoundArchive.BoostMusic(data);
        Assert.Equal(127, data[at[0]]);
        Assert.Equal(87, data[at[1]]);
        Assert.Equal(90, data[at[2]]);
        Assert.Equal(40, data[at[3]]);
    }

    [Fact]
    public void BoostMusic_NeverLowersASlot_AndChangesOnlyVolumeBytes()
    {
        var (data, at) = Archive(("STRM_N_LOUD_N", 200, 2), ("STRM_N_QUIET_N", 60, 2));
        var before = (byte[])data.Clone();
        Assert.Equal(1, SoundArchive.BoostMusic(data));
        Assert.Equal(200, data[at[0]]);
        Assert.Equal(1, data.Where((value, index) => value != before[index]).Count());
    }

    [Fact]
    public void BoostMusic_NotASoundArchive_Refuses()
    {
        var wrong = new byte[64];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wrong, 0);
        Assert.Throws<InvalidDataException>(() => SoundArchive.BoostMusic(wrong));
    }

    [Fact]
    public void BoostMusic_BrokenLayout_RefusesInsteadOfCorrupting()
    {
        var (data, _) = Archive(("STRM_N_DESERT_N", 51, 2));
        Encoding.ASCII.GetBytes("XXXX").CopyTo(data, 0x400);
        Assert.Throws<InvalidDataException>(() => SoundArchive.BoostMusic(data));
    }

    [Fact]
    public void BoostMusic_OffsetsPastTheEnd_RefusesInsteadOfCrashing()
    {
        var (data, _) = Archive(("STRM_N_DESERT_N", 51, 2));
        Put(data, 0x400 + 8 + 4, 999999);
        Assert.Throws<InvalidDataException>(() => SoundArchive.BoostMusic(data));
    }

    [Fact]
    public void Place_CopiesOnce_ThenLeavesItAlone()
    {
        string archive = Path.Combine(_folder, SoundArchive.FileName);
        File.WriteAllText(archive, "data");
        string target = Path.Combine(_folder, "mod");
        SoundArchive.Place(archive, true, target);
        string placed = Path.Combine(target, SoundArchive.FileName);
        Assert.Equal("data", File.ReadAllText(placed));

        File.SetAttributes(placed, FileAttributes.ReadOnly);
        SoundArchive.Place(archive, true, target);
        File.SetAttributes(placed, FileAttributes.Normal);
    }

    [Fact]
    public void Place_NoArchive_OnlyRemovesWhenTurnedOff()
    {
        string target = Path.Combine(_folder, "mod");
        Directory.CreateDirectory(target);
        string placed = Path.Combine(target, SoundArchive.FileName);
        File.WriteAllText(placed, "data");

        // Making the copy failed: keep what's working.
        SoundArchive.Place(null, true, target);
        Assert.True(File.Exists(placed));

        // Turned off in Settings: remove it.
        SoundArchive.Place(null, false, target);
        Assert.False(File.Exists(placed));
    }
}

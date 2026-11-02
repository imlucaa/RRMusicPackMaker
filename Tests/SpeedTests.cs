using System.Text;
using RRMusicPackMaker;
using Xunit;

namespace RRMusicPackMaker.Tests;

public sealed class SpeedTests : IDisposable
{
    private const int Rate = 32000;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rrm_speed_" + Guid.NewGuid().ToString("N"));

    public SpeedTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    /// <summary>Six seconds of a quiet tone, loud from 2 to 4 seconds, so the loud part marks a place in the song.</summary>
    private string WriteSong()
    {
        string path = Path.Combine(_folder, "song.wav");
        int samples = 6 * Rate;
        using var w = new BinaryWriter(File.Create(path));
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + samples * 4);
        w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);
        w.Write(Rate);
        w.Write(Rate * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(samples * 4);
        for (int i = 0; i < samples; i++)
        {
            double level = i >= 2 * Rate && i < 4 * Rate ? 20000 : 1000;
            short value = (short)(Math.Sin(2 * Math.PI * 440 * i / Rate) * level);
            w.Write(value);
            w.Write(value);
        }
        return path;
    }

    private Song Prepare(double speed, bool keepPitch, LoopMode mode = LoopMode.NoLoop, string start = "", string end = "", double crossfade = 0, bool reverb = false) =>
        Processing.Prepare(WriteSong(), Rate, mode, start, end, Options.LoudnessOff, crossfadeSeconds: crossfade, speed: speed, keepPitch: keepPitch, reverb: reverb);

    private static int LoudestIn(Song song, int from, int to)
    {
        int loudest = 0;
        for (int i = from; i < to; i++)
            loudest = Math.Max(loudest, Math.Abs((int)song.Channels[0][i]));
        return loudest;
    }

    [Fact]
    public void Slowed_LowersThePitch_AndLengthensTheSong()
    {
        var song = Prepare(0.8, keepPitch: false);
        Assert.InRange(song.Length, (int)(7.5 * Rate) - 200, (int)(7.5 * Rate) + 200);
        Assert.InRange(Pitch(song, 3 * Rate, 4 * Rate), 347, 357);
    }

    [Fact]
    public void Slowed_MovesTheLoopWithTheSong()
    {
        var song = Prepare(0.8, keepPitch: false, LoopMode.Custom, "0:02", "0:04");
        Assert.InRange(song.LoopStart, (int)(2.5 * Rate) - 3, (int)(2.5 * Rate) + 3);
        Assert.InRange(song.LoopEnd, 5 * Rate - 3, 5 * Rate + 3);
    }

    [Fact]
    public void Reverb_RingsOnAfterASoundEnds()
    {
        // The song drops from loud to quiet at 4 seconds. Without reverb, it's quiet straight away.
        int after = 4 * Rate + 300;
        int ringEnd = 4 * Rate + (int)(0.3 * Rate);
        Assert.InRange(LoudestIn(Prepare(1, keepPitch: false), after, ringEnd), 0, 1100);
        Assert.InRange(LoudestIn(Prepare(1, keepPitch: false, reverb: true), after, ringEnd), 1500, 32767);
    }

    [Fact]
    public void Reverb_AddsOnlyItsShortTail_AndDoesntClip()
    {
        var song = Prepare(1, keepPitch: false, reverb: true);
        Assert.InRange(song.Length, 6 * Rate - 50, (int)(6.35 * Rate));
        Assert.True(LoudestIn(song, 0, song.Length) < 32767);
    }

    [Fact]
    public void Reverb_LoopJump_GetsALongerBlend()
    {
        var plain = Prepare(0.8, keepPitch: false, LoopMode.Custom, "0:02", "0:04");
        var wet = Prepare(0.8, keepPitch: false, LoopMode.Custom, "0:02", "0:04", reverb: true);
        Assert.InRange(wet.LoopStart - plain.LoopStart, (int)(0.25 * Rate) - 3, (int)(0.25 * Rate) + 3);
    }

    [Fact]
    public void RenderPreview_PlaysTheSongWithItsEffects_AndDoesntChangeTheFile()
    {
        string source = WriteSong();
        byte[] before = File.ReadAllBytes(source);
        var row = new TrackRow { Name = "x", FileBase = "x", Source = source, Speed = 0.8 };

        string file = PackBuilder.RenderPreview(new Options { Loudness = Options.LoudnessOff }, row);
        try
        {
            var heard = new Song { Channels = AudioIO.Decode(file, Rate), Rate = Rate };
            Assert.InRange(heard.Length, (int)(7.5 * Rate) - 300, (int)(7.5 * Rate) + 300);
            Assert.InRange(Pitch(heard, 3 * Rate, 4 * Rate), 347, 357);
        }
        finally
        {
            File.Delete(file);
        }
        Assert.Equal(before, File.ReadAllBytes(source));
    }

    [Fact]
    public void RenderPreview_UsesTheTracksVolume()
    {
        var row = new TrackRow { Name = "x", FileBase = "x", Source = WriteSong() };
        int Loudest(int volume)
        {
            row.VolumeDb = volume;
            string file = PackBuilder.RenderPreview(new Options { Loudness = Options.LoudnessOff }, row);
            try
            {
                var heard = new Song { Channels = AudioIO.Decode(file, Rate), Rate = Rate };
                return LoudestIn(heard, 0, heard.Length);
            }
            finally
            {
                File.Delete(file);
            }
        }
        Assert.True(Loudest(-6) < Loudest(0) * 0.6);
    }

    [Fact]
    public void SpeedChoices_HaveSlowedAndReverb()
    {
        var names = PackBuilder.SpeedChoices.Select(choice => choice.Text).ToList();
        Assert.Contains("Slowed 0.8x + reverb", names);
        Assert.Contains("Slowed 0.8x", names);
        Assert.Contains("Normal + reverb", names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    /// <summary>The tone's pitch, from how often it crosses zero during the loud part.</summary>
    private static double Pitch(Song song, int from, int to)
    {
        var data = song.Channels[0];
        int crossings = 0;
        for (int i = from + 1; i < to; i++)
            if (data[i - 1] < 0 && data[i] >= 0)
                crossings++;
        return crossings / ((to - from) / (double)Rate);
    }

    private static int FirstLoud(Song song) => Array.FindIndex(song.Channels[0], sample => Math.Abs((int)sample) > 10000);

    [Fact]
    public void Nightcore_RaisesThePitch_AndShortensTheSong()
    {
        var song = Prepare(1.25, keepPitch: false);
        Assert.InRange(song.Length, (int)(4.8 * Rate) - 200, (int)(4.8 * Rate) + 200);
        Assert.InRange(Pitch(song, 2 * Rate, 3 * Rate), 545, 555);
    }

    [Fact]
    public void Faster_KeepsThePitch_AndShortensTheSong()
    {
        var song = Prepare(1.25, keepPitch: true);
        Assert.InRange(song.Length, (int)(4.8 * Rate) - 1600, (int)(4.8 * Rate) + 1600);
        Assert.InRange(Pitch(song, 2 * Rate, 3 * Rate), 435, 445);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Speed_MovesTheSongAndItsLoopTogether(bool keepPitch)
    {
        var song = Prepare(1.25, keepPitch, LoopMode.Custom, "0:02", "0:04");

        // 2 and 4 seconds at 1.25x are 1.6 and 3.2 seconds.
        int blend = keepPitch ? (int)(0.03 * Rate) : 0;
        int tolerance = keepPitch ? (int)(0.02 * Rate) : 3;
        Assert.InRange(song.LoopEnd, 102400 - tolerance, 102400 + tolerance);
        Assert.InRange(song.LoopStart, 51200 + blend - tolerance, 51200 + blend + tolerance);
        Assert.InRange(FirstLoud(song), 51200 - tolerance - 200, 51200 + tolerance + 200);
    }

    [Fact]
    public void Faster_LoopJump_GetsAShortBlend_UnlessALongerOneIsSet()
    {
        var plain = Prepare(1.25, keepPitch: true, LoopMode.Custom, "0:02", "0:04");
        var blended = Prepare(1.25, keepPitch: true, LoopMode.Custom, "0:02", "0:04", crossfade: 0.5);
        Assert.Equal(plain.LoopEnd, blended.LoopEnd);
        Assert.InRange(blended.LoopStart - plain.LoopStart, (int)(0.47 * Rate) - 2, (int)(0.47 * Rate) + 2);
    }

    [Fact]
    public void NormalSpeed_IsUntouched_WhateverThePitchSetting()
    {
        var song = Prepare(1, keepPitch: false);
        var same = Prepare(1, keepPitch: true);
        Assert.Equal(song.Channels[0], same.Channels[0]);
        Assert.InRange(song.Length, 6 * Rate - 5, 6 * Rate);
    }

    [Fact]
    public void SpeedIndex_FindsEveryChoice_AndFallsBackToNormal()
    {
        for (int i = 1; i < PackBuilder.SpeedChoices.Length; i++)
        {
            var choice = PackBuilder.SpeedChoices[i];
            Assert.Equal(i, PackBuilder.SpeedIndex(choice.Speed, choice.KeepPitch, choice.Reverb));
        }
        Assert.Equal(0, PackBuilder.SpeedIndex(1, true));
        Assert.Equal(0, PackBuilder.SpeedIndex(7, false));
    }

    [Fact]
    public void Prepare_BadSpeed_PlaysAtNormalSpeed()
    {
        Assert.InRange(Prepare(0, keepPitch: false).Length, 6 * Rate - 5, 6 * Rate);
    }
}

using RRMusicPackMaker;
using Xunit;

namespace RRMusicPackMaker.Tests;

public class EditorTests
{
    private static EditClip Clip(string source, long start, long end) => new(source, start, end);

    /// <summary>Each sample holds its own position plus an offset, so a test can see exactly which audio was used.</summary>
    private static short[][] Counting(int count, short offset = 0)
    {
        var data = Enumerable.Range(0, count).Select(i => (short)(i + offset)).ToArray();
        return [data, (short[])data.Clone()];
    }

    [Fact]
    public void SplitAt_CutsThePartUnderTheCursor_AndKeepsEverySample()
    {
        var clips = new List<EditClip> { Clip("a", 0, 44100), Clip("b", 1000, 45100) };
        var split = EditTimeline.SplitAt(clips, 44100 + 22050);
        Assert.Equal([Clip("a", 0, 44100), Clip("b", 1000, 23050), Clip("b", 23050, 45100)], split);
        Assert.Equal(EditTimeline.Length(clips), EditTimeline.Length(split));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(44100)]
    [InlineData(44099)]
    public void SplitAt_TooCloseToAnEdge_ChangesNothing(long position)
    {
        var clips = new List<EditClip> { Clip("a", 0, 44100), Clip("b", 0, 44100) };
        Assert.Equal(clips, EditTimeline.SplitAt(clips, position));
    }

    [Fact]
    public void SplitAt_NoParts_ChangesNothing() => Assert.Empty(EditTimeline.SplitAt([], 5));

    [Fact]
    public void SplitAt_LeavesTheOriginalListAlone_SoUndoWorks()
    {
        var clips = new List<EditClip> { Clip("a", 0, 44100) };
        EditTimeline.SplitAt(clips, 22050);
        Assert.Single(clips);
    }

    [Fact]
    public void CropStart_OnlyCutsThePartUnderTheCursor()
    {
        var clips = new List<EditClip> { Clip("a", 0, 44100), Clip("b", 1000, 45100) };
        Assert.Equal([Clip("a", 0, 44100), Clip("b", 23050, 45100)], EditTimeline.CropStart(clips, 44100 + 22050));
    }

    [Fact]
    public void CropEnd_OnlyCutsThePartUnderTheCursor()
    {
        var clips = new List<EditClip> { Clip("a", 0, 44100), Clip("b", 1000, 45100) };
        Assert.Equal([Clip("a", 0, 22050), Clip("b", 1000, 45100)], EditTimeline.CropEnd(clips, 22050));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(44099)]
    public void CropStart_NoRoom_ChangesNothing(long position)
    {
        var clips = new List<EditClip> { Clip("a", 0, 44100) };
        Assert.Equal(clips, EditTimeline.CropStart(clips, position));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(44100)]
    public void CropEnd_NoRoom_ChangesNothing(long position)
    {
        var clips = new List<EditClip> { Clip("a", 0, 44100) };
        Assert.Equal(clips, EditTimeline.CropEnd(clips, position));
    }

    [Fact]
    public void Crop_NoParts_ChangesNothing()
    {
        Assert.Empty(EditTimeline.CropStart([], 5));
        Assert.Empty(EditTimeline.CropEnd([], 5));
    }

    [Fact]
    public void MakeEqual_CutsEveryPartToTheShortest()
    {
        var clips = new List<EditClip> { Clip("a", 100, 1100), Clip("b", 0, 400) };
        Assert.Equal([Clip("a", 100, 500), Clip("b", 0, 400)], EditTimeline.MakeEqual(clips));
    }

    [Fact]
    public void MakeEqual_ChosenLength_AppliesToEveryPart()
    {
        var clips = new List<EditClip> { Clip("a", 0, 1000), Clip("b", 0, 400) };
        Assert.Equal([Clip("a", 0, 300), Clip("b", 0, 300)], EditTimeline.MakeEqual(clips, 300));
    }

    [Fact]
    public void MakeEqual_LongerThanThePartsHave_UsesTheShortest()
    {
        var clips = new List<EditClip> { Clip("a", 0, 1000), Clip("b", 0, 400) };
        Assert.Equal([Clip("a", 0, 400), Clip("b", 0, 400)], EditTimeline.MakeEqual(clips, 5000));
    }

    [Fact]
    public void MakeEqual_NoParts_IsEmpty() => Assert.Empty(EditTimeline.MakeEqual([]));

    [Theory]
    [InlineData(0, 2, "bac")]
    [InlineData(0, 3, "bca")]
    [InlineData(2, 0, "cab")]
    [InlineData(1, 1, "abc")]
    [InlineData(1, 2, "abc")]
    public void Move_PutsThePartBeforeTheChosenPart(int from, int insertBefore, string expected)
    {
        var clips = new List<EditClip> { Clip("a", 0, 1), Clip("b", 0, 1), Clip("c", 0, 1) };
        Assert.Equal(expected, string.Concat(EditTimeline.Move(clips, from, insertBefore).Select(c => c.Source)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]
    [InlineData(100, 1)]
    [InlineData(250, 1)]
    [InlineData(5000, 1)]
    public void ClipAt_FindsThePart(long position, int expected)
    {
        var clips = new List<EditClip> { Clip("a", 0, 100), Clip("b", 0, 200) };
        Assert.Equal(expected, EditTimeline.ClipAt(clips, position));
    }

    [Fact]
    public void Slice_TakesOnlyThatStretch_AcrossParts()
    {
        var clips = new List<EditClip> { Clip("a", 10, 110), Clip("b", 0, 100), Clip("c", 0, 100) };
        Assert.Equal([Clip("a", 60, 110), Clip("b", 0, 50)], EditTimeline.Slice(clips, 50, 100));
    }

    [Fact]
    public void Slice_PastTheEnd_IsEmpty() => Assert.Empty(EditTimeline.Slice([Clip("a", 0, 10)], 10, 5));

    [Fact]
    public void Render_PlaysThePartsInOrder_FromTheRightPlaces()
    {
        var audio = new Dictionary<string, short[][]> { ["a"] = Counting(100), ["b"] = Counting(100, 1000) };
        var result = EditTimeline.Render([Clip("b", 10, 13), Clip("a", 5, 7)], source => audio[source]);
        Assert.Equal(new short[] { 1010, 1011, 1012, 5, 6 }, result[0]);
        Assert.Equal(result[0], result[1]);
    }

    [Fact]
    public void Render_Blend_ShortensByTheBlend_AndLeavesTheEdgesAlone()
    {
        var loud = Enumerable.Repeat((short)10000, 1000).ToArray();
        var quiet = new short[1000];
        var audio = new Dictionary<string, short[][]> { ["loud"] = [loud, loud], ["quiet"] = [quiet, quiet] };
        var result = EditTimeline.Render([Clip("loud", 0, 1000), Clip("quiet", 0, 1000)], source => audio[source], 100);

        Assert.Equal(1900, result[0].Length);
        Assert.Equal(10000, result[0][899]);
        Assert.Equal(0, result[0][1000]);
        Assert.InRange(result[0][950], 6000, 8000);
    }

    [Fact]
    public void Render_Blend_NeverTakesMoreThanHalfOfAShortPart()
    {
        var audio = new Dictionary<string, short[][]> { ["a"] = Counting(1000) };
        var result = EditTimeline.Render([Clip("a", 0, 1000), Clip("a", 0, 100)], _ => audio["a"], 500);
        Assert.Equal(1050, result[0].Length);
    }

    [Fact]
    public void Render_PartPastTheEndOfItsFile_UsesWhatsThere()
    {
        var audio = new Dictionary<string, short[][]> { ["a"] = Counting(10) };
        Assert.Equal(5, EditTimeline.Render([Clip("a", 5, 500)], _ => audio["a"])[0].Length);
    }

    [Fact]
    public void UniquePath_NeverReplacesAFile_AndCleansTheName()
    {
        string folder = Path.Combine(Path.GetTempPath(), "rrm_edit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string first = EditTimeline.UniquePath(folder, "a:b");
            Assert.Equal("a_b.wav", Path.GetFileName(first));
            File.WriteAllText(first, "x");
            string second = EditTimeline.UniquePath(folder, "a:b");
            Assert.Equal("a_b (2).wav", Path.GetFileName(second));
            File.WriteAllText(second, "x");
            Assert.Equal("a_b (3).wav", Path.GetFileName(EditTimeline.UniquePath(folder, "a:b")));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}

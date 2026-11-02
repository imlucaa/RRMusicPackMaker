using RRMusicPackMaker;
using Xunit;

namespace RRMusicPackMaker.Tests;

[Collection("windows")]
public sealed class SongLibraryTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "rrm_lib_" + Guid.NewGuid().ToString("N"));
    private readonly string _downloads;

    public SongLibraryTests()
    {
        _downloads = Path.Combine(_data, "Downloaded songs");
        Directory.CreateDirectory(_downloads);
        Environment.SetEnvironmentVariable("RRMUSIC_DATA", _data);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("RRMUSIC_DATA", null);
        Directory.Delete(_data, true);
    }

    private string Song(string name, string? folder = null)
    {
        string path = Path.Combine(folder ?? _data, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private SongLibrary Library() => new(() => _downloads);

    [Fact]
    public void Add_PutsNewestFirst_AndKeepsAddedSongsInPlace()
    {
        var library = Library();
        string a = Song("a.mp3"), b = Song("b.mp3");
        library.Add(a);
        library.Add(b);
        library.Add(a);
        Assert.Equal([b, a], library.Songs);
    }

    [Fact]
    public void Add_SameSongWrittenAnotherWay_IsOneSong()
    {
        var library = Library();
        string a = Song("a.mp3");
        library.Add(a);
        library.Add(a.Replace('\\', '/').ToUpperInvariant());
        Assert.Single(library.Songs);
    }

    [Fact]
    public void Add_IgnoresBlanksAndFilesThatArentAudio()
    {
        var library = Library();
        library.Add(["", "  ", Song("notes.txt")]);
        Assert.Empty(library.Songs);
    }

    [Fact]
    public void Songs_FindsDownloads_AfterAddedSongs()
    {
        var library = Library();
        string download = Song("download.opus", _downloads);
        Song("cover.jpg", _downloads);
        string added = Song("a.mp3");
        library.Add(added);
        Assert.Equal([added, download], library.Songs);
    }

    [Fact]
    public void Songs_LeavesOutFilesThatAreGone()
    {
        var library = Library();
        string a = Song("a.mp3");
        library.Add(a);
        File.Delete(a);
        Assert.Empty(library.Songs);
    }

    [Fact]
    public void Songs_MissingDownloadsFolder_IsFine()
    {
        var library = new SongLibrary(() => Path.Combine(_data, "nothing here"));
        Assert.Empty(library.Songs);
    }

    [Fact]
    public void List_IsRememberedNextTime()
    {
        string a = Song("a.mp3");
        Library().Add(a);
        Assert.Equal([a], Library().Songs);
    }

    [Fact]
    public void Remove_TakesItOff_AndItStaysOff()
    {
        var library = Library();
        string a = Song("a.mp3");
        library.Add(a);
        library.Remove(a);
        Assert.Empty(library.Songs);
        Assert.True(File.Exists(a));
        Assert.Empty(Library().Songs);
    }

    [Fact]
    public void KindOf_TellsDownloadsImportsAndEditsApart()
    {
        var library = Library();
        string imported = Song("a.mp3"), download = Song("d.opus", _downloads), edit = Song("e.wav", _downloads);
        library.Add(imported);
        library.AddEdit(edit, [new EditClip(download, 0, 10)]);
        Assert.Equal(SongKind.Imported, library.KindOf(imported));
        Assert.Equal(SongKind.Downloaded, library.KindOf(download));
        Assert.Equal(SongKind.Edited, library.KindOf(edit));
        Assert.Contains("edited", library.LabelOf(edit));
        Assert.DoesNotContain("edited", library.LabelOf(download));
    }

    [Fact]
    public void Edits_RememberTheirParts_NextTime()
    {
        string source = Song("a.mp3"), edit = Song("e.wav", _downloads);
        Library().AddEdit(edit, [new EditClip(source, 5, 10), new EditClip(source, 20, 30)]);

        var again = Library();
        Assert.Equal(SongKind.Edited, again.KindOf(edit));
        Assert.Equal([new EditClip(source, 5, 10), new EditClip(source, 20, 30)], again.PartsOf(edit));
        Assert.Equal(["a"], again.MadeFrom(edit));
    }

    [Fact]
    public void Remove_ForgetsAnEditsParts()
    {
        var library = Library();
        string edit = Song("e.wav", _downloads);
        library.AddEdit(edit, [new EditClip(edit, 0, 1)]);
        library.Remove(edit);
        Assert.Null(library.PartsOf(edit));
        Assert.Equal(SongKind.Downloaded, library.KindOf(edit));
    }

    [Fact]
    public void OldList_OfJustPaths_StillLoads()
    {
        string a = Song("a.mp3");
        File.WriteAllText(Path.Combine(_data, "songs.json"), System.Text.Json.JsonSerializer.Serialize(new[] { a }));
        Assert.Equal([a], Library().Songs);
    }

    [Fact]
    public void ImportedCopies_CanBeDeleted_ButStillCountAsImported()
    {
        var library = Library();
        string copies = Path.Combine(_data, "Imported packs", "Pack");
        Directory.CreateDirectory(copies);
        string copy = Song("n_farm_n.brstm", copies);
        library.Add(copy);
        Assert.True(library.IsOwned(copy));
        Assert.Equal(SongKind.Imported, library.KindOf(copy));

        string mine = Song("mine.mp3");
        Assert.False(library.IsOwned(mine));
    }

    [Fact]
    public void Remove_Many_TakesThemAllOff()
    {
        var library = Library();
        string a = Song("a.mp3"), b = Song("b.mp3"), c = Song("c.mp3");
        library.Add([a, b, c]);
        library.Remove([a, c]);
        Assert.Equal([b], library.Songs);
    }

    [Fact]
    public void DamagedList_StartsEmpty()
    {
        File.WriteAllText(Path.Combine(_data, "songs.json"), "{ not json");
        Assert.Empty(Library().Songs);
    }

    [Fact]
    public void Changed_OnlyFiresWhenTheListChanges()
    {
        var library = Library();
        string a = Song("a.mp3");
        int changes = 0;
        library.Changed += () => changes++;
        library.Add(a);
        library.Add(a);
        Assert.Equal(1, changes);
        library.Remove(a);
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData(@"C:\x\Artist - Song [-_DPAJ71B7o].opus", "Artist - Song")]
    [InlineData(@"C:\x\LVL - [intro] beat [NZGsrUjvOls].opus", "LVL - [intro] beat")]
    [InlineData(@"C:\x\Song [live].mp3", "Song [live]")]
    [InlineData(@"C:\x\Plain.wav", "Plain")]
    public void DisplayName_DropsOnlyTheVideoId(string path, string expected) =>
        Assert.Equal(expected, SongLibrary.DisplayName(path));
}

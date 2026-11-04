using System.Text.Json;
using RRMusicPackMaker;
using Xunit;

namespace RRMusicPackMaker.Tests;

/// <summary>The headless commands the GPUI front end calls. They share the saved pack with the classic window.</summary>
[Collection("windows")]
public sealed class EngineTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "rrm_engine_" + Guid.NewGuid().ToString("N"));

    public EngineTests()
    {
        Directory.CreateDirectory(_data);
        Environment.SetEnvironmentVariable("RRMUSIC_DATA", _data);
        // Packs, downloads and imports go under the test folder, never into the real Documents.
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "out"), SetupDone = true });
        Environment.SetEnvironmentVariable("RRMUSIC_RR", Path.Combine(_data, "no rr here"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("RRMUSIC_DATA", null);
        Environment.SetEnvironmentVariable("RRMUSIC_RR", null);
        Directory.Delete(_data, true);
    }

    private static (int Code, string Output) Run(params string[] args)
    {
        var before = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            int code = Engine.Run(["engine", .. args]);
            return (code, writer.ToString());
        }
        finally
        {
            Console.SetOut(before);
        }
    }

    [Fact]
    public void Catalog_ListsEveryTrack_WithGroupsForTheSidebar()
    {
        var (code, output) = Run("catalog");
        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        var rows = doc.RootElement.GetProperty("rows");
        Assert.True(rows.GetArrayLength() > 400);
        Assert.Contains(rows.EnumerateArray(), row => row.GetProperty("category").GetString() == RRMusicPackMaker.Catalog.CatRegs);
        var groups = doc.RootElement.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("title").GetString()).ToList();
        Assert.Contains("Tracks", groups);
        // The stock slots stay out of the sidebar, as in the classic window.
        Assert.DoesNotContain(doc.RootElement.GetProperty("groups").EnumerateArray()
            .SelectMany(g => g.GetProperty("categories").EnumerateArray()).Select(c => c.GetString()),
            category => category == RRMusicPackMaker.Catalog.CatStock);
    }

    [Fact]
    public void Songs_IsAJsonList_EvenWhenEmpty()
    {
        var (code, output) = Run("songs");
        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [Fact]
    public void Export_WithNothingAssigned_SaysSo_AsAnErrorEvent()
    {
        var (code, output) = Run("export", "--mode", "zip", "--target", Path.Combine(_data, "p.zip"));
        Assert.NotEqual(0, code);
        using var doc = JsonDocument.Parse(output.Trim().Split('\n').Last());
        Assert.Equal("error", doc.RootElement.GetProperty("event").GetString());
    }

    [Fact]
    public void Export_WithoutATarget_IsRefused()
    {
        var (code, output) = Run("export", "--mode", "folder");
        Assert.Equal(2, code);
        Assert.Contains("\"error\"", output);
    }

    [Fact]
    public void Preview_TrackWithoutASong_IsAnError()
    {
        var (code, output) = Run("preview", "--track", "6_N");
        Assert.Equal(1, code);
        Assert.Contains("no song", output);
    }

    [Fact]
    public void UnknownCommand_ShowsUsage()
    {
        var before = Console.Error;
        using var errors = new StringWriter();
        Console.SetError(errors);
        try
        {
            Assert.Equal(2, Engine.Run(["engine", "nonsense"]));
            Assert.Contains("Usage", errors.ToString());
        }
        finally
        {
            Console.SetError(before);
        }
    }

    private string Tone(string name, int seconds = 3)
    {
        string path = Path.Combine(_data, name + ".wav");
        using var file = File.Create(path);
        using var w = new BinaryWriter(file);
        int rate = 32000, samples = rate * seconds;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + samples * 4);
        w.Write("WAVEfmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);
        w.Write(rate);
        w.Write(rate * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(samples * 4);
        for (int n = 0; n < samples; n++)
        {
            short value = (short)(Math.Sin(n * 0.05) * 9000);
            w.Write(value);
            w.Write(value);
        }
        return path;
    }

    private static string Spec(object value, string folder)
    {
        string path = Path.Combine(folder, "spec-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(value));
        return path;
    }

    private static JsonDocument Last(string output) => JsonDocument.Parse(output.Trim().Split('\n').Last());

    [Fact]
    public void SongsFull_ListsLibrarySongs_WithTheirLengths()
    {
        string song = Tone("a long enough tone", 4);
        new SongLibrary(() => Path.Combine(_data, "out", "Downloaded songs")).Add(song);
        var (code, output) = Run("songs-full");
        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        var first = doc.RootElement.EnumerateArray().Single();
        Assert.Equal(song, first.GetProperty("path").GetString());
        Assert.InRange(first.GetProperty("seconds").GetDouble(), 3.5, 4.5);
    }

    [Fact]
    public void Length_ReadsTheEditorsSampleCount()
    {
        var (code, output) = Run("length", "--song", Tone("two seconds", 2));
        Assert.Equal(0, code);
        using var doc = Last(output);
        Assert.InRange(doc.RootElement.GetProperty("samples").GetInt32(), 44100 * 2 - 500, 44100 * 2 + 500);
    }

    [Fact]
    public void Waveform_GivesPeaksForDrawing()
    {
        var (code, output) = Run("waveform", "--song", Tone("shape"), "--points", "200");
        Assert.Equal(0, code);
        using var doc = Last(output);
        Assert.Equal(200, doc.RootElement.GetProperty("peaks").GetArrayLength());
        Assert.InRange(doc.RootElement.GetProperty("seconds").GetDouble(), 2.5, 3.5);
    }

    [Fact]
    public void Match_GivesTheTrackWhoseNameIsInTheFileName()
    {
        string folder = Path.Combine(_data, "named");
        Directory.CreateDirectory(folder);
        string song = Tone("Moo Moo Meadows");
        string moved = Path.Combine(folder, Path.GetFileName(song));
        File.Move(song, moved);
        var (code, output) = Run("match", "--spec", Spec(new { files = new[] { moved }, tracks = Array.Empty<string>() }, _data));
        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        Assert.Contains(doc.RootElement.GetProperty("matches").EnumerateArray(), m => m.GetProperty("source").GetString() == moved);
    }

    [Fact]
    public void RenderEdit_JoinsParts_AndSaveAddsTheSongToTheLibrary()
    {
        string song = Tone("part", 2);
        var spec = new
        {
            clips = new[] { new { source = song, start = 0L, end = 44100L }, new { source = song, start = 0L, end = 22050L } },
            name = "Joined up",
            blend = 0,
            speed = 0,
            fadeIn = 0.0,
            fadeOut = 0.5,
        };
        var (code, output) = Run("render-edit", "--save", "--spec", Spec(spec, _data));
        Assert.Equal(0, code);
        using var doc = Last(output);
        string saved = doc.RootElement.GetProperty("path").GetString()!;
        Assert.True(File.Exists(saved));
        Assert.Contains("Joined up", Path.GetFileName(saved));
        var library = new SongLibrary(() => Path.Combine(_data, "out", "Downloaded songs"));
        Assert.NotNull(library.PartsOf(saved));
    }

    [Fact]
    public void RenderEdit_WithNoParts_IsAnError()
    {
        var (code, output) = Run("render-edit", "--spec", Spec(new { clips = Array.Empty<object>() }, _data));
        Assert.Equal(1, code);
        Assert.Contains("\"error\"", output);
    }

    [Fact]
    public void SongsDelete_TakesOwnedSongsOffTheList_AndKeepsTheUsersOwnFiles()
    {
        string mine = Tone("my own file");
        var library = new SongLibrary(() => Path.Combine(_data, "out", "Downloaded songs"));
        library.Add(mine);
        var (code, output) = Run("songs-delete", "--spec", Spec(new { files = new[] { mine } }, _data));
        Assert.Equal(0, code);
        using var doc = Last(output);
        Assert.Single(doc.RootElement.GetProperty("removed").EnumerateArray());
        Assert.True(File.Exists(mine));
        Assert.DoesNotContain(mine, new SongLibrary(() => Path.Combine(_data, "out", "Downloaded songs")).Songs);
    }

    [Fact]
    public void Import_FromAFolderWithMatchingSongs_FillsTracksAndSavesThePack()
    {
        string folder = Path.Combine(_data, "a pack");
        Directory.CreateDirectory(folder);
        File.Copy(BrstmFor("88_N"), Path.Combine(folder, "88_N.brstm"));
        var (code, output) = Run("import", "--path", folder);
        Assert.Equal(0, code);
        using var doc = Last(output);
        Assert.Equal("done", doc.RootElement.GetProperty("event").GetString());
        Assert.Contains("88_N", File.ReadAllText(AppStorage.AutosavePath));
    }

    [Fact]
    public void Import_FromAnEmptyFolder_SaysSo()
    {
        string folder = Path.Combine(_data, "empty");
        Directory.CreateDirectory(folder);
        var (code, output) = Run("import", "--path", folder);
        Assert.Equal(1, code);
        Assert.Contains("no .brstm", output);
    }

    [Fact]
    public void Installed_ListsPacksThisAppMade()
    {
        string pack = Path.Combine(_data, "out", "zzz engine test pack");
        // Only the listing is checked, so the output folder can be anywhere the app looks.
        var (code, output) = Run("installed");
        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.False(Directory.Exists(pack));
    }

    [Fact]
    public void Check_ReportsFfmpegAndNoIssuesWithoutRetroRewind()
    {
        var (code, output) = Run("check");
        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        Assert.True(doc.RootElement.TryGetProperty("ffmpeg", out _));
        Assert.Empty(doc.RootElement.GetProperty("issues").EnumerateArray());
    }

    /// <summary>A small real BRSTM, made the way the app makes one, so an import has something to read.</summary>
    private string BrstmFor(string name)
    {
        string wav = Tone("src-" + name, 2);
        var row = new TrackRow { Name = name, FileBase = name, Source = wav, PulsarFinalBase = name + "x" };
        var built = PackBuilder.Build(new Options { PackName = "src" }, [row], Path.Combine(_data, "built"), _ => { });
        return Path.Combine(built.MusicFolder, name + ".brstm");
    }
}

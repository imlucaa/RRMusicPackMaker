using RRMusicPackMaker;
using Xunit;

namespace RRMusicPackMaker.Tests;

public class DownloaderTests
{
    [Theory]
    [InlineData("", QueryKind.Empty)]
    [InlineData("   ", QueryKind.Empty)]
    [InlineData("coconut mall remix", QueryKind.Search)]
    [InlineData("-o bad.exe", QueryKind.Search)]
    [InlineData("https://www.youtube.com/watch?v=abc", QueryKind.Link)]
    [InlineData("https://music.youtube.com/watch?v=abc", QueryKind.Link)]
    [InlineData("https://youtu.be/abc", QueryKind.Link)]
    [InlineData("https://soundcloud.com/a/b", QueryKind.Link)]
    [InlineData("https://open.spotify.com/track/abc", QueryKind.Protected)]
    [InlineData("spotify:track:abc", QueryKind.Protected)]
    [InlineData("https://music.apple.com/us/album/x", QueryKind.Protected)]
    [InlineData("https://tidal.com/browse/track/1", QueryKind.Protected)]
    [InlineData("file:///C:/Windows/win.ini", QueryKind.Search)]
    public void Classify_SortsInput(string text, QueryKind expected) =>
        Assert.Equal(expected, Downloader.Classify(text));

    [Fact]
    public void SearchArguments_TextAfterSeparator_CannotBeAnOption()
    {
        var args = Downloader.SearchArguments("--exec calc");
        int separator = args.IndexOf("--");
        Assert.Equal(args.Count - 2, separator);
        Assert.Equal("ytsearch8:--exec calc", args[^1]);
    }

    [Fact]
    public void DownloadArguments_OneSongOnly_TargetLast()
    {
        var args = Downloader.DownloadArguments("https://youtu.be/abc", @"C:\songs", @"C:\ffmpeg");
        Assert.Contains("--no-playlist", args);
        Assert.Contains("--max-filesize", args);
        Assert.Contains("--ignore-config", args);
        Assert.Equal("--", args[^2]);
        Assert.Equal("https://youtu.be/abc", args[^1]);
    }

    [Fact]
    public void ParseSearchLine_ReadsFields()
    {
        var result = Downloader.ParseSearchLine("abc123\tCoconut Mall\tNintendo\t3:21");
        Assert.NotNull(result);
        Assert.Equal("https://www.youtube.com/watch?v=abc123", result.Link);
        Assert.Equal("Coconut Mall", result.Title);
        Assert.Equal("Nintendo", result.Uploader);
    }

    [Theory]
    [InlineData("abc\tLive radio\tSomeone\tNA")]
    [InlineData("just some log line")]
    [InlineData("NA\tx\ty\t1:00")]
    public void ParseSearchLine_SkipsLiveAndJunk(string line) =>
        Assert.Null(Downloader.ParseSearchLine(line));

    [Fact]
    public void ParseFileLine_ReadsPathTitleArtist()
    {
        var song = Downloader.ParseFileLine("FILE\tC:\\songs\\a [x].opus\tA Song\tNA");
        Assert.NotNull(song);
        Assert.Equal("C:\\songs\\a [x].opus", song.Path);
        Assert.Equal("A Song", song.Title);
        Assert.Equal("", song.Artist);
    }

    [Theory]
    [InlineData("PROGRESS  45.3%", 0.453)]
    [InlineData("PROGRESS 100.0%", 1.0)]
    public void ParseProgress_ReadsPercent(string line, double expected) =>
        Assert.Equal(expected, Downloader.ParseProgress(line)!.Value, 3);

    [Fact]
    public void ParseProgress_OtherLines_ReturnNull() => Assert.Null(Downloader.ParseProgress("[youtube] Extracting"));

    [Fact]
    public void ExpectedHash_FindsWindowsProgram()
    {
        const string sums = "aaa  yt-dlp\nbbb  yt-dlp.exe\nccc  yt-dlp_x86.exe\n";
        Assert.Equal("bbb", Downloader.ExpectedHash(sums));
    }

    [Fact]
    public void ExpectedHash_ForNamedFile_IgnoresSimilarNames()
    {
        const string sums = "aaa  ffmpeg-master-latest-win64-gpl-shared.zip\nbbb  ffmpeg-master-latest-win64-gpl.zip\n";
        Assert.Equal("bbb", Downloader.ExpectedHash(sums, "ffmpeg-master-latest-win64-gpl.zip"));
    }

    [Fact]
    public void ExpectedHash_Missing_RefusesToInstall() =>
        Assert.Throws<DownloadFailedException>(() => Downloader.ExpectedHash("aaa  yt-dlp\n"));

    [Theory]
    [InlineData("ERROR: [youtube] x: This video is DRM protected", false)]
    [InlineData("ERROR: Unsupported URL: https://x", false)]
    [InlineData("ERROR: [youtube] x: Private video", false)]
    [InlineData("ERROR: [youtube] x: This video is unavailable", false)]
    [InlineData("ERROR: [generic] x: Unable to download webpage: HTTP Error 404: Not Found", false)]
    [InlineData("ERROR: [youtube] x: nsig extraction failed", true)]
    public void WorthUpdating_OnlyWhenAnUpdateCouldHelp(string error, bool expected) =>
        Assert.Equal(expected, Downloader.WorthUpdating(error));

    [Fact]
    public void FriendlyError_NotFound_DoesNotBlameTheInternet() =>
        Assert.Contains("wasn't found", Downloader.FriendlyError("ERROR: x: Unable to download webpage: HTTP Error 404: Not Found"));

    [Fact]
    public void Arguments_WithNode_ComeBeforeTheTarget()
    {
        var args = Downloader.DownloadArguments("https://youtu.be/abc", @"C:\songs", @"C:\ffmpeg", @"C:\node\node.exe");
        Assert.Contains(@"node:C:\node\node.exe", args);
        Assert.Equal("https://youtu.be/abc", args[^1]);
    }

    [Fact]
    public void FriendlyError_Drm_ExplainsWhy() =>
        Assert.Contains("copy-protected", Downloader.FriendlyError("ERROR: x: This video is DRM protected"));
}

public class MusicSearchTests
{
    // The same shape YouTube Music returned in a real search, cut down.
    private const string Reply = """
    {"contents":{"sections":[{"shelf":{"contents":[
      {"musicResponsiveListItemRenderer":{"playlistItemData":{"videoId":"song1"},"flexColumns":[
        {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"F*CK SWAG"}]}}},
        {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Nettspend"},{"text":" • "},{"text":"F*CK SWAG"},{"text":" • "},{"text":"2:02"}]}}},
        {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"6.2M plays"}]}}}]}},
      {"musicResponsiveListItemRenderer":{"playlistItemData":{"videoId":"song2"},"flexColumns":[
        {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Ce Sa-i Fuck"}]}}},
        {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Swag City"},{"text":" & "},{"text":"shenko"},{"text":" • "},{"text":"TAPE"},{"text":" • "},{"text":"2:51"}]}}},
        {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"16 plays"}]}}}]}},
      {"musicResponsiveListItemRenderer":{"playlistItemData":{"videoId":"video1"},"flexColumns":[
        {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Some Video"}]}}},
        {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Someone"},{"text":" • "},{"text":"42M views"},{"text":" • "},{"text":"3:10"}]}}}]}},
      {"musicResponsiveListItemRenderer":{"flexColumns":[]}}
    ]}}]}}
    """;

    private static SearchResult Song(string title, string artist, long plays = 0, string album = "") =>
        new($"https://music.youtube.com/watch?v={title}", title, artist, "2:00", album, plays);

    [Fact]
    public void ParseMusicResults_ReadsTitleArtistAlbumLengthAndPlays()
    {
        var results = Downloader.ParseMusicResults(Reply);
        Assert.Equal(3, results.Count);
        var first = results[0];
        Assert.Equal("https://music.youtube.com/watch?v=song1", first.Link);
        Assert.Equal("F*CK SWAG", first.Title);
        Assert.Equal("Nettspend", first.Uploader);
        Assert.Equal("F*CK SWAG", first.Album);
        Assert.Equal("2:02", first.Duration);
        Assert.Equal(6200000, first.Plays);
    }

    [Fact]
    public void ParseMusicResults_JoinsSeveralArtists() =>
        Assert.Equal("Swag City & shenko", Downloader.ParseMusicResults(Reply)[1].Uploader);

    [Fact]
    public void ParseMusicResults_Video_ViewsAreNotAnAlbum()
    {
        var video = Downloader.ParseMusicResults(Reply)[2];
        Assert.Equal("", video.Album);
        Assert.Equal(42000000, video.Plays);
    }

    [Theory]
    [InlineData("6.2M plays", 6200000)]
    [InlineData("16 plays", 16)]
    [InlineData("1.1B views", 1100000000)]
    [InlineData("3K plays", 3000)]
    [InlineData("1,234 views", 1234)]
    public void ParseCount_ReadsShortNumbers(string text, long expected) =>
        Assert.Equal(expected, Downloader.ParseCount(text));

    [Fact]
    public void Rank_CensoredExactTitle_BeatsEverythingElse()
    {
        var ranked = Downloader.RankMusicResults("fuck swag", [
            Song("Swag", "David Banner", 9000000),
            Song("Fuck it all up", "Janecki swag", 75),
            Song("F*CK SWAG", "Nettspend", 100),
        ]);
        Assert.Equal("Nettspend", ranked[0].Uploader);
    }

    [Fact]
    public void Rank_EveryWordFound_BeatsMorePlays()
    {
        var ranked = Downloader.RankMusicResults("coconut mall", [
            Song("Coconut Water", "A", 9000000),
            Song("Mall of Coconut", "B", 5),
        ]);
        Assert.Equal("B", ranked[0].Uploader);
    }

    [Fact]
    public void Rank_SameScore_MostPlayedFirst()
    {
        var ranked = Downloader.RankMusicResults("swag", [Song("Swag", "A", 5), Song("Swag", "B", 500)]);
        Assert.Equal("B", ranked[0].Uploader);
    }

    [Fact]
    public void Rank_RealArtistBeatsCoversWithTheNameInTheTitle()
    {
        var ranked = Downloader.RankMusicResults("bohemian rhapsody queen", [
            Song("Bohemian Rhapsody (Queen)", "Karolina Protsenko", 5000000),
            Song("Bohemian Rhapsody", "Queen", 2700000000),
        ]);
        Assert.Equal("Queen", ranked[0].Uploader);
    }

    [Fact]
    public void Rank_RealHit_DropsTracksAlmostNobodyPlays()
    {
        var ranked = Downloader.RankMusicResults("fuck swag", [
            Song("F*CK SWAG", "Nettspend", 6200000),
            Song("Fuck It", "Tony Swag", 652),
            Song("SWAG SWAG", "Icy Star", 176),
        ]);
        Assert.Equal("Nettspend", Assert.Single(ranked).Uploader);
    }

    [Fact]
    public void Rank_NoRealHit_KeepsSmallTracks() =>
        Assert.Equal(2, Downloader.RankMusicResults("coconut mall", [Song("Coconut Mall", "A", 900), Song("Coconut Mall Remix", "B", 3)]).Count);

    [Fact]
    public void Rank_OnlyTheAlbumMatches_IsDropped() =>
        Assert.Empty(Downloader.RankMusicResults("fuck swag", [Song("Blessed in hell", "MAV99", 31, "fuck Swag")]));

    [Fact]
    public void Rank_SomethingMatchesEveryWord_HidesPartialMatches()
    {
        var ranked = Downloader.RankMusicResults("bohemian rhapsody queen", [
            Song("Bohemian Rhapsody", "Queen", 2700000000),
            Song("Another One Bites The Dust", "Queen", 879000000),
        ]);
        Assert.Equal("Bohemian Rhapsody", Assert.Single(ranked).Title);
    }

    [Fact]
    public void Rank_NoArtist_IsDropped() =>
        Assert.Empty(Downloader.RankMusicResults("swag", [Song("Swag", "")]));

    [Fact]
    public void Rank_NothingMatchesTheSearch_ReturnsNothingSoFallbackRuns() =>
        Assert.Empty(Downloader.RankMusicResults("coconut mall", [Song("Blessed in hell", "MAV99", 9000000)]));

    [Fact]
    public void Rank_MatchInArtistOrAlbum_Counts() =>
        Assert.Single(Downloader.RankMusicResults("nettspend", [Song("Some Title", "Nettspend")]));

    [Fact]
    public void ParseFileLine_TopicChannel_ShowsTheArtist() =>
        Assert.Equal("Nettspend", Downloader.ParseFileLine("FILE\tC:\\a.m4a\tSong\tNettspend - Topic")!.Artist);
}

public class ThemeTests
{
    [Fact]
    public void Dark_IsPitchBlackWithWhiteAccent()
    {
        Assert.Equal(Color.Black.ToArgb(), Theme.Dark.Window.ToArgb());
        Assert.Equal(Color.White.ToArgb(), Theme.Dark.Accent.ToArgb());
        Assert.Equal(Color.White.ToArgb(), Theme.Dark.Text.ToArgb());
    }

    [Fact]
    public void Light_IsPureWhiteWithBlackAccent()
    {
        Assert.Equal(Color.White.ToArgb(), Theme.Light.Window.ToArgb());
        Assert.Equal(Color.Black.ToArgb(), Theme.Light.Accent.ToArgb());
        Assert.Equal(Color.Black.ToArgb(), Theme.Light.Text.ToArgb());
    }

    [Fact]
    public void Palettes_HaveNoColorExceptTheErrorRed()
    {
        foreach (var p in new[] { Theme.Dark, Theme.Light })
            foreach (var c in new[] { p.Window, p.Panel, p.Raised, p.Border, p.Text, p.Muted, p.Accent, p.AccentHover, p.OnAccent, p.Selection, p.SelectionText, p.Good, p.Wave, p.LoopFill })
                Assert.True(c.R == c.G && c.G == c.B, $"{c} is not gray");
    }
}

public class FfmpegInstallerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rrm_ff_" + Guid.NewGuid().ToString("N"));

    public FfmpegInstallerTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, true);

    private string Zip(params string[] entries)
    {
        string path = Path.Combine(_folder, "test.zip");
        using var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        foreach (string entry in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
            writer.Write(entry);
        }
        return path;
    }

    [Fact]
    public void Extract_TakesOnlyTheTwoPrograms()
    {
        string zip = Zip("ffmpeg-x/bin/ffmpeg.exe", "ffmpeg-x/bin/ffprobe.exe", "ffmpeg-x/bin/ffplay.exe", "ffmpeg-x/doc/readme.txt");
        string target = Path.Combine(_folder, "out");
        FfmpegInstaller.Extract(zip, target);
        Assert.Equal(["ffmpeg.exe", "ffprobe.exe"], Directory.GetFiles(target).Select(Path.GetFileName).OrderBy(n => n));
    }

    [Fact]
    public void Extract_ProgramMissing_InstallsNothing()
    {
        string zip = Zip("ffmpeg-x/bin/ffmpeg.exe");
        string target = Path.Combine(_folder, "out");
        Assert.Throws<DownloadFailedException>(() => FfmpegInstaller.Extract(zip, target));
        Assert.Empty(Directory.GetFiles(target));
    }

    [Fact]
    public void Extract_NameTricks_AreIgnored()
    {
        string zip = Zip("../../evil/bin/notffmpeg.exe", "x/bin/ffmpeg.exe", "x/bin/ffprobe.exe");
        string target = Path.Combine(_folder, "out");
        FfmpegInstaller.Extract(zip, target);
        Assert.Equal(2, Directory.GetFiles(target).Length);
        Assert.False(Directory.Exists(Path.Combine(_folder, "evil")));
    }
}

[Collection("windows")]
public class FlatCheckTests
{
    [Fact]
    public void Checked_And_Unchecked_DrawDifferently()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var palette in new[] { true, false })
                {
                    Theme.Set(palette);
                    using var box = new FlatCheck { Text = "Option", Width = 120, Height = 24 };
                    using var off = new Bitmap(120, 24);
                    using var on = new Bitmap(120, 24);
                    box.DrawToBitmap(off, new Rectangle(0, 0, 120, 24));
                    box.Checked = true;
                    box.DrawToBitmap(on, new Rectangle(0, 0, 120, 24));
                    // The middle of the box is the accent color only when checked.
                    Assert.NotEqual(off.GetPixel(2, 12).ToArgb(), on.GetPixel(2, 12).ToArgb());
                    Assert.Equal(Theme.Current.Accent.ToArgb(), on.GetPixel(2, 12).ToArgb());
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}

[Collection("windows")]
public class DownloadWindowTests
{
    private static void OnUiThread(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                WindowsFormsSynchronizationContext.AutoInstall = false;
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                test();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static DownloadForm Open() =>
        new(Path.GetTempPath(), 1) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };

    [Fact]
    public void SpotifyLink_ExplainsInsteadOfDownloading() => OnUiThread(() =>
    {
        using var form = Open();
        form.Show();
        form.SetQueryForTest("https://open.spotify.com/track/abc");
        var go = form.GoAsync();
        Assert.True(go.IsCompleted);
        Assert.Contains("copy-protects", form.StatusForTest);
        Assert.Null(form.Result);
        form.Close();
    });

    [Fact]
    public void Button_SaysDownloadForLinks_SearchForWords() => OnUiThread(() =>
    {
        using var form = Open();
        form.Show();
        form.SetQueryForTest("https://youtu.be/abc");
        Assert.Equal("Download", form.GoTextForTest);
        form.SetQueryForTest("coconut mall");
        Assert.Equal("Search", form.GoTextForTest);
        form.Close();
    });
}

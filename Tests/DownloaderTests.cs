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
        Assert.Equal("ytsearch15:--exec calc", args[^1]);
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
    [Fact]
    public void Merge_KeepsEverything_WithOfficialFirst_AndEachVideoOnce()
    {
        var official = new SearchResult("https://music.youtube.com/watch?v=aaa", "Coconut Mall", "Nintendo", "3:21", Plays: 500);
        var unrelated = new SearchResult("https://music.youtube.com/watch?v=bbb", "Something Else", "Someone", "2:00");
        var video = new SearchResult("https://music.youtube.com/watch?v=ccc", "Coconut Mall video", "Someone", "3:00");
        var again = new SearchResult("https://www.youtube.com/watch?v=aaa", "Coconut Mall upload", "Uploader", "3:21");
        var upload = new SearchResult("https://www.youtube.com/watch?v=ddd", "coconut mall 1 hour", "Fan", "1:00:00");

        var merged = Downloader.MergeResults("coconut mall", [unrelated, official], [video], [again, upload]);

        Assert.Equal(["aaa", "bbb", "ccc", "ddd"], merged.Select(result => result.VideoId));
        Assert.True(merged[0].Official);
        Assert.All(merged.Skip(1), result => Assert.False(result.Official));
    }

    [Fact]
    public void Merge_NothingOfficial_StillShowsTheRest()
    {
        var upload = new SearchResult("https://www.youtube.com/watch?v=ddd", "unreleased leak", "Fan", "2:00");
        var merged = Downloader.MergeResults("zzz", [], [], [upload]);
        Assert.False(Assert.Single(merged).Official);
    }

    [Fact]
    public void Display_MarksOfficialSongs()
    {
        var song = new SearchResult("https://x/watch?v=a", "Title", "Artist", "1:00");
        Assert.DoesNotContain("Official", song.Display);
        Assert.StartsWith("Official", (song with { Official = true }).Display);
    }

    [Fact]
    public void DownloadArguments_BestQualityByDefault_AndACapWhenAsked()
    {
        var best = Downloader.DownloadArguments("https://youtu.be/abc", @"C:\songs", @"C:\ffmpeg");
        Assert.Equal("bestaudio/best", best[best.IndexOf("-f") + 1]);

        var capped = Downloader.DownloadArguments("https://youtu.be/abc", @"C:\songs", @"C:\ffmpeg", null, 128);
        Assert.Equal("bestaudio[abr<=128]/bestaudio/best", capped[capped.IndexOf("-f") + 1]);
        Assert.Equal("--", capped[^2]);
        Assert.Equal("https://youtu.be/abc", capped[^1]);
    }

    [Fact]
    public void QualityChoices_StartWithBest_AndGoDown()
    {
        Assert.Equal(("Best", 0), Downloader.QualityChoices[0]);
        Assert.Equal(Downloader.QualityChoices.Skip(1).Select(choice => choice.Kbps).OrderByDescending(kbps => kbps), Downloader.QualityChoices.Skip(1).Select(choice => choice.Kbps));
        Assert.Equal(2, Downloader.QualityIndex(192));
        Assert.Equal(0, Downloader.QualityIndex(999));
    }

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
    // WCAG contrast between two colors, from 1 to 21.
    private static double Contrast(Color a, Color b)
    {
        static double Channel(int value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    [Fact]
    public void Text_IsReadableOnEverySurface()
    {
        var p = Theme.Dark;
        foreach (var surface in new[] { p.Window, p.Panel, p.Raised, p.Hover })
        {
            Assert.True(Contrast(p.Text, surface) >= 4.5, $"text on {surface}");
            Assert.True(Contrast(p.Muted, surface) >= 4.5, $"muted text on {surface}");
        }
    }

    [Fact]
    public void Buttons_AndSelections_AreReadable()
    {
        var p = Theme.Dark;
        Assert.True(Contrast(p.OnAccent, p.Accent) >= 4.5, "check box tick");
        Assert.True(Contrast(p.SelectionText, p.Selection) >= 4.5, "selected row");
        Assert.True(Contrast(p.Text, p.Hover) >= 4.5, "hovered button");
        Assert.True(Contrast(p.Accent, p.Panel) >= 4.5, "status text");
        Assert.True(Contrast(p.Bad, p.Panel) >= 4.5, "failed status text");
    }

    [Fact]
    public void SelectionBar_AndOutlines_StandOut()
    {
        // Shapes that carry meaning need 3:1 against what's around them.
        var p = Theme.Dark;
        Assert.True(Contrast(p.Accent, p.Selection) >= 3, "selection bar");
        Assert.True(Contrast(Color.White, p.Raised) >= 3, "main button outline");
        Assert.True(Contrast(p.Text, p.Panel) >= 3, "focus ring");
    }

    [Fact]
    public void ThereIsOnlyTheDarkTheme()
    {
        Assert.True(Theme.Dark.IsDark);
        Assert.Null(typeof(Theme).GetField("Light"));
    }

    [Theory]
    [InlineData("#8A5CFF")]
    [InlineData("#FFFF00")]
    [InlineData("#102040")]
    [InlineData("#FFFFFF")]
    [InlineData("#000000")]
    public void CustomAccent_KeepsTheTickReadable_AndTheRestOfTheTheme(string hex)
    {
        var color = Theme.ParseColor(hex)!.Value;
        var p = Theme.WithAccent(color);
        Assert.Equal(color.ToArgb(), p.Accent.ToArgb());
        Assert.True(Contrast(p.OnAccent, p.Accent) >= 4.5, $"tick on {hex}");
        Assert.Equal(Theme.Dark.Window, p.Window);
        Assert.Equal(Theme.Dark.Text, p.Text);
    }

    [Theory]
    [InlineData("#e8963e", "#E8963E")]
    [InlineData("e8963e", "#E8963E")]
    [InlineData(" #00FF7F ", "#00FF7F")]
    public void AccentColor_ReadsHex(string text, string expected) => Assert.Equal(expected, Theme.ToHex(Theme.ParseColor(text)!.Value));

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void AccentColor_AnythingElse_MeansTheStandardAmber(string text) => Assert.Null(Theme.ParseColor(text));

    [Fact]
    public void AppSizes_IncludeNormal() => Assert.Contains(100, Theme.ScaleChoices);

    [Theory]
    [InlineData("#000000", true)]
    [InlineData("#14213D", true)]
    [InlineData("#808080", false)]
    [InlineData("#F2EEE6", false)]
    [InlineData("#FFFFFF", false)]
    [InlineData("#FF69B4", false)]
    public void BackgroundColor_KeepsTextReadable_AndPicksLightOrDarkText(string hex, bool dark)
    {
        var back = Theme.ParseColor(hex)!.Value;
        var p = Theme.WithBackground(back);
        Assert.Equal(dark, p.IsDark);
        Assert.Equal(back.ToArgb(), p.Window.ToArgb());
        Assert.True(Contrast(p.Text, p.Window) >= 4.5, $"text on {hex}");
        // Fields and buttons step away from the background, so a mid grey leaves them a little less clear.
        Assert.True(Contrast(p.Text, p.Raised) >= (hex == "#808080" ? 3.5 : 4.5), $"text on raised {hex}");
        Assert.True(Contrast(p.SelectionText, p.Selection) >= 4.5, $"selected row on {hex}");
        Assert.True(Contrast(p.Outline, p.Raised) >= 3, $"button outline on {hex}");
    }

    [Fact]
    public void BackgroundColor_AndAccent_GoTogether()
    {
        Theme.SetLook("#8A5CFF", "", "#F2EEE6");
        try
        {
            Assert.Equal(Color.FromArgb(0xF2, 0xEE, 0xE6).ToArgb(), Theme.Current.Window.ToArgb());
            Assert.Equal(Color.FromArgb(0x8A, 0x5C, 0xFF).ToArgb(), Theme.Current.Accent.ToArgb());
        }
        finally
        {
            Theme.SetLook("", "");
        }
    }

    [Fact]
    public void Fonts_AreTheWindowsOnes()
    {
        using var body = Theme.UiFont();
        using var heading = Theme.TitleFont();
        Assert.StartsWith("Segoe UI", body.FontFamily.Name);
        Assert.StartsWith("Segoe UI", heading.FontFamily.Name);
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
    public void Extract_SharedBuild_KeepsDllsBesideThePrograms()
    {
        string zip = Zip("x/bin/ffmpeg.exe", "x/bin/ffprobe.exe", "x/bin/ffplay.exe", "x/bin/avcodec-63.dll", "x/lib/avcodec.dll", "x/doc/a.dll");
        string target = Path.Combine(_folder, "out");
        FfmpegInstaller.Extract(zip, target);
        Assert.Equal(["avcodec-63.dll", "ffmpeg.exe", "ffprobe.exe"], Directory.GetFiles(target).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
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
        string zip = Zip("../../evil/bin/evil.dll", "../../evil/bin/notffmpeg.exe", "x/bin/ffmpeg.exe", "x/bin/ffprobe.exe");
        string target = Path.Combine(_folder, "out");
        FfmpegInstaller.Extract(zip, target);
        Assert.Equal(["evil.dll", "ffmpeg.exe", "ffprobe.exe"], Directory.GetFiles(target).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
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
                {
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
    public void ExportWindow_MadeFor_WiiByDefault_AndDolphinWhenChosen() => OnUiThread(() =>
    {
        using var form = new ExportForm(true) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();
        Assert.True(form.ForWii);
        form.ChooseTargetForTest(false);
        Assert.False(form.ForWii);
        form.Close();
        using var remembered = new ExportForm(true, forWii: false) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        Assert.False(remembered.ForWii);
    });

    [Fact]
    public void DownloadWindow_HasNoPreview_ButAQualityChoice() => OnUiThread(() =>
    {
        using var form = Open();
        form.Show();
        Application.DoEvents();
        Assert.False(form.HasPreviewForTest);
        Assert.Equal(Downloader.QualityChoices.Length, form.QualityForTest.Items.Count);
        Assert.Equal("Best", form.QualityForTest.Items[0]);
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

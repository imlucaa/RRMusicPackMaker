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

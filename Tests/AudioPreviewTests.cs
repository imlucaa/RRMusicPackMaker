using System.Text;
using RRMusicPackMaker;
using Xunit;

namespace RRMusicPackMaker.Tests;

/// <summary>These play silence through the real audio device, so they make no sound. They need a PC with one.</summary>
[Collection("windows")]
public sealed class AudioPreviewTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "rrm_preview_" + Guid.NewGuid().ToString("N"));

    public AudioPreviewTests()
    {
        Directory.CreateDirectory(_data);
        Environment.SetEnvironmentVariable("RRMUSIC_DATA", _data);
        Environment.SetEnvironmentVariable("RRMUSIC_MODS", Path.Combine(_data, "ww", "Mods"));
        Environment.SetEnvironmentVariable("RRMUSIC_RR", Path.Combine(_data, "rr"));
    }

    public void Dispose()
    {
        foreach (string name in new[] { "RRMUSIC_DATA", "RRMUSIC_MODS", "RRMUSIC_RR" })
            Environment.SetEnvironmentVariable(name, null);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(_data, true);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(100);
            }
        }
    }

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
            finally
            {
                AudioPreview.Stop();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private string Silence(double seconds, string name = "")
    {
        const int rate = 44100;
        string path = Path.Combine(_data, $"silence{name}{Guid.NewGuid():N}.wav");
        int bytes = (int)(seconds * rate) * 4;
        using var w = new BinaryWriter(File.Create(path));
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + bytes);
        w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);
        w.Write(rate);
        w.Write(rate * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(bytes);
        w.Write(new byte[bytes]);
        return path;
    }

    private static void Pump(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(clock.ElapsedMilliseconds < 30000, "timed out");
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static void Wait(int milliseconds)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Pump(() => clock.ElapsedMilliseconds >= milliseconds);
    }

    [Fact]
    public void PauseHoldsThePlace_ResumeCarriesOn_StopCleansUp() => OnUiThread(() =>
    {
        string file = Silence(20);
        var owner = new object();
        Assert.True(AudioPreview.Play(file, owner));
        Assert.Equal(PreviewState.Playing, AudioPreview.State);
        Assert.True(AudioPreview.IsActive(owner));

        Pump(() => AudioPreview.PositionMilliseconds > 200);
        AudioPreview.TogglePause();
        Assert.Equal(PreviewState.Paused, AudioPreview.State);
        Assert.True(AudioPreview.IsActive(owner));
        Wait(100);
        long held = AudioPreview.PositionMilliseconds;
        Assert.True(held > 200);
        Wait(400);
        Assert.Equal(held, AudioPreview.PositionMilliseconds);

        AudioPreview.TogglePause();
        Assert.Equal(PreviewState.Playing, AudioPreview.State);
        Pump(() => AudioPreview.PositionMilliseconds > held + 150);

        AudioPreview.Stop();
        Assert.Equal(PreviewState.Stopped, AudioPreview.State);
        Assert.False(AudioPreview.IsActive(owner));
        Assert.False(File.Exists(file));
    });

    [Fact]
    public void WhilePaused_ItDoesntCountAsFinished() => OnUiThread(() =>
    {
        Assert.True(AudioPreview.Play(Silence(5), new object()));
        AudioPreview.TogglePause();
        Wait(400);
        Assert.Equal(PreviewState.Paused, AudioPreview.State);
    });

    [Fact]
    public void FinishingByItself_StopsAndCleansUp() => OnUiThread(() =>
    {
        string file = Silence(0.3);
        int changes = 0;
        void Count() => changes++;
        AudioPreview.Changed += Count;
        try
        {
            Assert.True(AudioPreview.Play(file, new object()));
            Pump(() => AudioPreview.State == PreviewState.Stopped);
            Assert.False(File.Exists(file));
            Assert.Equal(2, changes);
        }
        finally
        {
            AudioPreview.Changed -= Count;
        }
    });

    [Fact]
    public void PlayingSomethingNew_StopsTheLast() => OnUiThread(() =>
    {
        string first = Silence(20), second = Silence(20);
        object a = new(), b = new();
        Assert.True(AudioPreview.Play(first, a));
        Assert.True(AudioPreview.Play(second, b));
        Assert.False(AudioPreview.IsActive(a));
        Assert.True(AudioPreview.IsActive(b));
        Assert.False(File.Exists(first));
        AudioPreview.StopFor(a);
        Assert.Equal(PreviewState.Playing, AudioPreview.State);
        AudioPreview.StopFor(b);
        Assert.Equal(PreviewState.Stopped, AudioPreview.State);
    });

    [Fact]
    public void BadFile_ReportsFailure_AndDoesntThrow() => OnUiThread(() =>
    {
        string bad = Path.Combine(_data, "bad.wav");
        File.WriteAllText(bad, "not audio");
        Assert.False(AudioPreview.Play(bad, new object()));
        Assert.Equal(PreviewState.Stopped, AudioPreview.State);
        Assert.False(File.Exists(bad));
    });

    [Fact]
    public void Nothing_Playing_PauseAndStopDoNothing() => OnUiThread(() =>
    {
        AudioPreview.TogglePause();
        AudioPreview.Stop();
        Assert.Equal(PreviewState.Stopped, AudioPreview.State);
    });

    [Fact]
    public void MainWindow_HasNoPauseOrStopInTheBottomBar() => OnUiThread(() =>
    {
        using var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();
        Assert.True(form.HasFooterButtonForTest("Export pack"));
        Assert.True(form.PlayPreviewForTest(Silence(20)));
        Application.DoEvents();
        Assert.False(form.HasFooterButtonForTest("Pause"));
        Assert.False(form.HasFooterButtonForTest("Stop"));
        AudioPreview.Stop();
        form.Close();
    });

    [Fact]
    public void MainWindow_SpacePauses_AndEscStops_WhenTheListHasFocus() => OnUiThread(() =>
    {
        using var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();
        form.FocusGridForTest();
        Assert.True(form.PlayPreviewForTest(Silence(20)));

        Assert.True(form.HandleShortcut(Keys.Space));
        Assert.Equal(PreviewState.Paused, AudioPreview.State);
        Assert.True(form.HandleShortcut(Keys.Space));
        Assert.Equal(PreviewState.Playing, AudioPreview.State);
        Assert.True(form.HandleShortcut(Keys.Escape));
        Assert.Equal(PreviewState.Stopped, AudioPreview.State);
        form.Close();
    });

    [Fact]
    public void MainWindow_HearButton_TurnsIntoPause_WhileItsPlaying() => OnUiThread(() =>
    {
        using var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();
        Assert.Contains("Hear current", form.HearCurrentTextForTest);

        form.SetPlayingHearCurrentForTest();
        Assert.True(form.PlayPreviewForTest(Silence(20)));
        Assert.Contains("Pause", form.HearCurrentTextForTest);
        AudioPreview.TogglePause();
        Assert.Contains("Resume", form.HearCurrentTextForTest);
        AudioPreview.Stop();
        Assert.Contains("Hear current", form.HearCurrentTextForTest);
        form.Close();
    });

    [Fact]
    public void SongsPage_PlayTurnsIntoPause_AndTheSongPlaysFromTheList() => OnUiThread(() =>
    {
        string folder = Path.Combine(_data, "packs", "Downloaded songs");
        Directory.CreateDirectory(folder);
        string song = Path.Combine(folder, "quiet.wav");
        File.Move(Silence(20), song);
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        using var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        form.ShowSongs();
        Application.DoEvents();
        var songs = form.SongsForTest;
        songs.SelectForTest(song);
        Assert.Contains("Play", songs.PlayTextForTest);

        var playing = songs.PlayOrPauseAsync();
        Pump(() => playing.IsCompleted);
        Assert.Equal(PreviewState.Playing, AudioPreview.State);
        Assert.Contains("Pause", songs.PlayTextForTest);

        var pausing = songs.PlayOrPauseAsync();
        Pump(() => pausing.IsCompleted);
        Assert.Equal(PreviewState.Paused, AudioPreview.State);
        Assert.Contains("Resume", songs.PlayTextForTest);

        Assert.True(File.Exists(song));
        AudioPreview.Stop();
        Assert.Contains("Play", songs.PlayTextForTest);
        Assert.True(File.Exists(song));
        form.Close();
    });

    [Fact]
    public void TrackWithASong_ButtonPlaysTheSong_AndChangingSpeedStartsItAgain() => OnUiThread(() =>
    {
        using var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();
        form.HandleDrop([Silence(10)], 0);
        form.SelectVisible(0);
        Application.DoEvents();
        Assert.Contains("Hear song", form.HearCurrentTextForTest);

        var hearing = form.HearSongAsync();
        Pump(() => hearing.IsCompleted);
        Assert.Equal(PreviewState.Playing, AudioPreview.State);
        Assert.Contains("Pause", form.HearCurrentTextForTest);
        Pump(() => AudioPreview.PositionMilliseconds > 300);

        // A new speed starts the song again from the top, so the change can be heard.
        form.SpeedComboForTest.SelectedIndex = PackBuilder.SpeedIndex(0.8, false, true);
        Pump(() => AudioPreview.State == PreviewState.Playing && AudioPreview.PositionMilliseconds < 300 && form.SelectedRows()[0].Reverb);
        Assert.Contains("Pause", form.HearCurrentTextForTest);

        // Another track doesn't offer to pause this one.
        form.SelectVisible(1);
        Application.DoEvents();
        Assert.Contains("Hear current", form.HearCurrentTextForTest);
        form.SelectVisible(0);
        Application.DoEvents();
        Assert.Contains("Pause", form.HearCurrentTextForTest);

        AudioPreview.Stop();
        Assert.Contains("Hear song", form.HearCurrentTextForTest);
        form.Close();
    });

    [Fact]
    public void ImportedPackSongs_CanBeHeard_OnTheSongsTabAndTheirTrack() => OnUiThread(() =>
    {
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        using var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();

        // A real pack: an encoded .brstm named after a track.
        var maker = new TrackRow { Name = "x", FileBase = form.Rows[0].FileBase, Source = Silence(5), FixedRate = 32000 };
        var made = PackBuilder.Build(new Options { PackName = "Shared" }, [maker], Path.Combine(_data, "made"), _ => { });
        var importing = form.ImportPackAsync(made.Folder, replaceAssigned: true);
        Pump(() => importing.IsCompleted);
        var row = form.Rows.Single(r => r.IsAssigned);
        Assert.EndsWith(".brstm", row.Source);

        // On the Songs tab.
        form.ShowSongs();
        Application.DoEvents();
        var songs = form.SongsForTest;
        songs.SelectForTest(row.Source);
        var playing = songs.PlayOrPauseAsync();
        Pump(() => playing.IsCompleted);
        Assert.Equal(PreviewState.Playing, AudioPreview.State);
        AudioPreview.Stop();

        // On its track.
        form.ShowTab(editor: false);
        form.SelectVisible(0);
        Application.DoEvents();
        var hearing = form.HearSongAsync();
        Pump(() => hearing.IsCompleted);
        Assert.Equal(PreviewState.Playing, AudioPreview.State);
        AudioPreview.Stop();
        form.Close();
    });

    [Fact]
    public void Editor_SpacePauses_EscStops_AndThePlayheadStopsWithIt() => OnUiThread(() =>
    {
        using var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();
        var opening = form.ShowTab(editor: true);
        Pump(() => opening.IsCompleted);
        var editor = form.EditorForTest;
        var adding = editor.AddSongAsync(Silence(20));
        Pump(() => adding.IsCompleted);

        var playing = editor.PlayAsync();
        Pump(() => playing.IsCompleted);
        Assert.True(editor.IsPlaying);
        Pump(() => editor.TimelineForTest.PlayPosition > 0);

        Assert.True(form.HandleShortcut(Keys.Space));
        Assert.True(editor.IsPaused);
        Assert.True(form.HandleShortcut(Keys.Space));
        Assert.False(editor.IsPaused);

        Assert.True(form.HandleShortcut(Keys.Escape));
        Assert.False(editor.IsPlaying);
        Assert.Equal(-1, editor.TimelineForTest.PlayPosition);
        form.Close();
    });
}

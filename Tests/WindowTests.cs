using RRMusicPackMaker;
using Xunit;

namespace RRMusicPackMaker.Tests;

/// <summary>Drives the real windows off screen. They run one at a time because the theme and data folder are shared.</summary>
[Collection("windows")]
public class WindowTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "rrm_ui_" + Guid.NewGuid().ToString("N"));

    public WindowTests()
    {
        Directory.CreateDirectory(_data);
        Environment.SetEnvironmentVariable("RRMUSIC_DATA", _data);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("RRMUSIC_DATA", null);
        Directory.Delete(_data, true);
    }

    private static void OnUiThread(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // The app runs its windows under this; without it, background results would return on the wrong thread.
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

    private static MainForm OpenWindow()
    {
        var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();
        return form;
    }

    private string WriteSong(int seconds = 6)
    {
        string path = Path.Combine(_data, "song.wav");
        const int rate = 32000;
        int samples = seconds * rate;
        using var w = new BinaryWriter(File.Create(path));
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + samples * 4);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);
        w.Write(rate);
        w.Write(rate * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(samples * 4);
        for (int i = 0; i < samples; i++)
        {
            short value = (short)(Math.Sin(i * 0.05) * 9000);
            w.Write(value);
            w.Write(value);
        }
        return path;
    }

    private static void Pump(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(clock.ElapsedMilliseconds < 60000, "timed out");
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    [Fact]
    public void Theme_TogglesBothWays_AndRemembersChoice() => OnUiThread(() =>
    {
        using (var form = OpenWindow())
        {
            bool start = Theme.Current.IsDark;
            form.ToggleTheme();
            Assert.NotEqual(start, Theme.Current.IsDark);
            Application.DoEvents();
            form.ToggleTheme();
            Assert.Equal(start, Theme.Current.IsDark);
            form.ToggleTheme();
            form.Close();
        }
        bool saved = Theme.Current.IsDark;
        Theme.Set(!saved);
        using var again = OpenWindow();
        Assert.Equal(saved, Theme.Current.IsDark);
        again.Close();
    });

    [Fact]
    public void Drop_OneSongOnATrack_SetsOnlyThatTrack() => OnUiThread(() =>
    {
        string song = WriteSong();
        using var form = OpenWindow();
        form.HandleDrop([song], 0);
        Application.DoEvents();
        Assert.Single(form.Rows, r => r.IsAssigned);
        Assert.Equal("song", form.Rows.First(r => r.IsAssigned).Song);
        form.Close();
    });

    [Fact]
    public void Drop_OnSelectedRows_FillsAllSelected() => OnUiThread(() =>
    {
        string song = WriteSong();
        using var form = OpenWindow();
        form.SelectVisible(0, 1, 2);
        Application.DoEvents();
        Assert.True(form.ManyPanelVisible);
        Assert.Equal(3, form.SelectedRows().Count);
        form.HandleDrop([song], 1);
        Assert.Equal(3, form.Rows.Count(r => r.IsAssigned));
        form.Close();
    });

    [Fact]
    public void Autosave_SongsComeBackAfterRestart() => OnUiThread(() =>
    {
        string song = WriteSong();
        string file;
        using (var form = OpenWindow())
        {
            form.HandleDrop([song], 3);
            file = form.Rows.First(r => r.IsAssigned).FileBase;
            form.Close();
        }
        using var again = OpenWindow();
        var row = again.Rows.Single(r => r.IsAssigned);
        Assert.Equal(file, row.FileBase);
        Assert.Equal(song, row.Source);
        again.Close();
    });

    [Fact]
    public void LoopEditor_LoadsSong_AndSavesMarkers() => OnUiThread(() =>
    {
        var row = new TrackRow { Name = "Test", FileBase = "x_N", Source = WriteSong() };
        using var editor = new LoopEditorForm(row) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        editor.Show();
        Pump(() => editor.MarkerEnd > 1);
        Assert.Equal(6 * 32000, editor.MarkerEnd);

        editor.SetMarkersForTest(32000, 64000);
        Assert.Equal(32000, editor.MarkerStart);
        editor.AcceptForTest();

        Assert.Equal(LoopMode.Custom, row.Loop);
        Assert.Equal(32000, AudioIO.ParsePosition(row.LoopStart, 32000));
        Assert.Equal(64000, AudioIO.ParsePosition(row.LoopEnd, 32000));
    });

    [Fact]
    public void Window_ClosedWhileSongIsLoading_DoesNotCrash() => OnUiThread(() =>
    {
        string song = WriteSong();
        var form = OpenWindow();
        form.HandleDrop([song], 0);
        form.SelectVisible(0);
        form.Close();
        form.Dispose();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 1500)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    });

    [Fact]
    public void LoopEditor_MissingSong_DoesNotCrash() => OnUiThread(() =>
    {
        var row = new TrackRow { Name = "Test", FileBase = "x_N", Source = Path.Combine(_data, "gone.mp3") };
        using var editor = new LoopEditorForm(row) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        var load = editor.LoadAudioAsync();
        Pump(() => load.IsCompleted);
        Assert.True(load.IsCompletedSuccessfully);
        editor.Close();
    });

    [Fact]
    public void Build_FromWindow_ProducesFile() => OnUiThread(() =>
    {
        string song = WriteSong();
        string output = Path.Combine(_data, "packs");
        AppStorage.SaveSettings(new AppSettings { Theme = "Light", OutputFolder = output });
        using var form = OpenWindow();
        form.HandleDrop([song], 0);
        string file = form.Rows.First(r => r.IsAssigned).FileBase;
        var build = form.BuildPackAsync();
        Pump(() => build.IsCompleted);
        Assert.True(build.IsCompletedSuccessfully);
        Assert.Single(Directory.GetFiles(output, file + ".brstm", SearchOption.AllDirectories));
        form.Close();
    });
}

[CollectionDefinition("windows", DisableParallelization = true)]
public class WindowCollection;

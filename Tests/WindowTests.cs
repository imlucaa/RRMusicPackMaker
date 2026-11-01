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
        // Never touch the real install or Wheel Wizard from a test.
        Environment.SetEnvironmentVariable("RRMUSIC_MODS", Path.Combine(_data, "ww", "Mods"));
        Environment.SetEnvironmentVariable("RRMUSIC_RR", Path.Combine(_data, "rr"));
        Directory.CreateDirectory(Path.Combine(_data, "rr"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("RRMUSIC_DATA", null);
        Environment.SetEnvironmentVariable("RRMUSIC_MODS", null);
        Environment.SetEnvironmentVariable("RRMUSIC_RR", null);
        // A background song read from the window can hold a file for a moment after it closes.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(_data, true);
                return;
            }
            catch (IOException) when (attempt < 40)
            {
                Thread.Sleep(250);
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
    public void Sidebar_SectionTitles_CannotBeSelected() => OnUiThread(() =>
    {
        using var form = OpenWindow();
        form.SelectCategory(Catalog.CatMenu);
        var before = form.SidebarSelectionForTest;
        for (int i = 0; i < form.SidebarCountForTest; i++)
        {
            form.PickSidebarIndexForTest(i);
            Assert.False(((SideList.Item)form.SidebarSelectionForTest!).IsHeader);
        }
        form.SelectCategory(Catalog.CatMenu);
        Assert.Equal(before, form.SidebarSelectionForTest);
        form.Close();
    });

    [Fact]
    public void Sidebar_Menus_ShowsOnlyMenus() => OnUiThread(() =>
    {
        using var form = OpenWindow();
        form.SelectCategory(Catalog.CatMenu);
        Assert.Equal(form.Rows.Count(r => r.Category == Catalog.CatMenu), form.VisibleCount);
        form.Close();
    });

    [Fact]
    public void Thumbnail_AndStatusPills_DrawWithoutErrors_InBothThemes() => OnUiThread(() =>
    {
        string song = WriteSong();
        using var form = OpenWindow();
        form.HandleDrop([song], 0);
        form.ToggleTheme();
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
        form.ToggleTheme();
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
        form.Close();
    });

    [Fact]
    public void MissingSong_IsMarked_AndClearsWhenRestored() => OnUiThread(() =>
    {
        string song = WriteSong();
        using var form = OpenWindow();
        form.HandleDrop([song], 0);
        var row = form.Rows.First(r => r.IsAssigned);
        Assert.Equal("Ready", form.StatusOfForTest(row));

        string moved = song + ".moved";
        File.Move(song, moved);
        form.RunChecksForTest();
        Assert.Equal("Missing", form.StatusOfForTest(row));

        File.Move(moved, song);
        form.RunChecksForTest();
        Assert.Equal("Ready", form.StatusOfForTest(row));
        form.Close();
    });

    [Fact]
    public void BulkSettings_ApplyToEverySelectedTrack() => OnUiThread(() =>
    {
        using var form = OpenWindow();
        form.SelectVisible(0, 1, 2);
        Application.DoEvents();
        form.ApplyToSelected(r => r.VolumeDb = 6);
        var rows = form.SelectedRows();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(6, r.VolumeDb));
        Assert.Equal(3, form.Rows.Count(r => r.VolumeDb == 6));
        form.Close();
    });

    [Fact]
    public void DeleteKey_RemovesSong_OnlyWhenTheListHasFocus() => OnUiThread(() =>
    {
        string song = WriteSong();
        using var form = OpenWindow();
        form.HandleDrop([song], 0);
        form.SelectVisible(0);
        Application.DoEvents();

        form.HandleShortcut(Keys.Control | Keys.F);
        Assert.False(form.HandleShortcut(Keys.Delete));
        Assert.Single(form.Rows, r => r.IsAssigned);

        form.FocusGridForTest();
        Assert.True(form.HandleShortcut(Keys.Delete));
        Assert.DoesNotContain(form.Rows, r => r.IsAssigned);
        form.Close();
    });

    [Fact]
    public void CurrentMusic_PrefersTheInstalledPack() => OnUiThread(() =>
    {
        string rr = Path.Combine(_data, "rr");
        Directory.CreateDirectory(Path.Combine(rr, "strm"));
        Directory.CreateDirectory(Path.Combine(rr, "Patches"));
        File.WriteAllText(Path.Combine(rr, "strm", "title_bg.brstm"), "x");
        using var form = OpenWindow();
        var row = form.Rows.First(r => r.FileBase == "title_bg");
        Assert.EndsWith(Path.Combine("strm", "title_bg.brstm"), form.CurrentMusicFile(row));
        File.WriteAllText(Path.Combine(rr, "Patches", "title_bg.brstm"), "x");
        Assert.EndsWith(Path.Combine("Patches", "title_bg.brstm"), form.CurrentMusicFile(row));
        Assert.Null(form.CurrentMusicFile(form.Rows.First(r => r.FileBase == "offline_bg")));
        form.Close();
    });

    [Fact]
    public void SizeEstimate_ShowsInTheStatus() => OnUiThread(() =>
    {
        string song = WriteSong();
        using var form = OpenWindow();
        form.HandleDrop([song], 0);
        var size = form.UpdateSizeForTest();
        Pump(() => size.IsCompleted);
        Assert.Contains("MB", form.StatusTextForTest);
        form.Close();
    });

    [Fact]
    public void BuildAndAdd_PutsSongInModAndPatches() => OnUiThread(() =>
    {
        string song = WriteSong();
        AppStorage.SaveSettings(new AppSettings { Theme = "Dark", OutputFolder = Path.Combine(_data, "packs") });
        using var form = OpenWindow();
        form.HandleDrop([song], 0);
        string file = form.Rows.First(r => r.IsAssigned).FileBase + ".brstm";

        var build = form.BuildAndAddAsync();
        Pump(() => build.IsCompleted);

        Assert.True(build.IsCompletedSuccessfully, build.Exception?.ToString());
        Assert.True(File.Exists(Path.Combine(_data, "rr", "Patches", file)));
        Assert.True(File.Exists(Path.Combine(_data, "ww", "Mods", "My Music Pack", file)));
        Assert.True(File.Exists(Path.Combine(_data, "ww", "Mods", "My Music Pack", "My Music Pack.ini")));

        // One click also exports a complete pack someone with a real Wii can use.
        Assert.Equal("Export pack and add to Patches", form.ActionButtonTextForTest);
        string export = form.ExportedZipForTest!;
        Assert.True(File.Exists(export));
        using (var zip = System.IO.Compression.ZipFile.OpenRead(export))
        {
            var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
            Assert.Contains($"RetroRewind6/Patches/{file}", names);
            Assert.Contains("README.txt", names);
            Assert.Contains("credits.csv", names);
            Assert.DoesNotContain(names, name => name.EndsWith(".brsar", StringComparison.OrdinalIgnoreCase));
            using var reader = new StreamReader(zip.GetEntry("README.txt")!.Open());
            string readme = reader.ReadToEnd();
            Assert.Contains("SD:/RetroRewind6/Patches", readme);
            Assert.Contains("Wheel Wizard", readme);
        }
        form.Close();
    });

    [Fact]
    public void BuildAndAdd_SongMissing_InstallsNothing() => OnUiThread(() =>
    {
        string song = WriteSong();
        AppStorage.SaveSettings(new AppSettings { Theme = "Dark", OutputFolder = Path.Combine(_data, "packs") });
        using var form = OpenWindow();
        form.HandleDrop([song], 0);
        File.Delete(song);

        var build = form.BuildAndAddAsync();
        Pump(() => build.IsCompleted);

        Assert.False(Directory.Exists(Path.Combine(_data, "ww", "Mods", "My Music Pack")));
        Assert.False(Directory.Exists(Path.Combine(_data, "rr", "Patches")));
        form.Close();
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

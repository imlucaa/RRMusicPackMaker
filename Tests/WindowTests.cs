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
    public void Look_AccentAndBackground_ShowStraightAway_AndPlainBlackComesBack() => OnUiThread(() =>
    {
        string picture = Path.Combine(_data, "picture.png");
        using (var bitmap = new Bitmap(64, 32))
        {
            using (var g = Graphics.FromImage(bitmap))
                g.Clear(Color.SteelBlue);
            bitmap.Save(picture, System.Drawing.Imaging.ImageFormat.Png);
        }
        try
        {
            using var form = OpenWindow();
            Assert.False(form.HasBackdropForTest);
            form.SettingsForTest.AccentColor = "#8A5CFF";
            form.SettingsForTest.BackgroundImage = picture;
            form.ApplyLook();
            Assert.True(form.HasBackdropForTest);
            Assert.Equal(Color.FromArgb(0x8A, 0x5C, 0xFF).ToArgb(), Theme.Current.Accent.ToArgb());

            // The window still draws over the picture, and the file isn't locked.
            using var shot = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(shot, new Rectangle(0, 0, form.Width, form.Height));
            File.Delete(picture);

            form.SettingsForTest.AccentColor = "";
            form.SettingsForTest.BackgroundImage = "";
            form.ApplyLook();
            Assert.False(form.HasBackdropForTest);
            Assert.Same(Theme.Dark, Theme.Current);
            form.Close();
        }
        finally
        {
            Theme.SetLook("", "");
        }
    });

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var inner in AllControls(child))
                yield return inner;
        }
    }

    [Fact]
    public void BigAppSize_SettingsFitsTheScreen_AndButtonsMatchTheirFields() => OnUiThread(() =>
    {
        Theme.Configure(new AppSettings { UiScale = 200 });
        try
        {
            using var settings = new SettingsForm(new Options(), new AppSettings());
            settings.Show();
            Application.DoEvents();
            var area = Screen.FromControl(settings).WorkingArea;
            Assert.True(settings.Height <= area.Height, $"{settings.Height} tall on a {area.Height} screen");
            Assert.Contains(AllControls(settings), control => control is Panel { AutoScroll: true });

            // A Browse button is as tall as the folder box beside it, not scaled twice.
            var field = AllControls(settings).OfType<FieldBox>().First();
            var browse = AllControls(settings).OfType<ModernButton>().First(button => button.Text == "Browse");
            Assert.InRange(browse.Height, field.Height - 2, field.Height + 2);
            settings.Close();

            using var form = OpenWindow();
            var search = AllControls(form).OfType<FieldBox>().First(box => box.Parent is TableLayoutPanel { Dock: DockStyle.Top });
            Assert.True(search.Bottom <= search.Parent!.ClientSize.Height, "the search box fits its row");
            form.Close();
        }
        finally
        {
            Theme.Configure(new AppSettings());
        }
    });

    [Fact]
    public void Look_BadPicture_StaysPlainBlack() => OnUiThread(() =>
    {
        string picture = Path.Combine(_data, "broken.png");
        File.WriteAllText(picture, "not a picture");
        try
        {
            using var form = OpenWindow();
            form.SettingsForTest.BackgroundImage = picture;
            form.ApplyLook();
            Assert.False(form.HasBackdropForTest);
            form.Close();
        }
        finally
        {
            Theme.SetLook("", "");
        }
    });

    [Fact]
    public void Sidebar_HidesTheStockMusicCategories() => OnUiThread(() =>
    {
        using var form = OpenWindow();
        var names = new List<string>();
        for (int i = 0; i < form.SidebarCountForTest; i++)
        {
            form.PickSidebarIndexForTest(i);
            if (form.SidebarSelectionForTest is SideList.Item item)
                names.Add(item.Name);
        }
        Assert.Contains(Catalog.CatMenu, names);
        Assert.DoesNotContain(Catalog.CatStock, names);
        Assert.DoesNotContain(Catalog.CatStockBattle, names);
        form.Close();
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
    public void Thumbnail_AndStatus_DrawWithoutErrors() => OnUiThread(() =>
    {
        string song = WriteSong();
        using var form = OpenWindow();
        form.HandleDrop([song], 0);
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
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
    public void ManyTracks_VolumeNudges_AddAndTakeAwayADecibel() => OnUiThread(() =>
    {
        using var form = OpenWindow();
        form.SelectVisible(0, 1, 2);
        Application.DoEvents();
        var rows = form.SelectedRows();
        rows[0].VolumeDb = 3;
        rows[1].VolumeDb = 12;
        rows[2].VolumeDb = -12;
        form.NudgeVolumeForTest(1);
        Assert.Equal([4, 12, -11], rows.Select(r => r.VolumeDb).ToArray());
        form.NudgeVolumeForTest(-1);
        form.NudgeVolumeForTest(-1);
        Assert.Equal([2, 10, -12], rows.Select(r => r.VolumeDb).ToArray());

        // One track shows the new volume by name, even between the old 3 dB steps.
        form.SelectVisible(0);
        Application.DoEvents();
        Assert.Equal("+2 dB", form.VolumeComboForTest.Text);
        form.Close();
    });

    [Fact]
    public void FinalLapSong_HasVolumeAndSpeed_OnlyOnceOneIsPicked() => OnUiThread(() =>
    {
        string song = WriteSong(3);
        using var form = OpenWindow();
        form.HandleDrop([song], 0);
        form.SelectVisible(0);
        Application.DoEvents();
        var row = form.SelectedRows().Single();
        Assert.False(form.FinalSongSettingsVisibleForTest);

        row.FinalSource = song;
        form.RefreshDetailsForTest();
        Assert.True(form.FinalSongSettingsVisibleForTest);
        form.FinalVolumeComboForTest.SelectedIndex = 1 + PackBuilder.VolumeIndex(-3);
        form.FinalSongSpeedComboForTest.SelectedIndex = 1 + PackBuilder.SpeedIndex(1.25, false);
        Assert.Equal(-3, row.FinalVolumeDb);
        Assert.Equal(1.25, row.FinalSongSpeed);

        form.FinalVolumeComboForTest.SelectedIndex = 0;
        form.FinalSongSpeedComboForTest.SelectedIndex = 0;
        Assert.Null(row.FinalVolumeDb);
        Assert.Equal(0, row.FinalSongSpeed);
        form.Close();
    });

    [Fact]
    public void Editor_Fades_ShapeTheSavedSong_AndAreRemembered() => OnUiThread(() =>
    {
        string song = WriteSong(6);
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        using (var form = OpenWindow())
        {
            var opening = form.ShowTab(editor: true);
            Pump(() => opening.IsCompleted);
            var editor = form.EditorForTest;
            var adding = editor.AddSongAsync(song);
            Pump(() => adding.IsCompleted);
            int twoSeconds = Array.FindIndex(EditorPanel.FadeChoices, choice => choice.Seconds == 2);
            editor.SetFadesForTest(twoSeconds, twoSeconds);

            var saving = editor.SaveAsync();
            Pump(() => saving.IsCompleted);
            var saved = AudioIO.Decode(saving.Result!, 44100)[0];
            Assert.True(Math.Abs((int)saved[10]) < 200, "starts silent");
            Assert.True(Math.Abs((int)saved[^10]) < 200, "ends silent");
            form.Close();
        }
        Assert.Equal(EditorState.Load().FadeIn, Array.FindIndex(EditorPanel.FadeChoices, choice => choice.Seconds == 2));
        Assert.Equal(EditorState.Load().FadeOut, Array.FindIndex(EditorPanel.FadeChoices, choice => choice.Seconds == 2));
    });

    [Fact]
    public void Editor_DeleteSong_ImportedFile_CanDeleteTheFileToo() => OnUiThread(() =>
    {
        var (form, editor, _, removed) = EditorWithDownload();
        using (form)
        {
            string mine = WriteSong(2);
            var adding = editor.AddSongAsync(mine);
            Pump(() => adding.IsCompleted);
            Assert.False(editor.IsDownloadedSong(mine));

            Assert.True(editor.DeleteSong(mine, deleteFile: true));

            Assert.Contains(mine, removed);
            Assert.DoesNotContain(mine, editor.SongPaths);
            form.Close();
        }
    });

    [Fact]
    public void SpeedChoices_SetOneTrackOrMany_AndShowWhatsSet() => OnUiThread(() =>
    {
        using var form = OpenWindow();
        int nightcore = PackBuilder.SpeedIndex(1.25, keepPitch: false);
        int faster = PackBuilder.SpeedIndex(1.5, keepPitch: true);

        form.SelectVisible(0);
        Application.DoEvents();
        var first = form.SelectedRows().Single();
        form.SpeedComboForTest.SelectedIndex = nightcore;
        Assert.Equal(1.25, first.Speed);
        Assert.False(first.KeepPitch);

        form.SelectVisible(0, 1, 2);
        Application.DoEvents();
        Assert.Equal(0, form.ManySpeedComboForTest.SelectedIndex);
        form.ManySpeedComboForTest.SelectedIndex = faster + 1;
        Assert.All(form.SelectedRows(), r =>
        {
            Assert.Equal(1.5, r.Speed);
            Assert.True(r.KeepPitch);
        });

        form.SelectVisible(1);
        Application.DoEvents();
        Assert.Equal(faster, form.SpeedComboForTest.SelectedIndex);
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

    private (MainForm Form, string File) WindowWithSong()
    {
        string song = WriteSong();
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        var form = OpenWindow();
        form.HandleDrop([song], 0);
        return (form, form.Rows.First(r => r.IsAssigned).FileBase + ".brstm");
    }

    [Fact]
    public void AddToGame_WithoutAModsList_PutsSongsInStrm() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            // Wheel Wizard's Mods folder isn't used, but the Retro Rewind folder is set.
            AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs"), UseWheelWizard = false });
            Environment.SetEnvironmentVariable("RRMUSIC_MODS", null);
            var add = form.AddToPatchesAsync();
            Pump(() => add.IsCompleted);
            Assert.True(add.IsCompletedSuccessfully, add.Exception?.ToString());
            Assert.True(File.Exists(Path.Combine(_data, "rr", "strm", file)));
            form.Close();
        }
    });

    [Fact]
    public void ImportPack_FromTheGamesStrmFolder_OnlyBringsInTheSongsYouAdded() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            string strm = Path.Combine(_data, "rr", "strm");
            Directory.CreateDirectory(strm);
            Directory.CreateDirectory(Path.Combine(_data, "rr", "Binaries"));
            foreach (string stock in new[] { "99_N.brstm", "98_N.brstm", "97_N.brstm" })
                File.WriteAllText(Path.Combine(strm, stock), "the game's own");

            // Nothing of yours is in there yet.
            var importing = form.ImportPackAsync(strm, replaceAssigned: true);
            Pump(() => importing.IsCompleted);
            Assert.Contains("nothing of yours", form.StatusTextForTest);
            Assert.DoesNotContain("99_N", form.SongPickForTest.Items.Cast<object>().Select(item => item.ToString()));

            var add = form.AddToPatchesAsync();
            Pump(() => add.IsCompleted);
            importing = form.ImportPackAsync(strm, replaceAssigned: true);
            Pump(() => importing.IsCompleted);
            var items = form.SongPickForTest.Items.Cast<object>().Select(item => item.ToString()).ToList();
            Assert.DoesNotContain("99_N", items);
            Assert.DoesNotContain("97_N", items);
            form.Close();
        }
    });

    [Fact]
    public void RemoveFromGame_ShowsOnceAdded_AndPutsRetroRewindsOwnSongBack() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            string strm = Path.Combine(_data, "rr", "strm");
            Directory.CreateDirectory(strm);
            File.WriteAllText(Path.Combine(strm, file), "retro rewind's own");
            Assert.False(form.RemoveFromGameVisibleForTest);

            var add = form.AddToPatchesAsync();
            Pump(() => add.IsCompleted);
            Assert.True(form.RemoveFromGameVisibleForTest);
            Assert.NotEqual("retro rewind's own", File.ReadAllText(Path.Combine(strm, file)));
            form.ConfirmRemoveForTest = true;
            form.ClickRemoveFromGameForTest();

            Assert.Equal("retro rewind's own", File.ReadAllText(Path.Combine(strm, file)));
            Assert.False(form.RemoveFromGameVisibleForTest);
            form.Close();
        }
    });

    [Fact]
    public void HearFinalLap_PlaysTheSpedUpSong_AndTurnsIntoPause() => OnUiThread(() =>
    {
        string song = WriteSong(3);
        using var form = OpenWindow();
        form.HandleDrop([song], 0);
        form.SelectVisible(0);
        Application.DoEvents();
        Assert.Contains("final lap", form.HearFinalTextForTest);

        var hearing = form.HearFinalAsync();
        Pump(() => hearing.IsCompleted);
        Assert.Equal(PreviewState.Playing, AudioPreview.State);
        Assert.Contains("Pause", form.HearFinalTextForTest);
        AudioPreview.Stop();
        Assert.Contains("final lap", form.HearFinalTextForTest);
        form.Close();
    });

    [Fact]
    public void AddToStrm_PutsSongInStrmAndMod() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            var add = form.AddToPatchesAsync();
            Pump(() => add.IsCompleted);

            Assert.True(add.IsCompletedSuccessfully, add.Exception?.ToString());
            Assert.DoesNotContain("Add to strm", form.FooterButtonsForTest);
            Assert.True(File.Exists(Path.Combine(_data, "rr", "strm", file)));
            Assert.False(File.Exists(Path.Combine(_data, "rr", "Patches", file)));
            Assert.True(File.Exists(Path.Combine(_data, "ww", "Mods", "My Music Pack", file)));
            Assert.True(File.Exists(Path.Combine(_data, "ww", "Mods", "My Music Pack", "My Music Pack.ini")));
            Assert.False(form.ShowPackButtonVisibleForTest);
            form.Close();
        }
    });

    [Fact]
    public void ExportToFolder_MakesACompleteFolder_WithoutTouchingTheGame() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            string destination = Path.Combine(_data, "my usb");
            var export = form.ExportToFolderAsync(destination);
            Pump(() => export.IsCompleted);

            Assert.True(export.IsCompletedSuccessfully, export.Exception?.ToString());
            Assert.Equal("Export pack", form.ExportButtonTextForTest);
            Assert.True(form.ShowPackButtonVisibleForTest);
            Assert.False(Directory.Exists(Path.Combine(_data, "rr", "Patches")));
            Assert.False(Directory.Exists(Path.Combine(_data, "ww", "Mods", "My Music Pack")));

            string target = form.ExportedFolderForTest!;
            Assert.Equal(Path.Combine(destination, "My Music Pack"), target);
            Assert.True(File.Exists(Path.Combine(target, "RetroRewind6", "Patches", file)));
            Assert.True(File.Exists(Path.Combine(target, "credits.csv")));
            Assert.Empty(Directory.GetFiles(target, "*.brsar", SearchOption.AllDirectories));
            string readme = File.ReadAllText(Path.Combine(target, "README.txt"));
            Assert.Contains("SD:/RetroRewind6/Patches", readme);
            Assert.Contains("Wheel Wizard", readme);
            form.Close();
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExportWindow_AddToGame_FollowsWhetherTheFolderIsSet(bool found) => OnUiThread(() =>
    {
        using var form = new ExportForm(found) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();
        Assert.Equal(found, form.GameEnabledForTest);
        Assert.Equal(ExportChoice.None, form.Choice);
        form.Close();
    });

    /// <summary>A real pack made by the app, so an import can be followed all the way back to a new build.</summary>
    private string BuildRealPack(MainForm form, TrackRow row)
    {
        row.Loop = LoopMode.Custom;
        row.LoopStart = "0:01";
        row.LoopEnd = "0:04";
        row.Song = "My Song";
        row.Credits = "Me, Myself";
        var built = PackBuilder.Build(new Options { PackName = "Shared" }, [row], Path.Combine(_data, "made"), _ => { });
        return built.Folder;
    }

    [Fact]
    public void ImportPack_RealPack_ComesBackWithItsLoopAndNames_AndRebuilds() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            var row = form.Rows.First(r => r.IsAssigned);
            string pack = BuildRealPack(form, row);
            row.Source = "";
            row.Song = "";
            row.Credits = "";
            row.Loop = LoopMode.WholeSong;

            var importing = form.ImportPackAsync(pack, replaceAssigned: true);
            Pump(() => importing.IsCompleted);
            Assert.True(importing.IsCompletedSuccessfully, importing.Exception?.ToString());

            Assert.Equal(Path.Combine(_data, "packs", "Imported packs", "Shared", file), row.Source);
            Assert.True(File.Exists(row.Source));
            Assert.Equal("My Song", row.Song);
            Assert.Equal("Me, Myself", row.Credits);
            Assert.Equal(LoopMode.Auto, row.Loop);
            // It's already on its track, so the dropdown only shows it for that track. The Songs tab still has it.
            Assert.Equal($"My Song  ({file.Replace(".brstm", "")})", form.SongPickForTest.Text);
            Assert.Contains(row.Source, form.SongsForTest.AllSongsForTest, StringComparer.OrdinalIgnoreCase);

            var rebuilt = PackBuilder.Build(new Options { PackName = "Again" }, [row], Path.Combine(_data, "again"), _ => { });
            Assert.Equal(0, rebuilt.Failed);
            var summary = AudioIO.ReadBrstmSummary(Path.Combine(rebuilt.MusicFolder, file));
            Assert.True(summary.Loop);
            Assert.InRange(summary.End - summary.Start, 3 * 32000 - 200, 3 * 32000 + 200);
            form.Close();
        }
    });

    [Fact]
    public void ImportPack_FromTheGamesOwnFolder_SurvivesInstallingOverIt() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            var add = form.AddToPatchesAsync();
            Pump(() => add.IsCompleted);
            string patches = Path.Combine(_data, "rr", "strm");
            var row = form.Rows.First(r => r.IsAssigned);

            var importing = form.ImportPackAsync(patches, replaceAssigned: true);
            Pump(() => importing.IsCompleted);
            Assert.StartsWith(Path.Combine(_data, "packs", "Imported packs"), row.Source);

            // The song isn't read from the folder it's about to be written to.
            add = form.AddToPatchesAsync();
            Pump(() => add.IsCompleted);
            Assert.True(add.IsCompletedSuccessfully, add.Exception?.ToString());
            Assert.True(File.Exists(Path.Combine(patches, file)));
            form.Close();
        }
    });

    [Fact]
    public void ImportPack_StockNames_ShowUnderSongsISet_AndRebuildWithTheirFinalLapFiles() => OnUiThread(() =>
    {
        string song = WriteSong();
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        using var form = OpenWindow();

        // A pack spelled the way a shared one is: lower case, with its own final lap file.
        var maker = new TrackRow { Name = "x", FileBase = "n_farm_n", Source = song };
        var made = PackBuilder.Build(new Options { PackName = "Shared" }, [maker], Path.Combine(_data, "made"), _ => { });
        File.Copy(Path.Combine(made.MusicFolder, "n_farm_n.brstm"), Path.Combine(made.MusicFolder, "n_farm_F.brstm"));

        var importing = form.ImportPackAsync(made.Folder, replaceAssigned: true);
        Pump(() => importing.IsCompleted);
        Assert.True(importing.IsCompletedSuccessfully, importing.Exception?.ToString());

        var row = Assert.Single(form.Rows, r => r.IsAssigned);
        Assert.Equal("n_Farm_n", row.FileBase);
        Assert.EndsWith("n_farm_F.brstm", row.FinalSource);
        Assert.Equal("Songs I set", form.SidebarSelectionForTest?.ToString());
        Assert.Equal(1, form.VisibleCount);

        // Only the song itself joins the song list, not its final lap copy.
        Assert.DoesNotContain("n_farm_F", form.SongPickForTest.Items.Cast<object>().Select(item => item.ToString()));

        var rebuilt = PackBuilder.Build(new Options { PackName = "Again" }, [row], Path.Combine(_data, "again"), _ => { });
        Assert.Equal(0, rebuilt.Failed);
        Assert.True(File.Exists(Path.Combine(rebuilt.MusicFolder, "n_Farm_n.brstm")));
        Assert.True(File.Exists(Path.Combine(rebuilt.MusicFolder, "n_Farm_f.brstm")));
        form.Close();
    });

    private string ImportedPackWithASong(MainForm form, string file)
    {
        string pack = Path.Combine(_data, "shared pack");
        Directory.CreateDirectory(pack);
        File.Copy(form.Rows.First(r => r.IsAssigned).Source, Path.Combine(pack, file));
        var importing = form.ImportPackAsync(pack, replaceAssigned: true);
        Pump(() => importing.IsCompleted);
        return Path.Combine(_data, "packs", "Imported packs", "shared pack");
    }

    [Fact]
    public void DeletePackAsync_DeletesOffTheWindowThread_ThenClearsTheTracks() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            string imported = ImportedPackWithASong(form, file);
            var row = form.Rows.First(r => r.IsAssigned);
            int uiThread = Environment.CurrentManagedThreadId;
            int deletedOn = uiThread;
            form.RemoveFolder = folder =>
            {
                deletedOn = Environment.CurrentManagedThreadId;
                Directory.Delete(folder, true);
            };

            var deleting = form.DeletePackAsync(new InstalledPack("Imported: shared pack", imported, 1, true));
            Pump(() => deleting.IsCompleted);

            Assert.True(deleting.Result);
            Assert.NotEqual(uiThread, deletedOn);
            Assert.False(Directory.Exists(imported));
            Assert.False(row.IsAssigned);
            form.Close();
        }
    });

    [Fact]
    public void DeleteOnlyWindow_HasNoImport_AndStillDeletes() => OnUiThread(() =>
    {
        var packs = new List<InstalledPack> { new("Imported: Big", "C:\b", 372, true) };
        var asked = new List<string>();
        using var form = new ImportPackForm(packs, pack =>
        {
            asked.Add(pack.Name);
            return Task.FromResult(true);
        }, deleteOnly: true) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();
        Assert.False(form.ImportVisibleForTest);
        Assert.True(form.DeleteVisibleForTest);
        form.DeleteForTest();
        Assert.Equal(["Imported: Big"], asked);
        Assert.Equal(0, form.PackCountForTest);
        form.Close();
    });

    [Fact]
    public void DeletePack_ClearsTracksListAndTimeline_AndKeepsYourOtherSongs() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            string original = form.Rows.First(r => r.IsAssigned).Source;
            string imported = ImportedPackWithASong(form, file);
            var row = form.Rows.First(r => r.IsAssigned);
            string copy = row.Source;
            Assert.StartsWith(imported, copy);

            var opening = form.ShowTab(editor: true);
            Pump(() => opening.IsCompleted);
            var adding = form.EditorForTest.AddSongAsync(copy);
            Pump(() => adding.IsCompleted);

            var removed = new List<string>();
            form.RemoveFolder = folder =>
            {
                removed.Add(folder);
                Directory.Delete(folder, true);
            };
            Assert.True(form.DeletePack(new InstalledPack("Imported: shared pack", imported, 1, true)));

            Assert.Equal([imported], removed);
            Assert.False(Directory.Exists(imported));
            Assert.False(row.IsAssigned);
            Assert.DoesNotContain(copy, form.EditorForTest.SongPaths);
            Assert.Empty(form.EditorForTest.ClipsForTest);
            Assert.True(File.Exists(original));
            form.Close();
        }
    });

    [Fact]
    public void DeletePack_ModWithAKart_LosesOnlyItsSongs() => OnUiThread(() =>
    {
        var (form, _) = WindowWithSong();
        using (form)
        {
            string mod = Path.Combine(_data, "ww", "Mods", "Mixed");
            Directory.CreateDirectory(mod);
            File.WriteAllText(Path.Combine(mod, "a_N.brstm"), "x");
            File.WriteAllText(Path.Combine(mod, "la_bike-fk.szs"), "kart");
            File.WriteAllText(Path.Combine(mod, "Mixed.ini"), "[Mod]");
            var recycled = new List<string>();
            form.EditorForTest.RemoveFile = path =>
            {
                recycled.Add(Path.GetFileName(path));
                File.Delete(path);
            };
            form.RemoveFolder = _ => throw new InvalidOperationException("the mod has a kart, so the folder must stay");

            Assert.True(form.DeletePack(new InstalledPack("Mod", mod, 1, IsMod: true)));

            Assert.Equal(["a_N.brstm"], recycled);
            Assert.True(File.Exists(Path.Combine(mod, "la_bike-fk.szs")));
            Assert.True(File.Exists(Path.Combine(mod, "Mixed.ini")));
            form.Close();
        }
    });

    [Fact]
    public void DeletePack_ThePatchesFolder_KeepsOtherFilesAndTheFolder() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            var add = form.AddToPatchesAsync();
            Pump(() => add.IsCompleted);
            string patches = Path.Combine(_data, "rr", "Patches");
            Directory.CreateDirectory(patches);

            // Wheel Wizard puts a mod's songs in Patches when the game starts.
            File.Copy(Path.Combine(_data, "rr", "strm", file), Path.Combine(patches, file), true);
            File.WriteAllText(Path.Combine(patches, "la_bike-fk.szs"), "kart");
            form.EditorForTest.RemoveFile = File.Delete;
            form.RemoveFolder = _ => throw new InvalidOperationException("Patches must never go");

            Assert.True(form.DeletePack(new InstalledPack("Patches", patches, 1)));

            Assert.False(File.Exists(Path.Combine(patches, file)));
            Assert.True(File.Exists(Path.Combine(patches, "la_bike-fk.szs")));
            form.Close();
        }
    });

    [Fact]
    public void DeletePack_StuckPartWay_ClearsWhatWentAndKeepsTheRest() => OnUiThread(() =>
    {
        var (form, _) = WindowWithSong();
        using (form)
        {
            string mod = Path.Combine(_data, "ww", "Mods", "Stuck");
            Directory.CreateDirectory(mod);
            foreach (string name in new[] { "a_N.brstm", "b_N.brstm", "kart.szs" })
                File.WriteAllText(Path.Combine(mod, name), "x");
            int calls = 0;
            form.EditorForTest.RemoveFile = path =>
            {
                if (++calls == 2)
                    throw new IOException("in use");
                File.Delete(path);
            };

            Assert.False(form.DeletePack(new InstalledPack("Mod", mod, 2, IsMod: true)));

            Assert.Single(Directory.GetFiles(mod, "*.brstm"));
            Assert.Contains("in use", form.LogTextForTest);
            form.Close();
        }
    });

    [Fact]
    public void DeletePack_WhenItFails_ChangesNothing() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            string imported = ImportedPackWithASong(form, file);
            var row = form.Rows.First(r => r.IsAssigned);
            form.RemoveFolder = _ => throw new IOException("in use");

            Assert.False(form.DeletePack(new InstalledPack("Imported: shared pack", imported, 1, true)));

            Assert.True(row.IsAssigned);
            Assert.True(Directory.Exists(imported));
            Assert.Contains("in use", form.LogTextForTest);
            form.Close();
        }
    });

    [Fact]
    public void ImportPack_AnEarlierImport_ComesBackWithoutANewCopy() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            string imported = ImportedPackWithASong(form, file);
            var importing = form.ImportPackAsync(imported, replaceAssigned: true);
            Pump(() => importing.IsCompleted);
            Assert.True(importing.IsCompletedSuccessfully, importing.Exception?.ToString());
            Assert.StartsWith(imported, form.Rows.First(r => r.IsAssigned).Source);
            Assert.Single(Directory.GetDirectories(Path.Combine(_data, "packs", "Imported packs")));
            form.Close();
        }
    });

    [Fact]
    public void ImportFiles_NamedAfterATrack_GoOnIt_AndOthersJustJoinTheList() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            string other = form.Rows.First(r => !r.IsAssigned).FileBase;
            string named = Path.Combine(_data, other + ".wav");
            string loose = Path.Combine(_data, "Just A Song.wav");
            File.Copy(form.Rows.First(r => r.IsAssigned).Source, named);
            File.Copy(named, loose);

            form.ImportFiles([named, loose, Path.Combine(_data, "notes.txt")], replaceAssigned: true);

            Assert.Equal(named, form.Rows.Single(r => r.FileBase == other).Source);
            var listed = form.SongPickForTest.Items.Cast<object>().Select(item => item.ToString()).ToList();
            Assert.Contains("Just A Song", listed);
            Assert.DoesNotContain("notes", listed);
            Assert.True(File.Exists(named));
            form.Close();
        }
    });

    [Fact]
    public void ImportWindow_EveryPackCanBeDeleted_AndLeavesTheList() => OnUiThread(() =>
    {
        var packs = new List<InstalledPack> { new("Patches", "C:\\a", 1), new("Mod", "C:\\m", 1, IsMod: true), new("Imported: Theirs", "C:\\b", 2, true) };
        var asked = new List<string>();
        using var form = new ImportPackForm(packs, pack =>
        {
            asked.Add(pack.Name);
            return true;
        }) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();

        foreach (int index in new[] { 0, 1, 2 })
        {
            form.SelectPackForTest(index);
            Assert.True(form.DeleteVisibleForTest);
        }

        form.DeleteForTest();
        Assert.Equal(["Imported: Theirs"], asked);
        Assert.Equal(2, form.PackCountForTest);
        Assert.True(form.DeleteVisibleForTest);
        Assert.True(form.ImportEnabledForTest);

        form.DeleteForTest();
        form.DeleteForTest();
        Assert.Equal(0, form.PackCountForTest);
        Assert.False(form.DeleteVisibleForTest);
        Assert.False(form.ImportEnabledForTest);
        form.Close();
    });

    [Fact]
    public void ExportToZip_HasTheSamePack_WithoutTouchingTheGame() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            string zip = Path.Combine(_data, "my zips", "pack.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
            var export = form.ExportToZipAsync(zip);
            Pump(() => export.IsCompleted);
            Assert.True(export.IsCompletedSuccessfully, export.Exception?.ToString());

            using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
            {
                var names = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToList();
                Assert.Contains($"RetroRewind6/Patches/{file}", names);
                Assert.Contains("README.txt", names);
                Assert.DoesNotContain(names, name => name.EndsWith(".brsar"));
            }
            Assert.False(Directory.Exists(Path.Combine(_data, "rr", "Patches")));
            Assert.True(form.ShowPackButtonVisibleForTest);

            // A zip made here can be imported again.
            var importing = form.ImportPackAsync(zip, replaceAssigned: true);
            Pump(() => importing.IsCompleted);
            Assert.True(importing.IsCompletedSuccessfully, importing.Exception?.ToString());
            Assert.Contains("Imported packs", form.Rows.First(r => r.IsAssigned).Source);
            form.Close();
        }
    });

    [Fact]
    public void ImportPack_EmptyFolder_SaysSo() => OnUiThread(() =>
    {
        var (form, _) = WindowWithSong();
        using (form)
        {
            string pack = Path.Combine(_data, "nothing");
            Directory.CreateDirectory(pack);
            var importing = form.ImportPackAsync(pack);
            Pump(() => importing.IsCompleted);
            Assert.Contains("no .brstm", form.StatusTextForTest);
            form.Close();
        }
    });

    [Fact]
    public void ImportPack_ZeroTypo_GoesOnTheFanfare_AndRebuildsWithTheRightName() => OnUiThread(() =>
    {
        string song = WriteSong();
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        using var form = OpenWindow();
        var maker = new TrackRow { Name = "x", FileBase = "0_FanfareTA1st_32", Source = song, FixedRate = 32000 };
        var made = PackBuilder.Build(new Options { PackName = "Shared" }, [maker], Path.Combine(_data, "made"), _ => { });

        var importing = form.ImportPackAsync(made.MusicFolder, replaceAssigned: true);
        Pump(() => importing.IsCompleted);

        var row = Assert.Single(form.Rows, r => r.IsAssigned);
        Assert.Equal("o_FanfareTA1st_32", row.FileBase);
        Assert.Contains("typo", form.LogTextForTest);
        Assert.Equal("Songs I set", form.SidebarSelectionForTest?.ToString());
        Assert.Equal(1, form.VisibleCount);

        var rebuilt = PackBuilder.Build(new Options { PackName = "Again" }, [row], Path.Combine(_data, "again"), _ => { });
        Assert.Equal(0, rebuilt.Failed);
        Assert.True(File.Exists(Path.Combine(rebuilt.MusicFolder, "o_FanfareTA1st_32.brstm")));
        form.Close();
    });

    [Fact]
    public void ImportPack_KeepingCurrentSongs_LeavesThemAlone() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            var row = form.Rows.First(r => r.IsAssigned);
            string mine = row.Source;
            string pack = Path.Combine(_data, "other");
            Directory.CreateDirectory(pack);
            File.Copy(mine, Path.Combine(pack, file));

            var importing = form.ImportPackAsync(pack, replaceAssigned: false);
            Pump(() => importing.IsCompleted);
            Assert.Equal(mine, row.Source);
            form.Close();
        }
    });

    [Fact]
    public void ImportPack_NothingMatches_SongsStillComeIn_AndTheSongsTabShowsThem() => OnUiThread(() =>
    {
        var (form, _) = WindowWithSong();
        using (form)
        {
            string pack = Path.Combine(_data, "other");
            Directory.CreateDirectory(pack);
            File.WriteAllText(Path.Combine(pack, "mystery.brstm"), "x");
            int before = form.Rows.Count(r => r.IsAssigned);

            var importing = form.ImportPackAsync(pack);
            Pump(() => importing.IsCompleted);

            Assert.Contains("match", form.StatusTextForTest);
            Assert.Contains("mystery", form.LogTextForTest);
            Assert.Equal(before, form.Rows.Count(r => r.IsAssigned));
            Assert.True(form.SongsForTest.Visible);
            Assert.Contains(form.SongsForTest.VisibleForTest, song => song.EndsWith("mystery.brstm"));
            form.Close();
        }
    });

    [Fact]
    public void ImportWindow_ShowsPacks_OrSaysThereAreNone() => OnUiThread(() =>
    {
        using (var form = new ImportPackForm([new InstalledPack("Music", "C:\\x", 3)]) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) })
        {
            form.Show();
            Application.DoEvents();
            Assert.Null(form.Chosen);
            form.Close();
        }
        using var none = new ImportPackForm([]) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        none.Show();
        Application.DoEvents();
        Assert.Null(none.Chosen);
        none.Close();
    });

    [Fact]
    public void Log_IsAlwaysShown_AndThereIsNoActivityButton() => OnUiThread(() =>
    {
        using var form = OpenWindow();
        Assert.True(form.LogVisibleForTest);
        Assert.DoesNotContain("Activity", form.FooterButtonsForTest);
        form.Close();
    });

    [Fact]
    public void Editor_Speed_ChangesTheSavedSong_AndIsRemembered() => OnUiThread(() =>
    {
        string song = WriteSong(6);
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        using (var form = OpenWindow())
        {
            var opening = form.ShowTab(editor: true);
            Pump(() => opening.IsCompleted);
            var editor = form.EditorForTest;
            var adding = editor.AddSongAsync(song);
            Pump(() => adding.IsCompleted);

            int nightcore = PackBuilder.SpeedIndex(1.25, false);
            Assert.Equal(1.25, PackBuilder.SpeedChoices[nightcore].Speed);
            editor.SetSpeedForTest(nightcore);
            var playing = editor.PlayAsync();
            Pump(() => playing.IsCompleted);
            Assert.Equal(PreviewState.Playing, AudioPreview.State);
            AudioPreview.Stop();

            var saving = editor.SaveAsync();
            Pump(() => saving.IsCompleted);
            int samples = AudioIO.Decode(saving.Result!, 44100)[0].Length;
            Assert.InRange(samples, 6 * 44100 / 1.25 - 500, 6 * 44100 / 1.25 + 500);
            form.Close();
        }

        Assert.Equal(PackBuilder.SpeedIndex(1.25, false), EditorState.Load().Speed);
    });

    [Fact]
    public void Editor_SplitDeleteJoinAndSave_MakesASongThatCanBeUsed() => OnUiThread(() =>
    {
        string song = WriteSong(6);
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        using var form = OpenWindow();
        var opening = form.ShowTab(editor: true);
        Pump(() => opening.IsCompleted);
        var editor = form.EditorForTest;

        var adding = editor.AddSongAsync(song);
        Pump(() => adding.IsCompleted);
        long whole = editor.ClipsForTest.Single().Length;
        Assert.InRange(whole, 6 * 44100 - 50, 6 * 44100 + 50);

        // Cut out the middle two seconds, then put the song on again after it.
        editor.SetPositionForTest(2 * 44100);
        editor.Split();
        editor.SetPositionForTest(4 * 44100);
        editor.Split();
        Assert.Equal(3, editor.ClipsForTest.Count);
        editor.SelectPartForTest(1);
        editor.DeletePart();
        Assert.Equal(2, editor.ClipsForTest.Count);
        Assert.Equal(whole - 2 * 44100, EditTimeline.Length(editor.ClipsForTest));

        editor.SelectPartForTest(1);
        adding = editor.AddSongAsync(song);
        Pump(() => adding.IsCompleted);
        Assert.Equal(3, editor.ClipsForTest.Count);
        editor.Undo();
        Assert.Equal(2, editor.ClipsForTest.Count);
        editor.Redo();
        Assert.Equal(3, editor.ClipsForTest.Count);

        editor.MovePart(2, 0);
        Assert.Equal(whole, editor.ClipsForTest[0].Length);

        form.SelectVisible(0);
        Application.DoEvents();
        var track = form.SelectedRows().Single();
        editor.SetNameForTest("My / edit");
        var saving = editor.SaveAsync();
        Pump(() => saving.IsCompleted);
        string saved = saving.Result!;

        Assert.Equal("My _ edit.wav", Path.GetFileName(saved));
        Assert.Equal(Path.Combine(_data, "packs", "Downloaded songs", "My _ edit.wav"), saved);
        Assert.Equal(whole * 2 - 2 * 44100, AudioIO.Decode(saved, 44100)[0].Length);
        Assert.False(track.IsAssigned);
        Assert.Contains(saved, editor.SongPaths);

        // Saving only lists the song. It goes on a track from the dropdown.
        form.SongPickForTest.SelectedIndex = form.SongPickForTest.Items.IndexOf("My _ edit  ·  edited");
        Assert.Equal(saved, track.Source);

        // Saving again never replaces the first song.
        saving = editor.SaveAsync();
        Pump(() => saving.IsCompleted);
        Assert.NotEqual(saved, saving.Result);
        Assert.True(File.Exists(saved));
        form.Close();
    });

    [Fact]
    public void Editor_RemembersTheEdit_AndLeavesOutMissingFiles() => OnUiThread(() =>
    {
        string song = WriteSong(3);
        string goes = Path.Combine(_data, "goes away.wav");
        File.Copy(song, goes);
        using (var form = OpenWindow())
        {
            var opening = form.ShowTab(editor: true);
            Pump(() => opening.IsCompleted);
            foreach (string file in new[] { song, goes })
            {
                var adding = form.EditorForTest.AddSongAsync(file);
                Pump(() => adding.IsCompleted);
            }
            form.EditorForTest.SetNameForTest("Kept");
            Assert.Equal(2, form.EditorForTest.ClipsForTest.Count);
            form.Close();
        }

        File.Delete(goes);
        using var again = OpenWindow();
        var reopening = again.ShowTab(editor: true);
        Pump(() => reopening.IsCompleted);
        Assert.Equal(song, again.EditorForTest.ClipsForTest.Single().Source);
        Assert.Contains("goes away", again.LogTextForTest);
        again.Close();
    });

    [Fact]
    public void Editor_SameSongWrittenTwoWays_IsListedOnce_AndLoadingStaysOutOfTheLog() => OnUiThread(() =>
    {
        string song = WriteSong(3);
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        using var form = OpenWindow();
        var opening = form.ShowTab(editor: true);
        Pump(() => opening.IsCompleted);
        foreach (string file in new[] { song, song.Replace('\\', '/') })
        {
            var adding = form.EditorForTest.AddSongAsync(file);
            Pump(() => adding.IsCompleted);
        }
        Assert.Single(form.EditorForTest.SongPaths);
        Assert.DoesNotContain("Loading", form.LogTextForTest);
        form.Close();
    });

    [Fact]
    public void Log_SameMessageAgain_IsOnlyWrittenOnce() => OnUiThread(() =>
    {
        using var form = OpenWindow();
        form.SetStatusForTest("Add a song first.");
        form.SetStatusForTest("Add a song first.");
        Assert.Equal(form.LogTextForTest.IndexOf("Add a song first."), form.LogTextForTest.LastIndexOf("Add a song first."));
        form.SetStatusForTest("Something else.");
        form.SetStatusForTest("Add a song first.");
        Assert.NotEqual(form.LogTextForTest.IndexOf("Add a song first."), form.LogTextForTest.LastIndexOf("Add a song first."));
        form.Close();
    });

    private (MainForm Form, EditorPanel Editor, string Download, List<string> Removed) EditorWithDownload()
    {
        string folder = Path.Combine(_data, "packs", "Downloaded songs");
        Directory.CreateDirectory(folder);
        string download = Path.Combine(folder, "download.wav");
        File.Copy(WriteSong(3), download);
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        var form = OpenWindow();
        var opening = form.ShowTab(editor: true);
        Pump(() => opening.IsCompleted);
        var removed = new List<string>();
        form.EditorForTest.RemoveFile = path =>
        {
            removed.Add(path);
            File.Delete(path);
        };
        return (form, form.EditorForTest, download, removed);
    }

    [Fact]
    public void Editor_DeleteSong_RemovesDownloadAndItsParts_AndKeepsOthers() => OnUiThread(() =>
    {
        var (form, editor, download, removed) = EditorWithDownload();
        using (form)
        {
            string other = WriteSong(2);
            foreach (string file in new[] { download, other })
            {
                var adding = editor.AddSongAsync(file);
                Pump(() => adding.IsCompleted);
            }
            Assert.Equal(2, editor.ClipsForTest.Count);

            Assert.True(editor.DeleteSong(download));

            Assert.Equal([download], removed);
            Assert.False(File.Exists(download));
            Assert.Equal(other, editor.ClipsForTest.Single().Source);
            Assert.DoesNotContain(download, editor.SongPaths);
            Assert.Contains(other, editor.SongPaths);
            form.Close();
        }
    });

    [Fact]
    public void Editor_DeleteSong_FileFromElsewhere_OnlyLeavesTheList() => OnUiThread(() =>
    {
        var (form, editor, _, removed) = EditorWithDownload();
        using (form)
        {
            string mine = WriteSong(2);
            var adding = editor.AddSongAsync(mine);
            Pump(() => adding.IsCompleted);
            Assert.False(editor.IsDownloadedSong(mine));

            Assert.True(editor.DeleteSong(mine));

            Assert.Empty(removed);
            Assert.True(File.Exists(mine));
            Assert.DoesNotContain(mine, editor.SongPaths);
            Assert.Empty(editor.ClipsForTest);
            form.Close();
        }
    });

    [Fact]
    public void Editor_DeleteSong_CanUndoItsParts_AndTracksGoBackToTheirOldMusic() => OnUiThread(() =>
    {
        var (form, editor, download, _) = EditorWithDownload();
        using (form)
        {
            var adding = editor.AddSongAsync(download);
            Pump(() => adding.IsCompleted);
            form.SelectVisible(0);
            Application.DoEvents();
            var track = form.SelectedRows().Single();
            form.HandleDrop([download], 0);

            // Dropping the song starts reading it in the background, which can hold the file for a moment.
            Pump(() => editor.DeleteSong(download));
            Assert.Empty(editor.ClipsForTest);
            Assert.False(track.IsAssigned);
            Assert.Equal("", track.Song);

            editor.Undo();
            Assert.Equal(download, editor.ClipsForTest.Single().Source);
            form.Close();
        }
    });

    [Fact]
    public void Editor_DeleteSong_WhenItFails_ChangesNothing() => OnUiThread(() =>
    {
        var (form, editor, download, _) = EditorWithDownload();
        using (form)
        {
            var adding = editor.AddSongAsync(download);
            Pump(() => adding.IsCompleted);
            editor.RemoveFile = _ => throw new IOException("in use");

            Assert.False(editor.DeleteSong(download));

            Assert.True(File.Exists(download));
            Assert.Single(editor.ClipsForTest);
            Assert.Contains(download, editor.SongPaths);
            Assert.Contains("in use", form.LogTextForTest);
            form.Close();
        }
    });

    private (MainForm Form, EditorPanel Editor, long Whole) EditorWithTwoParts()
    {
        var (form, editor, download, _) = EditorWithDownload();
        string other = WriteSong(3);
        foreach (string file in new[] { download, other })
        {
            var adding = editor.AddSongAsync(file);
            Pump(() => adding.IsCompleted);
        }
        Application.DoEvents();
        return (form, editor, editor.ClipsForTest[0].Length);
    }

    [Fact]
    public void Editor_DraggingAPartsEdge_CropsIt_AndUndoBringsItBack() => OnUiThread(() =>
    {
        var (form, editor, whole) = EditorWithTwoParts();
        using (form)
        {
            var timeline = editor.TimelineForTest;

            // Drag the end of the first part back to about half way.
            int edge = timeline.XForTest(whole) - 2;
            int middle = timeline.XForTest(whole / 2);
            timeline.MouseDownForTest(edge);
            timeline.MouseMoveForTest(middle);
            timeline.MouseUpForTest(middle);
            Assert.Equal(2, editor.ClipsForTest.Count);
            Assert.InRange(editor.ClipsForTest[0].Length, whole / 2 - 2000, whole / 2 + 2000);
            Assert.Equal(0, editor.ClipsForTest[0].Start);
            Assert.Equal(whole, editor.ClipsForTest[1].Length);

            editor.Undo();
            Assert.Equal(whole, editor.ClipsForTest[0].Length);

            // Drag the start of the second part forward.
            edge = timeline.XForTest(whole) + 2;
            middle = timeline.XForTest(whole + whole / 2);
            timeline.MouseDownForTest(edge);
            timeline.MouseMoveForTest(middle);
            timeline.MouseUpForTest(middle);
            Assert.Equal(whole, editor.ClipsForTest[0].Length);
            Assert.InRange(editor.ClipsForTest[1].Start, whole / 2 - 2000, whole / 2 + 2000);
            Assert.Equal(whole, editor.ClipsForTest[1].End);
            form.Close();
        }
    });

    [Fact]
    public void Editor_ClickingAPartsEdge_OnlyMovesTheCursor() => OnUiThread(() =>
    {
        var (form, editor, whole) = EditorWithTwoParts();
        using (form)
        {
            var timeline = editor.TimelineForTest;
            int edge = timeline.XForTest(whole) - 2;
            timeline.MouseDownForTest(edge);
            timeline.MouseUpForTest(edge);
            Assert.Equal(whole, editor.ClipsForTest[0].Length);
            Assert.InRange(timeline.Position, whole - 20000, whole);
            form.Close();
        }
    });

    [Fact]
    public void Editor_DraggingAPartEdgeTooFar_AlwaysLeavesSomeOfIt() => OnUiThread(() =>
    {
        var (form, editor, whole) = EditorWithTwoParts();
        using (form)
        {
            var timeline = editor.TimelineForTest;
            int edge = timeline.XForTest(whole) - 2;
            timeline.MouseDownForTest(edge);
            timeline.MouseMoveForTest(-500);
            timeline.MouseUpForTest(-500);
            Assert.Equal(EditTimeline.ShortestPart, editor.ClipsForTest[0].Length);
            form.Close();
        }
    });

    [Fact]
    public void Editor_DraggingThePartsMiddle_StillMovesIt() => OnUiThread(() =>
    {
        var (form, editor, whole) = EditorWithTwoParts();
        using (form)
        {
            var timeline = editor.TimelineForTest;
            string first = editor.ClipsForTest[0].Source;
            int middle = timeline.XForTest(whole / 2);
            int end = timeline.XForTest(whole * 2) - 1;
            timeline.MouseDownForTest(middle);
            timeline.MouseMoveForTest(end);
            timeline.MouseUpForTest(end);
            Assert.Equal(first, editor.ClipsForTest[1].Source);
            form.Close();
        }
    });

    [Fact]
    public void Editor_CropButtons_CutToTheCursor_OnlyInThatPart() => OnUiThread(() =>
    {
        var (form, editor, whole) = EditorWithTwoParts();
        using (form)
        {
            editor.SetPositionForTest(whole + whole / 2);
            editor.CropEnd();
            Assert.Equal(whole, editor.ClipsForTest[0].Length);
            Assert.Equal(whole / 2, editor.ClipsForTest[1].Length);

            editor.SetPositionForTest(whole / 2);
            editor.CropStart();
            Assert.Equal(whole / 2, editor.ClipsForTest[0].Start);
            Assert.Equal(whole / 2, editor.ClipsForTest[1].Length);
            Assert.Equal(0, editor.TimelineForTest.Position);
            form.Close();
        }
    });

    [Fact]
    public void Editor_DroppedSong_GoesWhereItWasDropped() => OnUiThread(() =>
    {
        var (form, editor, whole) = EditorWithTwoParts();
        using (form)
        {
            string third = WriteSong(2);
            int at = editor.TimelineForTest.PositionForDrop(editor.TimelineForTest.XForTest(whole));
            Assert.Equal(1, at);
            var adding = editor.AddSongAsync(third, at);
            Pump(() => adding.IsCompleted);
            Assert.Equal(third, editor.ClipsForTest[1].Source);
            Assert.Equal(3, editor.ClipsForTest.Count);
            form.Close();
        }
    });

    private static List<string> ItemsOf(ComboBox combo) => combo.Items.Cast<object>().Select(item => item.ToString()!).ToList();

    [Fact]
    public void SongPick_NoSongs_SaysSoAndCantBeOpened() => OnUiThread(() =>
    {
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        using var form = OpenWindow();
        form.SelectVisible(0);
        Application.DoEvents();
        Assert.Contains("No songs yet", form.SongPickForTest.Text);
        Assert.False(form.SongPickForTest.Enabled);
        form.Close();
    });

    [Fact]
    public void SongPick_ImportedSong_CanBeSetOnAnotherTrack() => OnUiThread(() =>
    {
        var (form, _) = WindowWithSong();
        using (form)
        {
            string song = form.Rows.First(r => r.IsAssigned).Source;
            form.SelectVisible(1);
            Application.DoEvents();
            var track = form.SelectedRows().Single();
            Assert.False(track.IsAssigned);
            Assert.Equal(0, form.SongPickForTest.SelectedIndex);

            form.SongPickForTest.SelectedIndex = 1;

            Assert.Equal(song, track.Source);
            Assert.Equal(Path.GetFileNameWithoutExtension(song), track.Song);
            form.Close();
        }
    });

    [Fact]
    public void SongPick_ShowsTheSongTheTrackUses() => OnUiThread(() =>
    {
        var (form, _) = WindowWithSong();
        using (form)
        {
            var assigned = form.Rows.First(r => r.IsAssigned);
            form.SelectVisible(1);
            Application.DoEvents();
            Assert.Equal(0, form.SongPickForTest.SelectedIndex);
            form.SelectVisible(0);
            Application.DoEvents();
            Assert.Same(assigned, form.SelectedRows().Single());
            Assert.Equal(Path.GetFileNameWithoutExtension(assigned.Source), form.SongPickForTest.Text);
            form.Close();
        }
    });

    [Fact]
    public void SongPick_ManyTracks_AllGetTheSong() => OnUiThread(() =>
    {
        var (form, _) = WindowWithSong();
        using (form)
        {
            string song = form.Rows.First(r => r.IsAssigned).Source;
            form.SelectVisible(1, 2, 3);
            Application.DoEvents();
            var tracks = form.SelectedRows();
            form.ManySongPickForTest.SelectedIndex = 1;
            Assert.All(tracks, track => Assert.Equal(song, track.Source));
            form.Close();
        }
    });

    [Fact]
    public void SongList_ImportDownloadAndEdit_AllReachTheDropdownAndTheEditor() => OnUiThread(() =>
    {
        var (form, editor, download, _) = EditorWithDownload();
        using (form)
        {
            // A download sitting in the folder.
            Assert.Contains(download, editor.SongPaths);
            Assert.Contains("download", ItemsOf(form.SongPickForTest));

            // An import lists the song without touching the timeline.
            string imported = WriteSong(2);
            editor.ImportSongs([imported]);
            Assert.Contains(imported, editor.SongPaths);
            Assert.Contains(Path.GetFileNameWithoutExtension(imported), ItemsOf(form.SongPickForTest));
            Assert.Empty(editor.ClipsForTest);

            // A song saved from the Editor.
            var adding = editor.AddSongAsync(imported);
            Pump(() => adding.IsCompleted);
            editor.SetNameForTest("My edit");
            var saving = editor.SaveAsync();
            Pump(() => saving.IsCompleted);
            Assert.Contains(saving.Result!, editor.SongPaths);
            Assert.Contains("My edit  ·  edited", ItemsOf(form.SongPickForTest));
            Assert.Contains("My edit  ·  edited", ItemsOf(form.ManySongPickForTest));
            form.Close();
        }
    });

    [Fact]
    public void SongList_ImportedSongs_AreStillThereNextTime() => OnUiThread(() =>
    {
        string imported = WriteSong(2);
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        using (var form = OpenWindow())
        {
            var opening = form.ShowTab(editor: true);
            Pump(() => opening.IsCompleted);
            form.EditorForTest.ImportSongs([imported]);
            form.Close();
        }
        using var again = OpenWindow();
        Assert.Contains(Path.GetFileNameWithoutExtension(imported), ItemsOf(again.SongPickForTest));
        again.Close();
    });

    [Fact]
    public void SongList_DeletedSong_LeavesTheDropdown() => OnUiThread(() =>
    {
        var (form, editor, download, _) = EditorWithDownload();
        using (form)
        {
            Assert.Contains("download", ItemsOf(form.SongPickForTest));
            Assert.True(editor.DeleteSong(download));
            Assert.DoesNotContain("download", ItemsOf(form.SongPickForTest));
            form.Close();
        }
    });

    [Fact]
    public void SongList_SongsAlreadyOnTracks_AreListed() => OnUiThread(() =>
    {
        var (form, _) = WindowWithSong();
        string song;
        using (form)
        {
            song = form.Rows.First(r => r.IsAssigned).Source;
            form.SaveForTest();
            form.Close();
        }
        File.Delete(Path.Combine(_data, "songs.json"));
        using var again = OpenWindow();
        Assert.Contains(Path.GetFileNameWithoutExtension(song), ItemsOf(again.SongPickForTest));
        again.Close();
    });

    private (MainForm Form, EditorPanel Editor, SongsPanel Songs, string Song) SongsPageWithASong()
    {
        var (form, editor, download, _) = EditorWithDownload();
        form.ShowSongs();
        Application.DoEvents();
        return (form, editor, form.SongsForTest, download);
    }

    [Fact]
    public void SongsPage_ListsDownloadsImportsAndEdits_AndFilters() => OnUiThread(() =>
    {
        var (form, editor, songs, download) = SongsPageWithASong();
        using (form)
        {
            string imported = WriteSong(2);
            songs.ImportSongs([imported]);
            Assert.Equal(2, songs.VisibleForTest.Count);

            var adding = editor.AddSongAsync(imported);
            Pump(() => adding.IsCompleted);
            editor.SetNameForTest("Mix");
            var saving = editor.SaveAsync();
            Pump(() => saving.IsCompleted);
            Assert.Equal(3, songs.VisibleForTest.Count);

            songs.PickKindForTest(SongKind.Edited);
            Assert.Equal([saving.Result!], songs.VisibleForTest);
            songs.PickKindForTest(SongKind.Imported);
            Assert.Equal([imported], songs.VisibleForTest);
            songs.PickKindForTest(SongKind.Downloaded);
            Assert.Equal([download], songs.VisibleForTest);
            songs.PickKindForTest(null);
            songs.SearchForTest("mix");
            Assert.Equal([saving.Result!], songs.VisibleForTest);
            form.Close();
        }
    });

    [Fact]
    public void SongsPage_EditedSong_ShowsWhatItsMadeFrom_AndCanBeWorkedOnAgain() => OnUiThread(() =>
    {
        var (form, editor, songs, download) = SongsPageWithASong();
        using (form)
        {
            var adding = editor.AddSongAsync(download);
            Pump(() => adding.IsCompleted);
            editor.SetPositionForTest(44100);
            editor.Split();
            editor.SetNameForTest("Mix");
            var saving = editor.SaveAsync();
            Pump(() => saving.IsCompleted);
            var parts = editor.ClipsForTest.ToList();

            songs.SelectForTest(saving.Result!);
            Assert.Contains("Edited", songs.DetailsForTest);
            Assert.Contains("Made from download", songs.DetailsForTest);
            Assert.Equal("Keep editing", songs.EditTextForTest);

            editor.SelectPartForTest(0);
            editor.DeletePart();
            Assert.Single(editor.ClipsForTest);

            var opening = editor.OpenSongAsync(saving.Result!);
            Pump(() => opening.IsCompleted);
            Assert.Equal(parts, editor.ClipsForTest);
            Assert.Equal("Mix", editor.NameForTest);
            form.Close();
        }
    });

    [Fact]
    public void SongsPage_TakeOffTracks_KeepsTheSong() => OnUiThread(() =>
    {
        var (form, _, songs, download) = SongsPageWithASong();
        using (form)
        {
            form.SelectVisible(0, 1);
            Application.DoEvents();
            var tracks = form.SelectedRows();
            songs.SelectForTest(download);
            Assert.False(songs.TakeOffEnabledForTest);

            form.ManySongPickForTest.SelectedIndex = 1;
            songs.RefreshList();
            songs.SelectForTest(download);
            Assert.All(tracks, track => Assert.Equal(download, track.Source));
            Assert.Contains("on 2 tracks", songs.RowTextForTest(0));
            Assert.Contains("On 2 tracks", songs.DetailsForTest);
            Assert.True(songs.TakeOffEnabledForTest);

            songs.TakeOffTracks();
            Assert.All(tracks, track => Assert.False(track.IsAssigned));
            Assert.True(File.Exists(download));
            Assert.Contains(download, songs.VisibleForTest);
            Assert.False(songs.TakeOffEnabledForTest);
            form.Close();
        }
    });

    [Fact]
    public void SongsPage_ManySongs_SelectAllAndTakeOffTracks_StaysQuick() => OnUiThread(() =>
    {
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        using var form = OpenWindow();

        // A big pack: hundreds of small songs, each on its own track.
        string folder = Path.Combine(_data, "big");
        Directory.CreateDirectory(folder);
        string template = WriteSong(1);
        var files = Enumerable.Range(0, 300).Select(i =>
        {
            string path = Path.Combine(folder, $"song{i}.wav");
            File.Copy(template, path);
            return path;
        }).ToArray();
        form.SongsForTest.ImportSongs(files);
        for (int i = 0; i < files.Length; i++)
            form.Rows[i].Source = files[i];
        form.ShowSongs();
        Application.DoEvents();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        form.SongsForTest.SelectAll();
        long select = clock.ElapsedMilliseconds;
        clock.Restart();
        form.SongsForTest.TakeOffTracks();
        long takeOff = clock.ElapsedMilliseconds;

        Assert.Equal(300, form.SongsForTest.SelectedSongs.Count);
        Assert.All(form.Rows.Take(300), row => Assert.False(row.IsAssigned));
        Assert.True(select < 2000, $"selecting all took {select} ms");
        Assert.True(takeOff < 3000, $"taking them off took {takeOff} ms");
        form.Close();
    });

    [Fact]
    public void DeleteSongsAsync_DeletesOffTheWindowThread() => OnUiThread(() =>
    {
        var (form, editor, songs, download) = SongsPageWithASong();
        using (form)
        {
            int uiThread = Environment.CurrentManagedThreadId;
            int deletedOn = uiThread;
            editor.RemoveFile = path =>
            {
                deletedOn = Environment.CurrentManagedThreadId;
                File.Delete(path);
            };
            var deleting = editor.DeleteSongsAsync([download], deleteAllFiles: false);
            Pump(() => deleting.IsCompleted);
            Assert.Equal(1, deleting.Result);
            Assert.NotEqual(uiThread, deletedOn);
            Assert.False(File.Exists(download));
            form.Close();
        }
    });

    [Fact]
    public void SongsPage_SelectAll_ThenDelete_RemovesManySongsAtOnce() => OnUiThread(() =>
    {
        var (form, editor, songs, download) = SongsPageWithASong();
        using (form)
        {
            string other = WriteSong(2);
            songs.ImportSongs([other]);
            songs.SelectAll();
            Assert.True(songs.SelectedSongs.Count >= 2);
            Assert.Equal($"Delete {songs.SelectedSongs.Count} songs", songs.DeleteTextForTest);

            var picked = songs.SelectedSongs;
            int gone = editor.DeleteSongs(picked, deleteAllFiles: false);

            Assert.Equal(picked.Count, gone);
            Assert.False(File.Exists(download));
            Assert.True(File.Exists(other), "a file from elsewhere stays unless asked");
            Assert.Empty(songs.VisibleForTest);
            form.Close();
        }
    });

    [Fact]
    public void ImportingASecondPack_CanOpenItOnItsOwn_KeepingTheFirstAsItWas() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            string source = form.Rows.First(r => r.IsAssigned).Source;
            string packA = Path.Combine(_data, "pack a");
            Directory.CreateDirectory(packA);
            File.Copy(source, Path.Combine(packA, file));
            form.ImportChoiceForTest = DialogResult.Yes;
            var first = form.ImportPackAsync(packA);
            Pump(() => first.IsCompleted);
            form.ImportChoiceForTest = null;

            // WindowWithSong already had a song, so "pack a" opened on its own and the first pack waits.
            Assert.Equal(["pack a", "My Music Pack", "+ New pack"], form.PackChoicesForTest.ToArray());
            var row = form.Rows.First(r => r.IsAssigned);
            Assert.Contains(Path.Combine("Imported packs", "pack a"), row.Source);

            // Switching back brings the first pack's tracks and songs back, and pack a waits.
            form.PickPackForTest("My Music Pack");
            Application.DoEvents();
            Assert.Equal(["My Music Pack", "pack a", "+ New pack"], form.PackChoicesForTest.ToArray());
            var mine = form.Rows.First(r => r.IsAssigned);
            Assert.DoesNotContain("Imported packs", mine.Source);

            form.PickPackForTest("pack a");
            Application.DoEvents();
            Assert.Contains(Path.Combine("Imported packs", "pack a"), form.Rows.First(r => r.IsAssigned).Source);
            form.Close();
        }
    });

    [Fact]
    public void ImportingASecondPack_ChoosingNo_PutsItsSongsOnTheCurrentTracks() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            string packA = Path.Combine(_data, "pack a");
            Directory.CreateDirectory(packA);
            // A song for a different track, so the question about tracks that already have songs doesn't come up too.
            File.Copy(form.Rows.First(r => r.IsAssigned).Source, Path.Combine(packA, form.Rows.First(r => !r.IsAssigned).FileBase + ".brstm"));
            form.ImportChoiceForTest = DialogResult.No;
            var importing = form.ImportPackAsync(packA, null);
            Pump(() => importing.IsCompleted);
            Assert.DoesNotContain("pack a", form.PackChoicesForTest);
            form.Close();
        }
    });

    [Fact]
    public void Save_WritesThePackNow_AndSaysItIsKept() => OnUiThread(() =>
    {
        var (form, _) = WindowWithSong();
        using (form)
        {
            string path = AppStorage.AutosavePath;
            if (File.Exists(path))
                File.Delete(path);
            Assert.True(form.HandleShortcut(Keys.Control | Keys.S));
            Assert.True(File.Exists(path));
            Assert.Contains("kept", form.StatusTextForTest);
            Assert.Contains(Project.Load(path).Rows, row => row.Source.Length > 0);
            form.Close();
        }
    });

    [Fact]
    public void DeletePack_RemovesTheOpenPack_AndOpensTheNextOne() => OnUiThread(() =>
    {
        var (form, _) = WindowWithSong();
        using (form)
        {
            form.PickPackForTest("+ New pack");
            Application.DoEvents();
            string second = form.PackChoicesForTest.First();
            Assert.Equal(["My Music Pack"], form.PackChoicesForTest.Where(name => name != second && name != "+ New pack").ToArray());

            // Deleting the open (empty) pack opens the one that was set aside, with its song still on its track.
            form.ConfirmDeleteOpenPackForTest = false;
            form.DeleteOpenPack();
            Assert.Equal(second, form.PackChoicesForTest.First());
            form.ConfirmDeleteOpenPackForTest = true;
            form.DeleteOpenPack();
            Assert.DoesNotContain(second, form.PackChoicesForTest);
            Assert.Contains(form.Rows, row => row.IsAssigned);
            Assert.Equal("My Music Pack", form.PackChoicesForTest.First());

            // Deleting the last pack leaves a fresh empty one.
            form.DeleteOpenPack();
            Assert.DoesNotContain(form.Rows, row => row.IsAssigned);
            Assert.Equal(["My Music Pack", "+ New pack"], form.PackChoicesForTest.ToArray());
            form.Close();
        }
    });

    [Fact]
    public void SwitchingToAPackWithTheSameNameAsTheOpenOne_LosesNeither() => OnUiThread(() =>
    {
        var (form, _) = WindowWithSong();
        using (form)
        {
            // Two packs that both ended up called "My Music Pack": one open with a song, one put aside.
            form.PickPackForTest("+ New pack");
            Application.DoEvents();
            form.RenameOpenPackForTest("My Music Pack");
            var empty = form.Rows.First(r => !r.IsAssigned);
            empty.Source = form.Rows.First(r => !r.IsAssigned).Source;
            form.Rows[1].Source = Directory.GetFiles(_data, "*.wav").First();
            form.Rows[1].Song = "Second pack song";
            form.PickPackForTest(form.PackChoicesForTest.First(name => name != form.PackChoicesForTest.First() && name != "+ New pack"));
            Application.DoEvents();

            // Both packs are still there, under different names, and each kept its own song.
            var choices = form.PackChoicesForTest.Where(name => name != "+ New pack").ToList();
            Assert.Equal(2, choices.Count);
            Assert.Equal(choices.Count, choices.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            form.Close();
        }
    });

    [Fact]
    public void RenamingAPack_ToANameAnotherHas_GetsANumber() => OnUiThread(() =>
    {
        var (form, _) = WindowWithSong();
        using (form)
        {
            form.PickPackForTest("+ New pack");
            Application.DoEvents();
            string other = form.PackChoicesForTest.Skip(1).First(name => name != "+ New pack");
            form.RenameOpenPackForTest(other);
            Assert.Equal($"{other} 2", form.PackChoicesForTest.First());
            form.RenameOpenPackForTest("Brand new name");
            Assert.Equal("Brand new name", form.PackChoicesForTest.First());
            form.Close();
        }
    });

    [Fact]
    public void NewPack_StartsEmpty_AndTheOldOneIsKept() => OnUiThread(() =>
    {
        var (form, _) = WindowWithSong();
        using (form)
        {
            form.PickPackForTest("+ New pack");
            Application.DoEvents();
            Assert.DoesNotContain(form.Rows, r => r.IsAssigned);
            Assert.Contains("My Music Pack", form.PackChoicesForTest);
            form.PickPackForTest("My Music Pack");
            Application.DoEvents();
            Assert.Contains(form.Rows, r => r.IsAssigned);
            form.Close();
        }
    });

    [Fact]
    public void SongPick_ShowsTheImportedPackSong_OfTheTrackYouAreLookingAt() => OnUiThread(() =>
    {
        var (form, file) = WindowWithSong();
        using (form)
        {
            string imported = ImportedPackWithASong(form, file);
            var row = form.Rows.First(r => r.IsAssigned);
            form.SelectVisible(form.VisibleIndexForTest(row));
            Application.DoEvents();
            string name = Path.GetFileNameWithoutExtension(row.Source);

            // Its own song is shown as the pick, even though other pack songs stay out of the list.
            Assert.Equal(name, form.SongPickForTest.Text);
            Assert.Contains(name, form.SongPickForTest.Items.Cast<object>().Select(item => item.ToString()));

            // With a title on the track, the dropdown shows the title and keeps the file name after it.
            row.Song = "A Real Title";
            form.SelectCategory("All tracks");
            form.SelectVisible(form.VisibleIndexForTest(form.Rows.First(r => !r.IsAssigned)));
            Application.DoEvents();
            form.SelectVisible(form.VisibleIndexForTest(row));
            Application.DoEvents();
            Assert.Equal($"A Real Title  ({name})", form.SongPickForTest.Text);

            form.SelectCategory("All tracks");
            form.SelectVisible(form.VisibleIndexForTest(form.Rows.First(r => !r.IsAssigned)));
            Application.DoEvents();
            Assert.DoesNotContain(name, form.SongPickForTest.Items.Cast<object>().Select(item => item.ToString()));
            form.Close();
        }
    });

    [Fact]
    public void SongPick_LeavesOutSongsFromImportedPacks_ButTheSongsTabKeepsThem() => OnUiThread(() =>
    {
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        string mine = WriteSong(2);
        using var form = OpenWindow();
        string packSong = WriteSong(2);
        string copies = Path.Combine(_data, "packs", "Imported packs", "Shared");
        Directory.CreateDirectory(copies);
        string copy = Path.Combine(copies, "99_N.wav");
        File.Copy(packSong, copy);
        form.HandleDrop([mine], 0);
        form.ImportFiles([copy]);

        var picks = form.SongPickForTest.Items.Cast<object>().Select(item => item.ToString()).ToList();
        Assert.DoesNotContain("99_N", picks);
        form.ShowSongs();
        Assert.Contains(Path.GetFullPath(copy), form.SongsForTest.VisibleForTest, StringComparer.OrdinalIgnoreCase);
        form.Close();
    });

    [Fact]
    public void SongsPage_DeleteButton_SaysWhatWillHappen() => OnUiThread(() =>
    {
        var (form, editor, songs, download) = SongsPageWithASong();
        using (form)
        {
            songs.SelectForTest(download);
            Assert.Equal("Delete song", songs.DeleteTextForTest);
            string imported = WriteSong(2);
            songs.ImportSongs([imported]);
            songs.SelectForTest(imported);
            Assert.Equal("Delete song", songs.DeleteTextForTest);

            Assert.True(editor.DeleteSong(imported));
            Assert.DoesNotContain(imported, songs.VisibleForTest);
            Assert.True(File.Exists(imported));
            form.Close();
        }
    });

    [Fact]
    public void TracksPage_DeleteButton_ShowsForASong_AndDeletingClearsTheTrack() => OnUiThread(() =>
    {
        var (form, editor, songs, download) = SongsPageWithASong();
        using (form)
        {
            form.ShowTab(editor: false);
            form.SelectVisible(0);
            Application.DoEvents();
            var track = form.SelectedRows().Single();
            Assert.False(form.DeleteTrackSongVisibleForTest);

            form.SongPickForTest.SelectedIndex = 1;
            form.SelectVisible(0);
            Application.DoEvents();
            Assert.Equal(download, track.Source);
            Assert.True(form.DeleteTrackSongVisibleForTest);
            Assert.Equal("Delete song", form.DeleteTrackSongTextForTest);

            Pump(() => editor.DeleteSong(download));
            Assert.False(track.IsAssigned);
            Assert.False(form.DeleteTrackSongVisibleForTest);
            form.Close();
        }
    });

    [Fact]
    public void Tabs_ShowOnePageAtATime() => OnUiThread(() =>
    {
        using var form = OpenWindow();
        form.ShowSongs();
        Assert.True(form.SongsForTest.Visible);
        Assert.False(form.EditorForTest.Visible);
        var opening = form.ShowTab(editor: true);
        Pump(() => opening.IsCompleted);
        Assert.False(form.SongsForTest.Visible);
        Assert.True(form.EditorForTest.Visible);
        form.ShowTab(editor: false);
        Assert.False(form.SongsForTest.Visible);
        Assert.False(form.EditorForTest.Visible);
        form.Close();
    });

    [Fact]
    public void TrackDetails_NameAndCredits_AreTuckedAway_UntilAskedFor() => OnUiThread(() =>
    {
        var (form, _) = WindowWithSong();
        using (form)
        {
            form.SelectVisible(0);
            Application.DoEvents();
            Assert.False(form.SongNameVisibleForTest);
            form.ToggleNamesForTest();
            Application.DoEvents();
            Assert.True(form.SongNameVisibleForTest);
            form.Close();
        }
    });

    [Fact]
    public void Editor_MakePartsEqual_GivesTwoSongsTheSameTime() => OnUiThread(() =>
    {
        var (form, editor, download, _) = EditorWithDownload();
        using (form)
        {
            string longer = WriteSong(6);
            foreach (string file in new[] { download, longer })
            {
                var adding = editor.AddSongAsync(file);
                Pump(() => adding.IsCompleted);
            }
            long shortest = editor.ClipsForTest.Min(part => part.Length);
            Assert.NotEqual(editor.ClipsForTest[0].Length, editor.ClipsForTest[1].Length);

            editor.MakePartsEqual();
            Assert.All(editor.ClipsForTest, part => Assert.Equal(shortest, part.Length));

            editor.Undo();
            Assert.NotEqual(editor.ClipsForTest[0].Length, editor.ClipsForTest[1].Length);
            form.Close();
        }
    });

    [Fact]
    public void Editor_EmptyTimeline_SaysWhySavingIsOff() => OnUiThread(() =>
    {
        var (form, editor, _, _) = EditorWithDownload();
        using (form)
        {
            Assert.Contains("You can save once", editor.InfoForTest);
            form.Close();
        }
    });

    [Fact]
    public void Editor_ShortcutsWorkOnlyOnItsTab_AndNotWhileTyping() => OnUiThread(() =>
    {
        using var form = OpenWindow();
        Assert.False(form.HandleShortcut(Keys.S));
        var opening = form.ShowTab(editor: true);
        Pump(() => opening.IsCompleted);
        Assert.True(form.HandleShortcut(Keys.S));
        form.ActiveControl = form.PackNameBoxForTest;
        Assert.False(form.HandleShortcut(Keys.S));
        form.Close();
    });

    [Fact]
    public void AddToPatches_SongMissing_InstallsNothing() => OnUiThread(() =>
    {
        string song = WriteSong();
        AppStorage.SaveSettings(new AppSettings { OutputFolder = Path.Combine(_data, "packs") });
        using var form = OpenWindow();
        form.HandleDrop([song], 0);
        File.Delete(song);

        var build = form.AddToPatchesAsync();
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
        AppStorage.SaveSettings(new AppSettings { OutputFolder = output });
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

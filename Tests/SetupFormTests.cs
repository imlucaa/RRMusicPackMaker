using RRMusicPackMaker;
using Xunit;

namespace RRMusicPackMaker.Tests;

[Collection("windows")]
public class SetupFormTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "rrm_setup_" + Guid.NewGuid().ToString("N"));

    public SetupFormTests()
    {
        Directory.CreateDirectory(_data);
        Environment.SetEnvironmentVariable("RRMUSIC_DATA", _data);
        Environment.SetEnvironmentVariable("RRMUSIC_MODS", Path.Combine(_data, "Mods"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("RRMUSIC_DATA", null);
        Environment.SetEnvironmentVariable("RRMUSIC_MODS", null);
        Directory.Delete(_data, true);
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
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ShouldShow_OnlyOnFirstLaunch(bool done, bool expected) =>
        Assert.Equal(expected, SetupForm.ShouldShow(done));

    [Fact]
    public void WheelWizard_TurnedOff_FindsNoMods()
    {
        string chosen = Path.Combine(_data, "Mods");
        Assert.Contains(chosen, PackBuilder.ModsFolders(new AppSettings { ModsFolder = chosen }));
        Assert.Empty(PackBuilder.ModsFolders(new AppSettings { ModsFolder = chosen, UseWheelWizard = false }));
    }

    [Fact]
    public void RetroRewindFolderIn_AcceptsTheFolderOrTheCardHoldingIt()
    {
        string folder = Path.Combine(_data, "RetroRewind6");
        Directory.CreateDirectory(Path.Combine(folder, "Binaries"));
        File.WriteAllText(Path.Combine(folder, "Binaries", "ConfigRT.pul"), "x");
        Assert.Equal(folder, SetupForm.RetroRewindFolderIn(folder));
        Assert.Equal(folder, SetupForm.RetroRewindFolderIn(_data));
        Assert.Null(SetupForm.RetroRewindFolderIn(Path.Combine(folder, "Binaries")));
    }

    [Theory]
    [InlineData(@"D:\wheel wizard", @"D:\wheel wizard\Mods")]
    [InlineData(@"D:\wheel wizard\Mods", @"D:\wheel wizard\Mods")]
    [InlineData(@"D:\wheel wizard\mods\", @"D:\wheel wizard\mods\")]
    public void ModsFolderIn_AcceptsTheDataFolderOrMods(string picked, string expected) =>
        Assert.Equal(expected, SetupForm.ModsFolderIn(picked));

    [Fact]
    public void FindSdCards_OnlyFindsCardsWithRetroRewind()
    {
        string card = Path.Combine(_data, "card");
        string empty = Path.Combine(_data, "empty");
        Directory.CreateDirectory(Path.Combine(card, "RetroRewind6", "Binaries"));
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(card, "RetroRewind6", "Binaries", "ConfigRT.pul"), "x");
        Assert.Equal([Path.Combine(card, "RetroRewind6")], PackBuilder.FindSdCards([card, empty]));
    }

    [Fact]
    public void FindSdCards_AutomatedRun_NeverLooksAtRealDrives() => Assert.Empty(PackBuilder.FindSdCards());

    private static SetupForm Open(Options options, AppSettings settings)
    {
        var form = new SetupForm(options, settings) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();
        return form;
    }

    [Fact]
    public void Window_StartsWithTheRecommendedSettingsChosen() => OnUiThread(() =>
    {
        var options = new Options();
        var settings = new AppSettings();
        using var form = Open(options, settings);
        Assert.True(form.AllChosenForTest);
        form.Close();
        Assert.Equal(Options.LoudnessEven, options.Loudness);
        Assert.True(options.RaceBoost);
        Assert.True(settings.BoostMusicSlots);
        Assert.True(settings.UseWheelWizard);
        Assert.True(settings.CheckForUpdates);
    });

    [Fact]
    public void Window_ChangedChoices_AreKept_HoweverItCloses() => OnUiThread(() =>
    {
        var options = new Options();
        var settings = new AppSettings();
        using var form = Open(options, settings);
        form.SetChoicesForTest(even: false, race: false, raise: false, wheelWizard: false, updates: false);
        Assert.False(form.RaceBoostEnabledForTest);
        form.Close();
        Assert.Equal(Options.LoudnessOff, options.Loudness);
        Assert.False(options.RaceBoost);
        Assert.False(settings.BoostMusicSlots);
        Assert.False(settings.UseWheelWizard);
        Assert.False(settings.CheckForUpdates);
    });

    [Fact]
    public void Window_KeepsAPeakOnlyVolumeSetting() => OnUiThread(() =>
    {
        var options = new Options { Loudness = Options.LoudnessPeak };
        using var form = Open(options, new AppSettings());
        form.Close();
        Assert.Equal(Options.LoudnessPeak, options.Loudness);
    });

    [Fact]
    public void Window_ShowsTheChosenFolders() => OnUiThread(() =>
    {
        var settings = new AppSettings { OutputFolder = @"D:\Packs", ModsFolder = @"D:\WW\Mods" };
        using var form = new SetupForm(new Options { RetroRewindFolder = "" }, settings) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();
        Assert.Contains(@"D:\Packs", form.OutputStatusForTest);
        Assert.Contains(@"D:\WW\Mods", form.ModsStatusForTest);
        form.Close();
    });

    [Fact]
    public void IsRetroRewindFolder_NeedsTheTrackList()
    {
        Assert.False(SetupForm.IsRetroRewindFolder(""));
        Assert.False(SetupForm.IsRetroRewindFolder(_data));
        Directory.CreateDirectory(Path.Combine(_data, "Binaries"));
        File.WriteAllText(Path.Combine(_data, "Binaries", "ConfigRT.pul"), "x");
        Assert.True(SetupForm.IsRetroRewindFolder(_data));
    }

    [Fact]
    public void Window_NothingFound_ExplainsAndOffersPicking() => OnUiThread(() =>
    {
        using var form = new SetupForm(new Options { RetroRewindFolder = "" }, new AppSettings()) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();
        Assert.Contains("built-in track list", form.RetroRewindStatusForTest);
        form.Close();
    });

    [Fact]
    public void Window_FfmpegOnThisPc_SaysInstalledAndCanStart() => OnUiThread(() =>
    {
        Assert.True(FfmpegInstaller.IsInstalled, "these tests need FFmpeg on PATH");
        using var form = new SetupForm(new Options { RetroRewindFolder = _data }, new AppSettings()) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        Application.DoEvents();
        Assert.Contains("Installed", form.FfmpegStatusForTest);
        Assert.False(form.InstallVisibleForTest);
        Assert.Equal("Start", form.StartTextForTest);
        form.Close();
    });

    [Fact]
    public void ShortPath_KeepsTheEndOfLongPaths()
    {
        const string longPath = @"C:\Users\Player\AppData\Roaming\Dolphin Emulator\Load\Riivolution\WheelWizard\RetroRewind6";
        string shown = SetupForm.ShortPath(longPath);
        Assert.StartsWith("...", shown);
        Assert.EndsWith(@"\WheelWizard\RetroRewind6", shown);
        Assert.True(shown.Length <= 61);
        Assert.Equal(@"C:\Games\RetroRewind6", SetupForm.ShortPath(@"C:\Games\RetroRewind6"));
    }

    [Fact]
    public void Settings_SetupDone_IsSaved()
    {
        AppStorage.SaveSettings(new AppSettings { SetupDone = true });
        Assert.True(AppStorage.LoadSettings().SetupDone);
    }
}

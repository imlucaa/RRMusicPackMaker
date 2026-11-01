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
    [InlineData(false, true, true, true)]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, false)]
    public void ShouldShow_WhenSomethingIsNeeded(bool ffmpeg, bool retroRewind, bool done, bool expected) =>
        Assert.Equal(expected, SetupForm.ShouldShow(ffmpeg, retroRewind, done));

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
        Assert.Contains("Not found", form.RetroRewindStatusForTest);
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

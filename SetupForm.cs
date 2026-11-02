namespace RRMusicPackMaker;

/// <summary>The first thing a new user sees: the folders it found and the settings most people want, already chosen.</summary>
public sealed class SetupForm : Form
{
    private readonly Options _options;
    private readonly AppSettings _settings;
    private readonly Label _ffmpegStatus = Status();
    private readonly Label _gameStatus = Status();
    private readonly Label _rrStatus = Status();
    private readonly Label _modsStatus = Status();
    private readonly Label _outputStatus = Status();
    private readonly ModernButton _install = new() { Text = "Install", AutoSize = true, Tag = Theme.AccentTag };
    private readonly ModernButton _pickRr = new() { Text = "Browse", AutoSize = true };
    private readonly ModernButton _pickGame = new() { Text = "Browse", AutoSize = true };
    private readonly ModernButton _pickMods = new() { Text = "Browse", AutoSize = true };
    private readonly ModernButton _pickOutput = new() { Text = "Change", AutoSize = true };
    private readonly ModernButton _start = new() { Text = "Start", AutoSize = true, Tag = Theme.AccentTag };
    private readonly ThinProgress _progress = new() { Dock = DockStyle.Bottom, Height = 4, Visible = false };
    private readonly FlatCheck _evenVolume = Choice("Even out the volume across songs");
    private readonly FlatCheck _raceBoost = Choice("Make race music louder, because the game's race mix is quiet");
    private readonly FlatCheck _raiseSlots = Choice("Raise the game's music volume, if your game file is found");
    private readonly FlatCheck _wheelWizard = Choice("Add packs to Wheel Wizard");
    private readonly FlatCheck _updates = Choice("Tell me when a new version is out");
    private CancellationTokenSource? _work;

    /// <summary>True when the Retro Rewind folder was changed here, so the track list must reload.</summary>
    public bool FolderChanged { get; private set; }

    /// <summary>Shows once, on first launch. Nothing here is forced: FFmpeg gets offered again when a pack first needs it.</summary>
    internal static bool ShouldShow(bool setupDone) => !setupDone;

    private static FlatCheck Choice(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };

    private static Label Status() => new()
    {
        AutoSize = true,
        MaximumSize = new Size(420, 0),
        Tag = Theme.MutedTag,
        Margin = new Padding(0, 2, 0, 0),
    };

    public SetupForm(Options options, AppSettings settings)
    {
        _options = options;
        _settings = settings;
        Text = "Set up Music Pack Maker";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = Theme.UiFont();
        BuildLayout();
        Theme.Apply(this);
        RefreshStatus();
        _evenVolume.Checked = _options.Loudness != Options.LoudnessOff;
        _raceBoost.Checked = _options.RaceBoost;
        _raiseSlots.Checked = _settings.BoostMusicSlots;
        _wheelWizard.Checked = _settings.UseWheelWizard;
        _updates.Checked = _settings.CheckForUpdates;
        _raceBoost.Enabled = _evenVolume.Checked;
        _evenVolume.CheckedChanged += (_, _) => _raceBoost.Enabled = _evenVolume.Checked;
        FormClosing += (_, _) =>
        {
            _work?.Cancel();
            ApplyChoices();
        };
    }

    private void BuildLayout()
    {
        var page = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(22, 20, 22, 18) };
        page.Controls.Add(new Label { Text = "Welcome to Music Pack Maker", AutoSize = true, Font = Theme.TitleFont(16f), Margin = new Padding(0, 0, 0, 4) });
        page.Controls.Add(new Label { Text = "It's all optional, and already set up the way most people want.", AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 14) });
        page.Controls.Add(Card("FFmpeg, to convert songs", _ffmpegStatus, _install, _progress));
        page.Controls.Add(Card("Retro Rewind folder, on your PC or SD card", _rrStatus, _pickRr));
        page.Controls.Add(Card("Wheel Wizard Mods folder", _modsStatus, _pickMods));
        page.Controls.Add(Card("Packs and downloads", _outputStatus, _pickOutput));
        page.Controls.Add(Card("Mario Kart Wii file, only to raise the volume", _gameStatus, _pickGame));

        var choices = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16, 12, 16, 6), Tag = Theme.PanelTag };
        choices.Controls.Add(new Label { Text = "Recommended settings", AutoSize = true, Font = Theme.TitleFont(10f), Margin = new Padding(0, 0, 0, 8) });
        choices.Controls.AddRange([_evenVolume, _raceBoost, _raiseSlots, _wheelWizard, _updates]);
        var choicesCard = new CardPanel { Width = 580, Height = 190, Margin = new Padding(0, 2, 0, 8) };
        choices.Dock = DockStyle.Fill;
        choicesCard.Controls.Add(choices);
        page.Controls.Add(choicesCard);

        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0) };
        footer.Controls.Add(_start);
        page.Controls.Add(footer);
        Controls.Add(page);

        _install.Click += async (_, _) =>
        {
            if (_work != null)
                _work.Cancel();
            else
                await InstallAsync();
        };
        _pickRr.Click += (_, _) => PickRetroRewind();
        _pickGame.Click += (_, _) => PickGame();
        _pickMods.Click += (_, _) => PickMods();
        _pickOutput.Click += (_, _) => PickOutput();
        _start.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
    }

    /// <summary>What's chosen here takes effect however the window closes, so skipping it still leaves the recommended settings.</summary>
    internal void ApplyChoices()
    {
        _options.Loudness = _evenVolume.Checked ? (_options.Loudness == Options.LoudnessOff ? Options.LoudnessEven : _options.Loudness) : Options.LoudnessOff;
        _options.RaceBoost = _raceBoost.Checked;
        _settings.BoostMusicSlots = _raiseSlots.Checked;
        _settings.UseWheelWizard = _wheelWizard.Checked;
        _settings.CheckForUpdates = _updates.Checked;
    }

    private static CardPanel Card(string title, Label status, ModernButton button, Control? below = null)
    {
        var card = new CardPanel { Width = 580, Height = below == null ? 60 : 64, Padding = new Padding(16, 9, 16, 6), Margin = new Padding(0, 0, 0, 6) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var text = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
        text.Controls.Add(new Label { Text = title, AutoSize = true, Font = Theme.TitleFont(10f), Margin = new Padding(0) });
        text.Controls.Add(status);
        grid.Controls.Add(text, 0, 0);
        button.Anchor = AnchorStyles.Right;
        button.Margin = new Padding(10, 0, 0, 0);
        grid.Controls.Add(button, 1, 0);
        card.Controls.Add(grid);
        if (below != null)
            card.Controls.Add(below);
        return card;
    }

    private void RefreshStatus()
    {
        bool hasFfmpeg = FfmpegInstaller.IsInstalled;
        if (_work == null)
        {
            _ffmpegStatus.Text = hasFfmpeg ? "✓  Installed" : "Not installed. It's a 77 MB download and takes about 150 MB of space.";
            _install.Visible = !hasFfmpeg;
        }
        _start.Text = hasFfmpeg ? "Start" : "Skip for now";
        _start.Tag = hasFfmpeg ? Theme.AccentTag : "";
        _start.Invalidate();

        bool hasRr = IsRetroRewindFolder(_options.RetroRewindFolder);
        _rrStatus.Text = hasRr ? $"✓  {ShortPath(_options.RetroRewindFolder)}" : "Not found. That's fine, you'll just see fewer tracks.";
        _pickRr.Text = hasRr ? "Change" : "Browse";

        string? mods = _settings.ModsFolder.Length > 0 ? _settings.ModsFolder : PackBuilder.ModsFolders().FirstOrDefault();
        _modsStatus.Text = mods != null ? $"✓  {ShortPath(mods)}" : "Not found. That's fine if you play on a real Wii.";
        _pickMods.Text = mods != null ? "Change" : "Browse";

        _outputStatus.Text = $"✓  {ShortPath(_settings.OutputFolder.Length > 0 ? _settings.OutputFolder : AppStorage.DefaultOutputFolder)}";

        string? game = SoundArchive.IsReady ? null : SoundArchive.FindGame(_settings);
        _gameStatus.Text = SoundArchive.IsReady ? "✓  Music volume is already raised, so this isn't needed."
            : game != null ? $"✓  {ShortPath(game)}"
            : "Not found. Your music can be quieter without it.";
        _pickGame.Visible = game == null && !SoundArchive.IsReady;
    }

    /// <summary>Keeps long paths on one line: the end of a path is the part people recognize.</summary>
    internal static string ShortPath(string path)
    {
        const int limit = 58;
        if (path.Length <= limit)
            return path;
        string end = path[^limit..];
        int cut = end.IndexOf(Path.DirectorySeparatorChar);
        return "..." + (cut >= 0 ? end[cut..] : end);
    }

    internal static bool IsRetroRewindFolder(string folder) =>
        folder.Length > 0 && File.Exists(Path.Combine(folder, "Binaries", "ConfigRT.pul"));

    /// <summary>Downloads and installs FFmpeg, showing progress. The same button stops it.</summary>
    internal async Task InstallAsync()
    {
        _work = new CancellationTokenSource();
        _install.Text = "Stop";
        _install.Tag = "";
        _install.Invalidate();
        _progress.Fraction = 0;
        _progress.Visible = true;
        try
        {
            await FfmpegInstaller.InstallAsync(
                message => OnWindow(() => _ffmpegStatus.Text = message),
                fraction => OnWindow(() =>
                {
                    _progress.Fraction = fraction;
                    _ffmpegStatus.Text = $"Downloading... {fraction * 100:0}%";
                }),
                _work.Token);
            if (IsDisposed)
                return;
            _work.Dispose();
            _work = null;
            _progress.Visible = false;
            RefreshStatus();
        }
        catch (Exception ex) when (ex is DownloadFailedException or HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or FileNotFoundException)
        {
            bool stopped = ex is OperationCanceledException;
            _work?.Dispose();
            _work = null;
            if (IsDisposed)
                return;
            _progress.Visible = false;
            _install.Text = stopped ? "Install" : "Try again";
            _install.Tag = Theme.AccentTag;
            _install.Invalidate();
            RefreshStatus();
            if (!stopped)
                _ffmpegStatus.Text = ex is HttpRequestException ? "Couldn't reach the internet. Check your connection and try again." : ex.Message;
        }
    }

    private void OnWindow(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window closed as the message arrived.
        }
    }

    private void PickRetroRewind()
    {
        using var dialog = new FolderBrowserDialog { Description = "Pick your RetroRewind6 folder", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        string? folder = RetroRewindFolderIn(dialog.SelectedPath);
        if (folder == null)
        {
            _rrStatus.Text = "That isn't a RetroRewind6 folder. It has no Binaries\\ConfigRT.pul file.";
            return;
        }
        _options.RetroRewindFolder = folder;
        FolderChanged = true;
        RefreshStatus();
    }

    /// <summary>Accepts the folder itself, or what holds it, like the root of an SD card.</summary>
    internal static string? RetroRewindFolderIn(string picked)
    {
        if (IsRetroRewindFolder(picked))
            return picked;
        string inside = Path.Combine(picked, "RetroRewind6");
        return IsRetroRewindFolder(inside) ? inside : null;
    }

    /// <summary>Accepts the Mods folder itself, or Wheel Wizard's data folder that holds it.</summary>
    internal static string ModsFolderIn(string picked) =>
        Path.GetFileName(picked.TrimEnd('\\', '/')).Equals("Mods", StringComparison.OrdinalIgnoreCase) ? picked : Path.Combine(picked, "Mods");

    private void PickMods()
    {
        using var dialog = new FolderBrowserDialog { Description = "Pick Wheel Wizard's data folder or its Mods folder", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        _settings.ModsFolder = ModsFolderIn(dialog.SelectedPath);
        RefreshStatus();
    }

    private void PickOutput()
    {
        using var dialog = new FolderBrowserDialog { Description = "Where should packs and downloads go?", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        _settings.OutputFolder = dialog.SelectedPath;
        RefreshStatus();
    }

    private void PickGame()
    {
        using var dialog = new OpenFileDialog { Title = "Pick your Mario Kart Wii file", Filter = "Wii games|*.rvz;*.iso;*.wbfs;*.ciso;*.gcz;*.wia|All files|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        _settings.GameFile = dialog.FileName;
        RefreshStatus();
    }

    // For the automated tests.
    internal string FfmpegStatusForTest => _ffmpegStatus.Text;
    internal string RetroRewindStatusForTest => _rrStatus.Text;
    internal string ModsStatusForTest => _modsStatus.Text;
    internal string OutputStatusForTest => _outputStatus.Text;
    internal string StartTextForTest => _start.Text;
    internal bool InstallVisibleForTest => _install.Visible;
    internal void SetChoicesForTest(bool even, bool race, bool raise, bool wheelWizard, bool updates)
    {
        _evenVolume.Checked = even;
        _raceBoost.Checked = race;
        _raiseSlots.Checked = raise;
        _wheelWizard.Checked = wheelWizard;
        _updates.Checked = updates;
    }

    internal bool AllChosenForTest => _evenVolume.Checked && _raceBoost.Checked && _raiseSlots.Checked && _wheelWizard.Checked && _updates.Checked;
    internal bool RaceBoostEnabledForTest => _raceBoost.Enabled;
}

namespace RRMusicPackMaker;

/// <summary>The first thing a new user sees: gets FFmpeg installed, and checks the app can find the game.</summary>
public sealed class SetupForm : Form
{
    private readonly Options _options;
    private readonly AppSettings _settings;
    private readonly Label _ffmpegStatus = Status();
    private readonly Label _gameStatus = Status();
    private readonly Label _rrStatus = Status();
    private readonly ModernButton _install = new() { Text = "Install", AutoSize = true, Tag = Theme.AccentTag };
    private readonly ModernButton _pickRr = new() { Text = "Browse", AutoSize = true };
    private readonly ModernButton _pickGame = new() { Text = "Browse", AutoSize = true };
    private readonly ModernButton _start = new() { Text = "Start", AutoSize = true, Tag = Theme.AccentTag };
    private readonly ThinProgress _progress = new() { Dock = DockStyle.Bottom, Height = 4, Visible = false };
    private CancellationTokenSource? _work;

    /// <summary>True when the Retro Rewind folder was changed here, so the track list must reload.</summary>
    public bool FolderChanged { get; private set; }

    /// <summary>
    /// FFmpeg is required, so setup comes back until it's installed. Finding Retro Rewind only matters
    /// the first time, because after that people can use Settings and may not have it installed yet.
    /// </summary>
    internal static bool ShouldShow(bool ffmpegInstalled, bool retroRewindFound, bool setupDone) =>
        !ffmpegInstalled || (!setupDone && !retroRewindFound);

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
        Text = "Set up RR Music";
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
        FormClosing += (_, _) => _work?.Cancel();
    }

    private void BuildLayout()
    {
        var page = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(22, 20, 22, 18) };
        page.Controls.Add(new Label { Text = "Welcome to RR Music", AutoSize = true, Font = Theme.TitleFont(16f), Margin = new Padding(0, 0, 0, 4) });
        page.Controls.Add(new Label { Text = "Let's get a few things ready. This only appears when something is needed.", AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 14) });
        page.Controls.Add(Card("FFmpeg", "Converts your songs for the game.", _ffmpegStatus, _install, _progress));
        page.Controls.Add(Card("Retro Rewind", "Used to list every track and to add your pack.", _rrStatus, _pickRr));
        page.Controls.Add(Card("Mario Kart Wii file", "Optional. Used to raise the game's music volume.", _gameStatus, _pickGame));

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
        _start.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
    }

    private static CardPanel Card(string title, string help, Label status, ModernButton button, Control? below = null)
    {
        var card = new CardPanel { Width = 580, Height = below == null ? 86 : 92, Padding = new Padding(16, 12, 16, 8), Margin = new Padding(0, 0, 0, 10) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var text = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
        text.Controls.Add(new Label { Text = title, AutoSize = true, Font = Theme.TitleFont(10f), Margin = new Padding(0) });
        text.Controls.Add(new Label { Text = help, AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 1, 0, 0) });
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
        _rrStatus.Text = hasRr ? $"✓  {ShortPath(_options.RetroRewindFolder)}" : "Not found. Pick your RetroRewind6 folder, or do it later in Settings.";
        _pickRr.Visible = !hasRr;

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
        if (!IsRetroRewindFolder(dialog.SelectedPath))
        {
            _rrStatus.Text = "That isn't a RetroRewind6 folder. It has no Binaries\\ConfigRT.pul file.";
            return;
        }
        _options.RetroRewindFolder = dialog.SelectedPath;
        FolderChanged = true;
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
    internal string StartTextForTest => _start.Text;
    internal bool InstallVisibleForTest => _install.Visible;
}

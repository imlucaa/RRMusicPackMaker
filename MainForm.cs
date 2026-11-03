using System.Diagnostics;

namespace RRMusicPackMaker;

public sealed class MainForm : Form
{
    private const string AllTracks = "All tracks";
    private const string MySongs = "Songs I set";
    private const string AudioFilter = "Audio files|*.wav;*.mp3;*.flac;*.ogg;*.m4a;*.aac;*.opus;*.wma;*.brstm;*.webm;*.mp4|All files|*.*";

    private readonly Font _titleFont = Theme.TitleFont(11f);
    private List<TrackRow> _rows = [];
    private List<TrackRow> _visible = [];
    private TrackRow? _selected;
    private bool _loading;
    private int _dropIndex = -1;
    private int _hoverRow = -1;
    private int _sidebarIndex;
    private string _category = "";
    private string _platform = "";
    private readonly Font _pillFont = Theme.UiFont(8.5f);
    private readonly FlowLayoutPanel _chips = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(4, 0, 4, 2), Visible = false };
    private string? _lastBuildMusicFolder;
    private string? _lastBuildFolder;
    private string? _lastExportFolder;
    private HashSet<string> _inGame = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppSettings _settings = AppStorage.LoadSettings();
    private readonly Options _options = new();
    private readonly System.Windows.Forms.Timer _autosave = new() { Interval = 1500 };
    private readonly Dictionary<string, Task<SongOverview>> _overviews = new();

    private readonly FieldBox _packName = new("Pack name") { Width = 190, Margin = new Padding(0, 0, 8, 0) };
    private readonly FieldBox _author = new("Author") { Width = 130, Margin = new Padding(0, 0, 8, 0) };
    private readonly FieldBox _filter = new("Search tracks, files or songs") { Dock = DockStyle.Fill };
    private readonly SideList _sidebar = new() { Dock = DockStyle.Fill, Tag = Theme.PanelTag };
    private readonly CleanGrid _grid = new() { Dock = DockStyle.Fill };
    private readonly ModernButton _openFolder = Footer("Show exported pack");
    private readonly ModernButton _removeFromGame = Footer("Remove from game");
    private readonly ModernButton _updateButton = Footer("Update available");
    private readonly ModernButton _tabTracks = Tab("Tracks");
    private readonly ModernButton _tabEditor = Tab("Editor");
    private readonly ModernButton _tabSongs = Tab("Songs");
    private SongsPanel _songsPage = null!;
    private ModernButton? _playingButton;
    private const string HearCurrentText = "▶  Hear current music";
    private const string HearSongText = "▶  Hear song";
    private TrackRow? _playingRow;
    private bool _hearingSong;
    private int _hearRequest;
    private const string HearJumpText = "▶  Hear the jump";
    private readonly ModernButton _deleteTrackSong = new() { Text = "Delete song", Height = 28, Dock = DockStyle.Fill, Margin = new Padding(4, 8, 0, 0) };
    private readonly ModernButton _namesToggle = new() { Text = "Name and credits", Height = 28, Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 4) };
    private readonly List<Control> _nameFields = [];
    private bool _showNames;
    private readonly Panel _body = new() { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 6, 0) };
    private EditorPanel _editor = null!;
    private readonly ModernButton _export = Footer("Export pack");
    private readonly ModernButton _importPack = Footer("Import pack");
    private string _reportedIssues = "";
    private string _lastLogged = "";
    private readonly ModernButton _hearCurrent = new() { Text = "▶  Hear current music", Height = 28, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
    private readonly ComboBox _manyLoop = new FlatCombo { Width = 310, Margin = new Padding(0, 0, 0, 4) };
    private readonly ComboBox _manyFade = new FlatCombo { Width = 310, Margin = new Padding(0, 0, 0, 4) };
    private readonly List<ModernButton> _nudgeButtons = [];
    private readonly ComboBox _manyVolume = new FlatCombo { Width = 310, Margin = new Padding(0, 0, 0, 4), MaxDropDownItems = 14 };
    private readonly ComboBox _manySpeed = new FlatCombo { Width = 310, Margin = new Padding(0, 0, 0, 8), MaxDropDownItems = 14 };
    private readonly ComboBox _manySongPick = new FlatCombo { Width = 310, Margin = new Padding(0, 0, 0, 8), MaxDropDownItems = 14 };
    private readonly ComboBox _songPick = new FlatCombo { Dock = DockStyle.Top, Margin = new Padding(0, 8, 0, 0), MaxDropDownItems = 14 };
    private readonly SongLibrary _library;
    private List<string> _songPaths = [];
    private static readonly LoopMode[] BulkLoopModes = [LoopMode.Auto, LoopMode.WholeSong, LoopMode.FindLoop, LoopMode.NoLoop];
    private const string NoChange = "Keep as they are";
    private readonly HashSet<TrackRow> _missing = [];
    private readonly Dictionary<string, double> _seconds = new(StringComparer.OrdinalIgnoreCase);
    private long _sizeEstimate;
    private DateTime _lastCheck = DateTime.MinValue;
    private readonly Label _status = new() { AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(14, 9, 0, 0) };
    private readonly ThinProgress _progress = new() { Dock = DockStyle.Bottom };
    private readonly Panel _logPanel = new() { Dock = DockStyle.Bottom, Height = 100 };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };

    // Details for one track.
    private readonly TableLayoutPanel _single = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12, 6, 12, 6) };
    private readonly Label _title = new() { AutoSize = true, MaximumSize = new Size(310, 0), Tag = Theme.TitleTag, Margin = new Padding(0, 0, 0, 2) };
    private readonly Label _fileLabel = new() { AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 2) };
    private readonly Label _note = new() { AutoSize = true, MaximumSize = new Size(310, 0), Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 10) };
    private readonly Label _sourceLabel = new() { AutoSize = true, MaximumSize = new Size(310, 0), Tag = Theme.MutedTag, Margin = new Padding(0, 4, 0, 8) };
    private readonly ModernButton _choose = new() { Text = "Choose song", Tag = Theme.AccentTag, Height = 28, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0) };
    private readonly ModernButton _remove = new() { Text = "Take off track", Height = 28, Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0) };
    private readonly ModernButton _fromWeb = new() { Text = "Find a song online", Height = 28, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 4, 0) };
    private readonly FieldBox _song = new("Song name") { Dock = DockStyle.Top };
    private readonly FieldBox _credits = new("Credits") { Dock = DockStyle.Top };
    private readonly ComboBox _loopMode = new FlatCombo { Dock = DockStyle.Top };
    private readonly LoopThumb _thumb = new() { Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 4) };
    private readonly Label _loopSummary = new() { AutoSize = true, MaximumSize = new Size(310, 0), Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 8) };
    private readonly ModernButton _editLoop = new() { Text = "Edit loop", Height = 28, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 8) };
    private readonly ModernButton _hearJump = new() { Text = "▶  Hear the jump", Height = 28, Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 8) };
    private readonly ComboBox _crossfade = new FlatCombo { Dock = DockStyle.Top };
    private readonly ComboBox _volume = new FlatCombo { Dock = DockStyle.Top, MaxDropDownItems = 14 };
    private readonly ComboBox _speed = new FlatCombo { Dock = DockStyle.Top, MaxDropDownItems = 14 };
    private readonly Label _finalCaption = new() { Text = "Different song on the final lap", AutoSize = true, Margin = new Padding(0, 12, 0, 4) };
    private readonly Label _finalLabel = new() { AutoSize = true, MaximumSize = new Size(310, 0), Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 6) };
    private readonly ModernButton _finalChoose = new() { Text = "Choose", Height = 28, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0) };
    private readonly ModernButton _finalRemove = new() { Text = "Remove", Height = 28, Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0) };
    private readonly Label _finalVolumeCaption = new() { Text = "Volume of this song", AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 8, 0, 3) };
    private readonly ComboBox _finalVolume = new FlatCombo { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 4), MaxDropDownItems = 14 };
    private readonly Label _finalSongSpeedCaption = new() { Text = "Speed of this song", AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 8, 0, 3) };
    private readonly ComboBox _finalSongSpeed = new FlatCombo { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 4), MaxDropDownItems = 14 };
    private readonly ModernButton _hearFinal = new() { Text = "▶  Hear the final lap", Height = 28, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
    private const string HearFinalText = "▶  Hear the final lap";
    private readonly Label _finalSpeedCaption = new() { Text = "Final lap speed", AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 8, 0, 3) };
    private readonly ComboBox _finalSpeed = new FlatCombo { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 4) };
    private readonly ComboBox _manyFinalSpeed = new FlatCombo { Width = 310, Margin = new Padding(0, 0, 0, 8) };

    private readonly List<Control> _songOnly = [];

    // Details for many tracks.
    private readonly FlowLayoutPanel _many = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12, 6, 12, 6), Visible = false };
    private readonly Label _manyTitle = new() { AutoSize = true, Tag = Theme.TitleTag, Margin = new Padding(0, 0, 0, 12) };

    public MainForm()
    {
        Text = "Music Pack Maker";
        Width = 1360;
        Height = 860;
        MinimumSize = new Size(1080, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.UiFont();
        AutoScaleMode = AutoScaleMode.Dpi;
        _library = new SongLibrary(() => DownloadFolder);

        BuildLayout();
        LoadBackdrop();
        ApplyTheme();
        SizeChanged += (_, _) => SizeBackdrop();

        _options.RetroRewindFolder = Catalog.DetectRetroRewind() ?? "";
        _autosave.Tick += (_, _) => SaveNow();
        LoadAutosave();

        FormClosing += (_, _) => SaveNow();
        Shown += (_, _) => ShowSetupIfNeeded();
        Shown += async (_, _) => await CheckForUpdateAsync();
        Activated += (_, _) => RunChecks(force: false);
        RunChecks(force: true);
    }

    /// <summary>The app can't convert songs without FFmpeg, so first-run setup comes before anything else.</summary>
    private void ShowSetupIfNeeded()
    {
        // RRMUSIC_DATA means an automated run, where nobody is there to answer.
        if (Environment.GetEnvironmentVariable("RRMUSIC_DATA") != null)
            return;
        if (!SetupForm.ShouldShow(_settings.SetupDone))
            return;

        using var setup = new SetupForm(_options, _settings);
        setup.ShowDialog(this);
        _settings.SetupDone = true;
        AppStorage.SaveSettings(_settings);
        MarkDirty();
        if (setup.FolderChanged)
            ReloadCatalog(Project.From(_options, _rows));
        RunChecks(force: true);
        _ = UpdateSizeAsync();
    }

    private async Task InstallFfmpegAsync()
    {
        _export.Enabled = false;
        _progress.Fraction = 0;
        try
        {
            string folder = await FfmpegInstaller.InstallAsync(
                message => OnWindow(() => SetStatus(message)),
                fraction => OnWindow(() => _progress.Fraction = fraction),
                CancellationToken.None);
            if (IsDisposed)
                return;
            SetStatus("FFmpeg is installed.");
            WriteLog($"FFmpeg folder: {folder}");
        }
        catch (Exception ex) when (ex is DownloadFailedException or HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or FileNotFoundException)
        {
            if (IsDisposed)
                return;
            SetStatus("FFmpeg didn't install. See the log for why.");
            WriteLog($"FFmpeg didn't install. {ex.Message}");
        }
        finally
        {
            if (!IsDisposed)
            {
                _export.Enabled = true;
                _progress.Fraction = 0;
            }
        }
    }

    private async Task CheckForUpdateAsync()
    {
        // RRMUSIC_DATA means an automated run, which shouldn't touch the network.
        if (!_settings.CheckForUpdates || Environment.GetEnvironmentVariable("RRMUSIC_DATA") != null)
            return;
        string? tag = await UpdateCheck.FindNewerAsync();
        if (tag == null || IsDisposed)
            return;
        _updateButton.Text = $"Update to {tag}";
        _updateButton.Visible = true;
    }

    /// <summary>Looks for setup problems and missing songs. Runs when the window regains focus, because that's when they get fixed.</summary>
    private void RunChecks(bool force)
    {
        if (!force && DateTime.UtcNow - _lastCheck < TimeSpan.FromSeconds(2))
            return;
        _lastCheck = DateTime.UtcNow;

        // Songs may have been added to or removed from the folder while the app was in the background.
        if (!_library.Songs.SequenceEqual(_songPaths))
            RefreshSongPicks();

        var issues = SetupCheck.Run(_options.RetroRewindFolder);
        string report = string.Join(Environment.NewLine, issues);
        if (report != _reportedIssues)
        {
            _reportedIssues = report;
            if (report.Length > 0)
                WriteLog(report);
        }

        int before = _missing.Count;
        _missing.Clear();
        foreach (var row in _rows.Where(r => r.IsAssigned && !File.Exists(r.Source)))
            _missing.Add(row);
        if (before != _missing.Count)
            ShowDetails();
        _grid.Invalidate();
        UpdateSummary();
    }

    private static ModernButton Header(string text) => new() { Text = text, AutoSize = true, Height = 26, Margin = new Padding(6, 0, 0, 0) };
    private static ModernButton Tab(string text) => new() { Text = text, AutoSize = true, Height = 28, Margin = new Padding(0), Tag = Theme.GhostTag };
    private static ModernButton Footer(string text) => new() { Text = text, AutoSize = true, Height = 28, Margin = new Padding(6, 0, 0, 0) };

    private void BuildLayout()
    {
        // App bar: name of the app and pack on the left, settings on the right.
        var header = new Panel { Dock = DockStyle.Top, Height = 42 };
        var headerLeft = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(10, 7, 0, 0) };
        var brand = new Label { Text = "Music Pack Maker", AutoSize = true, Font = Theme.TitleFont(10f), Margin = new Padding(0, 5, 18, 0) };
        _packName.Margin = new Padding(14, 0, 6, 0);
        var tabs = new SegmentedBar { Margin = new Padding(0, 0, 0, 0) };
        tabs.Controls.AddRange([_tabTracks, _tabSongs, _tabEditor]);
        headerLeft.Controls.AddRange([brand, tabs, _packPick, _packName, _author]);
        _packPick.SelectedIndexChanged += (_, _) => OnPackPicked();
        _tabTracks.Click += (_, _) => ShowTab(editor: false);
        _tabSongs.Click += (_, _) => ShowSongs();
        _tabEditor.Click += (_, _) => ShowTab(editor: true);
        _tabTracks.Tag = Theme.ActiveTag;
        var headerRight = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 8, 10, 0) };
        var settings = Header("Settings");
        headerRight.Controls.AddRange([settings, _deletePackButton, _saveButton]);
        _saveButton.Click += (_, _) => SaveProgress();
        _deletePackButton.Click += (_, _) => DeleteOpenPack();
        header.Controls.Add(headerRight);
        header.Controls.Add(headerLeft);
        settings.Click += (_, _) => OpenSettings();

        // Bottom bar.
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 42 };
        var footerRight = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 7, 10, 0) };
        footerRight.Controls.AddRange([_export, _importPack, _removeFromGame, _openFolder, _updateButton]);
        _removeFromGame.Visible = false;
        _removeFromGame.Click += (_, _) => RemovePackFromGame();
        _importPack.Click += async (_, _) => await PickPackToImportAsync();
        AudioPreview.Changed += ShowPreviewButtons;
        ShowPreviewButtons();
        _updateButton.Visible = false;
        _updateButton.Click += (_, _) => UpdateCheck.OpenPage();
        var footerLeft = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        footerLeft.Controls.Add(_status);
        footer.Controls.Add(footerLeft);
        footer.Controls.Add(footerRight);
        _export.Click += async (_, _) => await ExportPackAsync();
        _openFolder.Click += (_, _) => OpenBuildFolder();
        ShowInstallButtons(false);

        var logCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        logCard.Controls.Add(_log);
        _logPanel.Padding = new Padding(12, 10, 12, 0);
        _logPanel.Controls.Add(logCard);

        // Left card: where to look.
        var sidebarHolder = new Panel { Dock = DockStyle.Left, Width = 210, Padding = new Padding(0, 0, 6, 0) };
        var sidebarCard = new CardPanel { Dock = DockStyle.Fill };
        sidebarCard.Controls.Add(_sidebar);
        sidebarHolder.Controls.Add(sidebarCard);
        _sidebar.SelectedIndexChanged += (_, _) => SidebarChanged();

        // Middle card: the tracks.
        var centerCard = new CardPanel { Dock = DockStyle.Fill };
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 2, Padding = new Padding(2, 2, 2, 6) };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var addSongs = new ModernButton { Text = "Add songs", AutoSize = true, Margin = new Padding(8, 0, 0, 0), Dock = DockStyle.Fill };
        _filter.Margin = new Padding(0);
        searchRow.Controls.Add(_filter, 0, 0);
        searchRow.Controls.Add(addSongs, 1, 0);
        addSongs.Click += (_, _) => AddSongsByName();
        _filter.Box.TextChanged += (_, _) => RefreshGrid();
        var gridHolder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
        gridHolder.Controls.Add(_grid);
        centerCard.Controls.Add(gridHolder);
        centerCard.Controls.Add(_chips);
        centerCard.Controls.Add(searchRow);
        ConfigureGrid();

        // Right card: the selected track.
        var detailsHolder = new Panel { Dock = DockStyle.Right, Width = 384, Padding = new Padding(6, 0, 0, 0) };
        var detailsCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(4, 8, 4, 8) };
        var detailsScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Tag = Theme.PanelTag };
        BuildSinglePanel();
        BuildManyPanel();
        detailsScroll.Controls.Add(_many);
        detailsScroll.Controls.Add(_single);
        detailsCard.Controls.Add(detailsScroll);
        detailsHolder.Controls.Add(detailsCard);

        _body.Controls.Add(centerCard);
        _body.Controls.Add(detailsHolder);
        _body.Controls.Add(sidebarHolder);
        _library.Changed += RefreshSongPicks;
        RefreshSongPicks();
        _editor = new EditorPanel(() => DownloadFolder, message => _status.Text = message, WriteLog,
            path => _rows.Count(row => string.Equals(Path.GetFullPath(row.Source.Length > 0 ? row.Source : "x"), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)),
            SongDeleted, _library);
        _songsPage = new SongsPanel(_library, () => DownloadFolder, message => _status.Text = message, WriteLog, new SongsPanelActions(
            TracksUsing, RemoveSongFromTracks, _editor.ConfirmDeleteSong, OpenSongInEditor, ConfirmDeleteSongs, OpenDeletePacks,
            TracksUsingAny, RemoveSongsFromTracks, TrackCounts));
        _editor.SongsDeleted = SongsDeleted;

        Controls.Add(_body);
        Controls.Add(_editor);
        Controls.Add(_songsPage);
        Controls.Add(_logPanel);
        Controls.Add(_progress);
        Controls.Add(footer);
        Controls.Add(header);

        _packName.Box.TextChanged += (_, _) =>
        {
            MarkDirty();
            if (_packPick.Items.Count > 0 && !_switchingPack)
                _packPick.Items[0] = CurrentPackName;
        };

        // Renaming the open pack is typing in the name box. A name another pack already has gets a number when you leave the box.
        _packName.Box.Leave += (_, _) => KeepPackNameUnique();
        _author.Box.TextChanged += (_, _) => MarkDirty();
    }

    internal Task ShowTab(bool editor)
    {
        SwitchTo(editor ? _editor : _body, editor ? _tabEditor : _tabTracks);
        return editor ? _editor.ActivateAsync() : Task.CompletedTask;
    }

    internal void ShowSongs()
    {
        SwitchTo(_songsPage, _tabSongs);
        _songsPage.RefreshList();
    }

    private void SwitchTo(Panel page, ModernButton tab)
    {
        foreach (var other in new Panel[] { _body, _editor, _songsPage })
            other.Visible = other == page;
        foreach (var button in new[] { _tabTracks, _tabSongs, _tabEditor })
        {
            button.Tag = button == tab ? Theme.ActiveTag : Theme.GhostTag;
            button.Invalidate();
        }
    }

    private void OpenSongInEditor(string path)
    {
        var opening = ShowTab(editor: true);
        _ = opening.ContinueWith(_ => _editor.OpenSongAsync(path), TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
    }

    private List<TrackRow> RowsUsing(string path) => _rows.Where(row => SamePath(row.Source, path) || SamePath(row.FinalSource, path)).ToList();

    private static bool SamePath(string a, string b) =>
        a.Length > 0 && b.Length > 0 && Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private int TracksUsing(string path) => RowsUsing(path).Count;

    /// <summary>How many tracks use any of these songs. One pass over the tracks, however many songs there are.</summary>
    private int TracksUsingAny(IReadOnlyCollection<string> paths)
    {
        var set = new HashSet<string>(paths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        return _rows.Count(row => (row.Source.Length > 0 && set.Contains(Path.GetFullPath(row.Source)))
            || (row.FinalSource.Length > 0 && set.Contains(Path.GetFullPath(row.FinalSource))));
    }

    /// <summary>How many tracks use each song, worked out in one pass for the whole Songs list.</summary>
    private Dictionary<string, int> TrackCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows)
        {
            string? source = row.Source.Length > 0 ? Path.GetFullPath(row.Source) : null;
            string? final = row.FinalSource.Length > 0 ? Path.GetFullPath(row.FinalSource) : null;
            if (source != null)
                counts[source] = counts.GetValueOrDefault(source) + 1;
            if (final != null && !string.Equals(final, source, StringComparison.OrdinalIgnoreCase))
                counts[final] = counts.GetValueOrDefault(final) + 1;
        }
        return counts;
    }

    /// <summary>Tracks go back to their old music. The song itself stays in the list.</summary>
    private int RemoveSongFromTracks(string path) => RemoveSongsFromTracks([path]);

    /// <summary>Takes many songs off their tracks and refreshes the lists once, not once per song.</summary>
    private int RemoveSongsFromTracks(IReadOnlyCollection<string> paths)
    {
        var gone = new HashSet<string>(paths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        int changed = 0;
        foreach (var row in _rows)
        {
            bool hit = false;
            if (row.Source.Length > 0 && gone.Contains(Path.GetFullPath(row.Source)))
            {
                row.Source = "";
                row.Song = "";
                hit = true;
            }
            if (row.FinalSource.Length > 0 && gone.Contains(Path.GetFullPath(row.FinalSource)))
            {
                row.FinalSource = "";
                hit = true;
            }
            if (hit)
            {
                row.Status = "";
                changed++;
            }
        }
        if (changed > 0)
        {
            ShowDetails();
            RefreshAfterChange();
        }
        return changed;
    }

    /// <summary>Asks once, then deletes every picked song.</summary>
    private async void ConfirmDeleteSongs(IReadOnlyList<string> songs)
    {
        var others = songs.Where(song => !_library.IsOwned(song)).ToList();
        int tracks = TracksUsingAny(songs.ToList());
        var lines = new List<string> { $"Delete {songs.Count} songs? Ones you downloaded, made or got from an imported pack go to the Recycle Bin." };
        if (others.Count > 0)
            lines.Add($"{others.Count} {(others.Count == 1 ? "is a file" : "are files")} you imported from elsewhere.\r\n\r\nYes: delete those files too.\r\nNo: only take them off the list, and leave the files where they are.");
        if (tracks > 0)
            lines.Add($"{tracks} {(tracks == 1 ? "track uses" : "tracks use")} these songs and will go back to their old music. Packs you already made aren't affected.");
        var answer = MessageBox.Show(this, string.Join("\r\n\r\n", lines), "Delete songs",
            others.Count > 0 ? MessageBoxButtons.YesNoCancel : MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer == DialogResult.Yes)
            await _editor.DeleteSongsAsync(songs, deleteAllFiles: true);
        else if (answer == DialogResult.No && others.Count > 0)
            await _editor.DeleteSongsAsync(songs, deleteAllFiles: false);
    }

    /// <summary>Several songs went at once: the tracks that used them are cleared and the lists refreshed one time.</summary>
    private void SongsDeleted(IReadOnlyList<string> songs)
    {
        RemoveSongsFromTracks(songs.ToList());
        RunChecks(force: true);
        _songsPage.RefreshList();
    }

    /// <summary>A deleted song can't play any more, so it comes off the tracks that used it.</summary>
    private void SongDeleted(string path)
    {
        RemoveSongFromTracks(path);
        RunChecks(force: true);
        _songsPage.RefreshList();
    }

    private void SidebarChanged()
    {
        if (_sidebar.SelectedItem is not SideList.Item item)
            return;
        if (item.IsHeader)
        {
            // Section titles can't be picked, so stay where we were.
            _sidebar.SelectedIndex = _sidebarIndex;
            return;
        }
        _sidebarIndex = _sidebar.SelectedIndex;
        if (item.Name != _category)
        {
            _category = item.Name;
            _platform = "";
            RebuildChips();
        }
        RefreshGrid();
    }

    /// <summary>Console filter for the groups that have more than one console in them.</summary>
    private void RebuildChips()
    {
        foreach (Control old in _chips.Controls.Cast<Control>().ToList())
        {
            _chips.Controls.Remove(old);
            old.Dispose();
        }
        var platforms = _rows.Where(r => r.Category == _category && r.Platform.Length > 0)
            .Select(r => r.Platform).Distinct().OrderBy(Catalog.PlatformIndex).ToList();
        _chips.Visible = platforms.Count > 1;
        if (platforms.Count < 2)
            return;

        foreach (string platform in new[] { "" }.Concat(platforms))
        {
            string value = platform;
            var chip = new ModernButton
            {
                Text = value.Length == 0 ? "All" : value,
                IsPill = true,
                Height = 24,
                AutoSize = true,
                Margin = new Padding(0, 0, 6, 6),
                Tag = value == _platform ? Theme.ActiveTag : "",
            };
            chip.Click += (_, _) => PickPlatform(value);
            _chips.Controls.Add(chip);
        }
    }

    private void PickPlatform(string platform)
    {
        _platform = platform;
        int index = 0;
        foreach (ModernButton chip in _chips.Controls)
        {
            chip.Tag = (index == 0 ? "" : chip.Text) == platform ? Theme.ActiveTag : "";
            chip.Invalidate();
            index++;
        }
        RefreshGrid();
    }

    private void BuildSinglePanel()
    {
        _single.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _single.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        int row = 0;
        void Wide(Control control)
        {
            _single.Controls.Add(control, 0, row);
            _single.SetColumnSpan(control, 2);
            row++;
        }
        void Pair(Control left, Control right)
        {
            _single.Controls.Add(left, 0, row);
            _single.Controls.Add(right, 1, row);
            row++;
        }
        void Field(string caption, Control field)
        {
            var label = new Label { Text = caption, AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 6, 0, 3) };
            Wide(label);
            field.Margin = new Padding(0, 0, 0, 4);
            Wide(field);
            _songOnly.Add(label);
            _songOnly.Add(field);
        }

        void Section(string text)
        {
            var label = new Label { Text = text, AutoSize = true, Font = Theme.TitleFont(9.5f), Margin = new Padding(0, 14, 0, 2) };
            Wide(label);
            _songOnly.Add(label);
        }

        Wide(_title);
        Wide(_fileLabel);
        Wide(_note);
        Pair(_choose, _remove);
        Wide(_songPick);
        Pair(_fromWeb, _deleteTrackSong);
        Wide(_hearCurrent);
        Wide(_sourceLabel);
        Section("Loop");
        Field("Type", _loopMode);
        Wide(_thumb);
        Wide(_loopSummary);
        Pair(_editLoop, _hearJump);
        Section("Sound");
        Field("Smooth the jump", _crossfade);
        Field("Volume", _volume);
        Field("Speed", _speed);
        Wide(_finalCaption);
        Wide(_finalLabel);
        Pair(_finalChoose, _finalRemove);
        Wide(_finalVolumeCaption);
        Wide(_finalVolume);
        Wide(_finalSongSpeedCaption);
        Wide(_finalSongSpeed);
        Wide(_finalSpeedCaption);
        Wide(_finalSpeed);
        Wide(_hearFinal);
        Wide(_namesToggle);
        _songOnly.Add(_namesToggle);
        int beforeNames = _songOnly.Count;
        Field("Name", _song);
        Field("Credits", _credits);
        _nameFields.AddRange(_songOnly.Skip(beforeNames));
        _namesToggle.Click += (_, _) =>
        {
            _showNames = !_showNames;
            ShowDetails();
        };

        foreach (LoopMode mode in Enum.GetValues<LoopMode>())
            _loopMode.Items.Add(LoopModeText(mode));
        foreach (var (text, _) in PackBuilder.CrossfadeChoices)
            _crossfade.Items.Add(text);
        foreach (int db in PackBuilder.VolumeChoices)
            _volume.Items.Add(db == 0 ? "Normal" : $"{db:+0;-0} dB");
        foreach (var choice in PackBuilder.SpeedChoices)
            _speed.Items.Add(choice.Text);
        foreach (var (text, _) in PackBuilder.FinalSpeedChoices)
            _finalSpeed.Items.Add(text);
        _finalVolume.Items.Add("Same as the song");
        foreach (int db in PackBuilder.VolumeChoices)
            _finalVolume.Items.Add(db == 0 ? "Normal" : $"{db:+0;-0} dB");
        _finalSongSpeed.Items.Add("Same as the song");
        foreach (var choice in PackBuilder.SpeedChoices)
            _finalSongSpeed.Items.Add(choice.Text);

        _songPick.SelectedIndexChanged += (_, _) => PickSong(_songPick.SelectedIndex);
        _choose.Click += (_, _) => ChooseSource();
        _fromWeb.Click += (_, _) => GetFromWeb();
        _hearCurrent.Click += async (_, _) =>
        {
            if (IsHearing(_hearCurrent))
                AudioPreview.TogglePause();
            else if (_selected is { IsAssigned: true })
                await HearSongAsync();
            else
                await HearCurrentAsync();
        };
        _deleteTrackSong.Click += (_, _) =>
        {
            if (_selected is { IsAssigned: true } row)
                _editor.ConfirmDeleteSong(row.Source);
        };
        _remove.Click += (_, _) => ClearSongs(SelectedRows());
        _editLoop.Click += (_, _) => OpenLoopEditor();
        _hearJump.Click += async (_, _) =>
        {
            if (IsHearing(_hearJump))
                AudioPreview.TogglePause();
            else
                await HearJumpAsync();
        };
        _finalChoose.Click += (_, _) => ChooseFinal();
        _finalRemove.Click += (_, _) =>
        {
            if (_selected != null)
                _selected.FinalSource = "";
            ShowDetails();
            MarkDirty();
        };
        _hearFinal.Click += async (_, _) =>
        {
            if (IsHearing(_hearFinal))
                AudioPreview.TogglePause();
            else
                await HearFinalAsync();
        };
        _finalVolume.SelectedIndexChanged += (_, _) => DetailChanged(r => r.FinalVolumeDb = _finalVolume.SelectedIndex <= 0 ? null : PackBuilder.VolumeChoices[_finalVolume.SelectedIndex - 1]);
        _finalSongSpeed.SelectedIndexChanged += (_, _) => DetailChanged(r =>
        {
            int choice = _finalSongSpeed.SelectedIndex - 1;
            r.FinalSongSpeed = choice < 0 ? 0 : PackBuilder.SpeedChoices[choice].Speed;
            r.FinalSongKeepPitch = choice >= 0 && PackBuilder.SpeedChoices[choice].KeepPitch;
            r.FinalSongReverb = choice >= 0 && PackBuilder.SpeedChoices[choice].Reverb;
        });
        _finalSpeed.SelectedIndexChanged += (_, _) =>
        {
            DetailChanged(r => r.FinalSpeed = PackBuilder.FinalSpeedChoices[Math.Max(0, _finalSpeed.SelectedIndex)].Speed);
            HearChangeIfListening();
        };
        _song.Box.TextChanged += (_, _) => DetailChanged(r => r.Song = _song.Box.Text);
        _credits.Box.TextChanged += (_, _) => DetailChanged(r => r.Credits = _credits.Box.Text);
        _loopMode.SelectedIndexChanged += (_, _) => DetailChanged(r =>
        {
            r.Loop = (LoopMode)_loopMode.SelectedIndex;
            UpdateThumb();
        });
        _crossfade.SelectedIndexChanged += (_, _) => DetailChanged(r => r.Crossfade = PackBuilder.CrossfadeChoices[Math.Max(0, _crossfade.SelectedIndex)].Seconds);
        _volume.SelectedIndexChanged += (_, _) =>
        {
            DetailChanged(r => r.VolumeDb = PackBuilder.VolumeChoices[Math.Max(0, _volume.SelectedIndex)]);
            HearChangeIfListening();
        };
        _speed.SelectedIndexChanged += (_, _) =>
        {
            DetailChanged(r => SetSpeed(r, Math.Max(0, _speed.SelectedIndex)));
            if (!_loading)
                _ = UpdateSizeAsync();
            HearChangeIfListening();
        };
    }

    private static void SetSpeed(TrackRow row, int choice)
    {
        row.Speed = PackBuilder.SpeedChoices[choice].Speed;
        row.KeepPitch = PackBuilder.SpeedChoices[choice].KeepPitch;
        row.Reverb = PackBuilder.SpeedChoices[choice].Reverb;
    }

    private void BuildManyPanel()
    {
        ModernButton Wide(string text, string tag, Action click)
        {
            var button = new ModernButton { Text = text, Tag = tag, Width = 310, Height = 28, Margin = new Padding(0, 0, 0, 8) };
            button.Click += (_, _) => click();
            return button;
        }

        _many.Controls.Add(_manyTitle);
        _many.Controls.Add(Wide("Use one song for all", Theme.AccentTag, ChooseSource));
        _many.Controls.Add(_manySongPick);
        _manySongPick.SelectedIndexChanged += (_, _) => PickSong(_manySongPick.SelectedIndex);
        _many.Controls.Add(Wide("Find a song online", "", GetFromWeb));
        _many.Controls.Add(Wide("Fill from a folder of songs", "", FillFromFolder));
        _many.Controls.Add(Wide("Remove songs", "", () => ClearSongs(SelectedRows())));

        void Bulk(string caption, ComboBox combo)
        {
            _many.Controls.Add(new Label { Text = caption, AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 8, 0, 3) });
            _many.Controls.Add(combo);
        }
        _manyLoop.Items.Add(NoChange);
        foreach (LoopMode mode in BulkLoopModes)
            _manyLoop.Items.Add(LoopModeText(mode));
        _manyFade.Items.Add(NoChange);
        foreach (var (fade, _) in PackBuilder.CrossfadeChoices)
            _manyFade.Items.Add(fade);
        _manyVolume.Items.Add(NoChange);
        foreach (int db in PackBuilder.VolumeChoices)
            _manyVolume.Items.Add(db == 0 ? "Normal" : $"{db:+0;-0} dB");
        _manySpeed.Items.Add(NoChange);
        foreach (var choice in PackBuilder.SpeedChoices)
            _manySpeed.Items.Add(choice.Text);
        Bulk("Loop", _manyLoop);
        Bulk("Smooth the jump", _manyFade);
        Bulk("Volume", _manyVolume);
        var nudge = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        foreach (var (text, step) in new[] { ("Louder  +1 dB", 1), ("Quieter  −1 dB", -1) })
        {
            var button = new ModernButton { Text = text, Width = 151, Height = 28, Margin = new Padding(0, 0, 8, 0) };
            button.Click += (_, _) => ApplyToSelected(r => r.VolumeDb = PackBuilder.StepVolume(r.VolumeDb, step));
            _nudgeButtons.Add(button);
            nudge.Controls.Add(button);
        }
        _many.Controls.Add(nudge);
        Bulk("Speed", _manySpeed);
        _manyFinalSpeed.Items.Add(NoChange);
        foreach (var (text, _) in PackBuilder.FinalSpeedChoices)
            _manyFinalSpeed.Items.Add(text);
        Bulk("Final lap speed", _manyFinalSpeed);
        _manyFinalSpeed.SelectedIndexChanged += (_, _) =>
        {
            if (_manyFinalSpeed.SelectedIndex > 0 && !_loading)
                ApplyToSelected(r => r.FinalSpeed = PackBuilder.FinalSpeedChoices[_manyFinalSpeed.SelectedIndex - 1].Speed);
        };
        _manyLoop.SelectedIndexChanged += (_, _) =>
        {
            if (_manyLoop.SelectedIndex > 0)
                ApplyToSelected(r => r.Loop = BulkLoopModes[_manyLoop.SelectedIndex - 1]);
        };
        _manyFade.SelectedIndexChanged += (_, _) =>
        {
            if (_manyFade.SelectedIndex > 0)
                ApplyToSelected(r => r.Crossfade = PackBuilder.CrossfadeChoices[_manyFade.SelectedIndex - 1].Seconds);
        };
        _manyVolume.SelectedIndexChanged += (_, _) =>
        {
            if (_manyVolume.SelectedIndex > 0)
                ApplyToSelected(r => r.VolumeDb = PackBuilder.VolumeChoices[_manyVolume.SelectedIndex - 1]);
        };
        _manySpeed.SelectedIndexChanged += (_, _) =>
        {
            if (_manySpeed.SelectedIndex > 0 && !_loading)
            {
                ApplyToSelected(r => SetSpeed(r, _manySpeed.SelectedIndex - 1));
                _ = UpdateSizeAsync();
            }
        };
        _many.Controls.Add(new Label
        {
            Text = "Fill shuffles the folder's songs across these tracks, and reuses songs if there are fewer songs than tracks.",
            AutoSize = true,
            MaximumSize = new Size(310, 0),
            Tag = Theme.MutedTag,
            Margin = new Padding(0, 4, 0, 0),
        });
    }

    internal void ApplyToSelected(Action<TrackRow> apply)
    {
        if (_loading)
            return;
        foreach (var row in SelectedRows())
        {
            apply(row);
            row.Status = "";
        }
        _grid.Invalidate();
        MarkDirty();
    }

    /// <summary>Shows the shared value, or "Keep as they are" when the tracks differ.</summary>
    private void ShowManyValues(List<TrackRow> rows)
    {
        bool wasLoading = _loading;
        _loading = true;
        _manyLoop.SelectedIndex = rows.Select(r => r.Loop).Distinct().Count() == 1 ? Array.IndexOf(BulkLoopModes, rows[0].Loop) + 1 : 0;
        _manyFade.SelectedIndex = rows.Select(r => r.Crossfade).Distinct().Count() == 1 ? PackBuilder.CrossfadeIndex(rows[0].Crossfade) + 1 : 0;
        _manyVolume.SelectedIndex = rows.Select(r => r.VolumeDb).Distinct().Count() == 1 ? PackBuilder.VolumeIndex(rows[0].VolumeDb) + 1 : 0;
        _manySpeed.SelectedIndex = rows.Select(r => PackBuilder.SpeedIndex(r.Speed, r.KeepPitch, r.Reverb)).Distinct().Count() == 1 ? PackBuilder.SpeedIndex(rows[0].Speed, rows[0].KeepPitch, rows[0].Reverb) + 1 : 0;
        _manyFinalSpeed.SelectedIndex = rows.Select(r => r.FinalSpeed).Distinct().Count() == 1 ? PackBuilder.FinalSpeedIndex(rows[0].FinalSpeed) + 1 : 0;
        ShowPickedSong();
        _loading = wasLoading;
    }

    /// <summary>The file this track plays right now: from the pack if it's installed, otherwise Retro Rewind's own.</summary>
    internal string? CurrentMusicFile(TrackRow row)
    {
        if (_options.RetroRewindFolder.Length == 0)
            return null;
        foreach (string folder in new[] { "Patches", "strm" })
        {
            string path = Path.Combine(_options.RetroRewindFolder, folder, row.FileBase + ".brstm");
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private async Task HearCurrentAsync()
    {
        if (_selected == null || CurrentMusicFile(_selected) is not { } path)
            return;
        _playingButton = _hearCurrent;
        _playingRow = _selected;
        _hearingSong = false;
        SetStatus("Loading the current music...");
        try
        {
            string preview = await Task.Run(() =>
            {
                const int rate = 32000;
                return AudioIO.WriteRangePreview(AudioIO.Decode(path, rate), rate, 0, 30);
            });
            if (IsDisposed)
                return;
            if (PlayPreview(preview))
                SetStatus($"Playing 30 seconds of {Path.GetFileName(path)}. Press Esc to stop.");
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or IOException)
        {
            if (!IsDisposed)
                SetStatus($"Could not play it. {ex.Message}");
        }
    }

    /// <summary>True when this button is playing the selected track, so clicking it should pause. Another track starts fresh.</summary>
    private bool IsHearing(ModernButton button) =>
        AudioPreview.IsActive(this) && _playingButton == button && _playingRow == _selected;

    /// <summary>Plays the track's song the way it's set up, with its speed, reverb and volume.</summary>
    internal async Task HearSongAsync()
    {
        if (_selected is not { IsAssigned: true } row)
            return;
        if (!File.Exists(row.Source))
        {
            SetStatus("The song file is missing.");
            return;
        }
        int request = ++_hearRequest;
        _status.Text = "Getting the song ready...";
        try
        {
            var options = _options;
            string file = await Task.Run(() => PackBuilder.RenderPreview(options, row));
            if (IsDisposed || request != _hearRequest)
            {
                File.Delete(file);
                return;
            }
            _playingButton = _hearCurrent;
            _playingRow = row;
            _hearingSong = true;
            if (PlayPreview(file))
                _status.Text = $"Playing {row.Song} with its speed and volume settings. Press Esc to stop.";
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or FormatException or IOException or UnauthorizedAccessException)
        {
            if (!IsDisposed)
                SetStatus($"Could not play it. {ex.Message}");
        }
    }

    /// <summary>Changing a setting while listening starts the song again, so the change is heard straight away.</summary>
    private void HearChangeIfListening()
    {
        if (!_loading && IsHearing(_hearFinal))
        {
            _ = HearFinalAsync();
            return;
        }
        if (!_loading && _hearingSong && IsHearing(_hearCurrent))
            _ = HearSongAsync();
    }

    /// <summary>The button that started a track's music turns into Pause and Resume while it plays. Esc stops it.</summary>
    private void ShowPreviewButtons()
    {
        bool active = AudioPreview.IsActive(this);
        string pauseText = AudioPreview.State == PreviewState.Paused ? "▶  Resume" : "❚❚  Pause";
        _hearCurrent.Text = IsHearing(_hearCurrent) ? pauseText : _selected is { IsAssigned: true } ? HearSongText : HearCurrentText;
        _hearJump.Text = IsHearing(_hearJump) ? pauseText : HearJumpText;
        _hearFinal.Text = IsHearing(_hearFinal) ? pauseText : HearFinalText;
        if (active)
            _status.Text = AudioPreview.State == PreviewState.Paused ? "Paused." : "Playing. Press Esc to stop.";
    }

    private bool PlayPreview(string file)
    {
        if (AudioPreview.Play(file, this))
            return true;
        SetStatus("Could not play the sound. Check your audio device.");
        return false;
    }

    private static void StopPreview() => AudioPreview.Stop();

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        HandleShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);

    /// <summary>Delete and Space only act on the track list, so they still type normally in text boxes.</summary>
    internal bool HandleShortcut(Keys key)
    {
        if (key == (Keys.Control | Keys.S))
        {
            SaveProgress();
            return true;
        }
        if (_editor.Visible)
        {
            if (_editor.HandleShortcut(key))
                return true;
            if (key != Keys.Escape)
                return false;
        }
        if (_songsPage.Visible)
        {
            if (_songsPage.HandleShortcut(key))
                return true;
            if (key != Keys.Escape)
                return false;
        }
        if (key == (Keys.Control | Keys.F))
        {
            _filter.Box.Focus();
            _filter.Box.SelectAll();
            return true;
        }
        if (key == Keys.Escape && AudioPreview.State != PreviewState.Stopped)
        {
            StopPreview();
            SetStatus("Stopped.");
            return true;
        }
        if (ActiveControl != _grid)
            return false;
        if (key == Keys.Delete)
        {
            var assigned = SelectedRows().Where(r => r.IsAssigned).ToList();
            if (assigned.Count > 0)
                ClearSongs(assigned);
            return true;
        }
        if (key == Keys.Space && AudioPreview.IsActive(this))
        {
            AudioPreview.TogglePause();
            return true;
        }
        if (key == Keys.Space && _selected != null)
        {
            if (_selected.IsAssigned && !_missing.Contains(_selected))
                _ = HearSongAsync();
            else
                _ = HearCurrentAsync();
            return true;
        }
        return false;
    }

    private void ConfigureGrid()
    {
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AllowUserToResizeColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.VirtualMode = true;
        _grid.ReadOnly = true;
        _grid.AllowDrop = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowTemplate.Height = Theme.Px(24);
        _grid.ColumnHeadersHeight = Theme.Px(24);
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;

        foreach (var (title, weight) in new[] { ("Track", 46), ("Song", 38), ("Status", 16) })
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });

        _grid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex >= _visible.Count)
                return;
            var row = _visible[e.RowIndex];
            e.Value = e.ColumnIndex switch
            {
                0 => row.Name,
                1 => row.Song.Length > 0 ? row.Song : row.IsAssigned ? Path.GetFileName(row.Source) : "",
                _ => StatusOf(row),
            };
        };
        _grid.CellPainting += (_, e) =>
        {
            if (e.ColumnIndex == 0 && e.RowIndex >= 0 && e.RowIndex < _visible.Count && e.Graphics != null)
            {
                e.Handled = true;
                PaintTrackName(e.Graphics, e.CellBounds, _visible[e.RowIndex], (e.State & DataGridViewElementStates.Selected) != 0);
                return;
            }

            // Status is short colored text. The row behind it was already painted.
            if (e.ColumnIndex != 2 || e.RowIndex < 0 || e.RowIndex >= _visible.Count)
                return;
            e.Handled = true;
            string status = StatusOf(_visible[e.RowIndex]);
            if (status.Length == 0 || e.Graphics == null)
                return;
            var p = Theme.Current;
            Color text = status is "Failed" or "Missing" ? p.Bad : status == "Ready" ? p.Muted : p.Accent;
            var area = new Rectangle(e.CellBounds.X + 6, e.CellBounds.Y, e.CellBounds.Width - 8, e.CellBounds.Height);
            TextRenderer.DrawText(e.Graphics, status, _grid.Font, area, text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        _grid.RowPrePaint += (_, e) =>
        {
            var p = Theme.Current;
            var row = e.RowBounds;
            using (var back = new SolidBrush(p.Panel))
                e.Graphics.FillRectangle(back, row);
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            var line = new Rectangle(row.X, row.Y, Math.Min(row.Width, _grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible)), row.Height);
            if (selected || e.RowIndex == _dropIndex)
            {
                using (var fill = new SolidBrush(p.Selection))
                    e.Graphics.FillRectangle(fill, line);
                Theme.SelectionBar(e.Graphics, row.X, row.Y, row.Height);
            }
            else if (e.RowIndex == _hoverRow)
            {
                using var fill = new SolidBrush(p.Hover);
                e.Graphics.FillRectangle(fill, line);
            }
            e.PaintParts = DataGridViewPaintParts.ContentForeground;
        };
        _grid.CellMouseEnter += (_, e) => SetHoverRow(e.RowIndex);

        // Hovering a track's name shows what it is, like where a fanfare plays, without taking up room in the list.
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.ColumnIndex == 0 && e.RowIndex >= 0 && e.RowIndex < _visible.Count)
                e.ToolTipText = _visible[e.RowIndex].Note;
        };
        _grid.MouseLeave += (_, _) => SetHoverRow(-1);
        _grid.SelectionChanged += (_, _) =>
        {
            // Closing the window clears the list, and the details panel is already being destroyed by then.
            if (Disposing || IsDisposed)
                return;
            var rows = SelectedRows();
            _selected = rows.Count > 0 ? rows[0] : null;
            ShowDetails();
        };
        _grid.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        _grid.DragOver += (_, e) =>
        {
            e.Effect = DragDropEffects.Copy;
            var point = _grid.PointToClient(new Point(e.X, e.Y));
            int index = _grid.HitTest(point.X, point.Y).RowIndex;
            if (index != _dropIndex)
            {
                _dropIndex = index;
                _grid.Invalidate();
            }
        };
        _grid.DragLeave += (_, _) =>
        {
            _dropIndex = -1;
            _grid.Invalidate();
        };
        _grid.DragDrop += (_, e) =>
        {
            int index = _dropIndex;
            _dropIndex = -1;
            _grid.Invalidate();
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                HandleDrop(files, index);
        };
    }

    /// <summary>
    /// Tracks start with their console in grey, so the list can be scanned by console at a glance.
    /// The console always takes the same width, which keeps the track names lined up.
    /// </summary>
    private void PaintTrackName(Graphics g, Rectangle cell, TrackRow row, bool selected)
    {
        var p = Theme.Current;
        string name = row.Name;
        int x = cell.X + 8;
        if (row.Platform.Length > 0 && name.StartsWith(row.Platform + " ", StringComparison.Ordinal))
        {
            _badgeWidth ??= TextRenderer.MeasureText("Wii U", _grid.Font).Width + 6;
            var badge = new Rectangle(x, cell.Y, _badgeWidth.Value, cell.Height);
            TextRenderer.DrawText(g, row.Platform, _grid.Font, badge, p.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            x = badge.Right + 4;
            name = name[(row.Platform.Length + 1)..];
        }
        var text = new Rectangle(x, cell.Y, cell.Right - x - 6, cell.Height);
        TextRenderer.DrawText(g, name, _grid.Font, text, selected ? p.SelectionText : p.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private int? _badgeWidth;

    private void SetHoverRow(int index)
    {
        if (index == _hoverRow)
            return;
        if (_hoverRow >= 0 && _hoverRow < _grid.RowCount)
            _grid.InvalidateRow(_hoverRow);
        _hoverRow = index;
        if (index >= 0)
            _grid.InvalidateRow(index);
    }

    private string StatusOf(TrackRow row)
    {
        if (_missing.Contains(row))
            return "Missing";
        if (row.Status.Length > 0)
            return row.Status;
        if (row.IsAssigned)
            return "Ready";
        return _inGame.Contains(row.FileBase) ? "In game" : "";
    }

    internal void HandleDrop(string[] files, int rowIndex)
    {
        var targets = SelectedRows();
        if (files.Length == 1 && !Directory.Exists(files[0]) && rowIndex >= 0 && rowIndex < _visible.Count)
        {
            var row = _visible[rowIndex];
            // Dropping on one of several selected tracks fills all of them.
            if (targets.Count > 1 && targets.Contains(row))
                foreach (var target in targets)
                    AssignSong(target, files[0]);
            else
                AssignSong(row, files[0]);
        }
        else
        {
            MatchFiles(files);
        }
        RefreshAfterChange();
    }

    internal List<TrackRow> SelectedRows()
    {
        var result = new List<TrackRow>();
        foreach (DataGridViewRow row in _grid.SelectedRows)
            if (row.Index >= 0 && row.Index < _visible.Count)
                result.Add(_visible[row.Index]);
        result.Reverse();
        return result;
    }

    private static string LoopModeText(LoopMode mode) => mode switch
    {
        LoopMode.Auto => "Auto: use the file's loop, or the whole song",
        LoopMode.WholeSong => "Whole song",
        LoopMode.Custom => "My own points (Edit loop)",
        LoopMode.FindLoop => "Find a seamless loop when building",
        _ => "No loop",
    };

    private Image? _backdropSource;

    /// <summary>Loads the background picture, if one is set and can be read. A bad file just means plain black.</summary>
    private void LoadBackdrop()
    {
        _backdropSource?.Dispose();
        _backdropSource = null;
        string path = Theme.BackgroundPath;
        if (path.Length > 0 && File.Exists(path))
        {
            try
            {
                // Read into memory, so the picture's file isn't locked while the app is open.
                using var stream = new MemoryStream(File.ReadAllBytes(path));
                using var image = Image.FromStream(stream);
                _backdropSource = new Bitmap(image);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or OutOfMemoryException)
            {
                WriteLog($"Could not show the background picture. {ex.Message}");
            }
        }
        SizeBackdrop();
    }

    /// <summary>Fills the window with the picture, cropped rather than stretched, and darkened so text stays readable.</summary>
    private void SizeBackdrop()
    {
        var old = BackgroundImage;
        if (_backdropSource == null || ClientSize.Width < 1 || ClientSize.Height < 1)
        {
            BackgroundImage = null;
            old?.Dispose();
            return;
        }
        var size = ClientSize;
        var picture = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(picture))
        {
            float scale = Math.Max((float)size.Width / _backdropSource.Width, (float)size.Height / _backdropSource.Height);
            float width = _backdropSource.Width * scale, height = _backdropSource.Height * scale;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(_backdropSource, (size.Width - width) / 2, (size.Height - height) / 2, width, height);
            using var shade = new SolidBrush(Color.FromArgb(140, Theme.Current.Window));
            g.FillRectangle(shade, 0, 0, size.Width, size.Height);
        }
        BackgroundImageLayout = ImageLayout.None;
        BackgroundImage = picture;
        old?.Dispose();
    }

    internal bool HasBackdropForTest => BackgroundImage != null;
    internal AppSettings SettingsForTest => _settings;

    private void ApplyTheme()
    {
        Theme.Apply(this, seeThrough: _backdropSource != null);
        _title.Font = _titleFont;
        _sidebar.ItemHeight = Theme.Px(24);
        _manyTitle.Font = _titleFont;
        _sidebar.Invalidate();
        _grid.Invalidate();
    }

    private void MarkDirty()
    {
        if (_loading)
            return;
        _autosave.Stop();
        _autosave.Start();
    }

    private void SaveNow()
    {
        _autosave.Stop();
        try
        {
            ReadNames();
            AppStorage.WriteAutosave(Project.From(_options, _rows));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WriteLog($"Autosave failed. {ex.Message}");
        }
    }

    private void ReadNames()
    {
        _options.PackName = _packName.Box.Text;
        _options.Author = _author.Box.Text;
    }

    // Packs you've put aside, each with its own tracks and songs. Opening another never changes them.
    private Dictionary<string, Project> _otherPacks = AppStorage.LoadPacks();
    private readonly ComboBox _packPick = new FlatCombo { Width = 170, Margin = new Padding(14, 0, 0, 0), MaxDropDownItems = 14 };
    private const string NewPackChoice = "+ New pack";
    private readonly ModernButton _saveButton = new() { Text = "Save", AutoSize = true, Height = 26, Margin = new Padding(6, 0, 0, 0) };
    private readonly ModernButton _deletePackButton = new() { Text = "Delete pack", AutoSize = true, Height = 26, Margin = new Padding(6, 0, 0, 0) };

    /// <summary>
    /// Saves the open pack right now, with its tracks, songs and settings. It's also saved by itself as you work and when the
    /// app closes, so this is only for peace of mind: nothing has to be exported to be kept.
    /// </summary>
    internal void SaveProgress()
    {
        SaveNow();
        AppStorage.SavePacks(_otherPacks);
        SetStatus($"Saved \"{CurrentPackName}\". It's kept for next time, so you don't need to export it to keep it.");
    }

    /// <summary>
    /// Deletes the open pack's tracks and settings, then opens another pack, or a new empty one if it was the only one.
    /// The song files stay in your songs list and on disk.
    /// </summary>
    internal void DeleteOpenPack()
    {
        ReadNames();
        string name = CurrentPackName;
        int tracks = _rows.Count(row => row.IsAssigned);
        bool sure = ConfirmDeleteOpenPackForTest ?? MessageBox.Show(this,
            $"Delete the pack \"{name}\"{(tracks > 0 ? $" and its {tracks} tracks" : "")}?\r\n\r\nIts tracks and settings are removed. The song files stay in your songs list and on your PC, and packs you already exported aren't touched.\r\n\r\nThis can't be undone.",
            "Delete pack", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        if (!sure)
            return;

        string? next = _otherPacks.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        Project project;
        if (next != null)
        {
            project = _otherPacks[next];
            _otherPacks.Remove(next);
            AppStorage.SavePacks(_otherPacks);
        }
        else
        {
            var options = new Options();
            CopyOptions(_options, options);
            options.PackName = "My Music Pack";
            project = new Project { Options = options };
        }
        OpenPack(project);
        SetStatus($"Deleted \"{name}\".");
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool? ConfirmDeleteOpenPackForTest { get; set; }
    private bool _switchingPack;

    private string CurrentPackName => string.IsNullOrWhiteSpace(_packName.Box.Text) ? "My Music Pack" : _packName.Box.Text.Trim();

    /// <summary>Lists the open pack first, then the ones set aside, then a way to start another.</summary>
    private void RefreshPackPick()
    {
        _switchingPack = true;
        _packPick.Items.Clear();
        _packPick.Items.Add(CurrentPackName);
        foreach (string name in _otherPacks.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            _packPick.Items.Add(name);
        _packPick.Items.Add(NewPackChoice);
        _packPick.SelectedIndex = 0;
        _switchingPack = false;
    }

    private void OnPackPicked()
    {
        if (_switchingPack || _packPick.SelectedIndex <= 0)
            return;
        string choice = _packPick.SelectedItem?.ToString() ?? "";
        if (choice == NewPackChoice)
            StartNewPack(null);
        else
            SwitchPack(choice);
    }

    internal void KeepPackNameUnique()
    {
        string typed = CurrentPackName;
        if (!_otherPacks.ContainsKey(typed))
            return;
        string name = typed;
        for (int number = 2; _otherPacks.ContainsKey(name); number++)
            name = $"{typed} {number}";
        _packName.Box.Text = name;
    }

    internal void RenameOpenPackForTest(string name)
    {
        _packName.Box.Text = name;
        KeepPackNameUnique();
    }

    private string UniquePackName(string wanted)
    {
        string name = wanted;
        for (int number = 2; name.Equals(CurrentPackName, StringComparison.OrdinalIgnoreCase) || _otherPacks.ContainsKey(name); number++)
            name = $"{wanted} {number}";
        return name;
    }

    /// <summary>
    /// Puts the open pack aside, with all its tracks and settings, unless nothing is set on it. Two packs never share
    /// a name, so one that has the same name as another is given a number, and nothing is ever written over.
    /// </summary>
    private void StashCurrentPack(string? openNext = null)
    {
        ReadNames();
        var project = Project.From(_options, _rows).Clone();
        if (project.Rows.Count == 0)
            return;
        string key = CurrentPackName;
        for (int number = 2; _otherPacks.ContainsKey(key) || key.Equals(openNext, StringComparison.OrdinalIgnoreCase); number++)
            key = $"{CurrentPackName} {number}";
        project.Options.PackName = key;
        _otherPacks[key] = project;
        AppStorage.SavePacks(_otherPacks);
    }

    /// <summary>Opens a pack that was put aside. The one that's open now is put aside in its place.</summary>
    internal void SwitchPack(string name)
    {
        if (!_otherPacks.TryGetValue(name, out var target))
            return;

        // The pack being opened leaves the list first, so a pack with the same name as the open one can't be written over.
        _otherPacks.Remove(name);
        StashCurrentPack(openNext: name);
        AppStorage.SavePacks(_otherPacks);
        OpenPack(target);
    }

    /// <summary>Puts the open pack aside and opens an empty one.</summary>
    internal void StartNewPack(string? name)
    {
        StashCurrentPack();
        var options = new Options();
        CopyOptions(_options, options);
        options.PackName = UniquePackName(name ?? "New pack");
        options.Author = _options.Author;
        OpenPack(new Project { Options = options });
    }

    private void OpenPack(Project project)
    {
        bool wasLoading = _loading;
        _loading = true;
        _switchingPack = true;
        ApplyProject(project);
        _switchingPack = false;
        _loading = wasLoading;
        RefreshPackPick();
        _category = "";
        SaveNow();
    }

    internal IEnumerable<string> PackChoicesForTest => _packPick.Items.Cast<object>().Select(item => item.ToString()!);
    internal void PickPackForTest(string name) => _packPick.SelectedIndex = _packPick.Items.IndexOf(name);

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal DialogResult? ImportChoiceForTest { get; set; }

    private void LoadAutosave()
    {
        _loading = true;
        Project? project = null;
        try
        {
            if (File.Exists(AppStorage.AutosavePath))
                project = Project.Load(AppStorage.AutosavePath);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            WriteLog($"Could not read the last session. {ex.Message}");
        }

        if (project != null)
            ApplyProject(project);
        else
        {
            _packName.Box.Text = _options.PackName;
            ReloadCatalog(null);
        }
        _loading = false;
        RefreshPackPick();
    }

    private void ApplyProject(Project project)
    {
        string detected = _options.RetroRewindFolder;
        CopyOptions(project.Options, _options);
        if (_options.RetroRewindFolder.Length == 0 || !Directory.Exists(_options.RetroRewindFolder))
            _options.RetroRewindFolder = detected;
        _packName.Box.Text = _options.PackName;
        _author.Box.Text = _options.Author;
        ReloadCatalog(project);
        RunChecks(force: true);
        _ = UpdateSizeAsync();
    }

    private static void CopyOptions(Options from, Options to)
    {
        to.PackName = from.PackName;
        to.Author = from.Author;
        to.RetroRewindFolder = from.RetroRewindFolder;
        to.SampleRate = from.SampleRate;
        to.Loudness = from.Loudness;
        to.MultiChannelSlots = from.MultiChannelSlots;
        to.StockFinalLapFiles = from.StockFinalLapFiles;
    }

    private void ReloadCatalog(Project? project)
    {
        _rows = Catalog.Build(_options.RetroRewindFolder, out string message);
        foreach (string missing in project?.ApplyTo(_rows) ?? [])
            WriteLog($"No track uses the file name {missing}. It was skipped.");
        WriteLog(message);
        _library.Add(_rows.SelectMany(row => new[] { row.Source, row.FinalSource }));
        ScanInstalled();
        RebuildSidebar(AllTracks);
    }

    private void ScanInstalled()
    {
        _inGame = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string patches = Path.Combine(_options.RetroRewindFolder, "Patches");
        if (Directory.Exists(patches))
            foreach (string file in Directory.GetFiles(patches, "*.brstm"))
                _inGame.Add(Path.GetFileNameWithoutExtension(file));

        // The strm folder holds Retro Rewind's own songs too, so only the ones this app put there count.
        string backups = PackBuilder.StrmBackupFolder(OutputFolder);
        if (Directory.Exists(backups))
            foreach (string name in PackBuilder.PlacedInStrm(backups))
                _inGame.Add(Path.GetFileNameWithoutExtension(name));

        // The way to take the pack out shows once there is something in the game to take out.
        _removeFromGame.Visible = _rows.Any(row => row.IsAssigned && _inGame.Contains(row.FileBase));
    }

    private void RebuildSidebar(string? select)
    {
        string current = select ?? (_sidebar.SelectedItem as SideList.Item)?.Name ?? AllTracks;
        _sidebar.BeginUpdate();
        _sidebar.Items.Clear();
        _sidebar.Items.Add(new SideList.Item(AllTracks, _rows.Count));
        _sidebar.Items.Add(new SideList.Item(MySongs, _rows.Count(r => r.IsAssigned)));
        foreach (var (title, categories) in Catalog.Groups)
        {
            var present = categories.Where(category => !Catalog.HiddenFromSidebar.Contains(category) && _rows.Any(r => r.Category == category)).ToList();
            if (present.Count == 0)
                continue;
            _sidebar.Items.Add(new SideList.Item(title, SideList.HeaderCount));
            foreach (string category in present)
                _sidebar.Items.Add(new SideList.Item(category, _rows.Count(r => r.Category == category)));
        }
        for (int i = 0; i < _sidebar.Items.Count; i++)
            if (_sidebar.Items[i] is SideList.Item { IsHeader: false } item && item.Name == current)
                _sidebar.SelectedIndex = i;
        if (_sidebar.SelectedIndex < 0)
            _sidebar.SelectedIndex = 0;
        _sidebar.EndUpdate();
        RefreshGrid();
    }

    internal void SelectCategory(string name)
    {
        for (int i = 0; i < _sidebar.Items.Count; i++)
            if (((SideList.Item)_sidebar.Items[i]).Name == name)
                _sidebar.SelectedIndex = i;
    }

    private void RefreshGrid()
    {
        string category = (_sidebar.SelectedItem as SideList.Item)?.Name ?? AllTracks;
        string[] words = _filter.Box.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _missing.Clear();
        foreach (var row in _rows.Where(r => r.IsAssigned && !File.Exists(r.Source)))
            _missing.Add(row);
        _visible = _rows.Where(r =>
            (category == AllTracks || (category == MySongs ? r.IsAssigned : r.Category == category))
            && (_platform.Length == 0 || r.Platform == _platform)
            && words.All(w => r.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || r.VariantOf.Contains(w, StringComparison.OrdinalIgnoreCase) || r.FileBase.Contains(w, StringComparison.OrdinalIgnoreCase)
                || r.Song.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
        _grid.RowCount = 0;
        _grid.RowCount = _visible.Count;
        _grid.Invalidate();
        UpdateSummary();
    }

    private void RefreshAfterChange()
    {
        RebuildSidebar(null);
        MarkDirty();
        _ = UpdateSizeAsync();
    }

    /// <summary>Works out song lengths in the background, so a big pack never holds up the window.</summary>
    private async Task UpdateSizeAsync()
    {
        var unknown = _rows.Where(r => r.IsAssigned && !_missing.Contains(r)).Select(r => r.Source)
            .Where(source => !_seconds.ContainsKey(source)).Distinct().ToList();
        if (unknown.Count > 0)
        {
            var found = await Task.Run(() =>
            {
                var lengths = new Dictionary<string, double>();
                foreach (string source in unknown)
                {
                    try
                    {
                        lengths[source] = AudioIO.Probe(source).Duration;
                    }
                    catch (Exception ex) when (ex is InvalidDataException or IOException or System.Text.Json.JsonException or FileNotFoundException)
                    {
                        lengths[source] = 0;
                    }
                }
                return lengths;
            });
            if (IsDisposed)
                return;
            foreach (var (source, seconds) in found)
                _seconds[source] = seconds;
        }

        _sizeEstimate = _rows.Where(r => r.IsAssigned && _seconds.ContainsKey(r.Source)).Sum(r => PackBuilder.EstimateBytes(
            _seconds[r.Source] / r.Speed,
            r.FixedRate ?? _options.SampleRate,
            _options.MultiChannelSlots ? r.StockChannels : 2,
            (r.StockFinalBase != null && _options.StockFinalLapFiles) || PackBuilder.WritesPulsarFinal(r)));
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int songs = _rows.Count(r => r.IsAssigned);
        if (_status.Text.Length != 0 && !_status.Text.Contains(" set"))
            return;
        if (songs == 0)
        {
            _status.Text = "No songs set yet. Drag a song onto a track to start.";
            return;
        }
        string size = _sizeEstimate > 0 ? $", about {_sizeEstimate / 1048576.0:0} MB" : "";
        string missing = _missing.Count > 0 ? $", {_missing.Count} missing" : "";
        _status.Text = $"{songs} song{(songs == 1 ? "" : "s")} set{size}{missing}";
    }

    private void DetailChanged(Action<TrackRow> apply)
    {
        if (_loading || _selected == null)
            return;
        apply(_selected);
        _selected.Status = "";
        _grid.Invalidate();
        MarkDirty();
    }

    private void ShowDetails()
    {
        var rows = SelectedRows();
        bool many = rows.Count > 1;
        _many.Visible = many;
        _single.Visible = !many;
        if (many)
        {
            _manyTitle.Text = $"{rows.Count} tracks selected";
            ShowManyValues(rows);
            return;
        }

        bool wasLoading = _loading;
        _loading = true;
        var row = _selected;
        bool has = row?.IsAssigned == true;
        _title.Text = row?.Name ?? "Pick a track";
        _fileLabel.Text = row == null ? "" : row.FileBase + ".brstm";
        _note.Text = row?.Note ?? "";
        _sourceLabel.Text = !has ? "No song set. You can also drag one onto the track."
            : _missing.Contains(row!) ? $"Missing: {Path.GetFileName(row!.Source)}. Choose it again."
            : Path.GetFileName(row!.Source);
        _hearCurrent.Visible = row != null && (has || CurrentMusicFile(row) != null);
        _song.Box.Text = row?.Song ?? "";
        _credits.Box.Text = row?.Credits ?? "";
        _loopMode.SelectedIndex = (int)(row?.Loop ?? LoopMode.Auto);
        _crossfade.SelectedIndex = PackBuilder.CrossfadeIndex(row?.Crossfade ?? 0);
        _volume.SelectedIndex = PackBuilder.VolumeIndex(row?.VolumeDb ?? 0);
        _speed.SelectedIndex = PackBuilder.SpeedIndex(row?.Speed ?? 1, row?.KeepPitch ?? false, row?.Reverb ?? false);
        ShowPickedSong();
        _choose.Text = has ? "Change song" : "Choose song";
        _choose.Enabled = row != null;
        _fromWeb.Enabled = row != null;
        _remove.Visible = has;
        _deleteTrackSong.Visible = has;
        _single.SetColumnSpan(_fromWeb, has ? 1 : 2);
        _fromWeb.Margin = new Padding(0, 8, has ? 4 : 0, 0);
        _deleteTrackSong.Text = "Delete song";
        _single.SetColumnSpan(_choose, has ? 1 : 2);
        _choose.Margin = new Padding(0, 0, has ? 4 : 0, 0);
        foreach (Control control in _songOnly.Concat(new Control[] { _editLoop, _hearJump }))
            control.Visible = has;
        foreach (Control control in _nameFields)
            control.Visible = has && _showNames;
        _namesToggle.Text = _showNames ? "Hide name and credits" : "Name and credits";
        ShowPreviewButtons();

        bool canHaveFinal = row != null && (row.StockFinalBase != null || row.PulsarFinalBase != null);
        foreach (Control control in new Control[] { _finalCaption, _finalLabel, _finalChoose, _finalRemove, _finalSpeedCaption, _finalSpeed, _hearFinal })
            control.Visible = canHaveFinal && has;

        // Volume and speed of the song picked for the final lap only make sense once there is one.
        bool hasFinalSong = canHaveFinal && has && row!.FinalSource.Length > 0;
        foreach (Control control in new Control[] { _finalVolumeCaption, _finalVolume, _finalSongSpeedCaption, _finalSongSpeed })
            control.Visible = hasFinalSong;
        _finalVolume.SelectedIndex = row?.FinalVolumeDb is int finalDb ? PackBuilder.VolumeIndex(finalDb) + 1 : 0;
        _finalSongSpeed.SelectedIndex = row is { FinalSongSpeed: > 0 } ? PackBuilder.SpeedIndex(row.FinalSongSpeed, row.FinalSongKeepPitch, row.FinalSongReverb) + 1 : 0;
        _finalLabel.Text = row?.FinalSource.Length > 0 ? Path.GetFileName(row.FinalSource) : "None. The final lap plays this song, sped up.";
        _finalSpeed.SelectedIndex = PackBuilder.FinalSpeedIndex(row?.FinalSpeed ?? PackBuilder.GameFinalSpeed);
        _finalRemove.Enabled = row?.FinalSource.Length > 0;
        _loading = wasLoading;
        UpdateThumb();
    }

    private async void UpdateThumb()
    {
        if (Disposing || IsDisposed)
            return;
        var row = _selected;
        _thumb.Visible = row?.IsAssigned == true && !_missing.Contains(row);
        _loopSummary.Visible = _thumb.Visible;
        if (row == null || !row.IsAssigned || _missing.Contains(row))
            return;
        _thumb.Show([], 0, 1, false);
        _loopSummary.Text = "Reading the song...";
        try
        {
            string path = row.Source;
            if (!_overviews.TryGetValue(path, out var task))
                _overviews[path] = task = Task.Run(() => SongOverview.Read(path));
            var overview = await task;
            if (Disposing || IsDisposed)
                return;
            if (InvokeRequired)
            {
                // The song is read now, so running again on the window's thread finishes at once.
                OnWindow(UpdateThumb);
                return;
            }
            if (!ReferenceEquals(row, _selected))
                return;
            var (loop, start, end) = overview.LoopFor(row);
            _thumb.Show(overview.Peaks, start / Math.Max(0.001, overview.Seconds), end / Math.Max(0.001, overview.Seconds), loop);
            _loopSummary.Text = loop
                ? $"Loops {AudioIO.FormatTime(start)} to {AudioIO.FormatTime(end)} of {AudioIO.FormatTime(overview.Seconds)}"
                : row.Loop == LoopMode.FindLoop ? "The loop is found when you build." : "Plays once, then stops.";
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or FormatException or IOException)
        {
            if (Disposing || IsDisposed || InvokeRequired)
                return;
            _overviews.Remove(row.Source);
            if (ReferenceEquals(row, _selected))
                _loopSummary.Text = $"Could not read this song. {ex.Message}";
        }
    }

    private void AssignSong(TrackRow row, string file)
    {
        if (!PackBuilder.IsAudioFile(file))
        {
            WriteLog($"Not an audio file: {file}");
            return;
        }
        row.Source = file;
        row.KeepFile = false;
        row.Status = "";
        row.Song = SongLibrary.DisplayName(file);
        _library.Add(file);
    }

    /// <summary>Fills the dropdowns from the song list. Picking from them is the quick way to reuse a song.</summary>
    private void RefreshSongPicks()
    {
        bool wasLoading = _loading;
        _loading = true;
        // Songs copied in with an imported pack are already on their tracks, so they stay out of the dropdown.
        // The Songs tab still lists every song.
        // The one exception is the song on the track you're looking at, which has to show so you can see what it is.
        _songPaths = _library.Songs.Where(song => !_library.IsFromImportedPack(song)
            || (_pinnedSong != null && song.Equals(_pinnedSong, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var combo in new[] { _songPick, _manySongPick })
        {
            combo.BeginUpdate();
            combo.Items.Clear();
            combo.Items.Add(_songPaths.Count > 0 ? "Pick from your songs" : "No songs yet. Import or download one.");
            foreach (string song in _songPaths)
                combo.Items.Add(PickLabel(song));
            combo.EndUpdate();
            combo.Enabled = _songPaths.Count > 0;
        }
        ShowPickedSong();
        _loading = wasLoading;
    }

    /// <summary>Shows the song the selected tracks share, or the prompt when they differ or have none.</summary>
    private string? _pinnedSong;

    /// <summary>
    /// A song's name in the dropdown. A pack's files are named by track slot, like 6_N, so the song on the track you're
    /// looking at is shown by its title when it has one, with the file name after it.
    /// </summary>
    private string PickLabel(string song)
    {
        if (_pinnedSong != null && song.Equals(_pinnedSong, StringComparison.OrdinalIgnoreCase)
            && SelectedRows().FirstOrDefault()?.Song is { Length: > 0 } title
            && !title.Equals(Path.GetFileNameWithoutExtension(song), StringComparison.OrdinalIgnoreCase))
            return $"{title}  ({Path.GetFileNameWithoutExtension(song)})";
        return _library.LabelOf(song);
    }

    private void ShowPickedSong()
    {
        var sources = SelectedRows().Select(row => row.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // A song from an imported pack isn't in the dropdown, but the track that uses it still shows it.
        string? shared = sources.Count == 1 && sources[0].Length > 0 ? Path.GetFullPath(sources[0]) : null;
        string? wanted = shared != null && _library.IsFromImportedPack(shared) && File.Exists(shared) ? shared : null;
        if (!string.Equals(wanted, _pinnedSong, StringComparison.OrdinalIgnoreCase))
        {
            _pinnedSong = wanted;
            RefreshSongPicks();
            return;
        }
        int index = sources.Count == 1 && sources[0].Length > 0
            ? _songPaths.FindIndex(song => song.Equals(Path.GetFullPath(sources[0]), StringComparison.OrdinalIgnoreCase)) + 1
            : 0;
        _songPick.SelectedIndex = index;
        _manySongPick.SelectedIndex = index;
    }

    private void PickSong(int choice)
    {
        if (_loading || choice <= 0 || choice > _songPaths.Count)
            return;
        var targets = SelectedRows();
        if (targets.Count == 0)
            return;
        string path = _songPaths[choice - 1];
        if (!File.Exists(path))
        {
            SetStatus("That song file is gone.");
            RefreshSongPicks();
            return;
        }
        foreach (var row in targets)
            AssignSong(row, path);
        SetStatus($"Set {SongLibrary.DisplayName(path)} on {targets.Count} {(targets.Count == 1 ? "track" : "tracks")}.");
        ShowDetails();
        RefreshAfterChange();
    }

    private void ChooseSource()
    {
        var targets = SelectedRows();
        if (targets.Count == 0)
            return;
        string title = targets.Count == 1 ? $"Song for {targets[0].Name}" : $"Song for {targets.Count} tracks";
        using var dialog = new OpenFileDialog { Filter = AudioFilter, Title = title };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        foreach (var row in targets)
            AssignSong(row, dialog.FileName);
        ShowDetails();
        RefreshAfterChange();
    }

    private string DownloadFolder =>
        Path.Combine(_settings.OutputFolder.Length > 0 ? _settings.OutputFolder : AppStorage.DefaultOutputFolder, "Downloaded songs");

    private void GetFromWeb()
    {
        var targets = SelectedRows();
        if (targets.Count == 0)
            return;
        using var dialog = new DownloadForm(DownloadFolder, targets.Count);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is not { } song)
            return;
        foreach (var row in targets)
        {
            AssignSong(row, song.Path);
            row.Song = song.Title;
            if (row.Credits.Length == 0)
                row.Credits = song.Artist;
        }
        SetStatus($"Downloaded {song.Title}.");
        ShowDetails();
        RefreshAfterChange();
    }

    private void ClearSongs(List<TrackRow> rows)
    {
        foreach (var row in rows)
        {
            row.Source = "";
            row.FinalSource = "";
            row.Song = "";
            row.Status = "";
        }
        ShowDetails();
        RefreshAfterChange();
    }

    private void FillFromFolder()
    {
        var targets = SelectedRows();
        using var dialog = new FolderBrowserDialog { Description = "Pick a folder of songs", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        var files = Directory.GetFiles(dialog.SelectedPath, "*", SearchOption.AllDirectories).Where(PackBuilder.IsAudioFile).ToList();
        if (files.Count == 0)
        {
            SetStatus("That folder has no songs.");
            return;
        }
        _library.Add(files);
        PackBuilder.FillFromFolder(files, targets, Random.Shared);
        SetStatus($"Spread {files.Count} songs across {targets.Count} tracks.");
        RefreshAfterChange();
    }

    private void ChooseFinal()
    {
        if (_selected == null)
            return;
        using var dialog = new OpenFileDialog { Filter = AudioFilter, Title = $"Final lap song for {_selected.Name}" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _selected.FinalSource = dialog.FileName;
            _library.Add(dialog.FileName);
            _selected.Status = "";
            ShowDetails();
            MarkDirty();
        }
    }

    private async Task PickPackToImportAsync()
    {
        string? chosen;
        string[]? files;
        using (var form = new ImportPackForm(PackImporter.FindInstalled(_options.RetroRewindFolder, PackBuilder.ModsFolders(), OutputFolder), ConfirmDeletePack))
        {
            form.ShowDialog(this);
            chosen = form.Chosen;
            files = form.ChosenFiles;
        }
        if (chosen != null)
            await ImportPackAsync(chosen);
        else if (files != null)
            ImportFiles(files);
    }

    /// <summary>True for a Retro Rewind strm folder, the one the app adds songs to or any other install's.</summary>
    internal bool IsGameMusicFolder(string path)
    {
        if (!Directory.Exists(path))
            return false;
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (_options.RetroRewindFolder.Length > 0
            && full.Equals(Path.GetFullPath(PackBuilder.StrmFolder(_options.RetroRewindFolder)).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return true;
        string? parent = Path.GetDirectoryName(full);
        return Path.GetFileName(full).Equals("strm", StringComparison.OrdinalIgnoreCase) && parent != null
            && Directory.Exists(Path.Combine(parent, "Binaries"));
    }

    /// <summary>A window for deleting packs only, so it isn't part of importing or exporting.</summary>
    private void OpenDeletePacks()
    {
        using var form = new ImportPackForm(PackImporter.FindInstalled(_options.RetroRewindFolder, PackBuilder.ModsFolders(), OutputFolder), ConfirmDeletePack, deleteOnly: true);
        form.ShowDialog(this);
    }

    private string OutputFolder => _settings.OutputFolder.Length > 0 ? _settings.OutputFolder : AppStorage.DefaultOutputFolder;

    private async Task<bool> ConfirmDeletePack(InstalledPack pack)
    {
        var plan = PackImporter.DeletePlan(pack);
        int tracks = TracksUsingAny(plan.Songs);
        string songs = $"{plan.Songs.Length} {(plan.Songs.Length == 1 ? "song" : "songs")}";
        string name = Path.GetFileName(pack.Folder);
        string message = plan.WholeFolder
            ? $"Delete \"{name}\" and its {songs}? It goes to the Recycle Bin."
            : $"Delete the {songs} in \"{name}\"? They go to the Recycle Bin.";
        if (!plan.WholeFolder)
            message += plan.OtherFiles > 0
                ? $"\r\n\r\nIts {plan.OtherFiles} other {(plan.OtherFiles == 1 ? "file stays" : "files stay")}, like karts or tracks."
                : "\r\n\r\nAny other files there stay.";
        if (!pack.IsMod && !pack.MadeByApp)
            message += "\r\n\r\nWheel Wizard puts songs back here at launch from any mod that's turned on, so remove those mods too.";
        if (tracks > 0)
            message += $"\r\n\r\n{tracks} {(tracks == 1 ? "track uses" : "tracks use")} these songs and will go back to their old music.";
        if (MessageBox.Show(this, message, "Delete pack", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return false;
        return await DeletePackAsync(pack);
    }

    /// <summary>Sends a pack's songs to the Recycle Bin, and clears them from tracks, the list and the timeline.</summary>
    internal bool DeletePack(InstalledPack pack)
    {
        var (removed, error) = DeleteFiles(pack);
        return FinishDelete(pack, removed, error);
    }

    /// <summary>The same, but the slow part, sending hundreds of files to the Recycle Bin, runs off the window so it stays responsive.</summary>
    internal async Task<bool> DeletePackAsync(InstalledPack pack)
    {
        SetStatus($"Deleting {Path.GetFileName(pack.Folder)}... This can take a while for a big pack.");
        var (removed, error) = await Task.Run(() => DeleteFiles(pack));
        return !IsDisposed && FinishDelete(pack, removed, error);
    }

    /// <summary>Only touches files, so it's safe to run in the background. Returns what went, and why it stopped if it did.</summary>
    private (List<string> Removed, string? Error) DeleteFiles(InstalledPack pack)
    {
        var removed = new List<string>();
        string? error = null;
        try
        {
            var plan = PackImporter.DeletePlan(pack);
            if (plan.WholeFolder)
            {
                RemoveFolder(pack.Folder);
                removed.AddRange(plan.Songs);
                string manifest = pack.Folder + ".build.json";
                if (pack.MadeByApp && File.Exists(manifest))
                    File.Delete(manifest);
            }
            else
            {
                foreach (string song in plan.Songs)
                {
                    _editor.RemoveFile(song);
                    removed.Add(song);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            error = ex.Message;
        }
        return (removed, error);
    }

    private bool FinishDelete(InstalledPack pack, List<string> removed, string? error)
    {
        bool worked = error == null;
        if (!worked)
        {
            SetStatus("Could not delete all of that pack. See the log for why.");
            WriteLog($"Could not delete {pack.Folder}. {error}");
        }

        // Whatever did go has to leave everywhere, even if something else in the pack was stuck.
        RemoveSongsFromTracks(removed);
        if (removed.Count > 0)
        {
            _library.Remove(removed);
            _editor.RemovePartsOf(removed);
            ScanInstalled();
            RefreshGrid();
        }
        RunChecks(force: true);
        if (worked)
            SetStatus($"Deleted {removed.Count} {(removed.Count == 1 ? "song" : "songs")} from {Path.GetFileName(pack.Folder)}.");
        return worked;
    }

    /// <summary>The Recycle Bin keeps a mistake recoverable. Tests swap this out so they don't fill the real one.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<string> RemoveFolder { get; set; } = folder =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(folder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

    /// <summary>
    /// Single songs join the list, and any named after a track go on it. They stay where they are,
    /// because unlike a pack folder nothing here gets replaced when a pack is installed.
    /// </summary>
    internal void ImportFiles(string[] files, bool? replaceAssigned = null)
    {
        var audio = files.Where(PackBuilder.IsAudioFile).ToArray();
        _library.Add(audio);
        var plan = PackImporter.PlanFiles(audio, _rows);
        int assigned = 0;
        if (plan.Tracks > 0)
        {
            bool replace = replaceAssigned ?? true;
            if (replaceAssigned == null && plan.Conflicts > 0)
                replace = MessageBox.Show(this, $"{plan.Conflicts} of these tracks already have a song. Use the new song on them?", "Tracks already have songs",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            assigned = PackImporter.Assign(plan, plan.Matches.ToDictionary(match => match.File, match => match.File, StringComparer.OrdinalIgnoreCase), replace);
            ShowDetails();
            RefreshAfterChange();
            _ = ShowTab(editor: false);
            SelectCategory(MySongs);
        }
        SetStatus(plan.Tracks > 0
            ? $"Imported {audio.Length} {(audio.Length == 1 ? "song" : "songs")}, and {assigned} went on tracks with the same name."
            : $"Imported {audio.Length} {(audio.Length == 1 ? "song" : "songs")}. Pick them from the dropdown on a track.");
    }

    /// <summary>Brings a pack back in as songs on tracks. When tracks already have songs and no answer is given, it asks.</summary>
    internal async Task ImportPackAsync(string path, bool? replaceAssigned = null)
    {
        // Retro Rewind's own strm folder is full of the game's songs, named like tracks. Importing it would copy
        // hundreds of them into your songs, so only the songs this app put there are brought in.
        if (IsGameMusicFolder(path))
        {
            string backups = PackBuilder.StrmBackupFolder(OutputFolder);
            var mine = Directory.Exists(backups)
                ? PackBuilder.PlacedInStrm(backups).Select(name => Path.Combine(path, name)).Where(File.Exists).ToArray()
                : [];
            if (mine.Length == 0)
            {
                SetStatus("That's Retro Rewind's own music folder, so there's nothing of yours in it to import.");
                return;
            }
            WriteLog($"That's Retro Rewind's own music folder. Only the {mine.Length} songs you added to it are imported, not the game's.");
            // Copied out first, because the game's folder is where the next Add to strm writes.
            string copyFolder = PackImporter.NewFolder(OutputFolder, "Songs from strm");
            Directory.CreateDirectory(copyFolder);
            var copies = mine.Select(file =>
            {
                string copy = Path.Combine(copyFolder, Path.GetFileName(file));
                File.Copy(file, copy, true);
                return copy;
            }).ToArray();
            ImportFiles(copies, replaceAssigned);
            return;
        }

        bool isZip = File.Exists(path);
        string packName = Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));
        string output = OutputFolder;
        string? extracted = null;
        _status.Text = "Reading the pack...";
        try
        {
            var rows = _rows.ToList();
            string folder = isZip ? await Task.Run(() => PackImporter.ExtractZip(path)) : path;
            if (isZip)
                extracted = folder;
            var plan = await Task.Run(() => PackImporter.Plan(folder, rows));
            if (IsDisposed)
                return;
            foreach (var (file, spelled) in plan.Renamed)
                WriteLog($"{Path.GetFileName(file)} has a typo in its name, so the game wouldn't load it. It went on the {spelled} track, and your pack will use the right name.");
            foreach (string name in plan.Unmatched)
                WriteLog($"No track is named {name}. It's in your songs, so you can pick it from the dropdown on a track.");
            if (plan.Tracks == 0 && plan.Loose.Count == 0)
            {
                SetStatus("There are no .brstm songs in there.");
                return;
            }

            // A pack that's already open isn't overwritten. The new one opens on its own, and the old one waits in the pack list.
            if (replaceAssigned == null && plan.Tracks > 0 && _rows.Any(row => row.IsAssigned))
            {
                string open = CurrentPackName;
                var choice = ImportChoiceForTest ?? MessageBox.Show(this,
                    $"\"{open}\" is open with {_rows.Count(row => row.IsAssigned)} tracks set.\r\n\r\nYes: open \"{packName}\" as its own pack, and keep \"{open}\" as it is.\r\nNo: put its songs on the current tracks instead.\r\nCancel: don't import.",
                    "Import a pack", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (choice == DialogResult.Cancel)
                {
                    _status.Text = "";
                    return;
                }
                if (choice == DialogResult.Yes)
                {
                    // The empty pack has its own tracks, so the songs are matched to those instead.
                    StartNewPack(packName);
                    rows = _rows.ToList();
                    plan = await Task.Run(() => PackImporter.Plan(folder, rows));
                    replaceAssigned = true;
                }
            }

            bool replace = replaceAssigned ?? true;
            if (replaceAssigned == null && plan.Conflicts > 0)
            {
                var answer = MessageBox.Show(this,
                    $"{plan.Conflicts} of these tracks already have a song.\r\n\r\nYes: use the pack's song on them.\r\nNo: keep the songs they have, and only fill the empty tracks.\r\nCancel: don't import.",
                    "Tracks already have songs", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel)
                {
                    _status.Text = "";
                    return;
                }
                replace = answer == DialogResult.Yes;
            }

            _status.Text = "Copying the songs...";
            // Songs from an earlier import are already copies of their own, so a pack can come back without doubling up.
            bool alreadyCopied = PackImporter.IsImportedCopy(path, output);
            string destination = alreadyCopied ? path : PackImporter.NewFolder(output, packName);
            var copies = alreadyCopied
                ? plan.Matches.Select(match => match.File).Concat(plan.Loose).ToDictionary(file => file, file => file, StringComparer.OrdinalIgnoreCase)
                : await Task.Run(() => PackImporter.CopyFiles(plan, destination));
            if (IsDisposed)
                return;
            int assigned = PackImporter.Assign(plan, copies, replace);
            var songs = plan.Matches.Where(match => !match.IsFinal).Select(match => copies[match.File]).Concat(plan.Loose.Select(file => copies[file])).ToList();
            _library.Add(songs);
            ShowDetails();
            RefreshAfterChange();
            string extra = plan.Loose.Count > 0 ? $" {plan.Loose.Count} matched no track, so they're only in your songs." : "";
            if (plan.Tracks == 0)
            {
                // Nothing went on a track, so show where they did go instead of an empty list.
                ShowSongs();
                SetStatus($"{packName} has {plan.Loose.Count} {(plan.Loose.Count == 1 ? "song" : "songs")}, but none match a track by name. They're here in your songs.");
            }
            else
            {
                // Stock tracks and fanfares can be far down the list, so show what came in.
                _ = ShowTab(editor: false);
                SelectCategory(MySongs);
                SetStatus($"Imported {assigned} {(assigned == 1 ? "song" : "songs")} from {packName}. They're listed under {MySongs}.{extra}");
            }
            WriteLog($"Songs copied to {destination}. Your pack's original files weren't changed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (!IsDisposed)
            {
                SetStatus("Could not import that pack. See the log for why.");
                WriteLog($"Could not import {path}. {ex.Message}");
            }
        }
        finally
        {
            if (extracted != null)
            {
                try
                {
                    Directory.Delete(extracted, true);
                }
                catch (IOException)
                {
                    // It's in the temp folder, so Windows cleans it up later.
                }
            }
        }
    }

    private void AddSongsByName()
    {
        using var dialog = new OpenFileDialog { Filter = AudioFilter, Multiselect = true, Title = "Pick songs named after their tracks" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            MatchFiles(dialog.FileNames);
            RefreshAfterChange();
        }
    }

    private void MatchFiles(string[] paths)
    {
        var files = paths.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, "*", SearchOption.AllDirectories) : [p])
            .Where(PackBuilder.IsAudioFile).ToList();
        _library.Add(files);
        var (matched, unmatched) = PackBuilder.AutoMatch(files, _rows);
        SetStatus($"Matched {matched} of {files.Count} songs by file name.");
        foreach (string name in unmatched)
            WriteLog($"No single track matches: {name}. Drag it onto a track instead.");
    }

    private void OpenLoopEditor()
    {
        if (_selected is not { IsAssigned: true } row)
            return;
        using var editor = new LoopEditorForm(row);
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            ShowDetails();
            _grid.Invalidate();
            MarkDirty();
        }
    }

    /// <summary>Plays the track as it sounds on the final lap: its final lap song if it has one, sped up by the final lap speed.</summary>
    internal async Task HearFinalAsync()
    {
        if (_selected is not { IsAssigned: true } row)
            return;
        string source = row.FinalSource.Length > 0 ? row.FinalSource : row.Source;
        if (!File.Exists(source))
        {
            SetStatus("The song file is missing.");
            return;
        }
        _playingButton = _hearFinal;
        _playingRow = row;
        _hearingSong = false;
        SetStatus("Getting the final lap ready...");
        try
        {
            var options = _options;
            string file = await Task.Run(() => PackBuilder.RenderFinalPreview(options, row));
            if (IsDisposed)
                return;
            _playingButton = _hearFinal;
            _playingRow = row;
            if (PlayPreview(file))
                SetStatus($"Playing the final lap at {row.FinalSpeed:0.##}x. Press Esc to stop.");
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or FormatException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            if (!IsDisposed)
                SetStatus($"Could not play it. {ex.Message}");
        }
    }

    private async Task HearJumpAsync()
    {
        if (_selected is not { IsAssigned: true } row)
            return;
        if (!File.Exists(row.Source))
        {
            SetStatus("The song file is missing.");
            return;
        }
        var mode = row.Loop == LoopMode.NoLoop ? LoopMode.WholeSong : row.Loop;
        _playingButton = _hearJump;
        _playingRow = row;
        SetStatus("Preparing the jump...");
        try
        {
            int rate = row.FixedRate ?? _options.SampleRate;
            string file = await Task.Run(() =>
                AudioIO.WriteSeamPreview(Processing.Prepare(row.Source, rate, mode, row.LoopStart, row.LoopEnd, Options.LoudnessOff, crossfadeSeconds: row.Crossfade, speed: row.Speed, keepPitch: row.KeepPitch, reverb: row.Reverb)));
            if (IsDisposed)
                return;
            if (PlayPreview(file))
                SetStatus("Playing 4 seconds before the jump and 4 seconds after it.");
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or FormatException or IOException)
        {
            if (!IsDisposed)
                SetStatus($"Could not play it. {ex.Message}");
        }
    }

    private void OpenSettings()
    {
        int oldScale = _settings.UiScale;
        using var form = new SettingsForm(_options, _settings);
        if (form.ShowDialog(this) != DialogResult.OK)
            return;
        AppStorage.SaveSettings(_settings);
        ApplyLook();
        if (_settings.UiScale != oldScale && MessageBox.Show(this, "The new size shows once the app opens again. Restart it now?", "App size",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            SaveNow();
            Application.Restart();
            return;
        }
        if (form.FolderChanged)
        {
            var project = Project.From(_options, _rows);
            ReloadCatalog(project);
        }
        MarkDirty();
        RunChecks(force: true);
        _ = UpdateSizeAsync();
        if (form.WantsRemove)
            RemovePackFromGame();
        if (form.WantsFfmpeg)
            _ = InstallFfmpegAsync();
        if (form.WantsOpen)
            OpenProject();
        if (form.WantsSave)
            SaveProject();
    }

    /// <summary>Shows a new accent color or background straight away.</summary>
    internal void ApplyLook()
    {
        string oldBackground = Theme.BackgroundPath;
        Theme.SetLook(_settings.AccentColor, _settings.BackgroundImage, _settings.BackgroundColor);
        if (Theme.BackgroundPath != oldBackground)
            LoadBackdrop();
        ApplyTheme();
        Invalidate(true);
    }

    private void RemovePackFromGame()
    {
        ReadNames();
        string name = PackBuilder.SafeName(_options.PackName);
        var answer = ConfirmRemoveForTest is { } sure ? (sure ? DialogResult.Yes : DialogResult.No) : MessageBox.Show(this,
            $"Do you want to remove \"{name}\" from the game?\r\n\r\nIts Wheel Wizard mod and its songs will be removed, Retro Rewind's own songs in the strm folder are put back, and tracks go back to their old music.\r\n\r\nYour exported pack, your song files and this project stay.",
            "Remove pack", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return;
        try
        {
            string output = _settings.OutputFolder.Length > 0 ? _settings.OutputFolder : AppStorage.DefaultOutputFolder;
            string music = Path.Combine(output, name, "RetroRewind6", "Patches");
            int removed = PackBuilder.RemovePack(_options.PackName, PackBuilder.ModsFolders(), _options.RetroRewindFolder, music, PackBuilder.StrmBackupFolder(OutputFolder));
            foreach (var row in _rows)
                row.Status = "";
            ScanInstalled();
            _grid.Invalidate();
            SetStatus($"Removed {name} from the game, along with {removed} song files. Restart Wheel Wizard if it's open.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not remove the pack. See the log for why.");
            WriteLog($"Could not remove the pack. {ex.Message}");
        }
    }

    private void SaveProject()
    {
        ReadNames();
        using var dialog = new SaveFileDialog { Filter = "Music pack project|*.rrmusic.json", FileName = PackBuilder.SafeName(_options.PackName) + ".rrmusic.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        Project.From(_options, _rows).Save(dialog.FileName);
        SetStatus($"Saved {Path.GetFileName(dialog.FileName)}");
    }

    private void OpenProject()
    {
        using var dialog = new OpenFileDialog { Filter = "Music pack project|*.rrmusic.json|All files|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            OpenProjectFile(dialog.FileName);
    }

    public void OpenProjectFile(string path, bool showAssignedOnly = false)
    {
        try
        {
            _loading = true;
            ApplyProject(Project.Load(path));
            if (showAssignedOnly)
                SelectCategory(MySongs);
            _loading = false;
            SetStatus($"Opened {Path.GetFileName(path)}");
            MarkDirty();
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            _loading = false;
            SetStatus("Could not open the project.");
            WriteLog($"Could not open the project. {ex.Message}");
        }
    }

    /// <summary>Builds the songs and puts them in the game. Building first means a changed song can't be left out of date.</summary>
    internal async Task AddToPatchesAsync()
    {
        _lastBuildMusicFolder = null;
        await BuildPackAsync();
        if (IsDisposed || _lastBuildMusicFolder == null)
            return;
        string? archive = await PrepareSoundArchiveAsync();
        if (!IsDisposed)
            InstallToPatches(archive);
    }

    /// <summary>Asks where the pack should go, then sends it there.</summary>
    private async Task ExportPackAsync()
    {
        if (_rows.All(r => !r.IsAssigned))
        {
            SetStatus("Add a song to at least one track first.");
            return;
        }
        ExportChoice choice;
        using (var form = new ExportForm(Directory.Exists(_options.RetroRewindFolder), _settings.ExportForWii))
        {
            form.ShowDialog(this);
            choice = form.Choice;
            if (choice != ExportChoice.None)
            {
                // Remembered, and used for this build: a pack for a Wii has its songs brought down to what the console plays.
                _options.TargetWii = form.ForWii;
                _settings.ExportForWii = form.ForWii;
                AppStorage.SaveSettings(_settings);
            }
        }

        if (choice == ExportChoice.Game)
        {
            await AddToPatchesAsync();
        }
        else if (choice == ExportChoice.Zip)
        {
            ReadNames();
            using var dialog = new SaveFileDialog
            {
                Title = "Save the pack as a zip",
                Filter = "Zip files|*.zip",
                FileName = PackBuilder.SafeName(_options.PackName) + ".zip",
                InitialDirectory = Directory.Exists(_settings.ExportFolder) ? _settings.ExportFolder : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            _settings.ExportFolder = Path.GetDirectoryName(dialog.FileName) ?? "";
            AppStorage.SaveSettings(_settings);
            await ExportToZipAsync(dialog.FileName);
        }
        else if (choice == ExportChoice.Folder)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Where should the pack go?",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
                InitialDirectory = Directory.Exists(_settings.ExportFolder) ? _settings.ExportFolder : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            ReadNames();
            string target = Path.Combine(dialog.SelectedPath, PackBuilder.SafeName(_options.PackName));
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            {
                var answer = MessageBox.Show(this,
                    $"There's already a folder named \"{Path.GetFileName(target)}\" there.\r\n\r\nDo you want to update it? Its songs in RetroRewind6\\Patches will be replaced, and other files stay.",
                    "Update folder", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                    return;
            }
            _settings.ExportFolder = dialog.SelectedPath;
            AppStorage.SaveSettings(_settings);
            await ExportToFolderAsync(dialog.SelectedPath);
        }
    }

    /// <summary>Builds the songs and copies the pack to a folder. The game isn't touched.</summary>
    internal async Task ExportToFolderAsync(string destination)
    {
        _lastBuildMusicFolder = null;
        ShowInstallButtons(false);
        await BuildPackAsync();
        if (IsDisposed || _lastBuildMusicFolder == null || _lastBuildFolder == null)
            return;
        try
        {
            _lastExportFolder = PackBuilder.ExportFolder(_lastBuildFolder, destination);
            ShowInstallButtons(true);
            SetStatus($"Exported to {_lastExportFolder}.");
            WriteLog($"Exported pack: {_lastExportFolder}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not export. See the log for why.");
            WriteLog($"Could not export. {ex.Message}");
        }
    }

    /// <summary>Builds the songs and saves the pack as one zip. The game isn't touched.</summary>
    internal async Task ExportToZipAsync(string zipPath)
    {
        _lastBuildMusicFolder = null;
        ShowInstallButtons(false);
        await BuildPackAsync();
        if (IsDisposed || _lastBuildMusicFolder == null || _lastBuildFolder == null)
            return;
        string folder = _lastBuildFolder;
        try
        {
            await Task.Run(() => PackBuilder.ExportZip(folder, zipPath));
            if (IsDisposed)
                return;
            _lastExportFolder = Path.GetDirectoryName(zipPath);
            ShowInstallButtons(true);
            SetStatus($"Exported to {zipPath}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (IsDisposed)
                return;
            SetStatus("Could not export. See the log for why.");
            WriteLog($"Could not export. {ex.Message}");
        }
    }

    internal async Task BuildPackAsync()
    {
        if (_rows.All(r => !r.IsAssigned))
        {
            SetStatus("Add a song to at least one track first.");
            return;
        }
        if (!FfmpegInstaller.IsInstalled)
        {
            // Only asked for when it's really needed, so nobody has to install it just to look around.
            var install = MessageBox.Show(this, "Making a pack needs FFmpeg, which converts your songs for the game. It's a 77 MB download.\r\n\r\nInstall it now?",
                "FFmpeg needed", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (install != DialogResult.Yes)
                return;
            await InstallFfmpegAsync();
            if (IsDisposed || !FfmpegInstaller.IsInstalled)
                return;
        }
        string output = _settings.OutputFolder.Length > 0 ? _settings.OutputFolder : AppStorage.DefaultOutputFolder;
        ReadNames();
        _export.Enabled = false;
        _export.Enabled = false;
        _progress.Fraction = 0;
        SetStatus("Building...");
        var rows = _rows.ToList();
        try
        {
            Directory.CreateDirectory(output);
            var result = await Task.Run(() => PackBuilder.Build(_options, rows, output,
                message => OnWindow(() => WriteLog(message)),
                (done, total) => OnWindow(() =>
                {
                    _progress.Fraction = (double)done / total;
                    _grid.Invalidate();
                })));
            if (IsDisposed)
                return;
            _lastBuildMusicFolder = result.Built > 0 ? result.MusicFolder : null;
            _lastBuildFolder = result.Folder;
            _progress.Fraction = 0;
            string unchanged = result.Unchanged > 0 ? $", {result.Unchanged} unchanged" : "";
            SetStatus(result.Failed == 0
                ? $"Built {result.Built} songs{unchanged}, {result.Bytes / 1048576.0:0.0} MB."
                : $"Built {result.Built}, {result.Failed} failed. See the log for why.");
            if (result.Built > 0)
                WriteLog($"Pack: {result.Folder}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (IsDisposed)
                return;
            SetStatus("Build failed. See the log for why.");
            WriteLog($"Build failed. {ex.Message}");
        }
        finally
        {
            if (!IsDisposed)
                FinishBuildButtons();
        }
    }

    private void FinishBuildButtons()
    {
        {
            _export.Enabled = true;
            _export.Enabled = true;
            _grid.Invalidate();
        }
    }

    private void OnWindow(Action action)
    {
        if (!IsHandleCreated || IsDisposed)
            return;
        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window closed while the build was finishing.
        }
    }

    private void ShowInstallButtons(bool show)
    {
        foreach (var button in new[] { _openFolder })
            button.Visible = show;
    }

    /// <summary>Opens the folder the pack was exported to.</summary>
    private void OpenBuildFolder()
    {
        if (_lastExportFolder != null && Directory.Exists(_lastExportFolder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_lastExportFolder}\"") { UseShellExecute = true });
    }

    /// <summary>
    /// Puts the songs in Patches. If Wheel Wizard is installed the pack also becomes a mod, because
    /// that's what shows in its list, and it deletes files in Patches that no mod owns.
    /// </summary>
    private async Task<string?> PrepareSoundArchiveAsync()
    {
        if (!_settings.BoostMusicSlots)
            return null;
        _export.Enabled = false;
        try
        {
            return await Task.Run(() => SoundArchive.Prepare(_settings, message => OnWindow(() => SetStatus(message))));
        }
        catch (Exception ex) when (ex is DownloadFailedException or InvalidDataException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // The pack still works without it, only quieter, so this must not stop the install.
            if (!IsDisposed)
                WriteLog($"Music volume not raised. {ex.Message}");
            return null;
        }
        finally
        {
            if (!IsDisposed)
                _export.Enabled = true;
        }
    }

    private void InstallToPatches(string? archive = null)
    {
        if (_lastBuildMusicFolder == null)
            return;
        if (!Directory.Exists(_options.RetroRewindFolder))
        {
            SetStatus("Adding to the game needs your Retro Rewind folder. Set it in Settings, or use Export pack instead.");
            return;
        }
        var modsFolders = PackBuilder.ModsFolders();
        bool wheelWizard = modsFolders.Count > 0;
        try
        {
            ReadNames();
            var songs = Directory.GetFiles(_lastBuildMusicFolder, "*.brstm");
            List<string>? keep = null;
            if (wheelWizard)
            {
                var loose = PackBuilder.FindUnownedPatches(_options.RetroRewindFolder, modsFolders, songs.Append(SoundArchive.FileName));
                if (loose.Count > 0)
                {
                    string names = string.Join("\r\n", loose.Take(10).Select(Path.GetFileName));
                    var answer = MessageBox.Show(this,
                        "These files are in your Retro Rewind Patches folder, and no mod owns them:\r\n\r\n" + names
                        + "\r\n\r\nWheel Wizard deletes them at launch once any mod is enabled.\r\n\r\n"
                        + "Yes: save them in a mod named \"Kept Patches files\" so they keep working.\r\n"
                        + "No: install anyway. Wheel Wizard will delete them.\r\n"
                        + "Cancel: do nothing.",
                        "Files would be deleted", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                    if (answer == DialogResult.Cancel)
                        return;
                    if (answer == DialogResult.Yes)
                        keep = loose;
                }
            }


            foreach (string mods in modsFolders)
            {
                var shared = PackBuilder.ModsSharingSongs(_lastBuildMusicFolder, _options.PackName, mods);
                string folder = PackBuilder.InstallAsMod(_lastBuildMusicFolder, _options.PackName, _options.Author, mods, keep);
                SoundArchive.Place(archive, _settings.BoostMusicSlots, folder);
                WriteLog($"Mod: {folder}");
                if (shared.Count > 0)
                    WriteLog($"This pack now plays over the same songs in: {string.Join(", ", shared)}.");
            }
            int count = songs.Length;
            {
                // The songs go in Retro Rewind's strm folder. Its own songs are saved first, so removing the pack restores them.
                PackBuilder.CopyToStrm(_lastBuildMusicFolder, _options.RetroRewindFolder, PackBuilder.StrmBackupFolder(OutputFolder));
                SoundArchive.Place(archive, _settings.BoostMusicSlots, Path.Combine(_options.RetroRewindFolder, "Patches"));
            }

            ScanInstalled();
            _grid.Invalidate();
            WriteLog($"strm: {PackBuilder.StrmFolder(_options.RetroRewindFolder)}");
            WriteLog($"Retro Rewind's own songs that were replaced are saved in {PackBuilder.StrmBackupFolder(OutputFolder)}.");
            SetStatus(wheelWizard
                ? $"Done. Added {count} files to the strm folder. Restart Wheel Wizard."
                : $"Done. Added {count} files to the strm folder.");

            // The banner is gone, so a setting that would silence the pack still needs saying once it matters.
            var issues = SetupCheck.Run(_options.RetroRewindFolder).Where(issue => !issue.StartsWith("Dolphin", StringComparison.Ordinal)).ToList();
            if (issues.Count > 0)
            {
                SetStatus(issues[0]);
            }
            if (archive != null)
                WriteLog("Music slots raised to full volume with your own copy of revo_kart.brsar. It isn't in the exported pack.");
            WriteLog("No music change in game? Check Retro Rewind's Music setting and its Loose Archive Overrides setting.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not install. See the log for why.");
            WriteLog($"Could not install. {ex.Message}");
        }
    }

    private void SetStatus(string message)
    {
        _status.Text = message;
        // The log is always on screen, so the same message clicked again shouldn't fill it.
        if (message != _lastLogged)
            WriteLog(message);
    }

    private void WriteLog(string message)
    {
        _lastLogged = message;
        _log.AppendText(message + Environment.NewLine);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        AudioPreview.Changed -= ShowPreviewButtons;
        StopPreview();
        _autosave.Dispose();
        base.OnFormClosed(e);
    }

    // For the automated tests.
    internal IReadOnlyList<TrackRow> Rows => _rows;
    internal int VisibleCount => _visible.Count;

    internal void SelectVisible(params int[] indexes)
    {
        _grid.ClearSelection();
        foreach (int index in indexes)
            _grid.Rows[index].Selected = true;
    }

    internal int VisibleIndexForTest(TrackRow row) => _visible.IndexOf(row);
    internal IReadOnlyList<TrackRow> VisibleRowsForTest => _visible;
    internal IEnumerable<string> SidebarNamesForTest => _sidebar.Items.Cast<SideList.Item>().Select(item => item.Name);
    internal void SaveForTest() => SaveNow();
    internal void RunChecksForTest() => RunChecks(force: true);
    internal string StatusOfForTest(TrackRow row) => StatusOf(row);
    internal void FocusGridForTest()
    {
        ActiveControl = _grid;
    }
    internal Task UpdateSizeForTest() => UpdateSizeAsync();
    internal string StatusTextForTest => _status.Text;
    internal string? ExportedFolderForTest => _lastExportFolder;
    internal string ExportButtonTextForTest => _export.Text;
    internal bool ShowPackButtonVisibleForTest => _openFolder.Visible;
    internal ComboBox SpeedComboForTest => _speed;
    internal EditorPanel EditorForTest => _editor;
    internal SongsPanel SongsForTest => _songsPage;
    internal string HearCurrentTextForTest => _hearCurrent.Text;
    internal bool DeleteTrackSongVisibleForTest => _deleteTrackSong.Visible;
    internal string DeleteTrackSongTextForTest => _deleteTrackSong.Text;
    internal string NoteForTest => _note.Text;
    internal void SetPlayingHearCurrentForTest()
    {
        _playingButton = _hearCurrent;
        _playingRow = _selected;
    }
    internal bool SongNameVisibleForTest => _song.Visible;
    internal void ToggleNamesForTest() => _namesToggle.PerformClick();
    internal bool PlayPreviewForTest(string file) => PlayPreview(file);
    internal bool HasFooterButtonForTest(string text) => Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<FlowLayoutPanel>())
        .SelectMany(row => row.Controls.OfType<ModernButton>()).Any(button => button.Text.Contains(text));
    internal ComboBox SongPickForTest => _songPick;
    internal ComboBox ManySongPickForTest => _manySongPick;
    internal void SetStatusForTest(string message) => SetStatus(message);
    internal Control PackNameBoxForTest => _packName.Box;
    internal bool LogVisibleForTest => _logPanel.Visible;
    internal string LogTextForTest => _log.Text;
    internal IEnumerable<string> FooterButtonsForTest => _export.Parent!.Controls.Cast<Control>().Select(c => c.Text);
    internal ComboBox ManySpeedComboForTest => _manySpeed;
    internal ComboBox VolumeComboForTest => _volume;
    internal void NudgeVolumeForTest(int step) => _nudgeButtons[step > 0 ? 0 : 1].PerformClick();
    internal void RefreshDetailsForTest() => ShowDetails();
    internal ComboBox FinalSpeedComboForTest => _finalSpeed;
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool? ConfirmRemoveForTest { get; set; }
    internal bool RemoveFromGameVisibleForTest => _removeFromGame.Visible;
    internal void ClickRemoveFromGameForTest() => RemovePackFromGame();
    internal string HearFinalTextForTest => _hearFinal.Text;
    internal ComboBox FinalVolumeComboForTest => _finalVolume;
    internal ComboBox FinalSongSpeedComboForTest => _finalSongSpeed;
    internal bool FinalSongSettingsVisibleForTest => _finalVolume.Visible && _finalSongSpeed.Visible;
    internal ComboBox ManyFinalSpeedComboForTest => _manyFinalSpeed;
    internal void PickSidebarIndexForTest(int index) => _sidebar.SelectedIndex = index;
    internal object? SidebarSelectionForTest => _sidebar.SelectedItem;
    internal int SidebarCountForTest => _sidebar.Items.Count;
    internal void PickPlatformForTest(string platform) => PickPlatform(platform);
    internal int ChipCountForTest => _chips.Controls.Count;
    internal bool ManyPanelVisible => _many.Visible;
}

/// <summary>A small summary of a song for the details panel.</summary>
public sealed record SongOverview(float[] Peaks, double Seconds, double? TagStart, double? TagEnd, int SourceRate)
{
    private const int Rate = 8000;
    private const int PeakCount = 320;

    public static SongOverview Read(string path)
    {
        var info = AudioIO.Probe(path);
        var channels = AudioIO.Decode(path, Rate);
        int length = channels[0].Length;
        var peaks = new float[PeakCount];
        for (int i = 0; i < PeakCount; i++)
        {
            int from = (int)((long)i * length / PeakCount);
            int to = Math.Max(from + 1, (int)((long)(i + 1) * length / PeakCount));
            int peak = 0;
            for (int j = from; j < Math.Min(to, length); j++)
                peak = Math.Max(peak, Math.Abs(channels[0][j] + channels[1][j]) / 2);
            peaks[i] = peak / 32768f;
        }
        double? rate = info.SampleRate > 0 ? info.SampleRate : null;
        return new SongOverview(peaks, (double)length / Rate,
            info.LoopStart / rate, info.LoopEnd / rate, info.SampleRate);
    }

    /// <summary>The loop this row would use, in seconds.</summary>
    public (bool Loop, double Start, double End) LoopFor(TrackRow row)
    {
        switch (row.Loop)
        {
            case LoopMode.NoLoop:
            case LoopMode.FindLoop:
                return (false, 0, Seconds);
            case LoopMode.Custom:
                double start = (AudioIO.ParsePosition(row.LoopStart, SourceRate) ?? 0) / (double)Math.Max(1, SourceRate);
                double end = (AudioIO.ParsePosition(row.LoopEnd, SourceRate) ?? (long)(Seconds * SourceRate)) / (double)Math.Max(1, SourceRate);
                return (true, Math.Min(start, Seconds), Math.Min(end, Seconds));
            case LoopMode.Auto when row.DefaultNoLoop:
                return (false, 0, Seconds);
            case LoopMode.Auto when TagStart != null:
                return (true, TagStart.Value, Math.Min(TagEnd ?? Seconds, Seconds));
            default:
                return (true, 0, Seconds);
        }
    }
}

/// <summary>Build settings that rarely change, kept out of the main window.</summary>
public sealed class SettingsForm : Form
{
    private readonly Options _options;
    private readonly AppSettings _settings;
    private readonly FieldBox _rrFolder = new("Not needed. The built-in list is used.") { Width = 280 };
    private readonly FieldBox _output = new() { Width = 280 };
    private readonly FlatCheck _raceBoost = new() { Text = "Make race music louder", AutoSize = true };
    private readonly ComboBox _rate = new FlatCombo { Width = 200 };
    private readonly ComboBox _loudness = new FlatCombo { Width = 200 };
    private readonly FlatCheck _multi = new() { Text = "Multi-channel stock slots", AutoSize = true };
    private readonly FlatCheck _final = new() { Text = "Write stock final lap files (_f)", AutoSize = true };
    private readonly FlatCheck _updates = new() { Text = "Tell me when a new version is out", AutoSize = true };
    private readonly FlatCheck _boost = new() { Text = "Raise music volume in revo_kart.brsar", AutoSize = true };
    private readonly FieldBox _game = new("Only to raise the volume") { Width = 280 };
    private readonly ComboBox _scale = new FlatCombo { Width = 200 };
    private readonly FieldBox _background = new("None") { Width = 280 };
    private string _accent = "";
    private string _backColor = "";

    public bool FolderChanged { get; private set; }
    public bool WantsFfmpeg { get; private set; }
    public bool WantsOpen { get; private set; }
    public bool WantsRemove { get; private set; }
    public bool WantsSave { get; private set; }

    public SettingsForm(Options options, AppSettings settings)
    {
        _options = options;
        _settings = settings;
        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = Theme.UiFont();
        var tips = new ToolTip();
        Disposed += (_, _) => tips.Dispose();

        // Each setting is one line: its name, the field, then its buttons. Longer explanations are tooltips,
        // so the window stays short even at a big app size.
        var layout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 3, Padding = new Padding(14, 6, 14, 10) };
        int row = 0;
        void Heading(string text)
        {
            var label = new Label { Text = text, AutoSize = true, Font = Theme.TitleFont(9.5f), Margin = new Padding(0, row == 0 ? 2 : 12, 0, 2) };
            layout.Controls.Add(label, 0, row);
            layout.SetColumnSpan(label, 3);
            row++;
        }
        void Add(string caption, Control field, string tip, params Button[] buttons)
        {
            var label = new Label { Text = caption, AutoSize = true, Tag = Theme.MutedTag, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 12, 3) };
            if (tip.Length > 0)
            {
                tips.SetToolTip(label, tip);
                tips.SetToolTip(field, tip);
            }
            field.Margin = new Padding(0, 3, 0, 3);
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(field, 1, row);
            if (buttons.Length > 0)
            {
                var holder = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 3) };
                holder.Controls.AddRange(buttons);
                layout.Controls.Add(holder, 2, row);
            }
            row++;
        }
        void Check(FlatCheck check, string tip = "")
        {
            check.Margin = new Padding(0, 2, 0, 2);
            if (tip.Length > 0)
                tips.SetToolTip(check, tip);
            layout.Controls.Add(check, 1, row);
            layout.SetColumnSpan(check, 2);
            row++;
        }

        // A color row: a swatch showing the color, a button to pick another, and one to go back to the standard.
        void AddColor(string caption, string tip, Func<string> get, Action<string> set, Color standard, string standardName)
        {
            var swatch = new Panel { Width = 280, Height = 28 };
            swatch.Paint += (_, e) =>
            {
                var color = Theme.ParseColor(get()) ?? standard;
                Theme.FillBox(e.Graphics, swatch.ClientRectangle, color, Theme.Current.Border);
                TextRenderer.DrawText(e.Graphics, get().Length > 0 ? get().ToUpperInvariant() : standardName, Font, swatch.ClientRectangle,
                    Theme.Luminance(color) > 0.18 ? Color.Black : Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            };
            var pick = new ModernButton { Text = "Choose", AutoSize = true, Height = 28, Margin = new Padding(6, 0, 0, 0) };
            pick.Click += (_, _) =>
            {
                using var dialog = new ColorDialog { Color = Theme.ParseColor(get()) ?? standard, FullOpen = true };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    set(Theme.ToHex(dialog.Color));
                    swatch.Invalidate();
                }
            };
            var reset = new ModernButton { Text = "Reset", AutoSize = true, Height = 28, Margin = new Padding(6, 0, 0, 0) };
            reset.Click += (_, _) =>
            {
                set("");
                swatch.Invalidate();
            };
            Add(caption, swatch, tip, pick, reset);
        }

        Heading("Look");
        foreach (int percent in Theme.ScaleChoices)
            _scale.Items.Add($"{percent}%");
        _scale.SelectedIndex = Math.Max(0, Array.IndexOf(Theme.ScaleChoices, settings.UiScale) is var at && at >= 0 ? at : Array.IndexOf(Theme.ScaleChoices, 100));
        Add("App size", _scale, "How big the app is drawn. It changes the next time the app opens.");
        _accent = settings.AccentColor;
        _backColor = settings.BackgroundColor;
        AddColor("Background", "The color behind everything. Text turns dark on light colors.", () => _backColor, value => _backColor = value, Theme.Dark.Window, "Black (standard)");
        AddColor("Accent", "Selected rows, status text and check boxes.", () => _accent, value => _accent = value, Theme.Dark.Accent, "Amber (standard)");
        _background.Box.ReadOnly = true;
        _background.Box.Text = settings.BackgroundImage;
        var pickBackground = new ModernButton { Text = "Browse", AutoSize = true, Height = 28, Margin = new Padding(6, 0, 0, 0) };
        pickBackground.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "Pick a background picture", Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _background.Box.Text = dialog.FileName;
        };
        Add("Picture", _background, "A picture behind the main window, darkened so text stays readable.", pickBackground, Clear(_background));

        Heading("Folders");
        _rrFolder.Box.ReadOnly = true;
        _rrFolder.Box.Text = options.RetroRewindFolder;
        Add("Retro Rewind", _rrFolder, "Only needed for Add to strm, or if your Retro Rewind is newer than the app's track list.",
            Browse(_rrFolder, "Pick the RetroRewind6 folder"), Clear(_rrFolder));
        _output.Box.ReadOnly = true;
        _output.Box.Text = settings.OutputFolder.Length > 0 ? settings.OutputFolder : AppStorage.DefaultOutputFolder;
        Add("Save to", _output, "Where packs and downloaded songs go.", Browse(_output, "Where should packs and downloads go?"));
        _game.Box.ReadOnly = true;
        _game.Box.Text = settings.GameFile;
        var gameButton = new ModernButton { Text = "Browse", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        gameButton.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "Pick your Mario Kart Wii file", Filter = "Wii games|*.rvz;*.iso;*.wbfs;*.ciso;*.gcz;*.wia|All files|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _game.Box.Text = dialog.FileName;
        };
        Add("Game file", _game, "Your Mario Kart Wii file. Only needed to raise the music volume.", gameButton, Clear(_game));

        Heading("Sound");
        foreach (int rate in new[] { 32000, 24000, 22050, 16000 })
            _rate.Items.Add(rate);
        _rate.SelectedItem = options.SampleRate;
        Add("Sample rate", _rate, "32000 is recommended. Fanfares always use it.");
        _loudness.Items.AddRange([Options.LoudnessEven, Options.LoudnessPeak, Options.LoudnessOff]);
        _loudness.SelectedItem = options.Loudness;
        Add("Volume", _loudness, "How the volume is evened out across songs.");
        var ffmpegPath = Tools.TryFind("ffmpeg");
        bool hasFfmpeg = FfmpegInstaller.IsInstalled;
        var ffmpegStatus = new Label
        {
            Text = hasFfmpeg ? $"Installed: {ffmpegPath}" : "Not installed. It's needed to convert songs.",
            AutoSize = false,
            Width = 280,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Tag = Theme.MutedTag,
        };
        var ffmpegButton = new ModernButton { Text = hasFfmpeg ? "Reinstall" : "Install", AutoSize = true, Height = 28, Margin = new Padding(6, 0, 0, 0), Tag = hasFfmpeg ? "" : Theme.AccentTag };
        ffmpegButton.Click += (_, _) =>
        {
            WantsFfmpeg = true;
            Accept();
        };
        Add("FFmpeg", ffmpegStatus, hasFfmpeg ? ffmpegPath ?? "" : "", ffmpegButton);
        _boost.Checked = settings.BoostMusicSlots;
        _raceBoost.Checked = options.RaceBoost;
        _multi.Checked = options.MultiChannelSlots;
        _final.Checked = options.StockFinalLapFiles;
        Check(_boost, "Raises the music volumes in your own copy of revo_kart.brsar, made from your game file.");
        Check(_raceBoost, "The game's race mix is quiet, so race songs are made a little louder.");
        Check(_multi, "Only for CTGP or other mods. Retro Rewind doesn't need it.");
        Check(_final);

        Heading("Other");
        _updates.Checked = settings.CheckForUpdates;
        Check(_updates);

        // Project and dialog buttons stay at the bottom, so Save is always in reach however long the list is.
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, Padding = new Padding(14, 8, 14, 10) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var projectButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        var openProject = new ModernButton { Text = "Open project", AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
        var saveProject = new ModernButton { Text = "Save project", AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
        var removePack = new ModernButton { Text = "Remove pack from game", AutoSize = true, Margin = new Padding(0, 0, 18, 0) };
        openProject.Click += (_, _) =>
        {
            WantsOpen = true;
            Accept();
        };
        saveProject.Click += (_, _) =>
        {
            WantsSave = true;
            Accept();
        };
        removePack.Click += (_, _) =>
        {
            WantsRemove = true;
            Accept();
        };
        projectButtons.Controls.AddRange([openProject, saveProject, removePack]);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0) };
        var ok = new ModernButton { Text = "Save", Tag = Theme.AccentTag, AutoSize = true, Height = 28, Margin = new Padding(6, 0, 0, 0) };
        var cancel = new ModernButton { Text = "Cancel", AutoSize = true, Height = 28, Margin = new Padding(0) };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close();
        buttons.Controls.AddRange([ok, cancel]);
        footer.Controls.Add(projectButtons, 0, 0);
        footer.Controls.Add(buttons, 1, 0);
        var line = new Panel { Dock = DockStyle.Bottom, Height = 1, Tag = Theme.LineTag };

        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroller.Controls.Add(layout);
        Controls.Add(scroller);
        Controls.Add(line);
        Controls.Add(footer);
        AcceptButton = ok;
        CancelButton = cancel;
        Load += (_, _) => FitToScreen(layout, footer);
        Theme.Apply(this);
    }

    /// <summary>Sizes the window to its settings, but never taller than the screen. Past that, the settings scroll.</summary>
    private void FitToScreen(Control content, Control footer)
    {
        var area = Screen.FromControl(Owner ?? (Control)this).WorkingArea;
        int chromeWidth = Width - ClientSize.Width;
        int chromeHeight = Height - ClientSize.Height;
        var size = content.GetPreferredSize(Size.Empty);
        int footerHeight = footer.GetPreferredSize(Size.Empty).Height + 1;
        int height = size.Height + footerHeight;
        bool scrolls = height > area.Height - chromeHeight;
        int width = Math.Max(size.Width, footer.GetPreferredSize(Size.Empty).Width) + (scrolls ? SystemInformation.VerticalScrollBarWidth + Theme.Px(12) : 0);
        ClientSize = new Size(Math.Min(width, area.Width - chromeWidth), Math.Min(height, area.Height - chromeHeight));

        // Centered over the main window, and kept on the screen.
        var around = Owner?.Bounds ?? area;
        int x = Math.Clamp(around.Left + (around.Width - Width) / 2, area.Left, Math.Max(area.Left, area.Right - Width));
        int y = Math.Clamp(around.Top + (around.Height - Height) / 2, area.Top, Math.Max(area.Top, area.Bottom - Height));
        StartPosition = FormStartPosition.Manual;
        Location = new Point(x, y);
    }

    private static ModernButton Clear(FieldBox field)
    {
        var button = new ModernButton { Text = "Clear", AutoSize = true, Height = 28, Margin = new Padding(6, 0, 0, 0) };
        button.Click += (_, _) => field.Box.Text = "";
        return button;
    }

    private ModernButton Browse(FieldBox field, string description)
    {
        var button = new ModernButton { Text = "Browse", AutoSize = true, Height = 28, Margin = new Padding(6, 0, 0, 0) };
        button.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = description, UseDescriptionForTitle = true, InitialDirectory = field.Box.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                field.Box.Text = dialog.SelectedPath;
        };
        return button;
    }

    private void Accept()
    {
        FolderChanged = _rrFolder.Box.Text != _options.RetroRewindFolder;
        _options.RetroRewindFolder = _rrFolder.Box.Text;
        _options.RaceBoost = _raceBoost.Checked;
        _settings.OutputFolder = _output.Box.Text == AppStorage.DefaultOutputFolder ? "" : _output.Box.Text;
        _options.SampleRate = (int)(_rate.SelectedItem ?? 32000);
        _options.Loudness = _loudness.SelectedItem?.ToString() ?? Options.LoudnessEven;
        _options.MultiChannelSlots = _multi.Checked;
        _options.StockFinalLapFiles = _final.Checked;
        if (_game.Box.Text != _settings.GameFile)
            SoundArchive.ClearCache();
        _settings.GameFile = _game.Box.Text;
        _settings.BoostMusicSlots = _boost.Checked;
        _settings.CheckForUpdates = _updates.Checked;
        _settings.UiScale = Theme.ScaleChoices[Math.Max(0, _scale.SelectedIndex)];
        _settings.AccentColor = _accent;
        _settings.BackgroundColor = _backColor;
        _settings.BackgroundImage = _background.Box.Text;
        DialogResult = DialogResult.OK;
        Close();
    }
}

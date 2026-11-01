using System.Diagnostics;
using System.Media;

namespace RRMusicPackMaker;

public sealed class MainForm : Form
{
    private const string AllTracks = "All tracks";
    private const string MySongs = "Songs I set";
    private const string AudioFilter = "Audio files|*.wav;*.mp3;*.flac;*.ogg;*.m4a;*.aac;*.opus;*.wma;*.brstm;*.webm;*.mp4|All files|*.*";

    private readonly Font _titleFont = Theme.TitleFont();
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
    private string? _lastBuildZip;
    private SoundPlayer? _player;
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
    private readonly ModernButton _themeButton = Header("");
    private readonly ModernButton _build = new() { Text = "Export pack and add to Patches", Tag = Theme.AccentTag, AutoSize = true, Height = 34, Padding = new Padding(14, 2, 14, 2), Margin = new Padding(6, 0, 0, 0) };
    private readonly ModernButton _openFolder = Footer("Show exported pack");
    private readonly ModernButton _logButton = Footer("Activity");
    private readonly ModernButton _updateButton = Footer("Update available");
    private readonly NoticeBar _notice = new() { Dock = DockStyle.Fill };
    private readonly Panel _noticeHolder = new() { Dock = DockStyle.Top, Visible = false, Padding = new Padding(12, 0, 12, 10) };
    private readonly ModernButton _hearCurrent = new() { Text = "▶  Hear current music", Height = 32, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
    private readonly ComboBox _manyLoop = new FlatCombo { Width = 310, Margin = new Padding(0, 0, 0, 4) };
    private readonly ComboBox _manyFade = new FlatCombo { Width = 310, Margin = new Padding(0, 0, 0, 4) };
    private readonly ComboBox _manyVolume = new FlatCombo { Width = 310, Margin = new Padding(0, 0, 0, 8) };
    private static readonly LoopMode[] BulkLoopModes = [LoopMode.Auto, LoopMode.WholeSong, LoopMode.FindLoop, LoopMode.NoLoop];
    private const string NoChange = "Keep as they are";
    private readonly HashSet<TrackRow> _missing = [];
    private readonly Dictionary<string, double> _seconds = new(StringComparer.OrdinalIgnoreCase);
    private long _sizeEstimate;
    private string? _previewFile;
    private DateTime _lastCheck = DateTime.MinValue;
    private readonly Label _status = new() { AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(14, 9, 0, 0) };
    private readonly ThinProgress _progress = new() { Dock = DockStyle.Bottom };
    private readonly Panel _logPanel = new() { Dock = DockStyle.Bottom, Height = 160, Visible = false };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };

    // Details for one track.
    private readonly TableLayoutPanel _single = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12, 6, 12, 6) };
    private readonly Label _title = new() { AutoSize = true, MaximumSize = new Size(310, 0), Tag = Theme.TitleTag, Margin = new Padding(0, 0, 0, 2) };
    private readonly Label _fileLabel = new() { AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 2) };
    private readonly Label _note = new() { AutoSize = true, MaximumSize = new Size(310, 0), Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 10) };
    private readonly Label _sourceLabel = new() { AutoSize = true, MaximumSize = new Size(310, 0), Tag = Theme.MutedTag, Margin = new Padding(0, 4, 0, 8) };
    private readonly ModernButton _choose = new() { Text = "Choose song", Tag = Theme.AccentTag, Height = 34, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0) };
    private readonly ModernButton _remove = new() { Text = "Remove", Height = 34, Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0) };
    private readonly ModernButton _fromWeb = new() { Text = "Find a song online", Height = 32, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
    private readonly FieldBox _song = new("Song name") { Dock = DockStyle.Top };
    private readonly FieldBox _credits = new("Credits") { Dock = DockStyle.Top };
    private readonly ComboBox _loopMode = new FlatCombo { Dock = DockStyle.Top };
    private readonly LoopThumb _thumb = new() { Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 4) };
    private readonly Label _loopSummary = new() { AutoSize = true, MaximumSize = new Size(310, 0), Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 8) };
    private readonly ModernButton _editLoop = new() { Text = "Edit loop", Height = 32, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 8) };
    private readonly ModernButton _hearJump = new() { Text = "▶  Hear the jump", Height = 32, Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 8) };
    private readonly ComboBox _crossfade = new FlatCombo { Dock = DockStyle.Top };
    private readonly ComboBox _volume = new FlatCombo { Dock = DockStyle.Top };
    private readonly Label _finalCaption = new() { Text = "Different song on the final lap", AutoSize = true, Margin = new Padding(0, 12, 0, 4) };
    private readonly Label _finalLabel = new() { AutoSize = true, MaximumSize = new Size(310, 0), Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 6) };
    private readonly ModernButton _finalChoose = new() { Text = "Choose", Height = 30, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0) };
    private readonly ModernButton _finalRemove = new() { Text = "Remove", Height = 30, Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0) };

    private readonly List<Control> _songOnly = [];

    // Details for many tracks.
    private readonly FlowLayoutPanel _many = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12, 6, 12, 6), Visible = false };
    private readonly Label _manyTitle = new() { AutoSize = true, Tag = Theme.TitleTag, Margin = new Padding(0, 0, 0, 12) };

    public MainForm()
    {
        Text = "RR Music Pack Maker";
        Width = 1360;
        Height = 860;
        MinimumSize = new Size(1080, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.UiFont();
        AutoScaleMode = AutoScaleMode.Dpi;

        bool dark = _settings.Theme switch { "Dark" => true, "Light" => false, _ => Theme.SystemPrefersDark() };
        Theme.Set(dark);

        BuildLayout();
        ApplyTheme();

        _options.RetroRewindFolder = Catalog.DetectRetroRewind() ?? "";
        _autosave.Tick += (_, _) => SaveNow();
        LoadAutosave();

        FormClosing += (_, _) => SaveNow();
        Shown += async (_, _) => await OfferFfmpegAsync();
        Shown += async (_, _) => await CheckForUpdateAsync();
        Activated += (_, _) => RunChecks(force: false);
        RunChecks(force: true);
    }

    /// <summary>The app can't convert songs without FFmpeg, so offer it on launch rather than failing on the first build.</summary>
    private async Task OfferFfmpegAsync()
    {
        // RRMUSIC_DATA means an automated run, where nobody is there to answer.
        if (FfmpegInstaller.IsInstalled || Environment.GetEnvironmentVariable("RRMUSIC_DATA") != null)
            return;
        var answer = MessageBox.Show(this,
            "FFmpeg is needed to convert songs, and it isn't on this PC.\r\n\r\nDownload it now? It's a 77 MB download and takes about 150 MB of space.\r\n\r\nYou can also do this later from Settings.",
            "Install FFmpeg", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes)
            await InstallFfmpegAsync();
    }

    private async Task InstallFfmpegAsync()
    {
        _build.Enabled = false;
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
            SetStatus("FFmpeg didn't install. Open Activity to see why.");
            WriteLog($"FFmpeg didn't install. {ex.Message}");
            _logPanel.Visible = true;
        }
        finally
        {
            if (!IsDisposed)
            {
                _build.Enabled = true;
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

        var issues = SetupCheck.Run(_options.RetroRewindFolder);
        _notice.Text = string.Join(Environment.NewLine, issues);
        _noticeHolder.Height = 18 + Math.Max(1, issues.Count) * 22 + 10;
        _noticeHolder.Visible = issues.Count > 0;

        int before = _missing.Count;
        _missing.Clear();
        foreach (var row in _rows.Where(r => r.IsAssigned && !File.Exists(r.Source)))
            _missing.Add(row);
        if (before != _missing.Count)
            ShowDetails();
        _grid.Invalidate();
        UpdateSummary();
    }

    private static ModernButton Header(string text) => new() { Text = text, AutoSize = true, Height = 32, Margin = new Padding(6, 0, 0, 0) };
    private static ModernButton Footer(string text) => new() { Text = text, AutoSize = true, Height = 34, Margin = new Padding(6, 0, 0, 0) };

    private void BuildLayout()
    {
        // App bar: name of the app and pack on the left, settings on the right.
        var header = new Panel { Dock = DockStyle.Top, Height = 62 };
        var headerLeft = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(16, 14, 0, 0) };
        var brand = new Label { Text = "RR Music", AutoSize = true, Font = Theme.TitleFont(15f), Margin = new Padding(0, 3, 26, 0) };
        headerLeft.Controls.AddRange([brand, _packName, _author]);
        var headerRight = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 14, 12, 0) };
        var settings = Header("Settings");
        headerRight.Controls.AddRange([_themeButton, settings]);
        header.Controls.Add(headerRight);
        header.Controls.Add(headerLeft);
        settings.Click += (_, _) => OpenSettings();
        _themeButton.Click += (_, _) => ToggleTheme();

        // Bottom bar.
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 58 };
        var footerRight = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 12, 12, 0) };
        footerRight.Controls.AddRange([_build, _openFolder, _logButton, _updateButton]);
        _updateButton.Visible = false;
        _updateButton.Click += (_, _) => UpdateCheck.OpenPage();
        _noticeHolder.Controls.Add(_notice);
        var footerLeft = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        footerLeft.Controls.Add(_status);
        footer.Controls.Add(footerLeft);
        footer.Controls.Add(footerRight);
        _build.Click += async (_, _) => await BuildAndAddAsync();
        _openFolder.Click += (_, _) => OpenBuildFolder();
        _logButton.Click += (_, _) => _logPanel.Visible = !_logPanel.Visible;
        ShowInstallButtons(false);

        var logCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        logCard.Controls.Add(_log);
        _logPanel.Padding = new Padding(12, 10, 12, 0);
        _logPanel.Controls.Add(logCard);

        // Left card: where to look.
        var sidebarHolder = new Panel { Dock = DockStyle.Left, Width = 228, Padding = new Padding(0, 0, 10, 0) };
        var sidebarCard = new CardPanel { Dock = DockStyle.Fill };
        sidebarCard.Controls.Add(_sidebar);
        sidebarHolder.Controls.Add(sidebarCard);
        _sidebar.SelectedIndexChanged += (_, _) => SidebarChanged();

        // Middle card: the tracks.
        var centerCard = new CardPanel { Dock = DockStyle.Fill };
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 46, ColumnCount = 2, Padding = new Padding(4, 4, 4, 6) };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var addSongs = new ModernButton { Text = "Add songs", AutoSize = true, Margin = new Padding(8, 0, 0, 0), Dock = DockStyle.Fill };
        _filter.Margin = new Padding(0);
        searchRow.Controls.Add(_filter, 0, 0);
        searchRow.Controls.Add(addSongs, 1, 0);
        addSongs.Click += (_, _) => AddSongsByName();
        _filter.Box.TextChanged += (_, _) => RefreshGrid();
        var gridHolder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 4, 4) };
        gridHolder.Controls.Add(_grid);
        centerCard.Controls.Add(gridHolder);
        centerCard.Controls.Add(_chips);
        centerCard.Controls.Add(searchRow);
        ConfigureGrid();

        // Right card: the selected track.
        var detailsHolder = new Panel { Dock = DockStyle.Right, Width = 384, Padding = new Padding(10, 0, 0, 0) };
        var detailsCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(4, 8, 4, 8) };
        var detailsScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Tag = Theme.PanelTag };
        BuildSinglePanel();
        BuildManyPanel();
        detailsScroll.Controls.Add(_many);
        detailsScroll.Controls.Add(_single);
        detailsCard.Controls.Add(detailsScroll);
        detailsHolder.Controls.Add(detailsCard);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
        body.Controls.Add(centerCard);
        body.Controls.Add(detailsHolder);
        body.Controls.Add(sidebarHolder);

        Controls.Add(body);
        Controls.Add(_noticeHolder);
        Controls.Add(_logPanel);
        Controls.Add(_progress);
        Controls.Add(footer);
        Controls.Add(header);

        _packName.Box.TextChanged += (_, _) => MarkDirty();
        _author.Box.TextChanged += (_, _) => MarkDirty();
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
                Height = 28,
                AutoSize = true,
                Margin = new Padding(0, 0, 6, 6),
                Tag = value == _platform ? Theme.AccentTag : "",
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
            chip.Tag = (index == 0 ? "" : chip.Text) == platform ? Theme.AccentTag : "";
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
            var label = new Label { Text = text.ToUpperInvariant(), AutoSize = true, Tag = Theme.MutedTag, Font = Theme.UiFont(8f), Margin = new Padding(0, 16, 0, 4) };
            Wide(label);
            _songOnly.Add(label);
        }

        Wide(_title);
        Wide(_fileLabel);
        Wide(_note);
        Pair(_choose, _remove);
        Wide(_fromWeb);
        Wide(_hearCurrent);
        Wide(_sourceLabel);
        Section("Song");
        Field("Name", _song);
        Field("Credits", _credits);
        Section("Loop");
        Field("Type", _loopMode);
        Wide(_thumb);
        Wide(_loopSummary);
        Pair(_editLoop, _hearJump);
        Section("Sound");
        Field("Smooth the jump", _crossfade);
        Field("Volume", _volume);
        Wide(_finalCaption);
        Wide(_finalLabel);
        Pair(_finalChoose, _finalRemove);

        foreach (LoopMode mode in Enum.GetValues<LoopMode>())
            _loopMode.Items.Add(LoopModeText(mode));
        foreach (var (text, _) in PackBuilder.CrossfadeChoices)
            _crossfade.Items.Add(text);
        foreach (int db in PackBuilder.VolumeChoices)
            _volume.Items.Add(db == 0 ? "Normal" : $"{db:+0;-0} dB");

        _choose.Click += (_, _) => ChooseSource();
        _fromWeb.Click += (_, _) => GetFromWeb();
        _hearCurrent.Click += async (_, _) => await HearCurrentAsync();
        _remove.Click += (_, _) => ClearSongs(SelectedRows());
        _editLoop.Click += (_, _) => OpenLoopEditor();
        _hearJump.Click += async (_, _) => await HearJumpAsync();
        _finalChoose.Click += (_, _) => ChooseFinal();
        _finalRemove.Click += (_, _) =>
        {
            if (_selected != null)
                _selected.FinalSource = "";
            ShowDetails();
            MarkDirty();
        };
        _song.Box.TextChanged += (_, _) => DetailChanged(r => r.Song = _song.Box.Text);
        _credits.Box.TextChanged += (_, _) => DetailChanged(r => r.Credits = _credits.Box.Text);
        _loopMode.SelectedIndexChanged += (_, _) => DetailChanged(r =>
        {
            r.Loop = (LoopMode)_loopMode.SelectedIndex;
            UpdateThumb();
        });
        _crossfade.SelectedIndexChanged += (_, _) => DetailChanged(r => r.Crossfade = PackBuilder.CrossfadeChoices[Math.Max(0, _crossfade.SelectedIndex)].Seconds);
        _volume.SelectedIndexChanged += (_, _) => DetailChanged(r => r.VolumeDb = PackBuilder.VolumeChoices[Math.Max(0, _volume.SelectedIndex)]);
    }

    private void BuildManyPanel()
    {
        ModernButton Wide(string text, string tag, Action click)
        {
            var button = new ModernButton { Text = text, Tag = tag, Width = 310, Height = 34, Margin = new Padding(0, 0, 0, 8) };
            button.Click += (_, _) => click();
            return button;
        }

        _many.Controls.Add(_manyTitle);
        _many.Controls.Add(Wide("Use one song for all", Theme.AccentTag, ChooseSource));
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
        Bulk("Loop", _manyLoop);
        Bulk("Smooth the jump", _manyFade);
        Bulk("Volume", _manyVolume);
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
        _loading = wasLoading;
    }

    /// <summary>The file this track plays right now: from the pack if it's installed, otherwise Retro Rewind's own.</summary>
    internal string? CurrentMusicFile(TrackRow row)
    {
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
            PlayPreview(preview);
            SetStatus($"Playing 30 seconds of {Path.GetFileName(path)}. Press Esc to stop.");
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or IOException)
        {
            if (!IsDisposed)
                SetStatus($"Could not play it. {ex.Message}");
        }
    }

    private void PlayPreview(string file)
    {
        StopPreview();
        _previewFile = file;
        _player = new SoundPlayer(file);
        _player.Play();
    }

    private void StopPreview()
    {
        _player?.Stop();
        _player?.Dispose();
        _player = null;
        if (_previewFile != null && File.Exists(_previewFile))
            File.Delete(_previewFile);
        _previewFile = null;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        HandleShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);

    /// <summary>Delete and Space only act on the track list, so they still type normally in text boxes.</summary>
    internal bool HandleShortcut(Keys key)
    {
        if (key == (Keys.Control | Keys.F))
        {
            _filter.Box.Focus();
            _filter.Box.SelectAll();
            return true;
        }
        if (key == Keys.Escape && _player != null)
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
        if (key == Keys.Space && _selected != null)
        {
            if (_selected.IsAssigned && !_missing.Contains(_selected))
                _ = HearJumpAsync();
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
        _grid.RowTemplate.Height = 36;
        _grid.ColumnHeadersHeight = 32;
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
            // Status is a small pill. The row behind it was already painted.
            if (e.ColumnIndex != 2 || e.RowIndex < 0 || e.RowIndex >= _visible.Count)
                return;
            e.Handled = true;
            string status = StatusOf(_visible[e.RowIndex]);
            if (status.Length == 0 || e.Graphics == null)
                return;
            var p = Theme.Current;
            var size = TextRenderer.MeasureText(status, _pillFont);
            var pill = new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y + (e.CellBounds.Height - 22) / 2, size.Width + 12, 22);
            Color text;
            if (status is "Failed" or "Missing")
            {
                Theme.FillBox(e.Graphics, pill, p.Bad, null, 10);
                text = p.OnAccent;
            }
            else if (status == "Ready")
            {
                Theme.FillBox(e.Graphics, pill, Color.Transparent, p.Muted, 10);
                text = p.Muted;
            }
            else
            {
                Theme.FillBox(e.Graphics, pill, p.Accent, null, 10);
                text = p.OnAccent;
            }
            TextRenderer.DrawText(e.Graphics, status, _pillFont, pill, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        _grid.RowPrePaint += (_, e) =>
        {
            var p = Theme.Current;
            var row = e.RowBounds;
            using (var back = new SolidBrush(p.Panel))
                e.Graphics.FillRectangle(back, row);
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            if (selected || e.RowIndex == _dropIndex)
                Theme.FillBox(e.Graphics, new Rectangle(row.X, row.Y + 1, Math.Min(row.Width, _grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible)), row.Height - 2), p.Selection);
            else if (e.RowIndex == _hoverRow)
                Theme.FillBox(e.Graphics, new Rectangle(row.X, row.Y + 1, Math.Min(row.Width, _grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible)), row.Height - 2), p.Raised);
            e.PaintParts = DataGridViewPaintParts.ContentForeground;
        };
        _grid.CellMouseEnter += (_, e) => SetHoverRow(e.RowIndex);
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

    private void ApplyTheme()
    {
        Theme.Apply(this);
        _themeButton.Text = Theme.Current.IsDark ? "Light" : "Dark";
        _title.Font = _titleFont;
        _sidebar.ItemHeight = 34;
        _manyTitle.Font = _titleFont;
        _sidebar.Invalidate();
        _grid.Invalidate();
    }

    internal void ToggleTheme()
    {
        Theme.Set(!Theme.Current.IsDark);
        _settings.Theme = Theme.Current.IsDark ? "Dark" : "Light";
        AppStorage.SaveSettings(_settings);
        ApplyTheme();
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
            var present = categories.Where(category => _rows.Any(r => r.Category == category)).ToList();
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
            && words.All(w => r.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || r.FileBase.Contains(w, StringComparison.OrdinalIgnoreCase)
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
            _seconds[r.Source],
            r.FixedRate ?? _options.SampleRate,
            _options.MultiChannelSlots ? r.StockChannels : 2,
            r.StockFinalBase != null && _options.StockFinalLapFiles));
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
        _hearCurrent.Visible = row != null && CurrentMusicFile(row) != null;
        _song.Box.Text = row?.Song ?? "";
        _credits.Box.Text = row?.Credits ?? "";
        _loopMode.SelectedIndex = (int)(row?.Loop ?? LoopMode.Auto);
        _crossfade.SelectedIndex = PackBuilder.CrossfadeIndex(row?.Crossfade ?? 0);
        _volume.SelectedIndex = PackBuilder.VolumeIndex(row?.VolumeDb ?? 0);
        _choose.Text = has ? "Change song" : "Choose song";
        _choose.Enabled = row != null;
        _fromWeb.Enabled = row != null;
        _remove.Visible = has;
        _single.SetColumnSpan(_choose, has ? 1 : 2);
        _choose.Margin = new Padding(0, 0, has ? 4 : 0, 0);
        foreach (Control control in _songOnly.Concat(new Control[] { _editLoop, _hearJump }))
            control.Visible = has;

        bool canHaveFinal = row != null && (row.StockFinalBase != null || row.PulsarFinalBase != null);
        foreach (Control control in new Control[] { _finalCaption, _finalLabel, _finalChoose, _finalRemove })
            control.Visible = canHaveFinal && has;
        _finalLabel.Text = row?.FinalSource.Length > 0 ? Path.GetFileName(row.FinalSource) : "None. The game speeds up the normal song.";
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
        row.Status = "";
        row.Song = Path.GetFileNameWithoutExtension(file);
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
            _selected.Status = "";
            ShowDetails();
            MarkDirty();
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
        var (matched, unmatched) = PackBuilder.AutoMatch(files, _rows);
        SetStatus($"Matched {matched} of {files.Count} songs by file name.");
        foreach (string name in unmatched)
            WriteLog($"No single track matches: {name}. Drag it onto a track instead.");
        if (unmatched.Count > 0)
            _logPanel.Visible = true;
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
        SetStatus("Preparing the jump...");
        try
        {
            int rate = row.FixedRate ?? _options.SampleRate;
            string file = await Task.Run(() =>
                AudioIO.WriteSeamPreview(Processing.Prepare(row.Source, rate, mode, row.LoopStart, row.LoopEnd, Options.LoudnessOff, crossfadeSeconds: row.Crossfade)));
            if (IsDisposed)
                return;
            PlayPreview(file);
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
        using var form = new SettingsForm(_options, _settings);
        if (form.ShowDialog(this) != DialogResult.OK)
            return;
        AppStorage.SaveSettings(_settings);
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

    private void RemovePackFromGame()
    {
        ReadNames();
        string name = PackBuilder.SafeName(_options.PackName);
        var answer = MessageBox.Show(this,
            $"Do you want to remove \"{name}\" from the game?\r\n\r\nIts Wheel Wizard mod and its songs in Patches will be deleted, and tracks go back to their old music.\r\n\r\nYour exported pack, your song files and this project stay.",
            "Remove pack", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return;
        try
        {
            string output = _settings.OutputFolder.Length > 0 ? _settings.OutputFolder : AppStorage.DefaultOutputFolder;
            string music = Path.Combine(output, name, "RetroRewind6", "Patches");
            int removed = PackBuilder.RemovePack(_options.PackName, PackBuilder.ModsFolders(), _options.RetroRewindFolder, music);
            foreach (var row in _rows)
                row.Status = "";
            ScanInstalled();
            _grid.Invalidate();
            SetStatus($"Removed {name} from the game, along with {removed} files in Patches. Restart Wheel Wizard if it's open.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not remove the pack. Open Activity to see why.");
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

    /// <summary>Building, exporting and installing are one step, so a rebuilt song can never be left uninstalled.</summary>
    internal async Task BuildAndAddAsync()
    {
        _lastBuildMusicFolder = null;
        await BuildPackAsync();
        if (IsDisposed || _lastBuildMusicFolder == null)
            return;
        string? archive = await PrepareSoundArchiveAsync();
        if (!IsDisposed)
            InstallToPatches(archive);
    }

    internal async Task BuildPackAsync()
    {
        if (_rows.All(r => !r.IsAssigned))
        {
            SetStatus("Add a song to at least one track first.");
            return;
        }
        string output = _settings.OutputFolder.Length > 0 ? _settings.OutputFolder : AppStorage.DefaultOutputFolder;
        ReadNames();
        _build.Enabled = false;
        ShowInstallButtons(false);
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
            _lastBuildZip = result.ZipPath;
            ShowInstallButtons(result.Built > 0);
            _progress.Fraction = 0;
            string unchanged = result.Unchanged > 0 ? $", {result.Unchanged} unchanged" : "";
            SetStatus(result.Failed == 0
                ? $"Built {result.Built} songs{unchanged}, {result.Bytes / 1048576.0:0.0} MB."
                : $"Built {result.Built}, {result.Failed} failed. Open Activity to see why.");
            if (result.Failed > 0)
                _logPanel.Visible = true;
            if (result.Built > 0)
            {
                WriteLog($"Pack: {result.Folder}");
                WriteLog($"Zip: {result.ZipPath}");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (IsDisposed)
                return;
            SetStatus("Build failed. Open Activity to see why.");
            WriteLog($"Build failed. {ex.Message}");
            _logPanel.Visible = true;
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
            _build.Enabled = true;
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

    /// <summary>Opens the pack's folder in Explorer with the exported zip selected.</summary>
    private void OpenBuildFolder()
    {
        if (_lastBuildZip != null && File.Exists(_lastBuildZip))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastBuildZip}\"") { UseShellExecute = true });
        else if (_lastBuildFolder != null && Directory.Exists(_lastBuildFolder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_lastBuildFolder}\"") { UseShellExecute = true });
    }

    /// <summary>
    /// Puts the songs in Patches. If Wheel Wizard is installed the pack also becomes a mod, because
    /// that's what shows in its list, and it deletes files in Patches that no mod owns.
    /// </summary>
    private async Task<string?> PrepareSoundArchiveAsync()
    {
        if (!_settings.BoostMusicSlots)
            return null;
        _build.Enabled = false;
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
                _build.Enabled = true;
        }
    }

    private void InstallToPatches(string? archive = null)
    {
        if (_lastBuildMusicFolder == null)
            return;
        if (!Directory.Exists(_options.RetroRewindFolder))
        {
            SetStatus("Pick your Retro Rewind folder in Settings first.");
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
            else
            {
                var conflicts = PackBuilder.PatchesConflicts(_lastBuildMusicFolder, _options.RetroRewindFolder);
                if (conflicts.Count > 0)
                {
                    string names = string.Join("\r\n", conflicts.Take(10));
                    var answer = MessageBox.Show(this,
                        $"These {conflicts.Count} files in Patches are different and would be replaced:\r\n\r\n{names}\r\n\r\nReplace them?",
                        "Replace files in Patches", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes)
                        return;
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
            int count = PackBuilder.CopyToPatches(_lastBuildMusicFolder, _options.RetroRewindFolder);
            SoundArchive.Place(archive, _settings.BoostMusicSlots, Path.Combine(_options.RetroRewindFolder, "Patches"));

            ScanInstalled();
            _grid.Invalidate();
            WriteLog($"Patches: {Path.Combine(_options.RetroRewindFolder, "Patches")}");
            SetStatus(wheelWizard
                ? $"Done. Exported {Path.GetFileName(_lastBuildZip)} and added {count} files to Patches. Restart Wheel Wizard."
                : $"Done. Exported {Path.GetFileName(_lastBuildZip)} and added {count} files to Patches.");
            WriteLog($"Exported pack: {_lastBuildZip}");
            if (archive != null)
                WriteLog("Music slots raised to full volume with your own copy of revo_kart.brsar. It isn't in the exported pack.");
            WriteLog("No music change in game? Check Retro Rewind's Music setting and its Loose Archive Overrides setting.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not install. Open Activity to see why.");
            WriteLog($"Could not install. {ex.Message}");
        }
    }

    private void SetStatus(string message)
    {
        _status.Text = message;
        WriteLog(message);
    }

    private void WriteLog(string message) => _log.AppendText(message + Environment.NewLine);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
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

    internal void SaveForTest() => SaveNow();
    internal void RunChecksForTest() => RunChecks(force: true);
    internal string StatusOfForTest(TrackRow row) => StatusOf(row);
    internal void FocusGridForTest()
    {
        ActiveControl = _grid;
    }
    internal Task UpdateSizeForTest() => UpdateSizeAsync();
    internal string StatusTextForTest => _status.Text;
    internal string? ExportedZipForTest => _lastBuildZip;
    internal string ActionButtonTextForTest => _build.Text;
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
    private readonly FieldBox _rrFolder = new() { Width = 360 };
    private readonly FieldBox _output = new() { Width = 360 };
    private readonly ComboBox _rate = new FlatCombo { Width = 200 };
    private readonly ComboBox _loudness = new FlatCombo { Width = 200 };
    private readonly FlatCheck _multi = new() { Text = "Multi-channel stock slots (only for CTGP or other mods)", AutoSize = true };
    private readonly FlatCheck _final = new() { Text = "Write stock final lap files (_f)", AutoSize = true };
    private readonly FlatCheck _updates = new() { Text = "Tell me when a new version is out", AutoSize = true };
    private readonly FlatCheck _boost = new() { Text = "Raise music volume in revo_kart.brsar (made from your game file)", AutoSize = true };
    private readonly FieldBox _game = new("Found automatically through Wheel Wizard") { Width = 360 };

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
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = Theme.UiFont();

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(16) };
        int row = 0;
        void Add(string caption, Control field, Button? browse = null)
        {
            layout.Controls.Add(new Label { Text = caption, AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 8, 0, 3) }, 0, row);
            layout.SetColumnSpan(layout.GetControlFromPosition(0, row)!, 2);
            row++;
            layout.Controls.Add(field, 0, row);
            if (browse != null)
                layout.Controls.Add(browse, 1, row);
            row++;
        }

        _rrFolder.Box.ReadOnly = true;
        _rrFolder.Box.Text = options.RetroRewindFolder;
        _output.Box.ReadOnly = true;
        _output.Box.Text = settings.OutputFolder.Length > 0 ? settings.OutputFolder : AppStorage.DefaultOutputFolder;
        foreach (int rate in new[] { 32000, 24000, 22050, 16000 })
            _rate.Items.Add(rate);
        _rate.SelectedItem = options.SampleRate;
        _loudness.Items.AddRange([Options.LoudnessEven, Options.LoudnessPeak, Options.LoudnessOff]);
        _loudness.SelectedItem = options.Loudness;
        _multi.Checked = options.MultiChannelSlots;
        _final.Checked = options.StockFinalLapFiles;

        Add("Retro Rewind folder", _rrFolder, Browse(_rrFolder, "Pick the RetroRewind6 folder"));
        Add("Save packs to", _output, Browse(_output, "Where should packs go?"));
        Add("Sample rate (32000 recommended, fanfares always use it)", _rate);
        Add("Volume across songs", _loudness);
        var ffmpegPath = Tools.TryFind("ffmpeg");
        bool hasFfmpeg = FfmpegInstaller.IsInstalled;
        var ffmpegStatus = new Label
        {
            Text = hasFfmpeg ? $"Installed: {ffmpegPath}" : "Not installed. It's needed to convert songs.",
            AutoSize = false,
            Width = 360,
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Tag = Theme.MutedTag,
            Margin = new Padding(0),
        };
        var ffmpegButton = new ModernButton { Text = hasFfmpeg ? "Reinstall" : "Install", AutoSize = true, Height = 32, Margin = new Padding(6, 0, 0, 0), Tag = hasFfmpeg ? "" : Theme.AccentTag };
        ffmpegButton.Click += (_, _) =>
        {
            WantsFfmpeg = true;
            Accept();
        };
        Add("FFmpeg", ffmpegStatus, ffmpegButton);

        _game.Box.ReadOnly = true;
        _game.Box.Text = settings.GameFile;
        var gameButton = new ModernButton { Text = "Browse", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        gameButton.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "Pick your Mario Kart Wii file", Filter = "Wii games|*.rvz;*.iso;*.wbfs;*.ciso;*.gcz;*.wia|All files|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _game.Box.Text = dialog.FileName;
        };
        Add("Mario Kart Wii file", _game, gameButton);
        _boost.Checked = settings.BoostMusicSlots;
        layout.Controls.Add(_boost, 0, row++);
        layout.SetColumnSpan(_boost, 2);
        _boost.Margin = new Padding(0, 12, 0, 4);
        _updates.Checked = settings.CheckForUpdates;
        layout.Controls.Add(_multi, 0, row++);
        layout.SetColumnSpan(_multi, 2);
        _multi.Margin = new Padding(0, 0, 0, 4);
        layout.Controls.Add(_final, 0, row++);
        layout.SetColumnSpan(_final, 2);
        layout.Controls.Add(_updates, 0, row++);
        layout.SetColumnSpan(_updates, 2);

        var projectButtons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        var openProject = new ModernButton { Text = "Open project", AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
        var saveProject = new ModernButton { Text = "Save project", AutoSize = true, Margin = new Padding(0) };
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
        var removePack = new ModernButton { Text = "Remove pack from game", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        removePack.Click += (_, _) =>
        {
            WantsRemove = true;
            Accept();
        };
        projectButtons.Controls.AddRange([openProject, saveProject, removePack]);
        layout.Controls.Add(projectButtons, 0, row++);
        layout.SetColumnSpan(projectButtons, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0) };
        var ok = new ModernButton { Text = "Save", Tag = Theme.AccentTag, AutoSize = true, Height = 32, Margin = new Padding(6, 0, 0, 0) };
        var cancel = new ModernButton { Text = "Cancel", AutoSize = true, Height = 32 };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close();
        buttons.Controls.AddRange([ok, cancel]);
        layout.Controls.Add(buttons, 0, row);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        Theme.Apply(this);
    }

    private ModernButton Browse(FieldBox field, string description)
    {
        var button = new ModernButton { Text = "Browse", AutoSize = true, Height = 32, Margin = new Padding(6, 0, 0, 0) };
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
        DialogResult = DialogResult.OK;
        Close();
    }
}

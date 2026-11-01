using System.Diagnostics;
using System.Media;

namespace RRMusicPackMaker;

public sealed class MainForm : Form
{
    private const string AllTracks = "All tracks";
    private const string MySongs = "Songs I set";
    private const string AudioFilter = "Audio files|*.wav;*.mp3;*.flac;*.ogg;*.m4a;*.aac;*.opus;*.wma;*.brstm;*.webm;*.mp4|All files|*.*";

    private List<TrackRow> _rows = [];
    private List<TrackRow> _visible = [];
    private TrackRow? _selected;
    private bool _loading;
    private int _dropIndex = -1;
    private string? _lastBuildMusicFolder;
    private string? _lastBuildFolder;
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
    private readonly Button _themeButton = Header("");
    private readonly Button _build = new() { Text = "Build pack", Tag = Theme.AccentTag, AutoSize = true, Height = 34, Padding = new Padding(14, 2, 14, 2), Margin = new Padding(6, 0, 0, 0) };
    private readonly Button _install = Footer("Add to Wheel Wizard");
    private readonly Button _openFolder = Footer("Open folder");
    private readonly Button _logButton = Footer("Activity");
    private readonly Label _status = new() { AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(14, 9, 0, 0) };
    private readonly ThinProgress _progress = new() { Dock = DockStyle.Bottom };
    private readonly Panel _logPanel = new() { Dock = DockStyle.Bottom, Height = 150, Visible = false, Tag = Theme.PanelTag };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };

    // Details for one track.
    private readonly TableLayoutPanel _single = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(16, 14, 16, 14) };
    private readonly Label _title = new() { AutoSize = true, MaximumSize = new Size(320, 0), Tag = Theme.TitleTag, Margin = new Padding(0, 0, 0, 2) };
    private readonly Label _fileLabel = new() { AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 2) };
    private readonly Label _note = new() { AutoSize = true, MaximumSize = new Size(320, 0), Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 10) };
    private readonly Label _sourceLabel = new() { AutoSize = true, MaximumSize = new Size(320, 0), Tag = Theme.MutedTag, Margin = new Padding(0, 4, 0, 8) };
    private readonly Button _choose = new() { Text = "Choose song", Tag = Theme.AccentTag, Height = 34, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0) };
    private readonly Button _remove = new() { Text = "Remove", Height = 34, Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0) };
    private readonly Button _fromWeb = new() { Text = "Find online or paste a link", Height = 32, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
    private readonly FieldBox _song = new("Song name") { Dock = DockStyle.Top };
    private readonly FieldBox _credits = new("Credits") { Dock = DockStyle.Top };
    private readonly ComboBox _loopMode = new FlatCombo { Dock = DockStyle.Top };
    private readonly LoopThumb _thumb = new() { Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 4) };
    private readonly Label _loopSummary = new() { AutoSize = true, MaximumSize = new Size(320, 0), Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 8) };
    private readonly Button _editLoop = new() { Text = "Edit loop", Height = 32, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 8) };
    private readonly Button _hearJump = new() { Text = "▶  Hear the jump", Height = 32, Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 8) };
    private readonly ComboBox _crossfade = new FlatCombo { Dock = DockStyle.Top };
    private readonly ComboBox _volume = new FlatCombo { Dock = DockStyle.Top };
    private readonly Label _finalCaption = new() { Text = "Different song on the final lap", AutoSize = true, Margin = new Padding(0, 12, 0, 4) };
    private readonly Label _finalLabel = new() { AutoSize = true, MaximumSize = new Size(320, 0), Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 6) };
    private readonly Button _finalChoose = new() { Text = "Choose", Height = 30, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0) };
    private readonly Button _finalRemove = new() { Text = "Remove", Height = 30, Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0) };

    // Details for many tracks.
    private readonly FlowLayoutPanel _many = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16, 14, 16, 14), Visible = false };
    private readonly Label _manyTitle = new() { AutoSize = true, Tag = Theme.TitleTag, Margin = new Padding(0, 0, 0, 12) };

    public MainForm()
    {
        Text = "RR Music Pack Maker";
        Width = 1360;
        Height = 860;
        MinimumSize = new Size(1080, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;

        bool dark = _settings.Theme switch { "Dark" => true, "Light" => false, _ => Theme.SystemPrefersDark() };
        Theme.Set(dark);

        BuildLayout();
        ApplyTheme();

        _options.RetroRewindFolder = Catalog.DetectRetroRewind() ?? "";
        _autosave.Tick += (_, _) => SaveNow();
        LoadAutosave();

        FormClosing += (_, _) => SaveNow();
    }

    private static Button Header(string text) => new() { Text = text, AutoSize = true, Height = 32, Margin = new Padding(6, 0, 0, 0) };
    private static Button Footer(string text) => new() { Text = text, AutoSize = true, Height = 34, Margin = new Padding(6, 0, 0, 0) };

    private void BuildLayout()
    {
        // Header.
        var header = new Panel { Dock = DockStyle.Top, Height = 56, Tag = Theme.PanelTag };
        var headerLeft = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(14, 12, 0, 0) };
        headerLeft.Controls.AddRange([_packName, _author]);
        var headerRight = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 12, 14, 0) };
        var open = Header("Open");
        var save = Header("Save");
        var settings = Header("Settings");
        headerRight.Controls.AddRange([_themeButton, settings, save, open]);
        header.Controls.Add(headerRight);
        header.Controls.Add(headerLeft);
        open.Click += (_, _) => OpenProject();
        save.Click += (_, _) => SaveProject();
        settings.Click += (_, _) => OpenSettings();
        _themeButton.Click += (_, _) => ToggleTheme();

        // Footer.
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 54, Tag = Theme.PanelTag };
        var footerRight = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 10, 14, 0) };
        footerRight.Controls.AddRange([_build, _install, _openFolder, _logButton]);
        var footerLeft = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        footerLeft.Controls.Add(_status);
        footer.Controls.Add(footerLeft);
        footer.Controls.Add(footerRight);
        _build.Click += async (_, _) => await BuildPackAsync();
        _install.Click += (_, _) => InstallAsMod();
        _openFolder.Click += (_, _) => OpenBuildFolder();
        _logButton.Click += (_, _) => _logPanel.Visible = !_logPanel.Visible;
        _install.Enabled = false;
        _openFolder.Enabled = false;

        _logPanel.Padding = new Padding(14, 8, 14, 8);
        _logPanel.Controls.Add(_log);

        // Sidebar.
        var sidebarHolder = new Panel { Dock = DockStyle.Left, Width = 216, Tag = Theme.PanelTag, Padding = new Padding(0, 8, 0, 0) };
        sidebarHolder.Controls.Add(_sidebar);
        _sidebar.SelectedIndexChanged += (_, _) => RefreshGrid();

        // Track list.
        var center = new Panel { Dock = DockStyle.Fill };
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 48, ColumnCount = 2, Padding = new Padding(12, 8, 12, 6) };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var addSongs = new Button { Text = "Add songs", AutoSize = true, Height = 32, Margin = new Padding(8, 0, 0, 0), Dock = DockStyle.Fill };
        _filter.Margin = new Padding(0);
        searchRow.Controls.Add(_filter, 0, 0);
        searchRow.Controls.Add(addSongs, 1, 0);
        addSongs.Click += (_, _) => AddSongsByName();
        _filter.Box.TextChanged += (_, _) => RefreshGrid();
        var gridHolder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 12) };
        gridHolder.Controls.Add(_grid);
        center.Controls.Add(gridHolder);
        center.Controls.Add(searchRow);
        ConfigureGrid();

        // Details.
        var detailsHolder = new Panel { Dock = DockStyle.Right, Width = 356, AutoScroll = true, Tag = Theme.PanelTag };
        BuildSinglePanel();
        BuildManyPanel();
        detailsHolder.Controls.Add(_many);
        detailsHolder.Controls.Add(_single);

        Controls.Add(center);
        Controls.Add(Line(DockStyle.Right));
        Controls.Add(detailsHolder);
        Controls.Add(Line(DockStyle.Left));
        Controls.Add(sidebarHolder);
        Controls.Add(_logPanel);
        Controls.Add(_progress);
        Controls.Add(footer);
        Controls.Add(Line(DockStyle.Top));
        Controls.Add(header);

        _packName.Box.TextChanged += (_, _) => MarkDirty();
        _author.Box.TextChanged += (_, _) => MarkDirty();
    }

    private static Panel Line(DockStyle dock) => new()
    {
        Dock = dock,
        Width = 1,
        Height = 1,
        Tag = Theme.LineTag,
    };

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
            Wide(new Label { Text = caption, AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 6, 0, 3) });
            field.Margin = new Padding(0, 0, 0, 4);
            Wide(field);
        }

        Wide(_title);
        Wide(_fileLabel);
        Wide(_note);
        Pair(_choose, _remove);
        Wide(_fromWeb);
        Wide(_sourceLabel);
        Field("Song name", _song);
        Field("Credits", _credits);
        Field("Loop", _loopMode);
        Wide(_thumb);
        Wide(_loopSummary);
        Pair(_editLoop, _hearJump);
        Field("Smooth the jump", _crossfade);
        Field("Song volume", _volume);
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
        Button Wide(string text, string tag, Action click)
        {
            var button = new Button { Text = text, Tag = tag, Width = 320, Height = 34, Margin = new Padding(0, 0, 0, 8) };
            button.Click += (_, _) => click();
            return button;
        }

        _many.Controls.Add(_manyTitle);
        _many.Controls.Add(Wide("Use one song for all", Theme.AccentTag, ChooseSource));
        _many.Controls.Add(Wide("Find online or paste a link", "", GetFromWeb));
        _many.Controls.Add(Wide("Fill from a folder of songs", "", FillFromFolder));
        _many.Controls.Add(Wide("Remove songs", "", () => ClearSongs(SelectedRows())));
        _many.Controls.Add(new Label
        {
            Text = "Fill shuffles the folder's songs across these tracks, and reuses songs if there are fewer songs than tracks.",
            AutoSize = true,
            MaximumSize = new Size(320, 0),
            Tag = Theme.MutedTag,
            Margin = new Padding(0, 4, 0, 0),
        });
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
        _grid.RowTemplate.Height = 34;
        _grid.ColumnHeadersHeight = 34;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;

        foreach (var (title, weight) in new[] { ("Track", 46), ("Song", 40), ("Status", 14) })
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
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= _visible.Count || e.CellStyle == null)
                return;
            var p = Theme.Current;
            if (e.RowIndex == _dropIndex)
                e.CellStyle.BackColor = p.Selection;
            if (e.ColumnIndex == 2)
            {
                string status = StatusOf(_visible[e.RowIndex]);
                Color color = status switch { "Failed" => p.Bad, "Built" => p.Good, "In game" => p.Good, _ => p.Muted };
                e.CellStyle.ForeColor = color;
                e.CellStyle.SelectionForeColor = color;
            }
        };
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

    private string StatusOf(TrackRow row)
    {
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
        _themeButton.Text = Theme.Current.IsDark ? "☀  Light" : "☾  Dark";
        foreach (Label label in new[] { _title, _manyTitle })
            label.Font = new Font("Segoe UI Semibold", 12f);
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
        foreach (string category in _rows.Select(r => r.Category).Distinct())
            _sidebar.Items.Add(new SideList.Item(category, _rows.Count(r => r.Category == category)));
        for (int i = 0; i < _sidebar.Items.Count; i++)
            if (((SideList.Item)_sidebar.Items[i]).Name == current)
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
        _visible = _rows.Where(r =>
            (category == AllTracks || (category == MySongs ? r.IsAssigned : r.Category == category))
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
    }

    private void UpdateSummary()
    {
        int songs = _rows.Count(r => r.IsAssigned);
        if (_status.Text.Length == 0 || _status.Text.Contains(" set"))
            _status.Text = songs == 0 ? "No songs set yet. Drag a song onto a track to start." : $"{songs} song{(songs == 1 ? "" : "s")} set";
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
            return;
        }

        bool wasLoading = _loading;
        _loading = true;
        var row = _selected;
        bool has = row?.IsAssigned == true;
        _title.Text = row?.Name ?? "Pick a track";
        _fileLabel.Text = row == null ? "" : row.FileBase + ".brstm";
        _note.Text = row?.Note ?? "";
        _sourceLabel.Text = has ? Path.GetFileName(row!.Source) : "No song set. You can also drag one onto the track.";
        _song.Box.Text = row?.Song ?? "";
        _credits.Box.Text = row?.Credits ?? "";
        _loopMode.SelectedIndex = (int)(row?.Loop ?? LoopMode.Auto);
        _crossfade.SelectedIndex = PackBuilder.CrossfadeIndex(row?.Crossfade ?? 0);
        _volume.SelectedIndex = PackBuilder.VolumeIndex(row?.VolumeDb ?? 0);
        _choose.Text = has ? "Change song" : "Choose song";
        _choose.Enabled = row != null;
        _fromWeb.Enabled = row != null;
        _remove.Enabled = has;
        foreach (Control control in new Control[] { _editLoop, _hearJump, _loopMode, _crossfade, _volume, _song, _credits })
            control.Enabled = has;

        bool canHaveFinal = row != null && (row.StockFinalBase != null || row.PulsarFinalBase != null);
        foreach (Control control in new Control[] { _finalCaption, _finalLabel, _finalChoose, _finalRemove })
            control.Visible = canHaveFinal && has;
        _finalLabel.Text = row?.FinalSource.Length > 0 ? Path.GetFileName(row.FinalSource) : "None. The game speeds up the normal song.";
        _finalRemove.Enabled = row?.FinalSource.Length > 0;
        Theme.Apply(_single);
        _title.Font = new Font("Segoe UI Semibold", 12f);
        _loading = wasLoading;
        UpdateThumb();
    }

    private async void UpdateThumb()
    {
        if (Disposing || IsDisposed)
            return;
        var row = _selected;
        _thumb.Visible = row?.IsAssigned == true;
        _loopSummary.Visible = _thumb.Visible;
        if (row == null || !row.IsAssigned)
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
            _player?.Stop();
            _player = new SoundPlayer(file);
            _player.Play();
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
        _install.Enabled = false;
        _openFolder.Enabled = false;
        Theme.Apply(_build);
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
            _lastBuildMusicFolder = result.MusicFolder;
            _lastBuildFolder = result.Folder;
            _install.Enabled = result.Built > 0;
            _openFolder.Enabled = true;
            string unchanged = result.Unchanged > 0 ? $", {result.Unchanged} unchanged" : "";
            SetStatus(result.Failed == 0
                ? $"Built {result.Built} songs{unchanged}, {result.Bytes / 1048576.0:0.0} MB."
                : $"Built {result.Built}, {result.Failed} failed. Open Activity to see why.");
            if (result.Failed > 0)
                _logPanel.Visible = true;
            WriteLog($"Pack: {result.Folder}");
            WriteLog($"Zip: {result.ZipPath}");
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
            Theme.Apply(_build);
            Theme.Apply(_install);
            Theme.Apply(_openFolder);
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

    private void OpenBuildFolder()
    {
        if (_lastBuildFolder != null && Directory.Exists(_lastBuildFolder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_lastBuildFolder}\"") { UseShellExecute = true });
    }

    private void InstallAsMod()
    {
        if (_lastBuildMusicFolder == null)
            return;
        string mods = PackBuilder.ModsFolder();
        try
        {
            var loose = PackBuilder.FindUnownedPatches(_options.RetroRewindFolder, mods);
            List<string>? keep = null;
            if (loose.Count > 0)
            {
                string names = string.Join("\r\n", loose.Take(10).Select(Path.GetFileName));
                var answer = MessageBox.Show(this,
                    "These files are in your Retro Rewind Patches folder, and no mod owns them:\r\n\r\n" + names
                    + "\r\n\r\nWheel Wizard deletes them at launch once any mod is enabled.\r\n\r\n"
                    + "Yes: save them in a mod named \"Kept Patches files\" so they keep working.\r\n"
                    + "No: add the pack anyway. Wheel Wizard will delete them.\r\n"
                    + "Cancel: do nothing.",
                    "Files would be deleted", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (answer == DialogResult.Cancel)
                    return;
                if (answer == DialogResult.Yes)
                    keep = loose;
            }
            ReadNames();
            string folder = PackBuilder.InstallAsMod(_lastBuildMusicFolder, _options.PackName, _options.Author, mods, keep);
            SetStatus("Added to Wheel Wizard. Restart it if it's open.");
            WriteLog($"Added to Wheel Wizard: {folder}");
            WriteLog("No music change in game? Check Retro Rewind's Music setting and its Loose Archive Overrides setting.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not add the mod. Open Activity to see why.");
            WriteLog($"Could not add the mod. {ex.Message}");
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
        _player?.Stop();
        _player?.Dispose();
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
    private readonly CheckBox _multi = new() { Text = "Multi-channel stock slots (only for CTGP or other mods)", AutoSize = true };
    private readonly CheckBox _final = new() { Text = "Write stock final lap files (_f)", AutoSize = true };

    public bool FolderChanged { get; private set; }

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
        Font = new Font("Segoe UI", 9.5f);

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
        Add("Sample rate (fanfares always use 32000)", _rate);
        Add("Volume across songs", _loudness);
        layout.Controls.Add(_multi, 0, row++);
        layout.SetColumnSpan(_multi, 2);
        _multi.Margin = new Padding(0, 12, 0, 4);
        layout.Controls.Add(_final, 0, row++);
        layout.SetColumnSpan(_final, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0) };
        var ok = new Button { Text = "Save", Tag = Theme.AccentTag, AutoSize = true, Height = 32, Margin = new Padding(6, 0, 0, 0) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, Height = 32 };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close();
        buttons.Controls.AddRange([ok, cancel]);
        layout.Controls.Add(buttons, 0, row);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        Theme.Apply(this);
    }

    private Button Browse(FieldBox field, string description)
    {
        var button = new Button { Text = "Browse", AutoSize = true, Height = 32, Margin = new Padding(6, 0, 0, 0) };
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
        DialogResult = DialogResult.OK;
        Close();
    }
}

namespace RRMusicPackMaker;

/// <summary>What the Songs tab needs from the main window, because tracks live there.</summary>
public sealed record SongsPanelActions(
    Func<string, int> TracksUsing,
    Func<string, int> RemoveFromTracks,
    Action<string> ConfirmDelete,
    Action<string> OpenInEditor);

/// <summary>The Songs tab: everything imported, downloaded or edited, and what can be done with each song.</summary>
public sealed class SongsPanel : Panel
{
    private const string AllKinds = "All";

    private readonly SongLibrary _library;
    private readonly Func<string> _songsFolder;
    private readonly Action<string> _status;
    private readonly Action<string> _log;
    private readonly SongsPanelActions _actions;
    private readonly FieldBox _search = new("Search your songs") { Dock = DockStyle.Fill };
    private readonly SongRows _list = new() { Dock = DockStyle.Fill, Tag = Theme.PanelTag };
    private readonly Label _empty = new() { Dock = DockStyle.Fill, Tag = Theme.MutedTag, TextAlign = ContentAlignment.MiddleCenter, Text = "No songs here yet. Import some, or find one online.", Visible = false };
    private readonly FlowLayoutPanel _chips = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 0, 4, 2) };
    private readonly Label _title = new() { AutoSize = true, MaximumSize = new Size(330, 0), Tag = Theme.TitleTag, Margin = new Padding(0, 0, 0, 4) };
    private readonly Label _details = new() { AutoSize = true, MaximumSize = new Size(330, 0), Tag = Theme.MutedTag, Margin = new Padding(0, 0, 0, 12) };
    private readonly ModernButton _play = Action("▶  Play", Theme.AccentTag);
    private readonly ModernButton _stop = Action("■  Stop");
    private readonly ModernButton _edit = Action("Open in Editor");
    private readonly ModernButton _removeFromTracks = Action("Take off tracks");
    private readonly ModernButton _delete = Action("Delete song");
    private readonly Dictionary<string, double> _lengths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModernButton> _kindChips = [];
    private List<string> _visible = [];
    private string _kind = AllKinds;
    private string? _playing;
    private bool _refreshing;
    private int _playRequest;

    public SongsPanel(SongLibrary library, Func<string> songsFolder, Action<string> status, Action<string> log, SongsPanelActions actions)
    {
        _library = library;
        _songsFolder = songsFolder;
        _status = status;
        _log = log;
        _actions = actions;
        Dock = DockStyle.Fill;
        Padding = new Padding(12, 0, 12, 0);
        Visible = false;
        BuildLayout();
        _library.Changed += RefreshList;
        AudioPreview.Changed += ShowPlayback;
        RefreshList();
    }

    private static ModernButton Action(string text, string tag = "") =>
        new() { Text = text, Height = 28, Width = 330, Margin = new Padding(0, 0, 0, 8), Tag = tag };

    private void BuildLayout()
    {
        var import = new ModernButton { Text = "Import songs", Tag = Theme.AccentTag, AutoSize = true, Margin = new Padding(8, 0, 0, 0), Dock = DockStyle.Fill };
        var find = new ModernButton { Text = "Find a song online", AutoSize = true, Margin = new Padding(8, 0, 0, 0), Dock = DockStyle.Fill };
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 46, ColumnCount = 3, Padding = new Padding(4, 4, 4, 6) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _search.Margin = new Padding(0);
        top.Controls.Add(_search, 0, 0);
        top.Controls.Add(import, 1, 0);
        top.Controls.Add(find, 2, 0);

        foreach (string kind in new[] { AllKinds, nameof(SongKind.Downloaded), nameof(SongKind.Imported), nameof(SongKind.Edited) })
        {
            string value = kind;
            var chip = new ModernButton { Text = value, IsPill = true, Height = 28, AutoSize = true, Margin = new Padding(0, 0, 6, 6), Tag = value == _kind ? Theme.ActiveTag : "" };
            chip.Click += (_, _) => PickKind(value);
            _kindChips[value] = chip;
            _chips.Controls.Add(chip);
        }

        var listHolder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 4, 4) };
        listHolder.Controls.Add(_list);
        listHolder.Controls.Add(_empty);
        var listCard = new CardPanel { Dock = DockStyle.Fill };
        listCard.Controls.Add(listHolder);
        listCard.Controls.Add(_chips);
        listCard.Controls.Add(top);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16, 14, 10, 8), Tag = Theme.PanelTag };
        var transport = new TableLayoutPanel { Width = 330, Height = 42, ColumnCount = 2, Margin = new Padding(0), RowCount = 1 };
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _play.Width = _stop.Width = 160;
        _play.Dock = _stop.Dock = DockStyle.Fill;
        _play.Margin = new Padding(0, 0, 4, 8);
        _stop.Margin = new Padding(4, 0, 0, 8);
        transport.Controls.Add(_play, 0, 0);
        transport.Controls.Add(_stop, 1, 0);
        actions.Controls.AddRange([_title, _details, transport, _edit, _removeFromTracks, _delete]);
        var detailsCard = new CardPanel { Dock = DockStyle.Fill };
        detailsCard.Controls.Add(actions);
        var detailsHolder = new Panel { Dock = DockStyle.Right, Width = 384, Padding = new Padding(10, 0, 0, 0) };
        detailsHolder.Controls.Add(detailsCard);

        Controls.Add(listCard);
        Controls.Add(detailsHolder);

        import.Click += (_, _) => PickSongsToImport();
        find.Click += (_, _) => FindOnline();
        _search.Box.TextChanged += (_, _) => RefreshList();
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (!_refreshing)
                ShowDetails();
        };
        _list.DoubleClick += async (_, _) => await PlayOrPauseAsync();
        _play.Click += async (_, _) => await PlayOrPauseAsync();
        _stop.Click += (_, _) => AudioPreview.StopFor(this);
        _edit.Click += (_, _) =>
        {
            if (Selected is { } song)
                _actions.OpenInEditor(song);
        };
        _removeFromTracks.Click += (_, _) => TakeOffTracks();
        _delete.Click += (_, _) =>
        {
            if (Selected is { } song)
                _actions.ConfirmDelete(song);
        };
    }

    internal string? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _visible.Count ? _visible[_list.SelectedIndex] : null;

    private void PickKind(string kind)
    {
        _kind = kind;
        foreach (var (name, chip) in _kindChips)
        {
            chip.Tag = name == kind ? Theme.ActiveTag : "";
            chip.Invalidate();
        }
        RefreshList();
    }

    /// <summary>Rebuilds the list from the library, keeping the selected song selected.</summary>
    internal void RefreshList()
    {
        string? keep = Selected;
        var words = _search.Box.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _visible = _library.Songs
            .Where(song => _kind == AllKinds || _library.KindOf(song).ToString() == _kind)
            .Where(song => words.All(word => SongLibrary.DisplayName(song).Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        _refreshing = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (string song in _visible)
            _list.Items.Add(new SongRows.Row(SongLibrary.DisplayName(song), SummaryOf(song)));
        _list.EndUpdate();
        int again = keep == null ? -1 : _visible.FindIndex(song => song.Equals(keep, StringComparison.OrdinalIgnoreCase));
        _list.SelectedIndex = again >= 0 ? again : _visible.Count > 0 ? 0 : -1;
        _refreshing = false;

        _empty.Visible = _visible.Count == 0;
        _list.Visible = _visible.Count > 0;
        ShowDetails();
        _ = LoadLengthsAsync();
    }

    private string SummaryOf(string song)
    {
        var parts = new List<string> { _library.KindOf(song).ToString() };
        if (_lengths.TryGetValue(song, out double seconds))
            parts.Add(FormatLength(seconds));
        int tracks = _actions.TracksUsing(song);
        if (tracks > 0)
            parts.Add($"on {tracks} {(tracks == 1 ? "track" : "tracks")}");
        return string.Join("  ·  ", parts);
    }

    private static string FormatLength(double seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";

    private async Task LoadLengthsAsync()
    {
        var unknown = _visible.Where(song => !_lengths.ContainsKey(song)).ToList();
        if (unknown.Count == 0)
            return;
        foreach (string song in unknown)
            _lengths[song] = 0;
        var found = await Task.Run(() =>
        {
            var lengths = new Dictionary<string, double>();
            foreach (string song in unknown)
            {
                try
                {
                    lengths[song] = AudioIO.Probe(song).Duration;
                }
                catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The length is only a nicety, so a song that can't be read just doesn't show one.
                }
            }
            return lengths;
        });
        if (IsDisposed)
            return;
        foreach (var (song, seconds) in found)
            _lengths[song] = seconds;
        for (int i = 0; i < _visible.Count && i < _list.Items.Count; i++)
            _list.Items[i] = new SongRows.Row(SongLibrary.DisplayName(_visible[i]), SummaryOf(_visible[i]));
        ShowDetails();
    }

    private void ShowDetails()
    {
        string? song = Selected;
        bool any = song != null;
        _title.Text = any ? SongLibrary.DisplayName(song!) : "Pick a song";
        _title.Font = Theme.TitleFont();
        foreach (var button in new[] { _edit, _removeFromTracks, _delete, _play })
            button.Enabled = any;
        if (!any)
        {
            _details.Text = "";
            ShowPlayback();
            return;
        }

        var kind = _library.KindOf(song!);
        var lines = new List<string> { kind.ToString() + (_lengths.TryGetValue(song!, out double seconds) && seconds > 0 ? $"  ·  {FormatLength(seconds)}" : "") };
        var madeFrom = _library.MadeFrom(song!);
        if (madeFrom.Count > 0)
            lines.Add("Made from " + string.Join(" + ", madeFrom));
        int tracks = _actions.TracksUsing(song!);
        lines.Add(tracks == 0 ? "Not on any tracks" : $"On {tracks} {(tracks == 1 ? "track" : "tracks")}");
        _details.Text = string.Join("\r\n", lines);

        _edit.Text = kind == SongKind.Edited ? "Keep editing" : "Open in Editor";
        _removeFromTracks.Enabled = tracks > 0;
        _delete.Text = _library.IsOwned(song!) ? "Delete song" : "Remove from list";
        ShowPlayback();
    }

    private void ShowPlayback()
    {
        bool active = AudioPreview.IsActive(this);
        bool thisSong = active && string.Equals(_playing, Selected, StringComparison.OrdinalIgnoreCase);
        _play.Text = !thisSong ? "▶  Play" : AudioPreview.State == PreviewState.Paused ? "▶  Resume" : "❚❚  Pause";
        _stop.Enabled = active;
    }

    internal async Task PlayOrPauseAsync()
    {
        if (Selected is not { } song)
            return;
        if (AudioPreview.IsActive(this) && string.Equals(_playing, song, StringComparison.OrdinalIgnoreCase))
        {
            AudioPreview.TogglePause();
            return;
        }
        int request = ++_playRequest;
        _status($"Getting {SongLibrary.DisplayName(song)} ready...");
        try
        {
            string file = await Task.Run(() =>
            {
                const int rate = 44100;
                var channels = AudioIO.Decode(song, rate);
                return AudioIO.WriteRangePreview(channels, rate, 0, channels[0].Length / (double)rate + 1);
            });
            if (IsDisposed || request != _playRequest)
            {
                File.Delete(file);
                return;
            }
            if (!AudioPreview.Play(file, this))
            {
                _status("Could not play the sound. Check your audio device.");
                return;
            }
            _playing = song;
            ShowPlayback();
            _status($"Playing {SongLibrary.DisplayName(song)}.");
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (!IsDisposed)
                _status($"Could not play it. {ex.Message}");
        }
    }

    private void PickSongsToImport()
    {
        using var dialog = new OpenFileDialog { Title = "Import songs or parts", Multiselect = true, Filter = "Audio files|*.wav;*.mp3;*.flac;*.ogg;*.m4a;*.aac;*.opus;*.wma;*.brstm;*.webm;*.mp4|All files|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            ImportSongs(dialog.FileNames);
    }

    internal void ImportSongs(string[] files)
    {
        _library.Add(files);
        _status(files.Length == 1 ? $"Imported {SongLibrary.DisplayName(files[0])}." : $"Imported {files.Length} songs.");
    }

    private void FindOnline()
    {
        using var dialog = new DownloadForm(_songsFolder(), 1);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is not { } song)
            return;
        _library.Add(song.Path);
        _log($"Downloaded {song.Title}.");
        _status($"Downloaded {song.Title}.");
    }

    internal void TakeOffTracks()
    {
        if (Selected is not { } song)
            return;
        int tracks = _actions.RemoveFromTracks(song);
        _status($"Took {SongLibrary.DisplayName(song)} off {tracks} {(tracks == 1 ? "track" : "tracks")}. The song is still in your list.");
        RefreshList();
    }

    /// <summary>Space plays and pauses, and Delete removes the song, except while typing in the search box.</summary>
    internal bool HandleShortcut(Keys key)
    {
        if (FindForm()?.ActiveControl is TextBox { ReadOnly: false })
            return false;
        if (key == Keys.Space)
        {
            _ = PlayOrPauseAsync();
            return true;
        }
        if (key == Keys.Delete && _list.Focused && Selected is { } song)
        {
            _actions.ConfirmDelete(song);
            return true;
        }
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _library.Changed -= RefreshList;
            AudioPreview.Changed -= ShowPlayback;
            AudioPreview.StopFor(this);
        }
        base.Dispose(disposing);
    }

    // For the automated tests.
    internal List<string> VisibleForTest => _visible;
    internal void SelectForTest(string song) => _list.SelectedIndex = _visible.FindIndex(path => path.Equals(Path.GetFullPath(song), StringComparison.OrdinalIgnoreCase));
    internal void PickKindForTest(SongKind? kind) => PickKind(kind?.ToString() ?? AllKinds);
    internal void SearchForTest(string text) => _search.Box.Text = text;
    internal string DetailsForTest => _details.Text;
    internal string PlayTextForTest => _play.Text;
    internal string DeleteTextForTest => _delete.Text;
    internal string EditTextForTest => _edit.Text;
    internal bool TakeOffEnabledForTest => _removeFromTracks.Enabled;
    internal string RowTextForTest(int index) => ((SongRows.Row)_list.Items[index]).Details;
}

/// <summary>Two-line song rows: the name, then what kind of song it is and where it's used.</summary>
public sealed class SongRows : ListBox
{
    public sealed record Row(string Name, string Details)
    {
        public override string ToString() => Name;
    }

    private readonly Font _smallFont = Theme.UiFont(8.5f);

    public SongRows()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        ItemHeight = Theme.Px(52);
        IntegralHeight = false;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not Row row)
            return;
        var p = Theme.Current;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var g = e.Graphics;
        using (var back = new SolidBrush(p.Panel))
            g.FillRectangle(back, e.Bounds);
        if (selected)
        {
            Theme.FillBox(g, new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 4, e.Bounds.Height - 4), p.Selection);
            Theme.SelectionBar(g, e.Bounds.X + 2, e.Bounds.Y + 14, e.Bounds.Height - 28);
        }
        var name = new Rectangle(e.Bounds.X + 14, e.Bounds.Y + 7, e.Bounds.Width - 28, Font.Height + 2);
        TextRenderer.DrawText(g, row.Name, Font, name, selected ? p.SelectionText : p.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var details = new Rectangle(e.Bounds.X + 14, e.Bounds.Y + 28, e.Bounds.Width - 28, _smallFont.Height + 2);
        TextRenderer.DrawText(g, row.Details, _smallFont, details, p.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _smallFont.Dispose();
        base.Dispose(disposing);
    }
}

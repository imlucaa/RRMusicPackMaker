using System.ComponentModel;

namespace RRMusicPackMaker;

/// <summary>A song loaded for editing, with a summary of its waveform for quick drawing.</summary>
public sealed class EditSource
{
    public const int Step = 256;

    public short[][] Channels { get; }
    public short[] Low { get; }
    public short[] High { get; }
    public long Length => Channels[0].Length;

    public EditSource(short[][] channels)
    {
        Channels = channels;
        int count = channels[0].Length / Step + 1;
        Low = new short[count];
        High = new short[count];
        for (int i = 0; i < count; i++)
        {
            short low = short.MaxValue, high = short.MinValue;
            int end = Math.Min(channels[0].Length, (i + 1) * Step);
            for (int s = i * Step; s < end; s++)
            {
                short mono = (short)((channels[0][s] + channels[1][s]) / 2);
                if (mono < low) low = mono;
                if (mono > high) high = mono;
            }
            Low[i] = low;
            High[i] = high;
        }
    }
}

/// <summary>The Editor tab: cut songs into parts, reorder them, join them with others and save the result as a new song.</summary>
public sealed class EditorPanel : Panel
{
    private const double PreviewSeconds = 60;
    private static readonly (string Text, double Seconds)[] EqualChoices =
        [("As long as possible", 0), ("30 seconds", 30), ("45 seconds", 45), ("1 minute", 60), ("1:30", 90), ("2 minutes", 120)];
    private static readonly (string Text, double Seconds)[] BlendChoices =
        [("Off", 0), ("0.1 seconds", 0.1), ("0.5 seconds", 0.5), ("1 second", 1), ("2 seconds", 2), ("4 seconds", 4)];

    private readonly Func<string> _songsFolder;
    private readonly Action<string> _status;
    private readonly Action<string> _log;
    private readonly Func<string, int> _tracksUsing;
    private readonly Action<string> _songDeleted;
    private readonly Dictionary<string, EditSource> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<List<EditClip>> _undo = new();
    private readonly Stack<List<EditClip>> _redo = new();
    private readonly SongLibrary _library;
    private readonly TimelineView _timeline;
    private readonly SideList _songList = new() { Dock = DockStyle.Fill, Tag = Theme.PanelTag };
    private readonly Label _info = new() { AutoSize = false, Height = 26, Dock = DockStyle.Bottom, Tag = Theme.MutedTag, TextAlign = ContentAlignment.MiddleLeft };
    private readonly FieldBox _name = new("Name for the new song") { Width = 240, Margin = new Padding(14, 1, 0, 0) };
    private readonly ComboBox _blend = new FlatCombo { Width = 130, Margin = new Padding(0, 2, 0, 0) };
    private readonly ComboBox _equalLength = new FlatCombo { Width = 170, Margin = new Padding(0, 2, 6, 0) };
    private readonly ComboBox _speed = new FlatCombo { Width = 230, Margin = new Padding(0, 2, 0, 0), MaxDropDownItems = 14 };
    private readonly ModernButton _makeEqual = Tool("Make parts equal");
    private readonly ModernButton _play = Tool("▶  Play", Theme.AccentTag);
    private readonly ModernButton _pause = Tool("❚❚  Pause");
    private readonly ModernButton _stop = Tool("■  Stop");
    private readonly ModernButton _split = Tool("Split");
    private readonly ModernButton _delete = Tool("Delete part");
    private readonly ModernButton _cropStart = Tool("Crop start here");
    private readonly ModernButton _cropEnd = Tool("Crop end here");
    private readonly ModernButton _undoButton = Tool("Undo");
    private readonly ModernButton _redoButton = Tool("Redo");
    private readonly ModernButton _clear = Tool("Clear");
    private readonly ModernButton _save = Tool("Save song", Theme.AccentTag);
    private readonly ModernButton _deleteSong = new() { Text = "Delete song", Width = 262, Height = 28, Margin = new Padding(0, 0, 0, 10) };
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 50 };
    private long _playFrom;
    private double _playSpeed = 1;
    private int _playRequest;
    private bool _saving;
    private bool _restored;
    private bool _restoring;
    private Point? _songDragStart;

    public EditorPanel(Func<string> songsFolder, Action<string> status, Action<string> log, Func<string, int> tracksUsing, Action<string> songDeleted, SongLibrary library)
    {
        _library = library;
        _library.Changed += RefreshSongs;
        _tracksUsing = tracksUsing;
        _songDeleted = songDeleted;
        _songsFolder = songsFolder;
        _status = status;
        _log = log;
        _timeline = new TimelineView(source => _sources.GetValueOrDefault(source)) { Dock = DockStyle.Fill };
        Dock = DockStyle.Fill;
        Padding = new Padding(12, 0, 12, 0);
        Visible = false;
        BuildLayout();
        UpdateState();
    }

    private static ModernButton Tool(string text, string tag = "") =>
        new() { Text = text, AutoSize = true, Height = 28, Margin = new Padding(0, 0, 6, 6), Tag = tag };

    private void BuildLayout()
    {
        var addSong = new ModernButton { Text = "Add to timeline", Tag = Theme.AccentTag, Width = 262, Height = 28, Margin = new Padding(0, 8, 0, 6) };
        var openFile = new ModernButton { Text = "Import songs or parts", Width = 262, Height = 28, Margin = new Padding(0, 0, 0, 6) };
        var findOnline = new ModernButton { Text = "Find a song online", Width = 262, Height = 28, Margin = new Padding(0, 0, 0, 6) };
        var songsTop = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(10, 10, 0, 0) };
        songsTop.Controls.Add(new Label { Text = "Your songs", AutoSize = true, Font = Theme.TitleFont(11f), Margin = new Padding(0) });
        songsTop.Controls.Add(new Label { Text = "Drag one onto the timeline, or double-click.", AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 2, 0, 0) });
        songsTop.Controls.AddRange([addSong, openFile, findOnline, _deleteSong]);
        var songsCard = new CardPanel { Dock = DockStyle.Fill };
        songsCard.Controls.Add(_songList);
        songsCard.Controls.Add(songsTop);
        var songsHolder = new Panel { Dock = DockStyle.Left, Width = 298, Padding = new Padding(0, 0, 10, 0) };
        songsHolder.Controls.Add(songsCard);

        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 10, 10, 0) };
        tools.Controls.AddRange([_play, _pause, _stop, _cropStart, _cropEnd, _split, _delete, _undoButton, _redoButton, _clear]);
        var help = new Label
        {
            Text = "Drag a part's edge to crop, or its middle to move. Drag songs in to merge. Scroll to zoom. Q and W crop to the cursor, S splits, Space pauses.",
            AutoSize = true,
            Dock = DockStyle.Top,
            Tag = Theme.MutedTag,
            Padding = new Padding(12, 0, 0, 8),
        };
        var timelineHolder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
        timelineHolder.Controls.Add(_timeline);

        foreach (var (text, _) in BlendChoices)
            _blend.Items.Add(text);
        _blend.SelectedIndex = 0;
        foreach (var (text, _) in EqualChoices)
            _equalLength.Items.Add(text);
        _equalLength.SelectedIndex = 0;
        var mergeRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Padding = new Padding(12, 4, 12, 0) };
        mergeRow.Controls.Add(new Label { Text = "Each part plays", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        mergeRow.Controls.Add(_equalLength);
        _makeEqual.Margin = new Padding(0, 0, 22, 0);
        mergeRow.Controls.Add(_makeEqual);
        mergeRow.Controls.Add(new Label { Text = "Blend the joins", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        _blend.Margin = new Padding(0, 2, 22, 0);
        mergeRow.Controls.Add(_blend);
        foreach (var choice in PackBuilder.SpeedChoices)
            _speed.Items.Add(choice.Text);
        _speed.SelectedIndex = 0;
        mergeRow.Controls.Add(new Label { Text = "Speed", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
        mergeRow.Controls.Add(_speed);
        var saveRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Padding = new Padding(12, 6, 12, 8) };
        _name.Margin = new Padding(0, 1, 0, 0);
        saveRow.Controls.Add(_name);
        _save.Margin = new Padding(8, 0, 0, 0);
        saveRow.Controls.Add(_save);
        _info.Padding = new Padding(12, 0, 12, 0);

        var editCard = new CardPanel { Dock = DockStyle.Fill };
        editCard.Controls.Add(timelineHolder);
        editCard.Controls.Add(_info);
        editCard.Controls.Add(mergeRow);
        editCard.Controls.Add(saveRow);
        editCard.Controls.Add(help);
        editCard.Controls.Add(tools);

        Controls.Add(editCard);
        Controls.Add(songsHolder);

        addSong.Click += async (_, _) => await AddPickedSongAsync();
        _songList.DoubleClick += async (_, _) => await AddPickedSongAsync();
        openFile.Click += (_, _) => PickSongsToImport();
        findOnline.Click += async (_, _) => await FindOnlineAsync();
        _deleteSong.Click += (_, _) => ConfirmDeleteSelected();
        _makeEqual.Click += (_, _) => MakePartsEqual();
        _songList.SelectedIndexChanged += (_, _) => _deleteSong.Enabled = _songList.SelectedIndex >= 0;
        _play.Click += async (_, _) => await PlayAsync();
        _pause.Click += (_, _) => TogglePause();
        AudioPreview.Changed += ShowPlayback;
        ShowPlayback();
        _stop.Click += (_, _) => StopPlaying();
        _split.Click += (_, _) => Split();
        _delete.Click += (_, _) => DeletePart();
        _cropStart.Click += (_, _) => CropStart();
        _cropEnd.Click += (_, _) => CropEnd();
        _undoButton.Click += (_, _) => Undo();
        _redoButton.Click += (_, _) => Redo();
        _clear.Click += (_, _) => Commit([], -1);
        _save.Click += async (_, _) => await SaveAsync();
        _name.Box.TextChanged += (_, _) => SaveState();
        _blend.SelectedIndexChanged += (_, _) => SaveState();
        _speed.SelectedIndexChanged += (_, _) =>
        {
            SaveState();
            UpdateState();
        };
        _timeline.PositionChanged += UpdateState;
        _timeline.PartDragged += (from, insertBefore) => MovePart(from, insertBefore);
        _timeline.PartTrimmed += TrimPart;
        _timeline.AllowDrop = true;
        _timeline.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        _timeline.DragOver += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                _timeline.ShowInsertAt(_timeline.PointToClient(new Point(e.X, e.Y)).X);
        };
        _timeline.DragLeave += (_, _) => _timeline.ClearInsert();
        _timeline.DragDrop += async (_, e) =>
        {
            int at = _timeline.PositionForDrop(_timeline.PointToClient(new Point(e.X, e.Y)).X);
            _timeline.ClearInsert();
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
                foreach (string file in files.Where(PackBuilder.IsAudioFile))
                    await AddSongAsync(file, at++);
        };
        _songList.MouseDown += (_, e) => _songDragStart = e.Button == MouseButtons.Left ? e.Location : null;
        _songList.MouseUp += (_, _) => _songDragStart = null;
        _songList.MouseMove += (_, e) =>
        {
            if (_songDragStart is not { } start || e.Button != MouseButtons.Left)
                return;
            if (Math.Abs(e.X - start.X) < SystemInformation.DragSize.Width && Math.Abs(e.Y - start.Y) < SystemInformation.DragSize.Height)
                return;
            _songDragStart = null;
            int index = _songList.IndexFromPoint(start);
            if (index >= 0 && index < SongPaths.Count)
                _songList.DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { SongPaths[index] }), DragDropEffects.Copy);
        };
        _tick.Tick += (_, _) => FollowPlayback();
    }

    /// <summary>Called when the tab opens. Brings back the last edit the first time.</summary>
    internal async Task ActivateAsync()
    {
        RefreshSongs();
        if (_restored || _restoring)
            return;
        _restoring = true;
        var state = EditorState.Load();
        _name.Box.Text = state.Name;
        _blend.SelectedIndex = Math.Clamp(state.Blend, 0, BlendChoices.Length - 1);
        _speed.SelectedIndex = Math.Clamp(state.Speed, 0, PackBuilder.SpeedChoices.Length - 1);

        var kept = new List<EditClip>();
        foreach (var clip in state.Clips)
        {
            if (await LoadAsync(clip.Source) != null)
            {
                kept.Add(clip);
                _library.Add(clip.Source);
            }
            else
                _log($"Editor: left out {clip.Name} because the file couldn't be loaded.");
            if (IsDisposed)
                return;
        }
        _restored = true;
        _restoring = false;
        _status("");
        if (kept.Count > 0)
        {
            _timeline.SetClips(kept, -1);
            _timeline.Fit();
            RefreshSongs();
        }
        UpdateState();
    }

    private void RefreshSongs()
    {
        int index = _songList.SelectedIndex;
        string? picked = index >= 0 && index < SongPaths.Count ? SongPaths[index] : null;

        // Undo can bring back a part whose song left the list, and it should still be findable.
        var clipSongs = _timeline.Clips.Select(clip => clip.Source).Where(File.Exists).Select(Path.GetFullPath);
        SongPaths = _library.Songs.Concat(clipSongs).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        _songList.BeginUpdate();
        _songList.Items.Clear();
        foreach (string song in SongPaths)
            _songList.Items.Add(new SideList.Item(_library.LabelOf(song), -1));
        _songList.EndUpdate();
        int again = picked == null ? -1 : SongPaths.FindIndex(song => song.Equals(picked, StringComparison.OrdinalIgnoreCase));
        if (again >= 0)
            _songList.SelectedIndex = again;
        _deleteSong.Enabled = _songList.SelectedIndex >= 0;
    }

    internal List<string> SongPaths { get; private set; } = [];

    /// <summary>Only songs in the downloads folder belong to the app. A file opened from anywhere else is the user's own.</summary>
    internal bool IsDownloadedSong(string path) => _library.IsOwned(path);

    private void ConfirmDeleteSelected()
    {
        int index = _songList.SelectedIndex;
        if (index < 0 || index >= SongPaths.Count)
        {
            _status("Pick a song in the list first.");
            return;
        }
        ConfirmDeleteSong(SongPaths[index]);
    }

    /// <summary>Asks first, then deletes the song. Every page's delete button comes through here.</summary>
    internal void ConfirmDeleteSong(string path)
    {
        string name = SongLibrary.DisplayName(path);
        int parts = _timeline.Clips.Count(clip => SamePath(clip.Source, path));
        int tracks = _tracksUsing(path);
        var lines = new List<string>
        {
            IsDownloadedSong(path)
                ? $"Delete \"{name}\"? It goes to the Recycle Bin."
                : $"Take \"{name}\" off this list? The file stays where it is.",
        };
        if (parts > 0)
            lines.Add($"{parts} {(parts == 1 ? "part" : "parts")} on the timeline will be removed. Undo brings them back until you close the app.");
        if (tracks > 0)
            lines.Add($"{tracks} {(tracks == 1 ? "track uses" : "tracks use")} this song and will go back to their old music. Packs you already made aren't affected.");
        if (MessageBox.Show(this, string.Join("\r\n\r\n", lines), "Delete song", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            DeleteSong(path);
    }

    private static bool SamePath(string a, string b) =>
        Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Removes a song from the list and the timeline, and sends it to the Recycle Bin if it's a download.</summary>
    internal bool DeleteSong(string path)
    {
        string name = SongLibrary.DisplayName(path);
        if (IsDownloadedSong(path))
        {
            try
            {
                RemoveFile(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                _status($"Could not delete {name}. See the log.");
                _log($"Editor: could not delete {path}. {ex.Message}");
                return false;
            }
            _log($"Editor: sent {name} to the Recycle Bin.");
        }
        _library.Remove(path);
        if (_timeline.Clips.Any(clip => SamePath(clip.Source, path)))
        {
            var kept = _timeline.Clips.Where(clip => !SamePath(clip.Source, path)).ToList();
            Commit(kept, -1);
            if (kept.Count > 0)
                _timeline.Fit();
        }
        RefreshSongs();
        _songDeleted(path);
        _status($"Deleted {name}.");
        return true;
    }

    /// <summary>Takes parts off the timeline when their songs are gone. Undo still brings them back this session.</summary>
    internal void RemovePartsOf(IEnumerable<string> songs)
    {
        var gone = songs.ToList();
        if (!_timeline.Clips.Any(clip => gone.Any(song => SamePath(clip.Source, song))))
            return;
        Commit(_timeline.Clips.Where(clip => !gone.Any(song => SamePath(clip.Source, song))).ToList(), -1);
    }

    /// <summary>The Recycle Bin keeps a mistake recoverable. Tests swap this out so they don't fill the real one.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<string> RemoveFile { get; set; } = path =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

    private async Task AddPickedSongAsync()
    {
        int index = _songList.SelectedIndex;
        if (index < 0 || index >= SongPaths.Count)
        {
            _status("Pick a song in the list first.");
            return;
        }
        await AddSongAsync(SongPaths[index]);
    }

    private void PickSongsToImport()
    {
        using var dialog = new OpenFileDialog { Title = "Import songs or parts", Multiselect = true, Filter = "Audio files|*.wav;*.mp3;*.flac;*.ogg;*.m4a;*.aac;*.opus;*.wma;*.brstm;*.webm;*.mp4|All files|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            ImportSongs(dialog.FileNames);
    }

    /// <summary>Adds songs to the list without putting them on the timeline.</summary>
    internal void ImportSongs(string[] files)
    {
        _library.Add(files);
        _status(files.Length == 1 ? $"Imported {SongLibrary.DisplayName(files[0])}." : $"Imported {files.Length} songs.");
    }

    private async Task FindOnlineAsync()
    {
        using var dialog = new DownloadForm(_songsFolder(), 1);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is not { } song)
            return;
        _library.Add(song.Path);
        await AddSongAsync(song.Path);
    }

    private async Task<EditSource?> LoadAsync(string path)
    {
        if (_sources.TryGetValue(path, out var known))
            return known;
        try
        {
            _status($"Loading {Path.GetFileNameWithoutExtension(path)}...");
            var source = await Task.Run(() => new EditSource(AudioIO.Decode(path, EditTimeline.Rate)));
            _sources[path] = source;
            return source;
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            if (!IsDisposed)
                _status($"Could not load {Path.GetFileName(path)}. See the log.");
            _log($"Editor: could not load {path}. {ex.Message}");
            return null;
        }
    }

    /// <summary>Adds a whole song where it was dropped. Otherwise it goes after the selected part, or at the end.</summary>
    internal async Task AddSongAsync(string path, int? insertAt = null)
    {
        var source = await LoadAsync(path);
        if (source == null || IsDisposed)
            return;
        _library.Add(path);
        var next = _timeline.Clips.ToList();
        int index = Math.Clamp(insertAt ?? (_timeline.Selected >= 0 ? _timeline.Selected + 1 : next.Count), 0, next.Count);
        next.Insert(index, new EditClip(path, 0, source.Length));
        Commit(next, index);
        _timeline.Fit();
        RefreshSongs();
        _status($"Added {SongLibrary.DisplayName(path)}.");
    }

    private void Commit(List<EditClip> next, int selected)
    {
        _undo.Push(_timeline.Clips);
        _redo.Clear();
        Apply(next, selected);
    }

    private void Apply(List<EditClip> clips, int selected)
    {
        StopPlaying();
        _timeline.SetClips(clips, selected);
        SaveState();
        UpdateState();
    }

    private void SaveState()
    {
        if (_restored)
            new EditorState { Clips = _timeline.Clips, Name = _name.Box.Text, Blend = Math.Max(0, _blend.SelectedIndex), Speed = Math.Max(0, _speed.SelectedIndex) }.Save();
    }

    internal void Split()
    {
        var next = EditTimeline.SplitAt(_timeline.Clips, _timeline.Position);
        if (next.Count == _timeline.Clips.Count)
        {
            _status("Place the cursor inside a part first.");
            return;
        }
        Commit(next, EditTimeline.ClipAt(next, _timeline.Position));
    }

    internal void DeletePart()
    {
        int index = _timeline.Selected;
        if (index < 0 || index >= _timeline.Clips.Count)
        {
            _status("Click a part first.");
            return;
        }
        var next = _timeline.Clips.ToList();
        next.RemoveAt(index);
        Commit(next, Math.Min(index, next.Count - 1));
    }

    internal void CropStart()
    {
        long position = _timeline.Position;
        int index = EditTimeline.ClipAt(_timeline.Clips, position);
        var next = EditTimeline.CropStart(_timeline.Clips, position);
        if (next.SequenceEqual(_timeline.Clips))
        {
            _status("Place the cursor inside a part first.");
            return;
        }
        Commit(next, index);
        _timeline.Position = EditTimeline.StartOf(next, index);
        _status($"Cropped the start of {next[index].Name}.");
    }

    internal void CropEnd()
    {
        int index = EditTimeline.ClipAt(_timeline.Clips, _timeline.Position);
        var next = EditTimeline.CropEnd(_timeline.Clips, _timeline.Position);
        if (next.SequenceEqual(_timeline.Clips))
        {
            _status("Place the cursor inside a part first.");
            return;
        }
        Commit(next, index);
        _status($"Cropped the end of {next[index].Name}.");
    }

    /// <summary>Evens out the parts, so two merged songs each get the same time.</summary>
    internal void MakePartsEqual()
    {
        var clips = _timeline.Clips;
        if (clips.Count < 2)
        {
            _status("Add at least two parts first.");
            return;
        }
        double seconds = EqualChoices[Math.Max(0, _equalLength.SelectedIndex)].Seconds;
        var next = EditTimeline.MakeEqual(clips, seconds > 0 ? (long)(seconds * EditTimeline.Rate) : null);
        double each = next[0].Length / (double)EditTimeline.Rate;
        if (next.SequenceEqual(clips))
        {
            _status($"The parts are already equal, at {AudioIO.FormatTime(each)} each.");
            return;
        }
        Commit(next, -1);
        _timeline.Fit();
        _status(seconds > each + 0.01
            ? $"The shortest part only has {AudioIO.FormatTime(each)}, so each part now plays that long."
            : $"Each part now plays {AudioIO.FormatTime(each)}.");
    }

    /// <summary>Opens a song to work on. An edited song comes back as its parts, otherwise it's added to the timeline.</summary>
    internal async Task OpenSongAsync(string path)
    {
        if (_library.PartsOf(path) is not { } parts)
        {
            await AddSongAsync(path);
            return;
        }
        var kept = new List<EditClip>();
        foreach (var part in parts)
        {
            if (await LoadAsync(part.Source) != null)
                kept.Add(part);
            if (IsDisposed)
                return;
        }
        if (kept.Count == 0)
        {
            _status("The songs it was made from are gone, so its parts can't come back.");
            return;
        }
        Commit(kept, -1);
        _timeline.Fit();
        _name.Box.Text = SongLibrary.DisplayName(path);
        _status(kept.Count == parts.Count
            ? $"Opened the parts of {SongLibrary.DisplayName(path)}. Saving makes a new version."
            : $"Opened {kept.Count} of {parts.Count} parts. The songs for the others are gone.");
    }

    private void TrimPart(int index, long start, long end)
    {
        var next = _timeline.Clips.ToList();
        next[index] = next[index] with { Start = start, End = end };
        Commit(next, index);
        _status($"Cropped {next[index].Name}. Undo brings it back.");
    }

    internal void MovePart(int from, int insertBefore)
    {
        var next = EditTimeline.Move(_timeline.Clips, from, insertBefore);
        Commit(next, insertBefore > from ? insertBefore - 1 : insertBefore);
    }

    internal void Undo()
    {
        if (_undo.Count == 0)
            return;
        _redo.Push(_timeline.Clips);
        Apply(_undo.Pop(), -1);
    }

    internal void Redo()
    {
        if (_redo.Count == 0)
            return;
        _undo.Push(_timeline.Clips);
        Apply(_redo.Pop(), -1);
    }

    private int BlendSamples => (int)(BlendChoices[Math.Max(0, _blend.SelectedIndex)].Seconds * EditTimeline.Rate);

    private (string Text, double Speed, bool KeepPitch, bool Reverb) SpeedChoice => PackBuilder.SpeedChoices[Math.Max(0, _speed.SelectedIndex)];

    /// <summary>
    /// Plays the edit at the chosen speed, with the same processing a track's speed gets in the pack:
    /// nightcore and slowed move the pitch, "same pitch" keeps it, and reverb is added on top.
    /// </summary>
    internal static short[][] ApplySpeed(short[][] audio, (string Text, double Speed, bool KeepPitch, bool Reverb) choice)
    {
        if (choice.Speed == 1 && !choice.Reverb)
            return audio;
        string temp = AudioIO.WriteRangePreview(audio, EditTimeline.Rate, 0, audio[0].Length / (double)EditTimeline.Rate + 1);
        try
        {
            return Processing.Prepare(temp, EditTimeline.Rate, LoopMode.NoLoop, "", "", Options.LoudnessOff, null, 0, 0, choice.Speed, choice.KeepPitch, choice.Reverb).Channels;
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private Dictionary<string, short[][]> AudioFor(IEnumerable<EditClip> clips) =>
        clips.Select(clip => clip.Source).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(source => source, source => _sources[source].Channels, StringComparer.OrdinalIgnoreCase);

    internal bool IsPlaying => AudioPreview.IsActive(this);
    internal bool IsPaused => IsPlaying && AudioPreview.State == PreviewState.Paused;

    /// <summary>Keeps the buttons and playhead right, however playback changed. It can end by itself or from another window.</summary>
    private void ShowPlayback()
    {
        bool active = IsPlaying;
        _pause.Enabled = active;
        _stop.Enabled = active;
        _pause.Text = IsPaused ? "▶  Resume" : "❚❚  Pause";
        if (!active)
        {
            _tick.Stop();
            _timeline.PlayPosition = -1;
        }
    }

    internal void TogglePause()
    {
        if (!IsPlaying)
            return;
        AudioPreview.TogglePause();
        _status(IsPaused ? "Paused. Press Space to carry on." : "Playing. Press Esc to stop.");
    }

    internal async Task PlayAsync()
    {
        var clips = _timeline.Clips;
        if (clips.Count == 0)
            return;
        StopPlaying();
        int request = ++_playRequest;
        long total = EditTimeline.Length(clips);
        long from = _timeline.Position >= total ? 0 : _timeline.Position;
        var speed = SpeedChoice;
        var part = EditTimeline.Slice(clips, from, (long)(PreviewSeconds * speed.Speed * EditTimeline.Rate));
        var audio = AudioFor(part);
        int blend = BlendSamples;
        _status("Getting ready to play...");
        string file;
        try
        {
            file = await Task.Run(() =>
            {
                var rendered = ApplySpeed(EditTimeline.Render(part, source => audio[source], blend), speed);
                return AudioIO.WriteRangePreview(rendered, EditTimeline.Rate, 0, PreviewSeconds + 1);
            });
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.ComponentModel.Win32Exception)
        {
            if (!IsDisposed)
                _status($"Could not play it. {ex.Message}");
            return;
        }
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
        _playFrom = from;
        _playSpeed = speed.Speed;
        _tick.Start();
        _status($"Playing up to {PreviewSeconds:0} seconds. Press Esc to stop.");
    }

    private void FollowPlayback()
    {
        if (IsPlaying)
            _timeline.PlayPosition = _playFrom + (long)(AudioPreview.PositionMilliseconds * _playSpeed * EditTimeline.Rate / 1000);
    }

    internal void StopPlaying()
    {
        _playRequest++;
        AudioPreview.StopFor(this);
        ShowPlayback();
    }

    /// <summary>Saves the timeline as a new song in the downloads folder. Returns its path, or null if it didn't save.</summary>
    internal async Task<string?> SaveAsync()
    {
        var clips = _timeline.Clips;
        if (clips.Count == 0 || _saving)
            return null;
        StopPlaying();
        string name = _name.Box.Text.Trim();
        if (name.Length == 0)
            name = clips[0].Name + " edit";
        var audio = AudioFor(clips);
        int blend = BlendSamples;
        var speed = SpeedChoice;
        string folder = _songsFolder();
        _saving = true;
        UpdateState();
        _status("Saving the song...");
        try
        {
            string path = await Task.Run(() =>
            {
                var rendered = ApplySpeed(EditTimeline.Render(clips, source => audio[source], blend), speed);
                string seconds = AudioIO.WriteRangePreview(rendered, EditTimeline.Rate, 0, rendered[0].Length / (double)EditTimeline.Rate + 1);
                Directory.CreateDirectory(folder);
                string target = EditTimeline.UniquePath(folder, name);
                File.Move(seconds, target);
                return target;
            });
            if (IsDisposed)
                return path;
            _log($"Editor: saved {path}");
            _library.AddEdit(path, clips);
            _status($"Saved {name}. It's in your songs, and in the dropdown on every track.");
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            if (!IsDisposed)
                _status("Could not save the song. See the log.");
            _log($"Editor: could not save. {ex.Message}");
            return null;
        }
        finally
        {
            _saving = false;
            if (!IsDisposed)
                UpdateState();
        }
    }

    private void UpdateState()
    {
        var clips = _timeline.Clips;
        bool any = clips.Count > 0;
        _play.Enabled = any;
        _split.Enabled = any;
        _delete.Enabled = any;
        _cropStart.Enabled = any;
        _cropEnd.Enabled = any;
        _makeEqual.Enabled = clips.Count > 1;
        _clear.Enabled = any;
        _undoButton.Enabled = _undo.Count > 0;
        _redoButton.Enabled = _redo.Count > 0;
        _save.Enabled = any && !_saving;
        double total = EditTimeline.Length(clips) / (double)EditTimeline.Rate;
        double cursor = _timeline.Position / (double)EditTimeline.Rate;
        _info.Text = any
            ? $"{clips.Count} {(clips.Count == 1 ? "part" : "parts")}   ·   {AudioIO.FormatTime(total)} total   ·   cursor at {AudioIO.FormatTime(cursor)}"
            : "Add a song to start. You can save once there's something on the timeline.";
    }

    /// <summary>Keys for the timeline. Text boxes keep theirs, so a name can still be typed.</summary>
    internal bool HandleShortcut(Keys key)
    {
        if (key == Keys.Escape && IsPlaying)
        {
            StopPlaying();
            _status("Stopped.");
            return true;
        }
        if (FindForm()?.ActiveControl is TextBox { ReadOnly: false })
            return false;
        switch (key)
        {
            case Keys.Space:
                if (IsPlaying)
                    TogglePause();
                else
                    _ = PlayAsync();
                return true;
            case Keys.S:
                Split();
                return true;
            case Keys.Q:
                CropStart();
                return true;
            case Keys.W:
                CropEnd();
                return true;
            case Keys.Delete:
                if (_songList.Focused)
                    ConfirmDeleteSelected();
                else
                    DeletePart();
                return true;
            case Keys.Control | Keys.Z:
                Undo();
                return true;
            case Keys.Control | Keys.Y:
                Redo();
                return true;
            default:
                return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AudioPreview.Changed -= ShowPlayback;
            StopPlaying();
            _tick.Dispose();
        }
        base.Dispose(disposing);
    }

    // For the automated tests and screenshots.
    internal IReadOnlyList<EditClip> ClipsForTest => _timeline.Clips;
    internal TimelineView TimelineForTest => _timeline;
    internal void SetPositionForTest(long position) => _timeline.Position = position;
    internal void SelectPartForTest(int index) => _timeline.Selected = index;
    internal void SetNameForTest(string name) => _name.Box.Text = name;
    internal void SetBlendForTest(int index) => _blend.SelectedIndex = index;
    internal void SetSpeedForTest(int index) => _speed.SelectedIndex = index;
    internal void SetEqualChoiceForTest(int index) => _equalLength.SelectedIndex = index;
    internal string InfoForTest => _info.Text;
    internal string NameForTest => _name.Box.Text;
}

/// <summary>The parts of the song in order, drawn as waveforms on one line.</summary>
internal sealed class TimelineView : Control
{
    private const int RulerHeight = 20;
    private const int DragDistance = 6;
    private static readonly int[] TickSeconds = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1200];

    private readonly Func<string, EditSource?> _audio;
    private double _viewStart;
    private double _viewLength = EditTimeline.Rate * 60.0;
    private long _position;
    private long _playPosition = -1;
    private int _selected = -1;
    private int _dragFrom = -1;
    private int _dragStartX;
    private bool _dragging;
    private int _insertAt = -1;
    private const int EdgeGrab = 7;
    private int _trimIndex = -1;
    private bool _trimStartEdge;
    private int _trimX;
    private int _trimDownX;
    private bool _panning;
    private int _panX;
    private double _panStart;

    public event Action? PositionChanged;
    public event Action<int, int>? PartDragged;
    public event Action<int, long, long>? PartTrimmed;

    internal List<EditClip> Clips { get; private set; } = [];

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal long Position
    {
        get => _position;
        set
        {
            _position = Math.Clamp(value, 0, EditTimeline.Length(Clips));
            Invalidate();
            PositionChanged?.Invoke();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal long PlayPosition
    {
        get => _playPosition;
        set
        {
            _playPosition = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
        }
    }

    public TimelineView(Func<string, EditSource?> audio)
    {
        _audio = audio;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    internal void SetClips(List<EditClip> clips, int selected)
    {
        Clips = clips;
        _selected = selected < clips.Count ? selected : -1;
        long total = EditTimeline.Length(clips);
        _position = Math.Min(_position, total);
        _viewLength = Math.Min(_viewLength, Math.Max(2000, total));
        _viewStart = Math.Clamp(_viewStart, 0, Math.Max(0, total - _viewLength));
        Invalidate();
        PositionChanged?.Invoke();
    }

    /// <summary>Shows the whole song.</summary>
    internal void Fit()
    {
        _viewStart = 0;
        _viewLength = Math.Max(2000, EditTimeline.Length(Clips));
        Invalidate();
    }

    internal void ShowInsertAt(int x)
    {
        _insertAt = Clips.Count == 0 ? 0 : NearestJoin(x);
        Invalidate();
    }

    internal void ClearInsert()
    {
        _insertAt = -1;
        Invalidate();
    }

    internal int PositionForDrop(int x) => Clips.Count == 0 ? 0 : NearestJoin(x);

    /// <summary>The part edge under the mouse. Inside a join, each side of the line grabs the part on that side.</summary>
    private (int Index, bool StartEdge)? EdgeAt(int x)
    {
        long start = 0;
        for (int i = 0; i < Clips.Count; i++)
        {
            double x1 = PositionToX(start);
            double x2 = PositionToX(start + Clips[i].Length);
            start += Clips[i].Length;
            if (x2 - x1 < EdgeGrab * 3)
                continue;
            if (x >= x1 - (i == 0 ? EdgeGrab : 0) && x <= x1 + EdgeGrab)
                return (i, true);
            if (x <= x2 + (i == Clips.Count - 1 ? EdgeGrab : 0) && x >= x2 - EdgeGrab)
                return (i, false);
        }
        return null;
    }

    /// <summary>Where a crop would land, kept inside the part so something is always left.</summary>
    private long TrimOffset(int index, int x)
    {
        long inside = XToPosition(x) - EditTimeline.StartOf(Clips, index);
        return Math.Clamp(inside, EditTimeline.ShortestPart, Math.Max(EditTimeline.ShortestPart, Clips[index].Length - EditTimeline.ShortestPart));
    }

    private double PerPixel => _viewLength / Math.Max(1, Width);
    private long XToPosition(int x) => (long)(_viewStart + x * PerPixel);
    private double PositionToX(double position) => (position - _viewStart) / PerPixel;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (Clips.Count == 0)
            return;
        if (e.Button == MouseButtons.Right)
        {
            _panning = true;
            _panX = e.X;
            _panStart = _viewStart;
            return;
        }
        if (EdgeAt(e.X) is { } edge)
        {
            _trimIndex = edge.Index;
            _trimStartEdge = edge.StartEdge;
            _trimX = e.X;
            _trimDownX = e.X;
            _selected = edge.Index;
            Invalidate();
            return;
        }
        Position = XToPosition(e.X);
        _selected = EditTimeline.ClipAt(Clips, XToPosition(e.X));
        _dragFrom = _selected;
        _dragStartX = e.X;
        _dragging = false;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_trimIndex >= 0)
        {
            _trimX = e.X;
            Invalidate();
            return;
        }
        if (e.Button == MouseButtons.None)
            Cursor = EdgeAt(e.X) != null ? Cursors.SizeWE : Cursors.Default;
        if (_panning)
        {
            _viewStart = Math.Clamp(_panStart - (e.X - _panX) * PerPixel, 0, Math.Max(0, EditTimeline.Length(Clips) - _viewLength));
            Invalidate();
            return;
        }
        if (_dragFrom < 0 || e.Button != MouseButtons.Left)
            return;
        if (!_dragging && Math.Abs(e.X - _dragStartX) < DragDistance)
            return;
        _dragging = Clips.Count > 1;
        _insertAt = NearestJoin(e.X);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _panning = false;
        if (_trimIndex >= 0)
        {
            int index = _trimIndex;
            _trimIndex = -1;
            Invalidate();
            if (Math.Abs(_trimX - _trimDownX) < 2)
            {
                // A click on an edge is still just a click.
                Position = XToPosition(_trimDownX);
                return;
            }
            var clip = Clips[index];
            long at = clip.Start + TrimOffset(index, _trimX);
            PartTrimmed?.Invoke(index, _trimStartEdge ? at : clip.Start, _trimStartEdge ? clip.End : at);
            return;
        }
        int from = _dragFrom;
        int to = _insertAt;
        bool moved = _dragging && to != from && to != from + 1;
        _dragFrom = -1;
        _dragging = false;
        _insertAt = -1;
        Invalidate();
        if (moved)
            PartDragged?.Invoke(from, to);
    }

    private int NearestJoin(int x)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        long start = 0;
        for (int i = 0; i <= Clips.Count; i++)
        {
            double distance = Math.Abs(PositionToX(start) - x);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
            if (i < Clips.Count)
                start += Clips[i].Length;
        }
        return best;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        long total = EditTimeline.Length(Clips);
        if (total == 0)
            return;
        if ((ModifierKeys & Keys.Shift) != 0)
        {
            _viewStart = Math.Clamp(_viewStart - Math.Sign(e.Delta) * _viewLength * 0.15, 0, Math.Max(0, total - _viewLength));
        }
        else
        {
            double focus = _viewStart + e.X * PerPixel;
            _viewLength = Math.Clamp(_viewLength * (e.Delta > 0 ? 0.7 : 1 / 0.7), 2000, Math.Max(2000, total));
            _viewStart = Math.Clamp(focus - e.X * PerPixel, 0, Math.Max(0, total - _viewLength));
        }
        Invalidate();
    }

    // For the automated tests, which drive the real mouse handling.
    internal void MouseDownForTest(int x) => OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x, 40, 0));
    internal void MouseMoveForTest(int x) => OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, x, 40, 0));
    internal void MouseUpForTest(int x) => OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x, 40, 0));
    internal int XForTest(long position) => (int)Math.Round(PositionToX(position));

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(p.Panel);
        if (Clips.Count == 0)
        {
            TextRenderer.DrawText(g, "Add a song from the list, or drop song files here.", Font, ClientRectangle, p.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }

        DrawRuler(g);
        long start = 0;
        for (int i = 0; i < Clips.Count; i++)
        {
            DrawClip(g, Clips[i], i, start);
            start += Clips[i].Length;
        }

        if (_trimIndex >= 0)
            DrawTrim(g);
        if (_insertAt >= 0)
        {
            using var insert = new Pen(p.Accent, 3);
            float x = (float)PositionToX(EditTimeline.StartOf(Clips, _insertAt));
            g.DrawLine(insert, x, RulerHeight, x, Height);
        }
        using (var cursor = new Pen(p.Accent, 2))
        {
            float x = (float)PositionToX(_position);
            g.DrawLine(cursor, x, 0, x, Height);
        }
        if (_playPosition >= 0)
        {
            using var play = new Pen(p.Text, 1);
            float x = (float)PositionToX(_playPosition);
            g.DrawLine(play, x, 0, x, Height);
        }
    }

    /// <summary>Greys out what the crop will remove and says how much that is.</summary>
    private void DrawTrim(Graphics g)
    {
        var p = Theme.Current;
        long start = EditTimeline.StartOf(Clips, _trimIndex);
        var clip = Clips[_trimIndex];
        long offset = TrimOffset(_trimIndex, _trimX);
        double at = PositionToX(start + offset);
        double from = _trimStartEdge ? PositionToX(start) : at;
        double to = _trimStartEdge ? at : PositionToX(start + clip.Length);
        using (var shade = new SolidBrush(Color.FromArgb(190, p.Panel)))
            g.FillRectangle(shade, (float)Math.Max(from, 0), RulerHeight + 2, (float)Math.Max(0, Math.Min(to, Width) - Math.Max(from, 0)), Height - RulerHeight - 6);
        using (var line = new Pen(p.Accent, 2))
            g.DrawLine(line, (float)at, RulerHeight, (float)at, Height);
        long removed = _trimStartEdge ? offset : clip.Length - offset;
        string label = $"Cuts {AudioIO.FormatTime(removed / (double)EditTimeline.Rate)}";
        var size = TextRenderer.MeasureText(label, Font);
        int textX = Math.Clamp((int)at + (_trimStartEdge ? -size.Width - 6 : 6), 0, Math.Max(0, Width - size.Width));
        using (var back = new SolidBrush(p.Accent))
            g.FillRectangle(back, textX, RulerHeight + 26, size.Width, size.Height);
        TextRenderer.DrawText(g, label, Font, new Point(textX, RulerHeight + 26), p.OnAccent, TextFormatFlags.NoPrefix);
    }

    private void DrawRuler(Graphics g)
    {
        var p = Theme.Current;
        double pixelsPerSecond = EditTimeline.Rate / PerPixel;
        int step = TickSeconds.FirstOrDefault(seconds => seconds * pixelsPerSecond >= 70, TickSeconds[^1]);
        int first = (int)(_viewStart / EditTimeline.Rate / step) * step;
        using var tick = new Pen(p.Border);
        for (int seconds = first; PositionToX(seconds * (double)EditTimeline.Rate) < Width; seconds += step)
        {
            int x = (int)PositionToX(seconds * (double)EditTimeline.Rate);
            g.DrawLine(tick, x, RulerHeight - 5, x, RulerHeight - 1);
            TextRenderer.DrawText(g, $"{seconds / 60}:{seconds % 60:00}", Font, new Point(x + 3, 1), p.Muted, TextFormatFlags.NoPrefix);
        }
    }

    private void DrawClip(Graphics g, EditClip clip, int index, long start)
    {
        var p = Theme.Current;
        double x1 = PositionToX(start);
        double x2 = PositionToX(start + clip.Length);
        if (x2 < 0 || x1 > Width)
            return;
        int left = (int)Math.Max(x1, -8);
        int right = (int)Math.Min(x2, Width + 8);
        var box = new Rectangle(left, RulerHeight + 2, Math.Max(1, right - left - 1), Height - RulerHeight - 6);
        bool selected = index == _selected;
        if (box.Width >= 12)
            Theme.FillBox(g, box, p.Raised, selected ? p.Accent : p.Border);
        else
            using (var thin = new SolidBrush(selected ? p.Accent : p.Raised))
                g.FillRectangle(thin, box);

        if (_audio(clip.Source) is { } source)
            DrawWave(g, source, clip, start, box);

        if (box.Width > 40)
        {
            var label = new Rectangle(Math.Max(box.X, 0) + 8, box.Y + 5, Math.Min(box.Right, Width) - Math.Max(box.X, 0) - 14, Font.Height + 2);
            TextRenderer.DrawText(g, clip.Name, Font, label, p.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private void DrawWave(Graphics g, EditSource source, EditClip clip, long start, Rectangle box)
    {
        using var pen = new Pen(Theme.Current.Wave);
        float middle = box.Y + box.Height / 2f + 8;
        float scale = (box.Height / 2f - 14) / 32768f;
        double perPixel = PerPixel;
        int from = Math.Max(box.X + 1, 0);
        int to = Math.Min(box.Right - 1, Width);
        for (int x = from; x < to; x++)
        {
            double a = _viewStart + x * perPixel - start + clip.Start;
            double b = a + perPixel;
            a = Math.Max(a, clip.Start);
            b = Math.Min(b, clip.End);
            if (b <= a)
                continue;
            int low = short.MaxValue, high = short.MinValue;
            if (perPixel >= EditSource.Step)
            {
                int first = (int)(a / EditSource.Step);
                int last = Math.Min(source.Low.Length - 1, (int)(b / EditSource.Step));
                for (int i = first; i <= last; i++)
                {
                    low = Math.Min(low, source.Low[i]);
                    high = Math.Max(high, source.High[i]);
                }
            }
            else
            {
                var data = source.Channels[0];
                int first = Math.Clamp((int)a, 0, data.Length - 1);
                int last = Math.Clamp((int)Math.Ceiling(b), first + 1, data.Length);
                for (int i = first; i < last; i++)
                {
                    low = Math.Min(low, data[i]);
                    high = Math.Max(high, data[i]);
                }
            }
            if (low <= high)
                g.DrawLine(pen, x, middle - high * scale, x, Math.Max(middle - low * scale, middle - high * scale + 1));
        }
    }
}

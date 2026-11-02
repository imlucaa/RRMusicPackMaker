namespace RRMusicPackMaker;

/// <summary>Set a song's loop by eye and ear: waveform, draggable markers, loop search and instant replay of the jump.</summary>
public sealed class LoopEditorForm : Form
{
    private const int Rate = 32000;

    private readonly TrackRow _row;
    private readonly WaveView _wave = new() { Dock = DockStyle.Fill };
    private readonly SeamView _seam = new() { Dock = DockStyle.Fill };
    private readonly FieldBox _startBox = new();
    private readonly FieldBox _endBox = new();
    private readonly SideList _candidates = new() { Dock = DockStyle.Fill, Tag = Theme.PanelTag };
    private readonly FlatCombo _crossfade = new() { Width = 150 };
    private readonly Label _status = new() { AutoSize = false, Height = 26, Dock = DockStyle.Bottom, Tag = Theme.MutedTag, Padding = new Padding(14, 0, 14, 6) };
    private readonly ModernButton _find = Make("Find loops");
    private readonly ModernButton _snap = Make("Snap end to best match");
    private readonly ModernButton _playJump = Make("▶  Play the jump", Theme.AccentTag);
    private readonly ModernButton _playCursor = Make("▶  Play from cursor");
    private readonly ModernButton _pause = Make("❚❚  Pause");
    private readonly ModernButton _stop = Make("■  Stop");

    private short[][] _channels = [];
    private short[] _mono = [];
    private bool _updating;
    private List<LoopFinder.Result> _found = [];

    public LoopEditorForm(TrackRow row)
    {
        _row = row;
        Text = $"Loop: {row.Name}";
        Width = 1040;
        Height = 700;
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterParent;
        Font = Theme.UiFont();
        BuildLayout();
        Theme.Apply(this);
        Shown += async (_, _) => await LoadAudioAsync();
        FormClosed += (_, _) => CleanUpPreview();
    }

    private static ModernButton Make(string text, string tag = "") =>
        new() { Text = text, AutoSize = true, Height = 28, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 0, 6, 0), Tag = tag };

    private void BuildLayout()
    {
        foreach (var (text, _) in PackBuilder.CrossfadeChoices)
            _crossfade.Items.Add(text);
        _crossfade.SelectedIndex = PackBuilder.CrossfadeIndex(_row.Crossfade);

        var help = new Label
        {
            Text = _row.Speed == 1 && !_row.Reverb
                ? "Drag the Start and End markers. Scroll to zoom, drag the waveform to move around."
                : "Drag the markers. Scroll to zoom. This plays the original, and Speed is added when the pack is made.",
            AutoSize = true,
            Tag = Theme.MutedTag,
            Dock = DockStyle.Top,
            Padding = new Padding(14, 12, 0, 8),
        };

        var waveHolder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 8) };
        waveHolder.Controls.Add(_wave);

        var seamHolder = new Panel { Dock = DockStyle.Bottom, Height = 110, Padding = new Padding(14, 0, 14, 8) };
        seamHolder.Controls.Add(_seam);

        var side = new TableLayoutPanel { Dock = DockStyle.Right, Width = 310, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 12, 14, 8) };
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var sideTop = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        _find.Width = 286;
        _find.AutoSize = false;
        _find.Height = 32;
        sideTop.Controls.Add(_find);
        sideTop.Controls.Add(new Label { Text = "Best matches first. Click one, then listen.", AutoSize = true, Tag = Theme.MutedTag, Margin = new Padding(0, 6, 0, 6) });
        side.Controls.Add(sideTop, 0, 0);
        side.Controls.Add(_candidates, 0, 1);
        var sideBottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        sideBottom.Controls.Add(new Label { Text = "Smooth the jump", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        sideBottom.Controls.Add(_crossfade);
        side.Controls.Add(sideBottom, 0, 2);

        var fields = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 6, Padding = new Padding(14, 0, 14, 6) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        for (int i = 0; i < 4; i++)
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        AddMarkerRow(fields, 0, "Start", _startBox, delta => MoveMarkers(_wave.LoopStart + delta, _wave.LoopEnd));
        AddMarkerRow(fields, 1, "End", _endBox, delta => MoveMarkers(_wave.LoopStart, _wave.LoopEnd + delta));

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(14, 2, 14, 8), WrapContents = false };
        actions.Controls.AddRange([_playJump, _playCursor, _pause, _stop, _snap]);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(14, 8, 14, 10), Tag = Theme.PanelTag };
        var ok = Make("Use this loop", Theme.AccentTag);
        var cancel = Make("Cancel");
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close();
        footer.Controls.AddRange([ok, cancel]);

        var center = new Panel { Dock = DockStyle.Fill };
        center.Controls.Add(waveHolder);
        center.Controls.Add(seamHolder);
        center.Controls.Add(fields);
        center.Controls.Add(actions);
        center.Controls.Add(_status);
        center.Controls.Add(help);

        Controls.Add(center);
        Controls.Add(side);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, Tag = Theme.LineTag });
        Controls.Add(footer);
        // Footer must dock below the separator.
        Controls.SetChildIndex(footer, Controls.Count - 1);

        _wave.MarkersChanged += Sync;
        _startBox.Box.Leave += (_, _) => FieldsChanged();
        _endBox.Box.Leave += (_, _) => FieldsChanged();
        _find.Click += async (_, _) => await FindLoopsAsync();
        _snap.Click += async (_, _) => await SnapAsync();
        _playJump.Click += (_, _) => PlayJump();
        _playCursor.Click += (_, _) => PlayFromCursor();
        _pause.Click += (_, _) => AudioPreview.TogglePause();
        _stop.Click += (_, _) => AudioPreview.StopFor(this);
        AudioPreview.Changed += ShowPlayback;
        _candidates.SelectedIndexChanged += (_, _) =>
        {
            if (_candidates.SelectedIndex >= 0 && _candidates.SelectedIndex < _found.Count)
            {
                var pick = _found[_candidates.SelectedIndex];
                MoveMarkers(pick.Start, pick.End);
            }
        };
        SetEnabled(false);
    }

    private void AddMarkerRow(TableLayoutPanel table, int row, string label, FieldBox box, Action<int> nudge)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, row);
        box.Width = 142;
        box.Margin = new Padding(0, 0, 8, 4);
        table.Controls.Add(box, 1, row);
        (string Text, int Delta)[] steps = [("-10 ms", -Rate / 100), ("-1 ms", -Rate / 1000), ("+1 ms", Rate / 1000), ("+10 ms", Rate / 100)];
        for (int i = 0; i < steps.Length; i++)
        {
            int delta = steps[i].Delta;
            var button = new ModernButton { Text = steps[i].Text, Width = 64, Height = 28, Margin = new Padding(0, 1, 4, 3) };
            button.Click += (_, _) => nudge(delta);
            table.Controls.Add(button, 2 + i, row);
        }
    }

    private void SetEnabled(bool enabled)
    {
        foreach (var button in new[] { _find, _snap, _playJump, _playCursor })
        {
            button.Enabled = enabled;
        }
        ShowPlayback();
    }

    private void ShowPlayback()
    {
        bool active = AudioPreview.IsActive(this);
        _pause.Enabled = active;
        _stop.Enabled = active;
        _pause.Text = AudioPreview.State == PreviewState.Paused ? "▶  Resume" : "❚❚  Pause";
    }

    internal async Task LoadAudioAsync()
    {
        _status.Text = "Loading song...";
        try
        {
            var info = await Task.Run(() => AudioIO.Probe(_row.Source));
            _channels = await Task.Run(() => AudioIO.Decode(_row.Source, Rate));
            if (IsDisposed)
                return;
            _mono = new short[_channels[0].Length];
            for (int i = 0; i < _mono.Length; i++)
                _mono[i] = (short)((_channels[0][i] + _channels[1][i]) / 2);
            _wave.SetAudio(_mono);

            double scale = info.SampleRate > 0 ? (double)Rate / info.SampleRate : 1;
            int start = 0;
            int end = _mono.Length;
            if (_row.Loop == LoopMode.Custom)
            {
                long? s = AudioIO.ParsePosition(_row.LoopStart, info.SampleRate);
                long? e = AudioIO.ParsePosition(_row.LoopEnd, info.SampleRate);
                if (s != null) start = (int)(s.Value * scale);
                if (e != null) end = (int)(e.Value * scale);
            }
            else if (info.LoopStart != null)
            {
                start = (int)(info.LoopStart.Value * scale);
                if (info.LoopEnd != null) end = (int)(info.LoopEnd.Value * scale);
            }
            MoveMarkers(start, end);
            SetEnabled(true);
            _status.Text = "";
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or FormatException or IOException)
        {
            if (!IsDisposed)
                _status.Text = $"Could not load the song. {ex.Message}";
        }
    }

    // For the automated tests and screenshots.
    internal Task FindLoopsForTest() => FindLoopsAsync();
    internal int MarkerStart => _wave.LoopStart;
    internal int MarkerEnd => _wave.LoopEnd;
    internal void SetMarkersForTest(int start, int end) => MoveMarkers(start, end);
    internal void AcceptForTest() => Accept();

    private void MoveMarkers(int start, int end)
    {
        _wave.SetMarkers(start, end);
        Sync();
    }

    private void Sync()
    {
        _updating = true;
        _startBox.Box.Text = AudioIO.FormatPrecise((double)_wave.LoopStart / Rate);
        _endBox.Box.Text = AudioIO.FormatPrecise((double)_wave.LoopEnd / Rate);
        _updating = false;
        _seam.Show(_mono, _wave.LoopStart, _wave.LoopEnd, Rate);
    }

    private void FieldsChanged()
    {
        if (_updating || _mono.Length == 0)
            return;
        try
        {
            long? start = AudioIO.ParsePosition(_startBox.Box.Text, Rate);
            long? end = AudioIO.ParsePosition(_endBox.Box.Text, Rate);
            MoveMarkers((int)(start ?? _wave.LoopStart), (int)(end ?? _wave.LoopEnd));
        }
        catch (FormatException ex)
        {
            _status.Text = ex.Message;
            Sync();
        }
    }

    private async Task FindLoopsAsync()
    {
        _find.Enabled = false;
        _status.Text = "Searching the song for repeats...";
        var channels = _channels;
        _found = await Task.Run(() => LoopFinder.FindCandidates(channels, Rate));
        if (IsDisposed)
            return;
        _candidates.Items.Clear();
        foreach (var result in _found)
            _candidates.Items.Add(new SideList.Item($"{AudioIO.FormatTime((double)result.Start / Rate)} to {AudioIO.FormatTime((double)result.End / Rate)}   ·   {result.Score * 100:0}% match", -1));
        _status.Text = _found.Count == 0
            ? "No repeats found. Pick your own points and turn on Smooth the jump."
            : $"{_found.Count} loops found. Listen before you trust the percentage.";
        _find.Enabled = true;
        if (_found.Count > 0)
            _candidates.SelectedIndex = 0;
    }

    private async Task SnapAsync()
    {
        int start = _wave.LoopStart, end = _wave.LoopEnd;
        var channels = _channels;
        int snapped = await Task.Run(() => LoopFinder.SnapEnd(channels, start, end, Rate));
        if (IsDisposed)
            return;
        MoveMarkers(start, snapped);
        _status.Text = "End moved to the closest match within 50 ms.";
    }

    private void PlayJump()
    {
        var song = new Song
        {
            Channels = _channels.Select(c => (short[])c.Clone()).ToArray(),
            Rate = Rate,
            Loop = true,
            LoopStart = _wave.LoopStart,
            LoopEnd = _wave.LoopEnd,
        };
        Processing.ApplyCrossfade(song, SelectedCrossfade);
        if (Play(AudioIO.WriteSeamPreview(song)))
            _status.Text = "Playing 4 seconds before the jump and 4 seconds after it.";
    }

    private void PlayFromCursor()
    {
        if (Play(AudioIO.WriteRangePreview(_channels, Rate, _wave.CursorPosition, 30)))
            _status.Text = "Playing 30 seconds from the cursor. Click the waveform to move the cursor.";
    }

    private bool Play(string file)
    {
        if (AudioPreview.Play(file, this))
            return true;
        _status.Text = "Could not play the sound. Check your audio device.";
        return false;
    }

    private void CleanUpPreview()
    {
        AudioPreview.Changed -= ShowPlayback;
        AudioPreview.StopFor(this);
    }

    private double SelectedCrossfade => PackBuilder.CrossfadeChoices[Math.Max(0, _crossfade.SelectedIndex)].Seconds;

    private void Accept()
    {
        FieldsChanged();
        if (_mono.Length > 0)
        {
            _row.Loop = LoopMode.Custom;
            _row.LoopStart = AudioIO.FormatPrecise((double)_wave.LoopStart / Rate);
            _row.LoopEnd = AudioIO.FormatPrecise((double)_wave.LoopEnd / Rate);
            _row.Crossfade = SelectedCrossfade;
            _row.Status = "";
            DialogResult = DialogResult.OK;
        }
        Close();
    }
}

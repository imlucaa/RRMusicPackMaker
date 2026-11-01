using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace RRMusicPackMaker;

public sealed record Palette(
    bool IsDark,
    Color Window,
    Color Panel,
    Color Raised,
    Color Border,
    Color Text,
    Color Muted,
    Color Accent,
    Color AccentHover,
    Color OnAccent,
    Color Selection,
    Color SelectionText,
    Color Good,
    Color Bad,
    Color Wave,
    Color LoopFill);

/// <summary>Colors for the whole app. Controls pick their look from their Tag: accent, muted, title, panel or line.</summary>
public static class Theme
{
    public const string AccentTag = "accent";
    public const string MutedTag = "muted";
    public const string TitleTag = "title";
    public const string PanelTag = "panel";
    public const string LineTag = "line";

    public static readonly Palette Light = new(
        false,
        Color.FromArgb(247, 247, 249),
        Color.White,
        Color.White,
        Color.FromArgb(222, 224, 230),
        Color.FromArgb(28, 30, 36),
        Color.FromArgb(108, 113, 125),
        Color.FromArgb(37, 99, 235),
        Color.FromArgb(29, 78, 216),
        Color.White,
        Color.FromArgb(226, 235, 254),
        Color.FromArgb(28, 30, 36),
        Color.FromArgb(21, 128, 61),
        Color.FromArgb(200, 40, 40),
        Color.FromArgb(120, 130, 150),
        Color.FromArgb(205, 221, 253));

    public static readonly Palette Dark = new(
        true,
        Color.FromArgb(24, 25, 29),
        Color.FromArgb(32, 33, 38),
        Color.FromArgb(43, 45, 51),
        Color.FromArgb(58, 60, 68),
        Color.FromArgb(235, 236, 240),
        Color.FromArgb(155, 159, 170),
        Color.FromArgb(79, 134, 247),
        Color.FromArgb(100, 150, 250),
        Color.White,
        Color.FromArgb(44, 62, 102),
        Color.FromArgb(240, 242, 248),
        Color.FromArgb(74, 200, 122),
        Color.FromArgb(240, 100, 100),
        Color.FromArgb(135, 142, 160),
        Color.FromArgb(40, 58, 98));

    public static Palette Current { get; private set; } = Light;

    public static event Action? Changed;

    public static void Set(bool dark)
    {
        Current = dark ? Dark : Light;
        Changed?.Invoke();
    }

    public static bool SystemPrefersDark()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? appName, string? idList);

    private static void ThemeNative(Control control, string darkName)
    {
        void Paint()
        {
            string? name = Current.IsDark ? darkName : "Explorer";
            SetWindowTheme(control.Handle, name, null);
            control.Invalidate();
        }

        if (control.IsHandleCreated)
            Paint();
        else
            control.HandleCreated += (_, _) => Paint();
    }

    private static void ThemeTitleBar(Form form)
    {
        void Paint()
        {
            int dark = Current.IsDark ? 1 : 0;
            DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
        }

        if (form.IsHandleCreated)
            Paint();
        else
            form.HandleCreated += (_, _) => Paint();
    }

    public static void Apply(Control root)
    {
        var p = Current;
        string tag = root.Tag as string ?? "";
        switch (root)
        {
            case Form form:
                form.BackColor = p.Window;
                form.ForeColor = p.Text;
                ThemeTitleBar(form);
                break;
            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                if (tag == AccentTag)
                {
                    button.BackColor = button.Enabled ? p.Accent : p.Raised;
                    button.ForeColor = button.Enabled ? p.OnAccent : p.Muted;
                    button.FlatAppearance.BorderSize = button.Enabled ? 0 : 1;
                    button.FlatAppearance.BorderColor = p.Border;
                    button.FlatAppearance.MouseOverBackColor = p.AccentHover;
                    button.FlatAppearance.MouseDownBackColor = p.Accent;
                }
                else
                {
                    button.BackColor = p.Raised;
                    button.ForeColor = button.Enabled ? p.Text : p.Muted;
                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.BorderColor = p.Border;
                    button.FlatAppearance.MouseOverBackColor = p.Selection;
                    button.FlatAppearance.MouseDownBackColor = p.Selection;
                }
                break;
            case Label label:
                label.BackColor = Color.Transparent;
                label.ForeColor = tag == MutedTag ? p.Muted : p.Text;
                break;
            case TextBox text:
                text.BackColor = text.Multiline ? p.Panel : p.Raised;
                text.ForeColor = p.Text;
                if (text.Multiline)
                    ThemeNative(text, "DarkMode_Explorer");
                break;
            case ComboBox combo:
                combo.BackColor = p.Raised;
                combo.ForeColor = p.Text;
                combo.Invalidate();
                break;
            case FieldBox field:
                field.BackColor = p.Raised;
                field.ForeColor = p.Text;
                field.Invalidate();
                break;
            case CheckBox check:
                check.FlatStyle = FlatStyle.Flat;
                check.ForeColor = p.Text;
                check.BackColor = Color.Transparent;
                check.FlatAppearance.BorderColor = p.Muted;
                break;
            case DataGridView grid:
                ApplyGrid(grid);
                break;
            case ListBox list:
                list.BackColor = tag == PanelTag ? p.Panel : p.Raised;
                list.ForeColor = p.Text;
                ThemeNative(list, "DarkMode_Explorer");
                list.Invalidate();
                break;
            default:
                root.BackColor = tag switch
                {
                    PanelTag => p.Panel,
                    LineTag => p.Border,
                    _ => root.Parent is { } parent ? parent.BackColor : p.Window,
                };
                root.ForeColor = p.Text;
                root.Invalidate();
                break;
        }

        foreach (Control child in root.Controls)
            Apply(child);
    }

    private static void ApplyGrid(DataGridView grid)
    {
        var p = Current;
        grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.BackgroundColor = p.Panel;
        grid.GridColor = p.Border;
        var header = grid.ColumnHeadersDefaultCellStyle;
        header.BackColor = p.Panel;
        header.ForeColor = p.Muted;
        header.SelectionBackColor = p.Panel;
        header.SelectionForeColor = p.Muted;
        header.Padding = new Padding(8, 0, 0, 0);
        var cell = grid.DefaultCellStyle;
        cell.BackColor = p.Panel;
        cell.ForeColor = p.Text;
        cell.SelectionBackColor = p.Selection;
        cell.SelectionForeColor = p.SelectionText;
        cell.Padding = new Padding(8, 0, 4, 0);
        foreach (Control child in grid.Controls)
            if (child is ScrollBar)
                ThemeNative(child, "DarkMode_Explorer");
        grid.Invalidate();
    }
}

/// <summary>A text box with a clean one pixel border and room around the text.</summary>
public sealed class FieldBox : Panel
{
    public TextBox Box { get; } = new() { BorderStyle = BorderStyle.None, Dock = DockStyle.Fill };

    public FieldBox(string placeholder = "")
    {
        Box.PlaceholderText = placeholder;
        Padding = new Padding(8, 7, 8, 5);
        Height = 32;
        DoubleBuffered = true;
        Controls.Add(Box);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Box.Focused ? Theme.Current.Accent : Theme.Current.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

/// <summary>A drop-down list drawn in the app's colors, because Windows ignores them for the border and arrow.</summary>
public sealed class FlatCombo : ComboBox
{
    private const int PaintMessage = 0x000F;

    public FlatCombo()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 22;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var p = Theme.Current;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? p.Selection : p.Raised))
            e.Graphics.FillRectangle(back, e.Bounds);
        if (e.Index >= 0)
        {
            var bounds = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, bounds, selected ? p.SelectionText : p.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != PaintMessage || !IsHandleCreated)
            return;

        var p = Theme.Current;
        using var g = Graphics.FromHwnd(Handle);
        var bounds = new Rectangle(0, 0, Width, Height);
        using (var back = new SolidBrush(p.Raised))
            g.FillRectangle(back, bounds);
        var textBounds = new Rectangle(8, 0, Width - 30, Height);
        TextRenderer.DrawText(g, Text, Font, textBounds, Enabled ? p.Text : p.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        int x = Width - 16, y = Height / 2 - 1;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var arrow = new Pen(p.Muted, 1.5f))
            g.DrawLines(arrow, new[] { new PointF(x - 4, y - 2), new PointF(x, y + 2), new PointF(x + 4, y - 2) });
        g.SmoothingMode = SmoothingMode.None;
        using var border = new Pen(Focused ? p.Accent : p.Border);
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }
}

/// <summary>A track list without the dotted focus box Windows draws around it.</summary>
public sealed class CleanGrid : DataGridView
{
    public CleanGrid() => DoubleBuffered = true;

    protected override bool ShowFocusCues => false;
}

/// <summary>A thin progress line for the bottom of the window.</summary>
public sealed class ThinProgress : Control
{
    private double _fraction;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Fraction
    {
        get => _fraction;
        set
        {
            _fraction = Math.Clamp(value, 0, 1);
            Invalidate();
        }
    }

    public ThinProgress()
    {
        Height = 3;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Current.Border);
        using var brush = new SolidBrush(Theme.Current.Accent);
        e.Graphics.FillRectangle(brush, 0, 0, (float)(Width * _fraction), Height);
    }
}

/// <summary>Small picture of a song: its waveform with the looping part highlighted.</summary>
public sealed class LoopThumb : Control
{
    private float[] _peaks = [];
    private double _start;
    private double _end = 1;
    private bool _hasLoop;

    public LoopThumb()
    {
        Height = 56;
        DoubleBuffered = true;
    }

    public void Show(float[] peaks, double startFraction, double endFraction, bool hasLoop)
    {
        _peaks = peaks;
        _start = startFraction;
        _end = endFraction;
        _hasLoop = hasLoop;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(p.Raised);
        if (_hasLoop)
        {
            using var fill = new SolidBrush(p.LoopFill);
            g.FillRectangle(fill, (float)(_start * Width), 0, (float)((_end - _start) * Width), Height);
        }
        if (_peaks.Length > 0)
        {
            using var wave = new Pen(p.Wave);
            float middle = Height / 2f;
            for (int x = 0; x < Width; x++)
            {
                float peak = _peaks[Math.Min(_peaks.Length - 1, x * _peaks.Length / Math.Max(1, Width))];
                float half = Math.Max(0.5f, peak * (Height / 2f - 3));
                g.DrawLine(wave, x, middle - half, x, middle + half);
            }
        }
        if (_hasLoop)
        {
            using var mark = new Pen(p.Accent, 2);
            g.DrawLine(mark, (float)(_start * Width), 0, (float)(_start * Width), Height);
            g.DrawLine(mark, (float)(_end * Width) - 1, 0, (float)(_end * Width) - 1, Height);
        }
        using var border = new Pen(p.Border);
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}

/// <summary>Sidebar list drawn with roomy rows and a count on the right.</summary>
public sealed class SideList : ListBox
{
    public sealed record Item(string Name, int Count)
    {
        public override string ToString() => Name;
    }

    public SideList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        ItemHeight = 34;
        IntegralHeight = false;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not Item item)
            return;
        var p = Theme.Current;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var g = e.Graphics;
        using (var back = new SolidBrush(selected ? p.Selection : p.Panel))
            g.FillRectangle(back, e.Bounds);
        if (selected)
        {
            using var bar = new SolidBrush(p.Accent);
            g.FillRectangle(bar, e.Bounds.X, e.Bounds.Y + 6, 3, e.Bounds.Height - 12);
        }
        int reserved = item.Count < 0 ? 20 : 58;
        var nameBounds = new Rectangle(e.Bounds.X + 14, e.Bounds.Y, e.Bounds.Width - reserved, e.Bounds.Height);
        TextRenderer.DrawText(g, item.Name, Font, nameBounds, selected ? p.SelectionText : p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (item.Count < 0)
            return;
        var countBounds = new Rectangle(e.Bounds.Right - 50, e.Bounds.Y, 40, e.Bounds.Height);
        TextRenderer.DrawText(g, item.Count.ToString(), Font, countBounds, p.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }
}

/// <summary>The big waveform in the loop editor, with draggable start and end markers.</summary>
public sealed class WaveView : Control
{
    private const int MipStep = 256;
    private short[] _mono = [];
    private short[] _mipMin = [];
    private short[] _mipMax = [];
    private double _viewStart;
    private double _viewLength = 1;
    private int _start;
    private int _end;
    private int _cursor;
    private enum Drag { None, Start, End, Pan }
    private Drag _drag;
    private int _dragX;
    private double _dragViewStart;

    public event Action? MarkersChanged;

    public WaveView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LoopStart => _start;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LoopEnd => _end;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CursorPosition => _cursor;

    public void SetAudio(short[] mono)
    {
        _mono = mono;
        int count = mono.Length / MipStep + 1;
        _mipMin = new short[count];
        _mipMax = new short[count];
        for (int i = 0; i < count; i++)
        {
            short low = short.MaxValue, high = short.MinValue;
            int to = Math.Min(mono.Length, (i + 1) * MipStep);
            for (int j = i * MipStep; j < to; j++)
            {
                if (mono[j] < low) low = mono[j];
                if (mono[j] > high) high = mono[j];
            }
            _mipMin[i] = low;
            _mipMax[i] = high;
        }
        _viewStart = 0;
        _viewLength = Math.Max(1, mono.Length);
        Invalidate();
    }

    public void SetMarkers(int start, int end)
    {
        _end = Math.Clamp(end, 1, Math.Max(1, _mono.Length));
        _start = Math.Clamp(start, 0, _end - 1);
        Invalidate();
    }

    private double XToSample(int x) => _viewStart + x * _viewLength / Math.Max(1, Width);
    private float SampleToX(double sample) => (float)((sample - _viewStart) * Width / _viewLength);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (_mono.Length == 0)
            return;
        if (Math.Abs(e.X - SampleToX(_start)) <= 7)
            _drag = Drag.Start;
        else if (Math.Abs(e.X - SampleToX(_end)) <= 7)
            _drag = Drag.End;
        else
        {
            _drag = Drag.Pan;
            _dragX = e.X;
            _dragViewStart = _viewStart;
            _cursor = Math.Clamp((int)XToSample(e.X), 0, _mono.Length - 1);
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_mono.Length == 0)
            return;
        if (_drag == Drag.None)
        {
            Cursor = Math.Abs(e.X - SampleToX(_start)) <= 7 || Math.Abs(e.X - SampleToX(_end)) <= 7 ? Cursors.SizeWE : Cursors.Default;
            return;
        }
        int position = Math.Clamp((int)XToSample(e.X), 0, _mono.Length);
        if (_drag == Drag.Start)
        {
            _start = Math.Min(position, _end - 1);
            MarkersChanged?.Invoke();
        }
        else if (_drag == Drag.End)
        {
            _end = Math.Max(position, _start + 1);
            MarkersChanged?.Invoke();
        }
        else
        {
            double shift = (e.X - _dragX) * _viewLength / Math.Max(1, Width);
            _viewStart = Math.Clamp(_dragViewStart - shift, 0, Math.Max(0, _mono.Length - _viewLength));
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e) => _drag = Drag.None;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_mono.Length == 0)
            return;
        double focus = XToSample(e.X);
        double factor = e.Delta > 0 ? 0.7 : 1 / 0.7;
        _viewLength = Math.Clamp(_viewLength * factor, 200, _mono.Length);
        _viewStart = Math.Clamp(focus - e.X * _viewLength / Math.Max(1, Width), 0, Math.Max(0, _mono.Length - _viewLength));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(p.Panel);
        if (_mono.Length == 0)
            return;

        using (var fill = new SolidBrush(p.LoopFill))
        {
            float x1 = Math.Max(0, SampleToX(_start));
            float x2 = Math.Min(Width, SampleToX(_end));
            if (x2 > x1)
                g.FillRectangle(fill, x1, 0, x2 - x1, Height);
        }

        using var wave = new Pen(p.Wave);
        float middle = Height / 2f;
        float scale = (Height / 2f - 16) / 32768f;
        double perPixel = _viewLength / Math.Max(1, Width);
        for (int x = 0; x < Width; x++)
        {
            double from = _viewStart + x * perPixel;
            double to = from + perPixel;
            int low = short.MaxValue, high = short.MinValue;
            if (perPixel >= MipStep)
            {
                int a = (int)(from / MipStep), b = Math.Min(_mipMin.Length - 1, (int)(to / MipStep));
                for (int i = a; i <= b; i++)
                {
                    low = Math.Min(low, _mipMin[i]);
                    high = Math.Max(high, _mipMax[i]);
                }
            }
            else
            {
                int a = Math.Clamp((int)from, 0, _mono.Length - 1), b = Math.Clamp((int)Math.Ceiling(to), a + 1, _mono.Length);
                for (int i = a; i < b; i++)
                {
                    low = Math.Min(low, _mono[i]);
                    high = Math.Max(high, _mono[i]);
                }
            }
            if (low <= high)
                g.DrawLine(wave, x, middle - high * scale, x, Math.Max(middle - low * scale, middle - high * scale + 1));
        }

        using (var cursor = new Pen(p.Muted) { DashStyle = DashStyle.Dot })
            g.DrawLine(cursor, SampleToX(_cursor), 0, SampleToX(_cursor), Height);
        DrawMarker(g, _start, "Start");
        DrawMarker(g, _end, "End");
    }

    private void DrawMarker(Graphics g, int sample, string label)
    {
        var p = Theme.Current;
        float x = SampleToX(sample);
        if (x < -40 || x > Width + 40)
            return;
        using var pen = new Pen(p.Accent, 2);
        g.DrawLine(pen, x, 0, x, Height);
        var size = TextRenderer.MeasureText(label, Font);
        int textX = label == "End" ? (int)x - size.Width - 4 : (int)x + 2;
        using var back = new SolidBrush(p.Accent);
        g.FillRectangle(back, textX, 2, size.Width + 2, size.Height);
        TextRenderer.DrawText(g, label, Font, new Point(textX + 1, 2), p.OnAccent);
    }
}

/// <summary>Shows the exact join: the last moments of the loop, then the first moments after the jump.</summary>
public sealed class SeamView : Control
{
    private short[] _mono = [];
    private int _start;
    private int _end;
    private int _window = 1;

    public SeamView() => DoubleBuffered = true;

    public void Show(short[] mono, int start, int end, int rate)
    {
        _mono = mono;
        _start = start;
        _end = end;
        _window = rate / 20;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(p.Panel);
        if (_mono.Length == 0)
            return;

        float middle = Height / 2f;
        int half = Width / 2;
        int peak = 1;
        for (int i = 0; i < _window; i++)
        {
            peak = Math.Max(peak, Math.Abs((int)At(_end - i - 1)));
            peak = Math.Max(peak, Math.Abs((int)At(_start + i)));
        }
        float scale = (Height / 2f - 6) / peak;

        using var wave = new Pen(p.Accent);
        var points = new PointF[Width];
        for (int x = 0; x < Width; x++)
        {
            double offset = (x - half) * (double)_window / Math.Max(1, half);
            int sample = offset < 0 ? _end + (int)Math.Floor(offset) : _start + (int)offset;
            points[x] = new PointF(x, middle - At(sample) * scale);
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (points.Length > 1)
            g.DrawLines(wave, points);
        g.SmoothingMode = SmoothingMode.None;

        using var line = new Pen(p.Muted) { DashStyle = DashStyle.Dash };
        g.DrawLine(line, half, 0, half, Height);
        TextRenderer.DrawText(g, "end of loop", Font, new Point(6, 4), p.Muted);
        var text = "after the jump";
        TextRenderer.DrawText(g, text, Font, new Point(Width - TextRenderer.MeasureText(text, Font).Width - 6, 4), p.Muted);
    }

    private short At(int index) => index >= 0 && index < _mono.Length ? _mono[index] : (short)0;
}

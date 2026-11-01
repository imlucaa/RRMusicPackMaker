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
        Color.White,
        Color.FromArgb(249, 249, 249),
        Color.White,
        Color.FromArgb(229, 229, 229),
        Color.Black,
        Color.FromArgb(112, 112, 112),
        Color.Black,
        Color.FromArgb(48, 48, 48),
        Color.White,
        Color.FromArgb(235, 235, 235),
        Color.Black,
        Color.Black,
        Color.FromArgb(200, 30, 30),
        Color.FromArgb(120, 120, 120),
        Color.FromArgb(226, 226, 226));

    public static readonly Palette Dark = new(
        true,
        Color.Black,
        Color.FromArgb(12, 12, 12),
        Color.FromArgb(24, 24, 24),
        Color.FromArgb(36, 36, 36),
        Color.White,
        Color.FromArgb(140, 140, 140),
        Color.White,
        Color.FromArgb(214, 214, 214),
        Color.Black,
        Color.FromArgb(34, 34, 34),
        Color.White,
        Color.White,
        Color.FromArgb(255, 95, 95),
        Color.FromArgb(150, 150, 150),
        Color.FromArgb(38, 38, 38));

    public static Palette Current { get; private set; } = Light;

    public static event Action? Changed;

    private const int CornerRadius = 7;

    public static GraphicsPath Rounded(Rectangle area, int radius = CornerRadius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(area.X, area.Y, d, d, 180, 90);
        path.AddArc(area.Right - d, area.Y, d, d, 270, 90);
        path.AddArc(area.Right - d, area.Bottom - d, d, d, 0, 90);
        path.AddArc(area.X, area.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Paints the rounded box that buttons, fields and lists all share.</summary>
    public static void FillBox(Graphics g, Rectangle area, Color fill, Color? border = null, int radius = CornerRadius)
    {
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(new Rectangle(area.X, area.Y, area.Width - 1, area.Height - 1), radius);
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);
        if (border != null)
        {
            using var pen = new Pen(border.Value);
            g.DrawPath(pen, path);
        }
        g.SmoothingMode = smoothing;
    }

    private static readonly Lazy<string> TextFontName = new(() =>
    {
        using var font = new Font("Segoe UI Variable Text", 9.5f);
        return font.Name == "Segoe UI Variable Text" ? font.Name : "Segoe UI";
    });

    /// <summary>The Windows 11 font where it exists, otherwise Segoe UI.</summary>
    public static Font UiFont(float size = 9.5f) => new(TextFontName.Value, size);
    public static Font TitleFont(float size = 13f) => new("Segoe UI Semibold", size);

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

            // Windows 11 can match the title bar and border exactly. Windows 10 ignores these.
            int caption = ColorTranslator.ToWin32(Current.Window);
            int border = ColorTranslator.ToWin32(Current.Border);
            int title = ColorTranslator.ToWin32(Current.Text);
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 36, ref title, sizeof(int));
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
                button.Invalidate();
                break;
            case Panel { AutoScroll: true } scroller:
                scroller.BackColor = tag == PanelTag ? p.Panel : p.Window;
                ThemeNative(scroller, "DarkMode_Explorer");
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
                field.Box.BackColor = p.Raised;
                field.Box.ForeColor = p.Text;
                field.Invalidate();
                return;
            case CheckBox check:
                check.ForeColor = p.Text;
                check.Invalidate();
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
        grid.CellBorderStyle = DataGridViewCellBorderStyle.None;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.BackgroundColor = p.Panel;
        grid.GridColor = p.Panel;
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
        Padding = new Padding(10, 8, 10, 5);
        Height = 34;
        DoubleBuffered = true;
        Controls.Add(Box);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        e.Graphics.Clear(Parent?.BackColor ?? p.Window);
        Theme.FillBox(e.Graphics, ClientRectangle, p.Raised, Box.Focused ? p.Accent : p.Border);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // OnPaint covers the whole control, and painting twice would flicker.
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
        using (var back = new SolidBrush(Parent?.BackColor ?? p.Window))
            g.FillRectangle(back, bounds);
        Theme.FillBox(g, bounds, p.Raised, Focused ? p.Accent : p.Border);
        var textBounds = new Rectangle(10, 0, Width - 34, Height);
        TextRenderer.DrawText(g, Text, Font, textBounds, Enabled ? p.Text : p.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        int x = Width - 16, y = Height / 2 - 1;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var arrow = new Pen(p.Muted, 1.5f))
            g.DrawLines(arrow, new[] { new PointF(x - 4, y - 2), new PointF(x, y + 2), new PointF(x + 4, y - 2) });
        g.SmoothingMode = SmoothingMode.None;
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

/// <summary>A rounded surface that groups related controls, the main building block of the window.</summary>
public sealed class CardPanel : Panel
{
    private const int CardRadius = 12;

    public CardPanel()
    {
        DoubleBuffered = true;
        Tag = Theme.PanelTag;
        Padding = new Padding(8);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // OnPaint covers the whole control, and painting twice would flicker.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        e.Graphics.Clear(Parent?.BackColor ?? p.Window);
        Theme.FillBox(e.Graphics, ClientRectangle, p.Panel, p.Border, CardRadius);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }
}

/// <summary>A rounded button that paints itself, so hover, pressed and disabled all stay readable in both themes.</summary>
public class ModernButton : Button
{
    private bool _hover;
    private bool _pressed;

    /// <summary>Makes the ends fully round, for filter chips.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsPill { get; set; }

    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = 34;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font);
        return new Size(text.Width + 30, Math.Max(Height, text.Height + 12));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? p.Window);
        bool accent = Tag as string == Theme.AccentTag;
        Color fill;
        Color text;
        Color? border = null;
        if (!Enabled)
        {
            fill = p.Raised;
            text = p.Muted;
            border = p.Border;
        }
        else if (accent)
        {
            fill = _hover && !_pressed ? p.AccentHover : p.Accent;
            text = p.OnAccent;
        }
        else
        {
            fill = _hover ? p.Selection : p.Raised;
            text = p.Text;
            border = p.Border;
        }
        if (IsPill)
            Theme.FillBox(g, ClientRectangle, fill, border, Math.Max(1, Height / 2 - 1));
        else
            Theme.FillBox(g, ClientRectangle, fill, border);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A check box drawn in the app's colors. The Windows one hides its tick in dark mode.</summary>
public sealed class FlatCheck : CheckBox
{
    private const int BoxSize = 16;

    public FlatCheck() => DoubleBuffered = true;

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.Current;
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? p.Window);
        int top = (Height - BoxSize) / 2;
        var box = new Rectangle(0, top, BoxSize, BoxSize);
        using (var fill = new SolidBrush(Checked ? p.Accent : p.Raised))
            g.FillRectangle(fill, box);
        if (Checked)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var tick = new Pen(p.OnAccent, 2f);
            g.DrawLines(tick, new[] { new PointF(3.5f, top + 8.5f), new PointF(6.8f, top + 11.8f), new PointF(12.5f, top + 4.5f) });
            g.SmoothingMode = SmoothingMode.None;
        }
        else
        {
            using var border = new Pen(p.Muted);
            g.DrawRectangle(border, 0, top, BoxSize - 1, BoxSize - 1);
        }
        var text = new Rectangle(BoxSize + 6, 0, Width - BoxSize - 6, Height);
        TextRenderer.DrawText(g, Text, Font, text, Enabled ? p.Text : p.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font);
        return new Size(BoxSize + 6 + text.Width + 2, Math.Max(BoxSize, text.Height) + 4);
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
        Height = 2;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Current.Window);
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
        g.Clear(Parent?.BackColor ?? p.Window);
        using var shape = Theme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1));
        using (var back = new SolidBrush(p.Raised))
            g.FillPath(back, shape);
        g.SetClip(shape);
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
        g.ResetClip();
    }
}

/// <summary>Sidebar list drawn with roomy rows and a count on the right.</summary>
public sealed class SideList : ListBox
{
    /// <summary>Marks a row as a section title, which can't be selected.</summary>
    public const int HeaderCount = -2;

    public sealed record Item(string Name, int Count)
    {
        public bool IsHeader => Count == HeaderCount;
        public override string ToString() => Name;
    }

    private readonly Font _headerFont = Theme.UiFont(8f);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _headerFont.Dispose();
        base.Dispose(disposing);
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
        using (var back = new SolidBrush(p.Panel))
            g.FillRectangle(back, e.Bounds);
        if (item.IsHeader)
        {
            TextRenderer.DrawText(g, item.Name.ToUpperInvariant(), _headerFont, new Rectangle(e.Bounds.X + 18, e.Bounds.Y + 12, e.Bounds.Width - 20, e.Bounds.Height - 12), p.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }
        if (selected)
            Theme.FillBox(g, new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 2, e.Bounds.Width - 12, e.Bounds.Height - 4), p.Selection);
        int reserved = item.Count < 0 ? 20 : 58;
        var nameBounds = new Rectangle(e.Bounds.X + 18, e.Bounds.Y, e.Bounds.Width - reserved, e.Bounds.Height);
        TextRenderer.DrawText(g, item.Name, Font, nameBounds, selected ? p.SelectionText : p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (item.Count < 0)
            return;
        var countBounds = new Rectangle(e.Bounds.Right - 56, e.Bounds.Y, 40, e.Bounds.Height);
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

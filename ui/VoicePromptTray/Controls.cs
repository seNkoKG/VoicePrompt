using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace VoicePromptTray;

internal sealed class BrandMark : Control
{
    public BrandMark()
    {
        BackColor = Theme.Sidebar;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "VoicePrompt";
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        if (bounds.Width < 4 || bounds.Height < 4)
            return;

        using var tile = Theme.RoundedRect(bounds, Math.Max(7, bounds.Width / 4));
        using var fill = new LinearGradientBrush(bounds, Theme.SurfaceRaised, Theme.Canvas, 55f);
        using var border = new Pen(Theme.BorderStrong, 1.1f);
        e.Graphics.FillPath(fill, tile);
        e.Graphics.DrawPath(border, tile);

        PointF P(float x, float y) => new(bounds.Left + bounds.Width * x, bounds.Top + bounds.Height * y);
        PointF[] waveform =
        {
            P(.18f, .52f), P(.28f, .52f), P(.32f, .39f), P(.38f, .67f),
            P(.44f, .22f), P(.51f, .78f), P(.58f, .40f), P(.64f, .66f),
            P(.69f, .52f), P(.82f, .52f),
        };
        using var signal = new Pen(Theme.Accent, Math.Max(2f, bounds.Width * .065f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        e.Graphics.DrawLines(signal, waveform);
    }
}

internal sealed class ThemePicker : Control
{
    private int _hovered = -1;
    private string _selectedValue = Theme.Current.Id;

    public event EventHandler? SelectedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SelectedValue => _selectedValue;

    public ThemePicker()
    {
        Height = 76;
        BackColor = Theme.Surface;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.List;
        AccessibleName = "Interface theme";
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.UserPaint,
            true);
    }

    public void SelectValue(string? value)
    {
        ThemePalette palette = Theme.Find(value);
        if (_selectedValue == palette.Id)
            return;
        _selectedValue = palette.Id;
        Invalidate();
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        IReadOnlyList<ThemePalette> options = Theme.Available;
        using var labelFont = Theme.Font(9f, FontStyle.Bold);
        int gap = 8;
        int width = Math.Max(1, (ClientSize.Width - gap * (options.Count - 1)) / options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            ThemePalette palette = options[i];
            var bounds = new Rectangle(i * (width + gap), 1, i == options.Count - 1 ? ClientSize.Width - i * (width + gap) : width, Height - 2);
            bool selected = palette.Id == _selectedValue;
            using var path = Theme.RoundedRect(Rectangle.Inflate(bounds, -1, -1), 11);
            using var fill = new SolidBrush(palette.SurfaceRaised);
            using var border = new Pen(selected ? palette.Accent : i == _hovered ? palette.BorderStrong : palette.Border, selected ? 1.8f : 1f);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);

            using var accent = new SolidBrush(palette.Accent);
            using var surface = new SolidBrush(palette.Control);
            e.Graphics.FillEllipse(accent, bounds.Left + 14, bounds.Top + 15, 12, 12);
            e.Graphics.FillEllipse(surface, bounds.Left + 22, bounds.Top + 15, 12, 12);
            TextRenderer.DrawText(
                e.Graphics,
                palette.Name,
                labelFont,
                new Rectangle(bounds.Left + 14, bounds.Top + 37, bounds.Width - 28, 22),
                palette.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int next = IndexAt(e.Location);
        if (next != _hovered)
        {
            _hovered = next;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        int index = IndexAt(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0)
            SelectValue(Theme.Available[index].Id);
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int current = Theme.Available.ToList().FindIndex(option => option.Id == _selectedValue);
        int next = e.KeyCode switch
        {
            Keys.Left or Keys.Up => Math.Max(0, current - 1),
            Keys.Right or Keys.Down => Math.Min(Theme.Available.Count - 1, current + 1),
            _ => current,
        };
        if (next != current)
        {
            SelectValue(Theme.Available[next].Id);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private int IndexAt(Point location)
    {
        int count = Theme.Available.Count;
        int gap = 8;
        int width = Math.Max(1, (ClientSize.Width - gap * (count - 1)) / count);
        for (int i = 0; i < count; i++)
        {
            int right = i == count - 1 ? ClientSize.Width : i * (width + gap) + width;
            if (location.X >= i * (width + gap) && location.X < right)
                return i;
        }
        return -1;
    }
}

internal sealed class OverlayStylePicker : Control
{
    private static readonly (string Id, string Name)[] Options =
    [
        (RecordingOverlay.WaveStyle, "Wave"),
        (RecordingOverlay.BarsStyle, "Bars"),
        (RecordingOverlay.OrbStyle, "Orb"),
    ];

    private int _hovered = -1;
    private string _selectedValue = RecordingOverlay.WaveStyle;

    public event EventHandler? SelectedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SelectedValue => _selectedValue;

    public OverlayStylePicker()
    {
        Height = 84;
        BackColor = Theme.Surface;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.List;
        AccessibleName = "Recording overlay style";
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.UserPaint,
            true);
    }

    public void SelectValue(string? value)
    {
        string normalized = RecordingOverlay.NormalizeStyle(value);
        if (_selectedValue == normalized)
            return;
        _selectedValue = normalized;
        Invalidate();
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var labelFont = Theme.Font(8.7f, FontStyle.Bold);
        int gap = 8;
        int width = Math.Max(1, (ClientSize.Width - gap * (Options.Length - 1)) / Options.Length);
        for (int i = 0; i < Options.Length; i++)
        {
            (string id, string name) = Options[i];
            var bounds = new Rectangle(
                i * (width + gap),
                1,
                i == Options.Length - 1 ? ClientSize.Width - i * (width + gap) : width,
                Height - 2);
            bool selected = id == _selectedValue;
            using var path = Theme.RoundedRect(Rectangle.Inflate(bounds, -1, -1), 11);
            using var fill = new SolidBrush(Theme.SurfaceRaised);
            using var border = new Pen(selected ? Theme.Accent : i == _hovered ? Theme.BorderStrong : Theme.Border, selected ? 1.8f : 1f);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);

            DrawPreview(e.Graphics, id, new Rectangle(bounds.Left + 12, bounds.Top + 11, bounds.Width - 24, 29));
            TextRenderer.DrawText(
                e.Graphics,
                name,
                labelFont,
                new Rectangle(bounds.Left + 12, bounds.Top + 49, bounds.Width - 24, 20),
                Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2));
    }

    private static void DrawPreview(Graphics g, string id, Rectangle bounds)
    {
        using var signal = new Pen(Theme.TextSecondary, 1.55f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        float centerY = bounds.Top + bounds.Height / 2f;
        if (id == RecordingOverlay.OrbStyle)
        {
            var circle = new RectangleF(bounds.Left + 2, bounds.Top + 1, 27, 27);
            g.DrawEllipse(signal, circle);
            g.DrawLine(signal, circle.Left + 13.5f, circle.Top + 8, circle.Left + 13.5f, circle.Top + 17);
            return;
        }

        g.DrawEllipse(signal, bounds.Left + 2, centerY - 6, 7, 12);
        float startX = bounds.Left + 18;
        float endX = bounds.Right - 3;
        if (id == RecordingOverlay.BarsStyle)
        {
            float[] heights = [3, 7, 11, 6, 9, 4, 8, 5, 3];
            float step = Math.Max(4f, (endX - startX) / (heights.Length - 1));
            for (int i = 0; i < heights.Length && startX + i * step <= endX; i++)
                g.DrawLine(signal, startX + i * step, centerY - heights[i] / 2, startX + i * step, centerY + heights[i] / 2);
            return;
        }

        PointF[] points =
        [
            new(startX, centerY),
            new(startX + (endX - startX) * .14f, centerY - 3),
            new(startX + (endX - startX) * .28f, centerY + 6),
            new(startX + (endX - startX) * .43f, centerY - 8),
            new(startX + (endX - startX) * .58f, centerY + 7),
            new(startX + (endX - startX) * .73f, centerY - 4),
            new(endX, centerY),
        ];
        g.DrawLines(signal, points);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int next = IndexAt(e.Location);
        if (next != _hovered)
        {
            _hovered = next;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        int index = IndexAt(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0)
            SelectValue(Options[index].Id);
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int current = Array.FindIndex(Options, option => option.Id == _selectedValue);
        int next = e.KeyCode switch
        {
            Keys.Left or Keys.Up => Math.Max(0, current - 1),
            Keys.Right or Keys.Down => Math.Min(Options.Length - 1, current + 1),
            _ => current,
        };
        if (next != current)
        {
            SelectValue(Options[next].Id);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private int IndexAt(Point location)
    {
        int gap = 8;
        int width = Math.Max(1, (ClientSize.Width - gap * (Options.Length - 1)) / Options.Length);
        for (int i = 0; i < Options.Length; i++)
        {
            int right = i == Options.Length - 1 ? ClientSize.Width : i * (width + gap) + width;
            if (location.X >= i * (width + gap) && location.X < right)
                return i;
        }
        return -1;
    }
}

internal enum ActionButtonStyle
{
    Primary,
    Secondary,
    Quiet,
    Danger,
}

internal sealed class ActionButton : Control
{
    private bool _hovered;
    private bool _pressed;
    private ActionButtonStyle _visualStyle;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ActionButtonStyle VisualStyle
    {
        get => _visualStyle;
        set
        {
            _visualStyle = value;
            Invalidate();
        }
    }

    public ActionButton(string text, ActionButtonStyle style = ActionButtonStyle.Secondary)
    {
        Text = text;
        _visualStyle = style;
        Height = 40;
        Font = Theme.Font(9.25f, FontStyle.Bold);
        Width = Math.Max(86, TextRenderer.MeasureText(text, Font).Width + 34);
        ForeColor = Theme.Text;
        BackColor = Theme.Canvas;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = text;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.StandardClick |
            ControlStyles.UserPaint,
            true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(bounds, 10);

        Color fill;
        Color border;
        Color text;
        if (!Enabled)
        {
            fill = Color.Transparent;
            border = Theme.Border;
            text = Theme.Muted;
        }
        else
        {
            (fill, border, text) = _visualStyle switch
            {
                ActionButtonStyle.Primary => (
                    _pressed ? Theme.AccentPressed : _hovered ? Theme.AccentHover : Theme.Accent,
                    Theme.Accent,
                    Theme.AccentText),
                ActionButtonStyle.Danger => (
                    _pressed ? Theme.ErrSoft : _hovered ? Color.FromArgb(72, 34, 38) : Color.Transparent,
                    Theme.Err,
                    Theme.Err),
                ActionButtonStyle.Quiet => (
                    _pressed ? Theme.Control : _hovered ? Theme.SurfaceRaised : Color.Transparent,
                    Color.Transparent,
                    Theme.TextSecondary),
                _ => (
                    _pressed ? Theme.ControlHover : _hovered ? Theme.SurfaceRaised : Theme.Surface,
                    _hovered ? Theme.BorderStrong : Theme.Border,
                    Theme.Text),
            };
        }

        if (fill != Color.Transparent)
        {
            using var brush = new SolidBrush(fill);
            e.Graphics.FillPath(brush, path);
        }
        if (border != Color.Transparent)
        {
            using var pen = new Pen(border);
            e.Graphics.DrawPath(pen, path);
        }

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            bounds,
            text,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
        {
            var focus = Rectangle.Inflate(bounds, -4, -4);
            ControlPaint.DrawFocusRectangle(e.Graphics, focus, text, fill);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            _pressed = true;
            Invalidate();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            _pressed = false;
            Invalidate();
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyUp(e);
    }
}

internal enum NavigationGlyph
{
    Overview,
    Dictation,
    Audio,
    Intelligence,
    History,
    Advanced,
}

internal sealed class NavigationButton : Control
{
    private bool _selected;
    private bool _hovered;

    public string PageKey { get; }
    public NavigationGlyph Glyph { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            Invalidate();
        }
    }

    public NavigationButton(string pageKey, string text, NavigationGlyph glyph)
    {
        PageKey = pageKey;
        Glyph = glyph;
        Text = text;
        Height = 46;
        Font = Theme.Font(9.75f, FontStyle.Bold);
        ForeColor = Theme.TextSecondary;
        BackColor = Theme.Sidebar;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PageTab;
        AccessibleName = text;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.StandardClick |
            ControlStyles.UserPaint,
            true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        if (_selected || _hovered)
        {
            using var path = Theme.RoundedRect(Rectangle.Inflate(bounds, -4, -2), 11);
            using var fill = new SolidBrush(_selected ? Theme.AccentSoft : Theme.Surface);
            e.Graphics.FillPath(fill, path);
            if (_selected)
            {
                using var outline = new Pen(Color.FromArgb(145, Theme.BorderStrong), 1f);
                e.Graphics.DrawPath(outline, path);
            }
        }

        if (_selected)
        {
            using var marker = new SolidBrush(Theme.Accent);
            e.Graphics.FillEllipse(marker, 7, Height / 2 - 2, 4, 4);
        }

        var iconBounds = new Rectangle(20, 13, 20, 20);
        DrawGlyph(e.Graphics, iconBounds, _selected ? Theme.Accent : Theme.Muted);
        var textBounds = new Rectangle(53, 0, Math.Max(0, Width - 66), Height);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            textBounds,
            _selected ? Theme.Text : Theme.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -7, -5));
    }

    private void DrawGlyph(Graphics graphics, Rectangle r, Color color)
    {
        using var pen = new Pen(color, 1.7f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        switch (Glyph)
        {
            case NavigationGlyph.Overview:
                graphics.DrawRectangle(pen, r.X + 2, r.Y + 2, 6, 6);
                graphics.DrawRectangle(pen, r.X + 12, r.Y + 2, 6, 6);
                graphics.DrawRectangle(pen, r.X + 2, r.Y + 12, 6, 6);
                graphics.DrawRectangle(pen, r.X + 12, r.Y + 12, 6, 6);
                break;
            case NavigationGlyph.Dictation:
                graphics.DrawLine(pen, r.X + 4, r.Y + 5, r.X + 16, r.Y + 5);
                graphics.DrawLine(pen, r.X + 4, r.Y + 10, r.X + 14, r.Y + 10);
                graphics.DrawLine(pen, r.X + 4, r.Y + 15, r.X + 12, r.Y + 15);
                break;
            case NavigationGlyph.Audio:
                graphics.DrawArc(pen, r.X + 6, r.Y + 2, 8, 12, 0, 180);
                graphics.DrawLine(pen, r.X + 6, r.Y + 8, r.X + 6, r.Y + 7);
                graphics.DrawLine(pen, r.X + 14, r.Y + 8, r.X + 14, r.Y + 7);
                graphics.DrawArc(pen, r.X + 3, r.Y + 6, 14, 10, 0, 180);
                graphics.DrawLine(pen, r.X + 10, r.Y + 16, r.X + 10, r.Y + 19);
                break;
            case NavigationGlyph.Intelligence:
                graphics.DrawEllipse(pen, r.X + 3, r.Y + 3, 14, 14);
                graphics.DrawLine(pen, r.X + 10, r.Y, r.X + 10, r.Y + 4);
                graphics.DrawLine(pen, r.X + 10, r.Y + 16, r.X + 10, r.Y + 20);
                graphics.DrawLine(pen, r.X, r.Y + 10, r.X + 4, r.Y + 10);
                graphics.DrawLine(pen, r.X + 16, r.Y + 10, r.X + 20, r.Y + 10);
                break;
            case NavigationGlyph.History:
                graphics.DrawArc(pen, r.X + 2, r.Y + 2, 16, 16, -55, 300);
                graphics.DrawLine(pen, r.X + 2, r.Y + 2, r.X + 2, r.Y + 8);
                graphics.DrawLine(pen, r.X + 2, r.Y + 2, r.X + 8, r.Y + 2);
                graphics.DrawLine(pen, r.X + 10, r.Y + 5, r.X + 10, r.Y + 10);
                graphics.DrawLine(pen, r.X + 10, r.Y + 10, r.X + 14, r.Y + 12);
                break;
            case NavigationGlyph.Advanced:
                using (var brush = new SolidBrush(color))
                {
                    graphics.DrawLine(pen, r.X + 3, r.Y + 5, r.X + 17, r.Y + 5);
                    graphics.DrawLine(pen, r.X + 3, r.Y + 10, r.X + 17, r.Y + 10);
                    graphics.DrawLine(pen, r.X + 3, r.Y + 15, r.X + 17, r.Y + 15);
                    graphics.FillEllipse(brush, r.X + 6, r.Y + 3, 4, 4);
                    graphics.FillEllipse(brush, r.X + 12, r.Y + 8, 4, 4);
                    graphics.FillEllipse(brush, r.X + 7, r.Y + 13, 4, 4);
                }
                break;
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyUp(e);
    }
}

internal sealed class SurfacePanel : Panel
{
    private Color _surfaceColor = Theme.Surface;
    private Color _borderColor = Theme.Border;
    private int _radius = 16;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceColor
    {
        get => _surfaceColor;
        set
        {
            _surfaceColor = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius
    {
        get => _radius;
        set
        {
            _radius = value;
            Invalidate();
        }
    }

    public SurfacePanel()
    {
        BackColor = Theme.Canvas;
        DoubleBuffered = true;
        Padding = new Padding(26);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Canvas);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        using var fill = new SolidBrush(SurfaceColor);
        using var border = new Pen(BorderColor);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        using var highlight = new Pen(Color.FromArgb(24, Theme.Text));
        using var highlightPath = Theme.RoundedRect(new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), Math.Max(1, Radius - 1));
        e.Graphics.DrawPath(highlight, highlightPath);
    }
}

internal sealed class TextFieldFrame : Panel
{
    private readonly TextBox _textBox;
    private readonly bool _multiline;
    private bool _focused;

    public TextBox TextBox => _textBox;

    public TextFieldFrame(TextBox textBox, int height = 40, bool multiline = false)
    {
        _textBox = textBox;
        _multiline = multiline;
        Height = height;
        BackColor = Theme.Control;
        DoubleBuffered = true;
        Padding = multiline ? new Padding(12, 10, 12, 10) : new Padding(12, 8, 12, 8);

        textBox.BorderStyle = BorderStyle.None;
        textBox.BackColor = Theme.Control;
        textBox.ForeColor = Theme.Text;
        textBox.Font = Theme.Font(9.75f);
        textBox.Multiline = multiline;
        textBox.Dock = DockStyle.Fill;
        textBox.GotFocus += (_, _) =>
        {
            _focused = true;
            Invalidate();
        };
        textBox.LostFocus += (_, _) =>
        {
            _focused = false;
            Invalidate();
        };
        Controls.Add(textBox);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 8);
        using var border = new Pen(_focused ? Theme.Accent : Theme.Border, _focused ? 1.5f : 1f);
        e.Graphics.DrawPath(border, path);
    }
}

internal sealed class ChoiceStrip : Control
{
    private readonly string[] _labels;
    private readonly string[] _values;
    private int _selectedIndex;
    private int _hoverIndex = -1;

    public event EventHandler? SelectedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < 0 || value >= _labels.Length || value == _selectedIndex)
                return;
            _selectedIndex = value;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedValue => _values[_selectedIndex];

    public ChoiceStrip(string[] labels, string[] values)
    {
        if (labels.Length == 0 || labels.Length != values.Length)
            throw new ArgumentException("Choice labels and values must be non-empty and have equal length.");

        _labels = labels;
        _values = values;
        Height = 40;
        BackColor = Theme.Surface;
        Font = Theme.Font(9.25f);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PageTabList;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.UserPaint,
            true);
    }

    public void SelectValue(string value)
    {
        int index = Array.IndexOf(_values, value);
        if (index >= 0)
            SelectedIndex = index;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var selectedFont = Theme.Font(9.25f, FontStyle.Bold);
        var outer = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(outer, 9))
        using (var fill = new SolidBrush(Theme.Control))
        using (var border = new Pen(Focused ? Theme.BorderStrong : Theme.Border))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        for (int i = 0; i < _labels.Length; i++)
        {
            Rectangle item = ItemBounds(i);
            bool selected = i == _selectedIndex;
            if (Enabled && (selected || i == _hoverIndex))
            {
                using var path = Theme.RoundedRect(Rectangle.Inflate(item, -3, -4), 7);
                using var fill = new SolidBrush(selected ? Theme.SurfaceRaised : Theme.ControlHover);
                e.Graphics.FillPath(fill, path);
                if (selected)
                {
                    using var pen = new Pen(Theme.BorderStrong);
                    e.Graphics.DrawPath(pen, path);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                _labels[i],
                selected ? selectedFont : Font,
                item,
                !Enabled ? Theme.Muted : selected ? Theme.Text : Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private Rectangle ItemBounds(int index)
    {
        int left = index * Width / _labels.Length;
        int right = (index + 1) * Width / _labels.Length;
        return new Rectangle(left, 0, right - left, Height);
    }

    private int HitIndex(int x) => Math.Clamp(x * _labels.Length / Math.Max(1, Width), 0, _labels.Length - 1);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = HitIndex(e.X);
        if (_hoverIndex != index)
        {
            _hoverIndex = index;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            SelectedIndex = HitIndex(e.X);
        }
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Left or Keys.Up)
        {
            SelectedIndex = (_selectedIndex - 1 + _labels.Length) % _labels.Length;
            e.Handled = true;
        }
        else if (e.KeyCode is Keys.Right or Keys.Down)
        {
            SelectedIndex = (_selectedIndex + 1) % _labels.Length;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}

internal sealed class ToggleSwitch : Control
{
    private bool _checked;
    private bool _hovered;

    public event EventHandler? CheckedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleSwitch(string text)
    {
        Text = text;
        Height = 36;
        Font = Theme.Font(9.5f);
        Width = Math.Max(190, TextRenderer.MeasureText(text, Font).Width + 68);
        ForeColor = Theme.Text;
        BackColor = Theme.Surface;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
        AccessibleName = text;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.UserPaint,
            true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(0, 6, 42, 24);
        using (var path = Theme.RoundedRect(track, 12))
        using (var fill = new SolidBrush(_checked ? Theme.Accent : _hovered ? Theme.ControlHover : Theme.Control))
        using (var border = new Pen(_checked ? Theme.Accent : Theme.BorderStrong))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        int knobX = _checked ? 21 : 3;
        using (var knob = new SolidBrush(_checked ? Theme.AccentText : Theme.TextSecondary))
            e.Graphics.FillEllipse(knob, knobX, 9, 18, 18);

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            new Rectangle(54, 0, Math.Max(0, Width - 54), Height),
            Enabled ? Theme.Text : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(50, 5, Math.Max(8, Width - 52), Height - 10));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            Checked = !Checked;
        }
        base.OnMouseDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyUp(e);
    }
}

internal sealed class StatusPill : Control
{
    private Color _statusColor = Theme.Muted;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color StatusColor
    {
        get => _statusColor;
        set
        {
            _statusColor = value;
            Invalidate();
        }
    }

    public StatusPill()
    {
        Text = "Checking";
        Height = 30;
        Width = 112;
        Font = Theme.Font(8.75f, FontStyle.Bold);
        BackColor = Theme.Canvas;
        AccessibleRole = AccessibleRole.StaticText;
        DoubleBuffered = true;
    }

    public void SetStatus(string text, Color color)
    {
        Text = text;
        StatusColor = color;
        Width = Math.Max(88, TextRenderer.MeasureText(text, Font).Width + 38);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 15);
        using var fill = new SolidBrush(Color.FromArgb(35, _statusColor));
        using var border = new Pen(Color.FromArgb(110, _statusColor));
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        using var dot = new SolidBrush(_statusColor);
        e.Graphics.FillEllipse(dot, 12, 11, 8, 8);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            new Rectangle(27, 0, Width - 35, Height),
            Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

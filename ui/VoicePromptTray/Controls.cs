using System.Drawing.Drawing2D;

namespace VoicePromptTray;

internal sealed class CardPanel : Panel
{
    private readonly string _title;

    public CardPanel(string title)
    {
        _title = title;
        BackColor = Theme.Bg;
        DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 10);
        using var bg = new SolidBrush(Theme.Card);
        using var pen = new Pen(Theme.Border);
        g.FillPath(bg, path);
        g.DrawPath(pen, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var accent = new SolidBrush(Theme.Accent);
        g.FillRectangle(accent, 20, 16, 4, 16);
        using var brush = new SolidBrush(Theme.Text);
        using var font = Theme.Font(10.5f, FontStyle.Bold);
        g.DrawString(_title, font, brush, 32, 13);
        using var pen = new Pen(Theme.Border);
        g.DrawLine(pen, 20, 44, Width - 20, 44);
    }
}

internal sealed class FlatButton : Control
{
    public enum ButtonStyle { Accent, Subtle, Danger }

    private readonly Color _surface;
    private ButtonStyle _style;
    private bool _hover;
    private bool _down;

    public FlatButton(string text, ButtonStyle style = ButtonStyle.Subtle, Color? surface = null)
    {
        _surface = surface ?? Theme.Bar;
        _style = style;
        Text = text;
        Height = 34;
        Font = Theme.Font(9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        BackColor = _surface;
        SetStyle(ControlStyles.Selectable | ControlStyles.UserMouse, true);
        DoubleBuffered = true;
        Width = TextRenderer.MeasureText(text, Font).Width + 32;
        UpdateForeColor();
    }

    public void SetStyle(ButtonStyle style)
    {
        _style = style;
        UpdateForeColor();
        Invalidate();
    }

    private void UpdateForeColor() =>
        ForeColor = _style switch
        {
            ButtonStyle.Accent => Theme.AccentText,
            ButtonStyle.Danger => Theme.Err,
            _ => Theme.Text,
        };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(r, 8);
        Color fill = !Enabled
            ? Color.Transparent
            : _style switch
            {
                ButtonStyle.Accent => _down ? Theme.AccentDown : _hover ? Theme.AccentHover : Theme.Accent,
                _ => _down ? Theme.Input : _hover ? Theme.InputHover : Color.Transparent,
            };
        if (fill != Color.Transparent)
        {
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);
        }
        using var pen = new Pen(_style == ButtonStyle.Subtle ? Theme.Border : fill == Color.Transparent ? Theme.Border : fill);
        g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, r, Enabled ? ForeColor : Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); _hover = false; _down = false; Invalidate(); }
}

internal sealed class InputFrame : Panel
{
    private readonly Control _inner;
    private readonly bool _multiline;

    public InputFrame(Control inner, int height = 32, bool multiline = false)
    {
        _inner = inner;
        _multiline = multiline;
        Height = height;
        BackColor = Theme.Input;
        DoubleBuffered = true;
        if (inner is TextBox t)
        {
            t.BorderStyle = BorderStyle.None;
            t.BackColor = Theme.Input;
            t.ForeColor = Theme.Text;
            t.Font = Theme.Font();
        }
        Controls.Add(inner);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (Width <= 0 || Height <= 0)
            return;
        if (_multiline)
        {
            _inner.Bounds = new Rectangle(9, 6, Width - 18, Height - 12);
        }
        else
        {
            int h = _inner.Height;
            _inner.Bounds = new Rectangle(9, Math.Max(0, (Height - h) / 2), Math.Max(10, Width - 18), h);
        }
        UpdateRegion();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        if (Width > 12 && Height > 12)
        {
            using var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), 6);
            Region?.Dispose();
            Region = new Region(path);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.Border);
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 6);
        g.DrawPath(pen, path);
    }
}

internal sealed class SegmentedControl : Control
{
    private readonly string[] _labels;
    private readonly string[] _values;
    private int _selected;
    private int _hover = -1;

    public event EventHandler? SelectedChanged;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value == _selected || value < 0 || value >= _labels.Length)
                return;
            _selected = value;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedValue => _values[_selected];

    public SegmentedControl(string[] labels, string[] values)
    {
        _labels = labels;
        _values = values;
        Height = 32;
        BackColor = Theme.Card;
        Font = Theme.Font(9f);
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, false);
    }

    public void SelectValue(string value)
    {
        int i = Array.IndexOf(_values, value);
        if (i >= 0)
            SelectedIndex = i;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 7))
        using (var b = new SolidBrush(Theme.Input))
        using (var pen = new Pen(Theme.Border))
        {
            g.FillPath(b, p);
            g.DrawPath(pen, p);
        }

        int n = _labels.Length;
        int segW = (Width - 2) / n;
        for (int i = 0; i < n; i++)
        {
            int x = 1 + i * segW;
            int w = i == n - 1 ? Width - 1 - x : segW;
            var r = new Rectangle(x, 1, w, Height - 2);
            bool sel = i == _selected;
            if (sel || i == _hover)
            {
                var inner = Rectangle.Inflate(r, -2, -3);
                using var path = Theme.RoundedRect(inner, 5);
                using var b = new SolidBrush(sel ? Theme.Accent : Theme.InputHover);
                g.FillPath(b, path);
            }
            Color color = sel ? Theme.AccentText : Theme.Muted;
            using Font font = sel ? Theme.Font(9f, FontStyle.Bold) : Theme.Font(9f);
            TextRenderer.DrawText(g, _labels[i], font, r, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int idx = HitIndex(e.X);
        if (idx != _hover)
        {
            _hover = idx;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            SelectedIndex = HitIndex(e.X);
        base.OnMouseDown(e);
    }

    private int HitIndex(int x)
    {
        int segW = (Width - 2) / _labels.Length;
        if (segW <= 0)
            return 0;
        return Math.Clamp((x - 1) / segW, 0, _labels.Length - 1);
    }
}

internal sealed class StatusDot : Control
{
    private Color _dotColor = Theme.Muted;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color DotColor
    {
        get => _dotColor;
        set { _dotColor = value; Invalidate(); }
    }

    public StatusDot()
    {
        Size = new Size(10, 10);
        DoubleBuffered = true;
        BackColor = Theme.Bar;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var b = new SolidBrush(_dotColor);
        g.FillEllipse(b, 1, 1, Width - 2, Height - 2);
    }
}

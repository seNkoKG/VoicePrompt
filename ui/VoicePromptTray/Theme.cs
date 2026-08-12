using System.Drawing.Drawing2D;

namespace VoicePromptTray;

internal sealed record ThemePalette(
    string Id,
    string Name,
    string Description,
    Color Canvas,
    Color Sidebar,
    Color Surface,
    Color SurfaceRaised,
    Color Control,
    Color ControlHover,
    Color Border,
    Color BorderStrong,
    Color Text,
    Color TextSecondary,
    Color Muted,
    Color Accent,
    Color AccentHover,
    Color AccentPressed,
    Color AccentSoft,
    Color AccentText,
    Color Ok,
    Color OkSoft,
    Color Warn,
    Color WarnSoft,
    Color Err,
    Color ErrSoft);

internal static class Theme
{
    private static readonly ThemePalette[] Palettes =
    {
        new(
            "graphite", "Graphite", "Neutral and focused",
            Color.FromArgb(10, 12, 14), Color.FromArgb(13, 16, 19),
            Color.FromArgb(19, 23, 27), Color.FromArgb(24, 29, 34),
            Color.FromArgb(29, 35, 40), Color.FromArgb(37, 44, 50),
            Color.FromArgb(44, 51, 57), Color.FromArgb(63, 72, 79),
            Color.FromArgb(244, 246, 244), Color.FromArgb(186, 193, 188), Color.FromArgb(125, 135, 129),
            Color.FromArgb(205, 218, 209), Color.FromArgb(226, 233, 228), Color.FromArgb(178, 194, 183),
            Color.FromArgb(37, 48, 42), Color.FromArgb(13, 18, 15),
            Color.FromArgb(102, 211, 155), Color.FromArgb(24, 55, 42),
            Color.FromArgb(232, 184, 93), Color.FromArgb(60, 48, 27),
            Color.FromArgb(239, 112, 112), Color.FromArgb(63, 31, 34)),
        new(
            "evergreen", "Evergreen", "Calm and natural",
            Color.FromArgb(8, 13, 12), Color.FromArgb(11, 17, 15),
            Color.FromArgb(17, 25, 22), Color.FromArgb(21, 32, 28),
            Color.FromArgb(27, 39, 34), Color.FromArgb(34, 50, 43),
            Color.FromArgb(41, 58, 51), Color.FromArgb(58, 78, 69),
            Color.FromArgb(242, 247, 244), Color.FromArgb(181, 197, 188), Color.FromArgb(116, 139, 127),
            Color.FromArgb(163, 220, 188), Color.FromArgb(190, 237, 211), Color.FromArgb(132, 194, 159),
            Color.FromArgb(27, 56, 43), Color.FromArgb(9, 20, 14),
            Color.FromArgb(104, 215, 158), Color.FromArgb(23, 57, 43),
            Color.FromArgb(230, 188, 103), Color.FromArgb(59, 49, 29),
            Color.FromArgb(238, 116, 116), Color.FromArgb(62, 32, 34)),
        new(
            "ember", "Ember", "Warm and understated",
            Color.FromArgb(14, 12, 11), Color.FromArgb(18, 15, 14),
            Color.FromArgb(25, 21, 19), Color.FromArgb(31, 26, 23),
            Color.FromArgb(38, 32, 28), Color.FromArgb(49, 41, 35),
            Color.FromArgb(58, 48, 41), Color.FromArgb(78, 64, 53),
            Color.FromArgb(247, 244, 240), Color.FromArgb(202, 190, 177), Color.FromArgb(145, 128, 112),
            Color.FromArgb(224, 191, 146), Color.FromArgb(239, 211, 172), Color.FromArgb(202, 164, 116),
            Color.FromArgb(59, 45, 31), Color.FromArgb(24, 17, 10),
            Color.FromArgb(107, 208, 153), Color.FromArgb(27, 55, 42),
            Color.FromArgb(235, 180, 85), Color.FromArgb(63, 46, 24),
            Color.FromArgb(238, 111, 105), Color.FromArgb(65, 31, 31)),
    };

    private static ThemePalette _current = Palettes[0];
    private static string? _family;

    public static IReadOnlyList<ThemePalette> Available => Palettes;
    public static ThemePalette Current => _current;

    public static Color Canvas => _current.Canvas;
    public static Color Sidebar => _current.Sidebar;
    public static Color Surface => _current.Surface;
    public static Color SurfaceRaised => _current.SurfaceRaised;
    public static Color Control => _current.Control;
    public static Color ControlHover => _current.ControlHover;
    public static Color Border => _current.Border;
    public static Color BorderStrong => _current.BorderStrong;
    public static Color Text => _current.Text;
    public static Color TextSecondary => _current.TextSecondary;
    public static Color Muted => _current.Muted;
    public static Color Accent => _current.Accent;
    public static Color AccentHover => _current.AccentHover;
    public static Color AccentPressed => _current.AccentPressed;
    public static Color AccentSoft => _current.AccentSoft;
    public static Color AccentText => _current.AccentText;
    public static Color Ok => _current.Ok;
    public static Color OkSoft => _current.OkSoft;
    public static Color Warn => _current.Warn;
    public static Color WarnSoft => _current.WarnSoft;
    public static Color Err => _current.Err;
    public static Color ErrSoft => _current.ErrSoft;

    // Compatibility aliases for the compact recording overlay.
    public static Color Bg => Canvas;
    public static Color Bar => Sidebar;
    public static Color Card => Surface;
    public static Color Input => Control;
    public static Color InputHover => ControlHover;
    public static Color AccentDown => AccentPressed;

    public static ThemePalette Find(string? id) =>
        Palettes.FirstOrDefault(palette => palette.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? Palettes[0];

    public static ThemePalette Use(string? id)
    {
        ThemePalette previous = _current;
        _current = Find(id);
        return previous;
    }

    public static void ApplyToTree(Control root, ThemePalette previous)
    {
        ApplyToControl(root, previous);
        foreach (Control child in root.Controls)
            ApplyToTree(child, previous);
        root.Invalidate(true);
    }

    private static void ApplyToControl(Control control, ThemePalette previous)
    {
        control.BackColor = Map(control.BackColor, previous);
        control.ForeColor = Map(control.ForeColor, previous);
        if (control is SurfacePanel surface)
        {
            surface.SurfaceColor = Map(surface.SurfaceColor, previous);
            surface.BorderColor = Map(surface.BorderColor, previous);
        }
        if (control is StatusPill status)
            status.StatusColor = Map(status.StatusColor, previous);
    }

    private static Color Map(Color color, ThemePalette previous)
    {
        if (color == previous.Canvas) return Canvas;
        if (color == previous.Sidebar) return Sidebar;
        if (color == previous.Surface) return Surface;
        if (color == previous.SurfaceRaised) return SurfaceRaised;
        if (color == previous.Control) return Control;
        if (color == previous.ControlHover) return ControlHover;
        if (color == previous.Border) return Border;
        if (color == previous.BorderStrong) return BorderStrong;
        if (color == previous.Text) return Text;
        if (color == previous.TextSecondary) return TextSecondary;
        if (color == previous.Muted) return Muted;
        if (color == previous.Accent) return Accent;
        if (color == previous.AccentHover) return AccentHover;
        if (color == previous.AccentPressed) return AccentPressed;
        if (color == previous.AccentSoft) return AccentSoft;
        if (color == previous.AccentText) return AccentText;
        if (color == previous.Ok) return Ok;
        if (color == previous.OkSoft) return OkSoft;
        if (color == previous.Warn) return Warn;
        if (color == previous.WarnSoft) return WarnSoft;
        if (color == previous.Err) return Err;
        if (color == previous.ErrSoft) return ErrSoft;
        return color;
    }

    public static string Family
    {
        get
        {
            if (_family != null)
                return _family;

            _family = FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text")
                ? "Segoe UI Variable Text"
                : "Segoe UI";
            return _family;
        }
    }

    public static Font Font(float size = 9.5f, FontStyle style = FontStyle.Regular) =>
        new(Family, size, style, GraphicsUnit.Point);

    public static Font DisplayFont(float size = 18f, FontStyle style = FontStyle.Bold) =>
        new(Family, size, style, GraphicsUnit.Point);

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int safeRadius = Math.Max(1, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
        int diameter = safeRadius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Label Label(
        string text,
        Color? color = null,
        float size = 9.5f,
        FontStyle style = FontStyle.Regular,
        Color? background = null) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = color ?? Text,
        BackColor = background ?? Surface,
        Font = Font(size, style),
        UseMnemonic = false,
    };

    public static void StyleCombo(ComboBox combo)
    {
        combo.BackColor = Control;
        combo.ForeColor = Text;
        combo.FlatStyle = FlatStyle.Flat;
        combo.Font = Font(9.5f);
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.ItemHeight = 29;
        combo.IntegralHeight = false;
        combo.DropDownHeight = 248;
        combo.DrawItem += (_, e) =>
        {
            if (e.Index < 0)
                return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? AccentSoft : Control);
            e.Graphics.FillRectangle(background, e.Bounds);
            string label = combo.GetItemText(combo.Items[e.Index]) ?? string.Empty;
            TextRenderer.DrawText(
                e.Graphics,
                label,
                combo.Font,
                new Rectangle(e.Bounds.X + 10, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 14), e.Bounds.Height),
                selected ? Text : TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0)
                e.DrawFocusRectangle();
        };
    }

    public static void StyleNumeric(NumericUpDown numeric)
    {
        numeric.BackColor = Control;
        numeric.ForeColor = Text;
        numeric.BorderStyle = BorderStyle.FixedSingle;
        numeric.Font = Font(9.5f);
        numeric.TextAlign = HorizontalAlignment.Left;
    }
}

using System.Drawing.Drawing2D;

namespace VoicePromptTray;

internal static class Theme
{
    public static readonly Color Canvas = Color.FromArgb(12, 14, 17);
    public static readonly Color Sidebar = Color.FromArgb(16, 18, 22);
    public static readonly Color Surface = Color.FromArgb(21, 24, 29);
    public static readonly Color SurfaceRaised = Color.FromArgb(26, 30, 36);
    public static readonly Color Control = Color.FromArgb(30, 35, 42);
    public static readonly Color ControlHover = Color.FromArgb(38, 44, 53);
    public static readonly Color Border = Color.FromArgb(47, 54, 64);
    public static readonly Color BorderStrong = Color.FromArgb(67, 77, 91);

    public static readonly Color Text = Color.FromArgb(241, 243, 246);
    public static readonly Color TextSecondary = Color.FromArgb(176, 184, 195);
    public static readonly Color Muted = Color.FromArgb(126, 136, 150);

    public static readonly Color Accent = Color.FromArgb(124, 164, 246);
    public static readonly Color AccentHover = Color.FromArgb(148, 182, 250);
    public static readonly Color AccentPressed = Color.FromArgb(101, 143, 229);
    public static readonly Color AccentSoft = Color.FromArgb(31, 45, 70);
    public static readonly Color AccentText = Color.FromArgb(10, 16, 26);

    public static readonly Color Ok = Color.FromArgb(92, 201, 145);
    public static readonly Color OkSoft = Color.FromArgb(26, 55, 44);
    public static readonly Color Warn = Color.FromArgb(232, 184, 93);
    public static readonly Color WarnSoft = Color.FromArgb(60, 48, 27);
    public static readonly Color Err = Color.FromArgb(239, 112, 112);
    public static readonly Color ErrSoft = Color.FromArgb(63, 31, 34);

    // Compatibility aliases for the compact recording overlay.
    public static Color Bg => Canvas;
    public static Color Bar => Sidebar;
    public static Color Card => Surface;
    public static Color Input => Control;
    public static Color InputHover => ControlHover;
    public static Color AccentDown => AccentPressed;

    private static string? _family;

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
        combo.ItemHeight = 27;
        combo.IntegralHeight = false;
        combo.DropDownHeight = 240;
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
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height),
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

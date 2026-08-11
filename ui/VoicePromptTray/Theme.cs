using System.Drawing.Drawing2D;

namespace VoicePromptTray;

internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(14, 16, 19);
    public static readonly Color Bar = Color.FromArgb(18, 20, 24);
    public static readonly Color Card = Color.FromArgb(22, 25, 30);
    public static readonly Color Input = Color.FromArgb(28, 32, 38);
    public static readonly Color InputHover = Color.FromArgb(35, 40, 48);
    public static readonly Color Border = Color.FromArgb(47, 53, 62);
    public static readonly Color Accent = Color.FromArgb(198, 204, 212);
    public static readonly Color AccentHover = Color.FromArgb(218, 222, 227);
    public static readonly Color AccentDown = Color.FromArgb(168, 176, 186);
    public static readonly Color AccentText = Color.FromArgb(16, 18, 22);
    public static readonly Color Text = Color.FromArgb(239, 241, 244);
    public static readonly Color Muted = Color.FromArgb(139, 147, 158);
    public static readonly Color Ok = Color.FromArgb(95, 194, 134);
    public static readonly Color Warn = Color.FromArgb(215, 169, 75);
    public static readonly Color Err = Color.FromArgb(224, 94, 94);

    private static string? _family;

    public static string Family
    {
        get
        {
            if (_family == null)
            {
                _family = "Segoe UI";
                foreach (var f in FontFamily.Families)
                {
                    if (f.Name == "Segoe UI Variable Text")
                    {
                        _family = f.Name;
                        break;
                    }
                }
            }
            return _family;
        }
    }

    public static Font Font(float size = 9.5f, FontStyle style = FontStyle.Regular) =>
        new(Family, size, style, GraphicsUnit.Point);

    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Label Label(string text, Color? color = null, float size = 9.5f, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = color ?? Text,
        BackColor = Card,
        Font = Font(size, bold ? FontStyle.Bold : FontStyle.Regular),
    };

    public static void StyleCombo(ComboBox c)
    {
        c.BackColor = Input;
        c.ForeColor = Text;
        c.FlatStyle = FlatStyle.Flat;
        c.Font = Font();
    }

    public static void StyleNumeric(NumericUpDown n)
    {
        n.BackColor = Input;
        n.ForeColor = Text;
        n.BorderStyle = BorderStyle.FixedSingle;
        n.Font = Font();
    }
}

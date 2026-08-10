using System.Drawing.Drawing2D;

namespace VoicePromptTray;

internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(22, 23, 29);
    public static readonly Color Bar = Color.FromArgb(26, 28, 36);
    public static readonly Color Card = Color.FromArgb(31, 33, 43);
    public static readonly Color Input = Color.FromArgb(38, 41, 54);
    public static readonly Color InputHover = Color.FromArgb(46, 50, 64);
    public static readonly Color Border = Color.FromArgb(51, 55, 71);
    public static readonly Color Accent = Color.FromArgb(139, 92, 246);
    public static readonly Color AccentHover = Color.FromArgb(157, 116, 248);
    public static readonly Color AccentDown = Color.FromArgb(124, 77, 235);
    public static readonly Color Text = Color.FromArgb(236, 237, 242);
    public static readonly Color Muted = Color.FromArgb(152, 160, 179);
    public static readonly Color Ok = Color.FromArgb(74, 222, 128);
    public static readonly Color Warn = Color.FromArgb(250, 204, 21);
    public static readonly Color Err = Color.FromArgb(248, 113, 113);

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

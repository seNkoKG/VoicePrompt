using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace VoicePromptTray;

internal sealed class HotkeyRecorder : Control
{
    [DllImport("user32.dll")]
    private static extern short GetKeyState(int keyCode);

    private string _binding = "";
    private string _committedBinding = "";
    private bool _capturing;
    private bool _hovered;

    public event EventHandler<string>? BindingChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Binding
    {
        get => _binding;
        set
        {
            _binding = value?.Trim() ?? "";
            _committedBinding = _binding;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsCapturing => _capturing;

    public HotkeyRecorder()
    {
        Height = 46;
        MinimumSize = new Size(220, 46);
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = Theme.Font(9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.HotkeyField;
        AccessibleName = "Global dictation hotkey";
        AccessibleDescription = "Select this field and press a key or key combination.";
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.UserPaint,
            true);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            BeginCapture();
        }
        base.OnMouseDown(e);
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

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        CommitCapture();
        base.OnLostFocus(e);
    }

    private void BeginCapture()
    {
        if (!_capturing)
            _committedBinding = _binding;
        _capturing = true;
        Invalidate();
    }

    private void CommitCapture()
    {
        if (!_capturing)
            return;

        bool changed = !string.Equals(_binding, _committedBinding, StringComparison.Ordinal);
        _committedBinding = _binding;
        _capturing = false;
        Invalidate();
        if (changed)
            BindingChanged?.Invoke(this, _binding);
    }

    private void CancelCapture()
    {
        _binding = _committedBinding;
        _capturing = false;
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_capturing && e.KeyCode is Keys.Enter or Keys.Space)
        {
            BeginCapture();
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (!_capturing)
        {
            base.OnKeyDown(e);
            return;
        }

        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.KeyCode == Keys.Enter)
        {
            CommitCapture();
            Parent?.SelectNextControl(this, true, true, true, true);
            return;
        }
        if (e.KeyCode == Keys.Escape)
        {
            CancelCapture();
            return;
        }
        if (e.KeyCode is Keys.Back or Keys.Delete)
        {
            _binding = "";
            Invalidate();
            return;
        }
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return;

        string? key = ToBindingKey(e.KeyCode);
        if (key == null)
            return;

        var parts = new List<string>(5);
        if ((e.Modifiers & Keys.Control) != 0)
            parts.Add("ctrl");
        if ((e.Modifiers & Keys.Alt) != 0)
            parts.Add("alt");
        if ((e.Modifiers & Keys.Shift) != 0)
            parts.Add("shift");
        if (IsPressed(Keys.LWin) || IsPressed(Keys.RWin))
            parts.Add("cmd");
        parts.Add(key);

        _binding = string.Join('+', parts);
        Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_capturing)
        {
            OnKeyDown(new KeyEventArgs(keyData));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(bounds, 9))
        using (var fill = new SolidBrush(_hovered || _capturing ? Theme.ControlHover : Theme.Control))
        using (var border = new Pen(_capturing ? Theme.Accent : Focused ? Theme.BorderStrong : Theme.Border, _capturing ? 1.7f : 1f))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        if (_binding.Length == 0)
        {
            string empty = _capturing ? "Press your shortcut" : "Click and press a shortcut";
            using var emptyFont = Theme.Font(9.5f);
            TextRenderer.DrawText(
                e.Graphics,
                empty,
                emptyFont,
                new Rectangle(14, 0, Width - 28, Height),
                Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        else
        {
            DrawKeyCaps(e.Graphics);
        }

        if (_capturing && Width >= 410)
        {
            using var helperFont = Theme.Font(8.25f);
            TextRenderer.DrawText(
                e.Graphics,
                "Enter save  ·  Esc cancel  ·  Backspace clear",
                helperFont,
                new Rectangle(190, 0, Width - 204, Height),
                Theme.TextSecondary,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private void DrawKeyCaps(Graphics graphics)
    {
        string[] labels = _binding.Split('+').Select(DisplayPart).ToArray();
        int x = 10;
        using var keyFont = Theme.Font(8.75f, FontStyle.Bold);
        foreach (string label in labels)
        {
            int width = Math.Max(34, TextRenderer.MeasureText(label, keyFont).Width + 18);
            if (x + width > Width - 12)
                break;

            var keyBounds = new Rectangle(x, 9, width, 28);
            using var path = Theme.RoundedRect(keyBounds, 6);
            using var fill = new SolidBrush(Theme.SurfaceRaised);
            using var border = new Pen(Theme.BorderStrong);
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
            TextRenderer.DrawText(
                graphics,
                label,
                keyFont,
                keyBounds,
                Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            x += width + 6;
        }
    }

    private static bool IsPressed(Keys key) => (GetKeyState((int)key) & 0x8000) != 0;

    private static string DisplayPart(string part) => part switch
    {
        "ctrl" => "CTRL",
        "alt" => "ALT",
        "shift" => "SHIFT",
        "cmd" => "WIN",
        _ when Regex.IsMatch(part, @"^f\d+$") => part.ToUpperInvariant(),
        _ when part.Length == 1 => part.ToUpperInvariant(),
        _ => string.Join(' ', part.Split('_').Select(word => char.ToUpperInvariant(word[0]) + word[1..])),
    };

    private static string? ToBindingKey(Keys key)
    {
        Keys code = key & Keys.KeyCode;
        if (code is >= Keys.A and <= Keys.Z)
            return code.ToString().ToLowerInvariant();
        if (code is >= Keys.D0 and <= Keys.D9)
            return ((char)('0' + code - Keys.D0)).ToString();
        if (code is >= Keys.NumPad0 and <= Keys.NumPad9)
            return ((char)('0' + code - Keys.NumPad0)).ToString();
        if (code is >= Keys.F1 and <= Keys.F24)
            return "f" + (code - Keys.F1 + 1);

        return code switch
        {
            Keys.Space => "space",
            Keys.Tab => "tab",
            Keys.Enter => "enter",
            Keys.Escape => "esc",
            Keys.Back => "backspace",
            Keys.Insert => "insert",
            Keys.Delete => "delete",
            Keys.Home => "home",
            Keys.End => "end",
            Keys.PageUp => "page_up",
            Keys.PageDown => "page_down",
            Keys.Up => "up",
            Keys.Down => "down",
            Keys.Left => "left",
            Keys.Right => "right",
            Keys.PrintScreen => "print_screen",
            Keys.Pause => "pause",
            Keys.CapsLock => "caps_lock",
            Keys.Scroll => "scroll_lock",
            Keys.NumLock => "num_lock",
            Keys.Apps => "menu",
            _ => null,
        };
    }
}

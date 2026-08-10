using System.ComponentModel;
using System.Text.RegularExpressions;

namespace VoicePromptTray;

internal sealed class HotkeyRecorder : Control
{
    private string _binding = "";
    private string _committed = "";
    private bool _armed;

    public event EventHandler<string>? BindingChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Binding
    {
        get => _binding;
        set
        {
            _binding = value ?? "";
            _committed = _binding;
            Invalidate();
        }
    }

    public HotkeyRecorder()
    {
        Height = 32;
        BackColor = Theme.Card;
        Font = Theme.Font(10f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        _armed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        _armed = true;
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        _armed = false;
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        if (!_armed)
            return;

        switch (e.KeyCode)
        {
            case Keys.Enter:
                _armed = false;
                _committed = _binding;
                Parent?.SelectNextControl(this, true, true, true, true);
                Invalidate();
                return;

            case Keys.Escape:
                _armed = false;
                _binding = _committed;
                Invalidate();
                return;

            case Keys.Back:
                _binding = "";
                _committed = "";
                Invalidate();
                BindingChanged?.Invoke(this, _binding);
                return;

            case Keys.ControlKey:
            case Keys.ShiftKey:
            case Keys.Menu:
            case Keys.LWin:
            case Keys.RWin:
                return;
        }

        string? keyName = KeyName(e.KeyCode);
        if (keyName == null)
            return;

        var mods = new List<string>();
        if ((e.Modifiers & Keys.Control) != 0)
            mods.Add("ctrl");
        if ((e.Modifiers & Keys.Alt) != 0)
            mods.Add("alt");
        if ((e.Modifiers & Keys.Shift) != 0)
            mods.Add("shift");
        if ((e.KeyData & Keys.LWin) != 0 || (e.KeyData & Keys.RWin) != 0)
            mods.Add("cmd");

        mods.Add(keyName);
        string binding = string.Join("+", mods);
        if (binding == _binding)
            return;

        _binding = binding;
        _committed = binding;
        Invalidate();
        BindingChanged?.Invoke(this, _binding);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_armed)
        {
            OnKeyDown(new KeyEventArgs(keyData));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(r, 7))
        using (var fill = new SolidBrush(Theme.Input))
        using (var pen = new Pen(_armed ? Theme.Accent : Theme.Border, _armed ? 2f : 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        string text;
        Color color;
        Font font;
        if (_binding != "")
        {
            text = DisplayBinding();
            color = Theme.Text;
            font = Theme.Font(10f, FontStyle.Bold);
        }
        else if (_armed)
        {
            text = "Press keys…";
            color = Theme.Muted;
            font = Theme.Font(9.5f);
        }
        else
        {
            text = "Click to set hotkey";
            color = Theme.Muted;
            font = Theme.Font(9.5f);
        }

        var textRect = new Rectangle(12, 0, Width - 24, Height);
        TextRenderer.DrawText(g, text, font, textRect, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (_armed)
        {
            using var hintFont = Theme.Font(8f);
            TextRenderer.DrawText(g, "Enter ✓   Esc ✗   Backspace clear", hintFont, textRect, Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private string DisplayBinding()
    {
        if (_binding == "")
            return "";
        var parts = _binding.Split('+').Select(part => part switch
        {
            "ctrl" => "Ctrl",
            "alt" => "Alt",
            "shift" => "Shift",
            "cmd" => "Win",
            _ => PrettyKey(part),
        });
        return string.Join(" + ", parts);
    }

    private static string PrettyKey(string name)
    {
        if (Regex.IsMatch(name, @"^f\d+$"))
            return name.ToUpperInvariant();
        if (name.Length == 1 && char.IsDigit(name[0]))
            return name;
        if (name.Length == 1)
            return name.ToUpperInvariant();
        return string.Join(" ", name.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    private static string? KeyName(Keys key)
    {
        Keys k = key & Keys.KeyCode;
        if (k >= Keys.A && k <= Keys.Z)
            return k.ToString().ToLowerInvariant();
        if (k >= Keys.D0 && k <= Keys.D9)
            return ((char)('0' + (k - Keys.D0))).ToString();
        if (k >= Keys.NumPad0 && k <= Keys.NumPad9)
            return ((char)('0' + (k - Keys.NumPad0))).ToString();
        if (k >= Keys.F1 && k <= Keys.F24)
            return "f" + (k - Keys.F1 + 1);
        return k switch
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

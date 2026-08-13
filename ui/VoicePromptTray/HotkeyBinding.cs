using System.Text.RegularExpressions;

namespace VoicePromptTray;

internal static partial class HotkeyBinding
{
    private static readonly HashSet<string> Modifiers =
        new(["alt", "ctrl", "control", "shift"], StringComparer.Ordinal);
    private static readonly HashSet<string> NamedKeys =
        new(["space", "tab", "enter", "esc", "backspace", "insert", "delete", "home", "end",
            "page_up", "page_down", "up", "down", "left", "right", "print_screen", "pause",
            "caps_lock", "scroll_lock", "num_lock", "menu"], StringComparer.Ordinal);

    public static string? Validate(string? value)
    {
        string binding = value?.Trim().ToLowerInvariant() ?? "";
        string[] parts = binding.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(string.IsNullOrEmpty) ||
            parts[..^1].Any(part => !Modifiers.Contains(part)) ||
            parts[..^1].Distinct(StringComparer.Ordinal).Count() != parts.Length - 1)
            return "Choose one key with optional Ctrl, Alt, or Shift modifiers.";

        string key = parts[^1];
        if (key == "f12")
            return "F12 is reserved by Windows for debuggers. Choose another shortcut.";
        bool supported = (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) ||
            NamedKeys.Contains(key) || FunctionKey().IsMatch(key);
        if (!supported)
            return "Choose a letter, number, F1–F11/F13–F24, or supported named key.";
        return null;
    }

    [GeneratedRegex(@"^f(?:(?:[1-9]|1[0-1])|(?:1[3-9]|2[0-4]))$", RegexOptions.CultureInvariant)]
    private static partial Regex FunctionKey();
}

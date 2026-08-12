using System.Runtime.InteropServices;

namespace VoicePromptTray;

internal static class NativeWindowStyle
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;
    private const int WmThemeChanged = 0x031A;

    public static void Apply(Form form)
    {
        if (!OperatingSystem.IsWindows())
            return;

        int enabled = 1;
        SetDwm(form.Handle, DwmUseImmersiveDarkMode, enabled);
        SetDwm(form.Handle, DwmBorderColor, ColorRef(Theme.Border));
        SetDwm(form.Handle, DwmCaptionColor, ColorRef(Theme.Sidebar));
        SetDwm(form.Handle, DwmTextColor, ColorRef(Theme.Text));
        ApplyToTree(form);
    }

    public static void ApplyToTree(Control root)
    {
        if (!OperatingSystem.IsWindows())
            return;

        ApplyControl(root);
        foreach (Control child in root.Controls)
            ApplyToTree(child);
    }

    private static void ApplyControl(Control control)
    {
        if (!control.IsHandleCreated)
            return;

        _ = SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        _ = SendMessage(control.Handle, WmThemeChanged, IntPtr.Zero, IntPtr.Zero);
    }

    private static void SetDwm(IntPtr handle, int attribute, int value)
    {
        try
        {
            _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static int ColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}

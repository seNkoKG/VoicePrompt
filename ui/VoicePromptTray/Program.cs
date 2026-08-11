namespace VoicePromptTray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.Dark);

        const string mutexName = "VoicePromptTray_4F2A1C";
        using var mutex = new Mutex(true, mutexName, out bool created);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, mutexName + "_Show");

        if (!created)
        {
            showEvent.Set();
            return;
        }

        using var app = new TrayApp();
        if (!args.Contains("--tray"))
            app.OpenSettings();

        var waiter = new Thread(() =>
        {
            while (showEvent.WaitOne())
                app.OpenSettings();
        })
        { IsBackground = true };
        waiter.Start();

        Application.Run();
    }
}

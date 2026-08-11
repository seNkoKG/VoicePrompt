namespace VoicePromptTray;

internal sealed class TrayApp : IDisposable
{
    private readonly AppPaths _paths = AppPaths.Default;
    private readonly DaemonManager _daemon;
    private readonly MainForm _form;
    private readonly RecordingOverlay _overlay;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _restartItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _startupTimer;
    private DaemonState _lastState = DaemonState.Unknown;
    private bool _busy;

    public TrayApp()
    {
        _daemon = new DaemonManager(_paths);
        _form = new MainForm(_daemon, _paths);
        _overlay = new RecordingOverlay();
        _form.DaemonRestarted += () => Balloon("Settings applied", "Daemon restarted — press your hotkey to talk.");

        _tray = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Voice Typing",
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => OpenSettings();

        _toggleItem = new ToolStripMenuItem("Start daemon");
        _toggleItem.Click += async (_, _) =>
        {
            bool running = _daemon.Refresh(true).State == DaemonState.Running;
            if (running)
            {
                await RunDaemonActionAsync(_daemon.Stop, "Daemon stopped", "Dictation is off.");
            }
            else
            {
                await RunDaemonActionAsync(_daemon.Start, "Daemon started", "Hold your hotkey to talk.");
            }
        };

        _restartItem = new ToolStripMenuItem("Restart daemon");
        _restartItem.Click += async (_, _) =>
        {
            await RunDaemonActionAsync(_daemon.Restart, "Daemon restarted", "Ready.");
        };

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Exit();

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Open Settings") { Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        menu.Items[0].Click += (_, _) => OpenSettings();
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_restartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        _tray.ContextMenuStrip = menu;

        _timer = new System.Windows.Forms.Timer { Interval = 2500 };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();

        _startupTimer = new System.Windows.Forms.Timer { Interval = 600 };
        _startupTimer.Tick += async (_, _) =>
        {
            _startupTimer.Stop();
            if (_daemon.Refresh(true).State != DaemonState.Running)
                await RunDaemonActionAsync(_daemon.Start, "Voice Typing ready", "Hold your hotkey to talk.");
        };
        _startupTimer.Start();
        Poll();
        ShowFirstRunBalloon();
    }

    private static Icon LoadIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    public void OpenSettings()
    {
        if (_form.IsDisposed)
            return;
        if (_form.InvokeRequired)
        {
            _form.BeginInvoke(OpenSettings);
            return;
        }
        _form.Show();
        _form.WindowState = FormWindowState.Normal;
        _form.Activate();
    }

    private async Task RunDaemonActionAsync(Action action, string title, string text)
    {
        if (_busy)
            return;

        _busy = true;
        Poll();
        try
        {
            await Task.Run(action);
            Balloon(title, text);
        }
        catch (Exception ex)
        {
            Balloon("Voice Typing error", ex.Message);
        }
        finally
        {
            _busy = false;
            Poll();
        }
    }

    private void Poll()
    {
        if (_form.IsDisposed)
            return;

        var info = _daemon.Refresh();
        _form.UpdateStatus(info);

        if (_lastState != DaemonState.Unknown && info.State != _lastState)
        {
            if (info.State == DaemonState.Running)
                Balloon("Voice Typing ready", $"Daemon running — hotkey {info.Hotkey} ({info.Mode} mode).");
            else if (info.State == DaemonState.Stopped)
                Balloon("Daemon stopped", "Dictation is off.");
        }
        _lastState = info.State;

        _toggleItem.Text = info.State == DaemonState.Running ? "Stop daemon" : "Start daemon";
        _toggleItem.Enabled = !_busy;
        _restartItem.Enabled = info.State == DaemonState.Running && !_busy;
        _tray.Text = info.State == DaemonState.Running && info.Hotkey != null
            ? $"Voice Typing — {info.Hotkey} ({info.Mode})"
            : "Voice Typing";
    }

    private void ShowFirstRunBalloon()
    {
        try
        {
            string flag = Path.Combine(_paths.AppDataDir, "firstrun.flag");
            if (File.Exists(flag))
                return;
            Directory.CreateDirectory(_paths.AppDataDir);
            File.WriteAllText(flag, "1");
            _tray.BalloonTipTitle = "Voice Typing is running";
            _tray.BalloonTipText = "Click the icon for settings. Double-click to open.";
            _tray.ShowBalloonTip(4000);
        }
        catch
        {
        }
    }

    private void Balloon(string title, string text)
    {
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = text;
        _tray.ShowBalloonTip(3000);
    }

    public void Exit()
    {
        if (_busy)
            return;
        _timer.Stop();
        _startupTimer.Stop();
        _overlay.Dispose();
        _form.AllowClose = true;
        _form.Close();
        _tray.Visible = false;
        _tray.Dispose();
        Application.Exit();
    }

    public void Dispose()
    {
        try
        {
            _startupTimer.Stop();
            _startupTimer.Dispose();
            _overlay.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
        }
        catch
        {
        }
    }
}

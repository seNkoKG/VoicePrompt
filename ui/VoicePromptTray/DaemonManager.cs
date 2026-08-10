using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace VoicePromptTray;

internal enum DaemonState
{
    Unknown,
    Stopped,
    Running,
}

internal sealed record DaemonInfo
{
    public DaemonState State { get; init; } = DaemonState.Unknown;
    public int Pid { get; init; }
    public string? Hotkey { get; init; }
    public string? Mode { get; init; }
    public string? Engine { get; init; }
}

internal sealed class DaemonManager
{
    private readonly AppPaths _paths;
    private readonly object _lock = new();
    private DaemonInfo _last = new();
    private DateTime _lastRefresh;

    public DaemonManager(AppPaths paths) => _paths = paths;

    public bool Installed => _paths.Installed;

    public DaemonInfo Info => _last;

    public DaemonInfo Refresh(bool force = false)
    {
        lock (_lock)
        {
            if (!force && _last.State != DaemonState.Unknown && DateTime.UtcNow - _lastRefresh < TimeSpan.FromSeconds(2.5))
                return _last;

            _last = Query();
            _lastRefresh = DateTime.UtcNow;
            return _last;
        }
    }

    private DaemonInfo Query()
    {
        if (!Installed)
            return new DaemonInfo();

        try
        {
            string output = RunCli("status", 5000);
            var info = new DaemonInfo { State = DaemonState.Stopped };
            var m = System.Text.RegularExpressions.Regex.Match(output, @"running \(PID (\d+)\)");
            if (m.Success)
            {
                info = info with { State = DaemonState.Running, Pid = int.Parse(m.Groups[1].Value) };
                var hk = System.Text.RegularExpressions.Regex.Match(output, @"Hotkey:\s+(\S+)");
                var mode = System.Text.RegularExpressions.Regex.Match(output, @"Mode:\s+(\S+)");
                var eng = System.Text.RegularExpressions.Regex.Match(output, @"Engine:\s+(\S+)");
                info = info with
                {
                    Hotkey = hk.Success ? hk.Groups[1].Value : ReadStateHotkey(),
                    Mode = mode.Success ? mode.Groups[1].Value : null,
                    Engine = eng.Success ? eng.Groups[1].Value : null,
                };
            }
            return info;
        }
        catch
        {
            return new DaemonInfo();
        }
    }

    private string? ReadStateHotkey()
    {
        try
        {
            if (File.Exists(_paths.StatePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(_paths.StatePath));
                if (doc.RootElement.TryGetProperty("hotkey", out var hk) && hk.ValueKind == JsonValueKind.String)
                    return hk.GetString();
            }
        }
        catch
        {
        }
        return null;
    }

    public void Start()
    {
        Refresh(true);
        if (_last.State == DaemonState.Running || !File.Exists(_paths.Pythonw) || !File.Exists(_paths.RunnerPy))
            return;

        var psi = new ProcessStartInfo(_paths.Pythonw)
        {
            Arguments = "\"" + _paths.RunnerPy + "\"",
            UseShellExecute = true,
            CreateNoWindow = true,
        };
        using (Process.Start(psi))
        {
        }

        for (int i = 0; i < 40; i++)
        {
            Thread.Sleep(500);
            Refresh(true);
            if (_last.State == DaemonState.Running)
                return;
        }
    }

    public void Stop()
    {
        Refresh(true);
        if (_last.State != DaemonState.Running)
            return;

        RunCli("stop", 15000);
        for (int i = 0; i < 20; i++)
        {
            Thread.Sleep(400);
            Refresh(true);
            if (_last.State != DaemonState.Running)
                return;
        }
    }

    public void Restart()
    {
        Stop();
        Thread.Sleep(500);
        Start();
    }

    public IReadOnlyList<string> ListDevices()
    {
        var result = new List<string>();
        if (!Installed)
            return result;

        try
        {
            string output = RunCli("devices", 5000);
            foreach (var line in output.Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    line, @"^\s*(\d+)\s+(.+?)\s+(\d+)\s+([\d.]+)\s*$");
                if (m.Success)
                    result.Add($"{m.Groups[1].Value}: {m.Groups[2].Value.Trim()}");
            }
        }
        catch
        {
        }
        return result;
    }

    private string RunCli(string args, int timeoutMs)
    {
        var psi = new ProcessStartInfo(_paths.DaemonExe)
        {
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi)!;
        var stdout = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        if (!p.WaitForExit(timeoutMs))
        {
            try
            {
                p.Kill(true);
            }
            catch
            {
            }
            return "";
        }
        return stdout.ToString();
    }
}

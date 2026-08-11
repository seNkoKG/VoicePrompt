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

        if (!File.Exists(_paths.PidPath))
            return new DaemonInfo { State = DaemonState.Stopped };

        try
        {
            if (!int.TryParse(File.ReadAllText(_paths.PidPath).Trim(), out int pid))
                return new DaemonInfo { State = DaemonState.Stopped };

            using var process = Process.GetProcessById(pid);
            if (process.HasExited || process.StartTime.ToUniversalTime() > File.GetLastWriteTimeUtc(_paths.PidPath).AddSeconds(2))
                return new DaemonInfo { State = DaemonState.Stopped };

            var info = new DaemonInfo { State = DaemonState.Running, Pid = pid };
            if (File.Exists(_paths.StatePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(_paths.StatePath));
                var root = doc.RootElement;
                info = info with
                {
                    Hotkey = root.TryGetProperty("hotkey", out var hotkey) ? hotkey.GetString() : null,
                    Mode = root.TryGetProperty("mode", out var mode) ? mode.GetString() : null,
                    Engine = root.TryGetProperty("engine", out var engine) ? engine.GetString() : null,
                };
            }
            return info;
        }
        catch (ArgumentException)
        {
            return new DaemonInfo { State = DaemonState.Stopped };
        }
        catch
        {
            return new DaemonInfo();
        }
    }

    public void Start()
    {
        Refresh(true);
        if (_last.State == DaemonState.Running)
            return;
        if (!File.Exists(_paths.Pythonw) || !File.Exists(_paths.RunnerPy))
            throw new InvalidOperationException("Voice Typing runtime is not installed.");

        var psi = new ProcessStartInfo(_paths.Pythonw)
        {
            Arguments = "\"" + _paths.RunnerPy + "\"",
            UseShellExecute = true,
            CreateNoWindow = true,
        };
        using var started = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Voice Typing runtime.");

        for (int i = 0; i < 40; i++)
        {
            Thread.Sleep(500);
            Refresh(true);
            if (_last.State == DaemonState.Running)
                return;
        }
        throw new InvalidOperationException($"Daemon did not start. Check {_paths.LogPath}");
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
        throw new TimeoutException("Daemon did not stop within 8 seconds.");
    }

    public void Restart()
    {
        Stop();
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
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            try
            {
                p.Kill(true);
                p.WaitForExit(2000);
            }
            catch
            {
            }
            throw new TimeoutException($"Daemon command '{args}' timed out.");
        }
        string output = stdout.GetAwaiter().GetResult();
        string error = stderr.GetAwaiter().GetResult();
        if (p.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
        return output;
    }
}

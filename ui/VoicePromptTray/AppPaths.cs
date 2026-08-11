namespace VoicePromptTray;

internal sealed class AppPaths
{
    public static AppPaths Default { get; } = new();

    public string Home { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".voice-typing");

    public string VenvScripts => Path.Combine(Home, "venv", "Scripts");

    public string DaemonExe => Path.Combine(VenvScripts, "faster-whisper-dictation.exe");

    public string Pythonw => Path.Combine(VenvScripts, "pythonw.exe");

    public string Python => Path.Combine(VenvScripts, "python.exe");

    public string RunnerPy => Path.Combine(Home, "run_daemon.pyw");

    public string ConfigDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "faster-whisper-dictation",
        "faster-whisper-dictation");

    public string ConfigPath => Path.Combine(ConfigDir, "config.toml");

    public string StatePath => Path.Combine(ConfigDir, "state.json");

    public string PidPath => Path.Combine(ConfigDir, "daemon.pid");

    public string LogPath => Path.Combine(Home, "daemon.log");

    public string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoicePrompt");

    public string AiConfigPath => Path.Combine(AppDataDir, "ai.json");

    public string AiRewriterPath => Path.Combine(
        Home,
        "venv",
        "Lib",
        "site-packages",
        "whisper_dictation",
        "ai_rewriter.py");

    public bool Installed => File.Exists(DaemonExe) && File.Exists(RunnerPy);
}

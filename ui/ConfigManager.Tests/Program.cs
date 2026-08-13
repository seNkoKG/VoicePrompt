using System.Net;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var dir = Path.Combine(Path.GetTempPath(), "vptest_cfg_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
string path = Path.Combine(dir, "config.toml");
File.WriteAllText(path, """
[server]
url = "http://localhost:8000"         # WhisperLiveKit server URL
model = "Systran/faster-whisper-large-v3"
language = ""
prompt = "V kodi pišem"
# temperature = 0.0
# hotwords = ""

[hotkey]
binding = "f1"
mode = "hold"

[vad]
threshold = 0.6
silence_ms = 250
min_speech_ms = 250
max_speech_s = 90.0

[audio]
sample_rate = 16000

[engine]
type = "local"
compute_type = "float16"
""", System.Text.Encoding.UTF8);

var cfg = new VoicePromptTray.ConfigManager(path);
int failures = 0;

void Check(string name, bool ok)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
    if (!ok) failures++;
}

Check("read binding", cfg.GetString("hotkey", "binding") == "f1");
Check("read mode", cfg.GetString("hotkey", "mode") == "hold");
Check("read threshold", cfg.GetDouble("vad", "threshold") == 0.6);
Check("read silence", cfg.GetInt("vad", "silence_ms") == 250);
Check("read prompt", cfg.GetString("server", "prompt") == "V kodi pišem");
Check("commented temp is null", cfg.GetDouble("server", "temperature") == null);

cfg.Set("hotkey", "binding", "ctrl+shift+f1");
cfg.Set("server", "language", "sl-slang");
cfg.Set("vad", "threshold", 0.55);
cfg.Set("vad", "silence_ms", 300);
cfg.Set("server", "temperature", 0.2);
cfg.Set("server", "hotwords", "python, null");
cfg.Set("audio", "sample_rate", 44100);
cfg.Set("engine", "device", "cuda");
cfg.Set("audio", "device", "");
cfg.Set("voiceprompt", "slovenian_slang", true);
cfg.Set("voiceprompt", "output_mode", "clipboard");
cfg.Set("voiceprompt", "voice_commands", true);
cfg.Set("voiceprompt", "base_prompt", "V kodi pišem");
cfg.Save();

string after = File.ReadAllText(path);
Console.WriteLine("--- result ---");
Console.WriteLine(after);
Console.WriteLine("---------------");

Check("binding updated", Regex.IsMatch(after, @"binding = \""ctrl\+shift\+f1\"""));
Check("hybrid slang language saved", Regex.IsMatch(after, @"language = \""sl-slang\"""));
Check("threshold updated", Regex.IsMatch(after, @"threshold = 0\.55\r?$", RegexOptions.Multiline));
Check("silence updated", Regex.IsMatch(after, @"silence_ms = 300"));
Check("commented key uncommented", Regex.IsMatch(after, @"^temperature = 0\.2\r?$", RegexOptions.Multiline));
Check("hotwords added + escaped", Regex.IsMatch(after, @"hotwords = \""python, null\"""));
Check("inline comment preserved", after.Contains("WhisperLiveKit server URL"));
Check("VoicePrompt profile section added", new VoicePromptTray.ConfigManager(path).GetBool("voiceprompt", "slovenian_slang") == true);
Check("copy-only output setting round trips", new VoicePromptTray.ConfigManager(path).GetString("voiceprompt", "output_mode") == "clipboard");
Check("voice-command setting round trips", new VoicePromptTray.ConfigManager(path).GetBool("voiceprompt", "voice_commands") == true);
Check("file still parses after re-read", new VoicePromptTray.ConfigManager(path).GetInt("vad", "silence_ms") == 300);

string savedOnce = File.ReadAllText(path);
new VoicePromptTray.ConfigManager(path).Save();
Check("repeated save is stable", File.ReadAllText(path) == savedOnce);

string newPath = Path.Combine(dir, "new", "config.toml");
var defaults = new VoicePromptTray.ConfigManager(newPath);
Check("new config is written immediately", File.Exists(newPath) && File.ReadAllText(newPath).Contains("[hotkey]"));
Check("new config has working defaults", defaults.GetString("hotkey", "binding") == "f1");
Check("new config keeps recognition local with bounded server fallback",
    defaults.GetString("engine", "type") == "local" && defaults.GetInt("server", "timeout") == 60);
Check("new config enables lossless long-recording prefetch", defaults.GetBool("voiceprompt", "buffered_transcription") == true);
Check("new config defaults to automatic paste", defaults.GetString("voiceprompt", "output_mode") == "paste");
Check("new config keeps voice commands opt-in", defaults.GetBool("voiceprompt", "voice_commands") == false);

string legacyPath = Path.Combine(dir, "legacy.toml");
const string legacyPrompt = "V kodi pišem Python funkcije, JavaScript handlerje in TypeScript interface. API endpoint vrača JSON preko HTTPS na REST API in branje iz SQL baze deluje. Preveri refresh token, authentication middleware, async in await, null in undefined. Uporabljam npm in pip, docker build, ssh na strežnik, git pull, git commit in git push origin main. Odpri terminal in preveri ta file, nato popravi funkcijo in naredi pull request.";
File.WriteAllText(legacyPath, $"""
    [server]
    prompt = "{legacyPrompt}"

    [voiceprompt]
    base_prompt = "{legacyPrompt}"
    """);
var migrated = new VoicePromptTray.ConfigManager(legacyPath);
Check("legacy biased server prompt is cleared", migrated.GetString("server", "prompt") == "");
Check("legacy biased base prompt is cleared", migrated.GetString("voiceprompt", "base_prompt") == "");
Check("legacy prompt migration is persisted", !File.ReadAllText(legacyPath).Contains(legacyPrompt, StringComparison.Ordinal));

string aiPath = Path.Combine(dir, "ai.json");
const string secret = "vp-test-secret";
var ai = new VoicePromptTray.AiSettings
{
    Mode = "grammar",
    Endpoint = "http://127.0.0.1:11434/v1/chat/completions",
    Model = "qwen2.5:3b",
    TimeoutMs = 900,
    ApiKeyProtected = VoicePromptTray.AiSettingsStore.ProtectApiKey(secret),
};
VoicePromptTray.AiSettingsStore.Save(aiPath, ai);
string aiJson = File.ReadAllText(aiPath);
var aiReloaded = VoicePromptTray.AiSettingsStore.Load(aiPath);
Check("AI settings round trip", aiReloaded.Mode == "grammar" && aiReloaded.TimeoutMs == 900);
Check("AI key is not stored as plaintext", !aiJson.Contains(secret, StringComparison.Ordinal));
Check("AI key decrypts for current Windows user", VoicePromptTray.AiSettingsStore.UnprotectApiKey(aiReloaded.ApiKeyProtected) == secret);
Check("AI settings validation accepts compatible endpoint", VoicePromptTray.AiSettingsStore.Validate(aiReloaded) == null);
aiReloaded.Mode = "clean";
Check("AI settings accept conservative Clean mode", VoicePromptTray.AiSettingsStore.Validate(aiReloaded) == null);
aiReloaded.Mode = "grammar";
aiReloaded.TimeoutMs = 200;
Check("AI settings reject unsafe wait", VoicePromptTray.AiSettingsStore.Validate(aiReloaded)?.Contains("400") == true);
File.WriteAllText(aiPath, "{broken");
Check("broken AI settings fall back safely", VoicePromptTray.AiSettingsStore.Load(aiPath).Mode == "off");
File.WriteAllText(aiPath, """{"mode":"unknown","endpoint":null,"model":null,"timeout_ms":0}""");
var normalizedAi = VoicePromptTray.AiSettingsStore.Load(aiPath);
Check("AI settings normalize hand-edited values", normalizedAi.Mode == "off" && normalizedAi.TimeoutMs == 400 && normalizedAi.Endpoint.StartsWith("http"));

string slangPrompt = VoicePromptTray.SlovenianSlangProfile.ApplyPrompt("Moj osnovni prompt.");
Check("Slovenian slang prompt preserves custom context", slangPrompt.StartsWith("Moj osnovni prompt.") && slangPrompt.Contains("Kva tle ne štima?"));
Check("Slovenian slang prompt is idempotent", VoicePromptTray.SlovenianSlangProfile.ApplyPrompt(slangPrompt) == slangPrompt);
string slangHotwords = VoicePromptTray.SlovenianSlangProfile.ApplyHotwords("Python, dej");
Check("Slovenian slang hotwords merge without duplicates", slangHotwords.StartsWith("Python, dej") && slangHotwords.Split(',').Count(x => x.Trim() == "dej") == 1);

var corrections = VoicePromptTray.PersonalDictionaryStore.Parse("polly market => Polymarket\ncodecs => Codex");
Check("personal corrections parse", corrections.Count == 2 && corrections[0].Replacement == "Polymarket");
try
{
    VoicePromptTray.PersonalDictionaryStore.Parse("same => first\nSAME => second");
    Check("duplicate corrections rejected", false);
}
catch (InvalidDataException)
{
    Check("duplicate corrections rejected", true);
}
string correctionsPath = Path.Combine(dir, "local", "corrections.json");
var dictionary = new VoicePromptTray.PersonalDictionaryStore(correctionsPath);
dictionary.SaveText("polly market => Polymarket\nžabar => Ljubljančan");
Check("personal corrections round trip", dictionary.LoadText().Contains("žabar => Ljubljančan"));
File.WriteAllText(correctionsPath, new string('x', 128 * 1024 + 1));
Check("oversized personal corrections fail closed", dictionary.LoadText() == "");
dictionary.SaveText("polly market => Polymarket\nžabar => Ljubljančan");

string snippetsPath = Path.Combine(dir, "local", "snippets.json");
var snippets = new VoicePromptTray.TextSnippetStore(snippetsPath);
var parsedSnippets = VoicePromptTray.TextSnippetStore.Parse("signature => Lep pozdrav,\\nŽan\nreply => Thank you!");
Check("text snippets parse escaped lines", parsedSnippets.Count == 2 && parsedSnippets[0].Content == "Lep pozdrav,\nŽan");
snippets.SaveText("signature => Lep pozdrav,\\nŽan\nreply => Thank you!");
Check("text snippets round trip", snippets.LoadText().Contains("Lep pozdrav,\\nŽan") && snippets.LoadText().Contains("reply => Thank you!"));
File.WriteAllText(snippetsPath, new string('x', 512 * 1024 + 1));
Check("oversized text snippets fail closed", snippets.LoadText() == "");
snippets.SaveText("signature => Lep pozdrav,\\nŽan\nreply => Thank you!");
try
{
    VoicePromptTray.TextSnippetStore.Parse("reply => One\nREPLY => Two");
    Check("duplicate snippets rejected", false);
}
catch (InvalidDataException)
{
    Check("duplicate snippets rejected", true);
}

string appProfilesPath = Path.Combine(dir, "local", "app-profiles.json");
var appProfiles = new VoicePromptTray.AppProfileStore(appProfilesPath);
var parsedAppProfiles = VoicePromptTray.AppProfileStore.Parse(
    "Code.exe => prompt, paste\nDiscord.exe => verbatim, inherit");
Check("application profiles parse exact writing and output modes",
    parsedAppProfiles.Count == 2 &&
    parsedAppProfiles[0].WritingMode == "prompt" &&
    parsedAppProfiles[1].WritingMode == "off");
appProfiles.SaveText("Code.exe => prompt, paste\nBeležke.exe => off, clipboard");
Check("application profiles round trip Unicode executable names",
    appProfiles.LoadText().Contains("Beležke.exe => off, clipboard"));
File.WriteAllText(appProfilesPath, new string('x', 128 * 1024 + 1));
Check("oversized application profiles fail closed", appProfiles.LoadText() == "");
appProfiles.SaveText("Code.exe => prompt, paste\nBeležke.exe => off, clipboard");
Check("application profiles identify provider requirement",
    VoicePromptTray.AppProfileStore.UsesAi("Code.exe => grammar, inherit") &&
    !VoicePromptTray.AppProfileStore.UsesAi("Notepad.exe => verbatim, paste"));
try
{
    VoicePromptTray.AppProfileStore.Parse("C:\\Windows\\app.exe => prompt, paste");
    Check("application profile paths rejected", false);
}
catch (InvalidDataException)
{
    Check("application profile paths rejected", true);
}
try
{
    VoicePromptTray.AppProfileStore.Parse("Code.exe => prompt, paste\ncode.EXE => clean, paste");
    Check("duplicate application profiles rejected", false);
}
catch (InvalidDataException)
{
    Check("duplicate application profiles rejected", true);
}

var portableBackup = new VoicePromptTray.VoicePromptBackupDocument
{
    Dictation = new VoicePromptTray.BackupDictationSettings
    {
        Hotkey = "ctrl+shift+f1",
        Activation = "hold",
        OutputMode = "clipboard",
        VoiceCommands = true,
        Language = "sl",
        Prompt = "Imena: Žan",
        Hotwords = "Codex, Ljubljana",
    },
    Recognition = new VoicePromptTray.BackupRecognitionSettings
    {
        EngineType = "server",
        ServerUrl = "https://speech.example.test/",
        ServerTimeoutSeconds = 90,
        Model = "Systran/faster-whisper-large-v3",
        Processor = "cuda",
        ComputeType = "float16",
        BufferedTranscription = true,
    },
    Audio = new VoicePromptTray.BackupAudioSettings(),
    Writing = new VoicePromptTray.BackupWritingSettings
    {
        Mode = "clean",
        Endpoint = "http://127.0.0.1:11434/v1/chat/completions",
        Model = "qwen2.5:3b",
        TimeoutMs = 900,
    },
    Recovery = new VoicePromptTray.BackupRecoverySettings { Enabled = true, Limit = 25 },
    Corrections = corrections.ToList(),
    Snippets = parsedSnippets.ToList(),
    AppProfiles = parsedAppProfiles.ToList(),
};
string backupJson = VoicePromptTray.AppBackupStore.Serialize(portableBackup);
var restoredBackup = VoicePromptTray.AppBackupStore.Deserialize(backupJson);
Check("settings backup preserves portable Unicode values",
    restoredBackup.Dictation.Prompt.Contains("Žan") &&
    restoredBackup.Snippets[0].Content.Contains("Žan") &&
    restoredBackup.AppProfiles[0].Executable == "Code.exe" &&
    restoredBackup.Recognition.EngineType == "server" &&
    restoredBackup.Recognition.ServerUrl == "https://speech.example.test" &&
    restoredBackup.Recognition.ServerTimeoutSeconds == 90 &&
    restoredBackup.Dictation.OutputMode == "clipboard");
Check("settings backup excludes API keys", !backupJson.Contains("api_key", StringComparison.OrdinalIgnoreCase));
Check("settings backup excludes transcript history", !backupJson.Contains("\"history\"", StringComparison.OrdinalIgnoreCase));
Check("settings backup excludes microphone identity", !backupJson.Contains("microphone", StringComparison.OrdinalIgnoreCase));
string backupPath = Path.Combine(dir, "portable", "VoicePrompt-settings-backup.json");
VoicePromptTray.AppBackupStore.Save(backupPath, portableBackup);
Check("settings backup file round trip", VoicePromptTray.AppBackupStore.Load(backupPath).Snippets.Count == 2);
try
{
    VoicePromptTray.AppBackupStore.Serialize(portableBackup with
    {
        Dictation = portableBackup.Dictation with { Hotkey = "ctrl+not-a-key" },
    });
    Check("settings backup rejects invalid hotkeys", false);
}
catch (InvalidDataException)
{
Check("settings backup rejects invalid hotkeys", true);
Check("native Windows hotkeys reject reserved bindings",
    VoicePromptTray.HotkeyBinding.Validate("f12") != null &&
    VoicePromptTray.HotkeyBinding.Validate("cmd+l") != null &&
    VoicePromptTray.HotkeyBinding.Validate("ctrl++f1") != null &&
    VoicePromptTray.HotkeyBinding.Validate("+f1") != null &&
    VoicePromptTray.HotkeyBinding.Validate("f1+") != null &&
    VoicePromptTray.HotkeyBinding.Validate("ctrl+shift+f1") is null &&
    VoicePromptTray.HotkeyBinding.Validate("f24") is null);
}
try
{
    VoicePromptTray.AppBackupStore.Serialize(portableBackup with
    {
        Writing = portableBackup.Writing with { Endpoint = "https://secret@example.test/v1/chat/completions" },
    });
    Check("settings backup rejects endpoint credentials", false);
}
catch (InvalidDataException)
{
    Check("settings backup rejects endpoint credentials", true);
}
try
{
    VoicePromptTray.AppBackupStore.Serialize(portableBackup with
    {
        Recognition = portableBackup.Recognition with { ServerUrl = "ftp://speech.example.test" },
    });
    Check("settings backup rejects invalid recognition server", false);
}
catch (InvalidDataException)
{
    Check("settings backup rejects invalid recognition server", true);
}

string historyPath = Path.Combine(dir, "local", "history.json");
string historySettingsPath = Path.Combine(dir, "local", "history-settings.json");
var history = new VoicePromptTray.TranscriptHistoryStore(historyPath, historySettingsPath);
history.SaveSettings(false, 500);
var historySettings = history.LoadSettings();
Check("history settings clamp and round trip", !historySettings.Enabled && historySettings.Limit == 100);
File.WriteAllText(historyPath, """
{"version":1,"items":[{"id":"empty","createdAt":"2026-08-12T12:01:00Z","text":"","originalText":""},{"id":"one","createdAt":"2026-08-12T12:00:00Z","text":"Pozdravljen svet","originalText":""}]}
""");
Check("history reads Unicode transcript", history.Load().Last().Text == "Pozdravljen svet");
Check("history finds latest usable transcript", history.Latest()?.Text == "Pozdravljen svet");
var rewrittenHistory = new VoicePromptTray.TranscriptEntry
{
    Text = "This is the cleaned sentence.",
    OriginalText = "uh this is the sentence",
};
Check("history exposes delivered and original text separately",
    rewrittenHistory.WasRewritten &&
    rewrittenHistory.SourceText == "uh this is the sentence" &&
    rewrittenHistory.Text == "This is the cleaned sentence.");
var verbatimHistory = new VoicePromptTray.TranscriptEntry { Text = "Živjo svet" };
Check("verbatim history uses delivered text as its source",
    !verbatimHistory.WasRewritten && verbatimHistory.SourceText == "Živjo svet");
history.Delete("empty");
history.Delete("one");
Check("history deletes selected transcript", history.Load().Count == 0 && history.Latest() is null);
File.WriteAllText(historyPath, new string('x', 2 * 1024 * 1024 + 1));
Check("oversized transcript history fails closed", history.Load().Count == 0);

var languageProfile = VoicePromptTray.LanguageProfileStore.Create(
    "es",
    "Nombres propios: Žiga, Ljubljana",
    "Codex, Polymarket",
    "codecs => Codex\npolly market => Polymarket");
string languageProfileJson = VoicePromptTray.LanguageProfileStore.Serialize(languageProfile);
var importedLanguageProfile = VoicePromptTray.LanguageProfileStore.Deserialize(languageProfileJson);
Check("language profile preserves Unicode and vocabulary",
    importedLanguageProfile.Language == "es" &&
    importedLanguageProfile.Prompt.Contains("Žiga") &&
    importedLanguageProfile.CorrectionsText.Contains("polly market => Polymarket"));
Check("language profile excludes private and machine settings",
    !languageProfileJson.Contains("apiKey", StringComparison.OrdinalIgnoreCase) &&
    !languageProfileJson.Contains("microphone", StringComparison.OrdinalIgnoreCase) &&
    !languageProfileJson.Contains("hotkey", StringComparison.OrdinalIgnoreCase) &&
    !languageProfileJson.Contains("history", StringComparison.OrdinalIgnoreCase));
Check("language profile normalizes Auto", VoicePromptTray.LanguageProfileStore.Create("AUTO", "", "", "").Language == "");
string languageProfilePath = Path.Combine(dir, "language-profile.json");
VoicePromptTray.LanguageProfileStore.Save(languageProfilePath, languageProfile);
Check("language profile file round trip", VoicePromptTray.LanguageProfileStore.Load(languageProfilePath).Language == "es");
try
{
    VoicePromptTray.LanguageProfileStore.Deserialize("{\"format\":\"voiceprompt-language-profile\",\"version\":1,\"language\":\"xx\"}");
    Check("language profile rejects unsupported language", false);
}
catch (InvalidDataException)
{
    Check("language profile rejects unsupported language", true);
}
try
{
    VoicePromptTray.LanguageProfileStore.Deserialize("{\"format\":\"unknown\",\"version\":1,\"language\":\"en\"}");
    Check("language profile rejects unknown schema", false);
}
catch (InvalidDataException)
{
    Check("language profile rejects unknown schema", true);
}
try
{
    VoicePromptTray.LanguageProfileStore.Deserialize(new string('x', 128 * 1024 + 1));
    Check("language profile bounds input size", false);
}
catch (InvalidDataException)
{
    Check("language profile bounds input size", true);
}

Check("Whisper catalog has 100 unique languages",
    VoicePromptTray.LanguageCatalog.All.Count == 100 &&
    VoicePromptTray.LanguageCatalog.All.Select(option => option.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 100);
Check("default languages are catalogued",
    VoicePromptTray.LanguageCatalog.Find("en")?.Name == "English" &&
    VoicePromptTray.LanguageCatalog.Find("SL")?.Name == "Slovenian");
Check("primary language modes normalize safely",
    VoicePromptTray.LanguageCatalog.PrimaryModeFor("AUTO") == "" &&
    VoicePromptTray.LanguageCatalog.PrimaryModeFor("EN") == "en" &&
    VoicePromptTray.LanguageCatalog.PrimaryModeFor("sl-SLANG") == "sl-slang");
Check("unsupported language is rejected", !VoicePromptTray.LanguageCatalog.IsSupported("xx"));

string[] performanceLog =
{
    "2026-08-12 10:00:00 INFO whisper_dictation.daemon: Recording started (use_ws=False, streaming=False, engine=local)",
    "2026-08-12 10:00:00 INFO whisper_dictation.daemon: Audio capture ready in 20 ms",
    "2026-08-12 10:00:05 INFO whisper_dictation.daemon: Recording stopped (5.0s)",
    "2026-08-12 10:00:06 INFO whisper_dictation.engine.local: Transcription latency: primary 0.500s, retry 0.000s, total 0.500s",
    "2026-08-12 10:00:06 INFO whisper_dictation.engine.local: Detected language: en (conf 0.91) [1 segments]",
    "2026-08-12 10:00:06 INFO whisper_dictation.daemon: Paste shortcut sent: 50 chars",
    "2026-08-12 10:01:00 INFO whisper_dictation.daemon: Recording started (use_ws=False, streaming=False, engine=local)",
    "2026-08-12 10:01:00 INFO whisper_dictation.daemon: Audio capture ready in 30 ms",
    "2026-08-12 10:01:15 INFO whisper_dictation.daemon: Recording stopped (15.0s)",
    "2026-08-12 10:01:16 INFO whisper_dictation.engine.local: Transcription latency: primary 1.000s, retry 0.500s, total 1.500s",
    "2026-08-12 10:01:16 INFO whisper_dictation.engine.local: Detected language: sl (conf 1.00) [2 segments]",
    "2026-08-12 10:01:16 INFO whisper_dictation.daemon: Paste shortcut sent: 100 chars",
    "2026-08-12 10:02:00 INFO whisper_dictation.daemon: Recording started (use_ws=False, streaming=False, engine=local)",
    "2026-08-12 10:02:00 INFO whisper_dictation.daemon: Audio capture ready in 40 ms",
    "2026-08-12 10:02:30 INFO whisper_dictation.daemon: Recording stopped (30.0s)",
    "2026-08-12 10:02:33 INFO whisper_dictation.engine.local: Transcription latency: primary 3.000s, retry 0.000s, total 3.000s",
    "2026-08-12 10:02:33 INFO whisper_dictation.engine.local: Detected language: de (conf 0.88) [4 segments]",
    "2026-08-12 10:02:33 INFO whisper_dictation.daemon: Transcript copied to clipboard: 200 chars",
};
var performance = VoicePromptTray.PerformanceSnapshot.Parse(performanceLog);
Check("performance parser joins completed recordings",
    performance.Count == 3 && performance.Latest?.Language == "de" && performance.Latest.Segments == 4);
Check("performance parser accepts copy-only delivery", performance.Latest?.Language == "de");
Check("performance parser computes stable percentiles",
    performance.MedianTotalSeconds == 1.5 && performance.P95TotalSeconds == 3.0 && performance.MedianMicrophoneMs == 30);
Check("performance parser reports retries and throughput",
    performance.RetryCount == 1 && performance.MedianRealtimeSpeed == 10.0);

string performancePath = Path.Combine(dir, "daemon.log");
File.WriteAllText(performancePath, new string('x', 6000) + "\n" + string.Join("\n", performanceLog));
Check("performance reader bounds large logs", VoicePromptTray.PerformanceSnapshot.Read(performancePath, maxBytes: 4096).Count == 3);
File.WriteAllText(performancePath, string.Join("\n", performanceLog).Replace("primary 0.500s", "primary broken"));
Check("malformed performance log fails closed", VoicePromptTray.PerformanceSnapshot.Read(performancePath).Count == 2);

var bufferedPerformance = VoicePromptTray.PerformanceSnapshot.Parse(new[]
{
    "2026-08-12 11:00:00 INFO whisper_dictation.daemon: Recording started (use_ws=False, streaming=True, buffered=True, engine=local)",
    "2026-08-12 11:00:00 INFO whisper_dictation.daemon: Audio capture ready in 18 ms",
    "2026-08-12 11:00:10 INFO whisper_dictation.engine.local: Transcription latency: primary 0.400s, retry 0.200s, total 0.600s",
    "2026-08-12 11:00:10 INFO whisper_dictation.engine.local: Detected language: sl (conf 0.93) [2 segments]",
    "2026-08-12 11:00:20 INFO whisper_dictation.engine.local: Transcription latency: primary 0.500s, retry 0.000s, total 0.500s",
    "2026-08-12 11:00:20 INFO whisper_dictation.engine.local: Detected language: en (conf 0.89) [2 segments]",
    "2026-08-12 11:00:30 INFO whisper_dictation.daemon: Recording stopped (30.0s)",
    "2026-08-12 11:00:30 INFO whisper_dictation.engine.local: Transcription latency: primary 0.300s, retry 0.000s, total 0.300s",
    "2026-08-12 11:00:30 INFO whisper_dictation.engine.local: Detected language: en (conf 0.95) [1 segments]",
    "2026-08-12 11:00:30 INFO whisper_dictation.daemon: Buffered transcription ready: batches=3, prefetched=2, compute=1.400s, release_wait=0.320s, fallback=False",
    "2026-08-12 11:00:30 INFO whisper_dictation.daemon: Paste shortcut sent: 300 chars",
});
var bufferedLatest = bufferedPerformance.Latest;
Check("buffered diagnostics report actual release wait once",
    bufferedPerformance.Count == 1 &&
    bufferedLatest is { } bufferedSample &&
    bufferedSample.TotalSeconds == 0.320 &&
    bufferedSample.ComputeSeconds == 1.400 &&
    bufferedSample.BufferedBatches == 3);
Check("buffered diagnostics preserve aggregate safety signals",
    bufferedLatest is { } bufferedSafety &&
    bufferedSafety.RetrySeconds == 0.200 &&
    bufferedSafety.Segments == 5 &&
    !bufferedSafety.UsedFullFallback);

Check("update tags parse stable semantic versions",
    VoicePromptTray.UpdateChecker.ParseVersionTag("v1.6.0") == new Version(1, 6, 0) &&
    VoicePromptTray.UpdateChecker.ParseVersionTag(" V2.0.3 ") == new Version(2, 0, 3));
Check("update tags reject prereleases and malformed values",
    VoicePromptTray.UpdateChecker.ParseVersionTag("v1.6.0-beta.1") is null &&
    VoicePromptTray.UpdateChecker.ParseVersionTag("latest") is null);
var previewBeta1 = VoicePromptTray.UpdateChecker.ParseReleaseTag("v1.7.0-beta.1", allowPrerelease: true);
var previewBeta2 = VoicePromptTray.UpdateChecker.ParseReleaseTag("v1.7.0-beta.2", allowPrerelease: true);
var previewStable = VoicePromptTray.UpdateChecker.ParseReleaseTag("v1.7.0", allowPrerelease: true);
Check("preview tags follow semantic precedence",
    previewBeta1 is not null && previewBeta2 is not null && previewStable is not null &&
    previewBeta2.CompareTo(previewBeta1) > 0 && previewStable.CompareTo(previewBeta2) > 0);
Check("preview tags reject malformed identifiers",
    VoicePromptTray.UpdateChecker.ParseReleaseTag("v1.7.0-beta..1", allowPrerelease: true) is null &&
    VoicePromptTray.UpdateChecker.ParseReleaseTag("v1.7.0-beta_1", allowPrerelease: true) is null);

bool safeUpdateRequest = false;
var availableClient = new HttpClient(new StubHttpMessageHandler(request =>
{
    safeUpdateRequest =
        request.RequestUri?.ToString() == VoicePromptTray.UpdateChecker.LatestReleaseEndpoint &&
        request.Headers.Authorization is null &&
        request.Headers.UserAgent.ToString() == "VoicePrompt/1.5.1" &&
        request.Headers.Accept.Any(value => value.MediaType == "application/vnd.github+json");
    return new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(BuildReleaseJson("1.6.0")),
    };
}));
var availableUpdate = await new VoicePromptTray.UpdateChecker(availableClient)
    .CheckAsync("1.5.1", VoicePromptTray.UpdateChannel.Stable);
Check("update checker reports newer official stable release",
    safeUpdateRequest &&
    availableUpdate.State == VoicePromptTray.UpdateState.Available &&
    availableUpdate.LatestVersion?.Number == new Version(1, 6, 0) &&
    availableUpdate.ReleaseUrl == "https://github.com/seNkoKG/VoicePrompt/releases/tag/v1.6.0" &&
    availableUpdate.Package?.Archive.Name == "VoicePrompt-v1.6.0-windows-x64.zip" &&
    availableUpdate.Package.Checksums.DownloadUrl.Scheme == Uri.UriSchemeHttps);

bool safePreviewRequest = false;
var previewClient = new HttpClient(new StubHttpMessageHandler(request =>
{
    safePreviewRequest =
        request.RequestUri?.ToString() == VoicePromptTray.UpdateChecker.PreviewReleaseEndpoint &&
        request.Headers.Authorization is null &&
        request.Headers.UserAgent.ToString() == "VoicePrompt/1.6.0";
    return new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("[" +
            BuildReleaseJson("9.0.0-alpha.1", draft: true, prerelease: true) + "," +
            BuildReleaseJson("1.7.0-beta.2", prerelease: true) + "," +
            BuildReleaseJson("1.6.1") + "]"),
    };
}));
var previewUpdate = await new VoicePromptTray.UpdateChecker(previewClient)
    .CheckAsync("1.6.0", VoicePromptTray.UpdateChannel.Preview);
Check("preview checker includes prereleases and excludes drafts",
    safePreviewRequest &&
    previewUpdate.State == VoicePromptTray.UpdateState.Available &&
    previewUpdate.LatestVersion?.Display == "1.7.0-beta.2" &&
    previewUpdate.ReleaseUrl.EndsWith("/v1.7.0-beta.2", StringComparison.Ordinal) &&
    previewUpdate.Package?.Archive.Name == "VoicePrompt-v1.7.0-beta.2-windows-x64.zip");

var redirectedAssetClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
{
    Content = new StringContent(BuildReleaseJson("1.6.0").Replace(
        "https://github.com/seNkoKG/VoicePrompt/releases/download/v1.6.0/VoicePrompt-v1.6.0-windows-x64.zip",
        "https://example.test/VoicePrompt-v1.6.0-windows-x64.zip",
        StringComparison.Ordinal)),
}));
var redirectedAssetUpdate = await new VoicePromptTray.UpdateChecker(redirectedAssetClient)
    .CheckAsync("1.5.1");
Check("update checker refuses unofficial release asset URLs",
    redirectedAssetUpdate.State == VoicePromptTray.UpdateState.Available &&
    redirectedAssetUpdate.Package is null);

var currentClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
{
    Content = new StringContent("""{"tag_name":"v1.5.1"}"""),
}));
var currentUpdate = await new VoicePromptTray.UpdateChecker(currentClient)
    .CheckAsync("1.5.1");
Check("update checker recognizes the current release",
    currentUpdate.State == VoicePromptTray.UpdateState.UpToDate);

var olderClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
{
    Content = new StringContent(BuildReleaseJson("1.20.2")),
}));
var olderUpdate = await new VoicePromptTray.UpdateChecker(olderClient)
    .CheckAsync("1.21.1");
Check("update checker never offers a downgrade",
    olderUpdate.State == VoicePromptTray.UpdateState.UpToDate &&
    olderUpdate.LatestVersion?.Display == "1.20.2");

var failedClient = new HttpClient(new StubHttpMessageHandler(_ =>
    new HttpResponseMessage(HttpStatusCode.Forbidden)));
var failedUpdate = await new VoicePromptTray.UpdateChecker(failedClient)
    .CheckAsync("1.5.1");
Check("update checker fails closed without throwing",
    failedUpdate.State == VoicePromptTray.UpdateState.Unavailable &&
    failedUpdate.ReleaseUrl == "");

var oversizedClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
{
    Content = new StringContent("{\"tag_name\":\"v1.6.0\",\"body\":\"" + new string('x', 600_000) + "\"}"),
}));
var oversizedUpdate = await new VoicePromptTray.UpdateChecker(oversizedClient)
    .CheckAsync("1.5.1");
Check("update checker rejects oversized responses",
    oversizedUpdate.State == VoicePromptTray.UpdateState.Unavailable);

const string stagedVersion = "9.8.7";
byte[] updateArchive = BuildUpdateArchive(stagedVersion);
string updateHash = Convert.ToHexStringLower(SHA256.HashData(updateArchive));
string archiveName = $"VoicePrompt-v{stagedVersion}-windows-x64.zip";
string checksumName = $"VoicePrompt-v{stagedVersion}-SHA256SUMS.txt";
byte[] updateChecksums = Encoding.UTF8.GetBytes($"{updateHash}  {archiveName}\n");
string checksumHash = Convert.ToHexStringLower(SHA256.HashData(updateChecksums));
string downloadRoot = $"https://github.com/seNkoKG/VoicePrompt/releases/download/v{stagedVersion}/";
var stagedRelease = new VoicePromptTray.UpdatePackage(
    new VoicePromptTray.ReleaseVersion(new Version(stagedVersion)),
    new VoicePromptTray.ReleaseAsset(
        archiveName,
        new Uri(downloadRoot + archiveName),
        updateArchive.Length,
        "sha256:" + updateHash),
    new VoicePromptTray.ReleaseAsset(
        checksumName,
        new Uri(downloadRoot + checksumName),
        updateChecksums.Length,
        "sha256:" + checksumHash));
bool safeDownloadRequests = true;
var installerClient = new HttpClient(new StubHttpMessageHandler(request =>
{
    safeDownloadRequests &= request.Method == HttpMethod.Get &&
        request.Headers.Authorization is null &&
        request.Headers.UserAgent.ToString() == "VoicePrompt-Updater/1.0" &&
        request.Headers.Accept.Any(value => value.MediaType == "application/octet-stream");
    byte[] content = request.RequestUri?.AbsolutePath.EndsWith(checksumName, StringComparison.Ordinal) == true
        ? updateChecksums
        : updateArchive;
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
}));
VoicePromptTray.StagedUpdate staged = await new VoicePromptTray.UpdateInstaller(installerClient)
    .PrepareAsync(stagedRelease);
Check("updater downloads, verifies, and stages a complete release",
    safeDownloadRequests &&
    File.Exists(staged.InstallerPath) &&
    File.ReadAllText(Path.Combine(Path.GetDirectoryName(staged.InstallerPath)!, "version.txt")).Trim() == stagedVersion);
Directory.Delete(staged.Directory, recursive: true);

byte[] badChecksums = Encoding.UTF8.GetBytes($"{new string('0', 64)}  {archiveName}\n");
var badChecksumRelease = stagedRelease with
{
    Checksums = stagedRelease.Checksums with { Size = badChecksums.Length, Digest = "" },
    Archive = stagedRelease.Archive with { Digest = "" },
};
var badChecksumClient = new HttpClient(new StubHttpMessageHandler(request =>
    new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(
            request.RequestUri?.AbsolutePath.EndsWith(checksumName, StringComparison.Ordinal) == true
                ? badChecksums
                : updateArchive),
    }));
bool checksumRejected = await RejectsInvalidDataAsync(() =>
    new VoicePromptTray.UpdateInstaller(badChecksumClient).PrepareAsync(badChecksumRelease));
Check("updater refuses a package with a mismatched checksum", checksumRejected);

byte[] unsafeArchive = BuildUpdateArchive(stagedVersion, "../outside.txt");
string unsafeHash = Convert.ToHexStringLower(SHA256.HashData(unsafeArchive));
byte[] unsafeChecksums = Encoding.UTF8.GetBytes($"{unsafeHash}  {archiveName}\n");
var unsafeRelease = stagedRelease with
{
    Archive = stagedRelease.Archive with { Size = unsafeArchive.Length, Digest = "sha256:" + unsafeHash },
    Checksums = stagedRelease.Checksums with { Size = unsafeChecksums.Length, Digest = "" },
};
var unsafeClient = new HttpClient(new StubHttpMessageHandler(request =>
    new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(
            request.RequestUri?.AbsolutePath.EndsWith(checksumName, StringComparison.Ordinal) == true
                ? unsafeChecksums
                : unsafeArchive),
    }));
bool unsafeArchiveRejected = await RejectsInvalidDataAsync(() =>
    new VoicePromptTray.UpdateInstaller(unsafeClient).PrepareAsync(unsafeRelease));
Check("updater blocks ZIP path traversal", unsafeArchiveRejected);

Check("checksum parser rejects duplicate package entries",
    ThrowsInvalidData(() => VoicePromptTray.UpdateInstaller.ReadChecksum(
        $"{updateHash}  {archiveName}\n{updateHash}  {archiveName}\n",
        archiveName)));

string cleanupRoot = Path.Combine(dir, "update-cleanup");
string cleanupCandidate = Path.Combine(cleanupRoot, "VoicePrompt-Update-v9.8.7-test");
string cleanupUnmarked = Path.Combine(cleanupRoot, "VoicePrompt-Update-v9.8.7-unmarked");
Directory.CreateDirectory(cleanupCandidate);
Directory.CreateDirectory(cleanupUnmarked);
string cleanupMarker = Path.Combine(cleanupCandidate, ".voiceprompt-update-stage");
File.WriteAllText(cleanupMarker, "1");
File.SetLastWriteTimeUtc(cleanupMarker, DateTime.UtcNow.AddHours(-1));
VoicePromptTray.UpdateInstaller.CleanupStagedUpdates(cleanupRoot, TimeSpan.FromMinutes(1));
Check("updater removes only marked completed staging directories",
    !Directory.Exists(cleanupCandidate) && Directory.Exists(cleanupUnmarked));

Check("recognition server validates and normalizes safe base URLs",
    VoicePromptTray.RecognitionServer.Validate("http://localhost:8000/", 60) is null &&
    VoicePromptTray.RecognitionServer.NormalizeUrl(" http://localhost:8000/ ") == "http://localhost:8000" &&
    VoicePromptTray.RecognitionServer.Validate("ftp://localhost:8000", 60) != null &&
    VoicePromptTray.RecognitionServer.Validate("https://user@example.test", 60) != null &&
    VoicePromptTray.RecognitionServer.Validate("https://example.test?token=secret", 60) != null &&
    VoicePromptTray.RecognitionServer.Validate("https://example.test", 4) != null);
Check("recognition server privacy guidance distinguishes transport",
    VoicePromptTray.RecognitionServer.IsLoopback("http://127.0.0.1:8000") &&
    VoicePromptTray.RecognitionServer.IsLoopback("http://[::1]:8000") &&
    VoicePromptTray.RecognitionServer.PrivacyMessage("http://localhost:8000").Contains("stays on this PC") &&
    VoicePromptTray.RecognitionServer.PrivacyMessage("https://speech.example.test").Contains("over HTTPS") &&
    VoicePromptTray.RecognitionServer.PrivacyMessage("http://speech.example.test").StartsWith("Warning ·"));

bool safeRecognitionProbe = false;
var recognitionProbe = await VoicePromptTray.RecognitionServer.ProbeAsync(
    "http://localhost:8000/",
    default,
    new StubHttpMessageHandler(request =>
    {
        safeRecognitionProbe = request.Method == HttpMethod.Get &&
            request.RequestUri?.ToString() == "http://localhost:8000/health" &&
            request.Headers.Authorization is null && request.Content is null;
        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }));
Check("recognition server health probe sends no audio or credentials",
    safeRecognitionProbe && recognitionProbe.Success && recognitionProbe.Message.Contains("204"));

var failedRecognitionProbe = await VoicePromptTray.RecognitionServer.ProbeAsync(
    "https://speech.example.test",
    default,
    new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
Check("recognition server health probe reports failure without throwing",
    !failedRecognitionProbe.Success && failedRecognitionProbe.Message.Contains("503"));

string? localUpdatePackage = Environment.GetEnvironmentVariable("VOICEPROMPT_LOCAL_UPDATE_PACKAGE");
if (!string.IsNullOrWhiteSpace(localUpdatePackage))
{
    Match localName = Regex.Match(
        Path.GetFileName(localUpdatePackage),
        "^VoicePrompt-v(?<version>.+)-windows-x64\\.zip$",
        RegexOptions.CultureInvariant);
    VoicePromptTray.ReleaseVersion? localVersion = VoicePromptTray.UpdateChecker.ParseReleaseTag(
        localName.Success ? localName.Groups["version"].Value : null,
        allowPrerelease: true);
    string localSums = localName.Success
        ? Path.Combine(
            Path.GetDirectoryName(localUpdatePackage)!,
            $"VoicePrompt-v{localName.Groups["version"].Value}-SHA256SUMS.txt")
        : "";
    Check("local release package name, version, and checksum asset are complete",
        localVersion is not null && File.Exists(localUpdatePackage) && File.Exists(localSums));
    if (localVersion is not null && File.Exists(localSums))
    {
        string expected = VoicePromptTray.UpdateInstaller.ReadChecksum(
            File.ReadAllText(localSums),
            Path.GetFileName(localUpdatePackage));
        using FileStream localStream = File.OpenRead(localUpdatePackage);
        string actual = Convert.ToHexStringLower(SHA256.HashData(localStream));
        string localExtract = Path.Combine(dir, "local-update-package");
        VoicePromptTray.UpdateInstaller.ExtractVerifiedArchive(
            localUpdatePackage,
            localExtract,
            localVersion);
        string productVersion = FileVersionInfo.GetVersionInfo(
            Path.Combine(localExtract, "VoicePromptTray.exe")).ProductVersion ?? "";
        Check("local release package passes checksum, safe extraction, and executable version gates",
            actual == expected && productVersion == localVersion.Display);
    }
}

if (Environment.GetEnvironmentVariable("VOICEPROMPT_REAL_UPDATE_SMOKE") == "1")
{
    var realChecker = new VoicePromptTray.UpdateChecker();
    VoicePromptTray.UpdateResult realRelease = await realChecker.CheckAsync("1.20.1");
    Check("real GitHub update metadata exposes the verified installer assets",
        realRelease.State == VoicePromptTray.UpdateState.Available &&
        realRelease.Package is not null);
    if (realRelease.Package is not null)
    {
        VoicePromptTray.StagedUpdate realStage = await new VoicePromptTray.UpdateInstaller()
            .PrepareAsync(realRelease.Package);
        Check("real GitHub release downloads, verifies, and extracts",
            File.Exists(realStage.InstallerPath) &&
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(realStage.InstallerPath)!, "version.txt")).Trim() ==
                realRelease.LatestVersion?.Display);
        if (Environment.GetEnvironmentVariable("VOICEPROMPT_REAL_UPDATE_INSTALL") == "1")
        {
            using Process installer = VoicePromptTray.UpdateInstaller.Launch(realStage);
            await installer.WaitForExitAsync();
            string installedVersion = File.ReadAllText(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "VoicePrompt",
                "version.txt")).Trim();
            Check("real verified installer completes and preserves the current-user install",
                installer.ExitCode == 0 && installedVersion == realRelease.LatestVersion?.Display);
        }
        Directory.Delete(realStage.Directory, recursive: true);
    }
}

static string BuildReleaseJson(
    string version,
    bool draft = false,
    bool prerelease = false,
    long archiveSize = 1024,
    long checksumSize = 128)
{
    string tag = "v" + version;
    string archive = $"VoicePrompt-{tag}-windows-x64.zip";
    string checksums = $"VoicePrompt-{tag}-SHA256SUMS.txt";
    string root = $"https://github.com/seNkoKG/VoicePrompt/releases/download/{tag}/";
    return JsonSerializer.Serialize(new
    {
        tag_name = tag,
        draft,
        prerelease,
        assets = new object[]
        {
            new
            {
                name = archive,
                state = "uploaded",
                size = archiveSize,
                digest = "sha256:" + new string('a', 64),
                browser_download_url = root + archive,
            },
            new
            {
                name = checksums,
                state = "uploaded",
                size = checksumSize,
                digest = "sha256:" + new string('b', 64),
                browser_download_url = root + checksums,
            },
        },
    });
}

static byte[] BuildUpdateArchive(string version, string? extraEntry = null)
{
    string[] files =
    {
        "VoicePromptTray.exe", "install.ps1", "version.txt", "requirements.txt", "run_daemon.pyw",
        "scripts/apply_patches.ps1", "scripts/shortcut_manager.ps1", "scripts/runtime_meter.py",
        "scripts/ai_rewriter.py", "scripts/transcript_history.py", "scripts/text_corrections.py",
        "scripts/slang_retry.py", "scripts/decoding_options.py", "scripts/buffered_transcription.py",
        "scripts/output_mode.py", "scripts/app_profiles.py", "scripts/text_snippets.py",
        "scripts/voice_commands.py", "scripts/windows_hotkey.py",
    };
    using var buffer = new MemoryStream();
    using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (string file in files.Append(extraEntry).Where(value => value is not null)!)
        {
            ZipArchiveEntry entry = archive.CreateEntry(file!, CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(file == "version.txt" ? version : "test payload");
        }
    }
    return buffer.ToArray();
}

static async Task<bool> RejectsInvalidDataAsync(Func<Task> action)
{
    try
    {
        await action();
        return false;
    }
    catch (InvalidDataException)
    {
        return true;
    }
}

static bool ThrowsInvalidData(Action action)
{
    try
    {
        action();
        return false;
    }
    catch (InvalidDataException)
    {
        return true;
    }
}

Directory.Delete(dir, true);
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURES");
return failures;

internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
}

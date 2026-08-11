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
cfg.Set("voiceprompt", "base_prompt", "V kodi pišem");
cfg.Save();

string after = File.ReadAllText(path);
Console.WriteLine("--- result ---");
Console.WriteLine(after);
Console.WriteLine("---------------");

Check("binding updated", Regex.IsMatch(after, @"binding = \""ctrl\+shift\+f1\"""));
Check("hybrid slang language saved", Regex.IsMatch(after, @"language = \""sl-slang\"""));
Check("threshold updated", Regex.IsMatch(after, @"threshold = 0\.55$", RegexOptions.Multiline));
Check("silence updated", Regex.IsMatch(after, @"silence_ms = 300"));
Check("commented key uncommented", Regex.IsMatch(after, @"^temperature = 0\.2$", RegexOptions.Multiline));
Check("hotwords added + escaped", Regex.IsMatch(after, @"hotwords = \""python, null\"""));
Check("inline comment preserved", after.Contains("WhisperLiveKit server URL"));
Check("VoicePrompt profile section added", new VoicePromptTray.ConfigManager(path).GetBool("voiceprompt", "slovenian_slang") == true);
Check("file still parses after re-read", new VoicePromptTray.ConfigManager(path).GetInt("vad", "silence_ms") == 300);

string savedOnce = File.ReadAllText(path);
new VoicePromptTray.ConfigManager(path).Save();
Check("repeated save is stable", File.ReadAllText(path) == savedOnce);

string newPath = Path.Combine(dir, "new", "config.toml");
var defaults = new VoicePromptTray.ConfigManager(newPath);
Check("new config is written immediately", File.Exists(newPath) && File.ReadAllText(newPath).Contains("[hotkey]"));
Check("new config has working defaults", defaults.GetString("hotkey", "binding") == "f1");

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

Directory.Delete(dir, true);
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURES");
return failures;

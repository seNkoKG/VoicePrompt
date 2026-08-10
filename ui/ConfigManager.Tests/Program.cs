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
cfg.Set("vad", "threshold", 0.55);
cfg.Set("vad", "silence_ms", 300);
cfg.Set("server", "temperature", 0.2);
cfg.Set("server", "hotwords", "python, null");
cfg.Set("audio", "sample_rate", 44100);
cfg.Set("engine", "device", "cuda");
cfg.Set("audio", "device", "");
cfg.Save();

string after = File.ReadAllText(path);
Console.WriteLine("--- result ---");
Console.WriteLine(after);
Console.WriteLine("---------------");

Check("binding updated", Regex.IsMatch(after, @"binding = \""ctrl\+shift\+f1\"""));
Check("threshold updated", Regex.IsMatch(after, @"threshold = 0\.55$", RegexOptions.Multiline));
Check("silence updated", Regex.IsMatch(after, @"silence_ms = 300"));
Check("commented key uncommented", Regex.IsMatch(after, @"^temperature = 0\.2$", RegexOptions.Multiline));
Check("hotwords added + escaped", Regex.IsMatch(after, @"hotwords = \""python, null\"""));
Check("inline comment preserved", after.Contains("WhisperLiveKit server URL"));
Check("file still parses after re-read", new VoicePromptTray.ConfigManager(path).GetInt("vad", "silence_ms") == 300);

Directory.Delete(dir, true);
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURES");
return failures;

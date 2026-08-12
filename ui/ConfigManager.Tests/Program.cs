using System.Net;
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
Check("file still parses after re-read", new VoicePromptTray.ConfigManager(path).GetInt("vad", "silence_ms") == 300);

string savedOnce = File.ReadAllText(path);
new VoicePromptTray.ConfigManager(path).Save();
Check("repeated save is stable", File.ReadAllText(path) == savedOnce);

string newPath = Path.Combine(dir, "new", "config.toml");
var defaults = new VoicePromptTray.ConfigManager(newPath);
Check("new config is written immediately", File.Exists(newPath) && File.ReadAllText(newPath).Contains("[hotkey]"));
Check("new config has working defaults", defaults.GetString("hotkey", "binding") == "f1");
Check("new config enables lossless long-recording prefetch", defaults.GetBool("voiceprompt", "buffered_transcription") == true);
Check("new config defaults to automatic paste", defaults.GetString("voiceprompt", "output_mode") == "paste");

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
history.Delete("empty");
history.Delete("one");
Check("history deletes selected transcript", history.Load().Count == 0 && history.Latest() is null);

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
        Content = new StringContent("""{"tag_name":"v1.6.0"}"""),
    };
}));
var availableUpdate = await new VoicePromptTray.UpdateChecker(availableClient)
    .CheckAsync(new Version(1, 5, 1, 0));
Check("update checker reports newer official stable release",
    safeUpdateRequest &&
    availableUpdate.State == VoicePromptTray.UpdateState.Available &&
    availableUpdate.LatestVersion == new Version(1, 6, 0) &&
    availableUpdate.ReleaseUrl == "https://github.com/seNkoKG/VoicePrompt/releases/tag/v1.6.0");

var currentClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
{
    Content = new StringContent("""{"tag_name":"v1.5.1"}"""),
}));
var currentUpdate = await new VoicePromptTray.UpdateChecker(currentClient)
    .CheckAsync(new Version(1, 5, 1));
Check("update checker recognizes the current release",
    currentUpdate.State == VoicePromptTray.UpdateState.UpToDate);

var failedClient = new HttpClient(new StubHttpMessageHandler(_ =>
    new HttpResponseMessage(HttpStatusCode.Forbidden)));
var failedUpdate = await new VoicePromptTray.UpdateChecker(failedClient)
    .CheckAsync(new Version(1, 5, 1));
Check("update checker fails closed without throwing",
    failedUpdate.State == VoicePromptTray.UpdateState.Unavailable &&
    failedUpdate.ReleaseUrl == "");

var oversizedClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
{
    Content = new StringContent("{\"tag_name\":\"v1.6.0\",\"body\":\"" + new string('x', 600_000) + "\"}"),
}));
var oversizedUpdate = await new VoicePromptTray.UpdateChecker(oversizedClient)
    .CheckAsync(new Version(1, 5, 1));
Check("update checker rejects oversized responses",
    oversizedUpdate.State == VoicePromptTray.UpdateState.Unavailable);

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

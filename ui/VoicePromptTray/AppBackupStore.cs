using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VoicePromptTray;

internal sealed record BackupDictationSettings
{
    [JsonPropertyName("hotkey")]
    public string Hotkey { get; init; } = "f1";
    [JsonPropertyName("activation")]
    public string Activation { get; init; } = "hold";
    [JsonPropertyName("output_mode")]
    public string OutputMode { get; init; } = "paste";
    [JsonPropertyName("voice_commands")]
    public bool VoiceCommands { get; init; }
    [JsonPropertyName("language")]
    public string Language { get; init; } = "";
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = "";
    [JsonPropertyName("hotwords")]
    public string Hotwords { get; init; } = "";
}

internal sealed record BackupRecognitionSettings
{
    [JsonPropertyName("engine_type")]
    public string EngineType { get; init; } = "local";
    [JsonPropertyName("server_url")]
    public string ServerUrl { get; init; } = "http://localhost:8000";
    [JsonPropertyName("server_timeout_seconds")]
    public int ServerTimeoutSeconds { get; init; } = 60;
    [JsonPropertyName("model")]
    public string Model { get; init; } = "Systran/faster-whisper-large-v3";
    [JsonPropertyName("processor")]
    public string Processor { get; init; } = "auto";
    [JsonPropertyName("compute_type")]
    public string ComputeType { get; init; } = "float16";
    [JsonPropertyName("temperature")]
    public double Temperature { get; init; }
    [JsonPropertyName("buffered_transcription")]
    public bool BufferedTranscription { get; init; } = true;
}

internal sealed record BackupAudioSettings
{
    [JsonPropertyName("sample_rate")]
    public int SampleRate { get; init; } = 16000;
    [JsonPropertyName("threshold")]
    public double Threshold { get; init; } = 0.6;
    [JsonPropertyName("silence_ms")]
    public int SilenceMs { get; init; } = 250;
    [JsonPropertyName("minimum_speech_ms")]
    public int MinimumSpeechMs { get; init; } = 250;
    [JsonPropertyName("maximum_speech_seconds")]
    public double MaximumSpeechSeconds { get; init; } = 180;
}

internal sealed record BackupWritingSettings
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "off";
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; } = "http://127.0.0.1:11434/v1/chat/completions";
    [JsonPropertyName("model")]
    public string Model { get; init; } = "qwen2.5:3b";
    [JsonPropertyName("timeout_ms")]
    public int TimeoutMs { get; init; } = 900;
}

internal sealed record BackupRecoverySettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;
    [JsonPropertyName("limit")]
    public int Limit { get; init; } = 20;
}

internal sealed record VoicePromptBackupDocument
{
    [JsonPropertyName("format")]
    public string Format { get; init; } = AppBackupStore.Format;
    [JsonPropertyName("version")]
    public int Version { get; init; } = AppBackupStore.Version;
    [JsonPropertyName("dictation")]
    public BackupDictationSettings Dictation { get; init; } = new();
    [JsonPropertyName("recognition")]
    public BackupRecognitionSettings Recognition { get; init; } = new();
    [JsonPropertyName("audio")]
    public BackupAudioSettings Audio { get; init; } = new();
    [JsonPropertyName("writing")]
    public BackupWritingSettings Writing { get; init; } = new();
    [JsonPropertyName("recovery")]
    public BackupRecoverySettings Recovery { get; init; } = new();
    [JsonPropertyName("corrections")]
    public List<CorrectionEntry> Corrections { get; init; } = [];
    [JsonPropertyName("snippets")]
    public List<TextSnippetEntry> Snippets { get; init; } = [];
    [JsonPropertyName("app_profiles")]
    public List<AppProfileEntry> AppProfiles { get; init; } = [];
}

internal static class AppBackupStore
{
    internal const string Format = "voiceprompt-settings-backup";
    internal const int Version = 1;
    private const int MaxFileBytes = 512 * 1024;
    private static readonly HashSet<string> Modifiers =
        new(["alt", "ctrl", "control", "shift", "cmd", "super", "meta"], StringComparer.Ordinal);
    private static readonly HashSet<string> NamedKeys =
        new(["space", "tab", "enter", "esc", "backspace", "insert", "delete", "home", "end",
            "page_up", "page_down", "up", "down", "left", "right", "print_screen", "pause",
            "caps_lock", "scroll_lock", "num_lock", "menu"], StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Serialize(VoicePromptBackupDocument document) =>
        JsonSerializer.Serialize(Validate(document), JsonOptions);

    public static VoicePromptBackupDocument Deserialize(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxFileBytes)
            throw new InvalidDataException("VoicePrompt backups must be 512 KB or smaller.");
        try
        {
            return Validate(JsonSerializer.Deserialize<VoicePromptBackupDocument>(json)
                ?? throw new InvalidDataException("The VoicePrompt backup is empty."));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The VoicePrompt backup is not valid JSON.", ex);
        }
    }

    public static void Save(string path, VoicePromptBackupDocument document)
    {
        string json = Serialize(document);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    public static VoicePromptBackupDocument Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("The VoicePrompt backup was not found.", path);
        if (info.Length > MaxFileBytes)
            throw new InvalidDataException("VoicePrompt backups must be 512 KB or smaller.");
        return Deserialize(File.ReadAllText(path, Encoding.UTF8));
    }

    private static VoicePromptBackupDocument Validate(VoicePromptBackupDocument document)
    {
        if (!string.Equals(document.Format, Format, StringComparison.Ordinal) || document.Version != Version)
            throw new InvalidDataException("This is not a supported VoicePrompt settings backup.");

        BackupDictationSettings dictation = document.Dictation
            ?? throw new InvalidDataException("The backup is missing dictation settings.");
        BackupRecognitionSettings recognition = document.Recognition
            ?? throw new InvalidDataException("The backup is missing recognition settings.");
        BackupAudioSettings audio = document.Audio
            ?? throw new InvalidDataException("The backup is missing audio settings.");
        BackupWritingSettings writing = document.Writing
            ?? throw new InvalidDataException("The backup is missing writing settings.");
        BackupRecoverySettings recovery = document.Recovery
            ?? throw new InvalidDataException("The backup is missing recovery settings.");

        string hotkey = dictation.Hotkey?.Trim().ToLowerInvariant() ?? "";
        if (!ValidHotkey(hotkey))
            throw new InvalidDataException("The backup contains an invalid global hotkey.");
        string activation = dictation.Activation?.Trim().ToLowerInvariant() ?? "";
        if (activation is not ("hold" or "toggle"))
            throw new InvalidDataException("The backup contains an invalid activation mode.");
        string outputMode = dictation.OutputMode?.Trim().ToLowerInvariant() ?? "";
        if (outputMode is not ("paste" or "clipboard"))
            throw new InvalidDataException("The backup contains an invalid output mode.");

        string correctionsText = string.Join(Environment.NewLine, (document.Corrections ?? []).Select(entry =>
            $"{entry?.Heard ?? ""} => {entry?.Replacement ?? ""}"));
        LanguageProfileDocument profile = LanguageProfileStore.Create(
            dictation.Language ?? "",
            dictation.Prompt ?? "",
            dictation.Hotwords ?? "",
            correctionsText);
        IReadOnlyList<TextSnippetEntry> snippets = TextSnippetStore.Parse(
            TextSnippetStore.Format(document.Snippets ?? []));
        IReadOnlyList<AppProfileEntry> appProfiles = AppProfileStore.Parse(
            AppProfileStore.Format(document.AppProfiles ?? []));

        string recognitionModel = recognition.Model?.Trim() ?? "";
        string engineType = recognition.EngineType?.Trim().ToLowerInvariant() ?? "";
        string recognitionServerUrl = recognition.ServerUrl?.Trim() ?? "";
        if (engineType is not ("local" or "server") ||
            RecognitionServer.Validate(recognitionServerUrl, recognition.ServerTimeoutSeconds) != null)
            throw new InvalidDataException("The backup contains invalid recognition-engine settings.");
        if (recognitionModel.Length is 0 or > 200)
            throw new InvalidDataException("The backup contains an invalid recognition model.");
        string processor = recognition.Processor?.Trim().ToLowerInvariant() ?? "";
        if (processor is not ("auto" or "cuda" or "cpu"))
            throw new InvalidDataException("The backup contains an invalid processor.");
        string computeType = recognition.ComputeType?.Trim().ToLowerInvariant() ?? "";
        if (computeType is not ("auto" or "float16" or "int8"))
            throw new InvalidDataException("The backup contains an invalid recognition precision.");
        if (recognition.Temperature is < 0 or > 1)
            throw new InvalidDataException("The backup contains an invalid recognition temperature.");

        if (audio.SampleRate is not (8000 or 16000 or 22050 or 44100 or 48000) ||
            audio.Threshold is < 0 or > 1 ||
            audio.SilenceMs is < 50 or > 5000 ||
            audio.MinimumSpeechMs is < 50 or > 5000 ||
            audio.MaximumSpeechSeconds is < 1 or > 600)
            throw new InvalidDataException("The backup contains invalid audio detection settings.");

        string writingMode = writing.Mode?.Trim().ToLowerInvariant() ?? "";
        string endpoint = writing.Endpoint?.Trim() ?? "";
        string writingModel = writing.Model?.Trim() ?? "";
        if (endpoint.Length is 0 or > 2_048 ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? endpointUri) ||
            endpointUri.Scheme is not ("http" or "https") ||
            endpointUri.UserInfo.Length > 0 || endpointUri.Query.Length > 0 || endpointUri.Fragment.Length > 0 ||
            writingModel.Length is 0 or > 200 ||
            writing.TimeoutMs is < 400 or > 3000 ||
            writingMode is not ("off" or "clean" or "grammar" or "prompt"))
            throw new InvalidDataException("The backup contains invalid writing-provider settings.");

        if (recovery.Limit is < 5 or > 100)
            throw new InvalidDataException("The backup contains an invalid recovery limit.");

        return document with
        {
            Dictation = dictation with
            {
                Hotkey = hotkey,
                Activation = activation,
                OutputMode = outputMode,
                Language = profile.Language,
                Prompt = profile.Prompt,
                Hotwords = profile.Hotwords,
            },
            Recognition = recognition with
            {
                EngineType = engineType,
                ServerUrl = RecognitionServer.NormalizeUrl(recognitionServerUrl),
                Model = recognitionModel,
                Processor = processor,
                ComputeType = computeType,
            },
            Writing = writing with
            {
                Mode = writingMode,
                Endpoint = endpoint,
                Model = writingModel,
            },
            Corrections = profile.Corrections,
            Snippets = snippets.ToList(),
            AppProfiles = appProfiles.ToList(),
        };
    }

    private static bool ValidHotkey(string value)
    {
        string[] parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[..^1].Any(part => !Modifiers.Contains(part)) ||
            parts[..^1].Distinct(StringComparer.Ordinal).Count() != parts.Length - 1)
            return false;
        string key = parts[^1];
        return (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) ||
            NamedKeys.Contains(key) || Regex.IsMatch(key, @"^f(?:[1-9]|1[0-9]|2[0-4])$");
    }
}

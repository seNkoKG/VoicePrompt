using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoicePromptTray;

internal sealed record LanguageProfileDocument
{
    [JsonPropertyName("format")]
    public string Format { get; init; } = LanguageProfileStore.Format;

    [JsonPropertyName("version")]
    public int Version { get; init; } = LanguageProfileStore.Version;

    [JsonPropertyName("language")]
    public string Language { get; init; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = "";

    [JsonPropertyName("hotwords")]
    public string Hotwords { get; init; } = "";

    [JsonPropertyName("corrections")]
    public List<CorrectionEntry> Corrections { get; init; } = [];

    [JsonIgnore]
    public string CorrectionsText => string.Join(
        Environment.NewLine,
        Corrections.Select(entry => $"{entry.Heard} => {entry.Replacement}"));
}

internal static class LanguageProfileStore
{
    internal const string Format = "voiceprompt-language-profile";
    internal const int Version = 1;
    private const int MaxFileBytes = 128 * 1024;
    private const int MaxTextLength = 8_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static LanguageProfileDocument Create(
        string language,
        string prompt,
        string hotwords,
        string correctionsText) =>
        Validate(new LanguageProfileDocument
        {
            Language = NormalizeLanguage(language),
            Prompt = prompt.Trim(),
            Hotwords = hotwords.Trim(),
            Corrections = PersonalDictionaryStore.Parse(correctionsText).ToList(),
        });

    public static string Serialize(LanguageProfileDocument profile) =>
        JsonSerializer.Serialize(Validate(profile), JsonOptions);

    public static LanguageProfileDocument Deserialize(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxFileBytes)
            throw new InvalidDataException("Language profiles must be 128 KB or smaller.");
        try
        {
            return Validate(JsonSerializer.Deserialize<LanguageProfileDocument>(json)
                ?? throw new InvalidDataException("The language profile is empty."));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The language profile is not valid JSON.", ex);
        }
    }

    public static void Save(string path, LanguageProfileDocument profile)
    {
        string json = Serialize(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }

    public static LanguageProfileDocument Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("The language profile was not found.", path);
        if (info.Length > MaxFileBytes)
            throw new InvalidDataException("Language profiles must be 128 KB or smaller.");
        return Deserialize(File.ReadAllText(path, Encoding.UTF8));
    }

    private static LanguageProfileDocument Validate(LanguageProfileDocument profile)
    {
        if (!string.Equals(profile.Format, Format, StringComparison.Ordinal) || profile.Version != Version)
            throw new InvalidDataException("This is not a supported VoicePrompt language profile.");

        string language = NormalizeLanguage(profile.Language);
        string prompt = profile.Prompt ?? "";
        string hotwords = profile.Hotwords ?? "";
        List<CorrectionEntry> corrections = profile.Corrections ?? [];
        if (prompt.Length > MaxTextLength || hotwords.Length > MaxTextLength)
            throw new InvalidDataException("Profile prompt and hotwords must each be 8,000 characters or shorter.");

        string correctionsText = string.Join(
            Environment.NewLine,
            corrections.Select(entry => $"{entry?.Heard ?? ""} => {entry?.Replacement ?? ""}"));
        IReadOnlyList<CorrectionEntry> validatedCorrections = PersonalDictionaryStore.Parse(correctionsText);
        return profile with
        {
            Language = language,
            Prompt = prompt,
            Hotwords = hotwords,
            Corrections = validatedCorrections.ToList(),
        };
    }

    private static string NormalizeLanguage(string? language)
    {
        string value = language?.Trim().ToLowerInvariant() ?? "";
        if (value == "auto")
            value = "";
        if (value.Length == 0 || value == "sl-slang" || LanguageCatalog.IsSupported(value))
            return value;
        throw new InvalidDataException($"Unsupported Whisper language code: {value}.");
    }
}

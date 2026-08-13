using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoicePromptTray;

internal sealed record AppProfileEntry(
    [property: JsonPropertyName("executable")] string Executable,
    [property: JsonPropertyName("writingMode")] string WritingMode,
    [property: JsonPropertyName("outputMode")] string OutputMode);

internal sealed class AppProfileStore
{
    private const int MaximumFileBytes = 128 * 1024;
    private static readonly HashSet<string> WritingModes =
        new(["inherit", "off", "clean", "grammar", "prompt"], StringComparer.Ordinal);
    private static readonly HashSet<string> OutputModes =
        new(["inherit", "paste", "clipboard"], StringComparer.Ordinal);
    private readonly string _path;

    public AppProfileStore(string path) => _path = path;

    public string LoadText()
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                BoundedLocalFile.ReadUtf8(_path, MaximumFileBytes));
            if (!document.RootElement.TryGetProperty("version", out JsonElement version) || version.GetInt32() != 1 ||
                !document.RootElement.TryGetProperty("items", out JsonElement items))
                return "";
            var profiles = items.Deserialize<List<AppProfileEntry>>() ?? [];
            return Format(Validate(profiles));
        }
        catch
        {
            return "";
        }
    }

    public void SaveText(string text)
    {
        IReadOnlyList<AppProfileEntry> profiles = Parse(text);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + ".tmp";
        string json = JsonSerializer.Serialize(new { version = 1, items = profiles });
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, _path, true);
    }

    public static IReadOnlyList<AppProfileEntry> Parse(string text)
    {
        var profiles = new List<AppProfileEntry>();
        foreach (string rawLine in text.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            int separator = line.IndexOf("=>", StringComparison.Ordinal);
            if (separator <= 0 || separator >= line.Length - 2)
                throw new InvalidDataException($"Invalid application profile: {line}.");
            string executable = line[..separator].Trim();
            string[] settings = line[(separator + 2)..]
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (settings.Length != 2)
                throw new InvalidDataException("Use executable.exe => writing mode, output mode.");
            string writingMode = NormalizeWritingMode(settings[0]);
            string outputMode = settings[1].ToLowerInvariant();
            profiles.Add(new AppProfileEntry(executable, writingMode, outputMode));
        }
        return Validate(profiles);
    }

    public static string Format(IEnumerable<AppProfileEntry> profiles) => string.Join(
        Environment.NewLine,
        profiles.Select(profile =>
            $"{profile?.Executable ?? ""} => {profile?.WritingMode ?? ""}, {profile?.OutputMode ?? ""}"));

    public static bool UsesAi(string text)
    {
        try
        {
            return Parse(text).Any(profile => profile.WritingMode is "clean" or "grammar" or "prompt");
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<AppProfileEntry> Validate(IEnumerable<AppProfileEntry> values)
    {
        var profiles = new List<AppProfileEntry>();
        foreach (AppProfileEntry? value in values)
        {
            string executable = value?.Executable?.Trim() ?? "";
            string writingMode = NormalizeWritingMode(value?.WritingMode ?? "");
            string outputMode = value?.OutputMode?.Trim().ToLowerInvariant() ?? "";
            if (executable.Length is 0 or > 120 ||
                !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                executable != Path.GetFileName(executable) ||
                executable.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                executable.Any(character => char.IsControl(character)))
                throw new InvalidDataException($"Invalid executable name: {executable}.");
            if (!WritingModes.Contains(writingMode))
                throw new InvalidDataException($"Invalid writing mode for {executable}.");
            if (!OutputModes.Contains(outputMode))
                throw new InvalidDataException($"Invalid output mode for {executable}.");
            if (profiles.Any(profile => profile.Executable.Equals(executable, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Duplicate application profile: {executable}.");
            profiles.Add(new AppProfileEntry(executable, writingMode, outputMode));
        }
        if (profiles.Count > 50)
            throw new InvalidDataException("Application profiles are limited to 50 entries.");
        return profiles;
    }

    private static string NormalizeWritingMode(string value) => value.Trim().ToLowerInvariant() switch
    {
        "verbatim" => "off",
        var normalized => normalized,
    };
}

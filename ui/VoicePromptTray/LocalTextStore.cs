using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoicePromptTray;

internal sealed record TranscriptEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    [JsonPropertyName("originalText")]
    public string OriginalText { get; init; } = "";
}

internal sealed record HistorySettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("limit")]
    public int Limit { get; init; } = 20;
}

internal sealed class TranscriptHistoryStore
{
    private readonly string _historyPath;
    private readonly string _settingsPath;

    public TranscriptHistoryStore(string historyPath, string settingsPath)
    {
        _historyPath = historyPath;
        _settingsPath = settingsPath;
    }

    public HistorySettings LoadSettings()
    {
        try
        {
            var value = JsonSerializer.Deserialize<HistorySettings>(File.ReadAllText(_settingsPath));
            return value == null ? new() : value with { Limit = Math.Clamp(value.Limit, 5, 100) };
        }
        catch
        {
            return new();
        }
    }

    public void SaveSettings(bool enabled, int limit) =>
        WriteAtomic(_settingsPath, JsonSerializer.Serialize(new HistorySettings
        {
            Enabled = enabled,
            Limit = Math.Clamp(limit, 5, 100),
        }));

    public IReadOnlyList<TranscriptEntry> Load()
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_historyPath));
            if (!document.RootElement.TryGetProperty("items", out JsonElement items))
                return [];
            return items.Deserialize<List<TranscriptEntry>>() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Delete(string id)
    {
        var items = Load().Where(item => item.Id != id).ToList();
        WriteHistory(items);
    }

    public void Clear() => WriteHistory([]);

    private void WriteHistory(IReadOnlyList<TranscriptEntry> items) =>
        WriteAtomic(_historyPath, JsonSerializer.Serialize(new { version = 1, items }));

    private static void WriteAtomic(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}

internal sealed record CorrectionEntry(
    [property: JsonPropertyName("heard")] string Heard,
    [property: JsonPropertyName("replacement")] string Replacement);

internal sealed class PersonalDictionaryStore
{
    private readonly string _path;

    public PersonalDictionaryStore(string path) => _path = path;

    public string LoadText()
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_path));
            if (!document.RootElement.TryGetProperty("items", out JsonElement items))
                return "";
            var entries = items.Deserialize<List<CorrectionEntry>>() ?? [];
            return string.Join(Environment.NewLine, entries.Select(entry => $"{entry.Heard} => {entry.Replacement}"));
        }
        catch
        {
            return "";
        }
    }

    public void SaveText(string text)
    {
        var entries = Parse(text);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temp = _path + ".tmp";
        string json = JsonSerializer.Serialize(new { version = 1, items = entries });
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, _path, true);
    }

    public static IReadOnlyList<CorrectionEntry> Parse(string text)
    {
        var entries = new List<CorrectionEntry>();
        foreach (string rawLine in text.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            int separator = line.IndexOf("=>", StringComparison.Ordinal);
            if (separator <= 0 || separator >= line.Length - 2)
                throw new InvalidDataException($"Invalid correction: {line}. Use heard => replacement.");
            string heard = line[..separator].Trim();
            string replacement = line[(separator + 2)..].Trim();
            if (heard.Length == 0 || replacement.Length == 0)
                throw new InvalidDataException($"Invalid correction: {line}. Use heard => replacement.");
            if (heard.Length > 120 || replacement.Length > 120)
                throw new InvalidDataException("Corrections must be 120 characters or shorter.");
            if (entries.Any(entry => entry.Heard.Equals(heard, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Duplicate correction: {heard}.");
            entries.Add(new CorrectionEntry(heard, replacement));
        }
        if (entries.Count > 100)
            throw new InvalidDataException("Personal corrections are limited to 100 entries.");
        return entries;
    }
}

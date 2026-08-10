using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VoicePromptTray;

internal sealed class ConfigManager
{
    private const string DefaultToml = """
        [server]
        url = "http://localhost:8000"
        model = "Systran/faster-whisper-large-v3"
        language = ""
        timeout = 10
        prompt = ""

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
        channels = 1

        [engine]
        type = "local"
        compute_type = "float16"
        device = "cuda"

        [websocket]
        reconnect_attempts = 3
        reconnect_delay = 1.0
        """;

    private sealed class Entry
    {
        public required string Section;
        public required string Key;
        public required int Line;
        public required string RawValue;
        public string? InlineComment;
        public bool Commented;
        public string Indent = "";
    }

    private readonly string _path;
    private List<string> _lines = new();
    private string _eol = "\n";
    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, string> _sectionOrder = new();

    public ConfigManager(string path)
    {
        _path = path;
        Load();
    }

    public string ConfigPath => _path;

    public bool Exists { get; private set; }

    private void Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string raw;
        if (File.Exists(_path))
        {
            Exists = true;
            raw = File.ReadAllText(_path);
        }
        else
        {
            raw = DefaultToml;
            Save();
        }

        if (raw.Contains("\r\n"))
            _eol = "\r\n";
        _lines = raw.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        Parse();
    }

    private void Parse()
    {
        _entries.Clear();
        _sectionOrder.Clear();
        string section = "";
        for (int i = 0; i < _lines.Count; i++)
        {
            string line = _lines[i];
            var sm = Regex.Match(line, @"^\s*\[([^\]]+)\]");
            if (sm.Success)
            {
                section = sm.Groups[1].Value.Trim();
                _sectionOrder.TryAdd(section, string.Empty);
                continue;
            }

            if (section == "")
                continue;

            var kv = Regex.Match(line, @"^\s*(#?)\s*([a-z][a-z0-9_]*)\s*=\s*(.*)$");
            if (!kv.Success)
                continue;

            bool commented = kv.Groups[1].Value == "#";
            string key = kv.Groups[2].Value;
            string rest = kv.Groups[3].Value;
            string? comment = null;
            string value = rest;

            if (!commented)
            {
                int ci = IndexOfUnquoted(rest, '#');
                if (ci >= 0)
                {
                    comment = rest[ci..].Trim();
                    value = rest[..ci].TrimEnd();
                }
                value = value.Trim();
            }

            _entries.Add(new Entry
            {
                Section = section,
                Key = key,
                Line = i,
                RawValue = value,
                InlineComment = comment,
                Commented = commented,
                Indent = Regex.Match(line, @"^\s*").Value,
            });
            _sectionOrder[section] = string.Empty;
        }
    }

    private static int IndexOfUnquoted(string text, char target)
    {
        bool inQuote = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && inQuote)
            {
                i++;
                continue;
            }
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }
            if (c == target && !inQuote)
                return i;
        }
        return -1;
    }

    public object? Get(string section, string key)
    {
        var e = _entries.FirstOrDefault(x => x.Section == section && x.Key == key && !x.Commented);
        return e == null ? null : ParseValue(e.RawValue);
    }

    public string? GetString(string section, string key) => Get(section, key) as string;

    public int? GetInt(string section, string key)
    {
        var v = Get(section, key);
        return v is int i ? i : v is long l ? (int)l : v is double d ? (int)d : null;
    }

    public double? GetDouble(string section, string key)
    {
        var v = Get(section, key);
        return v is double d ? d : v is int i ? i : v is long l ? l : null;
    }

    public bool? GetBool(string section, string key) => Get(section, key) as bool?;

    private static object? ParseValue(string raw)
    {
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            return Unescape(raw[1..^1]);
        if (raw == "true")
            return true;
        if (raw == "false")
            return false;
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            return l;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;
        return raw;
    }

    private static string Unescape(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length)
            {
                sb.Append(c);
                continue;
            }
            char n = s[++i];
            sb.Append(n switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '\\' => '\\',
                '"' => '"',
                _ => n,
            });
        }
        return sb.ToString();
    }

    public void Set(string section, string key, object value)
    {
        var entry = _entries.FirstOrDefault(x => x.Section == section && x.Key == key);
        string formatted = FormatValue(value);

        if (entry != null)
        {
            string line = $"{entry.Indent}{key} = {formatted}";
            if (entry.InlineComment != null)
                line += "  " + entry.InlineComment;
            _lines[entry.Line] = line;
            entry.Commented = false;
            entry.RawValue = formatted;
            return;
        }

        string newLine = $"{key} = {formatted}";
        int insertAt = FindInsertPoint(section);
        _lines.Insert(insertAt, newLine);
        _sectionOrder[section] = string.Empty;
        Parse();
    }

    private int FindInsertPoint(string section)
    {
        int header = -1;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (Regex.IsMatch(_lines[i], $@"^\s*\[{Regex.Escape(section)}\]\s*$"))
            {
                header = i;
                break;
            }
        }
        if (header < 0)
        {
            _lines.Add("");
            _lines.Add($"[{section}]");
            return _lines.Count;
        }

        int insertAt = header + 1;
        while (insertAt < _lines.Count)
        {
            string l = _lines[insertAt];
            if (Regex.IsMatch(l, @"^\s*\["))
                break;
            if (l.Trim() != "")
                insertAt++;
            else
                break;
        }
        return insertAt;
    }

    private static string FormatValue(object value)
    {
        switch (value)
        {
            case string s:
                return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r") + "\"";
            case bool b:
                return b ? "true" : "false";
            case double d:
                return d.ToString("0.0#####", CultureInfo.InvariantCulture);
            case float f:
                return f.ToString("0.0#####", CultureInfo.InvariantCulture);
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture)!;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, string.Join(_eol, _lines) + _eol, new UTF8Encoding(false));
    }
}

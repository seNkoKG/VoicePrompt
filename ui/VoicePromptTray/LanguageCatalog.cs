namespace VoicePromptTray;

internal sealed record LanguageOption(string Name, string Code)
{
    public override string ToString() => $"{Name} ({Code})";
}

internal static class LanguageCatalog
{
    private static readonly LanguageOption[] Options =
    {
        new("Afrikaans", "af"), new("Albanian", "sq"), new("Amharic", "am"),
        new("Arabic", "ar"), new("Armenian", "hy"), new("Assamese", "as"),
        new("Azerbaijani", "az"), new("Bashkir", "ba"), new("Basque", "eu"),
        new("Belarusian", "be"), new("Bengali", "bn"), new("Bosnian", "bs"),
        new("Breton", "br"), new("Bulgarian", "bg"), new("Myanmar", "my"),
        new("Cantonese", "yue"), new("Catalan", "ca"), new("Chinese", "zh"),
        new("Croatian", "hr"), new("Czech", "cs"), new("Danish", "da"),
        new("Dutch", "nl"), new("English", "en"), new("Estonian", "et"),
        new("Faroese", "fo"), new("Finnish", "fi"), new("French", "fr"),
        new("Galician", "gl"), new("Georgian", "ka"), new("German", "de"),
        new("Greek", "el"), new("Gujarati", "gu"), new("Haitian Creole", "ht"),
        new("Hausa", "ha"), new("Hawaiian", "haw"), new("Hebrew", "he"),
        new("Hindi", "hi"), new("Hungarian", "hu"), new("Icelandic", "is"),
        new("Indonesian", "id"), new("Italian", "it"), new("Japanese", "ja"),
        new("Javanese", "jw"), new("Kannada", "kn"), new("Kazakh", "kk"),
        new("Khmer", "km"), new("Korean", "ko"), new("Lao", "lo"),
        new("Latin", "la"), new("Latvian", "lv"), new("Lingala", "ln"),
        new("Lithuanian", "lt"), new("Luxembourgish", "lb"), new("Macedonian", "mk"),
        new("Malagasy", "mg"), new("Malay", "ms"), new("Malayalam", "ml"),
        new("Maltese", "mt"), new("Maori", "mi"), new("Marathi", "mr"),
        new("Mongolian", "mn"), new("Nepali", "ne"), new("Norwegian", "no"),
        new("Nynorsk", "nn"), new("Occitan", "oc"), new("Pashto", "ps"),
        new("Persian", "fa"), new("Polish", "pl"), new("Portuguese", "pt"),
        new("Punjabi", "pa"), new("Romanian", "ro"), new("Russian", "ru"),
        new("Sanskrit", "sa"), new("Serbian", "sr"), new("Shona", "sn"),
        new("Sindhi", "sd"), new("Sinhala", "si"), new("Slovak", "sk"),
        new("Slovenian", "sl"), new("Somali", "so"), new("Spanish", "es"),
        new("Sundanese", "su"), new("Swahili", "sw"), new("Swedish", "sv"),
        new("Tagalog", "tl"), new("Tajik", "tg"), new("Tamil", "ta"),
        new("Tatar", "tt"), new("Telugu", "te"), new("Thai", "th"),
        new("Tibetan", "bo"), new("Turkish", "tr"), new("Turkmen", "tk"),
        new("Ukrainian", "uk"), new("Urdu", "ur"), new("Uzbek", "uz"),
        new("Vietnamese", "vi"), new("Welsh", "cy"), new("Yiddish", "yi"),
        new("Yoruba", "yo"),
    };

    public static IReadOnlyList<LanguageOption> All => Options;

    public static LanguageOption? Find(string? code) => Options.FirstOrDefault(
        option => string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase));

    public static bool IsSupported(string? code) => Find(code) != null;

    public static string? PrimaryModeFor(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || string.Equals(code, "auto", StringComparison.OrdinalIgnoreCase))
            return "";
        foreach (string primary in new[] { "sl", "sl-slang", "en" })
        {
            if (string.Equals(code, primary, StringComparison.OrdinalIgnoreCase))
                return primary;
        }
        return null;
    }

    public static bool IsPrimaryMode(string? code) => PrimaryModeFor(code) != null;
}

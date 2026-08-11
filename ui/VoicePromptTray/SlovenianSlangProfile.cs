namespace VoicePromptTray;

internal static class SlovenianSlangProfile
{
    public const string Prompt =
        "Dej, a lohk tole zrihtaš? Kva tle ne štima? Zdej sam poglej, pol pa dej nazaj. " +
        "Ful je fajn, čist kul, itak, ziher. Rabim neki na hitrco, tko da bo delal.";

    public const string Hotwords =
        "dej, lohk, kva, tko, tle, zdej, pol, sam, ful, čist, kul, ziher, itak, fajn, " +
        "štima, zrihtaj, pejt, rabim, neki, nič, mal, hitrco";

    public static string ApplyPrompt(string basePrompt)
    {
        string clean = basePrompt.Trim();
        if (clean.Contains(Prompt, StringComparison.Ordinal))
            return clean;
        return clean == "" ? Prompt : clean + " " + Prompt;
    }

    public static string ApplyHotwords(string baseHotwords)
    {
        return string.Join(", ", (baseHotwords + ", " + Hotwords)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }
}

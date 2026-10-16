namespace Toonel.Core;

/// <summary>
/// Egress countries commonly served by the Psiphon network, for the Settings dropdown.
/// Psiphon itself accepts any ISO 3166-1 alpha-2 code; this list covers the regions it
/// reliably offers. Use <see cref="Normalize"/> to turn a free-text entry (name or code)
/// into a canonical upper-case two-letter code, or "" for automatic/fastest.
/// </summary>
public static class PsiphonCountries
{
    public record Entry(string Code, string Name);

    public static readonly IReadOnlyList<Entry> List = new[]
    {
        new Entry("",   "Fastest (automatic)"),
        new Entry("US", "United States"),
        new Entry("GB", "United Kingdom"),
        new Entry("CA", "Canada"),
        new Entry("DE", "Germany"),
        new Entry("NL", "Netherlands"),
        new Entry("FR", "France"),
        new Entry("SE", "Sweden"),
        new Entry("NO", "Norway"),
        new Entry("FI", "Finland"),
        new Entry("CH", "Switzerland"),
        new Entry("AT", "Austria"),
        new Entry("ES", "Spain"),
        new Entry("IT", "Italy"),
        new Entry("PL", "Poland"),
        new Entry("RO", "Romania"),
        new Entry("CZ", "Czechia"),
        new Entry("IE", "Ireland"),
        new Entry("BE", "Belgium"),
        new Entry("DK", "Denmark"),
        new Entry("JP", "Japan"),
        new Entry("SG", "Singapore"),
        new Entry("HK", "Hong Kong"),
        new Entry("KR", "South Korea"),
        new Entry("IN", "India"),
        new Entry("AE", "United Arab Emirates"),
        new Entry("TR", "Turkey"),
        new Entry("IL", "Israel"),
        new Entry("AU", "Australia"),
        new Entry("NZ", "New Zealand"),
        new Entry("BR", "Brazil"),
        new Entry("AR", "Argentina"),
        new Entry("MX", "Mexico"),
        new Entry("ZA", "South Africa"),
        new Entry("MY", "Malaysia"),
        new Entry("ID", "Indonesia"),
        new Entry("TH", "Thailand"),
        new Entry("VN", "Vietnam"),
        new Entry("PH", "Philippines"),
        new Entry("UA", "Ukraine"),
    };

    /// <summary>All names/aliases that map to a code (used by the free-text normalizer).</summary>
    private static readonly Dictionary<string, string> NameToCode = BuildNameMap();

    private static Dictionary<string, string> BuildNameMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in List)
        {
            if (e.Code.Length == 0) continue;
            map[e.Code] = e.Code;
            map[e.Name] = e.Code;
        }
        // Common aliases.
        map["USA"] = "US";
        map["UK"] = "GB";
        map["Britain"] = "GB";
        map["England"] = "GB";
        map["South Korea"] = "KR";
        map["Korea"] = "KR";
        map["UAE"] = "AE";
        map["Dubai"] = "AE";
        map["Holland"] = "NL";
        map["Viet Nam"] = "VN";
        return map;
    }

    /// <summary>
    /// Canonicalizes a user entry: returns the upper-case ISO code if recognized,
    /// an upper-case 2-letter passthrough if it looks like a valid ISO code Psiphon
    /// may support beyond this list, or "" when blank/automatic.
    /// </summary>
    public static string Normalize(string? input)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0) return "";
        // Tolerate a flag-emoji/whitespace prefix from dropdown labels ("🇩🇪 Germany").
        var firstLetter = -1;
        for (var i = 0; i < text.Length; i++)
            if (char.IsAsciiLetter(text[i])) { firstLetter = i; break; }
        if (firstLetter > 0) text = text[firstLetter..].Trim();
        if (text.Length == 0) return "";
        if (NameToCode.TryGetValue(text, out var code)) return code;
        if (text.Length == 2 && text.All(char.IsLetter)) return text.ToUpperInvariant();
        return ""; // unrecognized name — fall back to fastest
    }

    /// <summary>Display name for a stored code ("" -> automatic label).</summary>
    public static string DisplayName(string code) =>
        List.FirstOrDefault(e => e.Code == code)?.Name ?? code;

    /// <summary>
    /// Flag emoji for a two-letter ISO code (built from the regional-indicator
    /// symbols), "🌐" for automatic/empty, and "" for anything unparseable.
    /// </summary>
    public static string Emoji(string code)
    {
        if (string.IsNullOrEmpty(code)) return "🌐"; // automatic/fastest
        if (code.Length != 2 || !code.All(char.IsAsciiLetter)) return "";
        var upper = code.ToUpperInvariant();
        return string.Concat(RegionalIndicator(upper[0]), RegionalIndicator(upper[1]));
    }

    private static string RegionalIndicator(char letter) =>
        char.ConvertFromUtf32(0x1F1E6 + (letter - 'A'));

    /// <summary>
    /// A short dropdown/list label for a code: "🇩🇪 Germany" ("🌐 Fastest (automatic)").
    /// </summary>
    public static string ListLabel(string code)
    {
        var emoji = Emoji(code);
        var name = DisplayName(code);
        return string.IsNullOrEmpty(emoji) ? name : $"{emoji}  {name}";
    }
}

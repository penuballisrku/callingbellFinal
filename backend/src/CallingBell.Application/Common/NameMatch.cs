using System.Text;

namespace CallingBell.Application.Common;

/// <summary>Loose matching of business names and phone numbers across sources, for removing duplicates.</summary>
public static class NameMatch
{
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
        { "the", "and", "pvt", "private", "ltd", "limited", "llp", "co", "company", "india", "services", "service" };

    /// <summary>Lower-case letters and digits of the meaningful words: "The Décor Studio Pvt. Ltd." -> "décorstudio".</summary>
    public static string Key(string? name)
    {
        var sb = new StringBuilder();
        foreach (var word in (name ?? "").Split([' ', '.', ',', '-', '&', '(', ')', '/', '\''], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Noise.Contains(word)) continue;
            foreach (var ch in word) if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>1 - normalised Levenshtein distance of the name keys (1 = same name).</summary>
    public static double Similarity(string? a, string? b)
    {
        string x = Key(a), y = Key(b);
        if (x.Length == 0 || y.Length == 0) return 0;
        if (x == y) return 1;
        var prev = new int[y.Length + 1];
        var cur = new int[y.Length + 1];
        for (var j = 0; j <= y.Length; j++) prev[j] = j;
        for (var i = 1; i <= x.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= y.Length; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (x[i - 1] == y[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return 1 - (double)prev[y.Length] / Math.Max(x.Length, y.Length);
    }

    /// <summary>Last 10 digits of an Indian phone number ("+91 98480 12345" -> "9848012345"), or null.</summary>
    public static string? Phone(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        return digits.Length >= 10 ? digits[^10..] : null;
    }

    /// <summary>
    /// The same business listed twice: the same phone number, or very similar names close together, or one name containing the other
    /// right next to it.
    /// </summary>
    public static bool SameBusiness(string nameA, string? phoneA, double latA, double lngA, string nameB, string? phoneB, double latB, double lngB)
    {
        if (Phone(phoneA) is { } pa && pa == Phone(phoneB)) return true;
        var km = GeoMath.HaversineKm(latA, lngA, latB, lngB);
        if (km > 1.5) return false;
        var sim = Similarity(nameA, nameB);
        if (sim >= 0.85 || (sim >= 0.7 && km <= 0.4)) return true;
        string ka = Key(nameA), kb = Key(nameB);
        return km <= 0.25 && ka.Length >= 5 && kb.Length >= 5 && (ka.Contains(kb) || kb.Contains(ka));
    }
}

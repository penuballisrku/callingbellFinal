using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace CallingBell.Application.Features.Onboarding;

/// <summary>A place outside Calling Bell that a business can be created from: "google:{place id}" or "osm:node/123".</summary>
public sealed record SourceRef(string Provider, string ExternalId)
{
    public override string ToString() => $"{Provider}:{ExternalId}";
}

/// <summary>
/// The pure parts of "Join Calling Bell": reading source references, cleaning imported text, turning opening-hours text into hours,
/// and comparing businesses for duplicates. No I/O here, so it is unit-tested directly.
/// </summary>
public static partial class JoinImport
{
    [GeneratedRegex("^[A-Za-z0-9_-]{10,300}$")] private static partial Regex GoogleId();
    [GeneratedRegex("^(node|way|relation)/[0-9]{1,15}$")] private static partial Regex OsmId();

    /// <summary>"google:ChIJ…" or "osm:node/123"; null for anything else.</summary>
    public static SourceRef? ParseSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 320) return null;
        var i = value.IndexOf(':');
        if (i <= 0) return null;
        var provider = value[..i].Trim().ToLowerInvariant();
        var id = value[(i + 1)..].Trim();
        return provider switch
        {
            "google" when GoogleId().IsMatch(id) => new SourceRef("google", id),
            "osm" when OsmId().IsMatch(id) => new SourceRef("osm", id),
            _ => null,
        };
    }

    // ---------- text ----------

    [GeneratedRegex(@"[\p{Cc}\p{Cf}<>]")] private static partial Regex Unsafe();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();

    /// <summary>Imported text as plain, single-spaced text without control characters or angle brackets, cut to <paramref name="max"/>.</summary>
    public static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = Spaces().Replace(Unsafe().Replace(value, " "), " ").Trim();
        if (text.Length == 0) return null;
        return text.Length <= max ? text : text[..max].TrimEnd();
    }

    public static string? CleanEmail(string? value)
    {
        var email = Clean(value, 256);
        if (email is null) return null;
        try { return new MailAddress(email).Address == email && email.Contains('.') ? email : null; }
        catch (FormatException) { return null; }
    }

    /// <summary>An http(s) website, or null. Search-engine links (e.g. a Google search saved as the "website") are not websites.</summary>
    public static string? CleanWebsite(string? value)
    {
        var url = Clean(value, 300);
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !uri.Host.Contains('.')) return null;
        var host = Host(url)!;
        return host.StartsWith("google.") || host.StartsWith("bing.") || host is "g.page" or "goo.gl" or "maps.app.goo.gl" ? null : url;
    }

    /// <summary>"https://www.Example.com/x" → "example.com".</summary>
    public static string? Host(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host).ToLowerInvariant() : null;

    /// <summary>
    /// The street part of a formatted address: the trailing parts naming the city, state, postal code or country are dropped, since the
    /// form has its own fields for them ("12 MG Road, Indiranagar, Bengaluru, Karnataka 560038, India" → "12 MG Road, Indiranagar").
    /// </summary>
    public static string? StreetAddress(string? formatted, params string?[] known)
    {
        var text = Clean(formatted, 300);
        if (text is null) return null;
        var drop = known.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!.Trim().ToLowerInvariant()).ToHashSet();
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        while (parts.Count > 1)
        {
            var last = parts[^1].ToLowerInvariant();
            var withoutPin = PostalCode().Replace(last, "").Trim();
            if (drop.Contains(last) || drop.Contains(withoutPin) || withoutPin.Length == 0) parts.RemoveAt(parts.Count - 1);
            else break;
        }
        var street = string.Join(", ", parts);
        return street.Length >= 5 ? street : text;
    }

    [GeneratedRegex(@"\b\d{5,6}\b")] private static partial Regex PostalCode();

    /// <summary>A 6-digit Indian pincode from the postal code or the address, if any.</summary>
    public static string? Pincode(string? postalCode, string? address)
    {
        foreach (var source in new[] { postalCode, address })
            if (source is not null && Regex.Match(source, @"\b[1-9]\d{5}\b") is { Success: true } m) return m.Value;
        return null;
    }

    /// <summary>ISO code for a country given as a code ("IN") or an English name ("India").</summary>
    public static string? CountryCode(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return null;
        var c = country.Trim();
        if (c.Length == 2 && c.All(char.IsAsciiLetter)) return c.ToUpperInvariant();
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            RegionInfo region;
            try { region = new RegionInfo(culture.Name); } catch (ArgumentException) { continue; }
            if (string.Equals(region.EnglishName, c, StringComparison.OrdinalIgnoreCase) || string.Equals(region.NativeName, c, StringComparison.OrdinalIgnoreCase))
                return region.TwoLetterISORegionName;
        }
        return null;
    }

    // ---------- opening hours ----------

    private static readonly string[] DayNames = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];
    private static readonly string[] OsmDays = ["su", "mo", "tu", "we", "th", "fr", "sa"];

    [GeneratedRegex(@"(\d{1,2})(?::(\d{2}))?\s*([AaPp]\.?[Mm]\.?)?")] private static partial Regex Clock();
    [GeneratedRegex(@"^(?<days>(?:Mo|Tu|We|Th|Fr|Sa|Su)(?:\s*[-,]\s*(?:Mo|Tu|We|Th|Fr|Sa|Su))*)\s+(?<times>off|closed|\d{1,2}:\d{2}\s*-\s*\d{1,2}:\d{2}(?:\s*,\s*\d{1,2}:\d{2}\s*-\s*\d{1,2}:\d{2})*)$", RegexOptions.IgnoreCase)]
    private static partial Regex OsmRule();

    /// <summary>
    /// Opening hours per day (0 = Sunday) from Google ("Monday: 9:00 AM – 6:00 PM", "Sunday: Closed", "Open 24 hours") or simple
    /// OpenStreetMap rules ("Mo-Fr 09:00-18:00; Sa 10:00-14:00", "24/7"). Split shifts become first opening to last closing. Days that
    /// can't be read are left out; nothing is guessed.
    /// </summary>
    public static IReadOnlyList<ImportedHours> ParseHours(IEnumerable<string> lines)
    {
        var result = new Dictionary<int, ImportedHours>();
        foreach (var raw in lines)
        {
            var line = raw.Replace('–', '-').Replace('—', '-').Replace(' ', ' ').Replace(' ', ' ').Trim();
            if (line.Length == 0) continue;
            if (line.Equals("24/7", StringComparison.OrdinalIgnoreCase))
            {
                for (var d = 0; d < 7; d++) result[d] = new ImportedHours(d, "00:00", "23:59", false);
                continue;
            }

            // Google: "Monday: 9:00 AM - 6:00 PM"
            var colon = line.IndexOf(':');
            if (colon > 0 && Array.IndexOf(DayNames, line[..colon].Trim().ToLowerInvariant()) is var day and >= 0)
            {
                if (ParseRange(line[(colon + 1)..]) is { } hours) result[day] = hours with { DayOfWeek = day };
                continue;
            }

            // OpenStreetMap: "Mo-Fr 09:00-18:00; Sa 10:00-14:00"
            foreach (var rule in line.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var m = OsmRule().Match(rule);
                if (!m.Success) continue;
                var range = ParseRange(m.Groups["times"].Value);
                if (range is null) continue;
                foreach (var d in OsmDaySet(m.Groups["days"].Value)) result[d] = range with { DayOfWeek = d };
            }
        }
        return result.Values.OrderBy(h => h.DayOfWeek).ToList();
    }

    private static IEnumerable<int> OsmDaySet(string days)
    {
        var set = new SortedSet<int>();
        foreach (var part in days.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var ends = part.Split('-', StringSplitOptions.TrimEntries);
            var from = Array.IndexOf(OsmDays, ends[0].ToLowerInvariant());
            var to = ends.Length > 1 ? Array.IndexOf(OsmDays, ends[1].ToLowerInvariant()) : from;
            if (from < 0 || to < 0) continue;
            for (var d = from; ; d = (d + 1) % 7) { set.Add(d); if (d == to) break; }
        }
        return set;
    }

    private static ImportedHours? ParseRange(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return null;
        if (t.Contains("closed", StringComparison.OrdinalIgnoreCase) || t.Equals("off", StringComparison.OrdinalIgnoreCase))
            return new ImportedHours(0, null, null, true);
        if (t.Contains("24 hours", StringComparison.OrdinalIgnoreCase)) return new ImportedHours(0, "00:00", "23:59", false);

        var clocks = Clock().Matches(t).Select(m => (Hour: int.Parse(m.Groups[1].Value), Minute: m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0,
            Meridiem: m.Groups[3].Success ? m.Groups[3].Value.Replace(".", "").ToUpperInvariant() : null)).ToList();
        if (clocks.Count < 2) return null;
        // "9:00 – 11:00 AM": a time without AM/PM takes the next time's.
        for (var i = clocks.Count - 2; i >= 0; i--)
            if (clocks[i].Meridiem is null && clocks[i + 1].Meridiem is { } next) clocks[i] = clocks[i] with { Meridiem = next };
        static TimeSpan? ToTime((int Hour, int Minute, string? Meridiem) c)
        {
            var h = c.Hour;
            if (c.Meridiem == "PM" && h < 12) h += 12;
            if (c.Meridiem == "AM" && h == 12) h = 0;
            return h is >= 0 and <= 24 && c.Minute is >= 0 and < 60 ? new TimeSpan(h == 24 ? 23 : h, h == 24 ? 59 : c.Minute, 0) : null;
        }
        var open = ToTime(clocks[0]);
        var close = ToTime(clocks[^1]);
        if (open is null || close is null) return null;
        // Past midnight ("6 PM – 2 AM"): the day's hours end at midnight.
        if (close <= open) close = new TimeSpan(23, 59, 0);
        return new ImportedHours(0, open.Value.ToString(@"hh\:mm"), close.Value.ToString(@"hh\:mm"), false);
    }

    // ---------- duplicates ----------

    private static readonly HashSet<string> Filler =
    [
        "the", "and", "pvt", "private", "ltd", "limited", "llp", "inc", "co", "company", "services", "service", "shop", "store", "centre", "center",
        "&", "of", "a", "an", "india",
    ];

    /// <summary>Lower-case words of a business name without punctuation and filler ("Sharma Electricals Pvt. Ltd." → sharma, electricals).</summary>
    public static IReadOnlyList<string> NameTokens(string? name) =>
        string.IsNullOrWhiteSpace(name) ? []
            : Regex.Split(RemoveDiacritics(name.ToLowerInvariant()), @"[^\p{L}\p{N}]+").Where(t => t.Length > 1 && !Filler.Contains(t)).Distinct().ToList();

    /// <summary>
    /// 0-1: how alike two business names are (shared words over the shorter name's words), so "Lakeview Hospital" and "Lakeview
    /// Multispeciality Hospital Pvt Ltd" score 1, and unrelated names 0.
    /// </summary>
    public static double NameSimilarity(string? a, string? b)
    {
        var x = NameTokens(a);
        var y = NameTokens(b);
        if (x.Count == 0 || y.Count == 0) return 0;
        var shared = x.Intersect(y).Count();
        return (double)shared / Math.Min(x.Count, y.Count);
    }

    /// <summary>Metres between two points (haversine).</summary>
    public static double DistanceMetres(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6_371_000;
        double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

/// <summary>Hours for one day (0 = Sunday), "HH:mm".</summary>
public sealed record ImportedHours(int DayOfWeek, string? Open, string? Close, bool IsClosed);

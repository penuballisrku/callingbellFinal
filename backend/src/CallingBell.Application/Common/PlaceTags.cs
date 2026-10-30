namespace CallingBell.Application.Common;

/// <summary>
/// Readable details from a place's OpenStreetMap tags (as Overpass and Geoapify return them): email, description, and short facts such as
/// "Cuisine: South Indian", "Wheelchair accessible", "Takeaway", "Accepts UPI". Only facts the tags state are given; nothing is guessed.
/// </summary>
public static class PlaceTags
{
    private const int MaxFeatures = 10;

    public static string? Email(Func<string, string?> tag) => First(tag, "email", "contact:email");

    public static string? Description(Func<string, string?> tag) =>
        First(tag, "description:en", "description") is { } d ? (d.Length > 400 ? d[..399].TrimEnd() + "…" : d) : null;

    /// <summary>Short facts for the place card, most useful first.</summary>
    public static IReadOnlyList<string> Features(Func<string, string?> tag)
    {
        var list = new List<string>();
        void Add(string? text) { if (!string.IsNullOrWhiteSpace(text) && !list.Contains(text)) list.Add(text); }
        bool Yes(string key) => tag(key) is "yes" or "only" or "designated";

        if (List(tag("cuisine")) is { } cuisine) Add($"Cuisine: {cuisine}");
        if (List(tag("healthcare:speciality")) is { } speciality) Add($"Speciality: {speciality}");
        if (tag("diet:vegetarian") == "only") Add("Pure vegetarian");
        else if (tag("diet:vegetarian") == "yes") Add("Vegetarian options");
        if (tag("wheelchair") is "yes" or "designated") Add("Wheelchair accessible");
        else if (tag("wheelchair") == "limited") Add("Limited wheelchair access");
        if (Yes("delivery")) Add("Delivery");
        if (Yes("takeaway")) Add("Takeaway");
        if (Yes("outdoor_seating")) Add("Outdoor seating");
        if (Yes("air_conditioning")) Add("Air-conditioned");
        if (tag("internet_access") is "wlan" or "yes" or "wifi") Add("Wi-Fi");
        if (Yes("drive_through")) Add("Drive-through");
        if (tag("parking") is { } parking && parking != "no") Add("Parking");
        if (Yes("payment:upi")) Add("Accepts UPI");
        if (Yes("payment:cards") || Yes("payment:credit_cards") || Yes("payment:debit_cards")) Add("Accepts cards");
        if (Yes("emergency")) Add("Emergency services");
        if (tag("opening_hours") == "24/7") Add("Open 24 hours");
        if (First(tag, "brand", "operator") is { } brand) Add($"By {brand}");
        return list.Take(MaxFeatures).ToList();
    }

    private static string? First(Func<string, string?> tag, params string[] keys)
    {
        foreach (var k in keys)
            if (tag(k) is { } v && !string.IsNullOrWhiteSpace(v)) return v.Split(';')[0].Trim();
        return null;
    }

    /// <summary>"south_indian;chinese" → "South indian, Chinese".</summary>
    private static string? List(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Replace('_', ' ')).Where(p => p.Length > 0).Take(3)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..]).ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}

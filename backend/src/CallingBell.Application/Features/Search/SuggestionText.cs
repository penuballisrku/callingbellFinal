namespace CallingBell.Application.Features.Search;

/// <summary>Wording and input checks for search suggestions, kept pure so they can be tested without a database.</summary>
public static class SuggestionText
{
    /// <summary>"AC Repair in Kukatpally": a service in a place.</summary>
    public static string Combined(string what, string place) => $"{what.Trim()} in {place.Trim()}";

    /// <summary>
    /// What a combined suggestion says about the place: the real number of listed businesses, or, honestly, that none are listed there
    /// yet. Never an estimate.
    /// </summary>
    public static string CountDetail(int count, string place) => count switch
    {
        <= 0 => $"None listed in {place} yet",
        1 => $"1 business in {place}",
        _ => $"{count:N0} businesses in {place}",
    };

    /// <summary>The start of an Indian PIN code: digits only, 2 to 6 of them.</summary>
    public static bool IsPostalCodePrefix(string? q) =>
        q is { Length: >= 2 and <= 6 } && q.All(char.IsAsciiDigit);

    /// <summary>A 2-letter ISO country code in upper case, or null.</summary>
    public static string? CountryCode(string? value) =>
        value?.Trim() is { Length: 2 } c && c.All(char.IsAsciiLetter) ? c.ToUpperInvariant() : null;

    /// <summary>Accepts simple plurals, as the main search does: "electricians" matches "Electrician".</summary>
    public static string Singular(string q) => q.Length > 4 && q.EndsWith('s') ? q[..^1] : q;
}

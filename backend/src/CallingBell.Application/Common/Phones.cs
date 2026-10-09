namespace CallingBell.Application.Common;

/// <summary>Mobile numbers: the stored form ("+91 98765 43210"), E.164 for providers ("+919876543210") and a masked form for logs.</summary>
public static class Phones
{
    /// <summary>
    /// The form numbers are stored in: Indian numbers written any way ("098480 12345", "+91 98480 12345", "9848012345") become
    /// "+91 98480 12345"; anything else is returned trimmed.
    /// </summary>
    public static string Normalize(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
        if (digits.Length == 11 && digits.StartsWith('0')) digits = digits[1..];
        return digits.Length == 10 ? $"+91 {digits[..5]} {digits[5..]}" : phone.Trim();
    }

    /// <summary>
    /// E.164 ("+919876543210"), or null when the number can't be one. Numbers without a country code are taken as Indian (10 digits,
    /// optionally with a leading 0); numbers with "+" keep their country code (8-15 digits in all).
    /// </summary>
    public static string? ToE164(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var trimmed = phone.Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (trimmed.StartsWith('+') || trimmed.StartsWith("00"))
        {
            if (trimmed.StartsWith("00")) digits = digits[2..];
            return digits.Length is >= 8 and <= 15 && digits[0] != '0' ? "+" + digits : null;
        }
        if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
        if (digits.Length == 11 && digits.StartsWith('0')) digits = digits[1..];
        return digits.Length == 10 ? "+91" + digits : null;
    }

    /// <summary>ISO country of an E.164 number, where the platform needs to know it (providers per country); null otherwise.</summary>
    public static string? CountryOf(string? e164) => e164 switch
    {
        null => null,
        _ when e164.StartsWith("+91") && e164.Length == 13 => "IN",
        _ when e164.StartsWith("+1") && e164.Length == 12 => null, // US and Canada share +1
        _ when e164.StartsWith("+44") => "GB",
        _ when e164.StartsWith("+971") => "AE",
        _ when e164.StartsWith("+61") => "AU",
        _ => null,
    };

    /// <summary>For logs and support screens: "+91••••••3210" (country code and last four digits only).</summary>
    public static string Mask(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "(none)";
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 6) return "•••";
        var country = phone.TrimStart().StartsWith('+') && digits.Length > 10 ? "+" + digits[..(digits.Length - 10)] : "";
        return $"{country}••••••{digits[^4..]}";
    }
}

using System.Globalization;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Common;

/// <summary>
/// How platform prices are shown and charged in one country (from dbo.CountryPricing). Platform prices are set in Indian rupees and
/// converted with a purchasing-power multiplier, so they are fair locally. A business's own prices are never converted.
/// </summary>
public sealed record CountryPrice(string CountryCode, string CurrencyCode, string Locale, decimal Multiplier, decimal RoundingStep, string? TaxName,
    decimal TaxRate)
{
    /// <summary>India: prices as set, in rupees, with 18% GST. Also used when the database has no pricing rows.</summary>
    public static readonly CountryPrice India = new("IN", "INR", "en-IN", 1, 1, "GST", 0.18m);

    private bool IsBase => CurrencyCode == "INR" && Multiplier == 1;

    /// <summary>A rupee platform price in this country's currency, rounded to <see cref="RoundingStep"/> (never below one step). Free stays free.</summary>
    public decimal Convert(decimal inr)
    {
        if (inr <= 0 || IsBase) return inr;
        var rounded = Math.Round(inr * Multiplier / RoundingStep, MidpointRounding.AwayFromZero) * RoundingStep;
        return Math.Round(Math.Max(RoundingStep, rounded), Currencies.MinorUnits(CurrencyCode));
    }

    /// <summary>Tax on a charge, rounded to the currency's minor unit (paise, cents, fils).</summary>
    public decimal Tax(decimal subtotal) => Math.Round(subtotal * TaxRate, Currencies.MinorUnits(CurrencyCode), MidpointRounding.AwayFromZero);

    /// <summary>An amount for text such as the AI overview: "₹1,499" in India, "USD 24" elsewhere (unambiguous for the model and the reader).</summary>
    public string Format(decimal? amount)
    {
        if (amount is not > 0) return "price on request";
        var digits = amount.Value == Math.Floor(amount.Value) ? 0 : Currencies.MinorUnits(CurrencyCode);
        return CurrencyCode == "INR"
            ? "₹" + amount.Value.ToString("N" + digits, CultureInfo.GetCultureInfo("en-IN"))
            : $"{CurrencyCode} {amount.Value.ToString("N" + digits, CultureInfo.InvariantCulture)}";
    }
}

public static class Currencies
{
    /// <summary>Decimal places of a currency's minor unit (ISO 4217): 0 for yen, 3 for Gulf dinars, otherwise 2.</summary>
    public static int MinorUnits(string currencyCode) => currencyCode.ToUpperInvariant() switch
    {
        "JPY" or "KRW" or "VND" or "CLP" or "ISK" or "UGX" or "PYG" or "XAF" or "XOF" => 0,
        "KWD" or "BHD" or "OMR" or "JOD" or "TND" or "LYD" or "IQD" => 3,
        _ => 2,
    };

    /// <summary>An amount in the currency's smallest unit, as payment gateways expect (paise, cents, fils).</summary>
    public static long ToMinor(decimal amount, string currencyCode) =>
        (long)Math.Round(amount * (decimal)Math.Pow(10, MinorUnits(currencyCode)), MidpointRounding.AwayFromZero);
}

/// <summary>Looks up <see cref="CountryPrice"/> by country, city or business. The table is small and kept in memory with the reference data.</summary>
public sealed class CountryPricingService(IUnitOfWork uow, ReferenceDataCache reference)
{
    /// <summary>
    /// Pricing for a country. No country (e.g. admin screens): India, the prices as set. A country without a row: the "ZZ" default.
    /// </summary>
    public async Task<CountryPrice> ForCountryAsync(string? countryCode, CancellationToken ct)
    {
        var code = countryCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) return CountryPrice.India;
        var all = await reference.GetDerivedAsync("country-pricing", () => LoadAsync(ct), ct);
        return all.GetValueOrDefault(code) ?? all.GetValueOrDefault("ZZ") ?? CountryPrice.India;
    }

    /// <summary>Pricing for the country a city is in.</summary>
    public async Task<CountryPrice> ForCityAsync(Guid? cityId, CancellationToken ct)
    {
        if (cityId is not { } id || id == Guid.Empty) return CountryPrice.India;
        var code = await uow.Repository<City>().QueryNoTracking().Where(c => c.Id == id).Select(c => c.State.CountryCode).FirstOrDefaultAsync(ct);
        return await ForCountryAsync(code, ct);
    }

    /// <summary>Pricing for the country a business is in (its city's country); India when it has no city.</summary>
    public async Task<CountryPrice> ForBusinessAsync(Guid businessId, CancellationToken ct)
    {
        var cityId = await uow.Repository<Business>().QueryNoTracking().Where(b => b.Id == businessId).Select(b => b.CityId).FirstOrDefaultAsync(ct);
        return await ForCityAsync(cityId, ct);
    }

    private async Task<Dictionary<string, CountryPrice>> LoadAsync(CancellationToken ct) =>
        (await uow.Repository<CountryPricing>().QueryNoTracking().Where(c => c.IsActive).ToListAsync(ct))
            .ToDictionary(c => c.CountryCode.Trim().ToUpperInvariant(),
                c => new CountryPrice(c.CountryCode.Trim().ToUpperInvariant(), c.CurrencyCode.Trim().ToUpperInvariant(), c.Locale, c.PriceMultiplier,
                    c.RoundingStep > 0 ? c.RoundingStep : 1, c.TaxName, c.TaxRate),
                StringComparer.OrdinalIgnoreCase);
}

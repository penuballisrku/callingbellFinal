using System.Text.RegularExpressions;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Content;

/// <summary>
/// The legal pages (Terms and Conditions, Privacy, Cancellation and Refund policies) are MarketingContent pages whose text says
/// {LegalName}, {SupportEmail}, {GrievanceOfficer} and so on. The values are settings (PageKey "Legal", SectionKey "Setting",
/// Code "LEGAL-SET-{Key}", Title = value) that admins edit, so company details are kept in one place and never typed into the text.
/// </summary>
public static class LegalText
{
    public const string SettingsPage = "Legal";
    public const string CodePrefix = "LEGAL-SET-";
    public const string PagePrefix = "Legal-";

    /// <summary>The settings, in the order the admin form shows them.</summary>
    public static readonly string[] Keys =
        ["Brand", "LegalName", "RegisteredAddress", "Website", "SupportEmail", "SupportPhone", "GrievanceOfficer", "GrievanceEmail", "GrievancePhone", "Jurisdiction", "EffectiveDate"];

    /// <summary>Settings shown as email addresses (checked when saved).</summary>
    public static readonly string[] EmailKeys = ["SupportEmail", "GrievanceEmail"];

    private static readonly Regex Token = new(@"\{([A-Za-z]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Wording used while a setting is still empty, so a sentence still reads correctly and points somewhere real instead of showing a
    /// blank or a made-up detail.
    /// </summary>
    private static string Fallback(string key, IReadOnlyDictionary<string, string> values) => key switch
    {
        "Brand" => "CallInBell",
        "LegalName" => Value("Brand", values) ?? "CallInBell",
        "Website" => "www.callinbell.com",
        "RegisteredAddress" => "address available from customer care on request",
        "SupportEmail" or "SupportPhone" or "GrievanceEmail" or "GrievancePhone" => "the Contact support page",
        "GrievanceOfficer" => "the Grievance Officer",
        "Jurisdiction" => "the city of our registered office",
        _ => "",
    };

    private static string? Value(string key, IReadOnlyDictionary<string, string> values) =>
        values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    /// <summary>The setting's value, or its fallback wording when empty.</summary>
    public static string Resolve(string key, IReadOnlyDictionary<string, string> values) => Value(key, values) ?? Fallback(key, values);

    /// <summary>Replaces every {Key} with its setting (unknown keys are left as they are).</summary>
    public static string? Fill(string? text, IReadOnlyDictionary<string, string> values) =>
        text is null ? null : Token.Replace(text, m => Keys.Contains(m.Groups[1].Value) ? Resolve(m.Groups[1].Value, values) : m.Value);
}

/// <summary>Reads the legal settings (key → value; empty when not set).</summary>
public sealed class LegalSettings(IUnitOfWork uow)
{
    public async Task<IReadOnlyDictionary<string, string>> GetAsync(CancellationToken ct)
    {
        var rows = await uow.Repository<MarketingContent>().QueryNoTracking()
            .Where(c => c.PageKey == LegalText.SettingsPage && c.SectionKey == "Setting" && c.IsActive)
            .Select(c => new { c.Code, c.Title }).ToListAsync(ct);
        return rows.Where(r => r.Code.StartsWith(LegalText.CodePrefix))
            .ToDictionary(r => r.Code[LegalText.CodePrefix.Length..], r => r.Title ?? "", StringComparer.Ordinal);
    }

    /// <summary>The version of the terms a business accepts before paying: the effective date.</summary>
    public async Task<string> TermsVersionAsync(CancellationToken ct) => LegalText.Resolve("EffectiveDate", await GetAsync(ct)) is { Length: > 0 } v ? v : "unversioned";
}

// ===================== Admin: edit the settings =====================

public sealed record LegalSettingDto(string Key, string Label, string? Help, string Value);

public sealed record GetLegalSettingsQuery : IRequest<IReadOnlyList<LegalSettingDto>>;

public sealed class GetLegalSettingsHandler(IUnitOfWork uow) : IRequestHandler<GetLegalSettingsQuery, IReadOnlyList<LegalSettingDto>>
{
    public async Task<IReadOnlyList<LegalSettingDto>> Handle(GetLegalSettingsQuery request, CancellationToken ct)
    {
        var rows = await uow.Repository<MarketingContent>().QueryNoTracking()
            .Where(c => c.PageKey == LegalText.SettingsPage && c.SectionKey == "Setting" && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new { c.Code, c.Title, c.Subtitle, c.Body }).ToListAsync(ct);
        return rows.Where(r => r.Code.StartsWith(LegalText.CodePrefix))
            .Select(r => new LegalSettingDto(r.Code[LegalText.CodePrefix.Length..], r.Subtitle ?? r.Code, r.Body, r.Title ?? "")).ToList();
    }
}

/// <param name="Values">Setting key → value; keys not included are left unchanged. An empty value clears the setting.</param>
public sealed record UpdateLegalSettingsCommand(IReadOnlyDictionary<string, string?> Values) : IRequest<IReadOnlyList<LegalSettingDto>>;

public sealed class UpdateLegalSettingsValidator : AbstractValidator<UpdateLegalSettingsCommand>
{
    public UpdateLegalSettingsValidator()
    {
        // Each problem is reported under the setting's own key ("SupportEmail"), so the admin form shows it beside that field.
        RuleFor(x => x.Values).NotNull().Custom((values, ctx) =>
        {
            foreach (var (key, raw) in values ?? new Dictionary<string, string?>())
            {
                var value = raw?.Trim() ?? "";
                string? problem = !LegalText.Keys.Contains(key) ? "Unknown setting."
                    : value.Length > 300 ? "Keep this under 300 characters."
                    : value.Contains('{') || value.Contains('}') || value.Contains('<') ? "Remove braces and angle brackets."
                    : LegalText.EmailKeys.Contains(key) && value.Length > 0 && !Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$") ? "Enter a valid email address."
                    : null;
                if (problem is not null) ctx.AddFailure(new FluentValidation.Results.ValidationFailure(key, problem));
            }
        });
    }
}

public sealed class UpdateLegalSettingsHandler(IUnitOfWork uow, ISender sender) : IRequestHandler<UpdateLegalSettingsCommand, IReadOnlyList<LegalSettingDto>>
{
    public async Task<IReadOnlyList<LegalSettingDto>> Handle(UpdateLegalSettingsCommand r, CancellationToken ct)
    {
        var codes = r.Values.Keys.Select(k => LegalText.CodePrefix + k).ToList();
        var rows = await uow.Repository<MarketingContent>().Query()
            .Where(c => c.PageKey == LegalText.SettingsPage && c.SectionKey == "Setting" && codes.Contains(c.Code)).ToListAsync(ct);
        if (rows.Count != codes.Count) throw new NotFoundException("Legal setting", string.Join(", ", codes.Except(rows.Select(x => x.Code))));
        foreach (var row in rows) row.Title = r.Values[row.Code[LegalText.CodePrefix.Length..]]?.Trim() ?? "";
        await uow.SaveChangesAsync(ct);
        return await sender.Send(new GetLegalSettingsQuery(), ct);
    }
}

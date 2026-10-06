using System.Text;
using System.Text.RegularExpressions;
using CallingBell.Application.Common;
using CallingBell.Application.Common.Exceptions;
using CallingBell.Application.Common.Interfaces;
using CallingBell.Application.Features.Auth;
using CallingBell.Application.Features.Engagement;
using CallingBell.Domain.Constants;
using CallingBell.Domain.Entities;
using FluentValidation;
using MediatR;
using ValidationException = CallingBell.Application.Common.Exceptions.ValidationException;
using Microsoft.EntityFrameworkCore;

namespace CallingBell.Application.Features.Onboarding;

// ===================== Contracts =====================

public sealed record OnboardingServiceInput(string Name, string? Description, decimal Price, string? PriceUnit, int DurationMinutes, string Type);
public sealed record OnboardingSocialLinkInput(string Platform, string Url);

/// <summary>Everything a business owner provides about the business itself (shared by sign-up and post-login setup).</summary>
public sealed record BusinessProfileInput(
    string BusinessName, string CategorySlug, string SubCategorySlug, string? Tagline, string Description, int? YearEstablished, int? TeamSize,
    string? Languages, bool AcceptsOnlineBooking, bool OffersHomeService, bool OffersVideoConsultation,
    string BusinessPhone, string? WhatsAppNumber, string? BusinessEmail, string? Website,
    string CitySlug, string? AreaSlug, string AddressLine, string? Landmark, string Pincode,
    IReadOnlyList<OnboardingServiceInput> Services, string PlanCode, string BillingCycle, IReadOnlyList<OnboardingSocialLinkInput>? SocialLinks);

/// <summary>
/// The business starts on the Free plan. When a paid plan was chosen, <see cref="RequestedPlanCode"/> is set and the client
/// opens checkout; the paid plan activates once payment is confirmed.
/// </summary>
public sealed record CreatedBusinessDto(Guid BusinessId, string Slug, string Status, string PlanName, string SubscriptionStatus,
    string? RequestedPlanCode, string? RequestedPlanName, string BillingCycle);

public sealed record BusinessRegistrationResultDto(AuthResultDto Auth, CreatedBusinessDto Business);

/// <summary>
/// Sign up as a business owner and create the business in one atomic operation. The owner's mobile number must have been
/// verified by OTP first (<c>POST /api/auth/otp/verify</c>), which issues <paramref name="PhoneVerificationToken"/>.
/// </summary>
public sealed record RegisterBusinessCommand(string DisplayName, string Email, string PhoneNumber, string PhoneVerificationToken, BusinessProfileInput Business)
    : IRequest<BusinessRegistrationResultDto>;

/// <summary>A signed-in business owner who has no business yet creates one.</summary>
public sealed record CreateOwnerBusinessCommand(BusinessProfileInput Business) : IRequest<CreatedBusinessDto>;

public sealed record EmailAvailabilityQuery(string Email) : IRequest<bool>;

// ===================== Validation =====================

internal static class OnboardingRules
{
    private static readonly Regex Pincode = new(@"^[1-9]\d{5}$", RegexOptions.Compiled);
    public static readonly string[] BillingCycles = ["Monthly", "Annual"];

    /// <summary>Indian mobile or landline (with STD code): 10 significant digits after an optional +91 / 0 prefix.</summary>
    public static bool IsIndianPhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (value.TrimStart().StartsWith('+') && digits.StartsWith("91")) digits = digits[2..];
        else if (digits.Length == 11 && digits.StartsWith('0')) digits = digits[1..];
        return digits.Length == 10 && digits[0] != '0';
    }

    public static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && uri.Host.Contains('.');

    public static bool IsPincode(string? value) => value is not null && Pincode.IsMatch(value.Trim());
}

public sealed class BusinessProfileInputValidator : AbstractValidator<BusinessProfileInput>
{
    public BusinessProfileInputValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        RuleFor(x => x.BusinessName).NotEmpty().WithMessage("Enter your business name.").Length(3, 150);
        RuleFor(x => x.CategorySlug).NotEmpty().WithMessage("Choose your industry.");
        RuleFor(x => x.SubCategorySlug).NotEmpty().WithMessage("Choose your business category.");
        RuleFor(x => x.Tagline).MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().WithMessage("Describe your business.")
            .MinimumLength(50).WithMessage("Description must be at least 50 characters.").MaximumLength(2000);
        RuleFor(x => x.YearEstablished).InclusiveBetween(1900, DateTime.UtcNow.Year).When(x => x.YearEstablished.HasValue)
            .WithMessage($"Enter a year between 1900 and {DateTime.UtcNow.Year}.");
        RuleFor(x => x.TeamSize).InclusiveBetween(1, 100000).When(x => x.TeamSize.HasValue);
        RuleFor(x => x.Languages).MaximumLength(200);

        RuleFor(x => x.BusinessPhone).Must(OnboardingRules.IsIndianPhone).WithMessage("Enter a valid 10-digit phone number.");
        RuleFor(x => x.WhatsAppNumber).Must(OnboardingRules.IsIndianPhone).When(x => !string.IsNullOrWhiteSpace(x.WhatsAppNumber))
            .WithMessage("Enter a valid 10-digit WhatsApp number.");
        RuleFor(x => x.BusinessEmail).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.BusinessEmail));
        RuleFor(x => x.Website).MaximumLength(300).Must(OnboardingRules.IsHttpUrl).WithMessage("Enter a valid website URL starting with https://")
            .When(x => !string.IsNullOrWhiteSpace(x.Website));

        RuleFor(x => x.CitySlug).NotEmpty().WithMessage("Choose your city.");
        RuleFor(x => x.AddressLine).NotEmpty().WithMessage("Enter your business address.").Length(5, 300);
        RuleFor(x => x.Landmark).MaximumLength(150);
        RuleFor(x => x.Pincode).Must(OnboardingRules.IsPincode).WithMessage("Enter a valid 6-digit pincode.");

        RuleFor(x => x.Services).NotEmpty().WithMessage("Add at least one service.");
        RuleForEach(x => x.Services).ChildRules(s =>
        {
            s.RuleLevelCascadeMode = CascadeMode.Stop;
            s.RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the service name.").Length(2, 150);
            s.RuleFor(x => x.Description).MaximumLength(1000);
            s.RuleFor(x => x.Price).InclusiveBetween(0, 10_000_000).WithMessage("Enter a price between ₹0 and ₹1,00,00,000.");
            s.RuleFor(x => x.PriceUnit).MaximumLength(40);
            s.RuleFor(x => x.DurationMinutes).InclusiveBetween(0, 1440).WithMessage("Duration must be between 0 and 1,440 minutes.");
            s.RuleFor(x => x.Type).NotEmpty().WithMessage("Choose how this service is delivered.");
        });
        RuleFor(x => x.Services).Must(s => s.Select(x => x.Name.Trim().ToLowerInvariant()).Distinct().Count() == s.Count)
            .When(x => x.Services is { Count: > 0 }).WithMessage("Each service must have a different name.");

        RuleFor(x => x.PlanCode).NotEmpty().WithMessage("Choose a plan.");
        RuleFor(x => x.BillingCycle).Must(c => OnboardingRules.BillingCycles.Contains(c)).WithMessage("Billing cycle must be Monthly or Annual.");

        RuleForEach(x => x.SocialLinks).ChildRules(l =>
        {
            l.RuleLevelCascadeMode = CascadeMode.Stop;
            l.RuleFor(x => x.Platform).NotEmpty().WithMessage("Choose a platform.");
            l.RuleFor(x => x.Url).NotEmpty().WithMessage("Enter the profile URL.").MaximumLength(300)
                .Must(OnboardingRules.IsHttpUrl).WithMessage("Enter a valid profile URL starting with https://");
        });
        RuleFor(x => x.SocialLinks).Must(l => l!.Select(x => x.Platform).Distinct().Count() == l!.Count)
            .When(x => x.SocialLinks is { Count: > 0 }).WithMessage("Add each social platform only once.");
    }
}

public sealed class RegisterBusinessValidator : AbstractValidator<RegisterBusinessCommand>
{
    public RegisterBusinessValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        RuleFor(x => x.DisplayName).NotEmpty().WithMessage("Enter your full name.").Length(2, 120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches(@"^(\+91[\s-]?)?[6-9]\d{4}[\s-]?\d{5}$")
            .WithMessage("Enter a valid 10-digit Indian mobile number.");
        RuleFor(x => x.PhoneVerificationToken).NotEmpty().WithMessage("Verify your mobile number with the code we send you.")
            .OverridePropertyName("phoneNumber");
        RuleFor(x => x.Business).NotNull().SetValidator(new BusinessProfileInputValidator());
    }
}

public sealed class CreateOwnerBusinessValidator : AbstractValidator<CreateOwnerBusinessCommand>
{
    public CreateOwnerBusinessValidator() => RuleFor(x => x.Business).NotNull().SetValidator(new BusinessProfileInputValidator());
}

// ===================== Creation =====================

/// <summary>Resolves reference data and builds the business aggregate. Shared by both onboarding paths.</summary>
internal sealed record ResolvedBusinessInput(Category Category, SubCategory SubCategory, City City, Area? Area, SubscriptionPlan Plan, SubscriptionPlan FreePlan);

internal sealed class BusinessFactory(IUnitOfWork uow)
{
    /// <summary>Checks every reference against the database before anything is written (no partial sign-ups).</summary>
    public async Task<ResolvedBusinessInput> ResolveAsync(BusinessProfileInput input, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        void Fail(string key, string message) => errors[key] = [message];

        var category = await uow.Repository<Category>().QueryNoTracking().FirstOrDefaultAsync(c => c.Slug == input.CategorySlug && c.IsActive, ct);
        if (category is null) Fail("business.categorySlug", "Choose a valid industry.");
        var sub = await uow.Repository<SubCategory>().QueryNoTracking().FirstOrDefaultAsync(s => s.Slug == input.SubCategorySlug && s.IsActive, ct);
        if (sub is null || (category is not null && sub.CategoryId != category.Id)) Fail("business.subCategorySlug", "Choose a business category from the selected industry.");

        var city = await uow.Repository<City>().QueryNoTracking().FirstOrDefaultAsync(c => c.Slug == input.CitySlug && c.IsActive, ct);
        if (city is null) Fail("business.citySlug", "Choose a valid city.");
        Area? area = null;
        if (!string.IsNullOrWhiteSpace(input.AreaSlug) && city is not null)
        {
            area = await uow.Repository<Area>().QueryNoTracking().FirstOrDefaultAsync(a => a.Slug == input.AreaSlug && a.CityId == city.Id && a.IsActive, ct);
            if (area is null) Fail("business.areaSlug", "Choose an area in the selected city.");
        }

        var plan = await uow.Repository<SubscriptionPlan>().QueryNoTracking().FirstOrDefaultAsync(p => p.Code == input.PlanCode && p.IsActive, ct);
        if (plan is null) Fail("business.planCode", "Choose a valid plan.");
        else if (input.Services.Count > plan.MaxServices) Fail("business.services", $"The {plan.Name} plan allows up to {plan.MaxServices} services.");

        var serviceTypes = await LookupCodes(LookupTypes.ServiceType, ct);
        for (var i = 0; i < input.Services.Count; i++)
            if (!serviceTypes.Contains(input.Services[i].Type)) Fail($"business.services[{i}].type", "Choose how this service is delivered.");

        if (input.SocialLinks is { Count: > 0 })
        {
            var platforms = await uow.Repository<LookupValue>().QueryNoTracking()
                .Where(l => l.LookupType == LookupTypes.SocialPlatform && l.IsActive)
                .ToDictionaryAsync(l => l.Code, l => l.Description, ct);
            for (var i = 0; i < input.SocialLinks.Count; i++)
            {
                var link = input.SocialLinks[i];
                if (!platforms.TryGetValue(link.Platform, out var example)) { Fail($"business.socialLinks[{i}].platform", "Choose a supported platform."); continue; }
                if (!SocialLinks.MatchesPlatform(link.Url, example)) Fail($"business.socialLinks[{i}].url", $"Enter a {link.Platform} profile URL, e.g. {example}");
            }
        }

        if (errors.Count > 0) throw new ValidationException(errors);
        var free = await uow.Repository<SubscriptionPlan>().QueryNoTracking().FirstAsync(p => p.Code == PlanCodes.Free, ct);
        return new ResolvedBusinessInput(category!, sub!, city!, area, plan!, free);
    }

    public async Task<CreatedBusinessDto> CreateAsync(string ownerUserId, BusinessProfileInput input, ResolvedBusinessInput r, CancellationToken ct)
    {
        var today = IndianTime.Today;
        var business = new Business
        {
            OwnerUserId = ownerUserId,
            CategoryId = r.Category.Id,
            SubCategoryId = r.SubCategory.Id,
            Name = Clean(input.BusinessName)!,
            Slug = await UniqueSlugAsync(input.BusinessName, r.City.Slug, ct),
            Tagline = Clean(input.Tagline),
            Description = input.Description.Trim(),
            CityId = r.City.Id,
            City = r.City.Name,
            AreaId = r.Area?.Id,
            Area = r.Area?.Name,
            AddressLine = Clean(input.AddressLine),
            Landmark = Clean(input.Landmark),
            Pincode = input.Pincode.Trim(),
            Latitude = r.Area?.Latitude,
            Longitude = r.Area?.Longitude,
            PhoneNumber = Phones.Normalize(input.BusinessPhone),
            WhatsAppNumber = string.IsNullOrWhiteSpace(input.WhatsAppNumber) ? null : Phones.Normalize(input.WhatsAppNumber),
            Email = Clean(input.BusinessEmail),
            Website = Clean(input.Website),
            YearEstablished = input.YearEstablished,
            TeamSize = input.TeamSize,
            Languages = Clean(input.Languages),
            AcceptsOnlineBooking = input.AcceptsOnlineBooking,
            OffersHomeService = input.OffersHomeService,
            OffersVideoConsultation = input.OffersVideoConsultation,
            Status = BusinessStatuses.PendingApproval,
            VerificationStatus = VerificationStatuses.Pending,
            AvailabilityStatus = AvailabilityStatuses.Offline,
            LastSeenOn = DateTimeOffset.UtcNow
        };
        uow.Repository<Business>().Add(business);

        foreach (var (s, i) in input.Services.Select((s, i) => (s, i)))
        {
            uow.Repository<BusinessService>().Add(new BusinessService
            {
                BusinessId = business.Id, Name = s.Name.Trim(), Description = s.Description?.Trim() ?? string.Empty, Price = s.Price,
                PriceUnit = Clean(s.PriceUnit), DurationMinutes = s.DurationMinutes, Type = s.Type, IsPopular = i == 0, IsActive = true
            });
        }

        foreach (var link in input.SocialLinks ?? [])
            uow.Repository<BusinessSocialLink>().Add(new BusinessSocialLink { BusinessId = business.Id, Platform = link.Platform, Url = link.Url.Trim() });

        // Every new business starts on the Free plan. A chosen paid plan is bought right after sign-up (Razorpay checkout)
        // and replaces Free once the payment is confirmed; if the owner doesn't pay, the listing simply stays on Free.
        var paid = r.Plan.Code != PlanCodes.Free;
        var subscription = new BusinessSubscription
        {
            SubscriptionNumber = References.New("SUB"),
            BusinessId = business.Id,
            PlanId = r.FreePlan.Id,
            BillingCycle = input.BillingCycle,
            StartDate = today,
            EndDate = today.AddYears(10),
            Amount = 0,
            Status = SubscriptionStatuses.Active,
            AutoRenew = false
        };
        uow.Repository<BusinessSubscription>().Add(subscription);

        uow.Repository<Notification>().Add(new Notification
        {
            UserId = ownerUserId,
            Title = "Welcome to Calling Bell",
            Message = $"{business.Name} has been submitted for review. Add photos and videos while our team approves your listing.",
            NotificationType = "BusinessCreated",
            LinkUrl = "/business",
            CreatedOn = DateTimeOffset.UtcNow
        });

        await uow.SaveChangesAsync(ct);
        return new CreatedBusinessDto(business.Id, business.Slug, business.Status, r.FreePlan.Name, subscription.Status,
            paid ? r.Plan.Code : null, paid ? r.Plan.Name : null, input.BillingCycle);
    }

    private async Task<HashSet<string>> LookupCodes(string type, CancellationToken ct) =>
        (await uow.Repository<LookupValue>().QueryNoTracking().Where(l => l.LookupType == type && l.IsActive).Select(l => l.Code).ToListAsync(ct)).ToHashSet();

    private async Task<string> UniqueSlugAsync(string name, string citySlug, CancellationToken ct)
    {
        var baseSlug = Slugify(name);
        if (baseSlug.Length < 3) baseSlug = $"business-{baseSlug}".Trim('-');
        var candidates = new[] { baseSlug, $"{baseSlug}-{citySlug}" };
        var taken = await uow.Repository<Business>().QueryNoTracking().IgnoreQueryFilters()
            .Where(b => b.Slug.StartsWith(baseSlug)).Select(b => b.Slug).ToListAsync(ct);
        foreach (var c in candidates) if (!taken.Contains(c)) return c;
        for (var n = 2; ; n++) if (!taken.Contains($"{baseSlug}-{citySlug}-{n}")) return $"{baseSlug}-{citySlug}-{n}";
    }

    private static string Slugify(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (ch == '&') sb.Append("-and-");
            else if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '.' or '/') sb.Append('-');
        }
        var slug = Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
        return slug.Length > 120 ? slug[..120].TrimEnd('-') : slug;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : Regex.Replace(value.Trim(), @"\s{2,}", " ");
}

internal static class Phones
{
    public static string Normalize(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
        if (digits.Length == 11 && digits.StartsWith('0')) digits = digits[1..];
        return digits.Length == 10 ? $"+91 {digits[..5]} {digits[5..]}" : phone.Trim();
    }
}

public static class SocialLinks
{
    /// <summary>The URL's host must belong to the platform's domain (taken from the lookup's example URL).</summary>
    public static bool MatchesPlatform(string url, string? exampleUrl)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        if (!Uri.TryCreate(exampleUrl, UriKind.Absolute, out var example)) return true;
        static string Root(string host) => host.StartsWith("www.") ? host[4..] : host;
        var host = Root(uri.Host.ToLowerInvariant());
        var domain = Root(example.Host.ToLowerInvariant());
        return (host == domain || host.EndsWith("." + domain)) && uri.AbsolutePath.Trim('/').Length > 0;
    }
}

// ===================== Handlers =====================

public sealed class RegisterBusinessHandler(IUnitOfWork uow, IIdentityService identity, IPhoneOtpService otp)
    : IRequestHandler<RegisterBusinessCommand, BusinessRegistrationResultDto>
{
    public async Task<BusinessRegistrationResultDto> Handle(RegisterBusinessCommand r, CancellationToken ct)
    {
        var factory = new BusinessFactory(uow);
        var resolved = await factory.ResolveAsync(r.Business, ct);
        var phone = Phones.Normalize(r.PhoneNumber);
        if (!await identity.IsEmailAvailableAsync(r.Email, ct)) throw new ConflictException("An account with this email already exists. Sign in to add your business.");
        if (await identity.IsPhoneRegisteredAsync(phone, ct)) throw AuthRules.PhoneTaken();

        // The account, business, services, social links and subscription are created together or not at all
        // (the phone verification token is only spent when everything succeeds).
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            await otp.ConsumeVerificationAsync(phone, r.PhoneVerificationToken, token);
            var auth = await identity.RegisterAsync(new RegisterRequest(r.DisplayName.Trim(), r.Email.Trim(), phone,
                Roles.BusinessOwner, r.Business.CitySlug), token);
            var business = await factory.CreateAsync(auth.User.Id, r.Business, resolved, token);
            return new BusinessRegistrationResultDto(auth, business);
        }, ct);
    }
}

public sealed class CreateOwnerBusinessHandler(IUnitOfWork uow, ICurrentUser user) : IRequestHandler<CreateOwnerBusinessCommand, CreatedBusinessDto>
{
    public async Task<CreatedBusinessDto> Handle(CreateOwnerBusinessCommand r, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenAccessException();
        var factory = new BusinessFactory(uow);
        var resolved = await factory.ResolveAsync(r.Business, ct);
        return await uow.ExecuteInTransactionAsync(token => factory.CreateAsync(userId, r.Business, resolved, token), ct);
    }
}

public sealed class EmailAvailabilityHandler(IIdentityService identity) : IRequestHandler<EmailAvailabilityQuery, bool>
{
    public Task<bool> Handle(EmailAvailabilityQuery request, CancellationToken ct) => identity.IsEmailAvailableAsync(request.Email, ct);
}

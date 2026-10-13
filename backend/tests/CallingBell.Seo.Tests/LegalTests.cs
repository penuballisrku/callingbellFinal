using CallingBell.Application.Features.Content;
using CallingBell.Application.Features.Payments;

namespace CallingBell.Seo.Tests;

/// <summary>The legal pages: company details filled into the policy text, and acceptance required before paying.</summary>
public sealed class LegalTests
{
    private static readonly Dictionary<string, string> Filled = new()
    {
        ["Brand"] = "CallInBell", ["LegalName"] = "TekOrtus Pvt. Ltd.", ["SupportEmail"] = "support@example.in",
        ["GrievanceOfficer"] = "A. Kumar, Grievance Officer", ["GrievanceEmail"] = "grievance@example.in", ["EffectiveDate"] = "13 October 2026",
    };

    [Fact]
    public void Fills_settings_into_the_text() =>
        Assert.Equal("CallInBell is operated by TekOrtus Pvt. Ltd.; write to grievance@example.in.",
            LegalText.Fill("{Brand} is operated by {LegalName}; write to {GrievanceEmail}.", Filled));

    [Fact]
    public void Empty_settings_read_as_a_real_place_to_go_never_a_blank_or_made_up_detail()
    {
        var empty = new Dictionary<string, string> { ["GrievanceEmail"] = "  ", ["RegisteredAddress"] = "" };
        Assert.Equal("Email the Contact support page.", LegalText.Fill("Email {GrievanceEmail}.", empty));
        Assert.Equal("CallInBell, address available from customer care on request", LegalText.Fill("{LegalName}, {RegisteredAddress}", empty));
        Assert.Equal("the courts at the city of our registered office", LegalText.Fill("the courts at {Jurisdiction}", empty));
    }

    [Fact]
    public void Unknown_placeholders_and_plain_text_are_left_alone()
    {
        Assert.Equal("Keep {Something} as is", LegalText.Fill("Keep {Something} as is", Filled));
        Assert.Null(LegalText.Fill(null, Filled));
    }

    [Fact]
    public void Paying_for_a_plan_requires_accepting_the_terms()
    {
        var validator = new CreatePlanOrderValidator();
        var refused = validator.Validate(new CreatePlanOrderCommand(Guid.NewGuid(), "GROWTH", "Monthly", null));
        Assert.Contains(refused.Errors, e => e.PropertyName == nameof(CreatePlanOrderCommand.AcceptTerms));
        Assert.True(validator.Validate(new CreatePlanOrderCommand(Guid.NewGuid(), "GROWTH", "Monthly", null, AcceptTerms: true)).IsValid);
    }

    [Theory]
    [InlineData("SupportEmail", "not-an-email", false)]
    [InlineData("SupportEmail", "support@callinbell.com", true)]
    [InlineData("SupportEmail", "", true)]                       // clearing a setting is allowed
    [InlineData("LegalName", "Name with {braces}", false)]       // would become a placeholder
    [InlineData("UnknownKey", "x", false)]
    public void Settings_are_checked_before_saving(string key, string value, bool valid) =>
        Assert.Equal(valid, new UpdateLegalSettingsValidator().Validate(new UpdateLegalSettingsCommand(new Dictionary<string, string?> { [key] = value })).IsValid);
}

using CallingBell.Application.Features.Search;

namespace CallingBell.Search.Tests;

/// <summary>Search suggestions: the wording of "service in a place" suggestions and the checks on what is typed.</summary>
public sealed class SearchSuggestionTests
{
    [Fact]
    public void Combined_names_the_service_and_place() =>
        Assert.Equal("AC Repair in Kukatpally", SuggestionText.Combined(" AC Repair ", "Kukatpally "));

    [Theory]
    [InlineData(0, "None listed in Kukatpally yet")]
    [InlineData(-1, "None listed in Kukatpally yet")]
    [InlineData(1, "1 business in Kukatpally")]
    [InlineData(12, "12 businesses in Kukatpally")]
    [InlineData(1500, "1,500 businesses in Kukatpally")]
    public void Count_detail_states_the_real_number_or_none(int count, string expected) =>
        Assert.Equal(expected, SuggestionText.CountDetail(count, "Kukatpally"));

    [Theory]
    [InlineData("50", true)]
    [InlineData("500072", true)]
    [InlineData("5", false)]
    [InlineData("5000721", false)]
    [InlineData("50a", false)]
    [InlineData("kuka", false)]
    [InlineData(null, false)]
    public void Postal_code_prefixes_are_two_to_six_digits(string? q, bool expected) =>
        Assert.Equal(expected, SuggestionText.IsPostalCodePrefix(q));

    [Theory]
    [InlineData("in", "IN")]
    [InlineData(" IN ", "IN")]
    [InlineData("IND", null)]
    [InlineData("1N", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Country_codes_are_two_letters(string? value, string? expected) =>
        Assert.Equal(expected, SuggestionText.CountryCode(value));

    [Theory]
    [InlineData("electricians", "electrician")]
    [InlineData("plumbers", "plumber")]
    [InlineData("bus", "bus")]
    [InlineData("ac", "ac")]
    public void Simple_plurals_match_the_singular(string q, string expected) => Assert.Equal(expected, SuggestionText.Singular(q));
}

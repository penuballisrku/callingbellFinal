using CallingBell.Application.Features.Onboarding;

namespace CallingBell.Onboarding.Tests;

/// <summary>The pure parts of "Join Calling Bell": source references, cleaning, hours, addresses and duplicate matching.</summary>
public sealed class JoinImportTests
{
    // ---------- source references ----------

    [Theory]
    [InlineData("google:ChIJAR0n0mOXyzsRbKUp9z5Bn6E", "google", "ChIJAR0n0mOXyzsRbKUp9z5Bn6E")]
    [InlineData("osm:node/123456", "osm", "node/123456")]
    [InlineData("OSM:way/42", "osm", "way/42")]
    public void Parses_valid_sources(string value, string provider, string id)
    {
        var s = JoinImport.ParseSource(value);
        Assert.NotNull(s);
        Assert.Equal(provider, s!.Provider);
        Assert.Equal(id, s.ExternalId);
        Assert.Equal($"{provider}:{id}", s.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("google")]
    [InlineData("google:short")]
    [InlineData("google:abc/../../etc")]
    [InlineData("osm:node/abc")]
    [InlineData("calling-bell:123")]
    [InlineData("javascript:alert(1)")]
    public void Rejects_invalid_sources(string? value) => Assert.Null(JoinImport.ParseSource(value));

    // ---------- text ----------

    [Fact]
    public void Clean_strips_control_characters_and_angle_brackets()
    {
        Assert.Equal("Sharma script Electricals", JoinImport.Clean("  Sharma\u0000 <script>  Electricals\n", 100)!.Replace("  ", " "));
        Assert.Null(JoinImport.Clean("   ", 10));
        Assert.Equal("abcde", JoinImport.Clean("abcdefgh", 5));
    }

    [Theory]
    [InlineData("https://www.sharmaelectricals.in/", "https://www.sharmaelectricals.in/")]
    [InlineData("https://www.google.com/search?q=sharma", null)]
    [InlineData("ftp://files.example.com", null)]
    [InlineData("not a url", null)]
    [InlineData("https://g.page/sharma", null)]
    public void Website_is_kept_only_when_it_is_a_real_site(string input, string? expected) => Assert.Equal(expected, JoinImport.CleanWebsite(input));

    [Theory]
    [InlineData("owner@sharmaelectricals.in", "owner@sharmaelectricals.in")]
    [InlineData("not-an-email", null)]
    [InlineData("a@b", null)]
    public void Email_is_validated(string input, string? expected) => Assert.Equal(expected, JoinImport.CleanEmail(input));

    [Fact]
    public void Street_address_drops_city_state_pincode_and_country()
    {
        var street = JoinImport.StreetAddress("12, MG Road, Indiranagar, Bengaluru, Karnataka 560038, India", "Bengaluru", "Karnataka", "India", "560038");
        Assert.Equal("12, MG Road, Indiranagar", street);
    }

    [Theory]
    [InlineData("500006", null, "500006")]
    [InlineData(null, "Old Mallepally, Hyderabad, Telangana 500006, India", "500006")]
    [InlineData("M5V 2T6", "Toronto, ON M5V 2T6, Canada", null)]
    public void Pincode_from_postal_code_or_address(string? postal, string? address, string? expected) =>
        Assert.Equal(expected, JoinImport.Pincode(postal, address));

    [Theory]
    [InlineData("India", "IN")]
    [InlineData("IN", "IN")]
    [InlineData("United Kingdom", "GB")]
    [InlineData("Atlantis", null)]
    public void Country_code_from_name_or_code(string input, string? expected) => Assert.Equal(expected, JoinImport.CountryCode(input));

    // ---------- hours ----------

    [Fact]
    public void Parses_google_weekday_descriptions()
    {
        var hours = JoinImport.ParseHours([
            "Monday: 9:00 AM – 6:00 PM",
            "Tuesday: 9:00 – 11:30 AM, 2:00 – 8:00 PM",
            "Saturday: Open 24 hours",
            "Sunday: Closed",
        ]);
        Assert.Equal(new ImportedHours(0, null, null, true), hours.Single(h => h.DayOfWeek == 0));
        Assert.Equal(new ImportedHours(1, "09:00", "18:00", false), hours.Single(h => h.DayOfWeek == 1));
        // Split shift: first opening to last closing; "9:00 – 11:30 AM" takes AM from the end time.
        Assert.Equal(new ImportedHours(2, "09:00", "20:00", false), hours.Single(h => h.DayOfWeek == 2));
        Assert.Equal(new ImportedHours(6, "00:00", "23:59", false), hours.Single(h => h.DayOfWeek == 6));
        Assert.DoesNotContain(hours, h => h.DayOfWeek is 3 or 4 or 5);
    }

    [Fact]
    public void Parses_simple_openstreetmap_rules()
    {
        var hours = JoinImport.ParseHours(["Mo-Fr 09:00-18:00; Sa 10:00-14:00; Su off"]);
        Assert.Equal(7, hours.Count);
        Assert.All(hours.Where(h => h.DayOfWeek is >= 1 and <= 5), h => Assert.Equal(("09:00", "18:00"), (h.Open, h.Close)));
        Assert.Equal(("10:00", "14:00"), (hours.Single(h => h.DayOfWeek == 6).Open, hours.Single(h => h.DayOfWeek == 6).Close));
        Assert.True(hours.Single(h => h.DayOfWeek == 0).IsClosed);
    }

    [Fact]
    public void Open_round_the_clock_and_past_midnight()
    {
        Assert.Equal(7, JoinImport.ParseHours(["24/7"]).Count(h => h is { Open: "00:00", Close: "23:59" }));
        // A night shift ends at midnight for that day (hours past midnight aren't stored).
        Assert.Equal(("18:00", "23:59"), JoinImport.ParseHours(["Friday: 6:00 PM – 2:00 AM"]).Select(h => (h.Open, h.Close)).Single());
    }

    [Fact]
    public void Unreadable_hours_are_left_out() => Assert.Empty(JoinImport.ParseHours(["Monday: by appointment", "PH off", ""]));

    // ---------- duplicates ----------

    [Theory]
    [InlineData("Lakeview Hospital", "Lakeview Multispeciality Hospital Pvt Ltd", 1.0)]
    [InlineData("Sharma Electricals", "SHARMA ELECTRICALS.", 1.0)]
    [InlineData("Sharma Electricals", "Gupta Plumbing Services", 0.0)]
    [InlineData("Café Mocha", "Cafe Mocha", 1.0)]
    public void Name_similarity(string a, string b, double expected) => Assert.Equal(expected, JoinImport.NameSimilarity(a, b), 2);

    [Fact]
    public void Filler_words_do_not_make_names_alike() => Assert.Equal(0, JoinImport.NameSimilarity("The Services Pvt Ltd", "Services India Pvt Ltd"));

    [Fact]
    public void Distance_in_metres()
    {
        Assert.Equal(0, JoinImport.DistanceMetres(17.385, 78.4867, 17.385, 78.4867), 3);
        // ~111 m per 0.001° of latitude.
        Assert.InRange(JoinImport.DistanceMetres(17.385, 78.4867, 17.386, 78.4867), 105, 116);
    }

    [Fact]
    public void Host_ignores_www_and_case() => Assert.Equal("example.com", JoinImport.Host("https://WWW.Example.com/about"));
}

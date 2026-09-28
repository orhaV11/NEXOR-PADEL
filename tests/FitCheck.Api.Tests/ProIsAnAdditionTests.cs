using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 16 — what Pro is for, under two rules the founder set: it may take NOTHING from the free plan, and it must
/// not cost much to serve. Those two together rule out most of what a subscription usually sells. What survives is the
/// slice of somebody's own wardrobe the stylist gets to see.
/// <para>
/// Twelve pieces is a handful. Someone who has kept sixty has a closet the stylist has never seen four fifths of, so
/// "the brown ones you wore on the 4th" only ever reaches for the same dozen. Forty is a real wardrobe — and it is
/// about 110 more input tokens, a tenth of a cent a check, which makes the thing worth paying for also the cheapest
/// thing here to give. It is the only benefit that grows the longer somebody stays.
/// </para>
/// </summary>
public class ProIsAnAdditionTests
{
    [Fact]
    public void The_free_slice_is_exactly_what_it_always_was()
    {
        // The rule the founder set: nothing is taken. Free keeps the twelve it has always had.
        Assert.Equal(12, new PlanOptions().WardrobeNamesToStylist);
        Assert.Equal(12, new PlanOptions().WardrobeNamesFor(isPro: false));
    }

    [Fact]
    public void Pro_sees_more_of_the_closet_and_can_never_see_less()
    {
        var plans = new PlanOptions();
        Assert.Equal(40, plans.WardrobeNamesFor(isPro: true));
        Assert.True(plans.WardrobeNamesFor(true) > plans.WardrobeNamesFor(false));

        // A server that sets the Pro number below the free one does not quietly give Pro the smaller slice.
        var backwards = new PlanOptions { WardrobeNamesToStylist = 12, WardrobeNamesToStylistPro = 5 };
        Assert.Equal(12, backwards.WardrobeNamesFor(isPro: true));

        // And turning the wardrobe off in the prompt turns it off for everyone, Pro included.
        Assert.Equal(0, new PlanOptions { WardrobeNamesToStylist = 0, WardrobeNamesToStylistPro = 0 }.WardrobeNamesFor(true));
    }

    [Fact]
    public async Task The_page_can_name_the_free_number_so_the_benefit_reads_as_an_addition()
    {
        using var app = new TestApp();
        var plans = (await app.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("plans");
        Assert.Equal(12, plans.GetProperty("wardrobeNames").GetInt32());
        Assert.Equal(40, plans.GetProperty("wardrobeNamesPro").GetInt32());

        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "app", "views", "pro.js")));
        Assert.Contains("free: plans.wardrobeNames", source, StringComparison.Ordinal);

        // "not the first 12" only reads as an addition if every language actually says the number.
        foreach (var code in new[] { "en", "he", "ar", "ru" })
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "i18n", code + ".json"));
            var hint = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("pro.benefit_wardrobe_hint").GetString()!;
            Assert.Contains("{free}", hint, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The cost of the whole idea, so nobody has to wonder later: a wardrobe name is a few words, and the difference
    /// between twelve of them and forty is far below the noise on one call.
    /// </summary>
    [Fact]
    public void The_addition_costs_about_a_tenth_of_a_cent()
    {
        const decimal perMillionIn = 2.00m;          // appsettings.json, claude-sonnet-5
        const int tokensPerName = 4;                 // a short lower-cased name, plus its separator
        var extraNames = new PlanOptions().WardrobeNamesFor(true) - new PlanOptions().WardrobeNamesFor(false);
        var extraCost = extraNames * tokensPerName / 1_000_000m * perMillionIn;

        Assert.Equal(28, extraNames);
        Assert.True(extraCost < 0.001m, $"the wider wardrobe costs {extraCost:F5} a check, which is no longer negligible");
    }
}

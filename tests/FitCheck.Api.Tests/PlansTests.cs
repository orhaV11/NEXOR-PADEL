using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 14 — Pro worth paying for. Two things are locked here.
/// <para>
/// <b>The page may not promise what the server cannot do.</b> <see cref="Every_promise_on_the_pro_page_maps_to_something_that_exists"/>
/// reads <c>wwwroot/app/views/pro.js</c>, pulls out every benefit it can draw and matches each one against
/// <see cref="Promises"/>: the flag from /api/config that gates it and the thing in the server that makes it true. A
/// benefit added to that page without a row here fails the build, and so does a row whose guard stopped matching. This is
/// the round's whole point: Pro sold a bigger number and nothing else, and the way back to that is an invented promise.
/// </para>
/// <para>
/// <b>Pro's day is two buckets.</b> Its comparisons have their own allowance, so deciding between two outfits never
/// spends a check; a free account keeps the one bucket it always had.
/// </para>
/// </summary>
public class PlansTests
{
    private static string ProPageSource()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "app", "views", "pro.js"));
        Assert.True(File.Exists(path), "the Pro page is not where PlansTests looks for it: " + path);
        return File.ReadAllText(path);
    }

    private static Dictionary<string, string> Strings(string code)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "i18n", code + ".json"));
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    /// <summary>
    /// What the Pro page is allowed to claim, and why each claim is true. <c>Guard</c> is the flag from /api/config that
    /// must gate the benefit on the page, "" for one that is true of every Pro account on every server. <c>Why</c> names
    /// the thing in the code that delivers it — if you cannot write that sentence, the benefit does not belong on the page.
    /// </summary>
    private static readonly Dictionary<string, (string Guard, string Why)> Promises = new(StringComparer.Ordinal)
    {
        ["pro.benefit_which"] = ("",
            "Pro's comparisons have their own rolling-day allowance (Plans:ProComparesPerDay, Plans.CompareCapFor, Allowance.Compares), counted apart from its checks on every server"),
        ["pro.benefit_taste"] = ("plans.tasteProfile",
            "the taste profile and its memory, which exist only where Plans:TasteProfile says this server has them built"),
        ["pro.benefit_wardrobe"] = ("plans.wardrobe",
            "the wardrobe reaching the stylist (Wardrobe.ForStylistAsync), which is Pro's where Plans:WardrobeNeedsPro is on"),
        ["pro.benefit_insights"] = ("plans.compareNeedsPro",
            "GET /api/users/me/insights, refused to a free account exactly where Plans:CompareNeedsPro is on")
    };

    /// <summary>The benefit lines of the page, as (guard, the keys on that line). One benefit per line, by construction.</summary>
    private static List<(string Guard, List<string> Keys)> BenefitLines(string source)
    {
        var lines = new List<(string, List<string>)>();
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();
            // The helper's own definition and any prose about it are not benefits.
            if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("function benefit", StringComparison.Ordinal) || !line.Contains("benefit(", StringComparison.Ordinal))
            {
                continue;
            }

            var call = line.IndexOf("benefit(", StringComparison.Ordinal);
            var guard = line[..call].Trim().TrimEnd('?').Trim();
            var keys = Regex.Matches(line, @"'(pro\.benefit_[a-z_]+)'").Select(m => m.Groups[1].Value).ToList();
            lines.Add((guard, keys));
        }

        return lines;
    }

    [Fact]
    public void Every_promise_on_the_pro_page_maps_to_something_that_exists()
    {
        var source = ProPageSource();
        var en = Strings("en");
        var benefits = BenefitLines(source);
        Assert.True(benefits.Count >= 3, $"only {benefits.Count} benefit lines were found; the Pro page's shape changed and this assertion no longer reads it");

        var drawn = new List<string>();
        foreach (var (guard, keys) in benefits)
        {
            Assert.NotEmpty(keys);
            // The title is the claim; the rest of the line is its hint (and its alternative wording).
            var title = keys[0];
            drawn.Add(title);
            Assert.True(Promises.ContainsKey(title),
                $"the Pro page promises \"{en.GetValueOrDefault(title, title)}\" ({title}) and PlansTests.Promises does not say what in the server makes that true. " +
                "Nothing on that page may claim a feature this app does not have: add the row, or take the promise off the page.");
            var (expectedGuard, why) = Promises[title];
            Assert.True(guard == expectedGuard,
                $"{title} is gated on \"{guard}\" but it is only true when \"{expectedGuard}\" ({why}).");
            foreach (var key in keys)
            {
                Assert.True(en.ContainsKey(key), $"the Pro page draws {key} and en.json has no such string");
            }
        }

        var stale = Promises.Keys.Except(drawn).ToList();
        Assert.True(stale.Count == 0, "PlansTests.Promises has rows for benefits the Pro page no longer draws: " + string.Join(", ", stale));

        // A promise left in the copy after the code stopped drawing it is still a promise somebody can ship by mistake.
        var orphaned = en.Keys.Where(k => k.StartsWith("pro.benefit_", StringComparison.Ordinal))
            .Select(k => k.Replace("_hint_only", "").Replace("_hint", ""))
            .Distinct()
            .Where(k => !drawn.Contains(k))
            .ToList();
        Assert.True(orphaned.Count == 0, "en.json still carries Pro benefits the page does not draw: " + string.Join(", ", orphaned));
    }

    [Fact]
    public void The_cap_is_mentioned_once_and_last_and_is_not_sold_as_a_benefit()
    {
        var source = ProPageSource();
        var en = Strings("en");

        // One fair-use line, drawn once.
        Assert.Single(Regex.Matches(source, @"'pro\.fair_use'"));
        // After every benefit: the cap is the footnote, not the pitch.
        var lastBenefit = source.LastIndexOf("benefit(", StringComparison.Ordinal);
        Assert.True(source.IndexOf("'pro.fair_use'", StringComparison.Ordinal) > lastBenefit, "the fair-use line has to come after the benefits");
        // And it is not a benefit row itself.
        Assert.DoesNotContain(Promises.Keys, key => key.Contains("fair", StringComparison.Ordinal));
        // It names both allowances, because Pro's day is two buckets.
        Assert.Contains("{checks}", en["pro.fair_use"], StringComparison.Ordinal);
        Assert.Contains("{compares}", en["pro.fair_use"], StringComparison.Ordinal);
        foreach (var code in new[] { "he", "ar", "ru" })
        {
            var text = Strings(code)["pro.fair_use"];
            Assert.Contains("{checks}", text, StringComparison.Ordinal);
            Assert.Contains("{compares}", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Config_publishes_the_allowances_as_the_server_really_enforces_them()
    {
        // Both plan numbers above the ceiling: what is published has to be what a Pro account actually gets.
        using var app = new TestApp
        {
            ChecksPerDay = 12,
            Settings = { ["Plans:ProChecksPerDay"] = "40", ["Plans:ProComparesPerDay"] = "40" }
        };
        var config = await (await app.NewClient().GetAsync("/api/config")).Content.ReadFromJsonAsync<JsonElement>();
        var plans = config.GetProperty("plans");
        Assert.Equal(12, plans.GetProperty("proChecksPerDay").GetInt32());
        Assert.Equal(12, plans.GetProperty("proComparesPerDay").GetInt32());
        // The wardrobe is Pro's by default, and the taste profile is not claimed until a server says it has it.
        Assert.True(plans.GetProperty("wardrobe").GetBoolean());
        Assert.False(plans.GetProperty("tasteProfile").GetBoolean());
    }

    [Fact]
    public void A_taste_profile_is_never_claimed_by_default()
    {
        // The Pro page draws pro.benefit_taste only behind plans.tasteProfile, and the setting is off out of the box:
        // turning it on is an operator saying, in configuration, that this server has the feature.
        Assert.False(new PlanOptions().TasteProfile);
        Assert.Equal("plans.tasteProfile", Promises["pro.benefit_taste"].Guard);
    }

    [Fact]
    public void Pro_splits_its_day_and_free_keeps_the_one_bucket()
    {
        var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var plans = new PlanOptions { FreeChecksPerDay = 3, ProChecksPerDay = 30, ProComparesPerDay = 10 };
        var limits = new LimitsOptions { ChecksPerDay = 20 };
        var free = new AppUser { Plan = Plans.Free };
        var pro = new AppUser { Plan = Plans.Pro, ProUntil = now.AddDays(1) };
        var lapsed = new AppUser { Plan = Plans.Pro, ProUntil = now.AddMinutes(-1) };

        Assert.Equal(Allowance.Together, Plans.CheckAllowanceFor(free, now));
        Assert.Equal(Allowance.Together, Plans.CompareAllowanceFor(free, now));
        Assert.Equal(Allowance.Checks, Plans.CheckAllowanceFor(pro, now));
        Assert.Equal(Allowance.Compares, Plans.CompareAllowanceFor(pro, now));
        // A guest has one look, not two buckets; a lapsed Pro is a free account again.
        Assert.Equal(Allowance.Together, Plans.CheckAllowanceFor(null, now));
        Assert.Equal(Allowance.Together, Plans.CheckAllowanceFor(lapsed, now));

        // A free account's comparison cap IS its check cap: the same allowance, as before Round 14.
        Assert.Equal(3, Plans.CompareCapFor(free, plans, limits, now));
        Assert.Equal(3, Plans.CapFor(free, plans, limits, now));
        // Pro's two numbers, each clamped to the ceiling.
        Assert.Equal(20, Plans.CapFor(pro, plans, limits, now));
        Assert.Equal(10, Plans.CompareCapFor(pro, plans, limits, now));
        Assert.Equal(10, Plans.ProCompareCap(plans, limits));
        Assert.Equal(5, Plans.ProCompareCap(plans, new LimitsOptions { ChecksPerDay = 5 }));
    }

    [Fact]
    public void The_wardrobe_reaches_the_stylist_only_for_the_plan_that_bought_it()
    {
        var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var sold = new PlanOptions { WardrobeNeedsPro = true };
        var given = new PlanOptions { WardrobeNeedsPro = false };
        var free = new AppUser { Plan = Plans.Free };
        var pro = new AppUser { Plan = Plans.Pro, ProUntil = now.AddDays(1) };

        Assert.False(Plans.WardrobeReachesStylist(free, sold, now));
        Assert.True(Plans.WardrobeReachesStylist(pro, sold, now));
        Assert.True(Plans.WardrobeReachesStylist(free, given, now));
        // A guest has no wardrobe on any server.
        Assert.False(Plans.WardrobeReachesStylist(null, given, now));
    }

    [Fact]
    public async Task A_pro_comparison_never_comes_out_of_the_days_checks()
    {
        using var app = new TestApp
        {
            ChecksPerDay = 20,
            Settings = { ["Plans:ProChecksPerDay"] = "2", ["Plans:ProComparesPerDay"] = "2" }
        };
        var (client, id, _) = await app.NewUserAsync("plan_pro");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Single(u => u.Id == id);
            user.Plan = Plans.Pro;
            user.ProUntil = DateTime.UtcNow.AddDays(30);
            await db.SaveChangesAsync();
        }

        app.Vision.Handler = request => request.HasSecondImage ? OutfitComparerTests.Pick() : Payloads.Ok();

        // Two comparisons fill the comparison bucket and touch nothing else.
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/compare", CompareTests.CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).StatusCode);
        }

        var thirdCompare = await client.PostAsync("/api/compare", CompareTests.CompareForm(TestImages.Jpeg(), TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, thirdCompare.StatusCode);

        // The day's checks are untouched by all of that: this is what the Pro page promises.
        var me = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, me.GetProperty("checksToday").GetInt32());
        Assert.Equal(2, me.GetProperty("checksPerDay").GetInt32());
        await app.CheckAsync(client);
        await app.CheckAsync(client);
        var full = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, full.StatusCode);
        Assert.Equal("You've reached today's limit of 2 checks. Come back tomorrow.", await SecurityFixtures.ErrorAsync(full));
    }

    [Fact]
    public async Task A_free_accounts_comparison_still_spends_a_check()
    {
        using var app = new TestApp { FreeChecksPerDay = 2, ChecksPerDay = 20 };
        var (client, _, _) = await app.NewUserAsync("plan_free");
        app.Vision.Handler = request => request.HasSecondImage ? OutfitComparerTests.Pick() : Payloads.Ok();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/compare", CompareTests.CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).StatusCode);
        var me = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, me.GetProperty("checksToday").GetInt32());

        await app.CheckAsync(client);
        var full = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, full.StatusCode);
    }
}

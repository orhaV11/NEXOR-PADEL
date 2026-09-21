using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using FitCheck.Api.Data;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 16 — the number that decides whether a subscription pays for itself. A cap of 30 a day is a promise of 900 a
/// month, and in Pro it is 1,980, because comparisons are counted in a second bucket. At the cost of a model call
/// measured on the live server that is about $40 of stylist for one subscriber, and up to $102 once the invite bonus
/// is in — more than any consumer price, while nobody real comes near it. So the day stays as the burst limit it
/// always was, and the month is what bounds the bill.
/// </summary>
public class MonthlyAllowanceTests
{
    /// <summary>
    /// The month itself, so both bounds come from this fixture rather than from TestApp's out-of-the-way defaults.
    /// The day is left wide (TestApp's 20) on purpose: every refusal in these tests must be the month's.
    /// </summary>
    private static TestApp MonthApp(int proMonth, int freeMonth = 100000)
    {
        var app = new TestApp { ProCallsPerMonth = proMonth, FreeCallsPerMonth = freeMonth };
        app.Vision.Handler = _ => Payloads.Ok();
        return app;
    }

    private static async Task<string> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString() ?? "";

    [Fact]
    public void The_month_is_a_rolling_thirty_days_and_the_default_is_a_number_a_price_can_carry()
    {
        Assert.Equal(TimeSpan.FromDays(30), Spend.MonthWindow);
        // 150 a month at the measured $0.0204 a call is about $3 of stylist, which a subscription can carry. The old
        // daily cap alone permitted 1,980.
        Assert.Equal(150, new PlanOptions().ProCallsPerMonth);
        // The free plan is bounded too, but only in the tail: two a day is what a person feels and is set to be
        // generous, while twenty a month is what bounds the bill and is met by nobody ordinary - the day alone would
        // allow sixty. The social half of this app costs no model calls at all, so free is unlimited there.
        Assert.Equal(2, new PlanOptions().FreeChecksPerDay);
        Assert.Equal(20, new PlanOptions().FreeCallsPerMonth);
        Assert.True(new PlanOptions().FreeCallsPerMonth < new PlanOptions().FreeChecksPerDay * 30,
            "a monthly bound at or above the day x 30 bounds nothing");
        // And Pro is worth having: seven and a half times the month, not the 1.67x a day-only cap left it at.
        Assert.True(new PlanOptions().ProCallsPerMonth >= new PlanOptions().FreeCallsPerMonth * 5);
    }

    [Fact]
    public async Task A_pro_account_is_refused_once_the_month_is_spent_and_told_the_number()
    {
        using var app = MonthApp(proMonth: 2);
        var (pro, _, _) = await app.NewUserAsync("month_pro");
        await AdminSync.SetProAsync(app.ConnectionString, "month_pro", DateTime.UtcNow.AddDays(30));

        Assert.Equal(HttpStatusCode.Created, (await pro.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await pro.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);

        var refused = await pro.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Contains("2", await ErrorOf(refused), StringComparison.Ordinal);

        // And nothing was spent to find out: the third call never reached the stylist.
        Assert.Equal(2, app.Vision.Requests.Count);
    }

    /// <summary>
    /// The point of the whole change: on the daily caps alone a Pro account's comparisons are a SECOND bucket, so the
    /// two could never exhaust each other. A bill does not care which bucket a call came from, so the month counts both.
    /// </summary>
    [Fact]
    public async Task A_comparison_spends_the_month_too_and_the_two_buckets_add_up()
    {
        using var app = MonthApp(proMonth: 2);
        var (pro, _, _) = await app.NewUserAsync("month_both");
        await AdminSync.SetProAsync(app.ConnectionString, "month_both", DateTime.UtcNow.AddDays(30));

        Assert.Equal(HttpStatusCode.Created, (await pro.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);

        app.Vision.Handler = request => request.HasSecondImage ? OutfitComparerTests.Pick() : Payloads.Ok();
        var compare = await pro.PostAsync("/api/compare", CompareTests.CompareForm(TestImages.Jpeg(), TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.Created, compare.StatusCode);

        // One check and one comparison is two calls, and two is the month.
        var refusedCheck = await pro.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, refusedCheck.StatusCode);

        var refusedCompare = await pro.PostAsync("/api/compare", CompareTests.CompareForm(TestImages.Jpeg(), TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, refusedCompare.StatusCode);
        Assert.Contains("2", await ErrorOf(refusedCompare), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zero_leaves_the_plan_bounded_by_its_day_alone()
    {
        using var app = MonthApp(proMonth: 0);
        var (pro, _, _) = await app.NewUserAsync("month_off");
        await AdminSync.SetProAsync(app.ConnectionString, "month_off", DateTime.UtcNow.AddDays(30));

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await pro.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        }

        Assert.Equal(4, app.Vision.Requests.Count);
    }

    /// <summary>
    /// A free account keeps its day and nothing else by default — but the bound exists for the day the free plan needs
    /// one, and it is the same counter, so the two cannot drift apart.
    /// </summary>
    [Fact]
    public async Task The_free_plan_can_be_given_a_month_as_well()
    {
        using var app = MonthApp(proMonth: 150, freeMonth: 1);
        var (free, _, _) = await app.NewUserAsync("month_free");

        Assert.Equal(HttpStatusCode.Created, (await free.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        var refused = await free.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Contains("1", await ErrorOf(refused), StringComparison.Ordinal);
    }
}

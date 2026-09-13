using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// What the daily allowances count (<see cref="Spend"/>): checks and comparisons together, on the check route, the compare
/// route and the global ceiling alike, and the Retry-After that names the call whose expiry actually frees a permit. Each
/// test owns its app: the caps under test are global state.
/// </summary>
public class SpendTests
{
    [Fact]
    public void Retry_after_is_the_call_that_has_to_leave_the_window_for_the_count_to_drop_below_the_cap()
    {
        var now = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
        var recent = Enumerable.Range(0, 10).Select(i => now.AddHours(-23).AddMinutes(10 * i)).ToList();

        // At the cap, the oldest call is the next permit. Over it (a lapsed Pro at 10 with a cap of 3), the count must
        // drop to 2: the 8th oldest call has to go, 1 h 70 min from now.
        Assert.Equal(60 * 60, Spend.RetryAfterSeconds(recent, 10, now));
        Assert.Equal(60 * 60 + 70 * 60, Spend.RetryAfterSeconds(recent, 3, now));
        Assert.Equal(60 * 60 + 90 * 60, Spend.RetryAfterSeconds(recent, 1, now));
        // A cap of zero never frees a permit; the newest call's expiry is the most that can be said. Nothing stored: nothing to say.
        Assert.Equal(60 * 60 + 90 * 60, Spend.RetryAfterSeconds(recent, 0, now));
        Assert.Null(Spend.RetryAfterSeconds([], 3, now));
        // Never below one second, even for a call that just left the window.
        Assert.Equal(1, Spend.RetryAfterSeconds([now.AddHours(-24)], 1, now));
    }

    [Fact]
    public async Task A_comparison_counts_against_the_global_ceiling_on_the_check_route()
    {
        using var app = new TestApp { ChecksPerDayGlobal = 2 };
        app.Vision.Handler = request => request.HasSecondImage ? OutfitComparerTests.Pick() : Payloads.Ok();
        var (a, _, _) = await app.NewUserAsync("spend_a");
        var (b, _, _) = await app.NewUserAsync("spend_b");
        var (c, _, _) = await app.NewUserAsync("spend_c");

        Assert.Equal(HttpStatusCode.Created, (await a.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await b.PostAsync("/api/compare", CompareTests.CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).StatusCode);

        // One check and one comparison are two paid calls: the ceiling of two is reached for a check as it is for a comparison.
        var check = await c.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, check.StatusCode);
        Assert.Equal("OREVOSH is at capacity for today. Please try again tomorrow.", (await check.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        var comparison = await c.PostAsync("/api/compare", CompareTests.CompareForm(TestImages.Jpeg(), TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, comparison.StatusCode);
        Assert.Equal(2, app.Vision.Requests.Count);

        // A guest is under the same ceiling.
        var guest = app.NewClient();
        guest.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.77");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(2, app.Vision.Requests.Count);
    }

    [Fact]
    public async Task Retry_after_names_the_call_whose_expiry_frees_a_permit_on_both_routes()
    {
        using var app = new TestApp { FreeChecksPerDay = 3, ChecksPerDay = 30 };
        app.Vision.Handler = request => request.HasSecondImage ? OutfitComparerTests.Pick() : Payloads.Ok();
        var (client, userId, _) = await app.NewUserAsync("spend_lapsed");
        var now = DateTime.UtcNow;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Ten calls while Pro, 23 h ago and every ten minutes after; then Pro lapsed and the cap is three.
            var user = await db.Users.FindAsync(userId);
            user!.Plan = Plans.Pro;
            user.ProUntil = now.AddHours(-1);
            for (var i = 0; i < 10; i++)
            {
                db.Checks.Add(new OutfitCheck
                {
                    Id = Guid.NewGuid(), UserId = userId, Intent = StyleIntent.Casual, Language = "en", Status = CheckStatus.Ok, Score = 6, PromptVersion = "v2",
                    CreatedAt = now.AddHours(-23).AddMinutes(10 * i)
                });
            }

            await db.SaveChangesAsync();
        }

        // The count drops below three once the eighth oldest call leaves the window: in 1 h 70 min, not when the oldest does in 1 h.
        var expected = TimeSpan.FromMinutes(60 + 70);
        var check = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, check.StatusCode);
        Assert.Equal("That's today's 3 free checks. Go Pro for 30 a day, or come back tomorrow.", (await check.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.InRange(check.Headers.RetryAfter!.Delta!.Value, expected - TimeSpan.FromMinutes(2), expected);

        var comparison = await client.PostAsync("/api/compare", CompareTests.CompareForm(TestImages.Jpeg(), TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, comparison.StatusCode);
        Assert.InRange(comparison.Headers.RetryAfter!.Delta!.Value, expected - TimeSpan.FromMinutes(2), expected);
        Assert.Empty(app.Vision.Requests);

        // The day's count on "me" is the same ten, comparisons included.
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(10, me.GetProperty("checksToday").GetInt32());
        Assert.Equal(3, me.GetProperty("checksPerDay").GetInt32());
    }
}

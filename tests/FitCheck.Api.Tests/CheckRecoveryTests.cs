using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Tests;

/// <summary>
/// A check that was interrupted. The stylist's minute is the longest wait in the app and a phone can take the screen away
/// in the middle of it — iOS discards a backgrounded tab, a locked phone suspends the page, an app is swiped away, a lift
/// eats the connection while the answer is on the wire. The row is written and it is the person's; what was lost is only
/// the answer's journey back. GET /api/checks/latest is the way back to it, with no id in the address, because the client
/// that was holding the id is the thing that died — and for a guest it is the difference between their one free look and
/// nothing at all.
/// <para>
/// The rule is the one GET /api/checks/{id} already keeps: the signed-in caller's own row, or, signed out, the row this
/// browser's own guest cookie made. Everyone else gets the 404 of a check that does not exist.
/// </para>
/// </summary>
public class CheckRecoveryTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public CheckRecoveryTests(TestApp app) => _app = app;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Moves a stored check back in time, the way waiting would have: a test cannot sit for twenty minutes.</summary>
    private async Task AgeAsync(Guid checkId, TimeSpan by)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var check = await db.Checks.SingleAsync(c => c.Id == checkId);
        check.CreatedAt -= by;
        await db.SaveChangesAsync();
    }

    // ---- 1. the signed-in recovery ----

    [Fact]
    public async Task The_verdict_a_phone_never_saw_is_still_there_for_the_account_that_asked_for_it()
    {
        var (client, _, _) = await _app.NewUserAsync("recover_me");
        var made = await Json(await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg())));
        var id = made.GetProperty("id").GetGuid();

        // What the phone does on the next boot: "my newest check, and I have been waiting two minutes".
        var latest = await Json(await client.GetAsync("/api/checks/latest?withinSeconds=120"));

        Assert.Equal(id, latest.GetProperty("id").GetGuid());
        // Everything the result screen draws comes back with it, or the recovery lands on an empty verdict.
        Assert.Equal("ok", latest.GetProperty("status").GetString());
        Assert.Equal(made.GetProperty("score").GetInt32(), latest.GetProperty("score").GetInt32());
        Assert.Equal(
            made.GetProperty("feedback").GetProperty("headline").GetString(),
            latest.GetProperty("feedback").GetProperty("headline").GetString());
        Assert.Equal(made.GetProperty("occasion").GetString(), latest.GetProperty("occasion").GetString());
    }

    [Fact]
    public async Task The_newest_is_what_comes_back_and_a_look_already_posted_carries_its_post()
    {
        var (client, _, _) = await _app.NewUserAsync("recover_newest");
        var first = await _app.CheckAsync(client);
        var post = await _app.PostAsync(client, first);
        var second = await _app.CheckAsync(client);

        Assert.Equal(second, (await Json(await client.GetAsync("/api/checks/latest"))).GetProperty("id").GetGuid());

        // And the one before it, once the newer one is out of the way: still the person's own, still carrying the look it
        // became, so a recovered result screen says "posted" instead of offering to post it a second time.
        await AgeAsync(second, TimeSpan.FromHours(2));
        var older = await Json(await client.GetAsync("/api/checks/latest"));
        Assert.Equal(first, older.GetProperty("id").GetGuid());
        Assert.Equal(post.GetProperty("id").GetGuid(), older.GetProperty("postId").GetGuid());
    }

    // ---- 2. the guest recovery: the whole reason the route has no id ----

    [Fact]
    public async Task A_guests_one_free_look_survives_the_answer_being_lost()
    {
        var guest = _app.NewClient();
        var made = await Json(await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg())));

        var latest = await Json(await guest.GetAsync("/api/checks/latest?withinSeconds=90"));

        Assert.Equal(made.GetProperty("id").GetGuid(), latest.GetProperty("id").GetGuid());
        Assert.Equal("ok", latest.GetProperty("status").GetString());
        Assert.Equal(made.GetProperty("feedback").GetProperty("oneTip").GetString(), latest.GetProperty("feedback").GetProperty("oneTip").GetString());
    }

    [Fact]
    public async Task A_guest_reads_their_own_and_never_another_guests()
    {
        var one = _app.NewClient();
        var mine = (await Json(await one.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg())))).GetProperty("id").GetGuid();

        // A second visitor on the same server: their own cookie, their own check, and no way to the first one's.
        var two = _app.NewClient();
        var theirs = (await Json(await two.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg())))).GetProperty("id").GetGuid();
        Assert.NotEqual(mine, theirs);

        Assert.Equal(mine, (await Json(await one.GetAsync("/api/checks/latest"))).GetProperty("id").GetGuid());
        Assert.Equal(theirs, (await Json(await two.GetAsync("/api/checks/latest"))).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await two.GetAsync($"/api/checks/{mine}")).StatusCode);

        // And a visitor with no cookie at all — every first load — is told nothing, not even that a check exists.
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewClient().GetAsync("/api/checks/latest")).StatusCode);
    }

    [Fact]
    public async Task An_account_never_reaches_another_accounts_latest_check_this_way()
    {
        var (a, _, _) = await _app.NewUserAsync("recover_a");
        var (b, _, _) = await _app.NewUserAsync("recover_b");
        var mineA = await _app.CheckAsync(a);

        // B has never checked: their own answer is a 404, not A's newest.
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/checks/latest")).StatusCode);

        var mineB = await _app.CheckAsync(b);
        Assert.Equal(mineB, (await Json(await b.GetAsync("/api/checks/latest"))).GetProperty("id").GetGuid());
        Assert.Equal(mineA, (await Json(await a.GetAsync("/api/checks/latest"))).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_signed_in_caller_reads_rows_it_owns_and_the_guests_row_comes_with_the_claim()
    {
        var client = _app.NewClient();
        var asGuest = (await Json(await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg())))).GetProperty("id").GetGuid();
        await _app.SignupAsync(client, "recover_claimed");

        // Signed in now, and the guest cookie is still in the jar: the route reads what the ACCOUNT owns, so until the
        // claim has run the account's own answer is that it has no checks. Nothing is read by two identities at once.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/checks/latest")).StatusCode);

        // The claim is what the client runs on every sign-in (core.js claimGuestChecks): the row becomes the account's,
        // and from then on the recovery finds it as an ordinary check of theirs.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/checks/claim", null)).StatusCode);
        Assert.Equal(asGuest, (await Json(await client.GetAsync("/api/checks/latest"))).GetProperty("id").GetGuid());
    }

    // ---- 3. a wait that is over, and a window nobody can widen ----

    [Fact]
    public async Task A_wait_that_is_over_cannot_be_answered_with_an_older_verdict()
    {
        var (client, _, _) = await _app.NewUserAsync("recover_window");
        var id = await _app.CheckAsync(client);
        await AgeAsync(id, TimeSpan.FromMinutes(20));

        // The phone says how long IT has been waiting, never when it started, so a phone whose clock is wrong cannot be
        // handed this morning's verdict as this minute's: a check older than the wait is not an answer to it.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/checks/latest?withinSeconds=600")).StatusCode);

        // And the window is clamped, so no caller can widen it into last week.
        Assert.True(CheckEndpoints.MaxWithinSeconds < (int)TimeSpan.FromMinutes(20).TotalSeconds);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/checks/latest?withinSeconds=99999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/checks/latest?withinSeconds=-5")).StatusCode);

        // With no window asked for it is simply the person's newest check, whatever its age: the same row "Your checks"
        // would open, reached without an id.
        Assert.Equal(id, (await Json(await client.GetAsync("/api/checks/latest"))).GetProperty("id").GetGuid());
    }

    // ---- 4. a marker for a check that never landed ----

    [Fact]
    public async Task A_check_that_never_reached_the_server_answers_with_nothing_to_recover()
    {
        var (client, _, _) = await _app.NewUserAsync("recover_nothing");

        // Cut off while the photo was still going up: the account has no check at all, and the client's own line —
        // "we couldn't find the check you started" — is what the person reads, with the two answers back on the chips.
        var response = await client.GetAsync("/api/checks/latest?withinSeconds=60");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            _app.Services.GetRequiredService<Localizer>().Get("en", "error.check_not_found"),
            await SecurityFixtures.ErrorAsync(response));
    }

    [Fact]
    public async Task A_check_the_model_failed_on_is_not_a_verdict_to_come_back_to()
    {
        var (client, _, _) = await _app.NewUserAsync("recover_error");
        var ok = await _app.CheckAsync(client);

        // What a 502 leaves behind: a row with the error status, which costs the person nothing (Spend counts every
        // status but this one) and says nothing about their photo. The recovery steps over it to the last real verdict,
        // rather than drawing the no-outfit screen over a failure that was never about the photo.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Checks.Where(c => c.Id == ok).Select(c => c.UserId).SingleAsync();
            db.Checks.Add(new OutfitCheck
            {
                Id = Guid.NewGuid(),
                UserId = owner,
                Intent = StyleIntent.Date,
                Occasion = OutfitOccasion.Date,
                Language = "en",
                ImagePath = "recover/error.jpg",
                Status = CheckStatus.Error,
                PromptVersion = "v4",
                LatencyMs = 900,
                CreatedAt = DateTime.UtcNow.AddSeconds(5)
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(ok, (await Json(await client.GetAsync("/api/checks/latest?withinSeconds=300"))).GetProperty("id").GetGuid());
    }

    // ---- 5. the door itself ----

    [Fact]
    public async Task The_way_back_is_a_read_so_a_reopened_app_needs_no_header_and_no_script_policy_change()
    {
        var client = _app.NewClient();
        var signup = await client.PostAsJsonAsync("/api/auth/signup",
            new { handle = "recover_read", password = "password123", confirmed16Plus = true, birthDate = "1990-01-01", language = "en", accountType = "Person" });
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);
        var id = await _app.CheckAsync(client);

        // The same session, presented by a client that sends no X-Requested-With at all: a GET is a read, the CSRF rule
        // is about writes (CsrfEnumerationTests), and a recovering client is doing nothing but looking.
        var noHeader = _app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        noHeader.DefaultRequestHeaders.Add("Cookie", SecurityFixtures.SessionCookie(signup));
        var response = await noHeader.GetAsync("/api/checks/latest");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(id, (await Json(response)).GetProperty("id").GetGuid());
    }
}

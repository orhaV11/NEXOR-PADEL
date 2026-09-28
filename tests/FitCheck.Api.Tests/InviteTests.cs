using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 13 — the growth loop: invite a friend. The handle an invite link carried rides in the signup body, is stored
/// when it names an account that may invite, and hands both sides one more check for the day. Nothing here ever refuses
/// a signup: a stale or forged link must not stand between a person and an account.
/// </summary>
public class InviteTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;
    private static int _handles;

    public InviteTests(TestApp app) => _app = app;

    private static string NextHandle(string prefix) => $"{prefix}{Interlocked.Increment(ref _handles)}";

    private static async Task<T> WithDbAsync<T>(TestApp app, Func<AppDbContext, Task<T>> read)
    {
        using var scope = app.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Signs up with an invite handle in the body and returns the answer, whatever it says.</summary>
    private static Task<HttpResponseMessage> SignupAsync(HttpClient client, string handle, string? invitedBy) =>
        client.PostAsJsonAsync("/api/auth/signup",
            new { handle, password = "password123", confirmed16Plus = true, birthDate = "1990-01-01", language = "en", accountType = "Person", invitedBy });

    private static async Task<Guid> CreatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        return me.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task An_invite_is_stored_and_both_sides_get_one_more_check_today()
    {
        var inviterHandle = NextHandle("inviter");
        var (_, inviterId, _) = await _app.NewUserAsync(inviterHandle);

        var friendId = await CreatedAsync(await SignupAsync(_app.NewClient(), NextHandle("friend"), inviterHandle));

        Assert.Equal(inviterId, await WithDbAsync(_app, db => db.Users.Where(u => u.Id == friendId).Select(u => u.InvitedByUserId).FirstAsync()));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var bonuses = await WithDbAsync(_app, async db => (
            Inviter: await Counters.ReadAsync(db, Spend.BonusName(inviterId, today), CancellationToken.None),
            Friend: await Counters.ReadAsync(db, Spend.BonusName(friendId, today), CancellationToken.None)));
        Assert.Equal(1, bonuses.Inviter);
        Assert.Equal(1, bonuses.Friend);
    }

    [Fact]
    public async Task The_invite_handle_is_matched_without_case_and_the_bonus_is_one_per_pair()
    {
        var inviterHandle = NextHandle("Case");
        var (_, inviterId, _) = await _app.NewUserAsync(inviterHandle);

        await CreatedAsync(await SignupAsync(_app.NewClient(), NextHandle("a"), inviterHandle.ToUpperInvariant()));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(1, await WithDbAsync(_app, db => Counters.ReadAsync(db, Spend.BonusName(inviterId, today), CancellationToken.None)));

        // A second, different friend is a second pair: the inviter's day grows by one more, never twice for one pair.
        await CreatedAsync(await SignupAsync(_app.NewClient(), NextHandle("b"), inviterHandle));
        Assert.Equal(2, await WithDbAsync(_app, db => Counters.ReadAsync(db, Spend.BonusName(inviterId, today), CancellationToken.None)));
    }

    [Fact]
    public async Task A_handle_that_cannot_invite_is_ignored_and_the_signup_still_works()
    {
        var suspendedHandle = NextHandle("gone");
        var (_, suspendedId, _) = await _app.NewUserAsync(suspendedHandle);
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.FirstAsync(u => u.Id == suspendedId)).Suspended = true;
            await db.SaveChangesAsync();
        }

        var selfHandle = NextHandle("self");
        foreach (var (handle, invitedBy) in new[]
        {
            (NextHandle("x"), "nobody-here"),               // an account that does not exist
            (NextHandle("y"), suspendedHandle),             // a suspended account may not invite
            (NextHandle("z"), "not a handle at all!!"),     // not even a handle
            (NextHandle("w"), ""),                          // an empty field from an older client
            (selfHandle, selfHandle)                        // a self-invite is one person minting a check
        })
        {
            var id = await CreatedAsync(await SignupAsync(_app.NewClient(), handle, invitedBy));
            Assert.Null(await WithDbAsync(_app, db => db.Users.Where(u => u.Id == id).Select(u => u.InvitedByUserId).FirstAsync()));
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            Assert.Equal(0, await WithDbAsync(_app, db => Counters.ReadAsync(db, Spend.BonusName(id, today), CancellationToken.None)));
        }
    }

    [Fact]
    public async Task A_signup_with_no_invite_at_all_is_what_it_always_was()
    {
        var id = await CreatedAsync(await SignupAsync(_app.NewClient(), NextHandle("plain"), null));
        Assert.Null(await WithDbAsync(_app, db => db.Users.Where(u => u.Id == id).Select(u => u.InvitedByUserId).FirstAsync()));
    }

    [Fact]
    public async Task The_bonus_is_a_check_the_allowance_really_gives_back()
    {
        // One check a day on this server, so the extra one is the whole difference.
        using var app = new TestApp { FreeChecksPerDay = 1 };
        var inviterClient = app.NewClient();
        var inviter = await app.SignupAsync(inviterClient, "capinviter");
        Assert.NotEqual(default, inviter.GetProperty("id").GetGuid());

        var friend = app.NewClient();
        var created = await SignupAsync(friend, "capfriend", "capinviter");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var me = await created.Content.ReadFromJsonAsync<JsonElement>();
        // The cap is the plan's; the bonus shows as a day that counts one call later, so "0 of 1" still has two in it.
        Assert.Equal(1, me.GetProperty("checksPerDay").GetInt32());
        Assert.Equal(0, me.GetProperty("checksToday").GetInt32());

        // Two checks go through where a plain free account gets one.
        Assert.Equal(HttpStatusCode.Created, (await friend.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await friend.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await friend.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);

        // And a plain account on the same server still gets its one.
        var plain = app.NewClient();
        await app.SignupAsync(plain, "capplain");
        Assert.Equal(HttpStatusCode.Created, (await plain.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await plain.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
    }

    [Fact]
    public void The_bonus_counter_is_named_per_account_and_per_utc_day()
    {
        var id = Guid.Parse("11112222-3333-4444-5555-666677778888");
        Assert.Equal("bonus:11112222333344445555666677778888:20260920", Spend.BonusName(id, new DateOnly(2026, 9, 20)));
    }

    [Fact]
    public async Task The_numbers_page_shows_the_invites_sent_accepted_and_who_is_inviting()
    {
        using var app = new TestApp();
        var mod = app.NewClient();
        await app.SignupAsync(mod, "invitemod");
        await app.PromoteAsync("invitemod");

        var (_, hostId, _) = await app.NewUserAsync("invitehost");
        Assert.NotEqual(default, hostId);
        await CreatedAsync(await SignupAsync(app.NewClient(), "guestone", "invitehost"));
        await CreatedAsync(await SignupAsync(app.NewClient(), "guesttwo", "invitehost"));
        // An arrival carrying the invite link is what "sent" counts.
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient().GetAsync("/?via=invitehost")).StatusCode);

        var metrics = await (await mod.GetAsync("/api/metrics/pilot")).Content.ReadFromJsonAsync<JsonElement>();
        var invites = metrics.GetProperty("funnel").GetProperty("invites");
        Assert.Equal(2, invites.GetProperty("accepted").GetInt32());
        Assert.Equal(1, invites.GetProperty("sent").GetInt32());
        var top = invites.GetProperty("top").EnumerateArray().ToList();
        Assert.Equal("invitehost", top[0].GetProperty("handle").GetString());
        Assert.Equal(2, top[0].GetProperty("accepted").GetInt32());
    }
}

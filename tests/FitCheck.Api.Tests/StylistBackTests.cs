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
/// Round 20 — the guest at the ceiling. A 503 "the stylist is resting" offers signup with one promise: a note when the
/// stylist is back. These are the promise's mechanics (<see cref="StylistBack"/>): the flag is a Counter row set at
/// signup only while the ceiling is really closed; the five-minute pass leaves the rows alone while it still is; once the
/// day is open again each row is one in-app notification (and its push job), one mail where the address is confirmed,
/// and then it is gone, so it happens once. Each test owns its app: the ceiling is global state. Since the review fixes
/// the pass keeps the try-tip nudge's local day, so every app's clock starts at 09:00 UTC today (noon or eleven in
/// Jerusalem), and a person's row goes in a save of its own before their mail.
/// </summary>
public class StylistBackTests
{
    private const string OwnerAddress = "owner@orevosh.example";

    /// <summary>
    /// A MoneyApp with a 3 USD ceiling (two 2 USD calls close it), mail on, a public origin for the link, and the owner's
    /// alert address; its clock at 09:00 UTC today, inside the note's day in Board:TimeZone.
    /// </summary>
    private static MoneyApp Resting()
    {
        var app = new MoneyApp
        {
            Settings =
            {
                ["Limits:SpendPerDayUsd"] = "3",
                ["Email:PublicOrigin"] = "https://orevosh.example",
                ["Alerts:Email"] = OwnerAddress
            }
        };
        app.Clock.Now = DateTime.UtcNow.Date.AddHours(9);
        return app;
    }

    private static async Task SqlAsync(TestApp app, string sql)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task CloseTheDayAsync(MoneyApp app)
    {
        var (client, _, _) = await app.NewUserAsync("closer_" + Guid.NewGuid().ToString("N")[..8]);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(4.0m, (await app.TodayAsync()).EstimatedUsd);
    }

    private static HttpClient Guest(TestApp app, string ip)
    {
        var guest = app.NewClient();
        guest.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return guest;
    }

    private static async Task<JsonElement> SignupAsync(HttpClient client, string handle, bool notify)
    {
        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            handle, password = "correct horse battery", birthDate = "1990-01-01", language = "en", notifyStylistBack = notify
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<long> AskedAsync(TestApp app, Guid id)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await Counters.ReadAsync(db, StylistBack.AskedName(id), CancellationToken.None);
    }

    private static async Task<List<JsonElement>> StylistBackNotificationsAsync(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/notifications");
        return page.GetProperty("items").EnumerateArray().Where(n => n.GetProperty("type").GetString() == NotificationType.StylistBack).ToList();
    }

    private static StylistBack Worker(TestApp app) => app.Services.GetRequiredService<StylistBack>();

    private static void WithDb(TestApp app, Action<AppDbContext> action)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        action(db);
        db.SaveChanges();
    }

    [Fact]
    public async Task A_guest_who_signs_up_while_the_stylist_rests_is_told_once_when_it_is_back()
    {
        using var app = Resting();
        await CloseTheDayAsync(app);

        // The guest meets the resting stylist: 503, nothing stored, nothing spent.
        var guest = Guest(app, "203.0.113.10");
        var refused = await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        var refusal = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("The stylist is resting until tomorrow. Your look is not spent.", refusal.GetProperty("error").GetString());
        // The code is what the check screen offers the note on: any other 503 (a proxy's during a deploy) carries none.
        Assert.Equal("stylist_resting", refusal.GetProperty("code").GetString());

        // The signup with the flag records the ask, because the ceiling really is closed, and the answer says so: the
        // welcome screen promises the note on the server's record, not on having sent the flag.
        var me = await SignupAsync(guest, "resting_guest", notify: true);
        var id = me.GetProperty("id").GetGuid();
        Assert.Equal(1, await AskedAsync(app, id));
        Assert.True(me.GetProperty("stylistBackAsked").GetBoolean());
        Assert.True((await guest.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("stylistBackAsked").GetBoolean());

        // Still resting: the pass does nothing and the row waits.
        Assert.Equal(0, await Worker(app).RunAsync(CancellationToken.None));
        Assert.Equal(1, await AskedAsync(app, id));
        Assert.Empty(await StylistBackNotificationsAsync(guest));

        // Midnight (the spend rows cleared): one pass, one person told, one row of their own type with themselves as the actor.
        await app.ClearSpendAsync();
        Assert.Equal(1, await Worker(app).RunAsync(CancellationToken.None));
        var told = Assert.Single(await StylistBackNotificationsAsync(guest));
        Assert.Equal("resting_guest", told.GetProperty("actorHandle").GetString());
        Assert.Equal(0, await AskedAsync(app, id));
        Assert.False((await guest.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("stylistBackAsked").GetBoolean());

        // Once. The next pass finds no row and writes nothing more.
        Assert.Equal(0, await Worker(app).RunAsync(CancellationToken.None));
        Assert.Single(await StylistBackNotificationsAsync(guest));
        // And the check screen works again for the account.
        Assert.Equal(HttpStatusCode.Created, (await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
    }

    [Fact]
    public async Task The_flag_is_set_only_while_the_ceiling_is_really_closed()
    {
        using var app = Resting();

        // An open day: the flag is asked for, and nothing is written, because there is nothing to tell.
        var open = await SignupAsync(Guest(app, "203.0.113.11"), "open_day", notify: true);
        Assert.Equal(0, await AskedAsync(app, open.GetProperty("id").GetGuid()));
        Assert.False(open.GetProperty("stylistBackAsked").GetBoolean());

        // A closed day without the flag: an ordinary signup, no row.
        await CloseTheDayAsync(app);
        var quiet = await SignupAsync(Guest(app, "203.0.113.12"), "closed_quiet", notify: false);
        Assert.Equal(0, await AskedAsync(app, quiet.GetProperty("id").GetGuid()));

        // The same closed day with the flag: the row.
        var asked = await SignupAsync(Guest(app, "203.0.113.13"), "closed_asked", notify: true);
        Assert.Equal(1, await AskedAsync(app, asked.GetProperty("id").GetGuid()));
        Assert.True(asked.GetProperty("stylistBackAsked").GetBoolean());
        Assert.False(quiet.GetProperty("stylistBackAsked").GetBoolean());
    }

    /// <summary>
    /// Review fixes: the ceiling reopens at UTC midnight, the small hours in Board:TimeZone, and nobody asked for a phone
    /// that buzzes at three. The note keeps the try-tip nudge's day (Push:TryTipDayStart to TryTipDayEnd): at night the
    /// pass does nothing and the rows wait; the first pass of the morning tells, once, and mails, once.
    /// </summary>
    [Fact]
    public async Task The_note_waits_for_the_morning()
    {
        using var app = Resting();
        await CloseTheDayAsync(app);
        var client = Guest(app, "203.0.113.50");
        var me = await SignupAsync(client, "night_owl", notify: true);
        var id = me.GetProperty("id").GetGuid();
        WithDb(app, db =>
        {
            var user = db.Users.Single(u => u.Id == id);
            user.Email = "owl@example.test";
            user.EmailVerifiedAt = DateTime.UtcNow;
        });
        await app.ClearSpendAsync();

        // 23:30 UTC, half past one or two in Jerusalem: the day is open, and the note still waits.
        app.Clock.Now = DateTime.UtcNow.Date.AddHours(23).AddMinutes(30);
        Assert.Equal(0, await Worker(app).RunAsync(CancellationToken.None));
        Assert.Equal(1, await AskedAsync(app, id));
        Assert.Empty(await StylistBackNotificationsAsync(client));
        Assert.Empty(app.Email.To("owl@example.test"));
        Assert.False(Worker(app).InsideTheDay(app.Clock.UtcNow));

        // 07:00 UTC the next day, nine or ten in Jerusalem: told, and mailed.
        app.Clock.Now = DateTime.UtcNow.Date.AddDays(1).AddHours(7);
        Assert.True(Worker(app).InsideTheDay(app.Clock.UtcNow));
        Assert.Equal(1, await Worker(app).RunAsync(CancellationToken.None));
        Assert.Single(await StylistBackNotificationsAsync(client));
        Assert.Single(app.Email.To("owl@example.test"));
        Assert.Equal(0, await AskedAsync(app, id));
    }

    /// <summary>
    /// Review fixes: each person's row goes, with their line, in a save of its own before their mail. A save that fails
    /// keeps that row for the next pass and sends nothing; the people already told are never mailed twice. The pass used
    /// to mail everyone first and save once at the end, so one failed save mailed them all again five minutes later.
    /// </summary>
    [Fact]
    public async Task A_row_that_cannot_be_saved_waits_and_nobody_is_mailed_twice()
    {
        using var app = Resting();
        await CloseTheDayAsync(app);
        var ids = new Dictionary<string, Guid>();
        var clients = new Dictionary<string, HttpClient>();
        foreach (var (handle, ip) in new[] { ("told_once", "203.0.113.60"), ("saved_later", "203.0.113.61") })
        {
            clients[handle] = Guest(app, ip);
            ids[handle] = (await SignupAsync(clients[handle], handle, notify: true)).GetProperty("id").GetGuid();
        }

        WithDb(app, db =>
        {
            foreach (var (handle, id) in ids)
            {
                var user = db.Users.Single(u => u.Id == id);
                user.Email = handle + "@example.test";
                user.EmailVerifiedAt = DateTime.UtcNow;
            }
        });
        await app.ClearSpendAsync();

        await SqlAsync(app, "CREATE TRIGGER saved_later BEFORE INSERT ON \"Notifications\" " +
            "WHEN NEW.\"Type\" = 'stylist_back' AND NEW.\"UserId\" = (SELECT \"Id\" FROM \"Users\" WHERE \"Handle\" = 'saved_later') " +
            "BEGIN SELECT RAISE(ABORT, 'this row cannot be written'); END");
        Assert.Equal(1, await Worker(app).RunAsync(CancellationToken.None));
        Assert.Single(app.Email.To("told_once@example.test"));
        Assert.Empty(app.Email.To("saved_later@example.test"));
        Assert.Equal(0, await AskedAsync(app, ids["told_once"]));
        Assert.Equal(1, await AskedAsync(app, ids["saved_later"]));
        Assert.Empty(await StylistBackNotificationsAsync(clients["saved_later"]));

        await SqlAsync(app, "DROP TRIGGER saved_later");
        Assert.Equal(1, await Worker(app).RunAsync(CancellationToken.None));
        Assert.Single(app.Email.To("saved_later@example.test"));
        Assert.Single(await StylistBackNotificationsAsync(clients["saved_later"]));
        Assert.Equal(0, await AskedAsync(app, ids["saved_later"]));
        // Once each: the person told on the first pass has one line and one mail.
        Assert.Single(app.Email.To("told_once@example.test"));
        Assert.Single(await StylistBackNotificationsAsync(clients["told_once"]));
    }

    [Fact]
    public async Task The_mail_goes_only_to_a_confirmed_address()
    {
        using var app = Resting();
        await CloseTheDayAsync(app);

        var confirmed = await SignupAsync(Guest(app, "203.0.113.20"), "mail_confirmed", notify: true);
        var confirmedId = confirmed.GetProperty("id").GetGuid();
        var unconfirmedClient = Guest(app, "203.0.113.21");
        var unconfirmed = await SignupAsync(unconfirmedClient, "mail_pending", notify: true);
        var unconfirmedId = unconfirmed.GetProperty("id").GetGuid();
        WithDb(app, db =>
        {
            var a = db.Users.Single(u => u.Id == confirmedId);
            a.Email = "confirmed@example.test";
            a.EmailVerifiedAt = DateTime.UtcNow;
            var b = db.Users.Single(u => u.Id == unconfirmedId);
            b.Email = "pending@example.test";
            b.EmailVerifiedAt = null;
        });

        await app.ClearSpendAsync();
        var before = app.Email.Sent.Count;
        Assert.Equal(2, await Worker(app).RunAsync(CancellationToken.None));

        // One mail, to the confirmed address, with the link the origin setting builds; the other account has the in-app line and no mail.
        var mails = app.Email.Sent.Skip(before).Where(m => m.To != OwnerAddress).ToList();
        var mail = Assert.Single(mails);
        Assert.Equal("confirmed@example.test", mail.To);
        Assert.Equal("The stylist is back", mail.Subject);
        Assert.Contains("https://orevosh.example/#/check", mail.Body);
        Assert.Contains("Hi mail_confirmed,", mail.Body);
        Assert.Empty(app.Email.To("pending@example.test"));
        Assert.Single(await StylistBackNotificationsAsync(unconfirmedClient));
        Assert.Equal(0, await AskedAsync(app, confirmedId));
        Assert.Equal(0, await AskedAsync(app, unconfirmedId));
    }

    [Fact]
    public async Task A_suspended_or_deleted_account_just_loses_its_row()
    {
        using var app = Resting();
        await CloseTheDayAsync(app);

        var suspendedClient = Guest(app, "203.0.113.30");
        var suspended = await SignupAsync(suspendedClient, "asked_then_banned", notify: true);
        var suspendedId = suspended.GetProperty("id").GetGuid();
        var missingId = Guid.NewGuid();
        WithDb(app, db =>
        {
            db.Users.Single(u => u.Id == suspendedId).Suspended = true;
            // A row whose account is gone: what a deleted account leaves behind if it asked and left before the day opened.
            db.Counters.Add(new Counter { Name = StylistBack.AskedName(missingId), Value = 1 });
        });

        await app.ClearSpendAsync();
        var before = app.Email.Sent.Count;
        Assert.Equal(0, await Worker(app).RunAsync(CancellationToken.None));

        Assert.Equal(0, await AskedAsync(app, suspendedId));
        Assert.Equal(0, await AskedAsync(app, missingId));
        Assert.Equal(before, app.Email.Sent.Count);
        using var scope = app.Services.CreateScope();
        var check = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await check.Notifications.AnyAsync(n => n.Type == NotificationType.StylistBack));
    }

    /// <summary>
    /// The ceiling closes when spend reaches it and opens when the owner raises it or the day turns; the pass cannot tell
    /// the two apart and does not need to. Clearing the spend rows stands in for either. The pass on an open day does
    /// not trip the once-a-day announce inside CeilingReachedAsync: the owner's alert was raised when the day closed,
    /// and nothing new arrives when the pass finds it open.
    /// </summary>
    [Fact]
    public async Task The_ceiling_lifted_by_the_owner_counts_as_back()
    {
        using var app = Resting();
        await CloseTheDayAsync(app);
        var guest = Guest(app, "203.0.113.40");
        // The guest's 503 is what first announces the closed day to the owner.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        var me = await SignupAsync(guest, "lifted", notify: true);
        Assert.Equal(1, await AskedAsync(app, me.GetProperty("id").GetGuid()));
        // The owner's mailbox also holds the "app started" alert, so the ceiling's own sentence is what is counted.
        static bool Ceiling(EmailMessage m) => m.To == OwnerAddress && m.Body.Contains("spend ceiling", StringComparison.Ordinal);
        Assert.Single(await app.Email.WaitForAsync(Ceiling));

        await app.ClearSpendAsync();
        Assert.Equal(1, await Worker(app).RunAsync(CancellationToken.None));
        Assert.Single(await StylistBackNotificationsAsync(guest));
        Assert.Single(app.Email.Sent.Where(Ceiling));
    }
}

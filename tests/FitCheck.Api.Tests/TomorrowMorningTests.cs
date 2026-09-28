using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 20 — the morning loop behind a switch. What these lock: one push per account per local day, to the accounts the
/// compose route would answer (a subscription, two kinds of piece, no outfit for today yet, something left to spend), and
/// to nobody else; the sender never composes; the receipt row is written before the push and the unique key makes a second
/// process give way; the tap (<c>GET /api/tomorrow?from=push</c>) counts once and only after a push; the server flag, the
/// window and the person's own switch; the hour's parsing; the push in the recipient's language. The app has real VAPID
/// keys so the push goes out to the recorded push service and is decrypted; the clock is the fake one and the board's zone
/// is UTC, so 07:30 is one instant a test can name.
/// </summary>
public class TomorrowMorningTests
{
    /// <summary>Well before the hour on a Wednesday, so nothing is due until a test moves the clock.</summary>
    private static readonly DateTime EarlyMorning = new(2026, 9, 16, 5, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 9, 16);
    private const string Today = "2026-09-16";

    private static TestApp NewApp(Action<Dictionary<string, string>>? more = null, bool keys = true)
    {
        var (publicKey, privateKey) = PushSender.GenerateVapidKeys();
        var settings = new Dictionary<string, string>
        {
            ["Board:TimeZone"] = "UTC",
            ["Plans:TomorrowMorningPush"] = "true",
            ["Plans:TomorrowMorningHour"] = "07:30",
            ["Plans:FreeSuggestionsPerDay"] = "20"
        };
        more?.Invoke(settings);
        var app = keys
            ? new TestApp { PushPublicKey = publicKey, PushPrivateKey = privateKey, Settings = settings }
            : new TestApp { Settings = settings };
        app.Clock.Now = EarlyMorning;
        app.PushHandler.StatusCode = HttpStatusCode.Created;
        return app;
    }

    private static TomorrowMorning Worker(TestApp app) => app.Services.GetRequiredService<TomorrowMorning>();

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static int ComposeCalls(TestApp app) => app.Vision.Requests.Count(r => r.Tool.Name == Tomorrow.ToolName);

    private static T WithDb<T>(TestApp app, Func<AppDbContext, T> read)
    {
        using var scope = app.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task WithDbAsync(TestApp app, Func<AppDbContext, Task> action)
    {
        using var scope = app.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task Subscribe(HttpClient client, PushTests.Browser browser)
    {
        var response = await client.PostAsJsonAsync("/api/push/subscriptions", browser.Body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> KeepAsync(HttpClient client, Guid checkId, string name)
    {
        var response = await client.PostAsJsonAsync("/api/wardrobe", new { checkId, name });
        Assert.True(response.IsSuccessStatusCode, $"keep {name}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    /// <summary>One check (the test payload names White tee, Dark jeans, Running shoes) and the three pieces kept from it: three kinds.</summary>
    private static async Task ThreeKeepsAsync(TestApp app, HttpClient client)
    {
        var checkId = await app.CheckAsync(client);
        foreach (var name in new[] { "White tee", "Dark jeans", "Running shoes" })
        {
            await KeepAsync(client, checkId, name);
        }
    }

    /// <summary>A check payload naming other pieces, so a wardrobe can be one kind only.</summary>
    private static JsonElement CheckNaming(params (string Name, string Category)[] items) => Payloads.Parse(JsonSerializer.Serialize(new
    {
        status = "ok", score = 7, intent_match = 70, headline = "Layered and warm", vibe = "autumn",
        items = items.Select(i => new { name = i.Name, category = i.Category, verdict = "works", note = "Fine." }).ToArray(),
        working = new[] { "Warm" }, one_tip = "Nothing to change."
    }));

    /// <summary>A planned outfit; <paramref name="today"/> is the phone's date, which the route honours within a day of the clock's.</summary>
    private static async Task<JsonElement> ComposeAsync(HttpClient client, string when, string today = Today, string occasion = "Office")
    {
        var response = await client.PostAsJsonAsync("/api/tomorrow", new { occasion, when, today });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    /// <summary>A subscribed account with three kinds of piece: the one the morning push is for.</summary>
    private static async Task<(HttpClient Client, Guid Id, string Handle, PushTests.Browser Browser)> KeeperAsync(TestApp app, string handle, string language = "en")
    {
        var (client, id, realHandle) = await app.NewUserAsync(handle, language);
        var browser = new PushTests.Browser(handle);
        await Subscribe(client, browser);
        await ThreeKeepsAsync(app, client);
        return (client, id, realHandle, browser);
    }

    [Fact]
    public async Task Sends_one_push_per_day_to_the_right_accounts_and_never_composes()
    {
        using var app = NewApp();
        var a = await KeeperAsync(app, "tm_morning_a");
        using var aBrowser = a.Browser;

        // B: subscribed, two pieces of ONE kind: below the two-kinds minimum, the compose route would say "not enough".
        var (b, _, _) = await app.NewUserAsync("tm_morning_b");
        using var bBrowser = new PushTests.Browser("tm_morning_b");
        await Subscribe(b, bBrowser);
        app.Vision.Handler = request => request.Tool.Name == Tomorrow.ToolName ? FakeVisionClient.ByTool(request) : CheckNaming(("Tee", "top"), ("Shirt", "top"));
        var bCheck = await app.CheckAsync(b);
        await KeepAsync(b, bCheck, "Tee");
        await KeepAsync(b, bCheck, "Shirt");
        app.Vision.Handler = FakeVisionClient.ByTool;

        // C: three kinds, no subscription: nothing to push to.
        var (c, _, _) = await app.NewUserAsync("tm_morning_c");
        await ThreeKeepsAsync(app, c);

        // D: an early bird who composed today's outfit at 06:00, before the hour.
        var d = await KeeperAsync(app, "tm_morning_d");
        using var dBrowser = d.Browser;
        app.Clock.Now = new DateTime(2026, 9, 16, 6, 0, 0, DateTimeKind.Utc);
        await ComposeAsync(d.Client, when: "today");

        // E: composed last night for tomorrow, which is today: they already have today's outfit.
        var e = await KeeperAsync(app, "tm_morning_e");
        using var eBrowser = e.Browser;
        app.Clock.Now = new DateTime(2026, 9, 15, 20, 0, 0, DateTimeKind.Utc);
        await ComposeAsync(e.Client, when: "tomorrow", today: "2026-09-15");
        Assert.Equal(Day, WithDb(app, db => db.Suggestions.Single(s => s.UserId == e.Id).ForDate));

        var composesBefore = ComposeCalls(app);
        var suggestionsBefore = WithDb(app, db => db.Suggestions.Count());
        app.PushHandler.Clear();

        app.Clock.Now = new DateTime(2026, 9, 16, 7, 35, 0, DateTimeKind.Utc);
        var run = await Worker(app).RunAsync(CancellationToken.None);
        Assert.Equal(1, run.Sent);
        Assert.Equal(3, run.Skipped);   // B, D and E; C was never a candidate

        var request = Assert.Single(await app.PushHandler.WaitForAsync(a.Browser.Endpoint));
        var payload = a.Browser.Decrypt(request.Body);
        Assert.Equal(PushSender.Title, payload.GetProperty("title").GetString());
        Assert.Equal("Your outfit for today is one tap away", payload.GetProperty("body").GetString());
        Assert.Equal("/#/tomorrow?from=push", payload.GetProperty("url").GetString());
        Assert.Equal($"tomorrow_morning:{a.Handle.ToLowerInvariant()}", payload.GetProperty("tag").GetString());
        Assert.Equal(NotificationType.TomorrowMorning, payload.GetProperty("type").GetString());
        Assert.Null(request.Topic);
        // The push service keeps it only for what is left of the window (07:35 to 10:30), not the default day: a phone
        // that comes back online in the evening is not told about "today's" outfit.
        Assert.Equal("10500", request.Ttl);
        foreach (var other in new[] { bBrowser.Endpoint, dBrowser.Endpoint, eBrowser.Endpoint })
        {
            Assert.DoesNotContain(app.PushHandler.Requests, r => r.Endpoint.ToString() == other);
        }

        // The sender never composes: no model call and no suggestion row came of the run.
        Assert.Equal(composesBefore, ComposeCalls(app));
        Assert.Equal(suggestionsBefore, WithDb(app, db => db.Suggestions.Count()));

        // One receipt: A, today, not yet opened. A doorbell is not an event: no activity row was written.
        var receipt = WithDb(app, db => db.TomorrowPushes.Single());
        Assert.Equal(a.Id, receipt.UserId);
        Assert.Equal(Day, receipt.Day);
        Assert.Equal(app.Clock.Now, receipt.SentAt);
        Assert.Null(receipt.OpenedAt);
        Assert.Equal(0, WithDb(app, db => db.Notifications.Count(n => n.Type == NotificationType.TomorrowMorning)));

        // The same morning again: the row keeps it to one, whatever the timer does.
        app.Clock.Now = new DateTime(2026, 9, 16, 8, 0, 0, DateTimeKind.Utc);
        var again = await Worker(app).RunAsync(CancellationToken.None);
        Assert.Equal(0, again.Sent);
        Assert.Single(await app.PushHandler.WaitForAsync(a.Browser.Endpoint, count: 2, timeoutMs: 1500));

        // The next morning: a new day, a new row for A; and yesterday's outfits do not silence today's ping, so D (whose
        // outfit was for yesterday) and E (whose outfit was for yesterday too) hear it now. B and C stay out.
        app.Clock.Now = new DateTime(2026, 9, 17, 7, 40, 0, DateTimeKind.Utc);
        var tomorrow = await Worker(app).RunAsync(CancellationToken.None);
        Assert.Equal(3, tomorrow.Sent);
        Assert.Equal(1, tomorrow.Skipped);
        Assert.Equal(2, (await app.PushHandler.WaitForAsync(a.Browser.Endpoint, count: 2)).Count);
        Assert.Equal("10200", (await app.PushHandler.WaitForAsync(a.Browser.Endpoint, count: 2))[1].Ttl);   // 07:40 to 10:30
        Assert.Single(await app.PushHandler.WaitForAsync(dBrowser.Endpoint));
        Assert.Single(await app.PushHandler.WaitForAsync(eBrowser.Endpoint));
        Assert.Equal(2, WithDb(app, db => db.TomorrowPushes.Count(p => p.UserId == a.Id)));
        Assert.Equal(composesBefore, ComposeCalls(app));
    }

    [Fact]
    public async Task Respects_the_flag_the_window_and_the_switches()
    {
        // The server flag off: nothing, whatever the hour.
        using (var off = NewApp(s => s.Remove("Plans:TomorrowMorningPush")))
        {
            var a = await KeeperAsync(off, "tm_off");
            using var browser = a.Browser;
            off.Clock.Now = new DateTime(2026, 9, 16, 7, 35, 0, DateTimeKind.Utc);
            Assert.Equal(new TomorrowMorning.Run(0, 0, "off"), await Worker(off).RunAsync(CancellationToken.None));
            Assert.Empty(await off.PushHandler.WaitForAsync(browser.Endpoint, timeoutMs: 500));
        }

        using (var tomorrowOff = NewApp(s => s["Plans:TomorrowEnabled"] = "false"))
        {
            tomorrowOff.Clock.Now = new DateTime(2026, 9, 16, 7, 35, 0, DateTimeKind.Utc);
            Assert.Equal("tomorrow off", (await Worker(tomorrowOff).RunAsync(CancellationToken.None)).Reason);
        }

        using (var pushOff = NewApp(keys: false))
        {
            pushOff.Clock.Now = new DateTime(2026, 9, 16, 7, 35, 0, DateTimeKind.Utc);
            Assert.Equal("push off", (await Worker(pushOff).RunAsync(CancellationToken.None)).Reason);
        }

        using var app = NewApp();
        var keeper = await KeeperAsync(app, "tm_window");
        using var keeperBrowser = keeper.Browser;
        var worker = Worker(app);

        // The window: before the hour, and once the morning is gone.
        app.Clock.Now = new DateTime(2026, 9, 16, 6, 59, 0, DateTimeKind.Utc);
        Assert.Equal("not due", (await worker.RunAsync(CancellationToken.None)).Reason);
        Assert.Null(worker.Due(app.Clock.Now.Value));
        app.Clock.Now = new DateTime(2026, 9, 16, 10, 31, 0, DateTimeKind.Utc);
        Assert.Equal("not due", (await worker.RunAsync(CancellationToken.None)).Reason);
        Assert.Equal<(DateTime, DateOnly)?>((new DateTime(2026, 9, 16, 7, 30, 0, DateTimeKind.Utc), Day), worker.Due(new DateTime(2026, 9, 16, 10, 29, 0, DateTimeKind.Utc)));

        // Per person, each one the compose route's own refusal, foreseen: the switch, the wardrobe switch, a suspension.
        var (quiet, _, _) = await app.NewUserAsync("tm_quiet");
        using var quietBrowser = new PushTests.Browser("tm_quiet");
        await Subscribe(quiet, quietBrowser);
        await ThreeKeepsAsync(app, quiet);
        var switched = await Json(await quiet.PostAsJsonAsync("/api/push/morning", new { on = false }));
        Assert.False(switched.GetProperty("on").GetBoolean());

        // The wardrobe switch is Pro's to flip on a default server (Plans:WardrobeNeedsPro), so the row is written here;
        // the sender reads the row, whatever the plan, exactly as the compose route does.
        var (closed, closedId, _) = await app.NewUserAsync("tm_closed");
        using var closedBrowser = new PushTests.Browser("tm_closed");
        await Subscribe(closed, closedBrowser);
        await ThreeKeepsAsync(app, closed);
        await WithDbAsync(app, async db =>
        {
            var setting = await Wardrobe.SettingAsync(db, closedId, app.Clock.UtcNow, CancellationToken.None);
            setting.ToStylist = false;
            await db.SaveChangesAsync();
        });

        var (suspended, suspendedId, _) = await app.NewUserAsync("tm_suspended");
        using var suspendedBrowser = new PushTests.Browser("tm_suspended");
        await Subscribe(suspended, suspendedBrowser);
        await ThreeKeepsAsync(app, suspended);
        await WithDbAsync(app, db => db.Users.Where(u => u.Id == suspendedId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Suspended, true)));

        app.Clock.Now = new DateTime(2026, 9, 16, 7, 35, 0, DateTimeKind.Utc);
        var run = await worker.RunAsync(CancellationToken.None);
        Assert.Equal(1, run.Sent);
        Assert.Equal(1, run.Skipped);   // the closed wardrobe; the switch and the suspension were never candidates
        Assert.Single(await app.PushHandler.WaitForAsync(keeperBrowser.Endpoint));
        foreach (var endpoint in new[] { quietBrowser.Endpoint, closedBrowser.Endpoint, suspendedBrowser.Endpoint })
        {
            Assert.DoesNotContain(app.PushHandler.Requests, r => r.Endpoint.ToString() == endpoint);
        }

        // Tomorrow is Pro's on this server: a free account is skipped.
        using (var walled = NewApp(s => s["Plans:TomorrowNeedsPro"] = "true"))
        {
            var free = await KeeperAsync(walled, "tm_free");
            using var freeBrowser = free.Browser;
            walled.Clock.Now = new DateTime(2026, 9, 16, 7, 35, 0, DateTimeKind.Utc);
            Assert.Equal(new TomorrowMorning.Run(0, 1), await Worker(walled).RunAsync(CancellationToken.None));
            Assert.Empty(await walled.PushHandler.WaitForAsync(freeBrowser.Endpoint, timeoutMs: 500));
        }

        // Nothing left to spend: free gets one planned outfit a day here, and it went yesterday morning, inside the
        // rolling day. A push into a 429 is a broken promise.
        using (var spent = NewApp(s => s["Plans:FreeSuggestionsPerDay"] = "1"))
        {
            var one = await KeeperAsync(spent, "tm_spent");
            using var oneBrowser = one.Browser;
            spent.Clock.Now = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);
            await ComposeAsync(one.Client, when: "today", today: "2026-09-15");
            spent.Clock.Now = new DateTime(2026, 9, 16, 7, 35, 0, DateTimeKind.Utc);
            Assert.Equal(new TomorrowMorning.Run(0, 1), await Worker(spent).RunAsync(CancellationToken.None));
            Assert.Empty(await spent.PushHandler.WaitForAsync(oneBrowser.Endpoint, timeoutMs: 500));
        }
    }

    [Fact]
    public async Task The_row_is_written_before_the_push_and_a_second_process_gives_way()
    {
        using var app = NewApp();
        var a = await KeeperAsync(app, "tm_race");
        using var browser = a.Browser;
        var worker = Worker(app);

        // Another process writes the same receipt between this run's Add and its save: the unique key refuses ours, the
        // run counts a skip, nothing is queued, and nothing throws.
        var sideWrites = 0;
        worker.BeforeSave = async (userId, day, ct) =>
        {
            sideWrites++;
            await WithDbAsync(app, async db =>
            {
                db.TomorrowPushes.Add(new TomorrowPush { Id = Guid.NewGuid(), UserId = userId, Day = day, SentAt = app.Clock.UtcNow.AddSeconds(-1) });
                await db.SaveChangesAsync(ct);
            });
        };
        app.Clock.Now = new DateTime(2026, 9, 16, 7, 35, 0, DateTimeKind.Utc);
        var raced = await worker.RunAsync(CancellationToken.None);
        Assert.Equal(1, sideWrites);
        Assert.Equal(new TomorrowMorning.Run(0, 1), raced);
        Assert.Empty(await app.PushHandler.WaitForAsync(browser.Endpoint, timeoutMs: 500));
        Assert.Equal(1, WithDb(app, db => db.TomorrowPushes.Count(p => p.UserId == a.Id && p.Day == Day)));

        // A receipt already there when the run starts: not a candidate at all, still nothing sent.
        worker.BeforeSave = null;
        Assert.Equal(new TomorrowMorning.Run(0, 0), await worker.RunAsync(CancellationToken.None));
        Assert.Empty(await app.PushHandler.WaitForAsync(browser.Endpoint, timeoutMs: 500));

        // The key itself: a second row for the same account and day is refused by the database.
        await WithDbAsync(app, async db =>
        {
            db.TomorrowPushes.Add(new TomorrowPush { Id = Guid.NewGuid(), UserId = a.Id, Day = Day, SentAt = app.Clock.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });
    }

    [Fact]
    public async Task The_open_marker_counts_once_and_only_after_a_push()
    {
        using var app = NewApp();
        var a = await KeeperAsync(app, "tm_open");
        using var browser = a.Browser;
        Assert.Equal(AdminChange.Changed, await app.PromoteAsync(a.Handle));
        var composesBefore = ComposeCalls(app);

        async Task<JsonElement> Metrics()
        {
            var metrics = await a.Client.GetFromJsonAsync<JsonElement>("/api/metrics/pilot");
            return metrics.GetProperty("tomorrow");
        }

        // Before any push: the read answers as always and counts nothing; no rate when nothing was sent.
        var read = await a.Client.GetAsync("/api/tomorrow?from=push");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True((await Json(read)).GetProperty("available").GetBoolean());
        var none = await Metrics();
        Assert.Equal(0, none.GetProperty("pushesSent").GetInt32());
        Assert.Equal(0, none.GetProperty("pushesOpened").GetInt32());
        Assert.False(none.TryGetProperty("openRate", out _));

        app.Clock.Now = new DateTime(2026, 9, 16, 7, 35, 0, DateTimeKind.Utc);
        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        var sent = await Metrics();
        Assert.Equal(1, sent.GetProperty("pushesSent").GetInt32());
        Assert.Equal(0, sent.GetProperty("pushesOpened").GetInt32());
        Assert.Equal(0.0, sent.GetProperty("openRate").GetDouble());

        // A plain read and a read from somewhere else never count.
        Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync("/api/tomorrow")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync("/api/tomorrow?from=other")).StatusCode);
        Assert.Equal(0, (await Metrics()).GetProperty("pushesOpened").GetInt32());

        // The tap: once, and once only.
        app.Clock.Now = new DateTime(2026, 9, 16, 8, 10, 0, DateTimeKind.Utc);
        Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync("/api/tomorrow?from=push")).StatusCode);
        var opened = await Metrics();
        Assert.Equal(1, opened.GetProperty("pushesOpened").GetInt32());
        Assert.Equal(1.0, opened.GetProperty("openRate").GetDouble());
        Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync("/api/tomorrow?from=push")).StatusCode);
        Assert.Equal(1, (await Metrics()).GetProperty("pushesOpened").GetInt32());
        var receipt = WithDb(app, db => db.TomorrowPushes.Single(p => p.UserId == a.Id));
        Assert.Equal(new DateTime(2026, 9, 16, 8, 10, 0, DateTimeKind.Utc), receipt.OpenedAt);

        // Reading is never a model call, from a push or not.
        Assert.Equal(composesBefore, ComposeCalls(app));

        // A receipt older than a day is not what a tap today is about: it stays unopened.
        Assert.False(await StaleReceiptIsOpenedAsync(app, a.Id, new DateTime(2026, 9, 17, 9, 0, 0, DateTimeKind.Utc)));
    }

    private static async Task<bool> StaleReceiptIsOpenedAsync(TestApp app, Guid userId, DateTime at)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // A fresh, unopened receipt from before the window: too old to be what a tap today is about.
        db.TomorrowPushes.Add(new TomorrowPush { Id = Guid.NewGuid(), UserId = userId, Day = new DateOnly(2026, 9, 14), SentAt = at.AddHours(-25) });
        await db.SaveChangesAsync();
        return await TomorrowMorning.MarkOpenedAsync(db, userId, at, CancellationToken.None);
    }

    [Fact]
    public async Task The_push_speaks_the_recipients_language()
    {
        using var app = NewApp();
        var dana = await KeeperAsync(app, "tm_dana", language: "he");
        using var browser = dana.Browser;
        app.Clock.Now = new DateTime(2026, 9, 16, 7, 35, 0, DateTimeKind.Utc);
        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        var request = Assert.Single(await app.PushHandler.WaitForAsync(browser.Endpoint));
        var payload = browser.Decrypt(request.Body);
        Assert.Equal("הלוק שלך להיום במרחק לחיצה אחת", payload.GetProperty("body").GetString());
        Assert.Equal("/#/tomorrow?from=push", payload.GetProperty("url").GetString());
    }

    [Fact]
    public void DST_and_hour_parsing()
    {
        // Israel is three hours ahead in September: 07:30 local is 04:30Z, and the minute matters.
        using var app = NewApp(s => s["Board:TimeZone"] = "Asia/Jerusalem");
        var worker = Worker(app);
        Assert.Null(worker.Due(new DateTime(2026, 9, 16, 4, 29, 0, DateTimeKind.Utc)));
        Assert.Equal<(DateTime, DateOnly)?>((new DateTime(2026, 9, 16, 4, 30, 0, DateTimeKind.Utc), Day), worker.Due(new DateTime(2026, 9, 16, 4, 31, 0, DateTimeKind.Utc)));
        // The local day is the row key: at 22:30Z it is already tomorrow in Israel, and tomorrow's hour has not come.
        Assert.Null(worker.Due(new DateTime(2026, 9, 16, 22, 30, 0, DateTimeKind.Utc)));

        // A clock that jumps forward over the hour: the next hour that exists is when the push goes. Israel's clocks go
        // from 02:00 to 03:00 on that Friday, so 02:30 becomes 03:30 summer time, which is 00:30Z.
        var jerusalem = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
        var spring = new DateOnly(2026, 3, 27);
        Assert.Equal(new DateTime(2026, 3, 27, 0, 30, 0, DateTimeKind.Utc), Board.LocalToUtc(spring, new TimeOnly(2, 30), jerusalem));
        Assert.Equal(new DateTime(2026, 3, 27, 4, 30, 0, DateTimeKind.Utc), Board.LocalToUtc(spring, new TimeOnly(7, 30), jerusalem));

        // The hour: HH:mm exactly, or 07:30.
        Assert.Equal(new TimeOnly(6, 45), TomorrowMorning.HourOf(new PlanOptions { TomorrowMorningHour = "06:45" }));
        Assert.Equal(new TimeOnly(6, 45), TomorrowMorning.HourOf(new PlanOptions { TomorrowMorningHour = " 06:45 " }));
        Assert.Equal(TomorrowMorning.DefaultHour, TomorrowMorning.HourOf(new PlanOptions { TomorrowMorningHour = "7:30" }));
        Assert.Equal(TomorrowMorning.DefaultHour, TomorrowMorning.HourOf(new PlanOptions { TomorrowMorningHour = "nonsense" }));
        Assert.Equal(TomorrowMorning.DefaultHour, TomorrowMorning.HourOf(new PlanOptions { TomorrowMorningHour = "" }));
        Assert.False(TomorrowMorning.HourIsValid(new PlanOptions { TomorrowMorningHour = "7:30" }));
        Assert.True(TomorrowMorning.HourIsValid(new PlanOptions()));
    }

    [Fact]
    public async Task The_switch_route()
    {
        using var app = NewApp();
        var (client, _, _) = await app.NewUserAsync("tm_switch");
        var localizer = app.Services.GetRequiredService<Localizer>();

        var state = await client.GetFromJsonAsync<JsonElement>("/api/push/morning");
        Assert.True(state.GetProperty("on").GetBoolean());
        Assert.True(state.GetProperty("offered").GetBoolean());
        Assert.Equal("07:30", state.GetProperty("hour").GetString());

        var off = await client.PostAsJsonAsync("/api/push/morning", new { on = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.False((await Json(off)).GetProperty("on").GetBoolean());
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/push/morning")).GetProperty("on").GetBoolean());
        Assert.True((await Json(await client.PostAsJsonAsync("/api/push/morning", new { on = true }))).GetProperty("on").GetBoolean());

        var empty = await client.PostAsJsonAsync("/api/push/morning", new { });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(localizer.Get("en", "error.invalid_request"), (await Json(empty)).GetProperty("error").GetString());

        // Signed out: the group requires a session.
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.NewClient().GetAsync("/api/push/morning")).StatusCode);

        // A server that does not offer it: the state says so, and the switch cannot be set (a promise nothing keeps).
        using var plain = new TestApp();
        var (visitor, _, _) = await plain.NewUserAsync("tm_plain");
        var notOffered = await visitor.GetFromJsonAsync<JsonElement>("/api/push/morning");
        Assert.True(notOffered.GetProperty("on").GetBoolean());
        Assert.False(notOffered.GetProperty("offered").GetBoolean());
        var refused = await visitor.PostAsJsonAsync("/api/push/morning", new { on = false });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(localizer.Get("en", "error.tomorrow_push_off"), (await Json(refused)).GetProperty("error").GetString());

        // The url and the tag the push carries, with no id behind it: the tap lands on the screen, repeats collapse per person.
        var job = new PushJob(Guid.NewGuid(), NotificationType.TomorrowMorning, "Handle", null, null);
        Assert.Equal("/#/tomorrow?from=push", PushSender.UrlFor(job));
        Assert.Equal("tomorrow_morning:handle", PushSender.TagFor(job));

        // A job's own time to live, in whole seconds between "now or never" and the default day; none is the default.
        Assert.Null(PushSender.TimeToLiveFor(job));
        Assert.Equal(10500, PushSender.TimeToLiveFor(job with { TimeToLive = TimeSpan.FromMinutes(175) }));
        Assert.Equal(1, PushSender.TimeToLiveFor(job with { TimeToLive = TimeSpan.FromMilliseconds(200) }));
        Assert.Equal(0, PushSender.TimeToLiveFor(job with { TimeToLive = TimeSpan.FromMinutes(-5) }));
        Assert.Equal(PushSender.TimeToLiveSeconds, PushSender.TimeToLiveFor(job with { TimeToLive = TimeSpan.FromDays(3) }));
    }

    /// <summary>
    /// The sender ticks every quarter hour through its three-hour window, and every tick re-checks the accounts it skipped.
    /// Its run line is written at Information once a morning (the day's first pass) and again only by a pass that pushed
    /// someone, so the log DEPLOY.md describes is a line a morning, not twelve.
    /// </summary>
    [Fact]
    public async Task The_run_line_is_written_once_a_morning_and_again_only_by_a_pass_that_sends()
    {
        using var app = NewApp();
        await app.NewUserAsync("tm_log_first");   // the host is up (its own first pass ran at 05:00, not due) before the clock moves
        var provider = new RecordingLoggerProvider();
        using var logs = LoggerFactory.Create(builder => builder.AddProvider(provider));   // Information and up, as a server logs
        var worker = new TomorrowMorning(
            app.Services.GetRequiredService<IServiceScopeFactory>(), app.Services.GetRequiredService<Board>(), app.Services.GetRequiredService<IClock>(),
            app.Services.GetRequiredService<PushSender>(), app.Services.GetRequiredService<IOptions<PlanOptions>>(),
            app.Services.GetRequiredService<IOptions<LimitsOptions>>(), logs.CreateLogger<TomorrowMorning>());
        List<string> RunLines() => provider.Lines.Select(l => l.Message).Where(m => m.StartsWith("TomorrowMorning: run at ", StringComparison.Ordinal)).ToList();

        // Nobody to push yet: the day's first pass still says so once; the next quarter hour is quiet.
        app.Clock.Now = new DateTime(2026, 9, 16, 7, 31, 0, DateTimeKind.Utc);
        Assert.Equal(0, (await worker.RunAsync(CancellationToken.None)).Sent);
        app.Clock.Now = new DateTime(2026, 9, 16, 7, 46, 0, DateTimeKind.Utc);
        Assert.Equal(0, (await worker.RunAsync(CancellationToken.None)).Sent);
        Assert.Single(RunLines());

        // Someone subscribes inside the window: the pass that pushes them writes its own line, the one after it does not.
        var late = await KeeperAsync(app, "tm_log_late");
        using var lateBrowser = late.Browser;
        app.Clock.Now = new DateTime(2026, 9, 16, 8, 1, 0, DateTimeKind.Utc);
        Assert.Equal(1, (await worker.RunAsync(CancellationToken.None)).Sent);
        app.Clock.Now = new DateTime(2026, 9, 16, 8, 16, 0, DateTimeKind.Utc);
        Assert.Equal(0, (await worker.RunAsync(CancellationToken.None)).Sent);
        Assert.Equal(2, RunLines().Count);
        Assert.Contains(", 1 sent, ", RunLines()[1]);

        // The next morning starts over: one line for its first pass, none for the tick after.
        app.Clock.Now = new DateTime(2026, 9, 17, 7, 31, 0, DateTimeKind.Utc);
        Assert.Equal(1, (await worker.RunAsync(CancellationToken.None)).Sent);
        app.Clock.Now = new DateTime(2026, 9, 17, 7, 46, 0, DateTimeKind.Utc);
        Assert.Equal(0, (await worker.RunAsync(CancellationToken.None)).Sent);
        Assert.Equal(3, RunLines().Count);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 20 — "Did you try the tip?": the day-after nudge. What these lock: one nudge per check, ever; one per person a
/// day, on the newest unanswered check; only inside the window (a day after the verdict, for a day) and inside the local
/// day; only for a change tip nobody answered, on a check that is not half of a pair, to an account with a push
/// subscription; and the push that goes with the row carries the check so the tap lands on it. The app has real VAPID
/// keys (the same fixture PushTests uses) so the push actually goes out to the recorded push service and is decrypted.
/// Every test seeds its checks at its own week so their windows never meet; the clock is the fake one throughout.
/// </summary>
public class TryTipNudgeTests : IClassFixture<PushTests.PushApp>
{
    private readonly PushTests.PushApp _fixture;
    private TestApp App => _fixture.App;
    private RecordingPushHandler Pushes => _fixture.App.PushHandler;
    private TryTipNudge Nudge => App.Services.GetRequiredService<TryTipNudge>();

    public TryTipNudgeTests(PushTests.PushApp fixture)
    {
        _fixture = fixture;
        App.Vision.Handler = _ => Payloads.Ok();
        Pushes.StatusCode = HttpStatusCode.Created;
    }

    /// <summary>A verdict whose tip is a keep: nothing to try.</summary>
    private static JsonElement Keep() => Payloads.Parse("""
        { "status": "ok", "score": 9, "intent_match": 92, "headline": "Deliberate, top to bottom", "vibe": "quiet confidence",
          "items": [{ "name": "Brown boots", "category": "shoes", "verdict": "works", "note": "They set the palette." }],
          "working": ["The palette is decided"], "one_tip": "Keep the brown boots: they hold this together.", "tip_kind": "keep" }
        """);

    private async Task Subscribe(HttpClient client, PushTests.Browser browser)
    {
        var response = await client.PostAsJsonAsync("/api/push/subscriptions", browser.Body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>A check made now, then moved to <paramref name="at"/>: the route stamps the wall clock, the tests speak the fake one.</summary>
    private async Task<Guid> CheckAtAsync(HttpClient client, DateTime at)
    {
        var id = await App.CheckAsync(client);
        await MoveAsync(id, at);
        return id;
    }

    private async Task MoveAsync(Guid checkId, DateTime at)
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Checks.Where(c => c.Id == checkId).ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, at));
    }

    private static async Task<List<JsonElement>> TryTipRows(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/notifications");
        return page.GetProperty("items").EnumerateArray().Where(n => n.GetProperty("type").GetString() == NotificationType.TryTip).ToList();
    }

    [Fact]
    public async Task A_change_tip_untried_for_a_day_gets_one_nudge_row_one_push_and_never_a_second()
    {
        var (client, _, handle) = await App.NewUserAsync("nudge_one");
        using var browser = new PushTests.Browser("nudge_one");
        await Subscribe(client, browser);
        var t0 = new DateTime(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc);
        var checkId = await CheckAtAsync(client, t0);

        // A day and an hour later, noon in Jerusalem.
        App.Clock.Now = t0.AddHours(25);
        Assert.Equal(1, await Nudge.RunAsync(CancellationToken.None));

        var row = Assert.Single(await TryTipRows(client));
        Assert.Equal(checkId, row.GetProperty("checkId").GetGuid());
        Assert.Equal(handle, row.GetProperty("actorHandle").GetString());
        Assert.False(row.GetProperty("read").GetBoolean());
        Assert.False(row.TryGetProperty("postId", out _));

        var request = Assert.Single(await Pushes.WaitForAsync(browser.Endpoint));
        var payload = browser.Decrypt(request.Body);
        Assert.Equal("Did you try the tip? Show me the look after the change.", payload.GetProperty("body").GetString());
        Assert.Equal($"/#/checks?try={checkId:D}", payload.GetProperty("url").GetString());
        Assert.Equal($"try_tip:{checkId:N}", payload.GetProperty("tag").GetString());
        Assert.StartsWith("try_tip-", request.Topic);

        // The same hour again, and the next: the check has its nudge and never gets a second.
        Assert.Equal(0, await Nudge.RunAsync(CancellationToken.None));
        App.Clock.Now = t0.AddHours(30);
        Assert.Equal(0, await Nudge.RunAsync(CancellationToken.None));
        Assert.Single(await TryTipRows(client));
        await Task.Delay(200);
        Assert.Single(Pushes.Requests, r => r.Endpoint.ToString() == browser.Endpoint);
    }

    [Fact]
    public async Task Nothing_is_nudged_without_a_subscription_for_a_keep_tip_for_an_answered_check_or_for_a_paired_check()
    {
        var t0 = new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);

        // No subscription: an activity row nobody would be told about is a row nobody asked for.
        var (silent, _, _) = await App.NewUserAsync("nudge_silent");
        await CheckAtAsync(silent, t0);

        // A keep tip: nothing to try.
        var (kept, _, _) = await App.NewUserAsync("nudge_keep");
        using var keptBrowser = new PushTests.Browser("nudge_keep");
        await Subscribe(kept, keptBrowser);
        App.Vision.Handler = _ => Keep();
        await CheckAtAsync(kept, t0);
        App.Vision.Handler = _ => Payloads.Ok();

        // Answered: "not my style" means they will not try it, and worked or didn't-work would mean they already did.
        var (answered, _, _) = await App.NewUserAsync("nudge_answered");
        using var answeredBrowser = new PushTests.Browser("nudge_answered");
        await Subscribe(answered, answeredBrowser);
        var answeredCheck = await CheckAtAsync(answered, t0);
        Assert.Equal(HttpStatusCode.OK, (await answered.PostAsJsonAsync($"/api/checks/{answeredCheck}/useful", new { reason = "not_my_style" })).StatusCode);

        // Already the before of a pair: they tried it, and showed it.
        var (paired, _, _) = await App.NewUserAsync("nudge_paired");
        using var pairedBrowser = new PushTests.Browser("nudge_paired");
        await Subscribe(paired, pairedBrowser);
        var before = await App.CheckAsync(paired);
        var after = await App.CheckAsync(paired);
        Assert.Equal(HttpStatusCode.Created, (await paired.PostAsJsonAsync($"/api/checks/{after}/tried", new { beforeId = before })).StatusCode);
        await MoveAsync(before, t0);
        await MoveAsync(after, t0.AddMinutes(30));

        App.Clock.Now = t0.AddHours(25);
        Assert.Equal(0, await Nudge.RunAsync(CancellationToken.None));
        foreach (var client in new[] { silent, kept, answered, paired })
        {
            Assert.Empty(await TryTipRows(client));
        }
    }

    [Fact]
    public async Task The_window_is_a_day_after_a_day()
    {
        var (client, _, _) = await App.NewUserAsync("nudge_window");
        using var browser = new PushTests.Browser("nudge_window");
        await Subscribe(client, browser);
        var t0 = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc);
        await CheckAtAsync(client, t0);

        // Too soon: the day is not over. Too late: a server that was down for two days does not nudge about the day before yesterday.
        App.Clock.Now = t0.AddHours(23);
        Assert.Equal(0, await Nudge.RunAsync(CancellationToken.None));
        App.Clock.Now = t0.AddHours(49);
        Assert.Equal(0, await Nudge.RunAsync(CancellationToken.None));
        Assert.Empty(await TryTipRows(client));

        App.Clock.Now = t0.AddHours(30);
        Assert.Equal(1, await Nudge.RunAsync(CancellationToken.None));
        Assert.Single(await TryTipRows(client));
    }

    [Fact]
    public async Task One_nudge_per_account_per_day_and_the_newest_check_carries_it()
    {
        var (client, _, _) = await App.NewUserAsync("nudge_daily");
        using var browser = new PushTests.Browser("nudge_daily");
        await Subscribe(client, browser);
        var t0 = new DateTime(2026, 8, 31, 8, 0, 0, DateTimeKind.Utc);
        await CheckAtAsync(client, t0);
        var newer = await CheckAtAsync(client, t0.AddHours(2));

        App.Clock.Now = t0.AddHours(26);
        Assert.Equal(1, await Nudge.RunAsync(CancellationToken.None));
        var row = Assert.Single(await TryTipRows(client));
        Assert.Equal(newer, row.GetProperty("checkId").GetGuid());

        // The older check is still inside its window, but the person heard from us an hour ago.
        App.Clock.Now = t0.AddHours(27);
        Assert.Equal(0, await Nudge.RunAsync(CancellationToken.None));
        Assert.Single(await TryTipRows(client));
    }

    [Fact]
    public async Task Quiet_hours_hold_the_nudge_until_morning()
    {
        var (client, _, _) = await App.NewUserAsync("nudge_night");
        using var browser = new PushTests.Browser("nudge_night");
        await Subscribe(client, browser);
        // 20:00Z is 23:00 in Jerusalem (the default Board:TimeZone, on summer time); the check is a day old at 02:00 local.
        var t0 = new DateTime(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc);
        await CheckAtAsync(client, t0);

        App.Clock.Now = new DateTime(2026, 8, 25, 23, 0, 0, DateTimeKind.Utc);   // 02:00 local: quiet
        Assert.Equal(0, await Nudge.RunAsync(CancellationToken.None));
        Assert.Empty(await TryTipRows(client));

        App.Clock.Now = new DateTime(2026, 8, 26, 6, 0, 0, DateTimeKind.Utc);    // 09:00 local: the morning's run
        Assert.Equal(1, await Nudge.RunAsync(CancellationToken.None));
        Assert.Single(await TryTipRows(client));
    }

    [Fact]
    public async Task Push_TryTipNudge_off_writes_nothing_and_a_guest_check_is_never_a_candidate()
    {
        var t0 = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);

        using (var off = new TestApp { PushPublicKey = _fixture.PublicKey, PushPrivateKey = _fixture.PrivateKey, Settings = { ["Push:TryTipNudge"] = "false" } })
        {
            off.Vision.Handler = _ => Payloads.Ok();
            var (client, _, _) = await off.NewUserAsync("nudge_off");
            using var browser = new PushTests.Browser("nudge_off");
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/push/subscriptions", browser.Body)).StatusCode);
            var checkId = await off.CheckAsync(client);
            using (var scope = off.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Checks.Where(c => c.Id == checkId).ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, t0));
            }

            off.Clock.Now = t0.AddHours(25);
            Assert.Equal(0, await off.Services.GetRequiredService<TryTipNudge>().RunAsync(CancellationToken.None));
            Assert.Empty(await TryTipRows(client));
        }

        // On, and a guest's check in the same window: a guest has no account to nudge, no subscription, and no pairs.
        var guest = App.NewClient();
        var created = await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var guestCheck = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await MoveAsync(guestCheck, t0);
        App.Clock.Now = t0.AddHours(25);
        Assert.Equal(0, await Nudge.RunAsync(CancellationToken.None));
        using (var scope = App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Notifications.AnyAsync(n => n.CheckId == guestCheck));
        }
    }

    [Fact]
    public void The_hosted_service_is_registered_and_the_worker_is_one_singleton()
    {
        Assert.Contains(App.Services.GetServices<IHostedService>(), s => s is TryTipNudgeService);
        Assert.Same(App.Services.GetRequiredService<TryTipNudge>(), App.Services.GetRequiredService<TryTipNudge>());
        Assert.Equal(TimeSpan.FromHours(1), TryTipNudgeService.Interval);
    }

    [Fact]
    public void The_change_tip_rule_reads_the_stored_verdict()
    {
        Assert.True(TryTipNudge.HasChangeTip("""{"status":"ok","oneTip":"Swap the shoes."}"""));
        Assert.True(TryTipNudge.HasChangeTip("""{"status":"ok","oneTip":"Swap the shoes.","tipKind":"change"}"""));
        Assert.False(TryTipNudge.HasChangeTip("""{"status":"ok","oneTip":"Keep the boots.","tipKind":"keep"}"""));
        Assert.False(TryTipNudge.HasChangeTip("""{"status":"ok","oneTip":""}"""));
        Assert.False(TryTipNudge.HasChangeTip(null));
        Assert.False(TryTipNudge.HasChangeTip("not json"));
    }
}

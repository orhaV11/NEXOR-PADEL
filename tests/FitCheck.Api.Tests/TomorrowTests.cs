using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 19 — Tomorrow: "what should I wear", composed from the pieces the person kept. Two things are not negotiable
/// and are tested first: the model cannot put a garment in the outfit that the person does not own (every piece is a
/// wardrobe row picked by id, whatever the model wrote), and a planned outfit is refused exactly where a check would
/// be (counted in the same day, month and ceiling, reserved in flight the same way). Everything else here is what the
/// screen and the bill rest on: the cache that makes five taps cost one, the thresholds that keep the model unasked,
/// the weather that reaches it only when the server fetched it, and the photo of the person wearing each piece.
/// </summary>
public class TomorrowTests
{
    /// <summary>The phone's "today", read at each call so a run that crosses UTC midnight is not sending yesterday.</summary>
    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> ErrorOf(HttpResponseMessage response) => (await Json(response)).GetProperty("error").GetString() ?? "";

    private static async Task<Guid> KeepAsync(HttpClient client, Guid checkId, string name)
    {
        var response = await client.PostAsJsonAsync("/api/wardrobe", new { checkId, name });
        Assert.True(response.IsSuccessStatusCode, $"keep {name}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    /// <summary>One check (the test payload names White tee, Dark jeans, Running shoes) and the three pieces kept from it.</summary>
    private static async Task<(Guid CheckId, Dictionary<string, Guid> Items)> ThreeKeepsAsync(TestApp app, HttpClient client)
    {
        var checkId = await app.CheckAsync(client);
        var items = new Dictionary<string, Guid>();
        foreach (var name in new[] { "White tee", "Dark jeans", "Running shoes" })
        {
            items[name] = await KeepAsync(client, checkId, name);
        }

        return (checkId, items);
    }

    private static Task<HttpResponseMessage> ComposeAsync(HttpClient client, string occasion = "Office", bool fresh = false, string? style = null,
        double? lat = null, double? lon = null, string when = "tomorrow") =>
        client.PostAsJsonAsync("/api/tomorrow", new { occasion, style, when, today = Today, fresh, lat, lon });

    private static List<string> Names(JsonElement suggestion) =>
        suggestion.GetProperty("pieces").EnumerateArray().Select(p => p.GetProperty("name").GetString() ?? "").ToList();

    private static int ComposeCalls(TestApp app) => app.Vision.Requests.Count(r => r.Tool.Name == Tomorrow.ToolName);

    private static VisionRequest LastCompose(TestApp app) => app.Vision.Requests.Last(r => r.Tool.Name == Tomorrow.ToolName);

    private static async Task<int> CallsThisMonthAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("callsThisMonth").GetInt32();

    private static T WithDb<T>(TestApp app, Func<AppDbContext, T> read)
    {
        using var scope = app.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>A check payload naming other pieces, so a wardrobe can grow past the three the standard payload gives.</summary>
    private static JsonElement CheckNaming(params (string Name, string Category)[] items) => Payloads.Parse(JsonSerializer.Serialize(new
    {
        status = "ok", score = 7, intent_match = 70, headline = "Layered and warm", vibe = "autumn",
        items = items.Select(i => new { name = i.Name, category = i.Category, verdict = "works", note = "Fine." }).ToArray(),
        working = new[] { "Warm" }, one_tip = "Nothing to change."
    }));

    // ---------- the two non-negotiables ----------

    [Fact]
    public async Task A_hallucinated_garment_is_dropped()
    {
        using var app = new TestApp { Settings = { ["Plans:FreeSuggestionsPerDay"] = "20" } };
        var (me, id, _) = await app.NewUserAsync("tm_honest");
        var (_, items) = await ThreeKeepsAsync(app, me);

        // Two refs that exist, one repeated, one that never existed, one that is a garment's name rather than a ref, and a
        // sentence that talks about a coat nobody owns; the gap names a garment instead of a kind.
        app.Vision.Handler = request => request.Tool.Name == Tomorrow.ToolName
            ? Payloads.Compose(["P1", "P9", "P2", "P2", "Silk scarf"], "These two go together; a camel coat would finish it.", "trench coat")
            : Payloads.Ok();
        var response = await ComposeAsync(me);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var suggestion = await Json(response);

        // Exactly the two real pieces, in the model's order, each a wardrobe row of this account.
        var pieces = suggestion.GetProperty("pieces").EnumerateArray().ToList();
        Assert.Equal(2, pieces.Count);
        var refs = Tomorrow.Refs(WithDb(app, db => Wardrobe.ListAsync(db, id, CancellationToken.None).Result));
        var p1 = refs.Single(r => r.Ref == "P1").Item;
        var p2 = refs.Single(r => r.Ref == "P2").Item;
        Assert.Equal(p1.Id, pieces[0].GetProperty("itemId").GetGuid());
        Assert.Equal(p2.Id, pieces[1].GetProperty("itemId").GetGuid());
        Assert.All(pieces, p => Assert.Contains(p.GetProperty("itemId").GetGuid(), items.Values));
        Assert.DoesNotContain("Silk scarf", Names(suggestion));
        // The invented refs were counted, the gap was not a kind they lack, and the sentence (naming nothing they own) stayed.
        var row = WithDb(app, db => db.Suggestions.Single(s => s.UserId == id));
        Assert.Equal(2, row.InventedRefs);
        Assert.Null(row.Gap);
        Assert.False(row.SentenceTemplated);
        Assert.Equal("These two go together; a camel coat would finish it.", suggestion.GetProperty("sentence").GetString());
        Assert.Equal(3, row.PiecesOffered);

        // A sentence that names a piece they own but the outfit does not include is replaced by the template built from
        // the rows: the person is never told about a garment that is not in front of them.
        var p3 = refs.Single(r => r.Ref == "P3").Item;
        app.Vision.Handler = request => request.Tool.Name == Tomorrow.ToolName
            ? Payloads.Compose(["P1", "P2"], $"These two, and swap in the {Wardrobe.CleanName(p3.Name).ToLowerInvariant()} for comfort.")
            : Payloads.Ok();
        var second = await Json(await ComposeAsync(me, fresh: true));
        Assert.Equal(2, second.GetProperty("seq").GetInt32());
        var sentence = second.GetProperty("sentence").GetString()!;
        Assert.StartsWith("Wear these together: ", sentence);
        Assert.Contains(Wardrobe.CleanName(p1.Name), sentence);
        Assert.Contains(Wardrobe.CleanName(p2.Name), sentence);
        Assert.DoesNotContain(Wardrobe.CleanName(p3.Name), sentence);
        Assert.True(WithDb(app, db => db.Suggestions.Single(s => s.Seq == 2 && s.UserId == id).SentenceTemplated));

        // Fewer than two real refs left is a failure, stored as an error row and not counted.
        app.Vision.Handler = request => request.Tool.Name == Tomorrow.ToolName ? Payloads.Compose(["P1", "P8"]) : Payloads.Ok();
        var before = await CallsThisMonthAsync(me);
        var failed = await ComposeAsync(me, fresh: true);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Contains("Nothing was spent", await ErrorOf(failed));
        Assert.Equal(before, await CallsThisMonthAsync(me));
        Assert.Equal(1, WithDb(app, db => db.Suggestions.Count(s => s.UserId == id && s.Status == CheckStatus.Error)));
    }

    [Fact]
    public async Task A_tenth_tap_is_refused_exactly_where_a_tenth_check_would_be()
    {
        using var app = new TestApp { Settings = { ["Plans:ProChecksPerDay"] = "9", ["Plans:ProSuggestionsPerDay"] = "9", ["Plans:ProCallsPerMonth"] = "1000" } };
        var (pro, _, handle) = await app.NewUserAsync("tm_tenth");
        await AdminSync.SetProAsync(app.ConnectionString, handle, DateTime.UtcNow.AddDays(30));
        await ThreeKeepsAsync(app, pro);

        // Eight more checks make nine; the tenth is refused by the check bucket, with a Retry-After.
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await pro.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        }

        var tenthCheck = await pro.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, tenthCheck.StatusCode);
        Assert.Contains("9", await ErrorOf(tenthCheck));
        Assert.NotNull(tenthCheck.Headers.RetryAfter);

        // Nine planned outfits in Pro's own bucket, the check bucket full beside it; the tenth is refused the same way.
        for (var i = 0; i < 9; i++)
        {
            var ok = await ComposeAsync(pro, fresh: true);
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        var tenth = await ComposeAsync(pro, fresh: true);
        Assert.Equal(HttpStatusCode.TooManyRequests, tenth.StatusCode);
        var said = await ErrorOf(tenth);
        Assert.Contains("9", said);
        Assert.Contains("Tomorrow", said);
        Assert.NotNull(tenth.Headers.RetryAfter);

        // The model was asked exactly nine times on each side: the two refusals never reached it, and no row was stored for them.
        Assert.Equal(9, app.Vision.Requests.Count(r => r.Tool.Name == OutfitAnalyzer.ToolName));
        Assert.Equal(9, ComposeCalls(app));
        Assert.Equal(9, WithDb(app, db => db.Suggestions.Count()));
        Assert.Equal(0, WithDb(app, db => db.Suggestions.Count(s => s.Status == CheckStatus.Error)));
    }

    // ---------- honesty, the rest ----------

    [Fact]
    public async Task The_ref_enum_and_the_gap_enum_are_exactly_the_wardrobe()
    {
        using var app = new TestApp { Settings = { ["Plans:FreeSuggestionsPerDay"] = "20" } };
        var (me, _, _) = await app.NewUserAsync("tm_enum");
        await ThreeKeepsAsync(app, me);
        Assert.Equal(HttpStatusCode.Created, (await ComposeAsync(me)).StatusCode);

        var request = LastCompose(app);
        var schema = request.Tool.InputSchema;
        var refs = schema.GetProperty("properties").GetProperty("pieces").GetProperty("items").GetProperty("properties").GetProperty("ref").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["P1", "P2", "P3"], refs);
        var gap = schema.GetProperty("properties").GetProperty("gap").GetProperty("enum").EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Null ? null : e.GetString()).ToList();
        Assert.Equal(["dress", "outerwear", "accessory", null], gap);
        Assert.Contains("gap", schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));

        // The figures: the three pieces by ref, their kinds, the kinds they lack, no forecast and no name of theirs.
        Assert.Contains("Kinds they own: top, bottom, shoes", request.UserText);
        Assert.Contains("Kinds they do not own: dress, outerwear, accessory", request.UserText);
        Assert.Contains("Forecast: unknown", request.UserText);
        Assert.Contains("| \"White tee\" | worn once, last today", request.UserText);
        Assert.Contains("Occasion: Office", request.UserText);
        Assert.Contains("Language to write in: English", request.UserText);
        Assert.DoesNotContain("tm_enum", request.UserText);
        Assert.False(request.HasImage);

        // A wardrobe of every kind: no gap property at all.
        var (full, _, _) = await app.NewUserAsync("tm_full");
        app.Vision.Handler = request => request.Tool.Name == OutfitAnalyzer.ToolName
            ? CheckNaming(("White tee", "top"), ("Dark jeans", "bottom"), ("Running shoes", "shoes"), ("Camel coat", "outerwear"), ("Black dress", "dress"), ("Gold hoops", "accessory"))
            : FakeVisionClient.ByTool(request);
        var check = await app.CheckAsync(full);
        foreach (var name in new[] { "White tee", "Dark jeans", "Running shoes", "Camel coat", "Black dress", "Gold hoops" })
        {
            await KeepAsync(full, check, name);
        }

        Assert.Equal(HttpStatusCode.Created, (await ComposeAsync(full)).StatusCode);
        var fullSchema = LastCompose(app).Tool.InputSchema;
        Assert.False(fullSchema.GetProperty("properties").TryGetProperty("gap", out _));
        Assert.DoesNotContain("gap", fullSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("Kinds they do not own", LastCompose(app).UserText);
    }

    [Fact]
    public async Task A_gap_is_kept_only_when_it_is_a_kind_they_lack_and_a_dress_sends_the_top_and_bottom_home()
    {
        using var app = new TestApp { Settings = { ["Plans:FreeSuggestionsPerDay"] = "20" } };
        var (me, id, _) = await app.NewUserAsync("tm_gap");
        app.Vision.Handler = request => request.Tool.Name == OutfitAnalyzer.ToolName
            ? CheckNaming(("White tee", "top"), ("Dark jeans", "bottom"), ("Running shoes", "shoes"), ("Black dress", "dress"))
            : FakeVisionClient.ByTool(request);
        var check = await app.CheckAsync(me);
        foreach (var name in new[] { "White tee", "Dark jeans", "Running shoes", "Black dress" })
        {
            await KeepAsync(me, check, name);
        }

        // The model names a kind they do own as the gap, and a dress together with a top and a bottom.
        var refs = Tomorrow.Refs(WithDb(app, db => Wardrobe.ListAsync(db, id, CancellationToken.None).Result));
        var dress = refs.Single(r => r.Item.Category == "dress").Ref;
        var top = refs.Single(r => r.Item.Category == "top").Ref;
        var bottom = refs.Single(r => r.Item.Category == "bottom").Ref;
        var shoes = refs.Single(r => r.Item.Category == "shoes").Ref;
        app.Vision.Handler = request => request.Tool.Name == Tomorrow.ToolName
            ? Payloads.Compose([top, bottom, dress, shoes], "Easy.", "shoes")
            : FakeVisionClient.ByTool(request);
        var suggestion = await Json(await ComposeAsync(me, "Party"));
        Assert.Equal(["Black dress", "Running shoes"], Names(suggestion));
        Assert.True(!suggestion.TryGetProperty("gap", out var noGap) || noGap.ValueKind == JsonValueKind.Null);

        // A kind they truly lack is kept, and reaches the screen.
        app.Vision.Handler = request => request.Tool.Name == Tomorrow.ToolName
            ? Payloads.Compose([dress, shoes], "Easy.", "outerwear")
            : FakeVisionClient.ByTool(request);
        var withGap = await Json(await ComposeAsync(me, "Party", fresh: true));
        Assert.Equal("outerwear", withGap.GetProperty("gap").GetString());
    }

    [Fact]
    public async Task A_sentence_that_mentions_a_body_or_invents_a_number_is_replaced()
    {
        using var app = new TestApp { Settings = { ["Plans:FreeSuggestionsPerDay"] = "20" } };
        foreach (var (language, opener) in new[] { ("en", "Wear these together: "), ("he", "ללבוש יחד: "), ("ar", "ارتدِ هذه معًا: "), ("ru", "Надень это вместе: ") })
        {
            var (me, _, _) = await app.NewUserAsync("tm_body_" + language, language: language);
            await ThreeKeepsAsync(app, me);
            app.Vision.Handler = request => request.Tool.Name == Tomorrow.ToolName
                ? Payloads.Compose(["P1", "P2"], "Slim and flattering on your figure.")
                : Payloads.Ok();
            var body = await Json(await ComposeAsync(me));
            Assert.StartsWith(opener, body.GetProperty("sentence").GetString());

            // A temperature the figures never gave.
            app.Vision.Handler = request => request.Tool.Name == Tomorrow.ToolName
                ? Payloads.Compose(["P1", "P2"], "It will be 31 degrees, so keep it light.")
                : Payloads.Ok();
            var number = await Json(await ComposeAsync(me, fresh: true));
            Assert.StartsWith(opener, number.GetProperty("sentence").GetString());
        }

        // A number that IS in the figures (the forecast) is fine.
        var (weathered, _, _) = await app.NewUserAsync("tm_number_ok");
        await ThreeKeepsAsync(app, weathered);
        app.Vision.Handler = request => request.Tool.Name == Tomorrow.ToolName
            ? Payloads.Compose(["P1", "P2"], "At 24 degrees the tee is enough.")
            : Payloads.Ok();
        var fine = await Json(await ComposeAsync(weathered, lat: 32.08, lon: 34.78));
        Assert.Equal("At 24 degrees the tee is enough.", fine.GetProperty("sentence").GetString());
    }

    // ---------- thresholds and gates that spend nothing ----------

    [Fact]
    public async Task Too_few_pieces_or_kinds_makes_no_call()
    {
        using var app = new TestApp();
        var (me, _, _) = await app.NewUserAsync("tm_thin");

        // Nothing kept at all.
        var empty = await ComposeAsync(me);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        var needs = await Json(empty);
        Assert.Equal(0, needs.GetProperty("have").GetInt32());
        Assert.Equal(2, needs.GetProperty("needs").GetInt32());
        Assert.Equal(2, needs.GetProperty("needsKinds").GetInt32());

        // One piece.
        var check = await app.CheckAsync(me);
        await KeepAsync(me, check, "White tee");
        needs = await Json(await ComposeAsync(me));
        Assert.Equal(1, needs.GetProperty("have").GetInt32());
        Assert.Equal(1, needs.GetProperty("haveKinds").GetInt32());

        // Two pieces of one kind: two tops are not an outfit.
        app.Vision.Handler = request => request.Tool.Name == OutfitAnalyzer.ToolName ? CheckNaming(("Grey tee", "top")) : FakeVisionClient.ByTool(request);
        var second = await app.CheckAsync(me);
        await KeepAsync(me, second, "Grey tee");
        needs = await Json(await ComposeAsync(me));
        Assert.Equal(2, needs.GetProperty("have").GetInt32());
        Assert.Equal(1, needs.GetProperty("haveKinds").GetInt32());

        Assert.Equal(0, ComposeCalls(app));
        Assert.Equal(2, await CallsThisMonthAsync(me));
        Assert.Equal(0, WithDb(app, db => db.Suggestions.Count()));
    }

    [Fact]
    public async Task The_wardrobe_switch_is_honoured_and_a_guest_has_no_tomorrow()
    {
        // The switch itself is Pro's while Plans:WardrobeNeedsPro is on (the default); this test is about the switch, not the plan.
        using var app = new TestApp { Settings = { ["Plans:WardrobeNeedsPro"] = "false" } };
        var (me, _, _) = await app.NewUserAsync("tm_switch");
        await ThreeKeepsAsync(app, me);
        Assert.Equal(HttpStatusCode.OK, (await me.PostAsJsonAsync("/api/wardrobe/stylist", new { on = false })).StatusCode);
        var refused = await ComposeAsync(me);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("wardrobe", await ErrorOf(refused));
        Assert.Equal(0, ComposeCalls(app));

        var read = await Json(await me.GetAsync("/api/tomorrow"));
        Assert.False(read.GetProperty("stylistOn").GetBoolean());

        var anonymous = app.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/tomorrow", new { occasion = "Office" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/tomorrow")).StatusCode);
    }

    [Fact]
    public async Task Kill_switch_and_the_wall()
    {
        using var off = new TestApp { Settings = { ["Plans:TomorrowEnabled"] = "false" } };
        var (dark, _, _) = await off.NewUserAsync("tm_off");
        await ThreeKeepsAsync(off, dark);
        Assert.Equal(HttpStatusCode.NotFound, (await dark.GetAsync("/api/tomorrow")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ComposeAsync(dark)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await dark.PostAsJsonAsync($"/api/tomorrow/{Guid.NewGuid()}/useful", new { reason = "worked" })).StatusCode);
        Assert.False((await off.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("plans").GetProperty("tomorrow").GetBoolean());

        using var walled = new TestApp { Settings = { ["Plans:TomorrowNeedsPro"] = "true" } };
        var (free, _, handle) = await walled.NewUserAsync("tm_wall");
        await ThreeKeepsAsync(walled, free);
        var refused = await ComposeAsync(free);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("Pro", await ErrorOf(refused));
        var read = await Json(await free.GetAsync("/api/tomorrow"));
        Assert.False(read.GetProperty("available").GetBoolean());
        Assert.True(read.GetProperty("needsPro").GetBoolean());
        // The strip is still theirs to see.
        Assert.Equal(3, read.GetProperty("strip").GetArrayLength());

        await AdminSync.SetProAsync(walled.ConnectionString, handle, DateTime.UtcNow.AddDays(30));
        Assert.Equal(HttpStatusCode.Created, (await ComposeAsync(free)).StatusCode);

        // And the default: free composes.
        using var open = new TestApp();
        var (anyone, _, _) = await open.NewUserAsync("tm_open");
        await ThreeKeepsAsync(open, anyone);
        Assert.Equal(HttpStatusCode.Created, (await ComposeAsync(anyone)).StatusCode);
    }

    // ---------- the cache ----------

    [Fact]
    public async Task Same_ask_twice_costs_once_and_ask_again_costs_again()
    {
        using var app = new TestApp { Settings = { ["Plans:FreeSuggestionsPerDay"] = "20" } };
        var (me, id, _) = await app.NewUserAsync("tm_cache");
        await ThreeKeepsAsync(app, me);

        var first = await Json(await ComposeAsync(me));
        Assert.False(first.GetProperty("reused").GetBoolean());
        Assert.True(first.GetProperty("counted").GetBoolean());
        Assert.Equal(1, first.GetProperty("seq").GetInt32());
        for (var i = 0; i < 4; i++)
        {
            var again = await ComposeAsync(me);
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            var reused = await Json(again);
            Assert.True(reused.GetProperty("reused").GetBoolean());
            Assert.False(reused.GetProperty("counted").GetBoolean());
            Assert.False(reused.GetProperty("stale").GetBoolean());
            Assert.Equal(first.GetProperty("id").GetGuid(), reused.GetProperty("id").GetGuid());
        }

        Assert.Equal(1, ComposeCalls(app));
        Assert.Equal(2, await CallsThisMonthAsync(me));
        Assert.Equal(4, WithDb(app, db => db.Suggestions.Single(s => s.UserId == id).Reuses));

        // Another occasion is another question; the same occasion with a style is too.
        Assert.Equal(HttpStatusCode.Created, (await ComposeAsync(me, "Party")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ComposeAsync(me, "Office", style: "Minimal")).StatusCode);
        Assert.Equal(3, ComposeCalls(app));

        // "Another idea" pays, is idea 2, and the model is told in refs what it already said.
        var second = await Json(await ComposeAsync(me, fresh: true));
        Assert.Equal(2, second.GetProperty("seq").GetInt32());
        Assert.True(second.GetProperty("counted").GetBoolean());
        Assert.Contains("Already suggested for this occasion today: P1 + P2 + P3", LastCompose(app).UserText);
        Assert.Contains("Compose something different", LastCompose(app).UserText);
        Assert.Equal(4, ComposeCalls(app));
        // The newest answer is the one handed back from now on.
        Assert.Equal(second.GetProperty("id").GetGuid(), (await Json(await ComposeAsync(me))).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_thumbs_down_breaks_the_cache_and_a_new_piece_marks_it_stale()
    {
        using var app = new TestApp { Settings = { ["Plans:FreeSuggestionsPerDay"] = "20" } };
        var (me, _, _) = await app.NewUserAsync("tm_stale");
        var (check, _) = await ThreeKeepsAsync(app, me);
        var first = await Json(await ComposeAsync(me));
        var id = first.GetProperty("id").GetGuid();

        // Not this: the next tap composes fresh and pays.
        var thumbs = await me.PostAsJsonAsync($"/api/tomorrow/{id}/useful", new { reason = "didnt_work" });
        Assert.Equal(HttpStatusCode.OK, thumbs.StatusCode);
        var next = await Json(await ComposeAsync(me));
        Assert.NotEqual(id, next.GetProperty("id").GetGuid());
        Assert.False(next.GetProperty("reused").GetBoolean());
        Assert.Equal(2, ComposeCalls(app));

        // A fourth piece kept since: the stored answer comes back, marked stale, and nothing is spent until they ask.
        app.Vision.Handler = request => request.Tool.Name == OutfitAnalyzer.ToolName ? CheckNaming(("Camel coat", "outerwear")) : FakeVisionClient.ByTool(request);
        var coat = await app.CheckAsync(me);
        await KeepAsync(me, coat, "Camel coat");
        var stale = await Json(await ComposeAsync(me));
        Assert.Equal(next.GetProperty("id").GetGuid(), stale.GetProperty("id").GetGuid());
        Assert.True(stale.GetProperty("reused").GetBoolean());
        Assert.True(stale.GetProperty("stale").GetBoolean());
        Assert.Equal(2, ComposeCalls(app));

        // Refresh is a fresh compose, and the coat is now in the list.
        var refreshed = await Json(await ComposeAsync(me, fresh: true));
        Assert.False(refreshed.GetProperty("stale").GetBoolean());
        Assert.Equal(3, ComposeCalls(app));
        Assert.Contains("\"Camel coat\"", LastCompose(app).UserText);
    }

    // ---------- the bill ----------

    [Fact]
    public async Task A_model_failure_spends_nothing_and_a_refusal_still_counts()
    {
        using var app = new TestApp { Settings = { ["Plans:FreeSuggestionsPerDay"] = "20" } };
        var (me, id, _) = await app.NewUserAsync("tm_fail");
        await ThreeKeepsAsync(app, me);
        Assert.Equal(1, await CallsThisMonthAsync(me));

        app.Vision.Handler = request => request.Tool.Name == Tomorrow.ToolName ? throw new VisionClientException("down") : Payloads.Ok();
        var failed = await ComposeAsync(me);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(1, await CallsThisMonthAsync(me));
        Assert.Equal(CheckStatus.Error, WithDb(app, db => db.Suggestions.Single(s => s.UserId == id)).Status);

        app.Vision.Handler = request => request.Tool.Name == Tomorrow.ToolName ? throw new VisionRefusedException("declined") : Payloads.Ok();
        var refused = await ComposeAsync(me);
        Assert.Equal(HttpStatusCode.Created, refused.StatusCode);
        var rejected = await Json(refused);
        Assert.Equal("rejected", rejected.GetProperty("status").GetString());
        Assert.True(rejected.GetProperty("counted").GetBoolean());
        Assert.Empty(rejected.GetProperty("pieces").EnumerateArray());
        Assert.Equal(2, await CallsThisMonthAsync(me));

        // A rejected row is not handed back as a stored answer: the next tap asks again.
        app.Vision.Handler = FakeVisionClient.ByTool;
        var fine = await Json(await ComposeAsync(me));
        Assert.Equal("ok", fine.GetProperty("status").GetString());
        Assert.False(fine.GetProperty("reused").GetBoolean());
    }

    [Fact]
    public async Task Free_is_braked_at_one_a_day_inside_its_shared_day()
    {
        using var app = new TestApp { FreeChecksPerDay = 3, Settings = { ["Plans:FreeSuggestionsPerDay"] = "1" } };
        var (me, _, _) = await app.NewUserAsync("tm_free");
        await ThreeKeepsAsync(app, me);   // 1 of the day's 3

        var read = await Json(await me.GetAsync("/api/tomorrow"));
        Assert.Equal(1, read.GetProperty("leftToday").GetInt32());
        Assert.Equal(1, read.GetProperty("capToday").GetInt32());

        var first = await Json(await ComposeAsync(me));   // 2 of 3
        Assert.Equal(0, first.GetProperty("leftToday").GetInt32());
        var braked = await ComposeAsync(me, fresh: true);
        Assert.Equal(HttpStatusCode.TooManyRequests, braked.StatusCode);
        var said = await ErrorOf(braked);
        Assert.Contains("Free", said);
        Assert.Contains("10", said);
        Assert.NotNull(braked.Headers.RetryAfter);
        // The brake's Retry-After is the suggestion's own age against the day: about 24 hours, not the check's.
        Assert.True(braked.Headers.RetryAfter!.Delta > TimeSpan.FromHours(23));

        // The check a day is still there — and the fourth call of any kind is the day's own refusal.
        Assert.Equal(HttpStatusCode.Created, (await me.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        var full = await me.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, full.StatusCode);
        Assert.Equal(1, ComposeCalls(app));
    }

    [Fact]
    public async Task A_free_suggestion_spends_a_shared_day_and_pro_has_its_own_bucket()
    {
        using var shared = new TestApp { FreeChecksPerDay = 2, Settings = { ["Plans:FreeSuggestionsPerDay"] = "2" } };
        var (free, _, _) = await shared.NewUserAsync("tm_shared");
        await ThreeKeepsAsync(shared, free);   // 1 of 2
        Assert.Equal(HttpStatusCode.Created, (await ComposeAsync(free)).StatusCode);   // 2 of 2
        var check = await free.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, check.StatusCode);
        Assert.Contains("2", await ErrorOf(check));
        var suggestion = await ComposeAsync(free, fresh: true);
        Assert.Equal(HttpStatusCode.TooManyRequests, suggestion.StatusCode);
        // The day was the binding one here, so the message is the day's.
        Assert.Contains("free checks", await ErrorOf(suggestion));

        using var own = new TestApp { Settings = { ["Plans:ProChecksPerDay"] = "2", ["Plans:ProSuggestionsPerDay"] = "1", ["Plans:ProCallsPerMonth"] = "100" } };
        var (pro, _, handle) = await own.NewUserAsync("tm_own");
        await AdminSync.SetProAsync(own.ConnectionString, handle, DateTime.UtcNow.AddDays(30));
        await ThreeKeepsAsync(own, pro);   // check 1 of 2
        Assert.Equal(HttpStatusCode.Created, (await ComposeAsync(pro)).StatusCode);   // suggestion 1 of 1
        var second = await ComposeAsync(pro, fresh: true);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Contains("1", await ErrorOf(second));
        // Pro's checks are untouched by its planned outfit.
        Assert.Equal(HttpStatusCode.Created, (await pro.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await pro.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        var me = await pro.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(2, me.GetProperty("checksToday").GetInt32());
        Assert.Equal(3, me.GetProperty("callsThisMonth").GetInt32());
    }

    [Fact]
    public async Task A_suggestion_counts_in_the_month_like_a_check()
    {
        using var app = new TestApp { ProCallsPerMonth = 3 };
        var (pro, _, handle) = await app.NewUserAsync("tm_month");
        await AdminSync.SetProAsync(app.ConnectionString, handle, DateTime.UtcNow.AddDays(30));
        await ThreeKeepsAsync(app, pro);   // 1
        Assert.Equal(HttpStatusCode.Created, (await ComposeAsync(pro)).StatusCode);   // 2
        Assert.Equal(HttpStatusCode.Created, (await ComposeAsync(pro, "Party")).StatusCode);   // 3
        var check = await pro.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, check.StatusCode);
        Assert.Contains("3", await ErrorOf(check));
        var suggestion = await ComposeAsync(pro, "Sport");
        Assert.Equal(HttpStatusCode.TooManyRequests, suggestion.StatusCode);
        Assert.Contains("3", await ErrorOf(suggestion));
        Assert.Equal(3, await CallsThisMonthAsync(pro));
        // A stored answer is still served when the month is spent: it costs nothing.
        Assert.Equal(HttpStatusCode.OK, (await ComposeAsync(pro)).StatusCode);
    }

    [Fact]
    public async Task The_in_flight_reservation_holds()
    {
        using var app = new TestApp { Settings = { ["Plans:ProSuggestionsPerDay"] = "1" } };
        var (pro, _, handle) = await app.NewUserAsync("tm_flight");
        await AdminSync.SetProAsync(app.ConnectionString, handle, DateTime.UtcNow.AddDays(30));
        await ThreeKeepsAsync(app, pro);

        var gate = new TaskCompletionSource();
        app.Vision.Handler = request =>
        {
            if (request.Tool.Name == Tomorrow.ToolName)
            {
                gate.Task.Wait(TimeSpan.FromSeconds(10));
            }

            return FakeVisionClient.ByTool(request);
        };
        var taps = Enumerable.Range(0, 12).Select(_ => ComposeAsync(pro, fresh: true)).ToList();
        await Task.Delay(300);
        gate.SetResult();
        var answers = await Task.WhenAll(taps);
        Assert.Equal(1, answers.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(11, answers.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.Equal(1, ComposeCalls(app));
    }

    /// <summary>
    /// Round 19 review: the slot in flight is counted per bucket. A Pro account's check uploading must not fill its planned
    /// outfit's slot (its own bucket), and a free account's check in flight counts against its shared day but not against
    /// the brake on planned outfits, so neither is told the day is full when it is not.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_check_in_flight_does_not_fill_a_planned_outfits_slot(bool pro)
    {
        using var app = new TestApp { Settings = { ["Plans:ProSuggestionsPerDay"] = "1", ["Plans:FreeSuggestionsPerDay"] = "1", ["Plans:FreeChecksPerDay"] = "3" } };
        var (me, _, handle) = await app.NewUserAsync(pro ? "tm_flight_pro" : "tm_flight_free");
        if (pro)
        {
            await AdminSync.SetProAsync(app.ConnectionString, handle, DateTime.UtcNow.AddDays(30));
        }

        await ThreeKeepsAsync(app, me);

        var gate = new TaskCompletionSource();
        app.Vision.Handler = request =>
        {
            if (request.Tool.Name == OutfitAnalyzer.ToolName)
            {
                gate.Task.Wait(TimeSpan.FromSeconds(10));
            }

            return FakeVisionClient.ByTool(request);
        };
        var check = me.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        await Task.Delay(300);
        var planned = await ComposeAsync(me, fresh: true);
        gate.SetResult();
        Assert.Equal(HttpStatusCode.Created, planned.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await check).StatusCode);
    }

    /// <summary>
    /// Round 19 review: a dress and a tee is a wardrobe the two-kinds gate lets through, and the dress rule then sends the
    /// tee home. A dress is an outfit on its own, so that is an answer, not a spent call and a 502 on every tap.
    /// </summary>
    [Fact]
    public async Task A_dress_is_an_outfit_on_its_own()
    {
        using var app = new TestApp { Settings = { ["Plans:FreeSuggestionsPerDay"] = "20" } };
        var (me, id, _) = await app.NewUserAsync("tm_dress");
        app.Vision.Handler = request => request.Tool.Name == OutfitAnalyzer.ToolName
            ? CheckNaming(("Black dress", "dress"), ("White tee", "top"))
            : FakeVisionClient.ByTool(request);
        var check = await app.CheckAsync(me);
        await KeepAsync(me, check, "Black dress");
        await KeepAsync(me, check, "White tee");
        var refs = Tomorrow.Refs(WithDb(app, db => Wardrobe.ListAsync(db, id, CancellationToken.None).Result));
        app.Vision.Handler = request => request.Tool.Name == Tomorrow.ToolName
            ? Payloads.Compose([refs.Single(r => r.Item.Category == "dress").Ref, refs.Single(r => r.Item.Category == "top").Ref], "")
            : FakeVisionClient.ByTool(request);

        var response = await ComposeAsync(me, "Party");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var suggestion = await Json(response);
        Assert.Equal(["Black dress"], Names(suggestion));
        Assert.Equal("Wear this: Black dress.", suggestion.GetProperty("sentence").GetString());
        Assert.Equal(1, ComposeCalls(app));
    }

    /// <summary>
    /// Round 19 review: the refs (P1..P40) are labels, not figures, so "around 10 degrees" on a wardrobe of ten is a made-up
    /// number; and an unpicked name inside a picked one ("Jeans" in "Dark jeans") is the picked piece being named.
    /// </summary>
    [Fact]
    public void Ref_labels_are_not_figures_and_a_name_inside_a_picked_name_is_not_a_stranger()
    {
        const string figures = "Forecast: unknown.\nPieces (refer to them ONLY by their ref):\nP1 | top | \"White tee\" | worn once, last today\nP10 | shoes | \"Boots\" | worn 3 times, last 2 days ago\n";
        Assert.True(Tomorrow.InventsANumber("Around 10 degrees tomorrow, so keep the jacket on.", figures));
        Assert.True(Tomorrow.InventsANumber("A solid 1 out of 10.", figures));
        Assert.False(Tomorrow.InventsANumber("Worn 3 times already; wear it again.", figures));
        Assert.False(Tomorrow.InventsANumber("A high of 24 today.", "Forecast: high 24 C, low 17 C\n" + figures));

        static Tomorrow.PromptPiece Piece(string @ref, string name, string category) => new(@ref, new WardrobeItem { Name = name, Category = category }, []);
        var jeans = Piece("P1", "Jeans", "bottom");
        var darkJeans = Piece("P2", "Dark jeans", "bottom");
        var tee = Piece("P3", "White tee", "top");
        var boots = Piece("P4", "Boots", "shoes");
        var offered = new[] { jeans, darkJeans, tee, boots };
        Assert.False(Tomorrow.NamesAnUnpickedPiece("Your dark jeans and white tee.", offered, [darkJeans, tee]));
        Assert.True(Tomorrow.NamesAnUnpickedPiece("Your dark jeans and white tee, with the boots.", offered, [darkJeans, tee]));
    }

    /// <summary>
    /// Round 19 review: the factory's default HttpClient logging printed the forecast URL, place and key included, at
    /// Information. Through the real pipeline, with a key set: no line anywhere carries the coordinates or the key.
    /// </summary>
    [Fact]
    public async Task No_log_line_carries_the_place_or_the_forecast_key()
    {
        const string key = "om-test-key-8f3a";
        using var app = new TestApp { Settings = { ["Plans:FreeSuggestionsPerDay"] = "20", ["Weather:ApiKey"] = key } };
        var provider = new RecordingLoggerProvider();
        using var logged = app.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(provider)));
        var me = logged.CreateClient();
        me.DefaultRequestHeaders.Add(Sessions.RequestHeader, Sessions.RequestHeaderValue);
        var signup = await me.PostAsJsonAsync("/api/auth/signup", new { handle = "tm_quiet", password = "Tr0ub4dor-quiet-42", birthDate = "1990-01-01", language = "en" });
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);
        await ThreeKeepsAsync(app, me);

        var response = await ComposeAsync(me, lat: 32.0853, lon: 34.7818);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var url = Assert.Single(app.WeatherHandler.Requests).ToString();
        Assert.Contains("apikey=" + key, url);
        Assert.Contains("latitude=32.09", url);

        var lines = provider.Lines.Select(l => l.Category + ": " + l.Message).ToList();
        foreach (var secret in new[] { key, "latitude=", "longitude=", "32.09", "34.78", "open-meteo" })
        {
            Assert.DoesNotContain(lines, l => l.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---------- weather ----------

    [Fact]
    public async Task Weather_reaches_the_model_only_when_the_server_fetched_it()
    {
        using var app = new TestApp { Settings = { ["Plans:FreeSuggestionsPerDay"] = "20" } };
        var (me, id, _) = await app.NewUserAsync("tm_weather");
        await ThreeKeepsAsync(app, me);

        var forecast = await Json(await ComposeAsync(me, lat: 32.0853214, lon: 34.7817676));
        Assert.Contains("Forecast: high 24 C, low 17 C, chance of rain 10%, clear", LastCompose(app).UserText);
        var weather = forecast.GetProperty("weather");
        Assert.Equal(24.0, weather.GetProperty("tempMaxC").GetDouble());
        Assert.Equal(17.0, weather.GetProperty("tempMinC").GetDouble());
        Assert.Equal(10, weather.GetProperty("precipChance").GetInt32());
        Assert.Equal("clear", weather.GetProperty("sky").GetString());
        // Rounded to a kilometre before it left, and never written anywhere.
        var url = Assert.Single(app.WeatherHandler.Requests);
        Assert.Contains("latitude=32.09&longitude=34.78", url.Query);
        Assert.DoesNotContain("32.0853", url.Query);
        var row = WithDb(app, db => db.Suggestions.Single(s => s.UserId == id));
        Assert.True(row.WeatherUsed);
        Assert.Equal(24.0, row.WeatherTempMaxC);
        Assert.Equal(0, row.WeatherCode);

        // A service that fails: no forecast, the outfit still composed, the row's columns empty.
        app.WeatherHandler.StatusCode = HttpStatusCode.InternalServerError;
        var noWeather = await Json(await ComposeAsync(me, "Party", lat: 31.77, lon: 35.21));
        Assert.Contains("Forecast: unknown", LastCompose(app).UserText);
        Assert.True(!noWeather.TryGetProperty("weather", out var w) || w.ValueKind == JsonValueKind.Null);
        Assert.False(WithDb(app, db => db.Suggestions.Single(s => s.UserId == id && s.Occasion == OutfitOccasion.Party)).WeatherUsed);

        // No place given: nothing asked.
        app.WeatherHandler.StatusCode = HttpStatusCode.OK;
        await ComposeAsync(me, "Sport");
        Assert.Contains("Forecast: unknown", LastCompose(app).UserText);
        Assert.Equal(2, app.WeatherHandler.Requests.Count);

        // Off on this server: nothing asked even with a place.
        using var off = new TestApp { Settings = { ["Weather:Enabled"] = "false" } };
        var (dark, _, _) = await off.NewUserAsync("tm_noweather");
        await ThreeKeepsAsync(off, dark);
        Assert.Equal(HttpStatusCode.Created, (await ComposeAsync(dark, lat: 32.08, lon: 34.78)).StatusCode);
        Assert.Empty(off.WeatherHandler.Requests);
        Assert.Contains("Forecast: unknown", LastCompose(off).UserText);
    }

    // ---------- the photo per piece and the first paint ----------

    [Fact]
    public async Task The_first_paint_never_composes_and_each_piece_gets_a_distinct_photo_where_it_can()
    {
        using var app = new TestApp();
        var (me, _, _) = await app.NewUserAsync("tm_paint");
        var first = await app.CheckAsync(me);
        await KeepAsync(me, first, "White tee");
        var second = await app.CheckAsync(me);
        await KeepAsync(me, second, "Dark jeans");
        await KeepAsync(me, second, "Running shoes");

        var read = await Json(await me.GetAsync("/api/tomorrow"));
        Assert.True(read.GetProperty("available").GetBoolean());
        Assert.True(read.GetProperty("stylistOn").GetBoolean());
        Assert.False(read.GetProperty("needsPro").GetBoolean());
        Assert.Equal(3, read.GetProperty("have").GetInt32());
        Assert.Equal(3, read.GetProperty("haveKinds").GetInt32());
        Assert.Equal(12, read.GetProperty("offered").GetInt32());
        Assert.Empty(read.GetProperty("recent").EnumerateArray());
        var strip = read.GetProperty("strip").EnumerateArray().ToList();
        Assert.Equal(3, strip.Count);
        Assert.All(strip, p => Assert.StartsWith("/api/checks/", p.GetProperty("photoUrl").GetString()));
        // No outfit yet: the newest check's occasion pre-lights the chips (the test check is a Date).
        Assert.Equal("Date", read.GetProperty("defaultOccasion").GetString());
        Assert.Equal(0, ComposeCalls(app));

        // The photos of them wearing each piece, and the check photo route answers each one.
        var suggestion = await Json(await ComposeAsync(me));
        var pieces = suggestion.GetProperty("pieces").EnumerateArray().ToList();
        var photoByName = pieces.ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("photoCheckId").GetGuid());
        Assert.Equal(first, photoByName["White tee"]);
        Assert.Equal(second, photoByName["Dark jeans"]);
        Assert.Equal(second, photoByName["Running shoes"]);
        foreach (var piece in pieces)
        {
            var photo = await me.GetAsync(piece.GetProperty("photoUrl").GetString());
            Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
            Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
            Assert.Equal(1, piece.GetProperty("worn").GetInt32());
        }

        // The first paint now carries it, with the chips to pre-light.
        read = await Json(await me.GetAsync("/api/tomorrow"));
        Assert.Single(read.GetProperty("recent").EnumerateArray());
        Assert.Equal("Office", read.GetProperty("defaultOccasion").GetString());
        Assert.Equal(1, ComposeCalls(app));
    }

    [Fact]
    public async Task The_thumbs_write_the_checks_vocabulary_and_belong_to_the_owner()
    {
        using var app = new TestApp { Settings = { ["Plans:FreeSuggestionsPerDay"] = "20" } };
        var (me, id, _) = await app.NewUserAsync("tm_thumbs");
        var (other, _, _) = await app.NewUserAsync("tm_other");
        await ThreeKeepsAsync(app, me);
        var suggestion = await Json(await ComposeAsync(me));
        var suggestionId = suggestion.GetProperty("id").GetGuid();

        var answered = await me.PostAsJsonAsync($"/api/tomorrow/{suggestionId}/useful", new { reason = "not_my_style", note = "too plain for me" });
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        var dto = await Json(answered);
        Assert.False(dto.GetProperty("useful").GetBoolean());
        Assert.Equal("not_my_style", dto.GetProperty("reason").GetString());
        var row = WithDb(app, db => db.Suggestions.Single(s => s.Id == suggestionId));
        Assert.False(row.Useful);
        Assert.Equal("not_my_style", row.UsefulReason);
        Assert.Equal("too plain for me", row.UsefulNote);
        Assert.NotNull(row.UsefulAt);

        var unknown = await me.PostAsJsonAsync($"/api/tomorrow/{suggestionId}/useful", new { reason = "meh" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/tomorrow/{suggestionId}/useful", new { reason = "worked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await me.PostAsJsonAsync($"/api/tomorrow/{Guid.NewGuid()}/useful", new { reason = "worked" })).StatusCode);

        // Yes is the one answer that reads as useful, and the first paint shows it.
        var yes = await Json(await me.PostAsJsonAsync($"/api/tomorrow/{suggestionId}/useful", new { reason = "worked" }));
        Assert.True(yes.GetProperty("useful").GetBoolean());
        var read = await Json(await me.GetAsync("/api/tomorrow"));
        var recent = Assert.Single(read.GetProperty("recent").EnumerateArray());
        Assert.Equal("worked", recent.GetProperty("usefulReason").GetString());
        Assert.Equal(id, WithDb(app, db => db.Suggestions.Single(s => s.Id == suggestionId).UserId));
    }

    /// <summary>
    /// "Wearing it? Check it": a check sent from a planned outfit links the two and records the strongest yes there is,
    /// unless the person already answered. Someone else's outfit, or a made-up id, is quietly nothing.
    /// </summary>
    [Fact]
    public async Task Wearing_it_closes_the_loop()
    {
        using var app = new TestApp { Settings = { ["Plans:FreeSuggestionsPerDay"] = "20" } };
        var (me, id, _) = await app.NewUserAsync("tm_worn");
        var (other, _, _) = await app.NewUserAsync("tm_worn_other");
        await ThreeKeepsAsync(app, me);
        var suggestion = await Json(await ComposeAsync(me));
        var suggestionId = suggestion.GetProperty("id").GetGuid();

        var form = TestApp.CheckForm(TestImages.Jpeg());
        form.Add(new StringContent(suggestionId.ToString()), "suggestionId");
        var worn = await me.PostAsync("/api/checks", form);
        Assert.Equal(HttpStatusCode.Created, worn.StatusCode);
        var checkId = (await Json(worn)).GetProperty("id").GetGuid();

        var row = WithDb(app, db => db.Suggestions.Single(s => s.Id == suggestionId));
        Assert.Equal(checkId, row.WornCheckId);
        Assert.Equal("worked", row.UsefulReason);
        Assert.True(row.Useful);
        Assert.Equal(suggestionId, WithDb(app, db => db.Checks.Single(c => c.Id == checkId).SuggestionId));
        var read = await Json(await me.GetAsync("/api/tomorrow"));
        Assert.Equal(checkId, Assert.Single(read.GetProperty("recent").EnumerateArray()).GetProperty("wornCheckId").GetGuid());

        // An answer already given is not overwritten by the check.
        var another = await Json(await ComposeAsync(me, "Party"));
        var anotherId = another.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await me.PostAsJsonAsync($"/api/tomorrow/{anotherId}/useful", new { reason = "not_my_style" })).StatusCode);
        var form2 = TestApp.CheckForm(TestImages.Jpeg());
        form2.Add(new StringContent(anotherId.ToString()), "suggestionId");
        Assert.Equal(HttpStatusCode.Created, (await me.PostAsync("/api/checks", form2)).StatusCode);
        var kept = WithDb(app, db => db.Suggestions.Single(s => s.Id == anotherId));
        Assert.NotNull(kept.WornCheckId);
        Assert.Equal("not_my_style", kept.UsefulReason);

        // Someone else's outfit: the check is an ordinary check and the outfit is untouched.
        var stranger = TestApp.CheckForm(TestImages.Jpeg());
        stranger.Add(new StringContent(suggestionId.ToString()), "suggestionId");
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsync("/api/checks", stranger)).StatusCode);
        Assert.Equal(checkId, WithDb(app, db => db.Suggestions.Single(s => s.Id == suggestionId)).WornCheckId);
        var nonsense = TestApp.CheckForm(TestImages.Jpeg());
        nonsense.Add(new StringContent("not-a-guid"), "suggestionId");
        Assert.Equal(HttpStatusCode.Created, (await me.PostAsync("/api/checks", nonsense)).StatusCode);
        Assert.Equal(id, row.UserId);
    }

    /// <summary>
    /// The thumbs on an outfit teach the same profile a check's thumbs teach: the counts grow, the turned-down outfit's
    /// sentence and note join the advisory under the same two-reason rule, and the outfits themselves are named as
    /// combinations of pieces — the ones they said yes to and the ones they turned down. Clearing the profile clears
    /// them; learning off silences them. The numbers page counts them beside the wardrobe's.
    /// </summary>
    [Fact]
    public async Task Thumbs_on_an_outfit_reach_the_taste_advisory_and_the_numbers_page()
    {
        using var app = new TestApp { Settings = { ["Plans:FreeSuggestionsPerDay"] = "20" } };
        var (me, _, handle) = await app.NewUserAsync("tm_taste");
        await ThreeKeepsAsync(app, me);
        var first = await Json(await ComposeAsync(me));
        var firstId = first.GetProperty("id").GetGuid();
        var names = Names(first);

        Assert.Equal(HttpStatusCode.OK, (await me.PostAsJsonAsync($"/api/tomorrow/{firstId}/useful", new { reason = "not_my_style", note = "too plain for a Tuesday" })).StatusCode);
        var card = await me.GetFromJsonAsync<JsonElement>("/api/users/me/taste");
        var advisory = card.GetProperty("advisory").GetString()!;
        Assert.Contains("Answered 'not my style' 1 time(s)", advisory);
        Assert.Contains("Outfits they turned down: ", advisory);
        foreach (var name in names)
        {
            Assert.Contains("\"" + name + "\"", advisory);
        }

        Assert.Contains("too plain for a Tuesday", advisory);
        // The sentence they turned down is a tip to steer away from, like a check's tip would be.
        Assert.Contains("Tips they turned down", advisory);
        Assert.DoesNotContain("Outfits they said yes to", advisory);
        var rejected = card.GetProperty("facts").GetProperty("rejected").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Single(rejected);

        // A yes on the next outfit, and it is named as one they said yes to; the advisory rides on the next compose.
        var second = await Json(await ComposeAsync(me, "Party"));
        Assert.Equal(HttpStatusCode.OK, (await me.PostAsJsonAsync($"/api/tomorrow/{second.GetProperty("id").GetGuid()}/useful", new { reason = "worked" })).StatusCode);
        advisory = (await me.GetFromJsonAsync<JsonElement>("/api/users/me/taste")).GetProperty("advisory").GetString()!;
        Assert.Contains("Outfits they said yes to: ", advisory);
        Assert.Contains("Past tips: 1 worked", advisory);
        await ComposeAsync(me, "Sport");
        // Round 20: the advisory travels as its own system block (SystemAdvisory), and a planned outfit is never a shared
        // rubric - its tool carries the wardrobe - so it never asks for a cache breakpoint.
        Assert.Contains("Outfits they said yes to", LastCompose(app).SystemAdvisory);
        Assert.Contains("Outfits they turned down", LastCompose(app).SystemAdvisory);
        Assert.False(LastCompose(app).SharedRubric);

        // Learning off: nothing rides along. Clearing: gone.
        Assert.Equal(HttpStatusCode.OK, (await me.PatchAsJsonAsync("/api/users/me/taste", new { learning = false })).StatusCode);
        await ComposeAsync(me, "Formal");
        Assert.DoesNotContain("WEARER'S TASTE", LastCompose(app).SystemText);
        Assert.Equal(HttpStatusCode.OK, (await me.PatchAsJsonAsync("/api/users/me/taste", new { learning = true })).StatusCode);
        Assert.True((await me.DeleteAsync("/api/users/me/taste")).IsSuccessStatusCode);
        var cleared = await me.GetFromJsonAsync<JsonElement>("/api/users/me/taste");
        Assert.True(cleared.GetProperty("empty").GetBoolean());

        // The numbers page: the outfits, the worn rate, the reuse rate, and the reasons beside the wardrobe's.
        Assert.Equal(AdminChange.Changed, await app.PromoteAsync(handle));
        await ComposeAsync(me, "Party");   // a stored answer handed back: one reuse
        var metrics = await me.GetFromJsonAsync<JsonElement>("/api/metrics/pilot");
        var tomorrow = metrics.GetProperty("tomorrow");
        Assert.Equal(4, tomorrow.GetProperty("suggestions").GetInt32());
        // Round 21 review: the people behind them, the number the morning push's go/no-go rule asks for: four outfits, one person.
        Assert.Equal(1, tomorrow.GetProperty("planners").GetInt32());
        Assert.Equal(1, tomorrow.GetProperty("worn").GetInt32());
        Assert.Equal(0.25, tomorrow.GetProperty("wornRate").GetDouble());
        Assert.Equal(1, tomorrow.GetProperty("reused").GetInt32());
        Assert.Equal(0.2, tomorrow.GetProperty("reuseRate").GetDouble());
        Assert.Equal(0, tomorrow.GetProperty("inventedRefs").GetInt32());
        var reasons = tomorrow.GetProperty("reasons").EnumerateArray().ToDictionary(r => r.GetProperty("name").GetString()!, r => r.GetProperty("n").GetInt32());
        Assert.Equal(1, reasons["not_my_style"]);
        Assert.Equal(1, reasons["worked"]);
        var wardrobe = metrics.GetProperty("wardrobe");
        Assert.Equal(2, wardrobe.GetProperty("reasons").GetInt32());
    }

    [Fact]
    public async Task Other_peoples_pieces_never_appear_and_a_removed_piece_keeps_its_name_in_history()
    {
        using var app = new TestApp { Settings = { ["Plans:FreeSuggestionsPerDay"] = "20" } };
        var (a, _, _) = await app.NewUserAsync("tm_a");
        var (b, _, _) = await app.NewUserAsync("tm_b");
        app.Vision.Handler = request => request.Tool.Name == OutfitAnalyzer.ToolName
            ? CheckNaming(("Neon parka", "outerwear"), ("Purple boots", "shoes"))
            : FakeVisionClient.ByTool(request);
        var bCheck = await app.CheckAsync(b);
        await KeepAsync(b, bCheck, "Neon parka");
        await KeepAsync(b, bCheck, "Purple boots");
        app.Vision.Handler = FakeVisionClient.ByTool;
        var (_, items) = await ThreeKeepsAsync(app, a);

        var suggestion = await Json(await ComposeAsync(a));
        Assert.DoesNotContain("Neon parka", LastCompose(app).UserText);
        Assert.DoesNotContain("Purple boots", LastCompose(app).UserText);
        Assert.Equal(3, suggestion.GetProperty("pieces").GetArrayLength());

        // The piece is sold: removed from the wardrobe, gone from the next list, still named in the old outfit.
        var jeans = items["Dark jeans"];
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync($"/api/wardrobe/{jeans}")).StatusCode);
        var read = await Json(await a.GetAsync("/api/tomorrow"));
        var old = Assert.Single(read.GetProperty("recent").EnumerateArray());
        var gone = old.GetProperty("pieces").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Dark jeans");
        Assert.True(!gone.TryGetProperty("itemId", out var itemId) || itemId.ValueKind == JsonValueKind.Null, "a removed piece has no item id");
        Assert.True(old.GetProperty("stale").GetBoolean());
        Assert.Equal(2, read.GetProperty("have").GetInt32());
        var next = await Json(await ComposeAsync(a, fresh: true));
        Assert.DoesNotContain("\"Dark jeans\"", LastCompose(app).UserText);
        Assert.Equal(2, next.GetProperty("pieces").GetArrayLength());
    }
}

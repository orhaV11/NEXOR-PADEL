using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 14 — the wardrobe that builds itself. What these lock: a piece can only be kept from a check that named it,
/// the wardrobe is the account's alone, it can be renamed and deleted, deleting the account takes it, the export carries
/// it, a free account sees its own wardrobe while the Pro-only part is refused with the plan message in all four
/// languages, and the stylist's request carries the names only when it should.
/// Round 20 — filling it faster: keep-all keeps every piece a check named in one request and honours the cap piece by
/// piece, the unkept list is the pieces of the latest looks minus the wardrobe and never another account's, and the Pro
/// moment is true only for a free account past the free slice while the wardrobe is Pro's, with a tally that moves only then.
/// Review of Round 20: a renamed piece is still the kept piece, keep-all keeps only the pieces the row still offered and
/// counts only a request that wrote, the unkept list reaches back to an older look, the moment's "all of them" counts what
/// Pro's prompt would carry, and its tally has the tallies' hourly brake.
/// </summary>
public class WardrobeTests
{
    /// <summary>The three pieces <see cref="Payloads.Ok"/> names, in the stylist's own order.</summary>
    private const string Tee = "White tee";
    private const string Jeans = "Dark jeans";
    private const string Shoes = "Running shoes";

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage response) => await SecurityFixtures.ErrorAsync(response);

    private static void WithDb(TestApp app, Action<AppDbContext> action)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        action(db);
        db.SaveChanges();
    }

    private static void MakePro(TestApp app, Guid id) => WithDb(app, db =>
    {
        var user = db.Users.Single(u => u.Id == id);
        user.Plan = "pro";
        user.ProUntil = DateTime.UtcNow.AddDays(30);
    });

    private static async Task<Guid> KeepAsync(HttpClient client, Guid checkId, string name)
    {
        var response = await client.PostAsJsonAsync("/api/wardrobe", new { checkId, name });
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_piece_is_kept_from_the_check_that_named_it_and_the_list_carries_the_looks_it_was_in()
    {
        using var app = new TestApp();
        var (me, id, _) = await app.NewUserAsync("wr_keep");

        var empty = await Json(await me.GetAsync("/api/wardrobe"));
        Assert.Empty(empty.GetProperty("items").EnumerateArray());

        var checkId = await app.CheckAsync(me);
        var kept = await Json(await me.PostAsJsonAsync("/api/wardrobe", new { checkId, name = Tee }));
        Assert.Equal(Tee, kept.GetProperty("name").GetString());
        Assert.Equal("top", kept.GetProperty("category").GetString());
        Assert.Single(kept.GetProperty("looks").EnumerateArray());

        // The same piece on a second check is one row with two looks, not two rows: "the brown ones you wore on the 4th"
        // needs a history, and a person owns one white tee.
        var second = await app.CheckAsync(me);
        var again = await me.PostAsJsonAsync("/api/wardrobe", new { checkId = second, name = Tee });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);   // 201 only the first time
        var list = await Json(await me.GetAsync("/api/wardrobe"));
        var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal(2, items[0].GetProperty("looks").GetArrayLength());
        Assert.Equal(kept.GetProperty("id").GetGuid(), items[0].GetProperty("id").GetGuid());

        // A check that was published carries its post id, so the list can open the look it names.
        var third = await app.CheckAsync(me);
        var post = await app.PostAsync(me, third);
        await KeepAsync(me, third, Jeans);
        var withPost = await Json(await me.GetAsync("/api/wardrobe"));
        var jeans = withPost.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == Jeans);
        Assert.Equal(post.GetProperty("id").GetGuid(), jeans.GetProperty("looks")[0].GetProperty("postId").GetGuid());
        Assert.Equal(id, id);
    }

    [Fact]
    public async Task Only_a_piece_the_check_named_can_be_kept_and_only_from_your_own_check()
    {
        using var app = new TestApp();
        var (me, _, _) = await app.NewUserAsync("wr_names");
        var (other, _, _) = await app.NewUserAsync("wr_names_b");
        var checkId = await app.CheckAsync(me);

        // Free text is not a wardrobe. The stylist named three pieces; nothing else gets in.
        var invented = await me.PostAsJsonAsync("/api/wardrobe", new { checkId, name = "a Rolex I do not own" });
        Assert.Equal(HttpStatusCode.BadRequest, invented.StatusCode);
        Assert.Equal("That piece isn't one this check named.", await ErrorAsync(invented));

        // Another person's check tells them nothing, not even that it exists.
        var theirs = await other.PostAsJsonAsync("/api/wardrobe", new { checkId, name = Tee });
        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
        Assert.Equal("We couldn't find this check.", await ErrorAsync(theirs));

        // A guest has no wardrobe at all: the route needs a session.
        var guest = app.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/wardrobe")).StatusCode);

        // Casing is the person's, the row is one: "white tee" is the same tee.
        var lower = await me.PostAsJsonAsync("/api/wardrobe", new { checkId, name = "white TEE" });
        Assert.Equal(HttpStatusCode.Created, lower.StatusCode);
        Assert.Single((await Json(await me.GetAsync("/api/wardrobe"))).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_piece_can_be_renamed_and_deleted_and_the_wardrobe_is_the_accounts_alone()
    {
        using var app = new TestApp();
        var (me, _, _) = await app.NewUserAsync("wr_edit");
        var (other, _, _) = await app.NewUserAsync("wr_edit_b");
        var checkId = await app.CheckAsync(me);
        var tee = await KeepAsync(me, checkId, Tee);
        var jeans = await KeepAsync(me, checkId, Jeans);

        var renamed = await Json(await me.PatchAsJsonAsync($"/api/wardrobe/{tee}", new { name = "the soft white tee" }));
        Assert.Equal("the soft white tee", renamed.GetProperty("name").GetString());
        // The looks survive a rename: it is the same piece with a better name.
        Assert.Single(renamed.GetProperty("looks").EnumerateArray());

        // An empty name is refused, and so is one that would collide with another piece of the same account.
        Assert.Equal(HttpStatusCode.BadRequest, (await me.PatchAsJsonAsync($"/api/wardrobe/{tee}", new { name = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await me.PatchAsJsonAsync($"/api/wardrobe/{tee}", new { name = Jeans })).StatusCode);

        // Another account's piece reads as missing on the rename door.
        Assert.Equal(HttpStatusCode.NotFound, (await other.PatchAsJsonAsync($"/api/wardrobe/{tee}", new { name = "mine now" })).StatusCode);
        Assert.Equal("We couldn't find this piece.", await ErrorAsync(await other.PatchAsJsonAsync($"/api/wardrobe/{tee}", new { name = "mine now" })));
        // The delete door says nothing at all: 204 for somebody else's piece and 204 for an id nobody has, so the two
        // cannot be told apart — and the piece is still the owner's afterwards.
        Assert.Equal(HttpStatusCode.NoContent, (await other.DeleteAsync($"/api/wardrobe/{tee}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.DeleteAsync($"/api/wardrobe/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(2, (await Json(await me.GetAsync("/api/wardrobe"))).GetProperty("items").GetArrayLength());
        Assert.Equal(jeans, jeans);

        Assert.Equal(HttpStatusCode.NoContent, (await me.DeleteAsync($"/api/wardrobe/{tee}")).StatusCode);
        // A second tap is not an error: the row is gone, which is what was asked for.
        Assert.Equal(HttpStatusCode.NoContent, (await me.DeleteAsync($"/api/wardrobe/{tee}")).StatusCode);
        var left = (await Json(await me.GetAsync("/api/wardrobe"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Single(left);
        Assert.Equal(Jeans, left[0].GetProperty("name").GetString());
        // The appearances went with the piece, and the check did not.
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.WardrobeAppearances.CountAsync());
        Assert.Equal(1, await db.Checks.CountAsync());
    }

    [Fact]
    public async Task The_fair_use_cap_brakes_a_script_and_never_a_piece_already_kept()
    {
        using var app = new TestApp { Settings = { ["Plans:WardrobeMaxItems"] = "2" } };
        var (me, _, _) = await app.NewUserAsync("wr_cap");
        var checkId = await app.CheckAsync(me);
        await KeepAsync(me, checkId, Tee);
        await KeepAsync(me, checkId, Jeans);

        var third = await me.PostAsJsonAsync("/api/wardrobe", new { checkId, name = Shoes });
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal("Your wardrobe is full at 2 pieces. Remove one to keep another.", await ErrorAsync(third));

        // A piece already kept is never refused by the cap: keeping it again only adds this look to its row.
        var again = await app.CheckAsync(me);
        Assert.Equal(HttpStatusCode.OK, (await me.PostAsJsonAsync("/api/wardrobe", new { checkId = again, name = Tee })).StatusCode);
        Assert.Equal(2, (await Json(await me.GetAsync("/api/wardrobe"))).GetProperty("max").GetInt32());
    }

    [Fact]
    public async Task A_free_account_sees_its_wardrobe_and_the_pro_only_part_is_refused_in_four_languages()
    {
        var expected = new Dictionary<string, string>
        {
            ["en"] = "This one is for Pro.",
            ["he"] = "זה לפרו.",
            ["ar"] = "هذه لمشتركي Pro.",
            ["ru"] = "Это только с Pro."
        };

        using var app = new TestApp { Settings = { ["Languages:Enabled:0"] = "en", ["Languages:Enabled:1"] = "he", ["Languages:Enabled:2"] = "ar", ["Languages:Enabled:3"] = "ru" } };
        foreach (var (language, message) in expected)
        {
            var (free, _, _) = await app.NewUserAsync("wr_pro_" + language, language);
            var checkId = await app.CheckAsync(free, language: language);
            // The list itself is everyone's: it cannot build itself behind a wall.
            await KeepAsync(free, checkId, Tee);
            var list = await Json(await free.GetAsync("/api/wardrobe"));
            Assert.Single(list.GetProperty("items").EnumerateArray());
            Assert.False(list.GetProperty("stylistAvailable").GetBoolean());

            // The advice from it is what Pro sells, and the refusal is the plan message in the caller's language.
            var refused = await free.PostAsJsonAsync("/api/wardrobe/stylist", new { on = true });
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(message, await ErrorAsync(refused));
        }
    }

    [Fact]
    public async Task Pro_may_turn_the_stylist_switch_off_and_on()
    {
        using var app = new TestApp();
        var (me, id, _) = await app.NewUserAsync("wr_switch");
        MakePro(app, id);

        var before = await Json(await me.GetAsync("/api/wardrobe"));
        Assert.True(before.GetProperty("stylistAvailable").GetBoolean());
        Assert.True(before.GetProperty("toStylist").GetBoolean());   // on by default: that is the feature

        var off = await Json(await me.PostAsJsonAsync("/api/wardrobe/stylist", new { on = false }));
        Assert.False(off.GetProperty("toStylist").GetBoolean());
        Assert.False((await Json(await me.GetAsync("/api/wardrobe"))).GetProperty("toStylist").GetBoolean());

        var on = await Json(await me.PostAsJsonAsync("/api/wardrobe/stylist", new { on = true }));
        Assert.True(on.GetProperty("toStylist").GetBoolean());
    }

    [Fact]
    public async Task The_stylists_request_carries_the_wardrobe_only_when_it_should()
    {
        using var app = new TestApp();
        var (free, _, _) = await app.NewUserAsync("wr_prompt_free");
        var (pro, proId, _) = await app.NewUserAsync("wr_prompt_pro");
        MakePro(app, proId);

        // Nothing kept: the call is the one it always was, with no wardrobe paragraph at all.
        await app.CheckAsync(pro);
        Assert.DoesNotContain("wearer's own wardrobe", app.Vision.Requests[^1].UserText, StringComparison.OrdinalIgnoreCase);

        var proCheck = await app.CheckAsync(pro);
        await KeepAsync(pro, proCheck, Tee);
        await app.CheckAsync(pro);
        var carried = app.Vision.Requests[^1].UserText;
        Assert.Contains("wearer's own wardrobe", carried, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Tee, carried, StringComparison.Ordinal);
        // The rule the wardrobe exists for, and the guard that stops it moving the number.
        Assert.Contains("name THAT piece instead of something to buy", carried, StringComparison.Ordinal);
        Assert.Contains("never a reason for a higher or lower score", carried, StringComparison.Ordinal);

        // Switched off by its owner: kept, listed, and not sent.
        await pro.PostAsJsonAsync("/api/wardrobe/stylist", new { on = false });
        await app.CheckAsync(pro);
        Assert.DoesNotContain("wearer's own wardrobe", app.Vision.Requests[^1].UserText, StringComparison.OrdinalIgnoreCase);

        // A free account keeps pieces and the stylist never sees them: that is the line Pro is sold on.
        var freeCheck = await app.CheckAsync(free);
        await KeepAsync(free, freeCheck, Tee);
        await app.CheckAsync(free);
        Assert.DoesNotContain("wearer's own wardrobe", app.Vision.Requests[^1].UserText, StringComparison.OrdinalIgnoreCase);

        // A guest never has one, and the route is never asked for one.
        var guest = app.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.DoesNotContain("wearer's own wardrobe", app.Vision.Requests[^1].UserText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_piece_that_names_a_person_never_reaches_the_stylist()
    {
        using var app = new TestApp();
        var (me, id, _) = await app.NewUserAsync("wr_rule1");
        MakePro(app, id);
        var checkId = await app.CheckAsync(me);
        var tee = await KeepAsync(me, checkId, Tee);
        var jeans = await KeepAsync(me, checkId, Jeans);

        // A rename is free text the person typed, and rule 1 holds there too: the row stays, the name does not travel.
        await me.PatchAsJsonAsync($"/api/wardrobe/{tee}", new { name = "the tee that hides my belly" });
        await app.CheckAsync(me);
        var text = app.Vision.Requests[^1].UserText;
        Assert.DoesNotContain("belly", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Jeans, text, StringComparison.Ordinal);
        Assert.Equal(jeans, jeans);

        // Nothing was deleted: the wardrobe is the person's, whatever they call their clothes.
        Assert.Equal(2, (await Json(await me.GetAsync("/api/wardrobe"))).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Only_a_handful_of_names_travel_most_recently_worn_first()
    {
        // Round 16: the Pro slice is its own number and this account is Pro, so both have to be pinned or the wider
        // one wins and nothing is cut. The test is about the cut, not about which plan gets how much.
        using var app = new TestApp
        {
            Settings = { ["Plans:WardrobeNamesToStylist"] = "1", ["Plans:WardrobeNamesToStylistPro"] = "1" }
        };
        var (me, id, _) = await app.NewUserAsync("wr_few");
        MakePro(app, id);

        var first = await app.CheckAsync(me);
        await KeepAsync(me, first, Tee);
        var second = await app.CheckAsync(me);
        await KeepAsync(me, second, Jeans);

        await app.CheckAsync(me);
        var text = app.Vision.Requests[^1].UserText;
        Assert.Contains(Jeans, text, StringComparison.Ordinal);      // the newer look wins the one slot
        Assert.DoesNotContain(Tee, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zero_names_turns_the_wardrobe_off_in_the_prompt_without_touching_the_list()
    {
        using var app = new TestApp { Settings = { ["Plans:WardrobeNamesToStylist"] = "0" } };
        var (me, id, _) = await app.NewUserAsync("wr_off");
        MakePro(app, id);
        var checkId = await app.CheckAsync(me);
        await KeepAsync(me, checkId, Tee);

        await app.CheckAsync(me);
        Assert.DoesNotContain("wearer's own wardrobe", app.Vision.Requests[^1].UserText, StringComparison.OrdinalIgnoreCase);
        Assert.Single((await Json(await me.GetAsync("/api/wardrobe"))).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task The_export_carries_the_wardrobe_and_deleting_the_account_takes_it()
    {
        using var app = new TestApp();
        var (me, id, _) = await app.NewUserAsync("wr_export");
        var checkId = await app.CheckAsync(me);
        await KeepAsync(me, checkId, Tee);
        await KeepAsync(me, checkId, Jeans);

        var export = await Json(await me.GetAsync("/api/users/me/export"));
        var wardrobe = export.GetProperty("wardrobe").EnumerateArray().ToList();
        Assert.Equal(2, wardrobe.Count);
        Assert.Contains(wardrobe, item => item.GetProperty("name").GetString() == Tee && item.GetProperty("category").GetString() == "top");
        Assert.Equal(checkId, wardrobe[0].GetProperty("looks")[0].GetGuid());

        Assert.Equal(HttpStatusCode.NoContent, (await me.DeleteAsync("/api/users/me")).StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.WardrobeItems.CountAsync(i => i.UserId == id));
        Assert.Equal(0, await db.WardrobeAppearances.CountAsync());
        Assert.Equal(0, await db.WardrobeSettings.CountAsync(s => s.UserId == id));
    }

    [Fact]
    public void A_name_is_cleaned_capped_and_keyed_the_way_a_stored_string_must_be()
    {
        Assert.Equal("camel coat", Wardrobe.CleanName("  camel\tcoat  "));
        Assert.Equal("camel 'coat'", Wardrobe.CleanName("camel \"coat\""));
        Assert.Equal("", Wardrobe.CleanName(null));
        Assert.Equal("", Wardrobe.CleanName("   "));
        var long_ = new string('a', 20) + " " + new string('b', 60);
        Assert.True(Wardrobe.CleanName(long_).Length <= Wardrobe.NameMaxLength);
        Assert.Equal("camel coat", Wardrobe.KeyOf(Wardrobe.CleanName("Camel Coat")));
        Assert.Equal("other", Wardrobe.CleanCategory("hat"));
        Assert.Equal("shoes", Wardrobe.CleanCategory("SHOES"));
    }

    [Fact]
    public void The_prompt_list_is_clothes_only_deduplicated_and_free_of_any_word_about_a_person()
    {
        var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var items = new List<WardrobeItem>
        {
            new() { Name = "camel coat", NameKey = "camel coat", Category = "outerwear", LastSeenAt = now },
            new() { Name = "the one that hides my legs", NameKey = "the one that hides my legs", Category = "bottom", LastSeenAt = now.AddDays(-1) },
            new() { Name = "something", NameKey = "something", Category = "other", LastSeenAt = now.AddDays(-2) },
            new() { Name = "Camel Coat", NameKey = "camel coat", Category = "outerwear", LastSeenAt = now.AddDays(-3) },
            new() { Name = "gold hoops", NameKey = "gold hoops", Category = "accessory", LastSeenAt = now.AddDays(-4) }
        };

        Assert.Equal(["camel coat", "gold hoops"], Wardrobe.PromptNames(items, 8));
        Assert.Equal(["camel coat"], Wardrobe.PromptNames(items, 1));
        Assert.Empty(Wardrobe.PromptNames(items, 0));
        Assert.Empty(Wardrobe.PromptNames([], 8));
        // Nothing to send is no paragraph at all: the call is byte for byte the one it always was.
        Assert.Equal("", OutfitAnalyzer.BuildWardrobeBlock([]));
        Assert.Equal("", OutfitAnalyzer.BuildWardrobeBlock(null));
    }

    // ---------- Round 20 — filling the closet faster ----------

    /// <summary>A Counter row's value, or 0 when the tally has never moved.</summary>
    private static long CounterOf(TestApp app, string name)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Counters.Where(c => c.Name == name).Select(c => (long?)c.Value).FirstOrDefault() ?? 0;
    }

    private static List<string> Names(JsonElement items) => items.EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();

    /// <summary>One check whose stylist named exactly these pieces, with the fake stylist put back afterwards.</summary>
    private static async Task<Guid> CheckNamingAsync(TestApp app, HttpClient client, params (string Name, string Category)[] pieces)
    {
        var json = JsonSerializer.Serialize(new
        {
            status = "ok", score = 7, intent_match = 70, headline = "Warm", vibe = "winter",
            items = pieces.Select(p => new { name = p.Name, category = p.Category, verdict = "works", note = "Fine." }),
            working = new[] { "Warm" }, one_tip = "Add a scarf."
        });
        app.Vision.Handler = _ => Payloads.Parse(json);
        try
        {
            return await app.CheckAsync(client);
        }
        finally
        {
            app.Vision.Handler = FakeVisionClient.ByTool;
        }
    }

    [Fact]
    public async Task Keep_all_keeps_every_piece_the_check_named_in_one_request_and_a_second_check_adds_only_looks()
    {
        using var app = new TestApp();
        var (me, _, _) = await app.NewUserAsync("wr_all");
        var first = await app.CheckAsync(me);

        var kept = await Json(await me.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = first }));
        Assert.Equal(3, kept.GetProperty("kept").GetInt32());
        Assert.Equal(3, kept.GetProperty("added").GetInt32());
        Assert.Equal(0, kept.GetProperty("skipped").GetInt32());
        Assert.False(kept.GetProperty("full").GetBoolean());
        Assert.Equal(3, kept.GetProperty("count").GetInt32());
        Assert.Equal(200, kept.GetProperty("max").GetInt32());
        // The stylist's own order, items then accessories, the same list the keep row offers one by one.
        Assert.Equal([Tee, Jeans, Shoes], Names(kept.GetProperty("items")));
        var ids = kept.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(3, (await Json(await me.GetAsync("/api/wardrobe"))).GetProperty("items").GetArrayLength());

        // The same three on a second check: nothing new, every row gains a look, and the rows are the same rows — one
        // per piece, which is what the unique index promises and what "the one you wore on the 4th" needs.
        var second = await app.CheckAsync(me);
        var again = await Json(await me.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = second }));
        Assert.Equal(3, again.GetProperty("kept").GetInt32());
        Assert.Equal(0, again.GetProperty("added").GetInt32());
        Assert.Equal(3, again.GetProperty("count").GetInt32());
        var rows = again.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(ids, rows.Select(r => r.GetProperty("id").GetGuid()).ToList());
        Assert.All(rows, r => Assert.Equal(2, r.GetProperty("looks").GetArrayLength()));
        Assert.Equal(3, (await Json(await me.GetAsync("/api/wardrobe"))).GetProperty("items").GetArrayLength());

        // Two taps that wrote something, two on the tally.
        Assert.Equal(2, CounterOf(app, CounterName.WardrobeKeepAll));

        // Review of Round 20: the same request again writes nothing, so it is a 200 that says what is there and the tally
        // does not move.
        var repeat = await Json(await me.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = second }));
        Assert.Equal(3, repeat.GetProperty("kept").GetInt32());
        Assert.Equal(0, repeat.GetProperty("added").GetInt32());
        Assert.All(repeat.GetProperty("items").EnumerateArray(), r => Assert.Equal(2, r.GetProperty("looks").GetArrayLength()));
        Assert.Equal(2, CounterOf(app, CounterName.WardrobeKeepAll));
    }

    [Fact]
    public async Task Keep_all_stops_at_the_cap_keeps_what_fits_and_refuses_plainly_when_nothing_fits()
    {
        using var app = new TestApp { Settings = { ["Plans:WardrobeMaxItems"] = "2" } };
        var (me, _, _) = await app.NewUserAsync("wr_all_cap");
        var first = await app.CheckAsync(me);

        // Three named, two fit: the first two in the stylist's order are kept, the third is skipped and the answer says so.
        var kept = await Json(await me.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = first }));
        Assert.Equal(2, kept.GetProperty("kept").GetInt32());
        Assert.Equal(2, kept.GetProperty("added").GetInt32());
        Assert.Equal(1, kept.GetProperty("skipped").GetInt32());
        Assert.True(kept.GetProperty("full").GetBoolean());
        Assert.Equal(2, kept.GetProperty("count").GetInt32());
        Assert.Equal(2, kept.GetProperty("max").GetInt32());
        Assert.Equal([Tee, Jeans], Names(kept.GetProperty("items")));

        // A full wardrobe and a check naming three other pieces: nothing can be written, and the refusal is the single
        // route's own sentence rather than a 200 that kept nothing.
        var (other, _, _) = await app.NewUserAsync("wr_all_cap_b");
        var theirs = await app.CheckAsync(other);
        await KeepAsync(other, theirs, Tee);
        await KeepAsync(other, theirs, Jeans);
        app.Vision.Handler = _ => Payloads.Parse("""
            {
              "status": "ok", "score": 7, "intent_match": 70, "headline": "Warm and neat", "vibe": "winter city",
              "items": [
                { "name": "Camel coat", "category": "outerwear", "verdict": "works", "note": "Sharp." },
                { "name": "Black tights", "category": "bottom", "verdict": "neutral", "note": "Fine." },
                { "name": "Loafers", "category": "shoes", "verdict": "works", "note": "Right weight." }
              ],
              "working": ["The palette is warm"], "one_tip": "Swap the black tights for brown ones."
            }
            """);
        Guid coats;
        try
        {
            coats = await app.CheckAsync(other);
        }
        finally
        {
            app.Vision.Handler = FakeVisionClient.ByTool;
        }

        var refused = await other.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = coats });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Your wardrobe is full at 2 pieces. Remove one to keep another.", await ErrorAsync(refused));
        Assert.Equal([Jeans, Tee], Names((await Json(await other.GetAsync("/api/wardrobe"))).GetProperty("items")).OrderBy(n => n).ToList());
        Assert.Equal(1, CounterOf(app, CounterName.WardrobeKeepAll));   // only the tap that wrote something

        // Known pieces are never refused by the cap: the first account's second check adds a look to its two rows and
        // the third piece is skipped again, which is a 200 with kept 2, not a refusal.
        var second = await app.CheckAsync(me);
        var again = await Json(await me.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = second }));
        Assert.Equal(2, again.GetProperty("kept").GetInt32());
        Assert.Equal(0, again.GetProperty("added").GetInt32());
        Assert.Equal(1, again.GetProperty("skipped").GetInt32());
        Assert.True(again.GetProperty("full").GetBoolean());
        Assert.All(again.GetProperty("items").EnumerateArray(), r => Assert.Equal(2, r.GetProperty("looks").GetArrayLength()));
        Assert.Equal(2, CounterOf(app, CounterName.WardrobeKeepAll));

        // Review of Round 20: the same request again finds its two looks already written and the third piece still over
        // the cap. Nothing at all can be written, so it is the refusal, not a 200 that counts a tap.
        var nothing = await me.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = second });
        Assert.Equal(HttpStatusCode.Conflict, nothing.StatusCode);
        Assert.Equal("Your wardrobe is full at 2 pieces. Remove one to keep another.", await ErrorAsync(nothing));
        Assert.Equal(2, CounterOf(app, CounterName.WardrobeKeepAll));
    }

    [Fact]
    public async Task Keep_all_keeps_only_the_pieces_the_row_still_offered_and_never_a_name_the_check_did_not_give()
    {
        using var app = new TestApp();
        var (me, _, _) = await app.NewUserAsync("wr_all_names");
        var first = await app.CheckAsync(me);

        // "Not this one" on the tee, then "Keep all 2": the row sends the two it still offers, and the tee stays out.
        var kept = await Json(await me.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = first, names = new[] { Jeans, Shoes } }));
        Assert.Equal(2, kept.GetProperty("kept").GetInt32());
        Assert.Equal(2, kept.GetProperty("added").GetInt32());
        Assert.Equal(2, kept.GetProperty("count").GetInt32());
        Assert.Equal([Jeans, Shoes], Names(kept.GetProperty("items")));
        // A piece under the stylist's own name carries no keptAs (only a renamed one does).
        Assert.All(kept.GetProperty("items").EnumerateArray(), i => Assert.False(i.TryGetProperty("keptAs", out _)));
        Assert.Equal([Jeans, Shoes], Names((await Json(await me.GetAsync("/api/wardrobe"))).GetProperty("items")).Order().ToList());
        // The refused piece is where every refused piece goes: back on the unkept list, from the check that named it.
        var unkept = (await Json(await me.GetAsync("/api/wardrobe/unkept"))).GetProperty("pieces");
        Assert.Equal([Tee], Names(unkept));
        Assert.Equal(first, unkept[0].GetProperty("checkId").GetGuid());

        // The names only take pieces off the check's own list: one it never named is ignored, so the body is never free
        // text, and the casing is the person's, as on the single route.
        var second = await app.CheckAsync(me);
        var narrowed = await Json(await me.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = second, names = new[] { "a Rolex I do not own", "white TEE" } }));
        Assert.Equal([Tee], Names(narrowed.GetProperty("items")));
        Assert.Equal(1, narrowed.GetProperty("kept").GetInt32());
        Assert.Equal(1, narrowed.GetProperty("added").GetInt32());
        Assert.Equal([Jeans, Shoes, Tee], Names((await Json(await me.GetAsync("/api/wardrobe"))).GetProperty("items")).Order().ToList());

        // An empty list keeps nothing, writes nothing and counts nothing.
        var none = await Json(await me.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = second, names = Array.Empty<string>() }));
        Assert.Equal(0, none.GetProperty("kept").GetInt32());
        Assert.Empty(none.GetProperty("items").EnumerateArray());
        Assert.Equal(3, none.GetProperty("count").GetInt32());
        Assert.Equal(2, CounterOf(app, CounterName.WardrobeKeepAll));
    }

    [Fact]
    public async Task A_renamed_piece_is_still_the_kept_piece_on_the_unkept_list_and_on_every_later_keep()
    {
        using var app = new TestApp { Settings = { ["Plans:WardrobeMaxItems"] = "2" } };
        var (me, _, _) = await app.NewUserAsync("wr_renamed");
        var first = await app.CheckAsync(me);
        var tee = await KeepAsync(me, first, Tee);
        var jeans = await KeepAsync(me, first, Jeans);
        // The jeans stand for a row kept before the stylist's key was recorded: its first rename records the key it had.
        WithDb(app, db => db.WardrobeItems.Single(i => i.Id == jeans).StylistKey = null);
        await Json(await me.PatchAsJsonAsync($"/api/wardrobe/{tee}", new { name = "the soft white tee" }));
        await Json(await me.PatchAsJsonAsync($"/api/wardrobe/{jeans}", new { name = "my good jeans" }));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("white tee", db.WardrobeItems.Single(i => i.Id == tee).StylistKey);
            Assert.Equal("dark jeans", db.WardrobeItems.Single(i => i.Id == jeans).StylistKey);
        }

        // A rename is the same piece: the unkept list does not offer the stylist's names for them back.
        Assert.Equal([Shoes], Names((await Json(await me.GetAsync("/api/wardrobe/unkept"))).GetProperty("pieces")));

        // The single route with the stylist's name adds a look to the renamed row: a 200, never a second tee, and never
        // the cap's refusal (the wardrobe is full at two), because the piece is already kept.
        var second = await app.CheckAsync(me);
        var again = await me.PostAsJsonAsync("/api/wardrobe", new { checkId = second, name = Tee });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var row = await Json(again);
        Assert.Equal(tee, row.GetProperty("id").GetGuid());
        Assert.Equal("the soft white tee", row.GetProperty("name").GetString());
        Assert.Equal(2, row.GetProperty("looks").GetArrayLength());
        // The row says which word it was kept under, so the keep row can leave the stylist's name out as well.
        Assert.Equal("white tee", row.GetProperty("keptAs").GetString());

        // Keep-all on a third check does the same for both: two looks added to the renamed rows, the shoes over the cap.
        var third = await app.CheckAsync(me);
        var all = await Json(await me.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = third }));
        Assert.Equal(2, all.GetProperty("kept").GetInt32());
        Assert.Equal(0, all.GetProperty("added").GetInt32());
        Assert.Equal(1, all.GetProperty("skipped").GetInt32());
        Assert.Equal(["the soft white tee", "my good jeans"], Names(all.GetProperty("items")));
        var list = (await Json(await me.GetAsync("/api/wardrobe"))).GetProperty("items");
        Assert.Equal(["my good jeans", "the soft white tee"], Names(list).Order().ToList());
        Assert.Equal(3, list.EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == tee).GetProperty("looks").GetArrayLength());
        Assert.Equal(2, list.EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == jeans).GetProperty("looks").GetArrayLength());
    }

    [Fact]
    public async Task Keep_all_is_the_owners_alone_and_a_check_that_named_nothing_keeps_nothing()
    {
        using var app = new TestApp();
        var (a, _, _) = await app.NewUserAsync("wr_all_a");
        var (b, _, _) = await app.NewUserAsync("wr_all_b");
        var bCheck = await app.CheckAsync(b);

        // Another account's check answers exactly as a missing one: nobody learns it exists.
        var theirs = await a.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = bCheck });
        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
        Assert.Equal("We couldn't find this check.", await ErrorAsync(theirs));
        var madeUp = await a.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, madeUp.StatusCode);
        Assert.Equal("We couldn't find this check.", await ErrorAsync(madeUp));
        Assert.Empty((await Json(await b.GetAsync("/api/wardrobe"))).GetProperty("items").EnumerateArray());

        // No check at all is a bad request, not a search.
        var none = await a.PostAsJsonAsync("/api/wardrobe/keep-all", new { });
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal("That request didn't look right.", await ErrorAsync(none));

        // A photo of a desk named no pieces: there is nothing to refuse and nothing to write, and the answer says zero.
        app.Vision.Handler = _ => Payloads.NotOutfit();
        Guid desk;
        try
        {
            desk = await app.CheckAsync(a);
        }
        finally
        {
            app.Vision.Handler = FakeVisionClient.ByTool;
        }

        var nothing = await Json(await a.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = desk }));
        Assert.Equal(0, nothing.GetProperty("kept").GetInt32());
        Assert.Equal(0, nothing.GetProperty("added").GetInt32());
        Assert.False(nothing.GetProperty("full").GetBoolean());
        Assert.Empty(nothing.GetProperty("items").EnumerateArray());
        Assert.Empty((await Json(await a.GetAsync("/api/wardrobe"))).GetProperty("items").EnumerateArray());
        Assert.Equal(0, CounterOf(app, CounterName.WardrobeKeepAll));
    }

    [Fact]
    public async Task The_unkept_list_is_the_pieces_of_the_last_checks_minus_the_wardrobe_newest_first_and_never_another_accounts()
    {
        using var app = new TestApp();
        var (a, aId, _) = await app.NewUserAsync("wr_unkept");
        var (b, _, _) = await app.NewUserAsync("wr_unkept_b");
        // Review of Round 20: the older look names a piece the newer one does not (the coat) and one it does (the jeans).
        var older = await CheckNamingAsync(app, a, ("Camel coat", "outerwear"), (Jeans, "bottom"));
        var newer = await app.CheckAsync(a);
        await KeepAsync(a, newer, Tee);

        // The tee is kept, so the list is the newer look's other two, then the coat only the older look named: each once,
        // the jeans carrying the NEWER check that named them (the one the keep route will validate against) and its time,
        // the coat carrying the older one.
        var unkept = await Json(await a.GetAsync("/api/wardrobe/unkept"));
        Assert.Equal(2, unkept.GetProperty("checks").GetInt32());
        var pieces = unkept.GetProperty("pieces").EnumerateArray().ToList();
        Assert.Equal([Jeans, Shoes, "Camel coat"], pieces.Select(p => p.GetProperty("name").GetString()).ToList());
        DateTime newerAt, olderAt;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            newerAt = DateTime.SpecifyKind(db.Checks.Single(c => c.Id == newer).CreatedAt, DateTimeKind.Utc);
            olderAt = DateTime.SpecifyKind(db.Checks.Single(c => c.Id == older).CreatedAt, DateTimeKind.Utc);
        }

        foreach (var piece in pieces)
        {
            var fromOlder = piece.GetProperty("name").GetString() == "Camel coat";
            Assert.Equal(fromOlder ? older : newer, piece.GetProperty("checkId").GetGuid());
            Assert.Equal(fromOlder ? olderAt : newerAt, piece.GetProperty("wornAt").GetDateTime().ToUniversalTime());
            Assert.False(piece.TryGetProperty("postId", out _));   // a private look has no post to open
        }

        Assert.Equal("bottom", pieces[0].GetProperty("category").GetString());
        Assert.Equal("outerwear", pieces[2].GetProperty("category").GetString());

        // Keeping from the list is the single route with the check the piece carries; the piece then leaves the list. The
        // coat is kept from the older look, which is the only one that named it.
        await KeepAsync(a, pieces[0].GetProperty("checkId").GetGuid(), pieces[0].GetProperty("name").GetString()!);
        await KeepAsync(a, pieces[2].GetProperty("checkId").GetGuid(), pieces[2].GetProperty("name").GetString()!);
        var left = (await Json(await a.GetAsync("/api/wardrobe/unkept"))).GetProperty("pieces").EnumerateArray().ToList();
        Assert.Single(left);
        Assert.Equal(Shoes, left[0].GetProperty("name").GetString());
        var coat = (await Json(await a.GetAsync("/api/wardrobe"))).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == "Camel coat");
        Assert.Equal(older, coat.GetProperty("looks").EnumerateArray().Single().GetProperty("checkId").GetGuid());

        // A published look carries its post, so the row can open it.
        var post = await app.PostAsync(a, newer);
        var posted = (await Json(await a.GetAsync("/api/wardrobe/unkept"))).GetProperty("pieces")[0];
        Assert.Equal(post.GetProperty("id").GetGuid(), posted.GetProperty("postId").GetGuid());

        // Another account sees its own looks only: nothing at all while it has not checked, then its own check alone.
        var theirs = await Json(await b.GetAsync("/api/wardrobe/unkept"));
        Assert.Empty(theirs.GetProperty("pieces").EnumerateArray());
        Assert.Equal(0, theirs.GetProperty("checks").GetInt32());
        var bCheck = await app.CheckAsync(b);
        var bList = await Json(await b.GetAsync("/api/wardrobe/unkept"));
        Assert.Equal(1, bList.GetProperty("checks").GetInt32());
        Assert.Equal([Tee, Jeans, Shoes], Names(bList.GetProperty("pieces")));
        Assert.All(bList.GetProperty("pieces").EnumerateArray(), p => Assert.Equal(bCheck, p.GetProperty("checkId").GetGuid()));
        // And A's list is still made of A's checks alone.
        List<Guid> aChecks;
        using (var scope = app.Services.CreateScope())
        {
            aChecks = scope.ServiceProvider.GetRequiredService<AppDbContext>().Checks.Where(c => c.UserId == aId).Select(c => c.Id).ToList();
        }

        Assert.All((await Json(await a.GetAsync("/api/wardrobe/unkept"))).GetProperty("pieces").EnumerateArray(),
            p => Assert.Contains(p.GetProperty("checkId").GetGuid(), aChecks));

        // How far back the list looks is the setting. One check back sees only the newest check's pieces; zero hides it.
        using var one = new TestApp { Settings = { ["Plans:WardrobeUnkeptChecks"] = "1" } };
        var (c, _, _) = await one.NewUserAsync("wr_unkept_one");
        one.Vision.Handler = _ => Payloads.Parse("""
            {
              "status": "ok", "score": 7, "intent_match": 70, "headline": "Warm", "vibe": "winter",
              "items": [ { "name": "Camel coat", "category": "outerwear", "verdict": "works", "note": "Sharp." } ],
              "working": ["Warm"], "one_tip": "Add a scarf."
            }
            """);
        try
        {
            await one.CheckAsync(c);
        }
        finally
        {
            one.Vision.Handler = FakeVisionClient.ByTool;
        }

        await one.CheckAsync(c);
        var recent = await Json(await c.GetAsync("/api/wardrobe/unkept"));
        Assert.Equal(1, recent.GetProperty("checks").GetInt32());
        Assert.Equal([Tee, Jeans, Shoes], recent.GetProperty("pieces").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList());

        using var off = new TestApp { Settings = { ["Plans:WardrobeUnkeptChecks"] = "0" } };
        var (d, _, _) = await off.NewUserAsync("wr_unkept_off");
        await off.CheckAsync(d);
        var hidden = await Json(await d.GetAsync("/api/wardrobe/unkept"));
        Assert.Empty(hidden.GetProperty("pieces").EnumerateArray());
        Assert.Equal(0, hidden.GetProperty("checks").GetInt32());
    }

    [Fact]
    public async Task The_pro_moment_is_true_for_a_free_account_past_the_free_slice_and_never_for_pro_or_where_the_wardrobe_is_everyones()
    {
        using var app = new TestApp { Settings = { ["Plans:WardrobeNamesToStylist"] = "2" } };
        var (me, id, _) = await app.NewUserAsync("wr_moment");
        var checkId = await app.CheckAsync(me);
        await KeepAsync(me, checkId, Tee);
        await KeepAsync(me, checkId, Jeans);

        // At the free slice the stylist would see every piece a free account could send, so there is nothing to say.
        // proSees is how many of THESE pieces Pro's prompt would carry (review of Round 20; it was the slice's size, 40,
        // which let "all of them" stand over pieces Pro never sends): both of them here.
        var two = await Json(await me.GetAsync("/api/wardrobe"));
        Assert.False(two.GetProperty("proMoment").GetBoolean());
        Assert.Equal(2, two.GetProperty("proSees").GetInt32());

        // One past it the sentence is a fact about this server, and Pro would carry all three.
        await KeepAsync(me, checkId, Shoes);
        var three = await Json(await me.GetAsync("/api/wardrobe"));
        Assert.True(three.GetProperty("proMoment").GetBoolean());
        Assert.Equal(3, three.GetProperty("proSees").GetInt32());

        // A piece the stylist filed as "other" never travels, nor does a name about a person, so "all of them" would be a
        // promise Pro does not keep: proSees falls below the count and the client names the number instead.
        var odd = await CheckNamingAsync(app, me, ("Mystery piece", "other"));
        await KeepAsync(me, odd, "Mystery piece");
        var jeans = (await Json(await me.GetAsync("/api/wardrobe"))).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == Jeans).GetProperty("id").GetGuid();
        await Json(await me.PatchAsJsonAsync($"/api/wardrobe/{jeans}", new { name = "the jeans that hide my belly" }));
        var four = await Json(await me.GetAsync("/api/wardrobe"));
        Assert.Equal(4, four.GetProperty("items").GetArrayLength());
        Assert.True(four.GetProperty("proMoment").GetBoolean());
        Assert.Equal(2, four.GetProperty("proSees").GetInt32());

        // The tally moves while the moment is true, and only then.
        Assert.Equal(HttpStatusCode.NoContent, (await me.PostAsJsonAsync("/api/wardrobe/moment", new { step = "shown" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await me.PostAsJsonAsync("/api/wardrobe/moment", new { step = "go" })).StatusCode);
        Assert.Equal(1, CounterOf(app, CounterName.WardrobeMomentShown));
        Assert.Equal(1, CounterOf(app, CounterName.WardrobeMomentGo));

        // Pro already sees the wardrobe: no moment, and its posts count nothing.
        MakePro(app, id);
        Assert.False((await Json(await me.GetAsync("/api/wardrobe"))).GetProperty("proMoment").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await me.PostAsJsonAsync("/api/wardrobe/moment", new { step = "shown" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await me.PostAsJsonAsync("/api/wardrobe/moment", new { step = "go" })).StatusCode);
        Assert.Equal(1, CounterOf(app, CounterName.WardrobeMomentShown));
        Assert.Equal(1, CounterOf(app, CounterName.WardrobeMomentGo));

        // A step that is neither word is a bad request, not a silent nothing.
        var bad = await me.PostAsJsonAsync("/api/wardrobe/moment", new { step = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("That request didn't look right.", await ErrorAsync(bad));

        // Where the wardrobe reaches everyone's stylist there is nothing Pro adds, so there is no moment at any count.
        using var everyone = new TestApp { Settings = { ["Plans:WardrobeNamesToStylist"] = "2", ["Plans:WardrobeNeedsPro"] = "false" } };
        var (free, _, _) = await everyone.NewUserAsync("wr_moment_all");
        var check = await everyone.CheckAsync(free);
        await KeepAsync(free, check, Tee);
        await KeepAsync(free, check, Jeans);
        await KeepAsync(free, check, Shoes);
        var open = await Json(await free.GetAsync("/api/wardrobe"));
        Assert.False(open.GetProperty("proMoment").GetBoolean());
        Assert.True(open.GetProperty("stylistAvailable").GetBoolean());

        // Review of Round 20: a free wardrobe past the slice that Pro's prompt would carry none of (all "other") has no
        // moment at all: Pro would show the stylist nothing more.
        var (clutter, _, _) = await app.NewUserAsync("wr_moment_other");
        var others = await CheckNamingAsync(app, clutter, ("Thing one", "other"), ("Thing two", "other"), ("Thing three", "other"));
        Assert.Equal(3, (await Json(await clutter.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = others }))).GetProperty("added").GetInt32());
        var none = await Json(await clutter.GetAsync("/api/wardrobe"));
        Assert.Equal(0, none.GetProperty("proSees").GetInt32());
        Assert.False(none.GetProperty("proMoment").GetBoolean());
    }

    [Fact]
    public async Task The_moment_tally_has_the_hourly_brake_the_other_tallies_have()
    {
        // Review of Round 20: the tally is client-driven, so it carries the per-account hourly brake of the tip's answers.
        using var app = new TestApp { Settings = { ["Plans:WardrobeNamesToStylist"] = "2" } };
        var (me, _, _) = await app.NewUserAsync("wr_moment_brake");
        Assert.Equal(3, (await Json(await me.PostAsJsonAsync("/api/wardrobe/keep-all", new { checkId = await app.CheckAsync(me) }))).GetProperty("added").GetInt32());
        for (var i = 0; i < FeedbackEndpoints.PerHour; i++)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await me.PostAsJsonAsync("/api/wardrobe/moment", new { step = "go" })).StatusCode);
        }

        var refused = await me.PostAsJsonAsync("/api/wardrobe/moment", new { step = "go" });
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("Slow down a little. Try again in a bit.", await ErrorAsync(refused));
        Assert.Equal(FeedbackEndpoints.PerHour, CounterOf(app, CounterName.WardrobeMomentGo));
    }
}

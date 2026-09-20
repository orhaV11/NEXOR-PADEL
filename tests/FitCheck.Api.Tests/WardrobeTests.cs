using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 14 — the wardrobe that builds itself. What these lock: a piece can only be kept from a check that named it,
/// the wardrobe is the account's alone, it can be renamed and deleted, deleting the account takes it, the export carries
/// it, a free account sees its own wardrobe while the Pro-only part is refused with the plan message in all four
/// languages, and the stylist's request carries the names only when it should.
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
        using var app = new TestApp { Settings = { ["Plans:WardrobeNamesToStylist"] = "1" } };
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
}

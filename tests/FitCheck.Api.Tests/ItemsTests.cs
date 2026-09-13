using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Items on a look, server side (Round 10): the stylist's pieces on every card, the owner tagging them (a brand, a model,
/// a store link, a dot) and adding their own, the validation, the pieces sent with the post itself, and what a deleted look
/// takes with it. The search, the brands list and the out door have their own classes below.
/// </summary>
public class ItemsTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public ItemsTests(TestApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => StylistPayload();
    }

    /// <summary>Three pieces, a swoosh seen on the shoes (rubric v3).</summary>
    public static JsonElement StylistPayload(string? shoesBrand = "Nike") => Payloads.Parse($$"""
        { "status": "ok", "score": 7, "intent_match": 70, "headline": "Seeded", "vibe": "seeded",
          "items": [
            { "name": "White tee", "category": "top", "verdict": "works", "note": "", "brand_seen": null },
            { "name": "Dark jeans", "category": "bottom", "verdict": "neutral", "note": "", "brand_seen": null },
            { "name": "Running shoes", "category": "shoes", "verdict": "weak", "note": "", "brand_seen": {{JsonSerializer.Serialize(shoesBrand)}} }
          ],
          "working": ["Seeded"], "one_tip": "Seeded." }
        """);

    public static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<string?> ErrorOf(HttpResponseMessage response) => (await Json(response)).GetProperty("error").GetString();

    public static List<JsonElement> Items(JsonElement post) => post.GetProperty("items").EnumerateArray().ToList();

    public static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    public static JsonElement ItemNamed(JsonElement post, string name) => Items(post).Single(i => i.GetProperty("name").GetString() == name);

    public static Task<HttpResponseMessage> PatchItemsAsync(HttpClient client, Guid postId, object items) =>
        client.PatchAsJsonAsync($"/api/posts/{postId}/items", new { items });

    public static async Task<List<PostItem>> RowsAsync(TestApp app, Guid postId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PostItems.Where(i => i.PostId == postId).OrderBy(i => i.Position).ToListAsync();
    }

    public static async Task HideAsync(TestApp app, Guid postId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Posts.SingleAsync(p => p.Id == postId)).Hidden = true;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Every_card_carries_the_stylists_pieces_in_order_bare_of_any_brand()
    {
        // Its own app: the feed's first page has to hold the look.
        using var app = new TestApp();
        app.Vision.Handler = _ => StylistPayload();
        var (owner, _, _) = await app.NewUserAsync("it_card_owner");
        var checkId = await app.CheckAsync(owner);
        var post = await app.PostAsync(owner, checkId);
        var postId = post.GetProperty("id").GetGuid();

        Assert.Equal(3, post.GetProperty("itemCount").GetInt32());
        var items = Items(post);
        Assert.Equal(["white tee", "dark jeans", "running shoes"], items.Select(i => i.GetProperty("name").GetString()).ToList());
        Assert.Equal(["top", "bottom", "shoes"], items.Select(i => i.GetProperty("category").GetString()).ToList());
        Assert.All(items, i => Assert.Equal("Stylist", i.GetProperty("source").GetString()));
        Assert.All(items, i => Assert.False(i.GetProperty("confirmed").GetBoolean()));
        // The brand the stylist saw stays a suggestion on the check; the card never carries it, nor a link, nor a dot.
        Assert.All(items, i => Assert.Null(Text(i, "brand")));
        Assert.All(items, i => Assert.Null(Text(i, "url")));
        Assert.All(items, i => Assert.Null(Text(i, "host")));
        Assert.All(items, i => Assert.False(i.TryGetProperty("x", out _)));
        Assert.Equal(3, items.Select(i => i.GetProperty("id").GetGuid()).Distinct().Count());
        var check = await owner.GetFromJsonAsync<JsonElement>($"/api/checks/{checkId}");
        Assert.Equal("Nike", check.GetProperty("feedback").GetProperty("items")[2].GetProperty("brandSeen").GetString());

        // The same pieces on the single post, on the feed and on the profile grid, for a stranger too.
        var single = await app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}");
        Assert.Equal(3, single.GetProperty("itemCount").GetInt32());
        Assert.Equal(items.Select(i => i.GetProperty("id").GetGuid()), Items(single).Select(i => i.GetProperty("id").GetGuid()));
        var feed = await app.NewClient().GetFromJsonAsync<JsonElement>("/api/feed?tab=fresh");
        var card = feed.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == postId);
        Assert.Equal(3, Items(card).Count);
        var grid = await app.NewClient().GetFromJsonAsync<JsonElement>("/api/users/it_card_owner/posts");
        Assert.Equal(3, grid.GetProperty("items")[0].GetProperty("itemCount").GetInt32());
    }

    [Fact]
    public async Task The_owner_tags_the_pieces_and_the_list_sent_is_the_whole_list()
    {
        var (owner, _, _) = await _app.NewUserAsync("it_tag_owner");
        var postId = await _app.CheckAndPostAsync(owner);
        var before = await owner.GetFromJsonAsync<JsonElement>($"/api/posts/{postId}");
        var tee = ItemNamed(before, "white tee").GetProperty("id").GetGuid();
        var shoes = ItemNamed(before, "running shoes").GetProperty("id").GetGuid();

        // The shoes get the brand the stylist saw, confirmed, a model, a link and a dot; the tee is kept as it is; the jeans
        // are dropped; a belt of the person's own is added with a brand and an http link.
        var response = await PatchItemsAsync(owner, postId, new object[]
        {
            new { id = shoes, brand = "Nike", confirmed = true, model = "  Air Max   90 ", url = " https://www.nike.com/t/air?color=white ", x = 0.5, y = 0.75 },
            new { id = tee },
            new { name = "  Leather   Belt ", category = "Accessory", brand = " Hermès ", url = "http://shop.example/belt" }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await Json(response)).EnumerateArray().ToList();
        Assert.Equal(["running shoes", "white tee", "leather belt"], list.Select(i => i.GetProperty("name").GetString()).ToList());

        var taggedShoes = list[0];
        Assert.Equal(shoes, taggedShoes.GetProperty("id").GetGuid());
        Assert.Equal("Stylist", taggedShoes.GetProperty("source").GetString());
        Assert.Equal("Nike", taggedShoes.GetProperty("brand").GetString());
        Assert.True(taggedShoes.GetProperty("confirmed").GetBoolean());
        Assert.Equal("Air Max 90", taggedShoes.GetProperty("model").GetString());
        Assert.Equal("https://www.nike.com/t/air?color=white", taggedShoes.GetProperty("url").GetString());
        Assert.Equal("nike.com", taggedShoes.GetProperty("host").GetString());
        Assert.Equal(0.5, taggedShoes.GetProperty("x").GetDouble());
        Assert.Equal(0.75, taggedShoes.GetProperty("y").GetDouble());
        Assert.Equal("shoes", taggedShoes.GetProperty("category").GetString());

        var keptTee = list[1];
        Assert.Equal(tee, keptTee.GetProperty("id").GetGuid());
        Assert.Equal("Stylist", keptTee.GetProperty("source").GetString());
        Assert.Equal("top", keptTee.GetProperty("category").GetString());
        Assert.Null(Text(keptTee, "brand"));

        var belt = list[2];
        Assert.Equal("User", belt.GetProperty("source").GetString());
        Assert.Equal("accessory", belt.GetProperty("category").GetString());
        Assert.Equal("Hermès", belt.GetProperty("brand").GetString());
        Assert.False(belt.GetProperty("confirmed").GetBoolean());
        Assert.Equal("shop.example", belt.GetProperty("host").GetString());
        Assert.False(belt.TryGetProperty("x", out _));

        var rows = await RowsAsync(_app, postId);
        Assert.Equal([0, 1, 2], rows.Select(r => r.Position).ToList());
        Assert.DoesNotContain(rows, r => r.Name == "dark jeans");
        Assert.Equal([shoes, tee], rows.Take(2).Select(r => r.Id).ToList());

        // The post reads the same, in the same order, and counts three.
        var after = await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}");
        Assert.Equal(3, after.GetProperty("itemCount").GetInt32());
        Assert.Equal(["running shoes", "white tee", "leather belt"], Items(after).Select(i => i.GetProperty("name").GetString()).ToList());

        // Renaming a stylist row makes it the person's; a confirmation without a brand means nothing; a brand typed on a
        // stylist row without the confirmation is not a confirmed one. Rows can be reordered; the position follows the list.
        var beltId = belt.GetProperty("id").GetGuid();
        response = await PatchItemsAsync(owner, postId, new object[]
        {
            new { id = beltId, confirmed = true },
            new { id = tee, name = "Cropped white tee", confirmed = true },
            new { id = shoes, brand = "Nike", confirmed = false }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        list = (await Json(response)).EnumerateArray().ToList();
        Assert.Equal([beltId, tee, shoes], list.Select(i => i.GetProperty("id").GetGuid()).ToList());
        Assert.False(list[0].GetProperty("confirmed").GetBoolean());
        Assert.Null(Text(list[0], "brand"));
        Assert.Equal("cropped white tee", list[1].GetProperty("name").GetString());
        Assert.Equal("User", list[1].GetProperty("source").GetString());
        Assert.False(list[1].GetProperty("confirmed").GetBoolean());
        Assert.Equal("Stylist", list[2].GetProperty("source").GetString());
        Assert.Equal("Nike", list[2].GetProperty("brand").GetString());
        Assert.False(list[2].GetProperty("confirmed").GetBoolean());
        // The link and the dot were not sent again: the list is the whole list, so they are gone.
        Assert.Null(Text(list[2], "url"));
        Assert.False(list[2].TryGetProperty("x", out _));

        // A category change makes a stylist row the person's too.
        response = await PatchItemsAsync(owner, postId, new object[] { new { id = shoes, category = "other" } });
        Assert.Equal("User", (await Json(response))[0].GetProperty("source").GetString());

        // An empty list clears the look.
        response = await PatchItemsAsync(owner, postId, Array.Empty<object>());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await Json(response)).EnumerateArray());
        Assert.Empty(await RowsAsync(_app, postId));
        Assert.Equal(0, (await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}")).GetProperty("itemCount").GetInt32());

        // Twelve rows fit; a thirteenth does not, and the twelve stay.
        var twelve = Enumerable.Range(1, 12).Select(i => new { name = $"piece {i}", category = "other" }).ToArray();
        Assert.Equal(HttpStatusCode.OK, (await PatchItemsAsync(owner, postId, twelve)).StatusCode);
        var thirteen = Enumerable.Range(1, 13).Select(i => new { name = $"piece {i}", category = "other" }).ToArray();
        var tooMany = await PatchItemsAsync(owner, postId, thirteen);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal("Up to 12 items on a look.", await ErrorOf(tooMany));
        Assert.Equal(12, (await RowsAsync(_app, postId)).Count);
    }

    public static TheoryData<string, string, string> InvalidItems => new()
    {
        { "a typed name longer than 40", $$"""{ "name": "{{new string('a', 41)}}", "category": "top" }""", "An item needs a name up to 40 characters; a brand up to 40, a model up to 60." },
        { "no name on a new row", """{ "category": "top" }""", "An item needs a name up to 40 characters; a brand up to 40, a model up to 60." },
        { "a blank name", """{ "name": "   \t ", "category": "top" }""", "An item needs a name up to 40 characters; a brand up to 40, a model up to 60." },
        { "a category outside the set", """{ "name": "beret", "category": "hat" }""", "An item needs a name up to 40 characters; a brand up to 40, a model up to 60." },
        { "a brand longer than 40", $$"""{ "name": "beret", "category": "accessory", "brand": "{{new string('b', 41)}}" }""", "An item needs a name up to 40 characters; a brand up to 40, a model up to 60." },
        { "a model longer than 60", $$"""{ "name": "beret", "category": "accessory", "model": "{{new string('m', 61)}}" }""", "An item needs a name up to 40 characters; a brand up to 40, a model up to 60." },
        { "an id that is not on the look", """{ "id": "11111111-1111-1111-1111-111111111111", "brand": "Nike" }""", "An item needs a name up to 40 characters; a brand up to 40, a model up to 60." },
        { "a javascript link", """{ "name": "beret", "category": "accessory", "url": "javascript:alert(1)" }""", "A store link has to start with http:// or https://." },
        { "a data link", """{ "name": "beret", "category": "accessory", "url": "data:text/html;base64,AAAA" }""", "A store link has to start with http:// or https://." },
        { "a file link", """{ "name": "beret", "category": "accessory", "url": "file:///etc/passwd" }""", "A store link has to start with http:// or https://." },
        { "an ftp link", """{ "name": "beret", "category": "accessory", "url": "ftp://files.example/x" }""", "A store link has to start with http:// or https://." },
        { "a relative link", """{ "name": "beret", "category": "accessory", "url": "/shop/beret" }""", "A store link has to start with http:// or https://." },
        { "a link with user info", """{ "name": "beret", "category": "accessory", "url": "https://nike.com@evil.example/x" }""", "A store link has to start with http:// or https://." },
        { "a link longer than 500", $$"""{ "name": "beret", "category": "accessory", "url": "https://shop.example/{{new string('p', 490)}}" }""", "A store link has to start with http:// or https://." },
        { "one coordinate only", """{ "name": "beret", "category": "accessory", "x": 0.5 }""", "The dot has to be on the photo." },
        { "x past the edge", """{ "name": "beret", "category": "accessory", "x": 1.2, "y": 0.5 }""", "The dot has to be on the photo." },
        { "y before the edge", """{ "name": "beret", "category": "accessory", "x": 0.5, "y": -0.1 }""", "The dot has to be on the photo." },
    };

    private static int _invalidCase;

    [Theory]
    [MemberData(nameof(InvalidItems))]
    public async Task An_invalid_item_is_refused_with_its_reason_and_nothing_changes(string label, string item, string expected)
    {
        Assert.NotEmpty(label);
        var (owner, _, _) = await _app.NewUserAsync("it_invalid_" + Interlocked.Increment(ref _invalidCase));
        var postId = await _app.CheckAndPostAsync(owner);
        var content = new StringContent($$"""{ "items": [{{item}}] }""", Encoding.UTF8, "application/json");

        var response = await owner.PatchAsync($"/api/posts/{postId}/items", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, await ErrorOf(response));
        var rows = await RowsAsync(_app, postId);
        Assert.Equal(["white tee", "dark jeans", "running shoes"], rows.Select(r => r.Name).ToList());
        Assert.All(rows, r => Assert.Null(r.Brand));
    }

    [Fact]
    public async Task A_duplicate_id_and_a_missing_body_are_refused_and_the_errors_speak_the_owners_language()
    {
        var (owner, _, _) = await _app.NewUserAsync("it_dup_owner", language: "he");
        var postId = await _app.CheckAndPostAsync(owner);
        var rows = await RowsAsync(_app, postId);
        var tee = rows[0].Id;

        var duplicate = await PatchItemsAsync(owner, postId, new object[] { new { id = tee }, new { id = tee, brand = "Nike" } });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal("לפריט צריך שם עד 40 תווים; מותג עד 40, דגם עד 60.", await ErrorOf(duplicate));

        var empty = await owner.PatchAsync($"/api/posts/{postId}/items", new StringContent("", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        // A body without the list, and a null list, are broken calls too: only an explicit [] clears the look.
        foreach (var body in new[] { "{}", """{ "items": null }""" })
        {
            var noList = await owner.PatchAsync($"/api/posts/{postId}/items", new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, noList.StatusCode);
            Assert.Equal("לפריט צריך שם עד 40 תווים; מותג עד 40, דגם עד 60.", await ErrorOf(noList));
            Assert.Equal(3, (await RowsAsync(_app, postId)).Count);
        }

        // A stylist name longer than 40 sent back as it is stays; so does the stylist's own name uncut (the check carries
        // it whole) and the stored name cut to forty (what a client that holds every name to the typed limit sends); a
        // different name that long does not.
        var longName = "A " + new string('x', 70);
        _app.Vision.Handler = _ => Payloads.Parse($$"""
            { "status": "ok", "score": 7, "intent_match": 70, "headline": "Seeded", "vibe": "seeded",
              "items": [ { "name": "{{longName}}", "category": "outerwear", "verdict": "works", "note": "" } ],
              "working": ["Seeded"], "one_tip": "Seeded." }
            """);
        var longPostId = await _app.CheckAndPostAsync(owner);
        var longRow = Assert.Single(await RowsAsync(_app, longPostId));
        Assert.Equal(60, longRow.Name.Length);
        foreach (var sentBack in new[] { longRow.Name, longName, longRow.Name[..40], longRow.Name[..40].ToUpperInvariant() + "  " })
        {
            var kept = await PatchItemsAsync(owner, longPostId, new object[] { new { id = longRow.Id, name = sentBack, brand = "Acne", confirmed = true } });
            Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
            var row = (await Json(kept))[0];
            Assert.Equal(longRow.Name, row.GetProperty("name").GetString());
            Assert.Equal("Stylist", row.GetProperty("source").GetString());
            Assert.True(row.GetProperty("confirmed").GetBoolean());
        }

        var renamed = await PatchItemsAsync(owner, longPostId, new object[] { new { id = longRow.Id, name = "B " + new string('x', 70) } });
        Assert.Equal(HttpStatusCode.BadRequest, renamed.StatusCode);
        var shortened = await PatchItemsAsync(owner, longPostId, new object[] { new { id = longRow.Id, name = longRow.Name[..39] } });
        Assert.Equal("User", (await Json(shortened))[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task At_posting_a_long_stylist_name_sent_whole_or_cut_to_forty_names_the_stylists_row()
    {
        var (owner, _, _) = await _app.NewUserAsync("it_long_owner");
        var longName = "Light-wash straight-leg jeans with a raw hem and a high rise";   // 60 as the stylist says it
        _app.Vision.Handler = _ => Payloads.Parse($$"""
            { "status": "ok", "score": 7, "intent_match": 70, "headline": "Seeded", "vibe": "seeded",
              "items": [ { "name": "{{longName}}", "category": "bottom", "verdict": "works", "note": "", "brand_seen": "Levi's" } ],
              "working": ["Seeded"], "one_tip": "Seeded." }
            """);
        var stored = PostItems.NormalizeName(longName);
        Assert.Equal(60, stored.Length);

        foreach (var sent in new[] { longName, longName[..40] })
        {
            var checkId = await _app.CheckAsync(owner);
            var response = await owner.PostAsJsonAsync("/api/posts", new { checkId, items = new object[] { new { name = sent, brand = "Levi's", confirmed = true } } });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var item = Assert.Single(Items(await Json(response)));
            Assert.Equal(stored, item.GetProperty("name").GetString());
            Assert.Equal("Stylist", item.GetProperty("source").GetString());
            Assert.Equal("Levi's", item.GetProperty("brand").GetString());
            Assert.True(item.GetProperty("confirmed").GetBoolean());
        }
    }

    [Fact]
    public async Task Only_the_owner_tags_and_a_hidden_look_answers_like_a_missing_one()
    {
        var (owner, _, _) = await _app.NewUserAsync("it_only_owner");
        var (other, _, _) = await _app.NewUserAsync("it_only_other");
        var postId = await _app.CheckAndPostAsync(owner);
        var rows = await RowsAsync(_app, postId);
        var body = new object[] { new { id = rows[2].Id, brand = "Nike", confirmed = true } };

        var stranger = await PatchItemsAsync(other, postId, body);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal("We couldn't find this post.", await ErrorOf(stranger));
        Assert.Equal(HttpStatusCode.Unauthorized, (await PatchItemsAsync(_app.NewClient(), postId, body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PatchItemsAsync(owner, Guid.NewGuid(), body)).StatusCode);
        Assert.All(await RowsAsync(_app, postId), r => Assert.Null(r.Brand));

        Assert.Equal(HttpStatusCode.OK, (await PatchItemsAsync(owner, postId, body)).StatusCode);
        await HideAsync(_app, postId);
        Assert.Equal(HttpStatusCode.NotFound, (await PatchItemsAsync(owner, postId, body)).StatusCode);
    }

    [Fact]
    public async Task The_post_sheet_sends_the_pieces_with_the_post_and_the_one_post_per_check_rule_stands()
    {
        var (owner, _, _) = await _app.NewUserAsync("it_sheet_owner");
        var checkId = await _app.CheckAsync(owner);

        // The stylist's shoes with their brand confirmed and a link, the tee kept by name, the jeans dropped, hoops added.
        var items = new object[]
        {
            new { name = "Running shoes", category = "shoes", brand = "Nike", confirmed = true, url = "https://nike.example/air" },
            new { name = "white tee" },
            new { name = "Gold hoops", category = "accessory" }
        };
        var response = await owner.PostAsJsonAsync("/api/posts", new { checkId, caption = "tagged at once", items });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = await Json(response);
        var postId = post.GetProperty("id").GetGuid();
        Assert.Equal(3, post.GetProperty("itemCount").GetInt32());
        var list = Items(post);
        Assert.Equal(["running shoes", "white tee", "gold hoops"], list.Select(i => i.GetProperty("name").GetString()).ToList());
        Assert.Equal(["Stylist", "Stylist", "User"], list.Select(i => i.GetProperty("source").GetString()).ToList());
        Assert.Equal("Nike", list[0].GetProperty("brand").GetString());
        Assert.True(list[0].GetProperty("confirmed").GetBoolean());
        Assert.Equal("nike.example", list[0].GetProperty("host").GetString());
        Assert.Equal("top", list[1].GetProperty("category").GetString());
        Assert.Equal("accessory", list[2].GetProperty("category").GetString());

        // One post per check, with or without items; the first post's pieces are untouched.
        var again = await owner.PostAsJsonAsync("/api/posts", new { checkId, items });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("This check is already posted.", await ErrorOf(again));
        Assert.Equal(3, (await RowsAsync(_app, postId)).Count);

        // An invalid list refuses the post itself and writes nothing, so the check can be posted afterwards, plain.
        var otherCheckId = await _app.CheckAsync(owner);
        var refused = await owner.PostAsJsonAsync("/api/posts", new
        {
            checkId = otherCheckId,
            items = new object[] { new { name = "Running shoes", url = "javascript:alert(1)" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("A store link has to start with http:// or https://.", await ErrorOf(refused));
        var tooMany = await owner.PostAsJsonAsync("/api/posts", new
        {
            checkId = otherCheckId,
            items = Enumerable.Range(1, 13).Select(i => new { name = $"piece {i}" }).ToArray()
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal("Up to 12 items on a look.", await ErrorOf(tooMany));
        var check = await owner.GetFromJsonAsync<JsonElement>($"/api/checks/{otherCheckId}");
        Assert.True(!check.TryGetProperty("postId", out var pid) || pid.ValueKind == JsonValueKind.Null);
        var plain = await _app.PostAsync(owner, otherCheckId);
        Assert.Equal(3, plain.GetProperty("itemCount").GetInt32());
        Assert.All(Items(plain), i => Assert.Null(Text(i, "brand")));

        // An empty list posts a look with no pieces at all.
        var bareCheckId = await _app.CheckAsync(owner);
        var bare = await owner.PostAsJsonAsync("/api/posts", new { checkId = bareCheckId, items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Created, bare.StatusCode);
        Assert.Equal(0, (await Json(bare)).GetProperty("itemCount").GetInt32());
    }

    [Fact]
    public async Task Deleting_the_look_takes_its_pieces_with_it()
    {
        var (owner, _, _) = await _app.NewUserAsync("it_delete_owner");
        var postId = await _app.CheckAndPostAsync(owner);
        var rows = await RowsAsync(_app, postId);
        Assert.Equal(HttpStatusCode.OK, (await PatchItemsAsync(owner, postId, new object[] { new { id = rows[2].Id, brand = "Zorbex", url = "https://zorbex.example/x" } })).StatusCode);
        Assert.Single((await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/items?brand=zorbex")).GetProperty("posts").EnumerateArray());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/posts/{postId}")).StatusCode);
        Assert.Empty(await RowsAsync(_app, postId));
        Assert.Empty((await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/items?brand=zorbex")).GetProperty("posts").EnumerateArray());
        var client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/items/{rows[2].Id}/out")).StatusCode);
    }

    [Fact]
    public async Task The_metrics_count_tagged_pieces_and_outs()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => StylistPayload();
        var (owner, _, _) = await app.NewUserAsync("it_metrics_owner");
        var (moderator, _, _) = await app.NewUserAsync("it_metrics_mod");
        await app.PromoteAsync("it_metrics_mod");
        var postId = await app.CheckAndPostAsync(owner);

        // The stylist's bare names are not tagging.
        var social = (await moderator.GetFromJsonAsync<JsonElement>("/api/metrics/pilot")).GetProperty("social");
        Assert.Equal(0, social.GetProperty("itemsTagged").GetInt32());
        Assert.Equal(0, social.GetProperty("itemOuts").GetInt32());

        var rows = await RowsAsync(app, postId);
        Assert.Equal(HttpStatusCode.OK, (await PatchItemsAsync(owner, postId, new object[]
        {
            new { id = rows[0].Id },
            new { id = rows[1].Id, brand = "Levi's" },
            new { id = rows[2].Id, url = "https://shop.example/shoes" },
            new { name = "beret", category = "accessory" }
        })).StatusCode);
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/api/items/{rows[2].Id}/out")).StatusCode);
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/api/items/{rows[2].Id}/out")).StatusCode);

        social = (await moderator.GetFromJsonAsync<JsonElement>("/api/metrics/pilot")).GetProperty("social");
        Assert.Equal(3, social.GetProperty("itemsTagged").GetInt32());
        Assert.Equal(2, social.GetProperty("itemOuts").GetInt32());
    }
}

/// <summary>GET /api/items: looks by brand, category and a free term, visible ones only, newest first, paged.</summary>
public class ItemSearchTests
{
    private static List<Guid> Ids(JsonElement dto) => dto.GetProperty("posts").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();

    /// <summary>A look whose stylist rows are then tagged as given: (name, brand, model) per row that gets one.</summary>
    private static async Task<Guid> TaggedLookAsync(TestApp app, HttpClient owner, params (string Name, string? Brand, string? Model)[] tags)
    {
        var postId = await app.CheckAndPostAsync(owner);
        var rows = await ItemsTests.RowsAsync(app, postId);
        var items = rows.Select(r =>
        {
            var tag = tags.FirstOrDefault(t => t.Name == r.Name);
            return new { id = r.Id, brand = tag.Brand, model = tag.Model };
        }).ToArray();
        var response = await ItemsTests.PatchItemsAsync(owner, postId, items);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return postId;
    }

    [Fact]
    public async Task Looks_are_found_by_brand_category_and_term_on_one_row_visible_ones_only_newest_first()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => ItemsTests.StylistPayload();
        var (owner, _, _) = await app.NewUserAsync("is_owner");
        var (banned, bannedId, _) = await app.NewUserAsync("is_banned");
        var anyone = app.NewClient();

        var l1 = await TaggedLookAsync(app, owner, ("running shoes", "Nike", "Air Max 90"), ("dark jeans", "Levi's", "501"));
        var l2 = await TaggedLookAsync(app, owner, ("running shoes", "nike", null));
        var l3 = await TaggedLookAsync(app, owner, ("white tee", "Nike", null));
        var hidden = await TaggedLookAsync(app, owner, ("running shoes", "Nike", "Pegasus"));
        await ItemsTests.HideAsync(app, hidden);
        var bannedLook = await TaggedLookAsync(app, banned, ("running shoes", "Nike", null));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.SingleAsync(u => u.Id == bannedId)).Suspended = true;
            await db.SaveChangesAsync();
        }

        // By brand, in any case; the brand comes back in the spelling most looks carry.
        var byBrand = await anyone.GetFromJsonAsync<JsonElement>("/api/items?brand=NIKE");
        Assert.Equal([l3, l2, l1], Ids(byBrand));
        Assert.Equal("Nike", byBrand.GetProperty("brand").GetString());
        Assert.DoesNotContain(hidden, Ids(byBrand));
        Assert.DoesNotContain(bannedLook, Ids(byBrand));
        Assert.True(!byBrand.TryGetProperty("nextOffset", out var next) || next.ValueKind == JsonValueKind.Null);
        Assert.Equal(3, byBrand.GetProperty("posts")[0].GetProperty("itemCount").GetInt32());

        // Brand and category on the same row: the Nike shoes, not a look with a Nike tee and unbranded jeans.
        Assert.Equal([l2, l1], Ids(await anyone.GetFromJsonAsync<JsonElement>("/api/items?brand=nike&category=shoes")));
        Assert.Equal([l3], Ids(await anyone.GetFromJsonAsync<JsonElement>("/api/items?brand=nike&category=Top")));
        Assert.Empty(Ids(await anyone.GetFromJsonAsync<JsonElement>("/api/items?brand=nike&category=bottom")));
        Assert.Equal([l1], Ids(await anyone.GetFromJsonAsync<JsonElement>("/api/items?brand=levi%27s&category=bottom")));

        // By category alone, and by a term anywhere in a name or a model, in any case.
        Assert.Equal([l3, l2, l1], Ids(await anyone.GetFromJsonAsync<JsonElement>("/api/items?category=bottom")));
        Assert.Equal([l1], Ids(await anyone.GetFromJsonAsync<JsonElement>("/api/items?q=air%20max")));
        Assert.Equal([l3, l2, l1], Ids(await anyone.GetFromJsonAsync<JsonElement>("/api/items?q=JEANS")));
        Assert.Equal([l2, l1], Ids(await anyone.GetFromJsonAsync<JsonElement>("/api/items?q=shoes&brand=nike")));
        // The brand is a piece of the item too: the search box promises pieces, brands and models.
        Assert.Equal([l3, l2, l1], Ids(await anyone.GetFromJsonAsync<JsonElement>("/api/items?q=nike")));
        Assert.Equal([l1], Ids(await anyone.GetFromJsonAsync<JsonElement>("/api/items?q=levi%27s")));
        Assert.Equal([l1], Ids(await anyone.GetFromJsonAsync<JsonElement>("/api/items?q=LEVI&category=bottom")));
        Assert.Empty(Ids(await anyone.GetFromJsonAsync<JsonElement>("/api/items?q=%25")));
        Assert.Empty(Ids(await anyone.GetFromJsonAsync<JsonElement>("/api/items?q=sandals")));

        // Nothing asked or a category nobody stores is an empty page; a term too long is refused like the search's.
        var nothing = await anyone.GetFromJsonAsync<JsonElement>("/api/items");
        Assert.Empty(Ids(nothing));
        Assert.Empty(Ids(await anyone.GetFromJsonAsync<JsonElement>("/api/items?category=hat")));
        Assert.Equal(HttpStatusCode.BadRequest, (await anyone.GetAsync("/api/items?q=" + new string('a', 41))).StatusCode);

        // Paged like a feed.
        var first = await anyone.GetFromJsonAsync<JsonElement>("/api/items?category=bottom&limit=2");
        Assert.Equal([l3, l2], Ids(first));
        Assert.Equal(2, first.GetProperty("nextOffset").GetInt32());
        var second = await anyone.GetFromJsonAsync<JsonElement>("/api/items?category=bottom&limit=2&offset=2");
        Assert.Equal([l1], Ids(second));
        Assert.True(!second.TryGetProperty("nextOffset", out var end) || end.ValueKind == JsonValueKind.Null);

        // Signed in, the viewer's own state rides along as on every list of looks.
        await owner.PostAsync($"/api/posts/{l3}/fire", null);
        var mine = await owner.GetFromJsonAsync<JsonElement>("/api/items?brand=nike");
        Assert.True(mine.GetProperty("posts")[0].GetProperty("fired").GetBoolean());
        Assert.True(mine.GetProperty("posts")[0].GetProperty("isMine").GetBoolean());
    }
}

/// <summary>GET /api/items/brands: the autocomplete's brands, tagged ones with their look counts and brand accounts.</summary>
public class ItemBrandsTests
{
    private static List<JsonElement> Brands(JsonElement dto) => dto.GetProperty("items").EnumerateArray().ToList();

    private static JsonElement Named(JsonElement dto, string name) => Brands(dto).Single(b => b.GetProperty("name").GetString() == name);

    [Fact]
    public async Task Brands_are_the_tagged_ones_merged_across_case_with_look_counts_and_the_brand_accounts_that_match()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => ItemsTests.StylistPayload();
        var (owner, _, _) = await app.NewUserAsync("ib_owner");
        var (_, nexorId, _) = await app.NewUserAsync("nexor", accountType: "Brand", displayName: "NEXOR");
        await app.NewUserAsync("nikeofficial", accountType: "Brand", displayName: "Nike");
        var (_, goneId, _) = await app.NewUserAsync("oldbrand", accountType: "Brand", displayName: "Old Brand");
        await app.NewUserAsync("hermes_fan", accountType: "Person", displayName: "Hermès");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.SingleAsync(u => u.Id == goneId)).Suspended = true;
            (await db.Users.SingleAsync(u => u.Id == nexorId)).Verified = true;
            await db.SaveChangesAsync();
        }

        async Task<Guid> Tag(string brandOnShoes, string? brandOnJeans = null)
        {
            var postId = await app.CheckAndPostAsync(owner);
            var rows = await ItemsTests.RowsAsync(app, postId);
            var response = await ItemsTests.PatchItemsAsync(owner, postId, new object[]
            {
                new { id = rows[1].Id, brand = brandOnJeans },
                new { id = rows[2].Id, brand = brandOnShoes }
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return postId;
        }

        await Tag("Nike");
        await Tag("nike", "Nike");
        await Tag("Hermès");
        var hidden = await Tag("Zara");
        await ItemsTests.HideAsync(app, hidden);

        var anyone = app.NewClient();
        var all = await anyone.GetFromJsonAsync<JsonElement>("/api/items/brands");
        var names = Brands(all).Select(b => b.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Nike", "Hermès", "NEXOR"], names);
        // "Nike" twice and "nike" once are one brand on two looks (the second look carries it on two rows), and the brand
        // account of that name rides on it.
        var nike = Named(all, "Nike");
        Assert.Equal(2, nike.GetProperty("looks").GetInt32());
        Assert.Equal("nikeofficial", nike.GetProperty("account").GetProperty("handle").GetString());
        Assert.Equal("Brand", nike.GetProperty("account").GetProperty("accountType").GetString());
        Assert.Equal(1, Named(all, "Hermès").GetProperty("looks").GetInt32());
        Assert.False(Named(all, "Hermès").TryGetProperty("account", out _));
        // An account nobody tagged yet is offered with no looks; a suspended one is nobody's brand; a person is not a brand.
        var nexor = Named(all, "NEXOR");
        Assert.Equal(0, nexor.GetProperty("looks").GetInt32());
        Assert.Equal("nexor", nexor.GetProperty("account").GetProperty("handle").GetString());
        Assert.True(nexor.GetProperty("account").GetProperty("verified").GetBoolean());

        // A prefix, in any case, on the tagged name, the handle or the display name.
        Assert.Equal(["Nike"], Brands(await anyone.GetFromJsonAsync<JsonElement>("/api/items/brands?q=NI")).Select(b => b.GetProperty("name").GetString()));
        Assert.Equal(["NEXOR"], Brands(await anyone.GetFromJsonAsync<JsonElement>("/api/items/brands?q=nex")).Select(b => b.GetProperty("name").GetString()));
        Assert.Equal(["Hermès"], Brands(await anyone.GetFromJsonAsync<JsonElement>("/api/items/brands?q=herm")).Select(b => b.GetProperty("name").GetString()));
        Assert.Empty(Brands(await anyone.GetFromJsonAsync<JsonElement>("/api/items/brands?q=zara")));
        Assert.Empty(Brands(await anyone.GetFromJsonAsync<JsonElement>("/api/items/brands?q=old")));
        Assert.Equal(HttpStatusCode.BadRequest, (await anyone.GetAsync("/api/items/brands?q=" + new string('a', 41))).StatusCode);

        // At most twenty, the most tagged first.
        for (var look = 0; look < 3; look++)
        {
            var postId = await app.CheckAndPostAsync(owner);
            var items = Enumerable.Range(1, 8).Select(i => new { name = $"piece {i}", category = "other", brand = $"Brand{look}{i}" }).ToArray();
            Assert.Equal(HttpStatusCode.OK, (await ItemsTests.PatchItemsAsync(owner, postId, items)).StatusCode);
        }

        var capped = await anyone.GetFromJsonAsync<JsonElement>("/api/items/brands");
        Assert.Equal(20, Brands(capped).Count);
        Assert.Equal("Nike", Brands(capped)[0].GetProperty("name").GetString());
    }
}

/// <summary>GET /api/items/{id}/out: the one door a store link leaves through.</summary>
public class ItemOutTests
{
    private static HttpClient NoRedirect(TestApp app) => app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<(Guid PostId, List<PostItem> Rows)> LinkedLookAsync(TestApp app, HttpClient owner, string shoesUrl, string? jeansUrl = null)
    {
        var postId = await app.CheckAndPostAsync(owner);
        var rows = await ItemsTests.RowsAsync(app, postId);
        var response = await ItemsTests.PatchItemsAsync(owner, postId, new object[]
        {
            new { id = rows[0].Id },
            new { id = rows[1].Id, url = jeansUrl },
            new { id = rows[2].Id, url = shoesUrl, brand = "Nike" }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (postId, await ItemsTests.RowsAsync(app, postId));
    }

    [Fact]
    public async Task The_link_is_followed_as_stored_without_a_referrer_or_a_cache_and_counted()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => ItemsTests.StylistPayload();
        var (owner, _, _) = await app.NewUserAsync("io_owner");
        var (moderator, _, _) = await app.NewUserAsync("io_mod");
        await app.PromoteAsync("io_mod");
        var (postId, rows) = await LinkedLookAsync(app, owner, "https://shop.example/p/air?color=red&size=42#reviews");
        var client = NoRedirect(app);

        var response = await client.GetAsync($"/api/items/{rows[2].Id}/out");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("https://shop.example/p/air?color=red&size=42#reviews", response.Headers.Location?.OriginalString);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.True(response.Headers.CacheControl?.NoStore, response.Headers.CacheControl?.ToString());

        // A missing item, one without a link, and one on a hidden look answer alike.
        var missing = await client.GetAsync($"/api/items/{Guid.NewGuid()}/out");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("We couldn't find this item.", await ItemsTests.ErrorOf(missing));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/items/{rows[0].Id}/out")).StatusCode);
        var hebrew = NoRedirect(app);
        hebrew.DefaultRequestHeaders.AcceptLanguage.ParseAdd("he-IL");
        Assert.Equal("לא מצאנו את הפריט הזה.", await ItemsTests.ErrorOf(await hebrew.GetAsync($"/api/items/{rows[0].Id}/out")));
        await ItemsTests.HideAsync(app, postId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/items/{rows[2].Id}/out")).StatusCode);

        // Only the taps that left were counted.
        var social = (await moderator.GetFromJsonAsync<JsonElement>("/api/metrics/pilot")).GetProperty("social");
        Assert.Equal(1, social.GetProperty("itemOuts").GetInt32());
    }

    [Fact]
    public async Task An_affiliate_host_gets_its_parameters_after_the_links_own_query_and_before_the_fragment()
    {
        using var app = new TestApp { Settings = { ["Affiliate:Hosts:example.com"] = "tag=orevosh-20" } };
        app.Vision.Handler = _ => ItemsTests.StylistPayload();
        var (owner, _, _) = await app.NewUserAsync("ia_owner");
        var client = NoRedirect(app);

        async Task<string?> Out(string url)
        {
            var (_, rows) = await LinkedLookAsync(app, owner, url);
            var response = await client.GetAsync($"/api/items/{rows[2].Id}/out");
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            return response.Headers.Location?.OriginalString;
        }

        Assert.Equal("https://www.example.com/p?color=red&tag=orevosh-20#top", await Out("https://www.example.com/p?color=red#top"));
        Assert.Equal("https://shop.example.com/p?tag=orevosh-20", await Out("https://shop.example.com/p"));
        Assert.Equal("http://EXAMPLE.com/p?tag=orevosh-20", await Out("http://EXAMPLE.com/p"));
        Assert.Equal("https://example.com/p?tag=orevosh-20#frag", await Out("https://example.com/p#frag"));
        // A host that merely ends in the name, and every host not listed, leave as given.
        Assert.Equal("https://notexample.com/p", await Out("https://notexample.com/p"));
        Assert.Equal("https://shop.other/p?x=1", await Out("https://shop.other/p?x=1"));
    }

    [Fact]
    public async Task A_link_pasted_with_hebrew_an_accent_or_a_host_in_its_own_script_leaves_in_its_ascii_form()
    {
        using var app = new TestApp { Settings = { ["Affiliate:Hosts:terminalx.com"] = "aff=orevosh" } };
        app.Vision.Handler = _ => ItemsTests.StylistPayload();
        var (owner, _, _) = await app.NewUserAsync("iu_owner");
        var (moderator, _, _) = await app.NewUserAsync("iu_mod");
        await app.PromoteAsync("iu_mod");
        var client = NoRedirect(app);

        // Pasted from a chat, where links show decoded: a Hebrew query, an accented path, a host in Hebrew. Each is
        // accepted and stored as pasted, the sheet shows its readable host, and the door sends what a header can carry:
        // punycode for the host, percent-encoding for the rest, the affiliate parameters after the encoded query.
        var links = new[]
        {
            ("https://www.terminalx.com/search?q=נעליים", "terminalx.com", "https://www.terminalx.com/search?q=%D7%A0%D7%A2%D7%9C%D7%99%D7%99%D7%9D&aff=orevosh"),
            ("https://shop.example/été#top", "shop.example", "https://shop.example/%C3%A9t%C3%A9#top"),
            ("https://חנות.co.il/x?a=1", "חנות.co.il", "https://xn--9dbd1a4b.co.il/x?a=1"),
            ("HTTP://Shop.Example:8080/été", "shop.example", "http://shop.example:8080/%C3%A9t%C3%A9")
        };
        foreach (var (pasted, host, ascii) in links)
        {
            var (postId, rows) = await LinkedLookAsync(app, owner, pasted);
            var dto = ItemsTests.ItemNamed(await app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}"), "running shoes");
            Assert.Equal(pasted, dto.GetProperty("url").GetString());
            Assert.Equal(host, dto.GetProperty("host").GetString());

            var response = await client.GetAsync($"/api/items/{rows[2].Id}/out");
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            var location = response.Headers.Location?.OriginalString;
            Assert.Equal(ascii, location);
            // TestServer sends any header; Kestrel refuses one outside printable ASCII, so the form itself is the test.
            Assert.All(location!, c => Assert.InRange(c, ' ', '~'));
            Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        }

        var social = (await moderator.GetFromJsonAsync<JsonElement>("/api/metrics/pilot")).GetProperty("social");
        Assert.Equal(links.Length, social.GetProperty("itemOuts").GetInt32());
    }

    [Fact]
    public async Task The_config_says_whether_the_commission_line_shows()
    {
        using var on = new TestApp();
        Assert.True((await on.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("affiliate").GetProperty("disclosure").GetBoolean());
        using var off = new TestApp { Settings = { ["Affiliate:Disclosure"] = "false" } };
        var affiliate = (await off.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("affiliate");
        Assert.False(affiliate.GetProperty("disclosure").GetBoolean());
        // The hosts and their parameters are the server's business, never published.
        Assert.Single(affiliate.EnumerateObject());
    }

    [Fact]
    public async Task Sixty_outs_a_minute_per_address_then_a_429()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => ItemsTests.StylistPayload();
        var (owner, _, _) = await app.NewUserAsync("ir_owner");
        var (_, rows) = await LinkedLookAsync(app, owner, "https://shop.example/p");
        var client = NoRedirect(app);

        for (var i = 1; i <= 60; i++)
        {
            Assert.Equal(HttpStatusCode.Found, (await client.GetAsync($"/api/items/{rows[2].Id}/out")).StatusCode);
        }

        var refused = await client.GetAsync($"/api/items/{rows[2].Id}/out");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("Slow down a little. Try again in a bit.", await ItemsTests.ErrorOf(refused));
        Assert.True(refused.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero && delta <= TimeSpan.FromMinutes(1));
        // The brake is the door's alone: the rest of the app answers.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/items?brand=nike")).StatusCode);
    }
}

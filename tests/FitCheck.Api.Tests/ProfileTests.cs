using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;

namespace FitCheck.Api.Tests;

/// <summary>Avatars, account type and interests, the community and featured tabs, and what account deletion takes with it.</summary>
public class ProfileTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public ProfileTests(TestApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>The version in a versioned avatar URL. Versions are wall-clock based, so tests compare them, never pin them.</summary>
    private static int Version(string url) => int.Parse(url[(url.IndexOf("v=", StringComparison.Ordinal) + 2)..]);

    private static bool IsNull(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    private static List<Guid> Ids(JsonElement feed) =>
        feed.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();

    private static string[] Interests(JsonElement me) =>
        me.GetProperty("interests").EnumerateArray().Select(i => i.GetString()!).ToArray();

    private static MultipartFormDataContent AvatarForm(byte[] image, string fileName = "me.jpg")
    {
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "image", fileName } };
    }

    private string UserFolder(Guid userId) => Path.Combine(_app.StorageRoot, userId.ToString("N"));

    // The caption parser and the feature endpoint belong to other changes; mentions, tags, feature marks and the
    // notifications they would send are written straight to the rows here.
    private void WithDb(Action<AppDbContext> action)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        action(db);
        db.SaveChanges();
    }

    private void Mention(Guid postId, Guid userId) => WithDb(db => db.PostMentions.Add(new PostMention { PostId = postId, UserId = userId }));

    private void Tag(Guid postId, string tag) => WithDb(db => db.PostTags.Add(new PostTag { PostId = postId, Tag = tag }));

    private void Feature(Guid postId, Guid brandId, DateTime at) => WithDb(db =>
    {
        var post = db.Posts.Find(postId)!;
        post.FeaturedByBrandId = brandId;
        post.FeaturedAt = at;
    });

    private void Hide(Guid postId) => WithDb(db => db.Posts.Find(postId)!.Hidden = true);

    private void Notify(Guid userId, string type, string actorHandle, Guid postId) => WithDb(db => db.Notifications.Add(new Notification
    {
        Id = Guid.NewGuid(), UserId = userId, Type = type, ActorHandle = actorHandle, PostId = postId, CreatedAt = DateTime.UtcNow
    }));

    [Fact]
    public async Task Avatar_upload_serves_a_cacheable_versioned_photo_and_delete_removes_it()
    {
        var (client, id, _) = await _app.NewUserAsync("av_one", displayName: "Av One");
        var anonymous = _app.NewClient();

        // Nothing yet: no URL on me, 404 on the route.
        Assert.True(IsNull(await client.GetFromJsonAsync<JsonElement>("/api/auth/me"), "avatarUrl"));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/users/av_one/avatar")).StatusCode);

        var upload = await client.PostAsync("/api/users/me/avatar", AvatarForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var me = await Json(upload);
        Assert.Equal("av_one", me.GetProperty("handle").GetString());
        Assert.Equal("Av One", me.GetProperty("name").GetString());
        var url = me.GetProperty("avatarUrl").GetString()!;
        Assert.Matches(@"^/api/users/av_one/avatar\?v=\d+$", url);
        var v1 = Version(url);

        var image = await anonymous.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=86400", image.Headers.CacheControl?.ToString());
        Assert.Equal(TestImages.Jpeg(), await image.Content.ReadAsByteArrayAsync());
        // The file sits beside the person's checks, under the private root, never under wwwroot.
        Assert.True(File.Exists(Path.Combine(UserFolder(id), "avatar.jpg")));

        // Every user ref carries the URL: me, the profile, and the cards in feeds and on a post.
        Assert.Equal(url, (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("avatarUrl").GetString());
        Assert.Equal(url, (await anonymous.GetFromJsonAsync<JsonElement>("/api/users/av_one")).GetProperty("avatarUrl").GetString());
        var postId = await _app.CheckAndPostAsync(client);
        var feed = await anonymous.GetFromJsonAsync<JsonElement>("/api/feed?limit=30");
        var card = feed.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == postId);
        Assert.Equal(url, card.GetProperty("user").GetProperty("avatarUrl").GetString());
        var view = await anonymous.GetFromJsonAsync<JsonElement>($"/api/posts/{postId}");
        Assert.Equal(url, view.GetProperty("user").GetProperty("avatarUrl").GetString());

        // A new photo gets a new version whatever its format, and the old file is replaced, not kept beside it.
        var png = await Json(await client.PostAsync("/api/users/me/avatar", AvatarForm(TestImages.Png(), "me.png")));
        var v2 = Version(png.GetProperty("avatarUrl").GetString()!);
        Assert.True(v2 > v1, "a new photo gets a newer version");
        var pngImage = await anonymous.GetAsync($"/api/users/AV_ONE/avatar?v={v2}");
        Assert.Equal(HttpStatusCode.OK, pngImage.StatusCode);
        Assert.Equal("image/png", pngImage.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TestImages.Png(), await pngImage.Content.ReadAsByteArrayAsync());
        Assert.False(File.Exists(Path.Combine(UserFolder(id), "avatar.jpg")));

        var webp = await Json(await client.PostAsync("/api/users/me/avatar", AvatarForm(TestImages.WebP(), "me.webp")));
        var v3 = Version(webp.GetProperty("avatarUrl").GetString()!);
        Assert.True(v3 > v2);
        var webpImage = await anonymous.GetAsync("/api/users/av_one/avatar");
        Assert.Equal("image/webp", webpImage.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TestImages.WebP(), await webpImage.Content.ReadAsByteArrayAsync());

        // Delete: the URL leaves every ref and the route 404s. Deleting again is not an error.
        var removed = await client.DeleteAsync("/api/users/me/avatar");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.True(IsNull(await Json(removed), "avatarUrl"));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url)).StatusCode);
        Assert.True(IsNull(await anonymous.GetFromJsonAsync<JsonElement>("/api/users/av_one"), "avatarUrl"));
        Assert.True(IsNull((await anonymous.GetFromJsonAsync<JsonElement>($"/api/posts/{postId}")).GetProperty("user"), "avatarUrl"));
        Assert.Empty(Directory.GetFiles(UserFolder(id), "avatar.*"));
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/users/me/avatar")).StatusCode);

        // A re-upload never reuses an old version, so a cached earlier URL cannot be mistaken for the new photo.
        var again = await Json(await client.PostAsync("/api/users/me/avatar", AvatarForm(TestImages.Jpeg())));
        var v4 = Version(again.GetProperty("avatarUrl").GetString()!);
        Assert.True(v4 > v3);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/users/av_one/avatar?v={v4}")).StatusCode);
    }

    [Fact]
    public async Task Avatar_upload_refuses_what_is_not_a_small_image()
    {
        var (client, _, _) = await _app.NewUserAsync("av_picky", language: "he");

        var garbage = await client.PostAsync("/api/users/me/avatar", AvatarForm(TestImages.Garbage()));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, garbage.StatusCode);
        Assert.Equal("זה לא נראה כמו JPEG, PNG או WebP.", (await Json(garbage)).GetProperty("error").GetString());

        var empty = await client.PostAsync("/api/users/me/avatar", AvatarForm([]));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, empty.StatusCode);

        // Just over the cap is caught on the file; far over it is refused from the content length alone.
        var over = await client.PostAsync("/api/users/me/avatar", AvatarForm(TestImages.Jpeg(2 * 1024 * 1024 + 1)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, over.StatusCode);
        Assert.Equal("התמונה גדולה מ-2MB. שווה לנסות קטנה יותר.", (await Json(over)).GetProperty("error").GetString());
        var huge = await client.PostAsync("/api/users/me/avatar", AvatarForm(TestImages.Jpeg(4 * 1024 * 1024)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);

        // Not a form, or a form without the photo.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/users/me/avatar", new { image = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/users/me/avatar", new MultipartFormDataContent { { new StringContent("x"), "note" } })).StatusCode);

        // None of that left a photo behind.
        Assert.True(IsNull(await client.GetFromJsonAsync<JsonElement>("/api/auth/me"), "avatarUrl"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/users/av_picky/avatar")).StatusCode);

        // Exactly the cap is fine.
        var atCap = await client.PostAsync("/api/users/me/avatar", AvatarForm(TestImages.Jpeg(2 * 1024 * 1024)));
        Assert.Equal(HttpStatusCode.OK, atCap.StatusCode);
        Assert.Matches(@"^/api/users/av_picky/avatar\?v=\d+$", (await Json(atCap)).GetProperty("avatarUrl").GetString());

        // Signed out: no upload, no delete; an unknown handle has no photo.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().PostAsync("/api/users/me/avatar", AvatarForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().DeleteAsync("/api/users/me/avatar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewClient().GetAsync("/api/users/av_nobody/avatar")).StatusCode);
    }

    [Fact]
    public async Task Account_type_switches_both_ways_and_rejects_anything_else()
    {
        var (client, _, _) = await _app.NewUserAsync("pf_switch");
        Assert.Equal("Person", (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("accountType").GetString());

        var brand = await client.PatchAsJsonAsync("/api/users/me", new { accountType = "brand" });
        Assert.Equal(HttpStatusCode.OK, brand.StatusCode);
        Assert.Equal("Brand", (await Json(brand)).GetProperty("accountType").GetString());
        Assert.Equal("Brand", (await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/users/pf_switch")).GetProperty("accountType").GetString());

        // Brand-only features open up right away.
        var checkId = await _app.CheckAsync(client);
        var post = await _app.PostAsync(client, checkId, products: new[] { new { label = "Tee", url = "https://shop.example/tee" } });
        Assert.Single(post.GetProperty("products").EnumerateArray());
        Assert.Equal("Brand", post.GetProperty("user").GetProperty("accountType").GetString());

        foreach (var bad in new[] { "shop", "", "1", "Person Brand" })
        {
            var response = await client.PatchAsJsonAsync("/api/users/me", new { accountType = bad });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("An account is either a person or a brand.", (await Json(response)).GetProperty("error").GetString());
        }

        Assert.Equal("Brand", (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("accountType").GetString());

        var person = await Json(await client.PatchAsJsonAsync("/api/users/me", new { accountType = " PERSON ", displayName = "Back to me" }));
        Assert.Equal("Person", person.GetProperty("accountType").GetString());
        Assert.Equal("Back to me", person.GetProperty("name").GetString());
        Assert.Equal("Person", (await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/users/pf_switch")).GetProperty("accountType").GetString());
        // Product links are for brands; the setting is checked at posting time, so the person is refused now.
        var another = await _app.CheckAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/posts",
            new { checkId = another, products = new[] { new { label = "Tee", url = "https://shop.example/tee" } } })).StatusCode);
    }

    [Fact]
    public async Task Interests_are_validated_deduplicated_and_round_trip()
    {
        var (client, id, _) = await _app.NewUserAsync("pf_tastes");
        Assert.Empty(Interests(await client.GetFromJsonAsync<JsonElement>("/api/auth/me")));

        var set = await client.PatchAsJsonAsync("/api/users/me", new { interests = new[] { "casual", " OLDMONEY ", "Casual", "sport" } });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(["Casual", "OldMoney", "Sport"], Interests(await Json(set)));
        Assert.Equal(["Casual", "OldMoney", "Sport"], Interests(await client.GetFromJsonAsync<JsonElement>("/api/auth/me")));
        using (var scope = _app.Services.CreateScope())
        {
            Assert.Equal("Casual,OldMoney,Sport", scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Find(id)!.Interests);
        }

        // One unknown entry refuses the whole list, and the list stays as it was.
        foreach (var bad in new object[] { new[] { "casual", "gothic" }, new[] { "3" }, new[] { "" }, new string?[] { null } })
        {
            var response = await client.PatchAsJsonAsync("/api/users/me", new { interests = bad });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Pick styles from the list, up to 8.", (await Json(response)).GetProperty("error").GetString());
        }

        Assert.Equal(["Casual", "OldMoney", "Sport"], Interests(await client.GetFromJsonAsync<JsonElement>("/api/auth/me")));

        // Every style at once is the ceiling, and it fits.
        var all = Enum.GetNames<StyleIntent>();
        var full = await Json(await client.PatchAsJsonAsync("/api/users/me", new { interests = all.Select(n => n.ToUpperInvariant()) }));
        Assert.Equal(all, Interests(full));

        // Leaving the field out keeps them; an empty list clears them.
        var untouched = await Json(await client.PatchAsJsonAsync("/api/users/me", new { bio = "styles stay" }));
        Assert.Equal(all, Interests(untouched));
        var cleared = await Json(await client.PatchAsJsonAsync("/api/users/me", new { interests = Array.Empty<string>() }));
        Assert.Empty(Interests(cleared));
        Assert.Empty(Interests(await client.GetFromJsonAsync<JsonElement>("/api/auth/me")));
        using (var scope = _app.Services.CreateScope())
        {
            Assert.Null(scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Find(id)!.Interests);
        }
    }

    [Fact]
    public async Task Community_and_featured_tabs_and_the_counts_on_the_profile()
    {
        var (_, brandId, _) = await _app.NewUserAsync("pf_house", accountType: "Brand");
        var (author, _, _) = await _app.NewUserAsync("pf_wearer");
        var (other, otherId, _) = await _app.NewUserAsync("pf_passerby");
        var anonymous = _app.NewClient();

        var p1 = await _app.CheckAndPostAsync(author);
        var p2 = await _app.CheckAndPostAsync(author);
        var p3 = await _app.CheckAndPostAsync(other);
        var hidden = await _app.CheckAndPostAsync(author);

        var now = DateTime.UtcNow;
        Mention(p1, brandId);
        Mention(p3, brandId);
        Mention(hidden, brandId);
        Mention(p2, otherId);
        Feature(p3, brandId, now.AddHours(-1));
        Feature(p1, brandId, now);
        Feature(hidden, brandId, now);
        Hide(hidden);

        // Community: newest first, hidden looks left out, each card showing the mention.
        var community = await anonymous.GetFromJsonAsync<JsonElement>("/api/users/pf_house/community");
        Assert.Equal([p3, p1], Ids(community));
        Assert.True(IsNull(community, "nextOffset"));
        Assert.All(community.GetProperty("items").EnumerateArray(),
            p => Assert.Contains(p.GetProperty("mentions").EnumerateArray(), m => m.GetProperty("handle").GetString() == "pf_house"));

        // Featured: newest featured first, not newest posted.
        var featured = await anonymous.GetFromJsonAsync<JsonElement>("/api/users/pf_house/featured");
        Assert.Equal([p1, p3], Ids(featured));
        Assert.All(featured.GetProperty("items").EnumerateArray(),
            p => Assert.Equal("pf_house", p.GetProperty("featuredBy").GetProperty("handle").GetString()));

        // A person's featured tab is their own looks a brand featured; their community tab is looks mentioning them.
        Assert.Equal([p1], Ids(await anonymous.GetFromJsonAsync<JsonElement>("/api/users/pf_wearer/featured")));
        Assert.Equal([p3], Ids(await anonymous.GetFromJsonAsync<JsonElement>("/api/users/PF_PASSERBY/featured")));
        Assert.Equal([p2], Ids(await anonymous.GetFromJsonAsync<JsonElement>("/api/users/pf_passerby/community")));
        Assert.Empty(Ids(await anonymous.GetFromJsonAsync<JsonElement>("/api/users/pf_wearer/community")));

        // Paging walks the same order.
        var first = await anonymous.GetFromJsonAsync<JsonElement>("/api/users/pf_house/community?limit=1");
        Assert.Equal([p3], Ids(first));
        Assert.Equal(1, first.GetProperty("nextOffset").GetInt32());
        var second = await anonymous.GetFromJsonAsync<JsonElement>("/api/users/pf_house/community?offset=1&limit=1");
        Assert.Equal([p1], Ids(second));
        Assert.True(IsNull(second, "nextOffset"));
        var page = await anonymous.GetFromJsonAsync<JsonElement>("/api/users/pf_house/featured?limit=1");
        Assert.Equal([p1], Ids(page));
        Assert.Equal(1, page.GetProperty("nextOffset").GetInt32());

        // The viewer's own state still applies.
        await author.PostAsync($"/api/posts/{p3}/fire", null);
        var asAuthor = await author.GetFromJsonAsync<JsonElement>("/api/users/pf_house/community");
        Assert.True(asAuthor.GetProperty("items")[0].GetProperty("fired").GetBoolean());
        Assert.True(asAuthor.GetProperty("items")[1].GetProperty("isMine").GetBoolean());

        // The counts on the profiles match the tabs.
        var house = await anonymous.GetFromJsonAsync<JsonElement>("/api/users/pf_house");
        Assert.Equal(2, house.GetProperty("featured").GetInt32());
        Assert.Equal(2, house.GetProperty("community").GetInt32());
        var wearer = await anonymous.GetFromJsonAsync<JsonElement>("/api/users/pf_wearer");
        Assert.Equal(1, wearer.GetProperty("featured").GetInt32());
        Assert.Equal(0, wearer.GetProperty("community").GetInt32());
        Assert.Equal(2, wearer.GetProperty("posts").GetInt32());
        var passerby = await anonymous.GetFromJsonAsync<JsonElement>("/api/users/pf_passerby");
        Assert.Equal(1, passerby.GetProperty("featured").GetInt32());
        Assert.Equal(1, passerby.GetProperty("community").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/users/pf_nobody/community")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/users/pf_nobody/featured")).StatusCode);
    }

    [Fact]
    public async Task Deleting_an_account_clears_its_feature_marks_mentions_tags_and_avatar()
    {
        var (brand, brandId, _) = await _app.NewUserAsync("pf_gone_brand", accountType: "Brand");
        var (author, authorId, _) = await _app.NewUserAsync("pf_stays");

        var look = await _app.CheckAndPostAsync(author);
        var brandLook = await _app.CheckAndPostAsync(brand);
        Mention(look, brandId);
        Mention(brandLook, authorId);
        Tag(brandLook, "sale");
        Feature(look, brandId, DateTime.UtcNow);
        Notify(authorId, NotificationType.Featured, "pf_gone_brand", look);
        Notify(brandId, NotificationType.Mention, "pf_stays", look);
        Notify(authorId, NotificationType.Mention, "pf_gone_brand", brandLook);
        Assert.Equal(HttpStatusCode.OK, (await brand.PostAsync("/api/users/me/avatar", AvatarForm(TestImages.Jpeg()))).StatusCode);

        var before = await author.GetFromJsonAsync<JsonElement>($"/api/posts/{look}");
        Assert.Equal("pf_gone_brand", before.GetProperty("featuredBy").GetProperty("handle").GetString());
        Assert.Single(before.GetProperty("mentions").EnumerateArray());
        Assert.Equal(1, (await author.GetFromJsonAsync<JsonElement>("/api/users/pf_stays")).GetProperty("featured").GetInt32());
        Assert.Equal(2, (await author.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("items").GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, (await brand.DeleteAsync("/api/users/me")).StatusCode);

        // The look stays up, no longer featured and no longer mentioning anyone who is gone.
        var after = await author.GetFromJsonAsync<JsonElement>($"/api/posts/{look}");
        Assert.True(IsNull(after, "featuredBy"));
        Assert.Empty(after.GetProperty("mentions").EnumerateArray());
        Assert.Equal(0, (await author.GetFromJsonAsync<JsonElement>("/api/users/pf_stays")).GetProperty("featured").GetInt32());
        Assert.Empty(Ids(await author.GetFromJsonAsync<JsonElement>("/api/users/pf_stays/featured")));
        Assert.Empty(Ids(await author.GetFromJsonAsync<JsonElement>("/api/users/pf_stays/community")));
        // Activity no longer announces a mark that is gone, from an account that is gone.
        Assert.Empty((await author.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("items").EnumerateArray());
        // The avatar went with the folder.
        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync("/api/users/pf_gone_brand/avatar")).StatusCode);
        Assert.False(Directory.Exists(UserFolder(brandId)));

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.Posts.Find(look)!;
        Assert.Null(row.FeaturedByBrandId);
        Assert.Null(row.FeaturedAt);
        Assert.False(db.PostMentions.Any(m => m.UserId == brandId || m.PostId == brandLook));
        Assert.False(db.PostTags.Any(t => t.PostId == brandLook));
        Assert.False(db.Notifications.Any(n => n.UserId == brandId || n.ActorHandle == "pf_gone_brand"));
        Assert.Null(db.Users.Find(brandId));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;

namespace FitCheck.Api.Tests;

/// <summary>
/// Verified brands: the owner sets the flag by hand (<c>--verify</c>, AdminSync.SetVerifiedAsync) and it travels on every
/// user ref (posts, search cards), the profile and "me", so the client can draw the check next to the brand mark.
/// </summary>
public class VerifiedTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public VerifiedTests(TestApp app) => _app = app;

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task The_verify_command_sets_and_clears_the_flag_case_insensitively()
    {
        var (client, _, _) = await _app.NewUserAsync("Vf_Brand", accountType: "Brand", displayName: "Verified Co");

        Assert.Equal(AdminChange.NotFound, await AdminSync.SetVerifiedAsync(_app.ConnectionString, "nobody_here", true));
        Assert.Equal(AdminChange.Changed, await AdminSync.SetVerifiedAsync(_app.ConnectionString, "@vf_brand", true));
        Assert.Equal(AdminChange.Unchanged, await AdminSync.SetVerifiedAsync(_app.ConnectionString, "VF_BRAND", true));
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("verified").GetBoolean());

        Assert.Equal(AdminChange.Changed, await AdminSync.SetVerifiedAsync(_app.ConnectionString, "vf_brand", false));
        Assert.Equal(AdminChange.Unchanged, await AdminSync.SetVerifiedAsync(_app.ConnectionString, "vf_brand", false));
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("verified").GetBoolean());
    }

    [Fact]
    public async Task A_verified_brand_reads_verified_on_its_posts_its_profile_and_search_cards()
    {
        var (brand, _, _) = await _app.NewUserAsync("vf_shop", accountType: "Brand", displayName: "Velour Shop");
        var (person, _, _) = await _app.NewUserAsync("vf_person", displayName: "Velour Fan");
        var brandPost = await _app.CheckAndPostAsync(brand, caption: "Our drop @vf_person");
        var personPost = await _app.CheckAndPostAsync(person);

        Assert.Equal(AdminChange.Changed, await AdminSync.SetVerifiedAsync(_app.ConnectionString, "vf_shop", true));

        // The post page and the feed build their refs in one batched query; a person stays unverified either way.
        var post = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/posts/" + brandPost);
        Assert.True(post.GetProperty("user").GetProperty("verified").GetBoolean());
        Assert.False(post.GetProperty("mentions")[0].GetProperty("verified").GetBoolean());
        var other = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/posts/" + personPost);
        Assert.False(other.GetProperty("user").GetProperty("verified").GetBoolean());

        var feed = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/feed?tab=foryou");
        var inFeed = feed.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == brandPost);
        Assert.True(inFeed.GetProperty("user").GetProperty("verified").GetBoolean());

        // The profile carries it beside the counts.
        var profile = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/users/vf_shop");
        Assert.True(profile.GetProperty("verified").GetBoolean());
        Assert.False((await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/users/vf_person")).GetProperty("verified").GetBoolean());

        // Search cards (PostReader.Ref) say the same.
        var search = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/search?q=Velour");
        var cards = search.GetProperty("users").EnumerateArray().ToDictionary(c => c.GetProperty("user").GetProperty("handle").GetString()!, c => c.GetProperty("user").GetProperty("verified").GetBoolean());
        Assert.True(cards["vf_shop"]);
        Assert.False(cards["vf_person"]);

        // A comment's author ref too, and the brand's own "me".
        var comment = await Json(await brand.PostAsJsonAsync($"/api/posts/{personPost}/comments", new { text = "Love it" }));
        Assert.True(comment.GetProperty("user").GetProperty("verified").GetBoolean());
        Assert.True((await brand.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("verified").GetBoolean());

        // The comment list says the same as the comment's own answer (it builds its refs by hand, not through PostReader).
        Assert.Equal(HttpStatusCode.Created, (await person.PostAsJsonAsync($"/api/posts/{personPost}/comments", new { text = "Thanks" })).StatusCode);
        var listed = await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{personPost}/comments");
        var byHandle = listed.EnumerateArray().ToDictionary(c => c.GetProperty("user").GetProperty("handle").GetString()!, c => c.GetProperty("user").GetProperty("verified").GetBoolean());
        Assert.True(byHandle["vf_shop"]);
        Assert.False(byHandle["vf_person"]);
    }
}

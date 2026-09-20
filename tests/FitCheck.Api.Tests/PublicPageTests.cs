using System.Net;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 13 — the growth loop: a look has a public address. What a crawler reads off /look/{id} and /u/{handle}, what
/// the page shows to a thumb, which photos may leave through the public image route and which may not, and the day
/// tallies that say the loop turned.
/// </summary>
public class PublicPageTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;
    private static int _handles;

    public PublicPageTests(TestApp app) => _app = app;

    private static string NextHandle(string prefix) => $"{prefix}{Interlocked.Increment(ref _handles)}";

    /// <summary>A client with no session and no CSRF header: exactly what a phone or a crawler is.</summary>
    private HttpClient Anonymous() => _app.CreateClient();

    private HttpClient Crawler(string agent)
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add("User-Agent", agent);
        return client;
    }

    private static async Task WithDbAsync(TestApp app, Func<AppDbContext, Task> action)
    {
        using var scope = app.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<long> CounterAsync(TestApp app, string name)
    {
        long value = 0;
        await WithDbAsync(app, async db => value = await Counters.ReadAsync(db, name, CancellationToken.None));
        return value;
    }

    /// <summary>One ok check posted, with the scripted stylist's headline and tip on it.</summary>
    private async Task<(Guid PostId, string Handle)> PostedLookAsync(string prefix = "public", string language = "en")
    {
        var handle = NextHandle(prefix);
        var (client, _, _) = await _app.NewUserAsync(handle, language: language);
        var checkId = await _app.CheckAsync(client, language: language);
        var post = await _app.PostAsync(client, checkId);
        return (post.GetProperty("id").GetGuid(), handle);
    }

    /// <summary>The content of a meta tag, by its property or name; null when the page has none.</summary>
    private static string? Meta(string html, string attribute, string key)
    {
        var marker = $"{attribute}=\"{key}\"";
        var at = html.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var contentAt = html.IndexOf("content=\"", at, StringComparison.Ordinal);
        if (contentAt < 0)
        {
            return null;
        }

        contentAt += "content=\"".Length;
        var end = html.IndexOf('"', contentAt);
        return WebUtility.HtmlDecode(html[contentAt..end]);
    }

    [Fact]
    public async Task A_posted_look_has_a_page_with_the_tags_a_share_unfurls()
    {
        var (postId, handle) = await PostedLookAsync();

        var response = await Anonymous().GetAsync($"/look/{postId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();

        // The title is the stylist's headline; the description carries the score, the intent and the tip.
        Assert.Equal("Clean casual with one weak link", Meta(html, "property", "og:title"));
        var description = Meta(html, "property", "og:description");
        Assert.NotNull(description);
        Assert.Contains("7/10", description);
        Assert.Contains("Date", description);
        Assert.Contains("Swap the running shoes", description);
        Assert.Equal(description, Meta(html, "name", "twitter:description"));
        Assert.Equal("summary_large_image", Meta(html, "name", "twitter:card"));

        // og:image is the public image route of this look, never the app's own /api one.
        var image = Meta(html, "property", "og:image");
        Assert.NotNull(image);
        Assert.EndsWith($"/look/{postId}/image", image);
        Assert.DoesNotContain("/api/", image);
        Assert.Equal(image, Meta(html, "name", "twitter:image"));

        // The body: the handle, the tip, the score, and the two ways on.
        Assert.Contains("@" + handle, html);
        Assert.Contains("Swap the running shoes", html);
        Assert.Contains("Check yours", html);
        Assert.Contains($"/#/post/{postId}", html);
        // Nothing that needs a session, and nothing that needs a script.
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", html);
        Assert.Contains("index, follow", Meta(html, "name", "robots")!);
    }

    [Fact]
    public async Task A_whatsapp_crawler_with_no_session_reads_the_same_tags()
    {
        var (postId, _) = await PostedLookAsync();

        foreach (var agent in new[]
        {
            "WhatsApp/2.2319.9 A",
            "facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)",
            "Twitterbot/1.0"
        })
        {
            var response = await Crawler(agent).GetAsync($"/look/{postId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal("Clean casual with one weak link", Meta(html, "property", "og:title"));
            Assert.NotNull(Meta(html, "property", "og:image"));
            Assert.Equal("OREVOSH", Meta(html, "property", "og:site_name"));
        }
    }

    [Fact]
    public async Task The_public_image_route_serves_a_posted_look_and_nothing_else()
    {
        var (postId, _) = await PostedLookAsync();

        var image = await Anonymous().GetAsync($"/look/{postId}/image");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await image.Content.ReadAsByteArrayAsync());

        // A check that was never posted has no public address at all: its id is not a post id.
        var (client, _, _) = await _app.NewUserAsync(NextHandle("unposted"));
        var checkId = await _app.CheckAsync(client);
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/look/{checkId}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/look/{Guid.NewGuid()}/image")).StatusCode);
    }

    [Fact]
    public async Task A_hidden_look_is_not_a_page_and_its_photo_does_not_leave()
    {
        var (postId, _) = await PostedLookAsync("hidden");
        await WithDbAsync(_app, async db =>
        {
            var post = await db.Posts.FirstAsync(p => p.Id == postId);
            post.Hidden = true;
            await db.SaveChangesAsync();
        });

        var page = await Anonymous().GetAsync($"/look/{postId}");
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("noindex", Meta(html, "name", "robots")!);
        Assert.Null(Meta(html, "property", "og:image"));
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/look/{postId}/image")).StatusCode);
    }

    [Fact]
    public async Task A_suspended_account_takes_its_looks_and_its_profile_off_the_public_pages()
    {
        var (postId, handle) = await PostedLookAsync("susp");
        Assert.Equal(HttpStatusCode.OK, (await Anonymous().GetAsync($"/u/{handle}")).StatusCode);

        await WithDbAsync(_app, async db =>
        {
            var user = await db.Users.FirstAsync(u => u.HandleLower == handle.ToLowerInvariant());
            user.Suspended = true;
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/look/{postId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/look/{postId}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/u/{handle}")).StatusCode);
    }

    [Fact]
    public async Task The_page_speaks_the_looks_own_language_and_direction()
    {
        var (postId, _) = await PostedLookAsync("heb", language: "he");

        var html = await Anonymous().GetStringAsync($"/look/{postId}");
        Assert.Contains("<html lang=\"he\" dir=\"rtl\">", html);
        Assert.Equal("he", Meta(html, "property", "og:locale"));
        Assert.Contains("לבדוק את שלך", html);
    }

    [Fact]
    public async Task A_profile_page_lists_the_visible_looks_as_a_grid_with_the_same_tags()
    {
        var handle = NextHandle("grid");
        var (client, _, _) = await _app.NewUserAsync(handle);
        var first = await _app.CheckAndPostAsync(client);
        var second = await _app.CheckAndPostAsync(client);

        var html = await Anonymous().GetStringAsync($"/u/{handle}");
        Assert.Contains($"/look/{first}", html);
        Assert.Contains($"/look/{second}", html);
        Assert.Contains("@" + handle, html);
        Assert.Equal($"@{handle} on OREVOSH", Meta(html, "property", "og:title"));
        Assert.Equal("summary_large_image", Meta(html, "name", "twitter:card"));
        // The newest look is the picture the profile unfurls with.
        Assert.EndsWith($"/look/{second}/image", Meta(html, "property", "og:image")!);
        Assert.Contains("index, follow", Meta(html, "name", "robots")!);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync("/u/nobody-at-all")).StatusCode);
    }

    [Fact]
    public async Task Every_arrival_is_counted_and_a_share_arrival_is_counted_again()
    {
        var (postId, _) = await PostedLookAsync("count");
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var arrivalsBefore = await CounterAsync(_app, Funnel.LookArrivals(day));
        var sharesBefore = await CounterAsync(_app, Funnel.ShareArrivals(day));

        Assert.Equal(HttpStatusCode.OK, (await Anonymous().GetAsync($"/look/{postId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Anonymous().GetAsync($"/look/{postId}?via=share")).StatusCode);
        // A look that is not there was not arrived at: a crawler re-fetching a deleted one must not move the funnel.
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/look/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync("/u/nobody-at-all")).StatusCode);

        Assert.Equal(arrivalsBefore + 2, await CounterAsync(_app, Funnel.LookArrivals(day)));
        Assert.Equal(sharesBefore + 1, await CounterAsync(_app, Funnel.ShareArrivals(day)));
    }

    [Fact]
    public async Task A_profile_arrival_is_counted_once_a_profile_is_really_there()
    {
        var handle = NextHandle("arr");
        var (client, _, _) = await _app.NewUserAsync(handle);
        Assert.NotNull(client);
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var before = await CounterAsync(_app, Funnel.ProfileArrivals(day));

        Assert.Equal(HttpStatusCode.OK, (await Anonymous().GetAsync($"/u/{handle}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync("/u/still-nobody")).StatusCode);

        Assert.Equal(before + 1, await CounterAsync(_app, Funnel.ProfileArrivals(day)));
    }

    [Fact]
    public async Task Robots_indexes_the_looks_and_the_profiles_and_nothing_of_the_app()
    {
        var robots = await Anonymous().GetStringAsync("/robots.txt");
        Assert.Contains("Allow: /look/", robots);
        Assert.Contains("Allow: /u/", robots);
        Assert.Contains("Disallow: /api/", robots);
        Assert.Contains("Disallow: /app/", robots);
        Assert.Contains("Disallow: /$", robots);
    }

    [Fact]
    public void The_public_urls_are_built_from_one_place()
    {
        var id = Guid.NewGuid();
        Assert.Equal($"https://looks.example.com/look/{id}", PublicPageEndpoints.LookUrl("https://looks.example.com", id));
        Assert.Equal($"/look/{id}/image", PublicPageEndpoints.ImagePath(id));
        Assert.Equal("https://looks.example.com/u/a.b_c", PublicPageEndpoints.ProfileUrl("https://looks.example.com", "a.b_c"));
    }

    [Fact]
    public async Task A_configured_public_origin_is_what_the_tags_name()
    {
        using var app = new TestApp { Settings = { ["Email:PublicOrigin"] = "https://looks.test" } };
        var client = app.NewClient();
        var me = await app.SignupAsync(client, "originhandle");
        Assert.NotEqual(default, me.GetProperty("id").GetGuid());
        var postId = await app.CheckAndPostAsync(client);

        var html = await app.CreateClient().GetStringAsync($"/look/{postId}");
        Assert.Equal($"https://looks.test/look/{postId}", Meta(html, "property", "og:url"));
        Assert.Equal($"https://looks.test/look/{postId}/image", Meta(html, "property", "og:image"));
    }
}

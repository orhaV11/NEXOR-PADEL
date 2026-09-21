using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;
using FitCheck.Api.Services.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 13, "all the protected things": the sweep's findings, each locked by a test. The two enumeration tests are the
/// point of the round: they read every route the app maps (<see cref="EndpointDataSource"/>), so a route added in a later
/// round is refused without the CSRF header and must declare its ownership rule here, or the suite fails.
/// </summary>
public static class SecurityFixtures
{
    /// <summary>Every mapped route under /api as (pattern, method), one row per method.</summary>
    public static List<(string Pattern, string Method)> ApiRoutes(TestApp app)
    {
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var routes = new List<(string, string)>();
        foreach (var endpoint in endpoints)
        {
            var pattern = endpoint.RoutePattern.RawText ?? "";
            if (!pattern.StartsWith("/api", StringComparison.Ordinal))
            {
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            foreach (var method in methods)
            {
                routes.Add((pattern, method.ToUpperInvariant()));
            }
        }

        return routes;
    }

    /// <summary>A pattern with its parameters filled: guids for guid constraints, otherwise by name.</summary>
    public static string Fill(string pattern, Func<string, string>? value = null) =>
        Regex.Replace(pattern, @"\{([^}:]+)(:[^}]*)?\}", match =>
        {
            var name = match.Groups[1].Value;
            var constraint = match.Groups[2].Value;
            return value?.Invoke(name) ?? (constraint.Contains("guid", StringComparison.Ordinal) ? Guid.NewGuid().ToString() : name switch
            {
                "handle" => "someone",
                "side" => "a",
                "tag" => "ootd",
                _ => "x"
            });
        });

    public static bool HasParameter(string pattern) => pattern.Contains('{');

    public static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        try
        {
            return JsonDocument.Parse(body).RootElement.GetProperty("error").GetString() ?? "";
        }
        catch (Exception)
        {
            return body;
        }
    }

    public static string SessionCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(Sessions.CookieName + "=", StringComparison.Ordinal)).Split(';')[0];

    /// <summary>A client that presents one cookie as its own and never learns another: a copy of a session.</summary>
    public static HttpClient WithCookie(TestApp app, string cookie)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        client.DefaultRequestHeaders.Add(Sessions.RequestHeader, Sessions.RequestHeaderValue);
        return client;
    }

    public static HttpClient FromAddress(TestApp app, string address)
    {
        var client = app.NewClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", address);
        return client;
    }
}

// ---------- 3. CSRF ----------

public class CsrfEnumerationTests
{
    [Fact]
    public async Task Every_state_changing_api_route_refuses_a_call_without_the_header()
    {
        using var app = new TestApp();
        var bare = app.BareClient();
        var forbidden = app.Services.GetRequiredService<Localizer>().Get("en", "error.forbidden");
        var routes = SecurityFixtures.ApiRoutes(app).Where(r => r.Method is not ("GET" or "HEAD" or "OPTIONS")).ToList();
        Assert.True(routes.Count >= 40, $"only {routes.Count} state-changing routes were found; the enumeration is broken");

        // The one write that cannot carry our header, and why. A route listed here must exist, or the exemption is stale.
        var exempt = new Dictionary<string, string>
        {
            [BillingEndpoints.WebhookPath] = "Stripe posts the event; its signature (Stripe-Signature over the raw body) is the guard, checked in BillingEndpoints"
        };

        foreach (var (pattern, method) in routes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), SecurityFixtures.Fill(pattern))
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            var response = await bare.SendAsync(request);
            if (exempt.Remove(pattern))
            {
                Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
                continue;
            }

            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{method} {pattern} answered {(int)response.StatusCode} without {Sessions.RequestHeader}");
            Assert.Equal(forbidden, await SecurityFixtures.ErrorAsync(response));
        }

        Assert.Empty(exempt);
    }

    [Fact]
    public async Task The_header_must_carry_the_exact_value_and_reads_are_open()
    {
        using var app = new TestApp();
        var wrong = app.BareClient();
        wrong.DefaultRequestHeaders.Add(Sessions.RequestHeader, "XMLHttpRequest");
        Assert.Equal(HttpStatusCode.Forbidden, (await wrong.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.BareClient().GetAsync("/api/config")).StatusCode);
    }
}

// ---------- 4. IDOR ----------

public class IdorEnumerationTests
{
    /// <summary>What user B, who owns nothing here, gets when asking for one of A's things through the route.</summary>
    private sealed record Rule(Func<Fixture, string> Path, HttpStatusCode[] Refusals, string Why, object? Body = null)
    {
        public static Rule Private(Func<Fixture, string> path, string why, object? body = null, params HttpStatusCode[] refusals) =>
            new(path, refusals.Length == 0 ? [HttpStatusCode.NotFound] : refusals, why, body);
    }

    /// <summary>A route that is public by design (the rule is elsewhere); the test only proves it does not crash on a stranger's id.</summary>
    private sealed record Public(string Why);

    private sealed class Fixture
    {
        public string HandleA = "";
        public Guid CheckA;
        public Guid PostA;
        public Guid HiddenPostA;
        public Guid CommentOnVisible;
        public Guid CommentOnHidden;
        public Guid ComparisonA;
        public Guid GuestCheck;
        public Guid ItemOnHidden;
        public Guid WardrobeItemA;
    }

    private static readonly Dictionary<(string Pattern, string Method), object> Rules = new()
    {
        [("/api/checks/{id:guid}", "GET")] = Rule.Private(f => $"/api/checks/{f.CheckA}", "a check is its owner's, or the guest's whose cookie made it"),
        [("/api/checks/{id:guid}/shared-video", "POST")] = Rule.Private(f => $"/api/checks/{f.CheckA}/shared-video", "counting a share is the owner's or the guest's"),
        [("/api/checks/{id:guid}/useful", "POST")] = Rule.Private(f => $"/api/checks/{f.CheckA}/useful", "saying whether the tip landed is the owner's or the guest's", new { useful = true }),
        // Round 14 — the loop: a pair is one account's own two checks, and so is the preference between them.
        [("/api/checks/{id:guid}/tried", "POST")] = Rule.Private(f => $"/api/checks/{f.CheckA}/tried", "linking a second look to a first is the owner's, over their own two checks", new { beforeId = Guid.Empty }),
        [("/api/checks/{id:guid}/tried/prefer", "POST")] = Rule.Private(f => $"/api/checks/{f.CheckA}/tried/prefer", "which of the pair you prefer is the pair owner's alone", new { prefer = "after" }),
        [("/api/compare/{id:guid}", "GET")] = Rule.Private(f => $"/api/compare/{f.ComparisonA}", "a comparison is its owner's alone"),
        [("/api/compare/{id:guid}/image/{side}", "GET")] = Rule.Private(f => $"/api/compare/{f.ComparisonA}/image/a", "the only route to a comparison's photos, owner only"),
        [("/api/posts/{id:guid}", "GET")] = Rule.Private(f => $"/api/posts/{f.HiddenPostA}", "a hidden look is invisible to everyone but its author and a moderator"),
        [("/api/posts/{id:guid}/image", "GET")] = Rule.Private(f => $"/api/posts/{f.HiddenPostA}/image", "the photo of a hidden look is private again"),
        [("/api/posts/{id:guid}/video", "GET")] = Rule.Private(f => $"/api/posts/{f.HiddenPostA}/video", "the clip of a hidden look is private again"),
        [("/api/posts/{id:guid}", "DELETE")] = Rule.Private(f => $"/api/posts/{f.PostA}", "author only, and a stranger cannot tell the look exists"),
        [("/api/posts/{id:guid}/items", "PATCH")] = Rule.Private(f => $"/api/posts/{f.PostA}/items", "tagging is the author's", new { items = Array.Empty<object>() }),
        [("/api/posts/{id:guid}/fire", "POST")] = Rule.Private(f => $"/api/posts/{f.HiddenPostA}/fire", "no social action on a hidden look"),
        [("/api/posts/{id:guid}/fire", "DELETE")] = Rule.Private(f => $"/api/posts/{f.HiddenPostA}/fire", "no social action on a hidden look"),
        [("/api/posts/{id:guid}/save", "POST")] = Rule.Private(f => $"/api/posts/{f.HiddenPostA}/save", "no social action on a hidden look"),
        [("/api/posts/{id:guid}/save", "DELETE")] = new Public("undoing your own save is idempotent and answers { saved: false } for any id: nothing about the look"),
        [("/api/posts/{id:guid}/report", "POST")] = new Public("a report of any existing look is taken (204, one per person) and answers nothing about it; a hidden one is already in the queue"),
        [("/api/posts/{id:guid}/feature", "POST")] = Rule.Private(f => $"/api/posts/{f.HiddenPostA}/feature", "brands only, and never a hidden look", null, HttpStatusCode.Forbidden, HttpStatusCode.NotFound),
        [("/api/posts/{id:guid}/feature", "DELETE")] = Rule.Private(f => $"/api/posts/{f.HiddenPostA}/feature", "brands only, and never a hidden look", null, HttpStatusCode.Forbidden, HttpStatusCode.NotFound),
        [("/api/posts/{id:guid}/comments", "GET")] = Rule.Private(f => $"/api/posts/{f.HiddenPostA}/comments", "the thread of a hidden look is hidden with it"),
        [("/api/posts/{id:guid}/comments", "POST")] = Rule.Private(f => $"/api/posts/{f.HiddenPostA}/comments", "no comment on a hidden look", new { text = "hello" }),
        // Round 14 — the community round: the two switches on a look are the author's own, and both answer a stranger
        // the way DELETE does — 404, saying nothing about whether the look exists.
        [("/api/posts/{id:guid}/score-privacy", "PATCH")] = Rule.Private(f => $"/api/posts/{f.PostA}/score-privacy", "keeping the grade private is the author's choice about their own look", new { scorePrivate = true }),
        [("/api/posts/{id:guid}/shared-after", "POST")] = Rule.Private(f => $"/api/posts/{f.PostA}/shared-after", "counting a before/after share is the author's, like counting a share video is the check owner's", new { withScores = true }),
        [("/api/comments/{id:guid}", "DELETE")] = Rule.Private(f => $"/api/comments/{f.CommentOnVisible}", "the comment's author or the look's author", null, HttpStatusCode.Forbidden),
        [("/api/comments/{id:guid}/report", "POST")] = new Public("a report of any existing comment is taken (204, one per person) and answers nothing about it, like a look's"),
        [("/api/items/{id:guid}/out", "GET")] = Rule.Private(f => $"/api/items/{f.ItemOnHidden}/out", "the store-link door opens only onto a public look"),
        [("/api/admin/posts/{id:guid}/hide", "POST")] = Rule.Private(f => $"/api/admin/posts/{f.PostA}/hide", "moderators only", null, HttpStatusCode.Forbidden),
        [("/api/admin/posts/{id:guid}/unhide", "POST")] = Rule.Private(f => $"/api/admin/posts/{f.HiddenPostA}/unhide", "moderators only", null, HttpStatusCode.Forbidden),
        [("/api/admin/posts/{id:guid}", "DELETE")] = Rule.Private(f => $"/api/admin/posts/{f.PostA}", "moderators only", null, HttpStatusCode.Forbidden),
        [("/api/admin/comments/{id:guid}/hide", "POST")] = Rule.Private(f => $"/api/admin/comments/{f.CommentOnVisible}/hide", "moderators only", null, HttpStatusCode.Forbidden),
        [("/api/admin/comments/{id:guid}/unhide", "POST")] = Rule.Private(f => $"/api/admin/comments/{f.CommentOnVisible}/unhide", "moderators only", null, HttpStatusCode.Forbidden),
        [("/api/admin/comments/{id:guid}", "DELETE")] = Rule.Private(f => $"/api/admin/comments/{f.CommentOnVisible}", "moderators only", null, HttpStatusCode.Forbidden),
        [("/api/admin/users/{handle}/suspend", "POST")] = Rule.Private(f => $"/api/admin/users/{f.HandleA}/suspend", "moderators only", null, HttpStatusCode.Forbidden),
        [("/api/admin/users/{handle}/unsuspend", "POST")] = Rule.Private(f => $"/api/admin/users/{f.HandleA}/unsuspend", "moderators only", null, HttpStatusCode.Forbidden),
        [("/api/admin/board/exclude/{postId:guid}", "DELETE")] = Rule.Private(f => $"/api/admin/board/exclude/{f.PostA}", "moderators only", null, HttpStatusCode.Forbidden),
        [("/api/users/{handle}", "GET")] = new Public("a profile is public; a suspended or blocked account reads as missing (ProfileTests, BlockTests)"),
        [("/api/users/{handle}/avatar", "GET")] = new Public("an avatar is public; a suspended account's is not served (ProfileTests)"),
        [("/api/users/{handle}/posts", "GET")] = new Public("public looks only (ProfileTests)"),
        [("/api/users/{handle}/community", "GET")] = new Public("public looks only"),
        [("/api/users/{handle}/featured", "GET")] = new Public("public looks only"),
        [("/api/users/{handle}/follow", "POST")] = new Public("anyone may follow a visible account (SocialTests, BlockTests)"),
        [("/api/users/{handle}/follow", "DELETE")] = new Public("anyone may unfollow"),
        [("/api/users/{handle}/block", "POST")] = new Public("anyone may block a visible account (BlockTests)"),
        [("/api/users/{handle}/block", "DELETE")] = new Public("only the blocker's own row (BlockTests)"),
        [("/api/tags/{tag}/posts", "GET")] = new Public("public looks only (ExploreTests)"),
        [("/api/challenges/{id:guid}", "GET")] = new Public("a challenge is public"),
        [("/api/challenges/{id:guid}/vote", "POST")] = new Public("one vote per person, never on your own entry (ChallengeTests)"),
        [("/api/challenges/{id:guid}/vote", "DELETE")] = new Public("your own vote only (ChallengeTests)"),
        // Round 14 — the wardrobe: a person's pieces are their own, and a stranger's id tells nobody one exists.
        [("/api/wardrobe/{id:guid}", "PATCH")] = Rule.Private(f => $"/api/wardrobe/{f.WardrobeItemA}", "a kept piece is its owner's alone", new { name = "not yours" }),
        [("/api/wardrobe/{id:guid}", "DELETE")] = new Public("deleting a piece that is not yours is idempotent and answers 204 for any id, the same as a made-up one: nothing about what anyone else keeps, and the row survives (WardrobeTests)")
    };

    private static async Task<Fixture> BuildAsync(TestApp app)
    {
        var (a, _, handleA) = await app.NewUserAsync("idor_a");
        var fixture = new Fixture { HandleA = handleA, CheckA = await app.CheckAsync(a) };
        fixture.PostA = await app.CheckAndPostAsync(a);
        fixture.CommentOnVisible = (await Json(await a.PostAsJsonAsync($"/api/posts/{fixture.PostA}/comments", new { text = "mine" }))).GetProperty("id").GetGuid();

        fixture.HiddenPostA = await app.CheckAndPostAsync(a);
        fixture.CommentOnHidden = (await Json(await a.PostAsJsonAsync($"/api/posts/{fixture.HiddenPostA}/comments", new { text = "under review soon" }))).GetProperty("id").GetGuid();
        var items = await Json(await a.PatchAsJsonAsync($"/api/posts/{fixture.HiddenPostA}/items",
            new { items = new[] { new { name = "black boots", category = "shoes", url = "https://shop.example/boots" } } }));
        var rows = items.ValueKind == JsonValueKind.Array ? items : items.GetProperty("items");
        fixture.ItemOnHidden = rows.EnumerateArray().First().GetProperty("id").GetGuid();

        var (moderator, _, _) = await app.NewUserAsync("idor_mod");
        Assert.Equal(AdminChange.Changed, await app.PromoteAsync("idor_mod"));
        var hide = await moderator.PostAsync($"/api/admin/posts/{fixture.HiddenPostA}/hide", null);
        Assert.True(hide.IsSuccessStatusCode, $"hide: {(int)hide.StatusCode}");

        var compare = new MultipartFormDataContent { { new StringContent("Date"), "intent" }, { new StringContent("en"), "language" } };
        compare.Add(Part(TestImages.Jpeg()), "imageA", "a.jpg");
        compare.Add(Part(TestImages.Jpeg(3000)), "imageB", "b.jpg");
        app.Vision.Handler = request => request.HasSecondImage ? OutfitComparerTests.Pick() : Payloads.Ok();
        var comparison = await a.PostAsync("/api/compare", compare);
        Assert.Equal(HttpStatusCode.Created, comparison.StatusCode);
        fixture.ComparisonA = (await Json(comparison)).GetProperty("id").GetGuid();
        app.Vision.Handler = _ => Payloads.Ok();

        // Round 14: one piece of A's, kept from A's own check (the only way a piece gets in).
        var kept = await a.PostAsJsonAsync("/api/wardrobe", new { checkId = fixture.CheckA, name = "White tee" });
        fixture.WardrobeItemA = (await Json(kept)).GetProperty("id").GetGuid();

        var guest = app.NewClient();
        var guestCheck = await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.Created, guestCheck.StatusCode);
        fixture.GuestCheck = (await Json(guestCheck)).GetProperty("id").GetGuid();
        return fixture;
    }

    private static ByteArrayContent Part(byte[] bytes)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return part;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Every_route_with_an_id_or_a_handle_declares_its_rule_and_keeps_a_strangers_things_from_user_B()
    {
        using var app = new TestApp();
        var fixture = await BuildAsync(app);
        var (b, _, _) = await app.NewUserAsync("idor_b");
        var anonymous = app.NewClient();

        var routes = SecurityFixtures.ApiRoutes(app).Where(r => SecurityFixtures.HasParameter(r.Pattern)).Distinct().ToList();
        Assert.True(routes.Count >= 40, $"only {routes.Count} routes with a parameter were found; the enumeration is broken");
        var undeclared = routes.Where(r => !Rules.ContainsKey(r)).ToList();
        Assert.True(undeclared.Count == 0, "routes with a parameter and no rule in IdorEnumerationTests.Rules: " + string.Join(", ", undeclared.Select(r => $"{r.Method} {r.Pattern}")));
        var stale = Rules.Keys.Except(routes).ToList();
        Assert.True(stale.Count == 0, "rules for routes that no longer exist: " + string.Join(", ", stale.Select(r => $"{r.Method} {r.Pattern}")));

        // The private rules first: every one of A's things, asked for by B and by nobody.
        foreach (var (pattern, method) in routes)
        {
            if (Rules[(pattern, method)] is not Rule rule)
            {
                continue;
            }

            var path = rule.Path(fixture);
            var asB = await Send(b, method, path, rule.Body);
            Assert.True(rule.Refusals.Contains(asB.StatusCode), $"{method} {path} answered {(int)asB.StatusCode} to user B ({rule.Why}); expected {string.Join("/", rule.Refusals.Select(s => (int)s))}");
            var asNobody = await Send(anonymous, method, path, rule.Body);
            Assert.True((int)asNobody.StatusCode is 401 or 403 or 404, $"{method} {path} answered {(int)asNobody.StatusCode} to nobody");
        }

        // Then the public ones, after the private pass: a block or a follow B makes here must not colour the answers above.
        foreach (var (pattern, method) in routes)
        {
            if (Rules[(pattern, method)] is not Public)
            {
                continue;
            }

            var response = await Send(b, method, SecurityFixtures.Fill(pattern, name => name == "handle" ? fixture.HandleA : Guid.NewGuid().ToString()), null);
            Assert.True((int)response.StatusCode < 500, $"{method} {pattern} crashed: {(int)response.StatusCode}");
        }

        // Nothing serves a check's photo by id, posted or not: the look's post is the only door (README, "Photos are never served by path").
        Assert.DoesNotContain(routes, r => r.Pattern.StartsWith("/api/checks/", StringComparison.Ordinal)
            && (r.Pattern.EndsWith("/image", StringComparison.Ordinal) || r.Pattern.Contains("/image/", StringComparison.Ordinal) || r.Pattern.EndsWith("/video", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_guests_check_is_the_guests_alone()
    {
        using var app = new TestApp();
        var fixture = await BuildAsync(app);
        var (b, _, _) = await app.NewUserAsync("idor_b");
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/checks/{fixture.GuestCheck}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.NewClient().GetAsync($"/api/checks/{fixture.GuestCheck}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/checks/{fixture.GuestCheck}/shared-video", null)).StatusCode);
    }

    [Fact]
    public async Task The_admin_routes_the_metrics_and_the_export_are_gated_and_a_suspended_moderator_is_out()
    {
        using var app = new TestApp();
        var fixture = await BuildAsync(app);
        var (b, _, _) = await app.NewUserAsync("idor_b");
        var anonymous = app.NewClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync("/api/metrics/pilot")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync("/api/admin/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync("/api/admin/users?q=idor")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.PostAsJsonAsync("/api/admin/board/exclude", new { postId = fixture.PostA })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/metrics/pilot")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/queue")).StatusCode);

        // B's export is B's: nothing of A's in it.
        var export = await b.GetStringAsync("/api/users/me/export");
        Assert.Contains("idor_b", export);
        Assert.DoesNotContain(fixture.CheckA.ToString(), export);
        Assert.DoesNotContain(fixture.PostA.ToString(), export);

        // A moderator whose account is suspended (a flag on the row, set on the box) is refused at every admin door and signed
        // out. The refusal deletes the cookie, so the client here keeps presenting a copy of it, as a kept device would.
        var signup = await app.NewClient().PostAsJsonAsync("/api/auth/signup", new { handle = "idor_mod2", password = "password123", birthDate = "1990-01-01", language = "en" });
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);
        var moderatorId = (await signup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var suspendedModerator = SecurityFixtures.WithCookie(app, SecurityFixtures.SessionCookie(signup));
        Assert.Equal(AdminChange.Changed, await app.PromoteAsync("idor_mod2"));
        Assert.Equal(HttpStatusCode.OK, (await suspendedModerator.GetAsync("/api/admin/queue")).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Id == moderatorId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Suspended, true));
        }

        var refused = await suspendedModerator.GetAsync("/api/admin/queue");
        Assert.True(refused.StatusCode == HttpStatusCode.Forbidden, $"{(int)refused.StatusCode}: {await refused.Content.ReadAsStringAsync()}");
        Assert.Contains(refused.Headers.GetValues("Set-Cookie"), c => c.StartsWith(Sessions.CookieName + "=", StringComparison.Ordinal) && c.Contains("1970", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Forbidden, (await suspendedModerator.PostAsync($"/api/admin/posts/{fixture.PostA}/hide", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await suspendedModerator.DeleteAsync($"/api/admin/board/exclude/{fixture.PostA}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await suspendedModerator.GetAsync("/api/metrics/pilot")).StatusCode);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string method, string path, object? body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        else if (method is "POST" or "PATCH" or "PUT")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return client.SendAsync(request);
    }
}

// ---------- 6. Headers ----------

public class SecurityHeaderSetTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/landing/")]
    [InlineData("/landing/index.he.html")]
    [InlineData("/api/config")]
    [InlineData("/api/posts/00000000-0000-0000-0000-000000000000")]
    public async Task Every_response_carries_the_policy_and_the_rest_of_the_set(string path)
    {
        using var app = new TestApp();
        var response = await app.NewClient().GetAsync(path);

        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("none", Assert.Single(response.Headers.GetValues("X-Permitted-Cross-Domain-Policies")));
        Assert.Equal(SecurityHeaders.ReferrerPolicy, Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal(SecurityHeaders.PermissionsPolicy, Assert.Single(response.Headers.GetValues("Permissions-Policy")));
    }

    [Fact]
    public void The_policy_says_what_the_client_is()
    {
        var csp = SecurityHeaders.ContentSecurityPolicy;
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self';", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe", csp);
        Assert.Contains("style-src 'self' 'unsafe-inline' https://fonts.googleapis.com", csp);
        Assert.Contains("font-src 'self' https://fonts.gstatic.com", csp);
        Assert.Contains("img-src 'self' blob: data:", csp);
        Assert.Contains("media-src 'self' blob:", csp);
        Assert.Contains("connect-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("base-uri 'self'", csp);
        Assert.Contains("form-action 'self'", csp);
    }

    [Fact]
    public async Task Api_answers_are_never_stored_and_never_loaded_cross_origin_unless_a_route_says_otherwise()
    {
        using var app = new TestApp();
        var (client, _, handle) = await app.NewUserAsync("hdr_a");
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal("no-store", me.Headers.CacheControl?.ToString());
        Assert.Equal("same-origin", Assert.Single(me.Headers.GetValues("Cross-Origin-Resource-Policy")));

        var avatarForm = new MultipartFormDataContent();
        var part = new ByteArrayContent(TestImages.Jpeg());
        part.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        avatarForm.Add(part, "image", "me.jpg");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/users/me/avatar", avatarForm)).StatusCode);
        var avatar = await app.NewClient().GetAsync($"/api/users/{handle}/avatar");
        Assert.Equal(HttpStatusCode.OK, avatar.StatusCode);
        // The avatar route's own rule stands (public, a day); the policy's default did not overwrite it.
        Assert.Equal("public, max-age=86400", avatar.Headers.CacheControl?.ToString());

        var shell = await app.NewClient().GetAsync("/");
        Assert.False(shell.Headers.Contains("Cross-Origin-Resource-Policy"));
    }

    [Fact]
    public async Task The_headers_are_on_a_csrf_refusal_and_on_a_404()
    {
        using var app = new TestApp();
        var refused = await app.BareClient().PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, Assert.Single(refused.Headers.GetValues("Content-Security-Policy")));
        var missing = await app.NewClient().GetAsync("/no-such-page");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, Assert.Single(missing.Headers.GetValues("Content-Security-Policy")));
    }
}

// ---------- 2. Cookies and sessions ----------

public class SessionCookieTests
{
    private static object Signup(string handle) => new { handle, password = "correct horse battery", birthDate = "1990-01-01", language = "en" };

    [Fact]
    public async Task The_session_cookie_is_httponly_strict_and_secure_only_when_the_request_came_over_https()
    {
        using var app = new TestApp();
        var plain = await app.NewClient().PostAsJsonAsync("/api/auth/signup", Signup("cookie_plain"));
        Assert.Equal(HttpStatusCode.Created, plain.StatusCode);
        var cookie = plain.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(Sessions.CookieName + "=", StringComparison.Ordinal)).ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/", cookie);
        Assert.DoesNotContain("secure", cookie);

        var https = app.NewClient();
        https.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        var secure = await https.PostAsJsonAsync("/api/auth/signup", Signup("cookie_https"));
        Assert.Equal(HttpStatusCode.Created, secure.StatusCode);
        var secureCookie = secure.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(Sessions.CookieName + "=", StringComparison.Ordinal)).ToLowerInvariant();
        Assert.Contains("httponly", secureCookie);
        Assert.Contains("samesite=strict", secureCookie);
        Assert.Contains("secure", secureCookie);
        Assert.Contains("expires=", secureCookie);
    }

    [Fact]
    public async Task The_guest_cookie_has_the_same_flags()
    {
        using var app = new TestApp();
        var https = app.NewClient();
        https.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        var check = await https.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.Created, check.StatusCode);
        var cookie = check.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(GuestChecks.CookieName + "=", StringComparison.Ordinal)).ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("expires=", cookie);
    }

    [Fact]
    public async Task Logout_ends_the_session_on_the_server_and_only_that_session()
    {
        using var app = new TestApp();
        var phone = app.NewClient();
        var signup = await phone.PostAsJsonAsync("/api/auth/signup", Signup("logout_a"));
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);
        var stolen = SecurityFixtures.WithCookie(app, SecurityFixtures.SessionCookie(signup));
        Assert.Equal(HttpStatusCode.OK, (await stolen.GetAsync("/api/auth/me")).StatusCode);

        var laptop = app.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await laptop.PostAsJsonAsync("/api/auth/login", new { handle = "logout_a", password = "correct horse battery" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await phone.PostAsync("/api/auth/logout", null)).StatusCode);

        // The copy of the phone's cookie is dead on the server, not only gone from the phone; the laptop's session is untouched.
        var replay = await stolen.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Contains(replay.Headers.GetValues("Set-Cookie"), c => c.StartsWith(Sessions.CookieName + "=", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.PostAsync("/api/checks/claim", null)).StatusCode);

        // The revocation is a row, so it survives a restart; signing out again is a quiet 204.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Counters.CountAsync(c => c.Name.StartsWith(SessionRevocation.RevokedPrefix)));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await stolen.PostAsync("/api/auth/logout", null)).StatusCode);
    }

    [Fact]
    public async Task A_password_reset_ends_every_earlier_session_and_keeps_the_one_it_opens()
    {
        using var app = new TestApp();
        var phone = app.NewClient();
        var signup = await phone.PostAsJsonAsync("/api/auth/signup", Signup("reset_all"));
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);
        var stolen = SecurityFixtures.WithCookie(app, SecurityFixtures.SessionCookie(signup));
        Assert.Equal(HttpStatusCode.OK, (await phone.PatchAsJsonAsync("/api/users/me", new { email = "reset_all@example.com" })).StatusCode);
        var verifyToken = TokenIn(Assert.Single(app.Email.To("reset_all@example.com")), "verify");
        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().PostAsJsonAsync("/api/auth/verify-email", new { token = verifyToken })).StatusCode);

        // A ticket remembers when it was issued to the second, and the cutoff is a second too: a session opened in the very
        // second of the reset would be the one the reset opens. So the phone's session is at least a second older here.
        await Task.Delay(1100);
        Assert.Equal(HttpStatusCode.Accepted, (await app.NewClient().PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail = "reset_all" })).StatusCode);
        var mails = await app.Email.WaitForAsync(m => m.To == "reset_all@example.com" && m.Body.Contains("#/reset/"));
        var resetToken = TokenIn(Assert.Single(mails), "reset");
        // 43 base64url characters: 32 random bytes, 256 bits, and the link is the only place it appears.
        Assert.Matches("^[A-Za-z0-9_-]{43}$", resetToken);

        var fresh = app.NewClient();
        var reset = await fresh.PostAsJsonAsync("/api/auth/reset", new { token = resetToken, password = "new correct horse" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stolen.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/auth/me")).StatusCode);
        // Single use: the same link with another password is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await app.NewClient().PostAsJsonAsync("/api/auth/reset", new { token = resetToken, password = "third password!" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().PostAsJsonAsync("/api/auth/login", new { handle = "reset_all", password = "new correct horse" })).StatusCode);
    }

    [Fact]
    public async Task A_ticket_without_a_session_id_is_refused_and_one_with_an_id_is_not()
    {
        using var app = new TestApp();
        var (_, userId, _) = await app.NewUserAsync("old_ticket");
        var format = new TicketDataFormat(app.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", CookieAuthenticationDefaults.AuthenticationScheme, "v2"));

        string Mint(bool withId)
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Name, "old_ticket")], CookieAuthenticationDefaults.AuthenticationScheme);
            var properties = new AuthenticationProperties { IsPersistent = true, IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) };
            if (withId)
            {
                properties.Items[Sessions.SessionIdKey] = "test-session-id";
            }

            return format.Protect(new AuthenticationTicket(new ClaimsPrincipal(identity), properties, CookieAuthenticationDefaults.AuthenticationScheme));
        }

        // A cookie from before this round decrypts fine and is still refused: the deploy signs everyone in again, once.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SecurityFixtures.WithCookie(app, $"{Sessions.CookieName}={Mint(withId: false)}").GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SecurityFixtures.WithCookie(app, $"{Sessions.CookieName}={Mint(withId: true)}").GetAsync("/api/auth/me")).StatusCode);
    }

    private static string TokenIn(EmailMessage message, string purpose)
    {
        var match = Regex.Match(message.Body, $"#/{purpose}/([A-Za-z0-9_-]+)");
        Assert.True(match.Success, $"no {purpose} link in: {message.Body}");
        return match.Groups[1].Value;
    }
}

// ---------- 5. Auth brakes and the password rule ----------

public class AuthBrakeTests
{
    [Fact]
    public async Task The_token_routes_are_braked_per_address()
    {
        using var app = new TestApp { Settings = { ["Limits:TokenAttemptsPerQuarterHourPerIp"] = "3" } };
        var first = SecurityFixtures.FromAddress(app, "203.0.113.10");
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/auth/reset", new { token = "nope", password = "a fine password" })).StatusCode);
        }

        var refused = await first.PostAsJsonAsync("/api/auth/reset", new { token = "nope", password = "a fine password" });
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.NotNull(refused.Headers.RetryAfter);
        // The two token routes share the window: the address that guessed reset links cannot switch to verify links.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.PostAsJsonAsync("/api/auth/verify-email", new { token = "nope" })).StatusCode);

        var second = SecurityFixtures.FromAddress(app, "203.0.113.11");
        Assert.Equal(HttpStatusCode.BadRequest, (await second.PostAsJsonAsync("/api/auth/verify-email", new { token = "nope" })).StatusCode);
    }

    [Fact]
    public async Task Failed_sign_ins_are_braked_per_account_across_addresses()
    {
        using var app = new TestApp { Settings = { ["Limits:LoginFailuresPerQuarterHourPerAccount"] = "3" } };
        await app.NewUserAsync("braked");
        await app.NewUserAsync("neighbour");
        var attacker = SecurityFixtures.FromAddress(app, "198.51.100.1");
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.PostAsJsonAsync("/api/auth/login", new { handle = "braked", password = $"guess {i}" })).StatusCode);
        }

        // The fourth try, from another machine and with the right password, meets the wall until the quarter hour turns.
        var elsewhere = SecurityFixtures.FromAddress(app, "198.51.100.2");
        var refused = await elsewhere.PostAsJsonAsync("/api/auth/login", new { handle = "BRAKED", password = "password123" });
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(app.Services.GetRequiredService<Localizer>().Get("en", "error.login_limited"), await SecurityFixtures.ErrorAsync(refused));
        var retryAfter = refused.Headers.RetryAfter?.Delta;
        Assert.True(retryAfter is { } delta && delta > TimeSpan.Zero && delta <= TimeSpan.FromMinutes(15), $"Retry-After: {retryAfter}");

        // Another account from the same machine is not slowed, and an unknown handle still answers 401 (nothing is counted for it).
        Assert.Equal(HttpStatusCode.OK, (await elsewhere.PostAsJsonAsync("/api/auth/login", new { handle = "neighbour", password = "password123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await elsewhere.PostAsJsonAsync("/api/auth/login", new { handle = "nobody_here", password = "password123" })).StatusCode);
    }

    [Fact]
    public void The_brake_counts_in_fixed_windows_and_says_when_the_window_turns()
    {
        var brake = new AccountBrake();
        var window = TimeSpan.FromMinutes(15);
        var now = new DateTime(2026, 9, 20, 10, 3, 0, DateTimeKind.Utc);
        Assert.False(brake.IsBraked("k", 2, window, now));
        Assert.Equal(1, brake.Hit("k", window, now));
        Assert.Equal(2, brake.Hit("k", window, now.AddMinutes(5)));
        Assert.True(brake.IsBraked("k", 2, window, now.AddMinutes(10)));
        Assert.False(brake.IsBraked("k", 2, window, now.AddMinutes(20)));
        Assert.Equal(1, brake.Hit("k", window, now.AddMinutes(20)));
        Assert.Equal(12 * 60, AccountBrake.SecondsUntilWindowEnds(window, now));
        brake.Clear("k");
        Assert.False(brake.IsBraked("k", 1, window, now.AddMinutes(20)));
    }

    [Theory]
    [InlineData("gabriella", null, "gabriella2024", "error.password_personal")]
    [InlineData("gabriella", null, "xxGABRIELLAxx", "error.password_personal")]
    [InlineData("noa", "noa.levi@example.com", "noa.levi@example.com!", "error.password_personal")]
    [InlineData("noa", "noa.levi@example.com", "my-noa.levi-pass", "error.password_personal")]
    [InlineData("al", null, "salad-days-2026", null)]
    [InlineData("gabriella", null, "1234567", "error.password_short")]
    [InlineData("gabriella", null, "correct horse battery staple", null)]
    public void The_password_rule(string handle, string? email, string password, string? expected)
    {
        Assert.Equal(expected, AuthEndpoints.PasswordProblem(password, handle, email));
    }

    [Fact]
    public void Long_passphrases_pass_and_the_ceiling_is_far_above_128()
    {
        Assert.Null(AuthEndpoints.PasswordProblem(new string('p', 128), "handle", null));
        Assert.Null(AuthEndpoints.PasswordProblem(new string('p', AuthEndpoints.PasswordMaxLength), "handle", null));
        Assert.Equal("error.password_short", AuthEndpoints.PasswordProblem(new string('p', AuthEndpoints.PasswordMaxLength + 1), "handle", null));
        Assert.True(AuthEndpoints.PasswordMaxLength >= 128);
    }

    [Fact]
    public async Task Signup_and_reset_refuse_a_password_that_carries_the_handle_or_the_address()
    {
        using var app = new TestApp();
        var localizer = app.Services.GetRequiredService<Localizer>();
        var refused = await app.NewClient().PostAsJsonAsync("/api/auth/signup", new { handle = "yael_k", password = "Yael_K-2026!", birthDate = "1990-01-01", language = "en" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(localizer.Get("en", "error.password_personal"), await SecurityFixtures.ErrorAsync(refused));

        var (client, _, _) = await app.NewUserAsync("yael_k");
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync("/api/users/me", new { email = "yael.k@example.com" })).StatusCode);
        var verify = Regex.Match(Assert.Single(app.Email.To("yael.k@example.com")).Body, "#/verify/([A-Za-z0-9_-]+)").Groups[1].Value;
        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().PostAsJsonAsync("/api/auth/verify-email", new { token = verify })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await app.NewClient().PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail = "yael.k@example.com" })).StatusCode);
        var reset = Regex.Match(Assert.Single(await app.Email.WaitForAsync(m => m.To == "yael.k@example.com" && m.Body.Contains("#/reset/"))).Body, "#/reset/([A-Za-z0-9_-]+)").Groups[1].Value;

        var personal = await app.NewClient().PostAsJsonAsync("/api/auth/reset", new { token = reset, password = "yael.k forever" });
        Assert.Equal(HttpStatusCode.BadRequest, personal.StatusCode);
        Assert.Equal(localizer.Get("en", "error.password_personal"), await SecurityFixtures.ErrorAsync(personal));
        // The link survives the refusal: the person fixes the password, not the link.
        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().PostAsJsonAsync("/api/auth/reset", new { token = reset, password = "a whole new phrase" })).StatusCode);
    }
}

// ---------- 12. Logs ----------

public class LogSweepTests
{
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Logger(RecordingLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (provider.Lines)
                {
                    provider.Lines.Add($"{category}: {formatter(state, exception)} {exception}");
                }
            }
        }
    }

    [Fact]
    public async Task Nothing_logs_a_password_a_token_a_cookie_a_query_or_a_mail_body()
    {
        using var app = new TestApp();
        var provider = new RecordingLoggerProvider();
        using var logged = app.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(RequestLog.SettingKey, "true");
            builder.ConfigureLogging(logging => logging.AddProvider(provider));
        });
        var client = logged.CreateClient();
        client.DefaultRequestHeaders.Add(Sessions.RequestHeader, Sessions.RequestHeaderValue);

        const string password = "Tr0ub4dor-secret-42";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { handle = "log_sweep", password, birthDate = "1990-01-01", language = "en" });
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);
        var cookieValue = SecurityFixtures.SessionCookie(signup).Split('=', 2)[1];
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync("/api/users/me", new { email = "log_sweep@example.com" })).StatusCode);
        var verifyMail = Assert.Single(app.Email.To("log_sweep@example.com"));
        var verifyToken = Regex.Match(verifyMail.Body, "#/verify/([A-Za-z0-9_-]+)").Groups[1].Value;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/verify-email", new { token = verifyToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail = "log_sweep" })).StatusCode);
        var resetMail = Assert.Single(await app.Email.WaitForAsync(m => m.To == "log_sweep@example.com" && m.Body.Contains("#/reset/")));
        var resetToken = Regex.Match(resetMail.Body, "#/reset/([A-Za-z0-9_-]+)").Groups[1].Value;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/reset", new { token = resetToken, password = "another-secret-phrase" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { handle = "log_sweep", password = "wrong-guess-secret" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { handle = "log_sweep", password = "another-secret-phrase" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/search?q=secretquery")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);

        List<string> lines;
        lock (provider.Lines)
        {
            lines = provider.Lines.ToList();
        }

        Assert.Contains(lines, l => l.Contains("api POST /api/auth/signup 201", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("api GET /api/search 200", StringComparison.Ordinal));
        foreach (var secret in new[] { password, "another-secret-phrase", "wrong-guess-secret", verifyToken, resetToken, cookieValue, "secretquery", verifyMail.Body, resetMail.Body })
        {
            Assert.DoesNotContain(lines, l => l.Contains(secret, StringComparison.Ordinal));
        }

        Assert.DoesNotContain(lines, l => l.Contains("Cookie:", StringComparison.OrdinalIgnoreCase) || l.Contains("Stripe-Signature", StringComparison.OrdinalIgnoreCase));
    }
}

// ---------- 1. Photo privacy ----------

public static class MetadataSamples
{
    /// <summary>An 8×8 baseline JPEG from ffmpeg: SOI, JFIF APP0, COM, DQT, DHT, SOF0, SOS, scan, EOI.</summary>
    public static byte[] TinyJpeg() => Convert.FromHexString(
        "ffd8ffe000104a46494600010200000100010000fffe00104c61766336302e33312e31303200ffdb0043000804040404040505050505050606060606060606060606060607070708080807070706060707080808080909090808080809090a0a0a0c0c0b0b0e0e0e111114ffc4004c0001010000000000000000000000000000000501010100000000000000000000000000000506100100000000000000000000000000000000110100000000000000000000000000000000ffc00011080008000803012200021100031100ffda000c03010002110311003f008202d8b3ffd9");

    /// <summary>A 4×4 RGB PNG from ffmpeg: IHDR, pHYs, IDAT, IEND.</summary>
    public static byte[] TinyPng() => Convert.FromHexString(
        "89504e470d0a1a0a0000000d49484452000000040000000408020000002693092900000009704859730000000100000001004f25c4d60000001049444154789c63a8b7db0f470cc4710065d217c1f399dde20000000049454e44ae426082");

    /// <summary>A 4×4 lossless WebP from libwebp: RIFF, WEBP, one VP8L chunk.</summary>
    public static byte[] TinyWebP() => Convert.FromHexString("524946461e000000574542505650384c110000002f03c000000750a00236b8ff8188e87f0000");

    public const string Device = "Pixel 9 Pro";

    /// <summary>An Exif APP1 segment: IFD0 with the device model and a GPS IFD with a latitude, the way a phone writes it.</summary>
    public static byte[] ExifApp1()
    {
        var tiff = new MemoryStream();
        var w = new BinaryWriter(tiff);
        w.Write("II"u8);
        w.Write((ushort)42);
        w.Write(8u);
        // IFD0 at 8: two entries, then the next-IFD pointer. Data after: the model string at 8 + 2 + 24 + 4 = 38.
        var model = Encoding.ASCII.GetBytes(Device + "\0");
        const uint modelOffset = 38;
        var gpsOffset = modelOffset + (uint)model.Length;
        w.Write((ushort)2);
        w.Write((ushort)0x0110); w.Write((ushort)2); w.Write((uint)model.Length); w.Write(modelOffset);
        w.Write((ushort)0x8825); w.Write((ushort)4); w.Write(1u); w.Write(gpsOffset);
        w.Write(0u);
        w.Write(model);
        // GPS IFD: the reference and the latitude (three rationals, right after the two entries and the pointer).
        var latitudeOffset = gpsOffset + 2 + 24 + 4;
        w.Write((ushort)2);
        w.Write((ushort)0x0001); w.Write((ushort)2); w.Write(2u); w.Write("N\0\0\0"u8);
        w.Write((ushort)0x0002); w.Write((ushort)5); w.Write(3u); w.Write(latitudeOffset);
        w.Write(0u);
        foreach (var (num, den) in new[] { (31u, 1u), (46u, 1u), (5789u, 100u) })
        {
            w.Write(num);
            w.Write(den);
        }

        w.Flush();
        var payload = "Exif\0\0"u8.ToArray().Concat(tiff.ToArray()).ToArray();
        return Segment(0xE1, payload);
    }

    public static byte[] Segment(byte marker, byte[] payload)
    {
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2, 2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);
        return segment;
    }

    /// <summary>The JPEG with extra segments inserted after SOI and bytes appended after EOI.</summary>
    public static byte[] JpegWith(byte[] jpeg, IEnumerable<byte[]> segmentsAfterSoi, byte[]? trailer = null) =>
        jpeg[..2].Concat(segmentsAfterSoi.SelectMany(s => s)).Concat(jpeg[2..]).Concat(trailer ?? []).ToArray();

    public static byte[] PngChunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(0, 4), (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length, 4), Crc32(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    /// <summary>The PNG with chunks inserted after IHDR (the first chunk).</summary>
    public static byte[] PngWith(byte[] png, IEnumerable<byte[]> chunksAfterIhdr, byte[]? trailer = null)
    {
        const int afterIhdr = 8 + 12 + 13;
        return png[..afterIhdr].Concat(chunksAfterIhdr.SelectMany(c => c)).Concat(png[afterIhdr..]).Concat(trailer ?? []).ToArray();
    }

    public static byte[] WebPChunk(string fourcc, byte[] data)
    {
        var padded = data.Length + (data.Length & 1);
        var chunk = new byte[8 + padded];
        Encoding.ASCII.GetBytes(fourcc).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4, 4), (uint)data.Length);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    /// <summary>The simple WebP rebuilt as an extended one: VP8X with the EXIF and XMP flags, an EXIF chunk, the picture, an XMP chunk.</summary>
    public static byte[] ExtendedWebP(byte[] simple, byte[] exif, byte[] xmp, byte[]? trailer = null)
    {
        var picture = simple[12..];
        // Flags (EXIF, XMP set), three reserved bytes, then the canvas width-1 and height-1 as 24-bit little-endian numbers.
        var vp8x = new byte[10];
        vp8x[0] = 0x08 | 0x04;
        vp8x[4] = 3;
        vp8x[7] = 3;
        var body = WebPChunk("VP8X", vp8x).Concat(WebPChunk("EXIF", exif)).Concat(picture).Concat(WebPChunk("XMP ", xmp)).ToArray();
        var file = new byte[12 + body.Length];
        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4, 4), (uint)(4 + body.Length));
        "WEBP"u8.CopyTo(file.AsSpan(8));
        body.CopyTo(file, 12);
        return file.Concat(trailer ?? []).ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    /// <summary>True when ffmpeg decodes the bytes without a complaint; true with a note when there is no ffmpeg to ask.</summary>
    public static bool Decodes(byte[] bytes, string extension, ITestOutputHelper output)
    {
        if (!Ffmpeg.Present)
        {
            output.WriteLine("ffmpeg is not on PATH: the decode was not exercised.");
            return true;
        }

        var path = Path.Combine(Path.GetTempPath(), "fitcheck-tests", Guid.NewGuid().ToString("N") + extension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        try
        {
            var result = Ffmpeg.Run("ffmpeg", "-v", "error", "-i", path, "-f", "null", "-");
            return result is { ExitCode: 0, Stderr: "" };
        }
        finally
        {
            File.Delete(path);
        }
    }

    public static bool Contains(byte[] haystack, string needle) => Contains(haystack, Encoding.ASCII.GetBytes(needle));

    public static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;
}

public class ImageMetadataTests(ITestOutputHelper output)
{
    [Fact]
    public void A_jpeg_loses_exif_com_and_the_trailer_and_keeps_the_picture()
    {
        var clean = MetadataSamples.TinyJpeg();
        var tagged = MetadataSamples.JpegWith(clean, [MetadataSamples.ExifApp1(), MetadataSamples.Segment(0xFE, "shot at home"u8.ToArray())], "MotionPhoto_Data\0ftypmp42 the clip"u8.ToArray());
        Assert.True(MetadataSamples.Contains(tagged, "Exif"));
        Assert.True(MetadataSamples.Contains(tagged, MetadataSamples.Device));

        var stripped = ImageMetadata.Strip(tagged, ImageFormat.Jpeg);

        Assert.False(MetadataSamples.Contains(stripped, "Exif"));
        Assert.False(MetadataSamples.Contains(stripped, MetadataSamples.Device));
        Assert.False(MetadataSamples.Contains(stripped, "shot at home"));
        Assert.False(MetadataSamples.Contains(stripped, "Lavc"));
        Assert.False(MetadataSamples.Contains(stripped, "MotionPhoto"));
        Assert.Equal(0xD9, stripped[^1]);
        // What draws the picture is byte for byte what ffmpeg wrote, minus its own COM segment (18 bytes after the 18-byte APP0).
        var expected = clean[..20].Concat(clean[38..]).ToArray();
        Assert.Equal(expected, stripped);
        Assert.True(MetadataSamples.Decodes(stripped, ".jpg", output));
    }

    [Fact]
    public void A_jpeg_keeps_its_icc_profile_and_adobe_marker_and_loses_every_other_app_segment()
    {
        var clean = MetadataSamples.TinyJpeg();
        var icc = MetadataSamples.Segment(0xE2, "ICC_PROFILE\0\x01\x01 display-p3 profile bytes"u8.ToArray());
        var mpf = MetadataSamples.Segment(0xE2, "MPF\0 an embedded preview"u8.ToArray());
        var adobe = MetadataSamples.Segment(0xEE, "Adobe\0d\0\0\0\0\x01"u8.ToArray());
        var photoshop = MetadataSamples.Segment(0xED, "Photoshop 3.0\08BIM caption"u8.ToArray());
        var xmp = MetadataSamples.Segment(0xE1, "http://ns.adobe.com/xap/1.0/\0<x:xmpmeta/>"u8.ToArray());
        var tagged = MetadataSamples.JpegWith(clean, [icc, mpf, adobe, photoshop, xmp]);

        var stripped = ImageMetadata.Strip(tagged, ImageFormat.Jpeg);

        Assert.True(MetadataSamples.Contains(stripped, "display-p3"));
        Assert.True(MetadataSamples.Contains(stripped, "Adobe"));
        Assert.False(MetadataSamples.Contains(stripped, "embedded preview"));
        Assert.False(MetadataSamples.Contains(stripped, "8BIM"));
        Assert.False(MetadataSamples.Contains(stripped, "xmpmeta"));
        Assert.True(MetadataSamples.Decodes(stripped, ".jpg", output));
    }

    [Fact]
    public void A_progressive_jpeg_with_several_scans_and_restart_markers_comes_through_whole()
    {
        // Built by hand: the walker must find the next marker after each scan without mistaking stuffed bytes or RSTn for one.
        byte[] scan1 = [0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56, 0xFF, 0x00];
        byte[] scan2 = [0x78, 0xFF, 0xD7, 0x9A];
        var file = new byte[] { 0xFF, 0xD8 }
            .Concat(MetadataSamples.ExifApp1())
            .Concat(MetadataSamples.Segment(0xDB, new byte[65]))
            .Concat(MetadataSamples.Segment(0xC2, new byte[15]))
            .Concat(MetadataSamples.Segment(0xC4, new byte[20]))
            .Concat(MetadataSamples.Segment(0xDA, new byte[10])).Concat(scan1)
            .Concat(MetadataSamples.Segment(0xC4, new byte[20]))
            .Concat(MetadataSamples.Segment(0xDA, new byte[10])).Concat(scan2)
            .Concat(new byte[] { 0xFF, 0xD9 })
            .ToArray();

        var stripped = ImageMetadata.Strip(file, ImageFormat.Jpeg);

        var expected = file[..2].Concat(file[(2 + MetadataSamples.ExifApp1().Length)..]).ToArray();
        Assert.Equal(expected, stripped);
    }

    [Fact]
    public void A_png_loses_text_time_and_exif_chunks_and_the_trailer_and_keeps_the_picture()
    {
        var clean = MetadataSamples.TinyPng();
        var tagged = MetadataSamples.PngWith(clean,
        [
            MetadataSamples.PngChunk("tEXt", "Comment\0taken in the kitchen"u8.ToArray()),
            MetadataSamples.PngChunk("eXIf", "II*\0 gps here"u8.ToArray()),
            MetadataSamples.PngChunk("tIME", new byte[7]),
            MetadataSamples.PngChunk("iTXt", "XML:com.adobe.xmp\0\0\0\0\0<x:xmpmeta/>"u8.ToArray()),
            MetadataSamples.PngChunk("zTXt", "Title\0\0x"u8.ToArray()),
            MetadataSamples.PngChunk("iCCP", "p3\0\0x"u8.ToArray()),
            MetadataSamples.PngChunk("prVt", "a private chunk"u8.ToArray())
        ], "junk after IEND"u8.ToArray());

        var stripped = ImageMetadata.Strip(tagged, ImageFormat.Png);

        Assert.False(MetadataSamples.Contains(stripped, "kitchen"));
        Assert.False(MetadataSamples.Contains(stripped, "gps here"));
        Assert.False(MetadataSamples.Contains(stripped, "tIME"));
        Assert.False(MetadataSamples.Contains(stripped, "xmpmeta"));
        Assert.False(MetadataSamples.Contains(stripped, "zTXt"));
        Assert.False(MetadataSamples.Contains(stripped, "private chunk"));
        Assert.False(MetadataSamples.Contains(stripped, "junk after"));
        Assert.True(MetadataSamples.Contains(stripped, "iCCP"));
        Assert.True(MetadataSamples.Contains(stripped, "pHYs"));
        Assert.Equal(MetadataSamples.PngWith(clean, [MetadataSamples.PngChunk("iCCP", "p3\0\0x"u8.ToArray())]), stripped);
        Assert.True(MetadataSamples.Decodes(stripped, ".png", output));
    }

    [Fact]
    public void A_webp_loses_exif_and_xmp_chunks_clears_their_flags_and_keeps_the_picture()
    {
        var simple = MetadataSamples.TinyWebP();
        var tagged = MetadataSamples.ExtendedWebP(simple, "II*\0 gps here"u8.ToArray(), "<x:xmpmeta/>"u8.ToArray(), "trailing junk"u8.ToArray());

        var stripped = ImageMetadata.Strip(tagged, ImageFormat.WebP);

        Assert.False(MetadataSamples.Contains(stripped, "EXIF"));
        Assert.False(MetadataSamples.Contains(stripped, "XMP "));
        Assert.False(MetadataSamples.Contains(stripped, "gps here"));
        Assert.False(MetadataSamples.Contains(stripped, "trailing junk"));
        Assert.True(MetadataSamples.Contains(stripped, "VP8X"));
        Assert.True(MetadataSamples.Contains(stripped, "VP8L"));
        Assert.Equal(0, stripped[20] & 0x0C);
        Assert.Equal((uint)(stripped.Length - 8), BinaryPrimitives.ReadUInt32LittleEndian(stripped.AsSpan(4, 4)));
        Assert.Equal(ImageFormat.WebP, ImageFormat.Detect(stripped));
        Assert.True(MetadataSamples.Decodes(stripped, ".webp", output));
    }

    [Fact]
    public void The_suites_fixtures_and_a_corrupt_file_come_back_as_they_are()
    {
        foreach (var (bytes, format) in new[] { (TestImages.Jpeg(), ImageFormat.Jpeg), (TestImages.Png(), ImageFormat.Png), (TestImages.WebP(), ImageFormat.WebP) })
        {
            Assert.Same(bytes, ImageMetadata.Strip(bytes, format));
        }

        var truncated = MetadataSamples.TinyJpeg()[..30];
        Assert.Same(truncated, ImageMetadata.Strip(truncated, ImageFormat.Jpeg));
        var empty = Array.Empty<byte>();
        Assert.Same(empty, ImageMetadata.Strip(empty, ImageFormat.Jpeg));
        var clean = MetadataSamples.TinyJpeg();
        // A JPEG with a COM segment still changes; one that is already clean is returned untouched.
        var stripped = ImageMetadata.Strip(clean, ImageFormat.Jpeg);
        Assert.Same(stripped, ImageMetadata.Strip(stripped, ImageFormat.Jpeg));
    }

    [Fact]
    public async Task A_check_a_comparison_and_an_avatar_are_stripped_before_the_model_and_before_the_disk()
    {
        using var app = new TestApp();
        var (client, userId, handle) = await app.NewUserAsync("exif_a");
        var tagged = MetadataSamples.JpegWith(MetadataSamples.TinyJpeg(), [MetadataSamples.ExifApp1()]);
        Assert.True(MetadataSamples.Contains(tagged, MetadataSamples.Device));

        var check = await client.PostAsync("/api/checks", TestApp.CheckForm(tagged));
        Assert.Equal(HttpStatusCode.Created, check.StatusCode);
        var request = Assert.Single(app.Vision.Requests);
        Assert.False(MetadataSamples.Contains(request.ImageBytes.ToArray(), "Exif"), "the model was handed the Exif block");
        var checkId = (await check.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var stored = await StoredCheckBytesAsync(app, checkId);
        Assert.False(MetadataSamples.Contains(stored, "Exif"), "the Exif block reached the disk");
        Assert.False(MetadataSamples.Contains(stored, MetadataSamples.Device));
        Assert.Equal(ImageFormat.Jpeg, ImageFormat.Detect(stored));

        var compare = new MultipartFormDataContent { { new StringContent("Date"), "intent" }, { new StringContent("en"), "language" } };
        compare.Add(Part(tagged), "imageA", "a.jpg");
        compare.Add(Part(MetadataSamples.PngWith(MetadataSamples.TinyPng(), [MetadataSamples.PngChunk("eXIf", "II*\0 gps here"u8.ToArray())])), "imageB", "b.png");
        app.Vision.Handler = r => r.HasSecondImage ? OutfitComparerTests.Pick() : Payloads.Ok();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/compare", compare)).StatusCode);
        var pair = app.Vision.Requests.Last();
        Assert.False(MetadataSamples.Contains(pair.ImageBytes.ToArray(), "Exif"));
        Assert.False(MetadataSamples.Contains(pair.ImageBytes2.ToArray(), "gps here"));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(app.StorageRoot, userId.ToString("N"))))
        {
            var bytes = await File.ReadAllBytesAsync(file);
            Assert.False(MetadataSamples.Contains(bytes, "Exif"), file);
            Assert.False(MetadataSamples.Contains(bytes, "gps here"), file);
        }

        var avatarForm = new MultipartFormDataContent();
        avatarForm.Add(Part(tagged), "image", "me.jpg");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/users/me/avatar", avatarForm)).StatusCode);
        var served = await app.NewClient().GetByteArrayAsync($"/api/users/{handle}/avatar");
        Assert.False(MetadataSamples.Contains(served, "Exif"));
        Assert.False(MetadataSamples.Contains(served, MetadataSamples.Device));
    }

    [Fact]
    public async Task A_clips_location_and_title_are_blanked_on_disk_and_the_clip_still_plays()
    {
        if (!Ffmpeg.Present)
        {
            output.WriteLine("ffmpeg is not on PATH: the clip strip was not exercised.");
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), "fitcheck-tests", Guid.NewGuid().ToString("N") + ".mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var made = Ffmpeg.Run("ffmpeg", "-y", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=duration=1:size=64x64:rate=10", "-c:v", "libx264", "-pix_fmt", "yuv420p",
            "-metadata", "location=+31.7683+035.2137/", "-metadata", "title=secret title", "-movflags", "+faststart", path);
        Assert.True(made is { ExitCode: 0 }, made is null ? "ffmpeg could not be started" : $"ffmpeg exit {made.Value.ExitCode}: {made.Value.Stderr} {made.Value.Stdout}");
        var clip = await File.ReadAllBytesAsync(path);
        File.Delete(path);
        // ffmpeg writes the position as a binary 3GPP "loci" box and the title as text, both under moov/udta.
        var before = FormatTags(clip);
        Assert.Contains("TAG:location=+31.7683+035.2137/", before);
        Assert.Contains("TAG:title=secret title", before);
        Assert.True(MetadataSamples.Contains(clip, "udta"));

        using var app = new TestApp();
        var (client, _, _) = await app.NewUserAsync("clip_gps");
        var check = await client.PostAsync("/api/checks", TestClips.Form(clip, fileName: "IMG_0007.MOV"));
        Assert.Equal(HttpStatusCode.Created, check.StatusCode);
        var checkId = (await check.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var stored = await StoredClipBytesAsync(app, checkId);

        Assert.Equal(clip.Length, stored.Length);
        var after = FormatTags(stored);
        Assert.DoesNotContain("location", after);
        Assert.DoesNotContain("secret title", after);
        Assert.False(MetadataSamples.Contains(stored, "secret title"), "the title survived on disk");
        Assert.False(MetadataSamples.Contains(stored, "udta"));
        Assert.False(MetadataSamples.Contains(stored, "loci"));
        Assert.Equal("h264", Ffmpeg.VideoCodec(stored));
        Assert.True(MetadataSamples.Decodes(stored, ".mp4", output));
    }

    /// <summary>The container's tags as ffprobe lists them ("TAG:name=value" lines).</summary>
    private static string FormatTags(byte[] mp4)
    {
        var path = Path.Combine(Path.GetTempPath(), "fitcheck-tests", Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllBytes(path, mp4);
        try
        {
            var result = Ffmpeg.Run("ffprobe", "-v", "error", "-show_entries", "format_tags", "-of", "default", path);
            Assert.True(result is { ExitCode: 0 }, result?.Stderr ?? "ffprobe could not be started");
            return result!.Value.Stdout;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_clip_the_walker_cannot_follow_is_left_alone()
    {
        using var app = new TestApp();
        var (client, _, _) = await app.NewUserAsync("clip_fake");
        var fake = TestClips.Mp4();
        var check = await client.PostAsync("/api/checks", TestClips.Form(fake, fileName: "fake.mp4"));
        Assert.Equal(HttpStatusCode.Created, check.StatusCode);
        var checkId = (await check.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(fake, await StoredClipBytesAsync(app, checkId));

        var webm = TestClips.WebM();
        var second = await client.PostAsync("/api/checks", TestClips.Form(webm));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(webm, await StoredClipBytesAsync(app, (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()));
    }

    private static ByteArrayContent Part(byte[] bytes)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return part;
    }

    private static async Task<byte[]> StoredCheckBytesAsync(TestApp app, Guid checkId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var path = await db.Checks.Where(c => c.Id == checkId).Select(c => c.ImagePath).SingleAsync();
        return await File.ReadAllBytesAsync(Path.Combine(app.StorageRoot, path));
    }

    private static async Task<byte[]> StoredClipBytesAsync(TestApp app, Guid checkId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var path = await db.Checks.Where(c => c.Id == checkId).Select(c => c.VideoPath).SingleAsync();
        Assert.NotNull(path);
        return await File.ReadAllBytesAsync(Path.Combine(app.StorageRoot, path!));
    }
}

// ---------- 10. Uploads ----------

public class UploadPathTests
{
    [Fact]
    public async Task Stored_names_come_from_guids_never_from_the_client()
    {
        using var app = new TestApp();
        var (client, userId, _) = await app.NewUserAsync("upload_a");
        var check = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Png(), fileName: "../../../../etc/evil.php%00.png"));
        Assert.Equal(HttpStatusCode.Created, check.StatusCode);
        var checkId = (await check.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var path = await db.Checks.Where(c => c.Id == checkId).Select(c => c.ImagePath).SingleAsync();
        Assert.Matches("^[0-9a-f]{32}/[0-9a-f]{32}\\.png$", path.Replace('\\', '/'));
        Assert.StartsWith(userId.ToString("N"), path);
        var full = Path.GetFullPath(Path.Combine(app.StorageRoot, path));
        Assert.StartsWith(Path.GetFullPath(app.StorageRoot) + Path.DirectorySeparatorChar, full);
        Assert.True(File.Exists(full));
        Assert.DoesNotContain("evil", full);
    }
}

// ---------- 11. The check that was interrupted: the one private route with no id ----------

/// <summary>
/// GET /api/checks/latest is the way back to a check whose answer never arrived (CheckRecoveryTests). It is private, and
/// it is the only private route that carries no id at all - which is why its rule is here and not in
/// <see cref="IdorEnumerationTests"/>: that test reads every route WITH a parameter and rejects, as stale, a rule for one
/// without. The rule it would have carried: "the caller's own newest check - the owner's row, or the row this browser's
/// own guest cookie made; the same 404 as a missing id for everyone else, so it says nothing about what exists."
/// </summary>
public class LatestCheckRuleTests
{
    [Fact]
    public async Task There_is_no_id_to_enumerate_and_user_B_still_gets_only_their_own()
    {
        using var app = new TestApp();
        var (a, _, _) = await app.NewUserAsync("latest_a");
        var checkA = await app.CheckAsync(a);
        var (b, _, _) = await app.NewUserAsync("latest_b");

        // It is mapped, it is a read, and there is nothing in the address a stranger could put someone else's id into.
        Assert.Contains(("/api/checks/latest", "GET"), SecurityFixtures.ApiRoutes(app));
        Assert.False(SecurityFixtures.HasParameter("/api/checks/latest"));

        // B owns nothing here, and nobody owns anything: both get the 404 of a check that does not exist, never A's.
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/checks/latest")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.NewClient().GetAsync("/api/checks/latest")).StatusCode);
        var mine = await (await a.GetAsync("/api/checks/latest")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(checkA, mine.GetProperty("id").GetGuid());
    }
}

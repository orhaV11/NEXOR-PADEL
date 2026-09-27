using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using FitCheck.Api.Services.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 20 — distribution that can be counted. The entry links (<c>GET /go/{source}</c>): one arrival per allowlisted
/// source per day, a crawler redirected but never counted, an unknown word sent to the landing page uncounted, an invite
/// carried through; the source stamped on the guest check and the signup and null when it is off the list; the launch
/// header counted on the first call only; the per-source table on the numbers page; the allowlist as a setting; and the
/// pins on the client keys, the service worker's passthrough and robots.txt.
/// </summary>
public class DistributionTests : IClassFixture<TestApp>
{
    private static readonly string WebRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot"));

    private readonly TestApp _app;

    public DistributionTests(TestApp app) => _app = app;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<long> CounterAsync(TestApp app, string name)
    {
        using var scope = app.Services.CreateScope();
        return await Counters.ReadAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), name, CancellationToken.None);
    }

    /// <summary>A browser that stops at the redirect, so the test can read Location; a following client would see the 200 of "/" and lose the fragment.</summary>
    private static HttpClient Stopping(TestApp app, bool csrf = false)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (csrf)
        {
            client.DefaultRequestHeaders.Add(Sessions.RequestHeader, Sessions.RequestHeaderValue);
        }

        return client;
    }

    private static MultipartFormDataContent CheckFormWithSource(string source)
    {
        var form = TestApp.CheckForm(TestImages.Jpeg());
        form.Add(new StringContent(source), "source");
        return form;
    }

    private static async Task<T> WithDbAsync<T>(TestApp app, Func<AppDbContext, Task<T>> read)
    {
        using var scope = app.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task An_entry_link_counts_one_arrival_per_source_and_opens_the_check_screen()
    {
        var ttBefore = await CounterAsync(_app, Funnel.SourceArrivals("tt", Today));
        var igBefore = await CounterAsync(_app, Funnel.SourceArrivals("ig", Today));
        var client = Stopping(_app);

        var response = await client.GetAsync("/go/tt");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/?src=tt#/check", response.Headers.Location!.ToString());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());

        // Case is nobody's signal: the same row.
        Assert.Equal("/?src=tt#/check", (await client.GetAsync("/go/TT")).Headers.Location!.ToString());
        Assert.Equal("/?src=ig#/check", (await client.GetAsync("/go/ig")).Headers.Location!.ToString());
        // An alias resolves to its code, so a link written the long way still lands on the same row.
        Assert.Equal("/?src=tt#/check", (await client.GetAsync("/go/tiktok")).Headers.Location!.ToString());

        Assert.Equal(ttBefore + 3, await CounterAsync(_app, Funnel.SourceArrivals("tt", Today)));
        Assert.Equal(igBefore + 1, await CounterAsync(_app, Funnel.SourceArrivals("ig", Today)));
    }

    [Fact]
    public async Task An_unknown_source_lands_on_the_landing_page_uncounted()
    {
        var client = Stopping(_app);
        var before = await WithDbAsync(_app, db => db.Counters.AsNoTracking().Where(c => c.Name.StartsWith("funnel:src:")).SumAsync(c => c.Value));

        foreach (var path in new[] { "/go/nope", "/go/tt2", "/go/%20", "/go/a-b" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Equal("/landing/", response.Headers.Location!.ToString());
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        }

        // Nothing to redirect: routing's own answer.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/go/")).StatusCode);
        Assert.Equal(before, await WithDbAsync(_app, db => db.Counters.AsNoTracking().Where(c => c.Name.StartsWith("funnel:src:")).SumAsync(c => c.Value)));
    }

    [Fact]
    public async Task A_crawler_is_redirected_but_never_counted()
    {
        var before = await CounterAsync(_app, Funnel.SourceArrivals("wa", Today));
        foreach (var agent in new[] { "facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)", "WhatsApp/2.23.20.0", "TelegramBot (like TwitterBot)", "Mozilla/5.0 (compatible; Googlebot/2.1)" })
        {
            var client = Stopping(_app);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(agent);
            var response = await client.GetAsync("/go/wa");
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Equal("/?src=wa#/check", response.Headers.Location!.ToString());
        }

        Assert.Equal(before, await CounterAsync(_app, Funnel.SourceArrivals("wa", Today)));

        // A phone is a person.
        var phone = Stopping(_app);
        phone.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 Instagram 300.0.0.0");
        Assert.Equal(HttpStatusCode.Found, (await phone.GetAsync("/go/wa")).StatusCode);
        Assert.Equal(before + 1, await CounterAsync(_app, Funnel.SourceArrivals("wa", Today)));
    }

    [Fact]
    public async Task An_entry_link_carries_an_invite_through()
    {
        var client = Stopping(_app);
        Assert.Equal("/?src=tt&via=dana#/check", (await client.GetAsync("/go/tt?via=dana")).Headers.Location!.ToString());
        // The share marker is not a person and noise is not a handle: both are dropped, the source stays.
        Assert.Equal("/?src=tt#/check", (await client.GetAsync("/go/tt?via=share")).Headers.Location!.ToString());
        Assert.Equal("/?src=tt#/check", (await client.GetAsync("/go/tt?via=not%20a%20handle")).Headers.Location!.ToString());
    }

    [Fact]
    public async Task The_source_is_stamped_on_the_guest_check_and_the_signup_and_a_bad_one_is_null()
    {
        // A fresh app, and one cookie jar per guest: a guest has one check a day, and the day's address cap is shared.
        using var app = new TestApp();
        var stamped = await app.NewClient().PostAsync("/api/checks", CheckFormWithSource("tt"));
        Assert.Equal(HttpStatusCode.Created, stamped.StatusCode);
        var stampedId = (await stamped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var bogus = await app.NewClient().PostAsync("/api/checks", CheckFormWithSource("bogus"));
        Assert.Equal(HttpStatusCode.Created, bogus.StatusCode);
        var bogusId = (await bogus.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // A signed-in check carries it too (the numbers page filters to guests, but the row says where the device came from).
        var (member, memberId, _) = await app.NewUserAsync("srcmember");
        var memberCheck = await member.PostAsync("/api/checks", CheckFormWithSource("qr"));
        Assert.Equal(HttpStatusCode.Created, memberCheck.StatusCode);

        Assert.Equal("tt", await WithDbAsync(app, db => db.Checks.AsNoTracking().Where(c => c.Id == stampedId).Select(c => c.Source).SingleAsync()));
        Assert.Null(await WithDbAsync(app, db => db.Checks.AsNoTracking().Where(c => c.Id == bogusId).Select(c => c.Source).SingleAsync()));
        Assert.Equal("qr", await WithDbAsync(app, db => db.Checks.AsNoTracking().Where(c => c.UserId == memberId).Select(c => c.Source).SingleAsync()));
        Assert.Null(await WithDbAsync(app, db => db.Users.AsNoTracking().Where(u => u.Id == memberId).Select(u => u.Source).SingleAsync()));

        async Task<string?> SignupWithSourceAsync(string handle, string source)
        {
            var response = await app.NewClient().PostAsJsonAsync("/api/auth/signup",
                new { handle, password = "password123", confirmed16Plus = true, birthDate = "1990-01-01", language = "en", accountType = "Person", source });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await WithDbAsync(app, db => db.Users.AsNoTracking().Where(u => u.Handle == handle).Select(u => u.Source).SingleAsync());
        }

        Assert.Equal("ig", await SignupWithSourceAsync("srcone", "ig"));
        Assert.Equal("ig", await SignupWithSourceAsync("srctwo", "IG "));
        // Off the list: silently null, and the signup is still a signup.
        Assert.Null(await SignupWithSourceAsync("srcthree", "unlisted"));
    }

    [Fact]
    public async Task A_home_screen_launch_is_one_tally_on_the_first_call_only()
    {
        var before = await CounterAsync(_app, Funnel.Standalone(Today));
        var client = _app.NewClient();
        client.DefaultRequestHeaders.Add(Funnel.StandaloneHeader, Funnel.StandaloneValue);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/config")).StatusCode);
        Assert.Equal(before + 1, await CounterAsync(_app, Funnel.Standalone(Today)));

        // The header on any other path is ignored: one launch, one call, one row.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/landing/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(before + 1, await CounterAsync(_app, Funnel.Standalone(Today)));

        // Without the header, a config read is a config read.
        Assert.Equal(HttpStatusCode.OK, (await _app.NewClient().GetAsync("/api/config")).StatusCode);
        var wrong = _app.NewClient();
        wrong.DefaultRequestHeaders.Add(Funnel.StandaloneHeader, "browser");
        Assert.Equal(HttpStatusCode.OK, (await wrong.GetAsync("/api/config")).StatusCode);
        Assert.Equal(before + 1, await CounterAsync(_app, Funnel.Standalone(Today)));
    }

    [Fact]
    public async Task The_numbers_page_attributes_the_walk_to_its_source()
    {
        using var app = new TestApp();
        var mod = app.NewClient();
        await app.SignupAsync(mod, "srcmod");
        await app.PromoteAsync("srcmod");

        // One browser, one cookie jar: follows the TikTok link, checks as a guest, signs up, claims, posts.
        var browser = Stopping(app, csrf: true);
        Assert.Equal("/?src=tt#/check", (await browser.GetAsync("/go/tt")).Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Created, (await browser.PostAsync("/api/checks", CheckFormWithSource("tt"))).StatusCode);
        var signup = await browser.PostAsJsonAsync("/api/auth/signup",
            new { handle = "srcwalker", password = "password123", confirmed16Plus = true, birthDate = "1990-01-01", language = "en", accountType = "Person", source = "tt" });
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync("/api/checks/claim", null)).StatusCode);
        var checkId = await app.CheckAsync(browser);
        await app.PostAsync(browser, checkId);

        var sources = (await (await mod.GetAsync("/api/metrics/pilot")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("funnel").GetProperty("sources").EnumerateArray().ToList();
        var expected = new FunnelOptions().List;
        Assert.Equal(expected, sources.Select(s => s.GetProperty("source").GetString()!).ToList());

        foreach (var row in sources)
        {
            var isTt = row.GetProperty("source").GetString() == "tt";
            Assert.Equal(isTt ? 1 : 0, row.GetProperty("arrivals").GetInt32());
            Assert.Equal(isTt ? 1 : 0, row.GetProperty("guestChecks").GetInt32());
            Assert.Equal(isTt ? 1 : 0, row.GetProperty("signups").GetInt32());
            Assert.Equal(isTt ? 1 : 0, row.GetProperty("firstPosts").GetInt32());
        }

        // Standalone: nothing until a launch says so, then one.
        async Task<int> StandaloneTodayAsync() => (await (await mod.GetAsync("/api/metrics/pilot")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("funnel").GetProperty("days").EnumerateArray().Last().GetProperty("standalone").GetInt32();
        Assert.Equal(0, await StandaloneTodayAsync());
        var installed = app.NewClient();
        installed.DefaultRequestHeaders.Add(Funnel.StandaloneHeader, Funnel.StandaloneValue);
        Assert.Equal(HttpStatusCode.OK, (await installed.GetAsync("/api/config")).StatusCode);
        Assert.Equal(1, await StandaloneTodayAsync());
    }

    [Fact]
    public async Task Funnel_sources_is_a_setting()
    {
        using var app = new TestApp { Settings = { ["Funnel:Sources:0"] = "campus", ["Funnel:Sources:1"] = "QR " } };
        var mod = app.NewClient();
        await app.SignupAsync(mod, "srcsetmod");
        await app.PromoteAsync("srcsetmod");

        var client = Stopping(app);
        Assert.Equal("/?src=campus#/check", (await client.GetAsync("/go/campus")).Headers.Location!.ToString());
        Assert.Equal("/?src=qr#/check", (await client.GetAsync("/go/qr")).Headers.Location!.ToString());
        Assert.Equal("/landing/", (await client.GetAsync("/go/tt")).Headers.Location!.ToString());

        var sources = (await (await mod.GetAsync("/api/metrics/pilot")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("funnel").GetProperty("sources").EnumerateArray().ToList();
        Assert.Equal(new[] { "campus", "qr" }, sources.Select(s => s.GetProperty("source").GetString()).ToArray());
        Assert.Equal(1, sources[0].GetProperty("arrivals").GetInt32());
        Assert.Equal(1, sources[1].GetProperty("arrivals").GetInt32());
    }

    [Fact]
    public void The_allowlist_is_normalized_and_falls_back_to_the_default()
    {
        Assert.Equal(new[] { "tt", "ig", "wa", "campus", "yt", "fb", "x", "qr", "story", "dm" }, new FunnelOptions().List);
        Assert.Equal(new[] { "tt", "x" }, new FunnelOptions { Sources = ["", "TT", "tt", "bad one", "x"] }.List);
        Assert.Equal(FunnelOptions.Defaults, new FunnelOptions { Sources = [] }.List);
        Assert.Equal(FunnelOptions.Defaults, new FunnelOptions { Sources = ["bad one", "toolongtobeasourcecode"] }.List);

        var options = new FunnelOptions();
        Assert.Equal("ig", options.Normalize("Ig "));
        Assert.Equal("ig", options.Normalize("instagram"));
        Assert.Null(options.Normalize("nope"));
        Assert.Null(options.Normalize(null));
        Assert.Null(options.Normalize(""));
        Assert.True(options.IsSource("qr"));
        Assert.False(options.IsSource("tt2"));
    }

    [Fact]
    public void The_counter_names_are_bounded_and_per_day()
    {
        var day = new DateOnly(2026, 9, 20);
        Assert.Equal("funnel:src:tt:20260920", Funnel.SourceArrivals("tt", day));
        Assert.Equal("funnel:standalone:20260920", Funnel.Standalone(day));
        Assert.Equal("X-Orevosh-Launch", Funnel.StandaloneHeader);
        // Every allowlisted source is a bounded word, so the row name is bounded by construction.
        foreach (var source in new FunnelOptions().List)
        {
            Assert.Matches("^[a-z0-9]{1,16}$", source);
        }

        // The fetchers that unfurl a pasted link, and not a phone.
        Assert.Matches(Funnel.CrawlerRegex(), "WhatsApp/2.23.20.0");
        Assert.Matches(Funnel.CrawlerRegex(), "facebookexternalhit/1.1");
        Assert.Matches(Funnel.CrawlerRegex(), "TelegramBot (like TwitterBot)");
        Assert.DoesNotMatch(Funnel.CrawlerRegex(), "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1");
    }

    private static Dictionary<string, string> Strings(string code)
    {
        var path = Path.Combine(WebRoot, "i18n", code + ".json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    private static HashSet<string> Placeholders(string text) => Regex.Matches(text, @"\{[a-z_]+\}").Select(m => m.Value).ToHashSet();

    [Fact]
    public void The_round20_client_keys_are_in_all_four_files_and_the_hebrew_share_line_is_a_challenge()
    {
        var keys = new List<string> { "inapp.title", "inapp.hint", "funnel.standalone", "funnel.sources_title", "funnel.sources_hint", "funnel.source", "funnel.source_arrivals" };
        keys.AddRange(FunnelOptions.Defaults.Select(code => "source." + code));

        var en = Strings("en");
        foreach (var code in new[] { "en", "he", "ar", "ru" })
        {
            var strings = Strings(code);
            foreach (var key in keys)
            {
                Assert.True(strings.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text), $"{code}.json is missing {key}");
                Assert.Equal(Placeholders(en[key]), Placeholders(text!));
            }

            // The copy button and the share sheet now send lookShareText; the old sentence has no caller left.
            Assert.False(strings.ContainsKey("link.share_text"), $"{code}.json still carries link.share_text");
            Assert.Equal(Placeholders(en["result.share_text"]), Placeholders(strings["result.share_text"]));
            Assert.Equal(Placeholders(en["post.share_text"]), Placeholders(strings["post.share_text"]));
        }

        var he = Strings("he");
        foreach (var key in new[] { "result.share_text", "post.share_text" })
        {
            Assert.Contains("{score}", he[key]);
            Assert.Contains("{intent}", he[key]);
            Assert.EndsWith("?", he[key]);
        }

        // Nothing here draws link.share_text any more.
        foreach (var file in Directory.EnumerateFiles(Path.Combine(WebRoot, "app"), "*.js", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("link.share_text", File.ReadAllText(file));
        }
    }

    [Fact]
    public async Task The_entry_links_carry_the_security_headers_on_the_redirect_itself()
    {
        // SecurityHeaderSetTests follows redirects and so reads the page's headers; this reads the 302's own.
        var response = await Stopping(_app).GetAsync("/go/tt");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal(SecurityHeaders.ReferrerPolicy, Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        // And no cookie: the source travels on the query and the funnel has never set one.
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public void The_service_worker_and_robots_leave_the_entry_links_to_the_server()
    {
        var sw = File.ReadAllText(Path.Combine(WebRoot, "sw.js"));
        var passthrough = sw.IndexOf("url.pathname.startsWith('/go/')", StringComparison.Ordinal);
        var shell = sw.IndexOf("new Request('/index.html'", StringComparison.Ordinal);
        Assert.True(passthrough > 0, "sw.js has no /go/ passthrough");
        Assert.True(shell > passthrough, "the /go/ passthrough must come before the shell branch");

        Assert.Contains("Disallow: /go/", File.ReadAllText(Path.Combine(WebRoot, "robots.txt")));
    }

    [Fact]
    public void The_client_keeps_the_source_the_way_it_keeps_the_invite()
    {
        var invite = File.ReadAllText(Path.Combine(WebRoot, "app", "invite.js"));
        Assert.Contains("'orevosh.source'", invite);
        Assert.Contains("export function captureSource", invite);
        Assert.Contains("export function takeSource", invite);
        Assert.Contains("captureSource();", invite);
        Assert.DoesNotContain("document.cookie", invite);

        // The check sends it while it is kept; the signup spends it.
        Assert.Contains("form.append('source', source)", File.ReadAllText(Path.Combine(WebRoot, "app", "views", "check.js")));
        Assert.Contains("source: takeSource()", File.ReadAllText(Path.Combine(WebRoot, "app", "views", "auth.js")));
        // The launch header rides the first call only.
        Assert.Contains("'X-Orevosh-Launch': 'standalone'", File.ReadAllText(Path.Combine(WebRoot, "app", "core.js")));
    }
}

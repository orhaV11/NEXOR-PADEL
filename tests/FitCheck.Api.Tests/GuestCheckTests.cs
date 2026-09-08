using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Check before signing up (Round 9). A visitor gets one check as a guest: the row carries the token of an HttpOnly guest
/// cookie instead of an owner, the guest reads it back with that cookie and nobody else can, and signing up claims it
/// (owner set, photo moved into the account's folder, cookie gone) so it can be posted. Guests are capped per cookie and
/// per client address; unclaimed rows expire with the cookie. Signed-in people are capped by their plan: three a day free,
/// thirty for Pro, comparisons included.
/// </summary>
public class GuestCheckTests : IClassFixture<GuestCheckTests.PlansApp>
{
    private const string GuestLimit = "That was your free look. Sign up to keep checking, it takes ten seconds.";

    /// <summary>The product's caps (three free, thirty Pro, one guest), with Limits:ChecksPerDay raised so Pro can reach thirty.</summary>
    public sealed class PlansApp : TestApp
    {
        public PlansApp()
        {
            FreeChecksPerDay = new PlanOptions().FreeChecksPerDay;
            ChecksPerDay = 30;
        }
    }

    /// <summary>Guests switched off (Plans:GuestChecksPerDay 0): the anonymous check path is a plain sign-in wall again.</summary>
    private sealed class NoGuestsApp : TestApp
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Plans:GuestChecksPerDay", "0");
        }
    }

    private static int _addresses;
    private readonly PlansApp _app;

    public GuestCheckTests(PlansApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    /// <summary>A fresh client address for the per-address brake, so tests never share a bucket. The app reads X-Forwarded-For (it sits behind a tunnel).</summary>
    private static string NextAddress()
    {
        var n = Interlocked.Increment(ref _addresses);
        return $"10.77.{n / 250}.{n % 250 + 1}";
    }

    /// <summary>A signed-out client from the given (or a fresh) address; its cookie jar keeps whatever the server sets.</summary>
    private HttpClient Guest(string? address = null)
    {
        var client = _app.NewClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", address ?? NextAddress());
        return client;
    }

    private static void MoveTo(HttpClient client, string address)
    {
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", address);
    }

    private static Task<HttpResponseMessage> CheckAsync(HttpClient client, string language = "en") =>
        client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), language: language));

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> ErrorAsync(HttpResponseMessage response) => (await Json(response)).GetProperty("error").GetString();

    /// <summary>The raw Set-Cookie for the guest cookie, or null when the answer did not set one.</summary>
    private static string? GuestCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(GuestChecks.CookieName + "=", StringComparison.Ordinal))
            : null;

    private static string TokenOf(string setCookie) => setCookie.Split(';')[0][(GuestChecks.CookieName.Length + 1)..];

    private static string? Attribute(string setCookie, string name) =>
        setCookie.Split(';').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..];

    private OutfitCheck Row(Guid id)
    {
        using var scope = _app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Checks.AsNoTracking().Single(c => c.Id == id);
    }

    private string StorageFile(string relative) => Path.Combine(_app.StorageRoot, relative);

    private string GuestFile(Guid checkId, string extension = "jpg") => StorageFile(Path.Combine(GuestChecks.StorageFolder.ToString("N"), $"{checkId:N}.{extension}"));

    private string UserFile(Guid userId, Guid checkId, string extension = "jpg") => StorageFile(Path.Combine(userId.ToString("N"), $"{checkId:N}.{extension}"));

    [Fact]
    public async Task A_visitors_check_is_201_with_a_guest_cookie_and_a_row_carrying_its_token()
    {
        var guest = Guest();

        var response = await CheckAsync(guest);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var check = await Json(response);
        var id = check.GetProperty("id").GetGuid();
        Assert.Equal("ok", check.GetProperty("status").GetString());
        Assert.Equal(7, check.GetProperty("score").GetInt32());

        var cookie = Assert.IsType<string>(GuestCookie(response));
        var token = TokenOf(cookie);
        Assert.True(GuestChecks.IsToken(token), cookie);
        var lower = cookie.ToLowerInvariant();
        Assert.Contains("httponly", lower);
        Assert.Contains("samesite=strict", lower);
        Assert.Contains("path=/", lower);
        Assert.DoesNotContain("secure", lower);   // plain http here, like the session cookie: Secure follows the request
        var expires = DateTimeOffset.Parse(Attribute(cookie, "expires")!);
        Assert.InRange(expires, DateTimeOffset.UtcNow.AddHours(23), DateTimeOffset.UtcNow.AddHours(25));

        var row = Row(id);
        Assert.Null(row.UserId);
        Assert.Equal(token, row.GuestToken);
        Assert.Null(row.ClaimedAt);
        Assert.Equal(CheckStatus.Ok, row.Status);
        Assert.StartsWith(GuestChecks.StorageFolder.ToString("N"), row.ImagePath);
        Assert.True(File.Exists(GuestFile(id)));

        // The photo is as private as anyone's: no route serves it.
        foreach (var url in new[] { $"/{row.ImagePath.Replace('\\', '/')}", $"/storage/{row.ImagePath.Replace('\\', '/')}", $"/api/posts/{id}/image" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(url)).StatusCode);
        }
    }

    [Fact]
    public async Task A_second_check_with_the_same_cookie_is_429_guest_limit_even_from_another_address()
    {
        var guest = Guest();
        Assert.Equal(HttpStatusCode.Created, (await CheckAsync(guest)).StatusCode);

        // A fresh address, so it is the cookie's count that refuses, not the address brake.
        MoveTo(guest, NextAddress());
        var refused = await CheckAsync(guest);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(GuestLimit, await ErrorAsync(refused));
        var retryAfter = refused.Headers.RetryAfter?.Delta;
        Assert.True(retryAfter is { } delta && delta > TimeSpan.Zero && delta <= TimeSpan.FromHours(24), $"Retry-After: {retryAfter}");
        Assert.Null(GuestCookie(refused));   // the cookie it has is the right one; nothing to set

        MoveTo(guest, NextAddress());
        var hebrew = await CheckAsync(guest, language: "he");
        Assert.Equal(HttpStatusCode.TooManyRequests, hebrew.StatusCode);
        Assert.Equal("זה היה הלוק החינמי. נרשמים כדי להמשיך לבדוק, זה לוקח עשר שניות.", await ErrorAsync(hebrew));
    }

    [Fact]
    public async Task A_second_cookie_from_the_same_address_is_429_and_a_signed_in_person_there_is_not()
    {
        var address = NextAddress();
        Assert.Equal(HttpStatusCode.Created, (await CheckAsync(Guest(address))).StatusCode);

        // No cookie at all, same address: the "guest" policy answers before any handler runs, with the same message.
        var refused = await CheckAsync(Guest(address));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(GuestLimit, await ErrorAsync(refused));
        Assert.True(refused.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Null(GuestCookie(refused));

        // The brake is for the anonymous path only: an account behind the same router checks as usual.
        var member = Guest(address);
        await _app.SignupAsync(member, "gc_same_address");
        Assert.Equal(HttpStatusCode.Created, (await CheckAsync(member)).StatusCode);
    }

    [Fact]
    public async Task A_cookie_that_is_not_one_of_ours_is_ignored_and_replaced()
    {
        var guest = Guest();
        guest.DefaultRequestHeaders.Add("Cookie", $"{GuestChecks.CookieName}=not-a-token");

        var response = await CheckAsync(guest);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cookie = Assert.IsType<string>(GuestCookie(response));
        Assert.True(GuestChecks.IsToken(TokenOf(cookie)));
        Assert.Equal(TokenOf(cookie), Row((await Json(response)).GetProperty("id").GetGuid()).GuestToken);
    }

    [Fact]
    public async Task The_guest_reads_the_check_with_the_cookie_and_nobody_else_can()
    {
        var guest = Guest();
        var id = (await Json(await CheckAsync(guest))).GetProperty("id").GetGuid();

        var mine = await guest.GetAsync($"/api/checks/{id}");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var check = await Json(mine);
        Assert.Equal(id, check.GetProperty("id").GetGuid());
        Assert.Equal("Clean casual with one weak link", check.GetProperty("feedback").GetProperty("headline").GetString());
        Assert.False(check.TryGetProperty("postId", out var postId) && postId.ValueKind != JsonValueKind.Null);

        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewClient().GetAsync($"/api/checks/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Guest().GetAsync($"/api/checks/{id}")).StatusCode);
        var (other, _, _) = await _app.NewUserAsync("gc_stranger");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/checks/{id}")).StatusCode);
    }

    [Fact]
    public async Task A_guest_cannot_post_and_cannot_claim()
    {
        var guest = Guest();
        var id = (await Json(await CheckAsync(guest))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsJsonAsync("/api/posts", new { checkId = id })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsync("/api/checks/claim", null)).StatusCode);
        Assert.Null(Row(id).UserId);
    }

    [Fact]
    public async Task Signing_up_claims_the_check_moves_its_files_and_clears_the_cookie()
    {
        var guest = Guest();
        var response = await guest.PostAsync("/api/checks", TestClips.Form(TestClips.WebM(), fileName: "look.webm"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await Json(response)).GetProperty("id").GetGuid();
        Assert.True(File.Exists(GuestFile(id)));
        Assert.True(File.Exists(GuestFile(id, "webm")));

        var me = await _app.SignupAsync(guest, "gc_keeper");
        var userId = me.GetProperty("id").GetGuid();

        var claim = await guest.PostAsync("/api/checks/claim", null);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        Assert.Equal(1, (await Json(claim)).GetProperty("claimed").GetInt32());
        var cleared = Assert.IsType<string>(GuestCookie(claim));
        Assert.Equal("", TokenOf(cleared));
        Assert.True(DateTimeOffset.Parse(Attribute(cleared, "expires")!) < DateTimeOffset.UtcNow, cleared);

        var row = Row(id);
        Assert.Equal(userId, row.UserId);
        Assert.Null(row.GuestToken);
        Assert.NotNull(row.ClaimedAt);
        Assert.Equal(Path.Combine(userId.ToString("N"), $"{id:N}.jpg"), row.ImagePath);
        Assert.Equal(Path.Combine(userId.ToString("N"), $"{id:N}.webm"), row.VideoPath);
        Assert.True(File.Exists(UserFile(userId, id)));
        Assert.True(File.Exists(UserFile(userId, id, "webm")));
        Assert.False(File.Exists(GuestFile(id)));
        Assert.False(File.Exists(GuestFile(id, "webm")));

        // It is an ordinary check now: in the history, readable, postable.
        var history = await guest.GetFromJsonAsync<JsonElement>("/api/users/me/checks");
        Assert.Contains(id, history.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/checks/{id}")).StatusCode);
        var post = await _app.PostAsync(guest, id, caption: "kept it");
        Assert.Equal(HttpStatusCode.OK, (await _app.NewClient().GetAsync($"/api/posts/{post.GetProperty("id").GetGuid()}/video")).StatusCode);

        // Nothing left to claim; the old cookie names nothing any more.
        Assert.Equal(0, (await Json(await guest.PostAsync("/api/checks/claim", null))).GetProperty("claimed").GetInt32());
        // The claimed check counts toward the account's day: two more, then the plan says no.
        Assert.Equal(HttpStatusCode.Created, (await CheckAsync(guest)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CheckAsync(guest)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await CheckAsync(guest)).StatusCode);
    }

    [Fact]
    public async Task Logging_in_claims_too_and_a_guest_check_can_still_be_read_by_its_cookie_until_then()
    {
        var (owner, ownerId, _) = await _app.NewUserAsync("gc_returning");
        var guest = Guest();
        var id = (await Json(await CheckAsync(guest))).GetProperty("id").GetGuid();

        var login = await guest.PostAsJsonAsync("/api/auth/login", new { handle = "gc_returning", password = "password123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        // Signed in with the guest cookie still there: the check reads as theirs by the cookie before the claim, and as the owner's after.
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/checks/{id}")).StatusCode);
        Assert.Equal(1, (await Json(await guest.PostAsync("/api/checks/claim", null))).GetProperty("claimed").GetInt32());
        Assert.Equal(ownerId, Row(id).UserId);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/checks/{id}")).StatusCode);
    }

    [Fact]
    public async Task The_sweeper_removes_day_old_unclaimed_rows_with_their_files_and_leaves_claimed_and_fresh_ones()
    {
        var (_, userId, _) = await _app.NewUserAsync("gc_sweep_owner");
        var old = Guid.NewGuid();
        var claimed = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        var oldComparison = Guid.NewGuid();
        var guestFolder = GuestChecks.StorageFolder.ToString("N");
        var now = DateTime.UtcNow;
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Checks.Add(new OutfitCheck
            {
                Id = old, UserId = null, GuestToken = GuestChecks.NewToken(), Intent = StyleIntent.Casual, Language = "en", Status = CheckStatus.Ok, Score = 6, PromptVersion = "v2",
                CreatedAt = now.AddHours(-25), ImagePath = Path.Combine(guestFolder, $"{old:N}.jpg"), VideoPath = Path.Combine(guestFolder, $"{old:N}.webm")
            });
            db.Checks.Add(new OutfitCheck
            {
                Id = claimed, UserId = userId, ClaimedAt = now.AddHours(-25), Intent = StyleIntent.Casual, Language = "en", Status = CheckStatus.Ok, Score = 6, PromptVersion = "v2",
                CreatedAt = now.AddHours(-26), ImagePath = Path.Combine(userId.ToString("N"), $"{claimed:N}.jpg")
            });
            db.Checks.Add(new OutfitCheck
            {
                Id = fresh, UserId = null, GuestToken = GuestChecks.NewToken(), Intent = StyleIntent.Casual, Language = "en", Status = CheckStatus.Ok, Score = 6, PromptVersion = "v2",
                CreatedAt = now.AddHours(-1), ImagePath = Path.Combine(guestFolder, $"{fresh:N}.jpg")
            });
            db.Comparisons.Add(new OutfitComparison
            {
                Id = oldComparison, UserId = null, GuestToken = GuestChecks.NewToken(), Intent = StyleIntent.Date, Language = "en", Status = CheckStatus.Ok, Winner = "a", PromptVersion = "c1",
                CreatedAt = now.AddHours(-25), ImagePathA = Path.Combine(guestFolder, $"{oldComparison:N}.jpg"), ImagePathB = Path.Combine(guestFolder, $"{Guid.NewGuid():N}.jpg")
            });
            await db.SaveChangesAsync();
        }

        string comparisonB;
        using (var scope = _app.Services.CreateScope())
        {
            comparisonB = scope.ServiceProvider.GetRequiredService<AppDbContext>().Comparisons.Single(c => c.Id == oldComparison).ImagePathB;
        }

        foreach (var relative in new[]
                 {
                     Path.Combine(guestFolder, $"{old:N}.jpg"), Path.Combine(guestFolder, $"{old:N}.webm"), Path.Combine(userId.ToString("N"), $"{claimed:N}.jpg"),
                     Path.Combine(guestFolder, $"{fresh:N}.jpg"), Path.Combine(guestFolder, $"{oldComparison:N}.jpg"), comparisonB
                 })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorageFile(relative))!);
            await File.WriteAllBytesAsync(StorageFile(relative), TestImages.Jpeg(64));
        }

        var sweeper = _app.Services.GetRequiredService<GuestCheckSweeper>();
        var (checks, comparisons) = await sweeper.SweepAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, checks);
        Assert.Equal(1, comparisons);
        Assert.False(File.Exists(GuestFile(old)));
        Assert.False(File.Exists(GuestFile(old, "webm")));
        Assert.False(File.Exists(GuestFile(oldComparison)));
        Assert.False(File.Exists(StorageFile(comparisonB)));
        Assert.True(File.Exists(UserFile(userId, claimed)));
        Assert.True(File.Exists(GuestFile(fresh)));
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null(db.Checks.Find(old));
            Assert.NotNull(db.Checks.Find(claimed));
            Assert.NotNull(db.Checks.Find(fresh));
            Assert.Null(db.Comparisons.Find(oldComparison));
        }

        // The hosted service is the same instance, registered once and started with the app.
        Assert.Contains(_app.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), s => ReferenceEquals(s, sweeper));
    }

    [Fact]
    public async Task A_free_accounts_fourth_check_in_a_day_is_429_naming_the_caps_and_comparisons_count()
    {
        var (client, _, _) = await _app.NewUserAsync("gc_free");
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await CheckAsync(client)).StatusCode);
        }

        var refused = await CheckAsync(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("That's today's 3 free checks. Go Pro for 30 a day, or come back tomorrow.", await ErrorAsync(refused));
        Assert.True(refused.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        var hebrew = await CheckAsync(client, language: "he");
        Assert.Equal("אלה 3 הבדיקות החינמיות של היום. עוברים לפרו ל-30 ביום, או חוזרים מחר.", await ErrorAsync(hebrew));

        // A comparison is a stylist call too and shares the allowance; a failed one does not count, like a failed check.
        var (comparer, comparerId, _) = await _app.NewUserAsync("gc_compares");
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Comparisons.Add(new OutfitComparison { Id = Guid.NewGuid(), UserId = comparerId, Intent = StyleIntent.Date, Language = "en", Status = CheckStatus.Ok, Winner = "b", PromptVersion = "c1", CreatedAt = DateTime.UtcNow.AddHours(-1) });
            db.Comparisons.Add(new OutfitComparison { Id = Guid.NewGuid(), UserId = comparerId, Intent = StyleIntent.Date, Language = "en", Status = CheckStatus.Error, PromptVersion = "c1", CreatedAt = DateTime.UtcNow.AddHours(-1) });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Created, (await CheckAsync(comparer)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CheckAsync(comparer)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await CheckAsync(comparer)).StatusCode);
    }

    [Fact]
    public async Task A_pro_account_gets_thirty_a_day_and_falls_back_to_free_when_it_lapses()
    {
        var (client, userId, _) = await _app.NewUserAsync("gc_pro");
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Find(userId)!;
            user.Plan = Plans.Pro;
            user.ProUntil = DateTime.UtcNow.AddDays(20);
            for (var i = 0; i < 29; i++)
            {
                db.Checks.Add(new OutfitCheck { Id = Guid.NewGuid(), UserId = userId, Intent = StyleIntent.Casual, Language = "en", Status = CheckStatus.Ok, Score = 6, PromptVersion = "v2", CreatedAt = DateTime.UtcNow.AddHours(-1) });
            }

            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Created, (await CheckAsync(client)).StatusCode);   // the thirtieth
        var refused = await CheckAsync(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("That's today's 30 free checks. Go Pro for 30 a day, or come back tomorrow.", await ErrorAsync(refused));

        // A lapsed Pro is a free account again, three a day, and the thirty already made keep the door shut.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Find(userId)!.ProUntil = DateTime.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }

        var lapsed = await CheckAsync(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, lapsed.StatusCode);
        Assert.Equal("That's today's 3 free checks. Go Pro for 30 a day, or come back tomorrow.", await ErrorAsync(lapsed));
    }

    [Fact]
    public async Task Metrics_ignore_guest_checks()
    {
        var (moderator, _, _) = await _app.NewUserAsync("gc_mod");
        Assert.Equal(AdminChange.Changed, await _app.PromoteAsync("gc_mod"));
        var before = await moderator.GetFromJsonAsync<JsonElement>("/api/metrics/pilot");

        Assert.Equal(HttpStatusCode.Created, (await CheckAsync(Guest())).StatusCode);

        var after = await moderator.GetFromJsonAsync<JsonElement>("/api/metrics/pilot");
        Assert.Equal(before.GetProperty("totalChecks").GetInt32(), after.GetProperty("totalChecks").GetInt32());
        Assert.Equal(before.GetProperty("usersWithAtLeastOneCheck").GetInt32(), after.GetProperty("usersWithAtLeastOneCheck").GetInt32());
        Assert.Equal(before.GetProperty("social").GetProperty("users").GetInt32(), after.GetProperty("social").GetProperty("users").GetInt32());
    }

    [Fact]
    public async Task Account_deletion_removes_a_claimed_guest_check_with_its_photo_and_its_post()
    {
        var guest = Guest();
        var id = (await Json(await CheckAsync(guest))).GetProperty("id").GetGuid();
        var userId = (await _app.SignupAsync(guest, "gc_gone")).GetProperty("id").GetGuid();
        Assert.Equal(1, (await Json(await guest.PostAsync("/api/checks/claim", null))).GetProperty("claimed").GetInt32());
        var postId = (await _app.PostAsync(guest, id)).GetProperty("id").GetGuid();
        Assert.True(File.Exists(UserFile(userId, id)));

        Assert.Equal(HttpStatusCode.NoContent, (await guest.DeleteAsync("/api/users/me")).StatusCode);

        Assert.False(File.Exists(UserFile(userId, id)));
        Assert.False(File.Exists(GuestFile(id)));
        Assert.False(Directory.Exists(Path.Combine(_app.StorageRoot, userId.ToString("N"))));
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewClient().GetAsync($"/api/posts/{postId}")).StatusCode);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(db.Checks.Find(id));
        Assert.Null(db.Posts.Find(postId));
        Assert.Null(db.Users.Find(userId));
    }

    [Fact]
    public async Task With_guests_switched_off_the_anonymous_check_is_a_401()
    {
        using var app = new NoGuestsApp();
        app.Vision.Handler = _ => Payloads.Ok();
        var visitor = app.NewClient();
        visitor.DefaultRequestHeaders.Add("X-Forwarded-For", NextAddress());

        var response = await CheckAsync(visitor);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Sign in to continue.", await ErrorAsync(response));
        Assert.Null(GuestCookie(response));
    }

    [Fact]
    public void The_product_defaults_are_three_free_thirty_pro_and_one_guest()
    {
        var plans = new PlanOptions();
        Assert.Equal(3, plans.FreeChecksPerDay);
        Assert.Equal(30, plans.ProChecksPerDay);
        Assert.Equal(1, plans.GuestChecksPerDay);
        Assert.Equal(TimeSpan.FromDays(1), GuestChecks.Lifetime);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Tests;

/// <summary>Account recovery by email: the address on the account, its verification link, forgot and reset, and the brakes.</summary>
public class AuthRecoveryTests : IClassFixture<TestApp>
{
    private const string TokenInvalid = "That link is not valid any more. Ask for a new one.";
    private readonly TestApp _app;
    private static int _addresses;
    private static int _pickyHandles;

    public AuthRecoveryTests(TestApp app) => _app = app;

    // Every test speaks from its own client address, so the recovery brake (a few an hour per address) never counts
    // another test's requests. The app sits behind a tunnel, so the address arrives in X-Forwarded-For.
    private static string NextAddress() => $"203.0.113.{Interlocked.Increment(ref _addresses) % 250 + 1}";

    private HttpClient Anonymous(string? address = null)
    {
        var client = _app.NewClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", address ?? NextAddress());
        return client;
    }

    private async Task<(HttpClient Client, Guid Id)> UserAsync(string handle, string password = "password123", string? address = null)
    {
        var client = Anonymous(address);
        var me = await _app.SignupAsync(client, handle, password);
        return (client, me.GetProperty("id").GetGuid());
    }

    /// <summary>Sets the address and opens the verification link: the state a person is in before they can ever forget a password.</summary>
    private async Task<HttpClient> VerifiedUserAsync(string handle, string email, string password = "password123")
    {
        var (client, _) = await UserAsync(handle, password);
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync("/api/users/me", new { email })).StatusCode);
        var token = TokenIn(Assert.Single(_app.Email.To(email)), "verify");
        Assert.Equal(HttpStatusCode.OK, (await Anonymous().PostAsJsonAsync("/api/auth/verify-email", new { token })).StatusCode);
        return client;
    }

    private static string TokenIn(EmailMessage message, string purpose)
    {
        var match = Regex.Match(message.Body, $"#/{purpose}/([A-Za-z0-9_-]+)");
        Assert.True(match.Success, $"no {purpose} link in: {message.Body}");
        return match.Groups[1].Value;
    }

    /// <summary>The reset links mailed to one address, in order, waiting for the count: forgot-password mail goes out in the background.</summary>
    private Task<List<EmailMessage>> ResetMailsAsync(string address, int count = 1, int timeoutMs = 5000) =>
        _app.Email.WaitForAsync(m => m.To == address && m.Body.Contains("#/reset/"), count, timeoutMs);

    private static Task<JsonElement> Json(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> ErrorOf(HttpResponseMessage response) => (await Json(response)).GetProperty("error").GetString()!;

    private async Task ExpireAsync(Guid userId, string purpose)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AuthTokens.Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
    }

    [Fact]
    public async Task Setting_an_address_mails_a_link_and_the_link_verifies_it_signed_out()
    {
        var (client, _) = await UserAsync("noa_mail");

        var response = await client.PatchAsJsonAsync("/api/users/me", new { email = "  Noa@Example.COM " });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await Json(response);
        Assert.Equal("noa@example.com", me.GetProperty("email").GetString());
        Assert.False(me.GetProperty("emailVerified").GetBoolean());

        var mail = Assert.Single(_app.Email.To("noa@example.com"));
        Assert.Equal("Confirm your email for OREVOSH", mail.Subject);
        Assert.Contains("@noa_mail", mail.Body);
        var token = TokenIn(mail, "verify");
        Assert.Equal(43, token.Length);
        Assert.Contains("http://localhost/#/verify/" + token, mail.Body);

        // The address is the account's own business: the public profile never carries it.
        var profile = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/users/noa_mail");
        Assert.False(profile.TryGetProperty("email", out _));

        // The link works from a browser that is not signed in, and does not sign it in.
        var stranger = Anonymous();
        var verify = await stranger.PostAsJsonAsync("/api/auth/verify-email", new { token });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var verified = await Json(verify);
        Assert.Equal("noa_mail", verified.GetProperty("handle").GetString());
        Assert.True(verified.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/auth/me")).StatusCode);

        var again = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.True(again.GetProperty("emailVerified").GetBoolean());

        // Spent on first use.
        var reuse = await Anonymous().PostAsJsonAsync("/api/auth/verify-email", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Equal(TokenInvalid, await ErrorOf(reuse));
    }

    [Fact]
    public async Task A_new_address_starts_unverified_and_the_old_link_stops_working()
    {
        var (client, _) = await UserAsync("mover");
        await client.PatchAsJsonAsync("/api/users/me", new { email = "first@example.com" });
        var first = TokenIn(Assert.Single(_app.Email.To("first@example.com")), "verify");

        var moved = await Json(await client.PatchAsJsonAsync("/api/users/me", new { email = "second@example.com" }));
        Assert.Equal("second@example.com", moved.GetProperty("email").GetString());
        Assert.False(moved.GetProperty("emailVerified").GetBoolean());
        var second = TokenIn(Assert.Single(_app.Email.To("second@example.com")), "verify");

        // The first link was issued for an address the account no longer has.
        Assert.Equal(HttpStatusCode.BadRequest, (await Anonymous().PostAsJsonAsync("/api/auth/verify-email", new { token = first })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Anonymous().PostAsJsonAsync("/api/auth/verify-email", new { token = second })).StatusCode);

        // The same address again, in other letters, is no change: still verified, no new mail.
        var same = await Json(await client.PatchAsJsonAsync("/api/users/me", new { email = "Second@Example.com", bio = "hi" }));
        Assert.True(same.GetProperty("emailVerified").GetBoolean());
        Assert.Single(_app.Email.To("second@example.com"));

        // A third address un-verifies the account again.
        var third = await Json(await client.PatchAsJsonAsync("/api/users/me", new { email = "third@example.com" }));
        Assert.False(third.GetProperty("emailVerified").GetBoolean());
        Assert.Single(_app.Email.To("third@example.com"));
    }

    [Fact]
    public async Task Clearing_the_address_drops_the_verification_and_the_open_links()
    {
        var (client, _) = await UserAsync("clearer");
        await client.PatchAsJsonAsync("/api/users/me", new { email = "clear@example.com" });
        var token = TokenIn(Assert.Single(_app.Email.To("clear@example.com")), "verify");

        // Omitted leaves it alone.
        var untouched = await Json(await client.PatchAsJsonAsync("/api/users/me", new { bio = "still here" }));
        Assert.Equal("clear@example.com", untouched.GetProperty("email").GetString());

        var cleared = await Json(await client.PatchAsJsonAsync("/api/users/me", new { email = "" }));
        Assert.False(cleared.TryGetProperty("email", out _));
        Assert.False(cleared.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await Anonymous().PostAsJsonAsync("/api/auth/verify-email", new { token })).StatusCode);

        // Nothing to send a link for any more.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/users/me/email/resend", null)).StatusCode);
    }

    [Theory]
    [InlineData("noatexample.com")]
    [InlineData("a@b")]
    [InlineData("Noa <noa@example.com>")]
    [InlineData("noa@example.com extra")]
    [InlineData("noa@@example.com")]
    [InlineData("noa@example.")]
    public async Task Refuses_what_is_not_one_address(string email)
    {
        var (client, _) = await UserAsync("picky_" + Interlocked.Increment(ref _pickyHandles));
        var response = await client.PatchAsJsonAsync("/api/users/me", new { email });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("That doesn't look like an email address.", await ErrorOf(response));
    }

    [Fact]
    public async Task Refuses_an_address_over_200_characters()
    {
        var (client, _) = await UserAsync("longmail");
        var response = await client.PatchAsJsonAsync("/api/users/me", new { email = new string('a', 190) + "@example.com" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_address_belongs_to_one_account_whatever_the_case()
    {
        var (first, _) = await UserAsync("owner_one");
        var (second, _) = await UserAsync("owner_two");
        Assert.Equal(HttpStatusCode.OK, (await first.PatchAsJsonAsync("/api/users/me", new { email = "shared@example.com" })).StatusCode);

        var taken = await second.PatchAsJsonAsync("/api/users/me", new { email = "SHARED@Example.com" });
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("That email is already on another account.", await ErrorOf(taken));
        Assert.Single(_app.Email.To("shared@example.com"));

        // Freed, it can move.
        await first.PatchAsJsonAsync("/api/users/me", new { email = "" });
        Assert.Equal(HttpStatusCode.OK, (await second.PatchAsJsonAsync("/api/users/me", new { email = "shared@example.com" })).StatusCode);
    }

    [Fact]
    public async Task Forgot_always_answers_202_and_mails_only_a_verified_address()
    {
        await VerifiedUserAsync("reset_me", "reset@example.com");
        var (unverified, _) = await UserAsync("unverified_x");
        await unverified.PatchAsJsonAsync("/api/users/me", new { email = "unv@example.com" });
        var client = Anonymous();

        async Task<HttpResponseMessage> Forgot(string handleOrEmail) => await client.PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail });

        var unknown = await Forgot("nobody_here");
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        Assert.Equal(unknownBody, await (await Forgot("unv@example.com")).Content.ReadAsStringAsync());
        Assert.Equal(unknownBody, await (await Forgot("unverified_x")).Content.ReadAsStringAsync());
        Assert.Equal(unknownBody, await (await Forgot("")).Content.ReadAsStringAsync());

        var byHandle = await Forgot("RESET_ME");
        Assert.Equal(HttpStatusCode.Accepted, byHandle.StatusCode);
        Assert.Equal(unknownBody, await byHandle.Content.ReadAsStringAsync());
        var mail = Assert.Single(await ResetMailsAsync("reset@example.com"));
        Assert.Equal("Reset your OREVOSH password", mail.Subject);
        Assert.Contains("@reset_me", mail.Body);
        Assert.Contains("http://localhost/#/reset/" + TokenIn(mail, "reset"), mail.Body);

        // By address, in any case; from another client address, since the brake is five an hour per address.
        Assert.Equal(HttpStatusCode.Accepted, (await Anonymous().PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail = "Reset@Example.com" })).StatusCode);
        Assert.Equal(2, (await ResetMailsAsync("reset@example.com", 2)).Count);

        // The unverified address got its verification link and nothing else.
        Assert.Single(_app.Email.To("unv@example.com"));
    }

    [Fact]
    public async Task Reset_changes_the_password_signs_in_and_spends_the_link()
    {
        await VerifiedUserAsync("resetter", "resetter@example.com", password: "old password1");
        await Anonymous().PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail = "resetter" });
        var token = TokenIn(Assert.Single(await ResetMailsAsync("resetter@example.com")), "reset");

        var browser = Anonymous();
        // A password the rule refuses does not spend the link.
        var tooShort = await browser.PostAsJsonAsync("/api/auth/reset", new { token, password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal("Use at least 8 characters for the password.", await ErrorOf(tooShort));

        var reset = await browser.PostAsJsonAsync("/api/auth/reset", new { token, password = "new password1" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal("resetter", (await Json(reset)).GetProperty("handle").GetString());
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous().PostAsJsonAsync("/api/auth/login", new { handle = "resetter", password = "old password1" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Anonymous().PostAsJsonAsync("/api/auth/login", new { handle = "resetter", password = "new password1" })).StatusCode);

        var reuse = await Anonymous().PostAsJsonAsync("/api/auth/reset", new { token, password = "another one1" });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Equal(TokenInvalid, await ErrorOf(reuse));
    }

    [Fact]
    public async Task Bad_tokens_are_refused_without_a_lookup()
    {
        var garbage = await Anonymous().PostAsJsonAsync("/api/auth/reset", new { token = "not-a-token", password = "long enough1" });
        Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
        Assert.Equal(TokenInvalid, await ErrorOf(garbage));
        Assert.Equal(HttpStatusCode.BadRequest, (await Anonymous().PostAsJsonAsync("/api/auth/reset", new { password = "long enough1" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Anonymous().PostAsJsonAsync("/api/auth/verify-email", new { token = "" })).StatusCode);
        // Right shape, unknown: the same answer.
        var unknown = await Anonymous().PostAsJsonAsync("/api/auth/verify-email", new { token = new string('A', 43) });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(TokenInvalid, await ErrorOf(unknown));
    }

    [Fact]
    public async Task A_newer_link_voids_the_earlier_one()
    {
        await VerifiedUserAsync("twice", "twice@example.com");
        var client = Anonymous();
        await client.PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail = "twice" });
        var first = TokenIn(Assert.Single(await ResetMailsAsync("twice@example.com")), "reset");
        await client.PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail = "twice" });
        var second = TokenIn((await ResetMailsAsync("twice@example.com", 2))[1], "reset");

        Assert.Equal(HttpStatusCode.BadRequest, (await Anonymous().PostAsJsonAsync("/api/auth/reset", new { token = first, password = "new password1" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Anonymous().PostAsJsonAsync("/api/auth/reset", new { token = second, password = "new password1" })).StatusCode);
    }

    [Fact]
    public async Task Links_expire()
    {
        var (client, id) = await UserAsync("expiry");
        await client.PatchAsJsonAsync("/api/users/me", new { email = "expiry@example.com" });
        var verify = TokenIn(Assert.Single(_app.Email.To("expiry@example.com")), "verify");
        await ExpireAsync(id, AuthTokenPurpose.Verify);
        var expired = await Anonymous().PostAsJsonAsync("/api/auth/verify-email", new { token = verify });
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Equal(TokenInvalid, await ErrorOf(expired));

        // A fresh one still works, and then a reset link expires the same way.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/users/me/email/resend", null)).StatusCode);
        var fresh = TokenIn((await _app.Email.WaitForAsync("expiry@example.com", 2))[1], "verify");
        Assert.Equal(HttpStatusCode.OK, (await Anonymous().PostAsJsonAsync("/api/auth/verify-email", new { token = fresh })).StatusCode);

        await Anonymous().PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail = "expiry" });
        var reset = TokenIn(Assert.Single(await ResetMailsAsync("expiry@example.com")), "reset");
        await ExpireAsync(id, AuthTokenPurpose.Reset);
        Assert.Equal(HttpStatusCode.BadRequest, (await Anonymous().PostAsJsonAsync("/api/auth/reset", new { token = reset, password = "new password1" })).StatusCode);
    }

    [Fact]
    public async Task A_suspended_account_cannot_reset_and_the_door_looks_the_same()
    {
        var client = await VerifiedUserAsync("banned_reset", "banned@example.com");
        var id = (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetGuid();
        await Anonymous().PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail = "banned_reset" });
        var token = TokenIn(Assert.Single(await ResetMailsAsync("banned@example.com")), "reset");

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Suspended, true));
        }

        var refused = await Anonymous().PostAsJsonAsync("/api/auth/reset", new { token, password = "new password1" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(TokenInvalid, await ErrorOf(refused));

        // And no new link is mailed while the account is suspended.
        Assert.Equal(HttpStatusCode.Accepted, (await Anonymous().PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail = "banned@example.com" })).StatusCode);
        Assert.Single(await ResetMailsAsync("banned@example.com", 2, timeoutMs: 300));
    }

    [Fact]
    public async Task Resend_issues_a_new_link_and_voids_the_old()
    {
        var (client, _) = await UserAsync("resend_me");
        await client.PatchAsJsonAsync("/api/users/me", new { email = "resend@example.com" });
        var first = TokenIn(Assert.Single(_app.Email.To("resend@example.com")), "verify");

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/users/me/email/resend", null)).StatusCode);
        var mails = _app.Email.To("resend@example.com");
        Assert.Equal(2, mails.Count);
        var second = TokenIn(mails[1], "verify");
        Assert.NotEqual(first, second);

        Assert.Equal(HttpStatusCode.BadRequest, (await Anonymous().PostAsJsonAsync("/api/auth/verify-email", new { token = first })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Anonymous().PostAsJsonAsync("/api/auth/verify-email", new { token = second })).StatusCode);

        // Verified: nothing left to confirm.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/users/me/email/resend", null)).StatusCode);
        // Signed out: the route is private.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous().PostAsync("/api/users/me/email/resend", null)).StatusCode);
    }

    [Fact]
    public async Task A_mail_server_that_fails_keeps_the_address_and_answers_502()
    {
        var (client, _) = await UserAsync("unlucky");
        _app.Email.Fail = true;
        try
        {
            var response = await client.PatchAsJsonAsync("/api/users/me", new { email = "unlucky@example.com", bio = "saved too" });
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal("We couldn't send the email. Try again in a minute.", await ErrorOf(response));

            var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
            Assert.Equal("unlucky@example.com", me.GetProperty("email").GetString());
            Assert.Equal("saved too", me.GetProperty("bio").GetString());
            Assert.Equal(HttpStatusCode.BadGateway, (await client.PostAsync("/api/users/me/email/resend", null)).StatusCode);
        }
        finally
        {
            _app.Email.Fail = false;
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/users/me/email/resend", null)).StatusCode);
        Assert.Single(_app.Email.To("unlucky@example.com"));
    }

    [Fact]
    public async Task Links_follow_the_forwarded_scheme_or_the_public_origin()
    {
        var (client, _) = await UserAsync("origin_a");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        await client.PatchAsJsonAsync("/api/users/me", new { email = "origin_a@example.com" });
        Assert.Contains("https://localhost/#/verify/", Assert.Single(_app.Email.To("origin_a@example.com")).Body);

        using var configured = _app.WithWebHostBuilder(builder => builder.UseSetting("Email:PublicOrigin", "https://looks.example.com/"));
        var other = configured.CreateClient();
        other.DefaultRequestHeaders.Add(Sessions.RequestHeader, Sessions.RequestHeaderValue);
        other.DefaultRequestHeaders.Add("X-Forwarded-For", NextAddress());
        await _app.SignupAsync(other, "origin_b");
        await other.PatchAsJsonAsync("/api/users/me", new { email = "origin_b@example.com" });
        Assert.Contains("https://looks.example.com/#/verify/", Assert.Single(_app.Email.To("origin_b@example.com")).Body);
    }

    [Fact]
    public async Task Recovery_requests_are_limited_per_client_address()
    {
        var address = NextAddress();
        var client = Anonymous(address);
        for (var i = 0; i < AuthEndpoints.RecoveryPerHourPerIpDefault; i++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail = "whoever" })).StatusCode);
        }

        var limited = await client.PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail = "whoever" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("Too many recovery requests. Try again in an hour.", await ErrorOf(limited));

        // The resend route shares the bucket.
        var (signedIn, _) = await UserAsync("limited_user", address: address);
        await signedIn.PatchAsJsonAsync("/api/users/me", new { email = "limited@example.com" });
        var resend = await signedIn.PostAsync("/api/users/me/email/resend", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, resend.StatusCode);
        Assert.Equal("Too many recovery requests. Try again in an hour.", await ErrorOf(resend));

        // Another address has its own bucket.
        Assert.Equal(HttpStatusCode.Accepted, (await Anonymous().PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail = "whoever" })).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_account_removes_its_links()
    {
        var (client, id) = await UserAsync("leaver");
        await client.PatchAsJsonAsync("/api/users/me", new { email = "leaver@example.com" });
        var token = TokenIn(Assert.Single(_app.Email.To("leaver@example.com")), "verify");
        using (var scope = _app.Services.CreateScope())
        {
            Assert.Single(scope.ServiceProvider.GetRequiredService<AppDbContext>().AuthTokens.Where(t => t.UserId == id));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/users/me")).StatusCode);

        using (var scope = _app.Services.CreateScope())
        {
            Assert.Empty(scope.ServiceProvider.GetRequiredService<AppDbContext>().AuthTokens.Where(t => t.UserId == id));
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await Anonymous().PostAsJsonAsync("/api/auth/verify-email", new { token })).StatusCode);
        // The address is free for the next account.
        var (next, _) = await UserAsync("leaver2");
        Assert.Equal(HttpStatusCode.OK, (await next.PatchAsJsonAsync("/api/users/me", new { email = "leaver@example.com" })).StatusCode);
    }

    [Fact]
    public async Task Config_says_mail_is_on()
    {
        var config = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/config");
        Assert.True(config.GetProperty("email").GetBoolean());
    }

    [Fact]
    public async Task With_mail_off_addresses_are_refused_and_forgot_still_answers_202()
    {
        using var app = new TestApp { EmailEnabled = false };
        var config = await app.NewClient().GetFromJsonAsync<JsonElement>("/api/config");
        Assert.False(config.GetProperty("email").GetBoolean());

        var (client, _, _) = await app.NewUserAsync("offline");
        var refused = await client.PatchAsJsonAsync("/api/users/me", new { email = "offline@example.com" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("Email isn't set up on this server, so there is nothing to send.", await ErrorOf(refused));
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).TryGetProperty("email", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/users/me/email/resend", null)).StatusCode);

        // Clearing and leaving alone still work, so the rest of the profile form is unaffected.
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync("/api/users/me", new { email = "", bio = "fine" })).StatusCode);

        Assert.Equal(HttpStatusCode.Accepted, (await app.NewClient().PostAsJsonAsync("/api/auth/forgot", new { handleOrEmail = "offline" })).StatusCode);
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task Password_reset_mail_speaks_the_accounts_language()
    {
        var client = Anonymous();
        await _app.SignupAsync(client, "dana_he", language: "he");
        await client.PatchAsJsonAsync("/api/users/me", new { email = "dana@example.com" });
        var verify = Assert.Single(_app.Email.To("dana@example.com"));
        Assert.Equal("אישור המייל שלך ב-OREVOSH", verify.Subject);
        Assert.Contains("@dana_he", verify.Body);
    }
}

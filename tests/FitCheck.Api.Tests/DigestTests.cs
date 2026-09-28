using System.Net;
using System.Net.Http.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 13 — the growth loop: the week comes back by mail. The Sunday send and what it says, the guard that makes a
/// second run silent, the signed unsubscribe link, and the account with nothing to say that gets nothing.
/// <para>
/// The clock is the fake one and the board's zone is UTC, so "Sunday morning" is one instant a test can name; the rows
/// the week is measured over are stamped by hand, since checks, posts, fires and comments are written with the wall
/// clock and a test must not wait a week for them.
/// </para>
/// </summary>
public class DigestTests
{
    /// <summary>Sunday, an hour after the send hour: the moment a digest is due.</summary>
    private static readonly DateTime SundayMorning = new(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>The instant the week's rows are stamped with: inside [Sunday-7d 09:00, Sunday 09:00).</summary>
    private static readonly DateTime InTheWeek = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Well before the window, so nothing in a fixture is mistaken for a new account.</summary>
    private static readonly DateTime LongBefore = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private const string DigestSubject = "Your week on OREVOSH";
    private const string WelcomeSubject = "Welcome to OREVOSH";

    /// <summary>
    /// A server with mail on, a public origin (without one no link can be built and nothing is sent), the board's week
    /// in UTC with its anti-gaming rules off, and a clock parked on a Wednesday so the worker's own first pass at start
    /// does nothing until a test moves it.
    /// </summary>
    private static TestApp NewApp()
    {
        var app = new TestApp
        {
            Settings = new()
            {
                ["Email:PublicOrigin"] = "https://looks.test",
                ["Board:TimeZone"] = "UTC",
                ["Board:MinChecksToCount"] = "0",
                ["Board:NewAccountDays"] = "0",
                ["Board:CacheSeconds"] = "0",
                ["Digest:Hour"] = "9"
            }
        };
        app.Clock.Now = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);   // a Wednesday: nothing is due
        return app;
    }

    private static Digest Worker(TestApp app) => app.Services.GetRequiredService<Digest>();

    private static async Task WithDbAsync(TestApp app, Func<AppDbContext, Task> action)
    {
        using var scope = app.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>An account with a confirmed address, the weekly mail on, and an age that is not a new signup's.</summary>
    private static async Task<(HttpClient Client, Guid Id)> SubscriberAsync(TestApp app, string handle, string address, DateTime? createdAt = null)
    {
        var (client, id, _) = await app.NewUserAsync(handle);
        await WithDbAsync(app, async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == id);
            user.Email = address;
            user.EmailVerifiedAt = LongBefore;
            user.CreatedAt = createdAt ?? LongBefore;
            user.DigestOn = true;
            await db.SaveChangesAsync();
        });
        return (client, id);
    }

    /// <summary>Puts every row of this look's story at one instant, so a test can say which week it belongs to.</summary>
    private static Task BackdateAsync(TestApp app, Guid postId, DateTime? at = null) => WithDbAsync(app, async db =>
    {
        var when = at ?? InTheWeek;
        var post = await db.Posts.FirstAsync(p => p.Id == postId);
        post.CreatedAt = when;
        (await db.Checks.FirstAsync(c => c.Id == post.CheckId)).CreatedAt = when;
        foreach (var fire in await db.Fires.Where(f => f.PostId == postId).ToListAsync())
        {
            fire.CreatedAt = when;
        }

        foreach (var comment in await db.Comments.Where(c => c.PostId == postId).ToListAsync())
        {
            comment.CreatedAt = when;
        }

        await db.SaveChangesAsync();
    });

    private static EmailMessage? WeeklyMail(TestApp app, string address) =>
        app.Email.To(address).LastOrDefault(m => m.Subject == DigestSubject);

    private static List<EmailMessage> Welcomes(TestApp app, string address) =>
        app.Email.To(address).Where(m => m.Subject == WelcomeSubject).ToList();

    private static string LinkIn(string body, string prefix) =>
        body.Split('\n', ' ').First(part => part.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public async Task Sunday_morning_the_week_goes_out_with_its_numbers_the_top_look_and_the_unsubscribe()
    {
        using var app = NewApp();
        var (author, _) = await SubscriberAsync(app, "digestauthor", "author@digest.test");
        var (friend, _) = await SubscriberAsync(app, "digestfriend", "friend@digest.test");

        var postId = await app.CheckAndPostAsync(author);
        Assert.Equal(HttpStatusCode.OK, (await friend.PostAsync($"/api/posts/{postId}/fire", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await friend.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "this works" })).StatusCode);
        await BackdateAsync(app, postId);

        app.Clock.Now = SundayMorning;
        var run = await Worker(app).RunAsync(CancellationToken.None);
        Assert.Equal(1, run.Digests);   // the friend made no look and no check of their own: nothing to say

        var mail = WeeklyMail(app, "author@digest.test");
        Assert.NotNull(mail);
        Assert.Contains("1 fires and 1 comments", mail.Body);
        Assert.Contains($"https://looks.test/look/{postId}", mail.Body);   // the week's top look on the board
        Assert.Contains("https://looks.test/#/check", mail.Body);
        Assert.Contains("https://looks.test/digest/off/", mail.Body);
        Assert.Null(WeeklyMail(app, "friend@digest.test"));

        // Stamped, so the guard has something to read.
        await WithDbAsync(app, async db =>
            Assert.Equal(SundayMorning, await db.Users.Where(u => u.Email == "author@digest.test").Select(u => u.LastDigestAt).FirstAsync()));
    }

    [Fact]
    public async Task A_second_run_in_the_same_window_sends_nothing_and_a_weekday_sends_nothing_at_all()
    {
        using var app = NewApp();
        var (author, _) = await SubscriberAsync(app, "guardauthor", "guard@digest.test");
        await BackdateAsync(app, await app.CheckAndPostAsync(author));

        app.Clock.Now = SundayMorning;
        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Digests);

        // A restart an hour later (the worker wakes hourly) must not write to the same inbox twice.
        app.Clock.Now = SundayMorning.AddHours(1);
        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Digests);
        Assert.Single(app.Email.To("guard@digest.test"), m => m.Subject == DigestSubject);

        // And the middle of the week is not a send at all, whatever the guard says.
        Assert.Null(Worker(app).DueAtUtc(new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc), Worker(app).DueAtUtc(SundayMorning)!.Value);
        // Sunday before the hour belongs to the week before, which is long past: nothing is due.
        Assert.Null(Worker(app).DueAtUtc(new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task An_account_with_nothing_to_say_gets_no_mail()
    {
        using var app = NewApp();
        await SubscriberAsync(app, "quietone", "quiet@digest.test");

        app.Clock.Now = SundayMorning;
        var run = await Worker(app).RunAsync(CancellationToken.None);
        Assert.Equal(0, run.Digests);
        Assert.Equal(1, run.Skipped);
        Assert.Empty(app.Email.To("quiet@digest.test"));
    }

    [Fact]
    public async Task An_account_that_only_checked_still_hears_from_us()
    {
        using var app = NewApp();
        var (client, _) = await SubscriberAsync(app, "checkeronly", "checker@digest.test");
        var checkId = await app.CheckAsync(client);
        await WithDbAsync(app, async db =>
        {
            (await db.Checks.FirstAsync(c => c.Id == checkId)).CreatedAt = InTheWeek;
            await db.SaveChangesAsync();
        });

        app.Clock.Now = SundayMorning;
        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Digests);
        var mail = WeeklyMail(app, "checker@digest.test");
        Assert.NotNull(mail);
        Assert.Contains("0 fires and 0 comments", mail.Body);
    }

    [Fact]
    public async Task The_unsubscribe_link_needs_no_login_turns_off_one_account_and_stops_the_mail()
    {
        using var app = NewApp();
        var (author, id) = await SubscriberAsync(app, "offauthor", "off@digest.test");
        var (_, otherId) = await SubscriberAsync(app, "otherauthor", "other@digest.test");
        await BackdateAsync(app, await app.CheckAndPostAsync(author));

        app.Clock.Now = SundayMorning;
        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Digests);
        var mail = WeeklyMail(app, "off@digest.test");
        Assert.NotNull(mail);
        var path = LinkIn(mail.Body, "https://looks.test/digest/off/")["https://looks.test".Length..];

        // A signed-out browser, no CSRF header, one GET: the mail is off.
        var page = await app.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("The weekly email is off", html);
        Assert.Contains("noindex", html);

        await WithDbAsync(app, async db =>
        {
            Assert.False(await db.Users.Where(u => u.Id == id).Select(u => u.DigestOn).FirstAsync());
            Assert.True(await db.Users.Where(u => u.Id == otherId).Select(u => u.DigestOn).FirstAsync());
        });

        // A week later, with a fresh look inside that week, that inbox still hears nothing.
        var next = SundayMorning.AddDays(7);
        await BackdateAsync(app, await app.CheckAndPostAsync(author), next.AddDays(-3));
        app.Clock.Now = next;
        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Digests);
    }

    [Fact]
    public async Task A_forged_or_swapped_token_turns_nobody_off()
    {
        using var app = NewApp();
        var (_, mine) = await SubscriberAsync(app, "tokenmine", "mine@digest.test");
        var (_, theirs) = await SubscriberAsync(app, "tokentheirs", "theirs@digest.test");

        var tokens = app.Services.GetRequiredService<DigestTokens>();
        string mineToken = "", theirsToken = "";
        await WithDbAsync(app, async db =>
        {
            mineToken = tokens.For(await db.Users.FirstAsync(u => u.Id == mine));
            theirsToken = tokens.For(await db.Users.FirstAsync(u => u.Id == theirs));
        });
        Assert.NotEqual(mineToken, theirsToken);

        // One account's signature on another's id, a tag of the wrong length, an id with no tag, and plain nonsense.
        foreach (var token in new[]
        {
            $"{theirs:N}." + mineToken.Split('.')[1],
            mineToken + "A",
            mineToken.Split('.')[0],
            "nonsense",
            $"{Guid.NewGuid():N}.abcdef"
        })
        {
            var page = await app.CreateClient().GetAsync("/digest/off/" + Uri.EscapeDataString(token));
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            // "That link didn&#39;t work": the page escapes everything it prints, the apostrophe included.
            Assert.Contains("That link didn&#39;t work", await page.Content.ReadAsStringAsync());
        }

        await WithDbAsync(app, async db =>
        {
            Assert.True(await db.Users.Where(u => u.Id == mine).Select(u => u.DigestOn).FirstAsync());
            Assert.True(await db.Users.Where(u => u.Id == theirs).Select(u => u.DigestOn).FirstAsync());
        });

        // The account's own token still works.
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient().GetAsync("/digest/off/" + mineToken)).StatusCode);
        await WithDbAsync(app, async db => Assert.False(await db.Users.Where(u => u.Id == mine).Select(u => u.DigestOn).FirstAsync()));
    }

    [Fact]
    public async Task A_configured_secret_keys_the_links_instead_of_the_password_hash()
    {
        using var app = new TestApp { Settings = new() { ["Digest:Secret"] = "a-long-server-secret-nobody-else-has" } };
        var (_, userId, _) = await app.NewUserAsync("secrethandle");

        var tokens = app.Services.GetRequiredService<DigestTokens>();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        var token = tokens.For(user);
        Assert.Equal(user.Id, (await tokens.FindAsync(db, token, CancellationToken.None))?.Id);

        // A password reset does not void the link when the key is the server's own secret.
        user.PasswordHash = "a-completely-different-hash";
        await db.SaveChangesAsync();
        Assert.Equal(user.Id, (await tokens.FindAsync(db, token, CancellationToken.None))?.Id);
    }

    [Fact]
    public async Task A_new_account_with_a_confirmed_address_gets_one_welcome_and_only_one()
    {
        using var app = NewApp();
        await SubscriberAsync(app, "welcomed", "welcome@digest.test", createdAt: SundayMorning.AddDays(-1));

        app.Clock.Now = SundayMorning;
        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Welcomes);
        var welcome = Assert.Single(Welcomes(app, "welcome@digest.test"));
        Assert.Contains("https://looks.test/#/check", welcome.Body);
        Assert.Contains("https://looks.test/digest/off/", welcome.Body);
        // Three lines and one thing to tap: the greeting, what OREVOSH is, the call to action, the way out.
        Assert.True(welcome.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length <= 5);

        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Welcomes);
        Assert.Single(Welcomes(app, "welcome@digest.test"));
    }

    [Fact]
    public async Task An_address_nobody_confirmed_is_never_written_to()
    {
        using var app = NewApp();
        var (_, id, _) = await app.NewUserAsync("unconfirmed");
        await WithDbAsync(app, async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == id);
            user.Email = "someone.elses@digest.test";
            user.EmailVerifiedAt = null;
            user.CreatedAt = SundayMorning.AddDays(-1);
            await db.SaveChangesAsync();
        });

        app.Clock.Now = SundayMorning;
        var run = await Worker(app).RunAsync(CancellationToken.None);
        Assert.Equal(0, run.Welcomes);
        Assert.Equal(0, run.Digests);
        Assert.Empty(app.Email.To("someone.elses@digest.test"));
    }

    [Fact]
    public async Task With_no_public_origin_nothing_is_sent_at_all()
    {
        using var app = new TestApp();   // mail on, but no Email:PublicOrigin
        app.Clock.Now = SundayMorning;
        var (_, id, _) = await app.NewUserAsync("noorigin");
        await WithDbAsync(app, async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == id);
            user.Email = "noorigin@digest.test";
            user.EmailVerifiedAt = LongBefore;
            await db.SaveChangesAsync();
        });

        var run = await Worker(app).RunAsync(CancellationToken.None);
        Assert.Equal(0, run.Digests);
        Assert.Equal(0, run.Welcomes);
        Assert.Empty(app.Email.To("noorigin@digest.test"));
    }

    [Fact]
    public async Task With_mail_off_the_worker_is_silent()
    {
        using var app = new TestApp { EmailEnabled = false, Settings = new() { ["Email:PublicOrigin"] = "https://looks.test" } };
        app.Clock.Now = SundayMorning;
        var run = await Worker(app).RunAsync(CancellationToken.None);
        Assert.Equal(0, run.Digests);
        Assert.Equal(0, run.Welcomes);
        Assert.Empty(app.Email.Sent);
        await Task.CompletedTask;
    }
}

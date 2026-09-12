using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>"Today's look": the prompt of the day and GET /api/today, the looks posted with its hashtag today.</summary>
public class TodayTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public TodayTests(TestApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static List<Guid> Ids(JsonElement list) => list.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();

    private async Task UpdatePostAsync(Guid postId, Action<Post> change)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var post = await db.Posts.SingleAsync(p => p.Id == postId);
        change(post);
        await db.SaveChangesAsync();
    }

    [Fact]
    public void The_prompt_of_the_day_is_stable_for_a_date_and_changes_from_one_day_to_the_next()
    {
        Assert.Equal(30, DailyPrompts.All.Count);

        var day = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);
        Assert.Same(DailyPrompts.For(day), DailyPrompts.For(day.AddHours(23).AddMinutes(59)));
        Assert.Same(DailyPrompts.For(day), DailyPrompts.For(day.AddSeconds(1)));

        // Consecutive days never repeat, the year's end included, and a month of days walks the whole list.
        var start = new DateTime(2026, 12, 15, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 40; i++)
        {
            Assert.NotSame(DailyPrompts.For(start.AddDays(i)), DailyPrompts.For(start.AddDays(i + 1)));
        }

        var month = Enumerable.Range(0, 30).Select(i => DailyPrompts.For(day.AddDays(i)).Tag).ToHashSet();
        Assert.Equal(30, month.Count);
        Assert.Equal(DateTime.UtcNow.Date, DailyPrompts.DayOf(DateTime.UtcNow));
        Assert.Equal(DateTimeKind.Utc, DailyPrompts.DayOf(DateTime.UtcNow).Kind);
    }

    [Fact]
    public void Every_prompt_has_a_hashtag_the_caption_parser_accepts_and_every_language()
    {
        var tags = DailyPrompts.All.Select(p => p.Tag).ToList();
        Assert.Equal(tags.Count, tags.Distinct(StringComparer.Ordinal).Count());
        foreach (var prompt in DailyPrompts.All)
        {
            Assert.Matches("^[a-z0-9_]{2,30}$", prompt.Tag);
            Assert.Equal([prompt.Tag], CaptionParser.Tags("#" + prompt.Tag + " today"));
            // Every shipped locale has its own title and hint, in its own script, and the picker returns exactly it.
            foreach (var (locale, title, hint, script) in new[]
            {
                ("en", prompt.TitleEn, prompt.HintEn, @"\p{IsBasicLatin}"),
                ("he", prompt.TitleHe, prompt.HintHe, @"\p{IsHebrew}"),
                ("ar", prompt.TitleAr, prompt.HintAr, @"\p{IsArabic}"),
                ("ru", prompt.TitleRu, prompt.HintRu, @"\p{IsCyrillic}"),
            })
            {
                Assert.False(string.IsNullOrWhiteSpace(title), $"{prompt.Tag}: no {locale} title");
                Assert.False(string.IsNullOrWhiteSpace(hint), $"{prompt.Tag}: no {locale} hint");
                Assert.Matches(new Regex(script), title);
                Assert.Matches(new Regex(script), hint);
                Assert.DoesNotContain('!', title + hint);
                Assert.Equal(title, prompt.Title(locale));
                Assert.Equal(hint, prompt.Hint(locale));
            }

            // The four are four different lines, not one copied around.
            Assert.Equal(4, new[] { prompt.TitleEn, prompt.TitleHe, prompt.TitleAr, prompt.TitleRu }.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(4, new[] { prompt.HintEn, prompt.HintHe, prompt.HintAr, prompt.HintRu }.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(prompt.HintEn, prompt.Hint("fr"));
            Assert.Equal(prompt.TitleEn, prompt.Title(""));
            Assert.Equal(prompt.TitleRu, prompt.Title("RU"));
            Assert.True(prompt.Intent is null || Enum.IsDefined(prompt.Intent.Value));
        }
    }

    [Fact]
    public async Task Today_lists_only_todays_visible_looks_with_the_tag_and_says_whether_the_caller_posted()
    {
        var prompt = DailyPrompts.For(DateTime.UtcNow);
        var (a, _, _) = await _app.NewUserAsync("today_a");
        var (b, _, _) = await _app.NewUserAsync("today_b");
        var (c, _, _) = await _app.NewUserAsync("today_c");
        var (d, _, _) = await _app.NewUserAsync("today_d");

        var mine = await _app.CheckAndPostAsync(a, intent: "Minimal", caption: $"#{prompt.Tag} head to toe");
        await _app.CheckAndPostAsync(b, caption: "no tag today");
        var hidden = await _app.CheckAndPostAsync(c, caption: $"#{prompt.Tag} reported");
        await UpdatePostAsync(hidden, p => p.Hidden = true);
        var yesterday = await _app.CheckAndPostAsync(d, caption: $"#{prompt.Tag} late");
        await UpdatePostAsync(yesterday, p => p.CreatedAt = DateTime.UtcNow.AddDays(-1));

        var anonymous = await Json(await _app.NewClient().GetAsync("/api/today"));
        Assert.Equal(prompt.Tag, anonymous.GetProperty("tag").GetString());
        Assert.Equal(prompt.TitleEn, anonymous.GetProperty("title").GetString());
        Assert.Equal(prompt.HintEn, anonymous.GetProperty("hint").GetString());
        Assert.Equal(DateTime.UtcNow.Date, anonymous.GetProperty("date").GetDateTime().ToUniversalTime());
        Assert.Equal([mine], Ids(anonymous.GetProperty("posts")));
        Assert.False(anonymous.GetProperty("posted").GetBoolean());
        if (prompt.Intent is StyleIntent intent)
        {
            Assert.Equal(intent.ToString(), anonymous.GetProperty("intent").GetString());
        }
        else
        {
            Assert.False(anonymous.TryGetProperty("intent", out _));
        }

        var look = anonymous.GetProperty("posts")[0];
        Assert.Equal("today_a", look.GetProperty("user").GetProperty("handle").GetString());
        Assert.Equal($"/api/posts/{mine}/image", look.GetProperty("imageUrl").GetString());
        Assert.Contains(prompt.Tag, look.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));

        Assert.True((await Json(await a.GetAsync("/api/today"))).GetProperty("posted").GetBoolean());
        Assert.False((await Json(await b.GetAsync("/api/today"))).GetProperty("posted").GetBoolean());
        Assert.False((await Json(await d.GetAsync("/api/today"))).GetProperty("posted").GetBoolean());
        // The look under review is off the list for everyone, but its owner did post today.
        var asC = await Json(await c.GetAsync("/api/today"));
        Assert.Equal([mine], Ids(asC.GetProperty("posts")));
        Assert.True(asC.GetProperty("posted").GetBoolean());
    }

    [Fact]
    public async Task The_prompt_speaks_the_callers_language()
    {
        var prompt = DailyPrompts.For(DateTime.UtcNow);
        var (hebrew, _, _) = await _app.NewUserAsync("today_he", language: "he");
        var signedIn = await Json(await hebrew.GetAsync("/api/today"));
        Assert.Equal(prompt.TitleHe, signedIn.GetProperty("title").GetString());
        Assert.Equal(prompt.HintHe, signedIn.GetProperty("hint").GetString());

        var visitor = _app.NewClient();
        visitor.DefaultRequestHeaders.AcceptLanguage.ParseAdd("he-IL");
        var anonymous = await Json(await visitor.GetAsync("/api/today"));
        Assert.Equal(prompt.TitleHe, anonymous.GetProperty("title").GetString());
        Assert.Equal(prompt.Tag, anonymous.GetProperty("tag").GetString());
    }

    [Theory]
    [InlineData("ar", "ar-EG")]
    [InlineData("ru", "ru-RU")]
    public async Task The_prompt_speaks_arabic_and_russian_too(string language, string acceptLanguage)
    {
        var prompt = DailyPrompts.For(DateTime.UtcNow);
        var (client, _, _) = await _app.NewUserAsync("today_" + language, language: language);
        var signedIn = await Json(await client.GetAsync("/api/today"));
        Assert.Equal(prompt.Title(language), signedIn.GetProperty("title").GetString());
        Assert.Equal(prompt.Hint(language), signedIn.GetProperty("hint").GetString());
        Assert.NotEqual(prompt.TitleEn, signedIn.GetProperty("title").GetString());

        var visitor = _app.NewClient();
        visitor.DefaultRequestHeaders.AcceptLanguage.ParseAdd(acceptLanguage);
        var anonymous = await Json(await visitor.GetAsync("/api/today"));
        Assert.Equal(prompt.Title(language), anonymous.GetProperty("title").GetString());
        Assert.Equal(prompt.Hint(language), anonymous.GetProperty("hint").GetString());
    }
}

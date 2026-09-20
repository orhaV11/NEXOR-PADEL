using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 13, the verdict's own verdict: POST /api/checks/{id}/useful stores whether the tip landed (with an optional note)
/// on the check, for its owner or the guest whose cookie made it; the check and the export carry it; the numbers page
/// shows the rate overall, by intent and by language. And the honest no-outfit answer: the rubric names the cases, the
/// model's reason is dropped when it mentions a person, the generic line takes its place, and a no-outfit answer within
/// Plans:NoOutfitForgivenPerDay spends nobody's allowance while the global ceiling still counts it.
/// </summary>
public class FeedbackTests
{
    private static async Task<string?> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();

    private static Task<HttpResponseMessage> UsefulAsync(HttpClient client, Guid checkId, object body) =>
        client.PostAsJsonAsync($"/api/checks/{checkId}/useful", body);

    [Fact]
    public async Task The_owner_says_whether_the_tip_landed_and_may_change_their_mind()
    {
        using var app = new TestApp();
        app.Clock.Now = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
        var (owner, ownerId, _) = await app.NewUserAsync("useful_owner");
        var checkId = await app.CheckAsync(owner);

        // Nothing said yet: the check carries no verdict.
        var before = await owner.GetFromJsonAsync<JsonElement>($"/api/checks/{checkId}");
        Assert.False(before.TryGetProperty("useful", out _));
        Assert.False(before.TryGetProperty("usefulAt", out _));

        var yes = await UsefulAsync(owner, checkId, new { useful = true, note = "  Tucked it,  looked sharper.  " });
        Assert.Equal(HttpStatusCode.OK, yes.StatusCode);
        var stored = await yes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(stored.GetProperty("useful").GetBoolean());
        Assert.Equal("Tucked it, looked sharper.", stored.GetProperty("note").GetString());
        Assert.Equal(app.Clock.Now, stored.GetProperty("usefulAt").GetDateTime().ToUniversalTime());

        var after = await owner.GetFromJsonAsync<JsonElement>($"/api/checks/{checkId}");
        Assert.True(after.GetProperty("useful").GetBoolean());
        Assert.Equal("Tucked it, looked sharper.", after.GetProperty("usefulNote").GetString());
        Assert.Equal(app.Clock.Now, after.GetProperty("usefulAt").GetDateTime().ToUniversalTime());
        // counted is the check route's own word on the day's allowance; a read says nothing about it.
        Assert.False(after.TryGetProperty("counted", out _));

        // Changed their mind, and the note is optional: the row is overwritten, the note cleared.
        app.Clock.Now = app.Clock.Now.Value.AddMinutes(5);
        var no = await UsefulAsync(owner, checkId, new { useful = false });
        Assert.Equal(HttpStatusCode.OK, no.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var row = scope.ServiceProvider.GetRequiredService<AppDbContext>().Checks.Single(c => c.Id == checkId);
            Assert.Equal(ownerId, row.UserId);
            Assert.False(row.Useful);
            Assert.Null(row.UsefulNote);
            Assert.Equal(app.Clock.Now, row.UsefulAt);
        }

        // The person's own list of checks carries the verdict too.
        var mine = await owner.GetFromJsonAsync<JsonElement>("/api/users/me/checks");
        Assert.False(mine.EnumerateArray().Single().GetProperty("useful").GetBoolean());
    }

    [Fact]
    public async Task The_rules_a_note_too_long_a_missing_verdict_and_a_check_without_a_tip()
    {
        using var app = new TestApp();
        var (owner, _, _) = await app.NewUserAsync("useful_rules", language: "he");
        var checkId = await app.CheckAsync(owner, language: "he");

        var missing = await UsefulAsync(owner, checkId, new { note = "no verdict" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("צריך לומר אם הטיפ קלע: כן או לא.", await ErrorOf(missing));

        var empty = await owner.PostAsync($"/api/checks/{checkId}/useful", new StringContent("", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var tooLong = await UsefulAsync(owner, checkId, new { useful = true, note = new string('x', FeedbackEndpoints.NoteMaxLength + 1) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("ההערה צריכה להיות עד 120 תווים.", await ErrorOf(tooLong));
        // Exactly the limit is fine; control characters and line breaks are folded first, so they never count.
        Assert.Equal(HttpStatusCode.OK, (await UsefulAsync(owner, checkId, new { useful = true, note = new string('x', FeedbackEndpoints.NoteMaxLength) })).StatusCode);
        var folded = await (await UsefulAsync(owner, checkId, new { useful = true, note = "line one\nline\ttwo\u0007" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("line one line two", folded.GetProperty("note").GetString());

        // A check the stylist did not score has no tip to rate.
        app.Vision.Handler = _ => Payloads.NotOutfit();
        var desk = await owner.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), language: "he"));
        app.Vision.Handler = _ => Payloads.Ok();
        var deskId = (await desk.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var notScored = await UsefulAsync(owner, deskId, new { useful = true });
        Assert.Equal(HttpStatusCode.BadRequest, notScored.StatusCode);
        Assert.Equal("אפשר לדרג רק בדיקה שהסטייליסט נתן לה ציון.", await ErrorOf(notScored));

        // The CSRF header is required like on every write.
        var bare = app.BareClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.PostAsJsonAsync($"/api/checks/{checkId}/useful", new { useful = true })).StatusCode);
    }

    [Fact]
    public async Task Anyone_but_the_owner_gets_the_checks_404_and_a_guest_rates_with_the_cookie()
    {
        using var app = new TestApp();
        var (owner, _, _) = await app.NewUserAsync("useful_mine");
        var (other, _, _) = await app.NewUserAsync("useful_other");
        var checkId = await app.CheckAsync(owner);

        var stranger = await UsefulAsync(other, checkId, new { useful = true });
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal("We couldn't find this check.", await ErrorOf(stranger));
        Assert.Equal(HttpStatusCode.NotFound, (await UsefulAsync(app.NewClient(), checkId, new { useful = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UsefulAsync(owner, Guid.NewGuid(), new { useful = true })).StatusCode);

        // A guest's check is rated by the guest, through the cookie the check set; another browser cannot.
        var guest = app.NewClient();
        guest.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.213");
        var created = await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var guestCheck = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await UsefulAsync(guest, guestCheck, new { useful = false, note = "not for a date" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UsefulAsync(app.NewClient(), guestCheck, new { useful = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UsefulAsync(other, guestCheck, new { useful = true })).StatusCode);
        var read = await guest.GetFromJsonAsync<JsonElement>($"/api/checks/{guestCheck}");
        Assert.False(read.GetProperty("useful").GetBoolean());
        Assert.Equal("not for a date", read.GetProperty("usefulNote").GetString());
    }

    [Fact]
    public async Task The_tally_is_rate_limited_like_the_other_counters()
    {
        using var app = new TestApp();
        var (owner, _, _) = await app.NewUserAsync("useful_limited");
        var checkId = await app.CheckAsync(owner);
        for (var i = 0; i < FeedbackEndpoints.PerHour; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await UsefulAsync(owner, checkId, new { useful = i % 2 == 0 })).StatusCode);
        }

        var refused = await UsefulAsync(owner, checkId, new { useful = true });
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("Slow down a little. Try again in a bit.", await ErrorOf(refused));
        Assert.NotNull(refused.Headers.RetryAfter);
    }

    [Fact]
    public async Task The_export_carries_the_verdict_and_the_numbers_page_shows_the_rate_by_intent_and_language()
    {
        using var app = new TestApp();
        var (noa, _, _) = await app.NewUserAsync("useful_noa");
        var (dan, _, _) = await app.NewUserAsync("useful_dan", language: "he");
        var (moderator, _, _) = await app.NewUserAsync("useful_mod");
        await app.PromoteAsync("useful_mod");

        var date1 = await app.CheckAsync(noa, "Date");
        var date2 = await app.CheckAsync(noa, "Date");
        var office = await app.CheckAsync(noa, "Office");
        var dateHe = await app.CheckAsync(dan, "Date", language: "he");
        await app.CheckAsync(dan, "Casual", language: "he");   // never answered
        Assert.Equal(HttpStatusCode.OK, (await UsefulAsync(noa, date1, new { useful = true, note = "yes" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UsefulAsync(noa, date2, new { useful = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UsefulAsync(noa, office, new { useful = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UsefulAsync(dan, dateHe, new { useful = true })).StatusCode);

        // A no-outfit and a rejected answer by an account, for the door's two counts; a guest's check stays out of every number.
        app.Vision.Handler = _ => Payloads.NotOutfit();
        Assert.Equal(HttpStatusCode.Created, (await noa.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        app.Vision.Handler = _ => Payloads.Rejected();
        Assert.Equal(HttpStatusCode.Created, (await dan.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        app.Vision.Handler = _ => Payloads.Ok();
        var guest = app.NewClient();
        guest.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.214");
        var guestCheck = (await (await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await UsefulAsync(guest, guestCheck, new { useful = false })).StatusCode);

        var export = await noa.GetFromJsonAsync<JsonElement>("/api/users/me/export");
        var exported = export.GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("id").GetGuid());
        Assert.True(exported[date1].GetProperty("useful").GetBoolean());
        Assert.Equal("yes", exported[date1].GetProperty("usefulNote").GetString());
        Assert.True(exported[date1].TryGetProperty("usefulAt", out _));
        Assert.False(exported[date2].GetProperty("useful").GetBoolean());
        Assert.False(exported[date2].TryGetProperty("usefulNote", out _));

        var metrics = await moderator.GetFromJsonAsync<JsonElement>("/api/metrics/pilot");
        var stylist = metrics.GetProperty("stylist");
        var overall = stylist.GetProperty("useful");
        Assert.Equal(3, overall.GetProperty("yes").GetInt32());
        Assert.Equal(1, overall.GetProperty("no").GetInt32());
        Assert.Equal(1, overall.GetProperty("unanswered").GetInt32());
        Assert.Equal(0.75, overall.GetProperty("rate").GetDouble());
        var date = stylist.GetProperty("byIntent").GetProperty("Date");
        Assert.Equal(2, date.GetProperty("yes").GetInt32());
        Assert.Equal(1, date.GetProperty("no").GetInt32());
        Assert.Equal(0, date.GetProperty("unanswered").GetInt32());
        Assert.Equal(0.6667, date.GetProperty("rate").GetDouble(), precision: 4);
        var casual = stylist.GetProperty("byIntent").GetProperty("Casual");
        Assert.Equal(1, casual.GetProperty("unanswered").GetInt32());
        Assert.False(casual.TryGetProperty("rate", out _));   // nobody answered: no rate, and null is left out of the JSON
        var he = stylist.GetProperty("byLanguage").GetProperty("he");
        Assert.Equal(1, he.GetProperty("yes").GetInt32());
        Assert.Equal(1, he.GetProperty("unanswered").GetInt32());
        Assert.Equal(2, stylist.GetProperty("byLanguage").GetProperty("en").GetProperty("yes").GetInt32());
        Assert.Equal(1, stylist.GetProperty("notOutfit").GetInt32());
        Assert.Equal(1, stylist.GetProperty("rejected").GetInt32());
    }

    [Fact]
    public void The_split_math()
    {
        var split = FeedbackEndpoints.Split([true, true, false, null, null, null]);
        Assert.Equal(new UsefulSplitDto(2, 1, 3, 0.6667), split);
        Assert.Equal(new UsefulSplitDto(0, 0, 2, null), FeedbackEndpoints.Split([null, null]));
        Assert.Equal(new UsefulSplitDto(0, 0, 0, null), FeedbackEndpoints.Split([]));
        Assert.Equal(new UsefulSplitDto(0, 3, 0, 0), FeedbackEndpoints.Split([false, false, false]));
    }

    // ---- the honest no-outfit answer ----

    [Theory]
    [InlineData("This looks like a photo of a desk. Try one where the clothes are visible.", true)]
    [InlineData("A landscape with no clothes in it; send a full-length photo of the outfit.", true)]
    [InlineData("Two people here: one outfit per photo, please.", true)]
    [InlineData("The clothes are on a hanger with nobody wearing them.", true)]
    [InlineData("Only a face is visible, not the clothes.", false)]
    [InlineData("The body is cropped out of the frame.", false)]
    [InlineData("This seems to be a child.", false)]
    [InlineData("A woman standing by a window, too far away.", false)]
    [InlineData("Too dark to read the outfit; the skin tones blend into the wall.", false)]
    [InlineData("רואים רק פנים, לא בגדים.", false)]
    [InlineData("זו תמונה של שולחן. שווה לנסות תמונה שבה הבגדים נראים.", true)]
    [InlineData("تظهر امرأة من بعيد.", false)]
    [InlineData("هذه صورة طبق طعام، الأفضل صورة تظهر الملابس.", true)]
    [InlineData("Видно только лицо, а не одежду.", false)]
    [InlineData("Это скриншот, а не фото образа.", true)]
    public void A_no_outfit_reason_that_names_a_person_is_dropped(string reason, bool kept)
    {
        Assert.Equal(!kept, OutfitAnalyzer.MentionsPerson(reason));
        Assert.Equal(kept ? reason : null, OutfitAnalyzer.SafeNoOutfitMessage(reason));

        var feedback = OutfitAnalyzer.MapToolInput(Payloads.Parse($$"""
            { "status": "not_outfit", "score": 1, "intent_match": 0, "headline": "", "vibe": "", "items": [], "working": [], "one_tip": "",
              "message": {{JsonSerializer.Serialize(reason)}} }
            """));
        Assert.Equal(CheckStatus.NotOutfit, feedback.Status);
        Assert.Equal(kept ? reason : null, feedback.Message);
    }

    [Fact]
    public void A_no_outfit_reason_is_one_line_and_never_invented()
    {
        Assert.Null(OutfitAnalyzer.SafeNoOutfitMessage(null));
        Assert.Null(OutfitAnalyzer.SafeNoOutfitMessage("   "));
        var rambling = string.Join(' ', Enumerable.Repeat("This photo shows a lamp on a table and nothing else.", 8));
        var cut = OutfitAnalyzer.SafeNoOutfitMessage(rambling)!;
        Assert.True(cut.Length <= OutfitAnalyzer.MaxNoOutfitMessageLength + 1, cut);
        Assert.EndsWith("…", cut);
        Assert.DoesNotContain("\n", OutfitAnalyzer.SafeNoOutfitMessage("a screenshot,\nnot a photo"));

        // The rubric names the cases and the rule for the message; the schema says the same to the model.
        var prompt = OutfitAnalyzer.BuildSystemPrompt("en");
        Assert.Contains("a screenshot", prompt);
        Assert.Contains("Two people", prompt);
        Assert.Contains("never about a person", prompt);
        var status = OutfitAnalyzer.ToolSchema.GetProperty("properties").GetProperty("status");
        Assert.Contains("not_outfit", status.GetProperty("description").GetString());
        Assert.Contains("never a word about a person", OutfitAnalyzer.ToolSchema.GetProperty("properties").GetProperty("message").GetProperty("description").GetString());
        Assert.Equal("v4", OutfitAnalyzer.PromptVersion);
    }

    [Fact]
    public async Task A_dropped_reason_becomes_the_generic_line_in_the_checks_language_and_a_comparison_drops_it_too()
    {
        using var app = new TestApp();
        var (owner, _, _) = await app.NewUserAsync("nooutfit_generic", language: "he");
        app.Vision.Handler = request => request.HasSecondImage
            ? OutfitComparerTests.Pick(status: "not_outfit", message: "Photo A shows a woman's face only.")
            : Payloads.Parse("""
                { "status": "not_outfit", "score": 1, "intent_match": 0, "headline": "", "vibe": "", "items": [], "working": [], "one_tip": "",
                  "message": "רואים רק את הפנים של הבחורה." }
                """);
        try
        {
            var response = await owner.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), language: "he"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var check = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("not_outfit", check.GetProperty("status").GetString());
            Assert.Equal("לא מצאנו לוק בתמונה הזו.", check.GetProperty("feedback").GetProperty("message").GetString());
            Assert.False(check.GetProperty("counted").GetBoolean());
            var read = await owner.GetFromJsonAsync<JsonElement>($"/api/checks/{check.GetProperty("id").GetGuid()}");
            Assert.Equal("לא מצאנו לוק בתמונה הזו.", read.GetProperty("feedback").GetProperty("message").GetString());

            var comparison = await owner.PostAsync("/api/compare", CompareTests.CompareForm(TestImages.Jpeg(), TestImages.Jpeg()));
            Assert.Equal(HttpStatusCode.Created, comparison.StatusCode);
            var dto = await comparison.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("not_outfit", dto.GetProperty("status").GetString());
            Assert.False(dto.GetProperty("feedback").TryGetProperty("message", out _));
        }
        finally
        {
            app.Vision.Handler = _ => Payloads.Ok();
        }
    }

    [Fact]
    public async Task A_no_outfit_answer_spends_nothing_up_to_the_forgiven_number_and_counts_after_it()
    {
        // Two free checks a day, two no-outfit answers forgiven.
        using var app = new TestApp { FreeChecksPerDay = 2, Settings = { ["Plans:NoOutfitForgivenPerDay"] = "2" } };
        var (owner, _, _) = await app.NewUserAsync("nooutfit_free");

        app.Vision.Handler = _ => Payloads.NotOutfit();
        try
        {
            for (var i = 0; i < 2; i++)
            {
                var forgiven = await owner.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
                Assert.Equal(HttpStatusCode.Created, forgiven.StatusCode);
                Assert.False((await forgiven.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("counted").GetBoolean());
            }

            Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("checksToday").GetInt32());

            // The third no-outfit answer of the day counts like any check.
            var third = await owner.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
            Assert.Equal(HttpStatusCode.Created, third.StatusCode);
            Assert.True((await third.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("counted").GetBoolean());
            Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("checksToday").GetInt32());
        }
        finally
        {
            app.Vision.Handler = _ => Payloads.Ok();
        }

        // One real check left, then the cap; a scored check says it counted.
        var scored = await owner.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.Created, scored.StatusCode);
        Assert.True((await scored.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("counted").GetBoolean());
        var capped = await owner.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode);
        Assert.Equal(2, (await owner.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("checksToday").GetInt32());
    }

    [Fact]
    public async Task The_global_ceiling_counts_every_no_outfit_answer_and_zero_forgives_none()
    {
        using var app = new TestApp { ChecksPerDayGlobal = 1, Settings = { ["Plans:NoOutfitForgivenPerDay"] = "0" } };
        var (a, _, _) = await app.NewUserAsync("nooutfit_global_a");
        var (b, _, _) = await app.NewUserAsync("nooutfit_global_b");
        app.Vision.Handler = _ => Payloads.NotOutfit();
        try
        {
            var first = await a.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            // Zero forgiven: it counted for the person as before Round 13, and for everyone.
            Assert.True((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("counted").GetBoolean());
            Assert.Equal(1, (await a.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("checksToday").GetInt32());
            Assert.Equal(HttpStatusCode.TooManyRequests, (await b.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        }
        finally
        {
            app.Vision.Handler = _ => Payloads.Ok();
        }
    }
}

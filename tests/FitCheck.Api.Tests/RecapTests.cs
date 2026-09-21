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
/// Round 16 — the month written back to the person, and the second half of what makes Pro worth keeping: the wardrobe
/// is worth more in month six because there is more of it, and this is worth more because there is a year to look
/// back on. Nobody remembers what they were told in March; the app does.
/// <para>
/// The cost shape is the design. One TEXT-ONLY call — no photograph, which is four fifths of an ordinary call — and
/// one per account per month per language, minted when it is first asked for. A subscriber who never opens the page
/// costs nothing at all, which a scheduled job could not manage.
/// </para>
/// </summary>
public class RecapTests
{
    private const string Paragraph = "You checked eleven looks this month and averaged 7.2, which is up on where you started.";

    private sealed class RecapApp : TestApp
    {
        public RecapApp()
        {
            // Every ordinary check succeeds; a recap answers the recap tool instead.
            Vision.Handler = request => request.HasImage
                ? Payloads.Ok()
                : Payloads.Parse($$"""{ "paragraph": {{JsonSerializer.Serialize(Paragraph)}} }""");
        }
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<HttpClient> ProWithChecksAsync(TestApp app, string handle, int checks)
    {
        var (client, _, _) = await app.NewUserAsync(handle);
        await AdminSync.SetProAsync(app.ConnectionString, handle, DateTime.UtcNow.AddDays(30));
        for (var i = 0; i < checks; i++)
        {
            await app.CheckAsync(client);
        }

        return client;
    }

    [Fact]
    public async Task A_month_with_enough_in_it_is_written_once_and_read_from_then_on()
    {
        using var app = new RecapApp();
        var pro = await ProWithChecksAsync(app, "recap_pro", Recaps.MinChecks);
        var checkCalls = app.Vision.Requests.Count;

        var first = await Json(await pro.GetAsync("/api/users/me/recap"));
        Assert.Equal(Paragraph, first.GetProperty("text").GetString());
        Assert.Equal(Recaps.MinChecks, first.GetProperty("checks").GetInt32());

        // Exactly one more model call than the checks made, and it carried no photograph.
        Assert.Equal(checkCalls + 1, app.Vision.Requests.Count);
        Assert.False(app.Vision.Requests[^1].HasImage, "a recap must not pay for an image it does not look at");

        // Asked again in the same month: the row, not the model.
        var second = await Json(await pro.GetAsync("/api/users/me/recap"));
        Assert.Equal(Paragraph, second.GetProperty("text").GetString());
        Assert.Equal(checkCalls + 1, app.Vision.Requests.Count);
    }

    [Fact]
    public async Task A_thin_month_says_so_rather_than_inventing_a_paragraph()
    {
        using var app = new RecapApp();
        var pro = await ProWithChecksAsync(app, "recap_thin", Recaps.MinChecks - 1);
        var before = app.Vision.Requests.Count;

        var recap = await Json(await pro.GetAsync("/api/users/me/recap"));
        // A null is left out of the document entirely (AppJson drops nulls), which the client reads as no paragraph.
        Assert.True(!recap.TryGetProperty("text", out var text) || text.ValueKind == JsonValueKind.Null,
            "a month too thin to write about must not come back with a paragraph: " + text);
        Assert.Equal(Recaps.MinChecks, recap.GetProperty("needs").GetInt32());
        // And nothing was spent finding that out.
        Assert.Equal(before, app.Vision.Requests.Count);
    }

    [Fact]
    public async Task It_is_pros_and_it_takes_nothing_from_anybody()
    {
        using var app = new RecapApp();
        var (free, _, _) = await app.NewUserAsync("recap_free");
        for (var i = 0; i < Recaps.MinChecks; i++)
        {
            await app.CheckAsync(free);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await free.GetAsync("/api/users/me/recap")).StatusCode);
        // The insights page, which was always free, is untouched by any of this.
        Assert.Equal(HttpStatusCode.OK, (await free.GetAsync("/api/users/me/insights")).StatusCode);
    }

    /// <summary>
    /// Someone who reads in Hebrew and switches to English gets their own, rather than last month's in the wrong
    /// language — and the month is keyed on all three, so that is a second row and a second call, not a rewrite.
    /// </summary>
    [Fact]
    public async Task Each_language_gets_its_own()
    {
        using var app = new RecapApp();
        var pro = await ProWithChecksAsync(app, "recap_lang", Recaps.MinChecks);
        await pro.GetAsync("/api/users/me/recap");

        var before = app.Vision.Requests.Count;
        Assert.True((await pro.PatchAsJsonAsync("/api/users/me", new { language = "he" })).IsSuccessStatusCode);
        Assert.Equal(Paragraph, (await Json(await pro.GetAsync("/api/users/me/recap"))).GetProperty("text").GetString());
        Assert.Equal(before + 1, app.Vision.Requests.Count);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, db.Recaps.Count());
    }

    /// <summary>
    /// The one that matters most. The model is handed figures this app computed and told to write them; it is never
    /// asked what the figures are, and it is never given a photograph, a caption, a handle or an address.
    /// </summary>
    [Fact]
    public void The_model_is_given_numbers_and_nothing_about_the_person()
    {
        var insights = new InsightsDto(11, 7.2, 9, "Date", "shoes", 0.36, 0.5, 4, ["a line"]);
        var figures = Recaps.Figures(insights, "he");

        Assert.Contains("Language to write in: he", figures, StringComparison.Ordinal);
        Assert.Contains("Checks in the last 30 days: 11", figures, StringComparison.Ordinal);
        Assert.Contains("7.2", figures, StringComparison.Ordinal);
        Assert.Contains("36%", figures, StringComparison.Ordinal);

        // Nothing identifying travels: a paragraph about a month needs the month's numbers and nothing else.
        foreach (var forbidden in new[] { "@", "handle", "http", "caption" })
        {
            Assert.DoesNotContain(forbidden, figures, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_figure_that_is_absent_is_left_out_rather_than_sent_as_a_zero()
    {
        // A month with too little in it has no average and no best; the model must not be handed "0" and asked to
        // write about it, because it would, and 0 out of 10 is not what happened.
        var thin = new InsightsDto(2, null, null, null, null, null, null, 0, []);
        var figures = Recaps.Figures(thin, "en");

        Assert.Contains("Checks in the last 30 days: 2", figures, StringComparison.Ordinal);
        Assert.DoesNotContain("Average score", figures, StringComparison.Ordinal);
        Assert.DoesNotContain("Best score", figures, StringComparison.Ordinal);
        Assert.DoesNotContain("Days in a row", figures, StringComparison.Ordinal);
    }
}

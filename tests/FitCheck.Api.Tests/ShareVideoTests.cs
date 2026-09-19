using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Endpoints;

namespace FitCheck.Api.Tests;

/// <summary>
/// POST /api/checks/{id}/shared-video: the share video is rendered and encoded on the phone (app/sharevideo.js), and this
/// route only counts one that was handed to the share sheet or saved, for the numbers page. The owner or the guest whose
/// cookie made the check may count; anyone else gets the check's 404; the tally is rate limited like the other counters.
/// </summary>
public class ShareVideoTests
{
    private static int _address;

    private static async Task<int> VideosMadeAsync(HttpClient moderator) =>
        (await moderator.GetFromJsonAsync<JsonElement>("/api/metrics/pilot")).GetProperty("social").GetProperty("videosMade").GetInt32();

    private static async Task<string?> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();

    [Fact]
    public async Task The_owner_counts_a_shared_video_and_the_metrics_show_the_tally()
    {
        using var app = new TestApp();
        var (owner, _, _) = await app.NewUserAsync("sv_owner");
        var (moderator, _, _) = await app.NewUserAsync("sv_mod");
        await app.PromoteAsync("sv_mod");
        var checkId = await app.CheckAsync(owner);

        Assert.Equal(0, await VideosMadeAsync(moderator));

        var first = await owner.PostAsync($"/api/checks/{checkId}/shared-video", null);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        // Shared to two apps, saved as well: every hand-off counts; the server stores nothing about the file itself.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/checks/{checkId}/shared-video", null)).StatusCode);

        Assert.Equal(2, await VideosMadeAsync(moderator));
    }

    [Fact]
    public async Task Anyone_but_the_owner_gets_the_checks_404_and_nothing_is_counted()
    {
        using var app = new TestApp();
        var (owner, _, _) = await app.NewUserAsync("sv_owner2");
        var (other, _, _) = await app.NewUserAsync("sv_other", language: "he");
        other.DefaultRequestHeaders.AcceptLanguage.ParseAdd("he-IL");   // the 404 speaks the request's language, as GET /api/checks/{id} does
        var (moderator, _, _) = await app.NewUserAsync("sv_mod2");
        await app.PromoteAsync("sv_mod2");
        var checkId = await app.CheckAsync(owner);

        // Another account, in its own language; a signed-out client with no guest cookie; an id that does not exist.
        var refused = await other.PostAsync($"/api/checks/{checkId}/shared-video", null);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Equal("לא מצאנו את הבדיקה הזו.", await ErrorOf(refused));
        Assert.Equal(HttpStatusCode.NotFound, (await app.NewClient().PostAsync($"/api/checks/{checkId}/shared-video", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync($"/api/checks/{Guid.NewGuid()}/shared-video", null)).StatusCode);
        // A state-changing call without the app's header is refused before the handler, like every other POST.
        Assert.Equal(HttpStatusCode.Forbidden, (await app.BareClient().PostAsync($"/api/checks/{checkId}/shared-video", null)).StatusCode);

        Assert.Equal(0, await VideosMadeAsync(moderator));
    }

    [Fact]
    public async Task A_guest_counts_their_own_check_with_the_cookie_and_nobody_else_can()
    {
        using var app = new TestApp();
        var guest = app.NewClient();
        guest.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.9.{Interlocked.Increment(ref _address)}.1");
        var made = await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        var checkId = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // The client keeps the guest cookie the check set; the same cookie names the caller here.
        Assert.Equal(HttpStatusCode.NoContent, (await guest.PostAsync($"/api/checks/{checkId}/shared-video", null)).StatusCode);
        // A fresh visitor, and a signed-in account, are not the guest.
        Assert.Equal(HttpStatusCode.NotFound, (await app.NewClient().PostAsync($"/api/checks/{checkId}/shared-video", null)).StatusCode);
        var (someone, _, _) = await app.NewUserAsync("sv_someone");
        Assert.Equal(HttpStatusCode.NotFound, (await someone.PostAsync($"/api/checks/{checkId}/shared-video", null)).StatusCode);

        var (moderator, _, _) = await app.NewUserAsync("sv_mod3");
        await app.PromoteAsync("sv_mod3");
        Assert.Equal(1, await VideosMadeAsync(moderator));
    }

    [Fact]
    public async Task The_tally_is_rate_limited_per_account()
    {
        using var app = new TestApp();
        var (owner, _, _) = await app.NewUserAsync("sv_limited");
        var (moderator, _, _) = await app.NewUserAsync("sv_mod4");
        await app.PromoteAsync("sv_mod4");
        var checkId = await app.CheckAsync(owner);

        for (var i = 0; i < CheckEndpoints.SharedVideosPerHour; i++)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/checks/{checkId}/shared-video", null)).StatusCode);
        }

        var refused = await owner.PostAsync($"/api/checks/{checkId}/shared-video", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.NotNull(refused.Headers.RetryAfter);
        Assert.Equal(CheckEndpoints.SharedVideosPerHour, await VideosMadeAsync(moderator));
    }
}

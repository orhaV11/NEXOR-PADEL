using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 14 — "I tried it": from a result, a second check of the same look after the change, and the two shown together.
/// The rule that is the whole point, and that these tests exist to make impossible to break: the second check is a real
/// check. It goes out through <c>POST /api/checks</c> with nothing of the first in it, the stylist is never told it is an
/// attempt at its own tip, the pair is written only afterwards (<c>POST /api/checks/{id}/tried</c>), and no score is ever
/// moved because somebody obeyed.
/// </summary>
public class TriedTests
{
    private static async Task<string?> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();

    /// <summary>A verdict with its own items, so "what changed" has something to name.</summary>
    private static JsonElement Look(int score, string headline, string tip, string shoes) => Payloads.Parse($$"""
        {
          "status": "ok", "score": {{score}}, "intent_match": 70,
          "headline": {{JsonSerializer.Serialize(headline)}},
          "vibe": "relaxed weekend",
          "items": [
            { "name": "White tee", "category": "top", "verdict": "works", "note": "Crisp." },
            { "name": "Dark jeans", "category": "bottom", "verdict": "neutral", "note": "Fine." },
            { "name": {{JsonSerializer.Serialize(shoes)}}, "category": "shoes", "verdict": "weak", "note": "The weak link." }
          ],
          "working": ["The palette is tight"],
          "one_tip": {{JsonSerializer.Serialize(tip)}}
        }
        """);

    [Fact]
    public async Task The_second_request_carries_no_trace_of_the_first_and_the_score_is_the_stylists_alone()
    {
        using var app = new TestApp();
        var (owner, _, _) = await app.NewUserAsync("tried_trace");

        const string firstHeadline = "Clean casual with one weak link";
        const string firstTip = "Swap the running shoes for plain white leather sneakers.";
        app.Vision.Handler = _ => Look(7, firstHeadline, firstTip, "Running shoes");
        var first = await app.CheckAsync(owner);

        // They say it worked. Nothing about that answer may reach the stylist as "this is an attempt".
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/checks/{first}/useful",
            new { reason = TipReason.Worked, note = "swapped them, much better" })).StatusCode);

        // The pair cannot exist before the second verdict does.
        var early = await owner.PostAsJsonAsync($"/api/checks/{Guid.NewGuid()}/tried", new { beforeId = first });
        Assert.Equal(HttpStatusCode.NotFound, early.StatusCode);

        // The second check: an ordinary check, and the form carries the fields a careless client might try to smuggle in.
        // The route has no door for them, so the recorded request must be the one any check would have made.
        app.Vision.Handler = _ => Look(5, "The loafers fight the rest", "Try a plain white leather sneaker instead.", "Black loafers");
        var form = TestApp.CheckForm(TestImages.Jpeg());
        form.Add(new StringContent(first.ToString()), "beforeId");
        form.Add(new StringContent("true"), "tried");
        var created = await owner.PostAsync("/api/checks", form);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var secondDto = await created.Content.ReadFromJsonAsync<JsonElement>();
        var second = secondDto.GetProperty("id").GetGuid();
        Assert.Equal(5, secondDto.GetProperty("score").GetInt32());

        var request = app.Vision.Requests[^1];
        var whole = request.SystemPrompt + "\n" + request.UserText;
        foreach (var trace in new[] { first.ToString(), first.ToString("N"), firstHeadline, firstTip, "swapped them", "tried", "attempt", "follow-up", "last time" })
        {
            Assert.DoesNotContain(trace, whole, StringComparison.OrdinalIgnoreCase);
        }

        // The user message is byte-for-byte the one a first-ever check of the same intent makes: the second check is not
        // a different kind of call, it is the same call.
        Assert.Equal(OutfitAnalyzer.BuildUserMessage(StyleIntent.Date, null), request.UserText);

        // Only now, with both verdicts stored, are the two linked — and the second check's score is untouched by it.
        var linked = await owner.PostAsJsonAsync($"/api/checks/{second}/tried", new { beforeId = first });
        Assert.Equal(HttpStatusCode.Created, linked.StatusCode);
        var pair = await linked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(first, pair.GetProperty("before").GetProperty("id").GetGuid());
        Assert.Equal(second, pair.GetProperty("after").GetProperty("id").GetGuid());
        Assert.Equal(7, pair.GetProperty("before").GetProperty("score").GetInt32());
        Assert.Equal(5, pair.GetProperty("after").GetProperty("score").GetInt32());
        Assert.False(pair.TryGetProperty("preferred", out _));

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(5, db.Checks.Single(c => c.Id == second).Score);
            Assert.Equal(7, db.Checks.Single(c => c.Id == first).Score);
            Assert.Equal(1, db.CheckLinks.Count());
        }

        // What changed in the combination: the shoes, named from the two stored verdicts and nothing else.
        var changed = pair.GetProperty("changed").EnumerateArray().ToList();
        var shoes = Assert.Single(changed, change => change.GetProperty("category").GetString() == "shoes");
        Assert.Equal("Running shoes", shoes.GetProperty("from").GetString());
        Assert.Equal("Black loafers", shoes.GetProperty("to").GetString());

        // The tally the numbers page reads.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await Counters.ReadAsync(db, CounterName.TriedPairs, default));
        }
    }

    [Fact]
    public async Task The_rules_of_a_pair()
    {
        using var app = new TestApp();
        var (owner, _, _) = await app.NewUserAsync("tried_rules", language: "he");
        var (other, _, _) = await app.NewUserAsync("tried_other");
        var first = await app.CheckAsync(owner, language: "he");
        var second = await app.CheckAsync(owner, language: "he");

        // A look cannot follow itself.
        var same = await owner.PostAsJsonAsync($"/api/checks/{second}/tried", new { beforeId = second });
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
        Assert.Equal("זה אותו לוק. שווה לצלם מחדש אחרי השינוי.", await ErrorOf(same));

        // The second has to come after the first.
        var backwards = await owner.PostAsJsonAsync($"/api/checks/{first}/tried", new { beforeId = second });
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
        Assert.Equal("הלוק השני צריך לבוא אחרי הראשון.", await ErrorOf(backwards));

        // A check the stylist never scored has no verdict to stand next to.
        app.Vision.Handler = _ => Payloads.NotOutfit();
        var desk = (await (await owner.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), language: "he"))).Content
            .ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        app.Vision.Handler = _ => Payloads.Ok();
        var notScored = await owner.PostAsJsonAsync($"/api/checks/{desk}/tried", new { beforeId = first });
        Assert.Equal(HttpStatusCode.BadRequest, notScored.StatusCode);
        Assert.Equal("לשני הלוקים צריך ציון לפני שאפשר להעמיד אותם זה ליד זה.", await ErrorOf(notScored));

        // Somebody else's check is not there at all, whichever side it is on.
        var strangerCheck = await app.CheckAsync(other);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"/api/checks/{strangerCheck}/tried", new { beforeId = first })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"/api/checks/{second}/tried", new { beforeId = strangerCheck })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/checks/{second}/tried", new { beforeId = first })).StatusCode);

        // A missing beforeId reads as a check that is not there, like any id that names nothing of theirs.
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"/api/checks/{second}/tried", new { })).StatusCode);

        // The good one, once; a second attempt on either half is refused.
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync($"/api/checks/{second}/tried", new { beforeId = first })).StatusCode);
        var third = await app.CheckAsync(owner, language: "he");
        var twice = await owner.PostAsJsonAsync($"/api/checks/{third}/tried", new { beforeId = first });
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("אחד מהלוקים האלה כבר מחובר לאחר.", await ErrorOf(twice));

        // The CSRF header is required, like on every write.
        Assert.Equal(HttpStatusCode.Forbidden, (await app.BareClient().PostAsJsonAsync($"/api/checks/{third}/tried", new { beforeId = first })).StatusCode);
    }

    [Fact]
    public async Task Which_one_do_you_prefer_is_stored_and_changes_no_score()
    {
        using var app = new TestApp();
        app.Clock.Now = new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc);
        var (owner, _, _) = await app.NewUserAsync("tried_prefer");
        var (other, _, _) = await app.NewUserAsync("tried_prefer_other");

        app.Vision.Handler = _ => Look(8, "Sharp", "Roll the sleeves.", "White sneakers");
        var first = await app.CheckAsync(owner);
        app.Vision.Handler = _ => Look(6, "Softer", "Untuck it.", "Brown boots");
        var second = await app.CheckAsync(owner);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync($"/api/checks/{second}/tried", new { beforeId = first })).StatusCode);

        var bad = await owner.PostAsJsonAsync($"/api/checks/{second}/tried/prefer", new { prefer = "neither" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("Say which one you prefer: the first or the second.", await ErrorOf(bad));

        // The person prefers the one with the lower number: their answer, not the stylist's.
        var stored = await owner.PostAsJsonAsync($"/api/checks/{second}/tried/prefer", new { prefer = "after" });
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);
        var pair = await stored.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("after", pair.GetProperty("preferred").GetString());
        Assert.Equal(app.Clock.Now, pair.GetProperty("preferredAt").GetDateTime().ToUniversalTime());
        Assert.Equal(8, pair.GetProperty("before").GetProperty("score").GetInt32());
        Assert.Equal(6, pair.GetProperty("after").GetProperty("score").GetInt32());

        // It can be changed, from either side of the pair.
        var changed = await owner.PostAsJsonAsync($"/api/checks/{first}/tried/prefer", new { prefer = "before" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("before", (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("preferred").GetString());

        // Nobody else can say anything about it, and neither can a check that is in no pair.
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/checks/{second}/tried/prefer", new { prefer = "after" })).StatusCode);
        var lonely = await app.CheckAsync(owner);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"/api/checks/{lonely}/tried/prefer", new { prefer = "after" })).StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(8, db.Checks.Single(c => c.Id == first).Score);
        Assert.Equal(6, db.Checks.Single(c => c.Id == second).Score);
    }

    [Fact]
    public async Task The_list_of_pairs_is_the_callers_own()
    {
        using var app = new TestApp();
        var (noa, _, _) = await app.NewUserAsync("tried_list_noa");
        var (dan, _, _) = await app.NewUserAsync("tried_list_dan");

        Assert.Empty((await noa.GetFromJsonAsync<JsonElement>("/api/users/me/tried")).GetProperty("items").EnumerateArray());

        var noaFirst = await app.CheckAsync(noa);
        var noaSecond = await app.CheckAsync(noa);
        Assert.Equal(HttpStatusCode.Created, (await noa.PostAsJsonAsync($"/api/checks/{noaSecond}/tried", new { beforeId = noaFirst })).StatusCode);
        var danFirst = await app.CheckAsync(dan);
        var danSecond = await app.CheckAsync(dan);
        Assert.Equal(HttpStatusCode.Created, (await dan.PostAsJsonAsync($"/api/checks/{danSecond}/tried", new { beforeId = danFirst })).StatusCode);

        var mine = (await noa.GetFromJsonAsync<JsonElement>("/api/users/me/tried")).GetProperty("items").EnumerateArray().ToList();
        var one = Assert.Single(mine);
        Assert.Equal(noaFirst, one.GetProperty("before").GetProperty("id").GetGuid());
        Assert.Equal(noaSecond, one.GetProperty("after").GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.NewClient().GetAsync("/api/users/me/tried")).StatusCode);
    }

    [Fact]
    public void What_changed_is_read_off_the_two_stored_verdicts_and_names_only_clothes()
    {
        var before = JsonSerializer.Serialize(OutfitAnalyzer.MapToolInput(Look(7, "A", "swap", "Running shoes")), AppJson.Options);
        var after = JsonSerializer.Serialize(OutfitAnalyzer.MapToolInput(Look(7, "B", "swap", "White leather sneakers")), AppJson.Options);

        var changes = Taste.Changes(before, after);
        var shoes = Assert.Single(changes);
        Assert.Equal("shoes", shoes.Category);
        Assert.Equal("Running shoes", shoes.From);
        Assert.Equal("White leather sneakers", shoes.To);

        // The same pieces on both sides: nothing changed.
        Assert.Empty(Taste.Changes(before, before));

        // A piece that is only on one side reads as added or gone, and nothing is said about a person.
        var withJacket = Payloads.Parse("""
            { "status": "ok", "score": 7, "intent_match": 70, "headline": "", "vibe": "", "working": [], "one_tip": "",
              "items": [ { "name": "Navy blazer", "category": "outerwear", "verdict": "works", "note": "" } ] }
            """);
        var jacket = JsonSerializer.Serialize(OutfitAnalyzer.MapToolInput(withJacket), AppJson.Options);
        var added = Taste.Changes("""{"items":[]}""", jacket);
        Assert.Contains(added, change => change.Category == "outerwear" && change.From is null && change.To == "Navy blazer");
        var gone = Taste.Changes(jacket, """{"items":[]}""");
        Assert.Contains(gone, change => change.Category == "outerwear" && change.From == "Navy blazer" && change.To is null);

        // Unreadable or missing feedback is not a crash and names nothing.
        Assert.Empty(Taste.Changes(null, null));
        Assert.Empty(Taste.Changes("not json", "not json either"));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 14 — the category error, corrected. One list used to hold two questions, and a person had to pick one of the
/// eight: streetwear for a date, or minimal for a party, could not be said at all. These tests hold the split in place:
/// the pair survives a round trip, a database written before the split still opens and every stored check comes out of
/// it split the right way, no style is a first-class answer rather than a blank, a keep stays a keep from the model's
/// word to the export, the anchored scale really is in the request the stylist is sent, and nothing the split added -
/// in any of the four languages - says a word about a body, a face or an age.
/// </summary>
public class IntentSplitTests : IClassFixture<IntentSplitTests.SplitApp>
{
    public sealed class SplitApp : TestApp;

    private readonly SplitApp _app;

    public IntentSplitTests(SplitApp app) => _app = app;

    /// <summary>The answer a stylist gives when the look is already right: a keep, with the tip naming what to keep.</summary>
    private static JsonElement Keep(string tip = "Keep the brown boots: they pick up the belt and hold this together.") => Payloads.Parse($$"""
        { "status": "ok", "score": 9, "intent_match": 92, "headline": "Deliberate, top to bottom", "vibe": "quiet confidence",
          "items": [{ "name": "Brown boots", "category": "shoes", "verdict": "works", "note": "They set the palette.", "brand_seen": null }],
          "working": ["The palette is decided"], "one_tip": {{JsonSerializer.Serialize(tip)}}, "tip_kind": "keep",
          "breakdown": { "fit": 9, "color": 9, "accessories": 8 },
          "accessories": { "verdict": "adds", "present": ["brown leather belt"], "note": "They finish it.", "add_one": "" } }
        """);

    // ---- 1. the two questions, in and out ----

    [Fact]
    public async Task A_check_carries_the_occasion_and_the_style_there_and_back()
    {
        _app.Vision.Handler = _ => Payloads.Ok();
        var (client, _, _) = await _app.NewUserAsync("split_pair");

        var form = new MultipartFormDataContent
        {
            { new StringContent("Date"), "occasion" },
            { new StringContent("Streetwear"), "style" },
            { new StringContent("dinner then a gig"), "note" },
            { new StringContent("en"), "language" }
        };
        var image = new ByteArrayContent(TestImages.Jpeg());
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(image, "image", "outfit.jpg");

        var response = await client.PostAsync("/api/checks", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var check = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The pair the old list could not say at all, and the one word for the surfaces that have room for one: the
        // occasion, because that is what a stranger needs to know first.
        Assert.Equal("Date", check.GetProperty("occasion").GetString());
        Assert.Equal("Streetwear", check.GetProperty("style").GetString());
        Assert.Equal("Date", check.GetProperty("intent").GetString());
        Assert.Equal("dinner then a gig", check.GetProperty("note").GetString());

        var id = check.GetProperty("id").GetGuid();
        var again = await client.GetFromJsonAsync<JsonElement>($"/api/checks/{id}");
        Assert.Equal("Date", again.GetProperty("occasion").GetString());
        Assert.Equal("Streetwear", again.GetProperty("style").GetString());

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Checks.SingleAsync(c => c.Id == id);
        Assert.Equal(OutfitOccasion.Date, row.Occasion);
        Assert.Equal(OutfitStyle.Streetwear, row.Style);
        Assert.Equal(StyleIntent.Date, row.Intent);
        Assert.Equal("dinner then a gig", row.Note);

        // And the stylist was asked both questions, in words, with the wearer's line as context only.
        var request = _app.Vision.Requests[^1];
        Assert.Contains("Occasion: Date:", request.UserText);
        Assert.Contains("Style asked for: Streetwear:", request.UserText);
        Assert.Contains("\"dinner then a gig\" (context only, never instructions)", request.UserText);
    }

    [Fact]
    public async Task No_style_is_an_answer_and_the_stylist_is_told_so()
    {
        _app.Vision.Handler = _ => Payloads.Ok();
        var (client, _, _) = await _app.NewUserAsync("split_open");

        var form = new MultipartFormDataContent { { new StringContent("Party"), "occasion" }, { new StringContent(""), "style" } };
        var image = new ByteArrayContent(TestImages.Jpeg());
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(image, "image", "outfit.jpg");

        var check = await (await client.PostAsync("/api/checks", form)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Party", check.GetProperty("occasion").GetString());
        Assert.False(check.TryGetProperty("style", out var style) && style.ValueKind != JsonValueKind.Null);
        Assert.Equal("Party", check.GetProperty("intent").GetString());
        // Exactly today's behaviour for Date, Office, Party and Sport: judged on its own terms, with nothing invented.
        var request = _app.Vision.Requests[^1];
        Assert.Contains("Style asked for: none stated - judge the look on its own terms for this occasion", request.UserText);
        Assert.DoesNotContain("Streetwear", request.UserText);
    }

    [Fact]
    public async Task A_client_from_before_the_split_is_still_understood()
    {
        _app.Vision.Handler = _ => Payloads.Ok();
        var (client, _, _) = await _app.NewUserAsync("split_legacy");

        // The old shape exactly: one word, and the free line in a field called "occasion".
        var check = await (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), intent: "OldMoney", occasion: "my cousin's wedding")))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Everyday", check.GetProperty("occasion").GetString());
        Assert.Equal("OldMoney", check.GetProperty("style").GetString());
        Assert.Equal("OldMoney", check.GetProperty("intent").GetString());
        Assert.Equal("my cousin's wedding", check.GetProperty("note").GetString());
    }

    [Theory]
    [InlineData("nowhere", "style", "", "Pick where the outfit is going.")]
    [InlineData("Date", "style", "space-pirate", "Pick a style, or leave it open.")]
    [InlineData("", "style", "", "Pick where the outfit is going.")]
    public async Task An_occasion_or_a_style_we_do_not_know_is_refused_by_name(string occasion, string field, string style, string message)
    {
        var (client, _, _) = await _app.NewUserAsync("split_bad_" + Math.Abs((occasion + style).GetHashCode() % 10000));

        var form = new MultipartFormDataContent { { new StringContent(occasion), "occasion" }, { new StringContent(style), field } };
        var image = new ByteArrayContent(TestImages.Jpeg());
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(image, "image", "outfit.jpg");

        var response = await client.PostAsync("/api/checks", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(message, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public void The_two_shapes_map_onto_each_other_and_the_one_word_is_the_occasion()
    {
        // Every one of the eight is exactly one pair: five occasions with no style, three styles worn everyday.
        Assert.Equal((OutfitOccasion.Everyday, null), StyleIntents.Split(StyleIntent.Casual));
        Assert.Equal((OutfitOccasion.Everyday, OutfitStyle.Minimal), StyleIntents.Split(StyleIntent.Minimal));
        Assert.Equal((OutfitOccasion.Sport, null), StyleIntents.Split(StyleIntent.Sport));
        foreach (var intent in Enum.GetValues<StyleIntent>())
        {
            var (occasion, style) = StyleIntents.Split(intent);
            Assert.Equal(intent, StyleIntents.Legacy(occasion, style));
        }

        // A pair the old list could not say comes back as its OCCASION, never as the style: a streetwear look for a
        // date is a Date look to anyone reading one word.
        Assert.Equal(StyleIntent.Date, StyleIntents.Legacy(OutfitOccasion.Date, OutfitStyle.Streetwear));
        Assert.Equal(StyleIntent.Office, StyleIntents.Legacy(OutfitOccasion.Office, OutfitStyle.Minimal));
        Assert.Equal(StyleIntent.Casual, StyleIntents.Legacy(OutfitOccasion.Everyday, OutfitStyle.Classic));
        // Formal has its own word now, so a wedding look no longer says PARTY on the card, the board and the share video.
        Assert.Equal(StyleIntent.Formal, StyleIntents.Legacy(OutfitOccasion.Formal, null));
        Assert.Equal(StyleIntent.Formal, StyleIntents.Legacy(OutfitOccasion.Formal, OutfitStyle.Minimal));
    }

    [Theory]
    [InlineData("date", true, "Date")]
    [InlineData("EVERYDAY", true, "Everyday")]
    [InlineData("formal", true, "Formal")]
    [InlineData("casual", false, "")]
    [InlineData("", false, "")]
    public void An_occasion_is_read_by_name_whatever_the_case(string name, bool known, string expected)
    {
        Assert.Equal(known, StyleIntents.TryParseOccasion(name, out var occasion));
        if (known) Assert.Equal(expected, occasion.ToString());
    }

    [Theory]
    [InlineData("streetwear", "Streetwear")]
    [InlineData("old money", "OldMoney")]
    [InlineData("old-money", "OldMoney")]
    [InlineData("OldMoney", "OldMoney")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("none", null)]
    public void A_style_is_read_by_name_and_nothing_is_not_a_failure(string name, string? expected)
    {
        Assert.True(StyleIntents.TryParseStyle(name, out var style));
        Assert.Equal(expected, style?.ToString());
        Assert.False(StyleIntents.TryParseStyle("space-pirate", out _));
    }

    // ---- 2. a database written before the split ----

    [Fact]
    public void A_database_from_before_the_split_opens_and_every_stored_check_comes_out_split()
    {
        var root = Path.Combine(Path.GetTempPath(), "fitcheck-tests", "split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "presplit.db");
        // Formal is younger than the split: no database written before it can hold that word, so the rows a pre-split
        // file has are the eight the old list had.
        var intents = Enum.GetValues<StyleIntent>().Where(i => i != StyleIntent.Formal).ToArray();
        try
        {
            // The schema as Round 13 left it, then rows exactly as that app wrote them: one word, and the wearer's
            // free line in the column called Occasion.
            using (var db = Open(path))
            {
                db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate("20260920084901_Round13");
                foreach (var intent in intents)
                {
                    db.Database.ExecuteSqlRaw(
                        """
                        INSERT INTO "Checks" ("Id","UserId","GuestToken","ClaimedAt","Intent","Occasion","Language","ImagePath","VideoPath",
                                              "Useful","UsefulAt","UsefulNote","Status","Score","FeedbackJson","PromptVersion","LatencyMs","CreatedAt")
                        VALUES ({0}, NULL, NULL, NULL, {1}, {2}, 'en', {3}, NULL, NULL, NULL, NULL, 'ok', 7, NULL, 'v4', 1200, '2026-09-01 10:00:00')
                        """,
                        Guid.NewGuid().ToString(), intent.ToString(), "note for " + intent, "old/" + intent + ".jpg");
                }
            }

            // What a deploy does on start: the app opens the file it finds.
            using (var db = Open(path))
            {
                DatabaseSetup.Apply(db, NullLogger.Instance);
            }

            using (var db = Open(path))
            {
                Assert.Empty(db.Database.GetPendingMigrations());
                Assert.Equal(intents.Length, db.Checks.Count());
                foreach (var intent in intents)
                {
                    var row = db.Checks.Single(c => c.ImagePath == "old/" + intent + ".jpg");
                    var (occasion, style) = StyleIntents.Split(intent);
                    Assert.Equal(occasion, row.Occasion);
                    Assert.Equal(style, row.Style);
                    // The one word is untouched, and not one character of what the person typed has moved.
                    Assert.Equal(intent, row.Intent);
                    Assert.Equal("note for " + intent, row.Note);
                }

                // Nothing invented: the three styles come only from the three style rows.
                Assert.Equal(3, db.Checks.Count(c => c.Style != null));
                Assert.Equal(4, db.Checks.Count(c => c.Occasion == OutfitOccasion.Everyday));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { /* a temp folder is not worth a failing test */ }
        }
    }

    private static AppDbContext Open(string path) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options);

    // ---- 3. the tip that says "change nothing" ----

    [Fact]
    public async Task A_keep_stays_a_keep_from_the_model_to_the_export_and_a_change_stays_a_change()
    {
        var (client, _, _) = await _app.NewUserAsync("split_keep");

        _app.Vision.Handler = _ => Keep();
        var kept = await (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("keep", kept.GetProperty("feedback").GetProperty("tipKind").GetString());
        Assert.Equal("Keep the brown boots: they pick up the belt and hold this together.", kept.GetProperty("feedback").GetProperty("oneTip").GetString());

        _app.Vision.Handler = _ => Payloads.Ok();
        var changed = await (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("change", changed.GetProperty("feedback").GetProperty("tipKind").GetString());

        // Stored as it was said, read back the same, and in the export beside the tip it belongs to.
        var keptId = kept.GetProperty("id").GetGuid();
        Assert.Equal("keep", (await client.GetFromJsonAsync<JsonElement>($"/api/checks/{keptId}")).GetProperty("feedback").GetProperty("tipKind").GetString());
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Contains("\"tipKind\":\"keep\"", (await db.Checks.SingleAsync(c => c.Id == keptId)).FeedbackJson);
        }

        var export = await client.GetFromJsonAsync<JsonElement>("/api/users/me/export");
        var kinds = export.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("tipKind").GetString()).ToList();
        Assert.Equal(["change", "keep"], kinds);   // newest first: the change, then the keep
    }

    [Fact]
    public async Task A_check_with_no_verdict_carries_no_keep()
    {
        var (client, _, _) = await _app.NewUserAsync("split_keep_refused");
        _app.Vision.Handler = _ => V2Payloads.Ok(status: "not_outfit");

        var check = await (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("not_outfit", check.GetProperty("status").GetString());
        Assert.Equal("", check.GetProperty("feedback").GetProperty("oneTip").GetString());
        Assert.Equal("change", check.GetProperty("feedback").GetProperty("tipKind").GetString());
        // And the export says nothing about a kind of tip that was never given.
        var export = await client.GetFromJsonAsync<JsonElement>("/api/users/me/export");
        var row = export.GetProperty("checks").EnumerateArray().First();
        Assert.False(row.TryGetProperty("tipKind", out _));
    }

    [Fact]
    public async Task A_check_stored_before_the_keep_existed_reads_as_a_change()
    {
        var (client, userId, _) = await _app.NewUserAsync("split_keep_legacy");
        var id = Guid.NewGuid();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var v4 = OutfitAnalyzer.MapToolInput(Payloads.Ok());
            // Exactly what a v4 document had: a tip and no word about its kind.
            var json = JsonSerializer.Serialize(v4, AppJson.Options).Replace(",\"tipKind\":\"change\"", "");
            Assert.DoesNotContain("tipKind", json);
            db.Checks.Add(new OutfitCheck
            {
                Id = id, UserId = userId, Intent = StyleIntent.Casual, Occasion = OutfitOccasion.Everyday, Language = "en",
                Status = CheckStatus.Ok, Score = 7, ImagePath = "legacy/photo.jpg", FeedbackJson = json, PromptVersion = "v4",
                LatencyMs = 900, CreatedAt = DateTime.UtcNow.AddDays(-3)
            });
            await db.SaveChangesAsync();
        }

        var check = await client.GetFromJsonAsync<JsonElement>($"/api/checks/{id}");

        Assert.Equal("change", check.GetProperty("feedback").GetProperty("tipKind").GetString());
    }

    // ---- 4. the anchors are in the request, not in a hope ----

    [Fact]
    public async Task The_stylist_is_sent_the_anchored_scale_and_the_two_questions_on_every_check()
    {
        _app.Vision.Handler = _ => Payloads.Ok();
        var (client, _, _) = await _app.NewUserAsync("split_anchors");
        _app.Vision.Requests.Clear();

        await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));

        var request = Assert.Single(_app.Vision.Requests);
        foreach (var anchor in new[]
        {
            "THE SCALE (anchored", "SCORE, 1-10", "FIT, 1-10", "COLOR, 1-10", "ACCESSORIES, 1-10", "INTENT_MATCH, 0-100",
            "WHAT YOU ARE ASKED (two questions, not one)", "THE OCCASION WINS.", "A keep is RARE."
        })
        {
            Assert.Contains(anchor, request.SystemPrompt);
        }

        // The tool asks for the kind of tip beside the tip, and still insists on a tip.
        var required = request.Tool.InputSchema.GetProperty("required").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains("one_tip", required);
        Assert.Contains("tip_kind", required);
    }

    // ---- 5. rule 1, over every string the split added ----

    [Fact]
    public void Nothing_the_split_added_says_a_word_about_a_body_a_face_or_an_age()
    {
        var strings = new List<(string Where, string Text)>();
        foreach (var (occasion, line) in OutfitAnalyzer.OccasionGuide) strings.Add(($"occasion:{occasion}", line));
        foreach (var (style, line) in OutfitAnalyzer.StyleGuide) strings.Add(($"style:{style}", line));
        strings.Add(("no style", OutfitAnalyzer.NoStyleLine));
        foreach (var occasion in StyleIntents.Occasions)
        {
            foreach (var style in StyleIntents.Styles.Cast<OutfitStyle?>().Append(null))
            {
                strings.Add(($"message:{occasion}/{style}", OutfitAnalyzer.BuildUserMessage(occasion, style, "a wedding at six")));
            }
        }

        var localizer = new Localizer();
        foreach (var language in Localizer.SupportedLocales)
        {
            foreach (var key in new[] { "error.occasion_invalid", "error.style_invalid" })
            {
                strings.Add(($"{key}:{language}", localizer.Get(language, key)));
            }
        }

        foreach (var (code, key, text) in NewLocaleStrings())
        {
            strings.Add(($"{code}:{key}", text));
        }

        var offenders = strings
            .Where(s => OutfitAnalyzer.MentionsPerson(WithoutTheTwoWordsThatAreNotAboutPeople(s.Text)))
            .Select(s => s.Where + ": " + s.Text)
            .ToList();

        Assert.True(offenders.Count == 0, "strings that name a body, a face or an age: " + string.Join(" | ", offenders));
        // Both exceptions really do trip the filter on their own, so this list cannot quietly become a hole in it.
        Assert.True(OutfitAnalyzer.MentionsPerson("Old money: muted palette"), "the filter still reads 'old' as a word about a person");
        Assert.True(OutfitAnalyzer.MentionsPerson("\u05e6\u05e8\u05d9\u05da \u05dc\u05d1\u05d7\u05d5\u05e8"), "the filter still reads the Hebrew stem inside the verb");
        // And it is only those two: the filter is untouched and still catches a real one in every language.
        Assert.All(new[] { "her face is lovely", "\u05d4\u05e4\u05e0\u05d9\u05dd \u05e9\u05dc\u05d4", "\u0648\u062c\u0647\u0647\u0627", "\u0435\u0451 \u043b\u0438\u0446\u043e" },
            text => Assert.True(OutfitAnalyzer.MentionsPerson(WithoutTheTwoWordsThatAreNotAboutPeople(text))));
    }

    /// <summary>
    /// Two words the filter reads as being about a person while they are not, both older than this round:
    /// "old money" is the name of a look, and the Hebrew verb "to choose" (לבחור) carries the stem for "a young man".
    /// The filter is deliberately loose - a false drop only ever costs the stylist's one line - so the test says out loud
    /// which two it forgives instead of loosening the filter for everyone.
    /// </summary>
    private static string WithoutTheTwoWordsThatAreNotAboutPeople(string text) => text
        .Replace("Old money", "OldMoney", StringComparison.OrdinalIgnoreCase)
        .Replace("\u05dc\u05d1\u05d7\u05d5\u05e8", "\u05dc\u05d1\u05d7\u05e8", StringComparison.Ordinal);

    /// <summary>
    /// The rubric is the one place those words are allowed, because it is where they are forbidden ("never the body").
    /// So the new blocks are read line by line: a line may name a body only while telling the stylist not to.
    /// </summary>
    [Fact]
    public void The_lines_the_split_added_to_the_rubric_talk_about_garments()
    {
        var prompt = OutfitAnalyzer.BuildSystemPrompt("en");
        var blocks = new[]
        {
            Between(prompt, "WHAT YOU ARE ASKED (two questions, not one)", "HARD RULES"),
            Between(prompt, "THE SCALE (anchored", "LANGUAGE:")
        };

        var offenders = blocks
            .SelectMany(block => block.Split('\n'))
            .Select(line => line.Trim())
            .Where(line => OutfitAnalyzer.MentionsPerson(line) && !line.Contains("never the body", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0, "rubric lines that name a body, a face or an age: " + string.Join(" | ", offenders));
        Assert.All(blocks, block => Assert.True(block.Length > 200, "the block is there to be read"));
    }

    private static string Between(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"the rubric has no \"{from}\"");
        var end = text.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, $"the rubric has no \"{to}\" after \"{from}\"");
        return text[start..end];
    }

    /// <summary>The Round 14 keys, read from the four locale files themselves, so a translation cannot slip past this.</summary>
    private static IEnumerable<(string Code, string Key, string Text)> NewLocaleStrings()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "i18n"));
        foreach (var code in new[] { "en", "he", "ar", "ru" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, code + ".json")));
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Name.StartsWith("occasion.", StringComparison.Ordinal)
                    || property.Name.StartsWith("style.", StringComparison.Ordinal)
                    || property.Name.StartsWith("tip.", StringComparison.Ordinal))
                {
                    yield return (code, property.Name, property.Value.GetString() ?? "");
                }
            }
        }
    }

    [Fact]
    public void The_four_locale_files_carry_every_key_the_split_needs()
    {
        var byCode = NewLocaleStrings().GroupBy(s => s.Code).ToDictionary(g => g.Key, g => g.Select(s => s.Key).ToHashSet());

        Assert.Equal(4, byCode.Count);
        foreach (var occasion in StyleIntents.Occasions)
        {
            Assert.All(byCode, pair => Assert.Contains("occasion." + occasion, pair.Value));
        }

        foreach (var style in StyleIntents.Styles)
        {
            Assert.All(byCode, pair => Assert.Contains("style." + style, pair.Value));
        }

        foreach (var key in new[] { "style.none", "style.title", "occasion.title", "occasion.asked", "tip.keep_title", "tip.keep_label", "tip.keep_hint" })
        {
            Assert.All(byCode, pair => Assert.Contains(key, pair.Value));
        }

        // Same keys in all four, and none of them left in English in another file.
        Assert.All(byCode, pair => Assert.Equal(byCode["en"].Count, pair.Value.Count));
    }
}

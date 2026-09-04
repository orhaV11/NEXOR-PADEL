using FitCheck.Api.Domain;
using FitCheck.Api.Services;

namespace FitCheck.Api.Tests;

public class OutfitAnalyzerTests
{
    [Fact]
    public void Maps_ok_payload()
    {
        var feedback = OutfitAnalyzer.MapToolInput(Payloads.Ok());

        Assert.Equal(CheckStatus.Ok, feedback.Status);
        Assert.Equal(7, feedback.Score);
        Assert.Equal(72, feedback.IntentMatch);
        Assert.Equal("Clean casual with one weak link", feedback.Headline);
        Assert.Equal("relaxed weekend", feedback.Vibe);
        Assert.Equal(3, feedback.Items.Count);
        Assert.Equal("Running shoes", feedback.Items[2].Name);
        Assert.Equal("shoes", feedback.Items[2].Category);
        Assert.Equal("weak", feedback.Items[2].Verdict);
        Assert.Equal(["The palette is tight", "Proportions are balanced"], feedback.Working);
        Assert.Equal("Swap the running shoes for plain white leather sneakers.", feedback.OneTip);
        Assert.Null(feedback.Message);
    }

    [Fact]
    public void Not_outfit_keeps_message_and_clears_everything_else()
    {
        var feedback = OutfitAnalyzer.MapToolInput(Payloads.Parse("""
            { "status": "not_outfit", "score": 6, "intent_match": 40, "headline": "should go", "vibe": "should go",
              "items": [{ "name": "Desk", "category": "other", "verdict": "neutral", "note": "x" }],
              "working": ["leak"], "one_tip": "leak", "message": "Not an outfit, try again." }
            """));

        Assert.Equal(CheckStatus.NotOutfit, feedback.Status);
        Assert.Equal("Not an outfit, try again.", feedback.Message);
        Assert.Equal(1, feedback.Score);
        Assert.Equal(0, feedback.IntentMatch);
        Assert.Empty(feedback.Items);
        Assert.Empty(feedback.Working);
        Assert.Equal("", feedback.OneTip);
        Assert.Equal("", feedback.Headline);
        Assert.Equal("", feedback.Vibe);
    }

    [Fact]
    public void Rejected_clears_everything_but_status_and_message()
    {
        var feedback = OutfitAnalyzer.MapToolInput(Payloads.Rejected());

        Assert.Equal(CheckStatus.Rejected, feedback.Status);
        Assert.Empty(feedback.Items);
        Assert.Empty(feedback.Working);
        Assert.Equal("", feedback.OneTip);
        Assert.Equal("", feedback.Headline);
    }

    [Theory]
    [InlineData(15, 10)]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(10, 10)]
    [InlineData(1, 1)]
    public void Score_is_clamped_to_1_10(int given, int expected)
    {
        var feedback = OutfitAnalyzer.MapToolInput(Payloads.Ok(score: given));
        Assert.Equal(expected, feedback.Score);
    }

    [Theory]
    [InlineData(140, 100)]
    [InlineData(-5, 0)]
    [InlineData(55, 55)]
    public void Intent_match_is_clamped_to_0_100(int given, int expected)
    {
        var feedback = OutfitAnalyzer.MapToolInput(Payloads.Ok(intentMatch: given));
        Assert.Equal(expected, feedback.IntentMatch);
    }

    [Fact]
    public void Tolerates_numbers_as_strings_and_floats()
    {
        var feedback = OutfitAnalyzer.MapToolInput(Payloads.Parse("""
            { "status": "OK", "score": "8", "intent_match": 66.6, "headline": "h", "vibe": "v", "items": [], "working": [], "one_tip": "t" }
            """));

        Assert.Equal(CheckStatus.Ok, feedback.Status);
        Assert.Equal(8, feedback.Score);
        Assert.Equal(67, feedback.IntentMatch);
    }

    [Fact]
    public void Missing_numbers_fall_back_to_the_bottom_of_the_range()
    {
        var feedback = OutfitAnalyzer.MapToolInput(Payloads.Parse("""
            { "status": "ok", "headline": "h", "vibe": "v", "items": [], "working": [], "one_tip": "t" }
            """));

        Assert.Equal(1, feedback.Score);
        Assert.Equal(0, feedback.IntentMatch);
    }

    [Fact]
    public void Unknown_categories_and_verdicts_are_normalized_and_nameless_items_dropped()
    {
        var feedback = OutfitAnalyzer.MapToolInput(Payloads.Parse("""
            { "status": "ok", "score": 6, "intent_match": 50, "headline": "h", "vibe": "v",
              "items": [
                { "name": "Scarf", "category": "Accessory", "verdict": "WORKS", "note": "n" },
                { "name": "Thing", "category": "gadget", "verdict": "amazing", "note": "n" },
                { "name": "", "category": "top", "verdict": "works", "note": "n" },
                "not an object"
              ],
              "working": ["a", "", "  ", 3], "one_tip": "t" }
            """));

        Assert.Equal(2, feedback.Items.Count);
        Assert.Equal("accessory", feedback.Items[0].Category);
        Assert.Equal("works", feedback.Items[0].Verdict);
        Assert.Equal("other", feedback.Items[1].Category);
        Assert.Equal("neutral", feedback.Items[1].Verdict);
        Assert.Equal(["a"], feedback.Working);
    }

    [Fact]
    public void Unknown_status_is_a_failure_not_a_guess()
    {
        var payload = Payloads.Parse("""{ "status": "maybe", "score": 5 }""");
        Assert.Throws<VisionClientException>(() => OutfitAnalyzer.MapToolInput(payload));
    }

    [Fact]
    public void Non_object_input_is_a_failure()
    {
        Assert.Throws<VisionClientException>(() => OutfitAnalyzer.MapToolInput(Payloads.Parse("[1,2]")));
    }

    [Fact]
    public void System_prompt_names_the_language_and_style_notes()
    {
        var he = OutfitAnalyzer.BuildSystemPrompt("he");
        Assert.Contains("in Hebrew (he)", he);
        Assert.Contains("שווה לנסות", he);
        Assert.DoesNotContain("{LANGUAGE", he);
        Assert.DoesNotContain("{BCP47}", he);

        var en = OutfitAnalyzer.BuildSystemPrompt("en");
        Assert.Contains("in English (en)", en);
        Assert.DoesNotContain("{LANGUAGE", en);
        Assert.Contains("Judge clothes, never the person.", en);
        Assert.Contains("A 6 is not an insult.", en);
    }

    [Fact]
    public void User_message_carries_the_english_guide_line_and_the_occasion()
    {
        var withNote = OutfitAnalyzer.BuildUserMessage(StyleIntent.OldMoney, "  brunch with the in-laws ");
        Assert.StartsWith("Stated intent: OldMoney: muted palette, quality fabrics, tailoring, no visible logos, restraint.", withNote);
        Assert.Contains("Occasion note from the wearer: brunch with the in-laws.", withNote);
        Assert.EndsWith("submit your feedback with the tool.", withNote);

        var without = OutfitAnalyzer.BuildUserMessage(StyleIntent.Sport, null);
        Assert.Contains("Occasion note from the wearer: none.", without);
    }

    [Fact]
    public void Tool_schema_matches_the_brief()
    {
        var schema = OutfitAnalyzer.ToolSchema;
        var required = schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Equal(["status", "score", "intent_match", "headline", "vibe", "items", "working", "one_tip"], required);
        Assert.Equal("submit_outfit_feedback", OutfitAnalyzer.ToolName);
        Assert.Equal("v1", OutfitAnalyzer.PromptVersion);
    }

    [Fact]
    public async Task AnalyzeAsync_builds_the_request_from_intent_language_and_image()
    {
        var fake = new FakeVisionClient();
        var analyzer = new OutfitAnalyzer(fake);

        var feedback = await analyzer.AnalyzeAsync(TestImages.Png(), "image/png", StyleIntent.Party, "birthday", "he", CancellationToken.None);

        var request = Assert.Single(fake.Requests);
        Assert.Equal("image/png", request.MediaType);
        Assert.Contains("Hebrew", request.SystemPrompt);
        Assert.Contains("Party:", request.UserText);
        Assert.Contains("birthday", request.UserText);
        Assert.Equal(OutfitAnalyzer.ToolName, request.Tool.Name);
        Assert.Equal(7, feedback.Score);
    }
}

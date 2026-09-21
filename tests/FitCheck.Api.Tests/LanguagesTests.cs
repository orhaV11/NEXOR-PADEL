using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 13, languages shipped only when real: Languages:Enabled (en and he by default) is what /api/config publishes and
/// what the stylist is asked for; a check or a comparison asked for in a language that is not enabled is written in
/// English, the row says English, and nothing else changes. The account's own preference and the server's messages keep
/// working in every language the app knows, since the files stay in the repository ready for the one setting.
/// </summary>
public class LanguagesTests
{
    [Fact]
    public void The_list_is_normalised_and_english_is_always_first()
    {
        Assert.Equal(["en", "he"], new LanguagesOptions().List);
        Assert.Equal(["en", "he", "ar"], new LanguagesOptions { Enabled = [" HE ", "ar", "he", "xx", "", "ar-EG"] }.List);
        Assert.Equal(["en"], new LanguagesOptions { Enabled = [] }.List);
        Assert.Equal(["en"], new LanguagesOptions { Enabled = ["fr"] }.List);
        Assert.Equal(["en", "ru"], new LanguagesOptions { Enabled = ["ru"] }.List);

        var options = new LanguagesOptions { Enabled = ["he"] };
        Assert.True(options.IsEnabled("en"));
        Assert.True(options.IsEnabled("he"));
        Assert.False(options.IsEnabled("ar"));
        Assert.False(options.IsEnabled(null));
        Assert.Equal("he", options.Effective("he"));
        Assert.Equal("en", options.Effective("ar"));
        Assert.Equal("en", options.Effective(null));
    }

    [Fact]
    public async Task Config_publishes_the_enabled_list_and_the_stylist_answers_only_in_one_of_them()
    {
        using var app = new TestApp();
        var config = await app.NewClient().GetFromJsonAsync<JsonElement>("/api/config");
        Assert.Equal(["en", "he"], config.GetProperty("languages").EnumerateArray().Select(x => x.GetString()).ToList());

        // A Russian-speaking account (its preference stays Russian: the files exist) checks a look: the stylist is asked in
        // English, the row says English, and the messages of that call are English too. Hebrew is live and stays Hebrew.
        var (ru, _, _) = await app.NewUserAsync("lang_ru", language: "ru");
        var (he, _, _) = await app.NewUserAsync("lang_he", language: "he");
        Assert.Equal("ru", (await ru.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("language").GetString());

        var ruCheck = await (await ru.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), language: "ru"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("en", ruCheck.GetProperty("language").GetString());
        var heCheck = await (await he.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), language: "he"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("he", heCheck.GetProperty("language").GetString());
        // No language in the form: the stored preference is tried, and it is not live either.
        var ruDefault = await (await ru.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), language: null))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("en", ruDefault.GetProperty("language").GetString());

        Assert.Equal(3, app.Vision.Requests.Count);
        Assert.Contains("in English (en)", app.Vision.Requests[0].SystemPrompt);
        Assert.Contains("in Hebrew (he)", app.Vision.Requests[1].SystemPrompt);
        Assert.Contains("in English (en)", app.Vision.Requests[2].SystemPrompt);

        // A comparison follows the same rule, and a guest with an Arabic browser gets English.
        app.Vision.Handler = request => request.HasSecondImage ? OutfitComparerTests.Pick() : Payloads.Ok();
        var comparison = await (await ru.PostAsync("/api/compare", CompareTests.CompareForm(TestImages.Jpeg(), TestImages.Jpeg(), language: "ru"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("en", comparison.GetProperty("language").GetString());
        var guest = app.NewClient();
        guest.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.215");
        guest.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ar-EG,ar;q=0.9");
        var guestCheck = await (await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), language: null))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("en", guestCheck.GetProperty("language").GetString());
        Assert.Contains("in English (en)", app.Vision.Requests[^1].SystemPrompt);

        // The error of a refused call in a language that is not live is English as well: nothing in Russian reaches anyone.
        var refused = await ru.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Garbage(), language: "ru"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, refused.StatusCode);
        Assert.Equal("Use a JPEG, PNG or WebP photo.", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task One_setting_enables_a_reviewed_language()
    {
        using var app = new TestApp { Settings = { ["Languages:Enabled:0"] = "he", ["Languages:Enabled:1"] = "ru" } };
        var config = await app.NewClient().GetFromJsonAsync<JsonElement>("/api/config");
        Assert.Equal(["en", "he", "ru"], config.GetProperty("languages").EnumerateArray().Select(x => x.GetString()).ToList());

        var (ru, _, _) = await app.NewUserAsync("lang_ru_on", language: "ru");
        var check = await (await ru.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), language: "ru"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ru", check.GetProperty("language").GetString());
        Assert.Contains("in Russian (ru)", app.Vision.Requests.Single().SystemPrompt);

        var (ar, _, _) = await app.NewUserAsync("lang_ar_off", language: "ar");
        var arCheck = await (await ar.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), language: "ar"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("en", arCheck.GetProperty("language").GetString());
    }

    [Fact]
    public void The_four_locale_files_keep_key_parity_and_the_round_13_keys_are_in_all_of_them()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "i18n"));
        var files = new[] { "en", "he", "ar", "ru" }.ToDictionary(code => code, code =>
            JsonDocument.Parse(File.ReadAllText(Path.Combine(root, code + ".json"))).RootElement.EnumerateObject().Select(p => p.Name).ToHashSet());
        foreach (var (code, keys) in files)
        {
            Assert.True(keys.SetEquals(files["en"]), $"{code}.json keys differ from en.json: {string.Join(", ", keys.Except(files["en"]).Concat(files["en"].Except(keys)))}");
        }

        foreach (var prefix in new[] { "useful.", "nooutfit.", "lang.", "install.", "offline." })
        {
            Assert.Contains(files["en"], key => key.StartsWith(prefix, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The client picks a plural form by n == 1 and nothing else, which is the whole of English and enough for Hebrew.
    /// Russian counts in three: one (1, 21, 31), a paucal (2-4) and a genitive plural (0, 5-20) - so "2 образов" reads
    /// as wrong to a Russian speaker as "2 checkses" does to an English one. The translation already answers this by
    /// writing the noun first and the number after a colon ("Образов: {n}"), which is fixed whatever the number is.
    /// This pins that shape: a count may sit directly before a word only behind a preposition that fixes the case
    /// itself. Give the client real plural categories and this test is the thing to delete.
    /// </summary>
    [Fact]
    public void The_russian_counts_are_written_so_that_two_forms_are_enough()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "i18n"));
        var en = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "en.json"))).RootElement;
        var ru = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "ru.json"))).RootElement;

        // A preposition governs the case of what follows it, so a number standing next to one counts nothing there:
        // "В {n} образах" and "{n} из {cap}" read the same for every number. Anything else is a noun being counted.
        var prepositions = new[] { "из", "в", "на", "за", "до", "от", "с", "по" };
        var counted = new System.Text.RegularExpressions.Regex(@"(?<before>\S+ )?\{(?:n|calls|count)\} (?<after>[\u0400-\u04FF]+)");

        var bases = en.EnumerateObject().Select(p => p.Name).Where(name => name.EndsWith("_one", StringComparison.Ordinal))
            .Select(name => name[..^4]).Where(name => en.TryGetProperty(name, out _)).ToList();
        Assert.NotEmpty(bases);

        foreach (var name in bases)
        {
            var text = ru.GetProperty(name).GetString()!;
            foreach (System.Text.RegularExpressions.Match match in counted.Matches(text))
            {
                var before = match.Groups["before"].Value.Trim().ToLowerInvariant();
                var after = match.Groups["after"].Value.ToLowerInvariant();
                Assert.True(prepositions.Contains(before) || prepositions.Contains(after),
                    $"ru.json \"{name}\" counts into a bare noun (\"{match.Value}\"), which only reads right for some numbers: {text}");
            }
        }
    }
}

using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FitCheck.Api.Services;

namespace FitCheck.Api.Tests;

public class LocalizerTests
{
    [Theory]
    [InlineData("he-IL", "he")]
    [InlineData("he", "he")]
    [InlineData("HE_il", "he")]
    [InlineData("ar-SA", "ar")]
    [InlineData("ar", "ar")]
    [InlineData("ru-RU", "ru")]
    [InlineData("RU", "ru")]
    [InlineData("fr-FR", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    [InlineData("   ", "en")]
    public void Match_uses_the_language_subtag_and_falls_back_to_english(string? tag, string expected)
    {
        Assert.Equal(expected, Localizer.Match(tag));
    }

    [Theory]
    [InlineData("he-IL,he;q=0.9,en-US;q=0.8", "he")]
    [InlineData("fr-FR,fr;q=0.9", "en")]
    [InlineData("fr-FR, he;q=0.5", "he")]
    [InlineData("en;q=0.3, he;q=0.9", "he")]
    [InlineData("ar-EG,ar;q=0.9,en;q=0.8", "ar")]
    [InlineData("ru-RU,ru;q=0.9,en-US;q=0.8", "ru")]
    [InlineData("fr-FR, ru;q=0.5, ar;q=0.6", "ar")]
    [InlineData("he;q=0", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    [InlineData("*", "en")]
    public void Accept_language_matching_honours_quality_order(string? header, string expected)
    {
        Assert.Equal(expected, Localizer.MatchAcceptLanguage(header));
    }

    [Fact]
    public void Messages_exist_in_every_locale_and_fall_back_to_english()
    {
        var localizer = new Localizer();
        Assert.Equal("Use a JPEG, PNG or WebP photo.", localizer.Get("en", "error.image_format"));
        Assert.NotEqual(localizer.Get("en", "error.image_format"), localizer.Get("he", "error.image_format"));
        Assert.Equal(localizer.Get("en", "error.image_format"), localizer.Get("fr", "error.image_format"));
        Assert.Equal("missing.key", localizer.Get("en", "missing.key"));
    }

    [Fact]
    public void Messages_format_arguments()
    {
        var localizer = new Localizer();
        Assert.Contains("20 checks", localizer.Get("en", "error.rate_limited", 20));
        Assert.Contains("20", localizer.Get("he", "error.rate_limited", 20));
    }

    [Fact]
    public void Language_names_are_english_for_the_prompt()
    {
        Assert.Equal("Hebrew", Localizer.LanguageName("he"));
        Assert.Equal("Arabic", Localizer.LanguageName("ar"));
        Assert.Equal("Russian", Localizer.LanguageName("ru"));
        Assert.Equal("English", Localizer.LanguageName("en"));
        Assert.Equal("English", Localizer.LanguageName("xx"));
    }

    [Fact]
    public void Every_supported_locale_has_a_language_name_and_a_dictionary()
    {
        Assert.Equal(["en", "he", "ar", "ru"], Localizer.SupportedLocales);
        var tables = Messages();
        foreach (var locale in Localizer.SupportedLocales)
        {
            Assert.True(Localizer.IsSupported(locale));
            Assert.Contains(locale, tables.Keys);
            if (locale != "en")
            {
                Assert.NotEqual("English", Localizer.LanguageName(locale));   // the prompt names the language; English is only the fallback
            }
        }
    }

    /// <summary>
    /// The parity rule for the server dictionaries, the same one the client files follow: every key of the English table
    /// exists in each other locale, no extra keys, the same {0}/{1} placeholders, nothing empty, nothing shouting and nothing
    /// left in English.
    /// </summary>
    [Theory]
    [InlineData("he")]
    [InlineData("ar")]
    [InlineData("ru")]
    public void Locale_dictionaries_mirror_the_english_keys_and_placeholders(string locale)
    {
        var tables = Messages();
        var english = tables["en"];
        var other = tables[locale];

        Assert.Empty(english.Keys.Except(other.Keys));
        Assert.Empty(other.Keys.Except(english.Keys));
        foreach (var (key, text) in english)
        {
            var translated = other[key];
            Assert.False(string.IsNullOrWhiteSpace(translated), $"{locale}: {key} is empty");
            Assert.True(Placeholders(text) == Placeholders(translated), $"{locale}: {key} has placeholders [{Placeholders(translated)}], English has [{Placeholders(text)}]");
            Assert.False(translated.Contains('!'), $"{locale}: {key} shouts");
            Assert.NotEqual(text, translated);
        }
    }

    [Fact]
    public void Arabic_and_russian_lines_are_in_their_own_scripts_with_western_digits()
    {
        var tables = Messages();
        var arabicIndicDigits = new Regex("[٠-٩۰-۹]");
        foreach (var (key, text) in tables["ar"])
        {
            Assert.True(Regex.IsMatch(text, @"\p{IsArabic}"), $"ar: {key} has no Arabic");
            Assert.False(arabicIndicDigits.IsMatch(text), $"ar: {key} uses Arabic-Indic digits; the app's numerals are Latin everywhere");
            Assert.False(Regex.IsMatch(text, @"\p{IsHebrew}|\p{IsCyrillic}"), $"ar: {key} mixes scripts");
        }

        foreach (var (key, text) in tables["ru"])
        {
            Assert.True(Regex.IsMatch(text, @"\p{IsCyrillic}"), $"ru: {key} has no Cyrillic");
            Assert.False(Regex.IsMatch(text, @"\p{IsArabic}|\p{IsHebrew}"), $"ru: {key} mixes scripts");
            // The informal register throughout: never the formal "вы" and its forms.
            Assert.False(Regex.IsMatch(text, @"(?<![\p{L}])(Вы|вы|Вас|вас|Вам|вам|Ваш\p{L}*|ваш\p{L}*)(?![\p{L}])"), $"ru: {key} slips into the formal register");
        }
    }

    [Theory]
    [InlineData("ar", "error.rate_limited", "20")]
    [InlineData("ru", "error.rate_limited", "20")]
    [InlineData("ar", "error.video_too_large", "40")]
    [InlineData("ru", "error.video_too_large", "40")]
    [InlineData("ar", "insights.line_weak", "38")]
    [InlineData("ru", "insights.line_weak", "38")]
    public void New_locales_format_arguments(string locale, string key, string expected)
    {
        var localizer = new Localizer();
        Assert.Contains(expected, localizer.Get(locale, key, expected, 30));
        Assert.NotEqual(localizer.Get("en", key, expected, 30), localizer.Get(locale, key, expected, 30));
    }

    /// <summary>
    /// Round 20: the renewal recap has seven holes, more than the theory above formats, so it gets its own check: every
    /// locale takes all seven, keeps the three numbers and the link verbatim, and differs from the English.
    /// </summary>
    [Fact]
    public void The_renewal_recap_formats_its_seven_holes_in_every_locale()
    {
        var localizer = new Localizer();
        var english = localizer.Get("en", "email.renewal_body", "noa", "14 October", "2", "3", "1", "5", "https://looks.test/#/settings");
        // Review of Round 20: each count after its noun, like the other three, so a one never reads "1 tips".
        Assert.Contains("Comparisons decided: 2.", english, StringComparison.Ordinal);
        Assert.Contains("Outfits planned: 3, worn: 1.", english, StringComparison.Ordinal);
        Assert.Contains("Tips that named something you already own: 5.", english, StringComparison.Ordinal);
        Assert.Equal("Your OREVOSH Pro renews on 14 October", localizer.Get("en", "email.renewal_subject", "14 October"));
        foreach (var locale in new[] { "he", "ar", "ru" })
        {
            var body = localizer.Get(locale, "email.renewal_body", "noa", "14 October", "2", "3", "1", "5", "https://looks.test/#/settings");
            Assert.NotEqual(english, body);
            Assert.Contains("@noa", body, StringComparison.Ordinal);
            Assert.Contains("14 October", body, StringComparison.Ordinal);
            Assert.Contains("https://looks.test/#/settings", body, StringComparison.Ordinal);
            foreach (var number in new[] { "2", "3", "1", "5" })
            {
                Assert.Contains(number, body, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("{", body, StringComparison.Ordinal);
            Assert.Contains("14 October", localizer.Get(locale, "email.renewal_subject", "14 October"), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Review of Round 20: a mailed date is the reader's own language's - its month, the Gregorian calendar, Western
    /// digits - and an unsupported language reads as English. It used to be English in all four.
    /// </summary>
    [Theory]
    [InlineData("en", "11 October")]
    [InlineData("he", "11 אוקטובר")]
    [InlineData("ar", "11 أكتوبر")]
    [InlineData("ru", "11 октября")]
    [InlineData("fr", "11 October")]
    [InlineData(null, "11 October")]
    public void A_mailed_date_is_in_the_readers_language(string? language, string expected) =>
        Assert.Equal(expected, Localizer.Day(new DateTime(2026, 10, 11, 23, 30, 0, DateTimeKind.Utc), language));

    /// <summary>
    /// Review of Round 21: the Hebrew speaks to nobody as a man, and names the plans in Hebrew, in the server's table and
    /// in the client's he.json alike. DECISIONS keeps Hebrew copy in infinitive and neutral forms, so as not to guess the
    /// reader's gender; Round 20 slipped masculine imperatives and "you" into a primary button, the activity lines, a push
    /// and the admin screen ("נסה את הטיפ, ואז תראה לי"), and wrote Pro and Free in Latin letters beside the file's פרו and
    /// חינם. The words below are the ones that crept in, each only ever a masculine imperative or address, so none may come
    /// back. "OREVOSH Pro" stays the product's name in the mail subjects.
    /// </summary>
    [Fact]
    public void Hebrew_speaks_to_nobody_as_a_man_and_names_the_plans_in_hebrew()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "i18n"));
        var client = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "he.json"))).RootElement.EnumerateObject()
            .Select(p => (Where: "he.json " + p.Name, Text: p.Value.GetString() ?? ""));
        var server = Messages()["he"].Select(p => (Where: "Localizer " + p.Key, Text: p.Value));
        string[] masculine = ["נסה", "תראה", "בדוק", "הירשם", "תודיע", "מתלבט", "התחל", "אתה", "הפעל", "הענק", "הסר", "תבקש", "תפעיל", "תוסיף", "שתלחץ"];
        var addressed = new Regex(@"(?<![\u05D0-\u05EA])ו?(?:" + string.Join("|", masculine) + @")(?![\u05D0-\u05EA])");
        var latinPlan = new Regex(@"(?<!OREVOSH )\bPro\b|\bFree\b");
        foreach (var (where, text) in client.Concat(server))
        {
            Assert.False(addressed.IsMatch(text), $"{where} addresses the reader as a man (\"{addressed.Match(text).Value}\"): {text}");
            Assert.False(latinPlan.IsMatch(text), $"{where} names a plan in Latin letters; the Hebrew says פרו and חינם: {text}");
        }
    }

    private static Dictionary<string, Dictionary<string, string>> Messages()
    {
        var field = typeof(Localizer).GetField("Messages", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return Assert.IsType<Dictionary<string, Dictionary<string, string>>>(field!.GetValue(null));
    }

    /// <summary>The composite-format holes of a line, sorted: "{0},{1}" for "Try one under {0} MB or shorter than {1} seconds."</summary>
    private static string Placeholders(string text) =>
        string.Join(",", Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order());
}

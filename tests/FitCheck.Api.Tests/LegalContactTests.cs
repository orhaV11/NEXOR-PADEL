using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 16 — the address on the two pages that promise a reader a human. The terms of use and the privacy policy tell
/// people where to write about a privacy question, about deleting their data, and about an account belonging to someone
/// under 16. That address was a constant in the four translation files, which makes it right for whoever owns that
/// domain and a dead letterbox for every other person who runs this code. It now comes from the server, and the pages
/// carry a placeholder rather than an address.
/// </summary>
public class LegalContactTests
{
    private static readonly string[] Locales = ["en", "he", "ar", "ru"];
    private static readonly string[] ContactKeys = ["legal.terms_10", "legal.privacy_10"];

    private static Dictionary<string, string> Strings(string code)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "i18n", code + ".json"));
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    [Fact]
    public void No_locale_file_carries_an_address_of_its_own()
    {
        foreach (var code in Locales)
        {
            var strings = Strings(code);
            foreach (var key in ContactKeys)
            {
                Assert.True(strings.ContainsKey(key), $"{code}.json is missing {key}");
                Assert.Contains("{email}", strings[key], StringComparison.Ordinal);
            }

            // And nowhere else in the file either: an address baked into any string is one nobody can change.
            foreach (var (key, value) in strings)
            {
                Assert.False(value.Contains("@orevosh.app", StringComparison.OrdinalIgnoreCase),
                    $"{code}.json \"{key}\" names an address the owner of this server does not have: {value}");
            }
        }
    }

    [Fact]
    public void The_address_is_the_setting_then_the_senders_own_mailbox_then_nothing()
    {
        Assert.Equal("write@here.test", LegalOptions.Contact(new LegalOptions { ContactEmail = " write@here.test " }, new EmailOptions()));
        // No setting: the mailbox the server already sends from, which the owner reads by definition.
        Assert.Equal("from@here.test", LegalOptions.Contact(new LegalOptions(), new EmailOptions { From = "from@here.test" }));
        // The setting wins over it.
        Assert.Equal("write@here.test", LegalOptions.Contact(new LegalOptions { ContactEmail = "write@here.test" }, new EmailOptions { From = "from@here.test" }));
        // A From written as a display name around the address gives up the address, not the whole envelope line.
        Assert.Equal("from@here.test", LegalOptions.Contact(new LegalOptions(), new EmailOptions { From = "OREVOSH <from@here.test>" }));
        Assert.Equal("from@here.test", LegalOptions.Contact(new LegalOptions(), new EmailOptions { From = "  OREVOSH  < from@here.test > " }));
        Assert.Equal("from@here.test", LegalOptions.Contact(new LegalOptions(), new EmailOptions { From = "<from@here.test>" }));
        // Nothing between the brackets is not an address: fall through to what was written rather than to "".
        Assert.Equal("OREVOSH <>", LegalOptions.Contact(new LegalOptions(), new EmailOptions { From = "OREVOSH <>" }));

        // Neither: null, and the pages leave the section out rather than name nobody.
        Assert.Null(LegalOptions.Contact(new LegalOptions(), new EmailOptions()));
        Assert.Null(LegalOptions.Contact(new LegalOptions { ContactEmail = "   " }, new EmailOptions { From = "  " }));
    }

    [Fact]
    public async Task Config_publishes_it_so_the_pages_can_print_it()
    {
        // No setting: TestApp sends mail as "OREVOSH <noreply@test.invalid>", and that address is what readers get.
        using (var fallback = new TestApp())
        {
            var config = await fallback.NewClient().GetFromJsonAsync<JsonElement>("/api/config");
            Assert.Equal("noreply@test.invalid", config.GetProperty("contactEmail").GetString());
        }

        // A server with no mail either: null, and the pages drop the section.
        using (var plain = new TestApp { Settings = { ["Email:Host"] = "", ["Email:From"] = "" } })
        {
            var config = await plain.NewClient().GetFromJsonAsync<JsonElement>("/api/config");
            // A null is left out of the document entirely (AppJson drops nulls), which the client reads as no address.
            Assert.True(!config.TryGetProperty("contactEmail", out var none) || none.ValueKind == JsonValueKind.Null,
                "with no Legal__ContactEmail and no Email__From, /api/config must not name an address: " + none);
        }

        using var app = new TestApp { Settings = { ["Legal:ContactEmail"] = "hello@orevosh.test" } };
        var set = await app.NewClient().GetFromJsonAsync<JsonElement>("/api/config");
        Assert.Equal("hello@orevosh.test", set.GetProperty("contactEmail").GetString());
    }

    /// <summary>
    /// The page drops a section built around an address when the server has none: "write to us at ." is a worse promise
    /// than no promise. This pins the mechanism the view uses to recognise such a section.
    /// </summary>
    [Fact]
    public void The_view_reads_the_address_from_the_config_and_skips_the_section_without_one()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "app", "views", "legal.js"));
        var source = File.ReadAllText(path);
        Assert.Contains("state.config && state.config.contactEmail", source, StringComparison.Ordinal);
        Assert.Contains("if (!email && t(key).includes('{email}')) continue;", source, StringComparison.Ordinal);
        Assert.Contains("t(key, { email })", source, StringComparison.Ordinal);
    }
}

/// <summary>
/// Round 17 — the way out of the app.
/// <para>
/// The terms, the privacy policy and the community guidelines had routes, pages and translations in four languages,
/// and NOTHING inside the app linked to them. They appeared on the signup form, inside a guard that hid them on the
/// login screen, and on two landing pages nothing links to. So the moment somebody had an account, the documents they
/// had just agreed to were unreachable from every screen — and the privacy policy asks a reader to write in about an
/// account belonging to someone under 16 without saying where.
/// </para>
/// <para>
/// This walks the settings screen's own source rather than asserting a list, because a list would agree with itself
/// while the screen drifted. The address row is conditional on purpose: a mailto with nothing behind it is worse than
/// no row, so it is drawn only where the server has an address to give (Legal:ContactEmail, else Email:From).
/// </para>
/// </summary>
public class SettingsLinksTests
{
    private static string Source(string view)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "FitCheck.Api", "wwwroot", "app", "views", view));
        Assert.True(File.Exists(path), "not where this test looks for it: " + path);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Settings_carries_the_documents_and_a_way_to_reach_a_human()
    {
        var settings = Source("settings.js");

        foreach (var route in new[] { "#/terms", "#/privacy", "#/guidelines" })
        {
            Assert.Contains($"href: '{route}'", settings, StringComparison.Ordinal);
        }

        // The address row: drawn from the server's value, and only when there is one.
        Assert.Contains("state.config.contactEmail", settings, StringComparison.Ordinal);
        Assert.Contains("'mailto:' + contact", settings, StringComparison.Ordinal);
        Assert.Contains("contact ?", settings, StringComparison.Ordinal);

        // Every locale can name the row.
        foreach (var code in new[] { "en", "he", "ar", "ru" })
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "FitCheck.Api", "wwwroot", "i18n", code + ".json"));
            var strings = JsonDocument.Parse(File.ReadAllText(path)).RootElement
                .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            foreach (var key in new[] { "settings.contact", "legal.terms_title", "legal.privacy_title", "guidelines.title" })
            {
                Assert.True(strings.ContainsKey(key), $"{code}.json is missing {key}");
                Assert.False(string.IsNullOrWhiteSpace(strings[key]), $"{code}: {key} is empty");
            }
        }
    }

    /// <summary>
    /// Round 17 — the camera opens to a visitor with no account. The guest check is the whole funnel: one free look is
    /// what turns a stranger into an account. The camera used to bounce anyone without a session back to the check
    /// screen, and the check screen hid the "Take a photo" row from them, so the one person the guest path exists for
    /// was sent to a file picker. Somebody standing in front of a mirror has no file to pick.
    /// </summary>
    [Fact]
    public void The_camera_opens_to_a_guest()
    {
        var camera = Source("camera.js");
        Assert.DoesNotContain("if (!state.me) { redirect('#/check'); return; }", camera, StringComparison.Ordinal);
        // Not by loosening one line while another still checks: the camera must not consult the session at all.
        Assert.DoesNotContain("state.me", camera, StringComparison.Ordinal);

        // And the row that opens it is offered to everybody.
        var check = Source("check.js");
        Assert.Contains("row('media-camera'", check, StringComparison.Ordinal);
        Assert.DoesNotContain("if (state.me) list.appendChild(row('media-camera'", check, StringComparison.Ordinal);
    }
}

/// <summary>
/// Round 18 — the documents describe the app that runs. Version 3 of the terms said Pro was "a number of checks a day"
/// (the plans are a month with a day behind it since Round 17), that to cancel you "write to us" (Settings has carried
/// the payment provider's portal since Round 13), and nothing about store links earning a commission (Round 16). The
/// privacy policy said the model provider gets "the photo and nothing about you", while the stylist is also sent the
/// names of wardrobe pieces and, when learning is on, a summary of which tips were kept and turned down; and it never
/// named the provider, which the guest disclosure on the check screen already does.
/// <para>
/// Pinned per language, because a translation that still promises the old thing is a promise the owner cannot keep in
/// that language. The phrases are the ones a reader would look for: the button's own label for cancelling, the
/// provider's name, the word for commission, and the word for month.
/// </para>
/// </summary>
public class LegalVersionFourTests
{
    private static Dictionary<string, string> Strings(string code)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "FitCheck.Api", "wwwroot", "i18n", code + ".json"));
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement
            .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    [Theory]
    [InlineData("en", "Manage subscription", "write to us", "commission", "month", "nothing about you", "store links")]
    [InlineData("he", "ניהול המינוי", "כותבים לנו", "עמלה", "חודש", "שום דבר עליכם", "קישורי חנויות")]
    [InlineData("ar", "إدارة الاشتراك", "رسالة إلينا", "عمولة", "شهر", "لا شيء عنك", "روابط المتاجر")]
    [InlineData("ru", "подпиской", "напиши нам", "комисси", "месяц", "ничего о тебе", "ссылок на магазины")]
    public void The_terms_and_the_policy_say_what_the_app_does(
        string code, string manage, string writeToUs, string commission, string month, string nothingAboutYou, string storeLinks)
    {
        var strings = Strings(code);

        // Cancelling is a button in Settings, and the terms name it by the button's own words.
        var terms = strings["legal.terms_6"];
        Assert.Contains(manage, terms, StringComparison.Ordinal);
        Assert.DoesNotContain(writeToUs, terms, StringComparison.Ordinal);
        // The plans are monthly, and a tap on a store link can earn the owner a commission.
        Assert.Contains(month, terms, StringComparison.Ordinal);
        Assert.Contains(commission, terms, StringComparison.Ordinal);

        // The provider is named, and the taste summary the stylist is sent is disclosed.
        var provider = strings["legal.privacy_2"];
        Assert.Contains("Anthropic", provider, StringComparison.Ordinal);
        // The same name the guest disclosure on the check screen gives, so the two never disagree.
        Assert.Contains("Anthropic", strings["guest.disclosure"], StringComparison.Ordinal);

        // "Nothing about you" was never true once the wardrobe was sent; the short version no longer says it.
        Assert.DoesNotContain(nothingAboutYou, strings["legal.privacy_intro"], StringComparison.Ordinal);

        // What is kept about a tapped store link is listed with everything else that is kept.
        Assert.Contains(storeLinks, strings["legal.privacy_1"], StringComparison.Ordinal);
    }

    /// <summary>The button the terms point at exists under that label in every language, so the sentence stays true.</summary>
    [Theory]
    [InlineData("en", "Manage subscription")]
    [InlineData("he", "ניהול המינוי")]
    [InlineData("ar", "إدارة الاشتراك")]
    [InlineData("ru", "Управление подпиской")]
    public void The_button_the_terms_point_at_is_labelled_that_way(string code, string label)
    {
        Assert.Equal(label, Strings(code)["billing.manage"]);
        var settings = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "FitCheck.Api", "wwwroot", "app", "views", "settings.js")));
        Assert.Contains("id: 'billing-manage', text: t('billing.manage')", settings, StringComparison.Ordinal);
    }

    /// <summary>
    /// A rewritten document is a new version with a new date, or the "version and date at the bottom" promise in the terms
    /// is broken. Round 19 moved it to 5: Tomorrow and the forecast are in the privacy policy. Round 20's review moved it
    /// to 6 a day later: what Round 20 keeps about a person is in it too.
    /// </summary>
    [Fact]
    public void The_documents_carry_version_six()
    {
        var legal = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "FitCheck.Api", "wwwroot", "app", "views", "legal.js")));
        Assert.Contains("const VERSION = '6';", legal, StringComparison.Ordinal);
        Assert.Contains("const DATED = '2026-09-28';", legal, StringComparison.Ordinal);
    }

    /// <summary>
    /// Round 20's review: what Round 20 keeps is in "what we store" in every language — which of our links a person
    /// arrived through (Users.Source, Checks.Source), the morning ping's receipt with when it was opened (TomorrowPushes),
    /// and the ask to hear when the stylist is back (StylistBack) — and the cookies section names the link and the invite
    /// the browser keeps until signup (invite.js), not only the language.
    /// </summary>
    [Theory]
    [InlineData("en", "links you arrived through", "morning ping", "stylist is back", "the link you arrived through")]
    [InlineData("he", "מהקישורים שלנו הגעתם", "פינג הבוקר", "הסטייליסט חוזר", "הקישור שהביאו אתכם")]
    [InlineData("ar", "من روابطنا وصلت", "تنبيه الصباح", "يعود المصمّم", "الرابط الذي وصلت عبره")]
    [InlineData("ru", "из наших ссылок ты пришёл", "утреннего напоминания", "стилист вернётся", "ссылку, по которой ты пришёл")]
    public void The_policy_lists_what_round_20_keeps(string code, string entryLink, string morningPing, string stylistBack, string kept)
    {
        var strings = Strings(code);
        var stored = strings["legal.privacy_1"];
        Assert.Contains(entryLink, stored, StringComparison.Ordinal);
        Assert.Contains(morningPing, stored, StringComparison.Ordinal);
        Assert.Contains(stylistBack, stored, StringComparison.Ordinal);
        Assert.Contains(kept, strings["legal.privacy_6"], StringComparison.Ordinal);
        // The sentences that were there stay: the store links, the planned outfit, the two cookies and "no analytics".
        Assert.Contains("Open-Meteo", strings["legal.privacy_2"], StringComparison.Ordinal);
        Assert.Contains("90", strings["legal.privacy_6"], StringComparison.Ordinal);
    }

    /// <summary>
    /// Round 19: the policy says what a planned outfit keeps (never where the person was) and that a rounded place is
    /// passed once to Open-Meteo and kept by nobody — in every language, by the service's name.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("he")]
    [InlineData("ar")]
    [InlineData("ru")]
    public void The_policy_names_the_forecast_service_and_what_a_planned_outfit_keeps(string code)
    {
        var strings = Strings(code);
        Assert.Contains("Open-Meteo", strings["legal.privacy_2"], StringComparison.Ordinal);
        Assert.True(strings["legal.privacy_1"].Length > 200, "the what-we-store list carries the planned outfit's line");
    }
}

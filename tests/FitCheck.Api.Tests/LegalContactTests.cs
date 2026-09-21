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

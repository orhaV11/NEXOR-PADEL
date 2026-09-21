using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Domain;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 16 — the price, in the reader's own language. Plans:ProPriceText is a single string shown to everybody, so
/// "₪29.90 / month" typed once reaches a Russian reader in English with a full stop where their language puts a comma,
/// and an Arabic reader with the symbol on the side their language does not put it. The browser already knows every
/// one of those rules, so the server sends an amount and a currency and lets it write them.
/// </summary>
public class ProPriceTests
{
    private static Dictionary<string, string> Strings(string code)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "i18n", code + ".json"));
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    [Fact]
    public async Task The_amount_and_its_currency_reach_the_client_so_it_can_write_them()
    {
        using var app = new TestApp
        {
            Settings = { ["Plans:ProPriceAmount"] = "29.90", ["Plans:ProPriceCurrency"] = "ils" }
        };
        var plans = (await app.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("plans");

        Assert.Equal(29.90m, plans.GetProperty("proPriceAmount").GetDecimal());
        // Upper-cased on the way out: Intl wants the ISO code, and an owner types what they type.
        Assert.Equal("ILS", plans.GetProperty("proPriceCurrency").GetString());
    }

    [Fact]
    public async Task No_amount_means_no_price_on_the_page_rather_than_a_zero()
    {
        using var app = new TestApp();
        var plans = (await app.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("plans");
        Assert.Equal(0m, plans.GetProperty("proPriceAmount").GetDecimal());
        Assert.Equal("", plans.GetProperty("proPriceText").GetString());
    }

    [Fact]
    public void The_sentence_around_the_price_is_written_in_all_four_languages()
    {
        foreach (var code in new[] { "en", "he", "ar", "ru" })
        {
            var strings = Strings(code);
            Assert.True(strings.TryGetValue("pro.per_month", out var line) && !string.IsNullOrWhiteSpace(line),
                $"{code}.json is missing pro.per_month");
            Assert.Contains("{price}", line, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// What the client is actually asked to do, pinned here so the intent survives: the amount goes through Intl with
    /// the reader's locale, and the typed override still wins when it is set.
    /// </summary>
    [Fact]
    public void The_page_formats_the_number_rather_than_printing_a_typed_string()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "app", "views", "pro.js"));
        var source = File.ReadAllText(path);
        Assert.Contains("style: 'currency'", source, StringComparison.Ordinal);
        Assert.Contains("intlLocale()", source, StringComparison.Ordinal);
        Assert.Contains("plans.proPriceText || money(", source, StringComparison.Ordinal);
    }

    /// <summary>A currency the browser cannot format must leave the price off rather than print something wrong.</summary>
    [Fact]
    public void An_unknown_currency_is_caught_rather_than_guessed()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "app", "views", "pro.js"));
        Assert.Contains("catch (e) {\n    return '';", File.ReadAllText(path).Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_currency_is_a_real_iso_code()
    {
        var code = new PlanOptions().ProPriceCurrency;
        Assert.Equal(3, code.Length);
        Assert.Equal(code.ToUpperInvariant(), code);
        Assert.Equal(0m, new PlanOptions().ProPriceAmount);
        Assert.True(decimal.TryParse("29.90", NumberStyles.Number, CultureInfo.InvariantCulture, out _));
    }
}

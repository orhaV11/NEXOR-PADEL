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

    [Fact]
    public async Task Every_currency_this_server_sells_in_reaches_the_client_with_the_regions_that_use_it()
    {
        using var app = new TestApp
        {
            Settings =
            {
                ["Plans:ProPriceAmount"] = "29.90",
                ["Plans:ProPriceCurrency"] = "ILS",
                ["Plans:ProPrices:USD"] = "7.99",
                ["Plans:ProPrices:eur"] = "6.99",
                // An amount of zero is not a price, and a code that is not a code is not one either.
                ["Plans:ProPrices:GBP"] = "0",
                ["Plans:ProPrices:NONSENSE"] = "5"
            }
        };
        var plans = (await app.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("plans");
        var prices = plans.GetProperty("proPrices");

        Assert.Equal(29.90m, prices.GetProperty("ILS").GetDecimal());
        Assert.Equal(7.99m, prices.GetProperty("USD").GetDecimal());
        Assert.Equal(6.99m, prices.GetProperty("EUR").GetDecimal());   // upper-cased on the way out
        Assert.False(prices.TryGetProperty("GBP", out _), "a price of zero is not a price");
        Assert.False(prices.TryGetProperty("NONSENSE", out _), "only ISO 4217 codes travel");

        var regions = plans.GetProperty("currencyByRegion");
        Assert.Equal("ILS", regions.GetProperty("IL").GetString());
        Assert.Equal("EUR", regions.GetProperty("DE").GetString());
        Assert.Equal("USD", regions.GetProperty("US").GetString());
        Assert.Equal("RUB", regions.GetProperty("RU").GetString());
    }

    [Fact]
    public void A_server_can_correct_a_region_without_retyping_the_whole_map()
    {
        var plans = new PlanOptions { CurrencyByRegion = { ["ch"] = "eur", ["ZZ"] = "USD" } };
        var map = plans.RegionCurrencies();

        Assert.Equal("EUR", map["CH"]);          // corrected, and both halves case-insensitive
        Assert.Equal("USD", map["ZZ"]);          // a region the defaults do not carry
        Assert.Equal("ILS", map["IL"]);          // everything else is still there
        Assert.True(map.Count > 50, "the built-in map should cover more than a handful of places");
    }

    /// <summary>
    /// The one that says what this is NOT: nothing multiplies by an exchange rate. Each currency is a price somebody
    /// chose, so a rate that moved overnight cannot move what a person is charged.
    /// </summary>
    [Fact]
    public void Prices_are_set_per_currency_and_never_converted()
    {
        var plans = new PlanOptions { ProPriceAmount = 29.90m, ProPriceCurrency = "ILS", ProPrices = { ["USD"] = 7.99m } };
        var table = plans.PriceTable();

        Assert.Equal(29.90m, table["ILS"]);
        Assert.Equal(7.99m, table["USD"]);
        // 7.99 is not 29.90 at any rate anybody quoted: it is the price of the American subscription.
        Assert.Equal(2, table.Count);

        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "app", "views", "pro.js")));
        // The client picks a currency it has a price for, and picks it by region rather than by language.
        Assert.Contains("new Intl.Locale(tag).region", source, StringComparison.Ordinal);
        Assert.Contains("prices[currency] > 0", source, StringComparison.Ordinal);
        // And the amount is READ from the table, never computed: no rate, no multiplication, no rounding of one price
        // into another. (Searching the source for the word "exchange" would only find the comment saying we do not.)
        Assert.Contains("plans.proPrices[currency]) || plans.proPriceAmount", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The home market is local and the rest of the world is dollars. Without this, the home currency is the only
    /// fallback and a reader in London meets a price in shekels.
    /// </summary>
    [Fact]
    public void A_country_this_server_does_not_price_in_is_shown_the_world_currency()
    {
        var plans = new PlanOptions
        {
            ProPriceAmount = 29.90m, ProPriceCurrency = "ILS",
            ProPrices = { ["USD"] = 7.99m, ["EUR"] = 6.99m },
            ProPriceWorldCurrency = "usd"
        };
        Assert.Equal("USD", plans.FallbackCurrency());

        // A world currency with no price behind it is ignored rather than shown empty.
        Assert.Equal("ILS", new PlanOptions { ProPriceAmount = 29.90m, ProPriceCurrency = "ILS", ProPriceWorldCurrency = "GBP" }.FallbackCurrency());
        // Unset: the home currency, which is the behaviour a single-market server wants.
        Assert.Equal("ILS", new PlanOptions { ProPriceAmount = 29.90m, ProPriceCurrency = "ILS" }.FallbackCurrency());
    }
}

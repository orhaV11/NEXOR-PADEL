using System.Net;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 19 — the forecast behind "what should I wear tomorrow". The server asks Open-Meteo for three days at a place
/// rounded to a kilometre, keeps the answer in memory for a while, and never lets a slow or broken forecast reach the
/// person as anything but a missing line. The numbers the model is told are the ones Open-Meteo gave, rounded here.
/// </summary>
public class WeatherTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => Json(HttpStatusCode.OK, ThreeDays);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(Respond(request));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(5) };
    }

    /// <summary>Open-Meteo's daily block as it really comes back: parallel arrays, dated in the place's own zone.</summary>
    private const string ThreeDays = """
        {
          "latitude": 32.08, "longitude": 34.78, "timezone": "Asia/Jerusalem",
          "daily_units": { "time": "iso8601", "weather_code": "wmo code", "temperature_2m_max": "°C", "temperature_2m_min": "°C", "precipitation_probability_max": "%" },
          "daily": {
            "time": ["2026-09-27", "2026-09-28", "2026-09-29"],
            "weather_code": [0, 3, 61],
            "temperature_2m_max": [29.1, 28.4, 24.6],
            "temperature_2m_min": [21.0, 20.5, 19.2],
            "precipitation_probability_max": [0, 10, 65]
          }
        }
        """;

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (Weather Weather, ScriptedHandler Handler, FakeClock Clock) Create(WeatherOptions? options = null)
    {
        var handler = new ScriptedHandler();
        var clock = new FakeClock { Now = new DateTime(2026, 9, 27, 6, 0, 0, DateTimeKind.Utc) };
        var weather = new Weather(new Factory(handler), Options.Create(options ?? new WeatherOptions()), clock, NullLogger<Weather>.Instance);
        return (weather, handler, clock);
    }

    [Fact]
    public async Task Tomorrow_is_the_second_day_of_the_answer_rounded_and_worded()
    {
        var (weather, handler, _) = Create();
        var forecast = await weather.ForAsync(32.0853, 34.7818, new DateOnly(2026, 9, 28), CancellationToken.None);

        Assert.NotNull(forecast);
        Assert.Equal(new DateOnly(2026, 9, 28), forecast.Day);
        Assert.Equal(28.4, forecast.MaxC);
        Assert.Equal(20.5, forecast.MinC);
        Assert.Equal(10, forecast.RainChance);
        Assert.Equal("cloudy", forecast.Sky);
        Assert.Equal("high 28 C, low 21 C, chance of rain 10%, cloudy", forecast.Figure());

        // What travelled: the daily arrays for three days, the place to two decimals, and no key on the keyless host.
        var url = Assert.Single(handler.Requests);
        Assert.Equal("api.open-meteo.com", url.Host);
        Assert.Contains("latitude=32.09", url.Query, StringComparison.Ordinal);
        Assert.Contains("longitude=34.78", url.Query, StringComparison.Ordinal);
        Assert.Contains("daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max", url.Query, StringComparison.Ordinal);
        Assert.Contains("forecast_days=3", url.Query, StringComparison.Ordinal);
        Assert.Contains("timezone=auto", url.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("apikey", url.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("34.7818", url.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_place_asked_twice_is_fetched_once_until_the_cache_ages()
    {
        var (weather, handler, clock) = Create(new WeatherOptions { CacheMinutes = 30 });
        var first = await weather.ForAsync(32.08, 34.78, new DateOnly(2026, 9, 27), CancellationToken.None);
        // Two streets away, another day of the same answer: the same cell, no second request.
        var second = await weather.ForAsync(32.11, 34.81, new DateOnly(2026, 9, 29), CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("clear", first.Sky);
        Assert.Equal("rain", second.Sky);
        Assert.Equal(65, second.RainChance);
        Assert.Single(handler.Requests);

        // A day the answer does not carry is null, still without a request.
        Assert.Null(await weather.ForAsync(32.08, 34.78, new DateOnly(2026, 10, 2), CancellationToken.None));
        Assert.Single(handler.Requests);

        // Another city is another request; the cache aging out is another request.
        Assert.NotNull(await weather.ForAsync(31.77, 35.21, new DateOnly(2026, 9, 27), CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
        clock.Now = clock.UtcNow.AddMinutes(31);
        Assert.NotNull(await weather.ForAsync(32.08, 34.78, new DateOnly(2026, 9, 27), CancellationToken.None));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Nothing_is_asked_for_a_place_off_the_earth_or_while_the_feature_is_off()
    {
        var (weather, handler, _) = Create();
        Assert.Null(await weather.ForAsync(91, 0, new DateOnly(2026, 9, 27), CancellationToken.None));
        Assert.Null(await weather.ForAsync(0, -181, new DateOnly(2026, 9, 27), CancellationToken.None));
        Assert.Null(await weather.ForAsync(double.NaN, 0, new DateOnly(2026, 9, 27), CancellationToken.None));
        Assert.Empty(handler.Requests);

        var (off, offHandler, _) = Create(new WeatherOptions { Enabled = false });
        Assert.False(off.Enabled);
        Assert.Null(await off.ForAsync(32.08, 34.78, new DateOnly(2026, 9, 27), CancellationToken.None));
        Assert.Empty(offHandler.Requests);
    }

    [Fact]
    public async Task A_forecast_that_fails_is_no_forecast_and_is_remembered_only_briefly()
    {
        var (weather, handler, clock) = Create();
        handler.Respond = _ => Json(HttpStatusCode.InternalServerError, """{ "error": true, "reason": "down" }""");
        Assert.Null(await weather.ForAsync(32.08, 34.78, new DateOnly(2026, 9, 27), CancellationToken.None));

        // Each failure below is a different place, so each is really asked and really fails in its own way.
        handler.Respond = _ => throw new HttpRequestException("connection refused");
        Assert.Null(await weather.ForAsync(33.08, 34.78, new DateOnly(2026, 9, 27), CancellationToken.None));

        handler.Respond = _ => throw new TaskCanceledException("timed out");
        Assert.Null(await weather.ForAsync(34.08, 34.78, new DateOnly(2026, 9, 27), CancellationToken.None));

        handler.Respond = _ => Json(HttpStatusCode.OK, "not json at all");
        Assert.Null(await weather.ForAsync(35.08, 34.78, new DateOnly(2026, 9, 27), CancellationToken.None));

        handler.Respond = _ => Json(HttpStatusCode.OK, """{ "daily": { "time": ["2026-09-27"], "temperature_2m_max": [29.0] } }""");
        Assert.Null(await weather.ForAsync(36.08, 34.78, new DateOnly(2026, 9, 27), CancellationToken.None));
        Assert.Equal(5, handler.Requests.Count);

        // Each of those was asked once and the failure remembered briefly, so a service that is down is not hammered by
        // every tap: within five minutes nothing more is sent, after five minutes the next tap asks again and gets it.
        var asked = handler.Requests.Count;
        handler.Respond = _ => Json(HttpStatusCode.OK, ThreeDays);
        Assert.Null(await weather.ForAsync(32.08, 34.78, new DateOnly(2026, 9, 27), CancellationToken.None));
        Assert.Equal(asked, handler.Requests.Count);
        clock.Now = clock.UtcNow + Weather.FailureMemory + TimeSpan.FromSeconds(1);
        Assert.NotNull(await weather.ForAsync(32.08, 34.78, new DateOnly(2026, 9, 27), CancellationToken.None));
        Assert.Equal(asked + 1, handler.Requests.Count);
    }

    [Fact]
    public async Task A_key_moves_the_call_to_the_customer_host_and_rides_along()
    {
        var (weather, handler, _) = Create(new WeatherOptions { ApiKey = "om-secret" });
        Assert.NotNull(await weather.ForAsync(32.08, 34.78, new DateOnly(2026, 9, 27), CancellationToken.None));
        var url = Assert.Single(handler.Requests);
        Assert.Equal("customer-api.open-meteo.com", url.Host);
        Assert.Contains("apikey=om-secret", url.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "clear", "clear")]
    [InlineData(1, "clear", "clear")]
    [InlineData(2, "cloudy", "cloudy")]
    [InlineData(3, "cloudy", "cloudy")]
    [InlineData(45, "fog", "fog")]
    [InlineData(48, "fog", "fog")]
    [InlineData(53, "drizzle", "drizzle")]
    [InlineData(63, "rain", "rain")]
    [InlineData(67, "rain", "rain")]
    [InlineData(73, "snow", "snow")]
    [InlineData(77, "snow", "snow")]
    [InlineData(80, "rain", "rain")]
    [InlineData(82, "rain", "rain")]
    [InlineData(85, "snow", "snow")]
    [InlineData(95, "storm", "storms")]
    [InlineData(99, "storm", "storms")]
    [InlineData(-1, "cloudy", "cloudy")]
    [InlineData(200, "storm", "storms")]
    public void Every_wmo_code_is_one_of_seven_buckets(int code, string sky, string words)
    {
        Assert.Equal(sky, Weather.Sky(code));
        Assert.Equal(words, Weather.SkyWords(sky));
        Assert.Contains(sky, Weather.Skies);
    }

    [Fact]
    public void The_parser_keeps_the_days_it_can_read_and_rounds_what_it_keeps()
    {
        using var doc = JsonDocument.Parse("""
            {
              "daily": {
                "time": ["2026-09-27", "not-a-date", "2026-09-29"],
                "weather_code": [0, 3, null],
                "temperature_2m_max": [29.16, 28.4, 24.64],
                "temperature_2m_min": [21.04, 20.5, 19.25],
                "precipitation_probability_max": [0, 10, 65.6]
              }
            }
            """);
        var days = Weather.Parse(doc.RootElement);
        Assert.NotNull(days);
        Assert.Equal(2, days.Count);
        Assert.Equal(29.2, days[new DateOnly(2026, 9, 27)].MaxC);
        Assert.Equal(21.0, days[new DateOnly(2026, 9, 27)].MinC);
        var later = days[new DateOnly(2026, 9, 29)];
        Assert.Equal(24.6, later.MaxC);
        Assert.Equal(19.3, later.MinC);
        Assert.Equal(66, later.RainChance);
        // A missing code is an unknown sky, not a clear one: the prompt and the pill leave it out.
        Assert.Null(later.Code);
        Assert.Null(later.Sky);
        Assert.Equal("high 25 C, low 19 C, chance of rain 66%", later.Figure());
    }

    /// <summary>Round 19 review: a missing rain chance is unknown too, never "0%"; the temperatures alone are still a forecast.</summary>
    [Fact]
    public void A_rain_chance_the_service_left_out_is_not_written_as_none()
    {
        using var doc = JsonDocument.Parse("""
            {
              "daily": {
                "time": ["2026-09-28"],
                "weather_code": [61],
                "temperature_2m_max": [14.0],
                "temperature_2m_min": [8.0],
                "precipitation_probability_max": [null]
              }
            }
            """);
        var day = Weather.Parse(doc.RootElement)![new DateOnly(2026, 9, 28)];
        Assert.Null(day.RainChance);
        Assert.Equal("rain", day.Sky);
        Assert.Equal("high 14 C, low 8 C, rain", day.Figure());
    }

    /// <summary>
    /// Round 19 review: a base URL without a scheme (or an empty one) made the client throw before any request, and that
    /// reached the person as a failed outfit. It is no forecast and one warning, and nothing is asked.
    /// </summary>
    [Theory]
    [InlineData("api.open-meteo.com")]
    [InlineData("")]
    [InlineData("ftp://weather.example")]
    public async Task A_base_url_that_is_not_an_absolute_web_address_is_no_forecast_and_no_exception(string baseUrl)
    {
        var (weather, handler, _) = Create(new WeatherOptions { BaseUrl = baseUrl });
        Assert.Null(await weather.ForAsync(32.08, 34.78, new DateOnly(2026, 9, 28), CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Options_pick_the_host_by_the_key()
    {
        Assert.Equal("https://api.open-meteo.com", new WeatherOptions().Host());
        Assert.Equal("https://customer-api.open-meteo.com", new WeatherOptions { ApiKey = "k" }.Host());
        Assert.Equal("https://mirror.example", new WeatherOptions { BaseUrl = "https://mirror.example/" }.Host());
    }
}

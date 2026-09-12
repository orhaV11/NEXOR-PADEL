using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Endpoints;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 9: signup needs a date of birth ("yyyy-MM-dd"), 16 and over on the day. The 16+ checkbox is ignored
/// (AuthEndpointTests says so); the date is stored as a UTC date and never leaves the server.
/// </summary>
public class SignupDobTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public SignupDobTests(TestApp app) => _app = app;

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private Task<HttpResponseMessage> SignupAsync(string handle, object body) => _app.NewClient().PostAsJsonAsync("/api/auth/signup", body);

    private static async Task<string> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()!;

    [Fact]
    public async Task A_signup_without_a_birth_date_is_refused_in_the_callers_language()
    {
        // No field at all, in Hebrew: the checkbox alone no longer opens the door.
        var missing = await SignupAsync("dob_missing", new { handle = "dob_missing", password = "password123", confirmed16Plus = true, language = "he" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("צריך להוסיף תאריך לידה.", await ErrorOf(missing));

        // Blank, in English.
        var blank = await SignupAsync("dob_blank", new { handle = "dob_blank", password = "password123", birthDate = "   ", language = "en" });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal("Add your date of birth.", await ErrorOf(blank));

        // Nothing was created.
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewClient().GetAsync("/api/users/dob_missing")).StatusCode);
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("2010-13-40")]
    [InlineData("01/02/1999")]
    [InlineData("1999-1-2")]
    [InlineData("1999-01-02T00:00:00Z")]
    [InlineData("1899-12-31")]
    public async Task An_unreadable_date_or_one_before_1900_is_refused(string birthDate)
    {
        var response = await SignupAsync("dob_bad", new { handle = "dob_bad", password = "password123", birthDate, language = "en" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("That date doesn't look right.", await ErrorOf(response));
    }

    [Fact]
    public async Task A_date_in_the_future_is_refused()
    {
        var response = await SignupAsync("dob_future", new { handle = "dob_future", password = "password123", birthDate = Iso(Today.AddDays(1)), language = "he" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("התאריך לא נראה נכון.", await ErrorOf(response));
    }

    [Fact]
    public async Task Younger_than_16_on_the_day_is_refused()
    {
        // Sixteen tomorrow: one day short.
        var response = await SignupAsync("dob_young", new { handle = "dob_young", password = "password123", birthDate = Iso(Today.AddYears(-16).AddDays(1)), language = "en" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("OREVOSH is for people 16 and over.", await ErrorOf(response));

        // Born today: readable, in range, and far too young.
        var newborn = await SignupAsync("dob_newborn", new { handle = "dob_newborn", password = "password123", birthDate = Iso(Today), language = "he" });
        Assert.Equal(HttpStatusCode.BadRequest, newborn.StatusCode);
        Assert.Equal("OREVOSH היא לגילאי 16 ומעלה.", await ErrorOf(newborn));
    }

    [Fact]
    public async Task Sixteen_today_gets_in_and_so_does_a_birthday_in_1900()
    {
        var sixteen = await SignupAsync("dob_sixteen", new { handle = "dob_sixteen", password = "password123", birthDate = Iso(Today.AddYears(-16)), language = "en" });
        Assert.Equal(HttpStatusCode.Created, sixteen.StatusCode);

        var oldest = await SignupAsync("dob_1900", new { handle = "dob_1900", password = "password123", birthDate = "1900-01-01", language = "en" });
        Assert.Equal(HttpStatusCode.Created, oldest.StatusCode);
    }

    [Fact]
    public async Task The_date_is_stored_as_a_utc_date_and_never_answered()
    {
        var response = await SignupAsync("dob_stored", new { handle = "dob_stored", password = "password123", birthDate = " 2001-06-15 ", confirmed16Plus = false, language = "en" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Not on "me", not on the profile: nobody sees it, the person included.
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(me.TryGetProperty("birthDate", out _));
        var profile = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/users/dob_stored");
        Assert.False(profile.TryGetProperty("birthDate", out _));

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.Single(u => u.HandleLower == "dob_stored");
        Assert.Equal(new DateTime(2001, 6, 15, 0, 0, 0), user.BirthDate);
        Assert.Equal(TimeSpan.Zero, user.BirthDate!.Value.TimeOfDay);
        Assert.True(user.Confirmed16Plus);
    }

    [Theory]
    [InlineData("2010-09-08", null)]                    // sixteen today
    [InlineData("2010-09-09", "error.underage")]        // sixteen tomorrow
    [InlineData("2026-09-08", "error.underage")]        // born today
    [InlineData("2026-09-09", "error.birthdate_invalid")]
    [InlineData("1899-12-31", "error.birthdate_invalid")]
    [InlineData("1900-01-01", null)]
    [InlineData("", "error.birthdate_required")]
    [InlineData(null, "error.birthdate_required")]
    [InlineData("2010-9-8", "error.birthdate_invalid")]
    public void The_rule_on_a_fixed_day(string? raw, string? expectedError)
    {
        var (date, error) = AuthEndpoints.ParseBirthDate(raw, new DateOnly(2026, 9, 8));

        Assert.Equal(expectedError, error);
        Assert.Equal(expectedError is null, date.HasValue);
    }

    [Fact]
    public void A_leap_day_birthday_turns_sixteen_on_the_29th()
    {
        Assert.Equal("error.underage", AuthEndpoints.ParseBirthDate("2012-02-29", new DateOnly(2028, 2, 28)).Error);
        Assert.Null(AuthEndpoints.ParseBirthDate("2012-02-29", new DateOnly(2028, 2, 29)).Error);
        // 2100 has no 29 February: the birthday counts from 1 March, not a year later.
        Assert.Equal("error.underage", AuthEndpoints.ParseBirthDate("2084-02-29", new DateOnly(2100, 2, 28)).Error);
        Assert.Null(AuthEndpoints.ParseBirthDate("2084-02-29", new DateOnly(2100, 3, 1)).Error);
    }
}

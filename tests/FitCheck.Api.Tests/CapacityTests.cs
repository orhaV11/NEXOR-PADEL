using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Services;

namespace FitCheck.Api.Tests;

/// <summary>Each test owns its app: the caps under test are global state.</summary>
public class CapacityTests
{
    [Fact]
    public async Task A_parallel_burst_cannot_slip_past_the_per_user_cap()
    {
        using var app = new TestApp { ChecksPerDay = 3 };
        var (client, _, _) = await app.NewUserAsync("burst");
        // A slow model keeps every request in flight at the same time.
        app.Vision.Handler = _ => { Thread.Sleep(300); return Payloads.Ok(); };

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))));

        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.Equal(3, app.Vision.Requests.Count);
    }

    [Fact]
    public async Task Global_ceiling_stops_checks_across_users_with_its_own_message()
    {
        using var app = new TestApp { ChecksPerDayGlobal = 2 };
        var (a, _, _) = await app.NewUserAsync("aa");
        var (b, _, _) = await app.NewUserAsync("bb");
        var (c, _, _) = await app.NewUserAsync("cc", language: "he");

        Assert.Equal(HttpStatusCode.Created, (await a.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await b.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        var third = await c.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), language: "he"));

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Contains("קיבולת", (await third.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(2, app.Vision.Requests.Count);
    }

    [Fact]
    public async Task Cap_of_zero_pauses_the_pilot_with_a_429_not_a_crash()
    {
        using var app = new TestApp { ChecksPerDay = 0 };
        var (client, _, _) = await app.NewUserAsync("paused");

        var response = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Empty(app.Vision.Requests);
    }

    [Fact]
    public async Task Signups_are_limited_per_client_address()
    {
        using var app = new TestApp { SignupsPerHourPerIp = 2 };
        var client = app.NewClient();
        // The app sits behind a tunnel, so the client address arrives in X-Forwarded-For.
        async Task<HttpResponseMessage> Signup(string handle, string address)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/signup")
            {
                Content = JsonContent.Create(new { handle, password = "password123", confirmed16Plus = true, language = "en" })
            };
            request.Headers.Add("X-Forwarded-For", address);
            return await client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.Created, (await Signup("one", "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Signup("two", "203.0.113.1")).StatusCode);

        var third = await Signup("three", "203.0.113.1");
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("Too many new accounts from this network. Try again in an hour.", (await third.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        // Another address has its own bucket.
        Assert.Equal(HttpStatusCode.Created, (await Signup("four", "203.0.113.2")).StatusCode);
    }

    [Fact]
    public async Task Login_attempts_are_limited_per_client_address()
    {
        using var app = new TestApp { LoginsPerQuarterHourPerIp = 2 };
        await app.SignupAsync(app.NewClient(), "target", password: "correct horse");
        var client = app.NewClient();
        async Task<HttpResponseMessage> Login(string password)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { handle = "target", password }) };
            request.Headers.Add("X-Forwarded-For", "203.0.113.9");
            return await client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login("wrong1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login("wrong2")).StatusCode);
        var third = await Login("correct horse");
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Contains("15 minutes", (await third.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Unexpected_failure_after_upload_is_an_error_row_and_a_502_with_no_photo()
    {
        using var app = new TestApp();
        var (client, userId, _) = await app.NewUserAsync("boom");
        app.Vision.Handler = _ => throw new InvalidOperationException("something nobody expected");

        var response = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(app.StorageRoot, userId.ToString("N"))));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("error", Assert.Single(db.Checks.Where(c => c.UserId == userId)).Status);
    }
}

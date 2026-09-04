using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Services;

namespace FitCheck.Api.Tests;

/// <summary>Own fixtures: these tests change the limits, so they cannot share a host with the others.</summary>
public class CapacityTests
{
    [Fact]
    public async Task Parallel_burst_cannot_slip_past_the_per_user_cap()
    {
        using var app = new TestApp { ChecksPerDay = 3 };
        using var client = app.CreateClient();
        var userId = await app.CreateUserAsync(client);
        // Slow model: every request is in flight at the same time, so only the reservation can stop the extras.
        app.Vision.Handler = _ => { Thread.Sleep(400); return Payloads.Ok(); };

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg()))));

        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.Equal(3, app.Vision.Requests.Count);

        // Reservations are released, so the stored count alone decides afterwards (still at cap).
        var again = await client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, again.StatusCode);
    }

    [Fact]
    public async Task Global_ceiling_stops_checks_across_users_with_its_own_message()
    {
        using var app = new TestApp { ChecksPerDayGlobal = 2 };
        using var client = app.CreateClient();
        var a = await app.CreateUserAsync(client, "aa");
        var b = await app.CreateUserAsync(client, "bb");
        var c = await app.CreateUserAsync(client, "cc");

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestApp.CheckForm(a, TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestApp.CheckForm(b, TestImages.Jpeg()))).StatusCode);

        var response = await client.PostAsync("/api/checks", TestApp.CheckForm(c, TestImages.Jpeg(), language: "he"));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("קיבולת", error.GetProperty("error").GetString());
        Assert.Equal(2, app.Vision.Requests.Count);
    }

    [Fact]
    public async Task Cap_of_zero_pauses_the_pilot_with_a_429_not_a_crash()
    {
        using var app = new TestApp { ChecksPerDay = 0 };
        using var client = app.CreateClient();
        var userId = await app.CreateUserAsync(client);

        var response = await client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg()));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Empty(app.Vision.Requests);
    }

    [Fact]
    public async Task Signups_are_limited_per_client_address()
    {
        using var app = new TestApp { SignupsPerHourPerIp = 2 };
        using var client = app.CreateClient();

        await app.CreateUserAsync(client, "one");
        await app.CreateUserAsync(client, "two");
        var response = await client.PostAsJsonAsync("/api/users", new { handle = "three", confirmed16Plus = true, language = "en" });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Too many new accounts from this network. Try again in an hour.", error.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Unexpected_failure_after_upload_stores_an_error_row_and_no_photo()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        var userId = await app.CreateUserAsync(client);
        app.Vision.Handler = _ => throw new InvalidOperationException("something nobody anticipated");

        var response = await client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg()));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var folder = Path.Combine(app.StorageRoot, userId.ToString("N"));
        Assert.True(!Directory.Exists(folder) || Directory.GetFiles(folder).Length == 0);
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/users/{userId}/checks");
        Assert.Equal("error", Assert.Single(list.EnumerateArray()).GetProperty("status").GetString());
    }

    [Fact]
    public void Reservation_release_restores_capacity()
    {
        var capacity = new CheckCapacity();
        var user = Guid.NewGuid();

        Assert.Equal(CapacityVerdict.Ok, capacity.TryReserve(user, storedForUser: 1, userCap: 2, storedGlobal: 1, globalCap: 10, out var first));
        Assert.Equal(CapacityVerdict.UserCapReached, capacity.TryReserve(user, 1, 2, 1, 10, out var second));
        Assert.Null(second);
        Assert.Equal(CapacityVerdict.GlobalCapReached, capacity.TryReserve(Guid.NewGuid(), 0, 2, 9, 10, out _));

        first!.Dispose();
        first.Dispose();
        Assert.Equal(CapacityVerdict.Ok, capacity.TryReserve(user, 1, 2, 1, 10, out var third));
        third!.Dispose();
    }
}

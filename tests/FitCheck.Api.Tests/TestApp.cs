using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FitCheck.Api.Tests;

/// <summary>Hosts the real app with a throwaway SQLite file, a throwaway storage root and a scripted vision client.</summary>
public class TestApp : WebApplicationFactory<Program>
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "fitcheck-tests", Guid.NewGuid().ToString("N"));
    public string StorageRoot => Path.Combine(Root, "storage");
    public FakeVisionClient Vision { get; } = new();
    public int ChecksPerDay { get; init; } = 20;
    public int ChecksPerDayGlobal { get; init; } = 100000;
    public int SignupsPerHourPerIp { get; init; } = 100000;
    public int LoginsPerQuarterHourPerIp { get; init; } = 100000;
    public int ReportsToHide { get; init; } = 3;
    public long MaxVideoBytes { get; init; } = 40 * 1024 * 1024;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Root);
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(Root, "test.db")}");
        builder.UseSetting("Storage:Root", StorageRoot);
        builder.UseSetting("Storage:MaxVideoBytes", MaxVideoBytes.ToString());
        builder.UseSetting("Limits:ChecksPerDay", ChecksPerDay.ToString());
        builder.UseSetting("Limits:ChecksPerDayGlobal", ChecksPerDayGlobal.ToString());
        builder.UseSetting("Limits:SignupsPerHourPerIp", SignupsPerHourPerIp.ToString());
        builder.UseSetting("Limits:LoginsPerQuarterHourPerIp", LoginsPerQuarterHourPerIp.ToString());
        builder.UseSetting("Limits:ReportsToHide", ReportsToHide.ToString());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOutfitVisionClient>();
            services.AddSingleton<IOutfitVisionClient>(Vision);
        });
    }

    /// <summary>A client with its own cookie jar and the CSRF header every state-changing call needs.</summary>
    public HttpClient NewClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(Sessions.RequestHeader, Sessions.RequestHeaderValue);
        return client;
    }

    /// <summary>A client with no CSRF header, for the tests that check the header is required.</summary>
    public HttpClient BareClient() => CreateClient();

    public async Task<JsonElement> SignupAsync(
        HttpClient client, string handle, string password = "password123", string language = "en", string accountType = "Person", string? displayName = null)
    {
        var response = await client.PostAsJsonAsync("/api/auth/signup",
            new { handle, password, confirmed16Plus = true, language, accountType, displayName });
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"signup {handle} failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>A fresh signed-in client for one user.</summary>
    public async Task<(HttpClient Client, Guid Id, string Handle)> NewUserAsync(
        string handle, string language = "en", string accountType = "Person", string? displayName = null)
    {
        var client = NewClient();
        var me = await SignupAsync(client, handle, language: language, accountType: accountType, displayName: displayName);
        return (client, me.GetProperty("id").GetGuid(), handle);
    }

    public static MultipartFormDataContent CheckForm(byte[] image, string intent = "Date", string? language = "en", string? occasion = null, string fileName = "outfit.jpg")
    {
        var form = new MultipartFormDataContent { { new StringContent(intent), "intent" } };
        if (language is not null)
        {
            form.Add(new StringContent(language), "language");
        }

        if (occasion is not null)
        {
            form.Add(new StringContent(occasion), "occasion");
        }

        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "image", fileName);
        return form;
    }

    /// <summary>Runs one ok check with the scripted client and returns the check id.</summary>
    public async Task<Guid> CheckAsync(HttpClient client, string intent = "Date", string language = "en")
    {
        var response = await client.PostAsync("/api/checks", CheckForm(TestImages.Jpeg(), intent, language));
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"check failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        var check = await response.Content.ReadFromJsonAsync<JsonElement>();
        return check.GetProperty("id").GetGuid();
    }

    public async Task<JsonElement> PostAsync(HttpClient client, Guid checkId, string? caption = null, Guid? challengeId = null, object? products = null)
    {
        var response = await client.PostAsJsonAsync("/api/posts", new { checkId, caption, challengeId, products });
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"post failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Check then post, the common social fixture.</summary>
    public async Task<Guid> CheckAndPostAsync(HttpClient client, string intent = "Date", Guid? challengeId = null, string? caption = null)
    {
        var checkId = await CheckAsync(client, intent);
        var post = await PostAsync(client, checkId, caption, challengeId);
        return post.GetProperty("id").GetGuid();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: temp folders are not worth a failing test.
        }
    }
}

/// <summary>Scripted stand-in for the Anthropic client. Records every request so tests can inspect the prompts.</summary>
public sealed class FakeVisionClient : IOutfitVisionClient
{
    public Func<VisionRequest, JsonElement> Handler { get; set; } = _ => Payloads.Ok();
    public List<VisionRequest> Requests { get; } = [];

    public Task<JsonElement> AnalyzeAsync(VisionRequest request, CancellationToken ct)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        return Task.FromResult(Handler(request));
    }
}

public static class Payloads
{
    public static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static JsonElement Ok(int score = 7, int intentMatch = 72, string headline = "Clean casual with one weak link") => Parse($$"""
        {
          "status": "ok",
          "score": {{score}},
          "intent_match": {{intentMatch}},
          "headline": "{{headline}}",
          "vibe": "relaxed weekend",
          "items": [
            { "name": "White tee", "category": "top", "verdict": "works", "note": "Crisp and simple." },
            { "name": "Dark jeans", "category": "bottom", "verdict": "neutral", "note": "Fine, does the job." },
            { "name": "Running shoes", "category": "shoes", "verdict": "weak", "note": "Too sporty for the rest." }
          ],
          "working": ["The palette is tight", "Proportions are balanced"],
          "one_tip": "Swap the running shoes for plain white leather sneakers."
        }
        """);

    public static JsonElement NotOutfit() => Parse("""
        {
          "status": "not_outfit", "score": 1, "intent_match": 0, "headline": "", "vibe": "",
          "items": [], "working": [], "one_tip": "",
          "message": "This looks like a photo of a desk. Try one where the clothes are visible."
        }
        """);

    public static JsonElement Rejected() => Parse("""
        {
          "status": "rejected", "score": 1, "intent_match": 0, "headline": "", "vibe": "",
          "items": [], "working": [], "one_tip": "",
          "message": "MODEL_MESSAGE_THAT_MUST_NOT_LEAK"
        }
        """);
}

public static class TestImages
{
    /// <summary>Only the magic bytes matter: the vision client is faked and nothing decodes the pixels.</summary>
    public static byte[] Jpeg(int size = 2048) => WithHeader([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46], size);
    public static byte[] Png(int size = 2048) => WithHeader([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], size);
    public static byte[] WebP(int size = 2048) => WithHeader([0x52, 0x49, 0x46, 0x46, 0x10, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50], size);
    public static byte[] Garbage(int size = 2048) => WithHeader("not an image at all"u8.ToArray(), size);

    private static byte[] WithHeader(byte[] header, int size)
    {
        var bytes = new byte[Math.Max(size, header.Length)];
        header.CopyTo(bytes, 0);
        for (var i = header.Length; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }
}

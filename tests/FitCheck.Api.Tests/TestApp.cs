using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Tests;

/// <summary>Hosts the real app with a throwaway SQLite file, a throwaway storage root and a scripted vision client.</summary>
public class TestApp : WebApplicationFactory<Program>
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "fitcheck-tests", Guid.NewGuid().ToString("N"));
    public string StorageRoot => Path.Combine(Root, "storage");
    public string DatabasePath => Path.Combine(Root, "test.db");
    public string ConnectionString => $"Data Source={DatabasePath}";
    public FakeVisionClient Vision { get; } = new();
    public int ChecksPerDay { get; init; } = 20;
    /// <summary>
    /// Plans:FreeChecksPerDay. The suite's fixtures were written when every account had Limits:ChecksPerDay, so the default
    /// here matches it; the product default is 3, and GuestCheckTests sets it to see the plan cap.
    /// </summary>
    public int FreeChecksPerDay { get; init; } = 20;
    public int ChecksPerDayGlobal { get; init; } = 100000;
    public int SignupsPerHourPerIp { get; init; } = 100000;
    public int LoginsPerQuarterHourPerIp { get; init; } = 100000;
    public int ReportsToHide { get; init; } = 3;
    public long MaxVideoBytes { get; init; } = 40 * 1024 * 1024;
    /// <summary>VAPID keys; push is off (and /api/config carries no key) unless both are set.</summary>
    public string? PushPublicKey { get; init; }
    public string? PushPrivateKey { get; init; }
    /// <summary>Stands in for the browsers' push services: records every push request and answers with <see cref="RecordingPushHandler.StatusCode"/>.</summary>
    public RecordingPushHandler PushHandler { get; } = new();
    /// <summary>How long the push worker waits for a job's activity row to be committed; shortened by the test that lets a row never appear.</summary>
    public int PushConfirmAttempts { get; init; } = PushSender.ConfirmAttempts;
    public int PushConfirmIntervalMs { get; init; } = PushSender.ConfirmIntervalMs;
    /// <summary>
    /// One handle for Admin:Handles:0; empty means the list is empty. The list reserves the handle at signup and promotes an
    /// account that already exists when the host starts; a moderator for a running app is made with <see cref="PromoteAsync"/>.
    /// </summary>
    public string AdminHandles { get; init; } = "";
    /// <summary>
    /// Mail on (Email:Host and Email:From set, so links are minted and the client offers recovery) or off, as a server
    /// without SMTP settings runs. Either way every message lands in <see cref="Email"/> and nothing is sent.
    /// </summary>
    public bool EmailEnabled { get; init; } = true;
    /// <summary>Stands in for the mail server: records every message the app sends, link included.</summary>
    public RecordingEmailSender Email { get; } = new();
    /// <summary>Storage:Transcode. Off by default so the suite never waits on ffmpeg; TranscoderTests turn it on.</summary>
    public bool Transcode { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Root);
        Email.Enabled = EmailEnabled;
        if (EmailEnabled)
        {
            builder.UseSetting("Email:Host", "smtp.test.invalid");
            builder.UseSetting("Email:From", "OREVOSH <noreply@test.invalid>");
        }

        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Storage:Root", StorageRoot);
        builder.UseSetting("Storage:MaxVideoBytes", MaxVideoBytes.ToString());
        builder.UseSetting("Limits:ChecksPerDay", ChecksPerDay.ToString());
        builder.UseSetting("Plans:FreeChecksPerDay", FreeChecksPerDay.ToString());
        builder.UseSetting("Limits:ChecksPerDayGlobal", ChecksPerDayGlobal.ToString());
        builder.UseSetting("Limits:SignupsPerHourPerIp", SignupsPerHourPerIp.ToString());
        builder.UseSetting("Limits:LoginsPerQuarterHourPerIp", LoginsPerQuarterHourPerIp.ToString());
        builder.UseSetting("Limits:ReportsToHide", ReportsToHide.ToString());
        builder.UseSetting("Admin:Handles:0", AdminHandles);
        builder.UseSetting("Storage:Transcode", Transcode ? "true" : "false");
        if (PushPublicKey is not null)
        {
            builder.UseSetting("Push:PublicKey", PushPublicKey);
        }

        if (PushPrivateKey is not null)
        {
            builder.UseSetting("Push:PrivateKey", PushPrivateKey);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOutfitVisionClient>();
            services.AddSingleton<IOutfitVisionClient>(Vision);
            // Outgoing mail goes to the recorder instead of SMTP or the log.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
            // Outgoing pushes go to the recorder instead of the network.
            services.AddHttpClient(PushSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => PushHandler);
            if (PushConfirmAttempts != PushSender.ConfirmAttempts || PushConfirmIntervalMs != PushSender.ConfirmIntervalMs)
            {
                // The same singleton the hosted-service registration in Program.cs resolves, with a shorter confirmation window.
                services.RemoveAll<PushSender>();
                services.AddSingleton(provider => new PushSender(
                    provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<IHttpClientFactory>(),
                    provider.GetRequiredService<Localizer>(), provider.GetRequiredService<IOptions<PushOptions>>(),
                    provider.GetRequiredService<ILogger<PushSender>>(), PushConfirmAttempts, PushConfirmIntervalMs));
            }
        });
    }

    /// <summary>What <c>--admin &lt;handle&gt;</c> does, against this app's database: the flag on the row, case-insensitively.</summary>
    public Task<AdminChange> PromoteAsync(string handle) => AdminSync.SetAdminAsync(ConnectionString, handle, isAdmin: true);

    /// <summary>What <c>--unadmin &lt;handle&gt;</c> does.</summary>
    public Task<AdminChange> DemoteAsync(string handle) => AdminSync.SetAdminAsync(ConnectionString, handle, isAdmin: false);

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

/// <summary>
/// Stand-in for the mail server. Records every message; <see cref="Fail"/> makes the next sends throw, the way a mail
/// server that is down would. Forgot-password mail goes out in the background, so <see cref="WaitForAsync"/> waits for it.
/// </summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];
    private readonly SemaphoreSlim _arrived = new(0);

    public bool Enabled { get; set; } = true;

    /// <summary>While true, every send throws (after recording nothing).</summary>
    public bool Fail { get; set; }

    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            lock (_sent)
            {
                return _sent.ToList();
            }
        }
    }

    /// <summary>Messages to one address, in the order they were sent.</summary>
    public List<EmailMessage> To(string address) => Sent.Where(m => m.To == address).ToList();

    /// <summary>Waits until at least <paramref name="count"/> messages to the address have arrived, or the timeout passes.</summary>
    public Task<List<EmailMessage>> WaitForAsync(string address, int count = 1, int timeoutMs = 5000) =>
        WaitForAsync(m => m.To == address, count, timeoutMs);

    /// <summary>Waits until at least <paramref name="count"/> messages matching the predicate have arrived, or the timeout passes.</summary>
    public async Task<List<EmailMessage>> WaitForAsync(Func<EmailMessage, bool> predicate, int count = 1, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var matching = Sent.Where(predicate).ToList();
            if (matching.Count >= count)
            {
                return matching;
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || !await _arrived.WaitAsync(remaining))
            {
                return matching;
            }
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (Fail)
        {
            throw new IOException("The mail server is not answering.");
        }

        lock (_sent)
        {
            _sent.Add(message);
        }

        _arrived.Release();
        return Task.CompletedTask;
    }
}

/// <summary>One push request as the push service would have seen it: the endpoint, the headers and the encrypted body.</summary>
public sealed record PushRequest(Uri Endpoint, string? Authorization, string? ContentEncoding, string? Ttl, string? Topic, string? Urgency, byte[] Body);

/// <summary>Stand-in for the push services. Records every request and answers with <see cref="StatusCode"/> (201 by default).</summary>
public sealed class RecordingPushHandler : HttpMessageHandler
{
    private readonly List<PushRequest> _requests = [];
    private readonly SemaphoreSlim _arrived = new(0);

    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.Created;

    public IReadOnlyList<PushRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToList();
            }
        }
    }

    public void Clear()
    {
        lock (_requests)
        {
            _requests.Clear();
        }

        while (_arrived.CurrentCount > 0 && _arrived.Wait(0))
        {
        }
    }

    /// <summary>Waits until at least <paramref name="count"/> requests for the endpoint have arrived, or the timeout passes.</summary>
    public async Task<List<PushRequest>> WaitForAsync(string endpoint, int count = 1, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var matching = Requests.Where(r => r.Endpoint.ToString() == endpoint).ToList();
            if (matching.Count >= count)
            {
                return matching;
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || !await _arrived.WaitAsync(remaining))
            {
                return matching;
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        string? Header(string name) => request.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;
        var record = new PushRequest(
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentEncoding is { Count: > 0 } encodings ? string.Join(", ", encodings) : null,
            Header("TTL"), Header("Topic"), Header("Urgency"), body);
        lock (_requests)
        {
            _requests.Add(record);
        }

        _arrived.Release();
        return new HttpResponseMessage(StatusCode) { Content = new StringContent("") };
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

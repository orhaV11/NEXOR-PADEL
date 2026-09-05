using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;

namespace FitCheck.Api.Tests;

/// <summary>
/// Web Push end to end against the recorded push service: the subscription rules, the disabled answer, the request
/// that goes out when someone fires a look (headers, VAPID, and the decrypted payload), gone subscriptions, no
/// self-pings, account deletion, and the public key on /api/config.
/// </summary>
public class PushTests : IClassFixture<PushTests.PushApp>
{
    /// <summary>The app with a real VAPID key pair (generated here with ECDsa P-256) so push is on.</summary>
    public sealed class PushApp : IDisposable
    {
        public string PublicKey { get; }
        public string PrivateKey { get; }
        public TestApp App { get; }

        public PushApp()
        {
            (PublicKey, PrivateKey) = GenerateKeys();
            App = new TestApp { PushPublicKey = PublicKey, PushPrivateKey = PrivateKey };
        }

        public void Dispose() => App.Dispose();

        private static (string PublicKey, string PrivateKey) GenerateKeys()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var p = ecdsa.ExportParameters(true);
            var point = new byte[65];
            point[0] = 0x04;
            p.Q.X!.CopyTo(point, 1);
            p.Q.Y!.CopyTo(point, 33);
            return (PushSender.Base64Url(point), PushSender.Base64Url(p.D!));
        }
    }

    /// <summary>A browser's side of a subscription: the endpoint, the keys it hands the server, and the private half that decrypts.</summary>
    private sealed class Browser : IDisposable
    {
        public string Endpoint { get; }
        public string P256dh { get; }
        public string Auth { get; }
        public ECDiffieHellman Key { get; } = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        public byte[] AuthSecret { get; } = RandomNumberGenerator.GetBytes(16);

        public Browser(string name)
        {
            Endpoint = $"https://push.example.test/send/{name}-{Guid.NewGuid():N}";
            P256dh = PushSender.Base64Url(PublicPoint(Key));
            Auth = PushSender.Base64Url(AuthSecret);
        }

        public object Body => new { endpoint = Endpoint, p256dh = P256dh, auth = Auth };

        public void Dispose() => Key.Dispose();

        public static byte[] PublicPoint(ECDiffieHellman key)
        {
            var p = key.ExportParameters(false);
            var point = new byte[65];
            point[0] = 0x04;
            p.Q.X!.CopyTo(point, 1);
            p.Q.Y!.CopyTo(point, 33);
            return point;
        }

        /// <summary>RFC 8291 + RFC 8188: what the service worker would read from event.data.</summary>
        public JsonElement Decrypt(byte[] body)
        {
            var salt = body[..16];
            var recordSize = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16, 4));
            var idLength = body[20];
            var serverPoint = body[21..(21 + idLength)];
            var ciphertext = body[(21 + idLength)..];
            Assert.True(ciphertext.Length <= recordSize, "a small payload is one record");

            using var serverKey = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = serverPoint[1..33], Y = serverPoint[33..65] }
            });
            var shared = Key.DeriveRawSecretAgreement(serverKey.PublicKey);
            var info = Concat("WebPush: info\0"u8.ToArray(), PublicPoint(Key), serverPoint);
            var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, AuthSecret, info);
            var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray());
            var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, "Content-Encoding: nonce\0"u8.ToArray());

            using var aes = new AesGcm(cek, 16);
            var plain = new byte[ciphertext.Length - 16];
            aes.Decrypt(nonce, ciphertext[..^16], ciphertext[^16..], plain);
            // The record ends with a delimiter (0x02 for the last record) and optional zero padding.
            var end = Array.FindLastIndex(plain, b => b != 0);
            Assert.Equal(2, plain[end]);
            return Payloads.Parse(Encoding.UTF8.GetString(plain, 0, end));
        }

        private static byte[] Concat(params byte[][] parts)
        {
            var result = new byte[parts.Sum(p => p.Length)];
            var offset = 0;
            foreach (var part in parts)
            {
                part.CopyTo(result, offset);
                offset += part.Length;
            }

            return result;
        }
    }

    private readonly PushApp _fixture;
    private TestApp App => _fixture.App;
    private RecordingPushHandler Pushes => _fixture.App.PushHandler;

    public PushTests(PushApp fixture)
    {
        _fixture = fixture;
        App.Vision.Handler = _ => Payloads.Ok();
        Pushes.StatusCode = HttpStatusCode.Created;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> State(HttpClient client) => await client.GetFromJsonAsync<JsonElement>("/api/push/state");

    private static async Task Subscribe(HttpClient client, Browser browser)
    {
        var response = await client.PostAsJsonAsync("/api/push/subscriptions", browser.Body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await Json(response);
        Assert.True(state.GetProperty("enabled").GetBoolean());
        Assert.True(state.GetProperty("subscribed").GetBoolean());
    }

    [Fact]
    public async Task Config_carries_the_public_key_only_when_both_keys_are_set()
    {
        var config = await App.NewClient().GetFromJsonAsync<JsonElement>("/api/config");
        Assert.Equal(_fixture.PublicKey, config.GetProperty("pushPublicKey").GetString());

        using var off = new TestApp();
        var noKeys = await off.NewClient().GetFromJsonAsync<JsonElement>("/api/config");
        Assert.False(noKeys.TryGetProperty("pushPublicKey", out _));

        using var half = new TestApp { PushPublicKey = _fixture.PublicKey };
        var oneKey = await half.NewClient().GetFromJsonAsync<JsonElement>("/api/config");
        Assert.False(oneKey.TryGetProperty("pushPublicKey", out _));

        // Both set, but not a key pair the sender can sign with: the key is not published either, or every browser would
        // subscribe to pings that never come. The app still starts; only push is off.
        using var broken = new TestApp { PushPublicKey = "not-a-key", PushPrivateKey = "not-a-key-either" };
        var badPair = await broken.NewClient().GetFromJsonAsync<JsonElement>("/api/config");
        Assert.False(badPair.TryGetProperty("pushPublicKey", out _));
        Assert.Equal(broken.MaxVideoBytes, badPair.GetProperty("maxVideoBytes").GetInt64());
        var (client, _, _) = await broken.NewUserAsync("badpair");
        Assert.False((await State(client)).GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/push/test", null)).StatusCode);
    }

    [Fact]
    public async Task Without_keys_subscribing_and_testing_are_refused_and_unsubscribing_still_answers_204()
    {
        using var off = new TestApp();
        var (client, _, _) = await off.NewUserAsync("nopush");
        using var browser = new Browser("nopush");

        var state = await State(client);
        Assert.False(state.GetProperty("enabled").GetBoolean());
        Assert.False(state.GetProperty("subscribed").GetBoolean());

        var subscribe = await client.PostAsJsonAsync("/api/push/subscriptions", browser.Body);
        Assert.Equal(HttpStatusCode.BadRequest, subscribe.StatusCode);
        Assert.Equal("Push notifications are not set up on this server.", (await Json(subscribe)).GetProperty("error").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/push/test", null)).StatusCode);

        var unsubscribe = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/push/subscriptions") { Content = JsonContent.Create(new { endpoint = browser.Endpoint }) });
        Assert.Equal(HttpStatusCode.NoContent, unsubscribe.StatusCode);
        Assert.Empty(off.PushHandler.Requests);
    }

    [Fact]
    public async Task Push_routes_need_a_session_and_the_csrf_header()
    {
        using var browser = new Browser("anon");
        Assert.Equal(HttpStatusCode.Unauthorized, (await App.NewClient().PostAsJsonAsync("/api/push/subscriptions", browser.Body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await App.NewClient().GetAsync("/api/push/state")).StatusCode);

        var bare = App.BareClient();
        await App.SignupAsync(App.NewClient(), "csrfpush");
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.PostAsJsonAsync("/api/push/subscriptions", browser.Body)).StatusCode);
    }

    [Fact]
    public async Task Subscription_validation_and_upsert_by_endpoint()
    {
        var (first, _, _) = await App.NewUserAsync("subber1");
        var (second, _, _) = await App.NewUserAsync("subber2");
        using var browser = new Browser("shared");

        async Task<HttpResponseMessage> Post(HttpClient client, object body) => await client.PostAsJsonAsync("/api/push/subscriptions", body);

        var missing = await Post(first, new { endpoint = browser.Endpoint, auth = browser.Auth });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("That push subscription is missing its endpoint or keys.", (await Json(missing)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(first, new { endpoint = "http://push.example.test/plain", p256dh = browser.P256dh, auth = browser.Auth })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(first, new { endpoint = "javascript:alert(1)", p256dh = browser.P256dh, auth = browser.Auth })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(first, new { endpoint = browser.Endpoint, p256dh = "not-a-point", auth = browser.Auth })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(first, new { endpoint = browser.Endpoint, p256dh = browser.P256dh, auth = "c2hvcnQ" })).StatusCode);
        Assert.False((await State(first)).GetProperty("subscribed").GetBoolean());

        // Subscribing twice is one row; the state says so.
        await Subscribe(first, browser);
        await Subscribe(first, browser);
        Assert.True((await State(first)).GetProperty("subscribed").GetBoolean());

        // The same browser signs into another account: the endpoint follows the person holding the phone.
        await Subscribe(second, browser);
        Assert.False((await State(first)).GetProperty("subscribed").GetBoolean());
        Assert.True((await State(second)).GetProperty("subscribed").GetBoolean());

        // Only the owner can drop it; a stranger's delete is a quiet 204 that changes nothing.
        var strangerDelete = await first.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/push/subscriptions") { Content = JsonContent.Create(new { endpoint = browser.Endpoint }) });
        Assert.Equal(HttpStatusCode.NoContent, strangerDelete.StatusCode);
        Assert.True((await State(second)).GetProperty("subscribed").GetBoolean());

        var ownerDelete = await second.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/push/subscriptions") { Content = JsonContent.Create(new { endpoint = browser.Endpoint }) });
        Assert.Equal(HttpStatusCode.NoContent, ownerDelete.StatusCode);
        Assert.False((await State(second)).GetProperty("subscribed").GetBoolean());

        // Unknown endpoint, and the query form the client may fall back to: both 204.
        Assert.Equal(HttpStatusCode.NoContent, (await second.DeleteAsync("/api/push/subscriptions?endpoint=" + Uri.EscapeDataString("https://push.example.test/send/nobody"))).StatusCode);
    }

    [Fact]
    public async Task Firing_a_look_sends_one_vapid_signed_encrypted_push_to_the_owners_browser()
    {
        var (owner, _, _) = await App.NewUserAsync("pushowner", displayName: "Push Owner");
        var (fan, _, _) = await App.NewUserAsync("pushfan", displayName: "Push Fan");
        using var browser = new Browser("owner");
        await Subscribe(owner, browser);
        var postId = await App.CheckAndPostAsync(owner);

        Assert.Equal(HttpStatusCode.OK, (await fan.PostAsync($"/api/posts/{postId}/fire", null)).StatusCode);

        var requests = await Pushes.WaitForAsync(browser.Endpoint);
        var request = Assert.Single(requests);
        Assert.Equal(browser.Endpoint, request.Endpoint.ToString());
        Assert.StartsWith("vapid t=", request.Authorization);
        Assert.Contains("k=" + _fixture.PublicKey, request.Authorization);
        Assert.Equal("aes128gcm", request.ContentEncoding);
        Assert.Equal("86400", request.Ttl);
        Assert.StartsWith("fire-", request.Topic);

        var payload = browser.Decrypt(request.Body);
        Assert.Equal("OREVOSH", payload.GetProperty("title").GetString());
        Assert.Equal("Push Fan set your look on fire", payload.GetProperty("body").GetString());
        Assert.Equal($"/#/post/{postId}", payload.GetProperty("url").GetString());
        Assert.Equal($"fire:{postId:N}", payload.GetProperty("tag").GetString());

        // Unfire and fire again is one activity row, so it is one push too.
        Assert.Equal(HttpStatusCode.OK, (await fan.DeleteAsync($"/api/posts/{postId}/fire")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fan.PostAsync($"/api/posts/{postId}/fire", null)).StatusCode);
        await Task.Delay(300);
        Assert.Single(Pushes.Requests, r => r.Endpoint.ToString() == browser.Endpoint);
    }

    [Fact]
    public async Task The_push_speaks_the_recipients_language_and_points_at_the_right_place()
    {
        var (owner, _, _) = await App.NewUserAsync("pushhe", language: "he");
        var (follower, _, _) = await App.NewUserAsync("pushfollower", displayName: "Dana");
        using var browser = new Browser("he");
        await Subscribe(owner, browser);

        Assert.Equal(HttpStatusCode.OK, (await follower.PostAsync("/api/users/pushhe/follow", null)).StatusCode);

        var request = Assert.Single(await Pushes.WaitForAsync(browser.Endpoint));
        Assert.Null(request.Topic);
        var payload = browser.Decrypt(request.Body);
        // The same neutral phrasing as the activity list: what happened, then who.
        Assert.Equal("עוקב חדש: Dana", payload.GetProperty("body").GetString());
        Assert.Equal("/#/u/pushfollower", payload.GetProperty("url").GetString());
        Assert.Equal("follow:pushfollower", payload.GetProperty("tag").GetString());
    }

    [Fact]
    public async Task A_410_from_the_push_service_drops_the_subscription()
    {
        var (owner, _, _) = await App.NewUserAsync("pushgone");
        var (fan, _, _) = await App.NewUserAsync("pushgonefan");
        using var browser = new Browser("gone");
        await Subscribe(owner, browser);
        var postId = await App.CheckAndPostAsync(owner);

        Pushes.StatusCode = HttpStatusCode.Gone;
        try
        {
            Assert.Equal(HttpStatusCode.Created, (await fan.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "gone?" })).StatusCode);
            Assert.Single(await Pushes.WaitForAsync(browser.Endpoint));

            // The row goes right after the answer; give the worker a moment.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((await State(owner)).GetProperty("subscribed").GetBoolean() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.False((await State(owner)).GetProperty("subscribed").GetBoolean());
        }
        finally
        {
            Pushes.StatusCode = HttpStatusCode.Created;
        }

        // Nothing is sent to a browser that is gone.
        Assert.Equal(HttpStatusCode.Created, (await fan.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "still there?" })).StatusCode);
        await Task.Delay(300);
        Assert.Single(Pushes.Requests, r => r.Endpoint.ToString() == browser.Endpoint);
    }

    [Fact]
    public async Task No_push_for_what_you_do_to_your_own_look()
    {
        var (owner, _, _) = await App.NewUserAsync("pushself");
        var (fan, _, _) = await App.NewUserAsync("pushselffan", displayName: "Fan");
        using var browser = new Browser("self");
        await Subscribe(owner, browser);
        var postId = await App.CheckAndPostAsync(owner);

        // Firing and commenting on your own look: no activity row, so no push.
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/posts/{postId}/fire", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "me" })).StatusCode);
        // A marker from someone else proves the queue was drained past the self-actions.
        Assert.Equal(HttpStatusCode.Created, (await fan.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "marker" })).StatusCode);

        var request = Assert.Single(await Pushes.WaitForAsync(browser.Endpoint));
        Assert.StartsWith("comment-", request.Topic);
        Assert.Equal("Fan commented on your look", browser.Decrypt(request.Body).GetProperty("body").GetString());
    }

    [Fact]
    public async Task The_test_ping_goes_to_your_own_browsers_only()
    {
        var (me, _, _) = await App.NewUserAsync("pushtester", displayName: "Tester");
        using var browser = new Browser("tester");
        await Subscribe(me, browser);

        Assert.Equal(HttpStatusCode.Accepted, (await me.PostAsync("/api/push/test", null)).StatusCode);

        var request = Assert.Single(await Pushes.WaitForAsync(browser.Endpoint));
        var payload = browser.Decrypt(request.Body);
        Assert.Equal("Tester set your look on fire", payload.GetProperty("body").GetString());
        Assert.Equal("/#/activity", payload.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Deleting_the_account_deletes_its_subscriptions()
    {
        var (me, id, _) = await App.NewUserAsync("pushdeleted");
        using var browser = new Browser("deleted");
        await Subscribe(me, browser);

        Assert.Equal(HttpStatusCode.NoContent, (await me.DeleteAsync("/api/users/me")).StatusCode);

        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(db.PushSubscriptions.Where(s => s.UserId == id || s.Endpoint == browser.Endpoint));
    }

    [Fact]
    public async Task A_403_from_the_push_service_drops_the_subscription_like_a_gone_one()
    {
        // 401/403 means the subscription was made against other VAPID keys (the pair was regenerated): it will never take ours.
        var (owner, _, _) = await App.NewUserAsync("pushrekeyed");
        var (fan, _, _) = await App.NewUserAsync("pushrekeyedfan");
        using var browser = new Browser("rekeyed");
        await Subscribe(owner, browser);
        var postId = await App.CheckAndPostAsync(owner);

        Pushes.StatusCode = HttpStatusCode.Forbidden;
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await fan.PostAsync($"/api/posts/{postId}/fire", null)).StatusCode);
            Assert.Single(await Pushes.WaitForAsync(browser.Endpoint));

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((await State(owner)).GetProperty("subscribed").GetBoolean() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.False((await State(owner)).GetProperty("subscribed").GetBoolean());
        }
        finally
        {
            Pushes.StatusCode = HttpStatusCode.Created;
        }

        // The client sees "not subscribed" and subscribes again with the current key; until then nothing more is sent.
        Assert.Equal(HttpStatusCode.Created, (await fan.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "again?" })).StatusCode);
        await Task.Delay(300);
        Assert.Single(Pushes.Requests, r => r.Endpoint.ToString() == browser.Endpoint);
    }

    [Fact]
    public async Task Endpoints_on_addresses_localhost_or_bare_names_are_refused()
    {
        var (client, _, _) = await App.NewUserAsync("hostrules");
        using var browser = new Browser("hostrules");
        var refused = new[]
        {
            "https://10.0.0.1/send/x", "https://127.0.0.1/send/x", "https://[::1]/send/x", "https://[2001:db8::1]/send/x",
            "https://localhost/send/x", "https://LOCALHOST:8443/send/x", "https://push/send/x", "https://push./send/x"
        };
        foreach (var endpoint in refused)
        {
            Assert.False(PushEndpoints.IsValidSubscription(endpoint, browser.P256dh, browser.Auth), endpoint);
            var response = await client.PostAsJsonAsync("/api/push/subscriptions", new { endpoint, p256dh = browser.P256dh, auth = browser.Auth });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("That push subscription is missing its endpoint or keys.", (await Json(response)).GetProperty("error").GetString());
        }

        Assert.False((await State(client)).GetProperty("subscribed").GetBoolean());

        // The real push services, and a fully qualified name with its trailing dot.
        var accepted = new[]
        {
            "https://fcm.googleapis.com/fcm/send/abc", "https://web.push.apple.com/QWxs", "https://updates.push.services.mozilla.com/wpush/v2/x",
            "https://push.example.test./trailing"
        };
        foreach (var endpoint in accepted)
        {
            Assert.True(PushEndpoints.IsValidSubscription(endpoint, browser.P256dh, browser.Auth), endpoint);
        }
    }

    [Fact]
    public async Task An_account_keeps_at_most_ten_subscriptions_and_the_oldest_makes_room()
    {
        var (client, id, _) = await App.NewUserAsync("manybrowsers");
        var browsers = Enumerable.Range(0, PushEndpoints.MaxPerAccount + 2).Select(i => new Browser($"many{i}")).ToList();
        try
        {
            List<string> Stored()
            {
                using var scope = App.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                return db.PushSubscriptions.Where(s => s.UserId == id).OrderBy(s => s.CreatedAt).Select(s => s.Endpoint).ToList();
            }

            foreach (var browser in browsers.Take(PushEndpoints.MaxPerAccount + 1))
            {
                await Subscribe(client, browser);
                await Task.Delay(2);
            }

            // Eleven subscribes, ten rows: the first browser made room for the eleventh.
            Assert.Equal(browsers.Skip(1).Take(PushEndpoints.MaxPerAccount).Select(b => b.Endpoint), Stored());

            // A browser that is already there only refreshes its row; nothing else is evicted.
            await Subscribe(client, browsers[5]);
            Assert.Equal(browsers.Skip(1).Take(PushEndpoints.MaxPerAccount).Select(b => b.Endpoint), Stored());

            // The twelfth evicts the next oldest.
            await Subscribe(client, browsers[^1]);
            Assert.Equal(browsers.Skip(2).Select(b => b.Endpoint), Stored());
        }
        finally
        {
            browsers.ForEach(b => b.Dispose());
        }
    }

    [Fact]
    public async Task A_job_whose_activity_row_never_appears_sends_nothing()
    {
        // The worker confirms the activity row before it sends. A shorter window here (ten looks, 100 ms apart) keeps the wait
        // for the job that never gets its row bounded; the default is twenty looks, 250 ms apart.
        using var app = new TestApp
        {
            PushPublicKey = _fixture.PublicKey, PushPrivateKey = _fixture.PrivateKey, PushConfirmAttempts = 10, PushConfirmIntervalMs = 100
        };
        app.Vision.Handler = _ => Payloads.Ok();
        var (owner, ownerId, _) = await app.NewUserAsync("pushghost");
        var (fan, _, _) = await app.NewUserAsync("pushghostfan", displayName: "Ghost Fan");
        using var browser = new Browser("ghost");
        await Subscribe(owner, browser);

        // A job for a row that was never committed (the request rolled back after queuing it).
        app.Services.GetRequiredService<PushSender>().Enqueue(new PushJob(ownerId, "fire", "pushghostfan", null, null, Guid.NewGuid()));
        Assert.Empty(await app.PushHandler.WaitForAsync(browser.Endpoint, timeoutMs: 2500));

        // The worker moved on: a real activity row still goes out, and so does the test ping, which has no row to wait for.
        var postId = await app.CheckAndPostAsync(owner);
        Assert.Equal(HttpStatusCode.OK, (await fan.PostAsync($"/api/posts/{postId}/fire", null)).StatusCode);
        var request = Assert.Single(await app.PushHandler.WaitForAsync(browser.Endpoint));
        Assert.Equal("Ghost Fan set your look on fire", browser.Decrypt(request.Body).GetProperty("body").GetString());
        Assert.Equal(HttpStatusCode.Accepted, (await owner.PostAsync("/api/push/test", null)).StatusCode);
        Assert.Equal(2, (await app.PushHandler.WaitForAsync(browser.Endpoint, count: 2)).Count);
    }

    [Fact]
    public void Generated_vapid_keys_have_the_web_push_shape()
    {
        var (publicKey, privateKey) = PushSender.GenerateVapidKeys();
        var point = PushSender.FromBase64Url(publicKey);
        var scalar = PushSender.FromBase64Url(privateKey);
        Assert.NotNull(point);
        Assert.Equal(65, point!.Length);
        Assert.Equal(0x04, point[0]);
        Assert.Equal(32, scalar!.Length);
        Assert.DoesNotContain('=', publicKey);
        Assert.DoesNotContain('+', publicKey + privateKey);
        Assert.DoesNotContain('/', publicKey + privateKey);

        using var browser = new Browser("shape");
        Assert.True(PushEndpoints.IsValidSubscription(browser.Endpoint, browser.P256dh, browser.Auth));
        Assert.False(PushEndpoints.IsValidSubscription("https://push.example.test/x", browser.P256dh, null));
        Assert.False(PushEndpoints.IsValidSubscription("https://push.example.test/x", publicKey + "AA", browser.Auth));
        Assert.False(PushEndpoints.IsValidSubscription(null, browser.P256dh, browser.Auth));
    }
}

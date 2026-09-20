using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Domain;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>What a Checkout Session needs from us: who is paying and where Stripe sends them afterwards.</summary>
public sealed record CheckoutSessionRequest(Guid UserId, string? CustomerId, string? CustomerEmail, string SuccessUrl, string CancelUrl);

/// <summary>
/// The little of Stripe we use, over a raw HttpClient: two form-encoded POSTs, one that opens a Checkout Session and
/// one that opens a Billing Portal session (Round 11), and the signature check on the events Stripe posts back. No SDK:
/// three calls do not earn a dependency, and the request that goes out is exactly what the tests record.
/// </summary>
public sealed class StripeClient
{
    public const string HttpClientName = "stripe";
    public const string BaseUrl = "https://api.stripe.com/";
    public const string SignatureHeader = "Stripe-Signature";

    /// <summary>How far a signed timestamp may sit from now before an event is refused as a replay.</summary>
    public static readonly TimeSpan SignatureTolerance = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpClients;
    private readonly IOptions<BillingOptions> _billing;
    private readonly ILogger<StripeClient> _logger;

    public StripeClient(IHttpClientFactory httpClients, IOptions<BillingOptions> billing, ILogger<StripeClient> logger)
    {
        _httpClients = httpClients;
        _billing = billing;
        _logger = logger;
    }

    /// <summary>
    /// Creates a subscription Checkout Session for the Pro price and returns the hosted page's URL, or null when Stripe
    /// did not answer with one (logged; the caller answers 502). The account id travels twice, as client_reference_id
    /// and as metadata, so the webhook finds the person whichever one the event carries.
    /// </summary>
    public async Task<string?> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken ct)
    {
        var options = _billing.Value;
        var form = new List<KeyValuePair<string, string>>
        {
            new("mode", "subscription"),
            new("line_items[0][price]", options.StripePriceId),
            new("line_items[0][quantity]", "1"),
            new("client_reference_id", request.UserId.ToString("N")),
            new("success_url", request.SuccessUrl),
            new("cancel_url", request.CancelUrl),
            new("metadata[userId]", request.UserId.ToString("N"))
        };
        // A returning customer keeps their Stripe record (and their saved card); a first-timer gets the address prefilled
        // when we know it belongs to them.
        if (!string.IsNullOrWhiteSpace(request.CustomerId))
        {
            form.Add(new("customer", request.CustomerId));
        }
        else if (!string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            form.Add(new("customer_email", request.CustomerEmail));
        }

        return await PostForUrlAsync("v1/checkout/sessions", form, "checkout session", request.UserId, ct);
    }

    /// <summary>
    /// Opens a Billing Portal session (Round 11) for a customer Checkout created and returns the hosted page's URL, or
    /// null when Stripe did not answer with one (logged; the caller answers 502). The portal is where the person changes
    /// the card or cancels; Stripe sends them back to <paramref name="returnUrl"/> afterwards, and the webhook is what
    /// tells us what they did there.
    /// </summary>
    public Task<string?> CreatePortalSessionAsync(Guid userId, string customerId, string returnUrl, CancellationToken ct)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("customer", customerId),
            new("return_url", returnUrl)
        };
        return PostForUrlAsync(Endpoints.BillingEndpoints.PortalSessionsPath, form, "portal session", userId, ct);
    }

    /// <summary>
    /// Ends a subscription now (<c>DELETE /v1/subscriptions/{id}</c>), so deleting an account stops the money the same
    /// second the row goes. True when Stripe confirms it is gone; true as well for a 404 or a subscription Stripe already
    /// reports as canceled, because the state the caller asked for is the state Stripe is in. False on anything else,
    /// including no answer at all: the caller must refuse the deletion rather than leave a card being charged for an
    /// account that no longer exists.
    /// </summary>
    public async Task<bool> CancelSubscriptionAsync(Guid userId, string subscriptionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return true;
        }

        using var message = new HttpRequestMessage(HttpMethod.Delete, Endpoints.BillingEndpoints.SubscriptionsPath + "/" + Uri.EscapeDataString(subscriptionId));
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _billing.Value.StripeSecretKey);

        var client = _httpClients.CreateClient(HttpClientName);
        try
        {
            using var response = await client.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Stripe knows no subscription {Subscription} for {UserId}; treating it as already ended", Trim(subscriptionId), userId);
                return true;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Stripe refused to cancel the subscription for {UserId}: {Status} {Body}", userId, (int)response.StatusCode, Trim(body));
                return false;
            }

            using var json = JsonDocument.Parse(body);
            var status = json.RootElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            if (status is "canceled" or "incomplete_expired")
            {
                return true;
            }

            _logger.LogError("Stripe answered the cancel for {UserId} with status {Status}", userId, status ?? "(none)");
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "Stripe did not answer the cancel for {UserId}", userId);
            return false;
        }
    }

    /// <summary>
    /// One form-encoded POST to Stripe with the secret key as the bearer token, answered with the "url" of the object it
    /// created; null (logged with <paramref name="what"/> and the account) on a refusal, a body without a url, or no answer.
    /// </summary>
    private async Task<string?> PostForUrlAsync(string path, List<KeyValuePair<string, string>> form, string what, Guid userId, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(form) };
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _billing.Value.StripeSecretKey);

        var client = _httpClients.CreateClient(HttpClientName);
        try
        {
            using var response = await client.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Stripe refused the {What} for {UserId}: {Status} {Body}", what, userId, (int)response.StatusCode, Trim(body));
                return null;
            }

            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(url.GetString()))
            {
                return url.GetString();
            }

            _logger.LogError("Stripe answered the {What} for {UserId} without a url: {Body}", what, userId, Trim(body));
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "Stripe did not answer the {What} for {UserId}", what, userId);
            return null;
        }
    }

    /// <summary>
    /// Whether a Stripe-Signature header (<c>t=&lt;unix seconds&gt;,v1=&lt;hex&gt;[,v1=...]</c>) signs this raw body with the
    /// webhook secret: HMAC-SHA256 over "&lt;t&gt;.&lt;body&gt;", compared in constant time against every v1 (Stripe sends two
    /// while a secret is being rotated), and a timestamp within <see cref="SignatureTolerance"/> of now.
    /// </summary>
    public static bool VerifySignature(string? header, string rawBody, string secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrEmpty(secret))
        {
            return false;
        }

        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = part[..eq];
            var value = part[(eq + 1)..];
            if (key == "t" && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var t))
            {
                timestamp = t;
            }
            else if (key == "v1")
            {
                signatures.Add(value);
            }
        }

        if (timestamp is null || signatures.Count == 0)
        {
            return false;
        }

        var age = now - DateTimeOffset.FromUnixTimeSeconds(timestamp.Value);
        if (age > SignatureTolerance || age < -SignatureTolerance)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(secret, timestamp.Value, rawBody));
        var matched = false;
        foreach (var signature in signatures)
        {
            var given = Encoding.ASCII.GetBytes(signature.ToLowerInvariant());
            // Every candidate is compared, so the time taken does not say which one was close.
            matched |= given.Length == expected.Length && CryptographicOperations.FixedTimeEquals(given, expected);
        }

        return matched;
    }

    /// <summary>The lower-case hex HMAC-SHA256 Stripe computes for a body at a timestamp; the tests sign their events with it.</summary>
    public static string Sign(string secret, long timestamp, string rawBody)
    {
        var payload = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + "." + rawBody);
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload)).ToLowerInvariant();
    }

    /// <summary>A Stripe-Signature header value for a body, as Stripe would send it right now.</summary>
    public static string SignatureHeaderValue(string secret, long timestamp, string rawBody) =>
        $"t={timestamp.ToString(CultureInfo.InvariantCulture)},v1={Sign(secret, timestamp, rawBody)}";

    private static string Trim(string body) => body.Length <= 400 ? body : body[..400] + "…";
}

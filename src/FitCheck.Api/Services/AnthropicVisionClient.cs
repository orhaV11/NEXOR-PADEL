using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Domain;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace FitCheck.Api.Services;

/// <summary>
/// Anthropic Messages API over a plain HttpClient: base64 image block(s) + forced tool call.
/// No SDK, so the request shape is spelled out here and the API key is read from the environment only.
/// <para>
/// Round 13 — money: every answer's <c>usage</c> block is handed to the <see cref="SpendMeter"/> before anything else is
/// done with it, so the day's tally is what the API said and not what we guessed. A failed call that the API still
/// billed (an error body that carried usage, a timeout) is recorded too, with whatever is known; a call that never
/// reached the API records nothing. The meter is optional so the client can still be built by hand in a test.
/// </para>
/// </summary>
public sealed class AnthropicVisionClient(
    HttpClient http,
    IOptions<AnthropicOptions> options,
    ILogger<AnthropicVisionClient> logger,
    SpendMeter? meter = null) : IOutfitVisionClient
{
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";
    private const string ApiVersion = "2023-06-01";
    private static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(1.5);

    private static readonly JsonSerializerOptions BodyJson = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<JsonElement> AnalyzeAsync(VisionRequest request, CancellationToken ct)
    {
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new VisionClientException($"{ApiKeyVariable} is not set.");
        }

        var body = JsonSerializer.Serialize(BuildBody(request), BodyJson);
        var url = options.Value.BaseUrl.TrimEnd('/') + "/v1/messages";

        for (var attempt = 1; ; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, url);
            message.Headers.Add("x-api-key", apiKey);
            message.Headers.Add("anthropic-version", ApiVersion);
            message.Content = new StringContent(body, Encoding.UTF8);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(message, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // Round 13 — money: a timeout means the request was sent and the API may well have billed it, so the
                // call is counted with unknown tokens. A connection that never opened (HttpRequestException) is not.
                if (ex is TaskCanceledException && meter is not null)
                {
                    await meter.RecordFailedAsync(VisionUsage.Unknown, CancellationToken.None);
                }

                throw new VisionClientException("Vision request failed before a response arrived.", ex);
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                {
                    // Round 13 — money: the tokens this answer reports, before the tool input is pulled out of it.
                    if (meter is not null)
                    {
                        await meter.RecordAsync(VisionUsage.ReadFrom(text), CancellationToken.None);
                    }

                    return ExtractToolInput(text, request.Tool.Name, options.Value.MaxTokens);
                }

                // Raw API errors stay in server logs only; the user gets a localized retry message.
                logger.LogWarning("Anthropic API returned {Status} on attempt {Attempt}: {Body}",
                    (int)response.StatusCode, attempt, Truncate(text));

                // Round 13 — money: the API answered, so it may have billed; count the call and any tokens the error
                // body carried. A retry that follows is a second call and is counted again, which is what it costs.
                if (meter is not null)
                {
                    await meter.RecordFailedAsync(VisionUsage.ReadFrom(text), CancellationToken.None);
                }

                var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (!retryable || attempt >= 2)
                {
                    throw new VisionClientException($"Anthropic API returned {(int)response.StatusCode}.");
                }
            }

            await Task.Delay(RetryBackoff, ct);
        }
    }

    /// <summary>
    /// The request, field by field. There is deliberately no <c>temperature</c>, <c>top_p</c> or <c>top_k</c> here:
    /// claude-sonnet-5 removed all three from the API and answers 400 to a request that carries one. Consistency of the
    /// scores therefore comes from the rubric's anchored bands (<see cref="OutfitAnalyzer"/>, "THE SCALE") and is
    /// measured rather than assumed — <c>tools/eval/stylist.js</c> runs the same photo N times and prints the spread.
    /// Do not add a sampling knob here to "make it stable": it would fail every call instead.
    /// </summary>
    private object BuildBody(VisionRequest request) => new
    {
        model = options.Value.Model,
        max_tokens = options.Value.MaxTokens,
        system = request.SystemPrompt,
        // A 10-second check does not need reasoning tokens, and max_tokens is shared with thinking on current models.
        thinking = new { type = "disabled" },
        tools = new[]
        {
            new
            {
                name = request.Tool.Name,
                description = request.Tool.Description,
                input_schema = request.Tool.InputSchema
            }
        },
        tool_choice = new { type = "tool", name = request.Tool.Name },
        messages = new[]
        {
            new
            {
                role = "user",
                content = BuildContent(request)
            }
        }
    };

    /// <summary>
    /// One image: the image block, then the text. Two (a comparison): a label, the first image, a label, the second image,
    /// then the text, so the model can tell "Outfit A" from "Outfit B" by the words in front of each photo. The one-image
    /// shape is byte-for-byte what it always was.
    /// </summary>
    private static object[] BuildContent(VisionRequest request)
    {
        // No photograph: the text is the whole message. The recap reasons over numbers, not a picture.
        if (!request.HasImage)
        {
            return [new { type = "text", text = request.UserText }];
        }

        if (!request.HasSecondImage)
        {
            return
            [
                ImageBlock(request.MediaType, request.ImageBytes),
                new { type = "text", text = request.UserText }
            ];
        }

        return
        [
            new { type = "text", text = "Outfit A:" },
            ImageBlock(request.MediaType, request.ImageBytes),
            new { type = "text", text = "Outfit B:" },
            ImageBlock(request.MediaType2!, request.ImageBytes2),
            new { type = "text", text = request.UserText }
        ];
    }

    private static object ImageBlock(string mediaType, ReadOnlyMemory<byte> bytes) => new
    {
        type = "image",
        source = new
        {
            type = "base64",
            media_type = mediaType,
            data = Convert.ToBase64String(bytes.Span)
        }
    };

    private static JsonElement ExtractToolInput(string responseJson, string toolName, int maxTokens)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(responseJson);
        }
        catch (JsonException ex)
        {
            throw new VisionClientException("The API returned a response that was not JSON.", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new VisionClientException("The API response was not an object.");
            }

            var stop = StringProperty(root, "stop_reason");
            if (stop == "refusal")
            {
                throw new VisionRefusedException("The API refused to process this image.");
            }

            // A tool call cut off at max_tokens still arrives as a tool_use block, carrying the fields the model had
            // finished and nothing after them. The schema's order is status, score, intent_match, headline, vibe, then
            // items, working, one_tip, breakdown, accessories — so a cut lands the cheap strings and loses the entire
            // verdict, and the result screen drew a score and a headline with nothing underneath. Nothing errored,
            // nothing logged, and the person had paid for it. It is a failed answer, and it says which one.
            if (stop == "max_tokens")
            {
                throw new VisionClientException(
                    $"The answer was cut off at max_tokens ({maxTokens.ToString(CultureInfo.InvariantCulture)}); raise Anthropic:MaxTokens.");
            }

            if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.ValueKind == JsonValueKind.Object
                        && StringProperty(block, "type") == "tool_use"
                        && StringProperty(block, "name") == toolName
                        && block.TryGetProperty("input", out var input))
                    {
                        // The document is disposed on return; Clone detaches the element from it.
                        return input.Clone();
                    }
                }
            }
        }

        throw new VisionClientException("The model did not call the feedback tool.");
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Truncate(string text) => text.Length <= 2000 ? text : text[..2000] + "…";
}

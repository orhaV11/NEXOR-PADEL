using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Domain;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>
/// Anthropic Messages API over a plain HttpClient: base64 image block + forced tool call.
/// No SDK, so the request shape is spelled out here and the API key is read from the environment only.
/// </summary>
public sealed class AnthropicVisionClient(
    HttpClient http,
    IOptions<AnthropicOptions> options,
    ILogger<AnthropicVisionClient> logger) : IOutfitVisionClient
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
                throw new VisionClientException("Vision request failed before a response arrived.", ex);
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                {
                    return ExtractToolInput(text, request.Tool.Name);
                }

                // Raw API errors stay in server logs only; the user gets a localized retry message.
                logger.LogWarning("Anthropic API returned {Status} on attempt {Attempt}: {Body}",
                    (int)response.StatusCode, attempt, Truncate(text));

                var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (!retryable || attempt >= 2)
                {
                    throw new VisionClientException($"Anthropic API returned {(int)response.StatusCode}.");
                }
            }

            await Task.Delay(RetryBackoff, ct);
        }
    }

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
                content = new object[]
                {
                    new
                    {
                        type = "image",
                        source = new
                        {
                            type = "base64",
                            media_type = request.MediaType,
                            data = Convert.ToBase64String(request.ImageBytes.Span)
                        }
                    },
                    new { type = "text", text = request.UserText }
                }
            }
        }
    };

    private static JsonElement ExtractToolInput(string responseJson, string toolName)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("stop_reason", out var stop) && stop.ValueKind == JsonValueKind.String
            && stop.GetString() == "refusal")
        {
            throw new VisionRefusedException("The API refused to process this image.");
        }

        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "tool_use"
                    && block.TryGetProperty("name", out var name) && name.GetString() == toolName
                    && block.TryGetProperty("input", out var input))
                {
                    // The document is disposed on return; Clone detaches the element from it.
                    return input.Clone();
                }
            }
        }

        throw new VisionClientException("The model did not call the feedback tool.");
    }

    private static string Truncate(string text) => text.Length <= 2000 ? text : text[..2000] + "…";
}

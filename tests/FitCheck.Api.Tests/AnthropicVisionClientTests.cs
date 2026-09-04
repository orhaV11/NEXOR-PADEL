using System.Net;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Tests;

/// <summary>
/// The real client against a scripted HttpMessageHandler: everything but the socket. Covers the request shape the
/// Messages API expects, the single retry, refusal handling and the failure modes that must become a 502.
/// </summary>
public class AnthropicVisionClientTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];
        public Queue<Func<HttpResponseMessage>> Responses { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return Responses.Dequeue()();
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string ToolUseResponse(object input, string stopReason = "tool_use") => JsonSerializer.Serialize(new
    {
        id = "msg_1", type = "message", role = "assistant", model = "claude-sonnet-5", stop_reason = stopReason,
        content = new object[] { new { type = "tool_use", id = "toolu_1", name = OutfitAnalyzer.ToolName, input } }
    });

    private static (AnthropicVisionClient Client, ScriptedHandler Handler) Create(string? apiKey = "test-key")
    {
        Environment.SetEnvironmentVariable(AnthropicVisionClient.ApiKeyVariable, apiKey);
        var handler = new ScriptedHandler();
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var options = Options.Create(new AnthropicOptions { Model = "claude-sonnet-5", MaxTokens = 1200, BaseUrl = "https://api.anthropic.test/" });
        return (new AnthropicVisionClient(http, options, NullLogger<AnthropicVisionClient>.Instance), handler);
    }

    private static VisionRequest Request() => new(
        OutfitAnalyzer.BuildSystemPrompt("he"),
        OutfitAnalyzer.BuildUserMessage(StyleIntent.Date, "dinner"),
        TestImages.Jpeg(64),
        "image/jpeg",
        OutfitAnalyzer.Tool);

    [Fact]
    public async Task Sends_the_documented_request_shape_and_returns_the_tool_input()
    {
        var (client, handler) = Create();
        handler.Responses.Enqueue(() => Json(HttpStatusCode.OK, ToolUseResponse(new { status = "ok", score = 6 })));

        var input = await client.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Equal("ok", input.GetProperty("status").GetString());
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.anthropic.test/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("test-key", request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var root = body.RootElement;
        Assert.Equal("claude-sonnet-5", root.GetProperty("model").GetString());
        Assert.Equal(1200, root.GetProperty("max_tokens").GetInt32());
        Assert.Contains("in Hebrew (he)", root.GetProperty("system").GetString());
        Assert.Equal("disabled", root.GetProperty("thinking").GetProperty("type").GetString());

        var tool = Assert.Single(root.GetProperty("tools").EnumerateArray());
        Assert.Equal(OutfitAnalyzer.ToolName, tool.GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(tool.GetProperty("description").GetString()));
        Assert.Equal(8, tool.GetProperty("input_schema").GetProperty("required").GetArrayLength());

        Assert.Equal("tool", root.GetProperty("tool_choice").GetProperty("type").GetString());
        Assert.Equal(OutfitAnalyzer.ToolName, root.GetProperty("tool_choice").GetProperty("name").GetString());

        var message = Assert.Single(root.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        var content = message.GetProperty("content").EnumerateArray().ToList();
        Assert.Equal(2, content.Count);
        Assert.Equal("image", content[0].GetProperty("type").GetString());
        var source = content[0].GetProperty("source");
        Assert.Equal("base64", source.GetProperty("type").GetString());
        Assert.Equal("image/jpeg", source.GetProperty("media_type").GetString());
        Assert.Equal(Convert.ToBase64String(TestImages.Jpeg(64)), source.GetProperty("data").GetString());
        Assert.Equal("text", content[1].GetProperty("type").GetString());
        Assert.StartsWith("Stated intent: Date:", content[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Retries_once_on_5xx_then_succeeds()
    {
        var (client, handler) = Create();
        handler.Responses.Enqueue(() => Json((HttpStatusCode)529, """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}"""));
        handler.Responses.Enqueue(() => Json(HttpStatusCode.OK, ToolUseResponse(new { status = "ok" })));

        var input = await client.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Equal("ok", input.GetProperty("status").GetString());
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Retries_once_on_429_then_gives_up()
    {
        var (client, handler) = Create();
        handler.Responses.Enqueue(() => Json(HttpStatusCode.TooManyRequests, """{"type":"error"}"""));
        handler.Responses.Enqueue(() => Json(HttpStatusCode.TooManyRequests, """{"type":"error"}"""));

        await Assert.ThrowsAsync<VisionClientException>(() => client.AnalyzeAsync(Request(), CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Does_not_retry_client_errors()
    {
        var (client, handler) = Create();
        handler.Responses.Enqueue(() => Json(HttpStatusCode.BadRequest, """{"type":"error","error":{"message":"bad"}}"""));

        await Assert.ThrowsAsync<VisionClientException>(() => client.AnalyzeAsync(Request(), CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Refusal_stop_reason_is_a_refusal_not_a_failure()
    {
        var (client, handler) = Create();
        handler.Responses.Enqueue(() => Json(HttpStatusCode.OK, """{"id":"m","type":"message","role":"assistant","stop_reason":"refusal","content":[]}"""));

        await Assert.ThrowsAsync<VisionRefusedException>(() => client.AnalyzeAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Missing_tool_use_block_is_a_failure()
    {
        var (client, handler) = Create();
        handler.Responses.Enqueue(() => Json(HttpStatusCode.OK, """{"id":"m","type":"message","role":"assistant","stop_reason":"end_turn","content":[{"type":"text","text":"Nice outfit"}]}"""));

        var ex = await Assert.ThrowsAsync<VisionClientException>(() => client.AnalyzeAsync(Request(), CancellationToken.None));
        Assert.IsNotType<VisionRefusedException>(ex);
    }

    [Fact]
    public async Task Connection_failures_are_not_retried()
    {
        var (client, handler) = Create();
        handler.Responses.Enqueue(() => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<VisionClientException>(() => client.AnalyzeAsync(Request(), CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Missing_api_key_fails_before_sending_anything()
    {
        var (client, handler) = Create(apiKey: null);
        try
        {
            var ex = await Assert.ThrowsAsync<VisionClientException>(() => client.AnalyzeAsync(Request(), CancellationToken.None));
            Assert.Contains(AnthropicVisionClient.ApiKeyVariable, ex.Message);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AnthropicVisionClient.ApiKeyVariable, "test-key");
        }
    }
}

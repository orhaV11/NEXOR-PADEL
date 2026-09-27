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

    private static (AnthropicVisionClient Client, ScriptedHandler Handler) Create(string? apiKey = "test-key", string promptCache = "off")
    {
        Environment.SetEnvironmentVariable(AnthropicVisionClient.ApiKeyVariable, apiKey);
        var handler = new ScriptedHandler();
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var options = Options.Create(new AnthropicOptions { Model = "claude-sonnet-5", MaxTokens = 1200, BaseUrl = "https://api.anthropic.test/", PromptCache = promptCache });
        return (new AnthropicVisionClient(http, options, NullLogger<AnthropicVisionClient>.Instance), handler);
    }

    /// <summary>The system blocks of the one request the handler saw.</summary>
    private static List<JsonElement> SystemBlocks(ScriptedHandler handler)
    {
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var system = body.RootElement.GetProperty("system");
        Assert.Equal(JsonValueKind.Array, system.ValueKind);
        return system.EnumerateArray().Select(b => b.Clone()).ToList();
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
        // Round 20: the system prompt is an array of text blocks whether caching is on or off (one wire shape). With the
        // setting off (the default) the rubric block carries no cache_control at all.
        var system = Assert.Single(root.GetProperty("system").EnumerateArray());
        Assert.Equal("text", system.GetProperty("type").GetString());
        Assert.Contains("in Hebrew (he)", system.GetProperty("text").GetString());
        Assert.False(system.TryGetProperty("cache_control", out _));
        Assert.Equal("disabled", root.GetProperty("thinking").GetProperty("type").GetString());

        var tool = Assert.Single(root.GetProperty("tools").EnumerateArray());
        Assert.Equal(OutfitAnalyzer.ToolName, tool.GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(tool.GetProperty("description").GetString()));
        // The analyzer's schema goes over verbatim (11 required fields: v2 added breakdown and accessories, v5 tip_kind).
        Assert.Equal(OutfitAnalyzer.ToolSchema.GetProperty("required").GetArrayLength(), tool.GetProperty("input_schema").GetProperty("required").GetArrayLength());
        Assert.Equal(11, tool.GetProperty("input_schema").GetProperty("required").GetArrayLength());

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
        Assert.StartsWith("Occasion: Date:", content[1].GetProperty("text").GetString());
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

    /// <summary>
    /// Round 17 — a connection that never opened IS retried, once. It was not before, and the reasoning has changed:
    /// nobody bills for a socket that never opened, so the retry costs no money and only the backoff in latency,
    /// while a single TCP reset is common enough that refusing to repeat it turns ordinary network noise into a 502
    /// somebody sees. A response that ARRIVED and failed is still the other case — that one was billed, and its
    /// retry rules (429 and 5xx only, never a 4xx) are unchanged.
    /// </summary>
    [Fact]
    public async Task A_connection_that_never_opened_is_retried_once()
    {
        var (client, handler) = Create();
        handler.Responses.Enqueue(() => throw new HttpRequestException("connection refused"));
        handler.Responses.Enqueue(() => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<VisionClientException>(() => client.AnalyzeAsync(Request(), CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_connection_that_opens_on_the_retry_is_answered_normally()
    {
        var (client, handler) = Create();
        handler.Responses.Enqueue(() => throw new HttpRequestException("connection refused"));
        handler.Responses.Enqueue(() => Json(HttpStatusCode.OK, ToolUseResponse(new { status = "ok", score = 6 })));

        var result = await client.AnalyzeAsync(Request(), CancellationToken.None);
        Assert.Equal(6, result.GetProperty("score").GetInt32());
        Assert.Equal(2, handler.Requests.Count);
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
    /// <summary>
    /// Round 16: an answer cut off at max_tokens is a failed answer, not a verdict. A truncated tool call still arrives
    /// as a tool_use block carrying the fields the model finished and nothing after them, and the schema's order puts
    /// the cheap strings first — status, score, intent_match, headline, vibe — and the whole verdict after them: every
    /// garment, what is working, the tip, the breakdown. So the cut was invisible, the check was charged for, and the
    /// person read a score and a headline with nothing underneath. stop_reason said so all along and nobody read it.
    /// </summary>
    [Fact]
    public async Task An_answer_cut_off_at_max_tokens_is_refused_and_names_the_setting()
    {
        var (client, handler) = Create();
        handler.Responses.Enqueue(() => Json(HttpStatusCode.OK, ToolUseResponse(new
        {
            status = "ok", score = 5, intent_match = 30,
            headline = "Comfortable family-dinner casual, not party or old money",
            vibe = "relaxed off-duty casual"
        }, stopReason: "max_tokens")));

        var error = await Assert.ThrowsAsync<VisionClientException>(() => client.AnalyzeAsync(Request(), CancellationToken.None));
        Assert.Contains("max_tokens", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1200", error.Message, StringComparison.Ordinal);
        // One call: the same prompt under the same ceiling would be cut in the same place, so this is not retried.
        Assert.Single(handler.Requests);
    }

    /// <summary>The ordinary stop_reason for a forced tool call still returns the input, unchanged.</summary>
    [Fact]
    public async Task A_complete_tool_call_is_untouched()
    {
        var (client, handler) = Create();
        handler.Responses.Enqueue(() => Json(HttpStatusCode.OK, ToolUseResponse(new { status = "ok", score = 6 })));
        var input = await client.AnalyzeAsync(Request(), CancellationToken.None);
        Assert.Equal(6, input.GetProperty("score").GetInt32());
    }


    // ---------- Round 20: prompt caching ----------

    /// <summary>
    /// A check's rubric and tool are the same for every call in a language, so with Anthropic:PromptCache=5m the rubric
    /// block carries the breakpoint. The API renders tools before system, so the schema is cached with it. The wearer's
    /// advisory is a second block after the breakpoint with no cache_control: it changes per person, and caching it
    /// would write an entry per wearer that nobody reads back. The user message is what it always was.
    /// </summary>
    [Fact]
    public async Task A_shared_rubric_carries_a_five_minute_breakpoint_when_the_setting_says_so()
    {
        var (client, handler) = Create(promptCache: "5m");
        handler.Responses.Enqueue(() => Json(HttpStatusCode.OK, ToolUseResponse(new { status = "ok", score = 6 })));

        await client.AnalyzeAsync(Request() with { SystemAdvisory = "advisory", SharedRubric = true }, CancellationToken.None);

        var blocks = SystemBlocks(handler);
        Assert.Equal(2, blocks.Count);
        Assert.Contains("in Hebrew (he)", blocks[0].GetProperty("text").GetString());
        var control = blocks[0].GetProperty("cache_control");
        Assert.Equal("ephemeral", control.GetProperty("type").GetString());
        Assert.False(control.TryGetProperty("ttl", out _));
        Assert.Equal("advisory", blocks[1].GetProperty("text").GetString());
        Assert.False(blocks[1].TryGetProperty("cache_control", out _));

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var content = Assert.Single(body.RootElement.GetProperty("messages").EnumerateArray()).GetProperty("content").EnumerateArray().ToList();
        Assert.Equal(2, content.Count);
        Assert.Equal("image", content[0].GetProperty("type").GetString());
        Assert.StartsWith("Occasion: Date:", content[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task An_hour_breakpoint_names_its_ttl()
    {
        var (client, handler) = Create(promptCache: "1h");
        handler.Responses.Enqueue(() => Json(HttpStatusCode.OK, ToolUseResponse(new { status = "ok" })));

        await client.AnalyzeAsync(Request() with { SharedRubric = true }, CancellationToken.None);

        var block = Assert.Single(SystemBlocks(handler));
        var control = block.GetProperty("cache_control");
        Assert.Equal("ephemeral", control.GetProperty("type").GetString());
        Assert.Equal("1h", control.GetProperty("ttl").GetString());
    }

    /// <summary>
    /// A planned outfit's tool carries the wearer's wardrobe as an enum, so nothing before its rubric is shared with any
    /// other request; the analyzer marks it SharedRubric=false and the client sends no breakpoint anywhere, whatever
    /// the setting says.
    /// </summary>
    [Fact]
    public async Task A_request_whose_tool_is_not_shared_never_carries_a_breakpoint()
    {
        var (client, handler) = Create(promptCache: "5m");
        handler.Responses.Enqueue(() => Json(HttpStatusCode.OK, ToolUseResponse(new { status = "ok" })));

        await client.AnalyzeAsync(Request() with { SystemAdvisory = "advisory", SharedRubric = false }, CancellationToken.None);

        Assert.DoesNotContain("cache_control", handler.Bodies[0], StringComparison.Ordinal);
        Assert.Equal(2, SystemBlocks(handler).Count);
    }

    [Fact]
    public async Task An_unknown_cache_value_is_off()
    {
        var (client, handler) = Create(promptCache: "forever");
        handler.Responses.Enqueue(() => Json(HttpStatusCode.OK, ToolUseResponse(new { status = "ok" })));

        await client.AnalyzeAsync(Request() with { SharedRubric = true }, CancellationToken.None);

        Assert.DoesNotContain("cache_control", handler.Bodies[0], StringComparison.Ordinal);
    }
}

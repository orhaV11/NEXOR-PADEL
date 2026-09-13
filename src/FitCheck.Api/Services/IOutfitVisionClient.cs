using System.Text.Json;

namespace FitCheck.Api.Services;

/// <summary>A tool the model is forced to call so the answer comes back as JSON matching our schema.</summary>
public sealed record VisionTool(string Name, string Description, JsonElement InputSchema);

/// <summary>
/// One vision call: the instructions, one image and the tool the answer must come back through. A comparison ("which
/// one?") carries a second image: when <see cref="MediaType2"/> is set the client sends both, labelled Outfit A and
/// Outfit B, in that order. A request without one is sent exactly as it was before the second image existed.
/// </summary>
public sealed record VisionRequest(
    string SystemPrompt,
    string UserText,
    ReadOnlyMemory<byte> ImageBytes,
    string MediaType,
    VisionTool Tool,
    ReadOnlyMemory<byte> ImageBytes2 = default,
    string? MediaType2 = null)
{
    /// <summary>True for a two-outfit request: a second image with its own media type rides along.</summary>
    public bool HasSecondImage => MediaType2 is not null && !ImageBytes2.IsEmpty;
}

/// <summary>
/// Sends one image (or two, for a comparison) plus instructions to a vision model and returns the tool call's input as raw JSON.
/// Mapping into <see cref="Domain.OutfitFeedback"/> is the analyzer's job, so this can be mocked with a plain payload.
/// </summary>
public interface IOutfitVisionClient
{
    /// <exception cref="VisionClientException">The model could not be reached or did not call the tool.</exception>
    /// <exception cref="VisionRefusedException">The API refused the request at the safety layer.</exception>
    Task<JsonElement> AnalyzeAsync(VisionRequest request, CancellationToken ct);
}

public class VisionClientException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Distinct from a failure: the image was declined, so the check should read as rejected rather than error.</summary>
public sealed class VisionRefusedException(string message) : VisionClientException(message);

using System.Text.RegularExpressions;

namespace FitCheck.Api.Services;

/// <summary>
/// Pulls #tags and @mentions out of a caption. Pure and culture-invariant, so the same caption always
/// yields the same rows, in the order the writer typed them. The client never sends these; it only renders them.
/// </summary>
public static partial class CaptionParser
{
    public const int MaxTags = 5;
    public const int MaxMentions = 5;
    public const int TagMinLength = 2;
    public const int TagMaxLength = 30;
    public const int HandleMinLength = 2;
    public const int HandleMaxLength = 40;

    // A marker glued to the previous word (someone@example.com, item#42) is not a marker. A run longer than the
    // limit is not a tag or a handle either: it is left as plain text rather than cut to fit.
    [GeneratedRegex(@"(?<![\p{L}\p{N}_])#([\p{L}\p{N}_]{2,30})(?![\p{L}\p{N}_])")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}_])@([\p{L}\p{N}_.]{2,40})(?![\p{L}\p{N}_.])")]
    private static partial Regex MentionRegex();

    /// <summary>Tags without the #, lower-cased (invariant), distinct, the first five in order of appearance.</summary>
    public static List<string> Tags(string? caption)
    {
        var tags = new List<string>(MaxTags);
        if (string.IsNullOrEmpty(caption))
        {
            return tags;
        }

        foreach (Match match in TagRegex().Matches(caption))
        {
            var tag = match.Groups[1].Value.ToLowerInvariant();
            if (tags.Contains(tag))
            {
                continue;
            }

            tags.Add(tag);
            if (tags.Count == MaxTags)
            {
                break;
            }
        }

        return tags;
    }

    /// <summary>
    /// Handles without the @, as first written, with sentence-ending dots trimmed ("@brand." mentions brand).
    /// Distinct case-insensitively, the first five. Resolving them against accounts is the caller's job.
    /// </summary>
    public static List<string> Mentions(string? caption)
    {
        var handles = new List<string>(MaxMentions);
        if (string.IsNullOrEmpty(caption))
        {
            return handles;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in MentionRegex().Matches(caption))
        {
            var handle = match.Groups[1].Value.TrimEnd('.');
            if (handle.Length < HandleMinLength || !seen.Add(handle.ToLowerInvariant()))
            {
                continue;
            }

            handles.Add(handle);
            if (handles.Count == MaxMentions)
            {
                break;
            }
        }

        return handles;
    }
}

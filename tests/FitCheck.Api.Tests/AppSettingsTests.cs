using System.Globalization;
using System.Reflection;
using System.Text.Json;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 16 — the file that quietly wins. appsettings.json ships inside the image and outranks every default written
/// in C#, so a number pinned there is the number production runs whatever the code, the README and the runbook say.
/// That is how Anthropic:MaxTokens stayed at 1200 after the code default was raised to 3000 to stop verdicts being cut
/// off mid-JSON: the raise was real, the deployment never saw it, and the failure it was meant to fix went on
/// happening — billed in full, thrown away, and by then reported as an outright error rather than a partial answer.
/// <para>
/// So: every value appsettings.json pins must equal the default in code, unless it is listed below with the reason.
/// A default that moves without its pinned twin now fails here instead of in production. The list is the point — it
/// makes each deliberate divergence a decision somebody wrote down rather than a fact nobody knows.
/// </para>
/// </summary>
public class AppSettingsTests
{
    /// <summary>Section name → the options type it binds to. A section with no type here is not checked.</summary>
    private static readonly (string Section, Type Options)[] Bound =
    [
        (AnthropicOptions.Section, typeof(AnthropicOptions)),
        (StorageOptions.Section, typeof(StorageOptions)),
        (LimitsOptions.Section, typeof(LimitsOptions)),
        (PlanOptions.Section, typeof(PlanOptions)),
        (AlertOptions.Section, typeof(AlertOptions)),
        (LanguagesOptions.Section, typeof(LanguagesOptions)),
        (BoardOptions.Section, typeof(BoardOptions)),
        (AffiliateOptions.Section, typeof(AffiliateOptions)),
        ("Digest", typeof(DigestOptions)),
    ];

    /// <summary>
    /// "Section:Property" → why this one is deliberately different from the code default. Adding a line here is a
    /// decision; leaving one out is a bug that used to reach production.
    /// </summary>
    private static readonly Dictionary<string, string> Deliberate = new(StringComparer.Ordinal)
    {
        // The code default is off (0) so a fresh clone never sends a real call by surprise; the shipped file names the
        // pilot numbers the runbook talks about.
        ["Limits:SpendPerDayUsd"] = "0 here and 0 in code, but the doctor warns and DEPLOY.md tells the owner to set it",
    };

    private static JsonElement Settings()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "appsettings.json"));
        Assert.True(File.Exists(path), "appsettings.json is not where this test looks for it: " + path);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    [Fact]
    public void Nothing_pinned_in_appsettings_quietly_overrides_a_default_in_code()
    {
        var root = Settings();
        var divergences = new List<string>();
        foreach (var (section, type) in Bound)
        {
            if (!root.TryGetProperty(section, out var pinned) || pinned.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // The file's side is read straight out of the JSON rather than bound onto an instance: Microsoft's binder
            // APPENDS a configured array to whatever the property already holds, so binding ["en","he"] onto a default
            // of ["en","he"] yields four entries and every array would look like a divergence. (Harmless here only
            // because LanguagesOptions.List dedupes; worth knowing before pinning an array anywhere else.)
            var fromCode = Activator.CreateInstance(type)!;

            foreach (var property in pinned.EnumerateObject())
            {
                // "_note" keys are prose for whoever opens the file; they bind to nothing.
                if (property.Name.StartsWith('_'))
                {
                    continue;
                }

                var info = type.GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                Assert.True(info is not null, $"appsettings.json pins {section}:{property.Name}, which no property of {type.Name} binds — it does nothing at all.");

                var mine = Text(property.Value);
                var theirs = Text(info!.GetValue(fromCode));
                if (mine == theirs || Deliberate.ContainsKey($"{section}:{info.Name}"))
                {
                    continue;
                }

                divergences.Add($"{section}:{info.Name} — appsettings.json says {mine}, {type.Name} says {theirs}");
            }
        }

        Assert.True(divergences.Count == 0,
            "appsettings.json ships inside the image and outranks the code, so each of these is what production really runs.\n" +
            "Make them match, or add the key to AppSettingsTests.Deliberate with the reason:\n  " + string.Join("\n  ", divergences));
    }

    /// <summary>
    /// The one that caused this test. A ceiling below what a full verdict needs is not a saving: max_tokens is a
    /// ceiling, not a charge, and an answer cut off mid-JSON is billed in full and thrown away.
    /// </summary>
    [Fact]
    public void The_shipped_token_ceiling_leaves_room_for_a_whole_verdict()
    {
        var pinned = Settings().GetProperty("Anthropic").GetProperty("MaxTokens").GetInt32();
        Assert.Equal(new AnthropicOptions().MaxTokens, pinned);
        Assert.True(pinned >= 3000, $"Anthropic:MaxTokens is {pinned.ToString(CultureInfo.InvariantCulture)}: a verdict with its breakdown, its accessories read and its tip does not fit, and a cut-off answer is billed in full.");
    }

    /// <summary>What the code says, in one comparable form. Booleans are lowered so they read like JSON's.</summary>
    private static string Text(object? value) => value switch
    {
        null => "null",
        bool flag => flag ? "true" : "false",
        string text => text,
        // A dictionary reads like JSON's object, so an empty one matches an empty {} instead of looking like [].
        System.Collections.IDictionary map => "{" + string.Join(", ", map.Keys.Cast<object>().Select(k => k + ": " + Text(map[k]))) + "}",
        System.Collections.IEnumerable list => "[" + string.Join(", ", list.Cast<object?>().Select(Text)) + "]",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    /// <summary>What the file says, in that same form — read from the document, never through the binder.</summary>
    private static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "null",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(Text)) + "]",
        JsonValueKind.Object => "{" + string.Join(", ", value.EnumerateObject().Select(p => p.Name + ": " + Text(p.Value))) + "}",
        _ => value.GetRawText()
    };
}

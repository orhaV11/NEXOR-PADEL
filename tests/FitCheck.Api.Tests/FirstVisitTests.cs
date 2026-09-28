using System.Text.Json;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 16 — what the first outside tester ran into. Every finding here is about the same stretch of road: the
/// distance between somebody deciding to try this and a photograph actually being uploaded. On a server with five
/// checks in it, that stretch matters more than anything else in the app.
/// </summary>
public class FirstVisitTests
{
    private static Dictionary<string, string> Strings(string code)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "i18n", code + ".json"));
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api" }.Concat(parts).ToArray())));

    /// <summary>
    /// The sign-in notice is the default on ten screens, so it could only ever speak in generalities — and one of its
    /// generalities stopped being true the day guest checks were turned on: it told a visitor that checks need an
    /// account while the first one was free. It no longer claims that in any language.
    /// </summary>
    [Fact]
    public void The_shared_sign_in_notice_no_longer_says_a_check_needs_an_account()
    {
        // English is the one this test can read; the others are checked for the key's presence and for being changed.
        Assert.DoesNotContain("Checks", Strings("en")["auth.required_body"], StringComparison.OrdinalIgnoreCase);
        foreach (var code in new[] { "en", "he", "ar", "ru" })
        {
            Assert.False(string.IsNullOrWhiteSpace(Strings(code)["auth.required_body"]), $"{code}.json lost auth.required_body");
        }
    }

    /// <summary>"Which one?" really does need an account, and now the screen says that and nothing wider.</summary>
    [Fact]
    public void The_comparison_screen_says_what_it_actually_gates_in_every_language()
    {
        foreach (var code in new[] { "en", "he", "ar", "ru" })
        {
            var strings = Strings(code);
            foreach (var key in new[] { "compare.needs_account", "compare.needs_account_free" })
            {
                Assert.True(strings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value), $"{code}.json is missing {key}");
            }

            // The two differ: the second is the one a guest sees, and it has to add what they CAN do.
            Assert.NotEqual(strings["compare.needs_account"], strings["compare.needs_account_free"]);
        }

        var compare = Source("wwwroot", "app", "views", "compare.js");
        Assert.Contains("compare.needs_account_free", compare, StringComparison.Ordinal);
        // The choice is made from the server's own guest setting, not from a constant.
        Assert.Contains("state.config.plans.guestChecksPerDay > 0", compare, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Check yours" is the one button on a shared page aimed at somebody who has already decided. It pointed at the
    /// app root, which has no hash, and core.js resolves that to the feed — so the tester who tapped it landed in a
    /// look list. Three pages carried the same href; none does now.
    /// </summary>
    [Fact]
    public void Every_check_yours_button_lands_on_the_check_screen()
    {
        var page = Source("Endpoints", "PublicPageEndpoints.cs");
        var lines = page.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("public.check_yours", StringComparison.Ordinal))
            {
                continue;
            }

            // The href is on this line or the one above it, the way the builder is written.
            var around = (i > 0 ? lines[i - 1] : "") + lines[i];
            Assert.True(around.Contains("#/check", StringComparison.Ordinal),
                $"PublicPageEndpoints.cs:{i + 1} renders \"check yours\" without sending it to #/check, so it lands on the feed: {around.Trim()}");
        }
    }
}

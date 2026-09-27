using System.Text.Json;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 20 — the seams the wedge stands on, pinned on their own so a refactor that moves one is told by name: the
/// notification type and its width, the five nudge settings and their defaults, the new route in the enumeration, the
/// four server strings in every language, the three client keys in every locale file, and the DTO that drops its check
/// id off the wire when it has none.
/// </summary>
public class Round20SkeletonTests
{
    private static readonly string I18nRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "i18n"));

    [Fact]
    public void The_try_tip_type_fits_the_column()
    {
        Assert.Equal("try_tip", NotificationType.TryTip);
        Assert.True(NotificationType.TryTip.Length <= 16, "Notifications.Type is sixteen characters wide");
    }

    [Fact]
    public void The_nudge_settings_have_their_defaults()
    {
        var options = new PushOptions();
        Assert.True(options.TryTipNudge);
        Assert.Equal(24, options.TryTipAfterHours);
        Assert.Equal(24, options.TryTipWindowHours);
        Assert.Equal(9, options.TryTipDayStart);
        Assert.Equal(21, options.TryTipDayEnd);
    }

    [Fact]
    public void The_shared_pair_route_is_mapped()
    {
        using var app = new TestApp();
        Assert.Contains(("/api/checks/{id:guid}/tried/shared", "POST"), SecurityFixtures.ApiRoutes(app));
    }

    [Fact]
    public void The_four_server_strings_exist_in_every_language()
    {
        var localizer = new Localizer();
        foreach (var locale in Localizer.SupportedLocales)
        {
            foreach (var key in new[] { "push.try_tip", "public.before", "public.after", "public.one_change" })
            {
                var text = localizer.Get(locale, key);
                Assert.False(string.IsNullOrWhiteSpace(text) || text == key, $"{locale} {key}");
                Assert.DoesNotContain("!", text);
            }
        }

        Assert.Equal("Did you try the tip? Show me the look after the change.", localizer.Get("en", "push.try_tip"));
        Assert.Equal("One change", localizer.Get("en", "public.one_change"));
    }

    [Fact]
    public void The_client_keys_exist_in_all_four_locale_files()
    {
        foreach (var code in new[] { "en", "he", "ar", "ru" })
        {
            var keys = JsonDocument.Parse(File.ReadAllText(Path.Combine(I18nRoot, code + ".json"))).RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
            foreach (var key in new[] { "tried.try_tip", "tried.try_tip_hint", "activity.try_tip", "share.before_after_action", "push.hint" })
            {
                Assert.True(keys.Contains(key), $"{code}.json lacks {key}");
            }
        }

        // The settings copy says what push now sends: the day-after nudge is named.
        var en = JsonDocument.Parse(File.ReadAllText(Path.Combine(I18nRoot, "en.json"))).RootElement;
        Assert.Contains("a day after a tip", en.GetProperty("push.hint").GetString());
        Assert.Equal("Try the tip, then show me", en.GetProperty("tried.try_tip").GetString());
    }

    [Fact]
    public void A_notification_without_a_check_carries_no_checkId_on_the_wire()
    {
        var plain = new NotificationDto(Guid.NewGuid(), "fire", "h", "H", null, Guid.NewGuid(), null, DateTime.UtcNow, false);
        Assert.DoesNotContain("checkId", JsonSerializer.Serialize(plain, AppJson.Options));
        var check = Guid.NewGuid();
        var nudge = new NotificationDto(Guid.NewGuid(), NotificationType.TryTip, "h", "H", null, null, null, DateTime.UtcNow, false, CheckId: check);
        Assert.Contains($"\"checkId\":\"{check}\"", JsonSerializer.Serialize(nudge, AppJson.Options));
    }
}

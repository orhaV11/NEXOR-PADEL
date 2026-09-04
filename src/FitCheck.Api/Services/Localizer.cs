using System.Globalization;

namespace FitCheck.Api.Services;

/// <summary>
/// Server-side user-facing strings (error messages) in every shipped locale, plus the locale matching rule
/// shared by Accept-Language and explicit language fields: language-subtag match, otherwise English.
/// </summary>
public sealed class Localizer
{
    public const string DefaultLocale = "en";
    public static readonly string[] SupportedLocales = ["en", "he"];

    private static readonly Dictionary<string, string> LanguageNames = new()
    {
        ["en"] = "English",
        ["he"] = "Hebrew"
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Messages = new()
    {
        ["en"] = new()
        {
            ["error.handle_invalid"] = "Pick a handle between 2 and 40 characters.",
            ["error.age_required"] = "You need to confirm you are 16 or older to use FitCheck.",
            ["error.language_invalid"] = "Pick a supported language.",
            ["error.user_not_found"] = "We couldn't find this account.",
            ["error.check_not_found"] = "We couldn't find this check.",
            ["error.intent_invalid"] = "Pick where the outfit is going.",
            ["error.occasion_too_long"] = "Keep the occasion note under 120 characters.",
            ["error.image_required"] = "Add a photo of the outfit.",
            ["error.image_too_large"] = "That photo is too large. Try one under {0} MB.",
            ["error.image_format"] = "Use a JPEG, PNG or WebP photo.",
            ["error.rate_limited"] = "You've reached today's limit of {0} checks. Come back tomorrow.",
            ["error.model_failed"] = "The stylist couldn't look at this one. Please try again in a moment.",
            ["error.invalid_request"] = "That request didn't look right.",
            ["error.server"] = "Something went wrong on our side. Please try again.",
            ["feedback.rejected"] = "We can't check this photo.",
        },
        ["he"] = new()
        {
            ["error.handle_invalid"] = "הכינוי צריך להיות באורך 2 עד 40 תווים.",
            ["error.age_required"] = "כדי להשתמש ב-FitCheck צריך לאשר גיל 16 ומעלה.",
            ["error.language_invalid"] = "צריך לבחור שפה נתמכת.",
            ["error.user_not_found"] = "לא מצאנו את החשבון הזה.",
            ["error.check_not_found"] = "לא מצאנו את הבדיקה הזו.",
            ["error.intent_invalid"] = "צריך לבחור לאן הלוק הולך.",
            ["error.occasion_too_long"] = "הערת האירוע צריכה להיות עד 120 תווים.",
            ["error.image_required"] = "צריך להוסיף תמונה של הלוק.",
            ["error.image_too_large"] = "התמונה גדולה מדי. אפשר לנסות תמונה עד {0}MB.",
            ["error.image_format"] = "אפשר להעלות תמונה בפורמט JPEG, PNG או WebP.",
            ["error.rate_limited"] = "המכסה היומית של {0} בדיקות נוצלה. שווה לחזור מחר.",
            ["error.model_failed"] = "הסטייליסט לא הצליח להסתכל על התמונה הזו. שווה לנסות שוב בעוד רגע.",
            ["error.invalid_request"] = "הבקשה לא תקינה.",
            ["error.server"] = "משהו השתבש אצלנו. שווה לנסות שוב.",
            ["feedback.rejected"] = "אי אפשר לבדוק את התמונה הזו.",
        }
    };

    public static bool IsSupported(string? locale) =>
        locale is not null && Array.IndexOf(SupportedLocales, locale) >= 0;

    /// <summary>"he-IL" → "he", "fr-FR" → "en", "" → "en". Only the language subtag is compared.</summary>
    public static string Match(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return DefaultLocale;
        }

        var language = tag.Trim().Split('-', '_')[0].ToLowerInvariant();
        return IsSupported(language) ? language : DefaultLocale;
    }

    /// <summary>First supported entry of an Accept-Language header, honouring q ordering. Falls back to English.</summary>
    public static string MatchAcceptLanguage(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return DefaultLocale;
        }

        var ranked = header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((entry, index) =>
            {
                var parts = entry.Split(';', StringSplitOptions.TrimEntries);
                var quality = 1.0;
                if (parts.Length > 1 && parts[1].StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(parts[1][2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var q))
                {
                    quality = q;
                }

                return (tag: parts[0], quality, index);
            })
            .Where(x => x.quality > 0)
            .OrderByDescending(x => x.quality)
            .ThenBy(x => x.index);

        foreach (var (tag, _, _) in ranked)
        {
            var language = tag.Split('-', '_')[0].ToLowerInvariant();
            if (IsSupported(language))
            {
                return language;
            }
        }

        return DefaultLocale;
    }

    /// <summary>Locale for API messages: an explicit supported value wins, otherwise Accept-Language, otherwise English.</summary>
    public static string Resolve(string? explicitLanguage, HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(explicitLanguage))
        {
            var language = explicitLanguage.Trim().Split('-', '_')[0].ToLowerInvariant();
            if (IsSupported(language))
            {
                return language;
            }
        }

        return MatchAcceptLanguage(request.Headers.AcceptLanguage.ToString());
    }

    public static string LanguageName(string locale) =>
        LanguageNames.TryGetValue(locale, out var name) ? name : LanguageNames[DefaultLocale];

    public string Get(string locale, string key, params object[] args)
    {
        if (!Messages.TryGetValue(locale, out var table) || !table.TryGetValue(key, out var text))
        {
            text = Messages[DefaultLocale].TryGetValue(key, out var fallback) ? fallback : key;
        }

        return args.Length == 0 ? text : string.Format(CultureInfo.InvariantCulture, text, args);
    }
}

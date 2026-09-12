using FitCheck.Api.Data;
using FitCheck.Api.Domain;

namespace FitCheck.Api.Services;

/// <summary>
/// The stylist's item names, copied onto a look when it is posted so Explore can find looks by piece ("black boots").
/// Lower-cased and trimmed so a search term meets them as stored; the category rides along for the insights. The item
/// verdicts and notes stay private with the check, like the tip. Round 10: the rows carry <see cref="ItemSource.Stylist"/>
/// and their position; the brand is never copied from <see cref="OutfitItem.BrandSeen"/> (the person confirms it on the
/// post sheet, through PATCH /api/posts/{id}/items).
/// </summary>
public static class PostItems
{
    /// <summary>Rows the stylist may put on a look. The stylist names a handful of pieces; more than this is noise, not a wardrobe.</summary>
    public const int MaxPerPost = 8;

    /// <summary>Rows a person may keep on a look after tagging (PATCH /api/posts/{id}/items): the stylist's plus a few of their own.</summary>
    public const int MaxTagged = 12;

    /// <summary>Matches the column (AppDbContext) and the migration. A name a person types is held to <see cref="TypedNameMaxLength"/>.</summary>
    public const int NameMaxLength = 60;

    public const int TypedNameMaxLength = 40;
    public const int BrandMaxLength = 40;
    public const int ModelMaxLength = 60;
    public const int UrlMaxLength = 500;

    /// <summary>The OutfitItem categories, as the analyzer clamps them; anything else is stored as "other".</summary>
    public static readonly string[] Categories = ["top", "bottom", "dress", "outerwear", "shoes", "accessory", "other"];

    /// <summary>
    /// Adds up to <see cref="MaxPerPost"/> PostItem rows for the post to the context (not saved here): names lower-cased
    /// and cut to <see cref="NameMaxLength"/>, distinct by that name, categories from the items, in the stylist's order,
    /// source Stylist, no brand.
    /// </summary>
    public static void AddFrom(AppDbContext db, Guid postId, OutfitFeedback feedback)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in feedback.Items)
        {
            var name = NormalizeName(item.Name);
            if (name.Length == 0 || !seen.Add(name))
            {
                continue;
            }

            db.PostItems.Add(new PostItem
            {
                Id = Guid.NewGuid(),
                PostId = postId,
                Name = name,
                Category = NormalizeCategory(item.Category),
                Source = ItemSource.Stylist,
                Position = seen.Count - 1
            });
            if (seen.Count >= MaxPerPost)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Whether a store link may be stored and followed: absolute, http or https, nothing else (a data: or javascript: link
    /// behind "Shop at" would be an attack on the person who taps it). The URL is stored as given; this only says yes or no.
    /// </summary>
    public static bool IsStoreUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.Length <= UrlMaxLength
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host);

    /// <summary>The host of a store link for "Shop at {host}", without a leading "www."; null when the link is not one.</summary>
    public static string? HostOf(string? url)
    {
        if (!IsStoreUrl(url))
        {
            return null;
        }

        var host = new Uri(url!.Trim(), UriKind.Absolute).Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) && host.Length > 4 ? host[4..] : host;
    }

    /// <summary>"  Black  Boots " → "black boots": one space between words, lower-case, at most 60 characters.</summary>
    public static string NormalizeName(string? name)
    {
        var words = (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var joined = string.Join(' ', words).ToLowerInvariant();
        return joined.Length > NameMaxLength ? joined[..NameMaxLength].TrimEnd() : joined;
    }

    public static string NormalizeCategory(string? category)
    {
        var lower = (category ?? "").Trim().ToLowerInvariant();
        return Array.IndexOf(Categories, lower) >= 0 ? lower : "other";
    }
}

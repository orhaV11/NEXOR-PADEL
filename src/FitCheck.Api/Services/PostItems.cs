using FitCheck.Api.Data;
using FitCheck.Api.Domain;

namespace FitCheck.Api.Services;

/// <summary>
/// The stylist's item names, copied onto a look when it is posted so Explore can find looks by piece ("black boots").
/// Lower-cased and trimmed so a search term meets them as stored; the category rides along for the insights. The item
/// verdicts and notes stay private with the check, like the tip.
/// </summary>
public static class PostItems
{
    /// <summary>Rows per look. The stylist names a handful of pieces; more than this is noise, not a wardrobe.</summary>
    public const int MaxPerPost = 8;

    /// <summary>Matches the column (AppDbContext) and the migration.</summary>
    public const int NameMaxLength = 60;

    /// <summary>The OutfitItem categories, as the analyzer clamps them; anything else is stored as "other".</summary>
    public static readonly string[] Categories = ["top", "bottom", "dress", "outerwear", "shoes", "accessory", "other"];

    /// <summary>
    /// Adds up to <see cref="MaxPerPost"/> PostItem rows for the post to the context (not saved here): names lower-cased
    /// and cut to <see cref="NameMaxLength"/>, distinct by that name (the table's key), categories from the items.
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

            db.PostItems.Add(new PostItem { PostId = postId, Name = name, Category = NormalizeCategory(item.Category) });
            if (seen.Count >= MaxPerPost)
            {
                break;
            }
        }
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

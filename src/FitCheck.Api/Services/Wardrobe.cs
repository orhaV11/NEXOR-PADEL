using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>
/// Round 14 — the wardrobe that builds itself, and what it is for.
/// <para>
/// <b>Why it is not an onboarding.</b> Nobody photographs their closet. An hour of work before the first minute of value
/// is how these products die, so nothing here asks for a photo, a form or a category: the stylist already names the
/// pieces it can see on every check, and the result screen offers ONE line under the tip — "keep the camel coat?" — one
/// tap. A piece can only be kept from a check that named it (<see cref="NamesOn"/>), which is also what keeps this from
/// becoming a free-text store of whatever anyone types.
/// </para>
/// <para>
/// <b>What it is for.</b> With the wearer's own pieces in front of it, a tip can say "swap the black tights for the brown
/// ones you wore on the 4th" instead of "buy sheer brown tights". That is the difference between advice and shopping, and
/// it is what makes the advice worth paying for. <see cref="PromptNames"/> is the door the names go through: a handful,
/// most recently worn first, clothes only, each one cleaned and capped like any other stored string, and nothing that
/// names a person (a renamed piece is free text, and rule 1 holds there too).
/// </para>
/// <para>
/// <b>The coordination with the loop.</b> "I do not own that" is one of the typed reasons a person can give a tip. A tip
/// that draws that answer is exactly the tip a wardrobe should have prevented, so the two features measure each other:
/// the wardrobe's worth is the fall in that reason.
/// </para>
/// </summary>
public static class Wardrobe
{
    /// <summary>A piece's name is a name, not a sentence: the same length a tagged item may have (PostItems.NameMaxLength).</summary>
    public const int NameMaxLength = 60;

    /// <summary>The categories a kept piece may carry, the stylist's own (<see cref="OutfitItem.Category"/>).</summary>
    public static readonly string[] Categories = ["top", "bottom", "dress", "outerwear", "shoes", "accessory", "other"];

    /// <summary>
    /// The categories whose pieces travel to the stylist. "other" stays home: the wardrobe reaches the model as a list of
    /// CLOTHES, and a row the stylist itself could not place is not worth the tokens or the risk of being read as an
    /// instruction. Accessories count — "the gold hoops you wore on the 4th" is exactly the tip this exists for.
    /// </summary>
    public static readonly string[] PromptCategories = ["top", "bottom", "dress", "outerwear", "shoes", "accessory"];

    /// <summary>The category a piece falls back to when the client sends one the stylist does not use.</summary>
    public const string OtherCategory = "other";

    /// <summary>
    /// A piece's name as it may be stored: control characters out, runs of spaces collapsed, quotes turned into single
    /// quotes (the names travel inside a quoted block in the prompt), cut to <see cref="NameMaxLength"/> on a word where
    /// one is near the end. Empty when nothing is left, which the caller refuses.
    /// </summary>
    public static string CleanName(string? name)
    {
        var text = OutfitAnalyzer.SanitizeOccasion(name);
        if (text.Length <= NameMaxLength)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', NameMaxLength);
        return text[..(cut > NameMaxLength / 2 ? cut : NameMaxLength)].TrimEnd();
    }

    /// <summary>
    /// The key one piece is one row under: the cleaned name, lower-cased invariantly. "Camel coat" and "camel coat" are
    /// the same coat, and keeping it twice from two checks adds a second look to one row rather than a second row.
    /// </summary>
    public static string KeyOf(string cleanName) => cleanName.ToLowerInvariant();

    /// <summary>The category as it may be stored: the stylist's word, or <see cref="OtherCategory"/> for anything else.</summary>
    public static string CleanCategory(string? category)
    {
        var text = (category ?? "").Trim().ToLowerInvariant();
        return Array.IndexOf(Categories, text) >= 0 ? text : OtherCategory;
    }

    /// <summary>
    /// The piece names a check actually named, in order: the stylist's items, then the accessories it saw. This is the
    /// list the keep line offers and the list the keep route validates against, so the wardrobe can only ever hold
    /// clothes somebody was photographed wearing. Empty for a check that is not <see cref="CheckStatus.Ok"/>.
    /// </summary>
    public static List<WardrobeCandidate> NamesOn(OutfitCheck check)
    {
        if (check.Status != CheckStatus.Ok || check.FeedbackJson is null)
        {
            return [];
        }

        OutfitFeedback? feedback;
        try
        {
            feedback = JsonSerializer.Deserialize<OutfitFeedback>(check.FeedbackJson, AppJson.Options);
        }
        catch (JsonException)
        {
            return [];
        }

        if (feedback is null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<WardrobeCandidate>();
        foreach (var item in feedback.Items)
        {
            Add(CleanName(item.Name), CleanCategory(item.Category));
        }

        foreach (var piece in feedback.Accessories?.Present ?? [])
        {
            Add(CleanName(piece), "accessory");
        }

        return candidates;

        void Add(string name, string category)
        {
            if (name.Length > 0 && seen.Add(KeyOf(name)))
            {
                candidates.Add(new WardrobeCandidate(name, category));
            }
        }
    }

    /// <summary>
    /// The account's pieces, most recently worn first, with the checks each one appeared in (newest first). One read per
    /// table whatever the count. <paramref name="limit"/> caps the rows; 0 or less means all of them.
    /// </summary>
    public static async Task<List<(WardrobeItem Item, List<WardrobeAppearance> Looks)>> ListAsync(
        AppDbContext db, Guid userId, CancellationToken ct, int limit = 0)
    {
        var query = db.WardrobeItems.AsNoTracking().Where(i => i.UserId == userId)
            .OrderByDescending(i => i.LastSeenAt).ThenByDescending(i => i.CreatedAt);
        var items = limit > 0 ? await query.Take(limit).ToListAsync(ct) : await query.ToListAsync(ct);
        if (items.Count == 0)
        {
            return [];
        }

        var ids = items.Select(i => i.Id).ToList();
        var looks = await db.WardrobeAppearances.AsNoTracking()
            .Where(a => ids.Contains(a.ItemId))
            .OrderByDescending(a => a.WornAt)
            .ToListAsync(ct);
        var byItem = looks.ToLookup(a => a.ItemId);
        return items.Select(i => (i, byItem[i.Id].ToList())).ToList();
    }

    /// <summary>How many pieces this account keeps (the fair-use brake, Plans:WardrobeMaxItems).</summary>
    public static Task<int> CountAsync(AppDbContext db, Guid userId, CancellationToken ct) =>
        db.WardrobeItems.CountAsync(i => i.UserId == userId, ct);

    /// <summary>
    /// Keeps one piece of one check. A piece the account already has is not kept twice: the check is added to the row it
    /// already has and the row is marked worn, which is how "the brown tights you wore on the 4th" gets its date. Returns
    /// the row and whether it is new; the caller has already checked the name against <see cref="NamesOn"/> and the cap.
    /// </summary>
    public static async Task<(WardrobeItem Item, bool Added)> KeepAsync(
        AppDbContext db, Guid userId, OutfitCheck check, string name, string category, DateTime now, CancellationToken ct)
    {
        var key = KeyOf(name);
        var item = await db.WardrobeItems.FirstOrDefaultAsync(i => i.UserId == userId && i.NameKey == key, ct);
        var added = item is null;
        if (item is null)
        {
            item = new WardrobeItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = name,
                NameKey = key,
                Category = category,
                CreatedAt = now,
                LastSeenAt = check.CreatedAt
            };
            db.WardrobeItems.Add(item);
        }
        else if (check.CreatedAt > item.LastSeenAt)
        {
            item.LastSeenAt = check.CreatedAt;
        }

        var already = await db.WardrobeAppearances.AnyAsync(a => a.ItemId == item.Id && a.CheckId == check.Id, ct);
        if (!already)
        {
            db.WardrobeAppearances.Add(new WardrobeAppearance { ItemId = item.Id, CheckId = check.Id, WornAt = check.CreatedAt });
        }

        await db.SaveChangesAsync(ct);
        return (item, added);
    }

    /// <summary>
    /// The account's setting row, made on first use. The default is on: for an account the wardrobe reaches the stylist
    /// for, that IS the feature, and a person who would rather their own piece names stayed off the wire turns it off.
    /// </summary>
    public static async Task<WardrobeSetting> SettingAsync(AppDbContext db, Guid userId, DateTime now, CancellationToken ct)
    {
        var setting = await db.WardrobeSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (setting is not null)
        {
            return setting;
        }

        setting = new WardrobeSetting { UserId = userId, ToStylist = true, UpdatedAt = now };
        db.WardrobeSettings.Add(setting);
        await db.SaveChangesAsync(ct);
        return setting;
    }

    /// <summary>
    /// The names that travel with a check, most recently worn first: at most <paramref name="max"/>, clothes only
    /// (<see cref="PromptCategories"/>), each one cleaned, and never one that names a body, a face, an age or a gender —
    /// a renamed piece is free text the person typed, and rule 1 is not negotiable there either. Duplicates are dropped.
    /// Empty when <paramref name="max"/> is 0 or nothing survives, and an empty list puts NOTHING in the prompt.
    /// </summary>
    public static List<string> PromptNames(IEnumerable<WardrobeItem> items, int max)
    {
        if (max <= 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var item in items.OrderByDescending(i => i.LastSeenAt).ThenByDescending(i => i.CreatedAt))
        {
            if (Array.IndexOf(PromptCategories, item.Category) < 0)
            {
                continue;
            }

            var name = CleanName(item.Name);
            if (name.Length == 0 || OutfitAnalyzer.MentionsPerson(name) || !seen.Add(KeyOf(name)))
            {
                continue;
            }

            names.Add(name);
            if (names.Count >= max)
            {
                break;
            }
        }

        return names;
    }

    /// <summary>
    /// The names this check should carry, or an empty list. Three things have to be true: the caller is an account whose
    /// plan lets the wardrobe reach the stylist (Plans:WardrobeNeedsPro), that account has not turned it off, and
    /// Plans:WardrobeNamesToStylist is above zero. A guest has no wardrobe and this never reads the database for one.
    /// </summary>
    public static async Task<List<string>> ForStylistAsync(
        AppDbContext db, AppUser? user, PlanOptions plans, DateTime now, CancellationToken ct)
    {
        if (!Plans.WardrobeReachesStylist(user, plans, now) || plans.WardrobeNamesToStylist <= 0)
        {
            return [];
        }

        var userId = user!.Id;
        var setting = await db.WardrobeSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (setting is { ToStylist: false })
        {
            return [];
        }

        // Round 16: how much of the closet the stylist gets to see is what Pro buys. Free keeps the same handful it
        // always had; Pro's is wider, so a tip can reach for the piece somebody actually owns rather than one of the
        // same twelve. About 110 more input tokens on a call - a tenth of a cent.
        var names = plans.WardrobeNamesFor(Plans.IsPro(user, now));

        // A few more rows than the prompt takes, so the ones dropped for being "other" or for naming a person do not
        // leave the list short; PromptNames does the ordering and the cut again over what comes back.
        var items = await db.WardrobeItems.AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.LastSeenAt).ThenByDescending(i => i.CreatedAt)
            .Take(names * 3)
            .ToListAsync(ct);
        return PromptNames(items, names);
    }
}

/// <summary>A piece a check named, offered for keeping: the cleaned name and the stylist's category.</summary>
public sealed record WardrobeCandidate(string Name, string Category);

/// <summary>
/// One piece somebody owns, kept from a check that named it. No photo: the wardrobe is a list of names, and a name is
/// all the stylist needs to say "the brown ones you wore on the 4th". <see cref="LastSeenAt"/> is the most recent check
/// it appeared in, which is the order the list and the prompt read in.
/// </summary>
public sealed class WardrobeItem
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>As the stylist named it, or as the person renamed it. At most <see cref="Wardrobe.NameMaxLength"/>.</summary>
    public string Name { get; set; } = "";

    /// <summary><see cref="Name"/> lower-cased: one row per piece per account, enforced by the unique index.</summary>
    public string NameKey { get; set; } = "";

    /// <summary>One of <see cref="Wardrobe.Categories"/>. Only <see cref="Wardrobe.PromptCategories"/> travel to the stylist.</summary>
    public string Category { get; set; } = Wardrobe.OtherCategory;

    public DateTime CreatedAt { get; set; }

    /// <summary>The newest check this piece appeared in: what "you wore on the 4th" is read from.</summary>
    public DateTime LastSeenAt { get; set; }
}

/// <summary>One look a kept piece appeared in. The pair is the key: a check can only add a piece once.</summary>
public sealed class WardrobeAppearance
{
    public Guid ItemId { get; set; }

    public Guid CheckId { get; set; }

    /// <summary>The check's own time, so the list reads in the order the looks happened rather than the order they were kept.</summary>
    public DateTime WornAt { get; set; }
}

/// <summary>
/// One account's say over its own wardrobe. Only one thing to say so far: whether the piece names travel with a check.
/// A row of its own rather than a column on the account, so Round 14's four builders do not share a table.
/// </summary>
public sealed class WardrobeSetting
{
    public Guid UserId { get; set; }

    /// <summary>Whether this account's pieces reach the stylist. On by default; that is the feature.</summary>
    public bool ToStylist { get; set; } = true;

    public DateTime UpdatedAt { get; set; }
}

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
/// <para>
/// <b>Round 20 — filling it faster.</b> One tap a piece is honest but slow: three checks is nine taps. So the keep row
/// can keep every piece a check named in one request (<see cref="KeepAllAsync"/>, the names still the server's own
/// <see cref="NamesOn"/>), and the wardrobe screen lists the pieces named on the person's latest looks that were never
/// kept (<see cref="UnkeptAsync"/>), each keepable through the same single route with the check that named it. The
/// wardrobe records no refusals, so a piece passed over in the keep row simply appears there again.
/// </para>
/// <para>
/// <b>Review of Round 20 — a piece is the row, whatever it is called.</b> A rename changes the name and its key, so each
/// row also keeps the stylist's own key it was kept under (<see cref="WardrobeItem.StylistKey"/>): a check that names the
/// piece again adds a look to that row, and the unkept list never offers it back as never kept.
/// </para>
/// </summary>
public static class Wardrobe
{
    /// <summary>The most pieces the "keep from an older look" list offers at once: a screen, not an archive.</summary>
    public const int UnkeptMaxPieces = 30;

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
    public static List<WardrobeCandidate> NamesOn(OutfitCheck check) => NamesOn(check.Status, check.FeedbackJson);

    /// <summary>
    /// The same list from a check's two columns alone, so a query that projects only the status and the stored feedback
    /// (Round 20's unkept list reads twenty checks at once) never has to build a whole <see cref="OutfitCheck"/> to ask.
    /// </summary>
    public static List<WardrobeCandidate> NamesOn(string status, string? feedbackJson)
    {
        if (status != CheckStatus.Ok || feedbackJson is null)
        {
            return [];
        }

        OutfitFeedback? feedback;
        try
        {
            feedback = JsonSerializer.Deserialize<OutfitFeedback>(feedbackJson, AppJson.Options);
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

    /// <summary>
    /// Review of Round 20 — the row a stylist's name belongs to: the piece that carries it now, or else the piece that was
    /// kept under it and renamed since (<see cref="WardrobeItem.StylistKey"/>). Null when this account has neither.
    /// </summary>
    public static async Task<WardrobeItem?> FindAsync(AppDbContext db, Guid userId, string key, CancellationToken ct)
    {
        var rows = await db.WardrobeItems.Where(i => i.UserId == userId && (i.NameKey == key || i.StylistKey == key)).ToListAsync(ct);
        return rows.FirstOrDefault(i => i.NameKey == key) ?? rows.FirstOrDefault();
    }

    /// <summary>How many pieces this account keeps (the fair-use brake, Plans:WardrobeMaxItems).</summary>
    public static Task<int> CountAsync(AppDbContext db, Guid userId, CancellationToken ct) =>
        db.WardrobeItems.CountAsync(i => i.UserId == userId, ct);

    /// <summary>
    /// Keeps one piece of one check. A piece the account already has is not kept twice: the check is added to the row it
    /// already has and the row is marked worn, which is how "the brown tights you wore on the 4th" gets its date; a piece
    /// the person renamed is still the piece (<see cref="FindAsync"/>). Returns the row and whether it is new; the caller
    /// has already checked the name against <see cref="NamesOn"/> and the cap.
    /// </summary>
    public static async Task<(WardrobeItem Item, bool Added)> KeepAsync(
        AppDbContext db, Guid userId, OutfitCheck check, string name, string category, DateTime now, CancellationToken ct)
    {
        var key = KeyOf(name);
        var item = await FindAsync(db, userId, key, ct);
        var added = item is null;
        if (item is null)
        {
            item = new WardrobeItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = name,
                NameKey = key,
                StylistKey = key,
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
    /// Round 20 — every piece a check named, in one request. The candidates are the caller's <see cref="NamesOn"/> of
    /// the check, so nothing here can be free text. A known piece gets this look added to its row (and is never refused
    /// by the cap, exactly as the single route never refuses one); an unknown piece is added while the account is
    /// under <paramref name="max"/> and skipped once it is not, in the stylist's order, so what fits is the first of the
    /// list. One read of the rows the names could already be, one count, one save. Returns the pieces of this look now in
    /// the wardrobe in the stylist's order, how many were new, how many the cap kept out, and (review of Round 20) how
    /// many rows were actually written — a new row, a new look on a known one, or a later "last worn" — so a repeat of
    /// the same request, which changes nothing, can be told from one that kept something.
    /// </summary>
    public static async Task<(List<WardrobeItem> Items, int Added, int Skipped, int Wrote)> KeepAllAsync(
        AppDbContext db, Guid userId, OutfitCheck check, IReadOnlyList<WardrobeCandidate> candidates, int max, DateTime now, CancellationToken ct)
    {
        if (candidates.Count == 0)
        {
            return ([], 0, 0, 0);
        }

        var keys = candidates.Select(c => KeyOf(c.Name)).ToList();
        var known = await db.WardrobeItems
            .Where(i => i.UserId == userId && (keys.Contains(i.NameKey) || (i.StylistKey != null && keys.Contains(i.StylistKey))))
            .ToListAsync(ct);
        // The piece carrying the name wins over one renamed away from it, as in FindAsync.
        var byKey = new Dictionary<string, WardrobeItem>(StringComparer.Ordinal);
        foreach (var row in known.Where(i => i.StylistKey is not null))
        {
            byKey.TryAdd(row.StylistKey!, row);
        }

        foreach (var row in known)
        {
            byKey[row.NameKey] = row;
        }

        var knownIds = known.Select(i => i.Id).ToList();
        var seenOn = (await db.WardrobeAppearances.AsNoTracking()
            .Where(a => a.CheckId == check.Id && knownIds.Contains(a.ItemId))
            .Select(a => a.ItemId)
            .ToListAsync(ct)).ToHashSet();
        var count = await CountAsync(db, userId, ct);

        var items = new List<WardrobeItem>();
        var added = 0;
        var skipped = 0;
        var wrote = 0;
        foreach (var candidate in candidates)
        {
            var key = KeyOf(candidate.Name);
            if (byKey.TryGetValue(key, out var item))
            {
                if (items.Contains(item))
                {
                    continue;
                }

                var touched = false;
                if (check.CreatedAt > item.LastSeenAt)
                {
                    item.LastSeenAt = check.CreatedAt;
                    touched = true;
                }

                if (seenOn.Add(item.Id))
                {
                    db.WardrobeAppearances.Add(new WardrobeAppearance { ItemId = item.Id, CheckId = check.Id, WornAt = check.CreatedAt });
                    touched = true;
                }

                wrote += touched ? 1 : 0;
                items.Add(item);
                continue;
            }

            if (count >= max)
            {
                skipped++;
                continue;
            }

            item = new WardrobeItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = candidate.Name,
                NameKey = key,
                StylistKey = key,
                Category = candidate.Category,
                CreatedAt = now,
                LastSeenAt = check.CreatedAt
            };
            db.WardrobeItems.Add(item);
            db.WardrobeAppearances.Add(new WardrobeAppearance { ItemId = item.Id, CheckId = check.Id, WornAt = check.CreatedAt });
            byKey[key] = item;
            items.Add(item);
            count++;
            added++;
            wrote++;
        }

        // One save for the batch; when the cap kept every new piece out and no known row was touched, it writes nothing.
        await db.SaveChangesAsync(ct);
        return (items, added, skipped, wrote);
    }

    /// <summary>
    /// Round 20 — the pieces the stylist named on the account's last <paramref name="checks"/> scored checks that are
    /// not in its wardrobe: newest look first, each piece once (carrying the newest check that named it, which is the
    /// check the single keep route will validate it against), at most <paramref name="maxPieces"/>. A piece counts as kept
    /// under its current name and under the stylist's name it was kept as, so a rename never brings it back. Three reads:
    /// the checks' two columns, the account's keys, the posts behind those checks. Empty, and no reads at all, when
    /// <paramref name="checks"/> is 0 or less. Returns the pieces and how many checks were looked at.
    /// </summary>
    public static async Task<(List<UnkeptPiece> Pieces, int Checks)> UnkeptAsync(
        AppDbContext db, Guid userId, int checks, int maxPieces, CancellationToken ct)
    {
        if (checks <= 0 || maxPieces <= 0)
        {
            return ([], 0);
        }

        var recent = await db.Checks.AsNoTracking()
            .Where(c => c.UserId == userId && c.Status == CheckStatus.Ok)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .Take(checks)
            .Select(c => new { c.Id, c.CreatedAt, c.Status, c.FeedbackJson })
            .ToListAsync(ct);
        if (recent.Count == 0)
        {
            return ([], 0);
        }

        // A piece is kept under its name and under the stylist's name it was kept as, so a renamed one is still kept.
        var keys = await db.WardrobeItems.AsNoTracking().Where(i => i.UserId == userId)
            .Select(i => new { i.NameKey, i.StylistKey }).ToListAsync(ct);
        var kept = keys.Select(k => k.NameKey).Concat(keys.Where(k => k.StylistKey != null).Select(k => k.StylistKey!))
            .ToHashSet(StringComparer.Ordinal);
        var ids = recent.Select(c => c.Id).ToList();
        // The account's own visible looks only, as the wardrobe list reads them: a hidden post is a private check again.
        var posts = await db.Posts.AsNoTracking()
            .Where(p => p.UserId == userId && !p.Hidden && ids.Contains(p.CheckId))
            .Select(p => new { p.CheckId, p.Id })
            .ToListAsync(ct);
        var postByCheck = posts.ToDictionary(p => p.CheckId, p => p.Id);

        var pieces = new List<UnkeptPiece>();
        foreach (var check in recent)
        {
            foreach (var candidate in NamesOn(check.Status, check.FeedbackJson))
            {
                if (!kept.Add(KeyOf(candidate.Name)))
                {
                    continue;
                }

                pieces.Add(new UnkeptPiece(candidate.Name, candidate.Category, check.Id, DateTime.SpecifyKind(check.CreatedAt, DateTimeKind.Utc),
                    postByCheck.TryGetValue(check.Id, out var postId) ? postId : null));
                if (pieces.Count >= maxPieces)
                {
                    return (pieces, recent.Count);
                }
            }
        }

        return (pieces, recent.Count);
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
    /// Round 19: the switch as a plain read — on when no row was ever made, which is the default. Tomorrow asks this on
    /// every tap, and twelve taps at once must not race to insert the row (<see cref="SettingAsync"/> makes it).
    /// </summary>
    public static async Task<bool> ToStylistAsync(AppDbContext db, Guid userId, CancellationToken ct) =>
        await db.WardrobeSettings.AsNoTracking().Where(s => s.UserId == userId).Select(s => (bool?)s.ToStylist).FirstOrDefaultAsync(ct) ?? true;

    /// <summary>
    /// The names that travel with a check, most recently worn first: at most <paramref name="max"/>, clothes only
    /// (<see cref="PromptCategories"/>), each one cleaned, and never one that names a body, a face, an age or a gender —
    /// a renamed piece is free text the person typed, and rule 1 is not negotiable there either. Duplicates are dropped.
    /// Empty when <paramref name="max"/> is 0 or nothing survives, and an empty list puts NOTHING in the prompt.
    /// </summary>
    public static List<string> PromptNames(IEnumerable<WardrobeItem> items, int max) =>
        PromptItems(items, max).Select(item => CleanName(item.Name)).ToList();

    /// <summary>
    /// Round 19 — the rows behind <see cref="PromptNames"/>: the same order, the same filters and the same cut, but the
    /// pieces themselves rather than their names, because Tomorrow needs each one's id (to hand the model a closed list
    /// and check its answer against it) and its category (to say which kind of piece the closet lacks). The name to
    /// show or send is still <see cref="CleanName"/> of the row's, never the raw one.
    /// </summary>
    public static List<WardrobeItem> PromptItems(IEnumerable<WardrobeItem> items, int max)
    {
        if (max <= 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<WardrobeItem>();
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

            rows.Add(item);
            if (rows.Count >= max)
            {
                break;
            }
        }

        return rows;
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
            .Take(names * PromptReadFactor)
            .ToListAsync(ct);
        return PromptNames(items, names);
    }

    /// <summary>How many rows <see cref="ForStylistAsync"/> reads for each name it may send.</summary>
    public const int PromptReadFactor = 3;

    /// <summary>
    /// Review of Round 20 — how many of these pieces a prompt of <paramref name="names"/> would actually carry: the rows
    /// <see cref="ForStylistAsync"/> reads (most recently worn first) through <see cref="PromptItems"/>, so a piece the
    /// stylist filed as "other" or a renamed one that names a person is not counted. The Pro moment's sentence says
    /// "all of them" only when this is every piece.
    /// </summary>
    public static int SeenBy(IEnumerable<WardrobeItem> items, int names) =>
        names <= 0 ? 0 : PromptItems(items.OrderByDescending(i => i.LastSeenAt).ThenByDescending(i => i.CreatedAt).Take(names * PromptReadFactor), names).Count;
}

/// <summary>A piece a check named, offered for keeping: the cleaned name and the stylist's category.</summary>
public sealed record WardrobeCandidate(string Name, string Category);

/// <summary>Round 20 — a piece named on one of the account's latest looks and never kept, with the newest check that named it.</summary>
public sealed record UnkeptPiece(string Name, string Category, Guid CheckId, DateTime WornAt, Guid? PostId);

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

    /// <summary>
    /// Review of Round 20: the key of the stylist's name this piece was kept under. A rename changes <see cref="Name"/>
    /// and <see cref="NameKey"/> and never this, so a later check naming the same piece adds a look to this row and the
    /// unkept list does not offer it again. Null on a row kept before the column that has not been renamed since (the
    /// first rename records the key it had).
    /// </summary>
    public string? StylistKey { get; set; }

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

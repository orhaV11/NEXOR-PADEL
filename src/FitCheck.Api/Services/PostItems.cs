using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;

namespace FitCheck.Api.Services;

/// <summary>
/// The pieces on a look. Round 9 copied the stylist's item names onto a look when it was posted so Explore can find looks
/// by piece ("black boots"): lower-cased and trimmed so a search term meets them as stored, the category riding along for
/// the insights, the verdicts and notes staying private with the check. Round 10 lets the person tag them (a brand, a
/// model, a store link, a dot on the photo) and add their own: <see cref="Apply"/> is the one validation of that list,
/// at posting time (POST /api/posts with items) and afterwards (PATCH /api/posts/{id}/items). The brand is never copied
/// from <see cref="OutfitItem.BrandSeen"/>: the client shows it as a suggestion and sends it back as a confirmed brand
/// only when the person accepts it.
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
    public static void AddFrom(AppDbContext db, Guid postId, OutfitFeedback feedback) =>
        db.PostItems.AddRange(FromFeedback(postId, feedback));

    /// <summary>The rows <see cref="AddFrom"/> writes, as a list: the post sheet's list is matched against them at posting time.</summary>
    public static List<PostItem> FromFeedback(Guid postId, OutfitFeedback feedback)
    {
        var rows = new List<PostItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in feedback.Items)
        {
            var name = NormalizeName(item.Name);
            if (name.Length == 0 || !seen.Add(name))
            {
                continue;
            }

            rows.Add(new PostItem
            {
                Id = Guid.NewGuid(),
                PostId = postId,
                Name = name,
                Category = NormalizeCategory(item.Category),
                Source = ItemSource.Stylist,
                Position = rows.Count
            });
            if (rows.Count >= MaxPerPost)
            {
                break;
            }
        }

        return rows;
    }

    /// <summary>
    /// Turns the list a person sends into the rows the look keeps, in that order, or names the error key (error.items_too_many,
    /// error.item_invalid, error.item_url_invalid, error.item_position_invalid; the first takes <see cref="MaxTagged"/> as its
    /// argument). The list is the whole list: a row of <paramref name="existing"/> that is not in <paramref name="rows"/>
    /// afterwards is to be removed. An input names an existing row by id, or, when it carries no id, by the name of a
    /// stylist row not yet named (the post sheet at posting time, before ids exist); anything else is a new row typed by
    /// the person. An existing row is edited in place: a stylist row stays the stylist's while only its brand, model,
    /// link, dot or confirmation change, and becomes the person's once its name or category does. A typed name is 1 to
    /// <see cref="TypedNameMaxLength"/> characters after normalisation; a stylist name sent back unchanged may be longer,
    /// and "unchanged" is read by <see cref="IsSameName"/> (the stored name, the stylist's uncut one, or its first forty).
    /// Confirmed is only ever true with a brand on a row that is still the stylist's (a typed row has no suggestion to
    /// accept); a store link is http(s) without user info (<see cref="IsStoreUrl"/>); the dot is both coordinates in 0..1
    /// or neither.
    /// </summary>
    public static string? Apply(Guid postId, IReadOnlyList<PostItemInput?> inputs, IReadOnlyList<PostItem> existing, out List<PostItem> rows)
    {
        rows = [];
        if (inputs.Count > MaxTagged)
        {
            return "error.items_too_many";
        }

        var byId = existing.ToDictionary(r => r.Id);
        var used = new HashSet<Guid>();
        foreach (var input in inputs)
        {
            if (input is null)
            {
                return "error.item_invalid";
            }

            var typedName = TypedName(input.Name);
            PostItem? row = null;
            if (input.Id is Guid id)
            {
                if (!byId.TryGetValue(id, out row) || !used.Add(id))
                {
                    return "error.item_invalid";
                }
            }
            else if (typedName.Length > 0)
            {
                row = existing.FirstOrDefault(r => r.Source == ItemSource.Stylist && !used.Contains(r.Id) && IsSameName(r.Name, typedName));
                if (row is not null)
                {
                    used.Add(row.Id);
                }
            }

            // The name: a new row needs one; an existing row keeps its own unless a different one is typed.
            string name;
            if (row is null || (typedName.Length > 0 && !IsSameName(row.Name, typedName)))
            {
                if (typedName.Length is 0 or > TypedNameMaxLength)
                {
                    return "error.item_invalid";
                }

                name = typedName;
            }
            else
            {
                name = row.Name;
            }

            var categoryInput = (input.Category ?? "").Trim().ToLowerInvariant();
            var category = categoryInput.Length == 0 ? row?.Category ?? "other" : categoryInput;
            if (Array.IndexOf(Categories, category) < 0)
            {
                return "error.item_invalid";
            }

            var brand = Optional(OutfitAnalyzer.SanitizeText(input.Brand, multiline: false));
            var model = Optional(OutfitAnalyzer.SanitizeText(input.Model, multiline: false));
            if (brand is { Length: > BrandMaxLength } || model is { Length: > ModelMaxLength })
            {
                return "error.item_invalid";
            }

            var url = Optional(input.Url?.Trim());
            if (url is not null && !IsStoreUrl(url))
            {
                return "error.item_url_invalid";
            }

            if (!IsDot(input.X, input.Y))
            {
                return "error.item_position_invalid";
            }

            var isNew = row is null;
            row ??= new PostItem { Id = Guid.NewGuid(), PostId = postId, Source = ItemSource.User };
            if (isNew || name != row.Name || category != row.Category)
            {
                row.Source = ItemSource.User;
            }

            row.Name = name;
            row.Category = category;
            row.Brand = brand;
            row.Model = model;
            row.Url = url;
            row.X = input.X is double x ? Math.Round(x, 4) : null;
            row.Y = input.Y is double y ? Math.Round(y, 4) : null;
            // A confirmation is the person accepting what the stylist saw: it needs a brand and a row that is still the stylist's.
            row.Confirmed = input.Confirmed && brand is not null && row.Source == ItemSource.Stylist;
            row.Position = rows.Count;
            rows.Add(row);
        }

        return null;
    }

    /// <summary>
    /// Whether a name sent back names the stored one: the stored name itself; the stylist's own name in full, longer than
    /// the <see cref="NameMaxLength"/> the row was cut to (the check carries it uncut); or the stored name cut to
    /// <see cref="TypedNameMaxLength"/>, which is what a client that held every name to the typed limit sends back. None
    /// of these is a rename, so none makes the stylist's row the person's.
    /// </summary>
    public static bool IsSameName(string stored, string typed) =>
        typed == stored || NormalizeName(typed) == stored || (stored.Length > TypedNameMaxLength && typed == stored[..TypedNameMaxLength].TrimEnd());

    /// <summary>Both coordinates in 0..1, or neither.</summary>
    public static bool IsDot(double? x, double? y) =>
        (x is null && y is null)
        || (x is double dx && y is double dy && double.IsFinite(dx) && double.IsFinite(dy) && dx is >= 0 and <= 1 && dy is >= 0 and <= 1);

    /// <summary>
    /// Whether a store link may be stored and followed: absolute, http or https, a host, no user info (a "user@" in front of
    /// the host is a way to make one site's link look like another's), nothing else (a data: or javascript: link behind
    /// "Shop at" would be an attack on the person who taps it). The URL is stored as given; this only says yes or no.
    /// </summary>
    public static bool IsStoreUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.Length <= UrlMaxLength
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo);

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

    /// <summary>
    /// The link a store link leaves through: the URL as stored (in its ASCII form, <see cref="AsciiUrl"/>, when it has
    /// characters outside ASCII) with the affiliate parameters appended when the host earns some, after its own query and
    /// before its fragment. Null parameters give the link back untouched.
    /// </summary>
    public static string OutUrl(string url, string? parameters)
    {
        url = AsciiUrl(url);
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return url;
        }

        var hash = url.IndexOf('#');
        var fragment = hash < 0 ? "" : url[hash..];
        var head = hash < 0 ? url : url[..hash];
        var separator = !head.Contains('?') ? "?" : head.EndsWith('?') || head.EndsWith('&') ? "" : "&";
        return head + separator + parameters + fragment;
    }

    /// <summary>
    /// A store link as a Location header can carry it. A link is accepted and stored as the person pasted it, which may be
    /// an IRI (a Hebrew query, an accented path, a host in its own script: browsers and chat apps show links decoded), but
    /// a response header holds printable ASCII and nothing else, so the door sends the same link in its ASCII form: the
    /// host as punycode, the path, query and fragment percent-encoded, the scheme and any port as they were. A link that
    /// is ASCII already goes as stored, host case included.
    /// </summary>
    public static string AsciiUrl(string url)
    {
        if (url.All(c => c is >= ' ' and <= '~'))
        {
            return url;
        }

        var uri = new Uri(url, UriKind.Absolute);
        var host = uri.HostNameType == UriHostNameType.IPv6 ? "[" + uri.IdnHost + "]" : uri.IdnHost;
        var port = uri.IsDefaultPort ? "" : ":" + uri.Port;
        return uri.Scheme + "://" + host + port + uri.GetComponents(UriComponents.PathAndQuery | UriComponents.Fragment, UriFormat.UriEscaped);
    }

    /// <summary>"  Black  Boots " → "black boots": one space between words, lower-case, at most 60 characters.</summary>
    public static string NormalizeName(string? name)
    {
        var joined = TypedName(name);
        return joined.Length > NameMaxLength ? joined[..NameMaxLength].TrimEnd() : joined;
    }

    public static string NormalizeCategory(string? category)
    {
        var lower = (category ?? "").Trim().ToLowerInvariant();
        return Array.IndexOf(Categories, lower) >= 0 ? lower : "other";
    }

    /// <summary>A name as typed, normalised like the stylist's (control characters out, one space between words, lower case) but not cut: the caller decides on the length.</summary>
    private static string TypedName(string? name)
    {
        var words = OutfitAnalyzer.SanitizeText(name, multiline: false).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', words).ToLowerInvariant();
    }

    private static string? Optional(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

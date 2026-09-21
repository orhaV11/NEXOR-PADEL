namespace FitCheck.Api.Domain;

/// <summary>
/// Round 14 — the category error, corrected. One list used to hold two different questions: Casual, Date, Office, Party
/// and Sport are OCCASIONS (where the outfit is going) while Streetwear, OldMoney and Minimal are STYLES (how the wearer
/// wants to read), and the person had to pick one of the eight (nine: Formal joined the list when the occasion it names
/// did, so a wedding look has a word of its own to be shown by). Someone who wanted streetwear for a date, or minimal for
/// a party, had no way to say so.
/// <para>
/// A check now carries both: <see cref="OutfitOccasion"/> on every check, and <see cref="OutfitStyle"/> as a preference
/// that may be unset. This enum stays as the ONE-WORD value: it is what a look, a board, a challenge, the feed filter and
/// the interests list carry, and what every surface with room for a single word shows. <see cref="StyleIntents"/> maps
/// between the two shapes; a check stores the pair and the one word.
/// </para>
/// </summary>
public enum StyleIntent
{
    Casual,
    Date,
    Streetwear,
    OldMoney,
    Minimal,
    Office,
    Party,
    Sport,

    /// <summary>
    /// A wedding, a ceremony, a big evening. Round 14 added the OCCASION and had no word for it here, so a wedding look
    /// went out as Party on every card, board, share card and share video. Appended, never inserted: the value is stored
    /// by NAME (Checks.Intent is TEXT), and the order of this list is the order the chips read in.
    /// </summary>
    Formal
}

/// <summary>
/// Where the outfit is going. Asked on every check: the score is always relative to it, and when the occasion and the
/// style disagree the occasion wins ("a fine streetwear look and a weak one for a wedding").
/// </summary>
public enum OutfitOccasion
{
    /// <summary>The street, errands, a coffee, a class. What Casual used to mean.</summary>
    Everyday,
    Date,
    Office,
    Party,

    /// <summary>A wedding, a ceremony, a big evening: the one occasion with a real dress code, and the gap the old list had.</summary>
    Formal,
    Sport
}

/// <summary>
/// How the wearer wants the look to read. A preference, set once and changeable per check, and UNSET is a first-class
/// answer: with no style the stylist judges the look on its own terms for the occasion, which is what Date, Office, Party
/// and Sport always did.
/// </summary>
public enum OutfitStyle
{
    Streetwear,
    OldMoney,
    Minimal,

    /// <summary>Familiar shapes done properly: a shirt that fits, a straight trouser, a clean shoe. Nothing trend-led.</summary>
    Classic
}

/// <summary>
/// The two shapes and the road between them. The split is lossless in one direction (every old <see cref="StyleIntent"/>
/// is exactly one pair) and lossy in the other on purpose: a pair the old list could not say (date + streetwear) comes
/// back as its OCCASION, because that is the word every one-word surface should show.
/// </summary>
public static class StyleIntents
{
    /// <summary>The occasions in the order the chips show them.</summary>
    public static readonly OutfitOccasion[] Occasions = Enum.GetValues<OutfitOccasion>();

    /// <summary>The styles in the order the chips show them. "No style" is the absence of one, never a member here.</summary>
    public static readonly OutfitStyle[] Styles = Enum.GetValues<OutfitStyle>();

    /// <summary>
    /// The pair behind one word. Casual, Date, Office, Party and Sport were occasions with no style; Streetwear, OldMoney
    /// and Minimal were styles worn everyday. For the eight old values this is exactly what the Round14Check migration
    /// writes onto every stored check — Formal is younger than that migration and no row written before it can hold it.
    /// </summary>
    public static (OutfitOccasion Occasion, OutfitStyle? Style) Split(StyleIntent intent) => intent switch
    {
        StyleIntent.Casual => (OutfitOccasion.Everyday, null),
        StyleIntent.Date => (OutfitOccasion.Date, null),
        StyleIntent.Office => (OutfitOccasion.Office, null),
        StyleIntent.Party => (OutfitOccasion.Party, null),
        StyleIntent.Formal => (OutfitOccasion.Formal, null),
        StyleIntent.Sport => (OutfitOccasion.Sport, null),
        StyleIntent.Streetwear => (OutfitOccasion.Everyday, OutfitStyle.Streetwear),
        StyleIntent.OldMoney => (OutfitOccasion.Everyday, OutfitStyle.OldMoney),
        StyleIntent.Minimal => (OutfitOccasion.Everyday, OutfitStyle.Minimal),
        _ => (OutfitOccasion.Everyday, null)
    };

    /// <summary>
    /// The one word for a pair, for every surface that has room for one: a look's tag, a board, the share card, the share
    /// video, the feed filter. A style worn everyday keeps its own name (that is what the old list meant); anywhere else
    /// the OCCASION wins, so a streetwear look for a date reads "Date", never "Streetwear".
    /// Formal has a word of its own now: a wedding look says Formal, not Party.
    /// </summary>
    public static StyleIntent Legacy(OutfitOccasion occasion, OutfitStyle? style)
    {
        if (occasion == OutfitOccasion.Everyday && style is { } worn)
        {
            return worn switch
            {
                OutfitStyle.Streetwear => StyleIntent.Streetwear,
                OutfitStyle.OldMoney => StyleIntent.OldMoney,
                OutfitStyle.Minimal => StyleIntent.Minimal,
                _ => StyleIntent.Casual
            };
        }

        return occasion switch
        {
            OutfitOccasion.Date => StyleIntent.Date,
            OutfitOccasion.Office => StyleIntent.Office,
            OutfitOccasion.Party => StyleIntent.Party,
            OutfitOccasion.Formal => StyleIntent.Formal,
            OutfitOccasion.Sport => StyleIntent.Sport,
            _ => StyleIntent.Casual
        };
    }

    /// <summary>An occasion by name, case-insensitively ("date", "Everyday"). False for anything else, including empty.</summary>
    public static bool TryParseOccasion(string? name, out OutfitOccasion occasion) =>
        Enum.TryParse(name, ignoreCase: true, out occasion) && Enum.IsDefined(occasion);

    /// <summary>
    /// A style by name, case-insensitively, with "old money" and "old-money" accepted as OldMoney (a person types the
    /// words, a chip sends the enum). Empty, "none" and "unset" are not a failure: they are the unset answer, and the
    /// caller tells them apart by <paramref name="style"/> being null.
    /// </summary>
    public static bool TryParseStyle(string? name, out OutfitStyle? style)
    {
        style = null;
        var text = (name ?? "").Trim();
        if (text.Length == 0
            || text.Equals("none", StringComparison.OrdinalIgnoreCase)
            || text.Equals("unset", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var compact = text.Replace(" ", "").Replace("-", "").Replace("_", "");
        if (Enum.TryParse<OutfitStyle>(compact, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            style = parsed;
            return true;
        }

        return false;
    }
}

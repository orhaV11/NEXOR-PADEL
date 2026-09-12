using FitCheck.Api.Domain;

namespace FitCheck.Api.Services;

/// <summary>
/// One "Today's look" prompt: the hashtag it runs under (lower-case, unique, the shape PostTag stores), a title and a
/// hint in both languages, and the intent it leans to (null when any intent fits). The hashtag is the whole
/// mechanism: a look posted with it today is in, the same way a challenge's hashtag enters a look.
/// </summary>
public sealed record DailyPrompt(string Tag, string TitleEn, string TitleHe, string HintEn, string HintHe, StyleIntent? Intent = null)
{
    public string Title(string language) => IsHebrew(language) ? TitleHe : TitleEn;

    public string Hint(string language) => IsHebrew(language) ? HintHe : HintEn;

    private static bool IsHebrew(string language) => string.Equals(language, "he", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The daily rhythm without ephemeral content: thirty prompts, one a day by the UTC day of the year modulo thirty, so
/// everyone on the app sees the same prompt on the same day and a prompt comes back about once a month. Nothing is
/// stored: the posts carry the hashtag, the day decides the prompt.
/// </summary>
public static class DailyPrompts
{
    public static IReadOnlyList<DailyPrompt> All { get; } =
    [
        new("monochrome", "One color, head to toe", "צבע אחד, מכף רגל ועד ראש",
            "Pick a color and commit. Shades of it count, prints don't.", "בוחרים צבע והולכים עד הסוף. גוונים שלו נחשבים, הדפסים לא.", StyleIntent.Minimal),
        new("denimondenim", "Denim on denim", "ג'ינס על ג'ינס",
            "Two washes, one look. The contrast is the point.", "שתי שטיפות, לוק אחד. הניגוד הוא כל העניין.", StyleIntent.Casual),
        new("officebutyou", "Office, but make it you", "משרד, אבל בגרסה שלך",
            "Work-ready, with one piece that's unmistakably yours.", "מוכן לעבודה, עם פריט אחד שאי אפשר לטעות בו: שלך.", StyleIntent.Office),
        new("shoesdecide", "The shoes decide", "הנעליים מחליטות",
            "Start from the shoes and build the whole look up from there.", "מתחילים מהנעליים ובונים מהן את כל הלוק למעלה."),
        new("blackdate", "Date night in black", "דייט בשחור",
            "All black, and nothing safe about it.", "כולו שחור, ושום דבר בו לא בטוח.", StyleIntent.Date),
        new("vintagenew", "Vintage piece, new look", "פריט וינטג', לוק חדש",
            "One thing older than you, styled like it came out this season.", "פריט אחד שמבוגר ממך, מסוגנן כאילו יצא העונה."),
        new("whiteout", "White out", "לבן על לבן",
            "White on white, from crisp to cream.", "לבן על לבן, מבוהק ועד שמנת.", StyleIntent.Minimal),
        new("layers", "Layers on layers", "שכבות על שכבות",
            "Three layers you can actually see. Play with weight, texture and length.", "שלוש שכבות שבאמת רואים. משחקים במשקל, בטקסטורה ובאורך.", StyleIntent.Streetwear),
        new("oneaccessory", "One accessory does the work", "אקססורי אחד עושה את העבודה",
            "Keep the clothes quiet and let one piece do the talking.", "משאירים את הבגדים שקטים ונותנים לפריט אחד לדבר."),
        new("tailored", "Tailored, no tie", "חליפתי, בלי עניבה",
            "A blazer or trousers with the edges taken off.", "בלייזר או מכנס מחויט, עם הקצוות מרוככים.", StyleIntent.OldMoney),
        new("sneakersup", "Sneakers, dressed up", "סניקרס, בגרסה מגונדרת",
            "Sneakers with something they're not supposed to go with.", "סניקרס עם משהו שהם לא אמורים ללכת איתו.", StyleIntent.Streetwear),
        new("sundaylook", "Sunday, but on purpose", "יום ראשון, אבל בכוונה",
            "Comfortable, and still a look. No pajamas.", "נוח, ועדיין לוק. בלי פיג'מות.", StyleIntent.Casual),
        new("touchofred", "A touch of red", "נגיעה של אדום",
            "One red piece, and everything else steps back.", "פריט אדום אחד, וכל השאר לוקח צעד אחורה.", StyleIntent.Date),
        new("earthtones", "Earth tones", "גווני אדמה",
            "Sand, olive, rust, brown. Nothing brighter than a leaf.", "חול, זית, חלודה, חום. שום דבר בהיר יותר מעלה.", StyleIntent.OldMoney),
        new("oneprint", "One print", "הדפס אחד",
            "A single print carrying the whole look. Nothing else patterned.", "הדפס אחד שנושא את כל הלוק. שום דבר אחר לא מודפס.", StyleIntent.Party),
        new("thrifted", "Thrifted top to bottom", "יד שנייה מלמעלה עד למטה",
            "Everything second-hand. Prove nobody can tell.", "הכול יד שנייה. תוכיחו שאי אפשר לדעת."),
        new("oversized", "Oversized, done right", "אוברסייז, כמו שצריך",
            "Big on top or big on the bottom. Never both.", "גדול למעלה או גדול למטה. אף פעם לא שניהם.", StyleIntent.Streetwear),
        new("gymtostreet", "Gym to street", "מהחדר כושר לרחוב",
            "Sportswear that walks into a café without changing.", "בגדי ספורט שנכנסים לבית קפה בלי להחליף.", StyleIntent.Sport),
        new("oneleather", "Leather, one piece", "עור, פריט אחד",
            "A leather jacket, trousers or boots. Just one of them.", "ז'קט עור, מכנס עור או מגפיים. רק אחד מהם.", StyleIntent.Party),
        new("knitsonly", "Knits only", "רק סריגים",
            "A sweater, a cardigan, a knit dress. Texture over everything.", "סוודר, קרדיגן, שמלת סריג. טקסטורה מעל הכול.", StyleIntent.Casual),
        new("beltit", "Belt it", "חגורה",
            "A belt that changes the silhouette, not just holds things up.", "חגורה שמשנה את הסילואט, לא רק מחזיקה.", StyleIntent.Office),
        new("silverorgold", "Silver or gold", "כסף או זהב",
            "Pick one metal and let every piece agree.", "בוחרים מתכת אחת ונותנים לכל הפריטים להסכים.", StyleIntent.Date),
        new("stripes", "Stripes somewhere", "פסים איפשהו",
            "Horizontal, vertical, or both if you dare.", "אופקיים, אנכיים, או שניהם אם יש אומץ.", StyleIntent.Casual),
        new("hatday", "Hat day", "יום כובע",
            "A hat that finishes the look, not one that hides hair.", "כובע שסוגר את הלוק, לא כזה שמסתיר שיער.", StyleIntent.Streetwear),
        new("noblack", "Neutrals, no black", "ניוטרלים, בלי שחור",
            "Beige, grey, cream, camel. Black stays in the closet.", "בז', אפור, שמנת, קאמל. השחור נשאר בארון.", StyleIntent.Minimal),
        new("desktodance", "Desk to dance floor", "מהמשרד לרחבה",
            "One swap turns your workday look into tonight's.", "החלפה אחת הופכת את הלוק של יום העבודה ללוק של הלילה.", StyleIntent.Party),
        new("pastel", "Pastel, not sweet", "פסטל, לא מתוק",
            "A soft color with a hard edge somewhere.", "צבע רך עם קצה קשה איפשהו.", StyleIntent.Date),
        new("thecoat", "The coat is the look", "המעיל הוא הלוק",
            "The outerwear leads. Whatever is under it just supports.", "השכבה החיצונית מובילה. מה שמתחתיה רק תומך.", StyleIntent.OldMoney),
        new("dresseddown", "Formal piece, dressed down", "פריט רשמי, בגרסה משוחררת",
            "A suit trouser, a silk shirt or a heel, next to something casual.", "מכנס חליפה, חולצת משי או עקב, ליד משהו יומיומי.", StyleIntent.Office),
        new("favoritepiece", "Your favorite piece", "הפריט האהוב עליך",
            "The one thing you'd save from a fire. Build around it.", "הדבר האחד שהיית מציל משריפה. בונים סביבו.")
    ];

    /// <summary>The prompt for a UTC moment: the day of the year modulo the list, so a date always lands on the same one.</summary>
    public static DailyPrompt For(DateTime utc) => All[utc.DayOfYear % All.Count];

    /// <summary>Midnight UTC of the day a moment falls on: the start of "today" for the posts that count.</summary>
    public static DateTime DayOf(DateTime utc) => DateTime.SpecifyKind(utc.Date, DateTimeKind.Utc);
}

using FitCheck.Api.Domain;

namespace FitCheck.Api.Services;

/// <summary>
/// One "Today's look" prompt: the hashtag it runs under (lower-case, unique, the shape PostTag stores), a title and a
/// hint in every shipped language (English, Hebrew, Arabic, Russian), and the intent it leans to (null when any intent
/// fits). The hashtag is the whole mechanism: a look posted with it today is in, the same way a challenge's hashtag
/// enters a look.
/// </summary>
public sealed record DailyPrompt(
    string Tag,
    string TitleEn, string TitleHe, string TitleAr, string TitleRu,
    string HintEn, string HintHe, string HintAr, string HintRu,
    StyleIntent? Intent = null)
{
    public string Title(string language) => Pick(language, TitleEn, TitleHe, TitleAr, TitleRu);

    public string Hint(string language) => Pick(language, HintEn, HintHe, HintAr, HintRu);

    /// <summary>The line for a locale; anything outside the shipped set reads English, as the Localizer falls back.</summary>
    private static string Pick(string language, string en, string he, string ar, string ru) => (language ?? "").ToLowerInvariant() switch
    {
        "he" => he,
        "ar" => ar,
        "ru" => ru,
        _ => en,
    };
}

/// <summary>
/// The daily rhythm without ephemeral content: thirty prompts, one a day by the UTC day of the year modulo thirty, so
/// everyone on the app sees the same prompt on the same day and a prompt comes back about once a month. Nothing is
/// stored: the posts carry the hashtag, the day decides the prompt. The copy keeps each language's register from the
/// i18n files: Hebrew and Arabic in neutral, ungendered forms, Russian in the informal "ты".
/// </summary>
public static class DailyPrompts
{
    public static IReadOnlyList<DailyPrompt> All { get; } =
    [
        new("monochrome",
            "One color, head to toe", "צבע אחד, מכף רגל ועד ראש", "لون واحد من الرأس إلى القدم", "Один цвет с головы до ног",
            "Pick a color and commit. Shades of it count, prints don't.", "בוחרים צבע והולכים עד הסוף. גוונים שלו נחשבים, הדפסים לא.",
            "اختيار لون والالتزام به. درجاته تُحسب، والنقوش لا.", "Выбери цвет и держись его. Оттенки считаются, принты нет.", StyleIntent.Minimal),
        new("denimondenim",
            "Denim on denim", "ג'ינס על ג'ינס", "دنيم على دنيم", "Деним на дениме",
            "Two washes, one look. The contrast is the point.", "שתי שטיפות, לוק אחד. הניגוד הוא כל העניין.",
            "غسلتان، إطلالة واحدة. التباين هو الفكرة.", "Две стирки, один образ. Контраст и есть смысл.", StyleIntent.Casual),
        new("officebutyou",
            "Office, but make it you", "משרד, אבל בגרסה שלך", "مكتب، لكن على طريقتك", "Офис, но по-твоему",
            "Work-ready, with one piece that's unmistakably yours.", "מוכן לעבודה, עם פריט אחד שאי אפשר לטעות בו: שלך.",
            "جاهزة للعمل، وفيها قطعة واحدة لا يمكن أن تكون إلا لك.", "Готово к работе, с одной вещью, которая безошибочно твоя.", StyleIntent.Office),
        new("shoesdecide",
            "The shoes decide", "הנעליים מחליטות", "الحذاء يقرر", "Обувь решает",
            "Start from the shoes and build the whole look up from there.", "מתחילים מהנעליים ובונים מהן את כל הלוק למעלה.",
            "البداية من الحذاء، ومنه تُبنى الإطلالة كلها إلى الأعلى.", "Начни с обуви и собери весь образ от неё вверх."),
        new("blackdate",
            "Date night in black", "דייט בשחור", "موعد بالأسود", "Свидание в чёрном",
            "All black, and nothing safe about it.", "כולו שחור, ושום דבר בו לא בטוח.",
            "أسود بالكامل، ولا شيء فيه آمن.", "Всё чёрное, и ничего безопасного.", StyleIntent.Date),
        new("vintagenew",
            "Vintage piece, new look", "פריט וינטג', לוק חדש", "قطعة فينتاج، إطلالة جديدة", "Винтажная вещь, новый образ",
            "One thing older than you, styled like it came out this season.", "פריט אחד שמבוגר ממך, מסוגנן כאילו יצא העונה.",
            "قطعة واحدة أكبر منك سنًا، منسّقة كأنها من هذا الموسم.", "Одна вещь старше тебя, собранная так, будто она из этого сезона."),
        new("whiteout",
            "White out", "לבן על לבן", "أبيض على أبيض", "Белым по белому",
            "White on white, from crisp to cream.", "לבן על לבן, מבוהק ועד שמנת.",
            "أبيض على أبيض، من الناصع إلى الكريمي.", "Белое на белом, от снежного до сливочного.", StyleIntent.Minimal),
        new("layers",
            "Layers on layers", "שכבות על שכבות", "طبقات فوق طبقات", "Слои на слоях",
            "Three layers you can actually see. Play with weight, texture and length.", "שלוש שכבות שבאמת רואים. משחקים במשקל, בטקסטורה ובאורך.",
            "ثلاث طبقات تُرى فعلًا. اللعب بالوزن والملمس والطول.", "Три слоя, которые действительно видно. Играй с весом, фактурой и длиной.", StyleIntent.Streetwear),
        new("oneaccessory",
            "One accessory does the work", "אקססורי אחד עושה את העבודה", "إكسسوار واحد يقوم بالعمل", "Один аксессуар делает всё",
            "Keep the clothes quiet and let one piece do the talking.", "משאירים את הבגדים שקטים ונותנים לפריט אחד לדבר.",
            "الملابس هادئة، وقطعة واحدة تتكلم.", "Одежда молчит, а говорит одна вещь."),
        new("tailored",
            "Tailored, no tie", "חליפתי, בלי עניבה", "مفصّل، من دون ربطة عنق", "Костюмное, без галстука",
            "A blazer or trousers with the edges taken off.", "בלייזר או מכנס מחויט, עם הקצוות מרוככים.",
            "بليزر أو بنطال مفصّل، مع تليين الحواف.", "Пиджак или брюки со сглаженными углами.", StyleIntent.OldMoney),
        new("sneakersup",
            "Sneakers, dressed up", "סניקרס, בגרסה מגונדרת", "سنيكرز، بإطلالة أنيقة", "Кроссовки, но нарядно",
            "Sneakers with something they're not supposed to go with.", "סניקרס עם משהו שהם לא אמורים ללכת איתו.",
            "سنيكرز مع شيء لا يُفترض أن يناسبها.", "Кроссовки с тем, с чем им вроде бы нельзя.", StyleIntent.Streetwear),
        new("sundaylook",
            "Sunday, but on purpose", "יום ראשון, אבל בכוונה", "يوم عطلة، لكن عن قصد", "Воскресенье, но осознанно",
            "Comfortable, and still a look. No pajamas.", "נוח, ועדיין לוק. בלי פיג'מות.",
            "مريح، ومع ذلك إطلالة. لا بيجامات.", "Удобно, и всё же образ. Никаких пижам.", StyleIntent.Casual),
        new("touchofred",
            "A touch of red", "נגיעה של אדום", "لمسة من الأحمر", "Капля красного",
            "One red piece, and everything else steps back.", "פריט אדום אחד, וכל השאר לוקח צעד אחורה.",
            "قطعة حمراء واحدة، وكل ما عداها يتراجع خطوة.", "Одна красная вещь, и всё остальное отступает.", StyleIntent.Date),
        new("earthtones",
            "Earth tones", "גווני אדמה", "ألوان الأرض", "Земляные тона",
            "Sand, olive, rust, brown. Nothing brighter than a leaf.", "חול, זית, חלודה, חום. שום דבר בהיר יותר מעלה.",
            "رمل، زيتوني، صدأ، بني. لا شيء أكثر إشراقًا من ورقة شجر.", "Песок, олива, ржавчина, коричневый. Ничего ярче листа.", StyleIntent.OldMoney),
        new("oneprint",
            "One print", "הדפס אחד", "نقشة واحدة", "Один принт",
            "A single print carrying the whole look. Nothing else patterned.", "הדפס אחד שנושא את כל הלוק. שום דבר אחר לא מודפס.",
            "نقشة واحدة تحمل الإطلالة كلها. لا شيء آخر منقوش.", "Один принт держит весь образ. Больше ничего с рисунком.", StyleIntent.Party),
        new("thrifted",
            "Thrifted top to bottom", "יד שנייה מלמעלה עד למטה", "مستعمل من الرأس إلى القدم", "Секонд-хенд с головы до ног",
            "Everything second-hand. Prove nobody can tell.", "הכול יד שנייה. תוכיחו שאי אפשר לדעת.",
            "كل شيء من الأزياء المستعملة. التحدي أن لا يلاحظ أحد.", "Всё из секонда. Докажи, что никто не заметит."),
        new("oversized",
            "Oversized, done right", "אוברסייז, כמו שצריך", "أوفرسايز، كما يجب", "Оверсайз, как надо",
            "Big on top or big on the bottom. Never both.", "גדול למעלה או גדול למטה. אף פעם לא שניהם.",
            "كبير في الأعلى أو كبير في الأسفل. ليس الاثنين أبدًا.", "Большое сверху или большое снизу. Никогда и то и другое.", StyleIntent.Streetwear),
        new("gymtostreet",
            "Gym to street", "מהחדר כושר לרחוב", "من النادي إلى الشارع", "Из зала на улицу",
            "Sportswear that walks into a café without changing.", "בגדי ספורט שנכנסים לבית קפה בלי להחליף.",
            "ملابس رياضية تدخل المقهى من دون تبديل.", "Спортивное, в котором заходишь в кафе, не переодеваясь.", StyleIntent.Sport),
        new("oneleather",
            "Leather, one piece", "עור, פריט אחד", "جلد، قطعة واحدة", "Кожа, одна вещь",
            "A leather jacket, trousers or boots. Just one of them.", "ז'קט עור, מכנס עור או מגפיים. רק אחד מהם.",
            "جاكيت جلد أو بنطال أو بوت. واحد منها فقط.", "Кожаная куртка, брюки или ботинки. Только что-то одно.", StyleIntent.Party),
        new("knitsonly",
            "Knits only", "רק סריגים", "تريكو فقط", "Только трикотаж",
            "A sweater, a cardigan, a knit dress. Texture over everything.", "סוודר, קרדיגן, שמלת סריג. טקסטורה מעל הכול.",
            "كنزة، كارديغان، فستان تريكو. الملمس فوق كل شيء.", "Свитер, кардиган, вязаное платье. Фактура важнее всего.", StyleIntent.Casual),
        new("beltit",
            "Belt it", "חגורה", "الحزام يصنع الفرق", "Подчеркни ремнём",
            "A belt that changes the silhouette, not just holds things up.", "חגורה שמשנה את הסילואט, לא רק מחזיקה.",
            "حزام يغيّر الشكل العام، لا يمسك الأشياء فقط.", "Ремень, который меняет силуэт, а не просто держит.", StyleIntent.Office),
        new("silverorgold",
            "Silver or gold", "כסף או זהב", "فضي أو ذهبي", "Серебро или золото",
            "Pick one metal and let every piece agree.", "בוחרים מתכת אחת ונותנים לכל הפריטים להסכים.",
            "معدن واحد، وكل القطع تتفق عليه.", "Выбери один металл, и пусть каждая вещь с ним согласна.", StyleIntent.Date),
        new("stripes",
            "Stripes somewhere", "פסים איפשהו", "خطوط في مكان ما", "Где-то полоска",
            "Horizontal, vertical, or both if you dare.", "אופקיים, אנכיים, או שניהם אם יש אומץ.",
            "أفقية، عمودية، أو الاثنان معًا لمن يجرؤ.", "Горизонтальная, вертикальная или обе, если хватит смелости.", StyleIntent.Casual),
        new("hatday",
            "Hat day", "יום כובע", "يوم القبعة", "День шляпы",
            "A hat that finishes the look, not one that hides hair.", "כובע שסוגר את הלוק, לא כזה שמסתיר שיער.",
            "قبعة تكمل الإطلالة، لا قبعة تخفي الشعر.", "Головной убор, который завершает образ, а не прячет волосы.", StyleIntent.Streetwear),
        new("noblack",
            "Neutrals, no black", "ניוטרלים, בלי שחור", "ألوان محايدة، من دون أسود", "Нейтральное, без чёрного",
            "Beige, grey, cream, camel. Black stays in the closet.", "בז', אפור, שמנת, קאמל. השחור נשאר בארון.",
            "بيج، رمادي، كريمي، جملي. الأسود يبقى في الخزانة.", "Бежевый, серый, сливочный, кэмел. Чёрный остаётся в шкафу.", StyleIntent.Minimal),
        new("desktodance",
            "Desk to dance floor", "מהמשרד לרחבה", "من المكتب إلى حلبة الرقص", "Из офиса на танцпол",
            "One swap turns your workday look into tonight's.", "החלפה אחת הופכת את הלוק של יום העבודה ללוק של הלילה.",
            "تبديل واحد يحوّل إطلالة يوم العمل إلى إطلالة الليلة.", "Одна замена превращает рабочий образ в вечерний.", StyleIntent.Party),
        new("pastel",
            "Pastel, not sweet", "פסטל, לא מתוק", "باستيل، من دون حلاوة زائدة", "Пастель, но не сладко",
            "A soft color with a hard edge somewhere.", "צבע רך עם קצה קשה איפשהו.",
            "لون ناعم مع حافة حادة في مكان ما.", "Мягкий цвет с жёстким акцентом где-то.", StyleIntent.Date),
        new("thecoat",
            "The coat is the look", "המעיל הוא הלוק", "المعطف هو الإطلالة", "Пальто и есть образ",
            "The outerwear leads. Whatever is under it just supports.", "השכבה החיצונית מובילה. מה שמתחתיה רק תומך.",
            "الطبقة الخارجية تقود. ما تحتها يساند فقط.", "Верхняя одежда ведёт. Всё под ней только поддерживает.", StyleIntent.OldMoney),
        new("dresseddown",
            "Formal piece, dressed down", "פריט רשמי, בגרסה משוחררת", "قطعة رسمية، بإطلالة مريحة", "Строгая вещь, но расслабленно",
            "A suit trouser, a silk shirt or a heel, next to something casual.", "מכנס חליפה, חולצת משי או עקב, ליד משהו יומיומי.",
            "بنطال بدلة، قميص حرير أو كعب، بجانب شيء يومي.", "Костюмные брюки, шёлковая рубашка или каблук рядом с чем-то простым.", StyleIntent.Office),
        new("favoritepiece",
            "Your favorite piece", "הפריט האהוב עליך", "قطعتك المفضلة", "Твоя любимая вещь",
            "The one thing you'd save from a fire. Build around it.", "הדבר האחד שהיית מציל משריפה. בונים סביבו.",
            "القطعة التي تُنقَذ من الحريق قبل أي شيء. البناء حولها.", "То единственное, что спасёшь из огня. Собери образ вокруг.")
    ];

    /// <summary>The prompt for a UTC moment: the day of the year modulo the list, so a date always lands on the same one.</summary>
    public static DailyPrompt For(DateTime utc) => All[utc.DayOfYear % All.Count];

    /// <summary>Midnight UTC of the day a moment falls on: the start of "today" for the posts that count.</summary>
    public static DateTime DayOf(DateTime utc) => DateTime.SpecifyKind(utc.Date, DateTimeKind.Utc);
}

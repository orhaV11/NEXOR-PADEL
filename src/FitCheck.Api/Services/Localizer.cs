using System.Globalization;

namespace FitCheck.Api.Services;

/// <summary>
/// Server-side user-facing strings (error messages, push lines, mail, insights) in every shipped locale (en, he, ar, ru),
/// plus the locale matching rule shared by Accept-Language and explicit language fields: language-subtag match, otherwise English.
/// </summary>
public sealed class Localizer
{
    public const string DefaultLocale = "en";
    public static readonly string[] SupportedLocales = ["en", "he", "ar", "ru"];

    private static readonly Dictionary<string, string> LanguageNames = new()
    {
        ["en"] = "English",
        ["he"] = "Hebrew",
        ["ar"] = "Arabic",
        ["ru"] = "Russian"
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Messages = new()
    {
        ["en"] = new()
        {
            ["error.handle_invalid"] = "Pick a handle between 2 and 40 characters.",
            ["error.age_required"] = "You need to confirm you are 16 or older to use OREVOSH.",
            ["error.language_invalid"] = "Pick a supported language.",
            ["error.user_not_found"] = "We couldn't find this account.",
            ["error.check_not_found"] = "We couldn't find this check.",
            ["error.intent_invalid"] = "Pick where the outfit is going.",
            ["error.occasion_too_long"] = "Keep the occasion note under 120 characters.",
            ["error.image_required"] = "Add a photo of the outfit.",
            ["error.image_too_large"] = "That photo is too large. Try one under {0} MB.",
            ["error.image_format"] = "Use a JPEG, PNG or WebP photo.",
            ["error.rate_limited"] = "You've reached today's limit of {0} checks. Come back tomorrow.",
            ["error.rate_limited_global"] = "OREVOSH is at capacity for today. Please try again tomorrow.",
            ["error.signup_limited"] = "Too many new accounts from this network. Try again in an hour.",
            ["error.model_failed"] = "The stylist couldn't look at this one. Please try again in a moment.",
            ["error.invalid_request"] = "That request didn't look right.",
            ["error.server"] = "Something went wrong on our side. Please try again.",
            ["feedback.rejected"] = "We can't check this photo.",
            ["error.sign_in_required"] = "Sign in to continue.",
            ["error.forbidden"] = "That isn't yours to change.",
            ["error.handle_taken"] = "That handle is taken.",
            ["error.handle_format"] = "Use 2 to 40 letters, numbers, dots or underscores.",
            ["error.password_short"] = "Use at least 8 characters for the password.",
            ["error.login_failed"] = "Wrong handle or password.",
            ["error.login_limited"] = "Too many attempts. Try again in 15 minutes.",
            ["error.profile_invalid"] = "Display name up to 40 characters, bio up to 160.",
            ["error.website_invalid"] = "Use an https link for the website.",
            ["error.check_not_postable"] = "Only checks the stylist scored can be posted.",
            ["error.already_posted"] = "This check is already posted.",
            ["error.post_not_found"] = "We couldn't find this post.",
            ["error.caption_too_long"] = "Keep the caption under 140 characters.",
            ["error.products_brand_only"] = "Only brand accounts can add product links.",
            ["error.product_invalid"] = "Product links need a label and an https link, up to 3.",
            ["error.challenge_not_found"] = "We couldn't find this challenge.",
            ["error.challenge_closed"] = "This challenge has ended.",
            ["error.challenge_intent"] = "This challenge is for {0} looks. Check one with that intent.",
            ["error.already_entered"] = "You already have an entry in this challenge.",
            ["error.brand_only"] = "Only brand accounts can open challenges.",
            ["error.challenge_invalid"] = "A challenge needs a title, a brief, a prize, and an end between 1 hour and 60 days from now.",
            ["error.vote_own"] = "You can't vote for your own entry.",
            ["error.brand_own_challenge"] = "Your own challenge is for other people to enter and vote on.",
            ["error.avatar_invalid"] = "That doesn't look like a JPEG, PNG or WebP.",
            ["error.avatar_too_large"] = "The photo is over 2 MB. Try a smaller one.",
            ["error.interests_invalid"] = "Pick styles from the list, up to 8.",
            ["error.account_type_invalid"] = "An account is either a person or a brand.",
            ["error.feature_not_allowed"] = "You can feature a look when it mentions your brand or entered one of your challenges.",
            ["error.already_featured"] = "Another brand featured this look first.",
            ["error.not_featured_by_you"] = "Only the brand that featured this look can undo it.",
            ["error.search_invalid"] = "Type 1 to 40 characters to search.",
            ["error.not_an_entry"] = "That post isn't in this challenge.",
            ["error.follow_self"] = "You can't follow yourself.",
            ["error.report_own"] = "You can't report your own post.",
            ["error.comment_invalid"] = "Comments are 1 to 200 characters.",
                        ["error.video_too_large"] = "That clip is too large. Try one under {0} MB or shorter than {1} seconds.",
            ["error.video_format"] = "That doesn't look like an MP4 or WebM clip.",
            ["error.suspended"] = "This account is suspended. Write to us if you think that's a mistake.",
            ["error.admin_only"] = "Only OREVOSH moderators can do that.",
            ["error.admin_protected"] = "Moderators are removed with the --unadmin command, not suspended.",
            ["error.admin_delete"] = "Moderators can't delete their account while they moderate. Run --unadmin first.",
            ["error.push_invalid"] = "That push subscription is missing its endpoint or keys.",
            ["error.push_disabled"] = "Push notifications are not set up on this server.",
            ["push.fire"] = "{0} set your look on fire",
            ["push.follow"] = "{0} started following you",
            ["push.comment"] = "{0} commented on your look",
            ["push.mention"] = "{0} mentioned you in a look",
            ["push.featured"] = "{0} featured your look",
            ["push.won"] = "Your look won {0}'s challenge",
            ["push.vote"] = "{0} voted for your look",
            ["push.entry"] = "{0} entered your challenge",
            ["push.ended"] = "Your challenge ended",
                        ["error.email_invalid"] = "That doesn't look like an email address.",
            ["error.email_taken"] = "That email is already on another account.",
            ["error.email_disabled"] = "Email isn't set up on this server, so there is nothing to send.",
            ["error.token_invalid"] = "That link is not valid any more. Ask for a new one.",
            ["error.recovery_limited"] = "Too many recovery requests. Try again in an hour.",
            ["error.email_send_failed"] = "We couldn't send the email. Try again in a minute.",
            ["email.verify_subject"] = "Confirm your email for OREVOSH",
            ["email.verify_body"] = "Tap this link to confirm your email for @{0}:\n\n{1}\n\nIf this wasn't you, ignore this message.",
            ["email.reset_subject"] = "Reset your OREVOSH password",
            ["email.reset_body"] = "Someone asked to reset the password for @{0}. Tap this link within an hour to pick a new one:\n\n{1}\n\nIf this wasn't you, ignore this message and your password stays as it is.",
                        ["error.plan_limit"] = "That's today's {0} free checks. Go Pro for {1} a day, or come back tomorrow.",
            ["error.guest_limit"] = "That was your free look. Sign up to keep checking, it takes ten seconds.",
            ["error.pro_required"] = "This one is for Pro.",
            ["error.billing_disabled"] = "Payments aren't set up on this server yet.",
            ["error.birthdate_required"] = "Add your date of birth.",
            ["error.birthdate_invalid"] = "That date doesn't look right.",
            ["error.underage"] = "OREVOSH is for people 16 and over.",
            ["error.compare_two_photos"] = "Add both photos.",
            ["error.before_invalid"] = "The earlier look has to be one of yours.",
            ["error.comparison_not_found"] = "We couldn't find this comparison.",
            ["error.billing_failed"] = "Checkout didn't open. Try again in a moment.",
            ["error.billing_signature"] = "That event isn't signed by Stripe.",
            ["insights.line_best"] = "Your {0} looks score highest, {1} on average.",
            ["insights.line_weak"] = "{0} are the weak link in {1}% of your looks.",
            ["insights.line_accessories"] = "{0}% of your looks had no accessories. One piece finishes a look.",
            ["insights.line_streak"] = "{0} days in a row. Keep it going.",
            ["insights.cat_shoes"] = "Shoes",
            ["insights.cat_top"] = "Tops",
            ["insights.cat_bottom"] = "Bottoms",
            ["insights.cat_outerwear"] = "Outer layers",
            ["insights.cat_accessory"] = "Accessories",
            ["insights.cat_dress"] = "Dresses",
            ["insights.cat_other"] = "Other pieces",
            ["insights.intent_Casual"] = "casual",
            ["insights.intent_Date"] = "date",
            ["insights.intent_Streetwear"] = "streetwear",
            ["insights.intent_OldMoney"] = "old money",
            ["insights.intent_Minimal"] = "minimal",
            ["insights.intent_Office"] = "office",
            ["insights.intent_Party"] = "party",
            ["insights.intent_Sport"] = "sport",
            ["error.comment_not_found"] = "We couldn't find this comment.",
            ["error.too_fast"] = "Slow down a little. Try again in a bit.",
        },
        ["he"] = new()
        {
            ["error.handle_invalid"] = "הכינוי צריך להיות באורך 2 עד 40 תווים.",
            ["error.age_required"] = "כדי להשתמש ב-OREVOSH צריך לאשר גיל 16 ומעלה.",
            ["error.language_invalid"] = "צריך לבחור שפה נתמכת.",
            ["error.user_not_found"] = "לא מצאנו את החשבון הזה.",
            ["error.check_not_found"] = "לא מצאנו את הבדיקה הזו.",
            ["error.intent_invalid"] = "צריך לבחור לאן הלוק הולך.",
            ["error.occasion_too_long"] = "הערת האירוע צריכה להיות עד 120 תווים.",
            ["error.image_required"] = "צריך להוסיף תמונה של הלוק.",
            ["error.image_too_large"] = "התמונה גדולה מדי. אפשר לנסות תמונה עד {0}MB.",
            ["error.image_format"] = "אפשר להעלות תמונה בפורמט JPEG, PNG או WebP.",
            ["error.rate_limited"] = "המכסה היומית של {0} בדיקות נוצלה. שווה לחזור מחר.",
            ["error.rate_limited_global"] = "OREVOSH הגיע לקיבולת היומית. שווה לנסות שוב מחר.",
            ["error.signup_limited"] = "יותר מדי חשבונות חדשים מהרשת הזו. שווה לנסות שוב בעוד שעה.",
            ["error.model_failed"] = "הסטייליסט לא הצליח להסתכל על התמונה הזו. שווה לנסות שוב בעוד רגע.",
            ["error.invalid_request"] = "הבקשה לא תקינה.",
            ["error.server"] = "משהו השתבש אצלנו. שווה לנסות שוב.",
            ["feedback.rejected"] = "אי אפשר לבדוק את התמונה הזו.",
            ["error.sign_in_required"] = "צריך להתחבר כדי להמשיך.",
            ["error.forbidden"] = "זה לא שלך לשנות.",
            ["error.handle_taken"] = "הכינוי הזה תפוס.",
            ["error.handle_format"] = "2 עד 40 אותיות, ספרות, נקודות או קו תחתון, בלי רווחים.",
            ["error.password_short"] = "הסיסמה צריכה להיות באורך 8 תווים לפחות.",
            ["error.login_failed"] = "הכינוי או הסיסמה לא נכונים.",
            ["error.login_limited"] = "יותר מדי ניסיונות. שווה לנסות שוב בעוד 15 דקות.",
            ["error.profile_invalid"] = "שם תצוגה עד 40 תווים, ביו עד 160.",
            ["error.website_invalid"] = "כתובת האתר צריכה להתחיל ב-https.",
            ["error.check_not_postable"] = "אפשר להעלות רק בדיקות שהסטייליסט דירג.",
            ["error.already_posted"] = "הבדיקה הזו כבר הועלתה.",
            ["error.post_not_found"] = "לא מצאנו את הפוסט הזה.",
            ["error.caption_too_long"] = "הכיתוב צריך להיות עד 140 תווים.",
            ["error.products_brand_only"] = "רק חשבונות מותג יכולים להוסיף קישורי מוצרים.",
            ["error.product_invalid"] = "קישור מוצר צריך שם וכתובת https, עד 3 קישורים.",
            ["error.challenge_not_found"] = "לא מצאנו את האתגר הזה.",
            ["error.challenge_closed"] = "האתגר הזה הסתיים.",
            ["error.challenge_intent"] = "האתגר הזה הוא ללוקים של {0}. שווה לבדוק לוק עם הכוונה הזו.",
            ["error.already_entered"] = "כבר יש לך כניסה לאתגר הזה.",
            ["error.brand_only"] = "רק חשבונות מותג יכולים לפתוח אתגרים.",
            ["error.challenge_invalid"] = "אתגר צריך כותרת, תיאור, פרס ותאריך סיום בין שעה ל-60 יום מהיום.",
            ["error.vote_own"] = "אי אפשר להצביע לכניסה של עצמך.",
            ["error.brand_own_challenge"] = "האתגר שלך הוא בשביל שאחרים ייכנסו אליו ויצביעו.",
            ["error.avatar_invalid"] = "זה לא נראה כמו JPEG, PNG או WebP.",
            ["error.avatar_too_large"] = "התמונה גדולה מ-2MB. שווה לנסות קטנה יותר.",
            ["error.interests_invalid"] = "בוחרים סגנונות מהרשימה, עד 8.",
            ["error.account_type_invalid"] = "חשבון הוא או של אדם או של מותג.",
            ["error.feature_not_allowed"] = "אפשר להציג לוק כשהוא מתייג את המותג שלך או נכנס לאחד האתגרים שלך.",
            ["error.already_featured"] = "מותג אחר הציג את הלוק הזה קודם.",
            ["error.not_featured_by_you"] = "רק המותג שהציג את הלוק יכול לבטל.",
            ["error.search_invalid"] = "כותבים בין תו אחד ל-40 תווים כדי לחפש.",
            ["error.not_an_entry"] = "הפוסט הזה לא נמצא באתגר.",
            ["error.follow_self"] = "אי אפשר לעקוב אחרי עצמך.",
            ["error.report_own"] = "אי אפשר לדווח על פוסט של עצמך.",
            ["error.comment_invalid"] = "תגובה היא בין תו אחד ל-200 תווים.",
                        ["error.video_too_large"] = "הסרטון גדול מדי. אפשר לנסות סרטון עד {0}MB או קצר מ-{1} שניות.",
            ["error.video_format"] = "זה לא נראה כמו סרטון MP4 או WebM.",
            ["error.suspended"] = "החשבון הזה מושעה. אם נראה לך שזו טעות, כתבו לנו.",
            ["error.admin_only"] = "רק צוות OREVOSH יכול לעשות את זה.",
            ["error.admin_protected"] = "חשבון של צוות OREVOSH לא משעים. מסירים אותו מהצוות עם הפקודה --unadmin.",
            ["error.admin_delete"] = "אי אפשר למחוק חשבון של צוות OREVOSH כל עוד הוא בצוות. קודם מריצים --unadmin.",
            ["error.push_invalid"] = "למינוי ההתראות חסרים הכתובת או המפתחות.",
            ["error.push_disabled"] = "התראות דחיפה לא מוגדרות בשרת הזה.",
            // The same neutral phrasing as the activity list (wwwroot/i18n/he.json, "activity.*"): the thing that happened,
            // then who did it, so nothing has to agree with a gender.
            ["push.fire"] = "אש מ-{0} על הלוק שלך",
            ["push.follow"] = "עוקב חדש: {0}",
            ["push.comment"] = "תגובה מ-{0} על הלוק שלך",
            ["push.mention"] = "תיוג מ-{0} בלוק",
            ["push.featured"] = "הלוק שלך מוצג אצל {0}",
            ["push.won"] = "זכית באתגר של {0}",
            ["push.vote"] = "הצבעה מ-{0} לכניסה שלך",
            ["push.entry"] = "כניסה חדשה לאתגר שלך: {0}",
            ["push.ended"] = "האתגר שלך הסתיים",
                        ["error.email_invalid"] = "זה לא נראה כמו כתובת מייל.",
            ["error.email_taken"] = "המייל הזה כבר מחובר לחשבון אחר.",
            ["error.email_disabled"] = "מייל לא מוגדר בשרת הזה, אז אין מה לשלוח.",
            ["error.token_invalid"] = "הקישור הזה כבר לא תקף. אפשר לבקש חדש.",
            ["error.recovery_limited"] = "יותר מדי בקשות שחזור. אפשר לנסות שוב בעוד שעה.",
            ["error.email_send_failed"] = "לא הצלחנו לשלוח את המייל. שווה לנסות שוב בעוד דקה.",
            ["email.verify_subject"] = "אישור המייל שלך ב-OREVOSH",
            ["email.verify_body"] = "לוחצים על הקישור כדי לאשר את המייל של @{0}:\n\n{1}\n\nאם זה לא מכם, אפשר להתעלם מההודעה.",
            ["email.reset_subject"] = "איפוס הסיסמה ב-OREVOSH",
            ["email.reset_body"] = "מישהו ביקש לאפס את הסיסמה של @{0}. לוחצים על הקישור תוך שעה כדי לבחור סיסמה חדשה:\n\n{1}\n\nאם זה לא מכם, מתעלמים והסיסמה נשארת כמו שהיא.",
                        ["error.plan_limit"] = "אלה {0} הבדיקות החינמיות של היום. עוברים לפרו ל-{1} ביום, או חוזרים מחר.",
            ["error.guest_limit"] = "זה היה הלוק החינמי. נרשמים כדי להמשיך לבדוק, זה לוקח עשר שניות.",
            ["error.pro_required"] = "זה לפרו.",
            ["error.billing_disabled"] = "תשלומים עדיין לא מוגדרים בשרת הזה.",
            ["error.birthdate_required"] = "צריך להוסיף תאריך לידה.",
            ["error.birthdate_invalid"] = "התאריך לא נראה נכון.",
            ["error.underage"] = "OREVOSH היא לגילאי 16 ומעלה.",
            ["error.compare_two_photos"] = "צריך להוסיף את שתי התמונות.",
            ["error.before_invalid"] = "הלוק הקודם צריך להיות שלך.",
            ["error.comparison_not_found"] = "לא מצאנו את ההשוואה הזו.",
            ["error.billing_failed"] = "התשלום לא נפתח. אפשר לנסות שוב עוד רגע.",
            ["error.billing_signature"] = "האירוע הזה לא חתום על ידי Stripe.",
            ["insights.line_best"] = "הלוקים שלך בסגנון {0} מקבלים את הציון הכי גבוה: {1} בממוצע.",
            ["insights.line_weak"] = "החוליה החלשה ב-{1}% מהלוקים שלך: {0}.",
            ["insights.line_accessories"] = "ב-{0}% מהלוקים שלך לא היה שום אקססורי. פריט אחד סוגר לוק.",
            ["insights.line_streak"] = "{0} ימים ברצף. ממשיכים ככה.",
            ["insights.cat_shoes"] = "הנעליים",
            ["insights.cat_top"] = "החלק העליון",
            ["insights.cat_bottom"] = "החלק התחתון",
            ["insights.cat_outerwear"] = "השכבה העליונה",
            ["insights.cat_accessory"] = "האקססוריז",
            ["insights.cat_dress"] = "השמלה",
            ["insights.cat_other"] = "פריטים אחרים",
            ["insights.intent_Casual"] = "קז'ואל",
            ["insights.intent_Date"] = "דייט",
            ["insights.intent_Streetwear"] = "סטריטוור",
            ["insights.intent_OldMoney"] = "אולד מאני",
            ["insights.intent_Minimal"] = "מינימלי",
            ["insights.intent_Office"] = "משרד",
            ["insights.intent_Party"] = "מסיבה",
            ["insights.intent_Sport"] = "ספורט",
            ["error.comment_not_found"] = "לא מצאנו את התגובה הזו.",
            ["error.too_fast"] = "קצת יותר לאט. אפשר לנסות שוב עוד רגע.",
        },
        // Arabic: Modern Standard Arabic in a light register. Second-person verbs are avoided (they carry gender), so the
        // lines lean on verbal nouns and "يمكن"; suffixes and past-tense forms read the same for everyone unvocalised.
        ["ar"] = new()
        {
            ["error.handle_invalid"] = "اسم المستخدم بين 2 و40 حرفًا.",
            ["error.age_required"] = "استخدام OREVOSH يتطلب تأكيد أن العمر 16 فما فوق.",
            ["error.language_invalid"] = "يجب اختيار لغة مدعومة.",
            ["error.user_not_found"] = "لم نجد هذا الحساب.",
            ["error.check_not_found"] = "لم نجد هذا التقييم.",
            ["error.intent_invalid"] = "يجب اختيار إلى أين تذهب الإطلالة.",
            ["error.occasion_too_long"] = "ملاحظة المناسبة حتى 120 حرفًا.",
            ["error.image_required"] = "يجب إضافة صورة للإطلالة.",
            ["error.image_too_large"] = "الصورة كبيرة جدًا. يمكن تجربة صورة أقل من {0} ميغابايت.",
            ["error.image_format"] = "الصورة يجب أن تكون JPEG أو PNG أو WebP.",
            ["error.rate_limited"] = "وصلت إلى حد اليوم وهو {0} تقييم. العودة غدًا.",
            ["error.rate_limited_global"] = "OREVOSH ممتلئ لهذا اليوم. يمكن المحاولة غدًا.",
            ["error.signup_limited"] = "حسابات جديدة كثيرة من هذه الشبكة. يمكن المحاولة بعد ساعة.",
            ["error.model_failed"] = "لم يستطع الستايلست النظر إلى هذه. يمكن المحاولة بعد لحظة.",
            ["error.invalid_request"] = "هذا الطلب لا يبدو صحيحًا.",
            ["error.server"] = "حدث خطأ من جهتنا. يمكن المحاولة مجددًا.",
            ["feedback.rejected"] = "لا يمكننا تقييم هذه الصورة.",
            ["error.sign_in_required"] = "تسجيل الدخول للمتابعة.",
            ["error.forbidden"] = "هذا ليس لك لتغييره.",
            ["error.handle_taken"] = "اسم المستخدم هذا محجوز.",
            ["error.handle_format"] = "من 2 إلى 40 حرفًا أو رقمًا أو نقطة أو شرطة سفلية.",
            ["error.password_short"] = "كلمة المرور 8 أحرف على الأقل.",
            ["error.login_failed"] = "اسم المستخدم أو كلمة المرور غير صحيحة.",
            ["error.login_limited"] = "محاولات كثيرة. يمكن المحاولة بعد 15 دقيقة.",
            ["error.profile_invalid"] = "الاسم المعروض حتى 40 حرفًا، والنبذة حتى 160.",
            ["error.website_invalid"] = "رابط الموقع يجب أن يبدأ بـ https.",
            ["error.check_not_postable"] = "لا يُنشر إلا تقييم منحه الستايلست نتيجة.",
            ["error.already_posted"] = "هذا التقييم منشور بالفعل.",
            ["error.post_not_found"] = "لم نجد هذا المنشور.",
            ["error.caption_too_long"] = "الوصف حتى 140 حرفًا.",
            ["error.products_brand_only"] = "حسابات الماركات وحدها تضيف روابط منتجات.",
            ["error.product_invalid"] = "رابط المنتج يحتاج إلى اسم ورابط https، حتى 3 روابط.",
            ["error.challenge_not_found"] = "لم نجد هذا التحدّي.",
            ["error.challenge_closed"] = "هذا التحدّي انتهى.",
            ["error.challenge_intent"] = "هذا التحدّي لإطلالات {0}. يمكن تقييم إطلالة بهذه الوجهة.",
            ["error.already_entered"] = "لديك مشاركة في هذا التحدّي بالفعل.",
            ["error.brand_only"] = "حسابات الماركات وحدها تفتح التحدّيات.",
            ["error.challenge_invalid"] = "التحدّي يحتاج إلى عنوان وموجز وجائزة وموعد انتهاء بين ساعة و60 يومًا من الآن.",
            ["error.vote_own"] = "لا يمكن التصويت لمشاركتك أنت.",
            ["error.brand_own_challenge"] = "تحدّيك أنت للآخرين كي يشاركوا فيه ويصوّتوا.",
            ["error.avatar_invalid"] = "هذا لا يبدو JPEG أو PNG أو WebP.",
            ["error.avatar_too_large"] = "الصورة أكبر من 2 ميغابايت. الأفضل تجربة صورة أصغر.",
            ["error.interests_invalid"] = "الأساليب تُختار من القائمة، حتى 8.",
            ["error.account_type_invalid"] = "الحساب إما لشخص أو لماركة.",
            ["error.feature_not_allowed"] = "يمكن إبراز إطلالة عندما تذكر ماركتك أو تشارك في أحد تحدّياتك.",
            ["error.already_featured"] = "ماركة أخرى أبرزت هذه الإطلالة أولًا.",
            ["error.not_featured_by_you"] = "الماركة التي أبرزت هذه الإطلالة وحدها تتراجع عن ذلك.",
            ["error.search_invalid"] = "للبحث، من حرف واحد إلى 40 حرفًا.",
            ["error.not_an_entry"] = "هذا المنشور ليس في هذا التحدّي.",
            ["error.follow_self"] = "لا يمكن متابعة نفسك.",
            ["error.report_own"] = "لا يمكن الإبلاغ عن منشورك أنت.",
            ["error.comment_invalid"] = "التعليق بين حرف واحد و200 حرف.",
            ["error.video_too_large"] = "المقطع كبير جدًا. يمكن تجربة مقطع أقل من {0} ميغابايت أو أقصر من {1} ثانية.",
            ["error.video_format"] = "هذا لا يبدو مقطع MP4 أو WebM.",
            ["error.suspended"] = "هذا الحساب معلّق. إن بدا لك أن هذا خطأ، فرسالة لنا تكفي.",
            ["error.admin_only"] = "مشرفو OREVOSH وحدهم يفعلون ذلك.",
            ["error.admin_protected"] = "المشرفون يُزالون بالأمر --unadmin، لا بالتعليق.",
            ["error.admin_delete"] = "لا يمكن للمشرفين حذف حسابهم أثناء الإشراف. الأمر --unadmin أولًا.",
            ["error.push_invalid"] = "اشتراك الإشعارات هذا ينقصه العنوان أو المفاتيح.",
            ["error.push_disabled"] = "إشعارات الدفع غير مهيّأة على هذا الخادم.",
            // The thing that happened, then who did it, as the Hebrew lines do: nothing has to agree with a gender.
            ["push.fire"] = "نار من {0} على إطلالتك",
            ["push.follow"] = "متابعة جديدة: {0}",
            ["push.comment"] = "تعليق من {0} على إطلالتك",
            ["push.mention"] = "ذكر لك من {0} في إطلالة",
            ["push.featured"] = "إطلالتك من مختارات {0}",
            ["push.won"] = "فزت في تحدّي {0}",
            ["push.vote"] = "تصويت من {0} لمشاركتك",
            ["push.entry"] = "مشاركة جديدة في تحدّيك: {0}",
            ["push.ended"] = "انتهى تحدّيك",
            ["error.email_invalid"] = "هذا لا يبدو عنوان بريد إلكتروني.",
            ["error.email_taken"] = "هذا البريد مرتبط بحساب آخر بالفعل.",
            ["error.email_disabled"] = "البريد غير مهيّأ على هذا الخادم، فلا شيء يُرسل.",
            ["error.token_invalid"] = "هذا الرابط لم يعد صالحًا. يمكن طلب واحد جديد.",
            ["error.recovery_limited"] = "طلبات استعادة كثيرة. يمكن المحاولة بعد ساعة.",
            ["error.email_send_failed"] = "تعذّر إرسال البريد. يمكن المحاولة بعد دقيقة.",
            ["email.verify_subject"] = "تأكيد بريدك في OREVOSH",
            ["email.verify_body"] = "نقرة على هذا الرابط لتأكيد بريد @{0}:\n\n{1}\n\nإن لم يكن هذا منك، يمكن تجاهل هذه الرسالة.",
            ["email.reset_subject"] = "إعادة تعيين كلمة مرور OREVOSH",
            ["email.reset_body"] = "طلب أحدهم إعادة تعيين كلمة المرور لحساب @{0}. نقرة على هذا الرابط خلال ساعة لاختيار كلمة جديدة:\n\n{1}\n\nإن لم يكن هذا منك، يمكن تجاهل الرسالة وتبقى كلمة المرور كما هي.",
            ["error.plan_limit"] = "هذه تقييمات اليوم المجانية: {0}. Pro يمنح {1} في اليوم، أو العودة غدًا.",
            ["error.guest_limit"] = "كانت هذه إطلالتك المجانية. التسجيل لمواصلة التقييم يستغرق عشر ثوانٍ.",
            ["error.pro_required"] = "هذه لمشتركي Pro.",
            ["error.billing_disabled"] = "المدفوعات غير مهيّأة على هذا الخادم بعد.",
            ["error.birthdate_required"] = "يجب إضافة تاريخ الميلاد.",
            ["error.birthdate_invalid"] = "هذا التاريخ لا يبدو صحيحًا.",
            ["error.underage"] = "OREVOSH لمن هم في 16 فما فوق.",
            ["error.compare_two_photos"] = "يجب إضافة الصورتين.",
            ["error.before_invalid"] = "الإطلالة السابقة يجب أن تكون من إطلالاتك.",
            ["error.comparison_not_found"] = "لم نجد هذه المقارنة.",
            ["error.billing_failed"] = "لم تُفتح صفحة الدفع. يمكن المحاولة بعد لحظة.",
            ["error.billing_signature"] = "هذا الحدث ليس موقّعًا من Stripe.",
            ["insights.line_best"] = "إطلالاتك بأسلوب {0} تحصل على أعلى النتائج: {1} في المتوسط.",
            ["insights.line_weak"] = "الحلقة الأضعف في {1}% من إطلالاتك: {0}.",
            ["insights.line_accessories"] = "{0}% من إطلالاتك من دون أي إكسسوار. قطعة واحدة تكمل الإطلالة.",
            ["insights.line_streak"] = "{0} أيام متتالية. الاستمرار هو السر.",
            ["insights.cat_shoes"] = "الأحذية",
            ["insights.cat_top"] = "القطع العلوية",
            ["insights.cat_bottom"] = "القطع السفلية",
            ["insights.cat_outerwear"] = "الطبقات الخارجية",
            ["insights.cat_accessory"] = "الإكسسوارات",
            ["insights.cat_dress"] = "الفساتين",
            ["insights.cat_other"] = "قطع أخرى",
            ["insights.intent_Casual"] = "كاجوال",
            ["insights.intent_Date"] = "موعد",
            ["insights.intent_Streetwear"] = "ستريت وير",
            ["insights.intent_OldMoney"] = "أولد موني",
            ["insights.intent_Minimal"] = "مينيمال",
            ["insights.intent_Office"] = "مكتب",
            ["insights.intent_Party"] = "حفلة",
            ["insights.intent_Sport"] = "رياضي",
            ["error.comment_not_found"] = "لم نجد هذا التعليق.",
            ["error.too_fast"] = "على مهل قليلًا. يمكن المحاولة بعد لحظة.",
        },
        // Russian: the informal "ты" throughout, as the client copy. Past-tense second-person forms carry gender in Russian,
        // so the lines use imperatives, the present tense and "label: number" for counts.
        ["ru"] = new()
        {
            ["error.handle_invalid"] = "Ник должен быть от 2 до 40 символов.",
            ["error.age_required"] = "Чтобы пользоваться OREVOSH, нужно подтвердить, что тебе есть 16.",
            ["error.language_invalid"] = "Выбери поддерживаемый язык.",
            ["error.user_not_found"] = "Мы не нашли этот аккаунт.",
            ["error.check_not_found"] = "Мы не нашли эту проверку.",
            ["error.intent_invalid"] = "Выбери, куда собирается образ.",
            ["error.occasion_too_long"] = "Заметка о поводе — до 120 символов.",
            ["error.image_required"] = "Добавь фото образа.",
            ["error.image_too_large"] = "Это фото слишком большое. Попробуй до {0} МБ.",
            ["error.image_format"] = "Нужно фото в JPEG, PNG или WebP.",
            ["error.rate_limited"] = "Дневной лимит проверок исчерпан: {0}. Возвращайся завтра.",
            ["error.rate_limited_global"] = "OREVOSH на сегодня загружен. Попробуй завтра.",
            ["error.signup_limited"] = "Слишком много новых аккаунтов из этой сети. Попробуй через час.",
            ["error.model_failed"] = "Стилист не смог посмотреть на это. Попробуй через минуту.",
            ["error.invalid_request"] = "Запрос выглядит странно.",
            ["error.server"] = "Что-то пошло не так у нас. Попробуй ещё раз.",
            ["feedback.rejected"] = "Это фото мы проверить не можем.",
            ["error.sign_in_required"] = "Войди, чтобы продолжить.",
            ["error.forbidden"] = "Это не твоё, менять нельзя.",
            ["error.handle_taken"] = "Этот ник занят.",
            ["error.handle_format"] = "От 2 до 40 букв, цифр, точек или подчёркиваний.",
            ["error.password_short"] = "Пароль — не меньше 8 символов.",
            ["error.login_failed"] = "Неверный ник или пароль.",
            ["error.login_limited"] = "Слишком много попыток. Попробуй через 15 минут.",
            ["error.profile_invalid"] = "Имя до 40 символов, описание до 160.",
            ["error.website_invalid"] = "Для сайта нужна https-ссылка.",
            ["error.check_not_postable"] = "Опубликовать можно только проверку с оценкой стилиста.",
            ["error.already_posted"] = "Эта проверка уже опубликована.",
            ["error.post_not_found"] = "Мы не нашли эту публикацию.",
            ["error.caption_too_long"] = "Подпись — до 140 символов.",
            ["error.products_brand_only"] = "Ссылки на товары добавляют только аккаунты брендов.",
            ["error.product_invalid"] = "Ссылке на товар нужны название и https-адрес, не больше 3.",
            ["error.challenge_not_found"] = "Мы не нашли этот челлендж.",
            ["error.challenge_closed"] = "Этот челлендж завершён.",
            ["error.challenge_intent"] = "Этот челлендж для образов в направлении {0}. Проверь образ с этим направлением.",
            ["error.already_entered"] = "У тебя уже есть работа в этом челлендже.",
            ["error.brand_only"] = "Челленджи открывают только аккаунты брендов.",
            ["error.challenge_invalid"] = "Челленджу нужны название, задание, приз и окончание от 1 часа до 60 дней от сейчас.",
            ["error.vote_own"] = "За свою работу голосовать нельзя.",
            ["error.brand_own_challenge"] = "Твой челлендж для других: они участвуют и голосуют.",
            ["error.avatar_invalid"] = "Это не похоже на JPEG, PNG или WebP.",
            ["error.avatar_too_large"] = "Фото больше 2 МБ. Попробуй поменьше.",
            ["error.interests_invalid"] = "Выбери стили из списка, не больше 8.",
            ["error.account_type_invalid"] = "Аккаунт бывает либо человека, либо бренда.",
            ["error.feature_not_allowed"] = "Выделить образ можно, когда в нём упомянут твой бренд или он участвует в твоём челлендже.",
            ["error.already_featured"] = "Этот образ первым выделил другой бренд.",
            ["error.not_featured_by_you"] = "Отменить может только бренд, который выделил этот образ.",
            ["error.search_invalid"] = "Для поиска введи от 1 до 40 символов.",
            ["error.not_an_entry"] = "Этой публикации нет в этом челлендже.",
            ["error.follow_self"] = "На себя подписаться нельзя.",
            ["error.report_own"] = "На свою публикацию пожаловаться нельзя.",
            ["error.comment_invalid"] = "Комментарий — от 1 до 200 символов.",
            ["error.video_too_large"] = "Этот клип слишком большой. Попробуй до {0} МБ или короче {1} секунд.",
            ["error.video_format"] = "Это не похоже на клип MP4 или WebM.",
            ["error.suspended"] = "Этот аккаунт заблокирован. Если это ошибка, напиши нам.",
            ["error.admin_only"] = "Это могут только модераторы OREVOSH.",
            ["error.admin_protected"] = "Модераторов снимают командой --unadmin, а не блокировкой.",
            ["error.admin_delete"] = "Модератор не может удалить аккаунт, пока модерирует. Сначала --unadmin.",
            ["error.push_invalid"] = "У этой push-подписки нет адреса или ключей.",
            ["error.push_disabled"] = "Push-уведомления на этом сервере не настроены.",
            ["push.fire"] = "Огонь от {0} твоему образу",
            ["push.follow"] = "Новый подписчик: {0}",
            ["push.comment"] = "Комментарий от {0} к твоему образу",
            ["push.mention"] = "Упоминание от {0} в образе",
            ["push.featured"] = "Твой образ выделен брендом {0}",
            ["push.won"] = "Твоя победа в челлендже {0}",
            ["push.vote"] = "Голос от {0} за твою работу",
            ["push.entry"] = "Новая работа в твоём челлендже: {0}",
            ["push.ended"] = "Твой челлендж завершён",
            ["error.email_invalid"] = "Это не похоже на адрес почты.",
            ["error.email_taken"] = "Эта почта уже привязана к другому аккаунту.",
            ["error.email_disabled"] = "Почта на этом сервере не настроена, так что отправлять нечего.",
            ["error.token_invalid"] = "Эта ссылка больше не действует. Запроси новую.",
            ["error.recovery_limited"] = "Слишком много запросов на восстановление. Попробуй через час.",
            ["error.email_send_failed"] = "Не удалось отправить письмо. Попробуй через минуту.",
            ["email.verify_subject"] = "Подтверди почту для OREVOSH",
            ["email.verify_body"] = "Нажми на ссылку, чтобы подтвердить почту для @{0}:\n\n{1}\n\nЕсли это не ты, просто не обращай внимания на это письмо.",
            ["email.reset_subject"] = "Сброс пароля OREVOSH",
            ["email.reset_body"] = "Кто-то попросил сбросить пароль для @{0}. Нажми на ссылку в течение часа, чтобы придумать новый:\n\n{1}\n\nЕсли это не ты, не обращай внимания, и пароль останется прежним.",
            ["error.plan_limit"] = "Это были бесплатные проверки на сегодня: {0}. С Pro их {1} в день, или возвращайся завтра.",
            ["error.guest_limit"] = "Это была бесплатная проверка. Зарегистрируйся, чтобы продолжить, это десять секунд.",
            ["error.pro_required"] = "Это только с Pro.",
            ["error.billing_disabled"] = "Платежи на этом сервере пока не настроены.",
            ["error.birthdate_required"] = "Добавь дату рождения.",
            ["error.birthdate_invalid"] = "Эта дата выглядит неверно.",
            ["error.underage"] = "OREVOSH для людей от 16 лет.",
            ["error.compare_two_photos"] = "Добавь оба фото.",
            ["error.before_invalid"] = "Прошлый образ должен быть твоим.",
            ["error.comparison_not_found"] = "Мы не нашли это сравнение.",
            ["error.billing_failed"] = "Оплата не открылась. Попробуй через минуту.",
            ["error.billing_signature"] = "Это событие не подписано Stripe.",
            ["insights.line_best"] = "Твои образы в направлении {0} получают самые высокие оценки: {1} в среднем.",
            ["insights.line_weak"] = "Слабое звено в {1}% твоих образов: {0}.",
            ["insights.line_accessories"] = "{0}% твоих образов без аксессуаров. Одна вещь завершает образ.",
            ["insights.line_streak"] = "Дней подряд: {0}. Так держать.",
            ["insights.cat_shoes"] = "обувь",
            ["insights.cat_top"] = "верх",
            ["insights.cat_bottom"] = "низ",
            ["insights.cat_outerwear"] = "верхняя одежда",
            ["insights.cat_accessory"] = "аксессуары",
            ["insights.cat_dress"] = "платья",
            ["insights.cat_other"] = "другие вещи",
            ["insights.intent_Casual"] = "кэжуал",
            ["insights.intent_Date"] = "свидание",
            ["insights.intent_Streetwear"] = "стритвир",
            ["insights.intent_OldMoney"] = "олд мани",
            ["insights.intent_Minimal"] = "минимал",
            ["insights.intent_Office"] = "офис",
            ["insights.intent_Party"] = "вечеринка",
            ["insights.intent_Sport"] = "спорт",
            ["error.comment_not_found"] = "Мы не нашли этот комментарий.",
            ["error.too_fast"] = "Чуть помедленнее. Попробуй через минуту.",
        }
    };

    public static bool IsSupported(string? locale) =>
        locale is not null && Array.IndexOf(SupportedLocales, locale) >= 0;

    /// <summary>"he-IL" → "he", "fr-FR" → "en", "" → "en". Only the language subtag is compared.</summary>
    public static string Match(string? tag) => TryMatch(tag, out var locale) ? locale : DefaultLocale;

    /// <summary>Same matching without the English fallback, for callers that have a better default of their own.</summary>
    public static bool TryMatch(string? tag, out string locale)
    {
        locale = DefaultLocale;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var language = tag.Trim().Split('-', '_')[0].ToLowerInvariant();
        if (!IsSupported(language))
        {
            return false;
        }

        locale = language;
        return true;
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
    public static string Resolve(string? explicitLanguage, HttpRequest request) =>
        TryMatch(explicitLanguage, out var language) ? language : MatchAcceptLanguage(request.Headers.AcceptLanguage.ToString());

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

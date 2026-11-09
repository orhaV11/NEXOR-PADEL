"""Build the campaign gallery (repo page + publishable page with JPEG previews) and README from copy.json.

    python3 src/build_gallery.py [--publish-dir DIR]

The repo page (venuvoy-ads/index.html) points at the PNG masters. With --publish-dir, a second,
lighter copy of the page is written there with full-size JPEGs, thumbnails and the reel, ready to
publish as one page plus files.
"""
import html
import json
import os
import subprocess
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
ADS = ROOT / "ads"
COPY = json.loads((ROOT / "src" / "copy.json").read_text(encoding="utf-8"))

FORMATS = [
    ("feed", "פיד", "4:5", 1080, 1350),
    ("story", "סטורי / ריל", "9:16", 1080, 1920),
    ("square", "ריבוע", "1:1", 1080, 1080),
    ("landscape", "רוחבי", "1.91:1", 1200, 628),
]
REEL = "07-reel-he/venuvoy-reel-he_9x16_1080x1920.mp4"
REEL_15 = "07-reel-he/venuvoy-reel-he-15s_9x16_1080x1920.mp4"
REEL_EN = "08-reel-en/venuvoy-reel-en_9x16_1080x1920.mp4"
REEL_EN_15 = "08-reel-en/venuvoy-reel-en-15s_9x16_1080x1920.mp4"
VID_FORMATS = [("story", "9:16", 1080, 1920), ("feed", "4:5", 1080, 1350)]
CAMP = COPY["campaign"]


def spot_mp4(sp, lang, fmt="9x16_1080x1920"):
    return f"{sp['folder']}/venuvoy-{sp['id']}-{lang}_{fmt}.mp4"


def mp4(cid, fmt):
    w, h = next((w, h) for f, _, w, h in VID_FORMATS if f == fmt)
    return f"ads/{cid}/{cid}_{fmt}_{w}x{h}.mp4"

NOTES = [
    ("הקמפיין הבינלאומי: הנתונים", "המשחק בסרטונים, באיירן מינכן נגד ריאל בטיס, ליגת האלופות, יום ד׳ 27.1.2027 ב־21:00 באליאנץ ארנה, מאושר מול uefa.com (נבדק 24.10), לקוח מהקטלוג של Venuvoy. הדרך מהשדה (כ־40 דק׳ ברכבת) וקו U6 לפרטמנינג הם מידע כללי על מינכן. כל מסך של המוצר מסומן ״המחשה״."),
    ("הקמפיין הבינלאומי: הסאונד", "שירת היציע היא מנגינה מקורית בלי מילים (״הו״ ו״אה״), ולא ציטוט של שיר קיים. החתימה הצלילית (נשיפה של כלי נשיפה, ״היי!״ של הקהל ופעמונים) זהה בכל הסרטונים, כדי שהמותג יישמע אותו דבר בכל מקום."),
    ("הקמפיין הבינלאומי: הקומדיה", "אתר הכרטיסים המפוקפק, הצ׳אט והמסמכים בסרטון הקומי הם בדיוניים וגנריים, בלי לוגו או שם של אתר אמיתי."),
    ("הדומיין", "המודעות מציגות VENUVOY.APP, הדומיין שמוגדר בקוד (src/lib/brand.ts). כדאי לוודא שהוא חי לפני שמעלים."),
    ("תאריכים", "המשחקים במודעות הם מינואר עד מרץ 2027, כרטיס השבת הוא ללונדון ב־19-25.11.2026, והסרטונים הראשיים מראים את לונדון ב־22-29.11.2026. אחרי התאריכים האלה מחליפים לנתונים חדשים ומרנדרים מחדש."),
    ("מדיניות Meta", "אין טרגוט לפי דת, ולכן מודעת השבת וסצנת השבת בסרטון רצות על קהל רחב. הטקסטים לא פונים לגולש לפי מאפיין אישי (למשל ״שומרים שבת?״), כדי לעמוד בכללים."),
    ("שקיפות", "כל מסך של המוצר בסרטונים מסומן ״המחשה״ (Illustration) באות קריאה. המעקב בתשלום, וזה כתוב בסרטונים ובתיאור של המודעה. שמות קבוצות מופיעים רק כשמות משחקים, בלי סמלים."),
    ("מוזיקה", "לכל הסרטונים יש מוזיקה מקורית שהולחנה בקוד, בלי זכויות של צד שלישי. כל סרטון מקבל עיבוד באורך שלו, לא חיתוך של שיר ארוך: אנרגיה מהפריים הראשון, אפקט אחד לכל דבר שקורה על המסך, ונחיתה על אקורד סיום עם רקע שקט מתחת לכפתור. הכול מאוזן ל־‎-14 LUFS עם שיא מתחת ל־‎-1 dBTP, כפי שהוא נשמע אחרי הקידוד."),
    ("15 שניות", "לכל סרטון ראשי יש גרסה של 15 שניות לקמפיינים ממומנים: הוק, מותג, תוכנית, קישור מחקה, התראה וסיום. היא נחתכה עם video-use (render.py) והמוזיקה שלה הולחנה מחדש לאורך הזה."),
    ("אזור בטוח", "בכל הסטוריז, הכפתור והכתובת נגמרים מעל גובה 1,565 פיקסלים, מחוץ לפס התחתון שהממשק של סטוריז מכסה. בסרטונים הראשיים כל הטקסט החשוב נמצא בין 270 ל־1,580."),
    ("שפות בסרטון האנגלי", "בסרטון כתוב שאנגלית ועברית זמינות היום ו־4 שפות בדרך. רוסית, ערבית, צרפתית וספרדית מוגדרות בקוד כ״מתוכננות״ ועוד לא פתוחות למשתמשים. אם לא רוצים להתחייב לזה בפומבי, צריך להוריד את הסצנה."),
    ("מחירים", "בסרטון האנגלי בדולרים ($15, $29, $39) ובעברי בשקלים (₪49, ₪99, ₪129), לפי ספר המחירים של המוצר. ״התכנון בחינם״ ו״תשלום על התוכנית המלאה״ מופיעים יחד, כדי שלא תהיה סתירה."),
    ("בדיקה ראשונה", "להתחיל בגרסאות ה־15 שניות וב־01, 02 ו־03 מול קהל רחב של אוהדי כדורגל שטסים לחו״ל, ואת 06 להפעיל כריטרגטינג. מודדים עלות לתוכנית שנבנתה, לא רק הקלקות."),
]

CREST = (
    '<svg viewBox="0 0 40 40" aria-hidden="true" class="crest">'
    '<circle cx="20" cy="20" r="19" fill="var(--band)"/>'
    '<circle cx="20" cy="20" r="18.25" fill="none" stroke="var(--brass)" stroke-width="1"/>'
    '<circle cx="20" cy="20" r="15.6" fill="none" stroke="var(--brass)" stroke-width="0.6"/>'
    '<path d="M20 1.2 21 2.9 20 4.6 19 2.9Z" fill="var(--brass)"/><path d="M20 35.4 21 37.1 20 38.8 19 37.1Z" fill="var(--brass)"/>'
    '<path d="M11.2 11.6h6.6v.75l-1.6.2 4.35 11.4 3.95-10.6c.25-.68.05-.95-.65-1.05l-1.05-.15v-.75h5.05v.75l-.75.12c-.6.1-.92.4-1.2 1.12L20.3 28.9h-.95l-6.3-16.1c-.25-.62-.55-.9-1.1-.98l-.75-.1Z" fill="var(--band-fg)"/>'
    "</svg>"
)

e = lambda s: html.escape(str(s), quote=True)


def png(cid, fmt):
    w, h = next((w, h) for f, _, _, w, h in FORMATS if f == fmt)
    return f"ads/{cid}/{cid}_{fmt}_{w}x{h}.png"


STYLE = """
<style>
/* Layout: one column "fixture book" — each ad is a ruled entry: name, the four sizes, then its copy. */
:root {
  --bg: #f4eee1; --fg: #26221c; --muted: #665d4c; --card: #fbf8f1; --line: #ddd2bb;
  --band: #16392b; --band-fg: #f3ecdc; --accent: #1e4d38; --brass: #b8955a; --brass-ink: #7e6127; --ox: #6f1d29;
  --shade: rgba(38, 30, 18, 0.18);
  --f-display: "Frank Ruhl Libre", "Playfair Display", Georgia, serif;
  --f-latin: "Playfair Display", Georgia, serif;
  --f-text: "Assistant", "Segoe UI", system-ui, sans-serif;
}
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) {
  --bg: #11211a; --fg: #ece4d2; --muted: #a99f89; --card: #172a21; --line: #31453a;
  --band: #0d2219; --band-fg: #f3ecdc; --accent: #cdae72; --brass: #b8955a; --brass-ink: #cdae72; --ox: #dc9890;
  --shade: rgba(0, 0, 0, 0.45); color-scheme: dark; } }
:root[data-theme="dark"] {
  --bg: #11211a; --fg: #ece4d2; --muted: #a99f89; --card: #172a21; --line: #31453a;
  --band: #0d2219; --band-fg: #f3ecdc; --accent: #cdae72; --brass: #b8955a; --brass-ink: #cdae72; --ox: #dc9890;
  --shade: rgba(0, 0, 0, 0.45); color-scheme: dark; }
body { background: var(--bg); color: var(--fg); font-family: var(--f-text); font-size: 17px; line-height: 1.6; -webkit-font-smoothing: antialiased; }
.page { max-width: 1180px; margin: 0 auto; padding-block: 28px 64px;
  padding-left: max(16px, env(safe-area-inset-left, 0px)); padding-right: max(16px, env(safe-area-inset-right, 0px)); }
.mast { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding-bottom: 18px; border-bottom: 4px double var(--brass); }
.lockup { display: inline-flex; align-items: center; gap: 12px; direction: ltr; }
.crest { width: 40px; height: 40px; flex: none; }
.wordmark { font-family: var(--f-latin); font-weight: 500; letter-spacing: 0.28em; text-transform: uppercase; font-size: 18px; color: var(--fg); }
.mast .meta { font-size: 14px; color: var(--muted); text-align: left; }
h1 { font-family: var(--f-display); font-weight: 800; font-size: clamp(38px, 6vw, 68px); line-height: 1.05; margin: 34px 0 10px; text-wrap: balance; }
h1 .g { color: var(--accent); }
.lede { max-width: 62ch; font-size: 19px; color: var(--muted); margin: 0; }
.specs { display: flex; flex-wrap: wrap; gap: 8px 10px; margin: 22px 0 0; padding: 0; list-style: none; }
.specs li { font-size: 14px; font-weight: 700; padding: 6px 10px; border: 1px solid var(--line); border-radius: 2px; background: var(--card); font-variant-numeric: tabular-nums; }
.specs li span { color: var(--muted); font-weight: 600; margin-inline-start: 4px; direction: ltr; unicode-bidi: isolate; }
section.entry { margin-top: 56px; padding-top: 22px; border-top: 1px solid var(--line); }
.entry-h { display: flex; align-items: baseline; flex-wrap: wrap; gap: 6px 16px; }
.no { font-family: var(--f-latin); font-style: italic; font-weight: 600; font-size: 28px; color: var(--brass); }
h2 { font-family: var(--f-display); font-weight: 700; font-size: clamp(26px, 3.4vw, 36px); line-height: 1.15; margin: 0; }
.onimg { font-family: var(--f-display); font-weight: 600; font-size: 20px; color: var(--accent); }
.angle { max-width: 70ch; margin: 8px 0 0; color: var(--muted); }
.formats { display: grid; grid-template-columns: 0.8fr 0.5625fr 1fr; gap: 12px; margin-top: 22px; align-items: start; }
.formats .wide { grid-column: 1 / -1; max-width: 760px; }
figure { margin: 0; min-width: 0; }
figure button { display: block; width: 100%; padding: 0; border: 0; background: none; cursor: zoom-in; border-radius: 2px; }
figure img { display: block; width: 100%; height: auto; border-radius: 2px; box-shadow: 0 0 0 1px var(--line), 0 14px 30px -18px var(--shade); }
figure button:focus-visible { outline: 3px solid var(--accent); outline-offset: 3px; }
figcaption { margin-top: 6px; font-size: 13px; font-weight: 700; color: var(--muted); }
figcaption span { font-weight: 600; }
.copy { margin-top: 22px; display: grid; grid-template-columns: 9.5em minmax(0, 1fr) auto; border: 1px solid var(--line); background: var(--card); border-radius: 3px; }
.copy > div { display: contents; }
.copy dt, .copy dd, .copy .act { padding: 12px 14px; border-top: 1px solid var(--line); margin: 0; }
.copy > div:first-child dt, .copy > div:first-child dd, .copy > div:first-child .act { border-top: 0; }
.copy dt { font-size: 13px; font-weight: 800; color: var(--brass-ink); letter-spacing: 0.02em; }
.copy dt small { display: block; font-weight: 600; color: var(--muted); letter-spacing: 0; font-variant-numeric: tabular-nums; }
.copy dd { min-width: 0; font-size: 17px; }
.copy .act { display: flex; align-items: flex-start; justify-content: flex-end; }
button.cp { font: 700 14px var(--f-text); color: var(--accent); background: transparent; border: 1px solid var(--line); border-radius: 2px; padding: 5px 12px; cursor: pointer; white-space: nowrap; }
button.cp:hover { border-color: var(--accent); }
button.cp:focus-visible { outline: 3px solid var(--accent); outline-offset: 2px; }
.basis { margin: 12px 0 0; font-size: 14px; color: var(--muted); max-width: 80ch; }
.basis b { color: var(--fg); }
.vids { display: grid; grid-template-columns: 0.5625fr 0.8fr; gap: 12px; margin-top: 18px; max-width: 640px; align-items: start; }
.vids figure video { width: 100%; height: auto; display: block; border-radius: 3px; background: #0d2219; box-shadow: 0 0 0 1px var(--line), 0 14px 30px -18px var(--shade); }
.sub-h { margin: 26px 0 0; font: 800 13px var(--f-text); letter-spacing: 0.08em; color: var(--brass-ink); }
.reel { display: grid; grid-template-columns: minmax(0, 470px) minmax(0, 1fr); gap: 28px; margin-top: 22px; align-items: start; }
.rv { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; align-items: start; }
.reel video { width: 100%; height: auto; display: block; border-radius: 3px; background: #0d2219; box-shadow: 0 0 0 1px var(--line), 0 20px 40px -24px var(--shade); }
.scenes { list-style: none; margin: 0; padding: 0; border-top: 1px solid var(--line); }
.scenes li { padding: 10px 0; border-bottom: 1px solid var(--line); font-size: 16px; }
.kit { margin-top: 22px; display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 14px; }
.kit .box { background: var(--card); border: 1px solid var(--line); border-radius: 3px; padding: 16px 18px; min-width: 0; }
.kit h3 { font-family: var(--f-display); font-size: 21px; margin: 0 0 8px; }
.kit ul { margin: 0; padding-inline-start: 1.1em; }
.kit li { margin: 4px 0; }
.kit li .n { color: var(--muted); font-size: 13px; font-variant-numeric: tabular-nums; margin-inline-start: 6px; }
.notes { margin: 22px 0 0; padding: 0; list-style: none; display: grid; gap: 10px; }
.notes li { padding: 12px 16px; border-inline-start: 3px solid var(--brass); background: var(--card); border-radius: 2px; }
.notes b { display: block; }
.foot { margin-top: 56px; padding-top: 18px; border-top: 4px double var(--brass); font-size: 14px; color: var(--muted); }
.foot code { font-size: 13px; direction: ltr; unicode-bidi: isolate; }
.lb { position: fixed; inset: 0; z-index: 50; background: rgba(6, 12, 9, 0.92); display: flex; align-items: center; justify-content: center;
  padding: max(16px, env(safe-area-inset-top, 0px)) max(16px, env(safe-area-inset-right, 0px)) max(16px, env(safe-area-inset-bottom, 0px)) max(16px, env(safe-area-inset-left, 0px)); }
.lb[hidden] { display: none; }
.lb img { max-width: 100%; max-height: 100%; width: auto; height: auto; box-shadow: 0 30px 80px rgba(0, 0, 0, 0.6); }
.lb button { position: absolute; top: max(14px, env(safe-area-inset-top, 0px)); left: max(14px, env(safe-area-inset-left, 0px)); font: 700 15px var(--f-text); color: #f3ecdc; background: rgba(0, 0, 0, 0.4); border: 1px solid rgba(243, 236, 220, 0.4); border-radius: 2px; padding: 8px 14px; cursor: pointer; }
.ltr { direction: ltr; unicode-bidi: isolate; }
@media (max-width: 720px) {
  .copy { grid-template-columns: minmax(0, 1fr) auto; }
  .copy dt { grid-column: 1 / -1; padding-bottom: 0; }
  .copy dd, .copy .act { border-top: 0; }
  .reel { grid-template-columns: minmax(0, 1fr); }
  .rv { max-width: 470px; }
  .mast .meta { display: none; }
}
@media (prefers-reduced-motion: reduce) { * { scroll-behavior: auto !important; } }
</style>
"""

SCRIPT = """
<script>
(function () {
  const lb = document.getElementById("lb"), lbImg = document.getElementById("lbImg");
  document.addEventListener("click", function (ev) {
    const z = ev.target.closest("[data-full]");
    if (z) { lbImg.src = z.dataset.full; lbImg.alt = z.dataset.alt || ""; lb.hidden = false; lb.querySelector("button").focus(); return; }
    if (ev.target.closest("#lb")) { lb.hidden = true; lbImg.removeAttribute("src"); return; }
    const b = ev.target.closest("button.cp");
    if (!b) return;
    const src = document.getElementById(b.dataset.for);
    const text = src ? src.textContent.trim() : "";
    const done = function (label) { b.textContent = label; setTimeout(function () { b.textContent = "העתקה"; }, 1600); };
    try {
      navigator.clipboard.writeText(text).then(function () { done("הועתק"); }, function () { select(src); done("מסומן, להעתקה ידנית"); });
    } catch (err) { select(src); done("מסומן, להעתקה ידנית"); }
  });
  document.addEventListener("keydown", function (ev) { if (ev.key === "Escape" && !lb.hidden) { lb.hidden = true; } });
  function select(el) { if (!el) return; const r = document.createRange(); r.selectNodeContents(el); const s = getSelection(); s.removeAllRanges(); s.addRange(r); }
})();
</script>
"""


def copy_rows(prefix, meta):
    rows = [
        ("טקסט ראשי", "Primary text", meta["primary"], 125),
        ("כותרת", "Headline", meta["headline"], 40),
        ("תיאור", "Description", meta["description"], 30),
        ("כפתור", "CTA", meta["cta"], None),
    ]
    out = []
    for i, (he, en, text, lim) in enumerate(rows):
        rid = f"{prefix}-{i}"
        count = f"{len(text)}/{lim}" if lim else en
        out.append(
            f'<div><dt>{e(he)}<small><span class="ltr">{e(count)}</span></small></dt>'
            f'<dd id="{rid}" dir="auto">{e(text)}</dd>'
            f'<span class="act"><button type="button" class="cp" data-for="{rid}">העתקה</button></span></div>'
        )
    return '<dl class="copy">' + "".join(out) + "</dl>"


def reel_videos(full, short, poster, lang):
    cap = ("35 ש׳", "15 ש׳") if lang == "he" else ('<span class="ltr">35 s</span>', '<span class="ltr">15 s</span>')
    return ('<div class="rv">'
            f'<figure><video src="{e(full)}" poster="{e(poster)}" controls playsinline loop preload="metadata"></video><figcaption>הסרטון המלא · {cap[0]}</figcaption></figure>'
            f'<figure><video src="{e(short)}" poster="{e(poster)}" controls playsinline loop preload="metadata"></video><figcaption>גרסה ממומנת · {cap[1]}</figcaption></figure>'
            "</div>")


def body(img_src, full_src, vid_src, reels, camp_src=None, camp_poster=None):
    """reels: (he_full, he_15, he_poster, en_full, en_15, en_poster); camp_*: (spot, lang) -> path"""
    camp_src = camp_src or (lambda sp, l: f"ads/{spot_mp4(sp, l)}")
    camp_poster = camp_poster or (lambda sp, l: f"ads/{sp['folder']}/poster-{l}.jpg")
    c = COPY
    parts = []
    parts.append(
        f'<header class="mast"><span class="lockup">{CREST}<span class="wordmark">Venuvoy</span></span>'
        f'<span class="meta">קמפיין בינלאומי · 2 סרטונים ראשיים · 6 מודעות · עברית ואנגלית</span></header>'
    )
    parts.append(
        '<h1>ערכת הקמפיין של <span class="g ltr">Venuvoy</span></h1>'
        '<p class="lede">קמפיין בינלאומי של ארבעה סרטונים באנגלית ובעברית, סרטון ראשי בעברית ובאנגלית (35 שניות, וגרסה של 15 שניות לכל אחד), ושש מודעות וידאו בעברית (9:16 ו־4:5, 8 שניות). '
        "לכל הסרטונים מוזיקה מקורית. כל מודעה קיימת גם כתמונה בארבעה גדלים. כל משחק, מחיר ושעה לקוחים מהמוצר עצמו: תוכניות שהמנוע ייצר, שורות מהקטלוג וספר המחירים. "
        "מתחת לכל מודעה יש את הטקסטים ל־Meta, מוכנים להעתקה.</p>"
    )
    parts.append(
        '<ul class="specs">'
        + "".join(f'<li>{e(lbl)} <span>{w}×{h}</span></li>' for _, lbl, _, w, h in FORMATS)
        + '<li>וידאו 9:16 <span>1080×1920</span></li><li>וידאו 4:5 <span>1080×1350</span></li></ul>'
    )

    # The international campaign first: the newest and strongest pieces.
    parts.append(
        '<section class="entry" aria-labelledby="h-camp"><div class="entry-h"><span class="no">★</span>'
        f'<h2 id="h-camp">קמפיין בינלאומי · <span class="ltr">{e(CAMP["title"])}</span></h2><span class="onimg">{e(CAMP["titleHe"])}</span></div>'
        '<p class="angle">ארבעה סרטונים, כל אחד באנגלית ובעברית, ב־9:16 וב־4:5: סרט ראשי עם סטדיון וגלובוס בתלת־ממד, קומדיה, מונטאז׳ ענפים ובאמפר של 6 שניות. '
        "לכל אחד מוזיקה ואפקטים מקוריים, וכולם נגמרים באותה חתימה צלילית.</p></section>"
    )
    for sp in CAMP["spots"]:
        vids = "".join(
            f'<figure><video src="{e(camp_src(sp, l))}" poster="{e(camp_poster(sp, l))}" controls playsinline loop preload="metadata"></video>'
            f'<figcaption>{"English" if l == "en" else "עברית"} · <span class="ltr">9:16</span></figcaption></figure>' for l in ("en", "he"))
        parts.append(
            f'<section class="entry" aria-labelledby="h-{sp["id"]}"><div class="entry-h"><span class="no">{sp["folder"][:2]}</span>'
            f'<h2 id="h-{sp["id"]}">{e(sp["name"])}</h2><span class="onimg">{e(sp["length"])}</span></div>'
            f'<p class="angle">{e(sp["angle"])}</p>'
            f'<div class="reel"><div class="rv">{vids}</div><div>'
            '<p class="sub-h" style="margin-top:0">English · Meta</p>' + copy_rows(f"{sp['id']}-en", sp["en"])
            + '<p class="sub-h">עברית · Meta</p>' + copy_rows(f"{sp['id']}-he", sp["he"])
            + '<p class="basis">כל גרסה קיימת גם ב־4:5 (<span class="ltr">1080×1350</span>) לפיד, באותה תיקייה.'
            + (' לסרט הראשי יש גם גרסה של 15 שניות לקמפיינים ממומנים (<span class="ltr">-15s</span>), שנחתכה עם video-use.' if sp["id"] == "anthem" else "")
            + "</p>"
            + "</div></div></section>"
        )

    # The two flagship reels next.
    he_full, he_15, he_poster, en_full, en_15, en_poster = reels
    r = c["reel"]
    parts.append(
        f'<section class="entry" aria-labelledby="h-reel"><div class="entry-h"><span class="no">07</span>'
        f'<h2 id="h-reel">{e(r["name"])}</h2><span class="onimg">טיול ספורט שאפשר לסמוך עליו.</span></div>'
        f'<p class="angle">{e(r["length"])}. כל המסר כתוב על המסך, כי רוב הצפיות בפיד הן בלי סאונד; עם סאונד יש מוזיקה מקורית ואפקט אחד לכל דבר שקורה.</p>'
        f'<div class="reel">{reel_videos(he_full, he_15, he_poster, "he")}'
        '<div><ol class="scenes">' + "".join(f"<li>{e(sc)}</li>" for sc in r["scenes"]) + "</ol>"
        + '<p class="sub-h">גרסת 15 השניות</p><ol class="scenes">' + "".join(f"<li>{e(sc)}</li>" for sc in r["cut15"]) + "</ol>"
        + copy_rows("reel", r["meta"]) + "</div></div></section>"
    )
    r2 = c["reelEn"]
    parts.append(
        f'<section class="entry" aria-labelledby="h-reel-en"><div class="entry-h"><span class="no">08</span>'
        f'<h2 id="h-reel-en">{e(r2["name"])}</h2><span class="onimg ltr">A sports trip you can trust.</span></div>'
        f'<p class="angle"><span class="ltr">{e(r2["length"])}</span>. קצבי וכיפי, עם מוזיקה מקורית שהולחנה בקוד, כך שאין בה זכויות של אף אחד אחר.</p>'
        f'<div class="reel">{reel_videos(en_full, en_15, en_poster, "en")}'
        '<div><ol class="scenes" dir="ltr" lang="en">' + "".join(f"<li>{e(sc)}</li>" for sc in r2["scenes"]) + "</ol>"
        + '<p class="sub-h">גרסת 15 השניות</p><ol class="scenes" dir="ltr" lang="en">' + "".join(f"<li>{e(sc)}</li>" for sc in r2["cut15"]) + "</ol>"
        + copy_rows("reel-en", r2["meta"]) + "</div></div></section>"
    )

    for i, x in enumerate(c["concepts"], start=1):
        cid = x["id"]
        figs = []
        for fmt, lbl, ratio, w, h in FORMATS:
            cls = ' class="wide"' if fmt == "landscape" else ""
            alt = f'{x["onImage"]}, {lbl} {w}×{h}'
            figs.append(
                f'<figure{cls}><button type="button" data-full="{e(full_src(cid, fmt))}" data-alt="{e(alt)}" aria-label="הגדלה: {e(alt)}">'
                f'<img src="{e(img_src(cid, fmt))}" alt="{e(alt)}" width="{w}" height="{h}" loading="lazy" decoding="async"></button>'
                f'<figcaption>{e(lbl)} · <span class="ltr">{e(ratio)}</span> · <span class="ltr">{w}×{h}</span></figcaption></figure>'
            )
        parts.append(
            f'<section class="entry" aria-labelledby="h-{cid}"><div class="entry-h"><span class="no">{i:02d}</span>'
            f'<h2 id="h-{cid}">{e(x["name"])}</h2><span class="onimg">{e(x["onImage"])}</span></div>'
            f'<p class="angle">{e(x["angle"])}</p>'
            '<p class="sub-h">וידאו · 8 שניות, עם מוזיקה</p>'
            + '<div class="vids">' + "".join(
                f'<figure><video src="{e(vid_src(cid, vf))}" poster="{e(img_src(cid, vf))}" controls playsinline loop preload="none" width="{vw}" height="{vh}"></video>'
                f'<figcaption>{"סטורי / ריל" if vf == "story" else "פיד"} · <span class="ltr">{vr}</span> · <span class="ltr">{vw}×{vh}</span></figcaption></figure>'
                for vf, vr, vw, vh in VID_FORMATS) + '</div>'
            + '<p class="sub-h">תמונות · ארבעה גדלים</p>'
            + f'<div class="formats">{"".join(figs)}</div>'
            + copy_rows(cid, x["meta"])
            + f'<p class="basis"><b>קהל:</b> {e(x["audience"])}</p>'
            f'<p class="basis"><b>על מה זה מבוסס:</b> {e(x["basis"])}</p></section>'
        )

    g = c["google"]
    def lst(items, lim, key):
        return "<ul>" + "".join(
            f'<li><span id="g-{key}-{j}">{e(s)}</span><span class="n ltr">{len(s)}/{lim}</span></li>' for j, s in enumerate(items)
        ) + "</ul>"
    parts.append(
        '<section class="entry" aria-labelledby="h-google"><div class="entry-h"><span class="no">G</span>'
        '<h2 id="h-google">Google Ads · מודעה רספונסיבית</h2></div>'
        '<p class="angle">התמונות הרוחביות והריבועיות מתאימות ישירות לרשת המדיה. אלה הטקסטים, בתוך המגבלות של Google.</p>'
        '<div class="kit">'
        f'<div class="box"><h3>כותרות קצרות</h3>{lst(g["short"], 30, "s")}</div>'
        f'<div class="box"><h3>כותרת ארוכה</h3>{lst(g["long"], 90, "l")}</div>'
        f'<div class="box"><h3>תיאורים</h3>{lst(g["descriptions"], 90, "d")}</div>'
        "</div></section>"
    )

    notes = NOTES
    parts.append(
        '<section class="entry" aria-labelledby="h-notes"><div class="entry-h"><span class="no">✓</span><h2 id="h-notes">לפני שעולים לאוויר</h2></div>'
        '<ul class="notes">' + "".join(f"<li><b>{e(t)}</b>{e(d)}</li>" for t, d in notes) + "</ul></section>"
    )
    parts.append(
        '<p class="foot">הקבצים המלאים (PNG ו־MP4) והקוד שמייצר אותם נמצאים בתיקייה <code>venuvoy-ads/</code> בענף '
        '<code>claude/keen-allen-v7ed6t</code>. שינוי טקסט, מחיר או משחק הוא עריכה של HTML והרצה של <code>node src/render.mjs</code>.</p>'
    )
    parts.append('<div class="lb" id="lb" hidden><button type="button">סגירה</button><img id="lbImg" alt=""></div>')
    return '<div class="page" dir="rtl" lang="he">' + "\n".join(parts) + "</div>"



def build_readme():
    c = COPY
    L = []
    L.append("# ערכת הקמפיין של Venuvoy")
    L.append("")
    L.append("שש מודעות בעברית, כל אחת בארבעה גדלים וכווידאו של 8 שניות, ושני סרטונים ראשיים (עברית ואנגלית, 35 שניות וגרסה של 15), בשפה העיצובית של Venuvoy "
             "(ירוק מרוצים, נייר שמנת, פליז ובורדו; Frank Ruhl Libre, Playfair Display ו־Assistant). "
             "כל משחק, מחיר ושעה במודעות לקוחים מהמוצר עצמו: תוכניות שהמנוע ייצר, שורות מהקטלוג וספר המחירים.")
    L.append("")
    L.append("לצפייה נוחה: פתחו את `index.html` בדפדפן. כל התמונות והווידאו נמצאים ב־`ads/`.")
    L.append("")
    L.append(f"## קמפיין בינלאומי · {CAMP['title']}")
    L.append("")
    L.append("ארבעה סרטונים, באנגלית ובעברית, ב־9:16 (1080×1920) וב־4:5 (1080×1350). מוזיקה ואפקטים מקוריים שהולחנו בקוד, סטדיון וגלובוס בתלת־ממד, וחתימה צלילית אחת לכולם.")
    L.append("")
    L.append("| סרטון | אורך | קבצים |")
    L.append("|---|---|---|")
    for sp in CAMP["spots"]:
        L.append(f"| {sp['name']} | {sp['length']} | `ads/{sp['folder']}/venuvoy-{sp['id']}-{{en,he}}_{{9x16_1080x1920,4x5_1080x1350}}.mp4` |")
    L.append("")
    for sp in CAMP["spots"]:
        L.append(f"### {sp['name']}")
        L.append("")
        L.append(sp["angle"])
        L.append("")
        L.append("| Meta | English | עברית |")
        L.append("|---|---|---|")
        for k, lbl in (("primary", "Primary text"), ("headline", "Headline"), ("description", "Description"), ("cta", "CTA")):
            L.append(f"| {lbl} | {sp['en'][k]} | {sp['he'][k]} |")
        L.append("")
    L.append("רינדור: `node src/render-spot.mjs anthem en` (וגם chaos, sports, bumper; en או he). הסצנות ב־`src/spots/`, התלת־ממד ב־`src/fx3d/`, המוזיקה ב־`src/audio/anthem.py`, `chaos.py`, `sports.py`, `bumper.py` ו־`kit.py`.")
    L.append("")
    L.append("## הקבצים")
    L.append("")
    L.append("| גודל | מידות | שימוש |")
    L.append("|---|---|---|")
    uses = {"feed": "פיד באינסטגרם ובפייסבוק (הכי יעיל בפיד)", "story": "סטוריז, ריילז, טיקטוק", "square": "פיד, קרוסלה, רשת המדיה של Google", "landscape": "פייסבוק (קישור), Google Display, לינקדאין"}
    for f, lbl, ratio, w, h in FORMATS:
        L.append(f"| {lbl} {ratio} | {w}×{h} | {uses[f]} |")
    L.append("| וידאו 9:16 (כל מודעה) | 1080×1920, 8 ש׳, עם מוזיקה | ריילז, סטוריז, טיקטוק |")
    L.append("| וידאו 4:5 (כל מודעה) | 1080×1350, 8 ש׳, עם מוזיקה | פיד באינסטגרם ובפייסבוק |")
    L.append("| סרטון ראשי בעברית | 1080×1920, 35 ש׳ + גרסת 15 ש׳, מוזיקה מקורית | ריילז, סטוריז, טיקטוק, יוטיוב שורטס |")
    L.append("| סרטון ראשי באנגלית | 1080×1920, 35 ש׳ + גרסת 15 ש׳, מוזיקה מקורית | ריילז, סטוריז, טיקטוק, יוטיוב שורטס |")
    L.append("")
    L.append("```")
    L.append("ads/01-kickoff … ads/06-offer   כל מודעה: <id>_<format>_<WxH>.png וגם .mp4 ל־story ו־feed")
    L.append("ads/07-reel-he/venuvoy-reel-he_9x16_1080x1920.mp4       + venuvoy-reel-he-15s_9x16_1080x1920.mp4")
    L.append("ads/08-reel-en/venuvoy-reel-en_9x16_1080x1920.mp4       + venuvoy-reel-en-15s_9x16_1080x1920.mp4")
    L.append("src/video-fun/reel.html         הסרטון הראשי, עברית ואנגלית (?lang=he|en), ציר זמן מדויק לפריים")
    L.append("src/concepts/*.html             המודעות עצמן (HTML/CSS), אחת לכל רעיון")
    L.append("src/motion.js                   השכבה שמנפישה את המודעות (data-in על כל אלמנט, data-music על ה־body)")
    L.append("src/audio/compose.py            המוזיקה והאפקטים, מסונתזים בקוד ומעובדים לאורך של כל סרטון")
    L.append("src/audio/master.py             מאסטר: ‎-14 LUFS ושיא מתחת ל־‎-1 dBTP, נמדד על הקידוד שיוצא")
    L.append("src/cutdown.py                  גרסאות 15 השניות (video-use render.py + מוזיקה חדשה)")
    L.append("src/copy.json                   כל הטקסטים ל־Meta ול־Google")
    L.append("```")
    L.append("")
    for i, x in enumerate(c["concepts"], start=1):
        m = x["meta"]
        L.append(f"## {i:02d} · {x['name']}")
        L.append("")
        L.append(f"**על התמונה:** {x['onImage']}")
        L.append("")
        L.append(x["angle"])
        L.append("")
        L.append("| שדה ב־Meta | טקסט |")
        L.append("|---|---|")
        L.append(f"| טקסט ראשי ({len(m['primary'])}/125) | {m['primary']} |")
        L.append(f"| כותרת ({len(m['headline'])}/40) | {m['headline']} |")
        L.append(f"| תיאור ({len(m['description'])}/30) | {m['description']} |")
        L.append(f"| כפתור | {m['cta']} |")
        L.append("")
        L.append(f"**קהל:** {x['audience']}")
        L.append("")
        L.append(f"**על מה זה מבוסס:** {x['basis']}")
        L.append("")
    for key, title, hdr in (("reel", "## 07 · סרטון ראשי בעברית 9:16", ("שדה ב־Meta", "טקסט", "טקסט ראשי", "כותרת", "תיאור", "כפתור")),
                            ("reelEn", "## 08 · English video 9:16", ("Meta field", "Text", "Primary text", "Headline", "Description", "CTA"))):
        r = c[key]
        L.append(title)
        L.append("")
        L.append(r["length"] + ".")
        L.append("")
        for sc in r["scenes"]:
            L.append(f"- {sc}")
        L.append("")
        L.append("15 s: " + " · ".join(r["cut15"]))
        L.append("")
        L.append(f"| {hdr[0]} | {hdr[1]} |")
        L.append("|---|---|")
        L.append(f"| {hdr[2]} ({len(r['meta']['primary'])}/125) | {r['meta']['primary']} |")
        L.append(f"| {hdr[3]} | {r['meta']['headline']} |")
        L.append(f"| {hdr[4]} | {r['meta']['description']} |")
        L.append(f"| {hdr[5]} | {r['meta']['cta']} |")
        L.append("")
    g = c["google"]
    L.append("## Google Ads · מודעה רספונסיבית")
    L.append("")
    L.append("כותרות קצרות (עד 30 תווים):")
    L.extend(f"- {s} ({len(s)})" for s in g["short"])
    L.append("")
    L.append("כותרת ארוכה (עד 90):")
    L.extend(f"- {s} ({len(s)})" for s in g["long"])
    L.append("")
    L.append("תיאורים (עד 90):")
    L.extend(f"- {s} ({len(s)})" for s in g["descriptions"])
    L.append("")
    L.append(f"שם העסק: {g['business']}")
    L.append("")
    L.append("## לפני שעולים לאוויר")
    L.append("")
    for t, d in NOTES:
        L.append(f"- **{t}:** {d}")
    L.append("")
    L.append("## עריכה ורינדור מחדש")
    L.append("")
    L.append("צריך Node 22, Playwright עם Chromium, ffmpeg ו־Python עם Pillow, numpy ו־scipy. לגרסאות 15 השניות צריך גם את video-use (https://github.com/browser-use/video-use) ב־‎~/.claude/skills/video-use או ב־$VIDEO_USE.")
    L.append("")
    L.append("```bash")
    L.append("node src/render.mjs                 # כל המודעות, כל הגדלים -> ads/")
    L.append("node src/render.mjs alert story     # רק מודעה אחת בגודל אחד")
    L.append("node src/render-motion.mjs          # שש מודעות הווידאו (9:16 ו־4:5) עם מוזיקה")
    L.append("node src/render-video.mjs --score   # הסרטון הראשי בעברית, עם המוזיקה שהעמוד מבקש -> ads/07-reel-he/")
    L.append('node src/render-video.mjs --page "video-fun/reel.html?lang=en" --out 08-reel-en/venuvoy-reel-en_9x16_1080x1920.mp4 --score')
    L.append("python3 src/cutdown.py he && python3 src/cutdown.py en   # גרסאות 15 השניות")
    L.append("node src/remix-ads.mjs              # רק פס קול חדש לשש המודעות, בלי לרנדר תמונה")
    L.append("python3 src/build_gallery.py        # index.html ו־README.md מתוך src/copy.json")
    L.append("```")
    L.append("")
    L.append("הגופנים ב־`fonts/` הם Frank Ruhl Libre, Assistant, Playfair Display, Noto Sans Arabic ו־Noto Sans (קירילית), כולם ברישיון SIL Open Font License.")
    L.append("")
    (ROOT / "README.md").write_text("\n".join(L), encoding="utf-8")


HEAD = (
    "<title>ערכת קמפיין Venuvoy</title>\n"
    '<meta name="claude:viewport-fit-sides" content="cover">\n'
    '<link rel="preconnect" href="https://fonts.googleapis.com">\n'
    '<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>\n'
    '<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Assistant:wght@400;600;700;800&family=Frank+Ruhl+Libre:wght@600;700;800&family=Playfair+Display:ital,wght@0,500;0,600;1,600&display=swap">\n'
)


def build_repo_page():
    page = body(lambda c, f: png(c, f), lambda c, f: png(c, f), lambda c, f: mp4(c, f),
                (f"ads/{REEL}", f"ads/{REEL_15}", "ads/07-reel-he/poster.jpg", f"ads/{REEL_EN}", f"ads/{REEL_EN_15}", "ads/08-reel-en/poster.jpg"))
    doc = (
        '<!doctype html>\n<html lang="he" dir="rtl">\n<head>\n<meta charset="utf-8">\n'
        '<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">\n'
        + HEAD + STYLE + "</head>\n<body>\n" + page + SCRIPT + "</body>\n</html>\n"
    )
    (ROOT / "index.html").write_text(doc, encoding="utf-8")


def build_publish(dirpath):
    d = Path(dirpath)
    (d / "img").mkdir(parents=True, exist_ok=True)
    (d / "thumb").mkdir(parents=True, exist_ok=True)
    for x in COPY["concepts"]:
        for fmt, *_ in FORMATS:
            im = Image.open(ROOT / png(x["id"], fmt)).convert("RGB")
            im.save(d / "img" / f'{x["id"]}_{fmt}.jpg', quality=90, subsampling=0, optimize=True, progressive=True)
            t = im.copy()
            t.thumbnail((720, 720 if fmt != "story" else 1280), Image.LANCZOS)
            t.save(d / "thumb" / f'{x["id"]}_{fmt}.jpg', quality=84, optimize=True, progressive=True)
    # lighter video copies for the page (the full-quality files stay in the repo)
    (d / "vid").mkdir(parents=True, exist_ok=True)
    web = ["-c:v", "libx264", "-preset", "medium", "-crf", "24", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart"]
    for x in COPY["concepts"]:
        for vf, *_ in VID_FORMATS:
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(ROOT / mp4(x["id"], vf)), "-vf", "scale=720:-2", *web, str(d / "vid" / f'{x["id"]}_{vf}.mp4')], check=True)
    for src, name in ((REEL, "reel.mp4"), (REEL_15, "reel-15.mp4"), (REEL_EN, "reel-en.mp4"), (REEL_EN_15, "reel-en-15.mp4")):
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(ADS / src), "-vf", "scale=720:-2", *web, str(d / name)], check=True)
    for sp in CAMP["spots"]:
        for l in ("en", "he"):
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(ADS / spot_mp4(sp, l)), "-vf", "scale=720:-2", *web, str(d / "vid" / f'{sp["id"]}-{l}.mp4')], check=True)
            subprocess.run(["cp", str(ADS / sp["folder"] / f"poster-{l}.jpg"), str(d / "vid" / f'{sp["id"]}-{l}.jpg')], check=True)
    page = body(
        lambda c, f: f"thumb/{c}_{f}.jpg",
        lambda c, f: f"img/{c}_{f}.jpg",
        lambda c, f: f"vid/{c}_{f}.mp4",
        ("reel.mp4", "reel-15.mp4", "poster.jpg", "reel-en.mp4", "reel-en-15.mp4", "poster-en.jpg"),
        lambda sp, l: f"vid/{sp['id']}-{l}.mp4",
        lambda sp, l: f"vid/{sp['id']}-{l}.jpg",
    )
    (d / "index.html").write_text(HEAD + STYLE + page + SCRIPT, encoding="utf-8")


if __name__ == "__main__":
    # posters: the hook, the frame the feed shows before anyone taps
    poster = ADS / "07-reel-he" / "poster.jpg"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", "0.6", "-i", str(ADS / REEL), "-frames:v", "1", "-q:v", "3", str(poster)], check=True)
    poster_en = ADS / "08-reel-en" / "poster.jpg"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", "0.6", "-i", str(ADS / REEL_EN), "-frames:v", "1", "-q:v", "3", str(poster_en)], check=True)
    for sp in CAMP["spots"]:
        for l in ("en", "he"):
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", "0.5", "-i", str(ADS / spot_mp4(sp, l)), "-frames:v", "1", "-q:v", "3", str(ADS / sp["folder"] / f"poster-{l}.jpg")], check=True)
    build_repo_page()
    build_readme()
    if "--publish-dir" in sys.argv:
        out = sys.argv[sys.argv.index("--publish-dir") + 1]
        build_publish(out)
        os.makedirs(out, exist_ok=True)
        subprocess.run(["cp", str(poster), os.path.join(out, "poster.jpg")], check=True)
        subprocess.run(["cp", str(poster_en), os.path.join(out, "poster-en.jpg")], check=True)
    print("ok")

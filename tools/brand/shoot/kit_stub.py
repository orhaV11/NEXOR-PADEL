"""The e2e stub stylist (tools/e2e/stub_anthropic.py, loaded unchanged) with one addition for the brand kit's captures:
GET /__look/<camel|street|pink> makes every following check answer with a verdict written for that photo, in English or
Hebrew by the system prompt's language line, so no screen in the kit judges clothes it is not looking at (the stub's own
verdict is about a white tee, dark jeans and running shoes). Everything else (the 529 on the first request, the
schema checks, Tomorrow, the forecast) is the stub's. kit-shoot.js starts it; by hand:
    python3 tools/brand/shoot/kit_stub.py <port> <api origin>
"""
import importlib.util
import os
import sys
from http.server import HTTPServer

spec = importlib.util.spec_from_file_location(
    "stub", os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "e2e", "stub_anthropic.py"))
stub = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stub)


def item(name, category, verdict, note):
    return {"name": name, "category": category, "verdict": verdict, "note": note, "brand_seen": None}


LOOKS = {
    # tools/brand/templates/photos/look-2-camel.jpg
    "camel": {
        "en": {
            "status": "ok", "score": 9, "intent_match": 84, "tip_kind": "change",
            "headline": "Camel and chocolate, done right", "vibe": "polished off-duty",
            "items": [
                item("Camel coat", "outerwear", "works", "Long and clean; it carries the whole look."),
                item("White turtleneck", "top", "works", "Keeps the neckline quiet under the gold."),
                item("Brown mini skirt", "bottom", "works", "Picks up the coat a shade darker."),
                item("Black tights", "other", "weak", "The one hard stop in a soft column."),
                item("Knee-high boots", "shoes", "works", "Chocolate leather closes the line."),
            ],
            "working": ["Camel, cream and chocolate in one family", "The long coat lengthens the whole line"],
            "one_tip": "Swap the black tights for sheer brown and the column runs unbroken.",
            "breakdown": {"fit": 9, "color": 9, "accessories": 8},
            "accessories": {"verdict": "adds", "present": ["Layered gold necklaces", "Gold hoops", "Quilted bag"],
                            "note": "Layered gold and a quilted bag finish it without shouting.", "add_one": ""},
        },
        "he": {
            "status": "ok", "score": 9, "intent_match": 84, "tip_kind": "change",
            "headline": "גמל ושוקולד, בדיוק כמו שצריך", "vibe": "אלגנטי ויומיומי",
            "items": [
                item("מעיל גמל ארוך", "outerwear", "works", "ארוך ונקי, הוא מחזיק את כל הלוק."),
                item("גולף לבן", "top", "works", "שומר על קו צוואר שקט מתחת לזהב."),
                item("חצאית מיני חומה", "bottom", "works", "ממשיכה את המעיל בגוון כהה יותר."),
                item("גרביונים שחורים", "other", "weak", "העצירה החדה היחידה בקו רך."),
                item("מגפיים גבוהים", "shoes", "works", "העור בגוון שוקולד סוגר את הקו."),
            ],
            "working": ["גמל, שמנת ושוקולד באותה משפחה", "המעיל הארוך מאריך את כל הקו"],
            "one_tip": "שווה להחליף את הגרביונים השחורים בחומים שקופים, והקו רץ בלי הפסקה.",
            "breakdown": {"fit": 9, "color": 9, "accessories": 8},
            "accessories": {"verdict": "adds", "present": ["שרשראות זהב בשכבות", "עגילי חישוק", "תיק מרופד"],
                            "note": "זהב בשכבות ותיק מרופד סוגרים את זה בלי לצעוק.", "add_one": ""},
        },
    },
    # tools/brand/templates/photos/look-1-streetwear.jpg
    "street": {
        "en": {
            "status": "ok", "score": 8, "intent_match": 86, "tip_kind": "change",
            "headline": "All grey, and it holds", "vibe": "easy street",
            "items": [
                item("Washed denim jacket", "outerwear", "works", "The boxy cut sets the shape."),
                item("Grey hoodie", "top", "works", "The layer that makes it read street."),
                item("Wide-leg jeans", "bottom", "works", "The volume balances the short jacket."),
                item("White cap", "accessory", "neutral", "Fine; the one bright spot up top."),
                item("White sneakers", "shoes", "works", "Chunky enough for the wide leg."),
            ],
            "working": ["One grey family from jacket to jeans", "A short jacket over wide jeans: the proportions are right"],
            "one_tip": "Swap the white cap for a charcoal one and the grey runs from top to toe.",
            "breakdown": {"fit": 9, "color": 8, "accessories": 6},
            "accessories": {"verdict": "neutral", "present": ["White cap"],
                            "note": "The cap is the only accessory, and it pulls the eye up.",
                            "add_one": "A thin silver chain over the black tee."},
        },
        "he": {
            "status": "ok", "score": 8, "intent_match": 86, "tip_kind": "change",
            "headline": "הכול באפור, וזה מחזיק", "vibe": "רחוב, בקלות",
            "items": [
                item("ז'קט ג'ינס משופשף", "outerwear", "works", "הגזרה הקופסתית קובעת את הצללית."),
                item("קפוצ'ון אפור", "top", "works", "השכבה שהופכת את זה לרחוב."),
                item("ג'ינס רחב", "bottom", "works", "הנפח מאזן את הז'קט הקצר."),
                item("כובע לבן", "accessory", "neutral", "בסדר, הנקודה הבהירה היחידה למעלה."),
                item("סניקרס לבנות", "shoes", "works", "מסיביות מספיק בשביל המכנס הרחב."),
            ],
            "working": ["משפחה אחת של אפור מהז'קט ועד הג'ינס", "ז'קט קצר על ג'ינס רחב: הפרופורציות נכונות"],
            "one_tip": "שווה להחליף את הכובע הלבן בכובע בצבע פחם, והאפור רץ מהראש ועד הרגליים.",
            "breakdown": {"fit": 9, "color": 8, "accessories": 6},
            "accessories": {"verdict": "neutral", "present": ["כובע לבן"],
                            "note": "הכובע הוא האקססורי היחיד, והוא מושך את העין למעלה.",
                            "add_one": "שרשרת כסף דקה מעל הטישרט השחורה."},
        },
    },
    # tools/brand/templates/photos/look-3-pink.jpg
    "pink": {
        "en": {
            "status": "ok", "score": 8, "intent_match": 78, "tip_kind": "change",
            "headline": "Full pink, full commitment", "vibe": "sharp and joyful",
            "items": [
                item("Pink blazer", "outerwear", "works", "Tailored, and the colour is sure of itself."),
                item("Pink trousers", "bottom", "works", "The jacket's own cloth: one block of colour."),
                item("Striped shirt", "top", "works", "The stripes break the pink just enough."),
                item("Striped tie", "accessory", "weak", "A second stripe fights the shirt's."),
                item("Pink fedora", "accessory", "works", "Closes the column at the top."),
                item("Pink shoes", "shoes", "works", "Glossy, and they finish it."),
            ],
            "working": ["One colour head to toe, worn with total confidence", "The tailoring keeps the pink sharp, not costume"],
            "one_tip": "Trade the striped tie for a solid pink one and let the shirt keep the stripes.",
            "breakdown": {"fit": 8, "color": 9, "accessories": 7},
            "accessories": {"verdict": "adds", "present": ["Pink fedora", "Watch", "Ring"],
                            "note": "The fedora, the watch and the ring keep it all in one key.", "add_one": ""},
        },
        "he": {
            "status": "ok", "score": 8, "intent_match": 78, "tip_kind": "change",
            "headline": "ורוד מלא, מחויבות מלאה", "vibe": "חד ושמח",
            "items": [
                item("בלייזר ורוד", "outerwear", "works", "מחויט, והצבע בטוח בעצמו."),
                item("מכנסיים ורודים", "bottom", "works", "אותו בד כמו הז'קט: גוש צבע אחד."),
                item("חולצה מפוספסת", "top", "works", "הפסים שוברים את הוורוד בדיוק במידה."),
                item("עניבה מפוספסת", "accessory", "weak", "פס שני שנלחם בפסים של החולצה."),
                item("פדורה ורודה", "accessory", "works", "סוגרת את הקו למעלה."),
                item("נעליים ורודות", "shoes", "works", "מבריקות, והן סוגרות את הלוק."),
            ],
            "working": ["צבע אחד מכף רגל ועד ראש, עם ביטחון מלא", "הגזרה המחויטת שומרת על הוורוד חד ולא תחפושת"],
            "one_tip": "שווה להחליף את העניבה המפוספסת בעניבה ורודה חלקה, ולהשאיר את הפסים לחולצה.",
            "breakdown": {"fit": 8, "color": 9, "accessories": 7},
            "accessories": {"verdict": "adds", "present": ["פדורה ורודה", "שעון", "טבעת"],
                            "note": "הפדורה, השעון והטבעת שומרים על הכול באותו טון.", "add_one": ""},
        },
    },
}


class Handler(stub.Handler):
    def do_GET(self):
        if self.path.startswith("/__look/"):
            name = self.path[len("/__look/"):].split("?")[0]
            if name not in LOOKS:
                return self._fail(404, "no look " + name)
            # the stub reads EN / HE as module globals when it answers, so swapping them is the whole hook
            stub.EN = LOOKS[name]["en"]
            stub.HE = LOOKS[name]["he"]
            sys.stderr.write("KIT LOOK %s\n" % name)
            return self._json(200, {"look": name})
        return super().do_GET()


if __name__ == "__main__":
    sys.stderr.write("kit stub listening on %d\n" % stub.PORT)
    HTTPServer(("127.0.0.1", stub.PORT), Handler).serve_forever()

"""Stub of the Anthropic Messages API for end-to-end runs without a real key.

Validates the request shape OREVOSH sends (headers, forced tool call, base64 image block) and answers
with a tool_use block in the requested language (English, Hebrew, Arabic or Russian). The very first request answers 529 so the client's
single retry is exercised too. Anything malformed gets a 400 with the reason, so mistakes are loud.

Two tools are understood: submit_outfit_feedback (a check: one image block, then the text) and pick_outfit
(a "which one?" comparison: the label "Outfit A:", the first image, the label "Outfit B:", the second image,
then the text). A comparison is answered with B winning, 6 to 8, in the requested language.
"""
import base64
import json
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 5099
REQUESTS = []

EN = {
    "status": "ok", "score": 7, "intent_match": 72,
    "headline": "Clean casual with one weak link",
    "vibe": "relaxed weekend",
    # rubric v3: brand_seen is null unless a mark is visible; the swoosh on the running shoes is the one the e2e asserts
    "items": [
        {"name": "White tee", "category": "top", "verdict": "works", "note": "Crisp and simple.", "brand_seen": None},
        {"name": "Dark jeans", "category": "bottom", "verdict": "neutral", "note": "Fine, does the job.", "brand_seen": None},
        {"name": "Running shoes", "category": "shoes", "verdict": "weak", "note": "Too sporty for the rest.", "brand_seen": "Nike"},
    ],
    "working": ["The palette is tight", "Proportions are balanced"],
    "one_tip": "Swap the running shoes for plain white leather sneakers.",
    # rubric v2: the three sub-scores and the accessories read
    "breakdown": {"fit": 7, "color": 8, "accessories": 4},
    "accessories": {
        "verdict": "missing", "present": [],
        "note": "Nothing on, so the look stops at the clothes and never quite finishes.",
        "add_one": "A thin black leather belt.",
    },
}

HE = {
    "status": "ok", "score": 6, "intent_match": 58,
    "headline": "קז'ואל נקי עם חוליה חלשה אחת",
    "vibe": "סופ\"ש רגוע",
    "items": [
        {"name": "טישרט לבנה", "category": "top", "verdict": "works", "note": "נקייה ופשוטה.", "brand_seen": None},
        {"name": "ג'ינס כהה", "category": "bottom", "verdict": "neutral", "note": "בסדר, עושה את העבודה.", "brand_seen": None},
        {"name": "נעלי ריצה", "category": "shoes", "verdict": "weak", "note": "ספורטיביות מדי לשאר הלוק.", "brand_seen": None},
    ],
    "working": ["הפלטה מצומצמת", "הפרופורציות מאוזנות"],
    "one_tip": "שווה להחליף את נעלי הריצה בסניקרס עור לבן פשוט.",
    "breakdown": {"fit": 7, "color": 8, "accessories": 4},
    "accessories": {
        "verdict": "missing", "present": [],
        "note": "בלי אקססוריז הלוק נעצר בבגדים ולא ממש נסגר.",
        "add_one": "חגורת עור שחורה דקה.",
    },
}

AR = {
    "status": "ok", "score": 6, "intent_match": 58,
    "headline": "كاجوال نظيف بحلقة ضعيفة واحدة",
    "vibe": "عطلة هادئة",
    "items": [
        {"name": "تيشيرت أبيض", "category": "top", "verdict": "works", "note": "نظيف وبسيط.", "brand_seen": None},
        {"name": "جينز داكن", "category": "bottom", "verdict": "neutral", "note": "لا بأس، يؤدي الغرض.", "brand_seen": None},
        {"name": "حذاء ركض", "category": "shoes", "verdict": "weak", "note": "رياضي أكثر من اللازم لبقية الإطلالة.", "brand_seen": None},
    ],
    "working": ["الألوان منسجمة", "النسب متوازنة"],
    "one_tip": "الأفضل تبديل حذاء الركض بسنيكرز جلد أبيض بسيط.",
    "breakdown": {"fit": 7, "color": 8, "accessories": 4},
    "accessories": {
        "verdict": "missing", "present": [],
        "note": "من دون إكسسوارات تتوقف الإطلالة عند الملابس ولا تكتمل.",
        "add_one": "حزام جلد أسود رفيع.",
    },
}

RU = {
    "status": "ok", "score": 6, "intent_match": 58,
    "headline": "Чистый кэжуал с одним слабым звеном",
    "vibe": "спокойные выходные",
    "items": [
        {"name": "Белая футболка", "category": "top", "verdict": "works", "note": "Чисто и просто.", "brand_seen": None},
        {"name": "Тёмные джинсы", "category": "bottom", "verdict": "neutral", "note": "Нормально, своё дело делают.", "brand_seen": None},
        {"name": "Беговые кроссовки", "category": "shoes", "verdict": "weak", "note": "Слишком спортивные для остального.", "brand_seen": None},
    ],
    "working": ["Палитра собранная", "Пропорции сбалансированы"],
    "one_tip": "Стоит заменить беговые кроссовки на простые белые кожаные кеды.",
    "breakdown": {"fit": 7, "color": 8, "accessories": 4},
    "accessories": {
        "verdict": "missing", "present": [],
        "note": "Без аксессуаров образ останавливается на одежде и не дотягивает до конца.",
        "add_one": "Тонкий чёрный кожаный ремень.",
    },
}

NOT_OUTFIT_EN = {
    "status": "not_outfit", "score": 1, "intent_match": 0, "headline": "", "vibe": "",
    "items": [], "working": [], "one_tip": "",
    "message": "This looks like a photo of a wall. Try one where the clothes are visible.",
}

# "Which one?": B wins, 6 to 8, with the reason and the tip.
COMPARE_EN = {
    "status": "ok", "winner": "b", "score_a": 6, "score_b": 8,
    "headline_a": "Safe casual, a little flat",
    "headline_b": "Sharper lines, clearer intent",
    "reason": "Outfit B reads as the intent from across the room: the cropped jacket and the straight trousers give it a line, and the loafers finish it. Outfit A is fine, but the running shoes and the loose tee pull it toward the gym. B wins on coherence.",
    "one_tip": "For Outfit A, swap the running shoes for plain white leather sneakers and tuck the tee.",
}

COMPARE_HE = {
    "status": "ok", "winner": "b", "score_a": 6, "score_b": 8,
    "headline_a": "קז'ואל בטוח, קצת שטוח",
    "headline_b": "קווים חדים, כוונה ברורה",
    "reason": "לוק B נקרא כמו הכוונה כבר מרחוק: הז'קט הקצר והמכנסיים הישרים נותנים לו קו, והלואפרים סוגרים אותו. לוק A בסדר, אבל נעלי הריצה והטישרט הרפויה מושכות אותו לכיוון חדר הכושר. B מנצח על קוהרנטיות.",
    "one_tip": "בלוק A שווה להחליף את נעלי הריצה בסניקרס עור לבן פשוט ולהכניס את הטישרט.",
}

COMPARE_AR = {
    "status": "ok", "winner": "b", "score_a": 6, "score_b": 8,
    "headline_a": "كاجوال آمن، مسطّح قليلًا",
    "headline_b": "خطوط أوضح، وجهة أوضح",
    "reason": "الإطلالة B تُقرأ كالوجهة من بعيد: الجاكيت القصير والبنطال المستقيم يمنحانها خطًا، واللوفرز تكملها. الإطلالة A لا بأس بها، لكن حذاء الركض والتيشيرت الفضفاض يسحبانها نحو النادي الرياضي. B تفوز بالانسجام.",
    "one_tip": "في الإطلالة A، الأفضل تبديل حذاء الركض بسنيكرز جلد أبيض بسيط وإدخال التيشيرت في البنطال.",
}

COMPARE_RU = {
    "status": "ok", "winner": "b", "score_a": 6, "score_b": 8,
    "headline_a": "Безопасный кэжуал, чуть плоский",
    "headline_b": "Чётче линии, яснее направление",
    "reason": "Образ B читается как направление издалека: укороченная куртка и прямые брюки дают ему линию, а лоферы завершают. Образ A нормальный, но беговые кроссовки и свободная футболка тянут его в сторону спортзала. B выигрывает за цельность.",
    "one_tip": "В образе A стоит заменить беговые кроссовки на простые белые кожаные кеды и заправить футболку.",
}

COMPARE_NOT_OUTFIT_EN = {
    "status": "not_outfit", "winner": "a", "score_a": 1, "score_b": 1,
    "headline_a": "", "headline_b": "", "reason": "", "one_tip": "",
    "message": "Photo A looks like a wall. Try one where the clothes are visible.",
}

IMAGE_TYPES = ("image/jpeg", "image/png", "image/webp")


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):  # quiet
        pass

    def _fail(self, status, reason):
        body = json.dumps({"type": "error", "error": {"type": "invalid_request_error", "message": reason}}).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    @staticmethod
    def _image(block, problems, label):
        """Decodes one image block, noting what is wrong with it. Returns (bytes, media_type)."""
        if block.get("type") != "image":
            problems.append("%s is not an image block" % label)
            return b"", ""
        src = block.get("source", {})
        media_type = src.get("media_type", "")
        if src.get("type") != "base64" or media_type not in IMAGE_TYPES:
            problems.append("%s source malformed" % label)
        try:
            image_bytes = base64.b64decode(src.get("data", ""))
        except Exception:  # noqa: BLE001
            problems.append("%s not base64" % label)
            image_bytes = b""
        if media_type == "image/jpeg" and image_bytes[:3] != b"\xff\xd8\xff":
            problems.append("%s media_type says jpeg but bytes are not" % label)
        return image_bytes, media_type

    def do_POST(self):
        if self.path != "/v1/messages":
            return self._fail(404, "unknown path " + self.path)
        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length)
        try:
            body = json.loads(raw)
        except Exception as e:  # noqa: BLE001
            return self._fail(400, "bad json: %s" % e)

        problems = []
        if not self.headers.get("x-api-key"):
            problems.append("missing x-api-key")
        if self.headers.get("anthropic-version") != "2023-06-01":
            problems.append("bad anthropic-version")
        if "model" not in body or "max_tokens" not in body or "system" not in body:
            problems.append("missing model/max_tokens/system")
        tools = body.get("tools") or []
        tool_name = tools[0].get("name") if len(tools) == 1 else None
        compare = tool_name == "pick_outfit"
        if len(tools) != 1 or "input_schema" not in tools[0] or tool_name not in ("submit_outfit_feedback", "pick_outfit"):
            problems.append("tools malformed")
        else:
            required = (tools[0].get("input_schema") or {}).get("required") or []
            if compare:
                for field in ("status", "winner", "score_a", "score_b", "headline_a", "headline_b", "reason", "one_tip"):
                    if field not in required:
                        problems.append("pick_outfit schema lacks " + field)
            # Rubric v2: the schema must ask for the breakdown and the accessories read, or the answer below would be ignored.
            elif "breakdown" not in required or "accessories" not in required:
                problems.append("schema lacks the v2 fields (breakdown, accessories)")
            else:
                # Rubric v3: every item asks for brand_seen (string or null), or the "Nike" below would never reach the post sheet.
                item_schema = (((tools[0].get("input_schema") or {}).get("properties") or {}).get("items") or {}).get("items") or {}
                if "brand_seen" not in (item_schema.get("properties") or {}) or "brand_seen" not in (item_schema.get("required") or []):
                    problems.append("schema lacks the v3 field (items[].brand_seen)")
        tc = body.get("tool_choice") or {}
        if tc.get("type") != "tool" or tc.get("name") != tool_name:
            problems.append("tool_choice not forced")
        msgs = body.get("messages") or []
        if len(msgs) != 1 or msgs[0].get("role") != "user":
            problems.append("messages malformed")
        content = msgs[0].get("content", []) if msgs else []
        image_bytes = b""
        media_type = ""
        image_bytes_b = b""
        media_type_b = ""
        user_text = ""
        if compare:
            # A comparison: "Outfit A:", image A, "Outfit B:", image B, the task text.
            if len(content) != 5:
                problems.append("comparison content must be 5 blocks, got %d" % len(content))
            else:
                if content[0].get("type") != "text" or content[0].get("text") != "Outfit A:":
                    problems.append("block 0 must be the text 'Outfit A:'")
                if content[2].get("type") != "text" or content[2].get("text") != "Outfit B:":
                    problems.append("block 2 must be the text 'Outfit B:'")
                if content[4].get("type") != "text":
                    problems.append("block 4 must be the task text")
                image_bytes, media_type = self._image(content[1], problems, "image A")
                image_bytes_b, media_type_b = self._image(content[3], problems, "image B")
                user_text = content[4].get("text", "")
        else:
            if len(content) != 2 or content[0].get("type") != "image" or content[1].get("type") != "text":
                problems.append("content blocks malformed")
            if content and content[0].get("type") == "image":
                image_bytes, media_type = self._image(content[0], problems, "image")
            user_text = content[1].get("text", "") if len(content) > 1 else ""
        if problems:
            sys.stderr.write("STUB REJECTED: %s\n" % problems)
            return self._fail(400, "; ".join(problems))

        record = {
            "model": body["model"], "max_tokens": body["max_tokens"], "thinking": body.get("thinking"), "tool": tool_name,
            "system_head": body["system"][:80], "language_line": [l for l in body["system"].splitlines() if l.startswith("Write every")],
            "user_text": user_text, "media_type": media_type, "image_len": len(image_bytes),
        }
        if compare:
            record["media_type_b"] = media_type_b
            record["image_len_b"] = len(image_bytes_b)
        REQUESTS.append(record)
        sys.stderr.write("STUB REQUEST #%d (%s): %s bytes %s%s | %s\n" % (
            len(REQUESTS), tool_name, len(image_bytes), media_type,
            (" + %s bytes %s" % (len(image_bytes_b), media_type_b)) if compare else "", user_text[:70]))

        # First request: simulate an overloaded API so the single retry gets exercised.
        if len(REQUESTS) == 1:
            return self._fail(529, "Overloaded")

        # The system prompt names the language ("Write every user-facing field ... in Hebrew (he)"); answer in it.
        system = body["system"]
        if "in Hebrew (he)" in system:
            answers, compare_answers = HE, COMPARE_HE
        elif "in Arabic (ar)" in system:
            answers, compare_answers = AR, COMPARE_AR
        elif "in Russian (ru)" in system:
            answers, compare_answers = RU, COMPARE_RU
        else:
            answers, compare_answers = EN, COMPARE_EN
        if compare:
            payload = compare_answers
            # A tiny image (a few KB) on either side stands in for a "not an outfit" photo.
            if len(image_bytes) < 3000 or len(image_bytes_b) < 3000:
                payload = COMPARE_NOT_OUTFIT_EN
        else:
            payload = answers
            if len(image_bytes) < 3000:
                payload = NOT_OUTFIT_EN
        response = {
            "id": "msg_stub", "type": "message", "role": "assistant", "model": body["model"],
            "stop_reason": "tool_use", "stop_sequence": None,
            "content": [{"type": "tool_use", "id": "toolu_stub", "name": tool_name, "input": payload}],
            "usage": {"input_tokens": 1000, "output_tokens": 300},
        }
        out = json.dumps(response, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(out)))
        self.end_headers()
        self.wfile.write(out)

    def do_GET(self):
        out = json.dumps(REQUESTS, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(out)))
        self.end_headers()
        self.wfile.write(out)


if __name__ == "__main__":
    sys.stderr.write("stub anthropic listening on %d\n" % PORT)
    HTTPServer(("127.0.0.1", PORT), Handler).serve_forever()

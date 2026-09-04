"""Stub of the Anthropic Messages API for end-to-end runs without a real key.

Validates the request shape OREVOSH sends (headers, forced tool call, base64 image block) and answers
with a tool_use block in the requested language. The very first request answers 529 so the client's
single retry is exercised too. Anything malformed gets a 400 with the reason, so mistakes are loud.
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
    "items": [
        {"name": "White tee", "category": "top", "verdict": "works", "note": "Crisp and simple."},
        {"name": "Dark jeans", "category": "bottom", "verdict": "neutral", "note": "Fine, does the job."},
        {"name": "Running shoes", "category": "shoes", "verdict": "weak", "note": "Too sporty for the rest."},
    ],
    "working": ["The palette is tight", "Proportions are balanced"],
    "one_tip": "Swap the running shoes for plain white leather sneakers.",
}

HE = {
    "status": "ok", "score": 6, "intent_match": 58,
    "headline": "קז'ואל נקי עם חוליה חלשה אחת",
    "vibe": "סופ\"ש רגוע",
    "items": [
        {"name": "טישרט לבנה", "category": "top", "verdict": "works", "note": "נקייה ופשוטה."},
        {"name": "ג'ינס כהה", "category": "bottom", "verdict": "neutral", "note": "בסדר, עושה את העבודה."},
        {"name": "נעלי ריצה", "category": "shoes", "verdict": "weak", "note": "ספורטיביות מדי לשאר הלוק."},
    ],
    "working": ["הפלטה מצומצמת", "הפרופורציות מאוזנות"],
    "one_tip": "שווה להחליף את נעלי הריצה בסניקרס עור לבן פשוט.",
}

NOT_OUTFIT_EN = {
    "status": "not_outfit", "score": 1, "intent_match": 0, "headline": "", "vibe": "",
    "items": [], "working": [], "one_tip": "",
    "message": "This looks like a photo of a wall. Try one where the clothes are visible.",
}


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
        if len(tools) != 1 or "input_schema" not in tools[0] or tools[0].get("name") != "submit_outfit_feedback":
            problems.append("tools malformed")
        tc = body.get("tool_choice") or {}
        if tc.get("type") != "tool" or tc.get("name") != "submit_outfit_feedback":
            problems.append("tool_choice not forced")
        msgs = body.get("messages") or []
        if len(msgs) != 1 or msgs[0].get("role") != "user":
            problems.append("messages malformed")
        content = msgs[0].get("content", []) if msgs else []
        if len(content) != 2 or content[0].get("type") != "image" or content[1].get("type") != "text":
            problems.append("content blocks malformed")
        image_bytes = b""
        media_type = ""
        if content and content[0].get("type") == "image":
            src = content[0].get("source", {})
            media_type = src.get("media_type", "")
            if src.get("type") != "base64" or media_type not in ("image/jpeg", "image/png", "image/webp"):
                problems.append("image source malformed")
            try:
                image_bytes = base64.b64decode(src.get("data", ""))
            except Exception:  # noqa: BLE001
                problems.append("image not base64")
            if media_type == "image/jpeg" and image_bytes[:3] != b"\xff\xd8\xff":
                problems.append("media_type says jpeg but bytes are not")
        if problems:
            sys.stderr.write("STUB REJECTED: %s\n" % problems)
            return self._fail(400, "; ".join(problems))

        REQUESTS.append({
            "model": body["model"], "max_tokens": body["max_tokens"], "thinking": body.get("thinking"),
            "system_head": body["system"][:80], "language_line": [l for l in body["system"].splitlines() if l.startswith("Write every")],
            "user_text": content[1]["text"], "media_type": media_type, "image_len": len(image_bytes),
        })
        sys.stderr.write("STUB REQUEST #%d: %s bytes %s | %s\n" % (len(REQUESTS), len(image_bytes), media_type, content[1]["text"][:70]))

        # First request: simulate an overloaded API so the single retry gets exercised.
        if len(REQUESTS) == 1:
            return self._fail(529, "Overloaded")

        hebrew = "in Hebrew (he)" in body["system"]
        payload = HE if hebrew else EN
        # A tiny image (a few KB) stands in for a "not an outfit" photo.
        if len(image_bytes) < 3000:
            payload = NOT_OUTFIT_EN
        response = {
            "id": "msg_stub", "type": "message", "role": "assistant", "model": body["model"],
            "stop_reason": "tool_use", "stop_sequence": None,
            "content": [{"type": "tool_use", "id": "toolu_stub", "name": "submit_outfit_feedback", "input": payload}],
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

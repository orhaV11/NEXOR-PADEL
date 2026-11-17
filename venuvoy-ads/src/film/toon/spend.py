"""The spending ledger of one film: every paid call is written down, and the film has a cap.

    from spend import Ledger
    led = Ledger("work/switch/ledger.jsonl", cap=5.0)
    with led.paid("three readings of the dialogue"):
        ... calls that cost money ...

The balance is read before and after; the difference is logged with its purpose. Entering a paid block when the film
has already used its cap raises, so the question goes to the founder before the money does.
"""
import json
import sys
import time
from contextlib import contextmanager
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import muapi  # noqa: E402


class Ledger:
    def __init__(self, path, cap=5.0):
        self.path, self.cap = Path(path), cap
        self.path.parent.mkdir(parents=True, exist_ok=True)

    def rows(self):
        return [json.loads(x) for x in self.path.read_text().splitlines()] if self.path.exists() else []

    def spent(self):
        return round(sum(r["cost"] for r in self.rows()), 4)

    @contextmanager
    def paid(self, purpose, expect=None):
        used = self.spent()
        if used >= self.cap or (expect and used + expect > self.cap):
            raise RuntimeError(f"cap of {self.cap} dollars: {used} used, this step needs {expect}. Ask the founder first.")
        before = float(muapi.balance()["balance"])
        try:
            yield
        finally:
            time.sleep(1.5)
            after = float(muapi.balance()["balance"])
            row = {"t": time.strftime("%Y-%m-%d %H:%M"), "purpose": purpose, "cost": round(before - after, 4), "balance": round(after, 3)}
            with self.path.open("a") as f:
                f.write(json.dumps(row) + "\n")
            print(f"  spent {row['cost']:.3f} on {purpose}; film total {self.spent():.2f} of {self.cap:.2f}; balance {after:.2f}", flush=True)


if __name__ == "__main__":
    led = Ledger(sys.argv[1])
    for r in led.rows():
        print(f'{r["t"]}  {r["cost"]:7.3f}  {r["purpose"]}')
    print(f"total {led.spent():.2f}")

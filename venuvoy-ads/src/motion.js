// Motion layer for the static ad pages. Inert until seek(t) is called, so the PNG renders keep the
// final layout. Elements opt in with data-in="start[,type[,duration]]" (seconds); a page can add
// its own per-frame effects with window.onSeek = (t) => {...}.
(function () {
  const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
  const P = (t, s, d) => clamp((t - s) / d);
  const lerp = (a, b, p) => a + (b - a) * p;
  const E = {
    out: (p) => 1 - Math.pow(1 - p, 3),
    out5: (p) => 1 - Math.pow(1 - p, 5),
    inOut: (p) => (p < 0.5 ? 4 * p * p * p : 1 - Math.pow(-2 * p + 2, 3) / 2),
    back: (p) => { const c1 = 1.7, c3 = c1 + 1; return 1 + c3 * Math.pow(p - 1, 3) + c1 * Math.pow(p - 1, 2); },
  };
  const DEF = { fade: 0.5, up: 0.55, slam: 0.32, pop: 0.38, row: 0.4, stamp: 0.3, rise: 0.9, typed: 1.2, left: 0.5 };
  const SOUND = { slam: "slam", pop: "pop", row: "tick", stamp: "stamp", rise: "whoosh", typed: "typing" };

  let items = null;
  let gilts = [];
  let ctas = [];
  function init() {
    window.DURATION = +(document.body.dataset.duration || 8);
    items = [...document.querySelectorAll("[data-in]")]
      .filter((el) => el.offsetParent !== null || getComputedStyle(el).position === "fixed")
      .map((el) => {
        const [t, type = "up", dur] = el.dataset.in.split(",");
        const cs = getComputedStyle(el);
        const it = { el, t: +t, type, dur: dur ? +dur : DEF[type] || 0.5, base: cs.transform === "none" ? "" : cs.transform, op: +cs.opacity };
        if (type === "typed") it.text = el.textContent;
        return it;
      });
    gilts = [...document.querySelectorAll(".gilt")].filter((el) => el.offsetParent !== null);
    gilts.forEach((g) => { g.style.backgroundSize = "260% 100%"; });
    ctas = [...document.querySelectorAll(".cta")].filter((el) => el.offsetParent !== null).map((el) => ({ el, base: getComputedStyle(el).transform }));
    const frame = document.querySelector(".frame");
    window.CUES = items.filter((i) => SOUND[i.type]).map((i) => ({ t: i.t, type: SOUND[i.type], dur: i.dur }));
    if (frame) window.CUES.push({ t: 0, type: "open", dur: 0.7 });
  }

  function apply(it, t) {
    const p = P(t, it.t, it.dur);
    let op = it.op, tr = "";
    switch (it.type) {
      case "fade": op *= E.out(p); break;
      case "up": { const q = E.out(p); op *= q; tr = `translateY(${(1 - q) * 46}px)`; break; }
      case "left": { const q = E.out(p); op *= q; tr = `translateX(${(1 - q) * -60}px)`; break; }
      case "row": { const q = E.out(p); op *= q; tr = `translateX(${(1 - q) * 70}px)`; break; }
      case "rise": { const q = E.out(p); op *= clamp(p * 4); tr = `translateY(${(1 - q) * 900}px)`; break; }
      case "slam": { const q = E.out5(p); op *= clamp(p * 2.5); tr = `scale(${lerp(1.55, 1, q)})`; it.el.style.filter = q < 0.98 ? `blur(${(1 - q) * 9}px)` : ""; break; }
      case "pop": { const q = p <= 0 ? 0 : E.back(p); op *= clamp(p * 3); tr = `scale(${Math.max(0, q)})`; break; }
      case "stamp": { const q = E.out5(p); op *= clamp(p * 3); tr = `scale(${lerp(2.2, 1, q)})`; break; }
      case "typed": { op = it.op; it.el.textContent = it.text.slice(0, Math.round(it.text.length * p)); break; }
    }
    it.el.style.opacity = op;
    if (it.type !== "typed") it.el.style.transform = `${tr} ${it.base}`.trim() || "none";
  }

  window.DURATION = 8;
  window.seek = function (t) {
    if (!items) init();
    for (const it of items) apply(it, t);
    // the brass double rule draws out from the centre
    const frame = document.querySelector(".frame");
    if (frame) {
      const f = 50 * (1 - E.inOut(P(t, 0, 0.7)));
      frame.style.clipPath = `inset(${f}% ${f}% ${f}% ${f}%)`;
    }
    document.querySelectorAll(".seal").forEach((s) => { s.style.opacity = E.out(P(t, 0.55, 0.3)); });
    // gilt shimmer, twice
    const sw = (a) => { const p = P(t, a, 1.1); return p > 0 && p < 1 ? lerp(120, -20, E.inOut(p)) : null; };
    const pos = sw(1.6) ?? sw(5.6);
    gilts.forEach((g) => { g.style.backgroundPosition = `${pos ?? 50}% 0`; });
    // the call to action breathes once near the end
    const pulse = 1 + 0.06 * Math.sin(Math.PI * P(t, 6.3, 0.5));
    ctas.forEach(({ el }) => { if (P(t, 6.3, 0.5) > 0 && P(t, 6.3, 0.5) < 1) el.style.transform = `scale(${pulse})`; });
    if (window.onSeek) window.onSeek(t, { P, E, lerp, clamp });
  };
})();

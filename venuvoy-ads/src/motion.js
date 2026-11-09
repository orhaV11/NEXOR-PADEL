// Motion layer for the static ad pages. Inert until seek(t) is called, so the PNG renders keep the
// final layout. Elements opt in with data-in="start[,type[,duration]]" (seconds; a negative start means
// the element is already arriving on frame 0); a page can add its own per-frame effects with
// window.onSeek = (t) => {...}, extra sound cues with window.MORE_CUES, and declare its score with
// <body data-music="pulse,lift,groove*2" data-outro="6"> (see src/audio/compose.py).
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
    ctas = [...document.querySelectorAll(".cta")].filter((el) => el.offsetParent !== null).map((el) => {
      const it = items.find((i) => i.el === el);
      return { el, base: getComputedStyle(el).transform, landed: it ? it.t + it.dur : 0, arrow: el.querySelector(".i-arrow") };
    });
    window.CUES = items.filter((i) => SOUND[i.type] && i.t >= 0).map((i) => ({ t: i.t, type: SOUND[i.type], dur: i.dur }));
    window.CUES.push(...(window.MORE_CUES || []));
    const b = document.body.dataset;
    window.MUSIC = { form: b.music || "hook,lift,groove*2", outro: b.outro != null ? +b.outro : 6 };
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
    // the brass double rule finishes drawing out from the centre in the first few frames (frame 0 is already framed)
    const frame = document.querySelector(".frame");
    if (frame) {
      const f = 50 * (1 - E.inOut(P(t, -0.4, 0.6)));
      frame.style.clipPath = `inset(${f}% ${f}% ${f}% ${f}%)`;
    }
    document.querySelectorAll(".seal").forEach((s) => { s.style.opacity = E.out(P(t, -0.25, 0.3)); });
    // gilt shimmer, twice
    const sw = (a) => { const p = P(t, a, 1.1); return p > 0 && p < 1 ? lerp(120, -20, E.inOut(p)) : null; };
    const pos = sw(1.6) ?? sw(5.6);
    gilts.forEach((g) => { g.style.backgroundPosition = `${pos ?? 50}% 0`; });
    // the call to action pulses once near the end, and from the moment it lands it keeps breathing while its
    // arrow nudges toward the tap, so the last seconds never sit frozen and the eye stays on the button
    const pulse = 1 + 0.06 * Math.sin(Math.PI * P(t, 6.3, 0.5));
    const rtl = document.documentElement.dir === "rtl";
    ctas.forEach(({ el, landed, arrow }) => {
      if (P(t, 6.3, 0.5) > 0 && P(t, 6.3, 0.5) < 1) el.style.transform = `scale(${pulse})`;
      const k = t - landed;
      el.style.scale = k > 0 ? String(1 + 0.02 * (1 - Math.cos((2 * Math.PI * k) / 1.5)) / 2) : "";
      if (arrow) arrow.style.translate = k > 0 ? `${(rtl ? -1 : 1) * 6 * (1 - Math.cos((2 * Math.PI * k) / 0.75)) / 2}px 0` : "";
    });
    if (window.onSeek) window.onSeek(t, { P, E, lerp, clamp });
  };
})();

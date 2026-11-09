// Shared runtime for the campaign spots (anthem, chaos, sports, bumper): language and direction,
// timing helpers, kinetic type, camera shake, the WebGL renderer with bloom, and font readiness.
// A page fills window.DURATION, window.CUES (sound effects on its own timeline) and window.SCORE
// ({ script: "anthem.py", args: [] }) and defines window.seek(t). Render with src/render-spot.mjs.
(function () {
  const qs = new URLSearchParams(location.search);
  const LANG = qs.get("lang") === "he" ? "he" : "en";
  const RTL = LANG === "he";
  document.documentElement.lang = LANG;
  document.documentElement.dir = RTL ? "rtl" : "ltr";

  const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
  const P = (t, s, d) => clamp((t - s) / d);
  const lerp = (a, b, p) => a + (b - a) * p;
  const E = {
    out: (p) => 1 - Math.pow(1 - p, 3),
    out5: (p) => 1 - Math.pow(1 - p, 5),
    expo: (p) => (p >= 1 ? 1 : 1 - Math.pow(2, -10 * p)),
    inOut: (p) => (p < 0.5 ? 4 * p * p * p : 1 - Math.pow(-2 * p + 2, 3) / 2),
    in: (p) => p * p * p,
    back: (p) => { const c1 = 1.7, c3 = c1 + 1; return 1 + c3 * Math.pow(p - 1, 3) + c1 * Math.pow(p - 1, 2); },
    elastic: (p) => (p === 0 || p === 1 ? p : Math.pow(2, -10 * p) * Math.sin((p * 10 - 0.75) * ((2 * Math.PI) / 3)) + 1),
  };
  function put(el, o = {}) {
    if (!el) return;
    const { op = 1, x = 0, y = 0, s = 1, sx = 1, r = 0, blur = 0 } = o;
    el.style.opacity = op;
    el.style.transform = `translate(${x}px, ${y}px) scale(${s * sx}, ${s}) rotate(${r}deg)`;
    el.style.filter = blur > 0.05 ? `blur(${blur}px)` : "";
  }
  const pop = (t, s, d = 0.32) => { const p = P(t, s, d); return { op: clamp(p * 3), s: p <= 0 ? 0 : E.back(p) }; };
  const slam = (t, s, d = 0.28, from = 1.7) => { const p = E.out5(P(t, s, d)); return { op: clamp(p * 2), s: lerp(from, 1, p), blur: (1 - p) * 10 }; };
  function rnd(seed) { let s = seed >>> 0; return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296); }

  /** Impact shake: decays fast after each hit time. */
  function shake(t, hits, amp = 14, dur = 0.4) {
    let x = 0, y = 0, r = 0;
    for (const h of hits) {
      const k = t - h;
      if (k < 0 || k > dur) continue;
      const d = Math.exp(-k * 9);
      x += Math.sin(k * 93) * amp * d; y += Math.cos(k * 71) * amp * 0.7 * d; r += Math.sin(k * 57) * 0.5 * d;
    }
    return { x, y, r };
  }

  /** Split an element's text into word spans once (keeps Hebrew shaping intact). */
  function words(el) {
    if (!el._w) {
      const parts = el.textContent.split(/(\s+)/);
      el.textContent = "";
      el._w = [];
      for (const p of parts) {
        if (/^\s+$/.test(p)) { el.appendChild(document.createTextNode(p)); continue; }
        if (!p) continue;
        const s = document.createElement("span"); s.className = "kw"; s.textContent = p; s.style.display = "inline-block";
        el.appendChild(s); el._w.push(s);
      }
    }
    return el._w;
  }
  /** Words rise into place one after another. */
  function kinetic(el, t, start, o = {}) {
    const { stagger = 0.07, dur = 0.42, rise = 70, blur = 10, s0 = 1 } = o;
    words(el).forEach((w, i) => {
      const p = E.out5(P(t, start + i * stagger, dur));
      w.style.opacity = clamp(p * 1.6);
      w.style.transform = `translateY(${(1 - p) * rise}px) scale(${lerp(s0, 1, p)})`;
      w.style.filter = p < 0.99 && blur ? `blur(${(1 - p) * blur}px)` : "";
    });
  }

  /** One WebGL renderer and bloom composer for the page's 3D scenes. */
  function three(canvas, o = {}) {
    const aa = new URLSearchParams(location.search).get("aa") !== "0";
    const renderer = new THREE.WebGLRenderer({ canvas, antialias: aa, preserveDrawingBuffer: true });
    renderer.setPixelRatio(1);
    renderer.setSize(canvas.width, canvas.height, false);
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = o.exposure ?? 0.95;
    const { EffectComposer, RenderPass, UnrealBloomPass, OutputPass } = THREE_POST;
    const composer = new EffectComposer(renderer);
    const pass = new RenderPass(new THREE.Scene(), new THREE.PerspectiveCamera());
    composer.addPass(pass);
    const [st, rad, th] = o.bloom || [0.75, 0.45, 0.9];
    const bq = +(new URLSearchParams(location.search).get("bq") || 2);
    const bloom = new UnrealBloomPass(new THREE.Vector2(canvas.width / bq, canvas.height / bq), st, rad, th);
    composer.addPass(bloom);
    composer.addPass(new OutputPass());
    return {
      renderer, composer, bloom,
      render(scene, camera) { pass.scene = scene; pass.camera = camera; composer.render(); },
    };
  }

  /** Copy for this language; fills [data-t] elements (data-html for markup). */
  function txt(dict) {
    const v = dict[LANG];
    document.querySelectorAll("[data-t]").forEach((el) => {
      const s = v[el.dataset.t];
      if (s === undefined) return;
      if (el.dataset.html) el.innerHTML = s; else el.textContent = s;
    });
    return v;
  }

  /** Every face the page draws, loaded before the first frame. */
  function fontsReady(extra = []) {
    const specs = ['italic 900 40px "Playfair"', '800 40px "Playfair"', '500 40px "Playfair"', '800 40px "Assistant"', '700 40px "Assistant"', '900 40px "FRL"', '800 40px "FRL"', ...extra];
    return Promise.all(specs.map((s) => document.fonts.load(s, "Abc אבג 0123 ₪$"))).then(() => {
      const missing = specs.filter((s) => !document.fonts.check(s, "Abc אבג"));
      if (missing.length) throw new Error("fonts not loaded: " + missing.join(", "));
    });
  }

  window.SPOT = { LANG, RTL, DIRX: RTL ? -1 : 1, clamp, P, lerp, E, put, pop, slam, rnd, shake, words, kinetic, three, txt, fontsReady };
})();

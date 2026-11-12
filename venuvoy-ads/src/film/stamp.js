// The series' rubber stamp ("TO BE CONFIRMED"), shared by overlay.html, chat.html and board.html so that it is the
// same object wherever it lands: on footage, on a chat screen, on a list.
//
//   <script src="stamp.js"></script>
//   const el = Stamp.make("TO BE\nCONFIRMED", { small: "EPISODE 1", size: 104, rot: -8, color: "red" });
//                                           // "\n" breaks the line; color: "red" (on paper), "bright" (on footage), "green"
//   parent.appendChild(el);                 // position it yourself: el.style.left / top are the stamp's CENTRE
//   Stamp.pose(el, u);                      // u = seconds since the stamp hits (negative: not there yet)
//   const [dx, dy] = Stamp.shake(u);        // the jolt everything under it takes on the hit (pixels, dies in 0.3 s)
//
// The hit: the stamp arrives large and lands in three frames, overshoots a hair, and stays. Sound: kit.thud at u = 0.
(function () {
  const INK = { red: "#b3261e", bright: "#ea3d2c", green: "#1f7a4d", ink: "#14201a" };  // "bright": on footage; "red": on paper
  const css = `
  .stp { position: absolute; left: 0; top: 0; display: none; transform-origin: 50% 50%; pointer-events: none; white-space: pre; text-align: center;
         font-family: "Mont", "Heebo", sans-serif; font-weight: 900; line-height: 1.02; letter-spacing: 0.07em; text-transform: uppercase; filter: url(#stp-ink); }
  .stp .f { display: inline-block; padding: 0.2em 0.42em 0.24em; border: 0.085em solid currentColor; border-radius: 0.16em; outline: 0.03em solid currentColor; outline-offset: -0.2em; }
  .stp .s { display: block; margin-top: 0.3em; font-size: 0.36em; letter-spacing: 0.2em; font-weight: 800; }`;
  const svg = `<svg width="0" height="0" style="position:absolute"><defs><filter id="stp-ink" x="-12%" y="-25%" width="124%" height="150%" color-interpolation-filters="sRGB">
      <feTurbulence type="fractalNoise" baseFrequency="0.55" numOctaves="2" seed="11" result="n"/>
      <feColorMatrix in="n" type="matrix" values="0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 -3.6 3.0" result="worn"/>
      <feComposite in="SourceGraphic" in2="worn" operator="in" result="inked"/>
      <feTurbulence type="fractalNoise" baseFrequency="0.03" numOctaves="2" seed="4" result="w"/>
      <feDisplacementMap in="inked" in2="w" scale="5" xChannelSelector="R" yChannelSelector="G"/>
    </filter></defs></svg>`;
  let ready = false;
  const boot = () => {
    if (ready) return;
    ready = true;
    const st = document.createElement("style"); st.textContent = css; document.head.appendChild(st);
    const d = document.createElement("div"); d.innerHTML = svg; document.body.appendChild(d.firstChild);
  };
  const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
  window.Stamp = {
    make(text, o = {}) {
      boot();
      const el = document.createElement("div");
      el.className = "stp";
      el.style.color = INK[o.color] || o.color || INK.red;
      el.style.fontSize = `${o.size ?? 104}px`;
      el.dataset.rot = o.rot ?? -8;
      el.dataset.opacity = o.opacity ?? 0.94;
      const f = document.createElement("span"); f.className = "f"; f.textContent = text;
      if (o.small) { const s = document.createElement("span"); s.className = "s"; s.textContent = o.small; f.appendChild(s); }
      el.appendChild(f);
      return el;
    },
    pose(el, u) {
      if (u < 0) { el.style.display = "none"; return; }
      el.style.display = "block";
      const p = clamp(u / 0.085), k = 1 - Math.pow(1 - p, 2);  // lands in two or three frames
      const settle = 1 + 0.045 * Math.exp(-u / 0.07) * Math.cos(u * 46) * (u > 0.085 ? 1 : 0);
      const sc = (2.5 - 1.5 * k) * settle;
      el.style.transform = `translate(-50%, -50%) rotate(${+el.dataset.rot + (1 - k) * 5}deg) scale(${sc})`;
      el.style.opacity = +el.dataset.opacity * clamp(p * 1.6);
    },
    shake(u, amp = 16) {
      if (u < 0.06 || u > 0.36) return [0, 0];
      const d = Math.exp(-(u - 0.06) / 0.08) * amp;
      return [Math.round(d * Math.sin(u * 93)), Math.round(d * Math.cos(u * 71))];
    },
  };
})();

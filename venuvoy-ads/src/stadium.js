// A night stadium drawn in true perspective: striped pitch, markings, stands, floodlight banks
// and beams. Returns SVG markup for a W×H canvas. Everything is computed, nothing is an image.
(function () {
  const L = 105; // pitch length (m)
  const HW = 34; // half width (m)

  function rng(seed) {
    let s = seed >>> 0;
    return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
  }

  function stadium(o) {
    const {
      W,
      H,
      cx = W / 2,
      farY, // screen y of the far goal line
      nearY, // screen y of the near goal line (may be below the canvas)
      farHalf, // screen half-width at the far line
      nearHalf, // screen half-width at the near line
      banks = [], // [{x, y, s, dir: 1|-1, beam: [[xw,t],[xw,t]]}]
      lamp = 1, // 0..1 floodlight intensity
      pin = true,
      standH = 90,
      id = "st",
      seed = 11,
      haze = 1,
      pinScale = 1,
      nearFade = 0.86,
    } = o;

    const c = (nearY - farY) / (nearHalf - farHalf);
    const yH = farY - c * farHalf; // vanishing horizon
    const k = nearHalf / farHalf;
    const half = (t) => nearHalf / (k + (1 - k) * t);
    const P = (xw, t) => {
      const h = half(t);
      return [cx + xw * h, yH + c * h];
    };
    const f = (n) => n.toFixed(1);
    const pt = (p) => `${f(p[0])},${f(p[1])}`;
    const poly = (pts) => pts.map(pt).join(" ");
    const pathT = (pts) => "M" + pts.map(pt).join("L");

    let s = "";
    s += `<defs>
      <linearGradient id="${id}-sky" x1="0" y1="0" x2="0" y2="1">
        <stop offset="0" stop-color="#050f0b"/><stop offset="1" stop-color="#0c2118"/>
      </linearGradient>
      <radialGradient id="${id}-lit" cx="${f(cx)}" cy="${f(P(0, 0.55)[1])}" r="${f(nearHalf * 0.95)}" gradientUnits="userSpaceOnUse">
        <stop offset="0" stop-color="#fff3d2" stop-opacity="${0.2 * lamp}"/>
        <stop offset="0.55" stop-color="#fff3d2" stop-opacity="${0.06 * lamp}"/>
        <stop offset="1" stop-color="#fff3d2" stop-opacity="0"/>
      </radialGradient>
      <linearGradient id="${id}-fog" x1="0" y1="${f(farY)}" x2="0" y2="${f(P(0, 0.6)[1])}" gradientUnits="userSpaceOnUse">
        <stop offset="0" stop-color="#0a1c14" stop-opacity="0.72"/>
        <stop offset="1" stop-color="#0a1c14" stop-opacity="0"/>
      </linearGradient>
      <linearGradient id="${id}-near" x1="0" y1="${f(P(0, 0.75)[1])}" x2="0" y2="${f(Math.min(H, nearY))}" gradientUnits="userSpaceOnUse">
        <stop offset="0" stop-color="#06130d" stop-opacity="0"/>
        <stop offset="1" stop-color="#06130d" stop-opacity="${nearFade}"/>
      </linearGradient>
      <linearGradient id="${id}-stand" x1="0" y1="${f(farY - standH)}" x2="0" y2="${f(farY)}" gradientUnits="userSpaceOnUse">
        <stop offset="0" stop-color="#0d2219"/><stop offset="1" stop-color="#081610"/>
      </linearGradient>
      <radialGradient id="${id}-lamp">
        <stop offset="0" stop-color="#ffffff"/>
        <stop offset="0.35" stop-color="#fff6dc"/>
        <stop offset="0.62" stop-color="#f1d595" stop-opacity="0.9"/>
        <stop offset="1" stop-color="#f1d595" stop-opacity="0"/>
      </radialGradient>
      <radialGradient id="${id}-halo">
        <stop offset="0" stop-color="#fff1cf" stop-opacity="${0.55 * lamp}"/>
        <stop offset="0.35" stop-color="#f6dfa6" stop-opacity="${0.2 * lamp}"/>
        <stop offset="1" stop-color="#f6dfa6" stop-opacity="0"/>
      </radialGradient>
      <filter id="${id}-blur" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="${f(9 * (W / 1080))}"/></filter>
      <clipPath id="${id}-pitchclip"><polygon points="${poly([P(-1.0, 0), P(1.0, 0), P(1.0, 1), P(-1.0, 1)])}"/></clipPath>
    </defs>`;

    // Side stands: tiers parallel to the touchlines, converging on the horizon.
    const r = rng(seed);
    let stands = "";
    const farL = P(-1.12, 0);
    const farR = P(1.12, 0);
    stands += `<polygon points="${poly([
      [farL[0] - standH * 1.15, farY - standH],
      [farR[0] + standH * 1.15, farY - standH],
      farR,
      farL,
    ])}" fill="url(#${id}-stand)"/>`;
    // roof edge of the far stand
    stands += `<line x1="${f(farL[0] - standH * 1.15)}" y1="${f(farY - standH)}" x2="${f(farR[0] + standH * 1.15)}" y2="${f(farY - standH)}" stroke="#cdae72" stroke-opacity="0.35" stroke-width="${f(1.4 * (W / 1080))}"/>`;
    for (let i = 1; i <= 4; i++) {
      const y = farY - (standH * i) / 5;
      const dx = (standH * 1.15 * i) / 5;
      stands += `<line x1="${f(farL[0] - dx)}" y1="${f(y)}" x2="${f(farR[0] + dx)}" y2="${f(y)}" stroke="#cdae72" stroke-opacity="0.12" stroke-width="1"/>`;
    }
    // side stands: polygons outside each touchline
    for (const side of [-1, 1]) {
      const a = P(side * 1.06, 0);
      const b = P(side * 1.06, 1.2);
      const outerFar = [a[0] + side * standH * 1.15, farY - standH];
      const outerNear = [b[0] + side * W, b[1] - standH * 6];
      stands += `<polygon points="${poly([a, outerFar, outerNear, b])}" fill="#081711"/>`;
      for (let j = 1; j <= 7; j++) {
        const xw = side * (1.06 + j * 0.09);
        const p0 = P(xw, 0);
        const p1 = P(xw, 1.3);
        const lift = (standH * j) / 8;
        stands += `<line x1="${f(p0[0] + side * lift * 1.1)}" y1="${f(p0[1] - lift)}" x2="${f(p1[0] + side * lift * 6)}" y2="${f(p1[1] - lift * 6)}" stroke="#cdae72" stroke-opacity="${(0.05 + 0.012 * j).toFixed(3)}" stroke-width="1"/>`;
      }
    }
    // crowd lights (phone torches and flashes), sparse and seeded
    for (let i = 0; i < 70; i++) {
      const u = r();
      const v = r();
      const x = farL[0] - standH * 1.1 + u * (farR[0] - farL[0] + standH * 2.2);
      const y = farY - 6 - v * (standH - 12);
      const rad = (0.7 + r() * 1.3) * (W / 1080);
      stands += `<circle cx="${f(x)}" cy="${f(y)}" r="${f(rad)}" fill="#fff3d2" fill-opacity="${(0.25 + r() * 0.55).toFixed(2)}"/>`;
    }

    // Pitch stripes
    let pitch = `<g clip-path="url(#${id}-pitchclip)">`;
    const N = 14;
    for (let i = 0; i < N; i++) {
      const t0 = i / N;
      const t1 = (i + 1) / N;
      pitch += `<polygon points="${poly([P(-1.0, t0), P(1.0, t0), P(1.0, t1), P(-1.0, t1)])}" fill="${i % 2 ? "#1c4a35" : "#21543c"}"/>`;
    }
    pitch += `<polygon points="${poly([P(-1, 0), P(1, 0), P(1, 1), P(-1, 1)])}" fill="url(#${id}-lit)"/>`;
    pitch += `</g>`;

    // Markings
    const lw = (t) => f((1.2 + 2.6 * t) * (W / 1080));
    const lineCol = `rgba(246,240,226,${(0.5 + 0.35 * lamp).toFixed(2)})`;
    let m = "";
    const seg = (a, b, t) =>
      (m += `<line x1="${f(a[0])}" y1="${f(a[1])}" x2="${f(b[0])}" y2="${f(b[1])}" stroke="${lineCol}" stroke-width="${lw(t)}" stroke-linecap="round"/>`);
    // touchlines in pieces so the width grows toward the viewer
    for (let i = 0; i < 10; i++) {
      const t0 = i / 10;
      const t1 = (i + 1) / 10;
      seg(P(-0.94, t0), P(-0.94, t1), t1);
      seg(P(0.94, t0), P(0.94, t1), t1);
    }
    const G = 0.94; // markings sit inside the striped turf
    seg(P(-G, 0), P(G, 0), 0);
    seg(P(-G, 0.5), P(G, 0.5), 0.5);
    const ell = (cxw, ct, rm, from = 0, to = Math.PI * 2, steps = 72) => {
      const pts = [];
      for (let i = 0; i <= steps; i++) {
        const a = from + ((to - from) * i) / steps;
        pts.push(P(cxw + (rm / HW) * G * Math.cos(a), ct + (rm / L) * Math.sin(a)));
      }
      return pts;
    };
    m += `<path d="${pathT(ell(0, 0.5, 9.15))}" fill="none" stroke="${lineCol}" stroke-width="${lw(0.5)}"/>`;
    m += `<ellipse cx="${f(P(0, 0.5)[0])}" cy="${f(P(0, 0.5)[1])}" rx="${f(3.2 * (W / 1080))}" ry="${f(1.6 * (W / 1080))}" fill="${lineCol}"/>`;
    for (const end of [0, 1]) {
      const dir = end === 0 ? 1 : -1;
      const box = (hw, depth) => {
        const tA = end;
        const tB = end + (dir * depth) / L;
        const xw = (hw / HW) * G;
        m += `<path d="${pathT([P(-xw, tA), P(-xw, tB), P(xw, tB), P(xw, tA)])}" fill="none" stroke="${lineCol}" stroke-width="${lw(tB)}"/>`;
      };
      box(20.15, 16.5);
      box(9.16, 5.5);
      const spotT = end + (dir * 11) / L;
      // the "D": the part of the 9.15 m circle around the spot that lies outside the box
      const a0 = Math.asin(5.5 / 9.15);
      const arc = end === 0 ? ell(0, spotT, 9.15, a0, Math.PI - a0, 40) : ell(0, spotT, 9.15, Math.PI + a0, 2 * Math.PI - a0, 40);
      m += `<path d="${pathT(arc)}" fill="none" stroke="${lineCol}" stroke-width="${lw(spotT)}"/>`;
      // goal frame (2.44 m tall)
      const g0 = P(-(3.66 / HW) * G, end);
      const g1 = P((3.66 / HW) * G, end);
      const hpx = (2.44 / HW) * half(end) * G;
      m += `<path d="M${pt(g0)}L${f(g0[0])},${f(g0[1] - hpx)}L${f(g1[0])},${f(g1[1] - hpx)}L${pt(g1)}" fill="none" stroke="rgba(255,255,255,${(0.6 + 0.3 * lamp).toFixed(2)})" stroke-width="${lw(end) * 1.2}"/>`;
    }

    // Fog on the far half, darkness toward the viewer
    let shade = `<polygon points="${poly([P(-1.6, 0), P(1.6, 0), P(1.6, 0.62), P(-1.6, 0.62)])}" fill="url(#${id}-fog)"/>`;
    shade += `<rect x="0" y="${f(P(0, 0.75)[1])}" width="${W}" height="${f(Math.max(0, H - P(0, 0.75)[1]))}" fill="url(#${id}-near)"/>`;

    // Floodlight beams, then banks on top
    let beams = "";
    let lights = "";
    banks.forEach((b, i) => {
      const s = b.s;
      const gx = b.x;
      const gy = b.y + 40 * s;
      const [a1, a2] = b.beam.map(([xw, t]) => P(xw, t));
      beams += `<linearGradient id="${id}-beam${i}" gradientUnits="userSpaceOnUse" x1="${f(gx)}" y1="${f(gy)}" x2="${f((a1[0] + a2[0]) / 2)}" y2="${f((a1[1] + a2[1]) / 2)}">
          <stop offset="0" stop-color="#fff4d8" stop-opacity="${(0.36 * lamp * haze).toFixed(3)}"/>
          <stop offset="0.6" stop-color="#fff4d8" stop-opacity="${(0.1 * lamp * haze).toFixed(3)}"/>
          <stop offset="1" stop-color="#fff4d8" stop-opacity="0"/>
        </linearGradient>
        <polygon points="${poly([[gx - 60 * s, gy], [gx + 60 * s, gy], a2, a1])}" fill="url(#${id}-beam${i})" filter="url(#${id}-blur)" style="mix-blend-mode:screen"/>`;
      // mast, fading into the dark
      lights += `<linearGradient id="${id}-mast${i}" x1="0" y1="${f(b.y)}" x2="0" y2="${f(b.y + 900 * s)}" gradientUnits="userSpaceOnUse">
          <stop offset="0" stop-color="#2a3c33"/><stop offset="1" stop-color="#2a3c33" stop-opacity="0"/>
        </linearGradient>`;
      const mx = b.x + b.dir * 6 * s;
      lights += `<path d="M${f(mx - 7 * s)},${f(b.y + 50 * s)}L${f(mx - 15 * s)},${f(b.y + 900 * s)}M${f(mx + 7 * s)},${f(b.y + 50 * s)}L${f(mx + 15 * s)},${f(b.y + 900 * s)}" stroke="url(#${id}-mast${i})" stroke-width="${f(3 * s)}" fill="none"/>`;
      let zz = "";
      for (let j = 0; j < 14; j++) {
        const y0 = b.y + 60 * s + j * 30 * s;
        const w0 = 7 * s + j * 0.6 * s;
        zz += `${j ? "L" : "M"}${f(mx + (j % 2 ? w0 : -w0))},${f(y0)}`;
      }
      lights += `<path d="${zz}" stroke="url(#${id}-mast${i})" stroke-width="${f(1.4 * s)}" fill="none"/>`;
      // halo
      lights += `<circle cx="${f(b.x)}" cy="${f(b.y)}" r="${f(230 * s)}" fill="url(#${id}-halo)"/>`;
      // bank frame, tilted toward the pitch
      lights += `<g transform="translate(${f(b.x)},${f(b.y)}) rotate(${b.dir * 9}) scale(${s})">
          <rect x="-82" y="-50" width="164" height="100" rx="4" fill="#0b1712" stroke="#cdae72" stroke-opacity="0.55" stroke-width="2"/>`;
      for (let rr = 0; rr < 3; rr++)
        for (let cc = 0; cc < 5; cc++) {
          const lx = -64 + cc * 32;
          const ly = -30 + rr * 30;
          lights += `<circle cx="${lx}" cy="${ly}" r="13" fill="#1a2a22"/><circle cx="${lx}" cy="${ly}" r="${(11 + 12 * lamp).toFixed(1)}" fill="url(#${id}-lamp)" opacity="${lamp.toFixed(3)}"/>`;
        }
      lights += `</g>`;
    });

    // Destination pin on the centre spot
    let pinSvg = "";
    if (pin) {
      const [px, py] = P(0, 0.5);
      const S = (W / 1080) * pinScale;
      const ring = (rm, op) =>
        `<path d="${pathT(ell(0, 0.5, rm, 0, Math.PI * 2, 60))}" fill="none" stroke="#e6cf98" stroke-opacity="${op}" stroke-width="${f(2 * S)}"/>`;
      pinSvg += ring(4.2, 0.95) + ring(6.6, 0.5);
      pinSvg += `<g transform="translate(${f(px)},${f(py)}) scale(${f(S * 1.15)})">
          <ellipse cx="0" cy="0" rx="13" ry="4.5" fill="#000" opacity=".45"/>
          <path d="M0 0C-4 -14 -26 -34 -26 -58A26 26 0 1 1 26 -58C26 -34 4 -14 0 0Z" fill="#cdae72" stroke="#f3e2b0" stroke-width="2"/>
          <circle cx="0" cy="-58" r="10" fill="#16392b"/>
        </g>`;
    }

    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}" style="position:absolute;inset:0;display:block">
      ${s}
      <rect width="${W}" height="${H}" fill="url(#${id}-sky)"/>
      ${stands}
      ${pitch}
      ${m}
      ${shade}
      ${beams}
      ${lights}
      ${pinSvg}
    </svg>`;
  }

  window.VVStadium = { stadium };
})();

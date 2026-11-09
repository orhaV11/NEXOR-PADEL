// A night stadium in three.js, built from primitives so it renders the same way every time:
// a striped pitch with its markings, a two-tier bowl full of fans (instanced, with phones lighting up
// and a Mexican wave), a cantilever roof lined with floodlights, LED ribbon boards with the brand,
// light beams through a little haze, fireworks and confetti. Everything is a function of t.
//   const st = VV3D.stadium({ led: "VENUVOY · THE WORLD IS YOUR STADIUM · " });
//   st.update(t, { wave: true, fireworks: [...], confetti: 0..1 }); composer renders st.scene with st.camera
(function () {
  const T = THREE;
  const rng = (seed) => { let s = seed >>> 0; return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296); };
  const hash = (a, b) => { let h = (a * 374761393 + b * 668265263) >>> 0; h = ((h ^ (h >>> 13)) * 1274126177) >>> 0; return ((h ^ (h >>> 16)) >>> 0) / 4294967296; };
  const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
  const smooth = (p) => p * p * (3 - 2 * p);

  function pitchTexture(anis) {
    const W = 2048, H = Math.round((2048 * 68) / 105), m = W / 105;
    const c = document.createElement("canvas"); c.width = W; c.height = H;
    const g = c.getContext("2d");
    const n = 18;
    for (let i = 0; i < n; i++) { g.fillStyle = i % 2 ? "#2b7a3c" : "#33884a"; g.fillRect((i * W) / n, 0, W / n + 1, H); }
    const r = rng(11);
    for (let i = 0; i < 26000; i++) { g.fillStyle = `rgba(${r() < 0.5 ? "0,0,0" : "255,255,255"},${0.02 + r() * 0.03})`; g.fillRect(r() * W, r() * H, 2, 2); }
    g.strokeStyle = "rgba(250,250,245,0.95)"; g.lineWidth = 0.14 * m; g.fillStyle = "rgba(250,250,245,0.95)";
    const L = (x1, y1, x2, y2) => { g.beginPath(); g.moveTo(x1 * m, y1 * m); g.lineTo(x2 * m, y2 * m); g.stroke(); };
    const R = (x, y, w, h) => g.strokeRect(x * m, y * m, w * m, h * m);
    const C = (x, y, rad, a0 = 0, a1 = Math.PI * 2) => { g.beginPath(); g.arc(x * m, y * m, rad * m, a0, a1); g.stroke(); };
    const D = (x, y) => { g.beginPath(); g.arc(x * m, y * m, 0.22 * m, 0, Math.PI * 2); g.fill(); };
    const e = 0.1;
    R(e, e, 105 - 2 * e, 68 - 2 * e); L(52.5, 0, 52.5, 68); C(52.5, 34, 9.15); D(52.5, 34);
    for (const side of [0, 1]) {
      const x0 = side ? 105 - e : e, dir = side ? -1 : 1;
      R(side ? 105 - 16.5 - e : e, 34 - 20.16, 16.5, 40.32);
      R(side ? 105 - 5.5 - e : e, 34 - 9.16, 5.5, 18.32);
      D(x0 + dir * 11, 34);
      const a = Math.acos(5.5 / 9.15);
      C(x0 + dir * 11, 34, 9.15, side ? Math.PI - a : -a, side ? Math.PI + a : a);
    }
    for (const [x, y, a0] of [[0, 0, 0], [105, 0, Math.PI / 2], [105, 68, Math.PI], [0, 68, -Math.PI / 2]]) C(x, y, 1, a0, a0 + Math.PI / 2);
    const tex = new T.CanvasTexture(c);
    tex.colorSpace = T.SRGBColorSpace; tex.anisotropy = anis;
    return tex;
  }

  function ledTexture(text, colors) {
    const c = document.createElement("canvas"); c.width = 4096; c.height = 64;
    const g = c.getContext("2d");
    g.fillStyle = "#04140d"; g.fillRect(0, 0, c.width, c.height);
    g.font = "800 44px Assistant, 'Playfair', sans-serif"; g.textBaseline = "middle";
    let x = 0, i = 0;
    while (x < c.width) { g.fillStyle = colors[i % colors.length]; g.fillText(text, x, 34); x += g.measureText(text).width; i++; }
    const tex = new T.CanvasTexture(c);
    tex.colorSpace = T.SRGBColorSpace; tex.wrapS = T.RepeatWrapping;
    return tex;
  }

  function glowSprite() {
    const c = document.createElement("canvas"); c.width = c.height = 64;
    const g = c.getContext("2d"), gr = g.createRadialGradient(32, 32, 0, 32, 32, 32);
    gr.addColorStop(0, "rgba(255,255,255,1)"); gr.addColorStop(0.25, "rgba(255,255,255,0.8)"); gr.addColorStop(1, "rgba(255,255,255,0)");
    g.fillStyle = gr; g.fillRect(0, 0, 64, 64);
    return new T.CanvasTexture(c);
  }

  /** The bowl follows a rounded rectangle around the pitch, sampled at equal arc length. */
  function makeLoop(a, b, n, step) {
    const raw = [];
    for (let i = 0; i <= 4000; i++) {
      const th = (i / 4000) * Math.PI * 2, c = Math.cos(th), s = Math.sin(th);
      raw.push([a * Math.sign(c) * Math.pow(Math.abs(c), 2 / n), b * Math.sign(s) * Math.pow(Math.abs(s), 2 / n)]);
    }
    const acc = [0];
    for (let i = 1; i < raw.length; i++) acc.push(acc[i - 1] + Math.hypot(raw[i][0] - raw[i - 1][0], raw[i][1] - raw[i - 1][1]));
    const total = acc[acc.length - 1], pts = [];
    let j = 0;
    for (let s = 0; s < total - 1e-6; s += step) {
      while (acc[j + 1] < s) j++;
      const f = (s - acc[j]) / (acc[j + 1] - acc[j] || 1);
      const x = raw[j][0] + (raw[j + 1][0] - raw[j][0]) * f, z = raw[j][1] + (raw[j + 1][1] - raw[j][1]) * f;
      const tx = raw[j + 1][0] - raw[j][0], tz = raw[j + 1][1] - raw[j][1], tl = Math.hypot(tx, tz) || 1;
      pts.push({ x, z, nx: tz / tl, nz: -tx / tl, s });
    }
    // make sure normals point outwards
    pts.forEach((p) => { if (p.nx * p.x + p.nz * p.z < 0) { p.nx = -p.nx; p.nz = -p.nz; } });
    return { pts, total };
  }

  /** A ribbon following the loop with a cross-section profile [[out, up], ...] (closed around the loop). */
  function ribbon(loop, profile, material) {
    const P = loop.pts, K = P.length, M = profile.length;
    const pos = new Float32Array(K * M * 3), idx = [];
    P.forEach((p, i) => profile.forEach(([o, u], j) => { const k = (i * M + j) * 3; pos[k] = p.x + p.nx * o; pos[k + 1] = u; pos[k + 2] = p.z + p.nz * o; }));
    for (let i = 0; i < K; i++) {
      const i2 = (i + 1) % K;
      for (let j = 0; j < M - 1; j++) {
        const a = i * M + j, b = i2 * M + j, c = i * M + j + 1, d = i2 * M + j + 1;
        idx.push(a, b, c, b, d, c);
      }
    }
    const geo = new T.BufferGeometry();
    geo.setAttribute("position", new T.BufferAttribute(pos, 3));
    geo.setIndex(idx); geo.computeVertexNormals();
    return new T.Mesh(geo, material);
  }

  function stadium(opts = {}) {
    const scene = new T.Scene();
    scene.background = new T.Color(0x050a14);
    scene.fog = new T.FogExp2(0x0b1626, 0.0042);
    const camera = new T.PerspectiveCamera(50, 1080 / 1920, 0.5, 2000);
    const anis = Math.min(4, opts.anisotropy || 4);

    // sky: a few stars over the open roof
    {
      const r = rng(3), n = 1400, pos = new Float32Array(n * 3);
      for (let i = 0; i < n; i++) {
        const th = r() * Math.PI * 2, ph = 0.15 + r() * 1.2;
        pos.set([Math.cos(th) * Math.cos(ph) * 900, Math.sin(ph) * 900, Math.sin(th) * Math.cos(ph) * 900], i * 3);
      }
      const g = new T.BufferGeometry(); g.setAttribute("position", new T.BufferAttribute(pos, 3));
      scene.add(new T.Points(g, new T.PointsMaterial({ color: 0xcfd8ff, size: 1.6, sizeAttenuation: false, fog: false, transparent: true, opacity: 0.7 })));
    }

    // pitch and apron
    const apron = new T.Mesh(new T.PlaneGeometry(128, 92), new T.MeshStandardMaterial({ color: 0x1f5a2e, roughness: 0.95 }));
    apron.rotation.x = -Math.PI / 2; apron.position.y = -0.02; scene.add(apron);
    const pitch = new T.Mesh(new T.PlaneGeometry(105, 68), new T.MeshStandardMaterial({ map: pitchTexture(anis), roughness: 0.82 }));
    pitch.rotation.x = -Math.PI / 2; scene.add(pitch);

    // goals
    const white = new T.MeshStandardMaterial({ color: 0xffffff, roughness: 0.4, emissive: 0x222222 });
    for (const sx of [-1, 1]) {
      const g = new T.Group();
      const post = new T.CylinderGeometry(0.06, 0.06, 2.44, 10), bar = new T.CylinderGeometry(0.06, 0.06, 7.32, 10);
      const p1 = new T.Mesh(post, white); p1.position.set(0, 1.22, -3.66); g.add(p1);
      const p2 = new T.Mesh(post, white); p2.position.set(0, 1.22, 3.66); g.add(p2);
      const cb = new T.Mesh(bar, white); cb.rotation.x = Math.PI / 2; cb.position.set(0, 2.44, 0); g.add(cb);
      const net = new T.Mesh(new T.BoxGeometry(2, 2.44, 7.32), new T.MeshBasicMaterial({ color: 0xffffff, wireframe: true, transparent: true, opacity: 0.18 }));
      net.position.set(sx * 1.0, 1.22, 0); g.add(net);
      g.position.x = sx * 52.5; scene.add(g);
    }

    // the bowl
    const concrete = new T.MeshStandardMaterial({ color: 0x252c38, roughness: 0.9, side: T.DoubleSide });
    const loop = makeLoop(62, 45, 5, 0.55);
    const seatLoop = makeLoop(62, 45, 5, opts.seatStep || 0.68);
    const lower = { o0: 2, u0: 1.4, rows: 26, depth: 0.8, rise: 0.44, every: 1.3 };
    const upper = { o0: 17.5, u0: 14.5, rows: 24, depth: 0.76, rise: 0.52, every: 1.3 };
    scene.add(ribbon(loop, [[lower.o0 - 0.4, 0], [lower.o0, lower.u0], [lower.o0 + lower.rows * lower.depth, lower.u0 + lower.rows * lower.rise], [lower.o0 + lower.rows * lower.depth + 1, lower.u0 + lower.rows * lower.rise + 1.2]], concrete));
    scene.add(ribbon(loop, [[upper.o0 - 0.6, upper.u0 - 2.4], [upper.o0, upper.u0], [upper.o0 + upper.rows * upper.depth, upper.u0 + upper.rows * upper.rise], [upper.o0 + upper.rows * upper.depth + 1.5, upper.u0 + upper.rows * upper.rise + 2]], concrete));
    // the roof: a cantilever ring with floodlights along its inner lip
    const roofOut = upper.o0 + upper.rows * upper.depth + 1.5, roofH = upper.u0 + upper.rows * upper.rise + 4;
    const roofMat = new T.MeshStandardMaterial({ color: 0x2a313d, roughness: 0.6, side: T.DoubleSide, emissive: 0x0b0f16 });
    scene.add(ribbon(loop, [[roofOut, roofH - 3], [roofOut, roofH], [6, roofH + 3.2], [6, roofH + 2.2]], roofMat));

    // LED ribbon boards: pitch-side and on the fascia between the tiers
    const ledTex = ledTexture(opts.led || "VENUVOY  ·  THE WORLD IS YOUR STADIUM  ·  ", ["#e6c47a", "#f4eee1"]);
    // the LED face points at the pitch; from behind a board is just a dark box, as in a real ground
    const ledMat = new T.MeshBasicMaterial({ map: ledTex, toneMapped: false, side: T.FrontSide });
    const boardLoop = makeLoop(55.5, 38.5, 7, 0.5);
    const board = ribbon(boardLoop, [[0, 0], [0, 0.95]], ledMat);
    scene.add(ribbon(boardLoop, [[0.08, 0], [0.08, 0.98], [0, 0.98]], new T.MeshStandardMaterial({ color: 0x0c0f14, roughness: 0.8, side: T.DoubleSide })));
    {
      const uv = new Float32Array(boardLoop.pts.length * 2 * 2);
      boardLoop.pts.forEach((p, i) => { uv.set([p.s / 40, 0, p.s / 40, 1], i * 4); });
      board.geometry.setAttribute("uv", new T.BufferAttribute(uv, 2));
    }
    scene.add(board);
    const fascia = ribbon(loop, [[upper.o0 - 0.62, upper.u0 - 2.3], [upper.o0 - 0.62, upper.u0 - 1.1]], ledMat.clone());
    {
      const uv = new Float32Array(loop.pts.length * 2 * 2);
      loop.pts.forEach((p, i) => { uv.set([p.s / 30, 0, p.s / 30, 1], i * 4); });
      fascia.geometry.setAttribute("uv", new T.BufferAttribute(uv, 2));
    }
    scene.add(fascia);

    // the crowd: one instanced quad per fan, facing the pitch
    const seats = [];
    const r = rng(7);
    const palettes = [
      ["#c8102e", "#c8102e", "#a10d25", "#e8e4dc", "#1c1c22", "#2a2a30", "#c8102e", "#7d0a1c"],
      ["#1d4fa0", "#f2c230", "#1d4fa0", "#e8e4dc", "#16181f", "#24324f", "#f2c230"],
      ["#2b2b33", "#3d3b45", "#e8e4dc", "#c8102e", "#57504a", "#1d4fa0", "#1e2a22", "#6b5a48"],
    ];
    for (const tier of [lower, upper]) {
      // every other step holds a fan, slightly bigger: the same crowd for half the geometry
      for (let rr = 0; rr < tier.rows / tier.every; rr++) {
        const row = rr * tier.every;
        const o = tier.o0 + (row + 0.5) * tier.depth, u = tier.u0 + (row + 0.5) * tier.rise;
        for (const p of seatLoop.pts) {
          if (r() < 0.06) continue; // a few empty seats
          const ang = Math.atan2(p.x, p.z);
          const pal = ang > 2.4 || ang < -2.6 ? palettes[1] : Math.abs(ang) < 0.7 ? palettes[2] : palettes[0];
          const j = (r() - 0.5) * 0.26; // not a grid: fans sit, lean and stand a little differently
          seats.push({ x: p.x + p.nx * o + p.nz * j, y: u + 0.42 + (r() - 0.5) * 0.12, z: p.z + p.nz * o - p.nx * j, nx: p.nx, nz: p.nz, s: p.s, row, col: pal[Math.floor(r() * pal.length)], ph: r() * 6.28, sz: 0.78 + r() * 0.4, lum: 0.3 + r() * 0.42 });
        }
      }
    }
    const fanGeo = new T.PlaneGeometry(0.58, 0.76);
    const fans = new T.InstancedMesh(fanGeo, new T.MeshBasicMaterial({ side: T.DoubleSide }), seats.length);
    const m4 = new T.Matrix4(), q = new T.Quaternion(), v3 = new T.Vector3(), sc = new T.Vector3(1, 1, 1), up = new T.Vector3(0, 1, 0), col = new T.Color();
    seats.forEach((st, i) => { col.set(st.col).multiplyScalar(st.lum); fans.setColorAt(i, col); st.yaw = Math.atan2(-st.nx, -st.nz); });
    scene.add(fans);
    // heads: a smaller quad above each body turns a mosaic into people
    const heads = new T.InstancedMesh(new T.PlaneGeometry(0.27, 0.3), new T.MeshBasicMaterial({ side: T.DoubleSide }), seats.length);
    const skin = ["#c99a7a", "#a87657", "#7a5038", "#e0b897", "#3a2a22", "#2a1e18", "#d8a888"];
    seats.forEach((st, i) => { col.set(skin[(i * 7 + st.row) % skin.length]).multiplyScalar(0.55 + 0.3 * ((i * 13) % 10) / 10); heads.setColorAt(i, col); });
    scene.add(heads);

    // phones lighting up in the stands
    const flashIdx = seats.map((_, i) => i).filter(() => r() < 0.012);
    const flashes = new T.InstancedMesh(new T.PlaneGeometry(0.11, 0.11), new T.MeshBasicMaterial({ color: 0xfff8e8, toneMapped: false, side: T.DoubleSide }), flashIdx.length);
    scene.add(flashes);

    // floodlights on the roof lip
    const lightLoop = makeLoop(62 + 7.2 * 0.9, 45 + 7.2 * 0.9, 5, 2.6);
    const lamps = new T.InstancedMesh(new T.BoxGeometry(0.9, 0.32, 0.3), new T.MeshBasicMaterial({ color: 0xfff3dc, toneMapped: false }), lightLoop.pts.length);
    lightLoop.pts.forEach((p, i) => { q.setFromAxisAngle(up, Math.atan2(-p.nx, -p.nz)); m4.compose(v3.set(p.x, roofH + 2.6, p.z), q, sc); lamps.setMatrixAt(i, m4); });
    scene.add(lamps);

    // lighting
    scene.add(new T.HemisphereLight(0x8fa4d6, 0x1c2a1c, 0.35));
    for (const [x, z] of [[-48, -40], [48, -40], [-48, 40], [48, 40], [0, -52], [0, 52]]) {
      const s = new T.SpotLight(0xfff1dd, 1.6, 0, 0.75, 0.7, 0);
      s.position.set(x, roofH + 3, z); s.target.position.set(x * 0.15, 0, z * 0.15);
      scene.add(s); scene.add(s.target);
    }
    const fill = new T.DirectionalLight(0xbfcfff, 0.25); fill.position.set(0, 80, 0); scene.add(fill);

    // light beams through the haze
    const beamMat = new T.ShaderMaterial({
      transparent: true, depthWrite: false, blending: T.AdditiveBlending, side: T.DoubleSide,
      uniforms: { k: { value: 0.07 } },
      vertexShader: "varying float vY; varying vec3 vN; varying vec3 vV; void main(){ vY = uv.y; vec4 mv = modelViewMatrix*vec4(position,1.); vN = normalize(normalMatrix*normal); vV = normalize(-mv.xyz); gl_Position = projectionMatrix*mv; }",
      fragmentShader: "uniform float k; varying float vY; varying vec3 vN; varying vec3 vV; void main(){ float edge = pow(abs(dot(vN, vV)), 1.6); gl_FragColor = vec4(vec3(1.0,0.95,0.85)*k*edge*smoothstep(0.0,1.0,vY), 1.0); }",
    });
    for (const [x, z] of opts.beams ? [[-44, -42], [44, -42], [-44, 42], [44, 42]] : []) { // costly in software GL: off unless asked
      const top = new T.Vector3(x, roofH + 2.5, z), bot = new T.Vector3(x * 0.2, 0, z * 0.2);
      const cone = new T.Mesh(new T.ConeGeometry(15, top.distanceTo(bot), 40, 1, true), beamMat);
      cone.position.copy(top).add(bot).multiplyScalar(0.5);
      cone.quaternion.setFromUnitVectors(up, top.clone().sub(bot).normalize());
      scene.add(cone);
    }

    // fireworks and confetti
    const spark = glowSprite();
    const FW_N = 220;
    const fwGeo = new T.BufferGeometry();
    const fwMax = 12;
    const fwPos = new Float32Array(FW_N * fwMax * 3), fwCol = new Float32Array(FW_N * fwMax * 3);
    fwGeo.setAttribute("position", new T.BufferAttribute(fwPos, 3)); fwGeo.setAttribute("color", new T.BufferAttribute(fwCol, 3));
    const fw = new T.Points(fwGeo, new T.PointsMaterial({ size: 2.6, map: spark, vertexColors: true, transparent: true, depthWrite: false, blending: T.AdditiveBlending, toneMapped: false }));
    fw.frustumCulled = false; scene.add(fw);
    const CONF_N = 1400;
    const conf = new T.InstancedMesh(new T.PlaneGeometry(0.09, 0.05), new T.MeshBasicMaterial({ side: T.DoubleSide, toneMapped: false }), CONF_N);
    const confP = [];
    {
      const rc = rng(21), cols = ["#e6c47a", "#f4eee1", "#c8102e", "#ffffff", "#2c6544", "#cdae72"];
      for (let i = 0; i < CONF_N; i++) { confP.push({ x: (rc() - 0.5) * 40, z: (rc() - 0.5) * 30, y0: 18 + rc() * 22, v: 1.4 + rc() * 1.6, w: rc() * 6.28, sp: 2 + rc() * 5 }); col.set(cols[i % cols.length]); conf.setColorAt(i, col); }
    }
    conf.visible = false; scene.add(conf);

    const e2 = new T.Euler();
    function update(t, o = {}) {
      // fans: idle sway, a travelling wave, and a jump on big moments
      const L = loop.total, wavePos = o.wave ? (t * o.wave * 1.0) % L : -1e9;
      const jump = o.jump || 0;
      seats.forEach((st, i) => {
        let y = st.y + 0.05 * Math.sin(t * 5 + st.ph);
        if (o.wave) { let d = Math.abs(st.s - wavePos); d = Math.min(d, L - d); y += 0.55 * Math.exp(-(d * d) / 40); }
        if (jump) y += jump * Math.max(0, Math.sin(t * 9 + st.ph * 0.3)) * 0.35;
        q.setFromAxisAngle(up, st.yaw);
        m4.compose(v3.set(st.x, y, st.z), q, sc.set(st.sz, st.sz, st.sz));
        fans.setMatrixAt(i, m4);
        m4.compose(v3.set(st.x - st.nx * 0.02, y + 0.52 * st.sz, st.z - st.nz * 0.02), q, sc);
        heads.setMatrixAt(i, m4);
      });
      sc.set(1, 1, 1);
      fans.instanceMatrix.needsUpdate = true;
      heads.instanceMatrix.needsUpdate = true;
      const fr = Math.floor(t * 7);
      flashIdx.forEach((si, k) => {
        const st = seats[si], on = hash(k, fr) < (o.phones ?? 0.4) ? 1 : 0;
        q.setFromAxisAngle(up, st.yaw);
        m4.compose(v3.set(st.x + st.nx * -0.05, st.y + 0.45, st.z + st.nz * -0.05), q, sc.set(on, on, on));
        flashes.setMatrixAt(k, m4);
      });
      sc.set(1, 1, 1);
      sc.set(1, 1, 1);
      flashes.instanceMatrix.needsUpdate = true;
      ledTex.offset.x = -t * 0.06;
      fascia.material.map.offset.x = t * 0.04;

      // fireworks: each burst is [t0, x, y, z, hue]
      let n = 0;
      for (const [t0, x, y, z, c] of o.fireworks || []) {
        const tau = t - t0;
        if (tau < 0 || tau > 2.4 || n >= fwMax) continue;
        const rb = rng(Math.floor(t0 * 1000) + 1), base = new T.Color(c);
        for (let i = 0; i < FW_N; i++) {
          const th = rb() * Math.PI * 2, ph = Math.acos(2 * rb() - 1), sp = 14 + rb() * 6;
          const k = (1 - Math.exp(-tau * 2.2)) / 2.2;
          const idx = (n * FW_N + i) * 3;
          fwPos[idx] = x + Math.sin(ph) * Math.cos(th) * sp * k;
          fwPos[idx + 1] = y + Math.cos(ph) * sp * k - 4.5 * tau * tau;
          fwPos[idx + 2] = z + Math.sin(ph) * Math.sin(th) * sp * k;
          const a = clamp(1 - tau / 2.4) * (0.6 + 0.4 * hash(i, Math.floor(tau * 20)));
          fwCol[idx] = base.r * a * 1.6; fwCol[idx + 1] = base.g * a * 1.6; fwCol[idx + 2] = base.b * a * 1.6;
        }
        n++;
      }
      fwGeo.setDrawRange(0, n * FW_N);
      fwGeo.attributes.position.needsUpdate = true; fwGeo.attributes.color.needsUpdate = true;

      const cf = o.confetti || 0;
      conf.visible = cf > 0;
      if (cf > 0) {
        const tc = o.confettiT || 0;
        confP.forEach((p, i) => {
          const y = p.y0 - p.v * tc;
          e2.set(p.w + tc * p.sp, p.w * 2 + tc * p.sp * 0.7, 0);
          q.setFromEuler(e2);
          m4.compose(v3.set(p.x + Math.sin(tc * 1.3 + p.w) * 1.2 + (o.confettiAt?.x || 0), y + (o.confettiAt?.y || 0), p.z + (o.confettiAt?.z || 0)), q, sc.set(cf, cf, cf));
          conf.setMatrixAt(i, m4);
        });
        sc.set(1, 1, 1);
        conf.instanceMatrix.needsUpdate = true;
      }
    }

    /** Camera along a path: keys = [[t, [px,py,pz], [tx,ty,tz], fov], ...], eased between keys. */
    function shoot(t, keys) {
      let a = keys[0], b = keys[keys.length - 1];
      for (let i = 0; i < keys.length - 1; i++) if (t >= keys[i][0] && t <= keys[i + 1][0]) { a = keys[i]; b = keys[i + 1]; break; }
      const p = b[0] > a[0] ? smooth(clamp((t - a[0]) / (b[0] - a[0]))) : 1;
      const L3 = (u, v) => u.map((x, k) => x + (v[k] - x) * p);
      camera.position.set(...L3(a[1], b[1]));
      camera.lookAt(new T.Vector3(...L3(a[2], b[2])));
      camera.fov = a[3] + (b[3] - a[3]) * p;
      camera.updateProjectionMatrix();
    }

    return { scene, camera, update, shoot, info: { fans: seats.length, flashes: flashIdx.length, lamps: lightLoop.pts.length } };
  }

  window.VV3D = Object.assign(window.VV3D || {}, { stadium });
})();

// A dotted gold globe on a deep-green ocean (brand colours), with an atmosphere glow, city points
// and great-circle flight arcs that draw themselves. Land comes from Natural Earth (world-atlas), projected
// with d3-geo into an equirectangular texture of dots. Everything is a function of t.
//   const gl = VV3D.globe(); gl.arc("tlv", [lon, lat], [lon, lat]); gl.update(t, { face: [lon, lat], dist, arcs: { tlv: progress } })
(function () {
  const T = THREE;
  const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));

  /** lon/lat (degrees) to a point on a sphere of radius r, matching three's SphereGeometry UVs. */
  function ll(lon, lat, r = 1) {
    const phi = ((lon + 180) / 360) * Math.PI * 2, th = ((90 - lat) / 180) * Math.PI;
    return new T.Vector3(-Math.cos(phi) * Math.sin(th) * r, Math.cos(th) * r, Math.sin(phi) * Math.sin(th) * r);
  }

  function landTexture() {
    const W = 4096, H = 2048;
    const mask = document.createElement("canvas"); mask.width = W; mask.height = H;
    const mc = mask.getContext("2d");
    const proj = GEO.geoEquirectangular().scale(W / (2 * Math.PI)).translate([W / 2, H / 2]);
    const path = GEO.geoPath(proj, mc);
    mc.fillStyle = "#fff"; mc.beginPath(); path(GEO.land); mc.fill();
    const md = mc.getImageData(0, 0, W, H).data;
    const c = document.createElement("canvas"); c.width = W; c.height = H;
    const g = c.getContext("2d");
    const ocean = g.createLinearGradient(0, 0, 0, H);
    ocean.addColorStop(0, "#04150f"); ocean.addColorStop(0.5, "#082a1d"); ocean.addColorStop(1, "#04150f");
    g.fillStyle = ocean; g.fillRect(0, 0, W, H);
    // faint land wash under the dots so coastlines read at a distance
    g.globalAlpha = 0.16; g.drawImage(mask, 0, 0); g.globalAlpha = 1;
    g.fillStyle = "#e9d29a";
    const step = 0.5; // degrees between dot rows
    for (let lat = -84; lat <= 84; lat += step) {
      const k = 1 / Math.max(0.12, Math.cos((lat * Math.PI) / 180));
      for (let lon = -180; lon < 180; lon += step * k) {
        const x = Math.round(((lon + 180) / 360) * W), y = Math.round(((90 - lat) / 180) * H);
        if (md[(y * W + x) * 4] < 128) continue;
        g.beginPath(); g.ellipse(x, y, 2.1 * k, 2.1, 0, 0, Math.PI * 2); g.fill();
      }
    }
    g.strokeStyle = "rgba(233,210,154,0.22)"; g.lineWidth = 1.2;
    const bp = GEO.geoPath(proj, g); g.beginPath(); bp(GEO.borders); g.stroke();
    const tex = new T.CanvasTexture(c);
    tex.colorSpace = T.SRGBColorSpace; tex.anisotropy = 4;
    return tex;
  }

  function glowTex() {
    const c = document.createElement("canvas"); c.width = c.height = 128;
    const g = c.getContext("2d"), gr = g.createRadialGradient(64, 64, 0, 64, 64, 64);
    gr.addColorStop(0, "rgba(255,255,255,1)"); gr.addColorStop(0.18, "rgba(255,244,214,0.9)"); gr.addColorStop(0.5, "rgba(230,196,122,0.25)"); gr.addColorStop(1, "rgba(230,196,122,0)");
    g.fillStyle = gr; g.fillRect(0, 0, 128, 128);
    return new T.CanvasTexture(c);
  }

  function globe(opts = {}) {
    const scene = new T.Scene();
    scene.background = new T.Color(0x020806);
    const camera = new T.PerspectiveCamera(32, 1080 / 1920, 0.05, 200);
    const world = new T.Group(); scene.add(world);

    // stars
    {
      let s = 9; const r = () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
      const n = 2500, pos = new Float32Array(n * 3);
      for (let i = 0; i < n; i++) { const v = new T.Vector3(r() - 0.5, r() - 0.5, r() - 0.5).normalize().multiplyScalar(60 + r() * 30); pos.set([v.x, v.y, v.z], i * 3); }
      const gg = new T.BufferGeometry(); gg.setAttribute("position", new T.BufferAttribute(pos, 3));
      scene.add(new T.Points(gg, new T.PointsMaterial({ color: 0xf3ead2, size: 1.5, sizeAttenuation: false, transparent: true, opacity: 0.75 })));
    }

    const sphere = new T.Mesh(new T.SphereGeometry(1, 128, 80), new T.MeshStandardMaterial({ map: landTexture(), roughness: 0.85, metalness: 0, emissive: 0x0a1f15, emissiveIntensity: 0.6 }));
    world.add(sphere);
    // atmosphere: a fresnel rim, warm on the lit side
    const atmo = new T.Mesh(new T.SphereGeometry(1.085, 96, 64), new T.ShaderMaterial({
      transparent: true, depthWrite: false, side: T.BackSide, blending: T.AdditiveBlending,
      vertexShader: "varying vec3 vN; varying vec3 vP; void main(){ vN = normalize(normalMatrix*normal); vec4 mv = modelViewMatrix*vec4(position,1.); vP = mv.xyz; gl_Position = projectionMatrix*mv; }",
      fragmentShader: "varying vec3 vN; varying vec3 vP; void main(){ float f = pow(clamp(1.0 - abs(dot(vN, normalize(-vP))), 0.0, 1.0), 2.4); gl_FragColor = vec4(vec3(0.95,0.82,0.52)*f*0.75 + vec3(0.25,0.75,0.55)*f*0.25, 1.0); }",
    }));
    scene.add(atmo);
    const sun = new T.DirectionalLight(0xfff4dc, 2.4); sun.position.set(-3, 2, 4); scene.add(sun);
    scene.add(new T.AmbientLight(0x6f8f80, 0.55));

    const glow = glowTex();
    const arcs = {}, pins = {};
    /** A great-circle flight from a to b ([lon, lat]), lifted into an arc that draws itself. */
    function arc(id, a, b, color = 0xf4e1a8, lift = 0.22) {
      const ip = GEO.geoInterpolate(a, b), dist = GEO.geoDistance(a, b);
      const pts = [];
      for (let i = 0; i <= 96; i++) { const k = i / 96, [lo, la] = ip(k); pts.push(ll(lo, la, 1.003 + lift * Math.min(1, dist * 1.4) * Math.sin(Math.PI * k))); }
      const curve = new T.CatmullRomCurve3(pts);
      const geo = new T.TubeGeometry(curve, 160, opts.tube || 0.0052, 8, false);
      const mesh = new T.Mesh(geo, new T.MeshBasicMaterial({ color, toneMapped: false, transparent: true }));
      const head = new T.Sprite(new T.SpriteMaterial({ map: glow, color: 0xffffff, transparent: true, depthWrite: false, blending: T.AdditiveBlending, toneMapped: false }));
      head.scale.setScalar(0.09);
      world.add(mesh); world.add(head);
      arcs[id] = { mesh, head, curve, count: geo.index.count };
      pin(id + ":a", a); pin(id + ":b", b);
      return arcs[id];
    }
    function pin(id, [lon, lat], color = 0xfff1c9) {
      if (pins[id]) return pins[id];
      const s = new T.Sprite(new T.SpriteMaterial({ map: glow, color, transparent: true, depthWrite: false, blending: T.AdditiveBlending, toneMapped: false }));
      s.position.copy(ll(lon, lat, 1.006)); s.scale.setScalar(0.035);
      const ring = new T.Mesh(new T.RingGeometry(0.02, 0.026, 48), new T.MeshBasicMaterial({ color: 0xe6c47a, transparent: true, side: T.DoubleSide, toneMapped: false }));
      ring.position.copy(ll(lon, lat, 1.004)); ring.lookAt(ll(lon, lat, 2));
      world.add(s); world.add(ring);
      pins[id] = { s, ring };
      return pins[id];
    }

    let lastYaw = null;
    /** face: [lon, lat] to turn toward the camera; dist: camera distance; arcs: {id: 0..1}; pins: {id: 0..1}. */
    function update(t, o = {}) {
      const [lon, lat] = o.face || [20, 35];
      const v = ll(lon, lat);
      let yaw = Math.atan2(-v.x, v.z);
      if (lastYaw !== null) { while (yaw - lastYaw > Math.PI) yaw -= 2 * Math.PI; while (yaw - lastYaw < -Math.PI) yaw += 2 * Math.PI; }
      lastYaw = yaw;
      world.rotation.set(((o.tilt ?? lat) * Math.PI) / 180, yaw + (o.spin || 0), 0);
      camera.position.set(o.camX || 0, o.camY || 0, o.dist || 3.4);
      camera.lookAt(o.lookX || 0, o.lookY || 0, 0);
      camera.fov = o.fov || 32; camera.updateProjectionMatrix();
      for (const [id, a] of Object.entries(arcs)) {
        const p = clamp(o.arcs?.[id] ?? 0);
        a.mesh.visible = p > 0;
        a.mesh.geometry.setDrawRange(0, Math.floor((a.count * p) / 48) * 48);
        a.mesh.material.opacity = o.arcFade?.[id] ?? 1;
        a.head.visible = p > 0 && p < 1;
        if (a.head.visible) a.head.position.copy(a.curve.getPoint(p));
        a.head.scale.setScalar(0.09 + 0.025 * Math.sin(t * 20));
      }
      for (const [id, pn] of Object.entries(pins)) {
        const shown = o.pins?.[id] ?? (o.allPins ? 1 : 0);
        pn.s.visible = pn.ring.visible = shown > 0;
        const ph = (t * 1.1 + id.length * 0.37) % 1;
        pn.ring.scale.setScalar(1 + ph * 2.2); pn.ring.material.opacity = (1 - ph) * shown;
        pn.s.material.opacity = shown;
      }
    }
    return { scene, camera, update, arc, pin, ll };
  }

  window.VV3D = Object.assign(window.VV3D || {}, { globe });
})();

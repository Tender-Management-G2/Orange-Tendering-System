// ORANGE INTELLIGENCE - Supplier Universe (Suppliers tab, Views/Intelligence/Index.cshtml).
// A slowly orbiting 3D sphere of the real, authorized suppliers in the workspace JSON. Nothing here is invented: positions only
// arrange the market for overview (the sphere is not a map), colour = registered category, size = closed rounds with an offer,
// and every link comes from the server (competition links and closed-round offers). Selection is shared with
// orange-radar.js through window.OrangeRadar, so charts and the 2D network see the same focus.
const root = document.querySelector('[data-radar]');
if (root) boot().catch(e => console.warn('Supplier Universe unavailable', e));

async function boot() {
  const THREE = await import(root.dataset.three);
  const $ = s => root.querySelector(s);
  const $$ = s => Array.from(root.querySelectorAll(s));
  const host = $('[data-universe]'), stage = $('[data-uni-stage]'), canvas = $('[data-uni-canvas]'), labelLayer = $('[data-uni-labels]'), tip = $('[data-uni-tip]');
  const inspector = $('[data-uni-inspector]');
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
  const R = 1;
  const esc = s => String(s ?? '').replace(/RADAR DEMO (- )?/g, '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const clean = s => String(s ?? '').replace(/^RADAR DEMO /, '');
  const fmtInt = n => (n == null ? '—' : Number(n).toLocaleString('en-US'));
  const fmtPct = (n, signed = false) => (n == null ? '—' : `${signed && n > 0 ? '+' : ''}${(n * 100).toFixed(0)}%`);
  const fmtDate = d => (d ? new Date(d).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }) : '—');
  const radar = () => window.OrangeRadar;
  const ar = root.dataset.uiLanguage === 'ar';
  const T = (en, a) => (ar ? a : en);
  const cycleLabel = n => n === 1 ? T('Initial Bidding', 'العطاء الأولي') : T(`Re-tendering — Cycle ${n}`, `إعادة الطرح — الدورة ${n}`);

  // ---------- renderer ----------
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, powerPreference: 'high-performance', preserveDrawingBuffer: false });
  renderer.setClearColor(0x000000, 0);
  renderer.setPixelRatio(Math.min(devicePixelRatio || 1, 1.5));
  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(34, 1, 0.05, 200);
  const HOME = { dist: 4.15, focus: 3.7 };
  camera.position.set(0, 0, HOME.dist);
  const world = new THREE.Group(); scene.add(world);           // rotates (orbit + drag); everything attached to the sphere
  const fixed = new THREE.Group(); scene.add(fixed);           // pedestal rings and stars stay put
  const BASE_TILT = new THREE.Quaternion().setFromEuler(new THREE.Euler(0.36, -0.5, 0.08));
  world.quaternion.copy(BASE_TILT);

  // ---------- palette (from CSS tokens; light theme is its own palette, not a filter) ----------
  let P = {};
  const css = n => getComputedStyle(root).getPropertyValue(n).trim();
  const dark = () => document.documentElement.dataset.theme !== 'light';
  function readPalette() {
    P = { dark: dark(), orange: new THREE.Color(css('--rd-orange-strong') || '#ff7900'), ink: css('--rd-ink'), muted: css('--rd-muted'), review: new THREE.Color(css('--rd-review') || '#f4b44c'),
      award: new THREE.Color(css('--rd-award') || '#45d0b5'), edge: new THREE.Color(css('--rd-edge') || '#7389a8'), neutral: new THREE.Color(css('--rd-neutral') || '#5d6b84'),
      link: new THREE.Color(dark() ? '#ffb070' : '#d4640a'), info: new THREE.Color(dark() ? '#6fb6ff' : '#2f6fd6') };
  }
  readPalette();
  const catColor = c => new THREE.Color(radar()?.catColor?.(c) || '#7f8ea3');

  // ---------- textures ----------
  function radialTexture(stops) {
    const c = document.createElement('canvas'); c.width = c.height = 128; const g = c.getContext('2d');
    const grd = g.createRadialGradient(64, 64, 0, 64, 64, 64); stops.forEach(([o, col]) => grd.addColorStop(o, col)); g.fillStyle = grd; g.fillRect(0, 0, 128, 128);
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; return t;
  }
  const haloTex = radialTexture([[0, 'rgba(255,255,255,0.95)'], [0.18, 'rgba(255,255,255,0.55)'], [0.45, 'rgba(255,255,255,0.14)'], [1, 'rgba(255,255,255,0)']]);
  const dotTex = radialTexture([[0, 'rgba(255,255,255,1)'], [0.35, 'rgba(255,255,255,0.6)'], [1, 'rgba(255,255,255,0)']]);
  const ringTex = (() => {
    const c = document.createElement('canvas'); c.width = c.height = 128; const g = c.getContext('2d');
    g.strokeStyle = '#fff'; g.lineWidth = 7; g.beginPath(); g.arc(64, 64, 52, 0, Math.PI * 2); g.stroke();
    g.globalAlpha = 0.35; g.lineWidth = 3; g.beginPath(); g.arc(64, 64, 60, 0, Math.PI * 2); g.stroke();
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; return t;
  })();

  // ---------- the glass sphere ----------
  const shellMat = new THREE.ShaderMaterial({
    transparent: true, depthWrite: false, side: THREE.FrontSide,
    uniforms: { uTop: { value: new THREE.Color() }, uBottom: { value: new THREE.Color() }, uBody: { value: new THREE.Color() }, uBodyA: { value: 0.1 }, uRimA: { value: 0.75 } },
    vertexShader: `varying vec3 vN; varying vec3 vV; varying float vY;
      void main(){ vec4 mv = modelViewMatrix * vec4(position,1.0); vN = normalize(normalMatrix * normal); vV = normalize(-mv.xyz);
        vY = (modelMatrix * vec4(position,1.0)).y; gl_Position = projectionMatrix * mv; }`,
    fragmentShader: `uniform vec3 uTop; uniform vec3 uBottom; uniform vec3 uBody; uniform float uBodyA; uniform float uRimA; varying vec3 vN; varying vec3 vV; varying float vY;
      void main(){ float f = 1.0 - max(dot(vN, vV), 0.0); float rim = pow(f, 2.6); float h = smoothstep(-1.0, 1.0, vY);
        vec3 rc = mix(uBottom, uTop, h); vec3 c = mix(uBody, rc, rim); float a = uBodyA * (1.0 - rim) + rim * uRimA;
        float spec = pow(max(dot(reflect(-normalize(vec3(-0.5,0.7,0.6)), vN), vV), 0.0), 40.0) * 0.35;
        gl_FragColor = vec4(c + spec, a + spec * 0.5); }`
  });
  const shell = new THREE.Mesh(new THREE.SphereGeometry(R, 72, 48), shellMat);
  shell.renderOrder = 2; scene.add(shell); // the shell does not rotate: its lighting stays stable while the content orbits

  // Interior back-face for depth (darker inside, gives the "glass ball" volume).
  const innerMat = new THREE.ShaderMaterial({
    transparent: true, depthWrite: false, side: THREE.BackSide, uniforms: { uC: { value: new THREE.Color() }, uA: { value: 0.35 } },
    vertexShader: 'varying vec3 vN; varying vec3 vV; void main(){ vec4 mv = modelViewMatrix*vec4(position,1.0); vN = normalize(normalMatrix*normal); vV = normalize(-mv.xyz); gl_Position = projectionMatrix*mv; }',
    fragmentShader: 'uniform vec3 uC; uniform float uA; varying vec3 vN; varying vec3 vV; void main(){ float f = abs(dot(vN, vV)); gl_FragColor = vec4(uC, uA * (0.35 + 0.65 * f)); }'
  });
  const inner = new THREE.Mesh(new THREE.SphereGeometry(R * 0.995, 48, 32), innerMat); inner.renderOrder = 0; scene.add(inner);

  // Graticule: faint latitude/longitude lines (no geography).
  const gratMat = new THREE.LineBasicMaterial({ transparent: true, opacity: 0.08, depthWrite: false });
  {
    const pts = [];
    for (let lat = -60; lat <= 60; lat += 30) { const y = Math.sin(lat * Math.PI / 180) * R, r = Math.cos(lat * Math.PI / 180) * R; for (let i = 0; i < 96; i++) { const a = i / 96 * Math.PI * 2, b = (i + 1) / 96 * Math.PI * 2; pts.push(r * Math.cos(a), y, r * Math.sin(a), r * Math.cos(b), y, r * Math.sin(b)); } }
    for (let lon = 0; lon < 180; lon += 30) { const a0 = lon * Math.PI / 180; for (let i = 0; i < 96; i++) { const t0 = i / 96 * Math.PI * 2, t1 = (i + 1) / 96 * Math.PI * 2; pts.push(R * Math.cos(t0) * Math.cos(a0), R * Math.sin(t0), R * Math.cos(t0) * Math.sin(a0), R * Math.cos(t1) * Math.cos(a0), R * Math.sin(t1), R * Math.cos(t1) * Math.sin(a0)); } }
    const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
    world.add(new THREE.LineSegments(g, gratMat));
  }
  // Surface dust: tiny points on the sphere, a texture for the eye only.
  const dustMat = new THREE.PointsMaterial({ size: 0.012, map: dotTex, transparent: true, depthWrite: false, opacity: 0.5, blending: THREE.AdditiveBlending });
  {
    const n = 700, pts = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) { const u = Math.random() * 2 - 1, a = Math.random() * Math.PI * 2, s = Math.sqrt(1 - u * u); pts.set([s * Math.cos(a) * R * 1.002, u * R * 1.002, s * Math.sin(a) * R * 1.002], i * 3); }
    const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(pts, 3)); world.add(new THREE.Points(g, dustMat));
  }
  // Orbit rings with travelling sparks, and the pedestal below the sphere.
  const ringMats = [];
  const orbitRings = [];
  [[1.28, 0.42, 0.2], [1.46, -0.28, 1.1]].forEach(([rad, tilt, rot], i) => {
    const pts = []; for (let k = 0; k <= 160; k++) { const a = k / 160 * Math.PI * 2; pts.push(new THREE.Vector3(Math.cos(a) * rad, 0, Math.sin(a) * rad)); }
    const m = new THREE.LineBasicMaterial({ transparent: true, opacity: 0.22, depthWrite: false }); ringMats.push(m);
    const line = new THREE.Line(new THREE.BufferGeometry().setFromPoints(pts), m);
    const g = new THREE.Group(); g.rotation.set(tilt, rot, 0.15 * (i ? -1 : 1)); g.add(line);
    const spark = new THREE.Sprite(new THREE.SpriteMaterial({ map: dotTex, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending })); spark.scale.setScalar(0.07);
    g.add(spark); fixed.add(g); orbitRings.push({ g, spark, rad, speed: i ? -0.07 : 0.05, phase: i * 2 });
  });
  const pedMats = [];
  [1.05, 1.32, 1.62].forEach((rad, i) => {
    const pts = []; for (let k = 0; k <= 128; k++) { const a = k / 128 * Math.PI * 2; pts.push(new THREE.Vector3(Math.cos(a) * rad, 0, Math.sin(a) * rad)); }
    const m = new THREE.LineBasicMaterial({ transparent: true, opacity: 0.32 - i * 0.08, depthWrite: false }); pedMats.push(m);
    const l = new THREE.Line(new THREE.BufferGeometry().setFromPoints(pts), m); l.position.y = -R * 1.12 - i * 0.03; fixed.add(l);
  });
  const pedGlow = new THREE.Sprite(new THREE.SpriteMaterial({ map: haloTex, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, opacity: 0.4 }));
  pedGlow.scale.set(3.4, 0.55, 1); pedGlow.position.y = -R * 1.13; fixed.add(pedGlow);
  const core = new THREE.Sprite(new THREE.SpriteMaterial({ map: haloTex, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, opacity: 0.18 }));
  core.scale.setScalar(1.5); scene.add(core); // decorative, non-interactive
  // Stars (dark theme only).
  const starMat = new THREE.PointsMaterial({ size: 0.03, map: dotTex, transparent: true, depthWrite: false, opacity: 0.6, blending: THREE.AdditiveBlending });
  {
    const n = 420, pts = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) { const u = Math.random() * 2 - 1, a = Math.random() * Math.PI * 2, s = Math.sqrt(1 - u * u), d = 9 + Math.random() * 10; pts.set([s * Math.cos(a) * d, u * d * 0.6, -Math.abs(s * Math.sin(a) * d) - 2], i * 3); }
    const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(pts, 3)); fixed.add(new THREE.Points(g, starMat));
  }

  function applyTheme() {
    readPalette();
    const d = P.dark;
    shellMat.uniforms.uTop.value.set(d ? '#5aa8ff' : '#7aa6dd'); shellMat.uniforms.uBottom.value.set(d ? '#ff8a2b' : '#ff9a4a');
    shellMat.uniforms.uBody.value.set(d ? '#0e2240' : '#e9f0f8'); shellMat.uniforms.uBodyA.value = d ? 0.16 : 0.32; shellMat.uniforms.uRimA.value = d ? 0.85 : 0.7;
    innerMat.uniforms.uC.value.set(d ? '#061226' : '#dbe5f1'); innerMat.uniforms.uA.value = d ? 0.5 : 0.35;
    gratMat.color.set(d ? '#9cc4ff' : '#33507a'); gratMat.opacity = d ? 0.075 : 0.1;
    dustMat.color.set(d ? '#9fc8ff' : '#6b86ad'); dustMat.opacity = d ? 0.45 : 0.25; dustMat.blending = d ? THREE.AdditiveBlending : THREE.NormalBlending;
    ringMats.forEach(m => { m.color.set(d ? '#ffb070' : '#d4640a'); m.opacity = d ? 0.22 : 0.3; });
    pedMats.forEach((m, i) => { m.color.set(d ? '#ff8a2b' : '#d4640a'); m.opacity = (d ? 0.34 : 0.3) - i * 0.08; });
    orbitRings.forEach(o => o.spark.material.color.set(d ? '#ffc48c' : '#ff7900'));
    pedGlow.material.color.set(d ? '#ff7a1a' : '#ffb27a'); pedGlow.material.opacity = d ? 0.4 : 0.35; pedGlow.material.blending = d ? THREE.AdditiveBlending : THREE.NormalBlending;
    core.material.color.set(d ? '#3b7bff' : '#ffffff'); core.material.opacity = d ? 0.16 : 0.0;
    starMat.opacity = d ? 0.55 : 0;
    nodes.forEach(n => paintNode(n));
    rebuildLinks(); wake();
  }

  // ---------- nodes ----------
  const nodeGeo = new THREE.SphereGeometry(1, 24, 16);
  const nodeVS = 'varying vec3 vN; varying vec3 vV; void main(){ vec4 mv = modelViewMatrix*vec4(position,1.0); vN = normalize(normalMatrix*normal); vV = normalize(-mv.xyz); gl_Position = projectionMatrix*mv; }';
  const nodeFS = `uniform vec3 uColor; uniform float uOpacity; uniform float uGlow; varying vec3 vN; varying vec3 vV;
    void main(){ vec3 L = normalize(vec3(-0.45,0.65,0.62)); float ndv = max(dot(vN,vV),0.0); float diff = 0.5 + 0.5*max(dot(vN,L),0.0);
      float spec = pow(max(dot(reflect(-L,vN),vV),0.0), 28.0) * 0.75; float rim = pow(1.0-ndv, 2.0);
      vec3 c = uColor * diff * (0.85 + uGlow) + vec3(spec) + mix(uColor, vec3(1.0), 0.35) * rim * 0.65; gl_FragColor = vec4(c, uOpacity); }`;
  let nodes = [];            // { id, s (stats), mesh, halo, ring, home, pos, from, to, r, alpha, alphaT, vis, visT, phase, cat }
  const byId = new Map();
  let hubs = [];             // tender hub sprites for the current mode
  const st = {
    mode: 'universe', selected: [], hover: null, motion: !reduced.matches, labels: true, links: true, tender: null, filter: null, view: '3d',
    drag: null, vel: { x: 0, y: 0 }, qTarget: null, distTarget: HOME.dist, offset: 0, offsetTarget: 0, trans: null, lastInteract: 0, visible: true, ready: false
  };

  function paintNode(n) {
    const col = catColor(n.cat);
    n.mesh.material.uniforms.uColor.value.copy(col);
    n.halo.material.color.copy(col);
    n.halo.material.blending = P.dark ? THREE.AdditiveBlending : THREE.NormalBlending;
    n.ring.material.color.copy(P.orange);
  }

  function buildNodes(data) {
    nodes.forEach(n => { world.remove(n.mesh); world.remove(n.halo); world.remove(n.ring); n.mesh.material.dispose(); n.halo.material.dispose(); n.ring.material.dispose(); });
    nodes = []; byId.clear();
    const max = Math.max(1, ...data.suppliers.map(s => s.participations));
    data.suppliers.forEach((s, i) => {
      const mat = new THREE.ShaderMaterial({ uniforms: { uColor: { value: new THREE.Color() }, uOpacity: { value: 1 }, uGlow: { value: 0 } }, vertexShader: nodeVS, fragmentShader: nodeFS, transparent: true });
      const mesh = new THREE.Mesh(nodeGeo, mat); mesh.renderOrder = 3;
      const halo = new THREE.Sprite(new THREE.SpriteMaterial({ map: haloTex, transparent: true, depthWrite: false, opacity: 0.5 })); halo.renderOrder = 1;
      const ring = new THREE.Sprite(new THREE.SpriteMaterial({ map: ringTex, transparent: true, depthWrite: false, depthTest: false, opacity: 0 })); ring.renderOrder = 5;
      // Size encodes one defined metric: closed rounds with a submitted offer (square-root scale). Zero keeps a visible base size.
      const r = 0.034 + 0.04 * Math.sqrt(s.participations / max);
      const n = { id: s.supplierId, s, mesh, halo, ring, r, cat: (s.category || '').trim() || 'Uncategorized', pos: new THREE.Vector3(), from: new THREE.Vector3(), to: new THREE.Vector3(), alpha: 1, alphaT: 1, vis: 1, visT: 1, phase: i * 1.7, screen: { x: 0, y: 0, z: 0, front: 1, rpx: 4 } };
      paintNode(n); world.add(mesh, halo, ring); nodes.push(n); byId.set(n.id, n);
    });
  }

  // ---------- layouts (unit sphere directions in world-local coordinates) ----------
  const fib = (i, n) => { const y = n === 1 ? 0 : 1 - (i / (n - 1)) * 2, r = Math.sqrt(1 - y * y), a = i * Math.PI * (3 - Math.sqrt(5)); return new THREE.Vector3(Math.cos(a) * r, y, Math.sin(a) * r); };
  function basis(c) { const up = Math.abs(c.y) > 0.9 ? new THREE.Vector3(1, 0, 0) : new THREE.Vector3(0, 1, 0); const u = new THREE.Vector3().crossVectors(up, c).normalize(); const v = new THREE.Vector3().crossVectors(c, u).normalize(); return [u, v]; }
  // Members of one group in a sunflower cap around direction c.
  function cap(c, ids, spacing = 0.17, out = new Map()) {
    const [u, v] = basis(c);
    ids.forEach((id, k) => {
      const rr = spacing * Math.sqrt(k + (ids.length > 1 ? 0.6 : 0)), a = k * 2.39996;
      const p = c.clone().addScaledVector(u, Math.cos(a) * rr).addScaledVector(v, Math.sin(a) * rr).normalize();
      out.set(id, p);
    });
    return out;
  }
  // Groups gather on the hemisphere facing the viewer (largest in the centre, the rest on rings around it), so every group is
  // readable at once; the sphere then sways gently instead of orbiting away from them.
  function frontDir() { return new THREE.Vector3(0, 0.06, 1).normalize().applyQuaternion(world.quaternion.clone().invert()); }
  function clusterLayout(groups, spacing) {
    const order = groups.slice().sort((a, b) => b.ids.length - a.ids.length);
    const F = st.front || frontDir(); const [u, v] = basis(F);
    const ring = [{ n: 1, t: 0 }, { n: 6, t: 0.82 }, { n: 10, t: 1.5 }, { n: 14, t: 2.15 }];
    const slots = []; ring.forEach(r => { for (let k = 0; k < r.n; k++) { const ph = (k / r.n) * Math.PI * 2 + (r.t > 1 ? 0.3 : -Math.PI / 2 + 0.6); slots.push(F.clone().multiplyScalar(Math.cos(r.t)).addScaledVector(u, Math.cos(ph) * Math.sin(r.t)).addScaledVector(v, Math.sin(ph) * Math.sin(r.t)).normalize()); } });
    const anchors = order.map((g, i) => ({ g, c: slots[i] || fib(i, order.length) }));
    const map = new Map(); anchors.forEach(({ g, c }) => cap(c, g.ids, spacing, map));
    return { map, anchors: anchors.map(({ g, c }) => { const [, v] = basis(c); const lift = spacing * Math.sqrt(g.ids.length + 0.6) + 0.09;
      return { label: g.label, sub: g.sub, color: g.color, dir: c, labelDir: c.clone().addScaledVector(v, lift).normalize(), count: g.ids.length, key: g.key }; }) };
  }
  let anchors = [];
  function layoutFor(mode) {
    const d = radar()?.data; if (!d) return { map: new Map(), anchors: [] };
    const list = nodes.slice();
    if (mode === 'category') {
      const groups = [...group(list, n => n.cat)].map(([cat, ns]) => ({ key: cat, label: clean(cat), ids: ns.sort((a, b) => b.s.participations - a.s.participations).map(n => n.id), color: catColor(cat) }));
      return clusterLayout(groups, 0.16);
    }
    if (mode === 'competition') {
      const groups = (d.network.competitionGroups || []).map((ids, i) => ({ key: `g${i}`, label: ids.length > 1 ? `${T('Competing group', 'مجموعة متنافسة')} ${i + 1}` : '', ids: ids.filter(id => byId.has(id)) })).filter(g => g.ids.length);
      const multi = groups.filter(g => g.ids.length > 1), single = groups.filter(g => g.ids.length === 1).flatMap(g => g.ids);
      if (single.length) multi.push({ key: 'solo', label: T('No repeated competitor', 'دون منافس متكرر'), ids: single });
      return clusterLayout(multi, 0.15);
    }
    if (mode === 'tender') {
      const round = tenderRound(); const ids = round ? round.supplierIds.filter(id => byId.has(id)) : [];
      const base = universeMap(list);
      const front = new THREE.Vector3(0, 0.05, 1).normalize();
      // Bidders gather on one cap (rotated to face the viewer); everyone else keeps their universe place, dimmed.
      const c = st.tenderDir || front; const map = new Map(base); cap(c, ids, 0.2, map);
      return { map, anchors: [], focus: c, ids: new Set(ids) };
    }
    return { map: universeMap(list), anchors: [] };
  }
  function group(list, key) { const m = new Map(); list.forEach(x => { const k = key(x); if (!m.has(k)) m.set(k, []); m.get(k).push(x); }); return m; }
  function universeMap(list) {
    // Ordered by category then activity along a Fibonacci spiral: an even, readable spread with gentle category bands.
    const order = list.slice().sort((a, b) => a.cat.localeCompare(b.cat) || b.s.participations - a.s.participations || a.id - b.id);
    const map = new Map(); order.forEach((n, i) => map.set(n.id, fib(i, order.length).multiplyScalar(1)));
    return map;
  }
  function tenderRound() {
    const d = radar()?.data; if (!d) return null;
    const rounds = d.network.rounds.filter(r => r.supplierIds.length);
    if (!rounds.length) return null;
    return rounds.find(r => r.roundId === st.tender) || rounds[rounds.length - 1];
  }

  let layoutInfo = { map: new Map(), anchors: [] };
  const GROUPED = new Set(['category', 'competition']);
  function applyLayout(animate = true) {
    st.front = GROUPED.has(st.mode) ? frontDir() : null; st.sway = 0;
    layoutInfo = layoutFor(st.mode); anchors = layoutInfo.anchors || [];
    const now = performance.now();
    nodes.forEach(n => { n.from.copy(n.pos.lengthSq() ? n.pos : layoutInfo.map.get(n.id) || fib(0, 1)); n.to.copy(layoutInfo.map.get(n.id) || n.from); if (!animate || !n.pos.lengthSq()) n.pos.copy(n.to); });
    st.trans = animate && !reduced.matches ? { t0: now, dur: 1250 } : null;
    if (!st.trans) nodes.forEach(n => n.pos.copy(n.to));
    // Focused modes turn their subject towards the viewer and hold the orbit.
    if (st.mode === 'tender' && layoutInfo.focus) faceDirection(layoutInfo.focus, HOME.focus + 0.25);
    updateEmphasis(); rebuildLinks(); renderLegend(); renderCaption(); wake();
  }

  // ---------- emphasis (selection, hover, filters, mode subject) ----------
  function visibleSet() { return radar()?.filteredSupplierIds?.() || null; }
  function updateEmphasis() {
    const filt = visibleSet(); const sel = new Set(st.selected); const neigh = neighbours(st.selected);
    nodes.forEach(n => {
      n.visT = !filt || filt.has(n.id) ? 1 : 0;
      let a = 1;
      if (sel.size) a = sel.has(n.id) ? 1 : neigh.has(n.id) ? 0.9 : 0.22;
      else if (st.mode === 'tender' && layoutInfo.ids) a = layoutInfo.ids.has(n.id) ? 1 : 0.16;
      n.alphaT = a;
    });
  }
  function neighbours(ids) {
    const out = new Set(); if (!ids.length) return out; const d = radar()?.data; if (!d) return out;
    const set = new Set(ids);
    d.network.competitionLinks.forEach(l => { if (set.has(l.supplierA)) out.add(l.supplierB); if (set.has(l.supplierB)) out.add(l.supplierA); });
    return out;
  }

  // ---------- links (arcs along the sphere; built only for what is relevant) ----------
  let linkObjs = [], flows = [];
  function arcCurve(a, b, lift) {
    const pts = []; const A = a.clone().normalize(), B = b.clone().normalize(); const ang = A.angleTo(B);
    const h = lift ?? (0.025 + 0.11 * (ang / Math.PI));
    for (let i = 0; i <= 32; i++) {
      const t = i / 32; let p;
      if (ang < 1e-3) p = A.clone(); else { const s = Math.sin(ang); p = A.clone().multiplyScalar(Math.sin((1 - t) * ang) / s).add(B.clone().multiplyScalar(Math.sin(t * ang) / s)); }
      pts.push(p.normalize().multiplyScalar(R * (1.012 + h * Math.sin(Math.PI * t))));
    }
    return new THREE.CatmullRomCurve3(pts);
  }
  function hubCurve(hub, b) { const mid = hub.clone().add(b).multiplyScalar(0.5); mid.normalize().multiplyScalar(Math.max(hub.length(), b.length()) * 1.04); return new THREE.QuadraticBezierCurve3(hub.clone(), mid, b.clone().multiplyScalar(1.01)); }
  function addTube(curve, color, width, opacity, meta) {
    const geo = new THREE.TubeGeometry(curve, 40, width, 5, false);
    const mat = new THREE.MeshBasicMaterial({ color, transparent: true, opacity, depthWrite: false, blending: P.dark ? THREE.AdditiveBlending : THREE.NormalBlending });
    const mesh = new THREE.Mesh(geo, mat); mesh.renderOrder = 4; world.add(mesh);
    const obj = { mesh, curve, meta, base: opacity }; linkObjs.push(obj); return obj;
  }
  function addFlow(curve, color, count = 2, speed = 0.22) {
    for (let i = 0; i < count; i++) {
      const sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: dotTex, color, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, opacity: 0.95 }));
      sp.scale.setScalar(0.042); sp.renderOrder = 6; world.add(sp); flows.push({ sp, curve, t: i / count, speed });
    }
  }
  function clearLinks() {
    linkObjs.forEach(o => { world.remove(o.mesh); o.mesh.geometry.dispose(); o.mesh.material.dispose(); }); linkObjs = [];
    flows.forEach(f => { world.remove(f.sp); f.sp.material.dispose(); }); flows = [];
    hubs.forEach(h => { world.remove(h.obj); h.obj.material?.dispose?.(); }); hubs = [];
  }
  const posOf = id => byId.get(id)?.to;
  function rebuildLinks() {
    clearLinks();
    const d = radar()?.data; if (!d || !nodes.length || st.view !== '3d') return;
    const filt = visibleSet(); const ok = id => byId.has(id) && (!filt || filt.has(id));
    const sel = new Set(st.selected);
    const focusIds = sel.size ? sel : st.hover ? new Set([st.hover]) : null;
    const min = d.network.competitionMinimum || 2;
    const maxRounds = Math.max(1, ...d.network.competitionLinks.map(l => l.roundIds.length));
    const competition = (only, alpha) => d.network.competitionLinks.forEach(l => {
      if (!ok(l.supplierA) || !ok(l.supplierB)) return;
      if (only && !only.has(l.supplierA) && !only.has(l.supplierB)) return;
      if (!only && l.roundIds.length < min) return;
      const w = 0.0019 + 0.0036 * (l.roundIds.length / maxRounds);
      const c = arcCurve(posOf(l.supplierA), posOf(l.supplierB));
      addTube(c, P.link, w, alpha * 0.85, { type: 'competition', a: l.supplierA, b: l.supplierB, rounds: l.roundIds.length });
      if (only && !reduced.matches) addFlow(c, P.link, 1 + Math.min(2, l.roundIds.length - 1));
    });
    if (st.mode === 'tender') {
      const round = tenderRound(); if (!round) return;
      const ids = round.supplierIds.filter(ok); if (!ids.length) return;
      const centre = ids.reduce((v, id) => v.add(posOf(id)), new THREE.Vector3()).normalize();
      const [, upT] = basis(centre); const hubPos = centre.clone().multiplyScalar(R * 1.28).addScaledVector(upT, 0.34);
      const hub = new THREE.Mesh(new THREE.OctahedronGeometry(0.075), new THREE.MeshBasicMaterial({ color: P.orange, transparent: true, opacity: 0.95 }));
      hub.position.copy(hubPos); hub.renderOrder = 6; world.add(hub); hubs.push({ obj: hub, pos: hubPos, label: `${clean(round.tender)} · ${cycleLabel(round.roundNumber)}`, kind: 'tender' });
      ids.forEach(id => {
        const won = round.awardedSupplierId === id; const c = hubCurve(hubPos, posOf(id));
        addTube(c, won ? P.award : P.link, won ? 0.0065 : 0.0038, won ? 0.95 : 0.6, { type: 'offer', a: id, won });
        if (!reduced.matches) addFlow(c, won ? P.award : P.link, 1, 0.3);
      });
      return;
    }
    if (st.mode === 'competition' && st.links) competition(focusIds, focusIds ? 0.85 : 0.5);
    else if (focusIds && (st.links || sel.size)) competition(focusIds, 0.8);
  }

  // ---------- camera & orientation ----------
  function faceDirection(localDir, dist) {
    const cur = localDir.clone().applyQuaternion(world.quaternion);
    const want = new THREE.Vector3(0, 0.04, 1).normalize();
    st.qTarget = new THREE.Quaternion().setFromUnitVectors(cur.normalize(), want).multiply(world.quaternion.clone());
    st.distTarget = dist ?? HOME.focus;
  }
  function home() { st.qTarget = null; st.distTarget = HOME.dist; }

  // ---------- labels (DOM: crisp 14-15px text, collision-aware, front hemisphere only) ----------
  const labelEls = new Map();
  function labelEl(key, html, cls) {
    let el = labelEls.get(key);
    if (!el) { el = document.createElement('div'); el.className = `oi-label ${cls || ''}`; labelLayer.append(el); labelEls.set(key, el); el._html = ''; }
    if (el._html !== html) { el.innerHTML = html; el._html = html; el._w = 0; }
    if (cls != null && el._cls !== cls) { el.className = `oi-label ${cls}`; el._cls = cls; el._w = 0; }
    return el;
  }
  const v3 = new THREE.Vector3();
  function project(worldPos, out) {
    v3.copy(worldPos).project(camera);
    out.x = (v3.x * 0.5 + 0.5) * size.w; out.y = (-v3.y * 0.5 + 0.5) * size.h; out.z = v3.z; return out;
  }
  function updateLabels() {
    const used = new Set(); const boxes = [];
    const place = (key, html, cls, sx, sy, rpx, prio, alpha, side) => {
      const el = labelEl(key, html, cls);
      if (!el._w) { el._w = el.offsetWidth; el._h = el.offsetHeight; }
      const w = el._w, h = el._h;
      let x = side === 'center' ? sx - w / 2 : sx + rpx + 9, y = side === 'center' ? sy - h / 2 : sy - h / 2;
      const limitR = Math.min(size.w, st.freeR ?? size.w) - 8, limitL = Math.max(0, st.freeL ?? 0) + 8;
      if (prio < 4 && side !== 'center' && (sx < limitL || sx > limitR)) return; // node hidden behind an overlay
      if (side !== 'center' && x + w > limitR) {
        // Flip to the other side; if neither side has room (panel and inspector), sit centred above the node.
        if (sx - rpx - 9 - w >= limitL) x = sx - rpx - 9 - w; else { x = Math.max(limitL, Math.min(limitR - w, sx - w / 2)); y = sy - rpx - h - 10; }
      }
      const box = { x1: x - 3, y1: y - 2, x2: x + w + 3, y2: y + h + 2 };
      if (prio < 4 && boxes.some(b => box.x1 < b.x2 && box.x2 > b.x1 && box.y1 < b.y2 && box.y2 > b.y1)) return;
      if (x < 2 || y < 2 || x + w > size.w - 2 || y + h > size.h - 2) { if (prio < 3) return; x = Math.max(2, Math.min(size.w - w - 2, x)); y = Math.max(2, Math.min(size.h - h - 2, y)); }
      boxes.push(box); used.add(key);
      el.style.transform = `translate3d(${Math.round(x)}px, ${Math.round(y)}px, 0)`; el.style.opacity = alpha.toFixed(2);
    };
    if (st.view === '3d' && st.ready) {
      const sel = new Set(st.selected); const neigh = neighbours(st.selected);
      const filt = visibleSet();
      // Priority: selected > hovered > anchors > neighbours > most active suppliers.
      const cands = [];
      nodes.forEach(n => {
        if (n.vis < 0.3) return;
        const sc = n.screen; const isSel = sel.has(n.id), isHover = st.hover === n.id;
        if (!isSel && !isHover && sc.front < 0.15) return;
        let prio = isSel ? 4 : isHover ? 3.5 : 0;
        if (!prio && !st.labels) return;
        if (!prio && neigh.has(n.id)) prio = 2;
        if (!prio && st.mode === 'tender' && layoutInfo.ids?.has(n.id)) prio = 2.2;
        if (!prio && !sel.size && st.mode !== 'tender' && sc.front > 0.18) prio = n.s.participations > 0 ? 1 + Math.min(0.9, n.s.participations / 20) : sc.front > 0.45 ? 0.6 : 0;
        if (!prio) return;
        if (sel.size && !isSel && !neigh.has(n.id) && !isHover) return;
        if (filt && !filt.has(n.id) && !isSel) return;
        cands.push({ n, prio });
      });
      // Group headings and hubs are placed first so supplier names never crowd them out.
      if (!sel.size) anchors.forEach((a, i) => {
        if (!a.label) return;
        const wp = (a.labelDir || a.dir).clone().multiplyScalar(R * 1.04).applyQuaternion(world.quaternion);
        const facing = wp.clone().normalize().dot(camera.position.clone().normalize());
        if (facing < 0.05) return;
        const p = project(wp, {}); const html = `${a.color ? `<i style="background:#${a.color.getHexString()}"></i>` : ''}<b>${esc(a.label)}</b><small>${a.count}</small>`;
        place(`a${i}-${a.key}`, html, 'is-anchor', p.x, p.y, 0, 5, Math.min(1, facing * 1.6), 'center');
      });
      hubs.forEach((h, i) => {
        const wp = h.pos.clone().applyQuaternion(world.quaternion); const p = project(wp, {});
        place(`h${i}`, `<b>${esc(h.label)}</b>`, `is-hub is-${h.kind}`, p.x, p.y, 12, 3.9, 1, 'side');
      });
      cands.sort((a, b) => b.prio - a.prio || b.n.s.participations - a.n.s.participations);
      cands.slice(0, sel.size ? 14 : 16).forEach(({ n, prio }) => {
        const isSel = sel.has(n.id);
        const meta = isSel ? `<small>${fmtInt(n.s.participations)} ${T('bidding cycles', 'دورات عطاء')} · ${fmtInt(n.s.wins)} ${T('awards', 'ترسيات')}</small>` : '';
        const html = `<i style="background:${radar()?.catColor?.(n.cat)}"></i><span>${esc(clean(n.s.name))}</span>${meta}`;
        place(`s${n.id}`, html, isSel ? 'is-selected' : st.hover === n.id ? 'is-hover' : '', n.screen.x, n.screen.y, n.screen.rpx, prio, Math.min(1, 0.35 + n.screen.front) * Math.min(1, n.alpha + 0.25), 'side');
      });
    }
    labelEls.forEach((el, key) => { if (!used.has(key)) el.style.opacity = '0'; });
  }

  // ---------- frame ----------
  const size = { w: 1, h: 1 };
  function resize() {
    const r = stage.getBoundingClientRect(); if (!r.width || !r.height) return false;
    if (r.width === size.w && r.height === size.h) return true;
    size.w = r.width; size.h = r.height;
    renderer.setSize(size.w, size.h, false); camera.aspect = size.w / size.h;
    // Keep the whole sphere in view on narrow stages.
    HOME.dist = size.w / size.h < 1 ? 4.15 / Math.max(0.62, size.w / size.h) : 4.15;
    if (!st.selected.length && st.mode !== 'tender') st.distTarget = HOME.dist;
    camera.updateProjectionMatrix(); labelEls.forEach(el => (el._w = 0)); st.offsetTarget = centreOffset(); return true;
  }
  new ResizeObserver(() => { resize(); wake(); }).observe(stage);
  // No frames while the universe is scrolled out of view, hidden in another tab or the page is in the background.
  let inView = true;
  new IntersectionObserver(es => { inView = es[es.length - 1].isIntersecting; if (inView) wake(); }).observe(stage);

  let raf = 0, last = performance.now(), clock = 0;
  const ease = t => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);
  const camDir = new THREE.Vector3(), tmp = new THREE.Vector3(), q = new THREE.Quaternion(), Y = new THREE.Vector3(0, 1, 0), X = new THREE.Vector3(1, 0, 0);
  function frame(now) {
    raf = 0;
    if (!st.visible || !inView || st.view !== '3d' || !resize()) return;
    const dt = Math.min(0.05, (now - last) / 1000); last = now;
    const moving = st.motion && !reduced.matches;
    if (moving) clock += dt;
    // Orbit: slow continuous rotation unless paused, dragging, focused or recently touched.
    const focused = st.selected.length > 0 || st.mode === 'tender';
    if (!st.drag) {
      if (Math.abs(st.vel.x) + Math.abs(st.vel.y) > 1e-4) {
        q.setFromAxisAngle(Y, st.vel.x); world.quaternion.premultiply(q); q.setFromAxisAngle(X, st.vel.y); world.quaternion.premultiply(q);
        st.vel.x *= 0.92; st.vel.y *= 0.92; st.qTarget = null;
      } else if (st.qTarget) {
        world.quaternion.slerp(st.qTarget, reduced.matches ? 1 : 1 - Math.pow(0.002, dt));
        if (world.quaternion.angleTo(st.qTarget) < 1e-3) world.quaternion.copy(st.qTarget);
      } else if (moving && !focused && now - st.lastInteract > 2200) {
        if (GROUPED.has(st.mode)) { const sw = Math.sin(clock * 0.21) * 0.2; q.setFromAxisAngle(Y, sw - (st.sway || 0)); st.sway = sw; world.quaternion.premultiply(q); }
        else { q.setFromAxisAngle(Y, dt * (st.hover ? 0.018 : 0.075)); world.quaternion.premultiply(q); }
      }
    }
    // Camera distance and horizontal offset (room for the inspector).
    const d0 = camera.position.z; const dz = st.distTarget - d0; camera.position.z = Math.abs(dz) < 1e-3 || reduced.matches ? st.distTarget : d0 + dz * (1 - Math.pow(0.004, dt));
    st.offset += (st.offsetTarget - st.offset) * (reduced.matches ? 1 : 1 - Math.pow(0.004, dt));
    if (Math.abs(st.offset) > 0.5) camera.setViewOffset(size.w, size.h, st.offset, 0, size.w, size.h); else camera.clearViewOffset();
    camera.updateMatrixWorld();
    // Layout transition: glide along the sphere surface.
    let animating = false;
    if (st.trans) { const t = Math.min(1, (now - st.trans.t0) / st.trans.dur); const e = ease(t); nodes.forEach(n => n.pos.copy(n.from).lerp(n.to, e).normalize()); if (t >= 1) st.trans = null; else animating = true; }
    camDir.copy(camera.position).normalize();
    nodes.forEach(n => {
      n.alpha += (n.alphaT - n.alpha) * 0.12; n.vis += (n.visT - n.vis) * 0.14;
      if (Math.abs(n.alphaT - n.alpha) > 0.01 || Math.abs(n.visT - n.vis) > 0.01) animating = true;
      const breathe = moving ? 1 + 0.012 * Math.sin(clock * 0.9 + n.phase) : 1;
      const lift = R * 1.015 * breathe;
      n.mesh.position.copy(n.pos).multiplyScalar(lift); n.halo.position.copy(n.mesh.position); n.ring.position.copy(n.mesh.position);
      const sel = st.selected.includes(n.id), hov = st.hover === n.id;
      const s = n.r * (sel ? 1.45 : hov ? 1.3 : 1) * Math.max(0.001, n.vis);
      n.mesh.scale.setScalar(s); n.halo.scale.setScalar(s * (P.dark ? 4.2 : 3.2));
      tmp.copy(n.mesh.position).applyQuaternion(world.quaternion);
      const facing = tmp.clone().normalize().dot(camDir); // 1 front, -1 back
      const depth = 0.28 + 0.72 * Math.min(1, Math.max(0, (facing + 0.35) / 0.9));
      const a = n.alpha * depth * n.vis;
      n.mesh.material.uniforms.uOpacity.value = a; n.mesh.material.uniforms.uGlow.value = sel || hov ? 0.35 : 0;
      n.halo.material.opacity = a * (P.dark ? (sel || hov ? 0.85 : 0.5) : (sel || hov ? 0.4 : 0.18));
      const ringOn = sel ? 1 : hov ? 0.55 : 0; n.ring.material.opacity += (ringOn * n.vis - n.ring.material.opacity) * 0.2; n.ring.scale.setScalar(s * 3.2 * (sel && moving ? 1 + 0.06 * Math.sin(clock * 3) : 1));
      project(tmp, n.screen); n.screen.front = facing;
      const edge = tmp.clone().add(new THREE.Vector3(s, 0, 0)); n.screen.rpx = Math.abs(project(edge, {}).x - n.screen.x);
    });
    flows.forEach(f => { if (moving) f.t = (f.t + dt * f.speed) % 1; f.sp.position.copy(f.curve.getPointAt(f.t)); });
    linkObjs.forEach(o => { o.mesh.material.opacity = o.base * (st.trans ? 0 : 1); });
    if (st.trans && linkObjs.length) animating = true;
    orbitRings.forEach(o => { const a = o.phase + clock * o.speed * 6; o.spark.position.set(Math.cos(a) * o.rad, 0, Math.sin(a) * o.rad); });
    hubs.forEach(h => { if (moving) h.obj.rotation.y += dt * 0.8; });
    renderer.render(scene, camera);
    updateLabels();
    const settling = (st.qTarget && world.quaternion.angleTo(st.qTarget) > 1e-3) || Math.abs(camera.position.z - st.distTarget) > 1e-3 || Math.abs(st.offset - st.offsetTarget) > 0.5 || Math.abs(st.vel.x) + Math.abs(st.vel.y) > 1e-4;
    if (moving || animating || settling || st.drag) wake();
  }
  function wake() { if (!raf) raf = requestAnimationFrame(frame); }

  // ---------- picking & interaction ----------
  function pick(px, py) {
    let best = null, bestD = Infinity;
    nodes.forEach(n => {
      if (n.vis < 0.5 || n.alpha < 0.12) return;
      const dx = n.screen.x - px, dy = n.screen.y - py; const d = Math.hypot(dx, dy); const reach = Math.max(14, n.screen.rpx + 6);
      if (d > reach) return;
      const score = d - n.screen.front * 8; // prefer front nodes when overlapping
      if (score < bestD) { bestD = score; best = n; }
    });
    return best;
  }
  function pickCompetitionLink(px, py) {
    if (st.mode !== 'competition' || st.view !== '3d') return null;
    world.updateMatrixWorld(); camera.updateMatrixWorld();
    let best = null, distance = 13;
    for (const link of linkObjs) {
      if (link.meta?.type !== 'competition') continue;
      let previous = null;
      for (let i = 0; i <= 20; i++) {
        const p = link.curve.getPoint(i / 20).applyMatrix4(world.matrixWorld).project(camera);
        if (p.z < -1 || p.z > 1) { previous = null; continue; }
        const current = { x: (p.x + 1) * size.w / 2, y: (1 - p.y) * size.h / 2 };
        if (previous) {
          const vx = current.x - previous.x, vy = current.y - previous.y;
          const t = Math.max(0, Math.min(1, ((px - previous.x) * vx + (py - previous.y) * vy) / (vx * vx + vy * vy || 1)));
          const d = Math.hypot(px - previous.x - t * vx, py - previous.y - t * vy);
          if (d < distance) { best = link.meta; distance = d; }
        }
        previous = current;
      }
    }
    return best;
  }
  const local = e => { const r = stage.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; };
  stage.addEventListener('pointerdown', e => {
    if (e.button !== 0) return; stage.setPointerCapture(e.pointerId);
    const p = local(e); st.drag = { x: p.x, y: p.y, x0: p.x, y0: p.y, moved: false }; st.vel = { x: 0, y: 0 }; st.lastInteract = performance.now(); wake();
  });
  stage.addEventListener('pointermove', e => {
    const p = local(e);
    if (st.drag) {
      const dx = p.x - st.drag.x, dy = p.y - st.drag.y; st.drag.x = p.x; st.drag.y = p.y;
      if (Math.hypot(p.x - st.drag.x0, p.y - st.drag.y0) > 4) st.drag.moved = true;
      if (st.drag.moved) {
        const k = 3.2 / Math.max(300, size.h);
        q.setFromAxisAngle(Y, dx * k); world.quaternion.premultiply(q); q.setFromAxisAngle(X, dy * k); world.quaternion.premultiply(q);
        st.vel = { x: dx * k, y: dy * k }; st.qTarget = null; tip.hidden = true; wake();
      }
      return;
    }
    const n = pick(p.x, p.y);
    if ((n?.id ?? null) !== st.hover) { st.hover = n?.id ?? null; stage.style.cursor = n ? 'pointer' : 'grab'; updateEmphasis(); if (!st.selected.length) rebuildLinks(); wake(); }
    if (n) showTip(n, p); else tip.hidden = true;
  });
  stage.addEventListener('pointerleave', () => { if (st.hover != null) { st.hover = null; tip.hidden = true; if (!st.selected.length) rebuildLinks(); wake(); } });
  stage.addEventListener('pointerup', e => {
    const drag = st.drag; st.drag = null; st.lastInteract = performance.now();
    if (!drag || drag.moved) { wake(); return; }
    const p = local(e); const n = pick(p.x, p.y);
    if (n) choose(n.id, e.shiftKey || e.ctrlKey || e.metaKey);
    else {
      const link = pickCompetitionLink(p.x, p.y);
      if (link) { radar()?.select?.([link.a, link.b], { source: 'universe' }); applySelection([link.a, link.b], true); }
      else if (st.selected.length) choose(null);
    }
  });
  stage.addEventListener('wheel', e => {
    e.preventDefault(); st.lastInteract = performance.now();
    st.distTarget = Math.min(7.5, Math.max(2.15, st.distTarget * (1 + Math.sign(e.deltaY) * 0.08))); wake();
  }, { passive: false });
  stage.addEventListener('dblclick', e => { const n = pick(...Object.values(local(e))); if (n) { choose(n.id); faceDirection(n.to, 2.6); wake(); } });
  function showTip(n, p) {
    const s = n.s;
    tip.innerHTML = `<b>${esc(clean(s.name))}</b><span><i style="background:${radar()?.catColor?.(n.cat)}"></i>${esc(clean(n.cat))} · ${esc(s.status)}</span>
      <span>${fmtInt(s.participations)} ${T('Bidding Cycles Participated In', 'دورات العطاء التي شارك فيها')} · ${fmtInt(s.wins)} ${T('awards', 'ترسيات')}</span><em>${T('Click to focus · Shift+click to compare', 'انقر للتركيز · Shift للمقارنة')}</em>`;
    tip.hidden = false;
    const w = tip.offsetWidth, h = tip.offsetHeight;
    let x = p.x + 18, y = p.y + 14; if (x + w > size.w - 8) x = p.x - w - 18; if (y + h > size.h - 8) y = p.y - h - 14;
    tip.style.transform = `translate(${Math.round(x)}px, ${Math.round(y)}px)`;
  }
  // Keyboard: arrows move between suppliers (screen order), Enter selects, Space pauses, Escape steps back.
  let kbIndex = -1;
  stage.addEventListener('keydown', e => {
    const list = nodes.filter(n => n.vis > 0.5).sort((a, b) => a.screen.x - b.screen.x || a.screen.y - b.screen.y);
    if (['ArrowRight', 'ArrowDown', 'ArrowLeft', 'ArrowUp'].includes(e.key)) {
      e.preventDefault(); if (!list.length) return;
      const cur = list.findIndex(n => n.id === st.hover); kbIndex = (cur < 0 ? 0 : cur + (e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : -1) + list.length) % list.length;
      const n = list[kbIndex]; st.hover = n.id; faceDirection(n.to, st.selected.length ? HOME.focus : HOME.dist - 0.3); updateEmphasis(); rebuildLinks(); announce(`${clean(n.s.name)}, ${clean(n.cat)}, ${n.s.participations} ${T('bidding cycles', 'دورات عطاء')}`); wake();
    } else if (e.key === 'Enter' && st.hover != null) { e.preventDefault(); choose(st.hover, e.shiftKey); }
    else if (e.key === ' ') { e.preventDefault(); setMotion(!st.motion); }
    else if (e.key === 'Escape') { e.preventDefault(); if (st.selected.length) choose(null); else reset(); }
  });
  const live = $('[data-uni-caption]');
  const announce = t => { live.dataset.sr = t; live.setAttribute('aria-label', t); };

  function choose(id, additive = false) {
    st.lastInteract = performance.now();
    let ids;
    if (id == null) ids = [];
    else if (additive) ids = st.selected.includes(id) ? st.selected.filter(x => x !== id) : [...st.selected, id].slice(-5);
    else ids = st.selected.length === 1 && st.selected[0] === id ? [] : [id];
    radar()?.select?.(ids, { source: 'universe' }); // shared selection: charts, 2D network, AI context
    applySelection(ids, true);
  }
  function applySelection(ids, fromUser = false) {
    const same = ids.length === st.selected.length && ids.every((x, i) => x === st.selected[i]);
    st.selected = ids.filter(id => byId.has(id));
    updateEmphasis(); rebuildLinks();
    if (st.selected.length) {
      const c = st.selected.reduce((v, id) => v.add(byId.get(id).to), new THREE.Vector3()).normalize();
      faceDirection(c, st.selected.length > 1 ? HOME.focus + 0.3 : HOME.focus);
      openInspector();
    } else { closeInspector(); if (st.mode !== 'tender') home(); }
    if (!same || fromUser) wake();
  }

  // ---------- inspector ----------
  let inspTab = 'overview';
  // The sphere is centred in the free space between the overlays (panel, tool rail, inspector), in LTR and RTL alike.
  const panelEl = $('.oi-uni-panel'), toolsEl = $('.oi-uni-tools');
  function centreOffset() {
    if (size.w < 1000) return 0;
    const sr = stage.getBoundingClientRect(); let left = 0, right = sr.width;
    // With the inspector open the tool rail becomes a top row, so it no longer narrows the free area.
    [panelEl, inspector.hidden ? toolsEl : inspector].forEach(el => {
      if (!el || el.offsetParent === null) return; const r = el.getBoundingClientRect(); if (!r.width) return;
      if ((r.left + r.right) / 2 - sr.left < sr.width / 2) left = Math.max(left, r.right - sr.left); else right = Math.min(right, r.left - sr.left);
    });
    st.freeL = left; st.freeR = right;
    return Math.round(sr.width / 2 - (left + right) / 2);
  }
  const recentre = () => { st.offsetTarget = centreOffset(); wake(); };
  function openInspector() {
    inspector.hidden = false; host.classList.add('has-inspector');
    requestAnimationFrame(recentre); setTimeout(recentre, 450);
    renderInspector();
  }
  function closeInspector() { inspector.hidden = true; host.classList.remove('has-inspector'); requestAnimationFrame(recentre); setTimeout(recentre, 450); }
  function kv(rows) { return `<dl class="oi-kv">${rows.map(([icon, k, v]) => `<div><dt><svg viewBox="0 0 24 24" aria-hidden="true">${icon}</svg>${k}</dt><dd>${v}</dd></div>`).join('')}</dl>`; }
  const IC = {
    status: '<circle cx="12" cy="12" r="9"/><path d="m8 12 3 3 5-6"/>', cat: '<path d="M3 7l9-4 9 4-9 4zM3 12l9 4 9-4"/>', inv: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/>',
    rounds: '<path d="M4 12a8 8 0 0 1 14-5l2 2M20 4v5h-5M20 12a8 8 0 0 1-14 5l-2-2M4 20v-5h5"/>', rate: '<path d="M4 20h16M7 17v-5M12 17V8M17 17v-9"/>', award: '<path d="M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0z"/>',
    money: '<rect x="3" y="6" width="18" height="12" rx="2"/><circle cx="12" cy="12" r="2.5"/>', cal: '<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/>', price: '<path d="M4 4h7l9 9-7 7-9-9z"/><circle cx="8.5" cy="8.5" r="1.5"/>'
  };
  function renderInspector() {
    const d = radar()?.data; if (!d || !st.selected.length) return;
    const body = $('[data-insp-body]'), foot = $('[data-insp-foot]');
    $$('[data-insp-tab]').forEach(b => b.setAttribute('aria-selected', String(b.dataset.inspTab === inspTab)));
    if (st.selected.length > 1) {
      // Compare (up to five): one table, same definitions as the charts.
      const ss = st.selected.map(id => byId.get(id).s);
      $('[data-insp-title]').textContent = `${T('Comparing', 'مقارنة')} ${ss.length} ${T('suppliers', 'موردين')}`;
      $('[data-insp-sub]').textContent = T('Shift+click a supplier to add or remove it', 'Shift+نقر لإضافة أو إزالة مورد');
      $('[data-insp-status]').textContent = ''; $('[data-insp-status]').hidden = true; $('[data-insp-tabs]').hidden = true;
      const rows = [[T('Category', 'الفئة'), s => esc(clean(s.category))], [T('Invitations', 'الدعوات'), s => fmtInt(s.invitations)], [T('Bidding Cycles Participated In', 'دورات العطاء التي شارك فيها'), s => fmtInt(s.participations)],
        [T('Response rate', 'معدل الاستجابة'), s => fmtPct(s.participationRate)], [T('Official awards', 'الترسيات'), s => fmtInt(s.wins)], [T('Win rate', 'معدل الفوز'), s => fmtPct(s.winRate)], [T('Price vs median', 'السعر مقابل الوسيط'), s => fmtPct(s.relativePrice, true)]];
      const shared = d.network.rounds.filter(r => st.selected.every(id => r.supplierIds.includes(id)));
      body.innerHTML = `<div class="oi-compare"><table><thead><tr><th></th>${ss.map(s => `<th><i style="background:${radar()?.catColor?.(s.category)}"></i>${esc(clean(s.name))}</th>`).join('')}</tr></thead><tbody>${rows.map(([k, f]) => `<tr><th>${k}</th>${ss.map(s => `<td>${f(s)}</td>`).join('')}</tr>`).join('')}</tbody></table></div>
        <p class="oi-note">${T('Shared Bidding Cycles', 'دورات العطاء المشتركة')}: <b>${shared.length}</b></p><ul class="oi-rounds">${shared.map(r => `<li><button type="button" data-insp-round="${r.roundId}"><span><b>${esc(clean(r.tender))}</b><small>${cycleLabel(r.roundNumber)} · ${fmtDate(r.availableAt)} · ${esc(r.department)}</small></span></button></li>`).join('')}</ul>`;
      foot.innerHTML = ss.length === 2 ? `<button type="button" class="rd-btn" data-insp-act="path">${T('Show path (2D)', 'إظهار المسار')}</button>` : '';
      return;
    }
    $('[data-insp-tabs]').hidden = false; $('[data-insp-status]').hidden = false;
    const n = byId.get(st.selected[0]); const s = n.s;
    $('[data-insp-title]').textContent = clean(s.name);
    $('[data-insp-sub]').innerHTML = `<i style="background:${radar()?.catColor?.(n.cat)}"></i>${esc(clean(n.cat))}`;
    const status = $('[data-insp-status]'); status.textContent = s.status; status.className = `oi-pill ${/approved/i.test(s.status) ? 'is-ok' : 'is-muted'}`;
    $('[data-insp-icon]').style.setProperty('--c', radar()?.catColor?.(n.cat));
    const rounds = d.network.rounds.filter(r => r.supplierIds.includes(s.supplierId));
    if (inspTab === 'participation') {
      const max = Math.max(1, ...rounds.map(r => r.supplierIds.length));
      body.innerHTML = rounds.length ? `<ul class="oi-rounds">${rounds.slice().reverse().map(r => {
        const won = r.awardedSupplierId === s.supplierId, other = r.awardedSupplierId && !won;
        return `<li><button type="button" data-insp-round="${r.roundId}"><span><b>${esc(clean(r.tender))}</b><small>${cycleLabel(r.roundNumber)} · ${fmtDate(r.availableAt)} · ${esc(r.department)}</small></span>
          <i class="oi-bar" style="--w:${(r.supplierIds.length / max) * 100}%"><u></u></i><em>${r.supplierIds.length} ${T('offers', 'عروض')}</em>${won ? `<strong class="oi-pill is-award">${T('Awarded', 'مُرسى')}</strong>` : other ? '' : ''}</button></li>`;
      }).join('')}</ul>` : `<p class="oi-empty-note">${T('No offer in a closed round in this period. Invitations', 'لا عروض في جولات مغلقة ضمن هذه الفترة. الدعوات')}: ${fmtInt(s.invitations)}.</p>`;
    } else if (inspTab === 'pricing') {
      const pos = d.positions.filter(p => p.supplierId === s.supplierId).sort((a, b) => Date.parse(a.date) - Date.parse(b.date));
      const max = Math.max(0.05, ...pos.map(p => Math.abs(p.relative)));
      body.innerHTML = (s.relativePrice != null ? `<div class="oi-big"><strong class="${s.relativePrice > 0 ? 'is-up' : 'is-down'}">${fmtPct(s.relativePrice, true)}</strong><span>${T('average vs. round median', 'المتوسط مقابل وسيط الجولة')} · ${s.pricedRounds} ${T('priced rounds', 'جولات مسعّرة')}</span></div>` : '') +
        (pos.length ? `<ul class="oi-diverge">${pos.map(p => { const t = d.network.rounds.find(r => r.roundId === p.roundId); return `<li><span>${esc(clean(t?.tender || `#${p.tenderId}`))}<small>${fmtDate(p.date)} · ${esc(p.currency)} ${Number(p.total).toLocaleString('en-US')}</small></span><i><u class="${p.relative > 0 ? 'is-up' : 'is-down'}" style="--w:${(Math.abs(p.relative) / max) * 50}%"></u></i><b>${fmtPct(p.relative, true)}</b></li>`; }).join('')}</ul>`
          : `<p class="oi-empty-note">${T('No comparable priced round (needs another offer in the same currency).', 'لا توجد جولة مسعّرة قابلة للمقارنة.')}</p>`);
    } else if (inspTab === 'relationships') {
      const links = d.network.competitionLinks.filter(l => l.supplierA === s.supplierId || l.supplierB === s.supplierId).map(l => ({ id: l.supplierA === s.supplierId ? l.supplierB : l.supplierA, n: l.roundIds.length })).sort((a, b) => b.n - a.n);
      body.innerHTML = `<h4>${T('Competes with', 'يتنافس مع')}</h4>` + (links.length ? `<ul class="oi-peers">${links.map(l => { const p = byId.get(l.id)?.s; return `<li><button type="button" data-insp-peer="${l.id}"><i style="background:${radar()?.catColor?.(p?.category)}"></i>${esc(clean(p?.name || `#${l.id}`))}<em>${l.n} ${T('shared rounds', 'جولات مشتركة')}</em></button></li>`; }).join('')}</ul>` : `<p class="oi-empty-note">${T('No shared closed round with another supplier.', 'لا جولات مغلقة مشتركة مع مورد آخر.')}</p>`);
    } else {
      const award = s.awardedValue?.[0];
      body.innerHTML = kv([
        [IC.status, T('Status', 'الحالة'), esc(s.status)], [IC.cat, T('Business category', 'فئة النشاط'), esc(clean(n.cat))], [IC.inv, T('Invitations', 'الدعوات'), fmtInt(s.invitations)],
        [IC.rounds, T('Bidding Cycles Participated In', 'دورات العطاء التي شارك فيها'), fmtInt(s.participations)], [IC.rate, T('Response rate', 'معدل الاستجابة'), fmtPct(s.participationRate)],
        [IC.award, T('Official awards', 'الترسيات الرسمية'), `${fmtInt(s.wins)}${s.winRate != null ? ` <small>(${fmtPct(s.winRate)})</small>` : ''}`],
        [IC.money, T('Awarded value', 'القيمة المرساة'), award ? `${esc(award.currency)} ${Number(award.value).toLocaleString('en-US', { maximumFractionDigits: 0 })}` : '—'],
        [IC.price, T('Price vs. round median', 'السعر مقابل الوسيط'), fmtPct(s.relativePrice, true)],
        [IC.cal, T('Active', 'النشاط'), s.firstActivity ? `${fmtDate(s.firstActivity)} → ${fmtDate(s.lastActivity)}` : '—']
      ]);
    }
    foot.innerHTML = `<button type="button" class="rd-btn" data-insp-act="ask"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3l1.8 4.7 4.7 1.8-4.7 1.8L12 16l-1.8-4.7-4.7-1.8 4.7-1.8z"/></svg>${T('Ask AI', 'اسأل الذكاء')}</button>
      <button type="button" class="rd-btn" data-insp-act="focus">${T('Focus Supplier', 'التركيز على المورد')}</button>`;
  }
  inspector.addEventListener('click', e => {
    const b = e.target.closest('button'); if (!b) return;
    const v = b.dataset;
    if (b.hasAttribute('data-insp-close')) choose(null);
    else if (v.inspTab) { inspTab = v.inspTab; renderInspector(); }
    else if (v.inspPick) choose(+v.inspPick);
    else if (v.inspPeer) choose(+v.inspPeer, true);
    else if (v.inspRound) radar()?.openRoundTab?.(+v.inspRound);
    else if (v.inspAct === 'focus') { const c = st.selected.reduce((x, id) => x.add(byId.get(id).to), new THREE.Vector3()).normalize(); faceDirection(c, HOME.focus - 0.4); wake(); }
    else if (v.inspAct === 'path' && st.selected.length === 2) { setView('2d'); radar()?.tracePath?.(st.selected[0], st.selected[1]); }
    // Open the AI Studio with this supplier already chosen (intelligence-ai.js).
    else if (v.inspAct === 'ask') window.OrangeIntelligenceAI?.chooseSupplier(st.selected[0]);
  });

  // ---------- controls ----------
  const DESC = {
    universe: T('Every authorized supplier on one sphere. Placement is for overview only - it has no geographic or analytical meaning. Hover a supplier to see who it competes with.', 'كل مورد مصرح به على كرة واحدة. الموضع للعرض فقط ولا يحمل معنى جغرافياً أو تحليلياً. مرّر فوق مورد لرؤية منافسيه.'),
    category: T('Grouped by registered category. A category is a classification, not a relationship.', 'مجمّعة حسب الفئة المسجلة. الفئة تصنيف وليست علاقة.'),
    competition: T('Suppliers that submitted offers in the same closed rounds. Link width = number of shared rounds.', 'موردون قدّموا عروضاً في الجولات المغلقة نفسها. سماكة الرابط = عدد الجولات المشتركة.'),
    tender: T('One closed round: who submitted an offer, and the official award. Sealed rounds never appear.', 'جولة مغلقة واحدة: من قدّم عرضاً، والترسية الرسمية. الجولات المختومة لا تظهر.')
  };
  function setMode(mode, opts = {}) {
    if (!DESC[mode]) return;
    st.mode = mode;
    $$('[data-uni-mode]').forEach(b => b.setAttribute('aria-checked', String(b.dataset.uniMode === mode)));
    host.dataset.mode = mode;
    $('[data-uni-desc]').textContent = DESC[mode];
    $('[data-uni-tender-field]').hidden = mode !== 'tender';
    if (mode !== 'tender' && !st.selected.length) home();
    if (st.view !== '3d' && !opts.keepView) setView('3d');
    applyLayout(!opts.instant);
  }
  function setMotion(on) {
    st.motion = on && !reduced.matches;
    const b = $('[data-uni-tool="motion"]'); b.setAttribute('aria-pressed', String(!st.motion));
    b.setAttribute('aria-label', st.motion ? T('Pause motion', 'إيقاف الحركة') : T('Resume motion', 'استئناف الحركة'));
    b.querySelector('[data-icon-pause]').hidden = !st.motion; b.querySelector('[data-icon-play]').hidden = st.motion;
    host.classList.toggle('is-paused', !st.motion); wake();
  }
  function reset() { choose(null); st.vel = { x: 0, y: 0 }; st.qTarget = BASE_TILT.clone(); st.distTarget = HOME.dist; setMode('universe'); }
  function setView(view) {
    st.view = view;
    ['3d', '2d', 'table'].forEach(v => $(`[data-uni-tool="${v}"]`).setAttribute('aria-pressed', String(v === view)));
    host.dataset.view = view;
    $('[data-net2d]').hidden = view !== '2d';
    $('[data-uni-table]').hidden = view !== 'table';
    if (view === 'table') renderTable();
    if (view === '2d') requestAnimationFrame(() => window.dispatchEvent(new Event('resize')));
    if (view === '3d') { rebuildLinks(); wake(); }
  }
  function renderTable() {
    const d = radar()?.data; if (!d) return;
    const filt = visibleSet();
    const rows = d.suppliers.filter(s => !filt || filt.has(s.supplierId)).sort((a, b) => b.participations - a.participations || clean(a.name).localeCompare(clean(b.name)));
    $('[data-uni-table]').innerHTML = `<table class="oi-table"><caption>${rows.length} ${T('suppliers in scope', 'مورداً في النطاق')}</caption><thead><tr><th>${T('Supplier', 'المورد')}</th><th>${T('Category', 'الفئة')}</th><th>${T('Status', 'الحالة')}</th><th>${T('Invitations', 'الدعوات')}</th><th>${T('Bidding Cycles Participated In', 'دورات العطاء التي شارك فيها')}</th><th>${T('Awards', 'الترسيات')}</th><th>${T('Response rate', 'معدل الاستجابة')}</th></tr></thead>
      <tbody>${rows.map(s => `<tr class="${st.selected.includes(s.supplierId) ? 'is-selected' : ''}"><th><button type="button" data-table-pick="${s.supplierId}"><i style="background:${radar()?.catColor?.(s.category)}"></i>${esc(clean(s.name))}</button></th><td>${esc(clean(s.category))}</td><td>${esc(s.status)}</td><td>${fmtInt(s.invitations)}</td><td>${fmtInt(s.participations)}</td><td>${fmtInt(s.wins)}</td><td>${fmtPct(s.participationRate)}</td></tr>`).join('')}</tbody></table>`;
  }
  host.addEventListener('click', e => {
    const b = e.target.closest('button'); if (!b || inspector.contains(b)) return;
    const v = b.dataset;
    if (v.uniMode) setMode(v.uniMode);
    else if (v.uniTool === 'motion') setMotion(!st.motion);
    else if (v.uniTool === 'reset') reset();
    else if (v.uniTool === '3d' || v.uniTool === '2d' || v.uniTool === 'table') setView(st.view === v.uniTool && v.uniTool !== '3d' ? '3d' : v.uniTool);
    else if (v.uniTool === 'fullscreen') { if (document.fullscreenElement) document.exitFullscreen(); else host.requestFullscreen?.().catch(() => { }); }
    else if (v.tablePick) { setView('3d'); choose(+v.tablePick); }
  });
  host.addEventListener('change', e => {
    const t = e.target;
    if (t.matches('[data-uni-opt="labels"]')) { st.labels = t.checked; wake(); }
    else if (t.matches('[data-uni-opt="links"]')) { st.links = t.checked; rebuildLinks(); wake(); }
    else if (t.matches('[data-uni-tender]')) { st.tender = +t.value; st.tenderDir = null; applyLayout(true); }
  });
  const search = $('[data-uni-search]');
  search.addEventListener('change', () => find(search.value));
  search.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); find(search.value); } });
  function find(text) {
    const q = clean(text).trim().toLowerCase(); if (!q) return;
    const hit = nodes.find(n => clean(n.s.name).toLowerCase() === q) || nodes.find(n => clean(n.s.name).toLowerCase().includes(q));
    if (hit) { if (st.view !== '3d') setView('3d'); choose(hit.id); search.value = ''; return true; }
    const cat = nodes.find(n => n.cat.toLowerCase() === q || clean(n.cat).toLowerCase().includes(q));
    if (cat) { focusCategory(cat.cat); search.value = ''; return true; }
    radar()?.toast?.(T('No authorized supplier or category matches.', 'لا يوجد مورد أو فئة مطابقة.')); return false;
  }
  function focusCategory(name) {
    const key = nodes.find(n => n.cat.toLowerCase() === String(name).toLowerCase())?.cat; if (!key) return false;
    if (st.mode !== 'category') setMode('category');
    const a = anchors.find(x => x.key === key); if (a) { faceDirection(a.dir, HOME.focus); wake(); }
    return true;
  }
  fullscreenFix();
  function fullscreenFix() { document.addEventListener('fullscreenchange', () => { host.classList.toggle('is-fullscreen', document.fullscreenElement === host); setTimeout(() => { resize(); wake(); }, 60); }); }

  function renderLegend() {
    const d = radar()?.data; if (!d) return;
    const cats = [...group(nodes, n => n.cat)].sort((a, b) => b[1].length - a[1].length);
    const linkKey = { universe: `<li><i class="is-line"></i>${T('Hover or select: shared closed rounds', 'مرور أو اختيار: جولات مشتركة')}</li>`, category: '', competition: `<li><i class="is-line"></i>${T('Shared closed rounds (min', 'جولات مشتركة (حد أدنى')} ${d.network.competitionMinimum})</li>`,
      tender: `<li><i class="is-line"></i>${T('Submitted an offer', 'قدّم عرضاً')}</li><li><i class="is-line is-award"></i>${T('Official award', 'ترسية رسمية')}</li><li><i class="is-diamond"></i>${T('Tender round', 'جولة العطاء')}</li>` }[st.mode];
    $('[data-uni-legend]').innerHTML = `<p class="oi-field-label">${T('Colour = registered category', 'اللون = الفئة المسجلة')}</p><ul class="oi-legend-cats">${cats.slice(0, 7).map(([c, ns]) => `<li><button type="button" data-legend-cat="${esc(c)}"><i style="background:${radar()?.catColor?.(c)}"></i>${esc(clean(c))}<small>${ns.length}</small></button></li>`).join('')}${cats.length > 7 ? `<li class="oi-more">+${cats.length - 7} ${T('more', 'أخرى')}</li>` : ''}</ul>
      <ul class="oi-legend-keys"><li><i class="is-size"></i>${T('Size = bidding cycles participated in', 'الحجم = دورات العطاء التي شارك فيها')}</li>${linkKey}</ul>`;
  }
  $('[data-uni-legend]').addEventListener('click', e => { const b = e.target.closest('[data-legend-cat]'); if (b) focusCategory(b.dataset.legendCat); });
  function renderCaption() {
    const d = radar()?.data; if (!d) return;
    const filt = visibleSet(); const shown = nodes.filter(n => !filt || filt.has(n.id));
    const active = shown.filter(n => n.s.participations > 0).length;
    const links = d.network.competitionLinks.filter(l => l.roundIds.length >= d.network.competitionMinimum).length;
    let text = `${shown.length} ${T('suppliers', 'مورداً')} · ${active} ${T('submitted offers in closed rounds', 'قدّموا عروضاً في جولات مغلقة')}`;
    if (st.mode === 'competition') text += ` · ${links} ${T('competition links', 'روابط منافسة')}`;
    if (st.mode === 'tender') { const r = tenderRound(); text = r ? `${clean(r.tender)} · ${T('round', 'جولة')} ${r.roundNumber} · ${r.supplierIds.length} ${T('offers', 'عروض')}${r.awardedSupplierId ? ` · ${T('awarded to', 'مُرسى على')} ${clean(byId.get(r.awardedSupplierId)?.s.name || '')}` : ''}` : T('No closed round with offers in this period.', 'لا جولة مغلقة بعروض في هذه الفترة.'); }
    live.textContent = text;
    const empty = $('[data-uni-empty]');
    empty.hidden = shown.length > 0;
    empty.textContent = nodes.length ? T('No supplier matches the current filters.', 'لا يوجد مورد يطابق المرشحات الحالية.') : T('No authorized suppliers in this scope.', 'لا يوجد موردون مصرح بهم في هذا النطاق.');
  }
  function renderTenderOptions() {
    const d = radar()?.data; const sel = $('[data-uni-tender]'); if (!d) return;
    const rounds = d.network.rounds.filter(r => r.supplierIds.length).slice().reverse();
    sel.innerHTML = rounds.map(r => `<option value="${r.roundId}">${esc(clean(r.tender))} · ${cycleLabel(r.roundNumber)} (${r.supplierIds.length})</option>`).join('') || `<option value="">${T('No closed bidding cycle', 'لا توجد دورة عطاء مغلقة')}</option>`;
    // Default: the closed round with the most offers (most recent on a tie), the most informative view of a tender.
    if (!(st.tender && rounds.some(r => r.roundId === st.tender))) st.tender = rounds.reduce((b, r) => (!b || r.supplierIds.length > b.supplierIds.length ? r : b), null)?.roundId ?? null;
    if (st.tender) sel.value = st.tender;
    $('[data-uni-list]').innerHTML = nodes.map(n => `<option value="${esc(clean(n.s.name))}"></option>`).join('') + [...new Set(nodes.map(n => clean(n.cat)))].map(c => `<option value="${esc(c)}"></option>`).join('');
  }

  // ---------- wiring to the page ----------
  function ingest(data) {
    buildNodes(data); st.ready = true;
    labelEls.forEach(el => el.remove()); labelEls.clear();
    st.selected = (radar()?.selected || []).filter(id => byId.has(id));
    renderTenderOptions();
    applyLayout(false);
    if (st.selected.length) applySelection(st.selected); else closeInspector();
    wake();
  }
  window.addEventListener('radar:data', e => ingest(e.detail));
  window.addEventListener('radar:selection', () => {
    const ids = (radar()?.selected || []).filter(id => byId.has(id));
    if (ids.join() !== st.selected.join()) applySelection(ids); else if (st.selected.length) renderInspector();
  });
  window.addEventListener('radar:filters', () => { updateEmphasis(); rebuildLinks(); renderCaption(); if (st.view === 'table') renderTable(); wake(); });
  window.addEventListener('orange:themechange', () => requestAnimationFrame(applyTheme));
  window.addEventListener('orange:sidebar', () => setTimeout(() => { resize(); wake(); }, 320));
  window.addEventListener('radar:tab', e => { st.visible = e.detail === 'suppliers' && !document.hidden; if (st.visible) { size.w = 0; resize(); wake(); } });
  document.addEventListener('visibilitychange', () => { st.visible = !document.hidden && root.dataset.tab === 'suppliers'; if (st.visible) wake(); });
  reduced.addEventListener?.('change', () => setMotion(st.motion));
  window.addEventListener('pagehide', () => { renderer.dispose(); });

  window.OrangeUniverse = Object.freeze({
    setMode: (m, o) => setMode(m, o), focusCategory, find, setView, reset,
    showTender: roundId => { st.tender = roundId; const sel = $('[data-uni-tender]'); if (sel) sel.value = roundId; setMode('tender'); },
    get mode() { return st.mode; }, get view() { return st.view; }, get selected() { return [...st.selected]; },
    png: () => { renderer.render(scene, camera); try { return canvas.toDataURL('image/png'); } catch { return null; } }
  });

  setMotion(!reduced.matches);
  setMode('universe', { instant: true });
  applyTheme();
  st.visible = root.dataset.tab === 'suppliers';
  if (radar()?.data) ingest(radar().data);
}

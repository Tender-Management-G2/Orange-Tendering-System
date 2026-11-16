/*
 * Our Team — "The Minds Behind Orange" (Three.js + GSAP ScrollTrigger).
 *
 * A dark Orange innovation room with a miniature Orange headquarters on a glowing platform. The seven
 * team cards sit above the building. Every visible state is a pure function of the scroll progress p of
 * #tx: no clock, no autoplay. Rendering happens only when p or the viewport changes, so the scene stops
 * the moment scrolling stops and reverses exactly when scrolling back.
 *
 *   0.00–0.08  welcome: the camera settles on the building
 *   0.08–0.82  one stage per member: the card is enlarged and highlighted, an Orange line is drawn from it
 *              to that member's section of the building, the section lights up and the right panel shows
 *              the member's details
 *   0.82–0.91  the camera pulls back and all seven lines connect at once
 *   0.91–1.00  "Seven Minds. One Connected Vision."
 */
import * as THREE from "three";
import { RoomEnvironment } from "./vendor/three/examples/jsm/environments/RoomEnvironment.js";

let openProfile = () => {};
const root = document.getElementById("tx");
if (root) init(root);

function init(section) {
  const canvas = section.querySelector(".tx-canvas");
  const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const lowPower = innerWidth < 760 || (navigator.hardwareConcurrency || 8) <= 4;
  const q = s => section.querySelector(s), qa = s => [...section.querySelectorAll(s)];
  const cards = qa(".tx-card"), N = Math.max(1, cards.length);
  const ui = {
    row: q(".tx-row"), wrap: q(".tx-row-wrap"), journey: qa(".tx-journey li"), info: q(".tx-info"), steps: qa(".tx-steps button"),
    finale: q(".tx-finale"), stepsBox: q(".tx-steps"), bar: q(".tx-progress i"), live: q(".tx-live"),
    links: qa(".tx-link"), glows: qa(".tx-link-glow"), dots: qa(".tx-link-dot"), svg: q(".tx-links")
  };
  const api = window.__team = { p: 0, frames: 0, ready: false, mode: reduced ? "static" : "scroll" };
  wireCards();

  let renderer;
  try {
    renderer = new THREE.WebGLRenderer({ canvas, antialias: !lowPower, powerPreference: "high-performance" });
  } catch {
    section.classList.add("is-static", "no-webgl"); api.mode = "no-webgl"; ui.svg.style.display = "none"; api.ready = true; return;
  }
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.05;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  renderer.shadowMap.autoUpdate = false;

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x0a0705);
  scene.fog = new THREE.Fog(0x0a0705, 70, 190);
  const camera = new THREE.PerspectiveCamera(36, 1, .1, 300);
  const pmrem = new THREE.PMREMGenerator(renderer);
  scene.environment = pmrem.fromScene(new RoomEnvironment(), .04).texture;

  let seed = 11;
  const rnd = () => ((seed = (seed * 16807) % 2147483647) - 1) / 2147483646;
  const V = (x, y, z) => new THREE.Vector3(x, y, z);
  const clamp01 = v => Math.min(1, Math.max(0, v));
  const seg = (p, a, b) => clamp01((p - a) / (b - a));
  const smooth = t => t * t * (3 - 2 * t);
  const lerp = (a, b, t) => a + (b - a) * t;
  const ORANGE = new THREE.Color(0xff7900);

  function canvasTex(w, h, draw, repeat) {
    const c = document.createElement("canvas"); c.width = w; c.height = h; draw(c.getContext("2d"), w, h);
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 4;
    if (repeat) { t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(repeat[0], repeat[1]); }
    return t;
  }
  const officeTex = canvasTex(512, 128, (g, w, h) => {
    g.fillStyle = "#2a160a"; g.fillRect(0, 0, w, h);
    for (let i = 0; i < 18; i++) { const x = rnd() * w, r = 30 + rnd() * 50, gr = g.createRadialGradient(x, 18, 0, x, 18, r); gr.addColorStop(0, "rgba(255,214,150,.95)"); gr.addColorStop(1, "rgba(255,170,90,0)"); g.fillStyle = gr; g.fillRect(x - r, 0, r * 2, r * 2); }
    for (let i = 0; i < 44; i++) { g.fillStyle = `rgba(20,12,8,${.55 + rnd() * .4})`; const x = rnd() * w; g.fillRect(x, 70 + rnd() * 20, 18 + rnd() * 30, 40); }
    for (let i = 0; i < 26; i++) { g.fillStyle = "rgba(14,9,6,.9)"; const x = rnd() * w; g.fillRect(x, 60 + rnd() * 16, 5, 34); g.beginPath(); g.arc(x + 2.5, 56 + rnd() * 14, 5, 0, 6.3); g.fill(); }
  }, [.35, 1.2]);
  const signTex = canvasTex(256, 256, (g, w, h) => { g.fillStyle = "#ff7900"; g.fillRect(0, 0, w, h); g.fillStyle = "#fff"; g.font = "bold 50px Helvetica, Arial, sans-serif"; g.fillText("orange", 22, h - 26); });
  const windowsTex = canvasTex(512, 256, (g, w, h) => {
    g.fillStyle = "#07090c"; g.fillRect(0, 0, w, h);
    for (let x = 0; x < w; x += 64) for (let y = 0; y < h; y += 32) { const k = rnd(); g.fillStyle = k > .82 ? `rgba(255,${150 + rnd() * 80},80,${.35 + rnd() * .4})` : k > .6 ? "rgba(90,110,140,.18)" : "rgba(30,36,46,.4)"; g.fillRect(x + 4, y + 4, 56, 24); }
    g.strokeStyle = "rgba(0,0,0,.9)"; g.lineWidth = 6; for (let x = 0; x <= w; x += 64) { g.beginPath(); g.moveTo(x, 0); g.lineTo(x, h); g.stroke(); }
  }, [10, 1]);

  /* ---------------- lights ---------------- */
  const hemi = new THREE.HemisphereLight(0x8a7464, 0x1a0b04, .55); scene.add(hemi);
  const key = new THREE.DirectionalLight(0xffe2c4, 1.6); key.position.set(9, 20, 14); key.castShadow = true;
  key.shadow.mapSize.set(lowPower ? 1024 : 2048, lowPower ? 1024 : 2048); Object.assign(key.shadow.camera, { left: -20, right: 20, top: 16, bottom: -12, near: 2, far: 70 }); key.shadow.bias = -.0005; key.shadow.normalBias = .02;
  scene.add(key);
  const glowA = new THREE.PointLight(0xff7a1a, 70, 34, 1.6); glowA.position.set(0, 1.2, 8); scene.add(glowA);
  const glowB = new THREE.PointLight(0xff9a40, 40, 30, 1.8); glowB.position.set(0, 12, -2); scene.add(glowB);

  /* ---------------- the innovation room ---------------- */
  const M = {
    floor: new THREE.MeshStandardMaterial({ color: 0x0f0b09, roughness: .16, metalness: .75, envMapIntensity: .55 }),
    wall: new THREE.MeshStandardMaterial({ color: 0x15100d, map: windowsTex, emissive: 0xffffff, emissiveMap: windowsTex, emissiveIntensity: .55, roughness: .4, side: THREE.BackSide }),
    column: new THREE.MeshStandardMaterial({ color: 0x1b1512, roughness: .5, metalness: .4 }),
    ring: new THREE.MeshBasicMaterial({ color: new THREE.Color(0xff7900).multiplyScalar(2.2), toneMapped: false }),
    ringSoft: new THREE.MeshBasicMaterial({ color: new THREE.Color(0xff7900).multiplyScalar(1.3), toneMapped: false, transparent: true, opacity: .55 }),
    pot: new THREE.MeshStandardMaterial({ color: 0xd9d3cb, roughness: .5 }),
    leaf: new THREE.MeshStandardMaterial({ color: 0x3f5f22, roughness: .8, emissive: 0x2a1404, emissiveIntensity: .3 }),
    slab: new THREE.MeshStandardMaterial({ color: 0xe8e2da, roughness: .5, envMapIntensity: .6 }),
    glass: new THREE.MeshPhysicalMaterial({ color: 0x9fb0bd, roughness: .03, metalness: .2, transparent: true, opacity: .26, envMapIntensity: 1.2, depthWrite: false }),
    mullion: new THREE.MeshStandardMaterial({ color: 0x2a2c30, roughness: .35, metalness: .8 }),
    stone: new THREE.MeshStandardMaterial({ color: 0x1d1714, roughness: .45, metalness: .3 }),
    lawn: new THREE.MeshStandardMaterial({ color: 0x35511f, roughness: .9 }),
    trunk: new THREE.MeshStandardMaterial({ color: 0x4a3324, roughness: .9 }),
    canopy: new THREE.MeshStandardMaterial({ color: 0xc98a34, roughness: .75, emissive: 0x5a2a04, emissiveIntensity: .35 }),
    sign: new THREE.MeshStandardMaterial({ map: signTex, emissive: 0xffffff, emissiveMap: signTex, emissiveIntensity: .7, roughness: .4 })
  };
  const add = (geo, mat, x = 0, y = 0, z = 0, parent = scene) => { const m = new THREE.Mesh(geo, mat); m.position.set(x, y, z); m.castShadow = m.receiveShadow = true; parent.add(m); return m; };
  add(new THREE.PlaneGeometry(240, 240).rotateX(-Math.PI / 2), M.floor).castShadow = false;
  add(new THREE.CylinderGeometry(52, 52, 30, 72, 1, true), M.wall, 0, 15, -6).castShadow = false;
  for (let i = 0; i < 14; i++) { const a = i / 14 * Math.PI * 2; if (Math.cos(a) > .3) continue; add(new THREE.CylinderGeometry(.8, .8, 30, 16), M.column, Math.sin(a) * 40, 15, Math.cos(a) * 40 - 6).castShadow = false; }
  [[24, 17, .2], [33, 18.5, .16], [15, 15.6, .12]].forEach(([r, y, t]) => { const m = new THREE.Mesh(new THREE.TorusGeometry(r, t, 8, 160).rotateX(Math.PI / 2), M.ring); m.position.set(0, y, -4); scene.add(m); });
  [[-26, 6], [26, 6], [-30, -8], [30, -8], [-20, -20], [20, -20], [-36, 14], [36, 14]].forEach(([x, z], i) => {
    add(new THREE.CylinderGeometry(1, .8, 1.6, 20), M.pot, x, .8, z);
    const n = 6 + (i % 3); for (let k = 0; k < n; k++) { const m = add(new THREE.IcosahedronGeometry(.9 + rnd() * .5, 1), M.leaf, x + (rnd() - .5) * 1.6, 2 + rnd() * 3.2, z + (rnd() - .5) * 1.6); m.scale.y = 1.3; }
  });

  /* ---------------- day / night: follows the site theme ---------------- */
  const windowsDay = canvasTex(512, 256, (g, w, h) => {
    g.fillStyle = "#c9d3dc"; g.fillRect(0, 0, w, h);
    for (let x = 0; x < w; x += 64) for (let y = 0; y < h; y += 32) { const k = rnd(); g.fillStyle = k > .7 ? "rgba(255,255,255,.75)" : k > .35 ? "rgba(170,200,228,.7)" : "rgba(140,168,196,.6)"; g.fillRect(x + 4, y + 4, 56, 24); }
    g.strokeStyle = "rgba(90,96,104,.9)"; g.lineWidth = 6; for (let x = 0; x <= w; x += 64) { g.beginPath(); g.moveTo(x, 0); g.lineTo(x, h); g.stroke(); }
  }, [10, 1]);
  const windowsNight = M.wall.map;
  let dayMode = null;
  function applyDayNight() {
    const day = document.documentElement.dataset.theme === "light"; if (day === dayMode) return false; dayMode = day;
    scene.background.set(day ? 0xece7e0 : 0x0a0705); scene.fog.color.set(day ? 0xece7e0 : 0x0a0705);
    hemi.color.set(day ? 0xf2f0ec : 0x8a7464); hemi.groundColor.set(day ? 0xb3a796 : 0x1a0b04); hemi.intensity = day ? 1.5 : .55;
    key.intensity = day ? 2.4 : 1.6; glowA.intensity = day ? 25 : 70; glowB.intensity = day ? 12 : 40;
    M.floor.color.set(day ? 0xd9d2c8 : 0x0f0b09); M.floor.roughness = day ? .3 : .16; M.floor.metalness = day ? .25 : .75;
    M.wall.map = M.wall.emissiveMap = day ? windowsDay : windowsNight; M.wall.color.set(day ? 0xffffff : 0x15100d); M.wall.emissiveIntensity = day ? .35 : .55; M.wall.needsUpdate = true;
    M.column.color.set(day ? 0xe7e2da : 0x1b1512); M.stone.color.set(day ? 0xcfc8be : 0x1d1714);
    renderer.toneMappingExposure = day ? 1 : 1.05;
    return true;
  }

  /* ---------------- miniature Orange HQ on a glowing platform ---------------- */
  function rrect(w, d, r, ox = 0) { const s = new THREE.Shape(), x = -w / 2 + ox, y = -d / 2; s.moveTo(x + r, y); s.lineTo(x + w - r, y); s.quadraticCurveTo(x + w, y, x + w, y + r); s.lineTo(x + w, y + d - r); s.quadraticCurveTo(x + w, y + d, x + w - r, y + d); s.lineTo(x + r, y + d); s.quadraticCurveTo(x, y + d, x, y + d - r); s.lineTo(x, y + r); s.quadraticCurveTo(x, y, x + r, y); return s; }
  const prism = (shape, h, mat) => new THREE.Mesh(new THREE.ExtrudeGeometry(shape, { depth: h, bevelEnabled: false, curveSegments: 10 }).rotateX(-Math.PI / 2), mat);
  const hq = new THREE.Group(); scene.add(hq);
  add(new THREE.CylinderGeometry(12, 12.3, .36, 96), M.stone, 0, .18, 0, hq);
  add(new THREE.RingGeometry(8.6, 11.7, 96).rotateX(-Math.PI / 2), M.lawn, 0, .37, 0, hq);
  [[12.15, .37, .08], [14, .02, .07], [16.2, .02, .06]].forEach(([r, y, t], i) => { const m = new THREE.Mesh(new THREE.TorusGeometry(r, t, 8, 160).rotateX(Math.PI / 2), i ? M.ringSoft : M.ring); m.position.y = y; hq.add(m); });
  for (let i = 0; i < 16; i++) { const a = i / 16 * Math.PI * 2 + .1; if (Math.abs(Math.sin(a)) < .25 && Math.cos(a) > 0) continue; const x = Math.sin(a) * 10.3, z = Math.cos(a) * 9.6; add(new THREE.CylinderGeometry(.05, .07, .7, 6), M.trunk, x, .72, z, hq); add(new THREE.IcosahedronGeometry(.42 + rnd() * .16, 2), M.canopy, x, 1.25, z, hq).scale.y = 1.25; }

  const LEVELS = [
    { name: "Podium", w: 15.2, d: 9.6, r: 3.6, h: 1.2, ox: 0 },
    { name: "Level 1", w: 13.4, d: 8.4, r: 3.2, h: .9, ox: -.25 },
    { name: "Level 2", w: 13.9, d: 8.7, r: 3.3, h: .9, ox: .25 },
    { name: "Level 3", w: 13.2, d: 8.3, r: 3.2, h: .9, ox: -.15 },
    { name: "Level 4", w: 13.6, d: 8.5, r: 3.2, h: .9, ox: .2 },
    { name: "Top floor", w: 12.4, d: 7.9, r: 3, h: 1, ox: 0 },
    { name: "Roof & Orange sign", w: 16.4, d: 10.6, r: 4, h: .34, ox: .9 }
  ];
  const levels = []; let y = .36;
  LEVELS.forEach((L, k) => {
    const g = new THREE.Group(); g.position.y = y; hq.add(g);
    const led = new THREE.MeshStandardMaterial({ color: 0x3a1a06, emissive: ORANGE, emissiveIntensity: .7, roughness: .4 });
    const inner = new THREE.MeshStandardMaterial({ color: 0x2a160a, map: officeTex, emissive: 0xffb36b, emissiveMap: officeTex, emissiveIntensity: .75, roughness: .8 });
    const slab = prism(rrect(L.w + .35, L.d + .35, L.r + .17, L.ox), k === 6 ? L.h : .14, M.slab); slab.castShadow = slab.receiveShadow = true; g.add(slab);
    const edge = rrect(L.w + .37, L.d + .37, L.r + .18, L.ox).getSpacedPoints(160).map(p => V(p.x, (k === 6 ? L.h : .14) - .03, -p.y));
    g.add(new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(edge, true), 320, .045, 6, true), led));
    let anchor;
    if (k < 6) {
      const core = prism(rrect(L.w - 1, L.d - 1, L.r - .5, L.ox), L.h - .2, inner); core.position.y = .14; g.add(core);
      const glass = prism(rrect(L.w, L.d, L.r, L.ox), L.h - .14, M.glass); glass.position.y = .14; g.add(glass);
      const pts = rrect(L.w + .02, L.d + .02, L.r + .01, L.ox).getSpacedPoints(Math.round((L.w + L.d) * 3)), mm = new THREE.InstancedMesh(new THREE.BoxGeometry(.05, L.h - .14, .05), M.mullion, pts.length), o = new THREE.Object3D();
      pts.forEach((p, i) => { o.position.set(p.x, .14 + (L.h - .14) / 2, -p.y); o.updateMatrix(); mm.setMatrixAt(i, o.matrix); }); g.add(mm);
      anchor = V(L.ox, y + L.h * .55, L.d / 2 + .1);
    } else {
      const sign = new THREE.Mesh(new THREE.BoxGeometry(2.6, 2.6, .3), [M.slab, M.slab, M.slab, M.slab, M.sign, M.slab]); sign.position.set(L.ox + 2.2, L.h + 1.3, L.d / 2 - 1.6); sign.castShadow = true; g.add(sign);
      anchor = V(L.ox + 2.2, y + L.h + 1.3, L.d / 2 - 1.44);
    }
    levels.push({ led, inner, anchor, name: L.name });
    y += k === 6 ? 0 : L.h;
  });

  /* ---------------- timeline ---------------- */
  const A = .08, B = .82, C = .91, SEG = (B - A) / N;
  const focusIndex = p => p < A || p >= B ? -1 : Math.min(N - 1, Math.floor((p - A) / SEG));
  const localT = (p, i) => (p - A - i * SEG) / SEG;
  const levelOf = i => levels[Math.round(i * (levels.length - 1) / Math.max(1, N - 1))];

  /* ---------------- camera ---------------- */
  const camTgt = V(0, 0, 0);
  let W = 1, H = 1, portrait = false, camAz = 0;
  function poseCamera(p) {
    const fi = focusIndex(p);
    let dist = lerp(58, 47, smooth(seg(p, 0, A))), elev = lerp(.3, .24, smooth(seg(p, 0, A))), az = lerp(-.32, .32, smooth(seg(p, A, B)));
    let ty = 3.2;
    if (fi >= 0) { const lv = levelOf(fi).anchor.y, w = smooth(seg(localT(p, fi), .05, .4)) * (1 - smooth(seg(localT(p, fi), .9, 1))); ty = lerp(3.2, lv, .25 * w); dist -= 2.2 * w; }
    const out = smooth(seg(p, B, C)); dist = lerp(dist, 56, out); elev = lerp(elev, .3, out); az = lerp(az, 0, out); camAz = az;
    if (portrait) dist *= 1.55;
    camTgt.set(0, ty, 0);
    camera.position.set(Math.sin(az) * Math.cos(elev) * dist, ty + Math.sin(elev) * dist, Math.cos(az) * Math.cos(elev) * dist);
    camera.lookAt(camTgt);
  }

  /* ---------------- building section light ---------------- */
  function poseLevels(p) {
    const fi = focusIndex(p), all = smooth(seg(p, B + .015, C - .02));
    levels.forEach(L => {
      let g = all;
      for (let i = 0; i < N; i++) if (levelOf(i) === L && i === fi) g = Math.max(g, smooth(seg(localT(p, i), .3, .45)) * (1 - smooth(seg(localT(p, i), .9, 1))));
      L.led.emissiveIntensity = .7 + 4.5 * g; L.led.color.setRGB(.23 + .77 * g, .1 + .3 * g, .02);
      L.inner.emissive.setRGB(1, lerp(.7, .45, g), lerp(.42, .1, g)); L.inner.emissiveIntensity = .75 + 1.1 * g;
    });
  }

  /* ---------------- HTML overlay driven by the same p ---------------- */
  const sp = V(0, 0, 0);
  function toScreen(v) { sp.copy(v).project(camera); return [(sp.x * .5 + .5) * W, (-sp.y * .5 + .5) * H]; }
  let shownInfo = -1;
  function showInfo(i) {
    if (i === shownInfo) return; shownInfo = i;
    const d = cards[i].dataset, info = ui.info;
    info.querySelector("h3").textContent = d.name; info.querySelector(".tx-info-role").textContent = d.role;
    const img = info.querySelector("img"); if (d.photo) { img.src = d.photo; img.hidden = false; } else { img.removeAttribute("src"); img.hidden = true; }
    const list = info.querySelector(".tx-info-list"); list.replaceChildren(...d.contrib.split(/\n|;\s*/).filter(Boolean).map(t => { const li = document.createElement("li"); li.textContent = t.trim(); return li; }));
    info.querySelector(".tx-info-section").textContent = `Connected to HQ section ${String(i + 1).padStart(2, "0")} · ${levelOf(i).name}`;
    info.dataset.member = String(i);
  }
  /* the cards orbit the building on a ring above the roof: they spin on their own before the first scroll and
     again after all seven are introduced; while scrolling through the members the scroll turns the ring so the
     current member comes to the front */
  const TAU = Math.PI * 2, RING = { rx: 21, rz: 12.5, y: 11.8 }, SPIN = .2; // an ellipse, wider than deep; rad/s
  const ringPt = V(0, 0, 0);
  const wrapAngle = a => { a %= TAU; return a < -Math.PI ? a + TAU : a > Math.PI ? a - TAU : a; };
  const lerpAngle = (a, b, t) => a + wrapAngle(b - a) * t;
  function ringBase(p, now) {
    const idle = now / 1000 * SPIN;
    const x = clamp01((p - A) / (B - A)) * N - .5, f = Math.floor(x), c = Math.min(N - 1, Math.max(0, f + smooth(clamp01((x - f) * 1.6 - .3))));
    const scroll = camAz - c * TAU / N;
    return lerpAngle(lerpAngle(idle, scroll, smooth(seg(p, 0, A))), idle, smooth(seg(p, B, C)));
  }
  let cardW = 0, cardH = 0;
  function measureCards() { const c = cards[0]; if (c) { cardW = c.offsetWidth; cardH = c.offsetHeight; } }
  function poseOverlay(p, now = performance.now()) {
    const fi = focusIndex(p), stage = p < A ? 0 : p < B ? (fi < N / 2 ? 1 : 2) : 3;
    ui.journey.forEach((li, k) => { li.classList.toggle("is-on", k === stage); li.classList.toggle("is-done", k < stage); });
    const sr = section.querySelector(".tx-sticky").getBoundingClientRect();
    if (!cardW) measureCards();
    const base = ringBase(p, now), sc = portrait ? .82 : 1;
    cards.forEach((c, i) => {
      const t = localT(p, i), e = fi >= 0 && Math.abs(i - fi) <= 1 ? smooth(seg(t, -.08, .12)) * (1 - smooth(seg(t, .9, 1.08))) : 0;
      const th = base + i * TAU / N, front = (Math.cos(th - camAz) + 1) / 2;
      ringPt.set(Math.sin(th) * RING.rx * (portrait ? .62 : 1), RING.y, Math.cos(th) * RING.rz); const [cx, cy] = toScreen(ringPt);
      const s = sc * (.6 + .4 * front) * (1 + .22 * e), dim = Math.max(.3 + .7 * front, e);
      c.style.transform = `translate(${(cx - cardW / 2).toFixed(1)}px,${(cy - cardH / 2).toFixed(1)}px) scale(${s.toFixed(4)})`; c.style.opacity = dim.toFixed(3); c.parentElement.style.zIndex = String(Math.round(front * 100) + (e > .5 ? 200 : 0));
      c.classList.toggle("is-on", e > .5 || p >= B + .02);
      // the connection line from this card to its building section
      const draw = Math.max(i === fi ? smooth(seg(t, .1, .38)) * (1 - smooth(seg(t, .9, 1))) : 0, smooth(seg(p, B + .005, C - .03)));
      const path = ui.links[i], glow = ui.glows[i], dot = ui.dots[i];
      if (!path) return;
      if (draw <= .001) { path.style.opacity = glow.style.opacity = dot.style.opacity = "0"; return; }
      const r = c.getBoundingClientRect(), x0 = r.left + r.width / 2 - sr.left, y0 = r.bottom - sr.top - 2, [x1, y1] = toScreen(levelOf(i).anchor), dy = y1 - y0;
      const d = `M${x0.toFixed(1)},${y0.toFixed(1)} C${x0.toFixed(1)},${(y0 + dy * .5).toFixed(1)} ${x1.toFixed(1)},${(y1 - dy * .5).toFixed(1)} ${x1.toFixed(1)},${y1.toFixed(1)}`;
      path.setAttribute("d", d); glow.setAttribute("d", d);
      const L = path.getTotalLength(); [path, glow].forEach(el => { el.style.strokeDasharray = `${L} ${L}`; el.style.strokeDashoffset = String(L * (1 - draw)); el.style.opacity = "1"; });
      glow.style.opacity = String(.28 * draw);
      dot.setAttribute("cx", x1.toFixed(1)); dot.setAttribute("cy", y1.toFixed(1)); dot.style.opacity = String(smooth(seg(draw, .9, 1)));
    });
    // right panel: the selected member
    let io = 0;
    if (fi >= 0) { const t = localT(p, fi); io = smooth(seg(t, .2, .36)) * (1 - smooth(seg(t, .88, .98))); if (io > 0) showInfo(fi); }
    ui.info.style.opacity = io.toFixed(3); ui.info.style.transform = `translateX(${((1 - io) * 24).toFixed(1)}px)`; ui.info.classList.toggle("is-live", io > .6); ui.info.inert = io <= .6;
    ui.steps.forEach((b, k) => { b.classList.toggle("is-on", k === fi); b.classList.toggle("is-done", p >= B || (fi >= 0 && k < fi)); });
    const fo = smooth(seg(p, C, .97)); ui.finale.style.opacity = fo.toFixed(3); ui.finale.style.transform = `translateY(${((1 - fo) * 20).toFixed(1)}px)`;
    const so = 1 - smooth(seg(p, C - .02, C + .03)); ui.stepsBox.style.opacity = so.toFixed(3); ui.stepsBox.style.visibility = so < .02 ? "hidden" : "";
    if (ui.bar) ui.bar.style.transform = `scaleX(${p})`;
    const label = fi >= 0 ? `${cards[fi].dataset.name}, team member ${fi + 1} of ${N}` : p >= C ? "Seven minds. One connected vision." : "";
    if (api.stageLabel !== label) { api.stageLabel = label; if (ui.live) ui.live.textContent = label; }
    api.focus = fi;
  }

  /* ---------------- frame = f(p) ---------------- */
  let lastP = -1, dirty = true;
  function resize() {
    const r = canvas.getBoundingClientRect(); W = Math.max(1, Math.round(r.width)); H = Math.max(1, Math.round(r.height)); portrait = W / H < .9;
    renderer.setPixelRatio(Math.min(devicePixelRatio || 1, lowPower ? 1.25 : 1.75)); renderer.setSize(W, H, false);
    camera.aspect = W / H; camera.fov = portrait ? 46 : W / H < 1.3 ? 40 : 34;
    camera.setViewOffset(W, H, 0, -H * (portrait ? .06 : W < 1100 ? .14 : .2), W, H); camera.updateProjectionMatrix(); dirty = true;
  }
  function draw(force) {
    const p = api.p;
    if (!force && !dirty && p === lastP) return;
    lastP = p; dirty = false;
    poseCamera(p); poseLevels(p); camera.updateMatrixWorld();
    renderer.shadowMap.needsUpdate = true; renderer.render(scene, camera); api.frames++;
    poseOverlay(p);
  }
  new MutationObserver(() => { if (applyDayNight()) { dirty = true; request(); } }).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
  applyDayNight();
  let spinning = false, onScreen = true;
  const needsSpin = () => !reduced && onScreen && !document.hidden && (api.p < A || api.p > B);
  function spinTick(now) { if (!needsSpin()) { spinning = false; return; } poseOverlay(api.p, now); requestAnimationFrame(spinTick); }
  function startSpin() { if (!spinning && needsSpin()) { spinning = true; requestAnimationFrame(spinTick); } }
  new IntersectionObserver(es => { onScreen = es[0].isIntersecting; startSpin(); }).observe(section);
  document.addEventListener("visibilitychange", startSpin);
  let queued = false;
  const request = () => { if (!queued) { queued = true; requestAnimationFrame(() => { queued = false; draw(); }); } };
  addEventListener("resize", () => { cardW = 0; resize(); request(); });
  function setProgress(v) { api.p = Math.round(clamp01(v) * 1e5) / 1e5; request(); startSpin(); }

  let toP = () => {};
  if (reduced) {
    section.classList.add("is-static"); api.p = .9; resize(); draw(true);
  } else if (window.gsap && window.ScrollTrigger) {
    gsap.registerPlugin(ScrollTrigger);
    const st = ScrollTrigger.create({ trigger: section, start: "top top", end: "bottom bottom", onUpdate: s => setProgress(s.progress), onRefresh: s => setProgress(s.progress) });
    api.trigger = () => ({ start: st.start, end: st.end });
    api.sync = () => { ScrollTrigger.update(); setProgress(st.progress); draw(); };
    toP = v => scrollTo({ top: st.start + (st.end - st.start) * v, behavior: "smooth" });
    resize(); setProgress(st.progress);
  } else {
    const prog = () => { const r = section.getBoundingClientRect(), span = section.offsetHeight - innerHeight; return span > 0 ? -r.top / span : 0; };
    addEventListener("scroll", () => setProgress(prog()), { passive: true });
    api.trigger = () => { const top = section.getBoundingClientRect().top + scrollY; return { start: top, end: top + section.offsetHeight - innerHeight }; };
    api.sync = () => { setProgress(prog()); draw(); };
    toP = v => { const t = api.trigger(); scrollTo({ top: t.start + (t.end - t.start) * v, behavior: "smooth" }); };
    resize(); setProgress(prog());
  }
  ui.steps.forEach((b, i) => b.addEventListener("click", () => toP(A + (i + .6) * SEG)));
  ui.info.querySelector(".tx-info-open")?.addEventListener("click", e => openProfile(cards[+ui.info.dataset.member], e.currentTarget));

  const r4 = v => +v.toFixed(4);
  api.timeline = { welcome: A, members: N, segment: SEG, connect: B, finale: C };
  api.state = () => ({
    p: api.p, focus: api.focus, camera: camera.position.toArray().map(r4),
    lines: ui.links.map(l => +(l.style.opacity || 0) > 0 ? r4(1 - (+l.style.strokeDashoffset || 0) / (l.getTotalLength() || 1)) : 0),
    glow: levels.map(L => r4(L.led.emissiveIntensity)), info: { op: +ui.info.style.opacity, member: +ui.info.style.opacity > 0 ? ui.info.dataset.member : null },
    finale: +ui.finale.style.opacity, selected: cards.map(c => c.classList.contains("is-on"))
  });
  api.ring = () => cards.map(c => { const m = new DOMMatrix(getComputedStyle(c).transform); return [+m.e.toFixed(1), +m.f.toFixed(1), +m.a.toFixed(3)]; });
  api.spinning = () => spinning;
  api.ready = true;
}

/* ---------------- profile popup ---------------- */
function wireCards() {
  const dlg = document.getElementById("tx-dialog");
  if (!dlg) return;
  let opener = null;
  const set = (sel, v) => { const el = dlg.querySelector(sel); if (el) el.textContent = v; };
  openProfile = (card, from) => {
    if (!card) return;
    const d = card.dataset; opener = from || card;
    set(".tx-dlg-name", d.name); set(".tx-dlg-role", d.role); set(".tx-dlg-text", d.contrib);
    set(".tx-dlg-status", d.placeholder === "true" ? `Team member ${d.index} · role and contribution pending confirmation` : `Team member ${d.index}`);
    const img = dlg.querySelector(".tx-dlg-photo img"), ph = dlg.querySelector(".tx-dlg-photo .tx-ph");
    if (d.photo) { img.src = d.photo; img.alt = d.name; img.hidden = false; ph.hidden = true; } else { img.removeAttribute("src"); img.hidden = true; ph.hidden = false; ph.querySelector("b").textContent = d.index; }
    const links = dlg.querySelector(".tx-dlg-links"); links.replaceChildren();
    [["GitHub", d.github], ["LinkedIn", d.linkedin]].forEach(([n, u]) => { if (!u) return; const a = document.createElement("a"); a.href = u; a.target = "_blank"; a.rel = "noopener noreferrer"; a.textContent = n + " ↗"; a.setAttribute("aria-label", `${d.name} on ${n} (opens a new tab)`); links.append(a); });
    dlg.showModal();
  };
  document.querySelectorAll(".tx-card").forEach(card => card.addEventListener("click", () => openProfile(card, card)));
  dlg.addEventListener("click", e => { if (e.target === dlg || e.target.closest("[data-tx-close]")) dlg.close(); });
  dlg.addEventListener("close", () => { opener?.focus?.({ preventScroll: true }); });
}

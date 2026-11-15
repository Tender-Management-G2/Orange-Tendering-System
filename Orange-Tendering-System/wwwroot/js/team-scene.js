/*
 * Our Team — scroll-controlled courtyard scene (Three.js + GSAP ScrollTrigger).
 *
 * Every visible state is a pure function of one number: the scroll progress p in [0, 1] of #tx.
 * Nothing animates on a clock: no autoplay, no loop. The page renders only when p (or the viewport)
 * changes, so it freezes the instant scrolling stops and reverses exactly when scrolling back.
 *
 * Sequence: a short establishing shot, then one segment per team member (team.json order). In each
 * segment the member walks along the path, sits on the bench, an orange falls on their head, they react
 * and look up while their profile card opens, then they stand up and take their place in a line beside
 * the bench. Each orange ends up in the crate, so the bench is left clear. The finale shows the whole
 * team side by side with "Great Ideas Start with Great People." and the team row.
 *
 * The people are procedural articulated figures (hips, knees, ankles, shoulders, elbows, neck, head)
 * posed analytically from p: a foot-synced walk cycle, sitting, the hit reaction and looking up.
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
  const ui = {
    intro: section.querySelector(".tx-intro"),
    reveal: section.querySelector(".tx-reveal"),
    cards: [...section.querySelectorAll(".tx-card")],
    stages: [...section.querySelectorAll(".tx-stages li")],
    bar: section.querySelector(".tx-progress i"),
    live: section.querySelector(".tx-live")
  };
  const api = window.__team = { p: 0, frames: 0, ready: false, mode: reduced ? "static" : "scroll" };

  let renderer;
  try {
    renderer = new THREE.WebGLRenderer({ canvas, antialias: !lowPower, powerPreference: "high-performance" });
  } catch {
    section.classList.add("is-static", "no-webgl"); api.mode = "no-webgl"; ui.reveal.inert = false; ui.reveal.classList.add("is-live"); wireCards(); return;
  }
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = .98;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;

  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(40, 1, .1, 400);
  const pmrem = new THREE.PMREMGenerator(renderer);
  scene.environment = pmrem.fromScene(new RoomEnvironment(), .04).texture;

  /* deterministic pseudo-random so the scene is identical on every load */
  let seed = 7;
  const rnd = () => ((seed = (seed * 16807) % 2147483647) - 1) / 2147483646;
  const V = (x, y, z) => new THREE.Vector3(x, y, z);
  const clamp01 = v => Math.min(1, Math.max(0, v));
  const seg = (p, a, b) => clamp01((p - a) / (b - a));
  const smooth = t => t * t * (3 - 2 * t);
  const lerp = (a, b, t) => a + (b - a) * t;

  /* ---------------- procedural textures ---------------- */
  function canvasTex(w, h, draw, repeat) {
    const c = document.createElement("canvas"); c.width = w; c.height = h; draw(c.getContext("2d"), w, h);
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8;
    if (repeat) { t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(repeat[0], repeat[1]); }
    return t;
  }
  function noiseFill(g, w, h, base, spread, n, size) {
    g.fillStyle = base; g.fillRect(0, 0, w, h);
    for (let i = 0; i < n; i++) { const v = (rnd() - .5) * spread; g.fillStyle = `rgba(${v > 0 ? 255 : 0},${v > 0 ? 245 : 0},${v > 0 ? 230 : 0},${Math.abs(v)})`; g.fillRect(rnd() * w, rnd() * h, size * (.5 + rnd()), size * (.5 + rnd())); }
  }
  const paverTex = canvasTex(512, 512, (g, w, h) => {
    noiseFill(g, w, h, "#8c7f6d", .08, 5000, 3);
    const tw = 128, th = 64;
    for (let r = 0; r < h / th; r++) for (let c = -1; c < w / tw + 1; c++) {
      const x = c * tw + (r % 2) * tw / 2, y = r * th, k = rnd();
      g.fillStyle = `rgba(${128 + k * 36},${116 + k * 30},${100 + k * 26},.6)`; g.fillRect(x + 2, y + 2, tw - 4, th - 4);
      g.strokeStyle = "rgba(60,50,40,.55)"; g.lineWidth = 2.5; g.strokeRect(x + 1, y + 1, tw - 2, th - 2);
    }
  }, [14, 14]);
  const pathTex = canvasTex(256, 256, (g, w, h) => {
    noiseFill(g, w, h, "#d8cfc1", .07, 3000, 2);
    for (let i = 0; i < 4; i++) { g.strokeStyle = "rgba(90,78,64,.5)"; g.lineWidth = 2; g.beginPath(); g.moveTo(0, i * 64 + 1); g.lineTo(w, i * 64 + 1); g.stroke(); }
    g.beginPath(); g.moveTo(w / 2, 0); g.lineTo(w / 2, h); g.stroke();
  }, [1, 1]);
  const grassTex = canvasTex(256, 256, (g, w, h) => {
    g.fillStyle = "#3d5a26"; g.fillRect(0, 0, w, h);
    for (let i = 0; i < 9000; i++) { const k = rnd(); g.strokeStyle = `rgba(${40 + k * 70},${70 + k * 80},${20 + k * 30},.8)`; g.lineWidth = 1; const x = rnd() * w, y = rnd() * h; g.beginPath(); g.moveTo(x, y); g.lineTo(x + (rnd() - .5) * 3, y - 3 - rnd() * 5); g.stroke(); }
  }, [10, 10]);
  const woodTex = canvasTex(512, 64, (g, w, h) => {
    g.fillStyle = "#8a5a32"; g.fillRect(0, 0, w, h);
    for (let i = 0; i < 60; i++) { const y = rnd() * h, k = rnd(); g.strokeStyle = `rgba(${60 + k * 40},${34 + k * 20},${16},.35)`; g.lineWidth = .6 + rnd() * 1.6; g.beginPath(); g.moveTo(0, y); for (let x = 0; x <= w; x += 32) g.lineTo(x, y + Math.sin(x * .02 + i) * 2); g.stroke(); }
  });
  const barkTex = canvasTex(128, 256, (g, w, h) => {
    noiseFill(g, w, h, "#4a3a2c", .25, 2500, 2);
    for (let i = 0; i < 40; i++) { g.strokeStyle = "rgba(20,14,10,.55)"; g.lineWidth = 1 + rnd() * 2; const x = rnd() * w; g.beginPath(); g.moveTo(x, 0); for (let y = 0; y <= h; y += 16) g.lineTo(x + Math.sin(y * .05 + i) * 4, y); g.stroke(); }
  }, [2, 3]);
  const peelTex = canvasTex(128, 128, (g, w, h) => {
    g.fillStyle = "#f07a12"; g.fillRect(0, 0, w, h);
    for (let i = 0; i < 1800; i++) { const k = rnd(); g.fillStyle = k > .5 ? "rgba(255,170,70,.35)" : "rgba(170,60,0,.3)"; g.beginPath(); g.arc(rnd() * w, rnd() * h, .6 + rnd() * 1.2, 0, 6.3); g.fill(); }
  });
  const officeTex = canvasTex(1024, 256, (g, w, h) => {
    g.fillStyle = "#3b2a1c"; g.fillRect(0, 0, w, h);
    for (let i = 0; i < 26; i++) { const x = rnd() * w, y = 30 + rnd() * 40, r = 40 + rnd() * 90, gr = g.createRadialGradient(x, y, 0, x, y, r); gr.addColorStop(0, "rgba(255,214,160,.9)"); gr.addColorStop(1, "rgba(255,190,120,0)"); g.fillStyle = gr; g.fillRect(x - r, y - r, r * 2, r * 2); }
    for (let i = 0; i < 70; i++) { g.fillStyle = `rgba(30,20,14,${.5 + rnd() * .4})`; const x = rnd() * w; g.fillRect(x, 150 + rnd() * 30, 30 + rnd() * 60, 70); }
    for (let i = 0; i < 40; i++) { g.fillStyle = "rgba(20,16,12,.85)"; const x = rnd() * w; g.fillRect(x, 120 + rnd() * 30, 8, 46); g.beginPath(); g.arc(x + 4, 114 + rnd() * 30, 6, 0, 6.3); g.fill(); }
  });
  const signTex = canvasTex(512, 512, (g, w, h) => {
    g.fillStyle = "#ff7900"; g.fillRect(0, 0, w, h);
    g.fillStyle = "#fff"; g.font = "bold 92px Helvetica, Arial, sans-serif"; g.textBaseline = "alphabetic"; g.fillText("orange", 40, h - 52);
  });
  const skyTex = canvasTex(16, 512, (g, w, h) => {
    const gr = g.createLinearGradient(0, 0, 0, h);
    gr.addColorStop(0, "#5b86b8"); gr.addColorStop(.38, "#9bb9d6"); gr.addColorStop(.49, "#f3d4ae"); gr.addColorStop(.52, "#e6b98a"); gr.addColorStop(1, "#6b5a48");
    g.fillStyle = gr; g.fillRect(0, 0, w, h);
  });
  scene.background = new THREE.Color(0x9bb9d6);
  { const sky = new THREE.Mesh(new THREE.SphereGeometry(300, 32, 24), new THREE.MeshBasicMaterial({ map: skyTex, side: THREE.BackSide, fog: false, depthWrite: false })); sky.renderOrder = -1; scene.add(sky); }
  scene.fog = new THREE.Fog(0xd9c3a6, 45, 150);

  /* ---------------- materials ---------------- */
  const M = {
    paver: new THREE.MeshStandardMaterial({ map: paverTex, roughness: .82, bumpMap: paverTex, bumpScale: .6, envMapIntensity: .4 }),
    path: new THREE.MeshStandardMaterial({ map: pathTex, roughness: .75, bumpMap: pathTex, bumpScale: .4, envMapIntensity: .4 }),
    grass: new THREE.MeshStandardMaterial({ map: grassTex, roughness: .95, envMapIntensity: .25 }),
    curb: new THREE.MeshStandardMaterial({ color: 0xcfc6b8, roughness: .7 }),
    glass: new THREE.MeshPhysicalMaterial({ color: 0x9fb3c2, roughness: .04, metalness: .2, transparent: true, opacity: .34, envMapIntensity: 1.3, side: THREE.DoubleSide, depthWrite: false }),
    office: new THREE.MeshStandardMaterial({ color: 0x2a1d14, map: officeTex, emissive: 0xffffff, emissiveMap: officeTex, emissiveIntensity: .55, roughness: .9, side: THREE.BackSide }),
    slab: new THREE.MeshStandardMaterial({ color: 0xe9e4dc, roughness: .55, envMapIntensity: .6 }),
    mullion: new THREE.MeshStandardMaterial({ color: 0xb7bcc2, roughness: .3, metalness: .85, envMapIntensity: .9 }),
    sign: new THREE.MeshStandardMaterial({ map: signTex, emissive: 0xffffff, emissiveMap: signTex, emissiveIntensity: .35, roughness: .5 }),
    wood: new THREE.MeshStandardMaterial({ map: woodTex, roughness: .62, envMapIntensity: .5 }),
    iron: new THREE.MeshStandardMaterial({ color: 0x2b2d30, roughness: .45, metalness: .8 }),
    bark: new THREE.MeshStandardMaterial({ map: barkTex, bumpMap: barkTex, bumpScale: 2, roughness: .95, color: 0xb8a898 }),
    leaf: new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: .55, side: THREE.DoubleSide, envMapIntensity: .7 }),
    leafCore: new THREE.MeshStandardMaterial({ color: 0x3a6228, roughness: .9, emissive: 0x16260c, emissiveIntensity: .6 }),
    orange: new THREE.MeshPhysicalMaterial({ map: peelTex, bumpMap: peelTex, bumpScale: 1.5, roughness: .42, clearcoat: .35, clearcoatRoughness: .5 }),
    stem: new THREE.MeshStandardMaterial({ color: 0x4d6b2a, roughness: .8 }),
    shrub: new THREE.MeshStandardMaterial({ color: 0x3b5d2c, roughness: .9 }),
    planter: new THREE.MeshStandardMaterial({ color: 0x55585c, roughness: .6, metalness: .3 }),
    accent: new THREE.MeshStandardMaterial({ color: 0xff7900, emissive: 0xff7900, emissiveIntensity: .9, roughness: .4 }),
    lamp: new THREE.MeshStandardMaterial({ color: 0xffe2b8, emissive: 0xffc985, emissiveIntensity: 1.4 })
  };
  const mesh = (geo, mat, x = 0, y = 0, z = 0, cast = true, recv = true) => { const m = new THREE.Mesh(geo, mat); m.position.set(x, y, z); m.castShadow = cast; m.receiveShadow = recv; scene.add(m); return m; };

  /* ---------------- lights ---------------- */
  scene.add(new THREE.HemisphereLight(0xbcd3ee, 0x6b5440, .75));
  const sun = new THREE.DirectionalLight(0xffd9ad, 2.3);
  sun.position.set(-22, 26, 14); sun.target.position.set(1, 0, -3); scene.add(sun, sun.target);
  sun.castShadow = true; sun.shadow.mapSize.set(lowPower ? 1024 : 2048, lowPower ? 1024 : 2048);
  Object.assign(sun.shadow.camera, { left: -20, right: 20, top: 18, bottom: -16, near: 1, far: 80 }); sun.shadow.bias = -.0004; sun.shadow.normalBias = .03;

  /* ---------------- ground: courtyard pavers, grass beds, path ---------------- */
  const ground = mesh(new THREE.PlaneGeometry(160, 160).rotateX(-Math.PI / 2), M.paver, 0, 0, 0, false, true);
  [[-14, -4, 9, 7], [14, 6, 10, 9], [-6, 18, 8, 5]].forEach(([x, z, w, d]) => {
    mesh(new THREE.BoxGeometry(w, .14, d), M.grass, x, .07, z, false, true);
    mesh(new THREE.BoxGeometry(w + .3, .18, .15), M.curb, x, .09, z - d / 2, false, true);
    mesh(new THREE.BoxGeometry(w + .3, .18, .15), M.curb, x, .09, z + d / 2, false, true);
  });
  const WALK = new THREE.CatmullRomCurve3([V(-12, 0, 12), V(-7.5, 0, 7.6), V(-3, 0, 2.8), V(1, 0, -1), V(3, 0, -3.15)], false, "centripetal");
  const PATH = new THREE.CatmullRomCurve3([V(-20, 0, 20), V(-12, 0, 12), V(-7.5, 0, 7.6), V(-3, 0, 2.8), V(1, 0, -1), V(3, 0, -3.15), V(4.2, 0, -4.6)], false, "centripetal");
  (function ribbon() {
    const n = 160, half = 1.15, pos = [], uv = [], idx = [];
    let dist = 0, prev = PATH.getPointAt(0);
    for (let i = 0; i <= n; i++) {
      const t = i / n, pt = PATH.getPointAt(t), tg = PATH.getTangentAt(t), side = V(-tg.z, 0, tg.x).normalize();
      dist += pt.distanceTo(prev); prev = pt;
      const a = pt.clone().addScaledVector(side, half), b = pt.clone().addScaledVector(side, -half);
      pos.push(a.x, .012, a.z, b.x, .012, b.z); uv.push(0, dist / 2.3, 1, dist / 2.3);
      if (i < n) idx.push(i * 2, i * 2 + 2, i * 2 + 1, i * 2 + 1, i * 2 + 2, i * 2 + 3);
    }
    const g = new THREE.BufferGeometry(); g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3)); g.setAttribute("uv", new THREE.Float32BufferAttribute(uv, 2)); g.setIndex(idx); g.computeVertexNormals();
    mesh(g, M.path, 0, 0, 0, false, true);
  })();
  // round paved pad around the bench and tree
  mesh(new THREE.CylinderGeometry(4.6, 4.6, .03, 64), M.path, 3.9, .015, -5, false, true);
  mesh(new THREE.TorusGeometry(4.6, .07, 6, 96).rotateX(Math.PI / 2), M.curb, 3.9, .03, -5, false, true);

  /* ---------------- Orange HQ facade bounding the courtyard ---------------- */
  (function hq() {
    const R = 60, cx = 2, cz = 40, a0 = Math.PI - .74, al = 1.48; // concave glass arc wrapping the courtyard
    const floors = 5, fh = 4.2, base = .6;
    // floor plates are annular sectors (an open cylinder sector would cap across the courtyard)
    const ring = (r0, r1, h, mat, y, t0 = a0, tl = al) => {
      const sh = new THREE.Shape(), n = 72;
      for (let i = 0; i <= n; i++) { const a = t0 + tl * i / n; i ? sh.lineTo(Math.sin(a) * r1, -Math.cos(a) * r1) : sh.moveTo(Math.sin(a) * r1, -Math.cos(a) * r1); }
      for (let i = n; i >= 0; i--) { const a = t0 + tl * i / n; sh.lineTo(Math.sin(a) * r0, -Math.cos(a) * r0); }
      const m = new THREE.Mesh(new THREE.ExtrudeGeometry(sh, { depth: h, bevelEnabled: false, curveSegments: 1 }).rotateX(-Math.PI / 2), mat);
      m.position.set(cx, y, cz); m.castShadow = m.receiveShadow = true; scene.add(m); return m;
    };
    const arc = (r, h, mat, y) => { const m = new THREE.Mesh(new THREE.CylinderGeometry(r, r, h, 96, 1, true, a0, al), mat); m.position.set(cx, y, cz); m.receiveShadow = true; scene.add(m); return m; };
    for (let f = 0; f < floors; f++) {
      const y = base + f * fh;
      arc(R, fh - .5, M.glass, y + (fh - .5) / 2 + .5);
      arc(R + 2.2, fh - .5, M.office, y + (fh - .5) / 2 + .5);
      ring(R - 1.1, R + 8, .5, M.slab, y);
    }
    ring(R - 2.8, R + 9, 1.2, M.slab, base + floors * fh, a0 - .03, al + .06);
    ring(R - 2.84, R - 2.76, .14, M.accent, base + floors * fh + .5, a0 - .03, al + .06); // Orange accent line along the roof fascia
    const geo = new THREE.BoxGeometry(.08, floors * fh, .14); const im = new THREE.InstancedMesh(geo, M.mullion, 130); const o = new THREE.Object3D();
    for (let i = 0; i < 130; i++) { const a = a0 + al * (i + .5) / 130; o.position.set(cx + Math.sin(a) * (R - .05), base + floors * fh / 2, cz + Math.cos(a) * (R - .05)); o.rotation.y = a; o.updateMatrix(); im.setMatrixAt(i, o.matrix); }
    scene.add(im);
    // Orange sign on the roof edge, centred above the courtyard
    const signZ = cz - (R - 2.8) + .6;
    const sign = new THREE.Mesh(new THREE.BoxGeometry(5, 5, .4), [M.slab, M.slab, M.slab, M.slab, M.sign, M.slab]);
    sign.position.set(cx, base + floors * fh + 1.2 + 2.6, signZ); sign.castShadow = true; scene.add(sign);
    // entrance canopy and planters along the facade
    const can = new THREE.Mesh(new THREE.BoxGeometry(12, .35, 4), M.slab); can.position.set(cx - 4, 4.1, cz - R + 1.2); can.castShadow = true; scene.add(can);
    for (let i = 0; i < 9; i++) { const a = a0 + .1 + i * (al - .2) / 8, x = cx + Math.sin(a) * (R - 4), z = cz + Math.cos(a) * (R - 4); if (Math.abs(x - (cx - 4)) < 6) continue; mesh(new THREE.BoxGeometry(2.6, .7, 1.1), M.planter, x, .35, z).rotation.y = a; mesh(new THREE.IcosahedronGeometry(.75, 4), M.shrub, x, 1.05, z).scale.set(1.5, .8, .7); }
  })();

  /* ---------------- lamp posts ---------------- */
  [[-9.5, 9.5], [-2.2, 4.6], [-1, -6], [9.5, -1.5]].forEach(([x, z]) => {
    mesh(new THREE.CylinderGeometry(.06, .08, 3.6, 10), M.iron, x, 1.8, z);
    mesh(new THREE.CylinderGeometry(.2, .14, .42, 14), M.lamp, x, 3.75, z, false, false);
  });

  /* ---------------- bench ---------------- */
  const BENCH = V(3, 0, -4.25); // seat centre on the ground plane; the bench faces +z (toward the courtyard)
  (function bench() {
    const g = new THREE.Group(); g.position.copy(BENCH); scene.add(g);
    const add = (geo, mat, x, y, z, rx = 0) => { const m = new THREE.Mesh(geo, mat); m.position.set(x, y, z); m.rotation.x = rx; m.castShadow = m.receiveShadow = true; g.add(m); };
    for (let i = 0; i < 4; i++) add(new THREE.BoxGeometry(1.9, .045, .11), M.wood, 0, .45, -.2 + i * .135);
    for (let i = 0; i < 3; i++) add(new THREE.BoxGeometry(1.9, .1, .035), M.wood, 0, .62 + i * .14, -.3 - i * .035, -.18);
    [-.82, .82].forEach(x => {
      add(new THREE.BoxGeometry(.06, .45, .06), M.iron, x, .225, .2);
      add(new THREE.BoxGeometry(.06, .95, .06), M.iron, x, .47, -.28, -.18);
      add(new THREE.BoxGeometry(.06, .05, .55), M.iron, x, .42, -.03);
      add(new THREE.BoxGeometry(.07, .05, .5), M.iron, x, .68, 0);
      add(new THREE.BoxGeometry(.06, .26, .06), M.iron, x, .56, .2);
    });
  })();

  /* ---------------- orange tree ---------------- */
  const TREE = V(4.9, 0, -6.1);
  const CANOPY = [[V(4.7, 4.3, -5.9), 2.5], [V(3.4, 4, -4.6), 1.6], [V(6.1, 4.1, -6.5), 1.7], [V(4.9, 5.4, -6.2), 1.7], [V(4.2, 3.4, -6.8), 1.4], [V(5.7, 3.5, -4.9), 1.4]];
  const HANG = V(3.12, 2.9, -4.12);  // the orange that falls, hanging under the canopy above the bench seat
  (function tree() {
    const taperTube = (pts, r0, r1, rad = 10) => {
      const curve = new THREE.CatmullRomCurve3(pts), ts = 24, g = new THREE.TubeGeometry(curve, ts, 1, rad, false), p = g.attributes.position;
      for (let i = 0; i <= ts; i++) { const c = curve.getPointAt(i / ts), r = lerp(r0, r1, Math.pow(i / ts, .8)); for (let j = 0; j <= rad; j++) { const k = i * (rad + 1) + j, v = V(p.getX(k), p.getY(k), p.getZ(k)).sub(c).multiplyScalar(r).add(c); p.setXYZ(k, v.x, v.y, v.z); } }
      g.computeVertexNormals(); const m = mesh(g, M.bark); return m;
    };
    taperTube([TREE.clone(), V(4.95, 1, -6.05), V(4.8, 2, -6), V(4.7, 2.7, -5.9)], .2, .1, 12);
    [[V(4.7, 2.6, -5.9), V(4, 3.2, -5.2), V(3.4, 3.7, -4.6)], [V(4.7, 2.6, -5.9), V(5.5, 3.3, -6.3), V(6.1, 3.9, -6.6)], [V(4.75, 2.5, -5.95), V(4.9, 3.6, -6.2), V(4.9, 4.9, -6.2)],
     [V(4.7, 2.4, -5.9), V(4.3, 3, -6.6), V(4.1, 3.4, -7)], [V(4.75, 2.45, -5.9), V(5.4, 3, -5.3), V(5.8, 3.4, -4.9)]].forEach(b => taperTube(b, .085, .03, 8));
    // twig to the falling orange
    taperTube([V(3.6, 3.6, -4.6), V(3.3, 3.35, -4.3), HANG.clone().add(V(0, .12, 0))], .025, .01, 6);
    // dense leaf cover: instanced leaves around canopy blobs plus dark inner volumes
    CANOPY.forEach(([c, r]) => { const m = mesh(new THREE.IcosahedronGeometry(r * .82, 2), M.leafCore, c.x, c.y, c.z, true, true); m.scale.y = .78; });
    const leafShape = new THREE.Shape(); leafShape.moveTo(0, 0); leafShape.quadraticCurveTo(.07, .1, 0, .24); leafShape.quadraticCurveTo(-.07, .1, 0, 0);
    const leafGeo = new THREE.ShapeGeometry(leafShape, 3);
    const count = lowPower ? 4500 : 11000, im = new THREE.InstancedMesh(leafGeo, M.leaf, count), o = new THREE.Object3D(), col = new THREE.Color();
    let k = 0, guard = 0;
    while (k < count && guard++ < count * 4) {
      const [c, r] = CANOPY[Math.floor(rnd() * CANOPY.length)], d = V(rnd() - .5, rnd() - .5, rnd() - .5).normalize(), rr = r * (.78 + rnd() * .24);
      o.position.set(c.x + d.x * rr, c.y + d.y * rr * .78, c.z + d.z * rr);
      if (o.position.distanceTo(HANG) < .22) continue;
      o.rotation.set(rnd() * 6.28, rnd() * 6.28, rnd() * 6.28); const s = .8 + rnd() * .6; o.scale.set(s, s, s); o.updateMatrix(); im.setMatrixAt(k, o.matrix);
      col.setHSL(.27 + rnd() * .06, .45 + rnd() * .25, .16 + rnd() * .16); im.setColorAt(k, col); k++;
    }
    im.count = k; im.castShadow = true; im.receiveShadow = true; scene.add(im);
    // oranges on the canopy surface
    const og = new THREE.SphereGeometry(1, 20, 14), oi = new THREE.InstancedMesh(og, M.orange, 34);
    for (let i = 0; i < 34; i++) { const [c, r] = CANOPY[i % CANOPY.length], d = V(rnd() - .5, -rnd() * .6 - .1, rnd() - .5).normalize(), rr = r * (.86 + rnd() * .08); o.position.set(c.x + d.x * rr, c.y + d.y * rr * .78, c.z + d.z * rr); o.rotation.set(rnd() * 3, rnd() * 3, 0); const s = .085 + rnd() * .025; o.scale.set(s, s * .95, s); o.updateMatrix(); oi.setMatrixAt(i, o.matrix); }
    oi.castShadow = true; scene.add(oi);
  })();
  /* ---------------- a few shrubs and the far courtyard edge ---------------- */
  for (let i = 0; i < 26; i++) { const a = rnd() * 6.28, r = 16 + rnd() * 18, x = Math.sin(a) * r, z = Math.cos(a) * r + 4; if (z < -14) continue; const m = mesh(new THREE.IcosahedronGeometry(.6 + rnd() * .5, 4), M.shrub, x, .45, z); m.scale.set(1.4, .8, 1.2); }

  /* ---------------- crate beside the bench: every fallen orange ends up here ---------------- */
  const CRATE = V(4.36, 0, -4.15);
  (function crate() {
    const g = new THREE.Group(); g.position.copy(CRATE); scene.add(g);
    const add = (w, h, d, x, y, z) => { const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), M.wood); m.position.set(x, y, z); m.castShadow = m.receiveShadow = true; g.add(m); };
    add(.68, .03, .6, 0, .045, 0);
    [-1, 1].forEach(s => { add(.68, .12, .025, 0, .1, s * .29); add(.68, .07, .025, 0, .23, s * .29); add(.025, .12, .6, s * .33, .1, 0); add(.025, .07, .6, s * .33, .23, 0); });
    [[-1, -1], [-1, 1], [1, -1], [1, 1]].forEach(([a, b]) => add(.04, .3, .04, a * .32, .15, b * .28));
  })();
  const crateSlot = i => V(CRATE.x - .2 + (i % 3) * .2, .155 + Math.floor(i / 9) * .17, CRATE.z - .18 + (Math.floor(i / 3) % 3) * .19);

  /* ---------------- the team: procedural, fully articulated people ---------------- */
  const cards = ui.cards, N = Math.max(1, cards.length);
  const LOOKS = [
    { h: 1.78, skin: 0xd9a37c, shirt: 0x17181b, pants: 0x2b2f36, hair: 0x1d1510, shoes: 0x111111, long: false, sleeve: true },
    { h: 1.66, skin: 0xf0c7a2, shirt: 0xff7900, pants: 0x23262b, hair: 0x4a2c16, shoes: 0xe9e4dc, long: true, sleeve: false },
    { h: 1.84, skin: 0x9a6440, shirt: 0xe9e6e1, pants: 0x3a3f47, hair: 0x0f0c0a, shoes: 0x2a1c12, long: false, sleeve: true },
    { h: 1.71, skin: 0xc68d63, shirt: 0x2f4a6b, pants: 0xcfc4b0, hair: 0x2a1a10, shoes: 0x222222, long: false, sleeve: false },
    { h: 1.75, skin: 0xe8b996, shirt: 0x5b5f66, pants: 0x1a1c20, hair: 0x6b4423, shoes: 0x111111, long: true, sleeve: true },
    { h: 1.63, skin: 0x7c4f33, shirt: 0xf3efe8, pants: 0x2e3238, hair: 0x120e0b, shoes: 0xff7900, long: true, sleeve: false },
    { h: 1.81, skin: 0xdcae88, shirt: 0x1f2d24, pants: 0x55493c, hair: 0x302014, shoes: 0x1b1b1b, long: false, sleeve: true }
  ];
  const matCache = {};
  const mat = (c, r = .7) => matCache[c + ":" + r] || (matCache[c + ":" + r] = new THREE.MeshStandardMaterial({ color: c, roughness: r, envMapIntensity: .5 }));
  function limb(parent, r, len, m, x, y, z) { const j = new THREE.Group(); j.position.set(x, y, z); parent.add(j); const s = new THREE.Mesh(new THREE.CapsuleGeometry(r, Math.max(.01, len - r * 2), 4, 12), m); s.position.y = -len / 2; s.castShadow = true; s.receiveShadow = true; j.add(s); return j; }
  function makePerson(L) {
    const k = L.h / 1.75, skin = mat(L.skin, .55), shirt = mat(L.shirt, .8), pants = mat(L.pants, .85), hairM = mat(L.hair, .6), shoe = mat(L.shoes, .5);
    const root = new THREE.Group(), body = new THREE.Group(); body.scale.setScalar(k); root.add(body); scene.add(root);
    const hips = new THREE.Group(); hips.position.y = .92; body.add(hips);
    const pelvis = new THREE.Mesh(new THREE.CapsuleGeometry(.13, .1, 4, 12).rotateZ(Math.PI / 2), pants); pelvis.scale.z = .75; pelvis.position.y = .01; pelvis.castShadow = true; hips.add(pelvis);
    const spine = new THREE.Group(); spine.position.y = .05; hips.add(spine);
    const prof = [[.12, 0], [.135, .08], [.14, .2], [.155, .32], [.18, .41], [.16, .47], [.06, .51]].map(([r, y]) => new THREE.Vector2(r, y));
    const torso = new THREE.Mesh(new THREE.LatheGeometry(prof, 20), shirt); torso.scale.z = .62; torso.castShadow = true; spine.add(torso);
    const neck = new THREE.Group(); neck.position.y = .5; spine.add(neck);
    const nm = new THREE.Mesh(new THREE.CylinderGeometry(.04, .045, .09, 12), skin); nm.position.y = .03; nm.castShadow = true; neck.add(nm);
    const head = new THREE.Group(); head.position.y = .07; neck.add(head);
    const skull = new THREE.Mesh(new THREE.SphereGeometry(.1, 24, 18), skin); skull.scale.set(.88, 1.1, .97); skull.position.set(0, .1, .01); skull.castShadow = true; head.add(skull);
    const hair = new THREE.Mesh(new THREE.SphereGeometry(.106, 24, 14, 0, Math.PI * 2, 0, Math.PI * .55), hairM); hair.scale.set(.92, 1.12, 1.02); hair.position.set(0, .115, -.005); hair.rotation.x = -.25; head.add(hair);
    if (L.long) { const back = new THREE.Mesh(new THREE.CapsuleGeometry(.08, .12, 4, 12), hairM); back.scale.set(1.05, 1, .55); back.position.set(0, .03, -.06); back.castShadow = true; head.add(back); }
    const eyeM = mat(0x1a1410, .3); [-1, 1].forEach(s => { const e = new THREE.Mesh(new THREE.SphereGeometry(.011, 8, 6), eyeM); e.position.set(s * .033, .115, .093); head.add(e); });
    const nose = new THREE.Mesh(new THREE.ConeGeometry(.012, .035, 8).rotateX(Math.PI / 2), skin); nose.position.set(0, .09, .1); head.add(nose);
    const sh = [], el = [], hip = [], kn = [], an = [];
    [1, -1].forEach(s => {
      const a = limb(spine, .047, .29, shirt, s * .19, .44, 0); sh.push(a);
      const e = limb(a, .041, .26, L.sleeve ? shirt : skin, 0, -.29, 0); el.push(e);
      const hand = new THREE.Mesh(new THREE.SphereGeometry(.045, 12, 10), skin); hand.scale.set(.75, 1.25, .55); hand.position.y = -.29; hand.castShadow = true; e.add(hand);
      const t = limb(hips, .072, .44, pants, s * .095, -.03, 0); hip.push(t);
      const kj = limb(t, .056, .43, pants, 0, -.44, 0); kn.push(kj);
      const aj = new THREE.Group(); aj.position.y = -.43; kj.add(aj); an.push(aj);
      const foot = new THREE.Mesh(new THREE.CapsuleGeometry(.045, .14, 4, 10).rotateX(Math.PI / 2), shoe); foot.scale.y = .75; foot.position.set(0, -.035, .05); foot.castShadow = true; aj.add(foot);
    });
    return { root, hips, spine, neck, head, sh, el, hip, kn, an, k, L };
  }
  const people = Array.from({ length: N }, (_, i) => makePerson(LOOKS[i % LOOKS.length]));

  // poses are plain numbers so they can be blended; angles in radians, hip height in metres (unscaled body)
  const POSE0 = { hy: .92, hz: 0, lean: 0, chest: 0, nod: 0, tilt: 0, yawP: 0, hf: [0, 0], kf: [0, 0], sf: [0, 0], sa: [.07, .07], ef: [.12, .12] };
  const clonePose = p => ({ ...p, hf: [...p.hf], kf: [...p.kf], sf: [...p.sf], sa: [...p.sa], ef: [...p.ef] });
  function mix(a, b, t) { const o = clonePose(a); for (const key in a) { if (Array.isArray(a[key])) o[key] = a[key].map((v, i) => lerp(v, b[key][i], t)); else o[key] = lerp(a[key], b[key], t); } return o; }
  function walkPose(phase, amp = 1) {
    const p = clonePose(POSE0), s = Math.sin(phase), c = Math.cos(phase);
    p.hf = [.42 * s * amp, -.42 * s * amp];
    p.kf = [(.06 + .72 * Math.pow(Math.max(0, c), 1.6)) * amp, (.06 + .72 * Math.pow(Math.max(0, -c), 1.6)) * amp];
    p.hy = .92 - .005 * amp + .018 * Math.cos(2 * phase) * amp;
    p.sf = [-.32 * s * amp, .32 * s * amp]; p.ef = [.12 + (.1 + .25 * Math.max(0, -s)) * amp, .12 + (.1 + .25 * Math.max(0, s)) * amp];
    p.lean = .05 * amp; p.yawP = .07 * s * amp; p.nod = -.02 * amp;
    return p;
  }
  function sitPose(s) {
    const p = clonePose(POSE0);
    p.hf = [1.52 * s, 1.52 * s]; p.kf = [1.55 * s, 1.55 * s]; p.hy = lerp(.92, .55, s);
    p.lean = Math.sin(Math.PI * Math.min(1, s * 1.05)) * .42 - .06 * s; p.sf = [.38 * s, .38 * s]; p.ef = [.12 + .85 * s, .12 + .85 * s]; p.sa = [.07 + .08 * s, .07 + .08 * s];
    return p;
  }
  function applyPose(P, q) {
    P.hips.position.y = q.hy; P.hips.position.z = q.hz; P.hips.rotation.y = q.yawP;
    P.spine.rotation.set(q.lean, -q.yawP * 1.4, 0); P.neck.rotation.x = q.chest; P.head.rotation.set(q.nod, 0, q.tilt);
    for (let i = 0; i < 2; i++) {
      P.hip[i].rotation.x = -q.hf[i]; P.kn[i].rotation.x = q.kf[i]; P.an[i].rotation.x = q.hf[i] - q.kf[i] + (q.lean ? 0 : 0);
      P.sh[i].rotation.set(-q.sf[i], 0, (i ? -1 : 1) * q.sa[i]); P.el[i].rotation.x = -q.ef[i];
    }
  }

  /* ---------------- choreography: one member after another ---------------- */
  const INTRO = .04, FINALE = .88, SEG = (FINALE - INTRO) / N;
  const STRIDE = 1.45, walkLen = WALK.getLength(), arrive = WALK.getPointAt(1), front = V(BENCH.x, 0, BENCH.z + .55), seat = V(BENCH.x, 0, BENCH.z + .02);
  const endYaw = (() => { const tg = WALK.getTangentAt(.999); return Math.atan2(tg.x, tg.z); })();
  const LEFT = Math.ceil(N / 2), RIGHT = N - LEFT, LINE_Z = BENCH.z + .62;
  const slotOf = i => i < LEFT ? V(BENCH.x - 1.5 - (LEFT - 1 - i) * .72, 0, LINE_Z) : V(BENCH.x + 2.15 + (RIGHT - 1 - (i - LEFT)) * .72, 0, LINE_Z);
  const localT = (p, i) => (p - INTRO - i * SEG) / SEG;
  const angTo = (a, b) => { let d = b - a; while (d > Math.PI) d -= Math.PI * 2; while (d < -Math.PI) d += Math.PI * 2; return a + d; };

  // returns { visible, pos, yaw, pose } for member i at member-local time t (scroll only)
  function memberState(i, t) {
    const P = people[i];
    if (t < 0) return { visible: false };
    if (t < .32) { // walk in along the path
      const u = seg(t, 0, .32), d = u * walkLen, pt = WALK.getPointAt(u), tg = WALK.getTangentAt(Math.min(u, .999));
      return { visible: true, pos: pt, yaw: Math.atan2(tg.x, tg.z), pose: mix(walkPose(d / (STRIDE * P.k) * Math.PI * 2), POSE0, smooth(seg(t, .29, .32))) };
    }
    if (t < .38) { // turn around in front of the bench, two short steps back
      const u = smooth(seg(t, .32, .38));
      return { visible: true, pos: arrive.clone().lerp(front, u), yaw: lerp(endYaw, angTo(endYaw, 0), u), pose: walkPose(u * Math.PI * 2, .35 * Math.sin(Math.PI * u)) };
    }
    if (t < .82) { // sit, the orange, the reaction, looking up
      const s = smooth(seg(t, .38, .48)), q = sitPose(s);
      const hit = smooth(seg(t, .56, .585)) * (1 - smooth(seg(t, .6, .66)));
      const hand = smooth(seg(t, .57, .61)) * (1 - smooth(seg(t, .66, .71)));
      const look = smooth(seg(t, .66, .72)) * (1 - smooth(seg(t, .8, .82)) * .2);
      q.nod += .28 * hit - .62 * look; q.tilt = .12 * hit; q.chest = -.1 * look; q.lean -= .1 * look;
      q.sf[1] = lerp(q.sf[1], 2.55, hand); q.sa[1] = lerp(q.sa[1], .3, hand); q.ef[1] = lerp(q.ef[1], 2.05, hand);
      q.sf[0] = lerp(q.sf[0], .2, look); q.ef[0] = lerp(q.ef[0], .5, look);
      return { visible: true, pos: front.clone().lerp(seat, s), yaw: 0, pose: q };
    }
    if (t < .9) { // stand up
      const s = 1 - smooth(seg(t, .82, .9)), q = sitPose(s), l = .8 * s; q.nod -= .62 * l; q.chest = -.1 * l; q.lean -= .1 * l; q.sf[0] = lerp(q.sf[0], .2, l); q.ef[0] = lerp(q.ef[0], .5, l);
      return { visible: true, pos: front.clone().lerp(seat, s), yaw: 0, pose: q };
    }
    const slot = slotOf(i);
    if (t < 1) { // walk to a place in the line beside the bench, then face the courtyard
      const u = seg(t, .9, .985), d = front.distanceTo(slot) * u, dir = Math.atan2(slot.x - front.x, slot.z - front.z), turnIn = smooth(seg(t, .02 + .9, .93)), face = smooth(seg(t, .975, 1));
      const yaw = lerp(lerp(0, dir, turnIn), angTo(dir, 0), face);
      return { visible: true, pos: front.clone().lerp(slot, u), yaw, pose: mix(walkPose(d / (STRIDE * P.k) * Math.PI * 2, smooth(seg(t, .9, .92))), POSE0, face) };
    }
    return { visible: true, pos: slot, yaw: 0, pose: POSE0 };
  }
  function poseMember(i, p) {
    const st = memberState(i, localT(p, i)), P = people[i];
    P.root.visible = st.visible; if (!st.visible) { P.root.position.copy(WALK.getPointAt(0)); P.root.rotation.y = 0; applyPose(P, POSE0); return st; }
    P.root.position.copy(st.pos); P.root.rotation.y = st.yaw; applyPose(P, st.pose); return st;
  }
  // where each member's orange touches the head (pose at the moment of contact)
  const contact = people.map((P, i) => { const t = .559; const st = memberState(i, t); P.root.position.copy(st.pos); P.root.rotation.y = st.yaw; applyPose(P, st.pose); P.root.updateMatrixWorld(true); return P.head.localToWorld(V(0, .22, .01)); });

  /* ---------------- oranges: one per member, regrowing on the same twig ---------------- */
  const fruits = people.map(() => { const f = mesh(new THREE.SphereGeometry(.095, 28, 20), M.orange, HANG.x, HANG.y, HANG.z); const st = new THREE.Mesh(new THREE.CylinderGeometry(.008, .01, .05, 6), M.stem); st.position.y = .11; f.add(st); f.userData.stem = st; return f; });
  function poseFruits(p) {
    fruits.forEach((f, i) => {
      const t = localT(p, i), prev = i ? localT(p, i - 1) : 1, C = contact[i], K = crateSlot(i);
      const grow = i ? smooth(seg(prev, .5, .62)) : 1;
      f.visible = grow > .001; f.userData.stem.visible = t < .48;
      let pos = HANG.clone(), spin = 0;
      if (t < .48) { const sway = Math.sin(t * 60) * .01 * seg(t, .4, .48); pos.x += sway; pos.z += sway * .6; f.scale.setScalar(Math.max(.001, grow)); }
      else if (t < .56) { const u = seg(t, .48, .56); pos.set(lerp(HANG.x, C.x, u), lerp(HANG.y, C.y, u * u), lerp(HANG.z, C.z, u)); spin = u * 2; f.scale.setScalar(1); }
      else if (t < .68) { const u = seg(t, .56, .68), e = 1 - Math.pow(1 - u, 1.4); pos = C.clone().lerp(K, e); pos.y = lerp(C.y, K.y, u) + Math.sin(u * Math.PI) * .42; spin = 2 + u * 10; f.scale.setScalar(1); }
      else { pos = K; spin = 12 + i; f.scale.setScalar(1); }
      f.position.copy(pos); f.rotation.set(spin, spin * .4 + i, 0);
    });
  }

  /* ---------------- camera: shots blended by member-local time ---------------- */
  const camPos = V(0, 0, 0), camTgt = V(0, 0, 0);
  const portrait = () => camera.aspect < .9;
  function fitDistance(halfW) { const hf = Math.atan(Math.tan(THREE.MathUtils.degToRad(camera.fov) / 2) * camera.aspect); return halfW / Math.tan(hf); }
  const SHOT = {
    intro: () => [V(-21, 3.4, 23), V(-6, 1.7, 4)],
    group: () => { const tg = V((slotOf(0).x + slotOf(LEFT < N ? LEFT : 0).x) / 2, 1.0, BENCH.z + .3), d = Math.max(6.5, fitDistance(N * .48 + 1.2)); return [tg.clone().add(V(0, .22 * d, d)), tg]; },
    follow: st => { const n = V(.74, 0, .67).multiplyScalar(portrait() ? 5.4 : 4.2), dir = V(Math.sin(st.yaw), 0, Math.cos(st.yaw)); return [st.pos.clone().add(n).add(V(0, 1.55, 0)), st.pos.clone().addScaledVector(dir, 1.3).add(V(0, 1.15, 0))]; },
    sit: () => portrait() ? [V(3.05, 1.7, .6), V(3, .95, -4.1)] : [V(3.6, 1.55, -.2), V(3, 1, -4.1)],
    close: i => { const c = contact[i]; return portrait() ? [V(3.1, 1.55, -1.2), V(3, (c.y + HANG.y) / 2 - .35, -4.1)] : [V(3.25, 1.45, -1.55), V(3.02, (c.y + HANG.y) / 2 - .5, -4.1)]; },
    profile: i => { const c = contact[i]; return portrait() ? [V(3.15, 1.45, -1.35), V(3.0, c.y - .55, -4.1)] : [V(2.15, 1.4, -1.65), V(3.95, c.y - .3, -4.15)]; }
  };
  function memberShots(i, t, st) {
    const first = i === 0 ? SHOT.intro() : SHOT.group(), f = SHOT.follow(st.visible ? st : { pos: WALK.getPointAt(0), yaw: 0 });
    const keys = [[0, first], [.08, f], [.29, f], [.42, SHOT.sit()], [.5, SHOT.close(i)], [.66, SHOT.close(i)], [.72, SHOT.profile(i)], [.84, SHOT.profile(i)], [.95, SHOT.group()]];
    for (let k = 0; k < keys.length - 1; k++) if (t <= keys[k + 1][0]) { const u = smooth(seg(t, keys[k][0], keys[k + 1][0])); return [keys[k][1][0].clone().lerp(keys[k + 1][1][0], u), keys[k][1][1].clone().lerp(keys[k + 1][1][1], u)]; }
    return SHOT.group();
  }
  function poseCamera(p, states) {
    let shot;
    if (p < INTRO) shot = SHOT.intro();
    else if (p >= FINALE) { const g = SHOT.group(), u = smooth(seg(p, FINALE, .93)), tight = [g[0].clone().lerp(g[1], .08), g[1].clone().add(V(0, -.12, 0))]; shot = [g[0].lerp(tight[0], u), g[1].lerp(tight[1], u)]; }
    else { const i = Math.min(N - 1, Math.floor((p - INTRO) / SEG)); shot = memberShots(i, localT(p, i), states[i]); }
    camPos.copy(shot[0]); camTgt.copy(shot[1]); camera.position.copy(camPos); camera.lookAt(camTgt);
  }

  /* ---------------- HTML overlays driven by the same p ---------------- */
  const card = section.querySelector(".tx-member");
  let shownMember = -1;
  function showMember(i) {
    if (i === shownMember || !card) return; shownMember = i;
    const d = cards[i].dataset;
    card.querySelector(".tx-member-idx").textContent = `${d.index} / ${String(N).padStart(2, "0")}`;
    card.querySelector(".tx-member-status").textContent = d.placeholder === "true" ? "Awaiting verification" : "Project contributor";
    card.querySelector("h3").textContent = d.name; card.querySelector(".tx-member-role").textContent = d.role; card.querySelector(".tx-member-text").textContent = d.contrib;
    const img = card.querySelector("img"), ph = card.querySelector(".tx-ph");
    if (d.photo) { img.src = d.photo; img.hidden = false; ph.hidden = true; } else { img.removeAttribute("src"); img.hidden = true; ph.hidden = false; ph.querySelector("b").textContent = d.index; }
    card.dataset.member = String(i);
  }
  function poseOverlay(p) {
    if (api.mode === "static") { ui.reveal.style.opacity = "1"; ui.reveal.inert = false; ui.reveal.classList.add("is-live"); if (card) card.hidden = true; return; }
    ui.intro.style.opacity = String(1 - seg(p, .005, .035)); ui.intro.style.visibility = p > .04 ? "hidden" : "";
    const i = p < INTRO ? -1 : p >= FINALE ? N : Math.floor((p - INTRO) / SEG);
    ui.stages.forEach((li, k) => { li.classList.toggle("is-on", k === i); li.classList.toggle("is-done", k < i); });
    if (api.stage !== i) { api.stage = i; if (ui.live) ui.live.textContent = i < 0 ? "" : i >= N ? "The whole team" : `Team member ${i + 1} of ${N}`; }
    if (ui.bar) ui.bar.style.transform = `scaleX(${p})`;
    const stagesBox = ui.stages[0]?.parentElement; if (stagesBox) stagesBox.style.opacity = String(1 - smooth(seg(p, .9, .94)));
    // the member's profile pops up while they look up at the tree
    let cv = 0;
    if (i >= 0 && i < N) { const t = localT(p, i); cv = smooth(seg(t, .68, .73)) * (1 - smooth(seg(t, .84, .88))); if (cv > 0) showMember(i); }
    if (card) { card.style.opacity = String(cv); card.style.transform = `translateY(${((1 - cv) * 18).toFixed(2)}px) scale(${(.96 + cv * .04).toFixed(4)})`; card.inert = cv < .6; card.classList.toggle("is-live", cv >= .6); }
    // finale: the whole team side by side, then the heading and the team row
    const rv = smooth(seg(p, .93, .97));
    ui.reveal.style.opacity = String(rv); ui.reveal.style.transform = `translateY(${((1 - rv) * 20).toFixed(2)}px)`;
    const live = p > .96; ui.reveal.classList.toggle("is-live", live); ui.reveal.inert = !live;
    ui.cards.forEach((c, k) => { const t = smooth(seg(p, .95 + k * .005, .975 + k * .005)); c.style.opacity = String(t); c.style.transform = `translateY(${((1 - t) * 24).toFixed(2)}px)`; });
  }

  /* ---------------- frame = f(p) ---------------- */
  let W = 0, H = 0, lastP = -1, dirty = true;
  function resize() {
    const r = canvas.getBoundingClientRect(); W = Math.max(1, Math.round(r.width)); H = Math.max(1, Math.round(r.height));
    renderer.setPixelRatio(Math.min(devicePixelRatio || 1, lowPower ? 1.25 : 1.75)); renderer.setSize(W, H, false);
    camera.aspect = W / H; camera.fov = W / H < .8 ? 56 : W / H < 1.3 ? 46 : 38; camera.updateProjectionMatrix(); dirty = true;
  }
  function draw(force) {
    const p = api.p;
    if (!force && !dirty && p === lastP) return;
    lastP = p; dirty = false;
    const states = people.map((_, i) => poseMember(i, p));
    poseFruits(p); poseCamera(p, states); poseOverlay(p);
    renderer.shadowMap.needsUpdate = true;
    renderer.render(scene, camera); api.frames++;
  }
  renderer.shadowMap.autoUpdate = false;
  // a render is requested on scroll/resize only; no continuous loop, so the scene freezes when scrolling stops
  let queued = false;
  const request = () => { if (!queued) { queued = true; requestAnimationFrame(() => { queued = false; draw(); }); } };
  addEventListener("resize", () => { resize(); request(); });

  function setProgress(v) { api.p = Math.round(clamp01(v) * 1e5) / 1e5; request(); }
  function scrollProgress() { const r = section.getBoundingClientRect(), span = section.offsetHeight - innerHeight; return span > 0 ? -r.top / span : 0; }

  if (reduced) {
    section.classList.add("is-static"); api.p = 1; resize(); draw(true);
  } else if (window.gsap && window.ScrollTrigger) {
    gsap.registerPlugin(ScrollTrigger);
    const st = ScrollTrigger.create({ trigger: section, start: "top top", end: "bottom bottom", onUpdate: s => setProgress(s.progress), onRefresh: s => setProgress(s.progress) });
    api.trigger = () => ({ start: st.start, end: st.end });
    api.sync = () => { ScrollTrigger.update(); setProgress(st.progress); draw(); };
    resize(); setProgress(st.progress);
  } else {
    addEventListener("scroll", () => setProgress(scrollProgress()), { passive: true });
    api.trigger = () => { const top = section.getBoundingClientRect().top + scrollY; return { start: top, end: top + section.offsetHeight - innerHeight }; };
    api.sync = () => { setProgress(scrollProgress()); draw(); };
    resize(); setProgress(scrollProgress());
  }
  const r4 = v => +v.toFixed(4);
  api.timeline = { intro: INTRO, finale: FINALE, segment: SEG, members: N };
  api.state = () => ({
    p: api.p, stage: api.stage, camera: camera.position.toArray().map(r4), target: camTgt.toArray().map(r4),
    people: people.map(P => ({ v: P.root.visible, pos: P.root.position.toArray().map(r4), yaw: r4(P.root.rotation.y), hipY: r4(P.hips.position.y * P.k), knee: r4(P.kn[0].rotation.x), head: r4(P.head.rotation.x) })),
    fruits: fruits.map(f => f.visible ? f.position.toArray().map(r4) : null), contact: contact.map(c => c.toArray().map(r4)),
    card: card ? { op: +card.style.opacity, member: +card.style.opacity > 0 ? card.dataset.member : null } : null, reveal: +ui.reveal.style.opacity
  });
  api.ready = true;
  wireCards();
  card?.querySelector(".tx-member-open")?.addEventListener("click", () => openProfile(cards[+card.dataset.member]));
}

/* ---------------- team cards + profile dialog ---------------- */
function wireCards() {
  const dlg = document.getElementById("tx-dialog");
  if (!dlg) return;
  let opener = null;
  const set = (sel, v) => { const el = dlg.querySelector(sel); if (el) el.textContent = v; };
  openProfile = (card, from) => {
    const d = card.dataset; opener = from || document.activeElement;
    set(".tx-dlg-name", d.name); set(".tx-dlg-role", d.role); set(".tx-dlg-text", d.contrib); set(".tx-dlg-status", d.placeholder === "true" ? "Profile awaiting verification" : "Project contributor");
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

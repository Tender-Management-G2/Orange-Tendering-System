/*
 * Our Team — scroll-controlled courtyard scene (Three.js + GSAP ScrollTrigger).
 *
 * Every visible state is a pure function of one number: the scroll progress p in [0, 1] of #tx.
 * Nothing animates on a clock: no autoplay, no loop. The page renders only when p (or the viewport)
 * changes, so it freezes the instant scrolling stops and reverses exactly when scrolling back.
 *
 *   0.00–0.25  the character walks along the path toward the orange tree
 *   0.25–0.45  turns and sits on the bench
 *   0.45–0.65  an orange falls, touches the character's head, the character reacts
 *   0.65–0.80  looks up with curiosity while the camera moves closer
 *   0.80–1.00  Orange-branded transition, "Great Ideas Start with Great People." and the team cards
 *
 * Character: a rigged GLB at the path in data-character-src (set by the view only when the file exists).
 * Its clips are matched by name (see CHARACTER below) and driven deterministically: action.time is set
 * from p and the mixer is evaluated with a zero delta. See Tools/TeamScene/README.md.
 */
import * as THREE from "three";
import { RoomEnvironment } from "./vendor/three/examples/jsm/environments/RoomEnvironment.js";
import { GLTFLoader } from "./vendor/three/examples/jsm/loaders/GLTFLoader.js";

const root = document.getElementById("tx");
if (root) init(root);

function init(section) {
  const canvas = section.querySelector(".tx-canvas");
  const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const lowPower = innerWidth < 760 || (navigator.hardwareConcurrency || 8) <= 4;
  const ui = {
    intro: section.querySelector(".tx-intro"),
    flash: section.querySelector(".tx-flash"),
    reveal: section.querySelector(".tx-reveal"),
    cards: [...section.querySelectorAll(".tx-card")],
    stages: [...section.querySelectorAll(".tx-stages li")],
    bar: section.querySelector(".tx-progress i"),
    live: section.querySelector(".tx-live")
  };
  const api = window.__team = { p: 0, frames: 0, character: "none", ready: false, mode: reduced ? "static" : "scroll" };

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
  const fruit = mesh(new THREE.SphereGeometry(.095, 28, 20), M.orange, HANG.x, HANG.y, HANG.z);
  const fruitStem = new THREE.Mesh(new THREE.CylinderGeometry(.008, .01, .05, 6), M.stem); fruitStem.position.y = .11; fruit.add(fruitStem);

  /* ---------------- a few shrubs and the far courtyard edge ---------------- */
  for (let i = 0; i < 26; i++) { const a = rnd() * 6.28, r = 16 + rnd() * 18, x = Math.sin(a) * r, z = Math.cos(a) * r + 4; if (z < -14) continue; const m = mesh(new THREE.IcosahedronGeometry(.6 + rnd() * .5, 4), M.shrub, x, .45, z); m.scale.set(1.4, .8, 1.2); }

  /* ---------------- character integration point ---------------- */
  const CHARACTER = {
    src: section.dataset.characterSrc || "",
    height: 1.75,           // metres; the model is scaled to this standing height
    stride: 1.45,           // metres covered by one walk cycle (keeps feet planted on the path)
    seatForward: .05,       // seated root position relative to the bench seat centre (z, toward the front edge)
    clips: { walk: /walk/i, sit: /sit.*down|stand.*sit|sit(?!.*idle)/i, sitIdle: /sit.*idle|sitting/i, react: /react|hit|surpri|head/i, lookUp: /look/i }
  };
  const standSpot = WALK.getPointAt(1), seatSpot = V(BENCH.x, 0, BENCH.z + CHARACTER.seatForward);
  const walkLen = WALK.getLength();
  const char = { group: new THREE.Group(), mixer: null, actions: {}, head: null, loaded: false };
  scene.add(char.group);

  // anchor pose of the character for progress p (position on the ground, yaw), shared by camera and orange
  function anchorAt(p) {
    if (p <= .25) { const u = smooth(seg(p, 0, .25)) * .92 + seg(p, 0, .25) * .08, pt = WALK.getPointAt(Math.min(u, 1)), tg = WALK.getTangentAt(Math.min(u, .999)); return { pos: pt, yaw: Math.atan2(tg.x, tg.z), dist: u * walkLen }; }
    const tg = WALK.getTangentAt(.999), yaw0 = Math.atan2(tg.x, tg.z), turn = smooth(seg(p, .25, .31));
    let yaw1 = 0; while (yaw1 - yaw0 > Math.PI) yaw1 -= Math.PI * 2; while (yaw0 - yaw1 > Math.PI) yaw1 += Math.PI * 2;
    const sitMove = smooth(seg(p, .31, .43));
    return { pos: standSpot.clone().lerp(seatSpot, sitMove), yaw: lerp(yaw0, yaw1, turn), dist: walkLen };
  }

  function loadCharacter() {
    if (!CHARACTER.src) { api.character = "missing"; return; }
    api.character = "loading";
    new GLTFLoader().load(CHARACTER.src, gltf => {
      const model = gltf.scene;
      model.traverse(o => { if (o.isMesh) { o.castShadow = true; o.receiveShadow = true; o.frustumCulled = false; } if (o.isBone && !char.head && /head/i.test(o.name) && !/top|end/i.test(o.name)) char.head = o; });
      const box = new THREE.Box3().setFromObject(model), h = box.max.y - box.min.y || 1, s = CHARACTER.height / h;
      model.scale.setScalar(s); model.position.y = -box.min.y * s;
      char.group.add(model);
      char.mixer = new THREE.AnimationMixer(model);
      for (const [key, re] of Object.entries(CHARACTER.clips)) {
        const clip = gltf.animations.find(c => re.test(c.name) && !Object.values(char.actions).some(a => a.getClip() === c));
        if (clip) { const a = char.mixer.clipAction(clip); a.play(); a.setEffectiveWeight(0); a.setEffectiveTimeScale(0); char.actions[key] = a; }
      }
      char.loaded = true; api.character = Object.keys(char.actions).length ? "ready" : "no-clips"; api.clips = Object.keys(char.actions);
      contactCache = null; draw(true);
    }, undefined, () => { api.character = "error"; });
  }

  function poseCharacter(p) {
    const an = anchorAt(p);
    char.group.position.copy(an.pos); char.group.rotation.y = an.yaw;
    if (!char.loaded || !char.mixer) return;
    const A = char.actions, w = {}, t = {};
    const dur = k => A[k] ? A[k].getClip().duration : 0;
    // walk: foot-synced cycle time from distance walked; cross-fades into the sit clip during the turn
    const toSit = smooth(seg(p, .25, .31));
    w.walk = 1 - toSit; t.walk = (an.dist / CHARACTER.stride % 1) * dur("walk");
    w.sit = toSit; t.sit = seg(p, .31, .45) * dur("sit");
    const idle = smooth(seg(p, .45, .48)), react = smooth(seg(p, .55, .57)) * (1 - smooth(seg(p, .66, .69))), look = smooth(seg(p, .65, .69));
    if (A.sitIdle) { w.sitIdle = idle * (1 - react) * (1 - look); w.sit *= 1 - idle; t.sitIdle = seg(p, .45, .55) * dur("sitIdle"); }
    if (A.react) { w.react = react; t.react = seg(p, .55, .65) * dur("react"); }
    if (A.lookUp) { w.lookUp = look; t.lookUp = seg(p, .65, .8) * dur("lookUp"); }
    // keep the weights summed to 1 so the rig never blends toward its bind pose
    let sum = 0; for (const k in A) sum += w[k] || 0;
    if (sum < 1e-3) { const last = A.lookUp && p > .65 ? "lookUp" : A.react && p > .55 ? "react" : A.sitIdle && p > .45 ? "sitIdle" : A.sit && p > .25 ? "sit" : "walk"; if (A[last]) { w[last] = 1; sum = 1; } }
    for (const k in A) { A[k].setEffectiveWeight((w[k] || 0) / (sum || 1)); A[k].time = Math.min(t[k] || 0, Math.max(0, dur(k) - 1e-4)); }
    char.mixer.update(0);
  }

  // the point the falling orange touches: the head bone at the moment of contact (p = .55), or the empty seat
  let contactCache = null;
  function contactPoint() {
    if (contactCache) return contactCache;
    if (char.loaded && char.head) { poseCharacter(.55); char.group.updateMatrixWorld(true); contactCache = char.head.getWorldPosition(V(0, 0, 0)).add(V(0, .14, .02)); }
    else contactCache = V(seatSpot.x + .05, .45 + .095, seatSpot.z + .02);
    return contactCache;
  }

  /* ---------------- falling orange ---------------- */
  function poseFruit(p) {
    const C = contactPoint(), G = V(C.x + .9, .095, C.z + 1.35), R = V(C.x + 1.5, .095, C.z + 2.05);
    let pos, spin = 0;
    if (p < .45) { const sway = Math.sin(p * 90) * .012 * seg(p, .36, .45); pos = HANG.clone().add(V(sway, 0, sway * .6)); }
    else if (p < .55) { const t = seg(p, .45, .55); pos = V(lerp(HANG.x, C.x, t), lerp(HANG.y, C.y, t * t), lerp(HANG.z, C.z, t)); spin = t * 2; }
    else if (p < .65) { const t = seg(p, .55, .65); pos = C.clone().lerp(G, t); pos.y = lerp(C.y, G.y, t) + Math.sin(t * Math.PI) * .38 * (1 - t * .4); spin = 2 + t * 9; }
    else { const t = smooth(seg(p, .65, .8)); pos = G.clone().lerp(R, t); spin = 11 + t * 7; }
    fruit.position.copy(pos); fruit.rotation.set(spin, spin * .4, 0);
    fruitStem.visible = p < .45;
    return pos;
  }

  /* ---------------- camera ---------------- */
  const KEYS = [
    [0, V(-21, 3.4, 23), V(-6, 1.7, 4)],
    [.12, V(-15.5, 2.5, 16.5), V(-3.5, 1.4, 3)],
    [.25, V(-6.8, 2.1, 6.4), V(2.2, 1.15, -3.2)],
    [.35, V(-1.6, 1.9, 3.6), V(3, 1, -4)],
    [.45, V(2.1, 1.55, 1.7), V(3, 1.45, -4.1)],
    [.55, V(2.75, 1.4, -1.35), V(3.05, 1.3, -4.05)],
    [.65, V(1.9, 1.15, -1.75), V(3.1, 1.25, -4.1)],
    [.8, V(3.5, .75, -2.05), V(3.3, 2.9, -4.7)],
    [1, V(3.25, 1.75, -3.25), V(3.45, 4.3, -5)]
  ];
  const camCurve = new THREE.CatmullRomCurve3(KEYS.map(k => k[1]), false, "centripetal"), tgtCurve = new THREE.CatmullRomCurve3(KEYS.map(k => k[2]), false, "centripetal");
  function keyU(p) { for (let i = 0; i < KEYS.length - 1; i++) if (p <= KEYS[i + 1][0]) return (i + seg(p, KEYS[i][0], KEYS[i + 1][0])) / (KEYS.length - 1); return 1; }
  const camPos = V(0, 0, 0), camTgt = V(0, 0, 0);
  function poseCamera(p) {
    const u = keyU(p); camPos.copy(camCurve.getPoint(u)); camTgt.copy(tgtCurve.getPoint(u));
    // during the walk the camera keeps the character's head framed
    const follow = (1 - seg(p, .2, .3)) * .65;
    if (follow > 0) { const a = anchorAt(p).pos; camTgt.lerp(V(a.x, 1.35, a.z), follow); camPos.x += (a.x - camPos.x) * follow * .12; camPos.z += (a.z - camPos.z) * follow * .12; }
    camera.position.copy(camPos); camera.lookAt(camTgt);
  }

  /* ---------------- HTML overlays driven by the same p ---------------- */
  const tmp = V(0, 0, 0);
  function poseOverlay(p) {
    if (api.mode === "static") { ui.flash.style.display = "none"; ui.reveal.style.opacity = "1"; ui.reveal.inert = false; ui.reveal.classList.add("is-live"); ui.cards.forEach(c => { c.style.opacity = "1"; c.style.transform = "none"; }); return; }
    ui.intro.style.opacity = String(1 - seg(p, .01, .06)); ui.intro.style.visibility = p > .07 ? "hidden" : "";
    const stage = p < .25 ? 0 : p < .45 ? 1 : p < .65 ? 2 : p < .8 ? 3 : 4;
    ui.stages.forEach((li, i) => li.classList.toggle("is-on", i === stage));
    if (api.stage !== stage) { api.stage = stage; if (ui.live) ui.live.textContent = ui.stages[stage]?.textContent || ""; }
    const stagesBox = ui.stages[0]?.parentElement; if (stagesBox) stagesBox.style.opacity = String(1 - smooth(seg(p, .84, .9)));
    if (ui.bar) ui.bar.style.transform = `scaleX(${p})`;
    // transition: an orange disc grows from where the orange fell (the "idea" in the tree) and fills the frame
    tmp.copy(HANG).project(camera);
    const fx = (tmp.x * .5 + .5) * 100, fy = (-tmp.y * .5 + .5) * 100, grow = smooth(seg(p, .8, .88));
    ui.flash.style.clipPath = `circle(${(grow * 150).toFixed(2)}% at ${fx.toFixed(1)}% ${fy.toFixed(1)}%)`;
    ui.flash.style.opacity = String(1 - smooth(seg(p, .9, .97)) * .08);
    const rv = seg(p, .86, .92);
    ui.reveal.style.opacity = String(smooth(rv)); ui.reveal.style.transform = `translateY(${((1 - smooth(rv)) * 26).toFixed(2)}px)`;
    const live = p > .9; ui.reveal.classList.toggle("is-live", live); ui.reveal.inert = !live;
    ui.cards.forEach((c, i) => { const t = smooth(seg(p, .9 + i * .012, .94 + i * .012)); c.style.opacity = String(t); c.style.transform = `translateY(${((1 - t) * 34).toFixed(2)}px)`; });
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
    contactPoint(); poseCharacter(p); poseFruit(p); poseCamera(p); poseOverlay(p);
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
    section.classList.add("is-static"); api.p = .76; resize(); draw(true);
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
  api.state = () => ({ p: api.p, stage: api.stage, camera: camera.position.toArray().map(v => +v.toFixed(4)), target: camTgt.toArray().map(v => +v.toFixed(4)), fruit: fruit.position.toArray().map(v => +v.toFixed(4)), anchor: char.group.position.toArray().map(v => +v.toFixed(4)), yaw: +char.group.rotation.y.toFixed(4), reveal: +ui.reveal.style.opacity, character: api.character });
  api.ready = true;
  loadCharacter();
  wireCards();
}

/* ---------------- team cards + profile dialog ---------------- */
function wireCards() {
  const dlg = document.getElementById("tx-dialog");
  if (!dlg) return;
  let opener = null;
  const set = (sel, v) => { const el = dlg.querySelector(sel); if (el) el.textContent = v; };
  document.querySelectorAll(".tx-card").forEach(card => card.addEventListener("click", () => {
    const d = card.dataset; opener = card;
    set(".tx-dlg-name", d.name); set(".tx-dlg-role", d.role); set(".tx-dlg-text", d.contrib); set(".tx-dlg-status", d.placeholder === "true" ? "Profile awaiting verification" : "Project contributor");
    const img = dlg.querySelector(".tx-dlg-photo img"), ph = dlg.querySelector(".tx-dlg-photo .tx-ph");
    if (d.photo) { img.src = d.photo; img.alt = d.name; img.hidden = false; ph.hidden = true; } else { img.removeAttribute("src"); img.hidden = true; ph.hidden = false; ph.querySelector("b").textContent = d.index; }
    const links = dlg.querySelector(".tx-dlg-links"); links.replaceChildren();
    [["GitHub", d.github], ["LinkedIn", d.linkedin]].forEach(([n, u]) => { if (!u) return; const a = document.createElement("a"); a.href = u; a.target = "_blank"; a.rel = "noopener noreferrer"; a.textContent = n + " ↗"; a.setAttribute("aria-label", `${d.name} on ${n} (opens a new tab)`); links.append(a); });
    dlg.showModal();
  }));
  dlg.addEventListener("click", e => { if (e.target === dlg || e.target.closest("[data-tx-close]")) dlg.close(); });
  dlg.addEventListener("close", () => { opener?.focus({ preventScroll: true }); });
}

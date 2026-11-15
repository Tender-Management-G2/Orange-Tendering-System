/* Orange headquarters: a procedural 3D reconstruction (real geometry, no images) of the approved reference
   (Tools/HQ3D/reference.webp). Three.js 0.160.0 (local, import map) + GSAP 3.12.5 ScrollTrigger (layout).
   Scroll: the floors start exploded and assemble into one building while the camera travels a cinematic
   path in 3D; live orange connection lines join each floor to its procurement panel as it locks in place.
   Units: metres. +z = the main (long) front facade, +x = the right-hand facade. */
import * as THREE from "three";
import { EffectComposer } from "./vendor/three/examples/jsm/postprocessing/EffectComposer.js";
import { RenderPass } from "./vendor/three/examples/jsm/postprocessing/RenderPass.js";
import { UnrealBloomPass } from "./vendor/three/examples/jsm/postprocessing/UnrealBloomPass.js";
import { OutputPass } from "./vendor/three/examples/jsm/postprocessing/OutputPass.js";
import { RoomEnvironment } from "./vendor/three/examples/jsm/environments/RoomEnvironment.js";

const gsap = window.gsap, ScrollTrigger = window.ScrollTrigger;
const root = document.documentElement;
const section = document.getElementById("home"), stage = document.getElementById("hq-stage"), canvas = document.getElementById("hq3-canvas");
const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
function webgl() { try { const c = document.createElement("canvas"); return !!(c.getContext("webgl2") || c.getContext("webgl")); } catch (e) { return false; } }
if (!section || !canvas || !webgl()) { root.classList.add("hq3-static"); } else { init(); }

function init() {
  const small = innerWidth < 760, lowPower = small || (navigator.hardwareConcurrency || 8) <= 4;
  const ORANGE = new THREE.Color("#ff7900");
  const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
  const rnd = (seed => () => ((seed = (seed * 16807) % 2147483647) / 2147483647))(42);

  /* ---------------- renderer ---------------- */
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: "high-performance" });
  renderer.setPixelRatio(Math.min(devicePixelRatio, lowPower ? 1 : 1.6));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = .98;
  renderer.shadowMap.enabled = !lowPower;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  const scene = new THREE.Scene();
  scene.background = skyTexture();
  scene.fog = new THREE.FogExp2(0x1c140f, 0.0028);
  const pmrem = new THREE.PMREMGenerator(renderer);
  scene.environment = pmrem.fromScene(new RoomEnvironment(renderer), 0.04).texture;
  // the studio environment only feeds subtle reflections; every material keeps envMapIntensity low for a night scene
  const camera = new THREE.PerspectiveCamera(34, 1, 0.5, 1200);

  const hemi = new THREE.HemisphereLight(0x50525e, 0x2a1606, 0.65); scene.add(hemi);
  const moon = new THREE.DirectionalLight(0xaab8ff, 0.75);
  moon.position.set(-60, 90, 70); moon.castShadow = !lowPower;
  moon.shadow.mapSize.set(1024, 1024);
  Object.assign(moon.shadow.camera, { left: -45, right: 45, top: 50, bottom: -12, near: 20, far: 200 }); // building only
  moon.shadow.bias = -0.0004;
  scene.add(moon);
  const warm = new THREE.DirectionalLight(0xffa860, 0.55); warm.position.set(60, 25, 80); scene.add(warm);
  const entranceLight = new THREE.PointLight(0xff9a40, 150, 34, 2); entranceLight.position.set(-3, 5, 24); scene.add(entranceLight);
  const plazaLight = new THREE.PointLight(0xff8a30, 60, 40, 2); plazaLight.position.set(20, 3, 30); scene.add(plazaLight);

  /* ---------------- procedural textures ---------------- */
  function canvasTex(w, h, draw, srgb = true) {
    const c = document.createElement("canvas"); c.width = w; c.height = h; draw(c.getContext("2d"), w, h);
    const t = new THREE.CanvasTexture(c); if (srgb) t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 4; return t;
  }
  function skyTexture() {
    return canvasTex(4, 512, (g, w, h) => {
      const gr = g.createLinearGradient(0, 0, 0, h);
      gr.addColorStop(0, "#111013"); gr.addColorStop(.45, "#221812"); gr.addColorStop(.68, "#5a3014"); gr.addColorStop(.78, "#3a2010"); gr.addColorStop(1, "#120c09");
      g.fillStyle = gr; g.fillRect(0, 0, w, h);
    });
  }
  const signTex = canvasTex(512, 512, (g, w, h) => {
    g.fillStyle = "#ff7900"; g.fillRect(0, 0, w, h);
    g.fillStyle = "#fff"; g.font = "700 128px Inter, Helvetica, Arial, sans-serif"; g.fillText("orange", 40, 440);
    g.font = "700 34px Arial"; g.fillText("™", 440, 372);
  });
  const ceilingTex = canvasTex(256, 256, (g, w, h) => {
    g.fillStyle = "#6b4a2e"; g.fillRect(0, 0, w, h);
    for (let y = 12; y < h; y += 48) for (let x = 10; x < w; x += 62) { g.fillStyle = "#ffe0b0"; g.fillRect(x, y, 40, 9); }
  });
  ceilingTex.wrapS = ceilingTex.wrapT = THREE.RepeatWrapping; ceilingTex.repeat.set(6, 4);
  const coreTex = canvasTex(256, 128, (g, w, h) => {
    const gr = g.createLinearGradient(0, 0, 0, h); gr.addColorStop(0, "#ffb267"); gr.addColorStop(1, "#7a3d12");
    g.fillStyle = gr; g.fillRect(0, 0, w, h);
    for (let x = 0; x < w; x += 16) { g.fillStyle = "rgba(60,25,5,.35)"; g.fillRect(x, 0, 3, h); }
  });
  const cityTex = canvasTex(64, 128, (g, w, h) => {
    g.fillStyle = "#08090b"; g.fillRect(0, 0, w, h);
    for (let y = 4; y < h; y += 8) for (let x = 4; x < w; x += 8) if (rnd() > .7) { g.fillStyle = rnd() > .5 ? "rgba(255,170,90,.95)" : "rgba(255,225,180,.7)"; g.fillRect(x, y, 4, 4); }
  });
  cityTex.wrapS = cityTex.wrapT = THREE.RepeatWrapping;
  const glowTex = canvasTex(128, 128, (g, w, h) => {
    const gr = g.createRadialGradient(64, 64, 0, 64, 64, 64); gr.addColorStop(0, "rgba(255,150,60,1)"); gr.addColorStop(.45, "rgba(255,110,30,.35)"); gr.addColorStop(1, "rgba(255,100,20,0)");
    g.fillStyle = gr; g.fillRect(0, 0, w, h);
  });

  /* ---------------- materials (PBR) ---------------- */
  const M = {
    slab: new THREE.MeshStandardMaterial({ color: 0xcfcac4, roughness: .42, metalness: .2, envMapIntensity: .28 }),
    slabUnder: new THREE.MeshStandardMaterial({ color: 0x8a7766, roughness: .6, metalness: .1 }),
    glass: new THREE.MeshPhysicalMaterial({ color: 0x0e1418, roughness: .02, metalness: .4, transparent: true, opacity: .2, envMapIntensity: .3, side: THREE.DoubleSide, depthWrite: false }),
    mullion: new THREE.MeshStandardMaterial({ color: 0x6e7378, roughness: .3, metalness: .9, envMapIntensity: .35 }),
    floor: new THREE.MeshStandardMaterial({ color: 0x8a5a30, roughness: .45, emissive: 0x6a3410, emissiveIntensity: .8, envMapIntensity: .2 }),
    ceiling: new THREE.MeshStandardMaterial({ color: 0x3a2412, emissive: 0xffaa60, emissiveMap: ceilingTex, emissiveIntensity: 1.7, roughness: .9 }),
    core: new THREE.MeshStandardMaterial({ color: 0x5a3416, emissive: 0xffffff, emissiveMap: coreTex, emissiveIntensity: 1.7, roughness: .8 }),
    chair: new THREE.MeshStandardMaterial({ color: 0x2a1c14, roughness: .6, emissive: 0x2a1206, emissiveIntensity: .4 }),
    desk: new THREE.MeshStandardMaterial({ color: 0xa87a4a, roughness: .45, emissive: 0x4a2408, emissiveIntensity: .45, envMapIntensity: .2 }),
    person: new THREE.MeshStandardMaterial({ color: 0x1a120c, roughness: .9, envMapIntensity: .05 }),
    leaf: new THREE.MeshStandardMaterial({ color: 0x3f6a2a, roughness: .85, emissive: 0x4a3008, emissiveIntensity: .55, envMapIntensity: .15 }),
    trunk: new THREE.MeshStandardMaterial({ color: 0x3a2618, roughness: .9 }),
    planter: new THREE.MeshStandardMaterial({ color: 0x2b2b2b, roughness: .6, metalness: .3 }),
    sign: new THREE.MeshBasicMaterial({ map: signTex, toneMapped: false }),
    ground: new THREE.MeshStandardMaterial({ color: 0x2a2018, roughness: .12, metalness: .75, envMapIntensity: .45 }),
    door: new THREE.MeshBasicMaterial({ color: 0xffc58a, toneMapped: false }),
    hidden: new THREE.MeshBasicMaterial({ visible: false }),
    rail: new THREE.MeshPhysicalMaterial({ color: 0x9fb0bc, envMapIntensity: .3, transparent: true, opacity: .14, roughness: .05, metalness: .1, side: THREE.DoubleSide, depthWrite: false }),
  };
  M.sign.color.setScalar(1.05);
  const ledMats = [];
  const led = (k = 1) => { const m = new THREE.MeshBasicMaterial({ color: ORANGE.clone().multiplyScalar(2.2 * k), toneMapped: false }); m.userData.k = k; ledMats.push(m); return m; };

  /* ---------------- geometry helpers ---------------- */
  // Rounded rectangle footprint in plan. Corner radii: front-left, front-right, back-right, back-left.
  function plan(w, d, r = [1, 6, 1, 1], ox = 0, oz = 0) {
    const s = new THREE.Shape(), x0 = -w / 2 + ox, x1 = w / 2 + ox, y0 = -d / 2 - oz, y1 = d / 2 - oz; // shape y = -z
    const [fl, fr, br, bl] = r;
    s.moveTo(x0 + fl, y0); s.lineTo(x1 - fr, y0); s.quadraticCurveTo(x1, y0, x1, y0 + fr);
    s.lineTo(x1, y1 - br); s.quadraticCurveTo(x1, y1, x1 - br, y1);
    s.lineTo(x0 + bl, y1); s.quadraticCurveTo(x0, y1, x0, y1 - bl);
    s.lineTo(x0, y0 + fl); s.quadraticCurveTo(x0, y0, x0 + fl, y0);
    return s;
  }
  function prism(shape, h, mat) {
    const g = new THREE.ExtrudeGeometry(shape, { depth: h, bevelEnabled: false, curveSegments: 18 });
    g.rotateX(-Math.PI / 2);
    return new THREE.Mesh(g, mat);
  }
  function outline(shape, y, n = 220) { return shape.getSpacedPoints(n).map(p => new THREE.Vector3(p.x, y, -p.y)); }
  function ledLoop(shape, y, k = 1, r = .07) {
    const pts = outline(shape, y, 260); pts.pop();
    return new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts, true), 520, r, 6, true), led(k));
  }
  function instanced(geo, mat, mats) {
    const m = new THREE.InstancedMesh(geo, mat, mats.length);
    mats.forEach((x, i) => m.setMatrixAt(i, x)); m.instanceMatrix.needsUpdate = true; return m;
  }
  const mtx = (x, y, z, ry = 0, sx = 1, sy = 1, sz = 1) => new THREE.Matrix4().compose(new THREE.Vector3(x, y, z), new THREE.Quaternion().setFromEuler(new THREE.Euler(0, ry, 0)), new THREE.Vector3(sx, sy, sz));
  function tree(parent, x, y, z, s = 1) {
    const g = new THREE.Group(); g.position.set(x, y, z); g.scale.setScalar(s);
    const t = new THREE.Mesh(new THREE.CylinderGeometry(.12, .18, 2.6, 6), M.trunk); t.position.y = 1.3; g.add(t);
    [[0, 3.3, 0, 1.5], [.8, 2.8, .3, 1.1], [-.7, 2.9, -.2, 1.1], [.1, 4.2, .2, 1]].forEach(([a, b, c, r]) => { const f = new THREE.Mesh(new THREE.IcosahedronGeometry(r, 1), M.leaf); f.position.set(a, b, c); f.castShadow = true; g.add(f); });
    parent.add(g); return g;
  }
  function shrubs(parent, from, to, y, n, s = .55) {
    const ms = []; for (let i = 0; i < n; i++) { const t = i / (n - 1); ms.push(mtx(from.x + (to.x - from.x) * t + (rnd() - .5) * .4, y + .3, from.z + (to.z - from.z) * t + (rnd() - .5) * .4, rnd() * 6, s * (.8 + rnd() * .5), s * (.7 + rnd() * .6), s * (.8 + rnd() * .5))); }
    parent.add(instanced(new THREE.IcosahedronGeometry(1, 0), M.leaf, ms));
  }

  function perimeterShrubs(parent, shape, y, n, s0 = .3) {
    const ms = shape.getSpacedPoints(n).map(pt => mtx(pt.x + (rnd() - .5) * .2, y + .28, -pt.y + (rnd() - .5) * .2, rnd() * 6, s0 * (.8 + rnd() * .5), s0 * (.7 + rnd() * .7), s0 * (.8 + rnd() * .5)));
    parent.add(instanced(new THREE.IcosahedronGeometry(1, 0), M.leaf, ms));
  }
  /* One storey: glass curtain wall + mullions + lit interior (+ optional slab on top with its orange LED edge). */
  function storey({ w, d, r, h, top = true, people = 18, desks = 22, ring = false, ox = 0, oz = 0, bal = .35 }) {
    const g = new THREE.Group();
    const inset = bal, glassShape = plan(w - inset * 2, d - inset * 2, r.map(v => Math.max(.2, v - inset)), ox, oz), inner = plan(w - inset * 2 - 1.8, d - inset * 2 - 1.8, r.map(v => Math.max(.2, v - inset - .9)), ox, oz);
    const gh = top ? h - .45 : h;
    const glass = new THREE.Mesh(new THREE.ExtrudeGeometry(glassShape, { depth: gh, bevelEnabled: false, curveSegments: 18 }).rotateX(-Math.PI / 2), [M.hidden, M.glass]);
    glass.renderOrder = 2; g.add(glass);
    // mullions every ~1.7 m along the curtain wall
    const perim = glassShape.getLength(), n = Math.floor(perim / 1.7), ms = [];
    for (let i = 0; i < n; i++) {
      const p = glassShape.getPointAt(i / n), q = glassShape.getPointAt(((i + .01) / n) % 1);
      ms.push(mtx(p.x, gh / 2, -p.y, -Math.atan2(-(q.y - p.y), q.x - p.x)));
    }
    g.add(instanced(new THREE.BoxGeometry(.06, gh, .12), M.mullion, ms));
    // interior: floor finish, glowing ceiling, warm core, desks, chairs and people silhouettes
    const fl = new THREE.Mesh(new THREE.ShapeGeometry(inner).rotateX(-Math.PI / 2), M.floor); fl.position.y = .03; fl.receiveShadow = true; g.add(fl);
    const ce = new THREE.Mesh(new THREE.ShapeGeometry(inner).rotateX(Math.PI / 2), M.ceiling); ce.position.y = gh - .06; g.add(ce);
    const core = new THREE.Mesh(new THREE.BoxGeometry(w * .12, gh - .1, d * .22), M.core); core.position.set(ox - w * .12, (gh - .1) / 2, oz - d * .2); g.add(core);
    // furnished office: rows of desks with chairs along the facades, people, warm partitions and ring lights
    const dm = [], cm = [], pm = [];
    for (let i = 0; i < desks * 1.6; i++) {
      const x = ox + (rnd() - .5) * (w - 5), z = oz + (rnd() - .5) * (d - 5);
      if (Math.abs(x - (ox - w * .12)) < w * .1 && Math.abs(z - (oz - d * .2)) < d * .14) continue;
      const rot = rnd() > .5 ? 0 : Math.PI / 2;
      dm.push(mtx(x, .74, z, rot, 1.6, .06, .8));
      cm.push(mtx(x + (rot ? .7 : 0), .5, z + (rot ? 0 : .7), rot, .5, 1, .5));
    }
    for (let i = 0; i < people; i++) pm.push(mtx(ox + (rnd() - .5) * (w - 4), .82, oz + (rnd() - .5) * (d - 4), 0, .8, .85 + rnd() * .2, .8));
    if (dm.length) { g.add(instanced(new THREE.BoxGeometry(1, 1, 1), M.desk, dm)); g.add(instanced(new THREE.BoxGeometry(1, 1, 1), M.chair, cm)); }
    if (pm.length) g.add(instanced(new THREE.CapsuleGeometry(.2, 1, 3, 8), M.person, pm));
    const part1 = new THREE.Mesh(new THREE.BoxGeometry(w * .34, gh - .2, .12), M.core); part1.position.set(ox + w * .12, (gh - .2) / 2, oz - d * .28); g.add(part1);
    const ringMat = led(.75);
    [[ox + w * .18, oz + d * .12, 3.2], [ox - w * .26, oz + d * .14, 2.2]].concat(ring ? [[ox + 3, oz + 2, 4.2]] : []).forEach(([x, z, r]) => { const t = new THREE.Mesh(new THREE.TorusGeometry(r, .06, 6, 64), ringMat); t.rotation.x = Math.PI / 2; t.position.set(x, gh - .3, z); g.add(t); });
    if (top) {
      const slabShape = plan(w, d, r, ox, oz);
      const slab = prism(slabShape, .45, M.slab); slab.position.y = gh; slab.castShadow = true; slab.receiveShadow = true; g.add(slab);
      g.add(ledLoop(plan(w + .04, d + .04, r.map(v => v + .02), ox, oz), gh + .02));
    }
    return g;
  }

  /* ---------------- the building ---------------- */
  const TW = 36, TD = 26, TR = [10, 10, 10, 10];                 // tower footprint: stadium-rounded like the master reference
  const POD_H = 8.6, FH = 4.8, TOP_H = 5.6;
  const building = new THREE.Group(); scene.add(building);

  // Podium (static foundation): two storeys, wide base with left and right wings.
  const podium = new THREE.Group(); building.add(podium);
  const PW = 66, PD = 32, POX = -3;
  const PR = [10, 10, 10, 10];
  const pod1 = storey({ w: PW, d: PD, r: PR, h: POD_H / 2, people: 14, desks: 30, ox: POX }); podium.add(pod1);
  const pod2 = storey({ w: PW, d: PD, r: PR, h: POD_H / 2, people: 12, desks: 34, ox: POX }); pod2.position.y = POD_H / 2; podium.add(pod2);
  const plinth = prism(plan(PW + 1, PD + 1, PR.map(v => v + .5), POX), .3, M.slabUnder); podium.add(plinth);
  // front steps, bollard lights and the curved light line on the plaza (as in the master reference)
  [0, 1, 2, 3].forEach(i => { const st2 = prism(plan(26 - i * 1.2, 1.1, [.3, .3, .3, .3], -1, PD / 2 + 3.4 - i * .9), .16, M.slab); st2.position.y = i * .16; podium.add(st2); });
  { const bm = [], bt = []; for (let i = 0; i < 14; i++) { const x = -34 + i * 5.2, z = PD / 2 + 7 + Math.sin(i * .7) * 1.2; bm.push(mtx(x, .5, z, 0)); bt.push(mtx(x, 1.02, z, 0)); }
    podium.add(instanced(new THREE.CylinderGeometry(.12, .12, 1, 8), M.mullion, bm)); podium.add(instanced(new THREE.CylinderGeometry(.13, .13, .08, 8), led(1), bt)); }
  // right wing (lower block) and its terrace
  // podium sign, entrance doors and canopy
  const psign = new THREE.Mesh(new THREE.PlaneGeometry(5.4, 5.4), M.sign); psign.position.set(7.5, 4.6, PD / 2 + .08); podium.add(psign);
  // automatic sliding entrance: two framed glass leaves part as the visitor arrives (door.open 0..1)
  const lobbyGlow = new THREE.Mesh(new THREE.PlaneGeometry(10, 4.4), M.door); lobbyGlow.position.set(-3.5, 2.2, PD / 2 - 3); podium.add(lobbyGlow);
  const header = new THREE.Mesh(new THREE.BoxGeometry(5.6, .38, .34), M.slab); header.position.set(-3.5, 3.78, PD / 2 + .3); podium.add(header);
  [-6.25, -.75].forEach(x => { const post = new THREE.Mesh(new THREE.BoxGeometry(.2, 3.6, .34), M.mullion); post.position.set(x, 1.8, PD / 2 + .3); podium.add(post); });
  const sensorMat = led(.5), sensor = new THREE.Mesh(new THREE.BoxGeometry(1.4, .1, .1), sensorMat); sensor.position.set(-3.5, 3.52, PD / 2 + .5); podium.add(sensor);
  const leafMat = new THREE.MeshPhysicalMaterial({ color: 0x8fa6b6, roughness: .03, metalness: .2, transparent: true, opacity: .34, envMapIntensity: .6, depthWrite: false });
  const leaves = [-1, 1].map(sgn => {
    const g = new THREE.Group();
    g.add(new THREE.Mesh(new THREE.BoxGeometry(2.5, 3.4, .06), leafMat));
    [[0, 1.66, 2.5, .08], [0, -1.66, 2.5, .08]].forEach(([x, y, w, h]) => { const f = new THREE.Mesh(new THREE.BoxGeometry(w, h, .1), M.mullion); f.position.set(x, y, 0); g.add(f); });
    const edge = new THREE.Mesh(new THREE.BoxGeometry(.08, 3.4, .1), M.mullion); edge.position.x = sgn * 1.21; g.add(edge);
    g.position.set(-3.5 + sgn * 1.25, 1.72, PD / 2 + .36); g.userData = { base: g.position.x, sgn }; podium.add(g); return g;
  });
  const door = { open: 0 };
  // podium roof terraces (planters and shrubs along the edges)
  perimeterShrubs(podium, plan(PW - 1.6, PD - 1.6, PR.map(v => v - .8), POX), POD_H, 120, .42);
  podium.traverse(o => { if (o.isMesh && o.material === M.slab) o.castShadow = true; });

  // Tower components that assemble (each pivot is its base on the building axis).
  const parts = [];
  function part(name, group, finalY, stageIdx, ex) {
    const pivot = new THREE.Group(); pivot.add(group); building.add(pivot);
    const p = { name, pivot, finalY, stage: stageIdx, ex, inner: group }; parts.push(p); return p;
  }
  const floorDefs = [
    { name: "f1", people: 22, desks: 26 }, { name: "f2", people: 20, desks: 24, ring: true },
    { name: "f3", people: 20, desks: 24 }, { name: "f4", people: 18, desks: 22 }
  ];
  const FLOOR = [{ w: 38, d: 27, ox: 0 }, { w: 36.5, d: 26, ox: -1.6 }, { w: 38.5, d: 27.5, ox: 1.3 }, { w: 36, d: 26, ox: -.9 }];
  floorDefs.forEach((f, i) => {
    const F = FLOOR[i];
    const s = storey({ w: F.w, d: F.d, r: TR, h: FH, people: f.people, desks: f.desks, ring: f.ring, ox: F.ox, bal: 2.1 });
    const rail = new THREE.Mesh(new THREE.ExtrudeGeometry(plan(F.w - .1, F.d - .1, TR.map(v => v - .05), F.ox), { depth: 1.05, bevelEnabled: false, curveSegments: 24 }).rotateX(-Math.PI / 2), [M.hidden, M.rail]);
    rail.position.y = FH; s.add(rail);
    perimeterShrubs(s, plan(F.w - .9, F.d - .9, TR.map(v => v - .45), F.ox), FH, 90, .28);
    part(f.name, s, POD_H + i * FH, i, [[.06, -1.6, 1.2], [-.08, 2.2, -1], [.1, -2.4, .6], [-.07, 1.8, -1.4]][i]);
  });
  // top floor: taller, garden terrace with a tree, upper orange sign
  const tf = storey({ w: 34, d: 24, r: TR, h: TOP_H, top: false, people: 14, desks: 16, bal: .6 });
  
  tree(tf, -6, 0, 7, .8); shrubs(tf, new THREE.Vector3(-13, 0, 10), new THREE.Vector3(-2, 0, 10), 0, 14, .32);
  part("tf", tf, POD_H + 4 * FH, 4, [.09, -2, 1.6]);
  // roof: thick slab with the cantilevered left overhang, rounded front-right corner and orange edge light
  const roof = new THREE.Group();
  const roofShape = plan(TW + 14, TD + 8, [10, 12, 10, 10], 3.5, .6);
  const rslab = prism(roofShape, 1.3, M.slab); rslab.castShadow = true; roof.add(rslab);
  const soffit = prism(plan(TW + 13.6, TD + 7.6, [9.8, 11.8, 9.8, 9.8], 3.5, .6), .06, new THREE.MeshStandardMaterial({ color: 0x6a4428, roughness: .6, emissive: 0x3a1c08, emissiveIntensity: .6, envMapIntensity: .15 })); soffit.position.y = -.04; roof.add(soffit);
  roof.add(ledLoop(plan(TW + 14.04, TD + 8.04, [10, 12, 10, 10], 3.5, .6), .05, 1.25, .1));
  const rsign = new THREE.Mesh(new THREE.BoxGeometry(6, 5.4, .5), [M.slabUnder, M.slabUnder, M.slabUnder, M.slabUnder, M.sign, M.slabUnder]); rsign.position.set(0, 1.3 + .9, (TD + 8) / 2 + .6 + .35); roof.add(rsign); // on the canopy fascia, front centre
  
  part("roof", roof, POD_H + 4 * FH + TOP_H, 5, [-.1, 2.6, -1.8]);
  // entrance canopy (structural component, lands with the first floor)
  const canopy = new THREE.Group();
  const cslab = prism(plan(13, 4.5, [.3, .3, .3, .3], -3.5, 18.2), .35, M.slab); canopy.add(cslab);
  canopy.add(ledLoop(plan(13.04, 4.54, [.3, .3, .3, .3], -3.5, 18.2), -.02, .9, .06));
  part("canopy", canopy, 4.3, 0, [0, 0, 6]);

  // per-floor attach points for the live lines (left facade corner, right facade)
  const ATTACH = {}; // per-floor attach points for the live lines (left and right balcony edges)
  ["f1", "f2", "f3", "f4"].forEach((n, i) => { const F = FLOOR[i], y = POD_H + i * FH + FH * .55; ATTACH[n] = [[F.ox - F.w / 2 - .1, y, 4], [F.ox + F.w / 2 + .1, y, 4]]; });
  ATTACH.roof = [[3.5 - (TW + 14) / 2 + 2, POD_H + 4 * FH + TOP_H + .6, 6], [3.5 + (TW + 14) / 2 - 2, POD_H + 4 * FH + TOP_H + .6, 6]];

  /* ---------------- interior: the curved Orange corridor behind the entrance ----------------
     Left: curved wall with one tile per navbar item. Right: glass meeting rooms. Reflective floor, orange light lines,
     the corridor straightens at its end where the overview video wall is. Built away from the HQ (GZ); the entry
     flash carries the cut. */
  const GZ = -700, HALF = 5.5, CH_ = 6.5;
  const gallery = new THREE.Group(); gallery.position.set(0, 0, GZ); gallery.visible = false; scene.add(gallery);
  const path = new THREE.CatmullRomCurve3([V3(0, 0, 10), V3(0, 0, -6), V3(2, 0, -20), V3(9, 0, -33), V3(20, 0, -42), V3(34, 0, -47), V3(50, 0, -48.5), V3(72, 0, -48.5), V3(88, 0, -48.5)], false, "centripetal");
  function V3(x, y, z) { return new THREE.Vector3(x, y, z); }
  function frameAt(u) { const p = path.getPointAt(clamp(u, 0, 1)), t = path.getTangentAt(clamp(u, 0, 1)); return { p, t, left: V3(t.z, 0, -t.x).normalize(), right: V3(-t.z, 0, t.x).normalize() }; }
  function ribbon(off, y0, y1, mat, n = 180, u0 = 0, u1 = 1) {
    const pos = [], idx = [];
    for (let i = 0; i <= n; i++) { const f = frameAt(u0 + (u1 - u0) * i / n), b = f.p.clone().addScaledVector(f.right, off); pos.push(b.x, y0, b.z, b.x, y1, b.z); if (i < n) { const a = i * 2; idx.push(a, a + 1, a + 2, a + 1, a + 3, a + 2); } }
    const g = new THREE.BufferGeometry(); g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3)); g.setIndex(idx); g.computeVertexNormals();
    const m = new THREE.Mesh(g, mat); gallery.add(m); return m;
  }
  function lineAlong(off, y, mat, r = .05, u0 = 0, u1 = 1) {
    const pts = []; for (let i = 0; i <= 120; i++) { const f = frameAt(u0 + (u1 - u0) * i / 120); pts.push(f.p.clone().addScaledVector(f.right, off).setY(y)); }
    gallery.add(new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 240, r, 6, false), mat));
  }
  const dashTex = canvasTex(512, 300, (g, w, h) => {
    g.fillStyle = "#120c08"; g.fillRect(0, 0, w, h); g.strokeStyle = "rgba(255,121,0,.55)"; g.lineWidth = 3; g.strokeRect(6, 6, w - 12, h - 12);
    for (let i = 0; i < 7; i++) { const bh = 30 + ((i * 37) % 90); g.fillStyle = "#ff7900"; g.fillRect(40 + i * 34, 250 - bh, 20, bh); }
    g.beginPath(); g.lineWidth = 16; g.strokeStyle = "#ff7900"; g.arc(400, 150, 58, -1.2, 3.4); g.stroke(); g.beginPath(); g.strokeStyle = "#3a2a1c"; g.arc(400, 150, 58, 3.4, 5.08); g.stroke();
    g.fillStyle = "rgba(255,255,255,.75)"; for (let i = 0; i < 4; i++) g.fillRect(40, 40 + i * 18, 120 - i * 18, 6);
  });
  const MAT = {
    floor: new THREE.MeshStandardMaterial({ color: 0x14110f, roughness: .08, metalness: .78, envMapIntensity: .45 }),
    wall: new THREE.MeshStandardMaterial({ color: 0x241a13, roughness: .5, metalness: .25, envMapIntensity: .25, side: THREE.DoubleSide }),
    ceil: new THREE.MeshStandardMaterial({ color: 0x0f0c0a, roughness: .7, side: THREE.DoubleSide }),
    glass: new THREE.MeshPhysicalMaterial({ color: 0x2a3540, roughness: .02, metalness: .2, transparent: true, opacity: .2, envMapIntensity: .8, side: THREE.DoubleSide, depthWrite: false }),
    frame: new THREE.MeshStandardMaterial({ color: 0x15161a, roughness: .35, metalness: .85 }),
    chair: new THREE.MeshStandardMaterial({ color: 0x8a3f16, roughness: .6 }),
    table: new THREE.MeshStandardMaterial({ color: 0xd8cfc4, roughness: .3, metalness: .1 }),
    dash: new THREE.MeshBasicMaterial({ map: dashTex, toneMapped: false }),
    down: new THREE.MeshBasicMaterial({ color: new THREE.Color(0xffd9a8).multiplyScalar(1.05), toneMapped: false }),
  };
  {
    const fl = new THREE.Mesh(new THREE.PlaneGeometry(220, 160), MAT.floor); fl.rotation.x = -Math.PI / 2; fl.position.set(40, 0, -40); gallery.add(fl);
    const ce = new THREE.Mesh(new THREE.PlaneGeometry(220, 160), MAT.ceil); ce.rotation.x = Math.PI / 2; ce.position.set(40, CH_, -40); gallery.add(ce);
    ribbon(-HALF, 0, CH_, MAT.wall);                       // curved section wall (left)
    ribbon(HALF, 0, CH_, MAT.glass);                       // glass meeting-room front (right)
    ribbon(HALF + 8, 0, CH_, MAT.wall);                     // back of the meeting rooms
    lineAlong(-HALF + .05, .04, led(1.1), .045); lineAlong(-HALF + .05, CH_ - .25, led(.9), .05);  // wall light lines
    lineAlong(-2.4, .02, led(.9), .03, .02, .9); lineAlong(HALF - .2, .03, led(.7), .03);            // curved floor lines
    lineAlong(-HALF + 1.4, CH_ - .02, led(.7), .08);                                                 // ceiling cove
    // mullions of the glass rooms, downlights along the ceiling, planters along the wall
    const mul = [], dl = [], pl = [];
    for (let i = 0; i <= 60; i++) { const f = frameAt(i / 60); const b = f.p.clone().addScaledVector(f.right, HALF); mul.push(mtx(b.x, CH_ / 2, b.z, Math.atan2(f.t.x, f.t.z), 1, 1, 1)); }
    for (let i = 0; i <= 44; i++) { const f = frameAt(i / 44); [-1.6, 1.6].forEach(o => { const b = f.p.clone().addScaledVector(f.right, o); dl.push(mtx(b.x, CH_ - .03, b.z, 0, 1, 1, 1)); }); }
    gallery.add(instanced(new THREE.BoxGeometry(.1, CH_, .14), MAT.frame, mul));
    gallery.add(instanced(new THREE.CylinderGeometry(.16, .16, .04, 12), MAT.down, dl));
    for (let i = 0; i < 9; i++) { const f = frameAt(.05 + i * .1); const b = f.p.clone().addScaledVector(f.right, -HALF + .7); pl.push(mtx(b.x, .55, b.z, rnd() * 6, .7, .75, .7)); }
    gallery.add(instanced(new THREE.IcosahedronGeometry(1, 1), M.leaf, pl));
    // chevrons on the floor pointing the way in
    const chev = new THREE.Shape(); chev.moveTo(-.9, 0); chev.lineTo(0, .55); chev.lineTo(.9, 0); chev.lineTo(.9, .25); chev.lineTo(0, .8); chev.lineTo(-.9, .25); chev.lineTo(-.9, 0);
    for (let i = 0; i < 4; i++) { const f = frameAt(.03 + i * .018), m = new THREE.Mesh(new THREE.ShapeGeometry(chev), led(.8)); m.rotation.x = -Math.PI / 2; m.rotation.z = -Math.atan2(f.t.x, -f.t.z); m.position.copy(f.p).addScaledVector(f.right, -.8).setY(.03); gallery.add(m); }
    [0, 1].forEach(i => { const f = frameAt(.25 + i * .45); const l = new THREE.PointLight(0xffb070, 140, 70, 1.6); l.position.copy(f.p).setY(CH_ - 1); gallery.add(l); });
  }
  // module stations (corridor positions) and the end video wall
  const STATIONS = [.08, .16, .24, .32, .4, .48, .56, .645]; // one station per navbar item along the curve
  const U_STOPS = [.02, ...STATIONS, .9];
  const tiles = [], rooms = [];
  STATIONS.forEach((u, k) => {
    const f = frameAt(u);
    // tile on the curved wall
    const tg = new THREE.Group(); tg.position.copy(f.p).addScaledVector(f.right, -HALF + .08).setY(2.6); tg.lookAt(tg.position.clone().add(f.right)); gallery.add(tg);
    tg.add(new THREE.Mesh(new THREE.PlaneGeometry(2.6, 4), new THREE.MeshBasicMaterial({ color: 0x1c120a })));
    const tmat = led(.6);
    [[0, 2.06, 2.8, .1], [0, -2.06, 2.8, .1], [-1.36, 0, .1, 4.2], [1.36, 0, .1, 4.2]].forEach(([x, y, w, h]) => { const b = new THREE.Mesh(new THREE.BoxGeometry(w, h, .06), tmat); b.position.set(x, y, .02); tg.add(b); });
    tiles.push({ g: tg, mat: tmat, corners: [[-1.3, 2], [1.3, 2], [1.3, -2], [-1.3, -2]].map(([a, b]) => V3(a, b, .04)) });
    // meeting room behind the glass on the right
    const rc = f.p.clone().addScaledVector(f.right, HALF + 4);
    const room = new THREE.Group(); room.position.copy(rc); room.lookAt(rc.clone().addScaledVector(f.right, -1)); gallery.add(room);
    const table = new THREE.Mesh(new THREE.BoxGeometry(4.2, .08, 1.6), MAT.table); table.position.set(0, .76, .2); room.add(table);
    const legs = new THREE.Mesh(new THREE.BoxGeometry(.2, .74, .8), MAT.frame); legs.position.set(0, .37, .2); room.add(legs);
    const cm = []; [-1.5, -.5, .5, 1.5].forEach(x => { cm.push(mtx(x, .45, -.9, 0)); cm.push(mtx(x, .45, 1.3, Math.PI)); });
    room.add(instanced(new THREE.BoxGeometry(.6, .9, .55), MAT.chair, cm));
    const dash = new THREE.Mesh(new THREE.PlaneGeometry(4.4, 2.6), MAT.dash); dash.position.set(0, 2.5, -3.85); room.add(dash);
    const ringL = new THREE.Mesh(new THREE.TorusGeometry(1.7, .05, 6, 48), led(.9)); ringL.rotation.x = Math.PI / 2; ringL.position.set(0, CH_ - .6, .2); room.add(ringL);
    [-4, 4].forEach(x => { const sidePane = new THREE.Mesh(new THREE.PlaneGeometry(8, CH_), MAT.glass); sidePane.rotation.y = Math.PI / 2; sidePane.position.set(x, CH_ / 2, 0); room.add(sidePane); });
    rooms.push({ g: room, center: rc.clone().setY(2.2), screen: rc.clone().addScaledVector(f.right, 3.85).setY(2.5) }); // corridor-local
  });
  const endF = frameAt(1);
  const vg = new THREE.Group(); vg.position.copy(endF.p).setY(3.2); vg.lookAt(vg.position.clone().addScaledVector(endF.t, -1)); gallery.add(vg);
  vg.add(new THREE.Mesh(new THREE.PlaneGeometry(13, 6), new THREE.MeshBasicMaterial({ color: 0x0b0806 })));
  const vmat = led(1);
  [[0, 3.08, 13.3, .14], [0, -3.08, 13.3, .14], [-6.58, 0, .14, 6.3], [6.58, 0, .14, 6.3]].forEach(([x, y, w, h]) => { const b = new THREE.Mesh(new THREE.BoxGeometry(w, h, .08), vmat); b.position.set(x, y, .03); vg.add(b); });
  const videoWall = { g: vg, corners: [[-6.5, 3], [6.5, 3], [6.5, -3], [-6.5, -3]].map(([a, b]) => V3(a, b, .05)) };

  /* ---------------- environment: plaza, trees, people, skyline ---------------- */
  const ground = new THREE.Mesh(new THREE.PlaneGeometry(260, 220), M.ground); ground.rotation.x = -Math.PI / 2; ground.receiveShadow = true; scene.add(ground);
  [[-3, .02, 30, 70, 34, .55], [24, .02, 26, 40, 18, .32], [-30, .02, 24, 36, 16, .3], [0, .02, 52, 100, 36, .2]].forEach(([x, y, z, w, d, o]) => {
    const m = new THREE.Mesh(new THREE.PlaneGeometry(w, d), new THREE.MeshBasicMaterial({ map: glowTex, transparent: true, opacity: o, blending: THREE.AdditiveBlending, depthWrite: false }));
    m.rotation.x = -Math.PI / 2; m.position.set(x, y, z); scene.add(m);
  });
  [[-34, 22], [-20, 24], [14, 24], [30, 22], [-44, 15], [46, 14]].forEach(([x, z]) => tree(scene, x, 0, z, .9 + rnd() * .35));
  { const pm = []; for (let i = 0; i < 8; i++) pm.push(mtx(-14 + rnd() * 26, .9, 21 + rnd() * 6, 0, .85, .95 + rnd() * .2, .85)); scene.add(instanced(new THREE.CapsuleGeometry(.26, 1.15, 3, 8), M.person, pm)); }
  let cityMat = null, bokeh = null;
  { const cm = cityMat = new THREE.MeshStandardMaterial({ color: 0x15161a, roughness: .8, emissive: 0xffffff, emissiveMap: cityTex, emissiveIntensity: 1.05 }), ms = [];
    for (let i = 0; i < (lowPower ? 18 : 40); i++) { const a = -1.3 + rnd() * 2.6, rad = 230 + rnd() * 180, h = 15 + rnd() * rnd() * 80; ms.push(new THREE.Matrix4().compose(new THREE.Vector3(Math.sin(a) * rad, h / 2, -Math.cos(a) * rad * .8 - 40), new THREE.Quaternion(), new THREE.Vector3(10 + rnd() * 18, h, 10 + rnd() * 18))); }
    scene.add(instanced(new THREE.BoxGeometry(1, 1, 1), cm, ms)); }

  { // curved orange light line sweeping across the plaza in front of the steps
    const arc = new THREE.CatmullRomCurve3([V3p(-60, .06, 30), V3p(-30, .06, 27), V3p(-6, .06, 31), V3p(16, .06, 36), V3p(44, .06, 30), V3p(64, .06, 26)]);
    function V3p(x, y, z) { return new THREE.Vector3(x, y, z); }
    scene.add(new THREE.Mesh(new THREE.TubeGeometry(arc, 200, .09, 6, false), led(1.1)));
  }
  { // soft bokeh city lights near the horizon and orange light streaks on the wet plaza
    const n = lowPower ? 40 : 90, pos = new Float32Array(n * 3), col = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) { const a = -1.5 + rnd() * 3, r = 120 + rnd() * 200; pos.set([Math.sin(a) * r, 2 + rnd() * 26, -Math.cos(a) * r * .8 - 20], i * 3); const c = rnd() > .4 ? [1, .62, .25] : [1, .9, .75]; col.set(c, i * 3); }
    const g = new THREE.BufferGeometry(); g.setAttribute("position", new THREE.BufferAttribute(pos, 3)); g.setAttribute("color", new THREE.BufferAttribute(col, 3));
    bokeh = new THREE.Points(g, new THREE.PointsMaterial({ size: 3.2, map: glowTex, vertexColors: true, transparent: true, opacity: .75, depthWrite: false, blending: THREE.AdditiveBlending })); scene.add(bokeh);
    const streak = new THREE.MeshBasicMaterial({ color: new THREE.Color(0xff8a2a).multiplyScalar(1.3), toneMapped: false });
    [[-40, 30, 90, .16, .08], [10, 42, 110, .14, -.12], [-60, 22, 60, .12, .3]].forEach(([x, z, len, wdt, ry]) => { const m = new THREE.Mesh(new THREE.PlaneGeometry(len, wdt), streak); m.rotation.set(-Math.PI / 2, 0, ry); m.position.set(x + len / 2, .03, z); scene.add(m); });
  }

  /* ---------------- day / night: the site theme switches the exterior between night and daylight ---------------- */
  const nightSky = scene.background, daySky = canvasTex(4, 512, (g, w, h) => {
    const gr = g.createLinearGradient(0, 0, 0, h);
    gr.addColorStop(0, "#5f8fc4"); gr.addColorStop(.42, "#a9c6e2"); gr.addColorStop(.6, "#e8e4dc"); gr.addColorStop(.7, "#d9cdbd"); gr.addColorStop(1, "#bcb3a7");
    g.fillStyle = gr; g.fillRect(0, 0, w, h);
  });
  const isDay = () => document.documentElement.dataset.theme === "light";
  let dayMode = null;
  function applyDayNight() {
    const day = isDay(); if (day === dayMode) return; dayMode = day;
    scene.background = day ? daySky : nightSky;
    scene.fog.color.set(day ? 0xd4d8dc : 0x1c140f); scene.fog.density = day ? .0016 : .0028;
    hemi.color.set(day ? 0xe4ecf7 : 0x50525e); hemi.groundColor.set(day ? 0x8c7c68 : 0x2a1606); hemi.intensity = day ? 1.55 : .65;
    moon.color.set(day ? 0xfff0d8 : 0xaab8ff); moon.intensity = day ? 2.7 : .75;
    warm.intensity = day ? .35 : .55; entranceLight.intensity = day ? 40 : 150; plazaLight.intensity = day ? 10 : 60;
    renderer.toneMappingExposure = day ? 1.02 : .98;
    M.ground.color.set(day ? 0x9a958e : 0x2a2018); M.ground.roughness = day ? .38 : .12; M.ground.metalness = day ? .35 : .75;
    if (cityMat) { cityMat.color.set(day ? 0x9aa3ad : 0x15161a); cityMat.emissiveIntensity = day ? .08 : 1.05; }
    if (bokeh) bokeh.visible = !day;
    if (bloom) bloom.strength = day ? .14 : .38;
    shadowSig = "";
  }
  new MutationObserver(applyDayNight).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });

  /* ---------------- post-processing ---------------- */
  let composer = null, bloom = null;
  if (!lowPower) {
    composer = new EffectComposer(renderer);
    composer.addPass(new RenderPass(scene, camera));
    bloom = new UnrealBloomPass(new THREE.Vector2(512, 512), .38, .35, .96); composer.addPass(bloom);
    composer.addPass(new OutputPass());
  }

  /* ---------------- assembly state ---------------- */
  // g[k] = lock progress of component stage k: 0 canopy+f1, 1 f2, 2 f3, 3 f4, 4 top floor, 5 roof
  const GAP = 3.6;
  const S = { g: reduce ? [1, 1, 1, 1, 1, 1] : [0, 0, 0, 0, 0, 0], glow: 0 };
  function lift(k) { let d = 0; for (let j = 0; j <= k; j++) d += GAP * (1 - S.g[j]); return d; }
  function applyParts(t) {
    for (const p of parts) {
      const k = p.stage, open = 1 - S.g[k], [yaw, dx, dz] = p.ex;
      const bob = reduce ? 0 : Math.sin(t * .8 + k) * .12 * open;
      p.pivot.position.set(dx * open, p.finalY + (p.name === "canopy" ? 2.5 * open : lift(k)) + bob, dz * open);
      p.pivot.rotation.set(0, yaw * open, 0);
    }
  }
  function assembled() { return S.g.reduce((a, b) => a + b, 0) / S.g.length; }

  /* ---------------- camera path (positions + look targets) ---------------- */
  const V = (x, y, z) => new THREE.Vector3(x, y, z);
  // 0..4: assembly (reference three-quarter -> dolly -> orbit + rise -> close to the facade -> reference hero view)
  // 4..6: orbit to the opposite three-quarter view, then fly toward the entrance
  const CAM = new THREE.CatmullRomCurve3([V(14, 12, 112), V(52, 20, 84), V(62, 34, 54), V(38, 26, 40), V(0, 6, 92), V(-44, 16, 70), V(-3.5, 2.5, 30)], false, "centripetal");
  const TGT = new THREE.CatmullRomCurve3([V(0, 26, 0), V(1, 24, 0), V(2, 25, 0), V(-2, 22, 4), V(0, 21.2, 0), V(-2, 16, 0), V(-3.5, 2.2, 14)], false, "centripetal");
  // scroll timeline (0..TL_END): assembly 0..70, orbit and entrance 70..108, corridor GAL0..GAL0+GAL_LEN
  const GAL0 = 108, GAL_LEN = 132, TL_END = GAL0 + GAL_LEN + 6;
  const INTRO = 60; // cinematic intro (welcome, connection, reveal) runs on the same scroll timeline before the assembly
  const camState = { t: 0, walk: 0, gal: 0, intro: 1 }; let override = null, inGallery = false;
  const DOOR_IN = V(-3.5, 2.0, 9), DOOR_T0 = V(-3.5, 2.2, 14), DOOR_T1 = V(-3.5, 2.0, -6);
  // corridor walk: the camera follows the curve, dwells at each module (zero speed at every stop) and turns
  // toward that module's glass room; at the end it faces the overview video wall
  let station = -1;
  function galleryCamera(gal) {
    const seg = U_STOPS.length - 1, sN = clamp(gal, 0, 1) * seg, i = Math.min(seg - 1, Math.floor(sN)), f = sN - i;
    const e = f - Math.sin(2 * Math.PI * f) / (2 * Math.PI) * .92;
    const u = U_STOPS[i] + (U_STOPS[i + 1] - U_STOPS[i]) * e;
    const fr = frameAt(u);
    const pos = fr.p.clone().addScaledVector(fr.right, 1.6).setY(1.75);
    let tgt = path.getPointAt(Math.min(1, u + .09)).addScaledVector(fr.right, -1.2).setY(1.85);
    let best = 0; station = -1;
    STATIONS.forEach((su, k) => { const w = Math.exp(-((u - su) * (u - su)) / (2 * .028 * .028)); if (w > best) { best = w; if (w > .5) station = k; } tgt = tgt.lerp(rooms[k].screen.clone().setY(2.1), w * .22); });
    const wEnd = Math.exp(-((u - .9) * (u - .9)) / (2 * .05 * .05)); if (wEnd > .5) station = STATIONS.length;
    tgt.lerp(vg.position.clone(), wEnd * .9);
    pos.add(gallery.position); tgt.add(gallery.position);
    return [pos, tgt];
  }
  // intro reveal: a long, low approach out of the darkness that ends exactly on the assembly's first camera stop
  const INTRO_CAM = new THREE.CatmullRomCurve3([V(-6, 3, 360), V(-22, 6, 230), CAM.getPoint(0)], false, "centripetal");
  const INTRO_TGT = new THREE.CatmullRomCurve3([V(0, 18, 0), V(0, 23, 0), TGT.getPoint(0)], false, "centripetal");
  const VIEWS = { reference: [V(0, 6, 92), V(0, 21.2, 0)], left: [V(-56, 18, 62), V(-2, 16, 0)], aerial: [V(48, 74, 70), V(0, 14, 0)], rear: [V(-30, 22, -84), V(0, 18, 0)], close: [V(26, 22, 34), V(-2, 21, 4)] };
  function applyCamera() {
    const W = stage.clientWidth, H = stage.clientHeight; camera.aspect = W / H;
    camera.fov = W / H < 1 ? 54 : W / H < 1.4 ? 44 : 36;
    let p, q;
    if (override) { [p, q] = VIEWS[override]; }
    else if (inGallery) { [p, q] = galleryCamera(camState.gal); }
    else {
      p = CAM.getPoint(camState.t); q = TGT.getPoint(camState.t);
      if (camState.intro < 1) { p = INTRO_CAM.getPoint(camState.intro); q = INTRO_TGT.getPoint(camState.intro); }
      if (camState.walk > 0) { p = p.clone().lerp(DOOR_IN, camState.walk); q = q.clone().lerp(DOOR_T0, camState.walk).lerp(DOOR_T1, camState.walk * .6); }
    }
    if (W / H < 1) { p = p.clone().sub(q).multiplyScalar(1.15).add(q); }
    camera.position.copy(p); camera.lookAt(q); camera.updateProjectionMatrix();
  }

  /* ---------------- interactive procurement panels + live lines (HTML/SVG, anchored in 3D) ---------------- */
  const panelsEl = document.getElementById("hq3-panels"), svg = document.getElementById("hq3-lines"), NS = "http://www.w3.org/2000/svg";
  const panels = Array.prototype.map.call(panelsEl.querySelectorAll(".hq3-panel"), el => {
    const floor = el.dataset.floor, side = el.dataset.side === "left" ? 0 : 1, stageIdx = +el.dataset.stage;
    const anchor = V(...(side === 0 ? [-TW / 2 - 13, 0, TD / 2 + 1] : [TW / 2 + 13, 0, 2])); anchor.y = ATTACH[floor][side][1] + (el.dataset.dy ? +el.dataset.dy : 0);
    const glow = document.createElementNS(NS, "path"), core = document.createElementNS(NS, "path"), spark = document.createElementNS(NS, "path"), dot = document.createElementNS(NS, "circle");
    glow.setAttribute("class", "hq3-l-glow"); core.setAttribute("class", "hq3-l-core"); spark.setAttribute("class", "hq3-l-spark"); dot.setAttribute("class", "hq3-l-dot"); dot.setAttribute("r", "4.5");
    [glow, core, spark].forEach(x => x.setAttribute("pathLength", "1"));
    svg.append(glow, core, spark, dot);
    const btn = el.querySelector("button");
    btn.addEventListener("click", () => { const open = btn.getAttribute("aria-expanded") !== "true"; panels.forEach(o => o.btn.setAttribute("aria-expanded", "false")); btn.setAttribute("aria-expanded", String(open)); });
    return { el, btn, floor, side, stageIdx, anchor, glow, core, spark, dot };
  });
  document.addEventListener("keydown", e => { if (e.key === "Escape") panels.forEach(o => o.btn.setAttribute("aria-expanded", "false")); });
  const STAGE_OF_FLOOR = { f1: 0, f2: 1, f3: 2, f4: 3, roof: 5 };
  const v = new THREE.Vector3(), w2 = new THREE.Vector3();
  function project(p) { v.copy(p).project(camera); return { x: (v.x * .5 + .5) * stage.clientWidth, y: (-v.y * .5 + .5) * stage.clientHeight, ok: v.z < 1 }; }
  let panelFade = 1;
  function applyPanels(t) {
    const W = stage.clientWidth, H = stage.clientHeight;
    // Read layout before changing any panel styles to avoid repeated synchronous layouts.
    const sizes = panels.map(pn => ({ width: pn.el.offsetWidth, height: pn.el.offsetHeight }));
    svg.setAttribute("viewBox", `0 0 ${W} ${H}`);
    for (const [index, pn] of panels.entries()) {
      const k = STAGE_OF_FLOOR[pn.floor], grow = S.g[k], show = clamp((S.g[k] + (k > 0 ? S.g[k - 1] : 1) * .25 - .05) * 2.2, 0, 1) * panelFade;
      const fp = parts.find(p => p.name === pn.floor);
      const a = ATTACH[pn.floor][pn.side];
      w2.set(a[0], a[1], a[2]);
      if (fp) { w2.y += fp.pivot.position.y - fp.finalY; w2.x += fp.pivot.position.x; w2.z += fp.pivot.position.z; }
      const A = project(pn.anchor), B = project(w2);
      const r = sizes[index];
      const left = pn.side === 0 ? A.x - r.width : A.x;
      pn.el.style.transform = `translate3d(${left.toFixed(1)}px,${(A.y - r.height / 2).toFixed(1)}px,0)`;
      pn.el.style.opacity = (A.ok ? show : 0).toFixed(3);
      pn.el.style.pointerEvents = show > .6 ? "auto" : "none";
      const sx = pn.side === 0 ? A.x : A.x, sy = A.y, mx = (sx + B.x) / 2;
      const d = `M${sx.toFixed(1)} ${sy.toFixed(1)}C${mx.toFixed(1)} ${sy.toFixed(1)} ${mx.toFixed(1)} ${B.y.toFixed(1)} ${B.x.toFixed(1)} ${B.y.toFixed(1)}`;
      for (const pth of [pn.glow, pn.core, pn.spark]) pth.setAttribute("d", d);
      const off = (1 - grow).toFixed(4), vis = A.ok && B.ok && grow > 0 ? panelFade : 0;
      pn.glow.style.strokeDashoffset = off; pn.core.style.strokeDashoffset = off;
      pn.glow.style.opacity = (vis * (.7 + .3 * S.glow)).toFixed(3); pn.core.style.opacity = vis.toFixed(3);
      pn.spark.style.opacity = (grow >= 1 ? vis * .9 : 0).toFixed(3);
      pn.spark.style.strokeDashoffset = (-((t * .35 + pn.stageIdx * .17) % 1)).toFixed(3); // energy travelling along a completed line
      pn.dot.setAttribute("cx", B.x.toFixed(1)); pn.dot.setAttribute("cy", B.y.toFixed(1)); pn.dot.style.opacity = (clamp((grow - .9) * 10, 0, 1) * vis).toFixed(3);
    }
  }

  /* ---------------- corridor overlays: tiles and video wall mapped onto 3D quads (CSS matrix3d) ---------------- */
  function adj(m) { return [m[4] * m[8] - m[5] * m[7], m[2] * m[7] - m[1] * m[8], m[1] * m[5] - m[2] * m[4], m[5] * m[6] - m[3] * m[8], m[0] * m[8] - m[2] * m[6], m[2] * m[3] - m[0] * m[5], m[3] * m[7] - m[4] * m[6], m[1] * m[6] - m[0] * m[7], m[0] * m[4] - m[1] * m[3]]; }
  function mmul(a, b) { const c = []; for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) { let s2 = 0; for (let k = 0; k < 3; k++) s2 += a[3 * i + k] * b[3 * k + j]; c[3 * i + j] = s2; } return c; }
  function mvec(m, v) { return [m[0] * v[0] + m[1] * v[1] + m[2] * v[2], m[3] * v[0] + m[4] * v[1] + m[5] * v[2], m[6] * v[0] + m[7] * v[1] + m[8] * v[2]]; }
  function basis(p) { const m = [p[0], p[2], p[4], p[1], p[3], p[5], 1, 1, 1], v = mvec(adj(m), [p[6], p[7], 1]); return mmul(m, [v[0], 0, 0, 0, v[1], 0, 0, 0, v[2]]); }
  function quad(dst, w, h) { const t = mmul(basis(dst), adj(basis([0, 0, w, 0, w, h, 0, h]))); for (let i = 0; i < 9; i++) t[i] /= t[8]; return "matrix3d(" + [t[0], t[3], 0, t[6], t[1], t[4], 0, t[7], 0, 0, 1, 0, t[2], t[5], 0, t[8]].map(n => +n.toFixed(6)).join(",") + ")"; }
  const cv = new THREE.Vector3(), cc = new THREE.Vector3();
  function mapQuad(el, obj, corners, w, h, fadeDist) {
    obj.updateMatrixWorld(true);
    const pts = [], W = stage.clientWidth, H = stage.clientHeight; let ok = inGallery;
    for (const c of corners) { cv.copy(c).applyMatrix4(obj.matrixWorld); cv.project(camera); if (cv.z > 1 || cv.z < -1) ok = false; pts.push((cv.x * .5 + .5) * W, (-cv.y * .5 + .5) * H); }
    cc.set(0, 0, 0).applyMatrix4(obj.matrixWorld);
    const o = ok ? clamp(1.3 - cc.distanceTo(camera.position) / fadeDist, 0, 1) : 0;
    el.style.visibility = o > .01 ? "visible" : "hidden"; el.style.opacity = o.toFixed(3); el.style.pointerEvents = o > .5 ? "auto" : "none";
    if (ok) el.style.transform = quad(pts, w, h);
    return o;
  }
  const tileEls = Array.prototype.slice.call(document.querySelectorAll("#hq3-tiles .hq3-tile"));
  const stepEls = Array.prototype.slice.call(document.querySelectorAll("#hq3-steps button"));
  const infoEl = document.getElementById("hq3-info"), infoBlocks = infoEl ? Array.prototype.slice.call(infoEl.querySelectorAll(".hq3-mod")) : [];
  const videoEl = document.getElementById("hq3-video"), uiEl = document.getElementById("hq3-corridor-ui");
  let dismissed = -1, shownStation = -2;
  function applyCards() {
    tileEls.forEach((el, k) => { const o = mapQuad(el, tiles[k].g, tiles[k].corners, 260, 400, 30); tiles[k].mat.color.copy(ORANGE).multiplyScalar(1.2 + (station === k ? 3 : o)); el.classList.toggle("is-on", station === k); });
    if (videoEl) mapQuad(videoEl, videoWall.g, videoWall.corners, 1300, 600, 60);
    if (uiEl) uiEl.classList.toggle("is-on", inGallery);
    stepEls.forEach((b, k) => { b.classList.toggle("is-on", station === k); b.classList.toggle("is-done", station > k); });
    const show = inGallery && station >= 0 && station < STATIONS.length && dismissed !== station ? station : -1;
    if (show !== shownStation) { shownStation = show; if (infoEl) { infoEl.classList.toggle("is-on", show >= 0); infoBlocks.forEach((b, k) => { b.hidden = k !== show; }); } }
    if (station !== dismissed && dismissed >= 0 && station !== -1) dismissed = -1;
  }
  function goStation(k) {
    if (!st) return;
    const gal = (k + 1) / (U_STOPS.length - 1), time = INTRO + GAL0 + GAL_LEN * gal;
    dismissed = -1;
    window.scrollTo({ top: st.start + (st.end - st.start) * time / (INTRO + TL_END), behavior: reduce ? "auto" : "smooth" });
  }
  tileEls.forEach((el, k) => el.addEventListener("click", () => goStation(k)));
  stepEls.forEach((el, k) => el.addEventListener("click", () => goStation(k)));
  const closeBtn = document.getElementById("hq3-info-close"); if (closeBtn) closeBtn.addEventListener("click", () => { dismissed = station; shownStation = -2; });
  // the overview video plays only if the project actually ships the file
  const vid = videoEl && videoEl.querySelector("video");
  if (vid && vid.dataset.available === "true") { vid.src = vid.dataset.src; videoEl.classList.add("has-video"); } // the server checks the file exists

  /* ---------------- cinematic intro: Orange welcome -> glowing connection -> building reveal ----------------
     A 2D canvas plus HTML layer above the 3D stage. Its state is a pure function of the intro progress p (scroll):
       0.00-0.25 welcome: Orange logo, light ribbons and particles, headline, scroll hint
       0.25-0.60 connection: the logo recedes into the horizon, an energy line emerges from it, the view travels
                 forward along it while more paths light up; "Every Great Connection Starts Somewhere."
       0.60-1.00 reveal: the paths converge on the headquarters as the darkness lifts; the camera arrives at the
                 assembly's first stop with the floors still separated and the hero copy fades in */
  const intro = (function () {
    const el = document.getElementById("hq-intro");
    const api = { p: 0, cover: 0, apply() {}, disable() {}, skip() {} };
    if (!el) { camState.intro = 1; return api; }
    const cv = el.querySelector(".hq-intro-fx"), g = cv.getContext("2d");
    const logo = el.querySelector(".hq-intro-logo"), s1 = el.querySelector(".hq-intro-s1"), hint = el.querySelector(".hq-intro-hint"), s2 = el.querySelector(".hq-intro-s2"), skipBtn = el.querySelector(".hq-skip");
    const sm = t => t * t * (3 - 2 * t), sg = (v, a, b) => clamp((v - a) / (b - a), 0, 1), lerp = (a, b, t) => a + (b - a) * t;
    let seed2 = 5; const r = () => ((seed2 = (seed2 * 16807) % 2147483647) - 1) / 2147483646;
    const dust = Array.from({ length: lowPower ? 120 : 240 }, () => ({ x: (r() * 2 - 1) * 46, y: r() * 7, z: r() * 170, s: .4 + r() }));
    const ribbonDots = Array.from({ length: lowPower ? 900 : 1800 }, () => ({ u: r(), k: r(), j: r() }));
    // one flowing S-shaped bundle: every path follows the main curve with its own lateral offset and ripple
    const PATHS = Array.from({ length: lowPower ? 9 : 13 }, (_, k) => k === 0
      ? { off: 0, amp: 0, f: .05, ph: 0, w: 1.15, at: 0 }
      : { off: (k % 2 ? -1 : 1) * (.35 + Math.ceil(k / 2) * (.45 + r() * .35)), amp: .2 + r() * .6, f: .04 + r() * .05, ph: r() * 6.28, w: .35 + r() * .55, at: .06 + k * .022 });
    const base = z => 3.4 * Math.sin(z * .026 + .6) + 1.4 * Math.sin(z * .061);
    let W = 1, H = 1, DPR = 1, queued = false;
    const bld = new THREE.Vector3();
    function size() { const rc = stage.getBoundingClientRect(); W = Math.max(1, rc.width); H = Math.max(1, rc.height); DPR = Math.min(devicePixelRatio || 1, lowPower ? 1.25 : 1.75); cv.width = Math.round(W * DPR); cv.height = Math.round(H * DPR); }
    function buildingOnScreen() { applyCamera(); camera.updateMatrixWorld(); bld.set(0, POD_H + 14, 0).project(camera); return [(bld.x * .5 + .5) * W, (-bld.y * .5 + .5) * H]; }
    function draw() {
      queued = false;
      const p = api.p; g.setTransform(DPR, 0, 0, DPR, 0, 0); g.clearRect(0, 0, W, H);
      const cover = 1 - sm(sg(p, .6, .88)); api.cover = cover;
      const day = isDay(), ADD = day ? "source-over" : "lighter";
      if (cover > 0) { g.globalCompositeOperation = "source-over"; g.fillStyle = day ? `rgba(246,243,238,${cover})` : `rgba(3,2,2,${cover})`; g.fillRect(0, 0, W, H); }
      g.globalCompositeOperation = ADD;
      const vmin = Math.min(W, H), cx = W / 2, logoY = H * (W / H < 1 ? .3 : .32);
      // welcome: warm halo behind the logo and two flowing ribbons of light and particles
      const wa = 1 - sm(sg(p, .2, .34));
      if (wa > 0) {
        const halo = g.createRadialGradient(cx, logoY, 0, cx, logoY, vmin * .55); halo.addColorStop(0, `rgba(255,121,0,${(day ? .22 : .45) * wa})`); halo.addColorStop(.35, `rgba(255,90,0,${(day ? .07 : .12) * wa})`); halo.addColorStop(1, "rgba(0,0,0,0)");
        g.fillStyle = halo; g.fillRect(0, 0, W, H);
        const drift = p * 7;
        const yAt = (u, k) => { const x = u * W, side = Math.abs(u - .5) * 2; return logoY + H * .07 + Math.sin(u * 5.2 + k * 1.3 + drift) * H * (.03 + .09 * side) * (.6 + k * .7) - side * side * H * .05 * (k - .5); };
        for (const d of ribbonDots) { const x = d.u * W, y = yAt(d.u, d.k) + (d.j - .5) * H * .05 * (.3 + Math.abs(d.u - .5)); g.fillStyle = day ? `rgba(${230 + d.j * 25 | 0},${100 + d.j * 40 | 0},0,${(.18 + .4 * d.j) * wa * Math.min(1, Math.abs(d.u - .5) * 6)})` : `rgba(255,${130 + d.j * 60 | 0},40,${(.12 + .35 * d.j) * wa * Math.min(1, Math.abs(d.u - .5) * 6)})`; g.fillRect(x, y, 1.4, 1.4); }
        for (let k = 0; k < 4; k++) {
          g.beginPath(); for (let i = 0; i <= 120; i++) { const u = i / 120; g[i ? "lineTo" : "moveTo"](u * W, yAt(u, k / 3)); }
          g.strokeStyle = `rgba(255,${day ? 110 : 140},${day ? 0 : 40},${(k === 1 ? (day ? .8 : .55) : (day ? .35 : .2)) * wa})`; g.lineWidth = k === 1 ? 2 : 1; g.shadowColor = "#ff7900"; g.shadowBlur = day ? 6 : 16; g.stroke(); g.shadowBlur = 0;
        }
      }
      // connection: a perspective road of light paths, travelled forward by scroll
      const ca = sm(sg(p, .25, .33)) * (1 - sm(sg(p, .86, 1)));
      if (ca > 0) {
        const conv = sm(sg(p, .58, .88)), [bx, by] = conv > 0 ? buildingOnScreen() : [cx, 0];
        const vx = cx + (bx - cx) * conv, hy = lerp(logoY, H * .5, sm(sg(p, .25, .4))) + (by - H * .5) * conv * (conv > 0 ? 1 : 0);
        const camZ = sg(p, .25, 1) * 150, camH = 2.2, f = H * 1.05, zMax = 190;
        const proj = (x, y, z) => { const dz = Math.max(.6, z - camZ); return [vx + x * f / dz, hy + (camH - y) * f / dz, dz]; };
        // floor particles (bokeh) along the route
        for (const d of dust) { let z = d.z - (camZ % 170); if (z < 0) z += 170; const [sx, sy, dz] = proj(d.x * (1 - conv * .6), d.y * .6, camZ + z + 1); if (dz > zMax) continue; const rad = Math.min(7, d.s * 26 / dz); const a = ca * Math.min(1, dz / 6) * (1 - dz / zMax) * (day ? .35 : .5); g.fillStyle = day ? `rgba(240,${110 + (d.s * 40 | 0)},20,${a})` : `rgba(255,${150 + (d.s * 50 | 0)},60,${a})`; g.beginPath(); g.arc(sx, sy, rad, 0, 6.283); g.fill(); }
        const grow = sm(sg(p, .25, .38));
        PATHS.forEach((P, k) => {
          const a = ca * (k === 0 ? 1 : sm(sg(p, .25 + P.at, .33 + P.at)));
          if (a <= .002) return;
          const zFar = camZ + zMax, zNear = k === 0 ? zFar - grow * (zMax - 1) : camZ + 1;
          const pts = []; for (let i = 0; i <= 90; i++) { const t = i / 90, z = zFar - (zFar - zNear) * Math.pow(t, .55), x = (base(z) + P.off * (1 + (z - camZ) * .012) + P.amp * Math.sin(z * P.f + P.ph)) * (1 - conv * .92); pts.push(proj(x, 0, z)); }
          [[14, .06], [5, .22], [1.6, .95]].forEach(([wd, al]) => {
            g.strokeStyle = day ? (al < .5 ? `rgba(255,121,0,${al * a * .7})` : `rgba(${k === 0 ? 235 : 255},${k === 0 ? 100 : 140},0,${al * a})`) : (k === 0 || al < .5 ? `rgba(255,121,0,${al * a})` : `rgba(255,210,150,${al * a})`);
            g.lineCap = "round";
            for (let i = 1; i < pts.length; i++) { const [x0, y0] = pts[i - 1], [x1, y1, dz] = pts[i]; g.lineWidth = Math.max(.6, wd * P.w * Math.min(6, 22 / dz)); g.beginPath(); g.moveTo(x0, y0); g.lineTo(x1, y1); g.stroke(); }
          });
        });
        // horizon glow where the paths meet
        const hg = g.createRadialGradient(vx, hy, 0, vx, hy, vmin * .35); hg.addColorStop(0, `rgba(255,140,40,${(day ? .18 : .35) * ca})`); hg.addColorStop(1, "rgba(0,0,0,0)"); g.fillStyle = hg; g.fillRect(0, 0, W, H);
      }
      g.globalCompositeOperation = "source-over";
      // HTML layer
      const lr = sm(sg(p, .25, .44)), ly = (H * .5 - logoY) * lr, ls = 1 - .9 * lr, lo = 1 - sm(sg(p, .38, .46));
      logo.style.transform = `translate(-50%,-50%) translateY(${ly.toFixed(1)}px) scale(${ls.toFixed(4)})`; logo.style.opacity = lo.toFixed(3);
      const o1 = 1 - sm(sg(p, .1, .22)); s1.style.opacity = o1.toFixed(3); s1.style.transform = `translateY(${(-24 * (1 - o1)).toFixed(1)}px)`;
      hint.style.opacity = (1 - sm(sg(p, .01, .08))).toFixed(3);
      const o2 = sm(sg(p, .41, .47)) * (1 - sm(sg(p, .56, .63))); s2.style.opacity = o2.toFixed(3); s2.style.transform = `translateY(${(14 * (1 - o2)).toFixed(1)}px)`;
      const so = 1 - sm(sg(p, .9, .985)); skipBtn.style.opacity = so.toFixed(3); skipBtn.hidden = so < .02;
      el.classList.toggle("is-cover", cover > .5); el.style.visibility = p >= .995 ? "hidden" : "";
      stage.style.setProperty("--hqi", sm(sg(p, .8, .97)).toFixed(3));
    }
    api.apply = v => { api.p = clamp(v, 0, 1); camState.intro = sm(sg(api.p, .5, 1)); if (!queued) { queued = true; requestAnimationFrame(draw); } };
    api.disable = () => { el.hidden = true; api.p = 1; api.cover = 0; camState.intro = 1; stage.style.setProperty("--hqi", "1"); };
    api.skip = () => { if (st) window.scrollTo({ top: st.start + (st.end - st.start) * INTRO / (INTRO + TL_END) + 2, behavior: "auto" }); };
    skipBtn.addEventListener("click", api.skip);
    new MutationObserver(() => { if (!el.hidden) draw(); }).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
    addEventListener("resize", () => { size(); if (!el.hidden) draw(); });
    size(); camState.intro = 0; api.cover = 1; draw();
    return api;
  })();

  /* ---------------- render loop ---------------- */
  function resize() {
    const W = stage.clientWidth, H = stage.clientHeight;
    renderer.setSize(W, H, false);
    if (composer) { composer.setSize(W, H); bloom.resolution.set(W / 2, H / 2); }
    stage.classList.toggle("hq-wide", W >= 1100 && W / H >= 1.2); stage.classList.toggle("hq-narrow", !(W >= 1100 && W / H >= 1.2));
  }
  const clock = new THREE.Clock();
  renderer.shadowMap.autoUpdate = false; let shadowSig = "";
  let visible = true, raf = 0;
  function frame() {
    const t = clock.getElapsedTime();
    applyParts(t);
    if (renderer.shadowMap.enabled) { const sig = assembled().toFixed(4) + door.open.toFixed(3) + (assembled() < 1 ? t.toFixed(1) : ""); if (sig !== shadowSig) { shadowSig = sig; renderer.shadowMap.needsUpdate = true; } }
    leaves.forEach(l => { l.position.x = l.userData.base + l.userData.sgn * 2.45 * door.open; });
    sensorMat.color.copy(ORANGE).multiplyScalar(1 + door.open * 3);
    const gm = !override && camState.walk >= .72;
    if (gm !== inGallery) { inGallery = gm; gallery.visible = gm; building.visible = !gm; }
    ledMats.forEach(m => m.color.copy(ORANGE).multiplyScalar(2.2 * m.userData.k * (1 + S.glow * .6 * (.6 + .4 * Math.sin(t * 2.4)))));
    applyCamera();
    if (intro.cover < .999) { if (composer) composer.render(); else renderer.render(scene, camera); } // fully covered by the intro: skip the 3D draw
    applyPanels(t);
    applyCards();
  }
  let lastFrame = -Infinity;
  // adaptive resolution: keep the frame budget for ~100 fps on strong GPUs and >= 50 fps on weaker ones
  const MAX_DPR = Math.min(devicePixelRatio, lowPower ? 1 : 1.6), MIN_DPR = Math.min(1, devicePixelRatio) * .75;
  let dpr = renderer.getPixelRatio(), acc = 0, cnt = 0, fps = 0;
  function adapt(cost, gap) {
    if (gap > 0 && gap < 1000) { fps = fps ? fps * .9 + (1000 / gap) * .1 : 1000 / gap; }
    acc += cost; if (++cnt < 40) return;
    const avg = acc / cnt; acc = 0; cnt = 0;
    let next = dpr;
    if (avg > 16 && dpr > MIN_DPR) next = Math.max(MIN_DPR, dpr - .2);
    else if (avg < 7 && dpr < MAX_DPR) next = Math.min(MAX_DPR, dpr + .1);
    if (next !== dpr) { dpr = next; renderer.setPixelRatio(dpr); if (composer) composer.setPixelRatio(dpr); resize(); }
  }
  function loop(now) {
    raf = 0;
    if (!visible || document.hidden) return;
    // Up to 100 fps: high-refresh displays run smoothly without burning 144/240 Hz.
    if (now - lastFrame >= 1000 / 100 - .5) {
      const t0 = performance.now(); frame(); adapt(performance.now() - t0, now - lastFrame); lastFrame = now;
    }
    raf = requestAnimationFrame(loop);
  }
  function resume() {
    if (visible && !document.hidden && !raf) {
      lastFrame = -Infinity;
      raf = requestAnimationFrame(loop);
    } else if ((!visible || document.hidden) && raf) {
      cancelAnimationFrame(raf); raf = 0;
    }
  }
  new IntersectionObserver(es => { visible = es[0].isIntersecting; resume(); }).observe(stage);
  document.addEventListener("visibilitychange", resume);
  addEventListener("resize", () => { resize(); if (visible && !document.hidden) frame(); });
  applyDayNight(); resize(); resume();
  root.classList.add("hq3-live");

  /* ---------------- verification hooks (read-only, used by the Playwright QA) ---------------- */
  const order = ["f1", "f2", "f3", "f4", "tf", "roof"];
  window.__hq3 = {
    get graphics() {
      const gl = renderer.getContext(), debug = gl.getExtension("WEBGL_debug_renderer_info");
      return {
        renderer: debug ? gl.getParameter(debug.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER),
        powerPreference: gl.getContextAttributes()?.powerPreference,
        drawCalls: renderer.info.render.calls,
        triangles: renderer.info.render.triangles
      };
    },
    get assembly() { return assembled(); },
    get trigger() { return st ? { start: st.start, end: st.end } : null; },
    get camera() { return { pos: camera.position.toArray().map(n => +n.toFixed(2)), dir: camera.getWorldDirection(new THREE.Vector3()).toArray().map(n => +n.toFixed(3)) }; },
    parts() { return parts.map(p => ({ name: p.name, pos: p.pivot.position.toArray().map(n => +n.toFixed(4)), rotY: +p.pivot.rotation.y.toFixed(5), finalY: p.finalY })); },
    // structural seams: top of each component vs bottom of the next (metres); 0 = joined, >0 = gap, <0 = overlap
    seams() {
      const box = new THREE.Box3(), out = [];
      const measure = p => {
        p.pivot.updateMatrixWorld(true);
        box.setFromObject(p.inner.children[0], true); const bottom = box.min.y; let top = box.max.y;
        const slab = p.inner.children.find(c => c.isMesh && c.material === M.slab); if (slab) { box.setFromObject(slab, true); top = box.max.y; }
        return { bottom, top };
      };
      let prevTop = POD_H, prev = "podium";
      for (const n of order) {
        const p = parts.find(x => x.name === n), m = measure(p);
        out.push({ joint: prev + "/" + n, gap: +(m.bottom - prevTop).toFixed(4), offsetXZ: +Math.hypot(p.pivot.position.x, p.pivot.position.z).toFixed(4), rotY: +p.pivot.rotation.y.toFixed(5) });
        prevTop = m.top; prev = n;
      }
      return out;
    },
    setView(name) { override = name || null; frame(); },
    get fps() { return Math.round(fps); },
    get pixelRatio() { return dpr; },
    get door() { return +door.open.toFixed(4); },
    get inGallery() { return inGallery; },
    get station() { return station; },
    get stations() { return STATIONS.length; },
    timeline: { intro: INTRO, gal0: INTRO + GAL0, galLen: GAL_LEN, end: INTRO + TL_END },
    get intro() { return { p: intro.p, cover: +intro.cover.toFixed(4), camera: +camState.intro.toFixed(4) }; },
    skipIntro() { intro.skip(); },
    goStation(k) { goStation(k); },
    // completes the scroll scrub immediately (software-rendered QA browsers draw only a few frames per second)
    sync() { if (st) { ScrollTrigger.update(); const tw = st.getTween && st.getTween(); if (tw) tw.progress(1); st.animation.progress(st.progress); } frame(); },
    frame
  };
  let st = null;
  if (!gsap || !ScrollTrigger || reduce) { intro.disable(); camState.t = 4 / 6; S.glow = .3; frame(); return; } // reduced motion: completed building, hero view

  /* ---------------- scroll storytelling ---------------- */
  gsap.registerPlugin(ScrollTrigger);
  const STAGES = [
    [0, "00", "Scroll to assemble the headquarters"],
    [8, "01", "Purchase Requests & Tenders — the first floor locks onto the podium"],
    [27, "02", "Suppliers & Bidding — the second floor lands"],
    [45, "03", "Compliance & AI Analysis — the third floor connects"],
    [63, "04", "Evaluation & Reports — the upper floors settle"],
    [85, "05", "Approvals & Award — the roof completes one headquarters"]
  ];
  const label = document.getElementById("hq-label"), bar = document.getElementById("hq-progress"), copy = document.getElementById("hq-copy"), fade = document.getElementById("hq-fade"), cap = document.getElementById("hq3-cap");
  const WIN = [[6, 18], [19, 31], [32, 44], [45, 52], [52, 59], [60, 70]]; // timeline windows (0..100), assembly = 0..70
  let lastIdx = -1;
  function build() {
    const tl = gsap.timeline({ defaults: { ease: "none" } });
    WIN.forEach(([a, b], k) => { const o0 = {}, o1 = {}; o0[k] = 0; o1[k] = 1; tl.fromTo(S.g, o0, Object.assign({ duration: b - a, ease: "power2.inOut", immediateRender: false }, o1), a); });
    tl.fromTo(camState, { t: 0 }, { t: 4 / 6, duration: 70, ease: "sine.inOut", immediateRender: false }, 0);
    tl.to(camState, { t: 5 / 6, duration: 15, ease: "sine.inOut" }, 70);
    tl.to(camState, { t: 1, duration: 13, ease: "power2.in" }, 85);
    tl.to([copy, "#hq-shade"], { autoAlpha: 0, duration: 5, ease: "power2.in" }, 2);
    tl.to(S, { glow: 1, duration: 5, ease: "power2.out" }, 66);
    const pf = { v: 1 }; tl.fromTo(pf, { v: 1 }, { v: 0, duration: 4, immediateRender: false, onUpdate() { panelFade = pf.v; } }, 86);
    tl.to(".hq-stagebar, #hq3-cap", { autoAlpha: 0, duration: 3 }, 86);
    // the automatic doors open, the camera walks through a warm flash into the gallery hall
    tl.fromTo(door, { open: 0 }, { open: 1, duration: 6, ease: "power2.inOut", immediateRender: false }, 92);
    tl.fromTo(camState, { walk: 0 }, { walk: 1, duration: 9, ease: "power1.in", immediateRender: false }, 97);
    tl.fromTo("#hq3-flash", { opacity: 0 }, { opacity: 1, duration: 3, ease: "power2.in", immediateRender: false }, 100.6)
      .to("#hq3-flash", { opacity: 0, duration: 4.5, ease: "power2.out" }, 103.6);
    // inside: one station per homepage section, then the portal into the platform sections
    tl.fromTo(camState, { gal: 0 }, { gal: 1, duration: GAL_LEN, ease: "none", immediateRender: false }, GAL0);
    tl.to(fade, { opacity: 1, duration: 3 }, TL_END - 3.5);
    tl.to({}, { duration: .01 }, TL_END);
    tl.eventCallback("onUpdate", () => {
      const time = tl.time(), pct = clamp(time / 70 * 100, 0, 100); let idx = 0;
      STAGES.forEach((s, i) => { if (pct >= s[0]) idx = i; });
      if (idx !== lastIdx) {
        lastIdx = idx; label.innerHTML = `<b>${STAGES[idx][1]}</b><span>${STAGES[idx][2]}</span>`;
        if (cap) { const shown = panels.filter(p => STAGE_OF_FLOOR[p.floor] === [-1, 0, 1, 2, 3, 5][idx]); cap.innerHTML = shown.map(p => `<div><b>${p.el.querySelector(".hq3-name").textContent}</b><span>${p.el.querySelector(".hq3-desc").textContent}</span></div>`).join(""); }
      }
      bar.style.transform = `scaleX(${(pct / 100).toFixed(4)})`;
      stage.classList.toggle("is-assembled", pct >= 99.9);
    });
    return tl;
  }
  const mm = gsap.matchMedia();
  mm.add({ wide: "(min-width:1100px)", mid: "(min-width:700px) and (max-width:1099px)", small: "(max-width:699px)" }, ctx => {
    const c = ctx.conditions, main = build(), tl = gsap.timeline({ defaults: { ease: "none" } });
    // intro first, then the existing assembly / orbit / entrance / corridor timeline, all on one scrubbed timeline
    const io = { p: 0 };
    tl.fromTo(io, { p: 0 }, { p: 1, duration: INTRO, immediateRender: false, onUpdate() { intro.apply(io.p); } }, 0);
    tl.add(main, INTRO);
    st = ScrollTrigger.create({ trigger: section, start: "top top", end: () => "+=" + Math.round(innerHeight * (c.wide ? 15 : c.mid ? 9 : 7) * (INTRO + TL_END) / 200), pin: true, scrub: c.wide ? 1 : .7, anticipatePin: 1, animation: tl, invalidateOnRefresh: true });
    const see = [...document.querySelectorAll("[data-see-action]")];
    function onSee(e) {
      e.preventDefault();
      const y0 = scrollY, target = st.end + 2, dur = clamp((target - y0) * 1.2, 5000, 14000), start = performance.now(); let stop = false;
      const cancel = () => { stop = true; removeEventListener("wheel", cancel); removeEventListener("touchstart", cancel); removeEventListener("keydown", cancel); };
      addEventListener("wheel", cancel, { passive: true }); addEventListener("touchstart", cancel, { passive: true }); addEventListener("keydown", cancel);
      (function step(now) { const k = Math.min(1, (now - start) / dur), e2 = k < .5 ? 2 * k * k : 1 - Math.pow(-2 * k + 2, 2) / 2; scrollTo(0, y0 + (target - y0) * e2); if (k < 1 && !stop) requestAnimationFrame(step); else cancel(); })(start);
    }
    see.forEach(a => a.addEventListener("click", onSee));
    return () => { see.forEach(a => a.removeEventListener("click", onSee)); st && st.kill(); st = null; tl.kill(); main.kill(); };
  });
  addEventListener("load", () => ScrollTrigger.refresh());
}

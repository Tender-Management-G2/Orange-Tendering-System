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
  renderer.toneMappingExposure = .92;
  renderer.shadowMap.enabled = !lowPower;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  const scene = new THREE.Scene();
  scene.background = skyTexture();
  scene.fog = new THREE.FogExp2(0x15100c, 0.0042);
  const pmrem = new THREE.PMREMGenerator(renderer);
  scene.environment = pmrem.fromScene(new RoomEnvironment(renderer), 0.04).texture;
  // the studio environment only feeds subtle reflections; every material keeps envMapIntensity low for a night scene
  const camera = new THREE.PerspectiveCamera(34, 1, 0.5, 1200);

  scene.add(new THREE.HemisphereLight(0x3a4252, 0x1a0e06, 0.55));
  const moon = new THREE.DirectionalLight(0xaab8ff, 0.75);
  moon.position.set(-60, 90, 70); moon.castShadow = !lowPower;
  moon.shadow.mapSize.set(2048, 2048);
  Object.assign(moon.shadow.camera, { left: -70, right: 70, top: 70, bottom: -30, near: 10, far: 260 });
  moon.shadow.bias = -0.0004;
  scene.add(moon);
  const warm = new THREE.DirectionalLight(0xffa860, 0.55); warm.position.set(60, 25, 80); scene.add(warm);
  const entranceLight = new THREE.PointLight(0xff9a40, 260, 40, 2); entranceLight.position.set(-3, 5, 24); scene.add(entranceLight);
  const plazaLight = new THREE.PointLight(0xff8a30, 120, 50, 2); plazaLight.position.set(20, 3, 30); scene.add(plazaLight);

  /* ---------------- procedural textures ---------------- */
  function canvasTex(w, h, draw, srgb = true) {
    const c = document.createElement("canvas"); c.width = w; c.height = h; draw(c.getContext("2d"), w, h);
    const t = new THREE.CanvasTexture(c); if (srgb) t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 4; return t;
  }
  function skyTexture() {
    return canvasTex(4, 512, (g, w, h) => {
      const gr = g.createLinearGradient(0, 0, 0, h);
      gr.addColorStop(0, "#0d0d10"); gr.addColorStop(.5, "#18130f"); gr.addColorStop(.72, "#3a2010"); gr.addColorStop(.8, "#1b120c"); gr.addColorStop(1, "#0a0807");
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
    glass: new THREE.MeshPhysicalMaterial({ color: 0x1c2833, roughness: .03, metalness: .3, transparent: true, opacity: .3, envMapIntensity: .55, side: THREE.DoubleSide, depthWrite: false }),
    mullion: new THREE.MeshStandardMaterial({ color: 0x1d1f22, roughness: .45, metalness: .7, envMapIntensity: .3 }),
    floor: new THREE.MeshStandardMaterial({ color: 0x7a4a24, roughness: .55, emissive: 0x5a2c0c, emissiveIntensity: .6, envMapIntensity: .2 }),
    ceiling: new THREE.MeshStandardMaterial({ color: 0x2a1a0e, emissive: 0xffb36b, emissiveMap: ceilingTex, emissiveIntensity: 1.6, roughness: .9 }),
    core: new THREE.MeshStandardMaterial({ color: 0x3a2412, emissive: 0xffffff, emissiveMap: coreTex, emissiveIntensity: 1.25, roughness: .8 }),
    desk: new THREE.MeshStandardMaterial({ color: 0x1b1612, roughness: .7, envMapIntensity: .1 }),
    person: new THREE.MeshStandardMaterial({ color: 0x120d0a, roughness: .9, envMapIntensity: .05 }),
    leaf: new THREE.MeshStandardMaterial({ color: 0x1f3317, roughness: .9, emissive: 0x3a2206, emissiveIntensity: .3, envMapIntensity: .1, flatShading: false }),
    trunk: new THREE.MeshStandardMaterial({ color: 0x3a2618, roughness: .9 }),
    planter: new THREE.MeshStandardMaterial({ color: 0x2b2b2b, roughness: .6, metalness: .3 }),
    sign: new THREE.MeshBasicMaterial({ map: signTex, toneMapped: false }),
    ground: new THREE.MeshStandardMaterial({ color: 0x0a0a0b, roughness: .22, metalness: .6, envMapIntensity: .12 }),
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

  /* One storey: glass curtain wall + mullions + lit interior (+ optional slab on top with its orange LED edge). */
  function storey({ w, d, r, h, top = true, people = 18, desks = 22, ring = false, ox = 0, oz = 0 }) {
    const g = new THREE.Group();
    const inset = .35, glassShape = plan(w - inset * 2, d - inset * 2, r.map(v => Math.max(.2, v - inset)), ox, oz), inner = plan(w - 2.2, d - 2.2, r.map(v => Math.max(.2, v - 1.1)), ox, oz);
    const gh = top ? h - .45 : h;
    const glass = new THREE.Mesh(new THREE.ExtrudeGeometry(glassShape, { depth: gh, bevelEnabled: false, curveSegments: 18 }).rotateX(-Math.PI / 2), [M.hidden, M.glass]);
    glass.renderOrder = 2; g.add(glass);
    // mullions every ~1.7 m along the curtain wall
    const perim = glassShape.getLength(), n = Math.floor(perim / 1.7), ms = [];
    for (let i = 0; i < n; i++) {
      const p = glassShape.getPointAt(i / n), q = glassShape.getPointAt(((i + .01) / n) % 1);
      ms.push(mtx(p.x, gh / 2, -p.y, -Math.atan2(-(q.y - p.y), q.x - p.x)));
    }
    g.add(instanced(new THREE.BoxGeometry(.1, gh, .22), M.mullion, ms));
    // interior: floor finish, glowing ceiling, warm core, desks, chairs and people silhouettes
    const fl = new THREE.Mesh(new THREE.ShapeGeometry(inner).rotateX(-Math.PI / 2), M.floor); fl.position.y = .03; fl.receiveShadow = true; g.add(fl);
    const ce = new THREE.Mesh(new THREE.ShapeGeometry(inner).rotateX(Math.PI / 2), M.ceiling); ce.position.y = gh - .06; g.add(ce);
    const core = new THREE.Mesh(new THREE.BoxGeometry(w * .12, gh - .1, d * .22), M.core); core.position.set(ox - w * .12, (gh - .1) / 2, oz - d * .2); g.add(core);
    const dm = [], pm = [];
    for (let i = 0; i < desks; i++) {
      const x = ox + (rnd() - .5) * (w - 6), z = oz + (rnd() - .5) * (d - 6);
      if (Math.abs(x - (ox - w * .12)) < w * .14 && Math.abs(z - (oz - d * .12)) < d * .18) continue;
      dm.push(mtx(x, .38, z, rnd() > .5 ? 0 : Math.PI / 2, 1.8, .75, .9));
    }
    for (let i = 0; i < people; i++) pm.push(mtx(ox + (rnd() - .5) * (w - 4), .85, oz + (rnd() - .5) * (d - 4) + d * .18, 0, 1, .9 + rnd() * .25, 1));
    if (dm.length) g.add(instanced(new THREE.BoxGeometry(1, 1, 1), M.desk, dm));
    if (pm.length) g.add(instanced(new THREE.CapsuleGeometry(.24, 1.05, 3, 8), M.person, pm));
    if (ring) { const t = new THREE.Mesh(new THREE.TorusGeometry(4.2, .07, 6, 64), led(.8)); t.rotation.x = Math.PI / 2; t.position.set(ox + 3, gh - .25, oz + 2); g.add(t); }
    if (top) {
      const slabShape = plan(w, d, r, ox, oz);
      const slab = prism(slabShape, .45, M.slab); slab.position.y = gh; slab.castShadow = true; slab.receiveShadow = true; g.add(slab);
      g.add(ledLoop(plan(w + .04, d + .04, r.map(v => v + .02), ox, oz), gh + .02));
    }
    return g;
  }

  /* ---------------- the building ---------------- */
  const TW = 34, TD = 24, TR = [1.2, 7, 1.2, 1.2];          // tower footprint (rounded front-right corner)
  const POD_H = 11, FH = 4.2, TOP_H = 5;
  const building = new THREE.Group(); scene.add(building);

  // Podium (static foundation): two storeys, wide base with left and right wings.
  const podium = new THREE.Group(); building.add(podium);
  const PW = 66, PD = 32, POX = -3;
  const pod1 = storey({ w: PW, d: PD, r: [2, 5, 2, 2], h: POD_H / 2, people: 46, desks: 30, ox: POX }); podium.add(pod1);
  const pod2 = storey({ w: PW, d: PD, r: [2, 5, 2, 2], h: POD_H / 2, people: 34, desks: 34, ox: POX }); pod2.position.y = POD_H / 2; podium.add(pod2);
  const plinth = prism(plan(PW + 1, PD + 1, [2.5, 5.5, 2.5, 2.5], POX), .3, M.slabUnder); podium.add(plinth);
  // right wing (lower block) and its terrace
  const rwing = storey({ w: 12, d: 26, r: [.6, 3, .6, .6], h: 8.5, people: 6, desks: 8, ox: 36, oz: -2 }); podium.add(rwing);
  // curved white sweep on the left wing (the reference's sculptural ribbon)
  const sweep = new THREE.CatmullRomCurve3([new THREE.Vector3(-41, .4, 13.5), new THREE.Vector3(-36, 2.6, 16.2), new THREE.Vector3(-29, 6.4, 16.6), new THREE.Vector3(-22, 9.8, 16.4), new THREE.Vector3(-17, 11.1, 16.2)]);
  const band = new THREE.Shape(); band.moveTo(-.18, -.9); band.lineTo(.18, -.9); band.lineTo(.18, .9); band.lineTo(-.18, .9); band.lineTo(-.18, -.9);
  const ribbon = new THREE.Mesh(new THREE.ExtrudeGeometry(band, { steps: 140, bevelEnabled: false, extrudePath: sweep }), M.slab); ribbon.castShadow = true; podium.add(ribbon);
  podium.add(new THREE.Mesh(new THREE.TubeGeometry(sweep, 120, .09, 6, false), led(.9)));
  // podium sign, entrance doors and canopy
  const psign = new THREE.Mesh(new THREE.PlaneGeometry(5.8, 5.8), M.sign); psign.position.set(6.2, 7.3, PD / 2 + .08); podium.add(psign);
  const doors = new THREE.Mesh(new THREE.PlaneGeometry(9, 3.6), M.door); doors.position.set(-3.5, 1.9, PD / 2 - .6); podium.add(doors);
  // podium roof terraces (planters and shrubs along the edges)
  shrubs(podium, new THREE.Vector3(-34, 0, 14.6), new THREE.Vector3(-18, 0, 14.6), POD_H, 16);
  shrubs(podium, new THREE.Vector3(18, 0, 13), new THREE.Vector3(27, 0, 10), POD_H, 10);
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
  floorDefs.forEach((f, i) => {
    const s = storey({ w: TW, d: TD, r: TR, h: FH, people: f.people, desks: f.desks, ring: f.ring });
    if (i === 0) { const rail = new THREE.Mesh(new THREE.ExtrudeGeometry(plan(TW + .3, TD + .3, TR.map(v => v + .15)), { depth: 1.1, bevelEnabled: false, curveSegments: 18 }).rotateX(-Math.PI / 2), [M.hidden, M.rail]); rail.position.y = FH; s.add(rail); shrubs(s, new THREE.Vector3(9, 0, 11.7), new THREE.Vector3(17, 0, 3), FH, 14, .45); }
    part(f.name, s, POD_H + i * FH, i, [[.06, -1.6, 1.2], [-.08, 2.2, -1], [.1, -2.4, .6], [-.07, 1.8, -1.4]][i]);
  });
  // top floor: taller, garden terrace with a tree, upper orange sign
  const tf = storey({ w: TW, d: TD, r: TR, h: TOP_H, top: false, people: 14, desks: 16 });
  const usign = new THREE.Mesh(new THREE.PlaneGeometry(4.6, 4.6), M.sign); usign.position.set(1.8, 2.6, TD / 2 - .2); tf.add(usign);
  tree(tf, -6, 0, 6, .75); shrubs(tf, new THREE.Vector3(-14, 0, 9), new THREE.Vector3(-2, 0, 9), 0, 10, .45);
  part("tf", tf, POD_H + 4 * FH, 4, [.09, -2, 1.6]);
  // roof: thick slab with the cantilevered left overhang, rounded front-right corner and orange edge light
  const roof = new THREE.Group();
  const roofShape = plan(TW + 9, TD + 3, [3, 8.5, 2, 2], -3.6, .6);
  const rslab = prism(roofShape, .95, M.slab); rslab.castShadow = true; roof.add(rslab);
  roof.add(ledLoop(plan(TW + 9.04, TD + 3.04, [3, 8.5, 2, 2], -3.6, .6), .02, 1.2, .09));
  const parapet = prism(plan(TW - 2, TD - 2, [1, 6, 1, 1]), .5, M.slab); parapet.position.y = .95; roof.add(parapet);
  part("roof", roof, POD_H + 4 * FH + TOP_H, 5, [-.12, 3.2, -2.2]);
  // entrance canopy (structural component, lands with the first floor)
  const canopy = new THREE.Group();
  const cslab = prism(plan(13, 4.5, [.3, .3, .3, .3], -3.5, 18.2), .35, M.slab); canopy.add(cslab);
  canopy.add(ledLoop(plan(13.04, 4.54, [.3, .3, .3, .3], -3.5, 18.2), -.02, .9, .06));
  part("canopy", canopy, 4.2, 0, [0, 0, 6]);

  // per-floor attach points for the live lines (left facade corner, right facade)
  const ATTACH = { f1: [[-17.2, 13, 10], [17.2, 13, -3]], f2: [[-17.2, 17.2, 10], [17.2, 17.2, -3]], f3: [[-17.2, 21.4, 10], [17.2, 21.4, -3]], f4: [[-17.2, 25.6, 10], [17.2, 25.6, -3]], roof: [[-17.2, 31, 10], [17.2, 31, -3]] };

  /* ---------------- environment: plaza, trees, people, skyline ---------------- */
  const ground = new THREE.Mesh(new THREE.PlaneGeometry(800, 800), M.ground); ground.rotation.x = -Math.PI / 2; ground.receiveShadow = true; scene.add(ground);
  [[-3, .02, 26, 34, 16, .32], [20, .02, 24, 24, 12, .18], [-30, .02, 21, 22, 10, .16]].forEach(([x, y, z, w, d, o]) => {
    const m = new THREE.Mesh(new THREE.PlaneGeometry(w, d), new THREE.MeshBasicMaterial({ map: glowTex, transparent: true, opacity: o, blending: THREE.AdditiveBlending, depthWrite: false }));
    m.rotation.x = -Math.PI / 2; m.position.set(x, y, z); scene.add(m);
  });
  [[-36, 21], [-24, 23], [-12, 24], [12, 23], [26, 22], [40, 18], [-46, 14], [50, 10], [32, 26], [-30, 28]].forEach(([x, z]) => tree(scene, x, 0, z, .9 + rnd() * .35));
  { const pm = []; for (let i = 0; i < 26; i++) pm.push(mtx(-14 + rnd() * 26, .9, 19 + rnd() * 6, 0, 1, .95 + rnd() * .2, 1)); scene.add(instanced(new THREE.CapsuleGeometry(.26, 1.15, 3, 8), M.person, pm)); }
  { const cm = new THREE.MeshStandardMaterial({ color: 0x0b0c0e, roughness: .8, emissive: 0xffffff, emissiveMap: cityTex, emissiveIntensity: .5 }), ms = [];
    for (let i = 0; i < (lowPower ? 50 : 110); i++) { const a = -1.3 + rnd() * 2.6, rad = 170 + rnd() * 160, h = 15 + rnd() * rnd() * 90; ms.push(new THREE.Matrix4().compose(new THREE.Vector3(Math.sin(a) * rad, h / 2, -Math.cos(a) * rad * .8 - 40), new THREE.Quaternion(), new THREE.Vector3(10 + rnd() * 18, h, 10 + rnd() * 18))); }
    scene.add(instanced(new THREE.BoxGeometry(1, 1, 1), cm, ms)); }

  /* ---------------- post-processing ---------------- */
  let composer = null, bloom = null;
  if (!lowPower) {
    composer = new EffectComposer(renderer);
    composer.addPass(new RenderPass(scene, camera));
    bloom = new UnrealBloomPass(new THREE.Vector2(512, 512), .55, .4, .93); composer.addPass(bloom);
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
  const CAM = new THREE.CatmullRomCurve3([V(40, 15, 104), V(52, 20, 84), V(62, 34, 54), V(38, 26, 40), V(34, 10, 84), V(-44, 16, 70), V(-3, 3.2, 30)], false, "centripetal");
  const TGT = new THREE.CatmullRomCurve3([V(0, 26, 0), V(1, 24, 0), V(2, 25, 0), V(-2, 22, 4), V(2, 17, 0), V(-2, 16, 0), V(-3, 2.6, 16)], false, "centripetal");
  const camState = { t: 0 }; let override = null;
  const VIEWS = { reference: [V(34, 10, 84), V(2, 17, 0)], left: [V(-56, 18, 62), V(-2, 16, 0)], aerial: [V(48, 74, 70), V(0, 14, 0)], rear: [V(-30, 22, -84), V(0, 18, 0)], close: [V(26, 22, 34), V(-2, 21, 4)] };
  function applyCamera() {
    const W = stage.clientWidth, H = stage.clientHeight; camera.aspect = W / H;
    camera.fov = W / H < 1 ? 52 : W / H < 1.4 ? 42 : 34;
    let p, q;
    if (override) { [p, q] = VIEWS[override]; }
    else { p = CAM.getPoint(camState.t); q = TGT.getPoint(camState.t); }
    if (W / H < 1) { p = p.clone().sub(q).multiplyScalar(1.15).add(q); }
    camera.position.copy(p); camera.lookAt(q); camera.updateProjectionMatrix();
  }

  /* ---------------- interactive procurement panels + live lines (HTML/SVG, anchored in 3D) ---------------- */
  const panelsEl = document.getElementById("hq3-panels"), svg = document.getElementById("hq3-lines"), NS = "http://www.w3.org/2000/svg";
  const panels = Array.prototype.map.call(panelsEl.querySelectorAll(".hq3-panel"), el => {
    const floor = el.dataset.floor, side = el.dataset.side === "left" ? 0 : 1, stageIdx = +el.dataset.stage;
    const anchor = V(...(side === 0 ? [-31, 0, 13] : [31, 0, 2])); anchor.y = ATTACH[floor][side][1] + (el.dataset.dy ? +el.dataset.dy : 0);
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
    svg.setAttribute("viewBox", `0 0 ${W} ${H}`);
    for (const pn of panels) {
      const k = STAGE_OF_FLOOR[pn.floor], grow = S.g[k], show = clamp((S.g[k] + (k > 0 ? S.g[k - 1] : 1) * .25 - .05) * 2.2, 0, 1) * panelFade;
      const fp = parts.find(p => p.name === pn.floor);
      const a = ATTACH[pn.floor][pn.side];
      w2.set(a[0], a[1], a[2]);
      if (fp) { w2.y += fp.pivot.position.y - fp.finalY; w2.x += fp.pivot.position.x; w2.z += fp.pivot.position.z; }
      const A = project(pn.anchor), B = project(w2);
      const r = pn.el.getBoundingClientRect(), sr = stage.getBoundingClientRect();
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

  /* ---------------- render loop ---------------- */
  function resize() {
    const W = stage.clientWidth, H = stage.clientHeight;
    renderer.setSize(W, H, false);
    if (composer) { composer.setSize(W, H); bloom.resolution.set(W / 2, H / 2); }
    stage.classList.toggle("hq-wide", W >= 1100 && W / H >= 1.2); stage.classList.toggle("hq-narrow", !(W >= 1100 && W / H >= 1.2));
  }
  const clock = new THREE.Clock();
  let visible = true, raf = 0;
  function frame() {
    const t = clock.getElapsedTime();
    applyParts(t);
    ledMats.forEach(m => m.color.copy(ORANGE).multiplyScalar(2.2 * m.userData.k * (1 + S.glow * .6 * (.6 + .4 * Math.sin(t * 2.4)))));
    applyCamera();
    if (composer) composer.render(); else renderer.render(scene, camera);
    applyPanels(t);
  }
  function loop() { raf = 0; if (!visible) return; frame(); raf = requestAnimationFrame(loop); }
  new IntersectionObserver(es => { visible = es[0].isIntersecting; if (visible && !raf) loop(); }).observe(stage);
  addEventListener("resize", () => { resize(); frame(); });
  resize(); loop();
  root.classList.add("hq3-live");

  /* ---------------- verification hooks (read-only, used by the Playwright QA) ---------------- */
  const order = ["f1", "f2", "f3", "f4", "tf", "roof"];
  window.__hq3 = {
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
    // completes the scroll scrub immediately (software-rendered QA browsers draw only a few frames per second)
    sync() { if (st) { ScrollTrigger.update(); const tw = st.getTween && st.getTween(); if (tw) tw.progress(1); st.animation.progress(st.progress); } frame(); },
    frame
  };
  let st = null;
  if (!gsap || !ScrollTrigger || reduce) { camState.t = 4 / 6; S.glow = .3; frame(); return; } // reduced motion: completed building, hero view

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
    tl.to(fade, { opacity: 1, duration: 2.5 }, 97.5);
    tl.to({}, { duration: .01 }, 100);
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
    const c = ctx.conditions, tl = build();
    st = ScrollTrigger.create({ trigger: section, start: "top top", end: () => "+=" + Math.round(innerHeight * (c.wide ? 7 : c.mid ? 4.2 : 3.2)), pin: true, scrub: c.wide ? 1 : .7, anticipatePin: 1, animation: tl, invalidateOnRefresh: true });
    const see = [...document.querySelectorAll("[data-see-action]")];
    function onSee(e) {
      e.preventDefault();
      const y0 = scrollY, target = st.end + 2, dur = clamp((target - y0) * 1.2, 5000, 14000), start = performance.now(); let stop = false;
      const cancel = () => { stop = true; removeEventListener("wheel", cancel); removeEventListener("touchstart", cancel); removeEventListener("keydown", cancel); };
      addEventListener("wheel", cancel, { passive: true }); addEventListener("touchstart", cancel, { passive: true }); addEventListener("keydown", cancel);
      (function step(now) { const k = Math.min(1, (now - start) / dur), e2 = k < .5 ? 2 * k * k : 1 - Math.pow(-2 * k + 2, 2) / 2; scrollTo(0, y0 + (target - y0) * e2); if (k < 1 && !stop) requestAnimationFrame(step); else cancel(); })(start);
    }
    see.forEach(a => a.addEventListener("click", onSee));
    return () => { see.forEach(a => a.removeEventListener("click", onSee)); st && st.kill(); st = null; tl.kill(); };
  });
  addEventListener("load", () => ScrollTrigger.refresh());
}

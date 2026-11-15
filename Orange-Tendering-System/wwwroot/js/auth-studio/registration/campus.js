import * as THREE from 'three';
import gsap from 'gsap';
import { Stroke } from '../core/stroke.js';
import { sheetMaterial, sheetGeometry, hingedSheet, attach, develop } from '../core/paper.js';
import { Path, roundedRect } from '../core/paths.js';
import { metalMaterial, studioEnvironment } from '../core/metal.js';
import { Label, drawLine } from '../core/text.js';
import { COLOR, LINE } from '../core/tokens.js';
import * as L from './layout.js';
import { buildCategory } from './categories.js';

// SUPPLIER GENESIS, R03–R08: the enterprise that grows out of the sealed identity package.
//
//   platform   Board leaves stand up around the package like the walls of a larger carton and fold
//              outward on their creases (edges first, then corners riding on them): the package
//              literally unfolds into the ground the company is built on. The package itself shrinks
//              to a parcel and keeps its identity: it is the parcel that later rides to the
//              Identity Station.
//   HQ         Four facades hinged on their footings rise and close like carton walls, the roof
//              settles, the sign carries the company name typed in the form (locally only).
//   category   One module tile; the eleven categories are modular assets (./categories.js) that
//              retract into the tile and rise out of it when the category changes.
//   conveyor   The Orange line, now a belt: packing station → package → Identity Station → dispatch.
//   documents  A paper per accepted file travels from HQ into the open application box; a file the
//              form refuses stops short in amber. Accepted means "passed the local checks" only.
//   submit     The application box closes and rides to dispatch while the server works; only the
//              server's redirect earns the orange label. A failure brings it back, lid ajar.
//
// The form owns every fact. Rig values are written by the director; this file turns them into a pose.

const clamp01 = v => Math.max(0, Math.min(1, v));
const smooth = v => {
  const t = clamp01(v);
  return t * t * (3 - 2 * t);
};
const lerp = (a, b, t) => a + (b - a) * t;
const span = (v, a, b) => clamp01((v - a) / (b - a));

// --- Layout (same plane and units as R00–R02) ------------------------------------------------------
const B = L.BOX;
const S = 11; // tile size
const C0 = { x: B.cx - S / 2, z: B.cz - S / 2 }; // centre tile corner
const T = L.T_BOARD;
const TOP = T; // platform surface height
export const SPOT = {
  centre: { x: B.cx, z: B.cz },
  hq: { x: B.cx - S, z: B.cz - S },
  category: { x: B.cx, z: B.cz - S },
  packing: { x: B.cx - S, z: B.cz + 0.6 },
  identity: { x: B.cx + S, z: B.cz - 1.4 },
  dispatch: { x: B.cx + 1.2, z: B.cz + S }
};
const BELT_Z = B.cz + 4.9;
const PARCEL = { x: B.cx + 1.4, z: BELT_Z, scale: 0.36 };

export const CAMPUS_RIG = {
  platform: 0, unfoldEdges: 0, unfoldCorners: 0, blueprint: 0, shrink: 0, letterAway: 0,
  hq: 0, roof: 0, sign: 0, conveyor: 0, stations: 0,
  ride: 0, irisReady: 0, fp: 0,
  appBox: 0, appLid: 0, appRide: 0, appWarn: 0, appLabel: 0
};

export function buildCampus(stage, scene) {
  const env = studioEnvironment(stage.renderer);
  const ground = new THREE.Color(COLOR.ground);
  const root = new THREE.Group();
  root.name = 'campus';
  scene.root.add(root);

  // ---------------------------------------------------------------------------------------------
  // Materials (one family for the whole enterprise)
  // ---------------------------------------------------------------------------------------------
  const kraft = sheetMaterial(COLOR.kraftOuter, { roughness: 0.92, bump: 0.35 });
  const kraftIn = sheetMaterial(COLOR.kraftInner, { roughness: 0.92, bump: 0.35 });
  const kraftEdge = sheetMaterial(COLOR.kraftEdge, { roughness: 0.95, bump: 0.2 });
  const boardMats = [kraftIn, kraft, kraftEdge];
  const ceramic = sheetMaterial(COLOR.ceramic, { roughness: 0.6, bump: 0.05, grain: false });
  const ceramicEdge = sheetMaterial(COLOR.ceramicEdge, { roughness: 0.65, bump: 0.05, grain: false });
  const paper = sheetMaterial(COLOR.paper, { roughness: 0.9, bump: 0.3 });
  const graphite = new THREE.MeshStandardMaterial({ color: '#3a332d', roughness: 0.55, metalness: 0.15, envMap: env, envMapIntensity: 0.4 });
  const glass = new THREE.MeshStandardMaterial({ color: '#4c4740', roughness: 0.12, metalness: 0.2, envMap: env, envMapIntensity: 0.9 });
  const champagne = metalMaterial(stage.renderer, COLOR.champagne, { roughness: 0.32 });
  const orange = new THREE.MeshStandardMaterial({ color: COLOR.orange, roughness: 0.55, metalness: 0.05 });
  const mats = { kraft, kraftIn, kraftEdge, ceramic, ceramicEdge, paper, graphite, glass, champagne, orange, env, renderer: stage.renderer };

  const box = (w, h, d, material, x = 0, y = 0, z = 0) => {
    const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), material);
    m.position.set(x, y + h / 2, z);
    m.castShadow = true;
    m.receiveShadow = true;
    return m;
  };

  // ---------------------------------------------------------------------------------------------
  // Platform: centre tile under the package, eight leaves unfolding from it
  // ---------------------------------------------------------------------------------------------
  const tile = (x0, z0) => roundedRect(x0 + 0.04, z0 + 0.04, x0 + S - 0.04, z0 + S - 0.04, 0.16);
  const platform = new THREE.Group();
  platform.name = 'campus.platform';
  root.add(platform);
  const centre = new THREE.Mesh(sheetGeometry(tile(C0.x, C0.z), T), boardMats);
  centre.receiveShadow = true;
  platform.add(centre);
  const x0 = C0.x;
  const x1 = C0.x + S;
  const z0 = C0.z;
  const z1 = C0.z + S;
  // Edge leaves hinge on the centre tile; folded they stand up like carton walls.
  const edge = (name, x, z, pivot, axis, folded) => {
    const g = hingedSheet(name, tile(x, z), T, boardMats, { pivot });
    g.userData.axis = axis;
    g.userData.folded = folded;
    platform.add(g);
    return g;
  };
  const north = edge('leaf.n', x0, z0 - S, [B.cx, 0, z0], 'x', Math.PI / 2);
  const south = edge('leaf.s', x0, z1, [B.cx, 0, z1], 'x', -Math.PI / 2);
  const west = edge('leaf.w', x0 - S, z0, [x0, 0, B.cz], 'z', -Math.PI / 2);
  const east = edge('leaf.e', x1, z0, [x1, 0, B.cz], 'z', Math.PI / 2);
  // Corner leaves ride on the north and south leaves and open sideways after them.
  const corner = (name, parent, x, z, pivot, folded) => {
    const g = hingedSheet(name, tile(x, z), T, boardMats, { pivot });
    g.userData.axis = 'z';
    g.userData.folded = folded;
    attach(parent, g);
    return g;
  };
  const nw = corner('leaf.nw', north, x0 - S, z0 - S, [x0, 0, z0 - S / 2], -Math.PI);
  const ne = corner('leaf.ne', north, x1, z0 - S, [x1, 0, z0 - S / 2], Math.PI);
  const sw = corner('leaf.sw', south, x0 - S, z1, [x0, 0, z1 + S / 2], -Math.PI);
  const se = corner('leaf.se', south, x1, z1, [x1, 0, z1 + S / 2], Math.PI);
  const edges = [north, east, south, west];
  const corners = [nw, ne, se, sw];

  // Crease scores along every fold, so the unfolded ground still reads as one die-cut sheet.
  const creases = [];
  const crease = (ax, az, bx, bz) => {
    const s = new Stroke(new Path().M(ax, az).L(bx, bz).build({ step: 0.1 }), { color: COLOR.score, width: LINE.hair, y: TOP + 0.004, dash: 0.3, gap: 0.2 });
    platform.add(s);
    creases.push(s);
  };
  crease(x0, z0 - S, x0, z1 + S);
  crease(x1, z0 - S, x1, z1 + S);
  crease(x0 - S, z0, x1 + S, z0);
  crease(x0 - S, z1, x1 + S, z1);

  // Blueprint: the plan of the enterprise drawn across the unfolded ground before anything rises.
  const blueprint = [];
  const plan = (pts, close = true) => {
    const p = new Path().M(pts[0][0], pts[0][1]);
    for (let i = 1; i < pts.length; i++) p.L(pts[i][0], pts[i][1], 0.25);
    if (close) p.L(pts[0][0], pts[0][1]);
    const s = new Stroke(p.build({ step: 0.08 }), { color: COLOR.ink, width: LINE.hair, y: TOP + 0.006 });
    root.add(s);
    blueprint.push(s);
  };
  const rect = (c, w, d) => [[c.x - w / 2, c.z - d / 2], [c.x + w / 2, c.z - d / 2], [c.x + w / 2, c.z + d / 2], [c.x - w / 2, c.z + d / 2]];
  plan(rect(SPOT.hq, 7.2, 6.0));
  plan(rect(SPOT.category, 9.2, 8.2));
  plan(rect({ x: SPOT.packing.x, z: SPOT.packing.z - 0.6 }, 6.2, 3.6));
  plan(rect(SPOT.identity, 6.4, 6.4));
  plan(rect(SPOT.dispatch, 6.0, 4.4));

  // ---------------------------------------------------------------------------------------------
  // HQ: hinged facades, roof, entrance canopy and the company sign
  // ---------------------------------------------------------------------------------------------
  const HQ = { w: 6.4, d: 5.0, h: 4.4 };
  const hq = new THREE.Group();
  hq.name = 'campus.hq';
  hq.position.set(SPOT.hq.x, TOP, SPOT.hq.z);
  root.add(hq);
  const plinth = box(HQ.w + 0.8, 0.22, HQ.d + 0.8, ceramic);
  hq.add(plinth);
  const facadeMat = new THREE.MeshStandardMaterial({ map: facadeTexture(), roughness: 0.75 });
  const facades = [];
  const facade = (w, axis, pos, folded, rotY) => {
    const pivot = new THREE.Group();
    pivot.position.set(pos[0], 0.22, pos[1]);
    pivot.rotation.y = rotY;
    const panel = new THREE.Mesh(new THREE.BoxGeometry(w, HQ.h, 0.14), [ceramicEdge, ceramicEdge, ceramic, ceramic, facadeMat, ceramic]);
    panel.position.set(0, HQ.h / 2, 0.07);
    panel.castShadow = true;
    panel.receiveShadow = true;
    const hinge = new THREE.Group();
    hinge.add(panel);
    pivot.add(hinge);
    hq.add(pivot);
    facades.push({ hinge, folded });
  };
  // Each facade lies flat, outward, on its footing and rises about it (rotation about local x).
  facade(HQ.w, 'x', [0, HQ.d / 2 - 0.14], Math.PI / 2, 0);
  facade(HQ.d, 'x', [HQ.w / 2 - 0.14, 0], Math.PI / 2, Math.PI / 2);
  facade(HQ.w, 'x', [0, -HQ.d / 2 + 0.14], Math.PI / 2, Math.PI);
  facade(HQ.d, 'x', [-HQ.w / 2 + 0.14, 0], Math.PI / 2, -Math.PI / 2);
  const roof = new THREE.Group();
  roof.position.y = 0.22 + HQ.h;
  const roofSlab = box(HQ.w + 0.5, 0.22, HQ.d + 0.5, ceramic, 0, 0, 0);
  const roofTrim = box(HQ.w + 0.56, 0.07, HQ.d + 0.56, orange, 0, 0.09, 0); // a band under the slab edge
  const plant = box(1.8, 0.5, 1.2, graphite, -1.3, 0.28, -0.6);
  roof.add(roofSlab, roofTrim, plant);
  hq.add(roof);
  const canopy = box(2.6, 0.12, 1.1, orange, 0, 1.55, HQ.d / 2 + 0.5);
  hq.add(canopy);
  const door = box(1.3, 1.5, 0.06, glass, 0, 0.22, HQ.d / 2 + 0.02);
  hq.add(door);
  // The sign: a ceramic plate on the facade, name written from the form's company field.
  const signPlate = box(4.6, 0.62, 0.1, ceramic, 0, 2.05, HQ.d / 2 + 0.06);
  hq.add(signPlate);
  const signLabel = new Label(4.3, 0.5, { ppu: 120, align: 'center' });
  signLabel.rotation.x = Math.PI / 2;
  signLabel.position.set(0, 2.36, HQ.d / 2 + 0.12);
  signLabel.material.depthWrite = false;
  hq.add(signLabel);
  let signText = '';
  const hqMaterials = [ceramic, ceramicEdge];

  // ---------------------------------------------------------------------------------------------
  // Conveyor: the Orange line as a belt on a low ceramic rail
  // ---------------------------------------------------------------------------------------------
  const beltPath = new Path()
    .M(SPOT.packing.x + 3.4, BELT_Z)
    .L(SPOT.identity.x, BELT_Z, 1.6)
    .L(SPOT.identity.x, SPOT.dispatch.z, 1.6)
    .L(SPOT.dispatch.x + 3.2, SPOT.dispatch.z)
    .build({ step: 0.06 });
  const rail = [];
  for (let s = 0; s < beltPath.total; s += 0.5) {
    const p = beltPath.pointAt(s);
    const t = beltPath.tangentAt(s);
    const m = new THREE.Mesh(new THREE.BoxGeometry(0.52, 0.22, 1.5), ceramic);
    m.position.set(p[0], TOP + 0.11, p[1]);
    m.rotation.y = Math.atan2(t[0], t[1]);
    m.castShadow = true;
    m.receiveShadow = true;
    m.userData.s = s;
    root.add(m);
    rail.push(m);
  }
  const belt = new Stroke(beltPath, { color: COLOR.orangeInk, width: 1.1, y: TOP + 0.23, maxWidth: 1.2 });
  root.add(belt);
  const beltLines = new Stroke(beltPath, { color: '#D45E00', width: 1.1, y: TOP + 0.235, dash: 0.08, gap: 0.42, maxWidth: 1.2 });
  beltLines.opacity = 0.6;
  root.add(beltLines);

  // ---------------------------------------------------------------------------------------------
  // Packing station and the application box
  // ---------------------------------------------------------------------------------------------
  const packing = new THREE.Group();
  packing.position.set(SPOT.packing.x, TOP, SPOT.packing.z);
  root.add(packing);
  packing.add(box(5.6, 0.9, 2.4, ceramic, 0, 0, -0.6));
  packing.add(box(5.6, 0.06, 2.4, champagne, 0, 0.9, -0.6));
  // A small articulated arm that lowers each paper into the box.
  const arm = new THREE.Group();
  arm.position.set(-2.2, 0.96, -1.4);
  const armBase = new THREE.Mesh(new THREE.CylinderGeometry(0.32, 0.4, 0.3, 24), graphite);
  armBase.position.y = 0.15;
  const armA = new THREE.Group();
  armA.position.y = 0.3;
  const armALink = box(0.18, 1.6, 0.18, champagne);
  armA.add(armALink);
  const armB = new THREE.Group();
  armB.position.y = 1.6;
  const armBLink = box(0.16, 1.3, 0.16, champagne);
  armB.add(armBLink);
  const armTip = box(0.36, 0.1, 0.36, orange, 0, 1.3, 0);
  armB.add(armTip);
  armA.add(armB);
  arm.add(armBase, armA);
  packing.add(arm);

  const app = new THREE.Group();
  app.name = 'application-box';
  root.add(app);
  const AB = { w: 2.6, d: 1.9, h: 0.75 };
  const appBody = new THREE.Group();
  app.add(appBody);
  appBody.add(box(AB.w, 0.06, AB.d, kraftIn));
  for (const [w, x, z, r] of [[AB.w, 0, AB.d / 2, 0], [AB.w, 0, -AB.d / 2, 0], [AB.d, AB.w / 2, 0, Math.PI / 2], [AB.d, -AB.w / 2, 0, Math.PI / 2]]) {
    const wall = box(w, AB.h, 0.06, kraft, x, 0, z);
    wall.rotation.y = r;
    appBody.add(wall);
  }
  const lidHinge = new THREE.Group();
  lidHinge.position.set(0, AB.h, -AB.d / 2);
  const lid = box(AB.w + 0.06, 0.06, AB.d + 0.04, kraft, 0, -0.03, AB.d / 2);
  lidHinge.add(lid);
  appBody.add(lidHinge);
  const appLabel = box(1.6, 0.012, 0.5, orange, 0, 0.06, AB.d / 2);
  lid.add(appLabel);
  appLabel.position.set(0, 0.03, 0);
  const appWarnRing = new Stroke(roundRectPath(-AB.w / 2 - 0.3, -AB.d / 2 - 0.3, AB.w / 2 + 0.3, AB.d / 2 + 0.3, 0.3), { color: COLOR.warning, width: 0.07, y: 0.02 });
  app.add(appWarnRing);
  const appHome = new THREE.Vector3(SPOT.packing.x + 0.6, TOP + 0.96, SPOT.packing.z - 0.6);
  const appMaterials = [kraft, kraftIn];

  // Documents: one paper per file field, with a coloured tab for its kind.
  const DOCS = [
    { key: 'registration', tab: COLOR.orange },
    { key: 'tax', tab: COLOR.champagne },
    { key: 'legal', tab: '#5E5A55' }
  ];
  const docStart = new THREE.Vector3(SPOT.hq.x + 0.6, TOP + 0.3, SPOT.hq.z + 3.4);
  const docPath = new THREE.CatmullRomCurve3([
    docStart,
    new THREE.Vector3(SPOT.hq.x + 0.8, TOP + 0.5, SPOT.hq.z + 6.5),
    new THREE.Vector3(appHome.x - 1.2, TOP + 1.6, appHome.z - 2.2),
    new THREE.Vector3(appHome.x, appHome.y + 1.7, appHome.z),
    new THREE.Vector3(appHome.x, appHome.y + 0.25, appHome.z)
  ]);
  const docs = DOCS.map((d, i) => {
    const g = new THREE.Group();
    const sheet = new THREE.Mesh(sheetGeometry(roundedRect(-0.62, -0.85, 0.62, 0.85, 0.04), 0.02), [paper, paper, ceramicEdge]);
    sheet.castShadow = true;
    const tabMat = new THREE.MeshStandardMaterial({ color: d.tab, roughness: 0.6 });
    const tab = new THREE.Mesh(sheetGeometry(roundedRect(0.18, -1.02, 0.58, -0.8, 0.05), 0.022), tabMat);
    g.add(sheet, tab);
    for (const [z, w] of [[-0.45, 0.8], [-0.15, 0.8], [0.15, 0.6], [0.45, 0.7]]) {
      const line = new Stroke(new Path().M(-0.4, z).L(-0.4 + w, z).build({ step: 0.05 }), { color: COLOR.ink, width: 0.05, y: 0.024 });
      line.drawn = 1;
      g.add(line);
    }
    g.visible = false;
    root.add(g);
    return { ...d, g, sheet, tabMat, t: 0, warn: 0, state: 'none', offset: i * 0.12 };
  });
  const _q = new THREE.Vector3();

  // ---------------------------------------------------------------------------------------------
  // Identity Station: the Paper Iris on a pedestal, an identity tray on the belt, a fingerprint seal
  // ---------------------------------------------------------------------------------------------
  const identity = new THREE.Group();
  identity.position.set(SPOT.identity.x, TOP, SPOT.identity.z);
  root.add(identity);
  const pedestal = new THREE.Mesh(new THREE.CylinderGeometry(2.3, 2.5, 0.9, 72), [ceramicEdge, ceramic, ceramic]);
  pedestal.position.y = 0.45;
  pedestal.castShadow = true;
  pedestal.receiveShadow = true;
  identity.add(pedestal);
  // Eight paper blades, each turned about its own pin (same construction as Login's optical iris).
  const irisBlades = new THREE.Group();
  irisBlades.position.y = 0.9;
  identity.add(irisBlades);
  const P = 1.5;
  const reach = P + 0.25;
  const outline = [[0, 0], [reach, 0]];
  for (let i = 0; i <= 12; i++) {
    const u = reach * (1 - i / 12);
    const rho = P - u;
    const cap = Math.abs(rho) < 1.75 ? Math.sqrt(1.75 * 1.75 - rho * rho) : 0.12;
    outline.push([u, Math.max(0.12, Math.min(rho * 1.06 + 0.2, cap))]);
  }
  const bladeGeo = sheetGeometry(outline, 0.03);
  const paperBlade = sheetMaterial('#FBF6EE', { roughness: 0.9, bump: 0.35 });
  const blades = [];
  for (let i = 0; i < 8; i++) {
    const phi = (i / 8) * Math.PI * 2;
    const pin = new THREE.Group();
    pin.position.set(Math.cos(phi) * P, 0.01 + i * 0.012, Math.sin(phi) * P);
    const mesh = new THREE.Mesh(bladeGeo, [paperBlade, paperBlade, ceramicEdge]);
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    pin.add(mesh);
    irisBlades.add(pin);
    blades.push({ pin, phi });
  }
  const irisRing = new THREE.Mesh(new THREE.TorusGeometry(1.95, 0.09, 12, 96), champagne);
  irisRing.rotation.x = Math.PI / 2;
  irisRing.position.y = 1.0;
  identity.add(irisRing);
  const readyRing = new Stroke(circlePath(0, 0, 2.25), { color: COLOR.orangeInk, width: 0.07, y: 0.93 });
  identity.add(readyRing);
  // Fingerprint seal (presentation only: fingerprint enrollment is not available yet).
  const seal = new THREE.Group();
  seal.position.set(-2.5, 0, 2.3);
  const sealDisc = new THREE.Mesh(new THREE.CylinderGeometry(0.85, 0.9, 0.3, 48), [ceramicEdge, ceramic, ceramic]);
  sealDisc.position.y = 0.15;
  sealDisc.castShadow = true;
  seal.add(sealDisc);
  const ridges = ridgePaths().map(p => {
    const s = new Stroke(p, { color: COLOR.warmGray, width: 0.028, y: 0.31 });
    seal.add(s);
    return s;
  });
  identity.add(seal);
  // Identity tray: where the parcel stops on the belt.
  const tray = box(2.0, 0.08, 1.6, champagne, 0, 0.24, BELT_Z - SPOT.identity.z);
  identity.add(tray);
  const identityMaterials = [ceramic, ceramicEdge, paperBlade];

  // ---------------------------------------------------------------------------------------------
  // Dispatch: a gateway frame and a waiting pad
  // ---------------------------------------------------------------------------------------------
  const dispatch = new THREE.Group();
  dispatch.position.set(SPOT.dispatch.x, TOP, SPOT.dispatch.z);
  root.add(dispatch);
  dispatch.add(box(4.6, 0.16, 3.2, ceramic, -0.6, 0, 0));
  dispatch.add(box(0.3, 3.0, 0.3, ceramic, -2.6, 0.16, -1.3), box(0.3, 3.0, 0.3, ceramic, -2.6, 0.16, 1.3), box(0.36, 0.3, 2.9, orange, -2.6, 3.16, 0));
  const dispatchPad = new Stroke(circlePath(-0.6, 0, 1.4), { color: COLOR.ink, width: LINE.detail, y: 0.18 });
  dispatch.add(dispatchPad);
  const stationsMaterials = [ceramic, ceramicEdge];

  // ---------------------------------------------------------------------------------------------
  // The parcel: the sealed identity package of R02, reparented so it can shrink and travel
  // ---------------------------------------------------------------------------------------------
  const parts = scene.parts;
  const parcel = new THREE.Group();
  parcel.name = 'identity-parcel';
  parcel.position.set(B.cx, 0, B.cz);
  const parcelInner = new THREE.Group();
  parcelInner.position.set(-B.cx, 0, -B.cz);
  parcel.add(parcelInner);
  scene.root.add(parcel);
  let adopted = false;
  function adoptParcel() {
    if (adopted) return;
    adopted = true;
    for (const o of [parts.carton, parts.card, parts.wafer, parts.stamp]) if (o) parcelInner.add(o);
  }

  // ---------------------------------------------------------------------------------------------
  // Category modules
  // ---------------------------------------------------------------------------------------------
  const modules = new Map();
  const moduleRoot = new THREE.Group();
  moduleRoot.position.set(SPOT.category.x, TOP, SPOT.category.z);
  moduleRoot.scale.setScalar(1.18);
  root.add(moduleRoot);
  const modulePad = box(9.0, 0.14, 8.0, ceramic);
  moduleRoot.add(modulePad);
  function moduleFor(name) {
    if (!modules.has(name)) {
      const m = buildCategory(name, mats);
      m.group.visible = false;
      moduleRoot.add(m.group);
      modules.set(name, { ...m, k: 0 });
    }
    return modules.get(name);
  }
  function applyModule(m) {
    m.group.visible = m.k > 0.002;
    // Parts rise from the pad in authored order with a short overlap; on the way out they sink in reverse.
    const n = m.parts.length;
    m.parts.forEach((p, i) => {
      const k = smooth(m.k * (1 + 0.5 * (n - 1) / n * 2) - (i / n) * 1.0);
      p.scale.set(p.userData.sx ?? 1, Math.max(0.001, k) * (p.userData.sy ?? 1), p.userData.sz ?? 1);
      p.visible = k > 0.002;
    });
  }

  // ---------------------------------------------------------------------------------------------
  // rig → pose
  // ---------------------------------------------------------------------------------------------
  const _c = new THREE.Color();
  const warnColor = new THREE.Color(COLOR.warning);
  const paperColor = new THREE.Color(COLOR.paper);
  const appPos = new THREE.Vector3();

  function apply(rig) {
    const active = rig.platform > 0.001;
    root.visible = active;
    parcel.visible = true;
    if (!active) return;
    adoptParcel();

    // The R00–R02 letter steps aside: it has delivered its card.
    const away = smooth(rig.letterAway);
    if (parts.letter) {
      parts.letter.scale.setScalar(Math.max(0.001, 1 - away));
      parts.letter.visible = away < 0.999;
    }
    if (parts.flap) parts.flap.visible = away < 0.999;
    if (parts.line) parts.line.visible = away < 0.6;
    if (scene.parts.dot) scene.parts.dot.visible = away < 0.6;

    // Platform: leaves develop standing up, then fold out; corners follow.
    develop(platform, boardMats, ground, rig.platform);
    const eK = rig.unfoldEdges;
    edges.forEach((g, i) => {
      const k = smooth(span(eK, i * 0.12, 0.64 + i * 0.12));
      const a = g.userData.folded * (1 - easeSettle(k));
      if (g.userData.axis === 'x') g.rotation.x = a;
      else g.rotation.z = a;
      g.visible = rig.platform > 0.01;
    });
    corners.forEach((g, i) => {
      const k = smooth(span(rig.unfoldCorners, i * 0.1, 0.7 + i * 0.1));
      g.rotation.z = g.userData.folded * (1 - easeSettle(k));
    });
    creases.forEach(s => (s.drawn = smooth(span(rig.unfoldCorners, 0.5, 1))));
    blueprint.forEach((s, i) => (s.drawn = clamp01(rig.blueprint * 1.6 - i * 0.15)));

    // Parcel: shrinks onto the belt.
    const sh = smooth(rig.shrink);
    const ride = rig.ride;
    const sc = lerp(1, PARCEL.scale, sh);
    parcel.scale.setScalar(sc);
    let px = lerp(B.cx, PARCEL.x, sh);
    let pz = lerp(B.cz, PARCEL.z, sh);
    if (ride > 0) {
      const s0 = sToX(PARCEL.x);
      const s1 = sToX(SPOT.identity.x) - 0.01;
      const p = beltPath.pointAt(lerp(s0, s1, glide(ride)));
      px = p[0];
      pz = p[1];
    }
    parcel.position.set(px, lerp(TOP * smooth(rig.platform), TOP + 0.24, sh), pz);

    // HQ.
    develop(hq, hqMaterials, ground, Math.min(1, rig.hq * 3));
    hq.scale.y = 1;
    hq.visible = rig.hq > 0.002;
    facades.forEach((f, i) => {
      const k = smooth(span(rig.hq, 0.15 + i * 0.12, 0.6 + i * 0.12));
      f.hinge.rotation.x = f.folded * (1 - easeSettle(k));
    });
    const r = smooth(rig.roof);
    roof.visible = rig.roof > 0.002;
    roof.position.y = 0.22 + HQ.h + (1 - easeSettle(r)) * 2.2;
    roof.rotation.y = (1 - r) * 0.5;
    canopy.scale.set(Math.max(0.001, smooth(rig.sign)), 1, 1);
    door.visible = rig.hq > 0.7;
    signPlate.scale.y = Math.max(0.001, smooth(rig.sign));
    signLabel.opacity = smooth(span(rig.sign, 0.4, 1));

    // Conveyor and stations.
    const cv = rig.conveyor;
    rail.forEach(m => {
      const k = smooth(span(cv * beltPath.total * 1.15, m.userData.s, m.userData.s + 1.2));
      m.scale.y = Math.max(0.001, k);
      m.visible = k > 0.002;
    });
    belt.range = [0, cv > 0 ? smooth(cv) * beltPath.total : -1];
    beltLines.range = [0, cv > 0.98 ? beltPath.total : -1];
    const st = rig.stations;
    develop(packing, stationsMaterials, ground, st);
    develop(identity, identityMaterials, ground, st);
    develop(dispatch, stationsMaterials, ground, st);
    for (const g of [packing, identity, dispatch]) g.visible = st > 0.002;
    // Paper iris stays closed on this page: face enrollment happens after email verification.
    blades.forEach(({ pin, phi }) => {
      const nx = -Math.cos(phi);
      const nz = -Math.sin(phi);
      pin.rotation.y = Math.atan2(-nz, nx) - 0.0;
    });
    readyRing.drawn = rig.irisReady;
    ridges.forEach((s, i) => (s.drawn = clamp01(rig.fp * 1.6 - i * 0.08)));

    // Application box.
    develop(appBody, appMaterials, ground, rig.appBox);
    app.visible = rig.appBox > 0.002;
    lidHinge.rotation.x = -lerp(2.1, 0, smooth(rig.appLid));
    const rideS = rig.appRide;
    if (rideS > 0) {
      const p = beltPath.pointAt(glide(rideS) * (beltPath.total - 0.01));
      // First a short hop from the packing counter onto the belt, then the belt.
      const hop = smooth(rideS / 0.08);
      appPos.set(lerp(appHome.x, p[0], hop), lerp(appHome.y, TOP + 0.24, hop) + Math.sin(hop * Math.PI) * 0.6, lerp(appHome.z, p[1], hop));
    } else appPos.copy(appHome);
    app.position.copy(appPos);
    appWarnRing.drawn = rig.appWarn;
    appWarnRing.opacity = rig.appWarn;
    appLabel.visible = rig.appLabel > 0.01;
    appLabel.scale.x = Math.max(0.001, smooth(rig.appLabel));

    // Documents.
    for (const d of docs) {
      d.g.visible = d.t > 0.001;
      if (!d.g.visible) continue;
      const t = Math.min(0.999, d.t);
      docPath.getPointAt(t, _q);
      d.g.position.copy(_q);
      d.g.position.y += d.offset * (1 - smooth(span(t, 0.85, 1)));
      d.g.rotation.set(-0.15 * Math.sin(t * Math.PI), t * 0.6 + d.offset, 0.12 * Math.sin(t * Math.PI * 2) + d.warn * 0.25);
      const inBox = smooth(span(t, 0.92, 1));
      d.g.scale.setScalar(lerp(1, 0.85, inBox));
      paper.color.copy(paperColor);
      _c.copy(paperColor).lerp(warnColor, d.warn * 0.35);
      d.sheet.material[0] = d.warn > 0.01 ? (d.warnMat ??= paper.clone()) : paper;
      if (d.warnMat) d.warnMat.color.copy(_c);
    }

    // Arm: reaches toward the box whenever a paper is landing.
    const landing = Math.max(0, ...docs.map(d => (d.state === 'accepted' ? Math.sin(span(d.t, 0.6, 1) * Math.PI) : 0)));
    armA.rotation.z = lerp(-0.25, -0.75, landing);
    armB.rotation.z = lerp(1.2, 1.7, landing);
    stage.invalidate();
  }

  function sToX(x) {
    // Arclength on the first straight run of the belt where it reaches world x.
    return Math.max(0, Math.min(beltPath.total, x - (SPOT.packing.x + 3.4)));
  }

  function setCompany(name) {
    const text = (name || '').trim().slice(0, 48);
    if (text === signText) return;
    signText = text;
    drawLine(signLabel, text || ' ', { weight: 680, size: 0.62, color: COLOR.graphite, tracking: 0.02 });
    stage.invalidate();
  }

  function camAnchor(name) {
    return SPOT[name];
  }

  return { root, apply, moduleFor, applyModule, modules, docs, setCompany, camAnchor, parcel, adoptParcel };
}

// --- helpers ----------------------------------------------------------------------------------------

/** Board settles past flat by a hair and comes back: stiff, not springy. */
function easeSettle(k) {
  return gsap.parseEase('boardSettle')(clamp01(k));
}

function glide(t) {
  return gsap.parseEase('glide')(clamp01(t));
}

function circlePath(cx, cz, r) {
  const k = 0.5523 * r;
  return new Path()
    .M(cx - r, cz)
    .C(cx - r, cz - k, cx - k, cz - r, cx, cz - r)
    .C(cx + k, cz - r, cx + r, cz - k, cx + r, cz)
    .C(cx + r, cz + k, cx + k, cz + r, cx, cz + r)
    .C(cx - k, cz + r, cx - r, cz + k, cx - r, cz)
    .build({ step: 0.04 });
}

function roundRectPath(x0, z0, x1, z1, r) {
  return new Path().M(x0 + r, z0).L(x1, z0, r).L(x1, z1, r).L(x0, z1, r).L(x0, z0, r).L(x0 + r, z0).build({ step: 0.05 });
}

function ridgePaths() {
  const out = [];
  for (let k = 0; k < 6; k++) {
    const a = 0.14 + k * 0.1;
    const top = 0.2 + k * 0.11;
    const foot = 0.12 + k * 0.08;
    const kk = 0.62 * a;
    out.push(new Path().M(-a, foot).L(-a, 0).C(-a, -top * 0.7, -kk, -top, 0, -top).C(kk, -top, a, -top * 0.7, a, 0).L(a, foot * (k % 2 ? 0.6 : 1)).build({ step: 0.03 }));
  }
  return out;
}

/** Window grid for the HQ facades: ceramic mullions, warm glass, one orange floor line. */
function facadeTexture() {
  const c = document.createElement('canvas');
  c.width = 512;
  c.height = 512;
  const g = c.getContext('2d');
  g.fillStyle = '#FDFBF7';
  g.fillRect(0, 0, 512, 512);
  const floors = 3;
  const cols = 5;
  const fh = 512 / floors;
  for (let f = 0; f < floors; f++) {
    for (let i = 0; i < cols; i++) {
      const x = 22 + i * ((512 - 44) / cols);
      const w = (512 - 44) / cols - 14;
      const y = f * fh + 26;
      const grad = g.createLinearGradient(0, y, 0, y + fh - 52);
      grad.addColorStop(0, '#6c645b');
      grad.addColorStop(1, '#3f3a34');
      g.fillStyle = grad;
      g.fillRect(x, y, w, fh - 52);
      g.fillStyle = 'rgba(255,255,255,0.12)';
      g.fillRect(x, y, w * 0.35, fh - 52);
    }
    g.fillStyle = '#E9E1D6';
    g.fillRect(0, (f + 1) * fh - 8, 512, 8);
  }
  g.fillStyle = '#FF7900';
  g.fillRect(0, fh - 8, 512, 5);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 8;
  return t;
}

import * as THREE from 'three';
import { Stroke, PenDot } from '../core/stroke.js';
import { sheetMaterial, sheetGeometry, develop } from '../core/paper.js';
import { Path, roundedRect } from '../core/paths.js';
import { metalMaterial, studioEnvironment } from '../core/metal.js';
import { COLOR, LINE } from '../core/tokens.js';
import * as L from './layout.js';

// ORANGE SIGNAL, L03–L10: the hardware that the flat SIM drawing grows into once the form is sent.
//
//   verification boundary  the ring the signal path already ends on (L01). The pulse waits there for
//                          the server, stops short and dims on a rejection, passes on acceptance.
//   electronic core        the card gains real thickness, the contact plates lift on their own
//                          footprints and reveal the die beneath them (exploded view, not an explosion).
//   Optical Identity       ceramic housing, champagne bezel and eight graphite iris blades. Each blade
//                          turns about its own pin on the pitch circle; the straight inner edges always
//                          leave a regular octagon whose inscribed radius is exactly the requested
//                          aperture, so the live camera can be clipped to the opening the blades leave.
//                          Coordinated motion from that one constraint, not a simulated linkage.
//   Touch Identity         ceramic sensor slab with engraved ridges, lit when the account has a passkey
//                          (WebAuthn: the device unlocks it with its fingerprint, face or PIN, or a phone via
//                          QR code). The server cannot tell which modality the device used.
//   fiber                  built for the finale only, from wherever the authenticated pulse stands.
//
// Every value is a rig number written by the director; this module only turns numbers into a pose.

const clamp01 = v => Math.max(0, Math.min(1, v));
const smooth = v => {
  const t = clamp01(v);
  return t * t * (3 - 2 * t);
};
const lerp = (a, b, t) => a + (b - a) * t;

// --- Layout ---------------------------------------------------------------------------------------
export const OPT = { x: 15.8, z: -6.2, housing: 3.85, h: 0.62, pitch: 2.3, open: 1.5, cover: 1.62, bezel: 3.72 };
export const TOUCH = { x: 15.8, z: 4.6, w: 3.8, d: 4.4, h: 0.46 };
const J = { x: 11.0, z: L.SIM.row }; // junction beyond the boundary where the two module paths split
const BLADES = 8;
const LIFT = 0.55; // world height the contact plates rise to at full lift
const DEPTH = 3.4; // card thickness multiplier in 3D (0.11 → 0.37)

export const RIG_DEFAULTS = {
  depth: 0, lift: 0,
  pulse: 0, pulseSig: 0, pulseBranch: 0, wait: 0,
  gateOk: 0, gateErr: 0, gateWarn: 0, block: 0,
  branches: 0, optic: 0, bezel: 0, touch: 0, fp: 0, hover: 0, touchLit: 0,
  iris: 0, irisWarn: 0, ring: 0, ringOk: 0, proc: 0, procPhase: 0,
  cable: 0, finale: 0
};

export function buildHardware(stage, root, card, pads, sig) {
  const groundColor = new THREE.Color(COLOR.ground);
  const env = studioEnvironment(stage.renderer);
  const topY = L.T_SHELL + 0.01;

  // ----------------------------------------------------------------------------------------------
  // Die under the contact plates: graphite substrate with orange bond lines, seen once they lift.
  // ----------------------------------------------------------------------------------------------
  const c = L.CHIP;
  const dieMat = new THREE.MeshStandardMaterial({ color: '#2f2924', roughness: 0.42, metalness: 0.2, envMap: env, envMapIntensity: 0.5 });
  const die = new THREE.Mesh(sheetGeometry(roundedRect(c.x0 + 0.5, c.z0 + 0.5, c.x1 - 0.5, c.z1 - 0.5, 0.5), 0.004, { y: L.T_SHELL + 0.004 }), dieMat);
  die.name = 'sim.die';
  die.receiveShadow = true;
  die.visible = false;
  card.add(die);
  const bond = [];
  for (let i = 0; i < 6; i++) {
    const z = c.z0 + 1.2 + i * ((c.z1 - c.z0 - 2.4) / 5);
    const p = new Path().M(c.x0 + 0.9, z).L(-0.6, z, 0).L(-0.2, z + (i < 3 ? 0.4 : -0.4), 0).L(0.9, z + (i < 3 ? 0.4 : -0.4)).build({ step: 0.05 });
    const s = new Stroke(p, { color: COLOR.orangeInk, width: 0.045, y: L.T_SHELL + 0.012 });
    s.visible = false;
    card.add(s);
    bond.push(s);
  }

  // ----------------------------------------------------------------------------------------------
  // Boundary furniture: the "blocked" bar and the two module paths beyond the junction.
  // ----------------------------------------------------------------------------------------------
  const G = L.GATE;
  const block = new Stroke(new Path().M(G.x - G.r - 0.32, G.z - 0.42).L(G.x - G.r - 0.32, G.z + 0.42).build({ step: 0.03 }), { color: COLOR.error, width: 0.07, y: 0.014 });
  root.add(block);

  const branchA = new Path().M(G.x + G.r, G.z).L(J.x, J.z, 0.2).L(13.25, -3.25).build({ step: 0.05 });
  const branchB = new Path().M(J.x, J.z).L(14.4, TOUCH.z - TOUCH.d / 2).build({ step: 0.05 });
  const inkA = new Stroke(branchA, { color: COLOR.ink, width: LINE.detail, y: 0.012 });
  const inkB = new Stroke(branchB, { color: COLOR.ink, width: LINE.detail, y: 0.012 });
  const litA = new Stroke(branchA, { color: COLOR.orangeInk, width: LINE.main, y: 0.013 });
  root.add(inkA, inkB, litA);

  // ----------------------------------------------------------------------------------------------
  // Optical Identity
  // ----------------------------------------------------------------------------------------------
  const optic = new THREE.Group();
  optic.name = 'optic';
  optic.position.set(OPT.x, 0, OPT.z);
  root.add(optic);

  const ceramic = sheetMaterial(COLOR.ceramic, { roughness: 0.5, bump: 0.05, grain: false });
  const ceramicSide = sheetMaterial(COLOR.ceramicEdge, { roughness: 0.55, bump: 0.05, grain: false });
  const opticBody = new THREE.Group();
  optic.add(opticBody);
  const housing = new THREE.Mesh(new THREE.CylinderGeometry(OPT.housing, OPT.housing, OPT.h, 96, 1, false), [ceramicSide, ceramic, ceramic]);
  housing.position.y = OPT.h / 2;
  housing.castShadow = true;
  housing.receiveShadow = true;
  opticBody.add(housing);

  // Lens well: a deep glossy graphite disc under the blades (the camera picture replaces it when open).
  const glassMat = new THREE.MeshStandardMaterial({ color: '#211c18', roughness: 0.1, metalness: 0.1, envMap: env, envMapIntensity: 0.9 });
  const glass = new THREE.Mesh(new THREE.CircleGeometry(OPT.cover + 0.05, 96), glassMat);
  glass.rotation.x = -Math.PI / 2;
  glass.position.y = OPT.h + 0.004;
  opticBody.add(glass);
  const lensRings = [0.55, 0.95].map(r => {
    const s = new Stroke(L.circlePath(0, 0, r, 0.03), { color: COLOR.champagne, width: 0.022, y: OPT.h + 0.01 });
    s.opacity = 0.55;
    opticBody.add(s);
    return s;
  });

  // Iris blades. Local frame of a blade: the pin at the origin, the cutting edge along +x, the body
  // on +z. A Y rotation of θ sends +x to (cos θ, −sin θ) and +z to (−sin θ … ) — see bladeAngle().
  const bladeMat = metalMaterial(stage.renderer, '#4b433b', { roughness: 0.5, intensity: 0.8 });
  const bladeEdgeMat = new THREE.MeshStandardMaterial({ color: '#2a2520', roughness: 0.6 });
  const P = OPT.pitch;
  const bladeShape = [];
  const reach = P + 0.35;
  for (let i = 0; i <= 16; i++) {
    const u = reach * (1 - i / 16);
    // Wide enough to close the aperture, narrow enough to stay inside the bezel at full opening.
    const rho = P - u;
    const cap = Math.abs(rho) < 2.6 ? Math.sqrt(2.6 * 2.6 - rho * rho) : 0.18;
    bladeShape.push([u, Math.max(0.18, Math.min(rho * 1.06 + 0.3, cap))]);
  }
  bladeShape.unshift([0, 0], [reach, 0]);
  const bladeOutline = [[0, 0], [reach, 0], ...bladeShape.slice(2)];
  const bladeGeo = sheetGeometry(bladeOutline, 0.022);
  const blades = [];
  for (let i = 0; i < BLADES; i++) {
    const phi = (i / BLADES) * Math.PI * 2;
    const pin = new THREE.Group();
    pin.position.set(Math.cos(phi) * P, OPT.h + 0.02 + i * 0.006, Math.sin(phi) * P);
    const mesh = new THREE.Mesh(bladeGeo, [bladeMat, bladeEdgeMat, bladeEdgeMat]);
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    pin.add(mesh);
    opticBody.add(pin);
    blades.push({ pin, phi });
  }

  // Bezel: a lathed champagne ring with a chamfered inner lip; it rises out of the housing and turns
  // into place. An orange anodised hairline marks its outer edge.
  const bezelProfile = [
    [OPT.cover, -0.5], [OPT.cover, 0.14], [OPT.cover + 0.1, 0.24], [OPT.bezel - 0.18, 0.28],
    [OPT.bezel, 0.16], [OPT.bezel, -0.5]
  ].reverse().map(([r, y]) => new THREE.Vector2(r, y)); // outer → inner, so the faces point outward
  const bezelMat = metalMaterial(stage.renderer, COLOR.champagne, { roughness: 0.3 });
  const bezelPivot = new THREE.Group();
  bezelPivot.position.y = OPT.h;
  const bezelMesh = new THREE.Mesh(new THREE.LatheGeometry(bezelProfile, 120), bezelMat);
  bezelMesh.castShadow = true;
  bezelMesh.receiveShadow = true;
  bezelPivot.add(bezelMesh);
  // Index marks on the bezel: a premium instrument detail that also makes its rotation readable.
  const tickMat = new THREE.MeshStandardMaterial({ color: '#7b6a55', roughness: 0.5, metalness: 0.6, envMap: env });
  for (let i = 0; i < 24; i++) {
    const a = (i / 24) * Math.PI * 2;
    const tick = new THREE.Mesh(new THREE.BoxGeometry(i % 6 === 0 ? 0.26 : 0.14, 0.012, 0.028), tickMat);
    const r = OPT.bezel - 0.42;
    tick.position.set(Math.cos(a) * r, 0.278, Math.sin(a) * r);
    tick.rotation.y = -a;
    bezelPivot.add(tick);
  }
  optic.add(bezelPivot);
  const accent = new THREE.Mesh(new THREE.TorusGeometry(OPT.bezel + 0.02, 0.035, 10, 120), new THREE.MeshStandardMaterial({ color: COLOR.orange, roughness: 0.4, metalness: 0.3, envMap: env }));
  accent.rotation.x = Math.PI / 2;
  accent.position.y = 0.12;
  bezelPivot.add(accent);

  // Verification ring and processing arc, printed just above the bezel's inner lip.
  const ringPath = L.circlePath(0, 0, OPT.cover + 0.28, 0.03);
  const ringY = OPT.h + 0.3;
  const verifyRing = new Stroke(ringPath, { color: COLOR.orangeInk, width: 0.07, y: ringY });
  const procArc = new Stroke(ringPath, { color: COLOR.orange, width: 0.06, y: ringY + 0.002 });
  const warnRing = new Stroke(ringPath, { color: COLOR.warning, width: 0.05, y: ringY + 0.001 });
  warnRing.drawn = 1;
  optic.add(verifyRing, procArc, warnRing);

  // ----------------------------------------------------------------------------------------------
  // Touch Identity (presentation only)
  // ----------------------------------------------------------------------------------------------
  const touch = new THREE.Group();
  touch.name = 'touch';
  touch.position.set(TOUCH.x, 0, TOUCH.z);
  root.add(touch);
  const tw = TOUCH.w / 2;
  const td = TOUCH.d / 2;
  const slab = new THREE.Mesh(sheetGeometry(roundedRect(-tw, -td, tw, td, 0.7), TOUCH.h), [ceramic, ceramic, ceramicSide]);
  slab.castShadow = true;
  slab.receiveShadow = true;
  touch.add(slab);
  const padMat = sheetMaterial(COLOR.substrate, { roughness: 0.75, bump: 0.1, grain: false });
  const sensor = new THREE.Mesh(sheetGeometry(roundedRect(-tw + 0.45, -td + 0.45, tw - 0.45, td - 0.45, 0.45), 0.012, { y: TOUCH.h }), padMat);
  sensor.receiveShadow = true;
  touch.add(sensor);
  const touchMaterials = [ceramic, ceramicSide, padMat];
  const ridges = fingerprintRidges().map(p => {
    const s = new Stroke(p, { color: COLOR.warmGray, width: 0.04, y: TOUCH.h + 0.016 });
    touch.add(s);
    return s;
  });
  const opticMaterials = [ceramic, ceramicSide];

  // ----------------------------------------------------------------------------------------------
  // The pulse
  // ----------------------------------------------------------------------------------------------
  const pulse = new PenDot({ color: COLOR.orange, radius: 0.24 });
  pulse.name = 'signal-pulse';
  root.add(pulse);

  // ----------------------------------------------------------------------------------------------
  // Fiber (built at the finale from the pulse's position)
  // ----------------------------------------------------------------------------------------------
  let fiber = null;
  const sheathMat = new THREE.MeshStandardMaterial({ color: '#F3ECE2', roughness: 0.22, metalness: 0, transparent: true, opacity: 0.55, envMap: env, envMapIntensity: 0.6, depthWrite: false });
  const coreMat = new THREE.MeshStandardMaterial({ color: '#E7BE98', roughness: 0.4 });
  const litMat = new THREE.MeshBasicMaterial({ color: COLOR.orange, toneMapped: false });
  const ferruleMat = metalMaterial(stage.renderer, COLOR.champagne, { roughness: 0.28 });
  const headMat = new THREE.MeshBasicMaterial({ color: '#FFB066', toneMapped: false });

  function buildFiber(from) {
    disposeFiber();
    const [x, z] = from;
    const curve = new THREE.CatmullRomCurve3([
      new THREE.Vector3(x, 0.2, z),
      new THREE.Vector3(x + 2.4, 0.22, z + 0.3),
      new THREE.Vector3(x + 6.5, 0.24, z - 0.8),
      new THREE.Vector3(x + 11, 0.24, z - 3.4),
      new THREE.Vector3(x + 17, 0.24, z - 7.5),
      new THREE.Vector3(x + 26, 0.24, z - 12)
    ], false, 'centripetal');
    const steps = 220;
    const group = new THREE.Group();
    group.name = 'fiber';
    const sheath = new THREE.Mesh(new THREE.TubeGeometry(curve, steps, 0.2, 18, false), sheathMat);
    sheath.castShadow = true;
    const core = new THREE.Mesh(new THREE.TubeGeometry(curve, steps, 0.055, 8, false), coreMat);
    const lit = new THREE.Mesh(new THREE.TubeGeometry(curve, steps, 0.07, 8, false), litMat);
    lit.renderOrder = 12;
    const ferrule = new THREE.Mesh(new THREE.CylinderGeometry(0.27, 0.27, 0.8, 32), ferruleMat);
    const t0 = curve.getTangentAt(0.012);
    ferrule.position.copy(curve.getPointAt(0.012));
    ferrule.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), t0);
    ferrule.castShadow = true;
    const head = new THREE.Mesh(new THREE.SphereGeometry(0.11, 16, 12), headMat);
    group.add(sheath, core, lit, ferrule, head);
    root.add(group);
    fiber = { group, curve, sheath, lit, head, steps, ferrule, perStep: 8 * 6 };
    return fiber;
  }

  function disposeFiber() {
    if (!fiber) return;
    fiber.group.traverse(o => o.geometry?.dispose());
    root.remove(fiber.group);
    fiber = null;
  }

  // ----------------------------------------------------------------------------------------------
  // Paths of the pulse
  // ----------------------------------------------------------------------------------------------
  const sigInner = sig.inner;
  const sigOuter = sig.outer;
  const sigTotal = sigInner.total + sigOuter.total;
  const _p = [0, 0];
  function pulsePoint(rig, cardTop) {
    if (rig.pulseBranch > 0) {
      branchA.pointAt(rig.pulseBranch * branchA.total, _p);
      return [_p[0], 0.05, _p[1]];
    }
    const run = rig.pulseSig * sigTotal;
    if (run <= sigInner.total) {
      sigInner.pointAt(run, _p);
      return [_p[0], cardTop + 0.03, _p[1]];
    }
    sigOuter.pointAt(run - sigInner.total, _p);
    return [_p[0], 0.05, _p[1]];
  }

  // ----------------------------------------------------------------------------------------------
  // rig → pose
  // ----------------------------------------------------------------------------------------------
  const _c = new THREE.Color();
  const errColor = new THREE.Color(COLOR.error);
  const warnColor = new THREE.Color(COLOR.warning);
  const okColor = new THREE.Color(COLOR.orangeInk);
  const inkColor = new THREE.Color(COLOR.ink);
  const successColor = new THREE.Color(COLOR.success);
  const ringBase = new THREE.Color(COLOR.orangeInk);
  const opening = { r: 0 };

  function apply(rig, gate) {
    // Electronic core: real thickness, then the plates lift on their own footprints.
    const depthK = smooth(rig.depth);
    card.scale.y *= lerp(1, DEPTH, depthK);
    const sy = card.scale.y;
    const maxOrder = Math.max(...pads.map(p => p.order));
    for (const p of pads) {
      const k = smooth((rig.lift * (maxOrder + 1.4) - p.order) / 1.4);
      p.mesh.position.y = L.T_SHELL + 0.006 + (LIFT * k) / Math.max(0.02, sy);
      p.mesh.castShadow = p.mesh.castShadow || k > 0.05;
    }
    const coreShown = rig.lift > 0.02;
    die.visible = coreShown;
    bond.forEach((s, i) => {
      s.visible = coreShown;
      s.drawn = clamp01(rig.lift * 2.2 - i * 0.18);
    });

    // Boundary tone: ink → orange (passed) / amber (no answer) / error (refused).
    _c.copy(inkColor).lerp(okColor, rig.gateOk).lerp(warnColor, rig.gateWarn).lerp(errColor, rig.gateErr);
    gate.material.uniforms.uColor.value.copy(_c);
    gate.width = lerp(LINE.detail, LINE.main, Math.max(rig.gateOk, rig.gateErr, rig.gateWarn));
    block.drawn = rig.block;
    block.opacity = rig.block > 0 ? 0.9 : 0;

    // Module paths.
    inkA.drawn = clamp01(rig.branches * 1.3);
    inkB.drawn = clamp01(rig.branches * 1.3 - 0.3);
    litA.range = [0, rig.pulseBranch > 0 ? rig.pulseBranch * branchA.total : -1];

    // Optical module: rises from the page like every other object, then the bezel turns into place.
    develop(opticBody, opticMaterials, groundColor, rig.optic);
    opticBody.visible = rig.optic > 0.002;
    const hoverY = 0.18 * smooth(rig.hover);
    opticBody.position.y = hoverY;
    const b = smooth(rig.bezel);
    bezelPivot.visible = rig.bezel > 0.002;
    // Hidden inside the housing at 0, seated on it at 1.
    bezelPivot.position.y = OPT.h * opticBody.scale.y + hoverY + lerp(-0.62, 0, b);
    bezelPivot.rotation.y = lerp(0.85, 0, b) + rig.hover * 0.12;
    lensRings.forEach(s => (s.opacity = 0.55 * smooth(rig.optic)));

    // Iris: θ such that every cutting edge lies OPT.open·iris from the axis (inscribed octagon).
    opening.r = OPT.open * clamp01(rig.iris);
    const alpha = Math.asin(Math.min(0.999, opening.r / P));
    for (const { pin, phi } of blades) {
      // Inward radial n = (−cos φ, −sin φ); edge direction d = n rotated by α.
      const nx = -Math.cos(phi);
      const nz = -Math.sin(phi);
      const dx = nx * Math.cos(alpha) - nz * Math.sin(alpha);
      const dz = nx * Math.sin(alpha) + nz * Math.cos(alpha);
      pin.rotation.y = Math.atan2(-dz, dx);
      pin.visible = rig.optic > 0.5;
    }
    glass.visible = rig.optic > 0.5;

    verifyRing.drawn = rig.ring;
    verifyRing.material.uniforms.uColor.value.copy(ringBase).lerp(successColor, rig.ringOk);
    const arc = ringPath.total * 0.16;
    const start = rig.procPhase * ringPath.total;
    procArc.range = [start, start + arc];
    procArc.opacity = rig.proc;
    warnRing.opacity = rig.irisWarn * 0.85;

    // Touch module.
    develop(touch, touchMaterials, groundColor, rig.touch);
    touch.visible = rig.touch > 0.002;
    _c.copy(inkColor).lerp(okColor, rig.touchLit);
    ridges.forEach((s, i) => {
      s.drawn = clamp01(rig.fp * 1.8 - i * 0.07);
      s.material.uniforms.uColor.value.copy(_c);
    });

    // Pulse.
    const pt = pulsePoint(rig, L.T_SHELL * sy + 0.02);
    if (fiber && rig.cable > 0) {
      const t = clamp01(rig.finale);
      const q = fiber.curve.getPointAt(Math.max(0.001, t));
      pulse.position.set(q.x, q.y + 0.05, q.z);
      fiber.head.position.copy(q);
      fiber.head.visible = t > 0.002 && t < 0.999;
      fiber.lit.geometry.setDrawRange(0, Math.floor(t * fiber.steps) * fiber.perStep);
      fiber.sheath.material.opacity = 0.55 * smooth(rig.cable);
      fiber.group.visible = true;
    } else {
      pulse.position.set(pt[0], pt[1], pt[2]);
    }
    pulse.opacity = rig.pulse;
    pulse.scale.setScalar(Math.max(0.001, 0.6 + 0.4 * rig.pulse));
    pulse.ring(rig.wait, rig.wait > 0 ? 0.6 * rig.pulse : 0);
  }

  /** Where the iris opening is on screen, in stage pixels (for the live camera). */
  const _v = new THREE.Vector3();
  const _r = new THREE.Vector3();
  function apertureOnScreen(camera, width, height) {
    const y = OPT.h + opticBody.position.y + 0.02;
    _v.set(OPT.x, y, OPT.z).project(camera);
    _r.set(OPT.x + OPT.cover, y, OPT.z).project(camera);
    const cx = (_v.x * 0.5 + 0.5) * width;
    const cy = (-_v.y * 0.5 + 0.5) * height;
    const rx = (_r.x * 0.5 + 0.5) * width;
    const ry = (-_r.y * 0.5 + 0.5) * height;
    const cover = Math.hypot(rx - cx, ry - cy);
    return { cx, cy, cover, r: cover * (opening.r / OPT.cover) };
  }

  /** Where a finale should start: the far side of the optical housing, or just past the boundary. */
  function finaleOrigin(fromOptic) {
    return fromOptic ? [OPT.x + OPT.housing + 0.1, OPT.z] : [G.x + G.r + 0.2, G.z];
  }

  return { apply, apertureOnScreen, buildFiber, disposeFiber, finaleOrigin, defaults: RIG_DEFAULTS };
}

/** Engraved ridges of the Touch Identity sensor: nested arches around an off-centre core. */
function fingerprintRidges() {
  const out = [];
  const cx = 0;
  const cz = 0.15;
  for (let k = 0; k < 8; k++) {
    const a = 0.32 + k * 0.17; // half width
    const top = 0.42 + k * 0.2; // height above the core
    const foot = 0.25 + k * 0.17 - (k % 2) * 0.12; // how far the arms run down
    const kk = 0.62 * a;
    const p = new Path()
      .M(cx - a, cz + foot)
      .L(cx - a, cz)
      .C(cx - a, cz - top * 0.7, cx - kk, cz - top, cx, cz - top)
      .C(cx + kk, cz - top, cx + a, cz - top * 0.7, cx + a, cz)
      .L(cx + a, cz + foot * (k % 3 === 0 ? 0.6 : 1))
      .build({ step: 0.04 });
    out.push(p);
  }
  // Loops at the base close the pattern the way a real print does.
  for (let k = 0; k < 3; k++) {
    const y = cz + 0.75 + k * 0.28;
    out.push(new Path().M(-1.05 + k * 0.08, y).C(-0.5, y - 0.18, 0.5, y - 0.18, 1.05 - k * 0.08, y).build({ step: 0.04 }));
  }
  return out;
}

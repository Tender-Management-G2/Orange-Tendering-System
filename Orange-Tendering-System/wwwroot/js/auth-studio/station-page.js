import * as THREE from 'three';
import gsap from 'gsap';
import { Stage, webglAvailable } from './core/stage.js';
import { Stroke, PenDot } from './core/stroke.js';
import { sheetMaterial, sheetGeometry, develop } from './core/paper.js';
import { Path, roundedRect } from './core/paths.js';
import { metalMaterial } from './core/metal.js';
import { COLOR, LINE } from './core/tokens.js';
import './core/segments.js'; // registers the material eases (paperSettle, boardSettle, glide)

// SUPPLIER GENESIS after the registration form, on the real pages:
//   data-chapter="verify"    VerifyEmail.cshtml    R09  a letter rides from the application package to
//                            the email-verification ring. The ring closes only when the server has
//                            accepted a code (that page then redirects); a refused or expired code
//                            leaves it open with an amber accent. Nothing here knows the code.
//   data-chapter="identity"  PendingApproval.cshtml R06/R07/R10  the Identity Station in close-up:
//                            the Paper Iris opens on the live camera only after biometric.js reports
//                            that the camera is really running; it folds shut and the identity card
//                            goes into the package only when the server confirms the enrollment.
//                            Fingerprint / Device Passkey: a mobile module in front of the station; its link to
//                            the identity package lights only after the server verified the WebAuthn registration
//                            (the browser's own prompt or QR code does the device part; no QR is drawn here).
// Server facts arrive as data attributes on #authStudio and as authstudio:face events from
// biometric.js. Animation never decides anything.

const clamp01 = v => Math.max(0, Math.min(1, v));
const smooth = v => {
  const t = clamp01(v);
  return t * t * (3 - 2 * t);
};
const lerp = (a, b, t) => a + (b - a) * t;

const shell = document.getElementById('authStudio');
const stageEl = document.getElementById('asStage');
const mode = shell?.dataset.chapter;

function circlePath(cx, cz, r, step = 0.03) {
  const k = 0.5523 * r;
  return new Path()
    .M(cx - r, cz)
    .C(cx - r, cz - k, cx - k, cz - r, cx, cz - r)
    .C(cx + k, cz - r, cx + r, cz - k, cx + r, cz)
    .C(cx + r, cz + k, cx + k, cz + r, cx, cz + r)
    .C(cx - k, cz + r, cx - r, cz + k, cx - r, cz)
    .build({ step });
}

function boot() {
  if (!shell || !stageEl || !mode) return;
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (!webglAvailable()) {
    stageEl.dataset.fallback = '1';
    stageEl.dataset.ready = '1';
    return;
  }
  let stage;
  try {
    stage = new Stage(stageEl, { maxPixelRatio: window.innerWidth < 900 ? 1.75 : 2 });
  } catch (e) {
    console.error('The illustration could not start:', e);
    stageEl.dataset.ready = '1';
    return;
  }
  const status = stageEl.querySelector('[data-scene-status]');
  const say = text => { if (status) status.textContent = text; };
  const ground = new THREE.Color(COLOR.ground);
  const scene = stage.scene;
  const rig = { dev: 0, line: 0, ride: 0, ring: 0, warn: 0, iris: 0, proc: 0, procPhase: 0, card: 0, lid: 0, fp: 0, pending: 0,
    phone: 0, link: 0, glow: 0, pulseT: 0, pulse: 0, wait: 0, pwarn: 0 };
  const invalidate = () => stage.invalidate();
  const to = (props, o = {}) => {
    if (reduced) { Object.assign(rig, props); stage.invalidate(); o.onComplete?.(); return; }
    gsap.to(rig, { duration: 0.8, ease: 'power2.inOut', ...o, ...props, overwrite: 'auto', onUpdate: invalidate });
  };

  const kraft = sheetMaterial(COLOR.kraftOuter, { roughness: 0.92, bump: 0.35 });
  const kraftIn = sheetMaterial(COLOR.kraftInner, { roughness: 0.92, bump: 0.35 });
  const ceramic = sheetMaterial(COLOR.ceramic, { roughness: 0.6, bump: 0.05, grain: false });
  const ceramicEdge = sheetMaterial(COLOR.ceramicEdge, { roughness: 0.65, bump: 0.05, grain: false });
  const paper = sheetMaterial(COLOR.paper, { roughness: 0.9, bump: 0.3 });
  const champagne = metalMaterial(stage.renderer, COLOR.champagne, { roughness: 0.3 });
  const orange = new THREE.MeshStandardMaterial({ color: COLOR.orange, roughness: 0.6 });
  const mesh = (geo, mat, shadow = true) => {
    const m = new THREE.Mesh(geo, mat);
    m.castShadow = shadow;
    m.receiveShadow = true;
    return m;
  };
  const box = (w, h, d, mat, x = 0, y = 0, z = 0) => {
    const m = mesh(new THREE.BoxGeometry(w, h, d), mat);
    m.position.set(x, y + h / 2, z);
    return m;
  };

  // The identity package (same parcel that rode to the station on the registration page).
  const parcel = new THREE.Group();
  const pkgBody = new THREE.Group();
  pkgBody.add(box(3.2, 1.0, 2.2, kraft));
  const lidHinge = new THREE.Group();
  lidHinge.position.set(0, 1.0, -1.1);
  const lid = box(3.24, 0.06, 2.22, kraftIn, 0, -0.03, 1.1);
  lidHinge.add(lid);
  const seal = mesh(new THREE.CylinderGeometry(0.28, 0.28, 0.05, 40), orange);
  seal.position.set(0, 0.04, 1.1);
  lid.add(seal);
  pkgBody.add(lidHinge);
  parcel.add(pkgBody);
  scene.add(parcel);

  const groups = [];
  let apply = () => {};

  if (mode === 'verify') {
    // ---------------------------------------------------------------------------------------- R09
    parcel.position.set(-6.5, 0, 1.5);
    const STATION = { x: 6.0, z: -1.0 };
    const station = new THREE.Group();
    station.position.set(STATION.x, 0, STATION.z);
    station.add(mesh(new THREE.CylinderGeometry(2.6, 2.8, 0.5, 64), [ceramicEdge, ceramic, ceramic]).translateY(0.25));
    scene.add(station);
    groups.push([station, [ceramic, ceramicEdge]], [parcel, [kraft, kraftIn]]);
    const ringPath = circlePath(0, 0, 1.9);
    const ringBase = new Stroke(ringPath, { color: COLOR.ink, width: LINE.detail, y: 0.52 });
    ringBase.drawn = 1;
    const ring = new Stroke(ringPath, { color: COLOR.orangeInk, width: 0.12, y: 0.53 });
    const warnRing = new Stroke(ringPath, { color: COLOR.warning, width: 0.08, y: 0.535 });
    warnRing.drawn = 1;
    station.add(ringBase, ring, warnRing);
    const route = new Path().M(-4.6, 1.5).L(-1.0, 1.5, 0.9).L(1.6, -1.0, 0.9).L(STATION.x - 2.7, -1.0).build({ step: 0.05 });
    const path = new Stroke(route, { color: COLOR.orangeInk, width: LINE.main, y: 0.01 });
    scene.add(path);
    // The letter: a folded paper envelope with an orange flap line.
    const letter = new THREE.Group();
    const env = mesh(sheetGeometry(roundedRect(-0.9, -0.6, 0.9, 0.6, 0.06), 0.03), [paper, paper, ceramicEdge]);
    const flap = new Stroke(new Path().M(-0.85, -0.55).L(0, 0.08).L(0.85, -0.55).build({ step: 0.04 }), { color: COLOR.orangeInk, width: 0.05, y: 0.035 });
    flap.drawn = 1;
    letter.add(env, flap);
    scene.add(letter);
    const _p = [0, 0];
    apply = () => {
      for (const [g, mats] of groups) develop(g, mats, ground, rig.dev);
      path.drawn = rig.line;
      const t = rig.ride;
      // Out of the package, along the path, into the ring. With an error it waits before the ring.
      route.pointAt(t * route.total, _p);
      const hop = Math.sin(Math.min(1, t * 6) * Math.PI) * 0.8;
      letter.position.set(_p[0], 0.25 + hop + (t > 0.97 ? 0.3 : 0), _p[1]);
      letter.visible = t > 0.01;
      letter.rotation.y = -0.4 * (1 - t);
      ring.drawn = rig.ring;
      warnRing.opacity = rig.warn;
      lidHinge.rotation.x = -lerp(0, 1.6, Math.sin(Math.min(1, t * 4) * Math.PI));
    };
    stage.rig.state = Object.assign(stage.rig.state, { tx: 0, ty: 0, tz: 0, w: 22, h: 16, tilt: 38, yaw: -18, fov: 26 });
    const failed = shell.dataset.result === 'failed';
    const resent = shell.dataset.result === 'resent';
    to({ dev: 1 }, { duration: 1.0, ease: 'power2.out' });
    to({ line: 1 }, { duration: 1.1, delay: 0.5, ease: 'power1.inOut' });
    to({ ride: failed ? 0.86 : 1 }, { duration: 1.8, delay: resent ? 0.4 : 1.0, ease: 'glide' });
    // The ring is never closed here: on success the server moves the person to the next page.
    if (failed) to({ warn: 1, ring: 0.42 }, { duration: 0.5, delay: 2.6 });
    else to({ ring: 0.42 }, { duration: 1.0, delay: 2.6, ease: 'power1.out' });
    say(failed ? 'The verification ring stays open: the code was not accepted.' : 'Your verification letter has reached the station. Enter the code from your email to close the ring.');
  } else {
    // ------------------------------------------------------------------------------- R06 / R07 / R10
    const ST = { x: 0, z: 0 };
    const station = new THREE.Group();
    scene.add(station);
    const pedestal = mesh(new THREE.CylinderGeometry(3.6, 3.8, 1.0, 96), [ceramicEdge, ceramic, ceramic]);
    pedestal.position.y = 0.5;
    station.add(pedestal);
    const well = mesh(new THREE.CircleGeometry(2.05, 72), new THREE.MeshStandardMaterial({ color: '#2a2521', roughness: 0.4 }), false);
    well.rotation.x = -Math.PI / 2;
    well.position.y = 1.004;
    station.add(well);
    // Paper Iris: eight paper blades on pins, the same geometric rule as Login's optical iris.
    const P = 2.0;
    const OPEN = 1.6;
    const reach = P + 0.3;
    const outline = [[0, 0], [reach, 0]];
    for (let i = 0; i <= 14; i++) {
      const u = reach * (1 - i / 14);
      const rho = P - u;
      const cap = Math.abs(rho) < 2.3 ? Math.sqrt(2.3 * 2.3 - rho * rho) : 0.14;
      outline.push([u, Math.max(0.14, Math.min(rho * 1.06 + 0.24, cap))]);
    }
    const bladeGeo = sheetGeometry(outline, 0.035);
    const bladeMat = sheetMaterial('#FBF6EE', { roughness: 0.9, bump: 0.35 });
    const blades = [];
    for (let i = 0; i < 8; i++) {
      const phi = (i / 8) * Math.PI * 2;
      const pin = new THREE.Group();
      pin.position.set(Math.cos(phi) * P, 1.02 + i * 0.012, Math.sin(phi) * P);
      const m = mesh(bladeGeo, [bladeMat, bladeMat, ceramicEdge]);
      pin.add(m);
      station.add(pin);
      blades.push({ pin, phi });
    }
    // A raised paper collar covers the blade pins; the aperture is its inner lip.
    // Wide enough to hide every blade tip at full opening (they reach ~3.4 from the axis).
    const collarProfile = [[3.55, 1.0], [3.55, 1.28], [3.4, 1.36], [OPEN + 0.12, 1.32], [OPEN + 0.04, 1.2], [OPEN + 0.04, 1.0]].map(([r, y]) => new THREE.Vector2(r, y));
    const collar = mesh(new THREE.LatheGeometry(collarProfile, 120), sheetMaterial(COLOR.paper, { roughness: 0.85, bump: 0.25 }));
    station.add(collar);
    const ringPath = circlePath(0, 0, OPEN + 0.42);
    const verifyRing = new Stroke(ringPath, { color: COLOR.orangeInk, width: 0.09, y: 1.37 });
    const procArc = new Stroke(ringPath, { color: COLOR.orange, width: 0.07, y: 1.372 });
    const warnRing = new Stroke(ringPath, { color: COLOR.warning, width: 0.07, y: 1.371 });
    warnRing.drawn = 1;
    station.add(verifyRing, procArc, warnRing);
    // Fingerprint seal: engraved ridges, presentation only.
    const fpSeal = new THREE.Group();
    fpSeal.position.set(-4.6, 0, 3.0);
    fpSeal.add(mesh(new THREE.CylinderGeometry(1.0, 1.05, 0.34, 48), [ceramicEdge, ceramic, ceramic]).translateY(0.17));
    const ridges = [];
    for (let k = 0; k < 6; k++) {
      const a = 0.16 + k * 0.11;
      const top = 0.22 + k * 0.12;
      const foot = 0.14 + k * 0.09;
      const kk = 0.62 * a;
      const s = new Stroke(new Path().M(-a, foot).L(-a, 0).C(-a, -top * 0.7, -kk, -top, 0, -top).C(kk, -top, a, -top * 0.7, a, 0).L(a, foot * (k % 2 ? 0.6 : 1)).build({ step: 0.03 }), { color: COLOR.warmGray, width: 0.03, y: 0.35 });
      fpSeal.add(s);
      ridges.push(s);
    }
    station.add(fpSeal);
    // Identity tray with the parcel; the card that is filed after a confirmed enrollment.
    const tray = box(4.0, 0.1, 2.9, champagne, 5.6, 0, 2.6);
    station.add(tray);
    parcel.position.set(5.6, 0.1, 2.6);
    const card = new THREE.Group();
    const cardSheet = mesh(sheetGeometry(roundedRect(-0.9, -0.58, 0.9, 0.58, 0.1), 0.04), [paper, paper, ceramicEdge]);
    const cardLine = new Stroke(new Path().M(-0.65, 0.3).L(0.65, 0.3).build({ step: 0.05 }), { color: COLOR.orangeInk, width: 0.08, y: 0.045 });
    cardLine.drawn = 1;
    const portrait = new Stroke(circlePath(-0.4, -0.12, 0.22), { color: COLOR.ink, width: 0.04, y: 0.045 });
    portrait.drawn = 1;
    card.add(cardSheet, cardLine, portrait);
    card.visible = false;
    scene.add(card);
    // Waiting platform for the pending application (R10).
    const pendingPad = new Stroke(circlePath(5.6, 2.6, 2.6), { color: COLOR.champagne, width: 0.05, y: 0.12, dash: 0.25, gap: 0.2 });
    scene.add(pendingPad);
    // Mobile module: a small phone in front of the station, linked to the identity package.
    const PH = { x: -1.1, z: 5.7, w: 1.7, d: 3.1 };
    const phone = new THREE.Group();
    phone.position.set(PH.x, 0, PH.z);
    scene.add(phone);
    const phoneBody = mesh(sheetGeometry(roundedRect(-PH.w / 2, -PH.d / 2, PH.w / 2, PH.d / 2, 0.3), 0.2),
      [new THREE.MeshStandardMaterial({ color: '#3a332d', roughness: 0.45, metalness: 0.3 }), ceramicEdge, champagne]);
    phone.add(phoneBody);
    const screenMat = new THREE.MeshStandardMaterial({ color: '#1f1b18', roughness: 0.1, emissive: new THREE.Color(COLOR.orange), emissiveIntensity: 0 });
    phone.add(mesh(sheetGeometry(roundedRect(-PH.w / 2 + 0.12, -PH.d / 2 + 0.2, PH.w / 2 - 0.12, PH.d / 2 - 0.2, 0.2), 0.01, { y: 0.2 }), screenMat, false));
    const phoneRidges = [];
    for (let k = 0; k < 5; k++) {
      const a = 0.1 + k * 0.08;
      const top = 0.14 + k * 0.09;
      const foot = 0.1 + k * 0.06;
      const kk = 0.62 * a;
      const s = new Stroke(new Path().M(-a, foot).L(-a, 0).C(-a, -top * 0.7, -kk, -top, 0, -top).C(kk, -top, a, -top * 0.7, a, 0).L(a, foot).build({ step: 0.03 }), { color: COLOR.orange, width: 0.03, y: 0.215 });
      phone.add(s);
      phoneRidges.push(s);
    }
    const linkPath = new Path().M(PH.x + PH.w / 2, PH.z).L(5.6, PH.z, 0.6).L(5.6, 2.6 + 1.25).build({ step: 0.04 });
    const link = new Stroke(linkPath, { color: COLOR.orangeInk, width: LINE.main, y: 0.015 });
    scene.add(link);
    const pulseDot = new PenDot({ color: COLOR.orange, radius: 0.22 });
    scene.add(pulseDot);
    const phoneMaterials = [ceramicEdge];
    const linkOk = new THREE.Color(COLOR.orangeInk);
    const linkWarn = new THREE.Color(COLOR.warning);

    groups.push([station, [ceramic, ceramicEdge, bladeMat]], [parcel, [kraft, kraftIn]]);

    const cardFrom = new THREE.Vector3(0, 1.6, 0);
    const cardTo = new THREE.Vector3(5.6, 1.4, 2.6);
    apply = () => {
      for (const [g, mats] of groups) develop(g, mats, ground, rig.dev);
      const r = OPEN * clamp01(rig.iris);
      const alpha = Math.asin(Math.min(0.999, r / P));
      for (const { pin, phi } of blades) {
        const nx = -Math.cos(phi);
        const nz = -Math.sin(phi);
        const dx = nx * Math.cos(alpha) - nz * Math.sin(alpha);
        const dz = nx * Math.sin(alpha) + nz * Math.cos(alpha);
        pin.rotation.y = Math.atan2(-dz, dx);
      }
      verifyRing.drawn = rig.ring;
      const arc = ringPath.total * 0.16;
      procArc.range = [rig.procPhase * ringPath.total, rig.procPhase * ringPath.total + arc];
      procArc.opacity = rig.proc;
      warnRing.opacity = rig.warn;
      ridges.forEach((s, i) => (s.drawn = clamp01(rig.fp * 1.6 - i * 0.1)));
      // Card: rises out of the closed iris, glides into the package, the lid closes over it.
      const c = rig.card;
      card.visible = c > 0.01 && c < 0.995;
      card.position.lerpVectors(cardFrom, cardTo, smooth(c));
      card.position.y += Math.sin(c * Math.PI) * 1.4;
      card.rotation.y = lerp(0, -0.35, c);
      lidHinge.rotation.x = -lerp(1.9, 0, smooth(rig.lid));
      pendingPad.drawn = rig.pending;
      // Mobile module and its link to the package.
      develop(phone, phoneMaterials, ground, rig.phone);
      phone.visible = rig.phone > 0.002;
      screenMat.emissiveIntensity = 0.25 * rig.glow;
      phoneRidges.forEach((s, i) => (s.drawn = clamp01(rig.link * 1.6 - i * 0.12)));
      link.drawn = rig.link;
      link.material.uniforms.uColor.value.copy(linkOk).lerp(linkWarn, rig.pwarn);
      const lp = linkPath.pointAt(clamp01(rig.pulseT) * linkPath.total);
      pulseDot.position.set(lp[0], 0.06, lp[1]);
      pulseDot.opacity = rig.pulse;
      pulseDot.ring(rig.wait, rig.wait > 0 ? 0.6 : 0);
      // Live camera in the aperture.
      if (cameraEl && shell.dataset.optics === '1') {
        stage.rig.apply();
        const a = project(r);
        cameraEl.style.setProperty('--cx', `${a.cx.toFixed(1)}px`);
        cameraEl.style.setProperty('--cy', `${a.cy.toFixed(1)}px`);
        cameraEl.style.setProperty('--size', `${Math.ceil(a.cover * 2 + 4)}px`);
        cameraEl.style.setProperty('--r', `${(a.r * 1.02).toFixed(1)}px`);
      }
    };
    const cameraEl = document.getElementById('facePanel');
    const _v = new THREE.Vector3();
    const _w = new THREE.Vector3();
    const project = r => {
      const cam = stage.rig.camera;
      _v.set(ST.x, 1.03, ST.z).project(cam);
      _w.set(ST.x + OPEN, 1.03, ST.z).project(cam);
      const cx = (_v.x * 0.5 + 0.5) * stage.width;
      const cy = (-_v.y * 0.5 + 0.5) * stage.height;
      const cover = Math.hypot((_w.x * 0.5 + 0.5) * stage.width - cx, (-_w.y * 0.5 + 0.5) * stage.height - cy);
      return { cx, cy, cover, r: cover * (r / OPEN) };
    };

    const SHOTS = {
      wide: { tx: 1.4, ty: 0.6, tz: 0.8, w: 19, h: 14, tilt: 40, yaw: -22, fov: 28 },
      // Straight down the aperture: stable, camera-facing, nothing moves while the picture is live.
      macro: { tx: 0, ty: 1.0, tz: 0, w: 8.0, h: 8.0, tilt: 0, yaw: 0, fov: 18 }
    };
    let shot = 'wide';
    const aim = name => {
      if (name === shot) return;
      shot = name;
      gsap.killTweensOf(stage.rig.state);
      if (reduced) { Object.assign(stage.rig.state, SHOTS[name]); stage.invalidate(); return; }
      gsap.to(stage.rig.state, { ...SHOTS[name], duration: 1.25, ease: 'power3.inOut', onUpdate: invalidate });
    };
    Object.assign(stage.rig.state, SHOTS.wide);

    const faceDone = shell.dataset.faceDone === 'true';
    const passkeyDone = shell.dataset.passkeyDone === 'true';
    if (passkeyDone) {
      to({ phone: 1 }, { duration: 1.0, delay: 0.4, ease: 'power2.out' });
      to({ link: 1 }, { duration: 1.2, delay: 1.0, ease: 'power1.inOut' });
    }
    to({ dev: 1 }, { duration: 1.0, ease: 'power2.out' });
    to({ fp: 1 }, { duration: 1.6, delay: 0.9, ease: 'power1.inOut' });
    if (faceDone || passkeyDone) {
      rig.lid = 1;
      to({ ring: 1 }, { duration: 0.9, delay: 0.6 });
      to({ pending: 1 }, { duration: 1.4, delay: 1.0, ease: 'power1.inOut' });
      say(shell.dataset.approved === 'true' ? 'Identity enrolled and company approved.' : 'Identity enrolled. The application waits on its platform for administrator approval.');
    } else {
      rig.lid = 0;
      say('The Identity Station is ready. The camera opens only after you start face enrollment.');
    }

    let waitLoop = null;
    const stopWait = () => { waitLoop?.kill(); waitLoop = null; rig.wait = 0; };
    // The browser's own WebAuthn prompt (this device, or a phone via its QR code) is open while 'requesting'.
    function passkeyState(state) {
      switch (state) {
        case 'requesting':
          stopWait();
          aim('wide');
          to({ pwarn: 0, phone: 1 }, { duration: 0.9, ease: 'power2.out' });
          to({ link: 1 }, { duration: 1.1, delay: 0.3, ease: 'power1.inOut' });
          to({ glow: 1, pulse: 1, pulseT: 0 }, { duration: 0.4, delay: 0.9, onComplete: () => {
            if (!reduced && !waitLoop) waitLoop = gsap.fromTo(rig, { wait: 0 }, { wait: 1, duration: 1.5, ease: 'power1.out', repeat: -1, repeatDelay: 0.25, onUpdate: invalidate });
          } });
          say('The mobile module is waiting for your device: confirm with its fingerprint, face or PIN, or choose a phone and scan the browser\'s QR code.');
          break;
        case 'confirmed':
          // Only after the server verified the registration: the pulse runs into the package, the lid closes.
          stopWait();
          to({ pulseT: 1 }, { duration: 1.0, ease: 'glide' });
          to({ glow: 0, pulse: 0 }, { duration: 0.4, delay: 1.0 });
          to({ lid: 1 }, { duration: 0.7, delay: 1.0, ease: 'boardSettle' });
          to({ ring: 1 }, { duration: 0.8, delay: 0.6 });
          say('Device passkey registered. Your identity package is sealed.');
          break;
        case 'failed':
        case 'cancelled':
          stopWait();
          to({ glow: 0, pulse: 0.4 }, { duration: 0.4 });
          to({ pwarn: state === 'failed' ? 1 : 0.5 }, { duration: 0.4 });
          say(state === 'failed' ? 'The passkey was not registered. The mobile module is ready for another try.' : 'The device prompt was closed. Try again when you are ready.');
          break;
        default:
          break;
      }
      stage.invalidate();
    }

    let procLoop = null;
    const stopProc = () => { procLoop?.kill(); procLoop = null; };
    shell.addEventListener('authstudio:face', event => {
      const { state, code, method } = event.detail || {};
      if (method === 'passkey') {
        passkeyState(state);
        return;
      }
      shell.dataset.faceState = state;
      const active = ['requesting', 'camera', 'capturing', 'processing'].includes(state);
      const actions = document.getElementById('faceActions');
      if (actions) actions.hidden = !active;
      switch (state) {
        case 'requesting':
          shell.dataset.optics = '1';
          stageEl.dataset.optics = '1';
          to({ warn: 0, ring: 0, proc: 0 }, { duration: 0.3 });
          aim('macro');
          say('The Identity Station turns toward you. Your camera opens once the browser allows it.');
          break;
        case 'camera':
          to({ iris: 1 }, { duration: 0.95, ease: 'power2.inOut' });
          say('The paper iris is open on your live camera.');
          break;
        case 'processing':
          to({ proc: 1 }, { duration: 0.3 });
          if (!reduced && !procLoop) procLoop = gsap.fromTo(rig, { procPhase: 0 }, { procPhase: 1, duration: 2.4, ease: 'none', repeat: -1, onUpdate: invalidate });
          break;
        case 'confirmed':
          // Only the server's confirmation files the identity card.
          stopProc();
          to({ proc: 0 }, { duration: 0.25 });
          to({ ring: 1 }, { duration: 0.7, ease: 'power2.out' });
          to({ iris: 0 }, { duration: 0.8, delay: 0.5, ease: 'paperSettle' });
          gsap.delayedCall(reduced ? 0 : 0.9, () => {
            shell.dataset.optics = '0';
            stageEl.dataset.optics = '0';
            aim('wide');
            to({ card: 1 }, { duration: 1.4, ease: 'glide' });
            to({ lid: 1 }, { duration: 0.7, delay: 1.2, ease: 'boardSettle' });
          });
          say('Face enrollment completed. Your identity card is filed in the package.');
          break;
        case 'failed': {
          stopProc();
          const camera = ['cameraDenied', 'cameraMissing', 'cameraUnavailable'].includes(code);
          to({ proc: 0, ring: 0 }, { duration: 0.3 });
          to({ iris: camera ? 0.08 : 0.3 }, { duration: 0.6 });
          to({ warn: 1 }, { duration: 0.4 });
          say(camera ? 'The camera is not available, so the iris stays closed.' : 'Enrollment did not complete. The station is ready for another try.');
          break;
        }
        case 'cancelled':
        case 'restart':
          stopProc();
          to({ iris: 0, proc: 0, warn: 0 }, { duration: 0.5 });
          shell.dataset.optics = '0';
          stageEl.dataset.optics = '0';
          aim('wide');
          break;
        default:
          break;
      }
      stage.invalidate();
    });
  }

  // The station scenes fill the stage; the tagline stays in the form copy here.
  stageEl.dataset.caption = '0';
  stage.updaters.add(() => apply());
  stage.setLightTarget(0, 0, 0);
  stageEl.dataset.ready = '1';
  stage.invalidate();
}

boot();

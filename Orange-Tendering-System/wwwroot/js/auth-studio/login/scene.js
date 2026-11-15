import * as THREE from 'three';
import { Stroke, PenDot } from '../core/stroke.js';
import { sheetMaterial, sheetGeometry, develop } from '../core/paper.js';
import { makePace } from '../core/paths.js';
import { metalMaterial } from '../core/metal.js';
import { COLOR, LINE } from '../core/tokens.js';
import * as L from './layout.js';
import { buildHardware, RIG_DEFAULTS } from './hardware.js';

// ORANGE SIGNAL scene graph. L00–L02 (the flat drawing) live here; L03–L10 (boundary, core,
// identity modules, optical iris, fiber) are built by ./hardware.js on the same objects.
// Built on the same stage, stroke, sheet and development code as Supplier Genesis: the card rises
// out of the page exactly the way the letter does — only the materials change (ceramic and
// champagne metal instead of paper and board).

const clamp01 = v => Math.max(0, Math.min(1, v));
const smooth = v => {
  const t = clamp01(v);
  return t * t * (3 - 2 * t);
};
const lerp = (a, b, t) => a + (b - a) * t;

export function createSignalScene(stage) {
  const root = new THREE.Group();
  root.name = 'orange-signal';
  stage.scene.add(root);
  const groundColor = new THREE.Color(COLOR.ground);

  const rig = {
    dot: 0, // Orange point presence
    draw: 0, // main line, normalised pen time
    tip: 0,
    dividers: 0, // contact pattern detail strokes
    devShell: 0, // card development (line → ceramic)
    inkShell: 1, // drawn card outline
    inkFrame: 1, // drawn contact-frame outline (settles to a hairline)
    inkDividers: 1,
    tracesA: 0, // circuit traces, group A (0..1, staggered)
    tracesB: 0, // group B
    signal: 0, // signal path toward the verification boundary 0..1
    gate: 0, // boundary ring
    terminals: 0, // terminal pads filled
    warn: 0, // unfinished-path accent
    pads: 0, // contact plates develop (0..1, staggered)
    ticks: 0, // registration ticks
    halo: 0,
    haloOn: 0,
    ...RIG_DEFAULTS
  };

  // ------------------------------------------------------------------------------------------
  // The continuous Orange line
  // ------------------------------------------------------------------------------------------
  const mainPath = L.buildMainLine();
  const marks = mainPath.marks;
  const line = new Stroke(mainPath, {
    color: COLOR.orangeInk,
    width: LINE.main,
    y: 0.012,
    zoneCuts: [marks.shellStart, marks.shellEnd, marks.frameStart]
  });
  line.name = 'orange-line';
  // The two zones that lie on the card are carried by strokes printed on the card itself.
  line.zone(L.ZONE.link, 0);
  line.zone(L.ZONE.frame, 0);
  root.add(line);

  // Plotter pacing: steady feed, firm slow-down into every corner.
  const pace = makePace(mainPath, {
    corner: 0.95,
    window: 0.55,
    floor: 0.2,
    stations: [
      { s: marks.shellEnd, depth: 0.55, width: 1.1 },
      { s: marks.frameStart, depth: 0.5, width: 0.8 }
    ]
  });

  const dot = new PenDot({ color: COLOR.orange, radius: LINE.dot });
  dot.name = 'orange-point';
  root.add(dot);

  // ------------------------------------------------------------------------------------------
  // SIM card: ceramic shell, recessed contact field, eight champagne plates
  // ------------------------------------------------------------------------------------------
  const card = new THREE.Group();
  card.name = 'sim';
  root.add(card);

  const ceramic = sheetMaterial(COLOR.ceramic, { roughness: 0.55, bump: 0.06, grain: false });
  const ceramicEdge = sheetMaterial(COLOR.ceramicEdge, { roughness: 0.6, bump: 0.06, grain: false });
  const field = sheetMaterial(COLOR.substrate, { roughness: 0.7, bump: 0.12, grain: false });
  const cardMaterials = [ceramic, ceramicEdge, field];

  const shell = new THREE.Mesh(sheetGeometry(L.shellOutline(), L.T_SHELL), [ceramic, ceramic, ceramicEdge]);
  shell.name = 'sim.shell';
  shell.castShadow = true;
  shell.receiveShadow = true;
  card.add(shell);

  const fieldMesh = new THREE.Mesh(sheetGeometry(L.chipOutline(0), 0.006, { y: L.T_SHELL }), field);
  fieldMesh.name = 'sim.contactField';
  fieldMesh.receiveShadow = true;
  card.add(fieldMesh);

  const fieldColor = new THREE.Color(COLOR.substrate);
  const champagne = new THREE.Color('#D6BA8E'); // reflectance: champagne under the studio ceiling
  const padY = L.T_SHELL + 0.006;
  const pads = L.padOutlines().map((p, i) => {
    // Reflections come from the authored studio (core/metal.js), used by metal only so paper
    // and ceramic keep their calibrated colours.
    const material = metalMaterial(stage.renderer, COLOR.champagne);
    const mesh = new THREE.Mesh(sheetGeometry(p.points, L.T_PAD, { y: 0 }), material);
    mesh.name = `sim.pad${i}`;
    mesh.position.y = padY;
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    card.add(mesh);
    // Plates settle in reading order from the signal row outward.
    const order = Math.abs(p.row - 1) + p.col * 0.45 + (p.col === 1 ? 0.2 : 0);
    // Separate plates never catch the light identically.
    const rough = 0.34 + ((i * 37) % 7) * 0.012;
    return { mesh, material, order, rough };
  });
  const maxOrder = Math.max(...pads.map(p => p.order));

  const topY = L.T_SHELL + 0.01;
  // Printed on the card: the link from the card edge to the frame, and the frame itself.
  // Exact end points are interpolated; interior samples are taken strictly between them (the
  // sampled arclengths are float32, so an equality test on the boundary would be unreliable).
  const subPath = (a, b) => {
    const pts = [...mainPath.pointAt(a)];
    for (let i = 0; i < mainPath.count; i++) {
      const s = mainPath.len[i];
      if (s > a + 1e-3 && s < b - 1e-3) pts.push(mainPath.pts[2 * i], mainPath.pts[2 * i + 1]);
    }
    pts.push(...mainPath.pointAt(b));
    return new mainPath.constructor(new Float32Array(pts));
  };
  const link = new Stroke(subPath(marks.shellEnd, marks.frameStart), { color: COLOR.orangeInk, width: LINE.main, y: topY });
  const frame = new Stroke(subPath(marks.frameStart, marks.end), { color: COLOR.orangeInk, width: LINE.main, y: topY });
  card.add(link, frame);

  const dividers = L.padDividers().map(p => {
    const s = new Stroke(p, { color: COLOR.ink, width: LINE.hair + 0.006, y: topY });
    card.add(s);
    return s;
  });

  const sig = L.signalPath();
  const signalInner = new Stroke(sig.inner, { color: COLOR.orangeInk, width: LINE.main, y: topY });
  card.add(signalInner);
  const signalOuter = new Stroke(sig.outer, { color: COLOR.orangeInk, width: LINE.main, y: 0.012 });
  root.add(signalOuter);
  const signalTotal = sig.inner.total + sig.outer.total;

  const gate = new Stroke(L.circlePath(L.GATE.x, L.GATE.z, L.GATE.r), { color: COLOR.ink, width: LINE.detail, y: 0.012 });
  root.add(gate);
  // An unfinished path ends in an open ring: something is still expected here.
  const openEnd = new Stroke(L.circlePath(0, 0, 0.3, 0.03), { color: COLOR.warning, width: 0.06, y: 0.012 });
  openEnd.drawn = 1;
  root.add(openEnd);

  // Circuit traces with their terminals.
  const discGeo = new THREE.CircleGeometry(L.TERMINAL.r * 0.5, 28);
  discGeo.rotateX(-Math.PI / 2);
  const discMat = new THREE.MeshBasicMaterial({ color: COLOR.ink, transparent: true, toneMapped: false, depthWrite: false });
  const traces = L.traces().map(t => {
    const stroke = new Stroke(t.path, { color: COLOR.ink, width: LINE.detail, y: 0.012 });
    const ringPath = L.circlePath(t.terminal[0], t.terminal[1], L.TERMINAL.r, 0.03);
    const ring = new Stroke(ringPath, { color: COLOR.ink, width: LINE.detail, y: 0.012 });
    const disc = new THREE.Mesh(discGeo, discMat.clone());
    disc.position.set(t.terminal[0], 0.014, t.terminal[1]);
    disc.renderOrder = 11;
    root.add(stroke, ring, disc);
    return { ...t, stroke, ring, disc };
  });
  const groupOf = g => traces.filter(t => t.group === g);
  const groupA = groupOf('a');
  const groupB = groupOf('b');

  const hw = buildHardware(stage, root, card, pads, sig);

  const ticks = L.cornerTicks().map(p => {
    const s = new Stroke(p, { color: COLOR.ink, width: LINE.hair + 0.004, y: 0.012 });
    root.add(s);
    return s;
  });

  // ------------------------------------------------------------------------------------------
  // rig → scene
  // ------------------------------------------------------------------------------------------
  const _c = new THREE.Color();
  function apply() {
    const drawing = rig.draw < 1;
    const s = rig.draw <= 0 ? 0 : rig.draw >= 1 ? mainPath.total : pace(rig.draw);
    line.range = [0, rig.draw <= 0 ? -1 : s];
    line.tip = rig.tip;
    line.zone(L.ZONE.shell, rig.inkShell);
    const onCard = s - marks.shellEnd;
    link.range = [0, rig.draw > 0 && onCard > 0 ? Math.min(link.total, onCard) : -1];
    frame.range = [0, s > marks.frameStart ? Math.min(frame.total, s - marks.frameStart) : -1];
    frame.opacity = rig.inkFrame;
    frame.width = lerp(LINE.hair, LINE.main, rig.inkFrame);

    dividers.forEach((d, i) => {
      d.drawn = clamp01(rig.dividers * 2.4 - i * 0.2);
      d.opacity = rig.inkDividers;
    });

    develop(card, cardMaterials, groundColor, rig.devShell);
    // Contact plates: they are the recessed field until they develop into raised metal.
    for (const p of pads) {
      const k = smooth((rig.pads * (maxOrder + 1.6) - p.order) / 1.6);
      _c.copy(rig.devShell < 1 ? groundColor : fieldColor);
      if (rig.devShell < 1) _c.lerp(fieldColor, smooth(rig.devShell));
      p.material.color.copy(_c).lerp(champagne, k);
      p.material.metalness = k;
      p.material.roughness = lerp(0.7, p.rough, k);
      p.material.bumpScale = 0.12 * k;
      p.material.envMapIntensity = k;
      p.mesh.scale.y = 0.05 + 0.95 * k;
      p.mesh.castShadow = k > 0.3 && rig.devShell > 0.5;
    }

    hw.apply(rig, gate);

    const stagger = (list, v) => {
      list.forEach((t, i) => {
        const k = clamp01(v * (1 + 0.28 * (list.length - 1)) - i * 0.28);
        const run = clamp01(k / 0.8);
        t.stroke.drawn = run;
        t.ring.drawn = clamp01((k - 0.72) / 0.28);
        t.disc.material.opacity = rig.terminals;
        t.disc.scale.setScalar(0.4 + 0.6 * smooth(rig.terminals));
        t.disc.visible = rig.terminals > 0.01 && k >= 1;
      });
    };
    stagger(groupA, rig.tracesA);
    stagger(groupB, rig.tracesB);

    // Signal path: across the card, then out along the ground to the boundary.
    const run = rig.signal * signalTotal;
    signalInner.range = [0, run > 0 ? Math.min(sig.inner.total, run) : -1];
    signalOuter.range = [0, run > sig.inner.total ? run - sig.inner.total : -1];
    gate.drawn = rig.gate;
    const tipX = L.SIM.x1 + Math.max(0, run - sig.inner.total);
    openEnd.position.set(tipX + 0.34, 0, L.SIM.row);
    openEnd.opacity = rig.warn * (run > sig.inner.total + 0.2 ? 1 : 0);

    ticks.forEach((t, i) => {
      t.drawn = clamp01(rig.ticks * 1.6 - i * 0.3);
    });

    // The Orange point: pen while drawing, then it rests on the contact frame.
    const pt = mainPath.pointAt(drawing ? s : mainPath.total);
    const y = drawing ? 0.03 : topY * card.scale.y + 0.03;
    dot.opacity = rig.dot;
    dot.position.set(pt[0], y, pt[1]);
    dot.scale.setScalar(Math.max(0.001, rig.dot));
    dot.ring(rig.halo, rig.haloOn * 0.5);
    stage.invalidate();
  }

  const state = () => ({
    rig: { ...rig },
    lineLength: +mainPath.total.toFixed(2),
    pads: pads.length,
    traces: traces.length
  });

  apply();

  return {
    root,
    rig,
    apply,
    state,
    hinges: [],
    mainPath,
    marks,
    nameLine: null,
    hw,
    parts: { line, dot, card, shell, pads, traces, gate },
    svg: { main: mainPath.svg }
  };
}

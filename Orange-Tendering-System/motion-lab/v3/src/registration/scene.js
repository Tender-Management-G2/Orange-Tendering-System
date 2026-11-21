import * as THREE from 'three';
import { Stroke, PenDot } from '../core/stroke.js';
import { sheetMaterial, sheetGeometry, hingedSheet, attach, develop } from '../core/paper.js';
import { makePace, Path, roundedRect } from '../core/paths.js';
import { Label, drawLine, WrittenLine, initialsOf } from '../core/text.js';
import { COLOR, LINE } from '../core/tokens.js';
import * as L from './layout.js';

// SUPPLIER GENESIS scene graph for R00–R02.
// Everything animates through `rig`, a flat set of named parameters (angles in degrees, progress
// 0..1). Timelines only ever write the rig; apply() is the single place that turns the rig into
// transforms, so any timeline position — forwards, backwards or scrubbed — renders the same pose.

const DEG = Math.PI / 180;
const clamp01 = v => Math.max(0, Math.min(1, v));
const smooth = v => {
  const t = clamp01(v);
  return t * t * (3 - 2 * t);
};
const lerp = (a, b, t) => a + (b - a) * t;
const backOut = (t, s = 1.7) => {
  const u = clamp01(t) - 1;
  return 1 + (s + 1) * u * u * u + s * u * u;
};

export function createGenesisScene(stage) {
  const root = new THREE.Group();
  root.name = 'supplier-genesis';
  stage.scene.add(root);
  const groundColor = new THREE.Color(COLOR.ground);
  const hinges = [];

  const rig = {
    dot: 0, // Orange point presence
    draw: 0, // main line, normalised pen time
    tip: 0, // wet-ink swell behind the pen
    seams: 0, // letter seam strokes
    scores: 0, // carton score lines (staggered)
    devEnv: 0, // letter development (line → paper)
    devBox: 0, // carton development (line → board)
    inkEnv: 1, // opacity of the drawn letter outline
    seamInk: 1, // seam pencil lines soften once real paper edges carry them
    inkBox: 1, // opacity of the drawn carton outline
    flap: 0, // letter flap, degrees from closed
    cardOut: 0, // card emergence 0..1
    cardInk: 0, // card artwork (ring, rule, tab, kicker)
    mail: 0, // contact glyph 0..1
    mailWarn: 0, // unfinished-glyph accent
    groove: 0, // score lines pressed into grooves
    wL: 0, wR: 0, wF: 0, wB: 0, // wall angles, degrees
    bF: 0, bB: 0, // half-lid angles relative to their wall, degrees (+ = closing)
    fly: 0, // card transfer letter → carton
    wafer: 0, // 0 = flat pen dot, 1 = physical seal wafer
    waferLit: 0, // wafer shading: 0 flat ink, 1 lit paper
    hop: 0, // wafer height above the lid rim
    seal: 0, // seal bloom 0..1
    press: 0, // stamp squash 0..1
    stamp: 0, // seal impression ring
    halo: 0, // idle halo phase 0..1
    haloOn: 0
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
    zoneCuts: [marks.letterStart, marks.letterEnd, marks.park]
  });
  line.name = 'orange-line';
  root.add(line);

  const tether = new Stroke(L.buildTether(), { color: COLOR.orangeInk, width: LINE.main, y: 0.012 });
  tether.name = 'orange-line.tether';
  tether.drawn = 1;
  root.add(tether);

  const pace = makePace(mainPath, {
    corner: 0.5,
    window: 0.8,
    stations: [
      { s: marks.letterEnd, depth: 0.5, width: 2.4 }, // the letter is complete: a breath
      { s: marks.park, depth: 0.62, width: 1.5 } // arrive at the carton before tracing it
    ]
  });

  const dot = new PenDot({ color: COLOR.orange, radius: LINE.dot });
  dot.name = 'orange-point';
  root.add(dot);

  // ------------------------------------------------------------------------------------------
  // Letter
  // ------------------------------------------------------------------------------------------
  const E = L.ENV;
  const out = L.letterOutlines();
  const envMaterials = [];
  const envMat = (color, opts) => {
    const m = sheetMaterial(color, opts);
    envMaterials.push(m);
    return m;
  };
  const letter = new THREE.Group();
  letter.name = 'letter';
  root.add(letter);

  const paperTone = envMat(COLOR.envelope);
  const paperTone2 = envMat('#FBF7F0'); // layered flaps are a hair warmer so seams read
  const linerTone = envMat(COLOR.liner, { bump: 0.3, roughness: 0.88 });

  const mesh = (name, pts, t, material, y) => {
    const m = new THREE.Mesh(sheetGeometry(pts, t, { y }), material);
    m.name = name;
    m.castShadow = true;
    m.receiveShadow = true;
    return m;
  };
  letter.add(mesh('letter.back', out.back, L.T_PAPER, paperTone, L.ENV_Y.back));
  letter.add(mesh('letter.liner', out.liner, 0.006, linerTone, L.ENV_Y.liner));
  letter.add(mesh('letter.sides', out.sides, L.T_PAPER, paperTone, L.ENV_Y.sides));
  letter.add(mesh('letter.bottom', out.bottom, L.T_PAPER, paperTone2, L.ENV_Y.bottom));

  const flapPivot = [E.cx, L.ENV_Y.flap, E.z0];
  const flap = hingedSheet('letter.flap', out.flap, L.T_PAPER, paperTone, { pivot: flapPivot, y: L.ENV_Y.flap });
  const flapLiner = new THREE.Mesh(sheetGeometry(out.flapLiner, 0.006, { y: L.ENV_Y.flap - 0.006 }), linerTone);
  flapLiner.geometry.translate(-flapPivot[0], -flapPivot[1], -flapPivot[2]);
  flapLiner.castShadow = false;
  flapLiner.receiveShadow = true;
  flap.add(flapLiner);
  letter.add(flap);
  hinges.push({ name: 'letter.flap', group: flap, axis: 'x', half: E.w / 2 });

  const seamData = L.letterSeams();
  const seamY = L.ENV_Y.bottom + L.T_PAPER + 0.004;
  const seams = [seamData.left, seamData.right].map(p => {
    const s = new Stroke(p, { color: COLOR.ink, width: LINE.detail, y: seamY });
    letter.add(s);
    return s;
  });

  // ------------------------------------------------------------------------------------------
  // Identity card
  // ------------------------------------------------------------------------------------------
  const card = new THREE.Group();
  card.name = 'identity-card';
  root.add(card);
  const cardMaterial = sheetMaterial(COLOR.card, { bump: 0.22, roughness: 0.9 });
  const cardSheet = new THREE.Mesh(sheetGeometry(L.cardOutline(), L.T_CARD), [cardMaterial, cardMaterial, sheetMaterial('#EDE6DC', { grain: false })]);
  cardSheet.castShadow = true;
  cardSheet.receiveShadow = true;
  card.add(cardSheet);

  const art = L.cardArtwork();
  const inkY = L.T_CARD + 0.004;
  const ring = new Stroke(art.ringPath, { color: COLOR.orangeInk, width: 0.06, y: inkY });
  const rule = new Stroke(art.rule, { color: '#CFC4B6', width: 0.034, y: inkY });
  const mail = new Stroke(art.mail, { color: COLOR.ink, width: 0.044, y: inkY });
  const mailDots = new Stroke(art.dots, { color: '#B4A99C', width: 0.09, y: inkY, dash: 0.09, gap: 0.2, dots: true });
  card.add(ring, rule, mail, mailDots);

  // Orange tab: a small strip of paper, grows from its left end.
  const tabGeo = sheetGeometry(roundedRect(0, -art.pill.h / 2, art.pill.w, art.pill.h / 2, art.pill.h / 2 - 0.01), 0.008);
  const tab = new THREE.Mesh(tabGeo, sheetMaterial(COLOR.orange, { grain: false, bump: 0.2 }));
  tab.position.set(art.pill.x - art.pill.w / 2, L.T_CARD, art.pill.z);
  tab.receiveShadow = true;
  card.add(tab);

  const kicker = new Label(art.kicker.w, art.kicker.h, { ppu: 170 });
  kicker.position.set(art.kicker.x, inkY, art.kicker.z);
  const nameLabel = new Label(art.name.w, art.name.h, { ppu: 170 });
  nameLabel.position.set(art.name.x, inkY, art.name.z);
  const initials = new Label(art.initials.size, art.initials.size, { ppu: 150, align: 'center' });
  initials.position.set(art.initials.x, inkY, art.initials.z);
  card.add(kicker, nameLabel, initials);
  const nameLine = new WrittenLine(nameLabel, { weight: 650, size: 0.6, color: COLOR.graphite, onChange: () => stage.invalidate() });
  let initialsText = '';
  const setName = (text, { instant = false, amount = 1 } = {}) => {
    const clean = (text ?? '').replace(/\s+/g, ' ').trim().slice(0, 60);
    if (instant) nameLine.setInstant(clean, amount);
    else nameLine.set(clean);
    const ini = amount > 0.05 ? initialsOf(clean) : '';
    if (ini !== initialsText) {
      initialsText = ini;
      drawLine(initials, ini, { weight: 600, size: 0.42, color: COLOR.orangeInk, tracking: 0.04 });
    }
    stage.invalidate();
  };
  const paintStatic = () => {
    drawLine(kicker, 'SUPPLIER CONTACT', { weight: 600, size: 0.62, color: '#9A8F84', tracking: 0.16 });
    drawLine(initials, initialsText, { weight: 600, size: 0.42, color: COLOR.orangeInk, tracking: 0.04 });
    nameLine.draw();
  };
  paintStatic();

  // Card flight path: slide clear of the pocket, lift, glide, lower into the open carton.
  const B = L.BOX;
  const flight = new THREE.CatmullRomCurve3(
    [
      new THREE.Vector3(L.CARD.out.x, L.ENV_Y.card, L.CARD.out.z),
      new THREE.Vector3(L.CARD.out.x, L.ENV_Y.card + 0.03, E.z0 - L.CARD.h / 2 - 0.25),
      new THREE.Vector3(L.CARD.out.x + 1.3, 1.35, E.z0 - L.CARD.h / 2 + 0.1),
      new THREE.Vector3(-1.4, 3.5, -10.4),
      new THREE.Vector3(4.6, 3.6, -1.6),
      new THREE.Vector3(B.cx, 2.75, B.cz - 0.2),
      new THREE.Vector3(B.cx, L.T_BOARD + 0.006, B.cz)
    ],
    false,
    'centripetal'
  );
  const _p = new THREE.Vector3();

  // ------------------------------------------------------------------------------------------
  // Carton
  // ------------------------------------------------------------------------------------------
  const co = L.cartonOutlines();
  const inner = sheetMaterial(COLOR.kraftInner, { bump: 0.8, roughness: 0.95 });
  const outer = sheetMaterial(COLOR.kraftOuter, { bump: 0.8, roughness: 0.95 });
  const edge = sheetMaterial(COLOR.kraftEdge, { bump: 0.4, grain: false });
  const boardMaterials = [inner, outer, edge];
  const carton = new THREE.Group();
  carton.name = 'carton';
  root.add(carton);

  const T = L.T_BOARD;
  const base = hingedSheet('box.base', co.base, T, boardMaterials, { pivot: [0, 0, 0] });
  const left = hingedSheet('box.left', co.left, T, boardMaterials, { pivot: [B.x0, T, B.cz] });
  const right = hingedSheet('box.right', co.right, T, boardMaterials, { pivot: [B.x1, T, B.cz] });
  const back = hingedSheet('box.back', co.back, T, boardMaterials, { pivot: [B.cx, T, B.z0] });
  const front = hingedSheet('box.front', co.front, T, boardMaterials, { pivot: [B.cx, T, B.z1] });
  const backLid = hingedSheet('box.backLid', co.backLid, T, boardMaterials, { pivot: [B.cx, T, B.backHinge] });
  const frontLid = hingedSheet('box.frontLid', co.frontLid, T, boardMaterials, { pivot: [B.cx, T, B.frontHinge] });
  carton.add(base, left, right, back, front);
  attach(back, backLid);
  attach(front, frontLid);
  hinges.push(
    { name: 'box.left', group: left, axis: 'z', half: B.d / 2 },
    { name: 'box.right', group: right, axis: 'z', half: B.d / 2 },
    { name: 'box.back', group: back, axis: 'x', half: B.w / 2 },
    { name: 'box.front', group: front, axis: 'x', half: B.w / 2 },
    { name: 'box.backLid', group: backLid, axis: 'x', half: B.w / 2 },
    { name: 'box.frontLid', group: frontLid, axis: 'x', half: B.w / 2 }
  );

  const parents = { base, back, front };
  const scores = L.cartonCreases().map(({ parent, path }) => {
    const g = parents[parent];
    const pv = g.userData.pivot;
    const dashed = new Stroke(path, { color: COLOR.score, width: LINE.detail, y: T + 0.004, dash: 0.3, gap: 0.2 });
    const solid = new Stroke(path, { color: '#A8947B', width: LINE.hair, y: T + 0.004 });
    for (const s of [dashed, solid]) {
      s.position.set(-pv[0], -pv[1], -pv[2]);
      g.add(s);
    }
    solid.drawn = 1;
    return { dashed, solid };
  });

  // The seal: the Orange point itself, grown into a paper wafer that bridges the two half-lids.
  const wafer = new THREE.Mesh(
    new THREE.CylinderGeometry(1, 1, 1, 56),
    new THREE.MeshStandardMaterial({ color: COLOR.orange, roughness: 0.82, metalness: 0, emissive: new THREE.Color(COLOR.orange), emissiveIntensity: 1 })
  );
  wafer.name = 'seal';
  wafer.material.shadowSide = THREE.FrontSide;
  wafer.receiveShadow = true;
  root.add(wafer);
  const stampPath = (() => {
    const r = 0.47;
    const c = 0.5523 * r;
    return new Path()
      .M(0, -r)
      .C(c, -r, r, -c, r, 0)
      .C(r, c, c, r, 0, r)
      .C(-c, r, -r, c, -r, 0)
      .C(-r, -c, -c, -r, 0, -r)
      .build({ step: 0.04 });
  })();
  const stamp = new Stroke(stampPath, { color: '#D85B00', width: 0.036, y: 0 });
  stamp.renderOrder = 14;
  root.add(stamp);

  const lidLength = B.lid - B.lidGap;
  const rimLocal = new THREE.Vector3(0, -T / 2, -lidLength);
  const rim = new THREE.Vector3();
  const SEAL_R = 0.74;
  const orange = new THREE.Color(COLOR.orange);
  const black = new THREE.Color(0, 0, 0);

  // ------------------------------------------------------------------------------------------
  // rig → scene
  // ------------------------------------------------------------------------------------------
  function apply() {
    // Line art
    const drawing = rig.draw < 1;
    const s = rig.draw <= 0 ? 0 : rig.draw >= 1 ? mainPath.total : pace(rig.draw);
    line.range = [0, rig.draw <= 0 ? -1 : s];
    line.tip = rig.tip;
    line.zone(L.ZONE.letter, rig.inkEnv);
    line.zone(L.ZONE.carton, rig.inkBox);

    seams.forEach((st, i) => {
      st.drawn = clamp01(rig.seams * 1.25 - i * 0.25);
      st.opacity = rig.seamInk;
    });
    scores.forEach(({ dashed, solid }, i) => {
      const k = clamp01(rig.scores * 2.2 - i * 0.24);
      dashed.drawn = k;
      dashed.opacity = 1 - rig.groove;
      solid.opacity = rig.groove * 0.9;
    });

    // The tether exists only once board covers it; it is revealed by the lid lifting, not by a fade.
    tether.opacity = rig.devBox >= 0.999 ? 1 : 0;

    // Development: line → paper
    develop(letter, envMaterials, groundColor, rig.devEnv);
    develop(carton, boardMaterials, groundColor, rig.devBox);

    // Letter flap. The fold is the back sheet's top edge, so the hinge sinks from the top of the
    // closed stack to sheet level as it opens.
    flap.position.y = lerp(L.ENV_Y.flap, 0.012, smooth(rig.flap / 110));
    flap.rotation.x = -rig.flap * DEG;

    // Card
    card.visible = rig.devEnv > 0.985;
    const fly = clamp01(rig.fly);
    if (fly <= 0) {
      card.position.set(L.CARD.inside.x, L.ENV_Y.card, lerp(L.CARD.inside.z, L.CARD.out.z, rig.cardOut));
      card.rotation.set(0, 0, 0);
    } else {
      flight.getPoint(fly, _p);
      card.position.copy(_p);
      const arc = Math.sin(Math.PI * clamp01((fly - 0.12) / 0.8));
      card.rotation.set(-0.07 * arc, -0.085 * Math.sin(Math.PI * 2 * fly) * arc, 0.11 * arc);
    }
    const ink = rig.cardInk;
    ring.drawn = clamp01(ink * 1.5);
    rule.drawn = clamp01(ink * 1.5 - 0.35);
    tab.scale.x = Math.max(0.001, smooth(ink * 1.6 - 0.55));
    tab.visible = ink > 0.35;
    kicker.opacity = smooth(ink * 1.7 - 0.5);
    initials.opacity = smooth(ink * 2 - 0.9);
    // Contact glyph: the stroke completes at 0.62, then the redacted-address dots follow.
    mail.drawn = clamp01(rig.mail / 0.62);
    mail.setColor(rig.mailWarn > 0.5 ? COLOR.warning : COLOR.ink);
    mailDots.drawn = clamp01((rig.mail - 0.62) / 0.38);

    // Carton hinges
    left.rotation.z = -rig.wL * DEG;
    right.rotation.z = rig.wR * DEG;
    back.rotation.x = rig.wB * DEG;
    front.rotation.x = -rig.wF * DEG;
    backLid.rotation.x = rig.bB * DEG;
    frontLid.rotation.x = -rig.bF * DEG;

    // The Orange point: pen while drawing, then it rides the rim of the lid it will seal.
    let px, py, pz;
    if (drawing) {
      const pt = mainPath.pointAt(s);
      px = pt[0];
      pz = pt[1];
      py = 0.03;
    } else {
      root.updateMatrixWorld(true);
      rim.copy(rimLocal);
      backLid.localToWorld(rim);
      px = rim.x;
      pz = rim.z;
      py = rim.y + T / 2 * carton.scale.y + 0.02;
    }
    const physical = rig.wafer > 0.5;
    dot.opacity = physical ? 0 : rig.dot;
    dot.position.set(px, py + 0.01, pz);
    dot.scale.setScalar(Math.max(0.001, rig.dot));
    dot.ring(rig.halo, rig.haloOn * 0.5);

    wafer.visible = physical;
    stamp.visible = physical && rig.stamp > 0.002;
    if (physical) {
      const bloom = backOut(rig.seal, 1.4);
      const r = lerp(LINE.dot, SEAL_R, bloom) * (1 + 0.07 * rig.press);
      const h = lerp(0.03, 0.045, clamp01(rig.seal)) * (1 - 0.35 * rig.press);
      const y = py + rig.hop;
      wafer.scale.set(r, h, r);
      wafer.position.set(px, y + h / 2, pz);
      wafer.material.color.copy(black).lerp(orange, rig.waferLit);
      wafer.material.emissiveIntensity = 1 - rig.waferLit;
      wafer.castShadow = rig.waferLit > 0.3;
      stamp.position.set(px, y + h + 0.004, pz);
      stamp.drawn = rig.stamp;
    }
    stage.invalidate();
  }

  const state = () => ({
    rig: { ...rig },
    hinges: hinges.map(h => ({ name: h.name, rotation: [h.group.rotation.x, h.group.rotation.y, h.group.rotation.z].map(v => +(v / DEG).toFixed(2)) })),
    card: [card.position.x, card.position.y, card.position.z].map(v => +v.toFixed(3)),
    lineLength: +mainPath.total.toFixed(2)
  });

  apply();

  return {
    root,
    rig,
    apply,
    state,
    hinges,
    mainPath,
    marks,
    setName,
    repaintText: paintStatic,
    nameLine,
    parts: { line, dot, letter, flap, card, carton, base, left, right, back, front, backLid, frontLid, wafer },
    svg: {
      main: mainPath.svg,
      seams: [seamData.left.svg, seamData.right.svg],
      scores: L.cartonCreases().map(c => c.path.svg)
    }
  };
}

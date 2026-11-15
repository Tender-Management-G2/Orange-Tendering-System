import { Path, roundedRect } from '../core/paths.js';

// SUPPLIER GENESIS — single source of geometry for R00–R02.
// Plane: x = right, z = toward the viewer (down-screen), y = up. Units are arbitrary "cm".
// The line art, the paper outlines, the hinges and the camera frames are all derived from the
// constants below, so the drawing and the objects it becomes are the same shapes by construction.

export const T_PAPER = 0.028;
export const T_CARD = 0.045;
export const T_BOARD = 0.09;

// --- Letter ---------------------------------------------------------------------------------
export const ENV = { x0: -13.2, x1: -2.0, z0: -13.0, z1: -5.4 };
ENV.w = ENV.x1 - ENV.x0;
ENV.h = ENV.z1 - ENV.z0;
ENV.cx = (ENV.x0 + ENV.x1) / 2;
ENV.tipZ = ENV.z0 + 0.68 * ENV.h; // closed flap tip
ENV.throatZ = ENV.z0 + 0.47 * ENV.h; // where the side flaps meet
ENV.apexZ = ENV.z0 + 0.4 * ENV.h; // bottom flap apex (overlaps the side flaps)
ENV.flapLen = ENV.tipZ - ENV.z0;

// Layer heights of the closed letter (bottom to top).
export const ENV_Y = {
  back: 0,
  liner: T_PAPER,
  card: T_PAPER + 0.01,
  sides: T_PAPER + 0.01 + T_CARD + 0.008,
  bottom: T_PAPER + 0.01 + T_CARD + 0.008 + T_PAPER,
  flap: T_PAPER + 0.01 + T_CARD + 0.008 + 2 * T_PAPER + 0.014
};

// --- Identity card ----------------------------------------------------------------------------
export const CARD = { w: 8.6, h: 5.4, r: 0.34 };
CARD.inside = { x: ENV.cx, z: ENV.z0 + 0.55 + CARD.h / 2 }; // tucked in the pocket
CARD.out = { x: ENV.cx, z: ENV.z0 - 0.72 * CARD.h + CARD.h / 2 }; // 72% above the fold line

// --- Carton (gate-fold mailer: four walls, two half-lids that meet under the seal) -----------
export const BOX = { x0: 1.4, x1: 11.0, z0: -0.8, z1: 5.6, wall: 2.0, lid: 3.2, taper: 0.14 };
BOX.cx = (BOX.x0 + BOX.x1) / 2;
BOX.cz = (BOX.z0 + BOX.z1) / 2;
BOX.w = BOX.x1 - BOX.x0;
BOX.d = BOX.z1 - BOX.z0;
BOX.backHinge = BOX.z0 - BOX.wall; // back wall ↔ back lid
BOX.frontHinge = BOX.z1 + BOX.wall;
BOX.backEdge = BOX.backHinge - BOX.lid; // free edge of the back lid (where the pen parks)
BOX.frontEdge = BOX.frontHinge + BOX.lid;
BOX.lidGap = 0.015;

/** Where the Orange point parks: midpoint of the back lid's free edge. */
export const PARK = { x: BOX.cx, z: BOX.backEdge };

// --- The continuous Orange line ---------------------------------------------------------------
// One unbroken stroke: hook → letter (rectangle, then the flap's V makes it an envelope) →
// arch → carton dieline (closed circuit) → parks on the lid edge it will later seal.
export function buildMainLine() {
  const { x0, x1, z0, z1, cx, tipZ } = ENV;
  const b = BOX;
  const p = new Path();

  // Hook: a short hand-drawn entry that becomes tangent to the letter's left edge.
  p.M(x0 + 1.05, z0 - 3.35).C(x0 + 0.3, z0 - 3.0, x0, z0 - 2.3, x0, z0 - 1.3);
  p.L(x0, z0).mark('letterStart');

  // Rectangle, clockwise from the top-left corner going down.
  p.L(x0, z1, 0.22).L(x1, z1, 0.22).L(x1, z0, 0.12).L(x0, z0, 0.07);
  // Flap: down to the tip and back up to the top-right corner.
  p.L(cx, tipZ, 0.7).L(x1, z0).mark('letterEnd');

  // Arch: leaves along the flap edge's direction and lands vertically above the carton.
  const dx = x1 - cx;
  const dz = z0 - tipZ;
  const dl = Math.hypot(dx, dz);
  const k = 3.7;
  p.C(x1 + (dx / dl) * k, z0 + (dz / dl) * k, PARK.x, PARK.z - 8.1, PARK.x, PARK.z - 3.4);
  p.L(PARK.x, PARK.z).mark('park');

  // Carton dieline, clockwise from the lid edge midpoint.
  const t = b.taper;
  p.L(b.x1 - t, b.backEdge, 0.5)
    .L(b.x1 - 0.04, b.backHinge)
    .L(b.x1, b.z0, 0.1)
    .L(b.x1 + b.wall, b.z0 + 0.18, 0.4)
    .L(b.x1 + b.wall, b.z1 - 0.18, 0.4)
    .L(b.x1, b.z1, 0.1)
    .L(b.x1 - 0.04, b.frontHinge)
    .L(b.x1 - t, b.frontEdge, 0.5)
    .L(b.x0 + t, b.frontEdge, 0.5)
    .L(b.x0 + 0.04, b.frontHinge)
    .L(b.x0, b.z1, 0.1)
    .L(b.x0 - b.wall, b.z1 - 0.18, 0.4)
    .L(b.x0 - b.wall, b.z0 + 0.18, 0.4)
    .L(b.x0, b.z0, 0.1)
    .L(b.x0 + 0.04, b.backHinge)
    .L(b.x0 + t, b.backEdge, 0.5)
    .L(PARK.x, PARK.z)
    .mark('end');

  return p.build();
}

// Zones of the main line: 0 hook, 1 letter outline, 2 arch + stem, 3 carton outline.
export const ZONE = { hook: 0, letter: 1, arch: 2, carton: 3 };

// --- Paper outlines (closed loops) --------------------------------------------------------------
export function letterOutlines() {
  const { x0, x1, z0, z1, cx, tipZ, throatZ, apexZ } = ENV;
  const back = roundedRect(x0, z0, x1, z1, [0.1, 0.1, 0.22, 0.22]);

  // Liner visible through the throat (extends under the side flaps so no edge shows).
  const liner = [
    [x0 + 0.3, z0 + 0.04],
    [x1 - 0.3, z0 + 0.04],
    [x1 - 0.3, z0 + 1.0],
    [cx, throatZ + 0.9],
    [x0 + 0.3, z0 + 1.0]
  ];

  // Side flaps: everything except the throat triangle.
  const sides = new Path()
    .M(x0, z0)
    .L(cx, throatZ, 0.35)
    .L(x1, z0)
    .L(x1, z1, 0.22)
    .L(x0, z1, 0.22)
    .L(x0, z0)
    .build()
    .toPoints();
  sides.pop();

  // Bottom flap: its two upper edges are the visible seams.
  const bottom = new Path()
    .M(cx, z1 - 0.03)
    .L(x0 + 0.03, z1 - 0.03, 0.2)
    .L(cx, apexZ, 0.5)
    .L(x1 - 0.03, z1 - 0.03, 0.2)
    .L(cx, z1 - 0.03)
    .build()
    .toPoints();
  bottom.pop();

  // Top flap in its closed position (hinge on z0, tip toward the viewer).
  const flap = new Path().M(x0, z0).L(x1, z0).L(cx, tipZ, 0.7).L(x0, z0).build().toPoints();
  flap.pop();

  // Liner on the inside of the flap: the flap triangle inset by a constant paper margin.
  const m = 0.4;
  const side = Math.hypot(ENV.w / 2, ENV.flapLen);
  const half = ENV.w / 2 - m * (ENV.w / 2 / ENV.flapLen) - m * (side / ENV.flapLen); // half-width at z0 + m
  const linerTip = tipZ - m * (side / (ENV.w / 2));
  const flapLiner = new Path()
    .M(cx, z0 + m)
    .L(cx + half, z0 + m, 0.12)
    .L(cx, linerTip, 0.42)
    .L(cx - half, z0 + m, 0.12)
    .L(cx, z0 + m)
    .build()
    .toPoints();
  flapLiner.pop();

  return { back, liner, sides, bottom, flap, flapLiner };
}

/**
 * The line does not stop at the lid: it runs on beneath the flat carton to the base. Hidden under
 * the board until the back wall lifts, it is why the Orange line still reaches the finished parcel.
 */
export function buildTether() {
  return new Path().M(PARK.x, PARK.z).L(PARK.x, BOX.z0 - 0.16).build();
}

/** Seams of the bottom flap, drawn from each bottom corner up to the apex. */
export function letterSeams() {
  const { x0, x1, z1, cx, apexZ } = ENV;
  const left = new Path().M(x0 + 0.2, z1 - 0.17).L(cx - 0.36, apexZ + 0.3).build();
  const right = new Path().M(x1 - 0.2, z1 - 0.17).L(cx + 0.36, apexZ + 0.3).build();
  // Fraction of each seam that the closed flap leaves uncovered (where the seam meets the flap
  // edge). The live scene gets this from real occlusion; the static SVG export needs the number.
  const u = (ENV.z1 - ENV.z0) / ((ENV.tipZ - ENV.z0) / (ENV.w / 2) + (z1 - apexZ) / (ENV.w / 2));
  const visible = Math.min(1, (u - 0.2) / (ENV.w / 2 - 0.56));
  return { left, right, visible: +visible.toFixed(3) };
}

export function cardOutline() {
  return roundedRect(-CARD.w / 2, -CARD.h / 2, CARD.w / 2, CARD.h / 2, CARD.r);
}

/** Line details printed on the card (card-local coordinates, origin at the card centre). */
export function cardArtwork() {
  const ring = { x: -2.72, z: -0.62, r: 0.98 };
  const ringPath = new Path();
  // Circle as four cubic arcs, starting at the top so it draws clockwise like a pen stroke.
  const c = 0.5523 * ring.r;
  ringPath
    .M(ring.x, ring.z - ring.r)
    .C(ring.x + c, ring.z - ring.r, ring.x + ring.r, ring.z - c, ring.x + ring.r, ring.z)
    .C(ring.x + ring.r, ring.z + c, ring.x + c, ring.z + ring.r, ring.x, ring.z + ring.r)
    .C(ring.x - c, ring.z + ring.r, ring.x - ring.r, ring.z + c, ring.x - ring.r, ring.z)
    .C(ring.x - ring.r, ring.z - c, ring.x - c, ring.z - ring.r, ring.x, ring.z - ring.r);

  const ruleZ = -0.02;
  const ruleEnd = 2.36;
  const rule = new Path().M(-1.22, ruleZ).L(ruleEnd, ruleZ);

  // "A small line extends from the card and forms an envelope symbol": the name rule itself runs
  // on and, in one stroke, draws the glyph the same way the big letter was drawn — rectangle
  // first, then the V that makes it an envelope.
  const gw = 0.74;
  const gh = 0.5;
  const gx = 3.62 - gw;
  const gt = ruleZ - gh / 2;
  const mail = new Path()
    .M(ruleEnd, ruleZ)
    .L(gx, ruleZ)
    .L(gx, gt, 0.06)
    .L(gx + gw, gt, 0.06)
    .L(gx + gw, gt + gh, 0.06)
    .L(gx, gt + gh, 0.06)
    .L(gx, gt + 0.07)
    .L(gx + gw / 2, gt + gh * 0.58, 0.05)
    .L(gx + gw - 0.05, gt + 0.05);

  // The address itself is never drawn: a row of dots stands in for it.
  const dots = new Path().M(-1.22, 0.56).L(1.7, 0.56);

  return {
    ring,
    ringPath: ringPath.build({ step: 0.05 }),
    rule: rule.build(),
    mail: mail.build(),
    dots: dots.build(),
    pill: { x: 2.72, z: -1.98, w: 1.8, h: 0.3 },
    kicker: { x: -1.22, z: -1.42, w: 4.4, h: 0.34 },
    name: { x: -1.26, z: -0.92, w: 5.0, h: 0.86 },
    initials: { x: ring.x, z: ring.z, size: 1.5 }
  };
}

export function cartonOutlines() {
  const b = BOX;
  const t = b.taper;
  const g = b.lidGap;
  const base = [
    [b.x0, b.z0],
    [b.x1, b.z0],
    [b.x1, b.z1],
    [b.x0, b.z1]
  ];
  const back = [
    [b.x0, b.z0],
    [b.x0 + 0.04, b.backHinge],
    [b.x1 - 0.04, b.backHinge],
    [b.x1, b.z0]
  ];
  const front = [
    [b.x0, b.z1],
    [b.x1, b.z1],
    [b.x1 - 0.04, b.frontHinge],
    [b.x0 + 0.04, b.frontHinge]
  ];
  const left = new Path()
    .M(b.x0, b.z0)
    .L(b.x0 - b.wall, b.z0 + 0.18, 0.4)
    .L(b.x0 - b.wall, b.z1 - 0.18, 0.4)
    .L(b.x0, b.z1)
    .L(b.x0, b.z0)
    .build()
    .toPoints();
  left.pop();
  const right = new Path()
    .M(b.x1, b.z0)
    .L(b.x1, b.z1)
    .L(b.x1 + b.wall, b.z1 - 0.18, 0.4)
    .L(b.x1 + b.wall, b.z0 + 0.18, 0.4)
    .L(b.x1, b.z0)
    .build()
    .toPoints();
  right.pop();
  const backLid = new Path()
    .M(b.x0 + 0.04, b.backHinge)
    .L(b.x0 + t, b.backEdge + g, 0.5)
    .L(b.x1 - t, b.backEdge + g, 0.5)
    .L(b.x1 - 0.04, b.backHinge)
    .L(b.x0 + 0.04, b.backHinge)
    .build()
    .toPoints();
  backLid.pop();
  const frontLid = new Path()
    .M(b.x0 + 0.04, b.frontHinge)
    .L(b.x1 - 0.04, b.frontHinge)
    .L(b.x1 - t, b.frontEdge - g, 0.5)
    .L(b.x0 + t, b.frontEdge - g, 0.5)
    .L(b.x0 + 0.04, b.frontHinge)
    .build()
    .toPoints();
  frontLid.pop();
  return { base, back, front, left, right, backLid, frontLid };
}

/** Score lines. `parent` names the sheet each crease is printed on (it folds with that sheet). */
export function cartonCreases() {
  const b = BOX;
  const line = (x0, z0, x1, z1) => new Path().M(x0, z0).L(x1, z1).build();
  const i = 0.18;
  return [
    { parent: 'base', path: line(b.x0 + i, b.z0, b.x1 - i, b.z0) },
    { parent: 'base', path: line(b.x1, b.z0 + i, b.x1, b.z1 - i) },
    { parent: 'base', path: line(b.x1 - i, b.z1, b.x0 + i, b.z1) },
    { parent: 'base', path: line(b.x0, b.z1 - i, b.x0, b.z0 + i) },
    { parent: 'back', path: line(b.x0 + i, b.backHinge, b.x1 - i, b.backHinge) },
    { parent: 'front', path: line(b.x1 - i, b.frontHinge, b.x0 + i, b.frontHinge) }
  ];
}

// --- Camera frames (what must be visible; the rig fits them to any aspect) -------------------
export const FRAMES = {
  wide: { tx: -0.1, tz: -3.4, w: 27.4, h: 29.0, tilt: 0, yaw: 0, fov: 3 },
  letter: { tx: ENV.cx, tz: -11.3, w: 15.4, h: 15.6, tilt: 0, yaw: 0, fov: 3 },
  carton: { tx: BOX.cx, tz: 2.0, w: 17.6, h: 19.4, tilt: 19, yaw: -5, fov: 3 },
  transfer: { tx: 0.2, tz: -4.6, w: 26.4, h: 26.4, tilt: 12, yaw: -3, fov: 3 },
  fold: { tx: BOX.cx, tz: 1.5, w: 14.6, h: 13.2, tilt: 27, yaw: -8, fov: 4 },
  sealed: { tx: BOX.cx, tz: 2.0, w: 13.4, h: 12.2, tilt: 20, yaw: 0, fov: 4 }
};

// Narrow screens: the scene is a compact banner, so shots follow the subject instead of
// showing the whole composition at once.
export const FRAMES_COMPACT = {
  wide: { tx: -0.6, tz: -4.4, w: 27.0, h: 17.5, tilt: 0, yaw: 0, fov: 3 },
  // The banner cannot hold the whole drawing, so the opening follows the pen to the dieline.
  net: { tx: BOX.cx - 2.2, tz: 2.2, w: 22.0, h: 18.6, tilt: 0, yaw: 0, fov: 3 },
  letter: { tx: ENV.cx, tz: -11.6, w: 15.0, h: 11.6, tilt: 0, yaw: 0, fov: 3 },
  carton: { tx: BOX.cx, tz: 2.4, w: 17.4, h: 12.2, tilt: 19, yaw: -5, fov: 3 },
  transfer: { tx: 0.2, tz: -4.6, w: 27.0, h: 17.5, tilt: 12, yaw: -3, fov: 3 },
  fold: { tx: BOX.cx, tz: 1.6, w: 14.0, h: 9.4, tilt: 27, yaw: -8, fov: 4 },
  sealed: { tx: BOX.cx, tz: 1.9, w: 14.4, h: 9.6, tilt: 18, yaw: 0, fov: 4 }
};

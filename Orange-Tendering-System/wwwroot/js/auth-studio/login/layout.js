import { Path, roundedRect } from '../core/paths.js';

// ORANGE SIGNAL — single source of geometry for L00–L02 (flat stage).
// Same plane and units as Supplier Genesis. Where Registration's line is drawn by hand (curves,
// breathing pace), this one is plotted: orthogonal and 45° runs, constant feed, crisp corners.

export const T_SHELL = 0.11; // ceramic card
export const T_PAD = 0.03; // contact plate

// --- SIM shell (portrait, one chamfered corner) ------------------------------------------------
export const SIM = { x0: -5.3, x1: 5.3, z0: -8.2, z1: 6.2, r: 1.0, cut: 2.5 };
SIM.cx = 0;
SIM.cz = (SIM.z0 + SIM.z1) / 2;
SIM.row = -1.0; // z of the signal axis: the line enters, crosses the chip and leaves on this row

// --- Contact frame and pads --------------------------------------------------------------------
export const CHIP = { x0: -3.5, x1: 3.5, z0: -5.3, z1: 3.3, r: 0.9, col: 2.25, gap: 0.075 };
CHIP.rows = [CHIP.z0, CHIP.z0 + (CHIP.z1 - CHIP.z0) / 3, CHIP.z0 + (2 * (CHIP.z1 - CHIP.z0)) / 3, CHIP.z1];
CHIP.cols = [CHIP.x0, CHIP.x0 + CHIP.col, CHIP.x1 - CHIP.col, CHIP.x1];

// --- Verification boundary (reached by the signal path; nothing passes it in this gate) ---------
export const GATE = { x: 9.5, z: SIM.row, r: 0.5 };

/** Where the Orange point parks: on the contact frame, where the line entered it. */
export const PARK = { x: CHIP.x0, z: SIM.row };

// --- The continuous Orange line ----------------------------------------------------------------
// Lead-in (dog-leg) → card outline (closed circuit) → straight on into the card → contact frame
// (closed circuit) → parks on the frame.
export function buildMainLine() {
  const s = SIM;
  const c = CHIP;
  const p = new Path();
  p.M(-12.8, s.row + 2.4)
    .L(-11.2, s.row + 2.4, 0.16)
    .L(-8.8, s.row, 0.16)
    .L(s.x0, s.row)
    .mark('shellStart');

  // Card outline, clockwise from the left edge.
  p.L(s.x0, s.z0, s.r)
    .L(s.x1 - s.cut, s.z0, 0.22)
    .L(s.x1, s.z0 + s.cut, 0.22)
    .L(s.x1, s.z1, s.r)
    .L(s.x0, s.z1, s.r)
    .L(s.x0, s.row)
    .mark('shellEnd');

  // Straight on, into the card, to the contact frame.
  p.L(c.x0, s.row).mark('frameStart');
  p.L(c.x0, c.z0, c.r).L(c.x1, c.z0, c.r).L(c.x1, c.z1, c.r).L(c.x0, c.z1, c.r).L(c.x0, s.row).mark('end');
  return p.build({ step: 0.07 });
}

export const ZONE = { lead: 0, shell: 1, link: 2, frame: 3 };

export function shellOutline() {
  const s = SIM;
  const pts = new Path()
    .M(s.x0, s.row)
    .L(s.x0, s.z0, s.r)
    .L(s.x1 - s.cut, s.z0, 0.22)
    .L(s.x1, s.z0 + s.cut, 0.22)
    .L(s.x1, s.z1, s.r)
    .L(s.x0, s.z1, s.r)
    .L(s.x0, s.row)
    .build()
    .toPoints();
  pts.pop();
  return pts;
}

export function chipOutline(inset = 0) {
  const c = CHIP;
  return roundedRect(c.x0 + inset, c.z0 + inset, c.x1 - inset, c.z1 - inset, c.r - inset);
}

/** Eight contact plates: three per side column, two in the centre column. */
export function padOutlines() {
  const c = CHIP;
  const g = c.gap;
  const R = c.r - g;
  const r = 0.13;
  const [x0, x1, x2, x3] = c.cols;
  const [z0, z1, z2, z3] = c.rows;
  const cell = (ax, az, bx, bz, radii, row, col) => ({
    points: roundedRect(ax + g, az + g, bx - g, bz - g, radii),
    centre: [(ax + bx) / 2, (az + bz) / 2],
    row,
    col
  });
  return [
    cell(x0, z0, x1, z1, [R, r, r, r], 0, 0),
    cell(x0, z1, x1, z2, r, 1, 0),
    cell(x0, z2, x1, z3, [r, r, r, R], 2, 0),
    cell(x1, z0, x2, z2, r, 0, 1),
    cell(x1, z2, x2, z3, r, 2, 1),
    cell(x2, z0, x3, z1, [r, R, r, r], 0, 2),
    cell(x2, z1, x3, z2, r, 1, 2),
    cell(x2, z2, x3, z3, [r, r, R, r], 2, 2)
  ];
}

/** Divider lines of the contact pattern (fine detail strokes of the opening drawing). */
export function padDividers() {
  const c = CHIP;
  const [x0, x1, x2, x3] = c.cols;
  const [z0, z1, z2, z3] = c.rows;
  const line = (ax, az, bx, bz) => new Path().M(ax, az).L(bx, bz).build();
  return [
    line(x1, z0, x1, z3),
    line(x2, z0, x2, z3),
    line(x0, z1, x1, z1),
    line(x0, z2, x1, z2),
    line(x1, z2, x2, z2),
    line(x3, z1, x2, z1),
    line(x3, z2, x2, z2)
  ];
}

// --- Circuit traces (drawn while the e-mail is typed) -------------------------------------------
// Every trace starts on the card's edge, in line with a pad row or column, and ends on a terminal.
const TERMINAL_R = 0.27;
function trace(points, group) {
  const p = new Path().M(points[0][0], points[0][1]);
  for (let i = 1; i < points.length; i++) p.L(points[i][0], points[i][1], i < points.length - 1 ? 0.14 : 0);
  const end = points[points.length - 1];
  const prev = points[points.length - 2];
  const d = Math.hypot(end[0] - prev[0], end[1] - prev[1]);
  // The stroke stops at the rim of its terminal ring.
  const ux = (end[0] - prev[0]) / d;
  const uz = (end[1] - prev[1]) / d;
  return { path: p.build(), terminal: [end[0] + ux * TERMINAL_R, end[1] + uz * TERMINAL_R], group };
}

export function traces() {
  const s = SIM;
  const rows = CHIP.rows;
  const up = (rows[0] + rows[1]) / 2; // -3.87
  const down = (rows[2] + rows[3]) / 2; // 1.87
  return [
    // Group A — left and top: appear as soon as something is typed.
    trace([[s.x0, up], [s.x0 - 1.3, up], [s.x0 - 2.7, up - 1.4], [s.x0 - 5.0, up - 1.4]], 'a'),
    trace([[s.x0, down], [s.x0 - 1.3, down], [s.x0 - 2.7, down + 1.4], [s.x0 - 5.0, down + 1.4]], 'a'),
    trace([[-2.0, s.z0], [-2.0, s.z0 - 1.4], [-3.4, s.z0 - 2.8], [-3.4, s.z0 - 4.1]], 'a'),
    // Group B — right, bottom: the address has reached its domain.
    trace([[0.9, s.z0], [0.9, s.z0 - 1.4], [2.3, s.z0 - 2.8], [2.3, s.z0 - 4.1]], 'b'),
    trace([[s.x1, up], [s.x1 + 1.3, up], [s.x1 + 2.7, up - 1.4], [s.x1 + 5.0, up - 1.4]], 'b'),
    trace([[s.x1, down], [s.x1 + 1.3, down], [s.x1 + 2.7, down + 1.4], [s.x1 + 5.0, down + 1.4]], 'b'),
    trace([[-2.0, s.z1], [-2.0, s.z1 + 1.4], [-3.4, s.z1 + 2.8], [-3.4, s.z1 + 3.7]], 'b'),
    trace([[2.0, s.z1], [2.0, s.z1 + 1.4], [3.4, s.z1 + 2.8], [3.4, s.z1 + 3.7]], 'b')
  ];
}

export const TERMINAL = { r: TERMINAL_R };

/** The signal path: from the contact frame, across the card, out to the verification boundary. */
export function signalPath() {
  const inner = new Path().M(CHIP.x1, SIM.row).L(SIM.x1, SIM.row).build();
  const outer = new Path().M(SIM.x1, SIM.row).L(GATE.x - GATE.r, SIM.row).build();
  return { inner, outer };
}

export function circlePath(cx, cz, r, step = 0.04) {
  const k = 0.5523 * r;
  // Starts on the left so a ring closes where its line arrives.
  return new Path()
    .M(cx - r, cz)
    .C(cx - r, cz - k, cx - k, cz - r, cx, cz - r)
    .C(cx + k, cz - r, cx + r, cz - k, cx + r, cz)
    .C(cx + r, cz + k, cx + k, cz + r, cx, cz + r)
    .C(cx - k, cz + r, cx - r, cz + k, cx - r, cz)
    .build({ step });
}

/** Registration ticks at the card corners: the "precision-drawn details" of the password stage. */
export function cornerTicks() {
  const s = SIM;
  const o = 0.55;
  const l = 0.7;
  const tick = (x, z, dx, dz) => new Path().M(x + dx * l, z).L(x, z).L(x, z + dz * l).build();
  return [
    tick(s.x0 - o, s.z0 - o, 1, 1),
    tick(s.x0 - o, s.z1 + o, 1, -1),
    tick(s.x1 + o, s.z1 + o, -1, -1)
  ];
}

// --- Camera frames --------------------------------------------------------------------------------
export const FRAMES = {
  wide: { tx: -0.9, tz: -1.3, w: 25.4, h: 25.4, tilt: 0, yaw: 0, fov: 3 },
  traces: { tx: -0.4, tz: -1.2, w: 23.4, h: 24.4, tilt: 0, yaw: 0, fov: 3 },
  core: { tx: 0.6, tz: -1.0, w: 17.6, h: 18.6, tilt: 0, yaw: 0, fov: 3 },
  // L03+: the same camera leaves the optically flat lens (a dolly-zoom) and tilts into depth.
  verify: { tx: 3.2, tz: -1.0, w: 21.5, h: 17.5, tilt: 32, yaw: -10, fov: 24 },
  choose: { tx: 7.6, tz: -1.4, w: 28.5, h: 19.5, tilt: 40, yaw: -14, fov: 26 },
  // Straight down the optical axis: the stable, camera-facing composition for the live picture.
  optics: { tx: 15.8, ty: 0.9, tz: -6.2, w: 8.1, h: 8.1, tilt: 0, yaw: 0, fov: 16 }
};

export const FRAMES_COMPACT = {
  wide: { tx: -0.9, tz: -1.0, w: 25.0, h: 16.4, tilt: 0, yaw: 0, fov: 3 },
  traces: { tx: -0.4, tz: -1.0, w: 24.0, h: 16.0, tilt: 0, yaw: 0, fov: 3 },
  core: { tx: 1.4, tz: -1.0, w: 19.0, h: 12.4, tilt: 0, yaw: 0, fov: 3 },
  verify: { tx: 3.2, tz: -1.0, w: 22.0, h: 13.0, tilt: 30, yaw: -10, fov: 24 },
  choose: { tx: 7.6, tz: -1.0, w: 30.0, h: 16.5, tilt: 38, yaw: -12, fov: 26 },
  optics: { tx: 15.8, ty: 0.9, tz: -6.2, w: 7.8, h: 7.8, tilt: 0, yaw: 0, fov: 16 }
};

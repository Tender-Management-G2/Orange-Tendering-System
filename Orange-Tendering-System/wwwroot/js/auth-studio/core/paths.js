// 2D path authoring on the drawing plane (x = right, z = toward the viewer / down-screen).
// One source of truth: the same Path feeds the in-world stroke ribbons, the paper outlines,
// the pen pacing and the exported SVG artwork, so line art and geometry cannot drift apart.

const TAU = Math.PI * 2;

export class Path {
  constructor() {
    this.cmds = [];
  }

  M(x, z) {
    this.cmds.push({ t: 'M', x, z });
    return this;
  }

  /** Straight segment. `r` rounds the corner AT this vertex when the next command is also a line. */
  L(x, z, r = 0) {
    this.cmds.push({ t: 'L', x, z, r });
    return this;
  }

  C(x1, z1, x2, z2, x, z) {
    this.cmds.push({ t: 'C', x1, z1, x2, z2, x, z });
    return this;
  }

  /** Names the arclength station at the end of the last command. */
  mark(name) {
    const last = this.cmds[this.cmds.length - 1];
    (last.marks ??= []).push(name);
    return this;
  }

  build({ step = 0.09 } = {}) {
    const pts = [];
    const marks = {};
    const svg = [];
    let total = 0;
    let cx = 0;
    let cz = 0;

    const push = (x, z) => {
      const n = pts.length;
      if (n) {
        const d = Math.hypot(x - pts[n - 2], z - pts[n - 1]);
        if (d < 1e-7) return;
        total += d;
      }
      pts.push(x, z);
    };
    const f = v => +v.toFixed(3);

    for (let i = 0; i < this.cmds.length; i++) {
      const c = this.cmds[i];
      const next = this.cmds[i + 1];
      if (c.t === 'M') {
        push(c.x, c.z);
        svg.push(`M ${f(c.x)} ${f(c.z)}`);
        cx = c.x;
        cz = c.z;
      } else if (c.t === 'L') {
        const round = c.r > 0 && next && next.t === 'L';
        if (!round) {
          push(c.x, c.z);
          svg.push(`L ${f(c.x)} ${f(c.z)}`);
          cx = c.x;
          cz = c.z;
          for (const m of c.marks ?? []) marks[m] = total;
          continue;
        }
        // Circular fillet between (cx,cz) -> c -> next.
        let ux = cx - c.x;
        let uz = cz - c.z;
        let vx = next.x - c.x;
        let vz = next.z - c.z;
        const lu = Math.hypot(ux, uz);
        const lv = Math.hypot(vx, vz);
        ux /= lu; uz /= lu; vx /= lv; vz /= lv;
        const theta = Math.acos(Math.max(-1, Math.min(1, ux * vx + uz * vz)));
        if (theta < 1e-3 || Math.PI - theta < 1e-3) {
          push(c.x, c.z);
          svg.push(`L ${f(c.x)} ${f(c.z)}`);
          cx = c.x;
          cz = c.z;
          for (const m of c.marks ?? []) marks[m] = total;
          continue;
        }
        let d = c.r / Math.tan(theta / 2);
        const dMax = 0.48 * Math.min(lu, lv);
        const r = d > dMax ? c.r * (dMax / d) : c.r;
        d = Math.min(d, dMax);
        const ax = c.x + ux * d;
        const az = c.z + uz * d;
        const bx = c.x + vx * d;
        const bz = c.z + vz * d;
        let mx = ux + vx;
        let mz = uz + vz;
        const lm = Math.hypot(mx, mz);
        mx /= lm; mz /= lm;
        const h = r / Math.sin(theta / 2);
        const ox = c.x + mx * h;
        const oz = c.z + mz * h;
        let a0 = Math.atan2(az - oz, ax - ox);
        let a1 = Math.atan2(bz - oz, bx - ox);
        let da = a1 - a0;
        while (da > Math.PI) da -= TAU;
        while (da < -Math.PI) da += TAU;
        push(ax, az);
        const before = total;
        const n = Math.max(4, Math.ceil((Math.abs(da) * r) / 0.035));
        for (let k = 1; k <= n; k++) {
          const a = a0 + (da * k) / n;
          push(ox + Math.cos(a) * r, oz + Math.sin(a) * r);
        }
        svg.push(`L ${f(ax)} ${f(az)}`, `A ${f(r)} ${f(r)} 0 0 ${da > 0 ? 1 : 0} ${f(bx)} ${f(bz)}`);
        cx = bx;
        cz = bz;
        for (const m of c.marks ?? []) marks[m] = (before + total) / 2;
      } else if (c.t === 'C') {
        // Estimate length to pick a sampling density, then sample uniformly in t.
        let est = 0;
        let px = cx;
        let pz = cz;
        for (let k = 1; k <= 12; k++) {
          const [x, z] = cubic(cx, cz, c, k / 12);
          est += Math.hypot(x - px, z - pz);
          px = x;
          pz = z;
        }
        const n = Math.max(10, Math.ceil(est / step));
        for (let k = 1; k <= n; k++) {
          const [x, z] = cubic(cx, cz, c, k / n);
          push(x, z);
        }
        svg.push(`C ${f(c.x1)} ${f(c.z1)} ${f(c.x2)} ${f(c.z2)} ${f(c.x)} ${f(c.z)}`);
        cx = c.x;
        cz = c.z;
        for (const m of c.marks ?? []) marks[m] = total;
      }
    }
    return new SampledPath(new Float32Array(pts), marks, svg.join(' '));
  }
}

function cubic(x0, z0, c, t) {
  const u = 1 - t;
  const a = u * u * u;
  const b = 3 * u * u * t;
  const d = 3 * u * t * t;
  const e = t * t * t;
  return [a * x0 + b * c.x1 + d * c.x2 + e * c.x, a * z0 + b * c.z1 + d * c.z2 + e * c.z];
}

export class SampledPath {
  constructor(pts, marks = {}, svg = '') {
    this.pts = pts;
    this.count = pts.length / 2;
    this.len = new Float32Array(this.count);
    for (let i = 1; i < this.count; i++) {
      this.len[i] = this.len[i - 1] + Math.hypot(pts[2 * i] - pts[2 * i - 2], pts[2 * i + 1] - pts[2 * i - 1]);
    }
    this.total = this.len[this.count - 1];
    this.marks = marks;
    this.svg = svg;
  }

  /** Index of the segment containing arclength s. */
  segment(s) {
    let lo = 0;
    let hi = this.count - 1;
    while (hi - lo > 1) {
      const mid = (lo + hi) >> 1;
      if (this.len[mid] <= s) lo = mid;
      else hi = mid;
    }
    return lo;
  }

  pointAt(s, out = [0, 0]) {
    s = Math.max(0, Math.min(this.total, s));
    const i = this.segment(s);
    const span = this.len[i + 1] - this.len[i] || 1;
    const k = (s - this.len[i]) / span;
    out[0] = this.pts[2 * i] + (this.pts[2 * i + 2] - this.pts[2 * i]) * k;
    out[1] = this.pts[2 * i + 1] + (this.pts[2 * i + 3] - this.pts[2 * i + 1]) * k;
    return out;
  }

  tangentAt(s, out = [1, 0]) {
    const i = this.segment(Math.max(0, Math.min(this.total - 1e-6, s)));
    const dx = this.pts[2 * i + 2] - this.pts[2 * i];
    const dz = this.pts[2 * i + 3] - this.pts[2 * i + 1];
    const l = Math.hypot(dx, dz) || 1;
    out[0] = dx / l;
    out[1] = dz / l;
    return out;
  }

  /** Points as [[x,z],...] — used to cut paper outlines from the same artwork. */
  toPoints() {
    const out = [];
    for (let i = 0; i < this.count; i++) out.push([this.pts[2 * i], this.pts[2 * i + 1]]);
    return out;
  }
}

/**
 * Pen pacing: maps normalised time u∈[0,1] to arclength so the pen slows through corners and
 * at named stations, then accelerates on straights — the difference between a drawn line and a
 * progress bar. `corner` controls how strongly curvature slows the pen; `stations` add authored
 * hesitations ({ s, depth 0..1, width in world units }).
 */
export function makePace(path, { corner = 0.55, window = 0.9, stations = [], floor = 0.16 } = {}) {
  const n = path.count;
  const turn = new Float32Array(n);
  for (let i = 1; i < n - 1; i++) {
    const ax = path.pts[2 * i] - path.pts[2 * i - 2];
    const az = path.pts[2 * i + 1] - path.pts[2 * i - 1];
    const bx = path.pts[2 * i + 2] - path.pts[2 * i];
    const bz = path.pts[2 * i + 3] - path.pts[2 * i + 1];
    const la = Math.hypot(ax, az) || 1;
    const lb = Math.hypot(bx, bz) || 1;
    turn[i] = Math.acos(Math.max(-1, Math.min(1, (ax * bx + az * bz) / (la * lb))));
  }
  // Speed at each sample: turning inside a sliding arclength window lowers the speed.
  const speed = new Float32Array(n);
  let lo = 0;
  let hi = 0;
  let acc = 0;
  for (let i = 0; i < n; i++) {
    const s = path.len[i];
    while (hi < n && path.len[hi] <= s + window) acc += turn[hi++];
    while (lo < n && path.len[lo] < s - window) acc -= turn[lo++];
    let v = 1 / (1 + corner * Math.max(0, acc) * 2.2);
    for (const st of stations) {
      const d = (s - st.s) / st.width;
      v *= 1 - st.depth * Math.exp(-d * d);
    }
    speed[i] = Math.max(floor, v);
  }
  // Integrate dt = ds / v into a time table, then normalise.
  const time = new Float32Array(n);
  for (let i = 1; i < n; i++) {
    const ds = path.len[i] - path.len[i - 1];
    time[i] = time[i - 1] + ds / ((speed[i] + speed[i - 1]) * 0.5);
  }
  const T = time[n - 1] || 1;
  return function sAt(u) {
    const t = Math.max(0, Math.min(1, u)) * T;
    let a = 0;
    let b = n - 1;
    while (b - a > 1) {
      const m = (a + b) >> 1;
      if (time[m] <= t) a = m;
      else b = m;
    }
    const span = time[b] - time[a] || 1;
    return path.len[a] + (path.len[b] - path.len[a]) * ((t - time[a]) / span);
  };
}

/** Closed rounded rectangle as a point loop (clockwise on screen), for paper outlines. */
export function roundedRect(x0, z0, x1, z1, r) {
  const rr = Array.isArray(r) ? r : [r, r, r, r]; // tl, tr, br, bl
  const mid = (x0 + x1) / 2;
  const p = new Path()
    .M(mid, z0)
    .L(x1, z0, rr[1])
    .L(x1, z1, rr[2])
    .L(x0, z1, rr[3])
    .L(x0, z0, rr[0])
    .L(mid, z0);
  const pts = p.build().toPoints();
  pts.pop();
  return pts;
}

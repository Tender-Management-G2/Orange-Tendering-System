import * as THREE from 'three';

// In-world ink. A stroke is a ribbon lying on a surface (ground, paper, ceramic) so it folds,
// tilts and is occluded with the geometry it is drawn on. Edges are anti-aliased analytically in
// the fragment shader, the reveal front is a rounded cap carved from the ribbon, and the visible
// window [uA, uB] is expressed in arclength so one uniform drives the whole drawing.

const MAX_ZONES = 8;

const vertexShader = /* glsl */ `
  attribute float aAcross;
  attribute float aDist;
  attribute float aZone;
  varying float vAcross;
  varying float vDist;
  varying float vZone;
  void main() {
    vAcross = aAcross;
    vDist = aDist;
    vZone = aZone;
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
  }
`;

const fragmentShader = /* glsl */ `
  uniform vec3 uColor;
  uniform float uOpacity;
  uniform float uHalf;
  uniform float uA;
  uniform float uB;
  uniform float uDash;
  uniform float uGap;
  uniform float uDots;
  uniform float uTip;
  uniform float uTipLen;
  uniform float uZone[${MAX_ZONES}];
  varying float vAcross;
  varying float vDist;
  varying float vZone;

  void main() {
    if (uB <= uA) discard;
    // Distance outside the revealed window, measured along the stroke.
    float dOut = max(max(uA - vDist, vDist - uB), 0.0);
    float d = length(vec2(vAcross, dOut));
    // Wet-ink swell just behind the pen.
    float half_ = uHalf * (1.0 + uTip * exp(-max(uB - vDist, 0.0) / uTipLen));
    float aa = max(fwidth(vAcross), fwidth(vDist) * step(1e-6, dOut)) * 0.8 + 1e-5;
    float alpha = 1.0 - smoothstep(half_ - aa, half_ + aa, d);

    if (uDots > 0.5) {
      // Row of round dots, one per period; a dot exists once the reveal front passes its centre.
      float period = uDash + uGap;
      float centre = (floor(vDist / period) + 0.5) * period;
      if (centre > uB || centre < uA) discard;
      float dd = length(vec2(vAcross, vDist - centre));
      float da = fwidth(dd) * 0.8 + 1e-5;
      alpha = 1.0 - smoothstep(uHalf - da, uHalf + da, dd);
    } else if (uDash > 0.0) {
      float period = uDash + uGap;
      float m = mod(vDist, period);
      float da = fwidth(vDist) * 0.8 + 1e-5;
      // Soft-ended dashes centred in each period.
      float inDash = smoothstep(0.0, da, m) * (1.0 - smoothstep(uDash - da, uDash, m));
      alpha *= inDash;
    }

    int zone = int(clamp(floor(vZone + 0.5), 0.0, ${MAX_ZONES - 1}.0));
    alpha *= uOpacity * uZone[zone];
    if (alpha < 0.003) discard;
    gl_FragColor = vec4(uColor, alpha);
    #include <colorspace_fragment>
  }
`;

/**
 * Builds the ribbon for a SampledPath.
 * @param path     SampledPath on the XZ plane
 * @param halfMax  largest half-width the material will ever use (world units)
 * @param zoneCuts ascending arclengths where the zone id increments (zones fade independently)
 */
export function buildStrokeGeometry(path, { halfMax = 0.06, pad = 0.07, y = 0, zoneCuts = [] } = {}) {
  // Split segments at zone boundaries and duplicate the boundary point so zone ids never interpolate.
  const P = [];
  let zone = 0;
  let cut = 0;
  for (let i = 0; i < path.count; i++) {
    const s = path.len[i];
    while (cut < zoneCuts.length && zoneCuts[cut] <= s + 1e-6) {
      const cs = zoneCuts[cut];
      if (i > 0 && cs > path.len[i - 1] + 1e-6 && cs < s - 1e-6) {
        const [x, z] = path.pointAt(cs);
        P.push({ x, z, s: cs, zone });
        P.push({ x, z, s: cs, zone: zone + 1 });
      } else if (P.length && Math.abs(cs - s) <= 1e-6) {
        // Boundary lands exactly on a sample: close the old zone on this point, the regular push
        // below reopens it in the new zone.
        P.push({ x: path.pts[2 * i], z: path.pts[2 * i + 1], s, zone });
      }
      zone++;
      cut++;
    }
    P.push({ x: path.pts[2 * i], z: path.pts[2 * i + 1], s, zone });
  }

  const n = P.length;
  const h = halfMax + pad;
  const positions = [];
  const across = [];
  const dist = [];
  const zones = [];
  const index = [];

  const dir = (a, b) => {
    const dx = b.x - a.x;
    const dz = b.z - a.z;
    const l = Math.hypot(dx, dz);
    return l < 1e-9 ? null : [dx / l, dz / l];
  };
  const segDir = new Array(n - 1);
  for (let i = 0; i < n - 1; i++) segDir[i] = dir(P[i], P[i + 1]);
  const prevDir = i => {
    for (let k = i - 1; k >= 0; k--) if (segDir[k]) return segDir[k];
    return null;
  };
  const nextDir = i => {
    for (let k = i; k < n - 1; k++) if (segDir[k]) return segDir[k];
    return null;
  };

  const addPair = (x, z, tx, tz, miter, s, zid) => {
    const nx = -tz * h * miter;
    const nz = tx * h * miter;
    positions.push(x + nx, y, z + nz, x - nx, y, z - nz);
    across.push(h, -h);
    dist.push(s, s);
    zones.push(zid, zid);
  };

  // Start cap extension.
  {
    const t = nextDir(0) ?? [1, 0];
    addPair(P[0].x - t[0] * h, P[0].z - t[1] * h, t[0], t[1], 1, P[0].s - h, P[0].zone);
  }
  for (let i = 0; i < n; i++) {
    const a = prevDir(i) ?? nextDir(i) ?? [1, 0];
    const b = nextDir(i) ?? a;
    let tx = a[0] + b[0];
    let tz = a[1] + b[1];
    const l = Math.hypot(tx, tz);
    if (l < 1e-6) {
      tx = b[0];
      tz = b[1];
    } else {
      tx /= l;
      tz /= l;
    }
    const cosHalf = Math.max(0.38, tx * b[0] + tz * b[1]);
    addPair(P[i].x, P[i].z, tx, tz, 1 / cosHalf, P[i].s, P[i].zone);
  }
  {
    const t = prevDir(n - 1) ?? [1, 0];
    addPair(P[n - 1].x + t[0] * h, P[n - 1].z + t[1] * h, t[0], t[1], 1, P[n - 1].s + h, P[n - 1].zone);
  }

  // Row r holds vertices 2r and 2r+1; rows are [startCap, P0..Pn-1, endCap].
  const rows = n + 2;
  const rowZone = r => zones[2 * r];
  for (let r = 0; r < rows - 1; r++) {
    const a = 2 * r;
    const b = 2 * (r + 1);
    const degenerate = Math.abs(dist[a] - dist[b]) < 1e-9 && rowZone(r) !== rowZone(r + 1);
    if (degenerate) continue;
    index.push(a, a + 1, b, b, a + 1, b + 1);
  }

  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
  g.setAttribute('aAcross', new THREE.Float32BufferAttribute(across, 1));
  g.setAttribute('aDist', new THREE.Float32BufferAttribute(dist, 1));
  g.setAttribute('aZone', new THREE.Float32BufferAttribute(zones, 1));
  g.setIndex(index);
  g.computeBoundingSphere();
  return g;
}

export class Stroke extends THREE.Mesh {
  /**
   * @param path SampledPath
   * @param opts { color, width, y, zoneCuts, dash, gap, renderOrder }
   */
  constructor(path, { color = '#F26A00', width = 0.088, y = 0.006, zoneCuts = [], dash = 0, gap = 0, dots = false, maxWidth } = {}) {
    const halfMax = (maxWidth ?? width * 1.6) / 2;
    const material = new THREE.ShaderMaterial({
      vertexShader,
      fragmentShader,
      transparent: true,
      depthWrite: false,
      side: THREE.DoubleSide,
      toneMapped: false,
      polygonOffset: true,
      polygonOffsetFactor: -2,
      polygonOffsetUnits: -2,
      uniforms: {
        uColor: { value: new THREE.Color(color) },
        uOpacity: { value: 1 },
        uHalf: { value: width / 2 },
        uA: { value: 0 },
        uB: { value: 0 },
        uDash: { value: dash },
        uGap: { value: gap },
        uDots: { value: dots ? 1 : 0 },
        uTip: { value: 0 },
        uTipLen: { value: 1.2 },
        uZone: { value: new Array(MAX_ZONES).fill(1) }
      }
    });
    super(buildStrokeGeometry(path, { halfMax, y, zoneCuts }), material);
    this.path = path;
    this.total = path.total;
    this.frustumCulled = false;
    this.renderOrder = 10;
  }

  /** Reveal window in arclength units. */
  set range([a, b]) {
    this.material.uniforms.uA.value = a;
    this.material.uniforms.uB.value = b;
  }

  /** Reveal from the start up to a fraction of the full length. */
  set drawn(f) {
    this.material.uniforms.uA.value = 0;
    this.material.uniforms.uB.value = f <= 0 ? -1 : Math.min(1, f) * this.total;
  }

  set opacity(v) {
    this.material.uniforms.uOpacity.value = v;
    this.visible = v > 0.002;
  }

  set width(w) {
    this.material.uniforms.uHalf.value = w / 2;
  }

  set tip(v) {
    this.material.uniforms.uTip.value = v;
  }

  zone(i, v) {
    this.material.uniforms.uZone.value[i] = v;
  }

  setColor(c) {
    this.material.uniforms.uColor.value.set(c);
  }
}

const dotVertex = /* glsl */ `
  varying vec2 vUv;
  void main() {
    vUv = uv * 2.0 - 1.0;
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
  }
`;

const dotFragment = /* glsl */ `
  uniform vec3 uColor;
  uniform float uOpacity;
  uniform float uRing;      // 0..1 progress of the idle halo ring
  uniform float uRingAlpha;
  varying vec2 vUv;
  void main() {
    float r = length(vUv) * 3.0;            // quad spans 3 dot radii so the halo has room
    float aa = fwidth(r) * 0.8 + 1e-5;
    float core = 1.0 - smoothstep(1.0 - aa, 1.0 + aa, r);
    float ringR = 1.0 + uRing * 1.7;
    float ring = (1.0 - smoothstep(0.0, 0.11 + aa, abs(r - ringR))) * uRingAlpha * (1.0 - uRing);
    float alpha = max(core, ring) * uOpacity;
    if (alpha < 0.003) discard;
    gl_FragColor = vec4(uColor, alpha);
    #include <colorspace_fragment>
  }
`;

/** The Orange point: the pen in Registration, the signal in Login. Flat, unlit, always crisp. */
export class PenDot extends THREE.Mesh {
  constructor({ color = '#FF7900', radius = 0.2 } = {}) {
    const g = new THREE.PlaneGeometry(radius * 6, radius * 6);
    g.rotateX(-Math.PI / 2);
    const m = new THREE.ShaderMaterial({
      vertexShader: dotVertex,
      fragmentShader: dotFragment,
      transparent: true,
      depthWrite: false,
      depthTest: false,
      toneMapped: false,
      uniforms: {
        uColor: { value: new THREE.Color(color) },
        uOpacity: { value: 1 },
        uRing: { value: 0 },
        uRingAlpha: { value: 0 }
      }
    });
    super(g, m);
    this.radius = radius;
    this.renderOrder = 30;
    this.frustumCulled = false;
  }

  set opacity(v) {
    this.material.uniforms.uOpacity.value = v;
    this.visible = v > 0.002;
  }

  ring(progress, alpha) {
    this.material.uniforms.uRing.value = progress;
    this.material.uniforms.uRingAlpha.value = alpha;
  }
}

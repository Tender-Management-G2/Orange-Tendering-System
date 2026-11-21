import * as THREE from 'three';

// Paper, card and board. Every sheet is a real solid with thickness, cut from the same outline
// the line art traced, and every moving sheet hangs from a pivot group placed on its crease.

let heightTexture = null;
let albedoTexture = null;

function drawFibres(ctx, size, count, spread, alpha) {
  for (let i = 0; i < count; i++) {
    const x = Math.random() * size;
    const y = Math.random() * size;
    const a = Math.random() * Math.PI;
    const l = 4 + Math.random() * 14;
    const v = 128 + (Math.random() - 0.5) * spread;
    ctx.strokeStyle = `rgba(${v},${v},${v},${alpha})`;
    ctx.lineWidth = 0.6 + Math.random() * 0.7;
    // Draw wrapped copies so the tile repeats without a seam.
    for (const ox of [-size, 0, size]) {
      for (const oy of [-size, 0, size]) {
        ctx.beginPath();
        ctx.moveTo(x + ox, y + oy);
        ctx.quadraticCurveTo(x + ox + Math.cos(a) * l * 0.5 + (Math.random() - 0.5) * 3, y + oy + Math.sin(a) * l * 0.5 + (Math.random() - 0.5) * 3, x + ox + Math.cos(a) * l, y + oy + Math.sin(a) * l);
        ctx.stroke();
      }
    }
  }
}

function paperCanvas(size) {
  const c = document.createElement('canvas');
  c.width = c.height = size;
  const ctx = c.getContext('2d', { willReadFrequently: true });
  ctx.fillStyle = 'rgb(128,128,128)';
  ctx.fillRect(0, 0, size, size);
  drawFibres(ctx, size, 1500, 46, 0.5);
  const img = ctx.getImageData(0, 0, size, size);
  const d = img.data;
  for (let i = 0; i < d.length; i += 4) {
    const n = (Math.random() - 0.5) * 16;
    d[i] = d[i + 1] = d[i + 2] = Math.max(0, Math.min(255, d[i] + n));
  }
  ctx.putImageData(img, 0, 0);
  return c;
}

function configure(tex, tile) {
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.repeat.set(1 / tile, 1 / tile);
  tex.anisotropy = 4;
  tex.generateMipmaps = true;
  tex.minFilter = THREE.LinearMipmapLinearFilter;
  return tex;
}

/** Grey height field for bump mapping: fine grain plus short fibres. One tile spans 5 world units. */
export function makePaperTexture() {
  if (!heightTexture) {
    heightTexture = configure(new THREE.CanvasTexture(paperCanvas(512)), 5);
    heightTexture.colorSpace = THREE.NoColorSpace;
  }
  return heightTexture;
}

let albedoMean = 1;
/**
 * The mottling darkens surfaces by its mean; lights are scaled by this gain so a palette colour
 * set on a material is the colour that reaches the screen.
 */
export function albedoGain() {
  makePaperAlbedo();
  return 1 / albedoMean;
}

/** Near-white albedo mottling (multiplied with the sheet colour) so flat sheets are never CG-flat. */
export function makePaperAlbedo() {
  if (!albedoTexture) {
    const src = paperCanvas(512);
    const ctx = src.getContext('2d');
    const img = ctx.getImageData(0, 0, 512, 512);
    const d = img.data;
    let sum = 0;
    for (let i = 0; i < d.length; i += 4) {
      const raw = 255 - Math.max(0, 128 - d[i]) * 0.2 - Math.abs(d[i] - 128) * 0.05;
      const v = Math.round(Math.max(238, Math.min(255, raw)));
      d[i] = d[i + 1] = d[i + 2] = v;
      const c = v / 255;
      sum += c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
    }
    albedoMean = sum / (d.length / 4);
    ctx.putImageData(img, 0, 0);
    albedoTexture = configure(new THREE.CanvasTexture(src), 5);
    albedoTexture.colorSpace = THREE.SRGBColorSpace;
  }
  return albedoTexture;
}

/**
 * Matte sheet material. `base` is kept so a sheet can "develop" out of the page: its colour starts
 * as the ground colour and resolves to its own as it rises.
 */
export function sheetMaterial(color, { roughness = 0.93, bump = 0.5, grain = true } = {}) {
  const m = new THREE.MeshStandardMaterial({
    color,
    roughness,
    metalness: 0,
    map: grain ? makePaperAlbedo() : null,
    bumpMap: makePaperTexture(),
    bumpScale: bump
  });
  m.shadowSide = THREE.FrontSide;
  m.userData.base = new THREE.Color(color);
  return m;
}

/**
 * Solid sheet from a closed outline on the XZ plane.
 * Material groups: 0 = top face, 1 = bottom face, 2 = cut edge.
 */
export function sheetGeometry(points, thickness, { y = 0 } = {}) {
  const contour = points.map(p => new THREE.Vector2(p[0], p[1]));
  // Signed area on (x, z): positive when the loop runs clockwise on screen (z points down-screen).
  let area = 0;
  for (let i = 0; i < contour.length; i++) {
    const a = contour[i];
    const b = contour[(i + 1) % contour.length];
    area += a.x * b.y - b.x * a.y;
  }
  const faces = THREE.ShapeUtils.triangulateShape(contour, []);
  const n = contour.length;
  const pos = [];
  const nor = [];
  const uv = [];
  const idx = [];

  for (const p of contour) {
    pos.push(p.x, y + thickness, p.y);
    nor.push(0, 1, 0);
    uv.push(p.x, p.y);
  }
  for (const p of contour) {
    pos.push(p.x, y, p.y);
    nor.push(0, -1, 0);
    uv.push(p.x, p.y);
  }
  const topStart = idx.length;
  for (const [a, b, c] of faces) {
    const A = contour[a];
    const B = contour[b];
    const C = contour[c];
    const ny = (B.y - A.y) * (C.x - A.x) - (B.x - A.x) * (C.y - A.y);
    if (ny >= 0) idx.push(a, b, c);
    else idx.push(a, c, b);
  }
  const topCount = idx.length - topStart;
  const bottomStart = idx.length;
  for (let i = topStart; i < topStart + topCount; i += 3) idx.push(idx[i] + n, idx[i + 2] + n, idx[i + 1] + n);
  const bottomCount = idx.length - bottomStart;

  const sideStart = idx.length;
  let run = 0;
  const sign = area > 0 ? 1 : -1;
  for (let i = 0; i < n; i++) {
    const a = contour[i];
    const b = contour[(i + 1) % n];
    const dx = b.x - a.x;
    const dz = b.y - a.y;
    const l = Math.hypot(dx, dz) || 1;
    // Outward normal for a loop that is clockwise on screen is (dz, -dx); flipped otherwise.
    const nx = (dz / l) * sign;
    const nz = (-dx / l) * sign;
    const base = pos.length / 3;
    pos.push(a.x, y + thickness, a.y, b.x, y + thickness, b.y, b.x, y, b.y, a.x, y, a.y);
    nor.push(nx, 0, nz, nx, 0, nz, nx, 0, nz, nx, 0, nz);
    uv.push(run, thickness, run + l, thickness, run + l, 0, run, 0);
    run += l;
    if (sign > 0) idx.push(base, base + 1, base + 2, base, base + 2, base + 3);
    else idx.push(base, base + 2, base + 1, base, base + 3, base + 2);
  }
  const sideCount = idx.length - sideStart;

  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('normal', new THREE.Float32BufferAttribute(nor, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  g.setIndex(idx);
  g.addGroup(topStart, topCount, 0);
  g.addGroup(bottomStart, bottomCount, 1);
  g.addGroup(sideStart, sideCount, 2);
  g.computeBoundingSphere();
  return g;
}

/**
 * A sheet hanging from a crease. `pivot` is the world point the sheet rotates about; the mesh is
 * re-based so the group's origin sits exactly on that crease.
 */
export function hingedSheet(name, points, thickness, materials, { pivot = [0, 0, 0], y = 0, shadow = true } = {}) {
  const g = sheetGeometry(points, thickness, { y });
  g.translate(-pivot[0], -pivot[1], -pivot[2]);
  const mesh = new THREE.Mesh(g, materials);
  mesh.name = `${name}:sheet`;
  mesh.castShadow = shadow;
  mesh.receiveShadow = true;
  const group = new THREE.Group();
  group.name = name;
  group.position.set(pivot[0], pivot[1], pivot[2]);
  group.add(mesh);
  group.userData.pivot = pivot.slice();
  group.userData.sheet = mesh;
  return group;
}

/** Re-express a world-space pivot group as a child of `parent` (both authored in world space). */
export function attach(parent, child) {
  const p = parent.userData.pivot;
  const c = child.userData.pivot;
  child.position.set(c[0] - p[0], c[1] - p[1], c[2] - p[2]);
  parent.add(child);
  return child;
}

const _c = new THREE.Color();
/**
 * Development: sheets resolve from the page colour to their own while they gain height.
 * At t = 0 a sheet is the page — same colour, same grain, 2% of its height — so it is invisible
 * yet already occludes correctly (a closed flap hides the seams beneath it even as line art).
 */
export function develop(root, materials, ground, t) {
  const k = Math.max(0, Math.min(1, t));
  root.scale.y = 0.02 + 0.98 * k;
  // An undeveloped sheet must leave no trace at all on the blank page: no shadow until it rises.
  const casts = k > 0.012;
  if (root.userData.casts !== casts) {
    root.userData.casts = casts;
    root.traverse(o => {
      if (!o.isMesh) return;
      o.userData.caster ??= o.castShadow;
      o.castShadow = casts && o.userData.caster;
    });
  }
  const e = k * k * (3 - 2 * k);
  for (const m of materials) {
    _c.copy(ground).lerp(m.userData.base, e);
    m.color.copy(_c);
  }
}

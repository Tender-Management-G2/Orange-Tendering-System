import * as THREE from 'three';

// Metal for Orange Signal (contact plates now; rings, blades and connectors in later gates).
// A metal is only its reflections, so it gets an authored studio: a bright ceiling softbox that
// falls off to a warm horizon. Seen flat from above every plate reflects the ceiling evenly (a
// calm, controllable champagne); the moment the camera tilts the gradient sweeps across it.

const environments = new WeakMap();

export function studioEnvironment(renderer) {
  if (environments.has(renderer)) return environments.get(renderer);
  const c = document.createElement('canvas');
  c.width = 256;
  c.height = 128;
  const ctx = c.getContext('2d');
  // Equirectangular: top row = straight up.
  const g = ctx.createLinearGradient(0, 0, 0, 128);
  g.addColorStop(0.0, '#ffffff');
  g.addColorStop(0.2, '#fffaf2');
  g.addColorStop(0.42, '#d9cdbd');
  g.addColorStop(0.55, '#a89a89');
  g.addColorStop(1.0, '#6f6457');
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, 256, 128);
  // The key softbox, upper left: a broad warm-white patch.
  const key = ctx.createRadialGradient(70, 30, 2, 70, 30, 54);
  key.addColorStop(0, 'rgba(255,255,255,1)');
  key.addColorStop(1, 'rgba(255,255,255,0)');
  ctx.fillStyle = key;
  ctx.fillRect(0, 0, 256, 128);
  const tex = new THREE.CanvasTexture(c);
  tex.mapping = THREE.EquirectangularReflectionMapping;
  tex.colorSpace = THREE.SRGBColorSpace;
  const pmrem = new THREE.PMREMGenerator(renderer);
  const env = pmrem.fromEquirectangular(tex).texture;
  pmrem.dispose();
  tex.dispose();
  environments.set(renderer, env);
  return env;
}

let brushed = null;
/** Fine straight grain for brushed plates (bump only). One tile spans 2 world units. */
export function brushedTexture() {
  if (brushed) return brushed;
  const size = 256;
  const c = document.createElement('canvas');
  c.width = c.height = size;
  const ctx = c.getContext('2d', { willReadFrequently: true });
  const img = ctx.createImageData(size, size);
  for (let y = 0; y < size; y++) {
    const row = 128 + (Math.random() - 0.5) * 70;
    for (let x = 0; x < size; x++) {
      const v = row + (Math.random() - 0.5) * 14;
      const i = (y * size + x) * 4;
      img.data[i] = img.data[i + 1] = img.data[i + 2] = v;
      img.data[i + 3] = 255;
    }
  }
  ctx.putImageData(img, 0, 0);
  brushed = new THREE.CanvasTexture(c);
  brushed.wrapS = brushed.wrapT = THREE.RepeatWrapping;
  brushed.repeat.set(0.5, 0.5);
  brushed.colorSpace = THREE.NoColorSpace;
  brushed.anisotropy = 8;
  return brushed;
}

export function metalMaterial(renderer, color, { roughness = 0.36, intensity = 1 } = {}) {
  const m = new THREE.MeshStandardMaterial({
    color,
    metalness: 1,
    roughness,
    envMap: studioEnvironment(renderer),
    envMapIntensity: intensity,
    bumpMap: brushedTexture(),
    bumpScale: 0.12
  });
  m.shadowSide = THREE.FrontSide;
  return m;
}

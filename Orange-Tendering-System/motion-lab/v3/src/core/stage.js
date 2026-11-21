import * as THREE from 'three';
import gsap from 'gsap';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { CameraRig } from './camera-rig.js';
import { installSoftShadows, installSoftKnee } from './softshadows.js';
import { COLOR } from './tokens.js';
import { makePaperTexture, makePaperAlbedo, albedoGain } from './paper.js';

// The shared ivory studio: one ground "page", one soft key, one warm fill. Both chapters render
// in this same world so Registration and Login cannot diverge in light, colour or camera.

const SHADOW_WIDTH = 44;
const SHADOW_DEPTH = 60;
const LIGHT_SIZE = 0.2; // tan of the key light's angular radius: penumbra width per unit of height

let shadersInstalled = false;

export function webglAvailable() {
  try {
    const c = document.createElement('canvas');
    return Boolean(c.getContext('webgl2'));
  } catch {
    return false;
  }
}

export class Stage {
  constructor(container, { maxPixelRatio = 2, shadowSize = 2048, blockerSamples = 12, pcfSamples = 20 } = {}) {
    if (!shadersInstalled) {
      installSoftShadows({ frustumWidth: SHADOW_WIDTH, minRadius: 0.17, blockerSamples, pcfSamples });
      installSoftKnee(0.96);
      shadersInstalled = true;
    }
    this.container = container;
    this.canvas = document.createElement('canvas');
    this.canvas.className = 'stage-canvas';
    this.canvas.setAttribute('aria-hidden', 'true');
    container.prepend(this.canvas);

    const renderer = new THREE.WebGLRenderer({ canvas: this.canvas, antialias: true, alpha: false, powerPreference: 'high-performance' });
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.CustomToneMapping;
    renderer.toneMappingExposure = 1;
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.BasicShadowMap; // raw depth; filtered by core/softshadows.js
    this.renderer = renderer;
    this.maxPixelRatio = maxPixelRatio;
    // Resolution governor: if sustained animation cannot hold ~40 fps, render fewer pixels.
    // It only ever steps down, so it cannot oscillate.
    this.pixelRatio = Math.min(window.devicePixelRatio || 1, maxPixelRatio);
    this.governor = { last: 0, slow: 0, steps: 0 };

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(COLOR.ground);
    this.scene = scene;

    // Fill from everywhere + one large soft key from the upper left. The two sum to exactly 1 on
    // an up-facing surface (after compensating the paper mottling), so flat sheets and the page
    // render at their palette colours and only shadow and tilt modulate them.
    const gain = albedoGain();
    const KEY_SHARE = 0.36;
    // Warm fill, slightly cool key: together they are neutral white where the key reaches, and
    // what is left in shadow is the warm fill alone — umber shadows instead of grey ones.
    const fillTint = new THREE.Color(1, 0.968, 0.925);
    const keyTint = new THREE.Color(1, 1, 1).sub(fillTint.clone().multiplyScalar(1 - KEY_SHARE)).multiplyScalar(1 / KEY_SHARE);
    this.fill = new THREE.HemisphereLight(fillTint, new THREE.Color(0.9, 0.83, 0.74), (1 - KEY_SHARE) * Math.PI * gain);
    scene.add(this.fill);

    const keyDir = new THREE.Vector3(-0.6, 1.0, -0.5).normalize();
    this.keyDir = keyDir;
    const key = new THREE.DirectionalLight(keyTint, (KEY_SHARE * Math.PI * gain) / keyDir.y);
    key.castShadow = true;
    key.shadow.mapSize.set(shadowSize, shadowSize);
    const sc = key.shadow.camera;
    sc.left = -SHADOW_WIDTH / 2;
    sc.right = SHADOW_WIDTH / 2;
    sc.top = SHADOW_WIDTH / 2;
    sc.bottom = -SHADOW_WIDTH / 2;
    sc.near = 10;
    sc.far = 10 + SHADOW_DEPTH;
    key.shadow.bias = -0.0035 / SHADOW_DEPTH;
    key.shadow.normalBias = 0;
    key.shadow.radius = (LIGHT_SIZE * SHADOW_DEPTH) / SHADOW_WIDTH;
    key.shadow.intensity = 0.9;
    this.key = key;
    scene.add(key, key.target);
    this.setLightTarget(0, 0, -3);

    // The page. Its grain is the same paper texture every sheet uses.
    // Same grain, roughness and albedo mottling as the sheets: a sheet at 0% development is
    // indistinguishable from the page it will rise out of.
    this.groundMaterial = new THREE.MeshStandardMaterial({
      color: COLOR.ground,
      roughness: 0.93,
      metalness: 0,
      map: makePaperAlbedo(),
      bumpMap: makePaperTexture(),
      bumpScale: 0.5
    });
    const ground = new THREE.Mesh(new THREE.PlaneGeometry(900, 900), this.groundMaterial);
    ground.rotation.x = -Math.PI / 2;
    ground.receiveShadow = true;
    // World-scale UVs so grain density matches the sheets.
    const uv = ground.geometry.attributes.uv;
    for (let i = 0; i < uv.count; i++) uv.setXY(i, uv.getX(i) * 900, uv.getY(i) * 900);
    this.ground = ground;
    scene.add(ground);

    this.rig = new CameraRig();
    this.updaters = new Set();
    this.dirty = true;
    this.frames = 0;
    this.running = true;

    this.orbit = new OrbitControls(this.rig.camera, this.canvas);
    this.orbit.enabled = false;
    this.orbit.enableDamping = true;
    this.orbit.addEventListener('change', () => this.invalidate());

    this._resize = new ResizeObserver(() => this.resize());
    this._resize.observe(container);
    this.resize();

    this._tick = () => this.tick();
    gsap.ticker.add(this._tick);
    this._visibility = () => {
      this.running = !document.hidden;
      if (this.running) this.invalidate();
    };
    document.addEventListener('visibilitychange', this._visibility);
  }

  setLightTarget(x, y, z) {
    this.key.target.position.set(x, y, z);
    this.key.position.copy(this.key.target.position).addScaledVector(this.keyDir, 40);
    this.key.target.updateMatrixWorld();
    this.invalidate();
  }

  resize() {
    const w = Math.max(1, this.container.clientWidth);
    const h = Math.max(1, this.container.clientHeight);
    this.width = w;
    this.height = h;
    this.renderer.setPixelRatio(this.pixelRatio);
    this.renderer.setSize(w, h, false);
    this.rig.setViewport(w, h);
    this.invalidate();
    this.render();
  }

  invalidate() {
    this.dirty = true;
  }

  /** Enter/leave free inspection. The authored camera is untouched and restored on exit. */
  setOrbit(enabled) {
    this.orbit.enabled = enabled;
    this.rig.suspended = enabled;
    if (enabled) {
      const s = this.rig.state;
      this.orbit.target.set(s.tx, s.ty, s.tz);
      this.orbit.update();
    } else {
      this.rig.apply();
    }
    this.invalidate();
  }

  tick() {
    if (!this.running) return;
    if (this.orbit.enabled) this.orbit.update();
    const g = this.governor;
    if (!this.dirty) {
      g.last = 0;
      return;
    }
    const now = performance.now();
    if (g.last) {
      const dt = now - g.last;
      // Count only back-to-back frames (continuous animation), ignore tab switches.
      if (dt < 200) g.slow = dt > 26 ? g.slow + dt : Math.max(0, g.slow - dt * 2);
      if (g.slow > 600 && this.pixelRatio > 1) {
        this.pixelRatio = Math.max(1, this.pixelRatio * 0.8);
        g.slow = 0;
        g.steps++;
        this.renderer.setPixelRatio(this.pixelRatio);
        this.renderer.setSize(this.width, this.height, false);
      }
    }
    g.last = now;
    this.render();
  }

  render() {
    this.dirty = false;
    for (const fn of this.updaters) fn(this);
    this.rig.apply();
    this.renderer.render(this.scene, this.rig.camera);
    this.frames++;
  }

  dispose() {
    gsap.ticker.remove(this._tick);
    document.removeEventListener('visibilitychange', this._visibility);
    this._resize.disconnect();
    this.orbit.dispose();
    this.scene.traverse(o => {
      if (o.geometry) o.geometry.dispose();
      const mats = Array.isArray(o.material) ? o.material : o.material ? [o.material] : [];
      for (const m of mats) {
        for (const k of ['map', 'bumpMap', 'normalMap', 'alphaMap', 'emissiveMap']) if (m[k]) m[k].dispose();
        m.dispose();
      }
    });
    this.key.shadow.map?.dispose();
    this.renderer.dispose();
    this.canvas.remove();
  }
}

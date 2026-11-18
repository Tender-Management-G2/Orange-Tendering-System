import * as THREE from 'three';
import gsap from 'gsap';

// Typography printed on scene surfaces (the identity card, signs). Text is ink in the world, never
// a DOM overlay floating beside the artwork: each label is a small canvas texture on a plane that
// sits on its sheet and moves, tilts and is occluded with it.

const FONT_URL = '/assets/auth-studio/fonts/inter-latin-wght-normal.woff2';
export const FONT_STACK = 'InterVar, "Segoe UI", Arial, sans-serif';
let fontPromise = null;

export function loadFont(url = FONT_URL) {
  if (!fontPromise) {
    const face = new FontFace('InterVar', `url(${url}) format("woff2")`, { weight: '100 900', style: 'normal' });
    fontPromise = face
      .load()
      .then(f => {
        document.fonts.add(f);
        return true;
      })
      .catch(() => false); // system fallback keeps everything usable
  }
  return fontPromise;
}

export class Label extends THREE.Mesh {
  /**
   * @param w,h   plane size in world units (on XZ, facing up)
   * @param ppu   canvas pixels per world unit
   */
  constructor(w, h, { ppu = 150, align = 'left' } = {}) {
    const canvas = document.createElement('canvas');
    canvas.width = Math.ceil(w * ppu);
    canvas.height = Math.ceil(h * ppu);
    const texture = new THREE.CanvasTexture(canvas);
    texture.colorSpace = THREE.SRGBColorSpace;
    texture.anisotropy = 8;
    texture.generateMipmaps = true;
    texture.minFilter = THREE.LinearMipmapLinearFilter;
    const geometry = new THREE.PlaneGeometry(w, h);
    geometry.rotateX(-Math.PI / 2);
    // Anchor: left edge / vertical centre for left-aligned text, centre otherwise.
    if (align === 'left') geometry.translate(w / 2, 0, 0);
    const material = new THREE.MeshBasicMaterial({
      map: texture,
      transparent: true,
      depthWrite: false,
      toneMapped: false,
      polygonOffset: true,
      polygonOffsetFactor: -3,
      polygonOffsetUnits: -3
    });
    super(geometry, material);
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.ppu = ppu;
    this.size = { w, h };
    this.align = align;
    this.renderOrder = 12;
  }

  clear() {
    this.ctx.setTransform(1, 0, 0, 1, 0, 0);
    this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
  }

  commit() {
    this.material.map.needsUpdate = true;
  }

  set opacity(v) {
    this.material.opacity = v;
    this.visible = v > 0.002;
  }
}

/** Static single-line label (kicker text, initials, signs). */
export function drawLine(label, text, { weight = 600, size = 0.6, color = '#292521', tracking = 0, alpha = 1 } = {}) {
  const { ctx, canvas } = label;
  label.clear();
  let px = size * canvas.height;
  ctx.font = `${weight} ${px}px ${FONT_STACK}`;
  if ('letterSpacing' in ctx) ctx.letterSpacing = `${tracking * px}px`;
  // Long text tightens to fit its plate rather than running off it.
  const width = ctx.measureText(text).width;
  const max = canvas.width - 2;
  if (width > max) {
    px *= Math.max(0.5, max / width);
    ctx.font = `${weight} ${px}px ${FONT_STACK}`;
    if ('letterSpacing' in ctx) ctx.letterSpacing = `${tracking * px}px`;
  }
  ctx.fillStyle = color;
  ctx.globalAlpha = alpha;
  ctx.textBaseline = 'middle';
  if (label.align === 'left') {
    ctx.textAlign = 'left';
    ctx.fillText(text, 0, canvas.height * 0.53);
  } else {
    ctx.textAlign = 'center';
    // letterSpacing adds trailing space; nudge to keep optical centring.
    ctx.fillText(text, canvas.width / 2 + (tracking * px) / 2, canvas.height * 0.54);
  }
  ctx.globalAlpha = 1;
  if ('letterSpacing' in ctx) ctx.letterSpacing = '0px';
  label.commit();
}

/**
 * A line of text that is "written": each new character inks in individually as it arrives, so the
 * name on the card appears progressively while the person types. Removed characters leave at once.
 */
export class WrittenLine {
  constructor(label, { weight = 650, size = 0.62, color = '#292521', onChange = () => {} } = {}) {
    this.label = label;
    this.style = { weight, size, color };
    this.chars = [];
    this.onChange = onChange;
    this.reduced = false;
    this.text = '';
    this.draw();
  }

  set(text) {
    text = text ?? '';
    if (text === this.text) return;
    let keep = 0;
    while (keep < text.length && keep < this.chars.length && this.chars[keep].ch === text[keep]) keep++;
    for (const c of this.chars.slice(keep)) gsap.killTweensOf(c);
    this.chars.length = keep;
    const fresh = [];
    for (let i = keep; i < text.length; i++) {
      const c = { ch: text[i], a: this.reduced ? 1 : 0 };
      this.chars.push(c);
      fresh.push(c);
    }
    this.text = text;
    if (fresh.length && !this.reduced) {
      gsap.to(fresh, { a: 1, duration: 0.34, ease: 'power2.out', stagger: Math.min(0.035, 0.4 / fresh.length), onUpdate: () => this.draw() });
    }
    this.draw();
  }

  /** Force a fully inked state (reduced motion, restoring a scene). */
  setInstant(text, amount = 1) {
    for (const c of this.chars) gsap.killTweensOf(c);
    this.text = text;
    const visible = text.length * amount;
    this.chars = [...text].map((ch, i) => ({ ch, a: Math.max(0, Math.min(1, visible - i)) }));
    this.draw();
  }

  draw() {
    const { label } = this;
    const { ctx, canvas } = label;
    label.clear();
    if (!this.chars.length) {
      label.commit();
      this.onChange();
      return;
    }
    let px = this.style.size * canvas.height;
    const text = this.chars.map(c => c.ch).join('');
    ctx.font = `${this.style.weight} ${px}px ${FONT_STACK}`;
    // Fit long names by tightening the size, then truncating.
    let width = ctx.measureText(text).width;
    const max = canvas.width - 2;
    let shown = this.chars;
    if (width > max) {
      const k = Math.max(0.62, max / width);
      px *= k;
      ctx.font = `${this.style.weight} ${px}px ${FONT_STACK}`;
      width = ctx.measureText(text).width;
      if (width > max) {
        let n = this.chars.length;
        while (n > 1 && ctx.measureText(text.slice(0, n) + '…').width > max) n--;
        shown = [...this.chars.slice(0, n), { ch: '…', a: 1 }];
      }
    }
    ctx.textBaseline = 'middle';
    ctx.textAlign = 'left';
    ctx.fillStyle = this.style.color;
    const y = canvas.height * 0.53;
    const str = shown.map(c => c.ch).join('');
    for (let i = 0; i < shown.length; i++) {
      const c = shown[i];
      // Advance by measured prefix width so kerning and shaping stay correct.
      const x = ctx.measureText(str.slice(0, i)).width;
      ctx.globalAlpha = c.a;
      ctx.fillText(c.ch, x, y + (1 - c.a) * px * 0.16);
    }
    ctx.globalAlpha = 1;
    label.commit();
    this.onChange();
  }
}

/** Initials for the monogram: first letters of the first and last word. */
export function initialsOf(name) {
  const words = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (!words.length) return '';
  const first = [...words[0]][0] ?? '';
  const last = words.length > 1 ? [...words[words.length - 1]][0] ?? '' : '';
  return (first + last).toLocaleUpperCase();
}

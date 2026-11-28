// ORANGE INTELLIGENCE - 3D Supplier Universe: the visual engine of the Suppliers tab (Views/Intelligence/Index.cshtml).
//
// STUDY NOTE - this file is presentation only. You do not need its mathematics to explain the backend.
//   * Its data comes from intelligence-suppliers.js (renderSupplierUniverse), which received it from
//     GET /Intelligence/Suppliers -> IntelligenceController.Suppliers -> IntelligenceService.GetSuppliersAsync -> SupplierCalculator.Build.
//   * It never calls the server, never calculates a statistic and never decides what a user may see.
//   * Public functions (window.SupplierUniverse):
//       init(element, callbacks)          builds the Three.js scene once (later calls are ignored)
//       update(view)                      new supplier list / filters / pairs from the server answer
//       selectSupplier(id, competitors)   highlights the supplier shown in the details panel (null = none)
//       resize()                          the card changed size
//       dispose()                         frees the GPU resources
//   * Callbacks it calls: callbacks.onSupplierClick(id or null) and callbacks.onCategoryClick(categoryName);
//     callbacks.colorOf(colorIndex) returns the CSS colour of a category (same colours as the charts).
// History: restored from the original orange-intelligence-universe.js (added in abec812, last version in 3ccc1df) and adapted
// to the new per-tab JSON. The old monolithic /Intelligence/Workspace answer, the 2D network and the tender mode are gone.
(function () {
  'use strict';

  let engine = null;
  let booting = false;
  let pendingView = null;
  let pendingSelection = null;

  window.SupplierUniverse = {
    init(host, callbacks) {
      if (engine || booting || !host) return;
      booting = true;
      boot(host, callbacks || {}).then(created => {
        engine = created;
        booting = false;
        if (pendingView) engine.update(pendingView);
        if (pendingSelection) engine.selectSupplier(pendingSelection.id, pendingSelection.competitors);
        pendingView = null;
        pendingSelection = null;
      }).catch(error => {
        booting = false;
        console.warn('Supplier Universe unavailable:', error);
        showFallback(host);
      });
    },
    update(view) { if (engine) engine.update(view); else pendingView = view; },
    selectSupplier(id, competitors) {
      if (engine) engine.selectSupplier(id, competitors || []);
      else pendingSelection = { id, competitors: competitors || [] };
    },
    resize() { if (engine) engine.resize(); },
    dispose() { if (engine) engine.dispose(); engine = null; }
  };

  function showFallback(host) {
    host.classList.add('is-fallback');
    const box = host.querySelector('[data-uni-fallback]');
    if (box) box.hidden = false;
  }

  function webglAvailable() {
    try {
      const probe = document.createElement('canvas');
      return !!(probe.getContext('webgl2') || probe.getContext('webgl'));
    } catch {
      return false;
    }
  }

  async function boot(host, cb) {
    if (!webglAvailable()) throw new Error('WebGL is not available in this browser.');
    const THREE = await import(host.dataset.three);

    const $ = s => host.querySelector(s);
    const stage = $('[data-uni-stage]'), canvas = $('[data-uni-canvas]'), labelLayer = $('[data-uni-labels]'), tip = $('[data-uni-tip]');
    const caption = $('[data-uni-caption]'), legendBox = $('[data-uni-legend]'), focusChip = $('[data-uni-focus]'), emptyBox = $('[data-uni-empty]');
    const descBox = $('[data-uni-desc]'), panelEl = $('[data-uni-panel]'), toolsEl = $('[data-uni-tools]'), hintBox = $('[data-uni-hint]');
    const arabic = host.closest('[data-ui-language]')?.dataset.uiLanguage === 'ar';
    const T = (en, ar) => (arabic ? ar : en);
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    const weak = (navigator.hardwareConcurrency || 8) <= 4 || window.matchMedia('(max-width: 760px)').matches;
    const clean = s => String(s ?? '').replace(/^RADAR DEMO (- )?/, '');
    const esc = s => clean(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmtInt = n => (n == null ? '—' : Number(n).toLocaleString('en-US'));
    const fmtPct = n => (n == null ? '—' : `${Math.round(n * 100)}%`);
    const R = 1;
    const disposables = [];
    const keep = x => { disposables.push(x); return x; };

    // ---------- renderer, camera, groups ----------
    const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, powerPreference: 'high-performance' });
    renderer.setClearColor(0x000000, 0);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, weak ? 1.25 : 1.75));
    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(34, 1, 0.05, 200);
    const HOME = { dist: 4.15, focus: 3.55 };
    camera.position.set(0, 0, HOME.dist);
    const world = new THREE.Group(); scene.add(world);   // rotates (orbit + drag): everything attached to the sphere surface
    const fixed = new THREE.Group(); scene.add(fixed);   // pedestal, orbit rings and stars stay put
    const BASE_TILT = new THREE.Quaternion().setFromEuler(new THREE.Euler(0.36, -0.5, 0.08));
    world.quaternion.copy(BASE_TILT);

    // ---------- palette (category colours come from the page CSS through callbacks.colorOf) ----------
    const cssVar = name => getComputedStyle(host).getPropertyValue(name).trim();
    const isDark = () => document.documentElement.dataset.theme !== 'light';
    let P = {};
    function readPalette() {
      const dark = isDark();
      P = {
        dark,
        orange: new THREE.Color(cssVar('--rd-orange-strong') || '#ff7900'),
        link: new THREE.Color(dark ? '#ffb070' : '#d4640a'),
        strong: new THREE.Color(dark ? '#ffd2a8' : '#b34d00'),
        neutral: cssVar('--rd-neutral') || '#7f8ea3'
      };
    }
    readPalette();
    const colorCss = index => (cb.colorOf ? cb.colorOf(index) : '') || P.neutral;

    // ---------- textures ----------
    function radialTexture(stops) {
      const c = document.createElement('canvas'); c.width = c.height = 128;
      const g = c.getContext('2d'); const grd = g.createRadialGradient(64, 64, 0, 64, 64, 64);
      stops.forEach(([o, col]) => grd.addColorStop(o, col)); g.fillStyle = grd; g.fillRect(0, 0, 128, 128);
      const t = keep(new THREE.CanvasTexture(c)); t.colorSpace = THREE.SRGBColorSpace; return t;
    }
    const haloTex = radialTexture([[0, 'rgba(255,255,255,0.95)'], [0.18, 'rgba(255,255,255,0.55)'], [0.45, 'rgba(255,255,255,0.14)'], [1, 'rgba(255,255,255,0)']]);
    const dotTex = radialTexture([[0, 'rgba(255,255,255,1)'], [0.35, 'rgba(255,255,255,0.6)'], [1, 'rgba(255,255,255,0)']]);
    const ringTex = (() => {
      const c = document.createElement('canvas'); c.width = c.height = 128; const g = c.getContext('2d');
      g.strokeStyle = '#fff'; g.lineWidth = 7; g.beginPath(); g.arc(64, 64, 52, 0, Math.PI * 2); g.stroke();
      g.globalAlpha = 0.35; g.lineWidth = 3; g.beginPath(); g.arc(64, 64, 60, 0, Math.PI * 2); g.stroke();
      const t = keep(new THREE.CanvasTexture(c)); t.colorSpace = THREE.SRGBColorSpace; return t;
    })();

    // ---------- the glass sphere (shell + interior + graticule + surface dust) ----------
    const shellMat = keep(new THREE.ShaderMaterial({
      transparent: true, depthWrite: false, side: THREE.FrontSide,
      uniforms: { uTop: { value: new THREE.Color() }, uBottom: { value: new THREE.Color() }, uBody: { value: new THREE.Color() }, uBodyA: { value: 0.1 }, uRimA: { value: 0.75 } },
      vertexShader: `varying vec3 vN; varying vec3 vV; varying float vY;
        void main(){ vec4 mv = modelViewMatrix * vec4(position,1.0); vN = normalize(normalMatrix * normal); vV = normalize(-mv.xyz);
          vY = (modelMatrix * vec4(position,1.0)).y; gl_Position = projectionMatrix * mv; }`,
      fragmentShader: `uniform vec3 uTop; uniform vec3 uBottom; uniform vec3 uBody; uniform float uBodyA; uniform float uRimA; varying vec3 vN; varying vec3 vV; varying float vY;
        void main(){ float f = 1.0 - max(dot(vN, vV), 0.0); float rim = pow(f, 2.6); float h = smoothstep(-1.0, 1.0, vY);
          vec3 rc = mix(uBottom, uTop, h); vec3 c = mix(uBody, rc, rim); float a = uBodyA * (1.0 - rim) + rim * uRimA;
          float spec = pow(max(dot(reflect(-normalize(vec3(-0.5,0.7,0.6)), vN), vV), 0.0), 40.0) * 0.35;
          gl_FragColor = vec4(c + spec, a + spec * 0.5); }`
    }));
    const shellGeo = keep(new THREE.SphereGeometry(R, weak ? 48 : 72, weak ? 32 : 48));
    const shell = new THREE.Mesh(shellGeo, shellMat); shell.renderOrder = 2; scene.add(shell); // stable lighting while the content orbits

    const innerMat = keep(new THREE.ShaderMaterial({
      transparent: true, depthWrite: false, side: THREE.BackSide, uniforms: { uC: { value: new THREE.Color() }, uA: { value: 0.35 } },
      vertexShader: 'varying vec3 vN; varying vec3 vV; void main(){ vec4 mv = modelViewMatrix*vec4(position,1.0); vN = normalize(normalMatrix*normal); vV = normalize(-mv.xyz); gl_Position = projectionMatrix*mv; }',
      fragmentShader: 'uniform vec3 uC; uniform float uA; varying vec3 vN; varying vec3 vV; void main(){ float f = abs(dot(vN, vV)); gl_FragColor = vec4(uC, uA * (0.35 + 0.65 * f)); }'
    }));
    const inner = new THREE.Mesh(keep(new THREE.SphereGeometry(R * 0.995, 48, 32)), innerMat); inner.renderOrder = 0; scene.add(inner);

    const gratMat = keep(new THREE.LineBasicMaterial({ transparent: true, opacity: 0.08, depthWrite: false }));
    {
      const pts = [];
      for (let lat = -60; lat <= 60; lat += 30) {
        const y = Math.sin(lat * Math.PI / 180) * R, r = Math.cos(lat * Math.PI / 180) * R;
        for (let i = 0; i < 96; i++) { const a = i / 96 * Math.PI * 2, b = (i + 1) / 96 * Math.PI * 2; pts.push(r * Math.cos(a), y, r * Math.sin(a), r * Math.cos(b), y, r * Math.sin(b)); }
      }
      for (let lon = 0; lon < 180; lon += 30) {
        const a0 = lon * Math.PI / 180;
        for (let i = 0; i < 96; i++) {
          const t0 = i / 96 * Math.PI * 2, t1 = (i + 1) / 96 * Math.PI * 2;
          pts.push(R * Math.cos(t0) * Math.cos(a0), R * Math.sin(t0), R * Math.cos(t0) * Math.sin(a0), R * Math.cos(t1) * Math.cos(a0), R * Math.sin(t1), R * Math.cos(t1) * Math.sin(a0));
        }
      }
      const g = keep(new THREE.BufferGeometry()); g.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
      world.add(new THREE.LineSegments(g, gratMat));
    }
    const dustMat = keep(new THREE.PointsMaterial({ size: 0.012, map: dotTex, transparent: true, depthWrite: false, opacity: 0.5, blending: THREE.AdditiveBlending }));
    {
      const n = weak ? 380 : 700, pts = new Float32Array(n * 3);
      for (let i = 0; i < n; i++) { const u = Math.random() * 2 - 1, a = Math.random() * Math.PI * 2, s = Math.sqrt(1 - u * u); pts.set([s * Math.cos(a) * R * 1.002, u * R * 1.002, s * Math.sin(a) * R * 1.002], i * 3); }
      const g = keep(new THREE.BufferGeometry()); g.setAttribute('position', new THREE.BufferAttribute(pts, 3)); world.add(new THREE.Points(g, dustMat));
    }

    // Category zones: glowing spherical caps drawn by one shader, so a chosen category becomes a visible region of the sphere.
    const MAXZ = 12;
    const zoneMat = keep(new THREE.ShaderMaterial({
      transparent: true, depthWrite: false, side: THREE.FrontSide,
      uniforms: {
        uDir: { value: Array.from({ length: MAXZ }, () => new THREE.Vector3(0, 1, 0)) },
        uCos: { value: new Array(MAXZ).fill(1) },
        uCol: { value: Array.from({ length: MAXZ }, () => new THREE.Color()) },
        uA: { value: new Array(MAXZ).fill(0) },
        uCount: { value: 0 }, uTime: { value: 0 }, uGain: { value: 1 }
      },
      vertexShader: `varying vec3 vP; varying vec3 vN; varying vec3 vV;
        void main(){ vP = position; vec4 mv = modelViewMatrix*vec4(position,1.0); vN = normalize(normalMatrix*normal); vV = normalize(-mv.xyz); gl_Position = projectionMatrix*mv; }`,
      fragmentShader: `#define MAXZ ${MAXZ}
        uniform vec3 uDir[MAXZ]; uniform float uCos[MAXZ]; uniform vec3 uCol[MAXZ]; uniform float uA[MAXZ]; uniform int uCount; uniform float uTime; uniform float uGain;
        varying vec3 vP; varying vec3 vN; varying vec3 vV;
        void main(){
          vec3 p = normalize(vP); vec3 acc = vec3(0.0); float alpha = 0.0; float facing = clamp(dot(vN, vV), 0.0, 1.0);
          for (int i = 0; i < MAXZ; i++) {
            if (i >= uCount) break;
            float d = dot(p, uDir[i]); float ca = uCos[i]; float span = max(1.0 - ca, 0.0005);
            float inside = smoothstep(ca - span * 0.03, ca + span * 0.05, d);
            float t = clamp((d - ca) / span, 0.0, 1.0);
            float e = (d - ca) / (span * 0.045 + 0.0012); float edge = exp(-e * e);
            float ripple = inside * pow(0.5 + 0.5 * sin(t * 16.0 - uTime * 2.2), 12.0) * (1.0 - t);
            float v = (inside * (0.07 + 0.11 * t) + ripple * 0.16 + edge * 0.9) * uA[i];
            acc += uCol[i] * v; alpha += v;
          }
          if (alpha < 0.003) discard;
          gl_FragColor = vec4(acc / alpha * uGain, min(alpha, 0.85) * (0.3 + 0.7 * facing));
          #include <colorspace_fragment>
        }`
    }));
    const zoneSphere = new THREE.Mesh(keep(new THREE.SphereGeometry(R * 1.005, weak ? 64 : 112, weak ? 40 : 72)), zoneMat);
    zoneSphere.renderOrder = 2.5; world.add(zoneSphere);

    // Orbit rings with travelling sparks, the pedestal below the sphere, a soft core and stars (decorative, non-interactive).
    const ringMats = [], orbitRings = [];
    [[1.28, 0.42, 0.2], [1.46, -0.28, 1.1]].forEach(([rad, tilt, rot], i) => {
      const pts = []; for (let k = 0; k <= 160; k++) { const a = k / 160 * Math.PI * 2; pts.push(new THREE.Vector3(Math.cos(a) * rad, 0, Math.sin(a) * rad)); }
      const m = keep(new THREE.LineBasicMaterial({ transparent: true, opacity: 0.22, depthWrite: false })); ringMats.push(m);
      const line = new THREE.Line(keep(new THREE.BufferGeometry().setFromPoints(pts)), m);
      const g = new THREE.Group(); g.rotation.set(tilt, rot, 0.15 * (i ? -1 : 1)); g.add(line);
      const spark = new THREE.Sprite(keep(new THREE.SpriteMaterial({ map: dotTex, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending }))); spark.scale.setScalar(0.07);
      g.add(spark); fixed.add(g); orbitRings.push({ spark, rad, speed: i ? -0.07 : 0.05, phase: i * 2 });
    });
    const pedMats = [];
    [1.05, 1.32, 1.62].forEach((rad, i) => {
      const pts = []; for (let k = 0; k <= 128; k++) { const a = k / 128 * Math.PI * 2; pts.push(new THREE.Vector3(Math.cos(a) * rad, 0, Math.sin(a) * rad)); }
      const m = keep(new THREE.LineBasicMaterial({ transparent: true, opacity: 0.32 - i * 0.08, depthWrite: false })); pedMats.push(m);
      const l = new THREE.Line(keep(new THREE.BufferGeometry().setFromPoints(pts)), m); l.position.y = -R * 1.12 - i * 0.03; fixed.add(l);
    });
    const pedGlow = new THREE.Sprite(keep(new THREE.SpriteMaterial({ map: haloTex, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, opacity: 0.4 })));
    pedGlow.scale.set(3.4, 0.55, 1); pedGlow.position.y = -R * 1.13; fixed.add(pedGlow);
    const core = new THREE.Sprite(keep(new THREE.SpriteMaterial({ map: haloTex, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, opacity: 0.18 })));
    core.scale.setScalar(1.5); scene.add(core);
    const atmosphere = new THREE.Sprite(keep(new THREE.SpriteMaterial({ map: haloTex, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, opacity: 0.2 })));
    atmosphere.scale.setScalar(3.3); atmosphere.renderOrder = -1; atmosphere.position.z = -0.6; scene.add(atmosphere);
    const starMat = keep(new THREE.PointsMaterial({ size: 0.03, map: dotTex, transparent: true, depthWrite: false, opacity: 0.6, blending: THREE.AdditiveBlending }));
    {
      const n = weak ? 220 : 420, pts = new Float32Array(n * 3);
      for (let i = 0; i < n; i++) { const u = Math.random() * 2 - 1, a = Math.random() * Math.PI * 2, s = Math.sqrt(1 - u * u), d = 9 + Math.random() * 10; pts.set([s * Math.cos(a) * d, u * d * 0.6, -Math.abs(s * Math.sin(a) * d) - 2], i * 3); }
      const g = keep(new THREE.BufferGeometry()); g.setAttribute('position', new THREE.BufferAttribute(pts, 3)); fixed.add(new THREE.Points(g, starMat));
    }

    function applyTheme() {
      readPalette();
      const d = P.dark;
      shellMat.uniforms.uTop.value.set(d ? '#5aa8ff' : '#7aa6dd'); shellMat.uniforms.uBottom.value.set(d ? '#ff8a2b' : '#ff9a4a');
      shellMat.uniforms.uBody.value.set(d ? '#0e2240' : '#e9f0f8'); shellMat.uniforms.uBodyA.value = d ? 0.16 : 0.32; shellMat.uniforms.uRimA.value = d ? 0.85 : 0.7;
      innerMat.uniforms.uC.value.set(d ? '#061226' : '#dbe5f1'); innerMat.uniforms.uA.value = d ? 0.5 : 0.35;
      gratMat.color.set(d ? '#9cc4ff' : '#33507a'); gratMat.opacity = d ? 0.075 : 0.1;
      dustMat.color.set(d ? '#9fc8ff' : '#6b86ad'); dustMat.opacity = d ? 0.45 : 0.25; dustMat.blending = d ? THREE.AdditiveBlending : THREE.NormalBlending;
      ringMats.forEach(m => { m.color.set(d ? '#ffb070' : '#d4640a'); m.opacity = d ? 0.22 : 0.3; });
      pedMats.forEach((m, i) => { m.color.set(d ? '#ff8a2b' : '#d4640a'); m.opacity = (d ? 0.34 : 0.3) - i * 0.08; });
      orbitRings.forEach(o => o.spark.material.color.set(d ? '#ffc48c' : '#ff7900'));
      pedGlow.material.color.set(d ? '#ff7a1a' : '#ffb27a'); pedGlow.material.opacity = d ? 0.4 : 0.35; pedGlow.material.blending = d ? THREE.AdditiveBlending : THREE.NormalBlending;
      core.material.color.set(d ? '#3b7bff' : '#ffffff'); core.material.opacity = d ? 0.16 : 0;
      atmosphere.material.color.set(d ? '#2f6fd6' : '#ffd2a8'); atmosphere.material.opacity = d ? 0.22 : 0.18; atmosphere.material.blending = d ? THREE.AdditiveBlending : THREE.NormalBlending;
      starMat.opacity = d ? 0.55 : 0;
      zoneMat.uniforms.uGain.value = d ? 1.15 : 0.92;
      zoneMat.blending = d ? THREE.AdditiveBlending : THREE.NormalBlending;
      nodes.forEach(paintNode);
      zones.forEach(z => { const member = nodes.find(n => n.cat === z.key); if (member) { z.css = member.css; z.color.set(member.css); } });
      renderFocusChip();
      renderLegend(); rebuildLinks(); wake();
    }

    // ---------- supplier nodes ----------
    const nodeGeo = keep(new THREE.SphereGeometry(1, weak ? 16 : 24, weak ? 12 : 16));
    const nodeVS = 'varying vec3 vN; varying vec3 vV; void main(){ vec4 mv = modelViewMatrix*vec4(position,1.0); vN = normalize(normalMatrix*normal); vV = normalize(-mv.xyz); gl_Position = projectionMatrix*mv; }';
    const nodeFS = `uniform vec3 uColor; uniform float uOpacity; uniform float uGlow; varying vec3 vN; varying vec3 vV;
      void main(){ vec3 L = normalize(vec3(-0.45,0.65,0.62)); float ndv = max(dot(vN,vV),0.0); float diff = 0.45 + 0.55*max(dot(vN,L),0.0);
        float spec = pow(max(dot(reflect(-L,vN),vV),0.0), 30.0) * 0.6; float rim = pow(1.0-ndv, 2.2);
        vec3 c = uColor * diff * (0.92 + uGlow) + vec3(spec) + mix(uColor, vec3(1.0), 0.45) * rim * (0.55 + uGlow);
        gl_FragColor = vec4(c, uOpacity);
        #include <colorspace_fragment>
      }`;
    let nodes = [];             // { id, s, cat, colorIndex, css, mesh, halo, ring, r, pos, from, to, delay, alpha, alphaT, size, sizeT, rad, radFrom, phase, screen }
    const byId = new Map();
    let zones = [];             // { key, label, count, css, color, dir, dirT, angle, angleT, alpha, alphaT, strong }
    const st = {
      mode: 'universe', category: '', shown: null, rows: new Map(), pairs: [], competitionMinimum: 2, dataKey: null,
      selected: null, competitors: [], hover: null, motion: !reduced.matches, labels: true,
      drag: null, pointers: new Map(), pinch: 0, vel: { x: 0, y: 0 }, qTarget: null, distTarget: HOME.dist, offset: 0, offsetTarget: 0,
      trans: null, linkFade: 1, lastInteract: 0, inView: true, ready: false, clock: 0, focusDir: null, sway: 0
    };

    function paintNode(n) {
      n.css = colorCss(n.colorIndex);
      const col = new THREE.Color(n.css);
      n.mesh.material.uniforms.uColor.value.copy(col);
      n.halo.material.color.copy(col);
      n.halo.material.blending = P.dark ? THREE.AdditiveBlending : THREE.NormalBlending;
      n.ring.material.color.copy(P.orange);
    }

    function disposeNode(n) {
      world.remove(n.mesh, n.halo, n.ring);
      n.mesh.material.dispose(); n.halo.material.dispose(); n.ring.material.dispose();
    }

    function buildNodes(list) {
      nodes.forEach(disposeNode);
      nodes = []; byId.clear();
      const max = Math.max(1, ...list.map(s => s.participations || 0));
      list.forEach((s, i) => {
        const mat = new THREE.ShaderMaterial({ uniforms: { uColor: { value: new THREE.Color() }, uOpacity: { value: 1 }, uGlow: { value: 0 } }, vertexShader: nodeVS, fragmentShader: nodeFS, transparent: true });
        const mesh = new THREE.Mesh(nodeGeo, mat); mesh.renderOrder = 3;
        const halo = new THREE.Sprite(new THREE.SpriteMaterial({ map: haloTex, transparent: true, depthWrite: false, opacity: 0.5 })); halo.renderOrder = 1;
        const ring = new THREE.Sprite(new THREE.SpriteMaterial({ map: ringTex, transparent: true, depthWrite: false, depthTest: false, opacity: 0 })); ring.renderOrder = 5;
        // Size shows one server value: closed bidding cycles with an offer (square-root scale). Zero keeps a visible base size.
        const r = 0.032 + 0.042 * Math.sqrt((s.participations || 0) / max);
        const n = {
          id: s.supplierId, s, cat: (s.category || '').trim() || 'Uncategorized', colorIndex: s.categoryColor, css: '', mesh, halo, ring, r,
          pos: new THREE.Vector3(), from: new THREE.Vector3(), to: new THREE.Vector3(), delay: 0,
          alpha: 0, alphaT: 1, size: 1, sizeT: 1, rad: 1, radFrom: 1, phase: i * 1.7,
          screen: { x: 0, y: 0, front: 1, rpx: 4 }
        };
        paintNode(n); world.add(mesh, halo, ring); nodes.push(n); byId.set(n.id, n);
      });
    }

    // ---------- layouts (unit directions in the sphere's own coordinates) ----------
    const fib = (i, n) => {
      const y = n === 1 ? 0 : 1 - (i / (n - 1)) * 2, r = Math.sqrt(Math.max(0, 1 - y * y)), a = i * Math.PI * (3 - Math.sqrt(5));
      return new THREE.Vector3(Math.cos(a) * r, y, Math.sin(a) * r);
    };
    function basis(c) {
      const up = Math.abs(c.y) > 0.9 ? new THREE.Vector3(1, 0, 0) : new THREE.Vector3(0, 1, 0);
      const u = new THREE.Vector3().crossVectors(up, c).normalize(); const v = new THREE.Vector3().crossVectors(c, u).normalize(); return [u, v];
    }
    // Members of one group on a sunflower cap around direction c.
    function cap(c, ids, spacing, out) {
      const [u, v] = basis(c);
      ids.forEach((id, k) => {
        const rr = spacing * Math.sqrt(k + (ids.length > 1 ? 0.6 : 0)), a = k * 2.39996;
        out.set(id, c.clone().addScaledVector(u, Math.cos(a) * rr).addScaledVector(v, Math.sin(a) * rr).normalize());
      });
      return out;
    }
    const capAngle = (count, spacing) => spacing * Math.sqrt(count + 0.6) + 0.06;
    const capSpacing = count => (count > 60 ? 0.085 : count > 30 ? 0.11 : count > 12 ? 0.135 : 0.165);
    const byActivity = (a, b) => (b.s.participations || 0) - (a.s.participations || 0) || a.id - b.id;
    // The direction of the sphere that currently faces the viewer.
    const frontDir = () => new THREE.Vector3(0, 0.06, 1).normalize().applyQuaternion(world.quaternion.clone().invert());

    function universeLayout() {
      // Ordered by category, then activity, along a Fibonacci spiral: an even spread with gentle category bands.
      const order = nodes.slice().sort((a, b) => a.cat.localeCompare(b.cat) || byActivity(a, b));
      const map = new Map(); order.forEach((n, i) => map.set(n.id, fib(i, order.length)));
      return { map, zones: [], focus: null };
    }

    // A chosen category: its suppliers gather on one cap facing the viewer; the others spread over the rest of the sphere.
    function focusLayout(category) {
      const members = nodes.filter(n => n.cat === category).sort(byActivity);
      const others = nodes.filter(n => n.cat !== category).sort((a, b) => a.cat.localeCompare(b.cat) || byActivity(a, b));
      if (!members.length) return universeLayout();
      const c = frontDir(); const spacing = capSpacing(members.length); const angle = capAngle(members.length, spacing);
      const map = cap(c, members.map(n => n.id), spacing, new Map());
      const start = Math.min(Math.PI * 0.82, angle + 0.3); const [u, v] = basis(c);
      others.forEach((n, i) => {
        const p = fib(i, others.length); const polar = Math.acos(Math.max(-1, Math.min(1, p.y))); const az = Math.atan2(p.z, p.x);
        const a = start + polar * (Math.PI - start) / Math.PI;
        map.set(n.id, c.clone().multiplyScalar(Math.cos(a)).addScaledVector(u, Math.sin(a) * Math.cos(az)).addScaledVector(v, Math.sin(a) * Math.sin(az)).normalize());
      });
      const m = members[0];
      return { map, focus: c, zones: [{ key: category, label: clean(category), count: members.length, css: m.css, dir: c, angle, strong: true }] };
    }

    // Category mode: every category is its own cap; the biggest sits in the centre of the visible hemisphere, the rest on rings around it.
    function categoryLayout() {
      const groups = new Map();
      nodes.forEach(n => { if (!groups.has(n.cat)) groups.set(n.cat, []); groups.get(n.cat).push(n); });
      const order = [...groups.entries()].sort((a, b) => b[1].length - a[1].length || a[0].localeCompare(b[0]));
      const F = frontDir(); const [u, v] = basis(F);
      const rings = [{ n: 1, t: 0 }, { n: 6, t: 0.84 }, { n: 10, t: 1.55 }, { n: 14, t: 2.25 }];
      const slots = [];
      rings.forEach(r => { for (let k = 0; k < r.n; k++) { const ph = (k / r.n) * Math.PI * 2 + (r.t > 1 ? 0.3 : -Math.PI / 2 + 0.6); slots.push(F.clone().multiplyScalar(Math.cos(r.t)).addScaledVector(u, Math.cos(ph) * Math.sin(r.t)).addScaledVector(v, Math.sin(ph) * Math.sin(r.t)).normalize()); } });
      const spacing = nodes.length > 120 ? 0.075 : nodes.length > 60 ? 0.095 : 0.13;
      const map = new Map(); const out = [];
      order.forEach(([cat, list], i) => {
        const c = slots[i] || fib(i, order.length); list.sort(byActivity); cap(c, list.map(n => n.id), spacing, map);
        out.push({ key: cat, label: clean(cat), count: list.length, css: list[0].css, dir: c, angle: capAngle(list.length, spacing), strong: cat === st.category });
      });
      const chosen = out.find(z => z.key === st.category);
      return { map, zones: out, focus: chosen ? chosen.dir : null };
    }

    function layoutFor() {
      if (!nodes.length) return { map: new Map(), zones: [], focus: null };
      if (st.mode === 'category') return categoryLayout();
      if (st.category) return focusLayout(st.category);
      return universeLayout();
    }

    let layout = { map: new Map(), zones: [], focus: null };
    // entry = the nodes were just created: they rise from the core of the sphere.
    function applyLayout(animate, entry) {
      layout = layoutFor();
      const instant = !animate || reduced.matches;
      const focusMembers = layout.zones.find(z => z.strong);
      let rank = 0;
      nodes.forEach(n => {
        const target = layout.map.get(n.id) || fib(0, 1);
        n.from.copy(n.pos.lengthSq() ? n.pos : target);
        n.to.copy(target);
        n.radFrom = entry ? 0.08 : n.rad;
        // Stagger: a chosen category streams in member by member; the rest leave together.
        n.delay = instant ? 0 : focusMembers && n.cat === focusMembers.key ? Math.min(520, 120 + (rank++) * 14) : entry ? Math.random() * 420 : Math.random() * 180;
        if (instant) { n.pos.copy(n.to); n.rad = 1; }
      });
      st.trans = instant ? null : { t0: performance.now(), dur: 1250 };
      setZones(layout.zones, instant);
      st.focusDir = layout.focus;
      if (st.selected != null && byId.has(st.selected)) faceDirection(layout.map.get(st.selected), HOME.focus);
      else if (layout.focus) faceDirection(layout.focus, st.mode === 'category' ? HOME.focus + 0.2 : HOME.focus);
      else home();
      updateEmphasis(); rebuildLinks(); wake();
    }

    function setZones(list, instant) {
      const next = [];
      list.slice(0, MAXZ).forEach(z => {
        const old = zones.find(o => o.key === z.key);
        const zone = old || { key: z.key, color: new THREE.Color(), dir: z.dir.clone(), angle: z.angle * 0.35, alpha: 0 };
        Object.assign(zone, { label: z.label, count: z.count, css: z.css, dirT: z.dir.clone(), angleT: z.angle, alphaT: z.strong ? 1 : st.category ? 0.22 : 0.55, strong: z.strong, leaving: false });
        zone.color.set(z.css);
        if (instant) { zone.dir.copy(zone.dirT); zone.angle = zone.angleT; zone.alpha = zone.alphaT; }
        next.push(zone);
      });
      zones.filter(z => !next.includes(z)).forEach(z => { z.alphaT = 0; z.leaving = true; next.push(z); });
      zones = next.slice(0, MAXZ);
    }

    // ---------- emphasis (server filters, selection, competition mode) ----------
    function pairedIds() {
      const out = new Set(); st.pairs.forEach(p => { out.add(p.supplierA); out.add(p.supplierB); }); return out;
    }
    function updateEmphasis() {
      const competitors = new Set(st.competitors.map(c => c.supplierB));
      const paired = st.mode === 'competition' ? pairedIds() : null;
      nodes.forEach(n => {
        const shown = !st.shown || st.shown.has(n.id);
        let a = shown ? 1 : 0.13, s = shown ? 1 : 0.62;
        if (paired && shown && !paired.has(n.id) && n.id !== st.selected) a = 0.32;
        if (st.selected != null) {
          if (n.id === st.selected) { a = 1; s = 1; }
          else if (competitors.has(n.id)) a = Math.max(a, 0.95);
          else a = Math.min(a, 0.24);
        }
        n.alphaT = a; n.sizeT = s;
      });
    }

    // ---------- links: glowing arcs along the sphere (only real pairs from the server) ----------
    let links = [], flows = [];
    function arcCurve(a, b) {
      const pts = []; const A = a.clone().normalize(), B = b.clone().normalize(); const ang = A.angleTo(B);
      const h = 0.025 + 0.12 * (ang / Math.PI);
      for (let i = 0; i <= 32; i++) {
        const t = i / 32; let p;
        if (ang < 1e-3) p = A.clone();
        else { const s = Math.sin(ang); p = A.clone().multiplyScalar(Math.sin((1 - t) * ang) / s).add(B.clone().multiplyScalar(Math.sin(t * ang) / s)); }
        pts.push(p.normalize().multiplyScalar(R * (1.014 + h * Math.sin(Math.PI * t))));
      }
      return new THREE.CatmullRomCurve3(pts);
    }
    function addLink(a, b, strength, opacity, withFlow) {
      const A = byId.get(a), B = byId.get(b); if (!A || !B) return;
      const curve = arcCurve(A.to, B.to);
      const geo = new THREE.TubeGeometry(curve, 40, 0.0018 + 0.0042 * strength, 5, false);
      const color = strength > 0.66 ? P.strong : P.link;
      const mat = new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0, depthWrite: false, blending: P.dark ? THREE.AdditiveBlending : THREE.NormalBlending });
      const mesh = new THREE.Mesh(geo, mat); mesh.renderOrder = 4; world.add(mesh);
      links.push({ mesh, base: opacity });
      if (withFlow && !reduced.matches) {
        const count = 1 + Math.round(strength * 2);
        for (let i = 0; i < count; i++) {
          const sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: dotTex, color, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, opacity: 0 }));
          sp.scale.setScalar(0.04 + strength * 0.02); sp.renderOrder = 6; world.add(sp); flows.push({ sp, curve, t: i / count, speed: 0.16 + strength * 0.12 });
        }
      }
    }
    function clearLinks() {
      links.forEach(o => { world.remove(o.mesh); o.mesh.geometry.dispose(); o.mesh.material.dispose(); }); links = [];
      flows.forEach(f => { world.remove(f.sp); f.sp.material.dispose(); }); flows = [];
    }
    function rebuildLinks() {
      clearLinks();
      if (!nodes.length) return;
      const ok = id => byId.has(id);
      if (st.selected != null && ok(st.selected)) {
        // The selected supplier's competitors (from GET /Intelligence/SupplierDetails).
        const max = Math.max(1, ...st.competitors.map(c => c.sharedRounds));
        st.competitors.forEach(c => addLink(st.selected, c.supplierB, c.sharedRounds / max, 0.9, true));
      } else if (st.mode === 'competition') {
        // The strongest supplier pairs of the current filters (SuppliersData.Pairs).
        const max = Math.max(1, ...st.pairs.map(p => p.sharedRounds));
        st.pairs.forEach(p => addLink(p.supplierA, p.supplierB, p.sharedRounds / max, p.sharedRounds >= st.competitionMinimum ? 0.85 : 0.45, p.sharedRounds >= st.competitionMinimum));
      } else if (st.hover != null) {
        const max = Math.max(1, ...st.pairs.map(p => p.sharedRounds));
        st.pairs.filter(p => p.supplierA === st.hover || p.supplierB === st.hover)
          .forEach(p => addLink(p.supplierA, p.supplierB, p.sharedRounds / max, 0.75, true));
      }
      st.linkFade = 0;
    }

    // ---------- camera and orientation ----------
    function faceDirection(localDir, dist) {
      if (!localDir) return;
      const cur = localDir.clone().applyQuaternion(world.quaternion).normalize();
      const want = new THREE.Vector3(0, 0.04, 1).normalize();
      st.qTarget = new THREE.Quaternion().setFromUnitVectors(cur, want).multiply(world.quaternion.clone());
      st.distTarget = dist ?? HOME.focus;
    }
    function home() { st.qTarget = null; st.distTarget = HOME.dist; }
    const focused = () => st.selected != null || !!st.focusDir;

    // ---------- labels (DOM text: crisp, collision-aware, front hemisphere only) ----------
    const labelEls = new Map();
    function labelEl(key, html, cls) {
      let el = labelEls.get(key);
      if (!el) { el = document.createElement('div'); labelLayer.append(el); labelEls.set(key, el); el._html = ''; el._cls = null; }
      if (el._html !== html) { el.innerHTML = html; el._html = html; el._w = 0; }
      if (el._cls !== cls) { el.className = `oi-label ${cls}`; el._cls = cls; el._w = 0; }
      return el;
    }
    function clearLabels() { labelEls.forEach(el => el.remove()); labelEls.clear(); }
    const v3 = new THREE.Vector3();
    function project(worldPos, out) {
      v3.copy(worldPos).project(camera);
      out.x = (v3.x * 0.5 + 0.5) * size.w; out.y = (-v3.y * 0.5 + 0.5) * size.h; out.z = v3.z; return out;
    }
    let overlayBoxes = [];
    function measureOverlays() {
      const sr = stage.getBoundingClientRect();
      overlayBoxes = [panelEl, toolsEl, legendBox, focusChip, caption].filter(el => el && !el.hidden && el.offsetParent !== null).map(el => {
        const r = el.getBoundingClientRect();
        return { x1: r.left - sr.left - 6, y1: r.top - sr.top - 6, x2: r.right - sr.left + 6, y2: r.bottom - sr.top + 6 };
      }).filter(b => b.x2 > 0 && b.y2 > 0 && b.x1 < sr.width && b.y1 < sr.height);
    }
    const tmpA = new THREE.Vector3();
    function updateLabels() {
      const used = new Set(); const boxes = overlayBoxes.slice();
      const place = (key, html, cls, sx, sy, rpx, prio, alpha, centred) => {
        const el = labelEl(key, html, cls);
        if (!el._w) { el._w = el.offsetWidth; el._h = el.offsetHeight; }
        const w = el._w, h = el._h;
        // Candidate spots: beside the node (reading side first, then the other side), then above it.
        const spots = centred ? [[sx - w / 2, sy - h / 2]]
          : [[arabic ? sx - rpx - 9 - w : sx + rpx + 9, sy - h / 2], [arabic ? sx + rpx + 9 : sx - rpx - 9 - w, sy - h / 2], [sx - w / 2, sy - rpx - h - 10]];
        const boxOf = ([x, y]) => ({ x, y, x1: x - 3, y1: y - 2, x2: x + w + 3, y2: y + h + 2 });
        const free = b => b.x >= 2 && b.y >= 2 && b.x + w <= size.w - 2 && b.y + h <= size.h - 2 && !boxes.some(o => b.x1 < o.x2 && b.x2 > o.x1 && b.y1 < o.y2 && b.y2 > o.y1);
        let box = spots.map(boxOf).find(free);
        if (!box) {
          if (prio < 4) return;
          box = boxOf([Math.max(2, Math.min(size.w - w - 2, spots[0][0])), Math.max(2, Math.min(size.h - h - 2, spots[0][1]))]);
        }
        const x = box.x, y = box.y;
        boxes.push(box); used.add(key);
        el.style.transform = `translate3d(${Math.round(x)}px, ${Math.round(y)}px, 0)`; el.style.opacity = alpha.toFixed(2);
      };
      if (st.ready && !st.trans) {
        const competitors = new Set(st.competitors.map(c => c.supplierB));
        // Zone headings first, so supplier names never crowd them out.
        // Each zone heading sits just above its region as seen on screen (the rim point towards the screen's "up").
        if (st.selected == null) zones.forEach(z => {
          if (z.leaving || z.alpha < 0.3 * z.alphaT || (!z.strong && st.category)) return;
          const centre = z.dir.clone().applyQuaternion(world.quaternion).normalize();
          const up = Y.clone().addScaledVector(centre, -centre.y); if (up.lengthSq() < 1e-6) return; up.normalize();
          const a = z.angle + 0.06;
          const wp = centre.clone().multiplyScalar(Math.cos(a)).addScaledVector(up, Math.sin(a)).multiplyScalar(R * 1.05);
          const facing = wp.clone().normalize().dot(camDir); if (facing < 0.08) return;
          const p = project(wp, {});
          place(`z-${z.key}`, `<i style="background:${z.css}"></i><b>${esc(z.label)}</b><small>${z.count}</small>`, z.strong ? 'is-anchor is-focus' : 'is-anchor', p.x, p.y, 0, z.strong ? 4.5 : 3.8,
            Math.min(1, facing * 1.6) * Math.min(1, z.alpha / Math.max(0.05, z.alphaT)), true);
        });
        const cands = [];
        nodes.forEach(n => {
          if (n.alpha < 0.3) return;
          const sc = n.screen; const isSel = st.selected === n.id, isHover = st.hover === n.id;
          if (!isSel && !isHover && sc.front < 0.15) return;
          let prio = isSel ? 4 : isHover ? 3.5 : 0;
          if (!prio && !st.labels) return;
          if (!prio && competitors.has(n.id)) prio = 2.4;
          if (!prio && st.category && n.cat === st.category && sc.front > 0.2) prio = 2.1 + Math.min(0.5, (n.s.participations || 0) / 40);
          if (!prio && st.selected == null && !st.category && sc.front > 0.18) prio = (n.s.participations || 0) > 0 ? 1 + Math.min(0.9, n.s.participations / 20) : sc.front > 0.5 ? 0.6 : 0;
          if (!prio) return;
          if (st.selected != null && !isSel && !isHover && !competitors.has(n.id)) return;
          cands.push({ n, prio });
        });
        cands.sort((a, b) => b.prio - a.prio || (b.n.s.participations || 0) - (a.n.s.participations || 0));
        cands.slice(0, st.category ? 22 : 16).forEach(({ n, prio }) => {
          const isSel = st.selected === n.id;
          const row = st.rows.get(n.id);
          const meta = isSel ? `<small>${fmtInt(n.s.participations)} ${n.s.participations === 1 ? T('cycle', 'دورة') : T('cycles', 'دورات')}${row ? ` · ${fmtInt(row.wins)} ${row.wins === 1 ? T('award', 'ترسية') : T('awards', 'ترسيات')}` : ''}</small>` : '';
          place(`s${n.id}`, `<i style="background:${n.css}"></i><span>${esc(n.s.name)}</span>${meta}`, isSel ? 'is-selected' : st.hover === n.id ? 'is-hover' : '',
            n.screen.x, n.screen.y, n.screen.rpx, prio, Math.min(1, 0.35 + n.screen.front) * Math.min(1, n.alpha + 0.2), false);
        });
      }
      labelEls.forEach((el, key) => { if (!used.has(key)) el.style.opacity = '0'; });
    }

    // ---------- frame loop (runs only while something moves and the card is visible) ----------
    const size = { w: 0, h: 0 };
    function resize() {
      const r = stage.getBoundingClientRect(); if (!r.width || !r.height) return false;
      if (r.width === size.w && r.height === size.h) return true;
      size.w = r.width; size.h = r.height;
      renderer.setSize(size.w, size.h, false); camera.aspect = size.w / size.h;
      HOME.dist = size.w / size.h < 1 ? 4.15 / Math.max(0.62, size.w / size.h) : 4.15;
      HOME.focus = HOME.dist - 0.6;
      if (!focused()) st.distTarget = HOME.dist;
      camera.updateProjectionMatrix(); labelEls.forEach(el => (el._w = 0));
      measureOverlays(); st.offsetTarget = centreOffset();
      return true;
    }
    // With a wide card the sphere sits in the free space to the side of the control panel (in LTR and RTL alike).
    function centreOffset() {
      if (size.w < 900 || !panelEl || panelEl.offsetParent === null) return 0;
      const sr = stage.getBoundingClientRect(), pr = panelEl.getBoundingClientRect();
      let left = 0, right = sr.width;
      if ((pr.left + pr.right) / 2 - sr.left < sr.width / 2) left = pr.right - sr.left; else right = pr.left - sr.left;
      return Math.round(sr.width / 2 - (left + right) / 2);
    }
    const resizeObserver = new ResizeObserver(() => { resize(); wake(); });
    resizeObserver.observe(stage);
    const intersection = new IntersectionObserver(entries => { st.inView = entries[entries.length - 1].isIntersecting; if (st.inView) wake(); });
    intersection.observe(stage);

    let raf = 0, last = performance.now();
    const ease = t => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);
    const camDir = new THREE.Vector3(), q = new THREE.Quaternion(), Y = new THREE.Vector3(0, 1, 0), X = new THREE.Vector3(1, 0, 0);
    const axis = new THREE.Vector3(), edgeV = new THREE.Vector3(), edgeP = { x: 0, y: 0 };
    function slerpDir(out, a, b, t) {
      const dot = Math.max(-1, Math.min(1, a.dot(b)));
      const angle = Math.acos(dot);
      if (angle < 1e-4) return out.copy(b);
      axis.crossVectors(a, b);
      if (axis.lengthSq() < 1e-8) axis.copy(Math.abs(a.y) < 0.9 ? Y : X).cross(a);
      axis.normalize();
      return out.copy(a).applyAxisAngle(axis, angle * t);
    }
    function frame(now) {
      raf = 0;
      if (document.hidden || !st.inView || !resize()) return;
      const dt = Math.min(0.05, (now - last) / 1000); last = now;
      const moving = st.motion && !reduced.matches;
      if (moving) st.clock += dt;

      // Orbit: slow rotation unless paused, dragging, focused or recently touched; focused views sway gently instead.
      if (!st.drag) {
        if (Math.abs(st.vel.x) + Math.abs(st.vel.y) > 1e-4) {
          q.setFromAxisAngle(Y, st.vel.x); world.quaternion.premultiply(q); q.setFromAxisAngle(X, st.vel.y); world.quaternion.premultiply(q);
          st.vel.x *= 0.92; st.vel.y *= 0.92; st.qTarget = null;
        } else if (st.qTarget) {
          world.quaternion.slerp(st.qTarget, reduced.matches ? 1 : 1 - Math.pow(0.0025, dt));
          if (world.quaternion.angleTo(st.qTarget) < 1e-3) { world.quaternion.copy(st.qTarget); st.qTarget = null; st.sway = 0; }
        } else if (moving && now - st.lastInteract > 2200) {
          if (focused() || st.mode === 'category') { const sw = Math.sin(st.clock * 0.21) * 0.16; q.setFromAxisAngle(Y, sw - st.sway); st.sway = sw; world.quaternion.premultiply(q); }
          else { q.setFromAxisAngle(Y, dt * (st.hover != null ? 0.018 : 0.075)); world.quaternion.premultiply(q); }
        }
      }
      const z0 = camera.position.z; const dz = st.distTarget - z0;
      camera.position.z = Math.abs(dz) < 1e-3 || reduced.matches ? st.distTarget : z0 + dz * (1 - Math.pow(0.004, dt));
      st.offset += (st.offsetTarget - st.offset) * (reduced.matches ? 1 : 1 - Math.pow(0.004, dt));
      if (Math.abs(st.offset) > 0.5) camera.setViewOffset(size.w, size.h, st.offset, 0, size.w, size.h); else camera.clearViewOffset();
      camera.updateMatrixWorld();

      // Layout transition: every supplier glides along the surface, lifting slightly in flight.
      let animating = false;
      if (st.trans) {
        const elapsed = now - st.trans.t0; let done = true;
        nodes.forEach(n => {
          const t = Math.max(0, Math.min(1, (elapsed - n.delay) / st.trans.dur)); if (t < 1) done = false;
          const e = ease(t); slerpDir(n.pos, n.from, n.to, e);
          const travel = n.from.angleTo(n.to) / Math.PI;
          n.rad = n.radFrom + (1 - n.radFrom) * e + Math.sin(Math.PI * e) * 0.16 * travel;
        });
        if (done) { st.trans = null; nodes.forEach(n => { n.pos.copy(n.to); n.rad = 1; }); measureOverlays(); } else animating = true;
      }
      // Zones: grow, move and fade towards their targets.
      let zoneCount = 0; const zu = zoneMat.uniforms; const k = reduced.matches ? 1 : 1 - Math.pow(0.02, dt);
      zones = zones.filter(z => !(z.leaving && z.alpha < 0.01));
      zones.forEach(z => {
        z.alpha += (z.alphaT - z.alpha) * k; z.angle += (z.angleT - z.angle) * k; z.dir.lerp(z.dirT, k).normalize();
        if (Math.abs(z.alphaT - z.alpha) > 0.005 || Math.abs(z.angleT - z.angle) > 0.002) animating = true;
        if (zoneCount < MAXZ) {
          zu.uDir.value[zoneCount].copy(z.dir); zu.uCos.value[zoneCount] = Math.cos(z.angle); zu.uCol.value[zoneCount].copy(z.color);
          zu.uA.value[zoneCount] = z.alpha * (z.strong ? 1 : 0.6); zoneCount++;
        }
      });
      zu.uCount.value = zoneCount; zu.uTime.value = st.clock;
      zoneSphere.visible = zoneCount > 0;

      camDir.copy(camera.position).normalize();
      nodes.forEach(n => {
        n.alpha += (n.alphaT - n.alpha) * 0.12; n.size += (n.sizeT - n.size) * 0.14;
        if (Math.abs(n.alphaT - n.alpha) > 0.01 || Math.abs(n.sizeT - n.size) > 0.01) animating = true;
        const breathe = moving ? 1 + 0.012 * Math.sin(st.clock * 0.9 + n.phase) : 1;
        n.mesh.position.copy(n.pos).multiplyScalar(R * 1.016 * breathe * n.rad);
        n.halo.position.copy(n.mesh.position); n.ring.position.copy(n.mesh.position);
        const sel = st.selected === n.id, hov = st.hover === n.id;
        const s = n.r * (sel ? 1.5 : hov ? 1.3 : 1) * n.size;
        n.mesh.scale.setScalar(s); n.halo.scale.setScalar(s * (P.dark ? 4.2 : 3.2));
        tmpA.copy(n.mesh.position).applyQuaternion(world.quaternion);
        const facing = v3.copy(tmpA).normalize().dot(camDir);
        const depth = 0.28 + 0.72 * Math.min(1, Math.max(0, (facing + 0.35) / 0.9));
        const a = Math.min(1, n.alpha) * depth;
        n.mesh.material.uniforms.uOpacity.value = a; n.mesh.material.uniforms.uGlow.value = sel || hov ? 0.4 : 0;
        n.halo.material.opacity = a * (P.dark ? (sel || hov ? 0.9 : 0.5) : (sel || hov ? 0.42 : 0.18));
        const ringOn = sel ? 1 : hov ? 0.55 : 0; n.ring.material.opacity += (ringOn - n.ring.material.opacity) * 0.2;
        n.ring.scale.setScalar(s * 3.2 * (sel && moving ? 1 + 0.08 * Math.sin(st.clock * 3) : 1));
        project(tmpA, n.screen); n.screen.front = facing;
        edgeV.copy(tmpA).addScaledVector(X, s); n.screen.rpx = Math.abs(project(edgeV, edgeP).x - n.screen.x);
      });
      // Links fade in once the nodes have arrived.
      if (!st.trans && st.linkFade < 1) { st.linkFade = Math.min(1, st.linkFade + dt * 2.6); animating = true; }
      const fade = st.trans ? 0 : st.linkFade;
      links.forEach(o => { o.mesh.material.opacity = o.base * fade; });
      flows.forEach(f => { if (moving) f.t = (f.t + dt * f.speed) % 1; f.sp.position.copy(f.curve.getPointAt(f.t)); f.sp.material.opacity = 0.95 * fade; });
      orbitRings.forEach(o => { const ang = o.phase + st.clock * o.speed * 6; o.spark.position.set(Math.cos(ang) * o.rad, 0, Math.sin(ang) * o.rad); });

      renderer.render(scene, camera);
      updateLabels();
      const settling = !!st.qTarget || Math.abs(camera.position.z - st.distTarget) > 1e-3 || Math.abs(st.offset - st.offsetTarget) > 0.5 || Math.abs(st.vel.x) + Math.abs(st.vel.y) > 1e-4;
      if (moving || animating || settling || st.drag) wake();
    }
    function wake() { if (!raf) raf = requestAnimationFrame(frame); }

    // ---------- picking and interaction ----------
    function pick(px, py) {
      let best = null, bestScore = Infinity;
      nodes.forEach(n => {
        if (n.alpha < 0.12 || n.screen.front < -0.2) return;
        const d = Math.hypot(n.screen.x - px, n.screen.y - py); if (d > Math.max(14, n.screen.rpx + 6)) return;
        const score = d - n.screen.front * 8 - (n.alpha > 0.5 ? 6 : 0);
        if (score < bestScore) { bestScore = score; best = n; }
      });
      return best;
    }
    const local = e => { const r = stage.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; };
    const pinchDistance = () => { const [a, b] = [...st.pointers.values()]; return Math.hypot(a.x - b.x, a.y - b.y); };
    const onPointerDown = e => {
      if (e.pointerType === 'mouse' && e.button !== 0) return;
      stage.setPointerCapture(e.pointerId); const p = local(e); st.pointers.set(e.pointerId, p);
      if (st.pointers.size === 2) { st.pinch = pinchDistance(); st.drag = null; return; }
      st.drag = { x: p.x, y: p.y, x0: p.x, y0: p.y, moved: false }; st.vel = { x: 0, y: 0 }; st.lastInteract = performance.now(); wake();
    };
    const onPointerMove = e => {
      const p = local(e);
      if (st.pointers.has(e.pointerId)) st.pointers.set(e.pointerId, p);
      if (st.pointers.size === 2 && st.pinch) {
        const d = pinchDistance(); st.distTarget = clampZoom(st.distTarget * st.pinch / Math.max(1, d)); st.pinch = d; wake(); return;
      }
      if (st.drag) {
        const dx = p.x - st.drag.x, dy = p.y - st.drag.y; st.drag.x = p.x; st.drag.y = p.y;
        if (Math.hypot(p.x - st.drag.x0, p.y - st.drag.y0) > 4) st.drag.moved = true;
        if (st.drag.moved) {
          const kk = 3.2 / Math.max(300, size.h);
          q.setFromAxisAngle(Y, dx * kk); world.quaternion.premultiply(q); q.setFromAxisAngle(X, dy * kk); world.quaternion.premultiply(q);
          st.vel = { x: dx * kk, y: dy * kk }; st.qTarget = null; tip.hidden = true; wake();
        }
        return;
      }
      if (e.pointerType !== 'mouse') return;
      const n = pick(p.x, p.y);
      if ((n?.id ?? null) !== st.hover) {
        st.hover = n?.id ?? null; stage.style.cursor = n ? 'pointer' : 'grab';
        if (st.selected == null) rebuildLinks();
        wake();
      }
      if (n) showTip(n, p); else tip.hidden = true;
    };
    const onPointerUp = e => {
      st.pointers.delete(e.pointerId); if (st.pointers.size < 2) st.pinch = 0;
      const drag = st.drag; st.drag = null; st.lastInteract = performance.now();
      if (!drag || drag.moved) { wake(); return; }
      const p = local(e); const n = pick(p.x, p.y);
      if (n) cb.onSupplierClick?.(n.id === st.selected ? null : n.id);
    };
    const onPointerLeave = () => { if (st.hover != null) { st.hover = null; tip.hidden = true; if (st.selected == null) rebuildLinks(); wake(); } };
    const clampZoom = d => Math.min(7.5, Math.max(2.1, d));
    const zoomBy = factor => { st.distTarget = clampZoom(st.distTarget * factor); st.lastInteract = performance.now(); wake(); };
    let hintTimer = 0;
    const onWheel = e => {
      // The page keeps scrolling normally; the wheel zooms once the universe is focused (clicked), with Ctrl, or in full screen.
      const engaged = document.activeElement === stage || e.ctrlKey || e.metaKey || document.fullscreenElement === host;
      if (!engaged) {
        if (hintBox) { hintBox.hidden = false; clearTimeout(hintTimer); hintTimer = setTimeout(() => { hintBox.hidden = true; }, 1600); }
        return;
      }
      e.preventDefault(); zoomBy(1 + Math.sign(e.deltaY) * 0.08);
    };
    const onDblClick = e => { const p = local(e); const n = pick(p.x, p.y); if (n) { faceDirection(n.to, 2.5); wake(); } };
    stage.addEventListener('pointerdown', onPointerDown);
    stage.addEventListener('pointermove', onPointerMove);
    stage.addEventListener('pointerup', onPointerUp);
    stage.addEventListener('pointercancel', onPointerUp);
    stage.addEventListener('pointerleave', onPointerLeave);
    stage.addEventListener('wheel', onWheel, { passive: false });
    stage.addEventListener('dblclick', onDblClick);

    function showTip(n, p) {
      const row = st.rows.get(n.id);
      tip.innerHTML = `<b>${esc(n.s.name)}</b><span><i style="background:${n.css}"></i>${esc(n.cat)}${row ? ` · ${esc(row.status)}` : ''}</span>
        <span>${fmtInt(n.s.participations)} ${T('closed bidding cycles with an offer', 'دورات مغلقة بعرض')}</span>
        ${row ? `<span>${fmtInt(row.wins)} ${row.wins === 1 ? T('official award', 'ترسية رسمية') : T('official awards', 'ترسيات رسمية')} · ${T('response', 'الاستجابة')} ${fmtPct(row.participationRate)}</span>` : `<span class="is-muted">${T('Outside the current filters', 'خارج المرشحات الحالية')}</span>`}
        <em>${n.id === st.selected ? T('Selected · click again to close the details', 'مختار · انقر مجدداً لإغلاق التفاصيل') : T('Click to open the supplier details', 'انقر لفتح تفاصيل المورد')}</em>`;
      tip.hidden = false;
      const w = tip.offsetWidth, h = tip.offsetHeight;
      let x = p.x + 18, y = p.y + 14; if (x + w > size.w - 8) x = p.x - w - 18; if (y + h > size.h - 8) y = p.y - h - 14;
      tip.style.transform = `translate(${Math.round(Math.max(4, x))}px, ${Math.round(Math.max(4, y))}px)`;
    }

    // Keyboard: arrows move between suppliers (screen order), Enter opens one, Space pauses, +/- zoom, Escape steps back.
    const announce = text => { caption.dataset.sr = text; stage.setAttribute('aria-label', text); };
    const onKey = e => {
      const list = nodes.filter(n => n.alpha > 0.5 && n.screen.front > -0.1).sort((a, b) => a.screen.x - b.screen.x || a.screen.y - b.screen.y);
      if (['ArrowRight', 'ArrowDown', 'ArrowLeft', 'ArrowUp'].includes(e.key)) {
        e.preventDefault(); if (!list.length) return;
        const forward = e.key === 'ArrowDown' ? true : e.key === 'ArrowUp' ? false : (e.key === 'ArrowRight') !== arabic;
        const cur = list.findIndex(n => n.id === st.hover);
        const n = list[(cur < 0 ? 0 : cur + (forward ? 1 : -1) + list.length) % list.length];
        st.hover = n.id; faceDirection(n.to, st.distTarget); if (st.selected == null) rebuildLinks();
        announce(`${clean(n.s.name)}, ${clean(n.cat)}, ${n.s.participations} ${T('bidding cycles', 'دورات عطاء')}`); wake();
      } else if (e.key === 'Enter' && st.hover != null) { e.preventDefault(); cb.onSupplierClick?.(st.hover); }
      else if (e.key === ' ') { e.preventDefault(); setMotion(!st.motion); }
      else if (e.key === '+' || e.key === '=') { e.preventDefault(); zoomBy(0.9); }
      else if (e.key === '-') { e.preventDefault(); zoomBy(1.1); }
      else if (e.key === 'Escape') { e.preventDefault(); if (st.selected != null) cb.onSupplierClick?.(null); else resetView(); }
    };
    stage.addEventListener('keydown', onKey);

    // ---------- controls rendered inside the card ----------
    const DESC = {
      universe: T('Every supplier in your authorized scope on one sphere. Colour = registered category, size = closed bidding cycles with an offer. Placement is for overview only.',
        'كل مورد ضمن نطاقك المصرح على كرة واحدة. اللون = الفئة المسجلة، والحجم = دورات العطاء المغلقة بعرض. الموضع للعرض فقط.'),
      category: T('Each registered category forms its own region. A category is a classification, not a relationship.',
        'كل فئة مسجلة تشكل منطقة خاصة بها. الفئة تصنيف وليست علاقة.'),
      competition: T('Arcs join suppliers that submitted offers in the same closed bidding cycles. Thicker arc = more shared cycles.',
        'تربط الأقواس موردين قدّموا عروضاً في دورات العطاء المغلقة نفسها. القوس الأسمك = دورات مشتركة أكثر.')
    };
    function setMode(mode) {
      if (!DESC[mode] || mode === st.mode) return;
      st.mode = mode; host.dataset.mode = mode;
      host.querySelectorAll('[data-uni-mode]').forEach(b => b.setAttribute('aria-checked', String(b.dataset.uniMode === mode)));
      descBox.textContent = DESC[mode];
      applyLayout(true, false); renderCaption();
    }
    function setMotion(on) {
      st.motion = on && !reduced.matches;
      const b = host.querySelector('[data-uni-tool="motion"]');
      if (b) {
        b.setAttribute('aria-pressed', String(!st.motion));
        b.setAttribute('aria-label', st.motion ? T('Pause motion', 'إيقاف الحركة') : T('Resume motion', 'استئناف الحركة'));
        b.querySelector('[data-icon-pause]').hidden = !st.motion; b.querySelector('[data-icon-play]').hidden = st.motion;
      }
      host.classList.toggle('is-paused', !st.motion); wake();
    }
    function resetView() {
      st.vel = { x: 0, y: 0 }; st.qTarget = BASE_TILT.clone(); st.distTarget = HOME.dist; st.focusDir = null;
      if (st.mode !== 'universe') setMode('universe'); else applyLayout(true, false);
    }
    const onHostClick = e => {
      const b = e.target.closest('button'); if (!b || !host.contains(b)) return;
      const v = b.dataset;
      if (v.uniMode) setMode(v.uniMode);
      else if (v.uniTool === 'motion') setMotion(!st.motion);
      else if (v.uniTool === 'reset') resetView();
      else if (v.uniTool === 'zoom-in') zoomBy(0.88);
      else if (v.uniTool === 'zoom-out') zoomBy(1.12);
      else if (v.uniTool === 'labels') { st.labels = !st.labels; b.setAttribute('aria-pressed', String(st.labels)); wake(); }
      else if (v.uniTool === 'fullscreen') { if (document.fullscreenElement) document.exitFullscreen(); else host.requestFullscreen?.().catch(() => { }); }
      else if (v.legendCat !== undefined) cb.onCategoryClick?.(v.legendCat);
      else if (v.uniClearCategory !== undefined) cb.onCategoryClick?.(st.category);
    };
    host.addEventListener('click', onHostClick);
    const onFullscreen = () => { host.classList.toggle('is-fullscreen', document.fullscreenElement === host); setTimeout(() => { size.w = 0; resize(); wake(); }, 60); };
    document.addEventListener('fullscreenchange', onFullscreen);

    // Legend: one button per category (the dots of that colour); a click asks intelligence-suppliers.js to filter on it.
    function renderLegend() {
      const groups = new Map();
      nodes.forEach(n => { if (!groups.has(n.cat)) groups.set(n.cat, { css: n.css, count: 0 }); groups.get(n.cat).count++; });
      const cats = [...groups.entries()].sort((a, b) => b[1].count - a[1].count || a[0].localeCompare(b[0]));
      legendBox.innerHTML = cats.length ? `<p class="oi-field-label">${T('Categories · click to cluster', 'الفئات · انقر للتجميع')}</p><ul class="oi-legend-cats">${cats.map(([c, g]) =>
        `<li><button type="button" data-legend-cat="${esc(c)}" aria-pressed="${c === st.category}" class="${c === st.category ? 'is-on' : st.category ? 'is-dim' : ''}"><i style="background:${g.css};color:${g.css}"></i><span>${esc(c)}</span><small>${g.count}</small></button></li>`).join('')}</ul>` : '';
    }
    function renderFocusChip() {
      const members = st.category ? nodes.filter(n => n.cat === st.category) : [];
      focusChip.hidden = !members.length;
      if (members.length) {
        const count = members.length === 1 ? T('1 supplier', 'مورد واحد') : `${members.length} ${T('suppliers', 'موردين')}`;
        focusChip.innerHTML = `<i style="background:${members[0].css};color:${members[0].css}"></i><b>${esc(st.category)}</b><span>${count} ${T('in this cluster', 'في هذا التجمع')}</span>` +
          `<button type="button" data-uni-clear-category aria-label="${T('Clear the category filter', 'إزالة مرشح الفئة')}">×</button>`;
      }
    }
    function renderCaption() {
      const shown = st.shown ? nodes.filter(n => st.shown.has(n.id)).length : nodes.length;
      let text = `${nodes.length} ${T('suppliers in scope', 'مورداً في النطاق')} · ${shown} ${T('match the filters', 'يطابقون المرشحات')}`;
      if (st.mode === 'competition') text += ` · ${st.pairs.length} ${T('competing pairs shown', 'زوجاً متنافساً معروضاً')}`;
      caption.textContent = text;
      emptyBox.hidden = nodes.length > 0;
      emptyBox.textContent = T('No authorized suppliers in this scope and period.', 'لا يوجد موردون مصرح بهم في هذا النطاق والفترة.');
    }

    // ---------- public interface used by intelligence-suppliers.js ----------
    function update(view) {
      const list = view.suppliers || [];
      const key = list.map(s => s.supplierId).join(',');
      const rebuild = key !== st.dataKey;
      const category = view.category || '';
      const categoryChanged = category !== st.category;
      st.rows = new Map((view.rows || []).map(r => [r.supplierId, r]));
      st.shown = new Set(view.shownIds || []);
      st.pairs = view.pairs || [];
      st.competitionMinimum = view.competitionMinimum || 2;
      st.category = category;
      if (rebuild) {
        buildNodes(list); st.dataKey = key; clearLabels(); st.ready = true;
        applyLayout(true, true);
      } else if (categoryChanged) {
        applyLayout(true, false);
      } else {
        updateEmphasis(); rebuildLinks();
      }
      renderLegend(); renderFocusChip(); renderCaption();
      requestAnimationFrame(() => { measureOverlays(); st.offsetTarget = centreOffset(); });
      wake();
    }
    function selectSupplier(id, competitors) {
      const changed = id !== st.selected;
      st.selected = id;
      st.competitors = id == null ? [] : competitors.filter(c => byId.has(c.supplierB));
      const n = id == null ? null : byId.get(id);
      if (changed) {
        if (n) { faceDirection(n.to, HOME.focus); announce(`${clean(n.s.name)} ${T('selected', 'مختار')}`); }
        else if (st.focusDir) faceDirection(st.focusDir, HOME.focus);
        else home();
      }
      updateEmphasis(); rebuildLinks(); wake();
    }
    function onTheme() { requestAnimationFrame(applyTheme); }
    function onSidebar() { setTimeout(() => { resize(); wake(); }, 320); }
    function onMotionPreference() { setMotion(st.motion || !reduced.matches); }
    function onContextLost(e) { e.preventDefault(); showFallback(host); }
    window.addEventListener('orange:themechange', onTheme);
    window.addEventListener('orange:sidebar', onSidebar);
    document.addEventListener('visibilitychange', wake);
    reduced.addEventListener?.('change', onMotionPreference);
    canvas.addEventListener('webglcontextlost', onContextLost);

    function dispose() {
      cancelAnimationFrame(raf); raf = 0;
      resizeObserver.disconnect(); intersection.disconnect();
      window.removeEventListener('orange:themechange', onTheme);
      window.removeEventListener('orange:sidebar', onSidebar);
      window.removeEventListener('pagehide', dispose);
      document.removeEventListener('visibilitychange', wake);
      document.removeEventListener('fullscreenchange', onFullscreen);
      reduced.removeEventListener?.('change', onMotionPreference);
      canvas.removeEventListener('webglcontextlost', onContextLost);
      stage.removeEventListener('pointerdown', onPointerDown); stage.removeEventListener('pointermove', onPointerMove);
      stage.removeEventListener('pointerup', onPointerUp); stage.removeEventListener('pointercancel', onPointerUp);
      stage.removeEventListener('pointerleave', onPointerLeave); stage.removeEventListener('wheel', onWheel);
      stage.removeEventListener('dblclick', onDblClick); stage.removeEventListener('keydown', onKey);
      host.removeEventListener('click', onHostClick);
      clearLinks(); nodes.forEach(disposeNode); nodes = []; byId.clear(); clearLabels();
      scene.traverse(o => { if (o.isSprite && o.material) o.material.dispose(); });
      disposables.forEach(x => x.dispose());
      renderer.dispose();
    }
    window.addEventListener('pagehide', dispose);

    descBox.textContent = DESC.universe;
    host.dataset.mode = 'universe';
    host.classList.add('is-ready');
    setMotion(!reduced.matches);
    applyTheme();
    return { update, selectSupplier, resize: () => { size.w = 0; resize(); wake(); }, dispose };
  }
})();

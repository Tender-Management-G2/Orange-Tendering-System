import gsap from 'gsap';
import * as THREE from 'three';
import { Stage, webglAvailable } from './core/stage.js';
import { loadFont } from './core/text.js';
import { bindRegistrationForm, bindLoginForm } from './ui/forms.js';
import { createInspector } from './ui/inspector.js';

// Orange Auth Studio — V3 Motion Lab (gate G2A).
// Isolated from the ASP.NET application: no network calls, no credentials, no camera.

const params = new URLSearchParams(location.search);
const chapterId = params.get('chapter') === 'login' ? 'login' : 'registration';
const embed = params.get('embed'); // "stage" = scene only (side-by-side page)
const clean = params.has('clean') || params.has('embed');

const CHAPTERS = {
  registration: {
    name: 'Supplier Genesis',
    tagline: 'Every great partnership begins with a small idea.',
    form: '#reg-form',
    title: 'Supplier registration — Motion Lab',
    load: () => import('./registration/index.js'),
    fallback: () => import('./registration/fallback.js'),
    bindOnly: bindRegistrationForm
  },
  login: {
    name: 'Orange Signal',
    tagline: 'Every connection begins with a signal.',
    form: '#login-form',
    title: 'Sign in — Motion Lab',
    load: () => import('./login/index.js'),
    fallback: () => import('./login/fallback.js'),
    bindOnly: bindLoginForm
  }
};

const $ = sel => document.querySelector(sel);

async function boot() {
  const def = CHAPTERS[chapterId];
  document.body.dataset.chapter = chapterId;
  if (clean) document.body.dataset.clean = '1';
  if (embed) document.body.dataset.embed = embed;
  document.title = def.title;
  $('#chapter-name').textContent = def.name;
  $('#tagline').textContent = def.tagline;
  for (const form of document.querySelectorAll('.auth-form')) form.hidden = `#${form.id}` !== def.form;
  for (const a of document.querySelectorAll('[data-chapter-link]')) {
    if (a.dataset.chapterLink === chapterId) a.setAttribute('aria-current', 'page');
  }

  const stageEl = $('#stage');
  const formEl = $(def.form);
  const statusEl = $('#scene-status');
  const onStatus = text => {
    statusEl.textContent = text;
  };

  const motionQuery = window.matchMedia('(prefers-reduced-motion: reduce)');
  let reduced = motionQuery.matches || params.has('reduced');

  await loadFont();

  // No WebGL: the finished drawing as static SVG, and a fully working form.
  if (!webglAvailable() || params.has('nowebgl')) {
    const { fallbackSvg } = await def.fallback();
    const box = $('#stage-fallback');
    box.innerHTML = fallbackSvg();
    box.hidden = false;
    stageEl.dataset.ready = '1';
    stageEl.dataset.fallback = '1';
    const form = def.bindOnly(formEl, () => {});
    form.render();
    onStatus('Illustration shown as a still image.');
    window.studio = { ready: true, fallback: true, chapter: chapterId, form, state: () => ({ fallback: true }) };
    return;
  }

  const compact = window.innerWidth < 900;
  const quality = { maxPixelRatio: compact ? 1.75 : 2 };
  // Bench overrides: ?shadow=2048&pcss=8,16&dpr=1
  if (params.has('shadow')) quality.shadowSize = Number(params.get('shadow'));
  if (params.has('dpr')) quality.maxPixelRatio = Number(params.get('dpr'));
  if (params.has('pcss')) [quality.blockerSamples, quality.pcfSamples] = params.get('pcss').split(',').map(Number);
  const stage = new Stage(stageEl, quality);
  const chapter = (await def.load()).mount(stage, { onStatus });
  const { director, scene, choreography } = chapter;
  director.reduced = reduced;
  if (scene.nameLine) scene.nameLine.reduced = reduced;

  let mode = 'live';
  let reel = null;
  let reelTween = null;

  // Caption visibility follows the shot.
  stage.updaters.add(() => {
    const shot = mode === 'live' ? director.camName ?? 'wide' : chapter.reelShot(reel, reel.tl.time());
    if (stageEl.dataset.shot !== shot) stageEl.dataset.shot = shot;
  });

  // Frame-time sampling for the inspector readout (measured, not a target).
  const perf = { last: 0, samples: [] };
  stage.updaters.add(() => {
    const now = performance.now();
    if (perf.last) {
      const dt = now - perf.last;
      if (dt < 250) {
        perf.samples.push(dt);
        if (perf.samples.length > 120) perf.samples.shift();
      }
    }
    perf.last = now;
  });

  const form = chapter.bind(formEl);

  // ---- showreel -------------------------------------------------------------------------------
  let settlePlay = null;
  const stopReel = () => {
    reelTween?.kill();
    reelTween = null;
    settlePlay?.();
    settlePlay = null;
  };
  const buildReel = (time = 0) => {
    stopReel();
    reel?.tl.kill();
    reel = choreography.buildShowreel();
    reel.enter(Math.min(time, reel.duration));
  };

  const api = {
    ready: false,
    chapter: chapterId,
    stage,
    scene,
    director,
    form,
    get mode() {
      return mode;
    },
    get reel() {
      return reel;
    },
    setMode(next) {
      if (next === mode) return;
      mode = next;
      if (mode === 'reel') {
        director.setEnabled(false);
        buildReel(0);
      } else {
        stopReel();
        reel?.tl.kill();
        reel = null;
        director.setEnabled(true);
        form.render();
      }
      stage.invalidate();
      inspector?.sync();
    },
    seek(time) {
      if (mode !== 'reel') api.setMode('reel');
      stopReel();
      reel.seek(time);
      inspector?.sync();
    },
    /** Plays the showreel between two times at a rate; resolves when it arrives. */
    play({ from, to, rate = 1 } = {}) {
      if (mode !== 'reel') api.setMode('reel');
      stopReel();
      const a = from ?? reel.tl.time();
      const b = to ?? reel.duration;
      reel.seek(a);
      return new Promise(resolve => {
        settlePlay = resolve;
        reelTween = gsap.to(reel.tl, {
          time: b,
          duration: Math.abs(b - a) / rate,
          ease: 'none',
          onUpdate: () => inspector?.sync(),
          onComplete: () => {
            reelTween = null;
            settlePlay = null;
            inspector?.sync();
            resolve();
          }
        });
      });
    },
    pause() {
      stopReel();
      inspector?.sync();
    },
    get playing() {
      return Boolean(reelTween);
    },
    setReduced(on) {
      reduced = on;
      director.setReduced(on);
      inspector?.sync();
    },
    get reduced() {
      return reduced;
    },
    setWireframe(on) {
      scene.root.traverse(o => {
        const mats = Array.isArray(o.material) ? o.material : o.material ? [o.material] : [];
        for (const m of mats) if (m.isMeshStandardMaterial) m.wireframe = on;
      });
      stage.invalidate();
    },
    setHinges(on) {
      for (const h of scene.hinges ?? []) {
        if (!h.helper) {
          const pts = h.axis === 'x' ? [-h.half, 0, 0, h.half, 0, 0] : [0, 0, -h.half, 0, 0, h.half];
          const g = new THREE.BufferGeometry();
          g.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
          h.helper = new THREE.Line(g, new THREE.LineBasicMaterial({ color: 0x0091b3, depthTest: false, transparent: true }));
          h.helper.renderOrder = 99;
          const ends = new THREE.Points(g, new THREE.PointsMaterial({ color: 0x0091b3, size: 7, sizeAttenuation: false, depthTest: false, transparent: true }));
          ends.renderOrder = 99;
          h.helper.add(ends);
          h.group.add(h.helper);
        }
        h.helper.visible = on;
      }
      stage.invalidate();
    },
    setOrbit(on) {
      stage.setOrbit(on);
    },
    state() {
      const gl = stage.renderer.getContext();
      const ext = gl.getExtension('WEBGL_debug_renderer_info');
      const s = perf.samples;
      const mean = s.length ? s.reduce((a, b) => a + b, 0) / s.length : 0;
      return {
        chapter: chapterId,
        mode,
        reduced,
        shot: stageEl.dataset.shot,
        camera: stage.rig.snapshot(),
        scene: scene.state(),
        reel: reel ? { time: +reel.tl.time().toFixed(3), duration: +reel.duration.toFixed(3), shots: reel.shots } : null,
        segments: Object.fromEntries(Object.entries(choreography.segments).map(([k, v]) => [k, { time: +v.tl.time().toFixed(3), duration: +v.duration.toFixed(3), moving: v.moving }])),
        status: statusEl.textContent,
        render: {
          renderer: ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : 'unknown',
          size: [stage.width, stage.height],
          pixelRatio: +stage.renderer.getPixelRatio().toFixed(2),
          governorSteps: stage.governor.steps,
          frames: stage.frames,
          meanFrameMs: +mean.toFixed(2),
          calls: stage.renderer.info.render.calls,
          triangles: stage.renderer.info.render.triangles
        }
      };
    }
  };
  window.studio = api;

  const inspector = clean ? null : createInspector(api, chapter);
  if (params.has('inspector')) inspector?.open();

  motionQuery.addEventListener('change', e => {
    if (!params.has('reduced')) api.setReduced(e.matches);
  });

  // Re-frame only when the stage really changes size (the first observer callback is not a change).
  let resizeTimer = 0;
  let lastSize = `${stageEl.clientWidth}x${stageEl.clientHeight}`;
  new ResizeObserver(() => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      const size = `${stageEl.clientWidth}x${stageEl.clientHeight}`;
      if (size === lastSize) return;
      lastSize = size;
      if (mode === 'reel') buildReel(reel.tl.time());
      else director.reframe();
      inspector?.sync();
    }, 60);
  }).observe(stageEl);

  // Same-origin embedding (side-by-side page) drives the showreel through postMessage.
  window.addEventListener('message', event => {
    if (event.origin !== location.origin || !event.data || event.data.source !== 'orange-auth-lab') return;
    const { type, value } = event.data;
    if (type === 'progress') {
      if (mode !== 'reel') api.setMode('reel');
      api.seek(value * reel.duration);
    }
    if (type === 'moment') {
      if (mode !== 'reel') api.setMode('reel');
      api.seek(chapter.moments(reel)[value] ?? 0);
    }
    if (type === 'play') {
      if (mode !== 'reel') api.setMode('reel');
      api.play({ from: 0, rate: value || 1 });
    }
    if (type === 'live') api.setMode('live');
  });

  form.render();
  director.start();
  // Caption arrives with the first stroke rather than before it.
  gsap.delayedCall(reduced ? 0 : 1.1, () => {
    stageEl.dataset.ready = '1';
  });
  api.ready = true;
  if (window.parent !== window) window.parent.postMessage({ source: 'orange-auth-lab', type: 'ready', chapter: chapterId }, location.origin);
}

boot().catch(error => {
  console.error(error);
  window.studio = { ready: false, error: String(error?.stack || error) };
});

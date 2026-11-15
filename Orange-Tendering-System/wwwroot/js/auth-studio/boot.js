import gsap from 'gsap';
import { Stage, webglAvailable } from './core/stage.js';
import { loadFont } from './core/text.js';

// Starts one chapter (Supplier Genesis or Orange Signal) on a stage element.
//
// The form never depends on this module. It is loaded after the form's own scripts, it only
// listens to the sanitised state those scripts publish, and any failure here leaves the finished
// drawing as a still image (or a plain ivory panel) beside a fully working form.

/**
 * @param stageEl       the .as-stage element
 * @param fallback      () => svg markup of the finished opening drawing
 * @param mount         (stage, { onStatus }) => { scene, choreography, director }
 * @param captionShots  camera shots during which the tagline shares the stage
 */
export async function bootStudio({ stageEl, fallback, mount, captionShots = ['wide'] }) {
  const statusEl = stageEl.querySelector('[data-scene-status]');
  const fallbackEl = stageEl.querySelector('[data-stage-fallback]');
  const onStatus = text => {
    if (statusEl) statusEl.textContent = text;
  };

  const showStill = () => {
    if (fallbackEl) {
      fallbackEl.innerHTML = fallback();
      fallbackEl.hidden = false;
    }
    stageEl.dataset.fallback = '1';
    stageEl.dataset.caption = '1';
    stageEl.dataset.ready = '1';
    onStatus('Illustration shown as a still image.');
  };

  if (!webglAvailable()) {
    showStill();
    return { fallback: true };
  }

  const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
  let stage = null;
  let chapter = null;
  try {
    const compact = window.innerWidth < 900;
    stage = new Stage(stageEl, { maxPixelRatio: compact ? 1.75 : 2 });
    chapter = mount(stage, { onStatus });
  } catch (error) {
    console.error('The illustration could not start:', error);
    stage?.dispose();
    showStill();
    return { fallback: true };
  }

  const { director, scene } = chapter;
  director.reduced = motion.matches;
  if (scene.nameLine) scene.nameLine.reduced = motion.matches;

  // Labels are first painted with the system font; repaint once the interface font has arrived.
  loadFont().then(loaded => {
    if (loaded) {
      scene.repaintText?.();
      stage.invalidate();
    }
  });

  // Caption visibility follows the shot.
  stage.updaters.add(() => {
    const shot = director.camName ?? 'wide';
    if (stageEl.dataset.shot !== shot) {
      stageEl.dataset.shot = shot;
      stageEl.dataset.caption = captionShots.includes(shot) ? '1' : '0';
    }
  });

  motion.addEventListener('change', event => director.setReduced(event.matches));

  // Re-frame only when the stage really changes size (the first observer callback is not a change).
  const sizeOf = () => `${stageEl.clientWidth}x${stageEl.clientHeight}`;
  let resizeTimer = 0;
  let lastSize = sizeOf();
  new ResizeObserver(() => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      const size = sizeOf();
      if (size === lastSize) return;
      lastSize = size;
      director.reframe();
    }, 60);
  }).observe(stageEl);

  // A lost graphics context cannot be animated; fall back to the still drawing.
  stage.canvas.addEventListener('webglcontextlost', event => {
    event.preventDefault();
    director.destroy();
    stage.canvas.hidden = true;
    showStill();
  });

  return {
    ...chapter,
    stage,
    start() {
      director.start();
      // The caption arrives with the first stroke rather than before it.
      gsap.delayedCall(motion.matches ? 0 : 0.7, () => {
        stageEl.dataset.ready = '1';
      });
    }
  };
}

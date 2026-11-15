// Development inspector. It is a review tool, deliberately separate from the experience:
// play / pause / restart / reverse / scrub / slow motion / shot jump, reduced motion, wireframe,
// hinge axes, free orbit, and synthetic form states. Nothing here exists in production.

const $ = id => document.getElementById(id);

export function createInspector(api, chapter) {
  const panel = $('inspector');
  const toggle = $('inspector-toggle');
  const scrub = $('reel-scrub');
  const time = $('reel-time');
  const speed = $('reel-speed');
  const playBtn = $('reel-play');
  const readout = $('readout');
  const reelRow = $('reel-controls');
  let readoutTimer = 0;

  const open = () => {
    panel.hidden = false;
    toggle.hidden = true;
    toggle.setAttribute('aria-expanded', 'true');
    clearInterval(readoutTimer);
    readoutTimer = setInterval(updateReadout, 500);
    sync();
  };
  const close = () => {
    panel.hidden = true;
    toggle.hidden = false;
    toggle.setAttribute('aria-expanded', 'false');
    clearInterval(readoutTimer);
  };
  toggle.addEventListener('click', open);
  $('inspector-close').addEventListener('click', close);

  $('mode-live').addEventListener('click', () => api.setMode('live'));
  $('mode-reel').addEventListener('click', () => api.setMode('reel'));

  const rate = () => Number(speed.value) || 1;
  $('reel-restart').addEventListener('click', () => api.play({ from: 0, rate: rate() }));
  playBtn.addEventListener('click', () => {
    if (api.playing) api.pause();
    else {
      if (api.mode !== 'reel') api.setMode('reel');
      const at = api.reel.tl.time();
      api.play({ from: at >= api.reel.duration - 0.01 ? 0 : at, rate: rate() });
    }
  });
  $('reel-reverse').addEventListener('click', () => {
    if (api.mode !== 'reel') api.setMode('reel');
    const at = api.reel.tl.time();
    api.play({ from: at <= 0.01 ? api.reel.duration : at, to: 0, rate: rate() });
  });
  scrub.addEventListener('input', () => {
    if (api.mode !== 'reel') api.setMode('reel');
    api.seek((Number(scrub.value) / 1000) * api.reel.duration);
  });

  const shots = $('reel-shots');
  const renderShots = () => {
    shots.textContent = '';
    if (!api.reel) return;
    for (const shot of api.reel.shots) {
      if (shot.id === 'end') continue;
      const b = document.createElement('button');
      b.type = 'button';
      b.textContent = shot.id;
      b.title = `Play from ${shot.id}`;
      b.addEventListener('click', () => api.play({ from: shot.time, rate: rate() }));
      shots.append(b);
    }
  };

  $('opt-reduced').addEventListener('change', e => api.setReduced(e.target.checked));
  $('opt-wire').addEventListener('change', e => api.setWireframe(e.target.checked));
  $('opt-hinges').addEventListener('change', e => api.setHinges(e.target.checked));
  $('opt-orbit').addEventListener('change', e => api.setOrbit(e.target.checked));
  $('opt-reset').addEventListener('click', () => {
    $('opt-orbit').checked = false;
    api.setOrbit(false);
  });

  const presets = $('presets');
  for (const preset of chapter.presets) {
    const b = document.createElement('button');
    b.type = 'button';
    b.textContent = preset.label;
    b.addEventListener('click', () => {
      if (api.mode !== 'live') api.setMode('live');
      api.form.fill(preset.values);
    });
    presets.append(b);
  }
  const clear = document.createElement('button');
  clear.type = 'button';
  clear.className = 'ghost';
  clear.textContent = 'Clear form';
  clear.addEventListener('click', () => {
    if (api.mode !== 'live') api.setMode('live');
    api.form.reset();
  });
  presets.append(clear);

  let shotsFor = null;
  function sync() {
    const reel = api.mode === 'reel';
    $('mode-live').setAttribute('aria-pressed', String(!reel));
    $('mode-reel').setAttribute('aria-pressed', String(reel));
    $('opt-reduced').checked = api.reduced;
    playBtn.textContent = api.playing ? 'Pause' : 'Play';
    reelRow.dataset.active = String(reel);
    if (reel && api.reel) {
      if (shotsFor !== api.reel) {
        shotsFor = api.reel;
        renderShots();
      }
      const t = api.reel.tl.time();
      scrub.value = String(Math.round((t / api.reel.duration) * 1000));
      time.value = `${t.toFixed(2)} / ${api.reel.duration.toFixed(1)} s`;
    } else {
      time.value = 'live';
    }
  }

  function updateReadout() {
    if (panel.hidden) return;
    const s = api.state();
    const c = s.camera;
    const fps = s.render.meanFrameMs ? (1000 / s.render.meanFrameMs).toFixed(0) : '–';
    readout.textContent =
      `shot ${s.shot} · camera tilt ${c.tilt.toFixed(1)}° yaw ${c.yaw.toFixed(1)}° fov ${c.fov.toFixed(1)}° ` +
      `target (${c.tx.toFixed(1)}, ${c.tz.toFixed(1)}) · ${c.pxPerUnit} px/unit · ` +
      `${s.render.size.join('×')} @${s.render.pixelRatio}x · ${s.render.calls} calls · ~${fps} fps while animating (measured here) · ${s.render.renderer}`;
  }

  return { open, close, sync };
}

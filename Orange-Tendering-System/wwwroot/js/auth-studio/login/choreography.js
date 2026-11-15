import { Segment, retimer } from '../core/segments.js';
import { FRAMES, FRAMES_COMPACT } from './layout.js';

// ORANGE SIGNAL choreography, L00–L02 (flat stage).
//   intro  → the plotted line: lead-in, card outline, contact frame; the card develops
//   traces → circuit paths answer the e-mail field (a → b → stub | done)
//   pads   → contact plates develop once a password has been started

// Same pacing rule as Supplier Genesis: the opening was authored over 4.76 s and plays at this
// fraction of that pace on the real page (about 1.5 s of plotting, 2.7 s until the card has risen).
const INTRO_PACE = 0.58;

export function buildSignalChoreography(scene, stage) {
  const { rig, apply } = scene;
  const seg = name => new Segment(name, apply);

  // L00 — The First Signal --------------------------------------------------------------------
  const R = retimer(0.3, 0.16, INTRO_PACE);
  const intro = seg('intro');
  intro
    .keys(rig, 'dot', R.frames([[0.3, 0], [0.62, 1, 'back.out(2.2)']]))
    .keys(rig, 'draw', R.frames([[0.78, 0], [3.3, 1, 'plotter']]))
    .keys(rig, 'tip', R.frames([[0.78, 0], [0.95, 0.35, 'power1.out'], [3.0, 0.35], [3.3, 0, 'power1.in']]))
    .keys(rig, 'dividers', R.frames([[3.24, 0], [3.86, 1, 'power1.inOut']]))
    .keys(rig, 'devShell', R.frames([[3.7, 0], [4.5, 1, 'power2.out']]))
    .keys(rig, 'inkShell', R.frames([[3.78, 1], [4.46, 0, 'power1.inOut']]))
    .keys(rig, 'inkFrame', R.frames([[3.86, 1], [4.56, 0.42, 'power1.inOut']]))
    .keys(rig, 'inkDividers', R.frames([[3.86, 1], [4.56, 0.5, 'power1.inOut']]))
    .keys(rig, 'haloOn', R.frames([[4.56, 0], [4.76, 1]]))
    .label('drawn', R.time(3.3))
    .end(R.time(4.76));

  // L01 — Email: establishing the connection -------------------------------------------------
  // Plotted in runs with a short dwell between them. `stub` is where an unfinished address
  // leaves the signal path: started, not delivered.
  const traces = seg('traces');
  traces
    .keys(rig, 'tracesA', [[0, 0], [0.62, 1, 'power1.inOut']])
    .keys(rig, 'tracesB', [[0.7, 0], [1.5, 1, 'power1.inOut']])
    .keys(rig, 'signal', [[1.58, 0], [1.9, 0.56, 'power2.out'], [2.0, 0.56], [2.36, 1, 'power2.inOut']])
    .keys(rig, 'gate', [[2.3, 0], [2.66, 1, 'power1.inOut']])
    .keys(rig, 'terminals', [[2.42, 0], [2.8, 1, 'back.out(1.6)']])
    .label('a', 0.62)
    .label('b', 1.5)
    .label('stub', 1.95)
    .label('done', 2.8)
    .end(2.8);

  // L02 (flat) — Password: the contact surfaces develop -----------------------------------------
  // Triggered by "a password has been started", never by its length.
  const pads = seg('pads');
  pads
    .keys(rig, 'pads', [[0, 0], [1.0, 1, 'power1.inOut']])
    .keys(rig, 'inkDividers', [[0.1, 0.5], [0.8, 0, 'power1.inOut']])
    .keys(rig, 'inkFrame', [[0.3, 0.42], [1.0, 0, 'power1.inOut']])
    .keys(rig, 'ticks', [[0.5, 0], [1.2, 1, 'power1.inOut']])
    .end(1.25);

  const segments = { intro, traces, pads };
  const order = ['intro', 'traces', 'pads'];
  function reset() {
    for (const name of [...order].reverse()) segments[name].set(0);
  }

  const frames = () => (stage.width < 560 || stage.width / stage.height > 1.25 ? FRAMES_COMPACT : FRAMES);

  return { segments, order, reset, frames };
}

import { Segment, retimer } from '../core/segments.js';
import { FRAMES, FRAMES_COMPACT } from './layout.js';

// SUPPLIER GENESIS choreography, R00–R02.
// Each Segment is reversible and owns a clear physical idea. Timings are authored in seconds.
// Order of dependence (enforced by the director):
//   intro → letter → mail        (the letter and the card that comes out of it)
//   crease → walls → transfer → close → seal   (the carton that receives the card)

// The opening was authored over 5.2 s; on the real page it plays at this fraction of that pace
// (about 1.8 s of pen travel, 3 s until both objects have risen out of the page).
const INTRO_PACE = 0.58;

export function buildGenesisChoreography(scene, stage) {
  const { rig, apply } = scene;
  const seg = name => new Segment(name, apply);

  // R00 — The First Line ---------------------------------------------------------------------
  // Blank → the Orange point arrives → one continuous stroke draws the letter, arches over and
  // draws the carton dieline → details → each object develops from line into paper.
  const R = retimer(0.3, 0.16, INTRO_PACE);
  const intro = seg('intro');
  intro
    .keys(rig, 'dot', R.frames([[0.3, 0], [0.62, 1, 'back.out(2.2)']]))
    .keys(rig, 'draw', R.frames([[0.78, 0], [3.86, 1, 'penStroke']]))
    .keys(rig, 'tip', R.frames([[0.78, 0], [1.0, 0.5, 'power1.out'], [3.5, 0.5], [3.9, 0, 'power1.in']]))
    .keys(rig, 'seams', R.frames([[2.02, 0], [2.5, 1, 'power2.inOut']]))
    .keys(rig, 'devEnv', R.frames([[2.3, 0], [3.15, 1, 'power2.out']]))
    .keys(rig, 'inkEnv', R.frames([[2.4, 1], [3.1, 0, 'power1.inOut']]))
    .keys(rig, 'seamInk', R.frames([[2.6, 1], [3.3, 0.34, 'power1.inOut']]))
    .keys(rig, 'scores', R.frames([[3.78, 0], [4.36, 1, 'power1.inOut']]))
    .keys(rig, 'devBox', R.frames([[4.12, 0], [4.98, 1, 'power2.out']]))
    .keys(rig, 'inkBox', R.frames([[4.22, 1], [4.95, 0, 'power1.inOut']]))
    .keys(rig, 'haloOn', R.frames([[5.0, 0], [5.2, 1]]))
    .label('drawn', R.time(3.9))
    .end(R.time(5.2));

  // R01 — The letter opens and the card comes out of it ----------------------------------------
  const letter = seg('letter');
  letter
    // Anticipation (the crease takes the load), swing through, spring back, then the card presses it flat.
    .keys(rig, 'flap', [
      [0.0, 0],
      [0.14, 5, 'power2.out'],
      [0.74, 164, 'power2.inOut'],
      [0.92, 157, 'sine.inOut'],
      [1.08, 161, 'sine.inOut'],
      [1.4, 180, 'power2.out']
    ])
    .keys(rig, 'cardOut', [[1.02, 0], [1.74, 1, 'power3.out']])
    .keys(rig, 'cardInk', [[1.5, 0], [2.3, 1, 'power1.inOut']])
    .label('open', 1.08)
    .end(2.3);

  // R01 — Email: a line leaves the card and forms a contact glyph -------------------------------
  const mail = seg('mail');
  mail
    .keys(rig, 'mail', [[0, 0], [0.34, 0.34, 'power1.out'], [1.0, 1, 'power1.inOut']])
    .label('begun', 0.34)
    .label('done', 1.0)
    .end(1.0);

  // R02a — Establish the fold lines: scores are pressed and every flap answers with a small flex.
  const crease = seg('crease');
  crease.keys(rig, 'groove', [[0, 0], [0.34, 1, 'power2.out']]);
  [['wL', 0.04], ['wR', 0.09], ['wF', 0.14], ['wB', 0.19]].forEach(([p, d]) => {
    crease.keys(rig, p, [[d, 0], [d + 0.2, 7, 'power2.out'], [d + 0.46, 3, 'sine.inOut']]);
  });
  crease.end(0.66);

  // R02b — Side walls, then the front wall with its half-lid falling open ---------------------
  // Deliberately overlapped so every intermediate time is a believable "partly folded" pose:
  // a weak password parks the playhead part-way (labels p1..p4).
  const walls = seg('walls');
  walls
    .keys(rig, 'wL', [[0.0, 3], [0.66, 90, 'boardSettle']])
    .keys(rig, 'wR', [[0.12, 3], [0.8, 90, 'boardSettle']])
    .keys(rig, 'wF', [[0.26, 3], [0.98, 90, 'boardSettle']])
    .keys(rig, 'bF', [[0.34, 0], [1.04, -68, 'power2.inOut'], [1.2, -62, 'sine.inOut']])
    .label('p1', 0.2)
    .label('p2', 0.33)
    .label('p3', 0.45)
    .label('p4', 0.56)
    .label('up', 1.2)
    .end(1.2);

  // R02c — The card leaves the letter and is lowered into the carton ------------------------------
  const transfer = seg('transfer');
  transfer
    .keys(rig, 'fly', [[0, 0], [1.5, 1, 'glide']])
    .keys(rig, 'flap', [[0.2, 180], [0.7, 170, 'sine.out']])
    .end(1.56);

  // R02d — Back wall up, then the two half-lids close; the Orange point rides the lid's rim -----
  const close = seg('close');
  close
    .keys(rig, 'wafer', [[0, 0], [0.01, 1]])
    .keys(rig, 'waferLit', [[0.02, 0], [0.4, 1, 'power1.inOut']])
    .keys(rig, 'wB', [[0.0, 3], [0.6, 90, 'boardSettle']])
    .keys(rig, 'bB', [[0.1, 0], [0.62, -64, 'power2.inOut'], [0.92, -62, 'sine.inOut'], [1.5, 90, 'power2.inOut']])
    .keys(rig, 'bF', [[1.22, -62], [1.36, -70, 'sine.out'], [1.98, 90, 'power2.inOut']])
    // The point lifts clear as the second lid comes down, ready to stamp.
    .keys(rig, 'hop', [[1.38, 0], [1.8, 0.62, 'power2.out']])
    .label('boxed', 0.92)
    .label('ajar', 1.5)
    .label('hesitate', 1.76)
    .label('closed', 2.02)
    .end(2.02);

  // R02e — The seal: the point drops, squashes, and spreads into a paper wafer over the seam ----
  const seal = seg('seal');
  seal
    .keys(rig, 'hop', [[0, 0.62], [0.18, 0, 'power2.in']])
    .keys(rig, 'press', [[0.16, 0], [0.24, 1, 'power2.out'], [0.62, 0, 'elastic.out(1, 0.6)']])
    .keys(rig, 'seal', [[0.17, 0], [0.72, 1, 'power2.out']])
    .keys(rig, 'stamp', [[0.5, 0], [0.92, 1, 'power1.inOut']])
    .end(0.95);

  const segments = { intro, letter, mail, crease, walls, transfer, close, seal };

  /** Rewind everything to the blank page (reverse order, so shared hinges unwind correctly). */
  const order = ['intro', 'letter', 'mail', 'crease', 'walls', 'transfer', 'close', 'seal'];
  function reset() {
    for (const name of [...order].reverse()) segments[name].set(0);
  }

  // Camera frames adapt to the stage: compact banner on narrow screens.
  const frames = () => (stage.width < 560 || stage.width / stage.height > 1.25 ? FRAMES_COMPACT : FRAMES);

  // Compact stage only: the banner cannot hold the whole drawing, so the camera follows the pen
  // down to the dieline while it is being drawn.
  const introFollow = { at: R.time(2.5), duration: 0.95 * Math.max(INTRO_PACE, 0.7) };

  return { segments, order, reset, frames, introFollow };
}

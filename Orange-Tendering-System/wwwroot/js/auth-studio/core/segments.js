import gsap from 'gsap';
import { CustomEase } from 'gsap/CustomEase';

gsap.registerPlugin(CustomEase);

// Material-specific eases. One curve for everything is what makes motion look generic:
// paper overshoots and breathes, board is stiff, a carried card glides.
CustomEase.create('paperSettle', 'M0,0 C0.18,0 0.32,0.62 0.46,0.9 0.54,1.06 0.64,1.06 0.72,1.02 0.8,0.985 0.9,1 1,1');
CustomEase.create('boardSettle', 'M0,0 C0.2,0 0.34,0.7 0.5,0.95 0.58,1.035 0.68,1.03 0.76,1.008 0.84,0.994 0.92,1 1,1');
CustomEase.create('glide', 'M0,0 C0.12,0 0.16,0.06 0.24,0.14 0.42,0.34 0.62,0.86 0.8,0.955 0.9,0.985 0.94,1 1,1');
CustomEase.create('penStroke', 'M0,0 C0.08,0 0.14,0.05 0.2,0.12 0.45,0.42 0.6,0.62 0.8,0.88 0.88,0.97 0.94,1 1,1');
CustomEase.create('plotter', 'M0,0 C0.04,0 0.06,0.03 0.1,0.08 0.4,0.38 0.6,0.62 0.9,0.92 0.94,0.97 0.96,1 1,1');

/**
 * A Segment is one reversible piece of choreography: a paused timeline that only writes the rig.
 * The director moves its playhead toward a goal (forwards or backwards, interruptible). Because
 * every tween has explicit start and end values, any playhead position always renders the same pose.
 */
export class Segment {
  constructor(name, apply) {
    this.name = name;
    this.apply = apply;
    this.tl = gsap.timeline({ paused: true, defaults: { ease: 'none', immediateRender: false, lazy: false }, onUpdate: apply });
    this.goal = 0;
    this.tween = null;
  }

  /** Keyframes for one rig property: [[time, value, ease?], ...] → explicit fromTo tweens. */
  keys(target, prop, frames) {
    for (let i = 1; i < frames.length; i++) {
      const [t0, v0] = frames[i - 1];
      const [t1, v1, ease = 'none'] = frames[i];
      this.tl.fromTo(target, { [prop]: v0 }, { [prop]: v1, duration: Math.max(0.0001, t1 - t0), ease }, t0);
    }
    return this;
  }

  label(name, time) {
    this.tl.addLabel(name, time);
    return this;
  }

  /** Pads the timeline so its duration is exactly `t` (labels may sit after the last tween). */
  end(t) {
    this.tl.to({}, { duration: 0.001 }, Math.max(0, t - 0.001));
    return this;
  }

  get duration() {
    return this.tl.duration();
  }

  timeOf(target) {
    if (typeof target === 'number') return target;
    if (target === 'end') return this.tl.duration();
    const t = this.tl.labels[target];
    if (t === undefined) throw new Error(`Segment ${this.name}: unknown label "${target}"`);
    return t;
  }

  at(target) {
    return Math.abs(this.tl.time() - this.timeOf(target)) < 1e-3;
  }

  get moving() {
    return Boolean(this.tween && this.tween.isActive());
  }

  /** Move the playhead toward a goal at natural speed (scaled by `speed`). */
  go(target, { speed = 1, onDone } = {}) {
    const t = this.timeOf(target);
    this.goal = target;
    if (this.tween) this.tween.kill();
    this.tween = null;
    const delta = Math.abs(this.tl.time() - t);
    if (delta < 1e-4) {
      onDone?.();
      return;
    }
    this.tween = this.tl.tweenTo(t, { ease: 'none', lazy: false, duration: delta / speed, onComplete: () => {
      this.tween = null;
      onDone?.();
    } });
  }

  /** Jump (reduced motion, initial state). */
  set(target) {
    if (this.tween) this.tween.kill();
    this.tween = null;
    this.goal = target;
    // totalTime(t, false): seek()/pause(t) would suppress child callbacks.
    this.tl.pause();
    this.tl.totalTime(this.timeOf(target), false);
    this.apply();
  }
}

/**
 * Rescales authored keyframe times: t' = start + (t - origin) * pace. The opening drawings were
 * authored at a studied pace; one constant sets how briskly they play on the real pages without
 * disturbing the relative timing between the pen, the details and the development of each object.
 */
export function retimer(origin, start, pace) {
  const time = t => +(start + (t - origin) * pace).toFixed(3);
  const frames = list => list.map(([t, ...rest]) => [time(t), ...rest]);
  return { time, frames };
}

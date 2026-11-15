import gsap from 'gsap';

// Live director for ORANGE SIGNAL (L00–L02, flat stage).
//
//   input = {
//     email:       'empty' | 'typing' | 'domain' | 'valid' | 'invalid'   (format only)
//     hasPassword: boolean    (never the value, never the length)
//     focus:       'email' | 'password' | null
//   }
//
// A valid e-mail FORMAT completes the drawing; it says nothing about whether an account exists.
// Nothing in this stage represents a credential check: that begins at L03, with a real server.

const TRACE_GOAL = { empty: 0, typing: 'a', domain: 'b', invalid: 'stub', valid: 'done' };

export class SignalDirector {
  constructor({ scene, choreography, stage, onStatus = () => {} }) {
    this.scene = scene;
    this.segs = choreography.segments;
    this.reset = choreography.reset;
    this.frames = choreography.frames;
    this.stage = stage;
    this.onStatus = onStatus;
    this.reduced = false;
    this.enabled = true;
    this.introDone = false;
    this.camName = null;
    this.input = { email: 'empty', hasPassword: false, focus: null };
    this.status = '';
    this.halo = null;
  }

  start() {
    Object.assign(this.stage.rig.state, this.frames().wide);
    this.camName = 'wide';
    if (this.reduced) {
      this.segs.intro.set('end');
      this.finishIntro();
      return;
    }
    this.segs.intro.go('end', { onDone: () => this.finishIntro() });
  }

  finishIntro() {
    this.introDone = true;
    this.say('Illustration ready: the outline of a SIM card.');
    this.startHalo();
    this.plan();
  }

  setReduced(on) {
    this.reduced = on;
    if (on) {
      this.stopHalo();
      for (const s of Object.values(this.segs)) if (s.moving) s.set(s.goal);
      if (!this.introDone) {
        this.segs.intro.set('end');
        this.introDone = true;
      }
      this.plan();
    } else if (this.introDone) {
      this.startHalo();
    }
  }

  setEnabled(on) {
    this.enabled = on;
    if (!on) {
      for (const s of Object.values(this.segs)) {
        if (s.tween) s.tween.kill();
        s.tween = null;
      }
      gsap.killTweensOf(this.stage.rig.state);
      this.stopHalo();
      return;
    }
    const keep = this.reduced;
    this.reduced = true;
    this.introDone = true;
    this.reset();
    this.segs.intro.set('end');
    this.camName = null;
    this.plan();
    this.reduced = keep;
    if (!keep) this.startHalo();
  }

  update(partial) {
    Object.assign(this.input, partial);
    if (this.enabled) this.plan();
  }

  move(name, target, opts = {}) {
    const s = this.segs[name];
    if (this.reduced) {
      if (!s.at(target)) s.set(target);
      return true;
    }
    if (s.at(target)) {
      if (s.moving) {
        s.tween.kill();
        s.tween = null;
      }
      s.goal = target;
      return true;
    }
    if (s.goal === target && s.moving) return false;
    s.go(target, { ...opts, onDone: () => this.plan() });
    return false;
  }

  plan() {
    if (!this.enabled) return;
    const i = this.input;
    if (!this.introDone) {
      if ((i.email !== 'empty' || i.hasPassword) && !this.rushed) {
        this.rushed = true;
        this.segs.intro.go('end', { speed: 2.6, onDone: () => this.finishIntro() });
      }
      return;
    }

    const goal = TRACE_GOAL[i.email] ?? 0;
    const retreat = this.segs.traces.tl.time() > this.segs.traces.timeOf(goal);
    this.move('traces', goal, { speed: retreat ? 1.6 : 1 });
    const warn = i.email === 'invalid' ? 1 : 0;
    if (this.scene.rig.warn !== warn) {
      this.scene.rig.warn = warn;
      this.scene.apply();
    }

    this.move('pads', i.hasPassword ? 'end' : 0, { speed: i.hasPassword ? 1 : 1.5 });

    this.aim();
    this.report();
  }

  shot() {
    const i = this.input;
    if (!this.introDone) return 'wide';
    if (i.focus === 'password' && i.email !== 'invalid') return 'core';
    if (i.hasPassword && i.focus !== 'email') return 'core';
    if (i.email !== 'empty' || i.focus === 'email') return 'traces';
    return 'wide';
  }

  aim(force = false) {
    const name = this.shot();
    if (name === this.camName && !force) return;
    this.camName = name;
    const pose = this.frames()[name];
    const state = this.stage.rig.state;
    gsap.killTweensOf(state);
    if (this.reduced || force) {
      Object.assign(state, pose);
      this.stage.invalidate();
      return;
    }
    gsap.to(state, { ...pose, duration: 1.1, ease: 'power2.inOut', onUpdate: () => this.stage.invalidate() });
  }

  reframe() {
    if (this.enabled) this.aim(true);
  }

  startHalo() {
    if (this.halo || this.reduced) return;
    const rig = this.scene.rig;
    this.halo = gsap.fromTo(rig, { halo: 0 }, { halo: 1, duration: 1.9, ease: 'power1.out', repeat: -1, repeatDelay: 2.6, delay: 1.2, onUpdate: this.scene.apply });
  }

  stopHalo() {
    this.halo?.kill();
    this.halo = null;
    this.scene.rig.halo = 0;
  }

  report() {
    const g = this.segs;
    const i = this.input;
    let text = this.status;
    if (i.email === 'invalid') text = 'One connection path is unfinished: check the email address.';
    else if (g.pads.at('end') && g.traces.at('done')) text = 'The drawing is complete. Nothing has been checked yet: sign-in starts when you continue.';
    else if (g.traces.at('done')) text = 'The connection paths are drawn.';
    else if (g.traces.at(0) && g.pads.at(0)) text = 'Illustration ready: the outline of a SIM card.';
    if (text !== this.status) this.say(text);
  }

  say(text) {
    this.status = text;
    this.onStatus(text);
  }

  destroy() {
    this.stopHalo();
    for (const s of Object.values(this.segs)) s.tl.kill();
    gsap.killTweensOf(this.stage.rig.state);
  }
}

import gsap from 'gsap';

// Live director for SUPPLIER GENESIS (R00–R02).
//
// The form owns the truth. The director receives a small, sanitised description of form
// readiness — never the password, never the e-mail address — and decides where each Segment's
// playhead should rest. Segments move toward their goals, can be interrupted and retargeted at any
// frame, and reverse only the last relevant fold when something becomes invalid.
//
//   input = {
//     name:        string   (shown on the card, locally only)
//     email:       'empty' | 'typing' | 'valid' | 'invalid'
//     hasPassword: boolean
//     rules:       0..5     (how many password requirements are met — the same thing the visible checklist shows)
//     confirm:     'empty' | 'partial' | 'match' | 'mismatch'
//     focus:       'name' | 'email' | 'password' | 'confirm' | null
//   }
//
// Nothing here means "account created": a sealed carton is form readiness only.

const WALL_LEVEL = [0, 'p1', 'p2', 'p3', 'p4', 'up'];

export class GenesisDirector {
  constructor({ scene, choreography, stage, onStatus = () => {} }) {
    this.scene = scene;
    this.segs = choreography.segments;
    this.reset = choreography.reset;
    this.frames = choreography.frames;
    this.introFollow = choreography.introFollow;
    this.stage = stage;
    this.onStatus = onStatus;
    this.reduced = false;
    this.enabled = true;
    this.introDone = false;
    this.cardBoxed = false;
    this.nameEver = false;
    this.camName = null;
    this.input = { name: '', email: 'empty', hasPassword: false, rules: 0, confirm: 'empty', focus: null };
    this.status = '';
    this.halo = null;
  }

  // ---- lifecycle ----------------------------------------------------------------------------
  start() {
    Object.assign(this.stage.rig.state, this.frames().wide);
    this.camName = 'wide';
    if (this.reduced) {
      this.segs.intro.set('end');
      this.introDone = true;
      this.say('Illustration ready: a letter and a flat carton.');
      this.plan();
      return;
    }
    this.segs.intro.go('end', { onDone: () => this.finishIntro() });
    const F = this.frames();
    if (F.net) {
      // Compact stage: follow the pen down to the carton while it is being drawn.
      const state = this.stage.rig.state;
      this.follow = gsap.delayedCall(this.introFollow.at, () => {
        gsap.to(state, { ...F.net, duration: this.introFollow.duration, ease: 'power2.inOut', onUpdate: () => this.stage.invalidate() });
      });
    }
  }

  finishIntro() {
    this.follow?.kill();
    this.camName = null; // re-aim even if the shot name is unchanged
    this.introDone = true;
    this.say('Illustration ready: a letter and a flat carton.');
    this.startHalo();
    this.plan();
  }

  setReduced(on) {
    this.reduced = on;
    this.scene.nameLine.reduced = on;
    if (on) {
      this.stopHalo();
      // Land every in-flight move on its goal.
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

  /** Suspend while the inspector's showreel owns the playheads. */
  setEnabled(on) {
    this.enabled = on;
    if (!on) {
      for (const s of Object.values(this.segs)) {
        if (s.tween) s.tween.kill();
        s.tween = null;
      }
      gsap.killTweensOf(this.stage.rig.state);
      this.follow?.kill();
      this.camTimer?.kill();
      this.camTimer = null;
      this.stopHalo();
      return;
    }
    // Returning from the showreel: rebuild the live state from the form, without animation.
    const keep = this.reduced;
    this.reduced = true;
    this.introDone = true;
    this.cardBoxed = false;
    this.reset();
    this.segs.intro.set('end');
    this.scene.setName(this.input.name, { instant: true });
    this.camName = null;
    this.plan();
    this.reduced = keep;
    if (!keep) this.startHalo();
  }

  // ---- input --------------------------------------------------------------------------------
  update(partial) {
    Object.assign(this.input, partial);
    if (this.input.name.trim().length > 0) this.nameEver = true;
    if ('name' in partial) this.scene.setName(this.input.name, { instant: this.reduced });
    if (this.enabled) this.plan();
  }

  hasInput() {
    const i = this.input;
    return this.nameEver || i.email !== 'empty' || i.hasPassword || i.confirm !== 'empty';
  }

  // ---- planning -----------------------------------------------------------------------------
  /** Returns true when the segment already rests on `target`; otherwise starts moving it there. */
  move(name, target, opts = {}) {
    const s = this.segs[name];
    if (this.reduced) {
      if (!s.at(target)) {
        s.set(target);
        this.changed = true;
      }
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
    // A long backlog (paste, autofill, password manager) plays brisker so the scene catches up.
    const speed = (opts.speed ?? 1) * this.pace();
    s.go(target, { ...opts, speed, onDone: () => { opts.onDone?.(); this.plan(); } });
    return false;
  }

  plan() {
    if (!this.enabled) return;
    // With reduced motion every move lands instantly, so keep resolving until the chain is stable.
    let guard = 0;
    do {
      this.changed = false;
      this.step();
    } while (this.reduced && this.changed && ++guard < 10);
    if (!this.introDone) return;
    this.aim();
    this.report();
  }

  step() {
    const g = this.segs;
    const i = this.input;

    if (!this.introDone) {
      // Someone is already typing: finish the opening briskly instead of making them wait.
      if (this.hasInput() && !this.rushed) {
        this.rushed = true;
        this.follow?.kill();
        g.intro.go('end', { speed: 2.6, onDone: () => this.finishIntro() });
      }
      return;
    }

    // Letter and card (latched: clearing the name later never swallows the card back).
    if (this.nameEver) this.move('letter', 'end');
    const cardOut = g.letter.at('end');

    if (cardOut) {
      const goal = i.email === 'valid' ? 'done' : i.email === 'empty' ? 0 : 'begun';
      this.move('mail', goal, { speed: goal === 0 ? 1.8 : 1 });
    }
    const warn = i.email === 'invalid' ? 1 : 0;
    if (this.scene.rig.mailWarn !== warn) {
      this.scene.rig.mailWarn = warn;
      this.scene.apply();
    }

    // Carton
    const committed = !g.transfer.at(0) || this.cardBoxed;
    if (!committed) {
      const level = i.hasPassword ? WALL_LEVEL[i.rules] : 0;
      if (level === 0) {
        if (this.move('walls', 0, { speed: 1.5 })) this.move('crease', i.hasPassword ? 'end' : 0, { speed: i.hasPassword ? 1 : 1.5 });
      } else if (this.move('crease', 'end')) {
        const lowering = g.walls.tl.time() > g.walls.timeOf(level);
        const up = this.move('walls', level, { speed: lowering ? 1.4 : 1 });
        if (up && level === 'up' && cardOut) this.beginTransfer();
      }
    } else if (!this.cardBoxed) {
      this.beginTransfer();
    } else {
      // The lids answer to the password pair; the seal answers to the whole contact step.
      const ready = i.rules === 5 && i.confirm === 'match';
      const complete = ready && i.name.trim().length > 0 && i.email === 'valid';
      if (ready) {
        this.hesitating = false;
        if (this.move('close', 'closed')) this.move('seal', complete ? 'end' : 0, { speed: complete ? 1 : 1.7 });
      } else if (this.move('seal', 0, { speed: 1.7 })) {
        const ajar = i.confirm === 'mismatch' || (i.rules < 5 && i.confirm !== 'empty');
        if (!ajar) {
          this.hesitating = false;
          this.move('close', 'boxed', { speed: 1.2 });
        } else if (g.close.tl.time() > g.close.timeOf('ajar') + 0.01 && !this.hesitating) {
          // Was closed (or closing): open back to ajar.
          this.move('close', 'ajar', { speed: 1.1 });
        } else if (!g.close.at('ajar') && !this.hesitating) {
          // The final lid starts to close, stops short, and backs off.
          this.hesitating = true;
          if (this.reduced) {
            g.close.set('ajar');
            this.hesitating = false;
          } else {
            g.close.go('hesitate', {
              onDone: () => g.close.go('ajar', { speed: 0.75, onDone: () => { this.hesitating = false; this.plan(); } })
            });
          }
        }
      }
    }
  }

  /** 1 when the scene is in step with the form, up to 1.6 when several stages are still owed. */
  pace() {
    const i = this.input;
    const g = this.segs;
    let owed = 0;
    if (this.nameEver && !g.letter.at('end')) owed++;
    if (i.hasPassword && i.rules === 5 && !g.walls.at('up')) owed++;
    if (i.rules === 5 && i.confirm === 'match') {
      if (!this.cardBoxed) owed++;
      if (!g.close.at('closed')) owed++;
    }
    return 1 + 0.2 * Math.max(0, Math.min(3, owed - 1));
  }

  beginTransfer() {
    if (this.cardBoxed) return;
    const done = this.move('transfer', 'end', { onDone: () => { this.cardBoxed = true; } });
    if (done) {
      this.cardBoxed = true;
      this.changed = true;
    }
    if (!this.reduced && !this.camTimer) {
      // Follow the card, then arrive at the fold just before it lands.
      this.camTimer = gsap.delayedCall(0.95, () => {
        this.camTimer = null;
        this.aim();
      });
    }
  }

  // ---- camera -------------------------------------------------------------------------------
  shot() {
    const g = this.segs;
    const i = this.input;
    if (!this.introDone) return 'wide';
    // From Company Details on, the campus director (./campus-director.js) directs the camera.
    const campusShot = this.shotOverride?.();
    if (campusShot) return campusShot;
    if (!g.transfer.at(0) || this.cardBoxed) {
      if (!this.cardBoxed && g.transfer.tl.progress() < 0.6) return 'transfer';
      if (i.rules === 5 && i.confirm === 'match' && g.close.tl.time() >= g.close.timeOf('hesitate')) return 'sealed';
      return 'fold';
    }
    // Stay with the card while its contact detail is unfinished and no password has been started.
    const cardNeedsAttention = i.email === 'invalid' && !i.hasPassword;
    if ((i.focus === 'password' || i.focus === 'confirm') && !cardNeedsAttention) return 'carton';
    if (i.hasPassword && i.focus !== 'name' && i.focus !== 'email') return 'carton';
    if (this.nameEver) return 'letter';
    return 'wide';
  }

  aim(force = false) {
    const name = this.shot();
    if (name === this.camName && !force) return;
    const from = this.camName;
    this.camName = name;
    const pose = { ty: 0, ...(this.poseOf?.(name) ?? this.frames()[name]) };
    const state = this.stage.rig.state;
    gsap.killTweensOf(state);
    if (this.reduced || force) {
      Object.assign(state, pose);
      this.stage.invalidate();
      return;
    }
    // Leaving the flat package for the campus is the 2D→3D handoff: slower, with a long settle.
    const handoff = name === 'unfold' || (from === 'unfold' && name === 'campus');
    gsap.to(state, { ...pose, duration: handoff ? 1.6 : 1.15, ease: handoff ? 'power3.inOut' : 'power2.inOut', onUpdate: () => this.stage.invalidate() });
  }

  /** The stage changed size or shape: re-frame the current shot. */
  reframe() {
    if (this.enabled) this.aim(true);
  }

  // ---- idle ---------------------------------------------------------------------------------
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

  // ---- accessible status (form readiness only) ------------------------------------------------
  report() {
    const g = this.segs;
    const i = this.input;
    let text = this.status;
    if (g.seal.at('end')) text = 'Contact details are complete. The illustration shows a sealed package; nothing has been submitted yet.';
    else if (g.close.at('closed') && !g.seal.moving) text = 'The package is closed but not sealed: the name or email address still needs attention.';
    else if (this.cardBoxed && i.confirm === 'mismatch') text = 'The package stays open: the two passwords do not match.';
    else if (this.cardBoxed && !(i.rules === 5 && i.confirm === 'match')) text = 'The contact card is in the package. Confirm the password to close it.';
    else if (g.letter.at('end') && i.email === 'invalid') text = 'The contact detail on the card is unfinished: check the email address.';
    else if (g.letter.at('end')) text = 'A contact card has come out of the letter.';
    if (text !== this.status) this.say(text);
  }

  say(text) {
    this.status = text;
    this.onStatus(text);
  }

  destroy() {
    this.stopHalo();
    this.camTimer?.kill();
    this.follow?.kill();
    for (const s of Object.values(this.segs)) s.tl.kill();
    gsap.killTweensOf(this.stage.rig.state);
  }
}

import gsap from 'gsap';

// Live director for ORANGE SIGNAL.
//
// L00–L02 (flat drawing) follow the sanitised form state:
//   input = {
//     email:       'empty' | 'typing' | 'domain' | 'valid' | 'invalid'   (format only)
//     hasPassword: boolean    (never the value, never the length)
//     focus:       'email' | 'password' | null
//   }
// A valid e-mail FORMAT completes the drawing; it says nothing about whether an account exists.
//
// L03–L10 follow what really happened, reported by the form scripts (supplier-login.js,
// biometric.js): the request was sent, the server refused / did not answer / accepted / asked for
// the supplier's identity step, the camera opened, the server confirmed the face, the session was
// issued. The director never decides any of it, never waits on it and never delays a request:
//
//   idle ──submitted──▶ verify ──rejected──▶ refused ──submitted──▶ verify …
//                         │ ──unavailable/timeout──▶ nosignal
//                         │ ──accepted──▶ passed ──signed-in──▶ finale
//                         └ ──biometric──▶ choose ──face requesting──▶ optics ──confirmed, signed-in──▶ finale
//                                            ▲            │ failed: stays, aperture ready again
//                                            └─cancelled──┘

const TRACE_GOAL = { empty: 0, typing: 'a', domain: 'b', invalid: 'stub', valid: 'done' };
const PHASE_SHOT = { verify: 'verify', refused: 'verify', nosignal: 'verify', passed: 'verify', choose: 'choose', optics: 'optics' };
const CAMERA_PROBLEMS = ['cameraDenied', 'cameraMissing', 'cameraUnavailable'];

export class SignalDirector {
  constructor({ scene, choreography, stage, onStatus = () => {}, stageEl = null, shell = null, cameraEl = null }) {
    this.scene = scene;
    this.segs = choreography.segments;
    this.reset = choreography.reset;
    this.frames = choreography.frames;
    this.stage = stage;
    this.onStatus = onStatus;
    this.stageEl = stageEl;
    this.shell = shell;
    this.cameraEl = cameraEl;
    this.reduced = false;
    this.enabled = true;
    this.introDone = false;
    this.camName = null;
    this.phase = 'idle';
    this.input = { email: 'empty', hasPassword: false, focus: null };
    this.status = '';
    this.halo = null;
    this.waitLoop = null;
    this.procLoop = null;
    this.confirmedAt = 0;
    this.navigated = false;

    // The live camera is placed in the opening the iris blades leave, every frame it changes.
    if (cameraEl) {
      stage.updaters.add(() => this.placeCamera());
    }
  }

  // ------------------------------------------------------------------------------------------------
  // L00–L02
  // ------------------------------------------------------------------------------------------------

  start() {
    Object.assign(this.stage.rig.state, { ty: 0 }, this.frames().wide);
    this.camName = 'wide';
    if (this.reduced) {
      this.segs.intro.set('end');
      this.finishIntro();
      return;
    }
    this.segs.intro.go('end', { onDone: () => this.finishIntro() });
  }

  finishIntro() {
    if (this.introDone) return;
    this.introDone = true;
    this.say('Illustration ready: the outline of a SIM card.');
    this.startHalo();
    this.plan();
  }

  /** Something real happened before the opening finished: complete it at once. */
  completeIntro() {
    if (this.introDone) return;
    this.segs.intro.set('end');
    this.finishIntro();
  }

  setReduced(on) {
    this.reduced = on;
    if (on) {
      this.stopHalo();
      this.stopWait();
      this.stopProc();
      for (const s of Object.values(this.segs)) if (s.moving) s.set(s.goal);
      if (!this.introDone) {
        this.segs.intro.set('end');
        this.introDone = true;
      }
      this.plan();
    } else if (this.introDone && this.phase === 'idle') {
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

    // Once the form has been sent, the paths it travelled stay drawn until the page starts over.
    const sent = this.phase !== 'idle' && this.phase !== 'refused' && this.phase !== 'nosignal';
    const goal = sent ? 'done' : TRACE_GOAL[i.email] ?? 0;
    const retreat = this.segs.traces.tl.time() > this.segs.traces.timeOf(goal);
    this.move('traces', goal, { speed: retreat ? 1.6 : sent ? 2.2 : 1 });
    const warn = i.email === 'invalid' && !sent ? 1 : 0;
    if (this.scene.rig.warn !== warn) {
      this.scene.rig.warn = warn;
      this.scene.apply();
    }

    const pads = sent || i.hasPassword;
    this.move('pads', pads ? 'end' : 0, { speed: pads ? (sent ? 2 : 1) : 1.5 });

    this.aim();
    if (this.phase === 'idle') this.report();
  }

  shot() {
    const i = this.input;
    if (!this.introDone) return 'wide';
    if (PHASE_SHOT[this.phase]) return PHASE_SHOT[this.phase];
    if (i.focus === 'password' && i.email !== 'invalid') return 'core';
    if (i.hasPassword && i.focus !== 'email') return 'core';
    if (i.email !== 'empty' || i.focus === 'email') return 'traces';
    return 'wide';
  }

  aim(force = false) {
    if (this.phase === 'finale') return;
    const name = this.shot();
    if (name === this.camName && !force) return;
    const from = this.camName;
    this.camName = name;
    const pose = { ty: 0, ...this.frames()[name] };
    const state = this.stage.rig.state;
    gsap.killTweensOf(state);
    if (this.reduced || force) {
      Object.assign(state, pose);
      this.stage.invalidate();
      return;
    }
    // Leaving the flat drawing is the 2D→3D handoff: the lens widens while the camera tilts, so the
    // drawing keeps its size on screen and simply gains depth. The macro approach to the optics
    // and the return from it are slower, with a long settle.
    const duration = name === 'optics' || from === 'optics' ? 1.25 : (from === 'core' || from === 'traces' || from === 'wide') && name === 'verify' ? 1.35 : 1.1;
    gsap.to(state, { ...pose, duration, ease: name === 'optics' ? 'power3.inOut' : 'power2.inOut', onUpdate: () => this.stage.invalidate() });
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

  // ------------------------------------------------------------------------------------------------
  // L03–L10
  // ------------------------------------------------------------------------------------------------

  /** Tween rig values; instant under reduced motion. Each material keeps its own ease. */
  to(props, { duration = 0.6, ease = 'power2.inOut', delay = 0, onComplete } = {}) {
    const rig = this.scene.rig;
    if (this.reduced) {
      gsap.killTweensOf(rig, Object.keys(props).join(','));
      Object.assign(rig, props);
      this.scene.apply();
      onComplete?.();
      return null;
    }
    return gsap.to(rig, { ...props, duration, ease, delay, overwrite: 'auto', onUpdate: this.scene.apply, onComplete });
  }

  startWait() {
    if (this.reduced || this.waitLoop) return;
    const rig = this.scene.rig;
    this.waitLoop = gsap.fromTo(rig, { wait: 0 }, { wait: 1, duration: 1.5, ease: 'power1.out', repeat: -1, repeatDelay: 0.25, onUpdate: this.scene.apply });
  }

  stopWait() {
    this.waitLoop?.kill();
    this.waitLoop = null;
    this.scene.rig.wait = 0;
    this.scene.apply();
  }

  startProc() {
    if (this.reduced || this.procLoop) return;
    const rig = this.scene.rig;
    this.procLoop = gsap.fromTo(rig, { procPhase: 0 }, { procPhase: 1, duration: 2.4, ease: 'none', repeat: -1, onUpdate: this.scene.apply });
  }

  stopProc() {
    this.procLoop?.kill();
    this.procLoop = null;
  }

  setPhase(phase) {
    this.phase = phase;
    if (this.shell) this.shell.dataset.signal = phase;
    if (phase !== 'idle') this.stopHalo();
    const optics = phase === 'optics';
    if (this.stageEl) this.stageEl.dataset.optics = optics ? '1' : '0';
    if (this.shell) this.shell.dataset.optics = optics ? '1' : '0';
  }

  /** Request phases from supplier-login.js. */
  signal(phase) {
    this.completeIntro();
    const rig = this.scene.rig;
    switch (phase) {
      case 'submitted': {
        this.setPhase('verify');
        this.stopWait();
        rig.pulseSig = 0;
        rig.pulseBranch = 0;
        this.to({ gateErr: 0, gateWarn: 0, gateOk: 0, block: 0 }, { duration: 0.3 });
        this.to({ depth: 1 }, { duration: 1.3, ease: 'power2.inOut' });
        this.to({ lift: 1 }, { duration: 0.85, ease: 'power3.out', delay: 0.45 });
        this.to({ pulse: 1 }, { duration: 0.25, delay: 0.2 });
        this.to({ pulseSig: 1 }, { duration: 1.05, ease: 'glide', delay: 0.3, onComplete: () => this.phase === 'verify' && this.startWait() });
        this.say('The signal is travelling to the verification boundary and waits there for the server.');
        break;
      }
      case 'rejected':
        this.setPhase('refused');
        this.stopWait();
        // Stops short, backs off, dims. Nothing shakes and nothing restarts.
        this.to({ pulseSig: 0.8 }, { duration: 0.6, ease: 'power2.out' });
        this.to({ pulse: 0.35 }, { duration: 1.0, ease: 'power1.out', delay: 0.15 });
        this.to({ gateErr: 1 }, { duration: 0.45, delay: 0.1 });
        this.to({ block: 1 }, { duration: 0.4, ease: 'power2.out', delay: 0.3 });
        this.to({ lift: 0 }, { duration: 0.85, ease: 'boardSettle', delay: 0.2 });
        this.say('The signal stopped at the boundary: the server refused the sign-in.');
        break;
      case 'unavailable':
      case 'timeout':
        this.setPhase('nosignal');
        this.stopWait();
        // No answer is not a refusal: the pulse holds near the boundary in a neutral amber.
        this.to({ pulseSig: 0.93, pulse: 0.55 }, { duration: 0.5, ease: 'power2.out' });
        this.to({ gateWarn: 1 }, { duration: 0.45 });
        this.to({ lift: 0.35 }, { duration: 0.7, ease: 'boardSettle' });
        this.say('No answer from the sign-in service. The signal is holding before the boundary.');
        break;
      case 'accepted':
        this.setPhase('passed');
        this.stopWait();
        this.to({ gateOk: 1, pulse: 1, pulseSig: 1 }, { duration: 0.35 });
        this.say('The server accepted the sign-in.');
        break;
      case 'biometric':
        this.enterChoose(true);
        break;
      default:
        break;
    }
    this.plan();
    this.aim();
  }

  /** Password accepted for an approved supplier: the identity modules emerge beyond the boundary. */
  enterChoose(fresh) {
    this.setPhase('choose');
    this.stopWait();
    this.stopProc();
    const rig = this.scene.rig;
    if (fresh && rig.pulseBranch === 0) rig.pulseSig = Math.max(rig.pulseSig, 0.6);
    this.to({ depth: 1, pulse: 1, pulseSig: 1, gateErr: 0, gateWarn: 0, block: 0 }, { duration: 0.6 });
    this.to({ gateOk: 1 }, { duration: 0.4 });
    this.to({ lift: 0.25 }, { duration: 0.8, ease: 'boardSettle' });
    this.to({ branches: 1 }, { duration: 0.9, ease: 'power1.inOut', delay: 0.2 });
    this.to({ pulseBranch: 1 }, { duration: 1.0, ease: 'glide', delay: 0.45, onComplete: () => this.phase === 'choose' && this.startWait() });
    this.to({ optic: 1 }, { duration: 1.0, ease: 'power2.out', delay: 0.25 });
    this.to({ bezel: 1 }, { duration: 0.95, ease: 'boardSettle', delay: 0.85 });
    this.to({ touch: 1 }, { duration: 1.0, ease: 'power2.out', delay: 0.45 });
    this.to({ fp: 1 }, { duration: 1.6, ease: 'power1.inOut', delay: 1.1 });
    this.to({ iris: 0, irisWarn: 0, ring: 0, ringOk: 0, proc: 0, hover: 0 }, { duration: 0.5 });
    this.say('Password accepted. Two identity modules: Optical Identity, and Touch Identity, which is not available yet.');
    this.aim();
  }

  /** Back to step 1 (another account, the step expired, or the server ended it). */
  backToCredentials() {
    this.stopWait();
    this.stopProc();
    this.confirmedAt = 0;
    this.to({ optic: 0, bezel: 0, touch: 0, fp: 0, branches: 0, pulseBranch: 0, iris: 0, ring: 0, ringOk: 0, proc: 0, irisWarn: 0, hover: 0 }, { duration: 0.7, ease: 'power2.in' });
    this.to({ depth: 0, lift: 0, pulse: 0, pulseSig: 0, gateOk: 0, gateErr: 0, gateWarn: 0, block: 0 }, { duration: 0.9, delay: 0.35 });
    this.setPhase('idle');
    this.status = '';
    this.plan();
    this.aim();
    if (this.introDone) this.startHalo();
  }

  step(detail) {
    if (!detail) return;
    if (detail.step === 'credentials' && this.phase !== 'idle' && this.phase !== 'finale') this.backToCredentials();
    else if (detail.step === 'biometric' && this.phase !== 'choose' && this.phase !== 'optics') {
      this.completeIntro();
      this.enterChoose(false);
    }
  }

  hoverFace(on) {
    if (this.phase !== 'choose') return;
    this.to({ hover: on ? 1 : 0 }, { duration: on ? 0.45 : 0.35, ease: on ? 'power2.out' : 'power2.inOut' });
  }

  /** Face step states from biometric.js; every one of them is something that really happened. */
  face(detail) {
    const state = detail?.state;
    if (!state || this.phase === 'finale') return;
    if (this.shell) this.shell.dataset.faceState = state;
    if (detail.method === 'passkey') return;
    switch (state) {
      case 'requesting':
        this.stopWait();
        this.stopProc();
        this.setPhase('optics');
        this.to({ hover: 0, irisWarn: 0, ring: 0, ringOk: 0, proc: 0 }, { duration: 0.3 });
        this.to({ iris: 0, pulse: 0 }, { duration: 0.3 });
        this.say('The optical module turns toward you. Your camera opens once the browser allows it.');
        break;
      case 'camera':
        // The stream is live: the blades open in one coordinated turn and reveal it.
        this.to({ iris: 1 }, { duration: 0.95, ease: 'power2.inOut' });
        this.say('The aperture is open on your live camera.');
        break;
      case 'capturing':
        this.say('Capturing. The scene stays still while you hold your position.');
        break;
      case 'processing':
        this.to({ proc: 1 }, { duration: 0.3 });
        this.startProc();
        this.say('The server is comparing the capture with your enrolled face.');
        break;
      case 'confirmed':
        this.stopProc();
        this.confirmedAt = performance.now();
        this.to({ proc: 0 }, { duration: 0.25 });
        this.to({ ring: 1 }, { duration: 0.7, ease: 'power2.out' });
        this.to({ ringOk: 1 }, { duration: 0.45, delay: 0.55 });
        this.to({ iris: 0 }, { duration: 0.8, ease: 'power2.inOut', delay: 0.6 });
        this.say('Identity verified by the server.');
        break;
      case 'failed': {
        this.stopProc();
        const camera = CAMERA_PROBLEMS.includes(detail.code);
        // Stays in front of the person: the aperture is ready for another try, never "verified".
        this.to({ proc: 0, ring: 0 }, { duration: 0.3 });
        this.to({ iris: camera ? 0.1 : 0.32 }, { duration: 0.6, ease: 'power2.inOut' });
        this.to({ irisWarn: 1 }, { duration: 0.45 });
        this.say(camera ? 'The camera is not available, so the aperture stays closed.' : 'The face check did not pass. The aperture is ready for another try.');
        break;
      }
      case 'cancelled':
        this.stopProc();
        this.to({ iris: 0, proc: 0, irisWarn: 0, ring: 0 }, { duration: 0.55 });
        if (this.phase === 'optics') this.enterChoose(false);
        break;
      case 'restart':
        this.stopProc();
        break;
      default:
        break;
    }
    this.aim();
  }

  placeCamera() {
    const el = this.cameraEl;
    if (!el || this.phase !== 'optics') return;
    this.stage.rig.apply();
    const a = this.scene.hw.apertureOnScreen(this.stage.rig.camera, this.stage.width, this.stage.height);
    const size = Math.ceil(a.cover * 2 + 4);
    el.style.setProperty('--cx', `${a.cx.toFixed(1)}px`);
    el.style.setProperty('--cy', `${a.cy.toFixed(1)}px`);
    el.style.setProperty('--size', `${size}px`);
    // A hair larger than the inscribed circle, so no sliver of the blade edge shows inside it.
    el.style.setProperty('--r', `${Math.max(0, a.r * 1.02).toFixed(1)}px`);
  }

  /**
   * L10. The session already exists (the server said so); this only plays the short closing move
   * and then goes to the redirect the server chose. Returns false when nothing will be played
   * (reduced motion), so the caller navigates at once. A timer guarantees the navigation.
   */
  finale(redirect) {
    if (this.reduced || this.navigated) return false;
    const fromOptic = this.phase === 'optics' || this.phase === 'choose';
    this.setPhase('finale');
    if (this.stageEl) this.stageEl.dataset.optics = '0';
    this.stopWait();
    this.stopProc();
    const go = () => {
      if (this.navigated) return;
      this.navigated = true;
      window.location.href = redirect;
    };
    window.setTimeout(go, 3200);

    const wait = this.confirmedAt ? Math.max(0, 0.95 - (performance.now() - this.confirmedAt) / 1000) : 0;
    const rig = this.scene.rig;
    const hw = this.scene.hw;
    const fiber = hw.buildFiber(hw.finaleOrigin(fromOptic));
    rig.cable = 0;
    rig.finale = 0;
    this.to({ gateOk: 1, pulse: 1, iris: 0 }, { duration: 0.3 });
    this.to({ cable: 1 }, { duration: 0.4, delay: wait, ease: 'power1.out' });

    const state = this.stage.rig.state;
    gsap.killTweensOf(state);
    const from = { ...state };
    const lerp = (a, b, t) => a + (b - a) * t;
    gsap.to(rig, {
      finale: 1,
      duration: 1.25,
      delay: wait + 0.2,
      ease: 'power2.in',
      onUpdate: () => {
        const t = rig.finale;
        const e = t * t * (3 - 2 * t);
        const q = fiber.curve.getPointAt(Math.max(0.001, t * 0.85));
        // The camera follows the pulse along the fiber, lowering toward it as it accelerates.
        Object.assign(state, {
          tx: lerp(from.tx, q.x, e), ty: lerp(from.ty, 0.2, e), tz: lerp(from.tz, q.z, e),
          w: lerp(from.w, 11, e), h: lerp(from.h, 9, e),
          tilt: lerp(from.tilt, 50, e), yaw: lerp(from.yaw, -24, e), fov: lerp(from.fov, 32, e)
        });
        if (t > 0.78 && this.shell && this.shell.dataset.finale !== '1') this.shell.dataset.finale = '1';
        this.scene.apply();
      },
      onComplete: () => window.setTimeout(go, 260)
    });
    this.say('Signed in. The signal is on its way to your workspace.');
    return true;
  }

  destroy() {
    this.stopHalo();
    this.stopWait();
    this.stopProc();
    for (const s of Object.values(this.segs)) s.tl.kill();
    gsap.killTweensOf(this.stage.rig.state);
    gsap.killTweensOf(this.scene.rig);
  }
}

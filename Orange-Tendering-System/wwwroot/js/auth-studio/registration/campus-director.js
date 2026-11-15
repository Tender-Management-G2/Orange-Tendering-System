import gsap from 'gsap';
import { buildCampus, CAMPUS_RIG, SPOT } from './campus.js';
import { BOX } from './layout.js';

// Director for SUPPLIER GENESIS R03–R08 (the campus). It reads the same sanitised form state as the
// R00–R02 director (supplier-registration.js publishes it) and never anything else:
//
//   step       0 contact · 1 company · 2 documents · 3 review
//   company    the legal name, written on the HQ sign locally (it is what the person just typed)
//   category   one of Models/Entities/SupplierCategories.cs
//   documents  { registration, tax, legal }: 'none' | 'accepted' (passed the local checks) | 'rejected'
//   submit     'idle' | 'sending' | 'accepted' (the server redirected to email verification) | 'failed'
//
// It never claims more than the form knows: an accepted document is "packed", not "verified"; the
// application rides to dispatch while the server works and is only labelled after its redirect.

const CAMPUS_FRAMES = {
  unfold: { tx: BOX.cx, ty: 0, tz: BOX.cz - 1, w: 30, h: 26, tilt: 34, yaw: -12, fov: 18 },
  campus: { tx: BOX.cx - 1.6, ty: 0, tz: BOX.cz - 1.2, w: 40, h: 33, tilt: 46, yaw: -24, fov: 28 },
  hq: { tx: SPOT.hq.x + 0.6, ty: 2.0, tz: SPOT.hq.z + 1.0, w: 15, h: 12, tilt: 36, yaw: -24, fov: 30 },
  category: { tx: SPOT.category.x, ty: 1.0, tz: SPOT.category.z + 0.6, w: 15, h: 12.5, tilt: 40, yaw: -20, fov: 30 },
  packing: { tx: SPOT.packing.x + 1.4, ty: 0.8, tz: SPOT.packing.z - 2.0, w: 16, h: 13, tilt: 44, yaw: -18, fov: 30 },
  identity: { tx: SPOT.identity.x - 1.0, ty: 0.6, tz: SPOT.identity.z + 1.8, w: 16, h: 13, tilt: 40, yaw: -28, fov: 30 },
  dispatch: { tx: SPOT.dispatch.x + 1.2, ty: 0.4, tz: SPOT.dispatch.z - 1.6, w: 18, h: 14, tilt: 44, yaw: -22, fov: 30 }
};
const COMPACT_SCALE = { w: 1.05, h: 0.62 };

export class CampusDirector {
  constructor({ genesis, scene, stage, onStatus = () => {} }) {
    this.genesis = genesis;
    this.scene = scene;
    this.stage = stage;
    this.onStatus = onStatus;
    this.rig = { ...CAMPUS_RIG };
    this.campus = buildCampus(stage, scene);
    this.on = false;
    this.waiting = null;
    this.input = null;
    this.hqStarted = false;
    this.category = '';
    this.docStates = {};
    this.submit = 'idle';
    this.rideStarted = false;
    this.holdUntil = 0;
    this.holdShot = null;
    this.reduced = genesis.reduced;
    this.apply = () => this.stage.invalidate();
    stage.updaters.add(() => this.campus.apply(this.rig));
    // Restrained operational motion for the active category, at most ~30 frames a second.
    let last = 0;
    this._tick = time => {
      if (!this.on || this.reduced || document.hidden) return;
      const m = this.category && this.campus.modules.get(this.category);
      if (!m?.tick || m.k < 0.99 || time - last < 0.033) return;
      last = time;
      if (m.tick(time)) this.stage.invalidate();
    };
    gsap.ticker.add(this._tick);
    // The R00–R02 director asks us for the shot while the campus is on screen.
    genesis.shotOverride = () => (this.on ? this.shot() : null);
    genesis.poseOf = name => this.pose(name);
  }

  pose(name) {
    const f = CAMPUS_FRAMES[name];
    if (!f) return null;
    const compact = this.stage.width < 560 || this.stage.width / this.stage.height > 1.25;
    return compact ? { ...f, w: f.w * COMPACT_SCALE.w, h: f.h * COMPACT_SCALE.h } : f;
  }

  to(props, { duration = 0.8, ease = 'power2.inOut', delay = 0, onComplete } = {}) {
    if (this.reduced) {
      Object.assign(this.rig, props);
      this.stage.invalidate();
      onComplete?.();
      return;
    }
    gsap.to(this.rig, { ...props, duration, ease, delay, overwrite: 'auto', onUpdate: this.apply, onComplete });
  }

  setReduced(on) {
    this.reduced = on;
    if (on) {
      gsap.killTweensOf(this.rig);
      this.plan(true);
    }
  }

  hold(shot, seconds) {
    this.holdShot = shot;
    this.holdUntil = performance.now() + seconds * 1000;
    this.genesis.aim();
    if (!this.reduced) gsap.delayedCall(seconds + 0.05, () => this.genesis.aim());
  }

  update(input) {
    this.input = input;
    if (!this.on) {
      if (input.step >= 1) this.requestStart();
      return;
    }
    this.plan();
  }

  /** Begin once the identity package is sealed (or promptly, if the person is moving fast). */
  requestStart() {
    if (this.on || this.waiting) return;
    const sealed = () => this.genesis.segs.seal.at('end');
    if (sealed() || this.reduced) {
      this.start();
      return;
    }
    let tries = 0;
    const check = () => {
      if (sealed() || ++tries > 10) {
        this.waiting = null;
        this.start();
      } else this.waiting = gsap.delayedCall(0.25, check);
    };
    this.waiting = gsap.delayedCall(0.25, check);
  }

  start() {
    if (this.on) return;
    this.on = true;
    this.genesis.stopHalo?.();
    this.stage.setLightTarget(BOX.cx + 2, 0, BOX.cz - 2);
    // R03: the package becomes the ground of a business.
    this.to({ letterAway: 1 }, { duration: 0.8, ease: 'power2.in' });
    this.to({ platform: 1 }, { duration: 0.9, ease: 'power2.out', delay: 0.1 });
    this.to({ shrink: 1 }, { duration: 1.2, ease: 'glide', delay: 0.35 });
    this.to({ unfoldEdges: 1 }, { duration: 1.5, ease: 'none', delay: 0.7 });
    this.to({ unfoldCorners: 1 }, { duration: 1.2, ease: 'none', delay: 1.7 });
    this.to({ blueprint: 1 }, { duration: 1.3, ease: 'power1.inOut', delay: 2.3 });
    this.to({ conveyor: 1 }, { duration: 1.5, ease: 'power1.inOut', delay: 2.7 });
    this.to({ stations: 1 }, { duration: 1.0, ease: 'power2.out', delay: 3.0 });
    this.to({ fp: 1 }, { duration: 1.6, ease: 'power1.inOut', delay: 3.8 });
    this.to({ appBox: 1 }, { duration: 0.8, ease: 'power2.out', delay: 3.5 });
    this.onStatus('The sealed package unfolds into the ground of your enterprise.');
    this.hold('unfold', this.reduced ? 0 : 2.0);
    this.plan();
  }

  plan(instant = false) {
    const i = this.input;
    if (!i || !this.on) return;
    const c = this.campus;

    // HQ: rises once a legal name exists; the sign follows the field.
    c.setCompany(i.company || '');
    if ((i.company || '').trim() && !this.hqStarted) {
      this.hqStarted = true;
      this.to({ hq: 1 }, { duration: 1.8, ease: 'none', delay: instant ? 0 : 0.2 });
      this.to({ roof: 1 }, { duration: 0.9, ease: 'power2.out', delay: 1.5 });
      this.to({ sign: 1 }, { duration: 0.7, ease: 'power2.out', delay: 2.0 });
      this.onStatus('Your headquarters rises on the platform.');
      if (i.focus === 'company') this.hold('hq', 2.6);
    }

    // Category: the previous module retracts into its tile before the next rises out of it.
    const category = i.category || '';
    if (category !== this.category) {
      const previous = this.category && c.modules.get(this.category);
      this.category = category;
      const next = category ? c.moduleFor(category) : null;
      if (previous) {
        gsap.killTweensOf(previous);
        if (this.reduced) {
          previous.k = 0;
          c.applyModule(previous);
        } else gsap.to(previous, { k: 0, duration: 0.7, ease: 'power2.in', onUpdate: () => { c.applyModule(previous); this.stage.invalidate(); } });
      }
      if (next) {
        gsap.killTweensOf(next);
        if (this.reduced) {
          next.k = 1;
          c.applyModule(next);
        } else gsap.to(next, { k: 1, duration: 1.5, ease: 'power2.out', delay: previous ? 0.55 : 0.1, onUpdate: () => { c.applyModule(next); this.stage.invalidate(); } });
        this.onStatus(`The ${category} module takes shape beside your headquarters.`);
        this.hold('category', 2.8);
      }
      this.stage.invalidate();
    }

    // Documents (R05): accepted papers travel into the application box; refused ones stop short.
    const docs = i.documents || {};
    for (const d of c.docs) {
      const state = docs[d.key] || 'none';
      if (state === d.state) continue;
      d.state = state;
      gsap.killTweensOf(d);
      const dur = s => (this.reduced ? 0 : s);
      if (state === 'accepted') {
        gsap.to(d, { t: 1, warn: 0, duration: dur(2.1), ease: 'power1.inOut', onUpdate: this.apply });
        this.onStatus('A document has passed the local checks and is packed. An administrator still reviews it.');
      } else if (state === 'rejected') {
        gsap.to(d, { t: 0.5, duration: dur(1.3), ease: 'power2.out', onUpdate: this.apply });
        gsap.to(d, { warn: 1, duration: dur(0.4), delay: dur(1.0), onUpdate: this.apply });
        this.onStatus('A document was refused before packing. Check the message beside the file field.');
      } else {
        gsap.to(d, { t: 0, warn: 0, duration: dur(0.6), ease: 'power2.in', onUpdate: this.apply });
      }
      if (i.step === 2) this.hold('packing', 2.2);
      if (this.reduced) {
        d.t = state === 'accepted' ? 1 : state === 'rejected' ? 0.5 : 0;
        d.warn = state === 'rejected' ? 1 : 0;
      }
    }

    // Review (R06 prelude): the identity parcel rides the belt to the Identity Station and waits there.
    if (i.step >= 3 && !this.rideStarted) {
      this.rideStarted = true;
      this.to({ ride: 1 }, { duration: 2.6, ease: 'none', onComplete: () => this.to({ irisReady: 1 }, { duration: 0.8 }) });
      this.onStatus('Your identity package travels to the Identity Station. Face enrollment follows email verification.');
      this.hold('identity', 3.0);
    }

    // Submission (R08): never ahead of the server.
    if (i.submit !== this.submit) {
      this.submit = i.submit;
      if (i.submit === 'sending') {
        this.to({ appWarn: 0, appLabel: 0 }, { duration: 0.3 });
        this.to({ appLid: 1 }, { duration: 0.55, ease: 'boardSettle' });
        this.to({ appRide: 1 }, { duration: 3.2, ease: 'none', delay: 0.45 });
        this.onStatus('The application is on its way to dispatch while the server checks it.');
      } else if (i.submit === 'failed') {
        this.to({ appRide: 0 }, { duration: 1.1, ease: 'power2.inOut' });
        this.to({ appLid: 0.45 }, { duration: 0.6, delay: 0.9, ease: 'boardSettle' });
        this.to({ appWarn: 1 }, { duration: 0.4, delay: 1.0 });
        this.onStatus('The application came back to the packing station: the server did not accept it yet.');
      } else if (i.submit === 'accepted') {
        this.to({ appLabel: 1 }, { duration: 0.35, ease: 'power2.out' });
        this.onStatus('Application received. Next: verify your email.');
      }
    }
    this.genesis.aim();
  }

  shot() {
    const i = this.input || {};
    if (performance.now() < this.holdUntil && this.holdShot) return this.holdShot;
    if (i.submit === 'sending' || i.submit === 'accepted') return 'dispatch';
    if (i.submit === 'failed') return 'packing';
    if (i.step === 1) {
      if (i.focus === 'category' && this.category) return 'category';
      if (this.hqStarted && i.focus === 'company') return 'hq';
      return 'campus';
    }
    if (i.step === 2) return 'packing';
    return 'campus';
  }

  destroy() {
    gsap.ticker.remove(this._tick);
    gsap.killTweensOf(this.rig);
  }
}

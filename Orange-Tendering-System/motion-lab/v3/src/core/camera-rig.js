import * as THREE from 'three';

// Directed camera. A shot is authored as a *frame* on the drawing plane (what must be visible)
// plus the viewing angles, never as a raw position, so the same shot holds at any aspect ratio.
//
//   tx, tz, ty : look-at target
//   w, h       : world rectangle that must fit inside the viewport
//   tilt       : degrees from straight-down (0 = flat illustration view, 90 = horizon)
//   yaw        : degrees around the vertical axis
//   fov        : vertical field of view. ~3° is optically flat (the "2D" look); the 2D→3D handoff
//                is a dolly-zoom on this one camera, so there is never a second renderer to align.
//
// Debug orbit is a separate mode that suspends the rig; Reset returns to the authored pose.

const DEG = Math.PI / 180;
const _q1 = new THREE.Quaternion();
const _q2 = new THREE.Quaternion();
const _fwd = new THREE.Vector3();
const X = new THREE.Vector3(1, 0, 0);
const Y = new THREE.Vector3(0, 1, 0);

export class CameraRig {
  constructor() {
    this.camera = new THREE.PerspectiveCamera(3, 1, 1, 2000);
    this.state = { tx: 0, ty: 0, tz: 0, w: 28, h: 28, tilt: 0, yaw: 0, fov: 3 };
    this.aspect = 1;
    this.padding = 1.08;
    this.suspended = false;
    this.pxPerUnit = 1;
    this.viewHeight = 1;
  }

  setViewport(width, height) {
    this.aspect = width / Math.max(1, height);
    this.viewHeight = height;
    this.camera.aspect = this.aspect;
    this.apply();
  }

  apply() {
    if (this.suspended) return;
    const s = this.state;
    const cam = this.camera;
    // Contain the frame: fit height, or width when the viewport is narrower than the frame.
    const halfH = Math.max(s.h / 2, s.w / 2 / this.aspect) * this.padding;
    const fov = Math.max(1.5, s.fov);
    const dist = halfH / Math.tan((fov * DEG) / 2);
    _q1.setFromAxisAngle(Y, s.yaw * DEG);
    _q2.setFromAxisAngle(X, -(Math.PI / 2 - s.tilt * DEG));
    cam.quaternion.copy(_q1).multiply(_q2);
    _fwd.set(0, 0, -1).applyQuaternion(cam.quaternion);
    cam.position.set(s.tx - _fwd.x * dist, s.ty - _fwd.y * dist, s.tz - _fwd.z * dist);
    cam.fov = fov;
    cam.near = Math.max(0.5, dist - 90);
    cam.far = dist + 160;
    cam.updateProjectionMatrix();
    cam.updateMatrixWorld();
    this.pxPerUnit = this.viewHeight / (2 * halfH);
  }

  /** Plain copy of the pose for evidence/QA. */
  snapshot() {
    const p = this.camera.position;
    return { ...this.state, position: [p.x, p.y, p.z].map(v => +v.toFixed(3)), pxPerUnit: +this.pxPerUnit.toFixed(2) };
  }
}

/** A named pose with the fields the rig tweens. Missing fields inherit from `base`. */
export function pose(base, overrides = {}) {
  return { tx: 0, ty: 0, tz: 0, w: 28, h: 28, tilt: 0, yaw: 0, fov: 3, ...base, ...overrides };
}

import { bootStudio } from './boot.js';
import { createSignalScene } from './login/scene.js';
import { buildSignalChoreography } from './login/choreography.js';
import { SignalDirector } from './login/director.js';
import { fallbackSvg } from './login/fallback.js';

// ORANGE SIGNAL on the real sign-in page (Areas/Identity/Pages/Account/Login.cshtml).
// The form scripts own everything that matters: supplier-login.js (validation, the credential
// request, the biometric step) and biometric.js (camera, face capture, the server's verdict).
// They publish sanitised state and real outcomes as events on #authStudio; this module only draws
// them. A valid e-mail format completes a drawing, nothing more.
//
//   authstudio:state      { email, hasPassword, focus }               form state (L00–L02)
//   authstudio:signal     { phase: submitted|rejected|unavailable|timeout|accepted|biometric|incomplete }
//   authstudio:step       { step: credentials|biometric, face, passkey }
//   authstudio:face       { state: requesting|camera|capturing|processing|confirmed|failed|cancelled|restart, code }
//   authstudio:signed-in  { redirect }  cancelable: cancelled here to play the short finale, which
//                                       then goes to exactly that server-chosen redirect.

(async () => {
  const shell = document.getElementById('authStudio');
  const stageEl = document.getElementById('asStage');
  if (!shell || !stageEl) return;

  const studio = await bootStudio({
    stageEl,
    fallback: fallbackSvg,
    captionShots: ['wide', 'traces'],
    mount(stage, { onStatus }) {
      const scene = createSignalScene(stage);
      const choreography = buildSignalChoreography(scene, stage);
      const director = new SignalDirector({
        scene, choreography, stage, onStatus, stageEl, shell,
        cameraEl: document.getElementById('facePanel')
      });
      return { scene, choreography, director };
    }
  });
  if (studio.fallback) return;

  const { director } = studio;
  const push = state => {
    director.update({ email: state.email, hasPassword: state.hasPassword, focus: state.focus });
  };

  studio.start();
  shell.addEventListener('authstudio:state', event => push(event.detail));
  shell.addEventListener('authstudio:signal', event => director.signal(event.detail?.phase));
  shell.addEventListener('authstudio:step', event => director.step(event.detail));
  shell.addEventListener('authstudio:face', event => director.face(event.detail));
  shell.addEventListener('authstudio:account', () => director.accountType());
  shell.addEventListener('authstudio:signed-in', event => {
    if (director.finale(event.detail.redirect)) event.preventDefault();
  });

  const faceButton = document.getElementById('btnFaceLogin');
  if (faceButton) {
    for (const [name, on] of [['pointerenter', true], ['pointerleave', false], ['focus', true], ['blur', false]]) {
      faceButton.addEventListener(name, () => director.hoverFace(on && !faceButton.disabled));
    }
  }

  // The form script ran first: pick up values restored by the browser and a step the server rendered.
  if (shell.authState) push(shell.authState);
  if (shell.dataset.step === 'biometric') {
    director.step({ step: 'biometric', passkey: document.getElementById('bio-root')?.dataset.passkey === 'true' });
  }
})();

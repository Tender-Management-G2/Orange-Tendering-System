import { bootStudio } from './boot.js';
import { createSignalScene } from './login/scene.js';
import { buildSignalChoreography } from './login/choreography.js';
import { SignalDirector } from './login/director.js';
import { fallbackSvg } from './login/fallback.js';

// ORANGE SIGNAL on the real sign-in page (Areas/Identity/Pages/Account/Login.cshtml).
// The form script (wwwroot/js/supplier-login.js) owns validation, the credential request and the
// biometric step, and publishes a sanitised state; this module only draws it. A valid e-mail
// format completes a drawing, nothing more: results come from the server through that state.

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
      const director = new SignalDirector({ scene, choreography, stage, onStatus });
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
  if (shell.authState) push(shell.authState);
})();

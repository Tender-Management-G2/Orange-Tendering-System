import { bootStudio } from './boot.js';
import { createGenesisScene } from './registration/scene.js';
import { buildGenesisChoreography } from './registration/choreography.js';
import { GenesisDirector } from './registration/director.js';
import { fallbackSvg } from './registration/fallback.js';

// SUPPLIER GENESIS on the real registration page (Areas/Identity/Pages/Account/Register.cshtml).
// The form script (wwwroot/js/supplier-registration.js) owns validation and submission and
// publishes a sanitised readiness state; this module only turns that state into the illustration.
// It never reads the password, and the e-mail address never leaves the form script.

(async () => {
  const shell = document.getElementById('authStudio');
  const stageEl = document.getElementById('asStage');
  if (!shell || !stageEl) return;

  const studio = await bootStudio({
    stageEl,
    fallback: fallbackSvg,
    captionShots: ['wide', 'letter'],
    mount(stage, { onStatus }) {
      const scene = createGenesisScene(stage);
      const choreography = buildGenesisChoreography(scene, stage);
      const director = new GenesisDirector({ scene, choreography, stage, onStatus });
      return { scene, choreography, director };
    }
  });
  if (studio.fallback) return;

  const { director } = studio;
  const push = state => {
    director.update({
      name: state.name,
      email: state.email,
      hasPassword: state.hasPassword,
      rules: state.rules,
      confirm: state.confirm,
      focus: state.focus
    });
  };

  studio.start();
  shell.addEventListener('authstudio:state', event => push(event.detail));
  // The form script ran first: pick up values restored by the browser or returned by the server.
  if (shell.authState) push(shell.authState);
})();

import { bootStudio } from './boot.js';
import { createGenesisScene } from './registration/scene.js';
import { buildGenesisChoreography } from './registration/choreography.js';
import { GenesisDirector } from './registration/director.js';
import { CampusDirector } from './registration/campus-director.js';
import { fallbackSvg } from './registration/fallback.js';

// SUPPLIER GENESIS on the real registration page (Areas/Identity/Pages/Account/Register.cshtml).
// The form script (wwwroot/js/supplier-registration.js) owns validation and submission and
// publishes a sanitised readiness state; this module only turns that state into the illustration.
// It never reads the password, and the e-mail address never leaves the form script.
//   R00–R02  letter, identity card, sealed package      → registration/director.js
//   R03–R08  platform, HQ, category, documents, submit  → registration/campus-director.js

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
      const campus = new CampusDirector({ genesis: director, scene, stage, onStatus });
      return { scene, choreography, director, campus };
    }
  });
  if (studio.fallback) return;

  const { director, campus } = studio;
  campus.setReduced(director.reduced);
  window.matchMedia('(prefers-reduced-motion: reduce)').addEventListener('change', event => campus.setReduced(event.matches));

  const push = state => {
    director.update({
      name: state.name,
      email: state.email,
      hasPassword: state.hasPassword,
      rules: state.rules,
      confirm: state.confirm,
      focus: state.focus
    });
    campus.update({
      step: state.step ?? 0,
      focus: state.focus,
      company: state.company || '',
      category: state.category || '',
      documents: state.documents || {},
      submit: state.submit || 'idle'
    });
  };

  studio.start();
  shell.addEventListener('authstudio:state', event => push(event.detail));
  // The form script ran first: pick up values restored by the browser or returned by the server.
  if (shell.authState) push(shell.authState);
})();

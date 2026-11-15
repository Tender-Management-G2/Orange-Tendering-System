import { createGenesisScene } from './scene.js';
import { buildGenesisChoreography } from './choreography.js';
import { GenesisDirector } from './director.js';
import { bindRegistrationForm } from '../ui/forms.js';

// Chapter 1 — SUPPLIER GENESIS (R00–R02 in gate G2A).
export function mount(stage, { onStatus }) {
  const scene = createGenesisScene(stage);
  const choreography = buildGenesisChoreography(scene, stage);
  const director = new GenesisDirector({ scene, choreography, stage, onStatus });

  return {
    id: 'registration',
    scene,
    choreography,
    director,
    bind: form => bindRegistrationForm(form, input => director.update(input)),
    /** Which caption state the showreel is in at time t (the live director names its own shots). */
    /** Matched moments for the side-by-side page (seconds on this chapter's showreel). */
    moments: reel => ({
      blank: 0.2,
      line: 2.2,
      drawn: 3.9,
      developed: 5.25,
      detail: reel.tl.labels.R02 - 0.1,
      final: reel.duration
    }),
    reelShot: (reel, t) => (t < reel.tl.labels.R02 ? (t < reel.tl.labels.R01 ? 'wide' : 'letter') : 'carton'),
    // Synthetic values only — never real people, companies or credentials.
    presets: [
      { label: 'Name', values: { name: 'Layla Haddad' } },
      { label: 'Valid email', values: { email: 'layla@northwind.example' } },
      { label: 'Invalid email', values: { email: 'layla@northwind' } },
      { label: 'Weak password', values: { password: 'paper', confirm: '' } },
      { label: 'Strong password', values: { password: 'Paper-Signal-25' } },
      { label: 'Mismatch', values: { confirm: 'Paper-Signal-52' } },
      { label: 'Match', values: { confirm: 'Paper-Signal-25' } }
    ]
  };
}

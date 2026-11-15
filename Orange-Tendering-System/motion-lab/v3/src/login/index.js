import { createSignalScene } from './scene.js';
import { buildSignalChoreography } from './choreography.js';
import { SignalDirector } from './director.js';
import { bindLoginForm } from '../ui/forms.js';

// Chapter 2 — ORANGE SIGNAL (L00–L02 flat stage in gate G2A).
export function mount(stage, { onStatus }) {
  const scene = createSignalScene(stage);
  const choreography = buildSignalChoreography(scene, stage);
  const director = new SignalDirector({ scene, choreography, stage, onStatus });

  return {
    id: 'login',
    scene,
    choreography,
    director,
    bind: form => bindLoginForm(form, input => director.update(input)),
    moments: reel => ({
      blank: 0.2,
      line: 2.0,
      drawn: 3.3,
      developed: 4.8,
      detail: reel.tl.labels.L02 - 0.1,
      final: reel.duration
    }),
    reelShot: (reel, t) => (t < reel.tl.labels.L01 ? 'wide' : t < reel.tl.labels.L02 ? 'traces' : 'core'),
    // Synthetic values only.
    presets: [
      { label: 'Typing email', values: { email: 'layla' } },
      { label: 'Invalid email', values: { email: 'layla@northwind' } },
      { label: 'Valid email', values: { email: 'layla@northwind.example' } },
      { label: 'Password', values: { password: 'Paper-Signal-25' } },
      { label: 'Clear password', values: { password: '' } }
    ]
  };
}

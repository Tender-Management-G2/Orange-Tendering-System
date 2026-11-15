// Shared design tokens for both chapters (Supplier Genesis / Orange Signal).
// Source of truth for colours is design/v3/tokens.json; values here are the lab's working copy.
export const COLOR = {
  orange: '#FF7900',
  // Thin strokes on ivory need a slightly deeper orange than the brand fill to hold contrast.
  orangeInk: '#F26A00',
  ivory: '#F8F4EE',
  paper: '#FFFDF9',
  beige: '#E7DCD0',
  warmGray: '#8D8278',
  graphite: '#292521',
  champagne: '#C5A477',
  success: '#438A62',
  warning: '#C78E3A',
  error: '#B84638',

  // Scene materials derived from the palette.
  ground: '#F6F1E9',
  envelope: '#FFFDF9',
  card: '#FFFFFF',
  liner: '#FF7900',
  kraftInner: '#EADFD1',
  kraftOuter: '#DDCDB6',
  kraftEdge: '#CDB999',
  ink: '#8D8278',
  score: '#B8A68F',
  ceramic: '#FDFBF7',
  ceramicEdge: '#E9E1D6',
  substrate: '#EFE7DB'
};

// World-space stroke weights (the line is ink in the world, so it scales with the camera).
export const LINE = {
  main: 0.088,
  detail: 0.05,
  hair: 0.034,
  dot: 0.2
};

// Motion tokens. Durations are seconds; eases are GSAP ease names registered in core/eases.js.
export const MOTION = {
  small: 0.22,
  fold: 1.15,
  handoff: 1.35,
  hero: 0.9
};

export const LAYOUT = {
  sceneFraction: 0.56,
  breakpoint: 900
};

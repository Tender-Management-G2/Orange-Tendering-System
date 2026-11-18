import { buildMainLine, padDividers } from './layout.js';
import { COLOR } from '../core/tokens.js';

// Static artwork for browsers without WebGL and for the editable-SVG export: the finished L00
// drawing, generated from the same paths as the live scene.
export function fallbackSvg({ title = 'The outline of a SIM card drawn with one orange line' } = {}) {
  const main = buildMainLine();
  const dividers = padDividers();
  const end = main.pointAt(main.total);
  const stroke = 'fill="none" stroke-linecap="round" stroke-linejoin="round"';
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="-14.4 -14.2 27.0 27.0" role="img" aria-label="${title}">
  <title>${title}</title>
  <g id="contact-dividers" ${stroke} stroke="${COLOR.ink}" stroke-width="0.04">
    ${dividers.map((d, i) => `<path id="divider-${i}" d="${d.svg}"/>`).join('\n    ')}
  </g>
  <path id="orange-line" d="${main.svg}" ${stroke} stroke="${COLOR.orangeInk}" stroke-width="0.088"/>
  <circle id="orange-point" cx="${end[0].toFixed(3)}" cy="${end[1].toFixed(3)}" r="0.2" fill="${COLOR.orange}"/>
</svg>`;
}

import { buildMainLine, letterSeams, cartonCreases } from './layout.js';
import { COLOR } from '../core/tokens.js';

// Static artwork for browsers without WebGL and for the editable-SVG export. It is generated from
// the same paths as the live scene, so it is the finished R00 drawing rather than a separate asset.
export function fallbackSvg({ title = 'A letter and an unfolded carton drawn with one orange line' } = {}) {
  const main = buildMainLine();
  const seams = letterSeams();
  const creases = cartonCreases();
  const view = '-15.2 -18.4 30.2 31.4';
  const stroke = 'fill="none" stroke-linecap="round" stroke-linejoin="round"';
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${view}" role="img" aria-label="${title}">
  <title>${title}</title>
  <g id="carton-scores" ${stroke} stroke="${COLOR.score}" stroke-width="0.05" stroke-dasharray="0.3 0.2">
    ${creases.map((c, i) => `<path id="score-${i}" d="${c.path.svg}"/>`).join('\n    ')}
  </g>
  <g id="letter-seams" ${stroke} stroke="${COLOR.ink}" stroke-width="0.05">
    <path id="seam-left" d="${seams.left.svg}" pathLength="1" stroke-dasharray="${seams.visible} 1"/>
    <path id="seam-right" d="${seams.right.svg}" pathLength="1" stroke-dasharray="${seams.visible} 1"/>
  </g>
  <path id="orange-line" d="${main.svg}" ${stroke} stroke="${COLOR.orangeInk}" stroke-width="0.088"/>
  <circle id="orange-point" cx="${main.pointAt(main.total)[0].toFixed(3)}" cy="${main.pointAt(main.total)[1].toFixed(3)}" r="0.2" fill="${COLOR.orange}"/>
</svg>`;
}

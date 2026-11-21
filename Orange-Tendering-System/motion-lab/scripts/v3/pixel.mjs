// Development helper: sample rendered canvas colours (calibration of palette vs lighting).
//   node scripts/v3/pixel.mjs --url "/v3/?clean" --eval "studio.seek(5.3)" --at "700,100 200,300"
import { chromium } from 'playwright-core';

const args = Object.fromEntries(
  process.argv.slice(2).reduce((acc, a, i, all) => {
    if (a.startsWith('--')) acc.push([a.slice(2), all[i + 1] && !all[i + 1].startsWith('--') ? all[i + 1] : true]);
    return acc;
  }, [])
);
const EDGE = process.env.LAB_BROWSER || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const base = process.env.LAB_URL || 'http://127.0.0.1:4173';
const browser = await chromium.launch({ executablePath: EDGE, headless: true, args: ['--enable-webgl', '--ignore-gpu-blocklist'] });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
await page.goto(base + String(args.url || '/v3/?clean'), { waitUntil: 'load' });
await page.waitForFunction(() => window.studio && (window.studio.ready || window.studio.error));
if (args.eval) await page.evaluate(`(async () => { ${args.eval} })()`);
await page.waitForTimeout(400);
const shot = await page.screenshot({ type: 'png' });
const points = String(args.at || '700,100').split(' ').map(p => p.split(',').map(Number));
const colours = await page.evaluate(
  async ({ data, points }) => {
    const img = new Image();
    img.src = 'data:image/png;base64,' + data;
    await img.decode();
    const c = document.createElement('canvas');
    c.width = img.width;
    c.height = img.height;
    const ctx = c.getContext('2d');
    ctx.drawImage(img, 0, 0);
    return points.map(([x, y]) => {
      // 5x5 mean so paper grain does not skew the reading.
      const d = ctx.getImageData(x - 2, y - 2, 5, 5).data;
      let r = 0, g = 0, b = 0;
      for (let i = 0; i < d.length; i += 4) { r += d[i]; g += d[i + 1]; b += d[i + 2]; }
      const n = d.length / 4;
      const hex = [r, g, b].map(v => Math.round(v / n).toString(16).padStart(2, '0')).join('');
      return { x, y, hex: '#' + hex.toUpperCase() };
    });
  },
  { data: shot.toString('base64'), points }
);
console.log(JSON.stringify(colours));
await browser.close();

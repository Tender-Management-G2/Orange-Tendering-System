// Development helper: one real-browser screenshot of the V3 lab at a chosen state.
//   node scripts/v3/shot.mjs --url "/v3/?chapter=registration&clean" --out out.png \
//        [--size 1440x900] [--dpr 1] [--eval "studio.seek(3.2)"] [--wait 250] [--stage] [--reduced]
import { chromium } from 'playwright-core';
import { resolve, dirname } from 'node:path';
import { mkdir } from 'node:fs/promises';

const args = Object.fromEntries(
  process.argv.slice(2).reduce((acc, a, i, all) => {
    if (a.startsWith('--')) acc.push([a.slice(2), all[i + 1] && !all[i + 1].startsWith('--') ? all[i + 1] : true]);
    return acc;
  }, [])
);
const EDGE = process.env.LAB_BROWSER || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const base = process.env.LAB_URL || 'http://127.0.0.1:4173';
const [width, height] = String(args.size || '1440x900').split('x').map(Number);
const out = resolve(String(args.out || 'shot.png'));
await mkdir(dirname(out), { recursive: true });

const browser = await chromium.launch({ executablePath: EDGE, headless: true, args: ['--enable-webgl', '--ignore-gpu-blocklist'] });
const context = await browser.newContext({
  viewport: { width, height },
  deviceScaleFactor: Number(args.dpr || 1),
  reducedMotion: args.reduced ? 'reduce' : 'no-preference'
});
const page = await context.newPage();
const errors = [];
page.on('pageerror', e => errors.push(e.message));
page.on('console', m => {
  if (m.type() === 'error' || m.type() === 'warning') errors.push(`${m.type()}: ${m.text()}`);
});
await page.goto(base + String(args.url || '/v3/'), { waitUntil: 'load' });
try {
  await page.waitForFunction(() => window.studio && (window.studio.ready || window.studio.error), null, { timeout: 15000 });
} catch {
  errors.push('studio did not become ready');
}
const bootError = await page.evaluate(() => window.studio?.error || null);
if (bootError) errors.push(bootError);
if (args.eval) {
  try {
    const result = await page.evaluate(`(async () => { ${args.eval} })()`);
    if (result !== undefined) console.log('eval →', JSON.stringify(result));
  } catch (e) {
    errors.push(`eval: ${e.message}`);
  }
}
await page.waitForTimeout(Number(args.wait || 250));
if (args.stage) await page.locator('#stage').screenshot({ path: out });
else await page.screenshot({ path: out });
if (errors.length) console.log('ERRORS\n' + errors.join('\n'));
console.log(out);
await browser.close();

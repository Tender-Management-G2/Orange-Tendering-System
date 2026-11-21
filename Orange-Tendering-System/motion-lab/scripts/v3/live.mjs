// Development helper: drive the lab's real form like a person would and capture what the stage
// shows. Steps are a compact script:
//   type:<field>:<text>   type into a field with human-ish delays      shot:<label>   capture
//   fill:<field>:<text>   set a value at once (paste / autofill)       wait:<ms>
//   focus:<field>         blur                                          clear:<field>
//   key:<Key>             press a key (Tab, Enter…)
//   node scripts/v3/live.mjs --url "/v3/?chapter=registration&clean" --out sheet.png \
//        --steps "wait:5600|type:name:Layla Haddad|wait:2600|shot:name" [--cols 4] [--scale 0.5] [--full]
import { chromium } from 'playwright-core';
import { resolve, dirname } from 'node:path';
import { mkdir, writeFile } from 'node:fs/promises';

const args = Object.fromEntries(
  process.argv.slice(2).reduce((acc, a, i, all) => {
    if (a.startsWith('--')) acc.push([a.slice(2), all[i + 1] && !all[i + 1].startsWith('--') ? all[i + 1] : true]);
    return acc;
  }, [])
);
const EDGE = process.env.LAB_BROWSER || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const base = process.env.LAB_URL || 'http://127.0.0.1:4173';
const [width, height] = String(args.size || '1440x900').split('x').map(Number);
const cols = Number(args.cols || 4);
const scale = Number(args.scale || 0.5);
const out = resolve(String(args.out || 'live.png'));
await mkdir(dirname(out), { recursive: true });

const browser = await chromium.launch({ executablePath: EDGE, headless: true, args: ['--enable-webgl', '--ignore-gpu-blocklist'] });
const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: Number(args.dpr || 1), reducedMotion: args.reduced ? 'reduce' : 'no-preference' });
const page = await context.newPage();
const errors = [];
page.on('pageerror', e => errors.push(e.message));
page.on('console', m => m.type() === 'error' && errors.push(m.text()));
await page.goto(base + String(args.url || '/v3/?clean'), { waitUntil: 'load' });
await page.waitForFunction(() => window.studio && (window.studio.ready || window.studio.error));

const form = await page.evaluate(() => (document.body.dataset.chapter === 'login' ? 'login' : 'reg'));
const sel = f => `#${form}-${f}`;
const frames = [];
const log = [];
for (const step of String(args.steps || 'shot:start').split('|')) {
  const [op, a, ...rest] = step.split(':');
  const b = rest.join(':');
  if (op === 'wait') await page.waitForTimeout(Number(a));
  else if (op === 'type') await page.locator(sel(a)).pressSequentially(b, { delay: 55 });
  else if (op === 'fill') await page.locator(sel(a)).fill(b);
  else if (op === 'clear') await page.locator(sel(a)).fill('');
  else if (op === 'focus') await page.locator(sel(a)).focus();
  else if (op === 'blur') await page.evaluate(() => document.activeElement?.blur());
  else if (op === 'key') await page.keyboard.press(a);
  else if (op === 'eval') await page.evaluate(`(async () => { ${[a, b].filter(Boolean).join(':')} })()`);
  else if (op === 'shot') {
    const target = args.full ? page : page.locator('#stage');
    frames.push({ t: a, data: (await target.screenshot({ type: 'png' })).toString('base64') });
    const s = await page.evaluate(() => {
      const st = window.studio.state();
      return { shot: st.shot, status: st.status, segments: Object.fromEntries(Object.entries(st.segments).map(([k, v]) => [k, v.time])) };
    });
    log.push({ label: a, ...s });
  }
}
const sheet = await context.newPage();
const png = await sheet.evaluate(
  async ({ frames, cols, scale }) => {
    const images = await Promise.all(
      frames.map(async f => {
        const img = new Image();
        img.src = 'data:image/png;base64,' + f.data;
        await img.decode();
        return img;
      })
    );
    const w = Math.round(images[0].width * scale);
    const h = Math.round(images[0].height * scale);
    const rows = Math.ceil(images.length / cols);
    const c = document.createElement('canvas');
    c.width = cols * w + (cols + 1) * 6;
    c.height = rows * (h + 22) + 6;
    const ctx = c.getContext('2d');
    ctx.fillStyle = '#2b2622';
    ctx.fillRect(0, 0, c.width, c.height);
    ctx.imageSmoothingQuality = 'high';
    images.forEach((img, i) => {
      const x = 6 + (i % cols) * (w + 6);
      const y = 6 + Math.floor(i / cols) * (h + 22);
      ctx.drawImage(img, x, y, w, h);
      ctx.fillStyle = '#f3ece2';
      ctx.font = '13px sans-serif';
      ctx.fillText(frames[i].t, x + 4, y + h + 15);
    });
    return c.toDataURL('image/png').split(',')[1];
  },
  { frames, cols, scale }
);
await writeFile(out, Buffer.from(png, 'base64'));
console.log(JSON.stringify(log, null, 1));
if (errors.length) console.log('ERRORS\n' + errors.join('\n'));
console.log(out);
await browser.close();

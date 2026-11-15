// Development helper: contact sheet of the showreel at several times (real browser frames).
//   node scripts/v3/strip.mjs --url "/v3/?chapter=registration&clean" --times "0,1,2,3" --out strip.png
//        [--cols 4] [--scale 0.5] [--size 1440x900] [--full] [--dpr 1]
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
const times = String(args.times || '0').split(',').map(Number);
const cols = Number(args.cols || 4);
const scale = Number(args.scale || 0.5);
const out = resolve(String(args.out || 'strip.png'));
await mkdir(dirname(out), { recursive: true });

const browser = await chromium.launch({ executablePath: EDGE, headless: true, args: ['--enable-webgl', '--ignore-gpu-blocklist'] });
const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: Number(args.dpr || 1) });
const page = await context.newPage();
const errors = [];
page.on('pageerror', e => errors.push(e.message));
await page.goto(base + String(args.url || '/v3/?clean'), { waitUntil: 'load' });
await page.waitForFunction(() => window.studio && (window.studio.ready || window.studio.error));
const bootError = await page.evaluate(() => window.studio?.error || null);
if (bootError) errors.push(bootError);
if (args.pre) await page.evaluate(`(async () => { ${args.pre} })()`);
await page.waitForTimeout(1300); // captions settle
const frames = [];
for (const t of times) {
  await page.evaluate(time => window.studio.seek(time), t);
  await page.waitForTimeout(140);
  const target = args.full ? page : page.locator('#stage');
  frames.push({ t, data: (await target.screenshot({ type: 'png' })).toString('base64') });
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
      ctx.fillText(`t = ${frames[i].t}s`, x + 4, y + h + 15);
    });
    return c.toDataURL('image/png').split(',')[1];
  },
  { frames, cols, scale }
);
await writeFile(out, Buffer.from(png, 'base64'));
if (errors.length) console.log('ERRORS\n' + errors.join('\n'));
console.log(out);
await browser.close();

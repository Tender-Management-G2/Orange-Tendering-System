// Optional Playwright smoke/capture script. Run using a locally installed Playwright package.
// Requires explicit demo URL and existing demo auth storage state; never collects credentials.
import { chromium } from 'playwright';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
const [url, stateFile, outDir, ...flags]=process.argv.slice(2);
if(!url||!stateFile||!outDir||!flags.includes('--demo-confirmed')) throw new Error('Usage: node capture-radar.mjs <local-url> <demo-storage-state.json> <output-dir> --demo-confirmed [--dev-https]');
const target=new URL(url);
if(!['localhost','127.0.0.1','[::1]'].includes(target.hostname)||!['http:','https:'].includes(target.protocol)) throw new Error('Only loopback URLs allowed');
await mkdir(resolve(outDir),{recursive:true});
const browser=await chromium.launch({headless:true});
let bad=0;
try {
 for(const [w,h] of [[1920,1080],[1366,768],[1024,768],[390,844]]) {
  for(const theme of ['light','dark']) {
   const ctx=await browser.newContext({viewport:{width:w,height:h}, colorScheme:theme, storageState:resolve(stateFile),ignoreHTTPSErrors:flags.includes('--dev-https')});
   const page=await ctx.newPage(); const issues=[];
   page.on('pageerror',e=>issues.push('pageerror: '+e.message));
   page.on('console',m=>{if(m.type()==='error')issues.push('console error');});
   try {
    const response=await page.goto(target.href,{waitUntil:'domcontentloaded'});
    if(!response?.ok()||new URL(page.url()).pathname!==target.pathname)throw new Error('Navigation failed or redirected; verify demo authentication.');
    // Adapt this selector to the actual Radar root and readiness condition before using as QA.
    await page.locator('.orange-radar').waitFor({state:'visible',timeout:15000});
    await page.evaluate(()=>document.fonts.ready);
    if(await page.evaluate(()=>document.documentElement.scrollWidth>window.innerWidth+2))issues.push('horizontal overflow');
    await page.screenshot({path:resolve(outDir,`radar-${w}-${theme}.png`),fullPage:true,animations:'disabled'});
    console.log(JSON.stringify({width:w,requestedTheme:theme,issues}));bad+=issues.length;
   } finally { await ctx.close(); }
  }
 }
} finally {await browser.close();}
if(bad)process.exitCode=1;
// Theme preference may be overridden by app settings. Verify actual theme and ready data manually.
// This is a baseline capture helper, not full E2E test or proof that every graph state works.

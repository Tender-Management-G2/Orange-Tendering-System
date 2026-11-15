import { chromium } from 'playwright-core';
import { mkdir, writeFile, unlink } from 'node:fs/promises';
import { resolve, join } from 'node:path';

const output=resolve(import.meta.dirname,'..','evidence','unified');await mkdir(output,{recursive:true});
const browser=await chromium.launch({executablePath:'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',headless:true,args:['--enable-webgl']});
const clips=[
  {name:'registration-normal',mode:'paper',from:0,to:1,rate:1},
  {name:'login-normal',mode:'signal',from:0,to:1,rate:1},
  {name:'registration-fold-slow',mode:'paper',from:.24,to:.39,rate:.4},
  {name:'registration-architecture-slow',mode:'paper',from:.50,to:.83,rate:.4},
  {name:'login-optical-slow',mode:'signal',from:.53,to:.94,rate:.4},
  {name:'login-fiber-slow',mode:'signal',from:.94,to:1,rate:.4}
];
const report={time:new Date().toISOString(),browser:browser.version(),url:'http://127.0.0.1:4173',clips:[],errors:[]};
for(const clip of clips){
  const context=await browser.newContext({viewport:{width:1440,height:900},deviceScaleFactor:1,recordVideo:{dir:output,size:{width:1440,height:900}}});
  const page=await context.newPage();page.on('pageerror',error=>report.errors.push({clip:clip.name,message:error.message}));
  await page.goto(report.url,{waitUntil:'networkidle'});await page.waitForFunction(()=>Boolean(window.motionLab));
  await page.evaluate(mode=>window.motionLab.setMode(mode),clip.mode);
  const started=Date.now();await page.evaluate(({from,to,rate})=>window.motionLab.playRange(from,to,rate),clip);
  await page.waitForFunction(to=>{const s=window.motionLab.getState();return !s.playing&&Math.abs(s.progress-to)<.002;},clip.to,{timeout:65000});
  await page.waitForTimeout(250);
  const video=page.video();await context.close();
  const filename=`${clip.name}.webm`;await video.saveAs(join(output,filename));await unlink(await video.path()).catch(()=>{});
  report.clips.push({...clip,file:filename,durationMs:Date.now()-started});
  console.log(`${filename} ${report.clips.at(-1).durationMs}ms`);
}
await browser.close();await writeFile(join(output,'recordings.json'),JSON.stringify(report,null,2));
console.log(JSON.stringify({clips:report.clips.length,errors:report.errors}));

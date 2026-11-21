import { chromium } from 'playwright-core';
import { join, resolve } from 'node:path';
import { writeFile } from 'node:fs/promises';

const output=resolve(import.meta.dirname,'..','evidence','unified');
const browser=await chromium.launch({executablePath:'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',headless:true});
const page=await browser.newPage({viewport:{width:1440,height:900}});
const clips={'registration-fold-slow':[1.5,3,5], 'registration-architecture-slow':[3,7,11], 'login-optical-slow':[2,4,6], 'login-fiber-slow':[.5,1]};
const report={videos:[],errors:[]};page.on('pageerror',e=>report.errors.push(e.message));
for(const [name,moments] of Object.entries(clips)){
  const src=`http://127.0.0.1:4173/evidence/unified/${name}.webm`;
  await page.setContent(`<body style="margin:0;background:#f8f5ef"><video id="v" src="${src}" preload="auto" muted playsinline style="width:1440px;height:900px"></video></body>`);
  await page.waitForFunction(()=>document.getElementById('v').readyState>=1,{timeout:15000});
  const duration=await page.locator('#v').evaluate(v=>v.duration);
  await page.locator('#v').evaluate(v=>{v.playbackRate=3;return v.play();});
  for(const second of moments){
    await page.waitForFunction(t=>document.getElementById('v').currentTime>=t,second,{timeout:18000});
    await page.locator('#v').evaluate(v=>v.pause());await page.waitForTimeout(120);
    await page.screenshot({path:join(output,`video-review-${name}-${String(second).replace('.','_')}.png`)});
    await page.locator('#v').evaluate(v=>v.play());
  }
  report.videos.push({name,duration,moments});
}
await browser.close();await writeFile(join(output,'recording-review.json'),JSON.stringify(report,null,2));
console.log(JSON.stringify(report,null,2));

import { chromium } from 'playwright-core';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';

const base=resolve(import.meta.dirname,'..');
const output=join(base,'evidence','unified');
await mkdir(output,{recursive:true});
const browser=await chromium.launch({executablePath:'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',headless:true,args:['--enable-webgl']});
const meta={time:new Date().toISOString(),browser:browser.version(),url:'http://127.0.0.1:4173',screenshots:[],errors:[],states:[]};
async function ready(viewport,reducedMotion='no-preference'){
  const context=await browser.newContext({viewport,deviceScaleFactor:1,reducedMotion});
  const page=await context.newPage();page.on('pageerror',e=>meta.errors.push(e.message));
  await page.goto(meta.url,{waitUntil:'networkidle'});await page.waitForFunction(()=>Boolean(window.motionLab));
  return {context,page};
}
async function frame(page,name){await page.screenshot({path:join(output,name)});meta.screenshots.push(name);}
for(const [label,viewport] of [['desktop',{width:1440,height:900}],['mobile',{width:390,height:844}]]){
  const {context,page}=await ready(viewport);
  for(const [mode,marks] of [['paper',[0,.07,.14,.25,.39,.65,.83,1]],['signal',[0,.07,.12,.32,.55,.78,.94,1]]]){
    await page.evaluate(m=>window.motionLab.setMode(m),mode);
    for(const p of marks){
      await page.evaluate(v=>window.motionLab.setProgress(v),p);await page.waitForTimeout(120);
      const prefix=`${mode}-${label}-${String(Math.round(p*100)).padStart(3,'0')}`;
      await frame(page,`${prefix}.png`);
      if(label==='desktop'){
        const stageName=`${prefix}-stage.png`;
        await page.locator('.stage').screenshot({path:join(output,stageName)});meta.screenshots.push(stageName);
      }
    }
  }
  meta.states.push({label,state:await page.evaluate(()=>window.motionLab.getState()),fingerprintDisabled:await page.locator('#login-fingerprint').isDisabled(),canvas:await page.evaluate(()=>window.motionLab.getRenderStats())});
  await context.close();
}
const {context,page}=await ready({width:1440,height:900},'reduce');
await page.evaluate(()=>window.motionLab.setMode('signal'));await frame(page,'signal-reduced.png');
meta.states.push({label:'reduced',state:await page.evaluate(()=>window.motionLab.getState())});await context.close();
await browser.close();
await writeFile(join(output,'capture.json'),JSON.stringify(meta,null,2));
console.log(JSON.stringify({files:meta.screenshots.length,errors:meta.errors,states:meta.states},null,2));

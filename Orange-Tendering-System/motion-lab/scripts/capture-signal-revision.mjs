import { chromium } from 'playwright-core';
import { writeFile, unlink } from 'node:fs/promises';
import { resolve, join } from 'node:path';

const output=join(resolve(import.meta.dirname,'..'),'evidence','revision');
const browser=await chromium.launch({executablePath:'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',headless:true,args:['--enable-webgl']});
const report={capturedAt:new Date().toISOString(),browser:browser.version(),files:[],errors:[]};
try{
  for(const rate of [1,.4]){
    const context=await browser.newContext({viewport:{width:1440,height:900},deviceScaleFactor:1,recordVideo:{dir:output,size:{width:1440,height:900}}});
    const page=await context.newPage();page.on('pageerror',error=>report.errors.push(error.message));
    await page.goto('http://127.0.0.1:4173',{waitUntil:'networkidle'});
    await page.waitForFunction(()=>window.motionLab);
    await page.evaluate(rate=>{window.motionLab.setMode('signal');window.motionLab.playRange(0,1,rate);},rate);
    await page.waitForFunction(()=>window.motionLab.getState().progress>=.999,null,{timeout:45000});
    await page.waitForTimeout(350);
    const video=page.video();await context.close();
    const name=`signal-${rate===1?'normal':'slow'}.webm`;
    await video.saveAs(join(output,name));report.files.push(name);
    await unlink(await video.path()).catch(()=>{});
  }
}finally{await writeFile(join(output,'signal-manifest.json'),JSON.stringify(report,null,2));await browser.close();}

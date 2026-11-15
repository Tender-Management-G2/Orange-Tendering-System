import { chromium } from 'playwright-core';
import { mkdir, writeFile, readFile, unlink } from 'node:fs/promises';
import { resolve, join } from 'node:path';

const root=resolve(import.meta.dirname,'..');
const output=join(root,'evidence','revision');
await mkdir(output,{recursive:true});
const browser=await chromium.launch({executablePath:'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',headless:true,args:['--enable-webgl']});
const irisOnly=process.argv.includes('--iris-only');
const prior=irisOnly?JSON.parse(await readFile(join(output,'manifest.json'),'utf8')):null;
const report=prior??{capturedAt:new Date().toISOString(),url:'http://127.0.0.1:4173',browser:browser.version(),files:[],errors:[]};
async function ready(context){
  const page=await context.newPage();page.on('pageerror',error=>report.errors.push(error.message));
  await page.goto(report.url,{waitUntil:'networkidle'});await page.waitForFunction(()=>window.motionLab);
  return page;
}
async function stills(label,viewport,points){
  const context=await browser.newContext({viewport,deviceScaleFactor:1});
  const page=await ready(context);
  for(const progress of points){
    await page.evaluate(p=>window.motionLab.setProgress(p),progress);
    await page.waitForTimeout(160);
    const filename=`paper-${label}-${String(Math.round(progress*1000)).padStart(3,'0')}.png`;
    await page.screenshot({path:join(output,filename)});report.files.push(filename);
  }
  await context.close();
}
async function record(name,start,end,rate){
  const context=await browser.newContext({viewport:{width:1440,height:900},deviceScaleFactor:1,recordVideo:{dir:output,size:{width:1440,height:900}}});
  const page=await ready(context);
  await page.evaluate(({start,end,rate})=>window.motionLab.playRange(start,end,rate),{start,end,rate});
  await page.waitForFunction(end=>window.motionLab.getState().progress>=end-.001,end,{timeout:60000});
  await page.waitForTimeout(350);
  const video=page.video();await context.close();
  const filename=`${name}-${rate===1?'normal':'slow'}.webm`;
  await video.saveAs(join(output,filename));report.files.push(filename);
  await unlink(await video.path()).catch(()=>{});
}
try{
  if(!irisOnly){
    await stills('desktop',{width:1440,height:900},[.08,.15,.21,.27,.32,.38,.46,.55,.62,.69,.76,.82,.86,.91,.95,1]);
    await stills('mobile',{width:390,height:844},[.15,.32,.62,.76,.91,1]);
  }
  if(!process.argv.includes('--stills-only'))for(const [name,start,end] of (irisOnly?[['c-iris',.825,1]]:[['a-card',.03,.39],['b-building',.43,.825],['c-iris',.825,1]])){
    await record(name,start,end,1);await record(name,start,end,.4);
  }
}finally{
  report.capturedAt=new Date().toISOString();report.files=[...new Set(report.files)];
  await writeFile(join(output,'manifest.json'),JSON.stringify(report,null,2));
  await browser.close();
}

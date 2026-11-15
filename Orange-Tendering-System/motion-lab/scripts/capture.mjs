import { chromium } from 'playwright-core';
import { mkdir, writeFile, unlink } from 'node:fs/promises';
import { resolve, join } from 'node:path';

const root=resolve(import.meta.dirname,'..');
const output=join(root,'evidence');
await mkdir(output,{recursive:true});
const url='http://127.0.0.1:4173';
const edge='C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const browser=await chromium.launch({executablePath:edge,headless:true,args:['--enable-webgl']});
const meta={capturedAt:new Date().toISOString(),browser:'Microsoft Edge',version:browser.version(),url,files:[],metrics:[],checks:{}};

async function pageReady(context){
  const page=await context.newPage();
  const errors=[];page.on('pageerror',error=>errors.push(error.message));
  await page.goto(url,{waitUntil:'networkidle'});
  await page.waitForFunction(()=>Boolean(window.motionLab));
  return {page,errors};
}
async function snapshot(page,file){await page.screenshot({path:join(output,file)});meta.files.push(file);}
async function sampleFps(page){
  return page.evaluate(()=>new Promise(resolve=>{
    let frames=0,start=0;
    const step=t=>{if(!start)start=t;frames++;if(t-start<2000)requestAnimationFrame(step);else resolve(Math.round(frames*1000/(t-start)));};
    requestAnimationFrame(step);
  }));
}
async function captureViewport(label,width,height){
  const context=await browser.newContext({viewport:{width,height},deviceScaleFactor:1,reducedMotion:'no-preference'});
  const {page,errors}=await pageReady(context);
  for(const mode of ['paper','signal']){
    await page.evaluate(m=>window.motionLab.setMode(m),mode);
    for(const percent of [0,25,50,75,100]){
      await page.evaluate(p=>window.motionLab.setProgress(p/100),percent);
      await page.waitForTimeout(180);
      await snapshot(page,`${mode}-${label}-${String(percent).padStart(3,'0')}.png`);
    }
    const fps=await sampleFps(page);
    const stats=await page.evaluate(()=>window.motionLab.getRenderStats());
    meta.metrics.push({mode,viewport:`${width}x${height}`,rafFps:fps,...stats});
  }
  meta.checks[`${label}PageErrors`]=errors;
  meta.checks[`${label}UserAgent`]=await page.evaluate(()=>navigator.userAgent);
  meta.checks[`${label}WebGLRenderer`]=await page.evaluate(()=>{
    const gl=document.getElementById('scene').getContext('webgl2');
    const info=gl?.getExtension('WEBGL_debug_renderer_info');
    return info?gl.getParameter(info.UNMASKED_RENDERER_WEBGL):'Renderer string unavailable';
  });
  meta.checks[`${label}FingerprintDisabled`]=await page.getByRole('button',{name:'Fingerprint / Coming soon'}).isDisabled();
  await context.close();
}

async function recordJourney(mode){
  const context=await browser.newContext({viewport:{width:1440,height:900},deviceScaleFactor:1,recordVideo:{dir:output,size:{width:1440,height:900}}});
  const {page,errors}=await pageReady(context);
  await page.evaluate(m=>window.motionLab.setMode(m),mode);
  await page.getByRole('button',{name:'▶ Play'}).click();
  await page.waitForFunction(()=>window.motionLab.getState().progress>=.999,null,{timeout:25000});
  await page.waitForTimeout(450);
  const state=await page.evaluate(()=>window.motionLab.getState());
  const video=page.video();
  await context.close();
  const filename=`${mode}-playback.webm`;
  await video.saveAs(join(output,filename));meta.files.push(filename);
  await unlink(await video.path()).catch(()=>{});
  meta.checks[`${mode}Playback`]={state,errors};
}
async function recordInteraction(kind){
  const context=await browser.newContext({viewport:{width:1440,height:900},deviceScaleFactor:1,recordVideo:{dir:output,size:{width:1440,height:900}}});
  const {page,errors}=await pageReady(context);
  await page.evaluate(()=>window.motionLab.setProgress(.8));
  if(kind==='orbit'){
    await page.getByLabel('Orbit inspection').check();
    const bounds=await page.locator('#scene').boundingBox();
    await page.mouse.move(bounds.x+bounds.width*.48,bounds.y+bounds.height*.45);
    await page.mouse.down();
    await page.mouse.move(bounds.x+bounds.width*.73,bounds.y+bounds.height*.6,{steps:55});
    await page.mouse.move(bounds.x+bounds.width*.42,bounds.y+bounds.height*.54,{steps:55});
    await page.mouse.up();
  }else{
    await page.getByLabel('Supplier category').selectOption({label:'Telecommunications'});
    await page.waitForTimeout(1450);
    await page.getByLabel('Supplier category').selectOption({label:'Construction & Maintenance'});
    await page.waitForTimeout(1450);
  }
  await page.waitForTimeout(400);
  const video=page.video();await context.close();
  const filename=`paper-${kind}.webm`;
  await video.saveAs(join(output,filename));meta.files.push(filename);
  await unlink(await video.path()).catch(()=>{});
  meta.checks[`${kind}RecordingErrors`]=errors;
}

try{
  await captureViewport('desktop',1440,900);
  await captureViewport('mobile',390,844);
  const context=await browser.newContext({viewport:{width:1440,height:900},deviceScaleFactor:1});
  const {page,errors}=await pageReady(context);
  await page.evaluate(()=>window.motionLab.setProgress(.83));
  await page.getByLabel('Supplier category').selectOption({label:'Telecommunications'});
  await page.waitForTimeout(1350);
  await snapshot(page,'paper-telecom-category.png');
  await page.getByLabel('Supplier category').selectOption({label:'Construction & Maintenance'});
  await page.waitForTimeout(1350);
  await snapshot(page,'paper-construction-category.png');
  await page.getByLabel('Wireframe proof').check();
  await snapshot(page,'paper-wireframe.png');
  await page.getByLabel('Wireframe proof').uncheck();
  await page.getByLabel('Orbit inspection').check();
  const bounds=await page.locator('#scene').boundingBox();
  await page.mouse.move(bounds.x+bounds.width*.48,bounds.y+bounds.height*.45);
  await page.mouse.down();await page.mouse.move(bounds.x+bounds.width*.68,bounds.y+bounds.height*.62,{steps:20});await page.mouse.up();
  await snapshot(page,'paper-orbit.png');
  meta.checks.orbit={...await page.evaluate(()=>window.motionLab.getState()),errors};
  await page.getByLabel('Orbit inspection').uncheck();
  await page.getByLabel('Wireframe proof').check();
  meta.checks.wireframeEnabled=await page.evaluate(()=>window.motionLab.getState().wireframe);
  await page.getByLabel('Wireframe proof').uncheck();
  await page.evaluate(()=>window.motionLab.setProgress(.8));
  await page.getByRole('button',{name:'↶ Reverse'}).click();
  await page.waitForTimeout(650);
  meta.checks.reverseProgress=await page.evaluate(()=>window.motionLab.getState().progress);
  await page.getByRole('button',{name:'Ⅱ Pause'}).click();
  await page.evaluate(()=>{window.motionLab.setMode('signal');window.motionLab.setMode('paper');window.motionLab.setMode('signal');});
  meta.checks.rapidModeSwitch={...await page.evaluate(()=>window.motionLab.getState()),errors};
  await context.close();
  const reduced=await browser.newContext({viewport:{width:390,height:844},reducedMotion:'reduce'});
  const reducedPage=(await pageReady(reduced)).page;
  meta.checks.reducedMotionInitiallyChecked=await reducedPage.getByLabel('Reduced motion endpoint').isChecked();
  await reducedPage.getByRole('button',{name:'▶ Play'}).click();
  meta.checks.reducedMotionEndpoint=await reducedPage.evaluate(()=>window.motionLab.getState());
  await reduced.close();
  const unavailable=await browser.newContext({viewport:{width:390,height:844}});
  await unavailable.addInitScript(()=>{
    const original=HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext=function(kind,...args){return kind==='webgl'||kind==='webgl2'?null:original.call(this,kind,...args);};
  });
  const unavailablePage=await unavailable.newPage();
  await unavailablePage.goto(url,{waitUntil:'domcontentloaded'});
  meta.checks.webglFallbackVisible=await unavailablePage.locator('#webgl-fallback').isVisible();
  await unavailable.close();
  await recordJourney('paper');
  await recordJourney('signal');
  await recordInteraction('orbit');
  await recordInteraction('categories');
}finally{
  await writeFile(join(output,'manifest.json'),JSON.stringify(meta,null,2));
  await browser.close();
}

import { chromium } from 'playwright-core';
import { writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';

const root=resolve(import.meta.dirname,'..');
const output=join(root,'evidence','revision');
const browser=await chromium.launch({executablePath:'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',headless:true});
const page=await browser.newPage({viewport:{width:1440,height:900}});
const details=[];
try{
  const signalOnly=process.argv.includes('--signal-only'),irisOnly=process.argv.includes('--iris-only');
  const shots=signalOnly?['signal']:irisOnly?['c-iris']:['a-card','b-building','c-iris'];
  for(const shot of shots)for(const rate of ['normal','slow']){
    const name=`${shot}-${rate}`;
    await page.goto('http://127.0.0.1:4173/evidence/revision/manifest.json',{waitUntil:'domcontentloaded'});
    await page.evaluate(name=>{
      document.body.replaceChildren();document.body.style.margin='0';document.body.style.background='#111';
      const video=document.createElement('video');video.id='review-video';video.src=`/evidence/revision/${name}.webm`;
      video.style.width='1440px';video.style.height='900px';video.style.objectFit='contain';document.body.append(video);
    },name);
    await page.waitForFunction(()=>document.getElementById('review-video')?.readyState>=1);
    const info=await page.evaluate(()=>{const v=document.getElementById('review-video');return {duration:v.duration,width:v.videoWidth,height:v.videoHeight};});
    details.push({name,...info});
    await page.evaluate(()=>{const v=document.getElementById('review-video');v.muted=true;return v.play();});
    for(const fraction of [.25,.5,.75]){
      await page.waitForFunction(f=>document.getElementById('review-video').currentTime>=document.getElementById('review-video').duration*f,fraction,{timeout:30000});
      await page.evaluate(()=>new Promise(resolve=>document.getElementById('review-video').requestVideoFrameCallback(()=>resolve())));
      await page.locator('#review-video').screenshot({path:join(output,`${name}-review-${Math.round(fraction*100)}.png`)});
    }
  }
  await writeFile(join(output,signalOnly?'signal-video-review.json':irisOnly?'iris-video-review.json':'video-review.json'),JSON.stringify(details,null,2));
  process.stdout.write(JSON.stringify(details,null,2)+'\n');
}finally{await browser.close();}

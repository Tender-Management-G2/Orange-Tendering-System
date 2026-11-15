import {chromium} from 'playwright-core';
const browser=await chromium.launch({executablePath:'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',headless:true,args:['--enable-webgl']});
const page=await browser.newPage({viewport:{width:1440,height:900}});
await page.goto('http://127.0.0.1:4173',{waitUntil:'networkidle'});await page.waitForFunction(()=>Boolean(window.motionLab));
for(const p of [.55,.78,.86,.94,1]){await page.evaluate(x=>{window.motionLab.setMode('signal');window.motionLab.setProgress(x);},p);console.log(p,await page.evaluate(()=>window.motionLab.inspectOpticalCenter()));}
await browser.close();

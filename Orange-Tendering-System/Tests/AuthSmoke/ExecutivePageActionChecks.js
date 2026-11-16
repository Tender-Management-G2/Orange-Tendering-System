'use strict';
// Command matching and fail-closed classification checks, not browser execution.
const assert=require('node:assert/strict');
const page=require('../../wwwroot/js/executive-page-actions.js');
let count=0;const check=(ok,label)=>{assert.ok(ok,label);console.log(`PASS ${++count} ${label}`);};
const open={label:'Prepare tender',aliases:['Prepare tender','إنشاء عطاء'],kind:'Interface',disabled:false};
const save={label:'Create tender',aliases:['Create tender','حفظ'],kind:'Review',disabled:false};
for(const text of ['اكبس زر إنشاء عطاء','اضغط على زر انشاء عطاء','click the Prepare tender button','press button Prepare tender']){
  // English may put "button" before or after its label.
  check(page.match(text,[open,save]).item===open,`exact page control: ${text}`);
}
check(page.match('اكبس حفظ',[open,save]).item.kind==='Review','save remains a review-only control');
check(page.match('اكبس حذف كل العطاءات',[open,save]).state==='Missing','unknown/destructive label is not guessed');
check(page.match('اكبس حفظ',[save,{...save}]).state==='Choose','duplicate labels require a choice');
check(page.match('اكبس حفظ',[{...save,disabled:true}]).state==='Disabled','disabled button is not enabled');
check(page.match('افتح صفحة العطاءات',[open]).state==='NotButtonCommand','normal navigation keeps its existing path');
check(page.match('اكبس حفظ ثم وافق',[save]).state==='Missing','compound mutation is not reduced to one button');
check(page.match('show buttons',[open,save]).state==='List','page control inventory is explicit');
check(page.match('اكبس https://evil.invalid',[open]).state==='Missing','URLs are not interpreted as controls');
console.log(`RESULT PAGE_ACTION_MATCH_PASS=${count} FAIL=0. Unit matching only; DOM, browser, SQL and workflow execution are separate.`);

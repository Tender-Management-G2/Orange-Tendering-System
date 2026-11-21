'use strict';
// Page controls are discovered afresh for each command. No model-generated CSS,
// JavaScript, URL or generic form submission is ever executed here.
((scope) => {
  const normalize = text => String(text || '').normalize('NFKD').replace(/[\u064b-\u065f\u0670\u0640]/g, '').replace(/[أإآٱ]/g,'ا').replace(/ة/g,'ه').replace(/ى/g,'ي').toLowerCase().replace(/[^\p{L}\p{N}\s]/gu,' ').split(/\s+/).filter(Boolean).map(w=>w.startsWith('ال')?w.slice(2):w).join(' ');
  function request(text) {
    const raw=String(text||'').trim();
    if(/^(?:شو|ايش|ما هي|اعرض|ورجيني|list|show)\s+(?:هي\s+)?(?:الازرار|الأزرار|الكبسات|الازرار المتاحه|buttons|page buttons|available buttons)[؟?!.\s]*$/i.test(raw))return {list:true};
    const m=raw.match(/^(?:اكبس|إكبس|اضغط|إضغط|انقر|كبس|click|press|tap)\s+(?:(?:على|علي|ع|on|the)\s+)*(?:(?:زر|الزر|كبسه|كبسة|الكبسه|الكبسة|button)\s+)?(.+?)[.!؟?]*$/i);
    return m?{name:normalize(m[1])}:null;
  }
  const reviewed = [
    ['[data-open-tender]', ['انشاء عطاء','عطاء جديد','جهز عطاء','تجهيز عطاء','prepare tender','new tender','create tender'], '#tc-dialog'],
    ['[data-open-workflow]', ['افتح سير العمل','سير العمل','open workflow'], '#wf-dialog'],
    ['[data-tender-details]', ['تفاصيل العطاء','details','tender details'], '#supplier-tender-dialog'],
    ['[data-tc-close]', ['اغلاق نموذج العطاء','اغلاق','close','cancel'], null],
    ['[data-wf-close]', ['اغلاق سير العمل','اغلاق','close'], null],
    ['[data-tender-close]', ['اغلاق تفاصيل العطاء','اغلاق','close tender details'], null],
    ['.wf-step[data-phase]', [], null],
    ['button[data-preview]', ['معاينه التقرير','معاينة التقرير','preview report','preview it here'], null]
  ];
  function visible(el) {
    if(!el.isConnected||el.closest('[hidden],[inert],[aria-hidden="true"],dialog:not([open])'))return false;
    const css=scope.getComputedStyle(el);return css.display!=='none'&&css.visibility!=='hidden'&&el.getClientRects().length>0;
  }
  function label(el){return (el.getAttribute('aria-label')||el.innerText||el.value||el.title||'').replace(/\s+/g,' ').trim().slice(0,160);}
  function catalog(doc) {
    const currentDialog=doc.querySelector('dialog[open]:not(.executive-agent)');
    // Include controls inside an open workflow modal as well as the page itself.
    // Exclude the agent's own controls and Identity/credential controls.
    return [...doc.querySelectorAll('button,input[type="submit"],input[type="button"],summary,a[href]')]
      .filter(el=>visible(el)&&(!currentDialog||currentDialog.contains(el))&&!el.closest('[data-executive-agent],[data-ai-chat],.ai-chat,.ws-topbar,nav,.executive-voice-notice')&&!el.matches('[data-voice-search],[data-agent-page-mic]'))
      .map(el=>{
        const text=label(el);if(!text)return null;
        const rule=reviewed.find(([selector])=>el.matches(selector));
        const form=el.form;
        // Submit/default buttons always require the backend workflow, even if they
        // happen to carry a reviewed UI attribute. Never invoke requestSubmit/click.
        const submits=!!form&&(el.tagName==='INPUT'||el.type==='submit');
        const kind=submits?'Review':el.tagName==='SUMMARY'?'Disclosure':rule&&el.type==='button'?'Interface':'Review';
        return {element:el,label:text,kind,disabled:!!el.disabled||el.getAttribute('aria-disabled')==='true',aliases:[text,el.innerText||'',...(rule?.[1]||[])],rule};
      }).filter(Boolean);
  }
  function match(text,items){
    const cmd=request(text);if(!cmd)return {state:'NotButtonCommand'};
    if(cmd.list)return {state:'List',items};
    const found=items.filter(item=>item.aliases.some(alias=>normalize(alias)===cmd.name));
    return found.length===1?{state:found[0].disabled?'Disabled':'Matched',item:found[0]}:found.length>1?{state:'Choose',items:found}:{state:'Missing',items};
  }
  function activate(item,doc) {
    if(!visible(item.element)||item.disabled||item.element.disabled||item.element.getAttribute('aria-disabled')==='true')throw new Error('الزر لم يعد متاحاً. أعد الأمر بعد تحديث الصفحة.');
    const now=catalog(doc).find(x=>x.element===item.element);
    if(!now||now.kind!==item.kind||now.label!==item.label)throw new Error('تغيّر الزر؛ تمت إعادة الطلب للمراجعة.');
    if(now.kind==='Review'){
      now.element.scrollIntoView({block:'center',behavior:'smooth'});now.element.focus();
      return {outcome:'Focused',message:`وصلت لزر «${now.label}». لم أنفذه: يلزم مسار العمل الحالي ومراجعة المدخلات والتأكيد. الموافقات والتوقيع والتصويت والإشعارات تبقى محمية بالتحقق من الهوية.`};
    }
    if(now.kind==='Disclosure'){now.element.parentElement.open=true;return {outcome:'Opened',message:`فتحت قسم «${now.label}» داخل الصفحة.`};}
    // Only reviewed UI-only handlers above may be clicked; unknown buttons fail closed.
    now.element.click();return {outcome:'Activated',message:`فعّلت زر الواجهة «${now.label}». هذا ليس إثبات حفظ أو إرسال أو موافقة.`};
  }
  const api={normalize,request,match,catalog,activate};
  if(typeof module!=='undefined'&&module.exports)module.exports=api;else scope.OrangeExecutivePageActions=api;
})(typeof window!=='undefined'?window:globalThis);

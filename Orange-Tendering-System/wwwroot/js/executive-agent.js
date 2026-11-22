'use strict';
(() => {
  const root = document.querySelector('[data-executive-agent]'); if (!root) return;
  const dialog = root.querySelector('dialog'), command = root.querySelector('[data-agent-command]');
  const language = root.querySelector('[data-agent-language]'), speechStatus = root.querySelector('[data-agent-speech-status]');
  const status = root.querySelector('[data-agent-status]'), steps = root.querySelector('[data-agent-steps]'), actions = root.querySelector('[data-agent-actions]');
  const mic = root.querySelector('[data-agent-mic]'), runButton = root.querySelector('[data-agent-run]');
  const topMic = document.querySelector('[data-voice-search]');
  const target = document.createElement('select'); target.className = 'agent-target'; target.setAttribute('aria-label','Microphone mode'); target.dataset.agentTarget = '';
  const adminSearch = document.body.dataset.workspaceRole === 'Admin';
  const modes = adminSearch
    ? [['agent', 'أوامر صوتية'], ['search', 'بحث صوتي بالعربي', 'ar-JO'], ['search-en', 'Voice search (English)', 'en-US']]
    : [['agent', 'أوامر صوتية'], ['search', 'Voice search']];
  for (const [value, text, speechLanguage] of modes) {
    const option = document.createElement('option');
    option.value = value;
    option.textContent = text;
    if (speechLanguage) option.dataset.speechLanguage = speechLanguage;
    target.append(option);
  }
  if (topMic) {
    // Admin starts in page search. Executive commands remain an explicit selection.
    target.value = adminSearch ? 'search' : 'agent';
    topMic.before(target);
    const updateMicLabel = () => {
      const commands = target.value === 'agent';
      topMic.title = commands ? 'أوامر صوتية مباشرة — اضغط واحكي' : 'Voice search';
      topMic.setAttribute('aria-label', commands ? 'بدء الأوامر الصوتية' : 'Voice search');
    };
    updateMicLabel();
    target.addEventListener('change', updateMicLabel);
  }
  const open = document.createElement('button');open.type='button';open.className='ws-icon-button';open.textContent='AI';open.title='Executive Agent';open.setAttribute('aria-label','Open Executive Agent');
  if(topMic)topMic.after(open);else root.before(open);
  const show = () => { if(!dialog.open)dialog.showModal();command.focus(); };
  open.addEventListener('click',show);
  root.querySelector('[data-agent-close]').addEventListener('click',()=>{stopSpeech(true);dialog.close();});
  dialog.addEventListener('cancel',()=>stopSpeech(true));
  const voiceNotice=document.createElement('aside');voiceNotice.className='executive-voice-notice';voiceNotice.hidden=true;voiceNotice.setAttribute('role','status');voiceNotice.setAttribute('aria-live','polite');root.append(voiceNotice);
  let recognizing, recording, stream, speechTimer, polling, busy=false, transcribing=false, discarded=false, audioContext, vadFrame, handsFree=false, starting=false;
  const message = (text,error=false) => {status.textContent=text;status.classList.toggle('agent-error',error);if(!dialog.open){voiceNotice.hidden=false;voiceNotice.replaceChildren();const p=document.createElement('span');p.textContent=text;voiceNotice.append(p);voiceNotice.classList.toggle('agent-error',error);const more=document.createElement('button');more.type='button';more.textContent='التفاصيل / Details';more.addEventListener('click',show);const close=document.createElement('button');close.type='button';close.textContent='×';close.setAttribute('aria-label','إخفاء التنبيه');close.addEventListener('click',()=>{voiceNotice.hidden=true;});voiceNotice.append(more,close);}};
  const post = async (action,body) => {
    const r=await fetch('/ExecutiveAgent/'+action,{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json','RequestVerificationToken':root.dataset.csrf},body:JSON.stringify(body)});
    if(!r.headers.get('content-type')?.includes('application/json'))throw new Error('Sign in again or check the server error.');
    const data=await r.json();if(!r.ok)throw new Error(data.error||`Request failed (${r.status})`);return data;
  };
  function resetMic(){mic.textContent='Start local microphone / الميكروفون';topMic?.classList.remove('is-listening');topMic?.setAttribute('aria-pressed','false');topMic?.setAttribute('aria-label','بدء الأوامر الصوتية');}
  function stopSpeech(discard=false){discarded=discard;recognizing?.abort();recognizing=null;if(recording?.state==='recording'){transcribing=!discard;recording.stop();}stream?.getTracks().forEach(t=>t.stop());stream=null;clearTimeout(speechTimer);cancelAnimationFrame(vadFrame);audioContext?.close().catch(()=>{});audioContext=null;resetMic();}
  function listeningState(){mic.textContent='Stop microphone / إيقاف';topMic?.classList.add('is-listening');topMic?.setAttribute('aria-pressed','true');topMic?.setAttribute('aria-label','إيقاف الميكروفون');}
  function silenceDetection(source){
    const Audio=window.AudioContext||window.webkitAudioContext;if(!Audio)return;
    audioContext=new Audio();audioContext.resume().catch(()=>{});const analyser=audioContext.createAnalyser();analyser.fftSize=2048;audioContext.createMediaStreamSource(source).connect(analyser);
    const samples=new Float32Array(analyser.fftSize);const started=performance.now();let voiced=0,lastVoice=started,previous=started;
    const frame=()=>{if(!audioContext||recording?.state!=='recording')return;analyser.getFloatTimeDomainData(samples);const now=performance.now();const rms=Math.sqrt(samples.reduce((sum,v)=>sum+v*v,0)/samples.length);if(rms>0.018){voiced+=Math.min(now-previous,100);lastVoice=now;}previous=now;if(voiced>250&&now-lastVoice>1400){stopSpeech();return;}if(voiced===0&&now-started>9000){stopSpeech(true);message('لم أسمع كلاماً. اضغط المايك وحاول مرة ثانية.',true);return;}vadFrame=requestAnimationFrame(frame);};frame();
  }
  let speechCapability;
  async function capabilities(){
    // The administrator-configured local Whisper recognizer is checked first; it does not depend on browser language packs.
    let serverReason='';
    try{
      const r=await fetch('/ExecutiveAgent/SpeechStatus',{credentials:'same-origin',cache:'no-store',redirect:'manual'});
      if(r.ok&&r.headers.get('content-type')?.includes('application/json')){const s=await r.json();if(s.available){speechCapability='server';speechStatus.textContent='Local Whisper speech recognizer is ready (Arabic and English). / التعرّف المحلي على الصوت جاهز.';return;}const missing=Object.entries(s.components||{}).filter(([,v])=>v!=='ready').map(([k,v])=>`${k}: ${v}`).join(', ');serverReason='local Whisper is not ready on the server'+(missing?` (${missing})`:'');}
      else serverReason=r.type==='opaqueredirect'?'your session expired; sign in again':`speech status check failed (HTTP ${r.status})`;
    }catch(e){serverReason=`speech status check failed (${e.message})`;}
    const R=window.SpeechRecognition||window.webkitSpeechRecognition;
    if(R?.available){try{const availability=await Promise.race([R.available({langs:[language.value],processLocally:true}),new Promise(done=>setTimeout(()=>done('timeout'),3000))]);if(availability==='available'){speechCapability='browser';speechStatus.textContent=`On-device speech is available for ${language.value}.`;return;}}catch{/* Text remains usable. */}}
    speechCapability=null;speechStatus.textContent=`Local speech is unavailable for ${language.value}: ${serverReason}. Type the command instead. No cloud fallback or model download is performed.`;
  }
  language.value=document.documentElement.lang.startsWith('ar')?'ar-JO':'en-US';
  language.addEventListener('change',()=>{stopSpeech();capabilities();});capabilities();
  async function listen(direct=false){
    if(starting)return;
    if(busy||transcribing){message('استنى لحد ما يخلص الأمر الحالي.');return;}
    if(recognizing||recording?.state==='recording'){stopSpeech();return;}
    handsFree=direct;discarded=false;starting=true;if(!direct)show();
    await capabilities();if(!speechCapability){starting=false;message(speechStatus.textContent,true);return;}
    if(!window.isSecureContext){starting=false;message('Open HTTPS or localhost to use the microphone.',true);return;}
    if(speechCapability==='browser'){
      const R=window.SpeechRecognition||window.webkitSpeechRecognition;recognizing=new R();recognizing.processLocally=true;recognizing.lang=language.value;recognizing.interimResults=false;recognizing.continuous=false;
      recognizing.onresult=e=>{const text=e.results[0][0].transcript;command.value=text;if(direct)runCommand(text,true);else message('النص جاهز للمراجعة.');};
      recognizing.onerror=e=>message(`Local speech failed: ${e.error}. Type your command instead.`,true);
      recognizing.onend=()=>{recognizing=null;resetMic();};
      try{recognizing.start();listeningState();message('سامعك… احكي اسم الصفحة.');}catch(e){stopSpeech(true);message(e.message,true);}finally{starting=false;}
      return;
    }
    try{
      stream=await navigator.mediaDevices.getUserMedia({audio:true});
      const chunks=[],recorder=new MediaRecorder(stream);recording=recorder;recorder.ondataavailable=e=>{if(e.data.size)chunks.push(e.data);};
      recorder.onstop=async()=>{
        stream?.getTracks().forEach(t=>t.stop());stream=null;
        if(discarded)return;transcribing=true;
        const body=new FormData();body.append('audio',new Blob(chunks,{type:recorder.mimeType}),'command.webm');body.append('language',language.value.startsWith('ar')?'ar':'en');
        message('Transcribing locally…');
        try{const r=await fetch('/ExecutiveAgent/Transcribe',{method:'POST',credentials:'same-origin',headers:{RequestVerificationToken:root.dataset.csrf},body});const data=await r.json();if(!r.ok)throw new Error(data.error||'Transcription failed');command.value=data.text;transcribing=false;if(direct)await runCommand(data.text,true);else message('النص جاهز للمراجعة.');}catch(e){message(e.message,true);}finally{transcribing=false;}
      };
      recording.start();speechTimer=setTimeout(()=>stopSpeech(),30000);silenceDetection(stream);listeningState();message('سامعك… احكي أمرك وسأتوقف بعد ما تخلص. اضغط المايك مرة ثانية للإيقاف اليدوي.');
    }catch(e){stopSpeech(true);message(`Microphone unavailable: ${e.message}. Type your command instead.`,true);}finally{starting=false;}
  }
  mic.addEventListener('click',()=>listen(false));
  // Voice commands run in the Orange AI chat (one assistant for pages, data, files and prepared actions) when it is on the page.
  if(topMic)topMic.addEventListener('click',event=>{if(target.value!=='agent')return;event.preventDefault();event.stopImmediatePropagation();const topLanguage=document.querySelector('[data-voice-language]');if(topLanguage)language.value=topLanguage.value;if(window.OrangeAI){window.OrangeAI.voice(language.value);return;}listen(true);},true);
  function navigateTo(url){if(typeof url!=='string'||!url.startsWith('/')||url.startsWith('//'))throw new Error('تم رفض رابط غير مسموح.');const next=new URL(url,location.origin);if(next.origin!==location.origin)throw new Error('تم رفض رابط خارج الموقع.');location.assign(next.href);}
  function offerPages(choices){for(const page of choices||[]){const button=document.createElement('button');button.type='button';button.textContent=page.label;button.addEventListener('click',async()=>{try{const r=await post('Navigate',{pageId:page.id,area:root.dataset.area});if(r.state==='Navigate')navigateTo(r.url);else message(r.message,true);}catch(e){message(e.message,true);}});voiceNotice.append(button);}}
  function link(url,text,parent){if(!url?.startsWith('/')||url.startsWith('//'))return;const a=document.createElement('a');a.href=url;a.textContent=text;a.target='_blank';a.rel='noopener';parent.append(a);}
  function displayResult(data){
    message(data.summary||data.error||'Command processed. Review authoritative steps and pending actions.');
    for(const entry of data.results||[]){const r=entry.result;if(entry.tool==='open_report')link(r.pdfUrl,'Open current report PDF',actions);if(entry.tool==='prepare_action')renderAction(r);if(entry.tool==='draft_tender')renderDraft(r);if(entry.tool==='navigate_page'){if(r.state==='Navigate')link(r.url,'افتح '+(r.choices?.[0]?.label||'الصفحة'),actions);else message(r.message,true);}if(entry.tool==='list_pages'&&Array.isArray(r))for(const page of r)link(page.url,page.label+' · '+page.area,actions);}
  }
  function renderDraft(r){
    if(!r)return;const card=document.createElement('article');const title=document.createElement('strong');
    title.textContent=r.state==='FormReady'?`New tender draft · ${r.purchaseRequest.reference} · ${r.purchaseRequest.title}`:'New tender / عطاء جديد';card.append(title);
    const p=document.createElement('p');p.textContent=r.message;card.append(p);
    if(r.state==='ChooseRequest'){const ul=document.createElement('ul');for(const c of r.candidates||[]){const li=document.createElement('li');li.textContent=`${c.reference} · ${c.title} (${c.department})`;ul.append(li);}card.append(ul);}
    if(r.state==='FormReady'&&r.formUrl?.startsWith('/SourcingOfficer/Workflow?')){const a=document.createElement('a');a.href=r.formUrl;a.className='agent-primary-link';a.textContent='Open the prefilled tender form / افتح نموذج العطاء';card.append(a);}
    actions.append(card);
  }
  function renderSteps(data){
    steps.replaceChildren();
    for(const s of data.steps||[]){const li=document.createElement('li');let evidence={};try{evidence=JSON.parse(s.evidence||'{}');}catch{}li.textContent=`${s.action}: ${JSON.stringify(evidence)}`;steps.append(li);}
  }
  const rendered = new Set();
  function renderAction(a){
    const id=a.actionId||a.id;if(rendered.has(id))return;rendered.add(id);
    const card=document.createElement('article');const title=document.createElement('strong');title.textContent=`${a.operation} · Tender ${a.tenderId} · ${a.state}`;card.append(title);
    const details=document.createElement('p');details.textContent=`${a.decision||''} ${a.comment||''} — Expires: ${a.expiresAt}`;card.append(details);
    if(a.pdfUrl||a.reportId)link(a.pdfUrl||`/ExecutiveAgent/Pdf/${a.reportId}`,'Review exact report PDF before signing',card);
    const hash=document.createElement('p');hash.textContent=`Reviewed state: ${a.snapshotHash}${a.documentHash?' · PDF: '+a.documentHash:''}`;card.append(hash);
    if(a.state!=='Pending'){actions.append(card);return;}
    const label=document.createElement('label'), checkbox=document.createElement('input');checkbox.type='checkbox';label.append(checkbox,document.createTextNode(' I reviewed this exact action and report, and explicitly confirm it. / أؤكد هذا الإجراء بعد مراجعته.'));card.append(label);
    const result=document.createElement('p');result.setAttribute('role','status');
    const secure=document.createElement('button');secure.type='button';secure.textContent='Confirm with device passkey / تأكيد بمفتاح المرور';secure.disabled=true;checkbox.addEventListener('change',()=>{secure.disabled=!checkbox.checked;});
    secure.addEventListener('click',async()=>{
      secure.disabled=true;try{
        const challenge=await post('Challenge',{actionId:id});
        let options=challenge.options;
        if(PublicKeyCredential.parseRequestOptionsFromJSON)options=PublicKeyCredential.parseRequestOptionsFromJSON(options);
        else{options.challenge=decode(options.challenge);options.allowCredentials=(options.allowCredentials||[]).map(c=>({...c,id:decode(c.id)}));}
        const credential=await navigator.credentials.get({publicKey:options});
        if(!credential)throw new Error('No device identity proof returned.');
        const json=credential.toJSON?credential.toJSON():{id:credential.id,rawId:encode(credential.rawId),type:credential.type,response:{clientDataJSON:encode(credential.response.clientDataJSON),authenticatorData:encode(credential.response.authenticatorData),signature:encode(credential.response.signature),userHandle:credential.response.userHandle?encode(credential.response.userHandle):null},clientExtensionResults:credential.getClientExtensionResults()};
        const r=await post('Confirm',{actionId:id,confirmed:true,credentialJson:JSON.stringify(json)});result.textContent=`${r.state}${r.duplicate?' (already executed)':''}`;
      }catch(e){result.textContent=e.message;secure.disabled=!checkbox.checked;}
    });card.append(secure);
    if(a.identity==='password-or-passkey'||['request_weights','forward_report','forward_decision'].includes(a.operation)){
      const pwLabel=document.createElement('label');pwLabel.textContent='Or verify your existing password for this forwarding/request action';const pw=document.createElement('input');pw.type='password';pw.autocomplete='current-password';pwLabel.append(pw);card.append(pwLabel);
      const button=document.createElement('button');button.type='button';button.textContent='Confirm with existing identity password';button.addEventListener('click',async()=>{if(!checkbox.checked){result.textContent='Explicit confirmation is required.';return;}button.disabled=true;const password=pw.value;pw.value='';try{const r=await post('Confirm',{actionId:id,confirmed:true,password});result.textContent=r.state;}catch(e){result.textContent=e.message;button.disabled=false;}});card.append(button);
    }
    card.append(result);actions.append(card);
  }
  function decode(s){return Uint8Array.from(atob(s.replace(/-/g,'+').replace(/_/g,'/').padEnd(Math.ceil(s.length/4)*4,'=')),c=>c.charCodeAt(0)).buffer;}
  function encode(buffer){return btoa(String.fromCharCode(...new Uint8Array(buffer))).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,'');}
  async function runCommand(text,autoNavigate=false){
    if(busy)return;stopSpeech(true);busy=true;runButton.disabled=true;actions.replaceChildren();steps.replaceChildren();rendered.clear();
    const requestId=crypto.randomUUID();let requestedModel=false;message('فهم الأمر وتحديد الصفحة المسموحة…');
    const progress=async()=>{try{const r=await fetch('/ExecutiveAgent/Progress?id='+requestId,{cache:'no-store',credentials:'same-origin'});if(!r.ok)return;const data=await r.json();renderSteps(data);for(const a of data.actions||[])renderAction(a);}catch{/* final response reports connection errors */}};
    try{
      const route=await post('Navigate',{command:text,area:root.dataset.area});
      if(route.state==='Navigate'){message(route.message);navigateTo(route.url);return;}
      if(route.state==='Choose'){message(route.message);offerPages(route.choices);return;}
      requestedModel=true;polling=setInterval(progress,1200);const data=await post('Run',{requestId,command:text});displayResult(data);
      if(autoNavigate){
        const pending=(data.results||[]).some(e=>e.tool==='prepare_action'&&e.result.state==='Pending');
        const destinations=[...new Set((data.results||[]).flatMap(e=>e.tool==='navigate_page'&&e.result.state==='Navigate'?[e.result.url]:e.tool==='open_report'&&e.result.pdfUrl?[e.result.pdfUrl]:e.tool==='draft_tender'&&e.result.state==='FormReady'?[e.result.formUrl]:[]))];
        if(!pending&&destinations.length===1)navigateTo(destinations[0]);
        else if(pending)message('جهزت الإجراء للمراجعة فقط. اضغط «التفاصيل» لتأكيده والتحقق من هويتك.');
        else if(destinations.length>1)message('الأمر يطلب أكثر من صفحة. افتح التفاصيل لاختيار الوجهة.');
      }
    }catch(error){message(error.message,true);}finally{clearInterval(polling);if(requestedModel)await progress();busy=false;runButton.disabled=false;}
  }
  root.querySelector('[data-agent-form]').addEventListener('submit',e=>{e.preventDefault();runCommand(command.value,false);});
  window.addEventListener('pagehide',()=>{stopSpeech(true);clearInterval(polling);});
})();

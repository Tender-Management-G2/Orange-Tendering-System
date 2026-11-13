"use strict";
(() => {
  const body = document.querySelector('.ws-shell');
  if (!body) return;
  body.classList.add('ws-enhanced');
  const root = document.documentElement;
  const key = 'orange-ui-theme';
  const system = matchMedia('(prefers-color-scheme: light)');
  const toggle = body.querySelector('.ws-theme-toggle');
  let explicit = false;
  try { explicit = ['dark','light'].includes(localStorage.getItem(key)); } catch { /* OS preference remains available. */ }
  function apply(theme, announce = false) {
    root.dataset.theme = theme; root.style.colorScheme = theme;
    const light = theme === 'light';
    toggle.setAttribute('aria-pressed', String(light));
    toggle.setAttribute('aria-label', `Switch to ${light?'dark':'light'} mode`);
    toggle.title = light?'Dark mode':'Light mode';
    document.querySelector('meta[name="theme-color"]').content = light?'#f3f4f3':'#101010';
    if(announce) document.querySelector('#workspace-theme-status').textContent = `${light?'Light':'Dark'} mode enabled`;
  }
  apply(root.dataset.theme === 'light'?'light':'dark');
  toggle.addEventListener('click',()=>{
    const theme=root.dataset.theme==='light'?'dark':'light';explicit=true;
    try { localStorage.setItem(key,theme); } catch { /* Switching still works without storage. */ }
    apply(theme,true);
  });
  system.addEventListener('change',event=>{if(!explicit)apply(event.matches?'light':'dark');});
  addEventListener('storage',event=>{if(event.key===key||event.key===null){let saved=null;try{saved=localStorage.getItem(key);}catch{}explicit=['light','dark'].includes(saved);apply(explicit?saved:system.matches?'light':'dark');}});

  const sidebar=body.querySelector('.ws-sidebar');
  if(sidebar){
    const openButton=body.querySelector('.ws-nav-open');
    const closeButton=body.querySelector('.ws-nav-close');
    const backdrop=body.querySelector('.ws-backdrop');
    const stage=body.querySelector('.ws-stage');
    const compact=matchMedia('(max-width: 1079px)');
    let open=false;
    function close(restoreFocus=true){
      open=false;body.classList.remove('ws-nav-is-open');stage.inert=false;
      backdrop.hidden=true;openButton.setAttribute('aria-expanded','false');
      sidebar.inert=compact.matches;
      if(restoreFocus&&compact.matches)openButton.focus();
    }
    openButton.addEventListener('click',()=>{
      if(!compact.matches)return;
      open=true;sidebar.inert=false;stage.inert=true;body.classList.add('ws-nav-is-open');
      backdrop.hidden=false;openButton.setAttribute('aria-expanded','true');closeButton.focus();
    });
    closeButton.addEventListener('click',()=>close());backdrop.addEventListener('click',()=>close());
    sidebar.querySelectorAll('a').forEach(link=>link.addEventListener('click',()=>close(false)));
    compact.addEventListener('change',()=>close(false));
    document.addEventListener('keydown',event=>{
      if(!open)return;
      if(event.key==='Escape'){event.preventDefault();close();return;}
      if(event.key==='Tab'){
        const items=[...sidebar.querySelectorAll('a[href],button:not(:disabled),summary,[tabindex="0"]')].filter(node=>node.getClientRects().length);
        const first=items[0],last=items.at(-1);
        if(event.shiftKey&&document.activeElement===first){event.preventDefault();last.focus();}
        else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first.focus();}
      }
    });
    close(false);
  }
  const supplierNav=body.querySelector('#supplier-navigation');
  if(supplierNav){
    const button=body.querySelector('.ws-supplier-menu');
    const compact=matchMedia('(max-width: 767px)');
    function close(){supplierNav.hidden=compact.matches;button.setAttribute('aria-expanded','false');}
    button.addEventListener('click',()=>{const open=supplierNav.hidden;supplierNav.hidden=!open;button.setAttribute('aria-expanded',String(open));});
    compact.addEventListener('change',close);
    document.addEventListener('keydown',event=>{if(event.key==='Escape'&&!supplierNav.hidden&&compact.matches){close();button.focus();}});
    close();
  }
  const account=body.querySelector('.ws-account');
  document.addEventListener('click',event=>{if(account&&!account.contains(event.target))account.open=false;});
  document.addEventListener('keydown',event=>{if(event.key==='Escape'&&account?.open){account.open=false;account.querySelector('summary').focus();}});

  // The shared header search filters records on every workspace page.
  const searches=[...body.querySelectorAll('input[type="search"]')];
  const searchStatus=document.querySelector('#workspace-search-status');
  if(searches.length){
    const candidates=[...document.querySelectorAll('.pg-table tbody tr, .pg-card, .pg-links li, article.ws-panel, .dash-activity-item, .dash-task-item, [data-search-item]')];
    function filterPage(){
      const terms=searches.map(input=>input.value.trim().toLocaleLowerCase()).filter(Boolean);
      const filters=[...document.querySelectorAll('.pg-toolbar select')].map(select=>select.value.trim().toLocaleLowerCase()).filter(value=>value&&!/^(all|select|status: all|period: this month)$/.test(value));
      let visible=0;
      for(const item of candidates){
        const content=item.innerText.toLocaleLowerCase();
        const match=terms.every(term=>content.includes(term))&&filters.every(filter=>content.includes(filter.replace(/^[^:]+:\s*/,'')));
        item.hidden=!match;if(match)visible++;
      }
      if(searchStatus)searchStatus.textContent=terms.length||filters.length?`${visible} matching records on this page.`:'Page search cleared.';
    }
    searches.forEach(input=>input.addEventListener('input',filterPage));
    document.querySelectorAll('.pg-toolbar select').forEach(select=>select.addEventListener('change',filterPage));
    const VoiceRecognition=window.SpeechRecognition||window.webkitSpeechRecognition;
    const voice=body.querySelector('.ws-voice-search');
    if(voice){
      if(!VoiceRecognition){voice.title='Voice search is not supported in this browser';voice.setAttribute('aria-label','Voice search is not supported in this browser');}
      else{
        const recognition=new VoiceRecognition();recognition.lang='en-US';recognition.interimResults=false;recognition.maxAlternatives=1;
        let listening=false;
        recognition.addEventListener('start',()=>{listening=true;voice.classList.add('is-listening');voice.setAttribute('aria-label','Stop voice search');if(searchStatus)searchStatus.textContent='Listening. Say what you want to find.';});
        recognition.addEventListener('end',()=>{listening=false;voice.classList.remove('is-listening');voice.setAttribute('aria-label','Start voice search');});
        recognition.addEventListener('result',event=>{const phrase=event.results[0][0].transcript;const search=searches.find(input=>input.hasAttribute('data-workspace-search'))||searches[0];search.value=phrase;search.dispatchEvent(new Event('input',{bubbles:true}));});
        recognition.addEventListener('error',event=>{if(searchStatus)searchStatus.textContent=event.error==='not-allowed'?'Allow microphone access to use voice search.':'Voice search could not hear that. Try again or type your search.';});
        voice.addEventListener('click',()=>{if(listening)recognition.stop();else{try{recognition.start();}catch{if(searchStatus)searchStatus.textContent='Voice search is already starting.';}}});
      }
    }
  }
})();

const showcase = document.querySelector('.editorial-team');
if (showcase) {
  const stage = showcase.querySelector('.editorial-team-stage');
  const viewport = showcase.querySelector('.editorial-viewport');
  const track = showcase.querySelector('.editorial-track');
  const members = [...showcase.querySelectorAll('.editorial-member')];
  const buttons = [...showcase.querySelectorAll('[data-team-index]')];
  const previous = showcase.querySelector('[data-team-prev]');
  const next = showcase.querySelector('[data-team-next]');
  const count = showcase.querySelector('.editorial-team-count');
  const announcement = showcase.querySelector('#team-announcement');
  const desktop = matchMedia('(min-width: 1024px) and (min-height: 700px) and (hover: hover)');
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  const vertical = matchMedia('(max-width: 767px)');
  let current = 0, cinematic = null, generation = 0, near = false, disposed = false;
  showcase.classList.add('team-controls-enabled');
  function select(index) {
    current = Math.max(0, Math.min(members.length - 1, index));
    buttons.forEach((button, i) => {
      if(i === current) button.setAttribute('aria-current','step'); else button.removeAttribute('aria-current');
    });
    previous.disabled = current === 0; next.disabled = current === members.length - 1;
    count.textContent = `${String(current + 1).padStart(2,'0')} / ${String(members.length).padStart(2,'0')}`;
    members.forEach((member,i) => {
      member.classList.toggle('is-current', i === current);
      if(cinematic) { member.inert = i !== current; member.setAttribute('aria-hidden',String(i !== current)); }
    });
  }
  function navigate(index, announce = true) {
    index = Math.max(0, Math.min(members.length - 1, index));
    if(cinematic) {
      const trigger = cinematic.trigger;
      window.scrollTo({ top: trigger.start + index/(members.length-1)*(trigger.end-trigger.start), behavior:'instant' });
    } else if(vertical.matches) {
      members[index].scrollIntoView({behavior: reduced.matches?'instant':'smooth',block:'start'});
    } else {
      viewport.scrollTo({left: members[index].offsetLeft,behavior: reduced.matches?'instant':'smooth'});
    }
    select(index);
    if(announce)announcement.textContent = `${index+1} of ${members.length}: ${members[index].querySelector('h2').textContent}`;
  }
  buttons.forEach((button,index) => button.addEventListener('click',()=>navigate(index)));
  previous.addEventListener('click',()=>navigate(current-1));next.addEventListener('click',()=>navigate(current+1));
  showcase.addEventListener('keydown',event=>{
    if(event.key==='ArrowRight'||event.key==='ArrowLeft'||event.key==='Home'||event.key==='End') {
      event.preventDefault();
      navigate(event.key==='Home'?0:event.key==='End'?members.length-1:current+(event.key==='ArrowRight'?1:-1));
    }
  });
  document.querySelectorAll('[data-team-jump]').forEach(link=>link.addEventListener('click',event=>{
    event.preventDefault();
    const index=Number(link.dataset.teamJump);
    if(!cinematic&&!vertical.matches)showcase.scrollIntoView({behavior:'instant',block:'start'});
    navigate(index);
    buttons[index].focus({preventScroll:true});
  }));
  function focusProfile(event) {
    const member=event.target.closest('.editorial-member');
    if(member&&!cinematic)navigate(members.indexOf(member),false);
  }
  showcase.addEventListener('focusin',focusProfile);
  // Observe real profile visibility for touch, keyboard and no-pin presentations.
  const observer=new IntersectionObserver(entries=>{
    if(cinematic)return;
    const visible=entries.filter(e=>e.isIntersecting&&e.intersectionRatio>.5).sort((a,b)=>b.intersectionRatio-a.intersectionRatio)[0];
    if(visible)select(members.indexOf(visible.target));
  },{threshold:[.5,.65,.85]});
  members.forEach(member=>observer.observe(member));
  document.querySelectorAll('.editorial-portrait img').forEach(image=>{
    image.addEventListener('error',()=>{image.hidden=true;image.previousElementSibling.hidden=false;},{once:true});
  });
  let libraryPromise;
  function script(path) { return new Promise((resolve,reject)=>{const node=document.createElement('script');node.src=new URL(path,import.meta.url).href;node.onload=resolve;node.onerror=reject;document.head.append(node);}); }
  async function libraries(){return libraryPromise ||= (async()=>{if(!window.gsap)await script('./vendor/gsap.min.js');if(!window.ScrollTrigger)await script('./vendor/ScrollTrigger.min.js');})().catch(error=>{libraryPromise=null;throw error;});}
  async function enhance(){
    const version=++generation;
    cinematic?.destroy();cinematic=null;
    if(disposed||!near||!desktop.matches||reduced.matches||navigator.connection?.saveData||members.length<2)return;
    try {
      await libraries();
      if(version!==generation||disposed)return;
      const {gsap,ScrollTrigger}=window;gsap.registerPlugin(ScrollTrigger);
      let oldBehavior,overridden=false;
      const before=()=>{if(!overridden){oldBehavior=document.documentElement.style.scrollBehavior;document.documentElement.style.scrollBehavior='auto';overridden=true;}};
      const after=()=>{if(overridden){document.documentElement.style.scrollBehavior=oldBehavior;overridden=false;}};
      before();ScrollTrigger.addEventListener('refreshInit',before);ScrollTrigger.addEventListener('refresh',after);
      showcase.classList.add('team-scroll-presentation');viewport.scrollLeft=0;
      const tween=gsap.to(track,{xPercent:-100*(members.length-1),ease:'none',scrollTrigger:{id:'editorial-team',trigger:showcase,pin:stage,start:'top top',end:()=>`+=${Math.min((members.length-1)*850,4200)}`,scrub:.65,invalidateOnRefresh:true,onUpdate:self=>select(Math.round(self.progress*(members.length-1)))}});
      const trigger=tween.scrollTrigger;
      cinematic={trigger,destroy(){tween.kill();trigger.kill(true);ScrollTrigger.removeEventListener('refreshInit',before);ScrollTrigger.removeEventListener('refresh',after);after();showcase.classList.remove('team-scroll-presentation');track.removeAttribute('style');members.forEach(member=>{member.inert=false;member.removeAttribute('aria-hidden');});}};
      select(current);ScrollTrigger.refresh();after();
    } catch {
      // The complete, server-rendered editorial showcase remains usable without GSAP.
      showcase.dataset.animation='native';
    }
  }
  const lazy=new IntersectionObserver(entries=>{if(entries.some(entry=>entry.isIntersecting)){near=true;lazy.disconnect();enhance();}},{rootMargin:'900px'});
  lazy.observe(showcase);desktop.addEventListener('change',enhance);reduced.addEventListener('change',enhance);
  vertical.addEventListener('change',()=>{viewport.scrollLeft=0;});
  select(0);
  addEventListener('pagehide',event=>{if(!event.persisted){disposed=true;generation++;cinematic?.destroy();observer.disconnect();lazy.disconnect();desktop.removeEventListener('change',enhance);reduced.removeEventListener('change',enhance);}});
}

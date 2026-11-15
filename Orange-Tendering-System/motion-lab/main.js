import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { PaperJourney } from './paper.js';
import { SignalJourney } from './signal.js';

const $ = id => document.getElementById(id);
const viewport=$('viewport'), canvas=$('scene');
const allCategories=[...$('category').options].map(o=>o.textContent);
const shots={
  paper:[
    {id:'R00',name:'First line',start:0,end:.14,detail:'The blank field receives one fine line, drawing a paper letter automatically.'},
    {id:'R01',name:'Card',start:.14,end:.25,detail:'Contact ink develops on the same connected card cut from the sheet.'},
    {id:'R02',name:'Letter fold',start:.25,end:.29,detail:'A connected envelope fold draws after the local email cue; no email is verified.'},
    {id:'R03',name:'Sleeve',start:.29,end:.39,detail:'Bottom, side, and closure folds wrap the card on separate hinges.'},
    {id:'R04',name:'Travel',start:.39,end:.535,detail:'The camera pulls from the card and descends toward the flat architectural cut lines.'},
    {id:'R05',name:'Building',start:.53,end:.765,detail:'Walls rise on connected creases; the attached roof closes after the walls clear.'},
    {id:'R06',name:'Category',start:.7,end:.83,detail:'A supplier kit rises on local creases while the foundation and marker remain fixed.'},
    {id:'R08',name:'Iris leaf',start:.825,end:.897,detail:'A die-cut paper leaf rises from a fixed dossier crease.'},
    {id:'R09',name:'Aperture',start:.895,end:1,detail:'Eight curved blades open around pinned pivots. The optical well is a fictional study; no camera or verification runs.'}
  ],
  signal:[
    {id:'L00',name:'First signal',start:0,end:.12,detail:'An empty ivory field receives one Orange line that traces the module and its fiber.'},
    {id:'L01',name:'Traces',start:.12,end:.32,detail:'Fine copper routes grow toward the core. Fictional local input state only.'},
    {id:'L02',name:'Contacts',start:.26,end:.55,detail:'The contact plate travels on mechanical rails into the same module.'},
    {id:'L03',name:'Assembly',start:.55,end:.78,detail:'Only the explicit supplier demo gate develops the sealed module into depth. No credential result is claimed.'},
    {id:'L04',name:'Optical iris',start:.78,end:.94,detail:'The optical aperture opens toward an abstract lens. No camera or face data is used.'},
    {id:'L06',name:'Fiber signal',start:.94,end:1,detail:'A separately triggered post-auth storyboard sends one pulse along the cable. No access is granted.'}
  ]
};
const clamp=n=>Math.max(0,Math.min(1,n));
const ease=n=>{n=clamp(n);return n*n*(3-2*n)};
let mode='paper',progress=0,playing=false,playDirection=1,segment=null,lastTime=0,playbackRate=1;
let glide=null,irisPreview=false,loginStage='credentials';
let selectedCategory='IT & Electronics',categoryMix=1,categorySwap=null;
let userOrbit=false,reducedMotion=matchMedia('(prefers-reduced-motion: reduce)').matches,wireframe=false;
let lightingRig='studio',materialMode='authored',shadows=true;
let frameCounter=0,frameSince=performance.now();
let renderer,controls,camera,paper,signal,paperScene,signalScene;

function makeScene(color,lightColor,dark=false){
  const scene=new THREE.Scene();scene.background=new THREE.Color(color);
  const ambient=new THREE.AmbientLight(lightColor,dark?.42:.75);scene.add(ambient);
  const key=new THREE.DirectionalLight(0xffffff,dark?1.55:2.25);key.position.set(-3,5,8);key.castShadow=true;
  key.shadow.mapSize.set(1024,1024);key.shadow.camera.left=-7;key.shadow.camera.right=7;key.shadow.camera.top=7;key.shadow.camera.bottom=-7;
  key.shadow.bias=-.0005;key.shadow.radius=4;scene.add(key);
  const fill=new THREE.DirectionalLight(lightColor,dark?.32:.65);fill.position.set(5,-5,3);scene.add(fill);
  scene.userData.lights={ambient,key,fill,dark};
  return scene;
}
function makeGround(scene,color){
  const material=new THREE.ShadowMaterial({color:0x775f47,opacity:scene===paperScene ? .2 : .22});
  const ground=new THREE.Mesh(new THREE.PlaneGeometry(14,11),material);ground.name='studioGround';ground.position.z=-.18;ground.receiveShadow=true;scene.add(ground);
}
const clayPaper=new THREE.MeshStandardMaterial({color:0xd4c8b7,roughness:.92,side:THREE.DoubleSide});
const claySignal=new THREE.MeshStandardMaterial({color:0x5b554e,metalness:.18,roughness:.58,side:THREE.DoubleSide});
function setLightingRig(name){
  lightingRig=name;
  for(const scene of [paperScene,signalScene]){
    const {ambient,key,fill,dark}=scene.userData.lights;
    if(name==='raking'){
      ambient.intensity=dark?.18:.28;key.intensity=dark?2.2:3.0;fill.intensity=dark?.13:.18;
      key.position.set(-6,-3,3.2);fill.position.set(4,5,4);
    }else if(name==='flat'){
      ambient.intensity=dark?.9:1.28;key.intensity=dark?.52:.8;fill.intensity=dark?.45:.7;
      key.position.set(-3,5,8);fill.position.set(5,-5,3);
    }else{
      ambient.intensity=dark?.42:.75;key.intensity=dark?1.55:2.25;fill.intensity=dark?.32:.65;
      key.position.set(-3,5,8);fill.position.set(5,-5,3);
    }
  }
}
function setMaterialMode(name){
  materialMode=name;
  for(const [scene,clay] of [[paperScene,clayPaper],[signalScene,claySignal]])scene.traverse(o=>{
    if(!o.isMesh||o.name==='studioGround')return;
    if(!o.userData.authoredMaterial)o.userData.authoredMaterial=o.material;
    o.material=name==='clay'?clay:o.userData.authoredMaterial;
    const materials=Array.isArray(o.material)?o.material:[o.material];for(const m of materials)m.wireframe=wireframe;
  });
}
function setShadows(value){
  shadows=value;renderer.shadowMap.enabled=value;renderer.shadowMap.needsUpdate=true;
  for(const scene of [paperScene,signalScene])scene.traverse(o=>{if(o.isMesh){const materials=Array.isArray(o.material)?o.material:[o.material];for(const m of materials)m.needsUpdate=true;}});
}
function initialize(){
  try{
    renderer=new THREE.WebGLRenderer({canvas,antialias:true,alpha:false,powerPreference:'high-performance'});
    renderer.setPixelRatio(Math.min(devicePixelRatio,1.75));renderer.shadowMap.enabled=true;renderer.shadowMap.type=THREE.PCFSoftShadowMap;
    renderer.outputColorSpace=THREE.SRGBColorSpace;renderer.toneMapping=THREE.ACESFilmicToneMapping;renderer.toneMappingExposure=1.38;
  }catch(error){$('webgl-fallback').hidden=false;console.error('WebGL initialization failed',error);return false;}
  paperScene=makeScene(0xf8f5ef,0xfff3df);signalScene=makeScene(0xf8f5ef,0xffead7,true);
  makeGround(paperScene,0xf8f5ef);makeGround(signalScene,0xf8f5ef);
  paper=new PaperJourney(paperScene);signal=new SignalJourney(signalScene);
  camera=new THREE.PerspectiveCamera(35,1,.1,100);camera.up.set(0,1,0);
  controls=new OrbitControls(camera,canvas);controls.enabled=false;controls.enableDamping=true;controls.dampingFactor=.08;controls.minZoom=.5;controls.maxZoom=5;
  controls.target.set(0,0,0);controls.update();
  new ResizeObserver(resize).observe(viewport);resize();return true;
}
function resize(){
  if(!renderer)return;
  const w=viewport.clientWidth,h=viewport.clientHeight,aspect=w/h;
  camera.aspect=aspect;camera.updateProjectionMatrix();
  renderer.setSize(w,h,false);
}
function authoredCamera(){
  if(userOrbit)return;
  if(mode==='signal'){
    const poses=[
      {p:0,pos:[0,0,9.5],target:[0,0,0],zoom:1},
      {p:.12,pos:[0,0,9.5],target:[0,0,0],zoom:1},
      {p:.32,pos:[-.85,.2,8.2],target:[-.7,.08,.1],zoom:1.08},
      {p:.55,pos:[-1.05,-.5,7.5],target:[-.25,0,.12],zoom:1.0},
      {p:.78,pos:[2.85,-4.6,7.25],target:[0,0,.19],zoom:1.08},
      {p:.94,pos:[2.1,-3.1,5.8],target:[.10,0,.26],zoom:1.14},
      {p:1,pos:[3.15,-3.95,6.3],target:[.65,-.35,.20],zoom:1.05}
    ];
    let index=0;while(index<poses.length-2&&progress>poses[index+1].p)index++;
    const a=poses[index],b=poses[index+1],t=ease((progress-a.p)/(b.p-a.p));
    camera.position.lerpVectors(new THREE.Vector3(...a.pos),new THREE.Vector3(...b.pos),t);
    camera.up.set(0,1,0);camera.lookAt(new THREE.Vector3(...a.target).lerp(new THREE.Vector3(...b.target),t));
    camera.zoom=THREE.MathUtils.lerp(a.zoom,b.zoom,t)*(viewport.clientWidth<600?.73:1);camera.updateProjectionMatrix();return;
  }
  const poses=[
    {p:0,pos:[-1.8,1.7,6.2],target:[-1.82,1.62,.02],up:[0,1,0],zoom:2.04},
    {p:.10,pos:[-1.8,1.7,6.2],target:[-1.82,1.62,.02],up:[0,1,0],zoom:2.04},
    {p:.18,pos:[-1.75,-.05,7.3],target:[-1.82,1.57,.06],up:[0,1,0],zoom:1.7},
    {p:.29,pos:[-2.2,-3.35,6.0],target:[-1.82,1.6,.09],up:[0,.82,.58],zoom:1.65},
    {p:.37,pos:[-2.05,-3.0,6.0],target:[-1.82,1.62,.08],up:[0,.84,.54],zoom:1.55},
    {p:.47,pos:[.6,-4.6,7.2],target:[.55,0,.12],up:[0,.72,.69],zoom:1.05},
    {p:.58,pos:[2.7,-6.0,6.4],target:[.55,.05,.28],up:[0,.36,.93],zoom:.86},
    {p:.73,pos:[3.1,-5.1,5.1],target:[.53,0,1.1],up:[0,.18,.98],zoom:.84},
    {p:.82,pos:[1.95,-5.3,4.7],target:[.55,-.03,.64],up:[0,.14,.99],zoom:.87},
    {p:.91,pos:[4.15,-4.7,1.48],target:[2.54,-.64,.54],up:[0,.035,1],zoom:1.70},
    {p:1,pos:[3.9,-4.2,.65],target:[2.60,-.64,.54],up:[0,0,1],zoom:2.00}
  ];
  let index=0;while(index<poses.length-2&&progress>poses[index+1].p)index++;
  const a=poses[index],b=poses[index+1],t=ease((progress-a.p)/(b.p-a.p));
  const mix=(key)=>new THREE.Vector3(...a[key]).lerp(new THREE.Vector3(...b[key]),t);
  camera.position.copy(mix('pos'));camera.up.copy(mix('up')).normalize();
  camera.lookAt(mix('target'));camera.zoom=THREE.MathUtils.lerp(a.zoom,b.zoom,t)*(viewport.clientWidth<600?.74:1);camera.updateProjectionMatrix();
}
function setProgress(value,{fromUI=false}={}){
  progress=clamp(value);$('timeline').value=(progress*100).toFixed(1);$('progress-value').textContent=`${Math.round(progress*100)}%`;
  paper.update(mode==='paper'?progress:0,selectedCategory,categoryMix);
  signal.update(mode==='signal'?progress:0);
  if(!userOrbit)authoredCamera();
  updateStage();
  if(fromUI){playing=false;segment=null;glide=null;updatePlayButton();}
}
function glideTo(target,duration=950){
  target=clamp(target);playing=false;segment=null;updatePlayButton();
  if(reducedMotion){glide=null;setProgress(target);return;}
  glide={from:progress,to:target,start:performance.now(),duration};
}
function updateStage(){
  const list=shots[mode],shot=list.reduce((active,next)=>progress>=next.start?next:active,list[0]);
  $('stage-state').textContent=`${shot.id} · ${shot.name.toUpperCase()}`;
  $('annotation-text').textContent=shot.detail;
  for(const button of $('shots').children)button.classList.toggle('active',button.dataset.shot===shot.id);
  $('method-section').hidden=mode!=='signal';
  $('face-method').disabled=mode!=='signal'||loginStage!=='supplier';
  $('signal-pulse-preview').disabled=mode!=='signal'||loginStage!=='face';
}
function updatePlayButton(){$('play').textContent=playing?'Ⅱ Pause':'▶ Play';}
function setMode(next){
  mode=next;document.body.dataset.journey=mode;glide=null;loginStage='credentials';irisPreview=false;
  $('paper-tab').setAttribute('aria-selected',String(mode==='paper'));
  $('signal-tab').setAttribute('aria-selected',String(mode==='signal'));
  $('journey-kicker').textContent=mode==='paper'?'01 / PAPER GENESIS':'02 / ORANGE SIGNAL';
  $('journey-title').textContent=mode==='paper'?'An identity built from one sheet.':'A signal built into one device.';
  $('journey-description').textContent=mode==='paper'?
    'A continuous paper object becomes a miniature company, then opens a mechanical paper iris.':
    'A planar SIM drawing becomes one precision hardware assembly with contact rails, a fiber cable, and a mechanical optical aperture.';
  $('stage-material').textContent=mode==='paper'?'MATTE PAPER / INK / ORANGE MARK':'SATIN GRAPHITE / COPPER / OPTICS';
  $('category-section').hidden=mode!=='paper';
  $('priority-section').hidden=mode!=='paper';
  $('method-section').hidden=mode!=='signal';
  $('registration-preview').hidden=mode!=='paper';$('login-preview').hidden=mode!=='signal';
  $('login-stage').hidden=true;$('employee-stage').hidden=true;
  $('form-kicker').textContent=mode==='paper'?'PAPER GENESIS / REGISTRATION':'ORANGE SIGNAL / LOGIN';
  $('form-title').textContent=mode==='paper'?'Create your company identity.':'Activate your identity.';
  $('form-intro').textContent=mode==='paper'?'Your details give shape to the illustration beside you.':'A drawn signal develops into a precision optical module.';
  $('form-disclaimer').textContent='Isolated visual study. Nothing entered here is submitted or authenticated.';
  const container=$('shots');container.replaceChildren();
  for(const shot of shots[mode]){
    const button=document.createElement('button');button.type='button';button.dataset.shot=shot.id;
    button.textContent=`${shot.id} ${shot.name}`;button.title=`Replay ${shot.name}`;
    button.addEventListener('click',()=>restartShot(shot));container.append(button);
  }
  playing=false;segment=null;playbackRate=1;$('playback-speed').value='1';updatePlayButton();userOrbit=false;$('orbit').checked=false;controls.enabled=false;$('orbit-hint').hidden=true;
  resize();setProgress(0);glideTo(mode==='paper'?.14:.12,2200);
}
function restartShot(shot){
  glide=null;
  if(reducedMotion){setProgress(shot.end);return;}
  setProgress(shot.start);segment={start:shot.start,end:shot.end};playing=true;playDirection=1;updatePlayButton();
}
function begin(direction){
  glide=null;
  if(reducedMotion){setProgress(direction>0?segment?.end??1:segment?.start??0);playing=false;updatePlayButton();return;}
  if(direction>0&&progress>=.999)setProgress(segment?.start??0);
  if(direction<0&&progress<=.001)setProgress(segment?.end??1);
  playing=true;playDirection=direction;updatePlayButton();
}
function changeCategory(name){
  if(!allCategories.includes(name)||name===selectedCategory)return;
  if(progress<.68){selectedCategory=name;categoryMix=1;paper.update(mode==='paper'?progress:0,selectedCategory,categoryMix);return;}
  categorySwap={from:selectedCategory,to:name,start:performance.now(),duration:1250};
}
const hasEmail=value=>/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
function syncRegistration(){
  if(mode!=='paper')return;
  let target=.14;
  if($('reg-contact').value.trim().length>=2){
    target=.25;
    if(hasEmail($('reg-email').value)){
      target=.29;
      if($('reg-password').value.length>=6){
        target=.39;
        if($('reg-company').value.trim().length>=2){
          target=.70;
          if($('reg-category').value)target=irisPreview?1:.83;
        }
      }
    }
  }
  $('reg-iris').disabled=target<.83;
  glideTo(target,Math.max(620,Math.abs(target-progress)*3500));
}
function syncLogin(){
  if(mode!=='signal')return;
  if(loginStage!=='credentials')return;
  let target=.12;
  if(hasEmail($('login-email').value))target=.32;
  if(target>=.32&&$('login-password').value.length>=6)target=.55;
  glideTo(target,Math.max(620,Math.abs(target-progress)*3200));
  $('login-continue').disabled=!(hasEmail($('login-email').value)&&$('login-password').value.length>=6);
}
function continueDemo(){
  if($('login-continue').disabled)return;
  if($('demo-role').value==='employee'){
    loginStage='employee';$('employee-stage').hidden=false;$('login-stage').hidden=true;
    $('form-disclaimer').textContent='Employee route visual study. Supplier biometrics are omitted. No account access is granted.';
    glideTo(.55,700);return;
  }
  loginStage='supplier';$('employee-stage').hidden=true;$('login-stage').hidden=false;
  $('form-disclaimer').textContent='Supplier method storyboard only. No credential validation, camera use, biometric verification, or access.';
  glideTo(.78,1300);
}
function tick(now){
  const dt=lastTime?Math.min((now-lastTime)/1000,.25):0;lastTime=now;
  if(glide){const t=clamp((now-glide.start)/glide.duration);setProgress(THREE.MathUtils.lerp(glide.from,glide.to,ease(t)));if(t>=1)glide=null;}
  if(playing){
    const duration=(mode==='paper'?16:7.3)/playbackRate;
    const end=playDirection>0?(segment?.end??1):(segment?.start??0);
    const next=progress+playDirection*dt/duration;
    if((playDirection>0&&next>=end)||(playDirection<0&&next<=end)){setProgress(end);playing=false;segment=null;updatePlayButton();}
    else setProgress(next);
  }
  if(categorySwap){
    const t=clamp((now-categorySwap.start)/categorySwap.duration);
    if(t<.5){selectedCategory=categorySwap.from;categoryMix=1-ease(t*2);}
    else{selectedCategory=categorySwap.to;categoryMix=ease((t-.5)*2);}
    paper.update(mode==='paper'?progress:0,selectedCategory,categoryMix);
    if(t>=1){categorySwap=null;categoryMix=1;}
  }
  if(userOrbit)controls.update();else authoredCamera();
  renderer.toneMappingExposure=mode==='paper'?1.38:.96;
  renderer.render(mode==='paper'?paperScene:signalScene,camera);
  frameCounter++;
  if(now-frameSince>1000){$('frame-readout').textContent=`${Math.round(frameCounter*1000/(now-frameSince))} FPS`;frameCounter=0;frameSince=now;}
  requestAnimationFrame(tick);
}
function bind(){
  for(const option of $('category').options){const copy=option.cloneNode(true);$('reg-category').append(copy);}
  for(const id of ['registration-preview','login-preview'])$(id).addEventListener('submit',event=>event.preventDefault());
  $('reg-iris').disabled=true;
  for(const id of ['reg-contact','reg-email','reg-password','reg-company'])$(id).addEventListener('input',()=>{irisPreview=false;syncRegistration();});
  $('reg-category').addEventListener('change',event=>{irisPreview=false;if(event.target.value){$('category').value=event.target.value;changeCategory(event.target.value);}syncRegistration();});
  $('reg-iris').addEventListener('click',()=>{if(!$('reg-category').value)return;irisPreview=true;glideTo(1,2100);});
  for(const id of ['login-email','login-password'])$(id).addEventListener('input',()=>{
    loginStage='credentials';$('login-stage').hidden=true;$('employee-stage').hidden=true;
    $('form-disclaimer').textContent='Isolated visual study. Nothing entered here is submitted or authenticated.';
    syncLogin();
  });
  $('login-continue').disabled=true;$('login-continue').addEventListener('click',continueDemo);
  $('login-face').addEventListener('click',()=>{if(loginStage!=='supplier')return;loginStage='face';glideTo(.94,1700);});
  $('demo-role').addEventListener('change',()=>{loginStage='credentials';$('login-stage').hidden=true;$('employee-stage').hidden=true;syncLogin();});
  $('signal-pulse-preview').addEventListener('click',()=>{if(loginStage==='face'){glideTo(1,1500);$('form-disclaimer').textContent='Post-auth signal storyboard preview. No verification or application access has occurred.';}});
  $('paper-tab').addEventListener('click',()=>setMode('paper'));
  $('signal-tab').addEventListener('click',()=>setMode('signal'));
  $('timeline').addEventListener('input',event=>setProgress(Number(event.target.value)/100,{fromUI:true}));
  $('play').addEventListener('click',()=>{if(playing){playing=false;updatePlayButton();}else begin(1);});
  $('reverse').addEventListener('click',()=>begin(-1));
  $('restart').addEventListener('click',()=>{segment=null;setProgress(0);begin(1);});
  for(const button of document.querySelectorAll('[data-range]'))button.addEventListener('click',()=>{
    glide=null;
    const ranges={a:[.03,.39],b:[.43,.825],c:[.825,1]};
    const [start,end]=ranges[button.dataset.range];
    if(reducedMotion){setProgress(end);return;}
    playbackRate=Number($('playback-speed').value);setProgress(start);segment={start,end};playing=true;playDirection=1;updatePlayButton();
  });
  $('playback-speed').addEventListener('change',event=>{playbackRate=Number(event.target.value);});
  $('category').addEventListener('change',event=>{$('reg-category').value=event.target.value;changeCategory(event.target.value);});
  $('light-rig').addEventListener('change',event=>setLightingRig(event.target.value));
  $('material-mode').addEventListener('change',event=>setMaterialMode(event.target.value));
  $('shadows').addEventListener('change',event=>setShadows(event.target.checked));
  $('face-method').addEventListener('click',()=>{if(loginStage==='supplier'||loginStage==='face'){loginStage='face';restartShot(shots.signal.find(s=>s.id==='L04'));}});
  $('orbit').addEventListener('change',event=>{userOrbit=event.target.checked;controls.enabled=userOrbit;$('orbit-hint').hidden=!userOrbit;if(!userOrbit)authoredCamera();});
  $('reset-view').addEventListener('click',()=>{userOrbit=false;$('orbit').checked=false;controls.enabled=false;$('orbit-hint').hidden=true;authoredCamera();});
  $('wireframe').addEventListener('change',event=>{
    wireframe=event.target.checked;
    for(const scene of [paperScene,signalScene])scene.traverse(o=>{if(o.isMesh&&o.name!=='studioGround'){
      const materials=Array.isArray(o.material)?o.material:[o.material];for(const m of materials)m.wireframe=wireframe;
    }});
  });
  $('reduced-motion').checked=reducedMotion;
  $('reduced-motion').addEventListener('change',event=>{reducedMotion=event.target.checked;if(reducedMotion&&playing){playing=false;setProgress(segment?.end??1);segment=null;updatePlayButton();}});
  matchMedia('(prefers-reduced-motion: reduce)').addEventListener('change',event=>{if(!$('reduced-motion').dataset.userSet){reducedMotion=event.matches;$('reduced-motion').checked=reducedMotion;}});
  $('reduced-motion').addEventListener('change',()=>$('reduced-motion').dataset.userSet='true');
  window.addEventListener('keydown',event=>{if(event.target.matches('input,select,button'))return;if(event.code==='Space'){event.preventDefault();playing=!playing;updatePlayButton();}});
}
if(initialize()){
  bind();setMode('paper');requestAnimationFrame(tick);
  window.motionLab={
    setProgress:p=>setProgress(p,{fromUI:true}),setMode,changeCategory,
    setPlaybackRate:rate=>{playbackRate=Math.max(.2,Math.min(2,rate));},
    playRange:(start,end,rate=1)=>{glide=null;playbackRate=Math.max(.2,Math.min(2,rate));setProgress(start);segment={start,end};playing=true;playDirection=1;updatePlayButton();},
    setOrbit:value=>{userOrbit=value;$('orbit').checked=value;controls.enabled=value;$('orbit-hint').hidden=!value;},
    getState:()=>({mode,progress,category:selectedCategory,categoryMix,transitioning:Boolean(glide),playing,reducedMotion,orbit:userOrbit,wireframe,lightingRig,materialMode,shadows,loginStage}),
    getNodes:()=>({paper:paper.root.children.map(o=>o.name),signal:signal.root.children.map(o=>o.name)}),
    getPose:()=>{
      const names=['cardHinge','sleeveHinge.left','sleeveHinge.right','sleeveHinge.bottom','sleeveHinge.top','wallFrontHinge','wallBackHinge','wallLeftHinge','wallRightHinge','roofHinge','paperIris.attachedLeafHinge','paperIris.bladePivot.0'];
      return Object.fromEntries(names.map(name=>{const o=paper.root.getObjectByName(name);return [name,o?{position:o.position.toArray(),rotation:[o.rotation.x,o.rotation.y,o.rotation.z],visible:o.visible}:null];}));
    },
    getCamera:()=>({position:camera.position.toArray(),up:camera.up.toArray(),zoom:camera.zoom}),
    inspectOpticalCenter:()=>{
      signal.root.updateMatrixWorld(true);
      const ray=new THREE.Raycaster(camera.position.clone(),new THREE.Vector3(0,0,.25).sub(camera.position).normalize());
      return ray.intersectObjects(signal.root.children,true).filter(hit=>hit.object.isMesh).slice(0,12).map(hit=>({name:hit.object.name,distance:hit.distance,color:hit.object.material?.color?.getHexString?.()}));
    },
    getRenderStats:()=>({pixelRatio:renderer.getPixelRatio(),calls:renderer.info.render.calls,triangles:renderer.info.render.triangles,points:renderer.info.render.points,lines:renderer.info.render.lines,viewport:[viewport.clientWidth,viewport.clientHeight]})
  };
}

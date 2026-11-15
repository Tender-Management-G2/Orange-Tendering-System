import * as THREE from 'three';

const clamp = n => Math.max(0, Math.min(1, n));
const ease = n => { n=clamp(n); return n*n*(3-2*n); };
const span = (p,a,b) => ease((p-a)/(b-a));
const M = {
  graphite: new THREE.MeshStandardMaterial({color:0x181818,metalness:.32,roughness:.43}),
  graphiteLight: new THREE.MeshStandardMaterial({color:0x383530,metalness:.4,roughness:.38}),
  graphiteDeep: new THREE.MeshStandardMaterial({color:0x111111,metalness:.3,roughness:.72}),
  copper: new THREE.MeshStandardMaterial({color:0xd89b60,metalness:.28,roughness:.3,emissive:0x2d1203,emissiveIntensity:.16}),
  orange: new THREE.MeshStandardMaterial({color:0xff7900,metalness:.36,roughness:.4,emissive:0xa63900,emissiveIntensity:.24}),
  ivory: new THREE.MeshStandardMaterial({color:0xf3e7d1,metalness:.08,roughness:.42}),
  titanium: new THREE.MeshStandardMaterial({color:0x5a5751,metalness:.58,roughness:.38}),
  phosphor: new THREE.MeshBasicMaterial({color:0xff8c13}),
  glass: new THREE.MeshStandardMaterial({color:0x26231f,metalness:.08,roughness:.16,transparent:true,opacity:.84}),
  preview: new THREE.MeshBasicMaterial({color:0x141412,side:THREE.DoubleSide}),
  blade: new THREE.MeshStandardMaterial({color:0x252422,metalness:.4,roughness:.56,side:THREE.DoubleSide})
};
function box(name,w,h,d,material,x=0,y=0,z=0){const o=new THREE.Mesh(new THREE.BoxGeometry(w,h,d),material);o.name=name;o.position.set(x,y,z);o.castShadow=true;o.receiveShadow=true;return o;}
function chamferedCase(name,w,h,d,corner,material,x=0,y=0,z=0){
  const s=new THREE.Shape();
  s.moveTo(-w/2+corner,-h/2);s.lineTo(w/2-corner,-h/2);s.lineTo(w/2,-h/2+corner);
  s.lineTo(w/2,h/2-corner);s.lineTo(w/2-corner,h/2);s.lineTo(-w/2+corner,h/2);
  s.lineTo(-w/2,h/2-corner);s.lineTo(-w/2,-h/2+corner);s.closePath();
  const o=new THREE.Mesh(new THREE.ExtrudeGeometry(s,{depth:d-.03,bevelEnabled:true,bevelThickness:.015,bevelSize:.025,bevelSegments:3,steps:1}),material);
  o.name=name;o.position.set(x,y,z-d/2+.015);o.castShadow=true;o.receiveShadow=true;return o;
}
function curveLine(name,points,color=0xff7900){const geometry=new THREE.BufferGeometry().setFromPoints(points.map(p=>new THREE.Vector3(...p)));const out=new THREE.Line(geometry,new THREE.LineBasicMaterial({color}));out.name=name;return out;}
function drawnLine(name,points,color){const dense=[];for(let i=0;i<points.length-1;i++)for(let j=0;j<24;j++){const t=j/24;dense.push(points[i].map((v,k)=>v*(1-t)+points[i+1][k]*t));}dense.push(points.at(-1));return curveLine(name,dense,color);}
function tracePath(name,pts,material,r=.012){const curve=new THREE.CatmullRomCurve3(pts.map(p=>new THREE.Vector3(...p)),false,'centripetal');const mesh=new THREE.Mesh(new THREE.TubeGeometry(curve,Math.max(20,pts.length*10),r,7,false),material);mesh.name=name;mesh.castShadow=true;return mesh;}
function rim(name,r,t,z,material){const mesh=new THREE.Mesh(new THREE.RingGeometry(r-t,r,64),material);mesh.name=name;mesh.position.z=z;return mesh;}
function cap(name,r,h,material,x,y,z){const mesh=new THREE.Mesh(new THREE.CylinderGeometry(r,r,h,14),material);mesh.name=name;mesh.rotation.x=Math.PI/2;mesh.position.set(x,y,z);mesh.castShadow=true;return mesh;}

export class SignalJourney {
  constructor(scene){
    this.scene=scene;this.root=new THREE.Group();this.root.name='signalRoot';scene.add(this.root);
    const root=this.root;
    root.add(chamferedCase('signalRoot.shadowPlate',3.35,3.12,.12,.20,M.graphiteDeep,.03,0,-.07));
    this.core=chamferedCase('coreBase',3.12,2.94,.23,.19,M.graphite,0,0,.075);this.core.userData.baseZ=this.core.position.z;root.add(this.core);
    root.add(box('coreBase.innerFrame',2.87,2.7,.021,M.graphiteLight,0,0,.206));
    root.add(box('coreBase.cornerMark',.15,.15,.018,M.orange,1.21,-1.16,.222));
    for(const [i,x,y] of [[0,-1.36,1.28],[1,1.36,1.28],[2,-1.36,-1.28],[3,1.36,-1.28]]){
      root.add(cap(`coreBase.fastener.${i}`,.074,.028,M.ivory,x,y,.231));
      root.add(curveLine(`coreBase.fastenerSlot.${i}`,[[x-.038,y,.25],[x+.038,y,.25]],0x2e2c29));
    }
    root.add(box('contactPlate.rail.upper',1.36,.07,.15,M.graphiteLight,-1.89,.52,.116));
    root.add(box('contactPlate.rail.lower',1.36,.07,.15,M.graphiteLight,-1.89,-.52,.116));
    this.tray=new THREE.Group();this.tray.name='contactPlate';root.add(this.tray);
    this.tray.add(box('contactPlate.carrier',1.12,.91,.085,M.graphiteLight,-.15,0,.18));
    this.tray.add(box('contactPlate.inset',.89,.69,.019,M.copper,-.15,0,.236));
    for(let r=0;r<2;r++)for(let c=0;c<3;c++){
      const x=-.46+c*.32,y=-.17+r*.34;
      this.tray.add(box(`contactPlate.pad.${r}.${c}`,.29,.28,.018,M.copper,x,y,.256));
      this.tray.add(curveLine(`contactPlate.padScore.${r}.${c}`,[[x+.15,y-.13,.27],[x+.15,y+.13,.27]],0x5c321b));
    }
    this.tray.add(box('contactPlate.orangeEdge',.08,.79,.017,M.orange,.37,0,.254));

    this.traceList=[];
    const paths=[
      [[-1.48,.89,.337],[-1.36,.89,.337],[-1.29,.76,.337],[-1.06,.76,.337]],
      [[-1.48,.63,.337],[-1.34,.63,.337],[-1.25,.54,.337],[-1.17,.54,.337]],
      [[-1.48,-.63,.337],[-1.34,-.63,.337],[-1.25,-.54,.337],[-1.17,-.54,.337]],
      [[-1.48,-.89,.337],[-1.36,-.89,.337],[-1.29,-.76,.337],[-1.06,-.76,.337]],
      [[-.48,1.35,.337],[-.48,1.26,.337],[-.62,1.11,.337]],
      [[.48,1.35,.337],[.48,1.26,.337],[.62,1.11,.337]],
      [[-.48,-1.35,.337],[-.48,-1.26,.337],[-.62,-1.11,.337]],
      [[.48,-1.35,.337],[.48,-1.26,.337],[.62,-1.11,.337]]
    ];
    paths.forEach((points,i)=>{
      const trace=curveLine(`circuitPlate.trace.${i}`,points,0xc88245);
      root.add(trace);this.traceList.push(trace);
      points.slice(0,-1).forEach((p,j)=>root.add(cap(`circuitPlate.via.${i}.${j}`,.026,.009,M.copper,p[0],p[1],p[2]+.004)));
    });
    root.add(rim('opticalCore.titaniumRing',1.03,.075,.322,M.titanium));
    root.add(rim('opticalCore.graphiteRim',1.16,.12,.329,M.graphiteDeep));
    root.add(rim('opticalCore.orangeIndex',1.18,.015,.345,M.orange));
    root.add(rim('opticalCore.innerCopper',.89,.019,.347,M.copper));
    for(let i=0;i<32;i++){
      const a=i*Math.PI/16,r=i%4===0?1.27:1.23;
      const tick=box(`opticalCore.index.${i}`,i%4===0?.105:.047,.013,.009,i%4===0?M.orange:M.copper,r*Math.cos(a),r*Math.sin(a),.342);
      tick.rotation.z=a;root.add(tick);
    }
    const lens=new THREE.Mesh(new THREE.CircleGeometry(.86,64),M.preview);lens.name='cameraPlane.abstractPreview';lens.position.z=.219;root.add(lens);
    const opticalGlass=new THREE.Mesh(new THREE.CircleGeometry(.64,96),new THREE.MeshBasicMaterial({color:0x0b181d,side:THREE.DoubleSide}));
    opticalGlass.name='cameraPlane.opticalGlass';opticalGlass.position.z=.228;root.add(opticalGlass);
    this.focusRings=[];
    for(const [i,radius] of [.32,.52,.73].entries()){
      const ring=new THREE.Mesh(new THREE.TorusGeometry(radius,i===2?.009:.006,6,96),i===2?M.copper:M.orange);
      ring.name=`cameraPlane.focusRing.${i}`;ring.position.z=.244+i*.002;root.add(ring);this.focusRings.push(ring);
    }
    const reticle=curveLine('cameraPlane.reticle',[[-.10,0,.251],[-.04,0,.251],[0,.04,.251],[.04,0,.251],[.10,0,.251]],0xffa24b);root.add(reticle);
    this.cameraItems=[lens,opticalGlass,...this.focusRings,reticle];this.blades=[];
    for(let i=0;i<8;i++){
      const a=i*Math.PI/4,b=(i+1)*Math.PI/4,r=.92;
      const p1=new THREE.Vector2(Math.cos(a)*r,Math.sin(a)*r),p2=new THREE.Vector2(Math.cos(b)*r,Math.sin(b)*r);
      const pivot=new THREE.Vector2((p1.x+p2.x)/2,(p1.y+p2.y)/2);
      const hinge=new THREE.Group();hinge.name=`apertureSegment.${i}.hinge`;hinge.position.set(pivot.x,pivot.y,.245+i*.001);
      const shape=new THREE.Shape();
      shape.moveTo(p1.x-pivot.x,p1.y-pivot.y);
      const mid=(a+b)/2;
      const nearA=new THREE.Vector2(.13*Math.cos(mid-.13),.13*Math.sin(mid-.13));
      const nearB=new THREE.Vector2(.13*Math.cos(mid+.13),.13*Math.sin(mid+.13));
      shape.quadraticCurveTo(.62*Math.cos(a+.19)-pivot.x,.62*Math.sin(a+.19)-pivot.y,nearA.x-pivot.x,nearA.y-pivot.y);
      shape.quadraticCurveTo(.065*Math.cos(mid)-pivot.x,.065*Math.sin(mid)-pivot.y,nearB.x-pivot.x,nearB.y-pivot.y);
      shape.quadraticCurveTo(.62*Math.cos(b-.19)-pivot.x,.62*Math.sin(b-.19)-pivot.y,p2.x-pivot.x,p2.y-pivot.y);
      shape.closePath();
      const blade=new THREE.Mesh(new THREE.ShapeGeometry(shape,24),M.blade);blade.name=`apertureSegment.${i}.blade`;blade.castShadow=true;hinge.add(blade);
      const axis=new THREE.Vector3(p2.x-p1.x,p2.y-p1.y,0).normalize();
      root.add(hinge);this.blades.push({hinge,pivot,axis});
    }
    const maskShape=new THREE.Shape();maskShape.moveTo(-1.40,-1.47);maskShape.lineTo(1.40,-1.47);maskShape.lineTo(1.56,-1.31);maskShape.lineTo(1.56,1.31);maskShape.lineTo(1.40,1.47);maskShape.lineTo(-1.40,1.47);maskShape.lineTo(-1.56,1.31);maskShape.lineTo(-1.56,-1.31);maskShape.closePath();
    const maskHole=new THREE.Path();maskHole.absarc(0,0,.87,0,Math.PI*2,true);maskShape.holes.push(maskHole);
    const mask=new THREE.Mesh(new THREE.ShapeGeometry(maskShape),M.graphite);mask.name='opticalCore.apertureMask';mask.position.z=.301;mask.receiveShadow=true;root.add(mask);
    for(const sign of [-1,1]){
      for(let i=0;i<9;i++){
        const vent=box(`coreBase.sideVent.${sign}.${i}`,.11,.025,.018,i%3===0?M.orange:M.graphiteDeep,sign*1.39,-.9+i*.21,.334);
        root.add(vent);
      }
    }
    for(const y of [-1.31,1.31])root.add(box(`coreBase.busBar.${y}`,1.37,.035,.02,M.copper,0,y,.337));
    for(const [i,x,y] of [[0,-1.36,1.28],[1,1.36,1.28],[2,-1.36,-1.28],[3,1.36,-1.28]])root.add(cap(`opticalCore.maskFastener.${i}`,.066,.021,M.ivory,x,y,.315));
    // Engraved touch sensor study: a permanently unavailable visual placeholder.
    const sensor=box('touchIdentity.unavailableSensor',.43,.43,.018,M.graphiteLight,1.27,-1.05,.356);root.add(sensor);
    for(let i=0;i<5;i++){
      const points=[];const radius=.055+i*.031;
      for(let j=0;j<=28;j++){const a=Math.PI*.18+(Math.PI*1.64)*j/28;points.push([1.27+radius*Math.cos(a),-1.05+radius*Math.sin(a),.372]);}
      root.add(curveLine(`touchIdentity.engraving.${i}`,points,0xc29c74));
    }
    this.fiberPath=new THREE.CatmullRomCurve3([
      new THREE.Vector3(1.58,-.26,.13),new THREE.Vector3(1.96,-.2,.13),
      new THREE.Vector3(2.43,-.45,.1),new THREE.Vector3(2.56,-1.1,.09),
      new THREE.Vector3(2.2,-1.52,.09),new THREE.Vector3(1.91,-1.67,.09),
      new THREE.Vector3(2.08,-1.99,.08)
    ],false,'centripetal');
    const cable=new THREE.Mesh(new THREE.TubeGeometry(this.fiberPath,70,.075,12,false),M.graphiteLight);cable.name='fiberPath.sheath';cable.castShadow=true;root.add(cable);
    const litPath=new THREE.CatmullRomCurve3(this.fiberPath.points.map(p=>new THREE.Vector3(p.x,p.y,p.z+.074)),false,'centripetal');
    for(let i=-2;i<=2;i++){
      const strandPath=new THREE.CatmullRomCurve3(litPath.points.map((p,j)=>new THREE.Vector3(p.x+i*.019,p.y,p.z+(i%2)*.006)),false,'centripetal');
      const strand=new THREE.Mesh(new THREE.TubeGeometry(strandPath,70,.007,5,false),i===0?M.phosphor:M.copper);
      strand.name=`fiberPath.strand.${i+2}`;root.add(strand);
    }
    const pulse=new THREE.Mesh(new THREE.SphereGeometry(.075,12,8),M.phosphor);pulse.name='fiberPath.signalPulse';root.add(pulse);this.pulse=pulse;this.litPath=litPath;
    this.pulseLight=new THREE.PointLight(0xff7900,0,.66,2);this.pulseLight.name='fiberPath.signalLight';root.add(this.pulseLight);
    const connector=cap('fiberPath.connector',.17,.26,M.graphiteLight,1.67,-.26,.14);connector.rotation.z=Math.PI/2;root.add(connector);
    root.add(rim('fiberPath.connectorIndex',.11,.035,.24,M.orange));root.children.at(-1).position.set(1.72,-.26,.24);
    this.assembly=new THREE.Group();this.assembly.name='signalRoot.assembly';
    for(const child of [...root.children])this.assembly.add(child);
    root.add(this.assembly);
    this.paint=[
      [M.graphite,0x252521],[M.graphiteLight,0x514c45],[M.graphiteDeep,0x171716],
      [M.copper,0xc98b50],[M.orange,0xff7900],[M.ivory,0xf2e7d6],
      [M.phosphor,0xff8c13],[M.titanium,0x817e78],[M.glass,0x2f3839],
      [M.preview,0x1b292d],[M.blade,0x36342f]
    ].map(([material,to])=>({material,from:new THREE.Color(0xf8f5ef),to:new THREE.Color(to),metalness:material.metalness??0}));
    this.outline=[];
    const outlinePts=[[-1.58,-1.49,.31],[1.58,-1.49,.31],[1.58,1.49,.31],[-1.58,1.49,.31],[-1.58,-1.49,.31]];
    this.outline.push(drawnLine('signalRoot.planarOutline',outlinePts,0xff7900));
    this.outline.push(drawnLine('contactPlate.planarOutline',[[-2.38,-.52,.31],[-1.21,-.52,.31],[-1.21,.52,.31],[-2.38,.52,.31],[-2.38,-.52,.31]],0x39342e));
    const ringPoints=[];for(let i=0;i<=64;i++){const a=i*Math.PI/32;ringPoints.push([1.18*Math.cos(a),1.18*Math.sin(a),.312]);}
    this.outline.push(drawnLine('opticalCore.planarRing',ringPoints,0xff7900));
    this.outline.push(drawnLine('fiberPath.planarLine',this.fiberPath.getPoints(30).map(p=>[p.x,p.y,.314]),0xff7900));
    this.outline.forEach(o=>root.add(o));
    this.update(0);
  }
  update(progress){
    const p=clamp(progress),traces=span(p,.12,.32),contact=span(p,.26,.55),depth=span(p,.53,.78),camera=span(p,.78,.94);
    const appearance=span(p,.12,.78);
    const footprint=span(p,.115,.34);
    this.assembly.scale.set(.001+.999*footprint,.001+.999*footprint,.018+.982*depth);
    for(const paint of this.paint){paint.material.color.copy(paint.from).lerp(paint.to,appearance);if('metalness' in paint.material)paint.material.metalness=paint.metalness*appearance;}
    M.orange.emissiveIntensity=.24*appearance;M.copper.emissiveIntensity=.16*appearance;
    this.tray.position.x=-2.55+1.0*contact;
    this.core.position.z=this.core.userData.baseZ-.035*depth;
    this.core.scale.z=.08+.92*depth;
    for(let i=0;i<this.traceList.length;i++){
      const trace=this.traceList[i];
      trace.geometry.setDrawRange(0,Math.max(0,Math.floor((trace.geometry.attributes.position.count-1)*traces)+1));
    }
    for(const [i,item] of this.blades.entries()){
      const open=.22*depth+.78*camera;
      // Each blade swings tangentially around its fixed outer pivot, clearing the
      // optical well while remaining attached to the same ring throughout.
      item.hinge.rotation.z=1.62*open;
      item.hinge.position.set(item.pivot.x,item.pivot.y,.245+i*.001+.025*depth);
    }
    this.focusRings.forEach((ring,i)=>{ring.rotation.z=(i%2?1:-1)*.16*camera;ring.scale.setScalar(.94+.06*camera);});
    this.outline.forEach(o=>{
      o.material.transparent=true;o.material.opacity=1-span(p,.45,.62);
      o.visible=p<.62;
      o.geometry.setDrawRange(0,Math.max(0,Math.floor(o.geometry.attributes.position.count*span(p,0,.12))));
    });
    const pulse=span(p,.94,.98)*(1-span(p,.992,1));
    this.pulse.scale.setScalar(.001+.999*pulse);
    this.pulse.position.copy(this.litPath.getPointAt(span(p,.94,1)));
    this.pulseLight.position.copy(this.pulse.position);this.pulseLight.intensity=.85*pulse;
  }
  dispose(){this.root.traverse(o=>{if(o.geometry)o.geometry.dispose();if(o.material){(Array.isArray(o.material)?o.material:[o.material]).forEach(m=>m.dispose());}});this.scene.remove(this.root);}
}

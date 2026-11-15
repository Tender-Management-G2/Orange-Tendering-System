import * as THREE from 'three';
import { paperSurface } from './paper-surface.js';

const clamp=n=>Math.max(0,Math.min(1,n));
const ease=n=>{n=clamp(n);return n*n*(3-2*n)};
const span=(p,a,b)=>ease((p-a)/(b-a));

function paper(color){return new THREE.MeshStandardMaterial({color,roughness:.91,side:THREE.DoubleSide,map:paperSurface(),bumpMap:paperSurface(),bumpScale:.006});}
function mesh(name,geometry,material,parent){
  const object=new THREE.Mesh(geometry,material);object.name=name;object.castShadow=true;object.receiveShadow=true;parent.add(object);return object;
}

// The frame is one attached leaf with an actual circular die-cut, rather than a square camera card.
export class PaperIris {
  constructor(root){
    this.hinge=new THREE.Group();this.hinge.name='paperIris.attachedLeafHinge';
    this.hinge.position.set(2.54,-.64,.063);root.add(this.hinge);
    this.hinge.scale.setScalar(.70);
    const frameShape=new THREE.Shape();
    frameShape.moveTo(-.31,0);frameShape.lineTo(.31,0);frameShape.lineTo(.31,-.08);
    frameShape.lineTo(.55,-.245);frameShape.absarc(0,-.77,.76,.76,2.38,true);
    frameShape.lineTo(-.31,-.08);frameShape.closePath();
    const hole=new THREE.Path();hole.absarc(0,-.77,.59,0,Math.PI*2,true);frameShape.holes.push(hole);
    const frame=mesh('paperIris.dieCutLeaf',new THREE.ExtrudeGeometry(frameShape,{depth:.038,bevelEnabled:false,curveSegments:80}),paper(0xfffbf3),this.hinge);
    const edge=new THREE.EdgesGeometry(frame.geometry);
    const contour=new THREE.LineSegments(edge,new THREE.LineBasicMaterial({color:0xd8c9b5}));contour.name='paperIris.cutEdges';this.hinge.add(contour);
    const dark=new THREE.MeshStandardMaterial({color:0x181a1b,roughness:.33,metalness:.16,side:THREE.DoubleSide});
    mesh('paperIris.recessedOpticalWell',new THREE.CircleGeometry(.552,96),dark,this.hinge).position.set(0,-.77,.057);
    const inner=new THREE.MeshStandardMaterial({color:0x23333c,roughness:.21,metalness:.55,side:THREE.DoubleSide});
    const lens=mesh('paperIris.opticalGlass',new THREE.CircleGeometry(.305,80),inner,this.hinge);
    lens.position.set(0,-.77,.052);
    const innerDepth=new THREE.MeshStandardMaterial({color:0x122027,roughness:.17,metalness:.6,side:THREE.DoubleSide});
    mesh('paperIris.innerGlass',new THREE.CircleGeometry(.205,80),innerDepth,this.hinge).position.set(0,-.77,.045);
    const silver=new THREE.MeshStandardMaterial({color:0xb5c4c4,roughness:.19,metalness:.78,side:THREE.DoubleSide});
    for(const [i,r] of [.25,.33,.41].entries()){
      const ring=mesh(`paperIris.lensRing.${i}`,new THREE.TorusGeometry(r,.006+i*.002,6,80),silver,this.hinge);
      ring.position.set(0,-.77,.04);
    }
    const glint=mesh('paperIris.glassGlint',new THREE.TorusGeometry(.245,.012,6,28,Math.PI*.44),new THREE.MeshBasicMaterial({color:0xd8e4e2,transparent:true,opacity:.7}),this.hinge);
    glint.position.set(0,-.77,.032);glint.rotation.z=.7;
    const halo=new THREE.MeshStandardMaterial({color:0xff7900,roughness:.48,metalness:.15,emissive:0xff5a00,emissiveIntensity:.24});
    const torus=(name,r,t,material,z)=>{
      const o=mesh(name,new THREE.TorusGeometry(r,t,8,96),material,this.hinge);o.position.set(0,-.77,z);return o;
    };
    torus('paperIris.apertureLining',.568,.013,inner,-.009);
    this.controlRing=torus('paperIris.controlRing',.641,.018,halo,-.023);
    const indexInk=new THREE.MeshBasicMaterial({color:0x4b321e});
    for(let i=0;i<8;i++){
      const a=i*Math.PI/4;
      const mark=mesh(`paperIris.ringDriveIndex.${i}`,new THREE.BoxGeometry(i%2?.025:.042,.008,.004),indexInk,this.controlRing);
      mark.position.set(.641*Math.cos(a),.641*Math.sin(a),-.029);mark.rotation.z=a;
    }
    torus('paperIris.cutRim',.686,.012,paper(0xd8c9b5),-.008);
    const orange=paper(0xff7900), ivory=paper(0xfffbf3),warm=paper(0xf0e5d3);
    this.blades=[];
    const count=8;
    for(let i=0;i<count;i++){
      const a=i*Math.PI*2/count;
      const pivot=new THREE.Vector2(.505*Math.cos(a),-.77+.505*Math.sin(a));
      const q=(r,angle)=>new THREE.Vector2(r*Math.cos(angle)-pivot.x,-.77+r*Math.sin(angle)-pivot.y);
      const a0=q(.64,a-.47),a1=q(.65,a+.31),tip=q(.035,a+.29);
      const control1=q(.44,a-.12),control2=q(.26,a+.26);
      const shape=new THREE.Shape();shape.moveTo(a0.x,a0.y);
      shape.quadraticCurveTo(control1.x,control1.y,tip.x,tip.y);
      shape.quadraticCurveTo(control2.x,control2.y,a1.x,a1.y);
      shape.quadraticCurveTo(q(.72,a+.04).x,q(.72,a+.04).y,a0.x,a0.y);
      shape.closePath();
      const hinge=new THREE.Group();hinge.name=`paperIris.bladePivot.${i}`;
      hinge.position.set(pivot.x,pivot.y,-.033-i*.0015);this.hinge.add(hinge);
      const blade=mesh(`paperIris.curvedBlade.${i}`,new THREE.ExtrudeGeometry(shape,{depth:.012,bevelEnabled:false,curveSegments:36}),i%2?ivory:warm,hinge);
      blade.add(new THREE.LineSegments(new THREE.EdgesGeometry(blade.geometry),new THREE.LineBasicMaterial({color:0xcbbba5})));
      const pin=mesh(`paperIris.pivotPin.${i}`,new THREE.CylinderGeometry(.019,.019,.018,16),orange,this.hinge);
      pin.rotation.x=Math.PI/2;pin.position.set(pivot.x,pivot.y,-.057-i*.0015);
      this.blades.push(hinge);
    }
    for(const side of [-1,1]){
      const notch=mesh(`paperIris.registrationTab.${side}`,new THREE.BoxGeometry(.09,.023,.012),orange,this.hinge);
      notch.position.set(side*.68,-.77,-.014);
    }
    this.update(0);
  }
  update(p){
    const rise=span(p,.825,.897);
    this.hinge.rotation.x=-Math.PI/2*rise;
    const open=span(p,.895,.975);
    this.controlRing.rotation.z=-.16*open;
    for(let i=0;i<this.blades.length;i++){
      const bladeOpen=span(p,.895+i*.003,.971+i*.003);
      this.blades[i].rotation.z=-1.04*bladeOpen;
    }
  }
}

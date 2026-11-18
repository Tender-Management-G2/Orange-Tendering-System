import * as THREE from 'three';

let surface;
export function paperSurface(){
  if(surface)return surface;
  const canvas=document.createElement('canvas');canvas.width=canvas.height=512;
  const ctx=canvas.getContext('2d');ctx.fillStyle='#fdfcf8';ctx.fillRect(0,0,512,512);
  let seed=37191;const random=()=>{seed=(seed*1664525+1013904223)>>>0;return seed/4294967296;};
  for(let i=0;i<18500;i++){
    const x=random()*512,y=random()*512,length=.5+random()*4.5;
    const shade=Math.floor(154+random()*75);
    ctx.strokeStyle=`rgba(${shade},${shade-8},${shade-20},${.012+random()*.032})`;
    ctx.lineWidth=.25+random()*.45;ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(x+length,y+length*.18);ctx.stroke();
  }
  surface=new THREE.CanvasTexture(canvas);surface.colorSpace=THREE.SRGBColorSpace;
  surface.wrapS=surface.wrapT=THREE.RepeatWrapping;surface.anisotropy=8;
  return surface;
}

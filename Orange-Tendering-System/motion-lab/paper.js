import * as THREE from 'three';
import { PaperIris } from './paper-iris.js';
import { paperSurface } from './paper-surface.js';

const C = { paper: 0xfffbf3, paperLight: 0xffffff, paperEdge: 0xd8c9b5, ink: 0x292723, orange: 0xff7900, glass: 0x9b7550 };
const clamp = n => Math.max(0, Math.min(1, n));
const ease = n => { n = clamp(n); return n * n * (3 - 2 * n); };
const span = (p, a, b) => ease((p - a) / (b - a));

function mat(color, extra = {}) {
  const isPaper=[C.paper,C.paperLight,C.paperEdge].includes(color);
  return new THREE.MeshStandardMaterial({ color,roughness:.9,metalness:0,side:THREE.DoubleSide,...(isPaper?{map:paperSurface(),bumpMap:paperSurface(),bumpScale:.006}:{}),...extra });
}
function box(name, w, h, d, material, x = 0, y = 0, z = 0) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), material);
  mesh.name = name; mesh.position.set(x, y, z); mesh.castShadow = true; mesh.receiveShadow = true; return mesh;
}
function line(name, points, color = C.ink, z = .045, dashed = false) {
  const vertices = points.map(p => new THREE.Vector3(p[0], p[1], p[2] ?? z));
  const geometry = new THREE.BufferGeometry().setFromPoints(vertices);
  const material = dashed ? new THREE.LineDashedMaterial({ color, dashSize: .065, gapSize: .052 }) : new THREE.LineBasicMaterial({ color });
  const out = new THREE.Line(geometry, material); out.name = name;
  if (dashed) out.computeLineDistances();
  return out;
}
function rectangleLines(name, w, h, z = .05, color = C.ink) {
  return line(name, [[-w/2,-h/2,z],[w/2,-h/2,z],[w/2,h/2,z],[-w/2,h/2,z],[-w/2,-h/2,z]], color, z);
}
function drawnLine(name, points, color=C.ink, z=.05) {
  const dense=[];
  for(let i=0;i<points.length-1;i++)for(let step=0;step<24;step++){
    const t=step/24;dense.push([points[i][0]*(1-t)+points[i+1][0]*t,points[i][1]*(1-t)+points[i+1][1]*t,z]);
  }
  dense.push([points.at(-1)[0],points.at(-1)[1],z]);
  return line(name,dense,color,z);
}
function panel(name, w, h, material, edge, depth = .044) {
  const group = new THREE.Group(); group.name = name;
  group.add(box(`${name}.paper`, w, h, depth, material, 0, 0, 0));
  group.add(rectangleLines(`${name}.edge`, w, h, depth / 2 + .003, edge));
  return group;
}
function facadePanel(name,w,h,material,edge,openings){
  const group=new THREE.Group();group.name=name;
  const shape=new THREE.Shape();shape.moveTo(-w/2,-h/2);shape.lineTo(w/2,-h/2);shape.lineTo(w/2,h/2);shape.lineTo(-w/2,h/2);shape.closePath();
  for(const [x,y,ow,oh] of openings){
    const hole=new THREE.Path();hole.moveTo(x-ow/2,y-oh/2);hole.lineTo(x-ow/2,y+oh/2);hole.lineTo(x+ow/2,y+oh/2);hole.lineTo(x+ow/2,y-oh/2);hole.closePath();shape.holes.push(hole);
  }
  const wall=new THREE.Mesh(new THREE.ExtrudeGeometry(shape,{depth:.044,bevelEnabled:false}),material);
  wall.name=`${name}.dieCutWall`;wall.position.z=-.022;wall.castShadow=true;wall.receiveShadow=true;group.add(wall);
  group.add(rectangleLines(`${name}.edge`,w,h,.026,edge));
  return group;
}
function polygonPanel(name,points,material){
  const shape=new THREE.Shape();shape.moveTo(points[0][0],points[0][1]);for(const p of points.slice(1))shape.lineTo(p[0],p[1]);shape.closePath();
  const group=new THREE.Group();group.name=name;
  const mesh=new THREE.Mesh(new THREE.ExtrudeGeometry(shape,{depth:.018,bevelEnabled:false}),material);mesh.name=`${name}.paper`;mesh.castShadow=true;mesh.receiveShadow=true;group.add(mesh);
  group.add(line(`${name}.inkEdge`,[...points,points[0]].map(p=>[p[0],p[1],.023]),C.ink,.023));
  return group;
}
function addWindow(group, name, x, y, w, h, materials) {
  for(const side of [1,-1]){
    group.add(box(`${name}.well.${side}`, w, h, .013, materials.glassPane??materials.glass, x, y, side*.039));
    const outline=rectangleLines(`${name}.outline.${side}`, w, h, side*.05);outline.position.set(x,y,0);group.add(outline);
    group.add(box(`${name}.mullion.${side}`, .018, h, .009, materials.ink, x, y, side*.055));
  }
}

function makeCategory(name, root, materials, kind) {
  const group = new THREE.Group(); group.name = `category.${name.replace(/\W+/g, '-')}`; root.add(group);
  group.position.y=-1.47;
  const hinges = [];
  const addLeaf = (leafName, x, y, width, height, decoration) => {
    const hinge = new THREE.Group(); hinge.name = `${group.name}.${leafName}.hinge`;
    hinge.position.set(x, y, .05); group.add(hinge);
    const leaf = panel(`${group.name}.${leafName}`, width, height, materials.paper, C.ink, .055);
    leaf.position.y = height / 2; hinge.add(leaf);
    decoration?.(leaf, width, height);
    hinges.push(hinge); return hinge;
  };
  const slats = (leaf, w, h, count = 5) => {
    for (let i = 0; i < count; i++) {
      const y = -h * .34 + i * h * .15;
      leaf.add(line(`${leaf.name}.slat${i}`, [[-w*.36,y],[w*.36,y]], i === 0 ? C.orange : C.ink, .055));
      if (i > 0) leaf.add(box(`${leaf.name}.light${i}`, .025,.025,.006, materials.orange,-w*.36,y,.06));
    }
  };
  const stripe = (leaf, w, h) => leaf.add(box(`${leaf.name}.stripe`, w*.62,.065,.008,materials.orange,0,h*.25,.055));
  if (kind === 'it') {
    addLeaf('serverLeft', -2.12, .38, .65, 1.1, leaf => slats(leaf,.65,1.1,6));
    addLeaf('serverRight', -1.38, .38, .65, 1.1, leaf => slats(leaf,.65,1.1,6));
    addLeaf('circuit', -1.74, -.85, 1.2, .78, leaf => {
      leaf.add(line('category.it.circuitTrace', [[-.48,-.2],[-.15,-.2],[-.15,.12],[.4,.12]],C.orange,.055));
      leaf.add(box('category.it.chip',.32,.24,.02,materials.ink,.2,-.12,.058));
    });
  } else if (kind === 'telecom') {
    addLeaf('equipment', -2, .42, .7, 1.02, leaf => slats(leaf,.7,1.02,5));
    addLeaf('mast', -1.22, .62, .88, 1.5, leaf => {
      const rails = [[[-.32,-.65],[0,.66]],[[.32,-.65],[0,.66]],[[-.25,-.37],[.25,-.37]],[[-.18,-.05],[.18,-.05]],[[-.1,.27],[.1,.27]],[[-.32,-.65],[.18,-.05]],[[.32,-.65],[-.18,-.05]]];
      for (let i=0;i<rails.length;i++) leaf.add(line(`category.telecom.truss${i}`,rails[i],C.ink,.058));
      leaf.add(box('category.telecom.beacon',.12,.12,.03,materials.orange,0,.65,.065));
    });
  } else if (kind === 'construction') {
    addLeaf('truss', -1.92, .5, .8, 1.42, leaf => {
      for(let i=0;i<4;i++) leaf.add(line(`category.construction.cross${i}`,[[-.3,-.52+i*.28],[.3,-.24+i*.28]],C.ink,.056));
      stripe(leaf,.8,1.42);
    });
    addLeaf('drawing', -1.05, -.7, 1.05, .82, leaf => {
      leaf.add(rectangleLines('category.construction.plan',.62,.42,.058));
      leaf.add(line('category.construction.diagonal',[[-.31,-.21],[.31,.21]],C.orange,.061));
    });
  } else {
    const profile = {
      office: { count: 4, w: .75, h: .98 }, furniture: { count: 2, w: .88, h: .95 },
      transport: { count: 3, w: .76, h: .88 }, professional: { count: 3, w: .7, h: .87 },
      medical: { count: 2, w: .84, h: 1.04 }, food: { count: 3, w: .76, h: .91 },
      cleaning: { count: 3, w: .72, h: .94 }, other: { count: 2, w: .94, h: .9 }
    }[kind];
    for(let i=0;i<profile.count;i++) {
      const x = -2.35 + i * (profile.w + .12);
      addLeaf(`module${i+1}`, x, .36, profile.w, profile.h + .1*i, (leaf,w,h) => {
        if (kind === 'medical') {
          leaf.add(box(`${leaf.name}.crossH`,w*.43,.075,.009,materials.orange,0,0,.06));
          leaf.add(box(`${leaf.name}.crossV`,.075,w*.43,.009,materials.orange,0,0,.06));
        } else if (kind === 'furniture') {
          leaf.add(line(`${leaf.name}.seat`,[[-w*.32,-h*.05],[w*.32,-h*.05],[w*.32,h*.25]],C.ink,.06));
          leaf.add(line(`${leaf.name}.leg`,[[-w*.26,-h*.05],[-w*.26,-h*.34]],C.orange,.06));
        } else if (kind === 'transport') {
          leaf.add(rectangleLines(`${leaf.name}.container`,w*.63,h*.37,.06));
          leaf.children.at(-1).position.y=.1;
          leaf.add(line(`${leaf.name}.route`,[[-w*.3,-h*.31],[w*.3,-h*.31]],C.orange,.06));
        } else if (kind === 'food') {
          leaf.add(box(`${leaf.name}.awning`,w*.78,.11,.009,materials.orange,0,h*.22,.06));
          slats(leaf,w,h,3);
        } else if (kind === 'cleaning') {
          leaf.add(line(`${leaf.name}.vent`,[[-w*.32,.2],[w*.32,.2],[-w*.32,-.08],[w*.32,-.08]],C.ink,.06));
          stripe(leaf,w,h);
        } else if (kind === 'professional') {
          leaf.add(rectangleLines(`${leaf.name}.folio`,w*.6,h*.5,.06));
          leaf.add(line(`${leaf.name}.folioScore`,[[0,-h*.23],[0,h*.23]],C.orange,.063));
        } else { slats(leaf,w,h,kind === 'office' ? 4 : 3); }
      });
    }
  }
  return { group, hinges };
}

export class PaperJourney {
  constructor(scene) {
    this.scene = scene;
    this.root = new THREE.Group(); this.root.name = 'sheetRoot'; scene.add(this.root);
    this.materials = {
      paper: mat(C.paperLight), paperWarm: mat(C.paper), paperEdge: mat(C.paperEdge),
      orange: mat(C.orange,{roughness:.65}), ink: mat(C.ink),
      glass: mat(C.glass,{roughness:.3,metalness:.06,emissive:0x4a260d,emissiveIntensity:.23})
    };
    const m=this.materials, root=this.root;
    m.glassPane=mat(0x60757a,{roughness:.19,metalness:.18,transparent:true,opacity:.46,emissive:0xff7900,emissiveIntensity:.04});
    this.base = box('sheetRoot.base',6,4.6,.075,m.paper,0,0,-.04); root.add(this.base);
    this.sheetOutline=drawnLine('sheetRoot.outline',[[-3,-2.3],[3,-2.3],[3,2.3],[-3,2.3],[-3,-2.3]],C.ink,.005);root.add(this.sheetOutline);
    root.add(line('sheetRoot.verticalScore',[[-1.13,-1.65],[-1.13,1.55]],C.paperEdge,.007,true));
    root.add(line('sheetRoot.cardScore',[[-2.6,1.19],[-1.05,1.19]],C.paperEdge,.007,true));
    root.add(box('registrationMark',.19,.19,.016,m.orange,-2.7,-2.01,.012));
    root.add(line('sheetRoot.editorialInk',[[-2.54,-1.63],[-1.61,-1.63]],C.ink,.009));
    root.add(line('sheetRoot.editorialInk2',[[-2.54,-1.77],[-1.83,-1.77]],C.ink,.009));

    this.cardHinge = new THREE.Group(); this.cardHinge.name='cardHinge'; this.cardHinge.position.set(-1.82,1.18,.045); root.add(this.cardHinge);
    const card=panel('identityCard',1.54,1.02,m.paperWarm,C.ink,.042); card.position.y=.51; this.cardHinge.add(card);
    this.cardOutline=card.children.find(o=>o.name==='identityCard.edge');
    card.add(box('identityCard.portraitDisc',.31,.31,.008,m.paperEdge,-.48,.12,.028));
    card.add(line('identityCard.nameRule',[[-.12,.2],[.6,.2]],C.ink,.03));
    card.add(line('identityCard.contactRule',[[-.12,.02],[.49,.02]],C.ink,.03));
    card.add(line('identityCard.secondaryRule',[[-.58,-.26],[.58,-.26]],C.paperEdge,.03));
    card.add(box('identityCard.marker',.1,.1,.01,m.orange,.57,-.35,.032));
    this.cardMarks=card.children.filter(o=>o.name.startsWith('identityCard.') && (o.name.includes('Rule') || o.name==='identityCard.nameRule'));
    this.cardMarker=card.children.find(o=>o.name==='identityCard.marker');
    this.envelopeLines=[
      drawnLine('identityCard.envelopeOutline',[[-.65,-.37],[.65,-.37],[.65,.34],[-.65,.34],[-.65,-.37]],C.ink,.031),
      drawnLine('identityCard.envelopeFold',[[-.65,.34],[0,-.06],[.65,.34]],C.orange,.033)
    ];this.envelopeLines.forEach(o=>card.add(o));
    this.card=card;
    // The opening stroke occupies the card's eventual place on the same sheet.
    // It remains an actual line while the connected paper geometry grows beneath it.
    this.firstLine=drawnLine('identityCard.firstLetterStroke',[[-2.48,1.22],[-2.48,2.16],[-1.16,2.16],[-1.16,1.22],[-2.48,1.22],[-1.82,1.63],[-1.16,2.16]],C.orange,.085);
    scene.add(this.firstLine);
    this.sleeveLeft=new THREE.Group(); this.sleeveLeft.name='sleeveHinge.left'; this.sleeveLeft.position.set(-2.55,1.18,.07); root.add(this.sleeveLeft);
    this.sleeveLeft.add(polygonPanel('sleeve.leftLeaf',[[0,0],[-.44,.515],[0,1.03]],m.paperEdge));
    this.sleeveRight=new THREE.Group(); this.sleeveRight.name='sleeveHinge.right'; this.sleeveRight.position.set(-1.08,1.18,.076); root.add(this.sleeveRight);
    this.sleeveRight.add(polygonPanel('sleeve.rightLeaf',[[0,0],[.44,.515],[0,1.03]],m.paperEdge));
    this.sleeveFlap=new THREE.Group(); this.sleeveFlap.name='sleeveHinge.bottom'; this.sleeveFlap.position.set(-1.82,1.18,.105); root.add(this.sleeveFlap);
    const flap=polygonPanel('sleeve.bottomLeaf',[[-.73,0],[0,-.66],[.73,0]],m.paperEdge);this.sleeveFlap.add(flap);
    flap.add(box('sleeve.closure',.22,.055,.009,m.orange,0,-.47,.026));
    this.sleeveTop=new THREE.Group();this.sleeveTop.name='sleeveHinge.top';this.sleeveTop.position.set(-1.82,2.21,.112);root.add(this.sleeveTop);
    this.sleeveTop.add(polygonPanel('sleeve.topLeaf',[[-.58,0],[0,.34],[.58,0]],m.paperEdge));
    this.sleeveSeal=box('sleeve.seal',.16,.055,.014,m.orange,-1.82,1.91,.15);root.add(this.sleeveSeal);

    this.front=new THREE.Group(); this.front.name='wallFrontHinge'; this.front.position.set(.53,-.8,.025); root.add(this.front);
    const frontPanel=facadePanel('company.frontWall',2.42,1.25,m.paperWarm,C.ink,[[0,-.07,.46,.79],[-.72,.13,.42,.44],[.72,.13,.42,.44]]); frontPanel.position.y=-.625; this.front.add(frontPanel);
    addWindow(frontPanel,'company.entry',0,-.07,.46,.79,m);
    addWindow(frontPanel,'company.frontWindow.left',-.72,.13,.42,.44,m);
    addWindow(frontPanel,'company.frontWindow.right',.72,.13,.42,.44,m);
    frontPanel.add(box('company.orangeLintel',2.13,.07,.012,m.orange,0,.48,.051));
    frontPanel.add(box('company.orangeLintel.exterior',2.13,.07,.012,m.orange,0,.48,-.051));
    frontPanel.add(box('company.entryCanopy',.76,.15,.05,m.paperEdge,0,-.39,-.087));
    frontPanel.add(box('company.entryPortal.left',.075,.9,.1,m.paperEdge,-.3,-.08,-.09));
    frontPanel.add(box('company.entryPortal.right',.075,.9,.1,m.paperEdge,.3,-.08,-.09));
    frontPanel.add(box('company.entryPortal.header',.72,.095,.13,m.paperEdge,0,.39,-.11));
    for(const side of [-1,1])frontPanel.add(box(`company.entryLight.${side}`,.055,.055,.028,m.orange,side*.43,.35,-.069));
    for(const side of [-1,1])frontPanel.add(box(`company.frontPilaster.${side}`,.065,1.19,.082,m.paperEdge,side*1.12,0,-.06));
    this.back=new THREE.Group(); this.back.name='wallBackHinge'; this.back.position.set(.53,.8,.025); root.add(this.back);
    const backPanel=panel('company.backWall',2.42,1.25,m.paper,C.ink); backPanel.position.y=.625; this.back.add(backPanel);
    addWindow(backPanel,'company.backWindow.left',-.69,-.02,.49,.43,m);
    addWindow(backPanel,'company.backWindow.right',.69,-.02,.49,.43,m);
    backPanel.add(box('company.interiorLobby',.95,.47,.012,mat(0xa88b6a,{roughness:.84}),0,-.28,-.063));
    backPanel.add(box('company.interiorOrangeLine',.83,.028,.013,m.orange,0,-.06,-.073));
    this.lobbyLight=new THREE.PointLight(0xffaa58,0,2.6,2);this.lobbyLight.name='company.interorWarmLight';this.lobbyLight.position.set(.53,-.10,.72);root.add(this.lobbyLight);
    this.receptionHinge=new THREE.Group();this.receptionHinge.name='company.receptionHinge';this.receptionHinge.position.set(.53,-.18,.045);root.add(this.receptionHinge);
    const reception=panel('company.receptionDesk',.62,.28,m.paperEdge,C.ink,.055);reception.position.y=.14;this.receptionHinge.add(reception);
    reception.add(box('company.receptionMark',.14,.035,.009,m.orange,0,0,-.037));
    this.left=new THREE.Group(); this.left.name='wallLeftHinge'; this.left.position.set(-.68,0,.026); root.add(this.left);
    const leftPanel=panel('company.leftWall',1.6,1.25,m.paperWarm,C.ink); leftPanel.rotation.z=Math.PI/2; leftPanel.position.x=-.8; this.left.add(leftPanel);
    addWindow(leftPanel,'company.leftWindow',0,0,.75,.44,m);
    this.right=new THREE.Group(); this.right.name='wallRightHinge'; this.right.position.set(1.74,0,.026); root.add(this.right);
    const rightPanel=panel('company.rightWall',1.6,1.25,m.paperEdge,C.ink); rightPanel.rotation.z=-Math.PI/2; rightPanel.position.x=.8; this.right.add(rightPanel);
    addWindow(rightPanel,'company.rightWindow',0,0,.75,.44,m);
    this.roof=new THREE.Group(); this.roof.name='roofHinge'; this.roof.position.set(0,1.25,.017); this.back.add(this.roof);
    const roofPanel=panel('company.roof',2.56,1.68,m.paperLight,C.ink,.055); roofPanel.position.y=.84; this.roof.add(roofPanel);
    roofPanel.add(line('company.roofScore',[[-1.12,0],[1.12,0]],C.paperEdge,.035,true));
    roofPanel.add(box('company.roofMark',.25,.12,.008,m.orange,.78,.41,.035));
    roofPanel.add(box('company.roofSkylight',.72,.39,.014,m.glass,-.43,.1,-.035));
    roofPanel.add(box('company.roofSkylightMullion',.018,.39,.018,m.ink,-.43,.1,-.048));
    roofPanel.add(box('company.roofFascia.left',.045,1.67,.13,m.paperEdge,-1.26,0,-.01));
    roofPanel.add(box('company.roofFascia.right',.045,1.67,.13,m.paperEdge,1.26,0,-.01));
    roofPanel.add(box('company.roofFascia.front',2.56,.05,.13,m.paperEdge,0,-.82,-.01));
    for(let i=0;i<5;i++)roofPanel.add(box(`company.roofSolar.${i}`,.12,.37,.01,m.glass,.38+i*.14,-.19,-.038));
    this.walls=[this.front,this.back,this.left,this.right];
    this.landscape=[];
    const treeShape=new THREE.Shape();treeShape.moveTo(-.07,0);treeShape.lineTo(-.07,.31);treeShape.lineTo(-.28,.31);treeShape.lineTo(-.18,.59);treeShape.lineTo(-.23,.59);treeShape.lineTo(0,.91);treeShape.lineTo(.23,.59);treeShape.lineTo(.18,.59);treeShape.lineTo(.28,.31);treeShape.lineTo(.07,.31);treeShape.lineTo(.07,0);treeShape.closePath();
    for(const [i,x,y,scale] of [[0,2.28,.42,.9],[1,2.58,1.25,.64]]){
      const hinge=new THREE.Group();hinge.name=`landscape.tree.${i}.baseHinge`;hinge.position.set(x,y,.045);root.add(hinge);
      const tree=new THREE.Mesh(new THREE.ExtrudeGeometry(treeShape,{depth:.035,bevelEnabled:false}),m.paperWarm);tree.name=`landscape.tree.${i}.foldedSilhouette`;tree.scale.setScalar(scale);tree.castShadow=true;tree.receiveShadow=true;hinge.add(tree);
      const cross=new THREE.Mesh(new THREE.ExtrudeGeometry(treeShape,{depth:.03,bevelEnabled:false}),m.paperEdge);
      cross.name=`landscape.tree.${i}.crossFold`;cross.rotation.y=0;cross.scale.setScalar(scale);cross.castShadow=true;cross.receiveShadow=true;hinge.add(cross);hinge.cross=cross;
      hinge.add(box(`landscape.tree.${i}.orangeFoot`,.18,.035,.012,m.orange,0,.03,-.016));
      this.landscape.push(hinge);
    }
    for(let i=0;i<4;i++)root.add(line(`landscape.pathScore.${i}`,[[1.94+i*.16,-1.82],[1.94+i*.16,-.94]],C.paperEdge,.007,true));

    const kinds={
      'IT & Electronics':'it','Office Supplies':'office','Furniture & Fixtures':'furniture',
      'Construction & Maintenance':'construction','Transport & Logistics':'transport',
      'Professional Services':'professional','Medical & Safety':'medical',
      'Food & Hospitality':'food','Cleaning & Facilities':'cleaning',
      'Telecommunications':'telecom','Other':'other'
    };
    this.kits=new Map(Object.entries(kinds).map(([name,kind])=>[name,makeCategory(name,root,m,kind)]));

    this.iris=new PaperIris(root);
    this.selected='IT & Electronics';
    this.update(0,this.selected,1);
  }
  update(progress, category=this.selected, categoryMix=1) {
    const p=clamp(progress);
    const kit=span(p,.70,.83)*categoryMix;
    const emergence=span(p,.085,.16);
    this.root.scale.setScalar(.001+.999*emergence);
    this.firstLine.geometry.setDrawRange(0,Math.floor(this.firstLine.geometry.attributes.position.count*span(p,0,.14)));
    this.firstLine.material.transparent=true;
    this.firstLine.material.opacity=1-span(p,.14,.22);
    this.firstLine.visible=p<.22;
    this.sheetOutline.geometry.setDrawRange(0,Math.floor(this.sheetOutline.geometry.attributes.position.count*span(p,.14,.25)));
    if(this.cardOutline)this.cardOutline.geometry.setDrawRange(0,Math.floor(this.cardOutline.geometry.attributes.position.count*span(p,.13,.22)));
    for(const envelopeLine of this.envelopeLines)envelopeLine.geometry.setDrawRange(0,Math.floor(envelopeLine.geometry.attributes.position.count*span(p,.25,.29)));
    for(const mark of this.cardMarks)mark.geometry.setDrawRange(0,Math.floor(mark.geometry.attributes.position.count*span(p,.15,.24)));
    this.cardMarker.scale.setScalar(.001+.999*span(p,.19,.24));
    const portrait=this.card.getObjectByName('identityCard.portraitDisc');
    portrait.scale.setScalar(.001+.999*span(p,.15,.22));
    const cardLift=span(p,.19,.25)*(1-span(p,.29,.36));
    this.cardHinge.rotation.x=.61*cardLift;
    // Die-cut leaves extend continuously from their existing creases before they fold.
    this.sleeveLeft.scale.y=.001+.999*span(p,.24,.285);
    this.sleeveRight.scale.y=.001+.999*span(p,.25,.295);
    this.sleeveFlap.scale.y=.001+.999*span(p,.25,.295);
    this.sleeveTop.scale.y=.001+.999*span(p,.255,.30);
    const prepare=span(p,.18,.23)*(1-span(p,.26,.30));
    this.sleeveLeft.rotation.y=.13*prepare-Math.PI*span(p,.30,.36);
    this.sleeveRight.rotation.y=-.13*prepare+Math.PI*span(p,.315,.37);
    this.sleeveFlap.rotation.x=Math.PI*span(p,.29,.35);
    this.sleeveTop.rotation.x=Math.PI*span(p,.35,.385);
    this.sleeveSeal.scale.setScalar(.001+.999*span(p,.365,.39));
    const frontRise=span(p,.535,.685),backRise=span(p,.53,.675),sideRise=span(p,.555,.705);
    const anticipate=span(p,.485,.53)*(1-span(p,.53,.56));
    this.front.rotation.x=-.055*anticipate-Math.PI/2*frontRise;
    this.back.rotation.x=.055*anticipate+Math.PI/2*backRise;
    this.left.rotation.y=Math.PI/2*sideRise;
    this.right.rotation.y=-Math.PI/2*span(p,.565,.715);
    this.roof.rotation.x=Math.PI/2*span(p,.655,.76);
    this.receptionHinge.rotation.x=-Math.PI/2*span(p,.61,.70);
    this.lobbyLight.intensity=1.25*span(p,.63,.75);
    for(const [i,tree] of this.landscape.entries()){
      tree.rotation.x=Math.PI/2*span(p,.62+i*.03,.79+i*.02);
      tree.cross.rotation.y=Math.PI/2*span(p,.71+i*.02,.82+i*.01);
    }
    for(const [name,item] of this.kits) {
      item.group.visible=name===category;
      for(const hinge of item.hinges) hinge.rotation.x=Math.PI/2*kit;
    }
    this.iris.update(p);
    this.selected=category;
  }
  dispose(){
    this.root.traverse(o=>{if(o.geometry)o.geometry.dispose();if(o.material){(Array.isArray(o.material)?o.material:[o.material]).forEach(m=>m.dispose());}});
    this.scene.remove(this.root);
  }
}

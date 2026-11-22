// Synthetic example helpers. NOT a replacement for authorized server graph logic.
// Expects already-authorized nodes and server-verified edges. Never call on sealed data.
const byId = (a,b) => String(a).localeCompare(String(b), 'en');
const pair = (a,b) => [a,b].sort(byId);
export function competitionEdges(suppliers, rounds, minimum = 2) {
  if (!Number.isInteger(minimum) || minimum < 1) throw new Error('invalid minimum');
  const allowed = new Set(suppliers.map(x=>x.id));
  const map = new Map();
  for (const r of rounds) {
    if (!r.closed || !r.authorized) continue;
    const ids = [...new Set(r.submittedSupplierIds.filter(x=>allowed.has(x)))].sort(byId);
    for (let i=0;i<ids.length;i++) for(let j=i+1;j<ids.length;j++) {
      const [a,b] = pair(ids[i],ids[j]); const key=JSON.stringify([a,b]);
      if(!map.has(key)) map.set(key,{source:a,target:b,roundIds:new Set()});
      map.get(key).roundIds.add(r.id);
    }
  }
  return [...map.values()].filter(e=>e.roundIds.size>=minimum).map(e=>({source:e.source,target:e.target,type:'joint-participation',classification:'context',count:e.roundIds.size,evidenceIds:[...e.roundIds].sort(byId)}));
}
export function competitionComponents(suppliers, edges) {
  const adj=new Map(suppliers.map(s=>[s.id,new Set()]));
  for (const e of edges) if(e.type==='joint-participation' && adj.has(e.source) && adj.has(e.target)) {adj.get(e.source).add(e.target);adj.get(e.target).add(e.source);}
  const seen=new Set(), groups=[];
  for (const id of [...adj.keys()].sort(byId)) {
    if(seen.has(id)) continue;
    const group=[], stack=[id]; seen.add(id);
    while(stack.length) {const u=stack.pop();group.push(u);for(const v of adj.get(u)) if(!seen.has(v)){seen.add(v);stack.push(v);}}
    groups.push(group.sort(byId));
  }
  return groups.sort((a,b)=>b.length-a.length||byId(a[0],b[0]));
}
export function crossCategoryPairs(suppliers, edges) {
  const categories=new Map(suppliers.map(s=>[s.id,s.category??'uncategorized']));
  const map=new Map();
  for(const e of edges) {
    const x=categories.get(e.source),y=categories.get(e.target);
    if(x===undefined||y===undefined||x===y)continue;
    const [a,b]=pair(x,y), k=JSON.stringify([a,b,e.type]);
    if(!map.has(k))map.set(k,{categories:[a,b],type:e.type,pairs:new Set(),evidenceIds:new Set()});
    const row=map.get(k);row.pairs.add(JSON.stringify(pair(e.source,e.target)));
    for(const id of e.evidenceIds??[])row.evidenceIds.add(id);
  }
  return [...map.values()].map(x=>({categories:x.categories,type:x.type,distinctPairCount:x.pairs.size,evidenceIds:[...x.evidenceIds].sort(byId)}));
}

// Pure reference code to illustrate relationship derivation. No database access.
// Valid evidence must be joined by authorized server logic beforehand.
export function distinctClosedCobidLinks(offers) {
  const roundToSuppliers = new Map();
  for (const o of offers) {
    if (o.roundStatus !== 'Closed' || o.sealed === true || !o.submitted) continue;
    const key = String(o.roundId), set = roundToSuppliers.get(key) || new Set();
    set.add(String(o.supplierId)); roundToSuppliers.set(key,set);
  }
  const pairs = new Map();
  for (const [round, idsSet] of roundToSuppliers) {
    const ids = [...idsSet].sort();
    for (let i=0;i<ids.length;i++) for(let j=i+1;j<ids.length;j++) {
      const key=`${ids[i]}:${ids[j]}`, entry=pairs.get(key)||{source:ids[i],target:ids[j],roundIds:[]};
      entry.roundIds.push(round); pairs.set(key,entry);
    }
  }
  return [...pairs.values()].map(e=>({...e,strength:e.roundIds.length}));
}
// Only a sample model; real submission-state logic depends on the actual domain schema.

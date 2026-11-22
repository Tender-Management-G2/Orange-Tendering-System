import test from 'node:test';
import assert from 'node:assert/strict';
import {competitionEdges,competitionComponents,crossCategoryPairs} from './network-semantics.mjs';
// Fictional example only. No actual procurement records.
const s=[{id:'A',category:'IT'},{id:'B',category:'Office'},{id:'C',category:'IT'},{id:'D',category:'Furniture'}];
const r=[{id:'R1',closed:true,authorized:true,submittedSupplierIds:['A','B','A']},{id:'R2',closed:true,authorized:true,submittedSupplierIds:['A','B']},{id:'R2',closed:true,authorized:true,submittedSupplierIds:['B','A']},{id:'R3',closed:false,authorized:true,submittedSupplierIds:['A','C']},{id:'R4',closed:true,authorized:false,submittedSupplierIds:['A','C']}];
test('distinct authorized closed rounds only',()=>{assert.deepEqual(competitionEdges(s,r),[{source:'A',target:'B',type:'joint-participation',classification:'context',count:2,evidenceIds:['R1','R2']}]);});
test('isolates remain; category not a relationship',()=>{assert.deepEqual(competitionComponents(s,competitionEdges(s,r)),[['A','B'],['C'],['D']]);});
test('category link derives from unique real pairs only',()=>{const e=competitionEdges(s,r);assert.deepEqual(crossCategoryPairs(s,[...e,...e]),[{categories:['IT','Office'],type:'joint-participation',distinctPairCount:1,evidenceIds:['R1','R2']}]);});
test('empty data creates no fake hub or edge',()=>{assert.deepEqual(competitionEdges(s,[]),[]);assert.equal(competitionComponents(s,[]).length,4);assert.deepEqual(crossCategoryPairs(s,[]),[]);});
test('do not combine heterogeneous edge types',()=>{const e=competitionEdges(s,r); const other={...e[0],type:'document-similarity',evidenceIds:['D1']};assert.equal(crossCategoryPairs(s,[...e,other]).length,2);});

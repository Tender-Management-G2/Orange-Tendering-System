import test from 'node:test';
import assert from 'node:assert/strict';
import { distinctClosedCobidLinks } from './graph_semantics.mjs';
test('shared closed round creates one pair',()=>{
 const got=distinctClosedCobidLinks([{roundId:1,roundStatus:'Closed',supplierId:'A',submitted:true},{roundId:1,roundStatus:'Closed',supplierId:'B',submitted:true}]);
 assert.deepEqual(got,[{source:'A',target:'B',roundIds:['1'],strength:1}]);
});
test('same supplier duplicate offers not double-counted',()=>{
 const got=distinctClosedCobidLinks([{roundId:1,roundStatus:'Closed',supplierId:'A',submitted:true},{roundId:1,roundStatus:'Closed',supplierId:'A',submitted:true},{roundId:1,roundStatus:'Closed',supplierId:'B',submitted:true}]);
 assert.equal(got[0].strength,1);
});
test('open and sealed offers are ignored',()=>{
 const got=distinctClosedCobidLinks([{roundId:1,roundStatus:'Open',supplierId:'A',submitted:true},{roundId:1,roundStatus:'Open',supplierId:'B',submitted:true},{roundId:2,roundStatus:'Closed',supplierId:'A',submitted:true,sealed:true},{roundId:2,roundStatus:'Closed',supplierId:'B',submitted:true}]);
 assert.equal(got.length,0);
});

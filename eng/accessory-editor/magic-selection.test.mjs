import test from 'node:test';
import assert from 'node:assert/strict';
import {selectSimilarPixels,selectionBoundary,constrainPixels,eraseSelectedPixels} from './selection-mask.js';

const rgba=colors=>Uint8ClampedArray.from(colors.flat());
const red=[200,20,30,255],blue=[20,40,200,255];
test('click selects native pixels without mutating source, including an isolated one-pixel seed',()=>{
 const source=rgba([blue,blue,blue,blue,red,blue,blue,blue,blue]),before=source.slice();
 const selection=selectSimilarPixels(source,3,3,1.8,1.2,0,true);
 assert.equal(selection.count,1);assert.deepEqual([...selection.mask],[0,0,0,0,1,0,0,0,0]);assert.deepEqual(source,before);
});
test('connected selection uses four-neighbour connectivity; global includes disconnected matching pixels',()=>{
 const source=rgba([red,blue,red,blue,red,blue]);
 assert.deepEqual([...selectSimilarPixels(source,3,2,0,0,0,true).mask],[1,0,0,0,0,0]);
 assert.deepEqual([...selectSimilarPixels(source,3,2,0,0,0,false).mask],[1,0,1,0,1,0]);
});
test('RGBA tolerance is inclusive and transparent colour garbage does not change empty-area selection',()=>{
 const source=rgba([red,[225,20,30,255],[226,20,30,255],[200,20,30,0]]);
 assert.deepEqual([...selectSimilarPixels(source,4,1,0,0,10,true).mask],[1,1,0,0]);
 const transparent=rgba([[1,2,3,0],[253,91,22,0],[1,2,3,1]]);
 assert.deepEqual([...selectSimilarPixels(transparent,3,1,0,0,100,true).mask],[1,1,0]);
 assert.equal(selectSimilarPixels(source,4,1,-1,0,10).count,0);
 assert.throws(()=>selectSimilarPixels(source,4,1,0,0,101));
});
test('border output follows selected pixels and holes, rather than the bounding rectangle',()=>{
 const mask=Uint8Array.from([1,1,1,1,1,1,1,1,0,1,1,1,1,1,1,1]);
 const edge=selectionBoundary(mask,4,4);
 assert.equal(edge[8],0);assert.equal(edge[5],0);assert.equal(edge[9],1);assert.equal(edge[12],1);
 const solid=selectionBoundary(new Uint8Array(25).fill(1),5,5);assert.equal(solid[12],0);assert.equal(solid[0],1);
});
test('raster edit commits and previews preserve every unselected RGBA pixel',()=>{
 const original=rgba([red,blue,[5,6,7,0]]),candidate=rgba([[0,0,0,0],[4,5,6,99],[2,3,4,255]]);
 assert.deepEqual([...constrainPixels(candidate,original,Uint8Array.from([1,0,0]))],[0,0,0,0,...blue,5,6,7,0]);
 assert.throws(()=>constrainPixels(candidate,original,new Uint8Array(2)));
});
test('Delete clears exactly selected pixels; original snapshot can restore exact source data',()=>{
 const source=rgba([red,blue,red,blue]),before=source.slice(),mask=Uint8Array.from([0,1,0,1]);
 eraseSelectedPixels(source,mask);assert.deepEqual([...source],[...red,0,0,0,0,...red,0,0,0,0]);assert.deepEqual([...before],[...red,...blue,...red,...blue]);
});

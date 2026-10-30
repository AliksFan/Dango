import test from 'node:test';
import assert from 'node:assert/strict';
import {appendNode,attachRoot,deleteNodes,moveNodes,restGeometry,SkeletonHistory,validateDocument} from './skeleton-commands.js';

const fixture = () => ({version:1,pivot:{id:'P',x:0,y:0},groups:[
 {id:'A',name:'A',targetId:null,nodes:[{id:'aR',x:10,y:0},{id:'aJ',x:20,y:0},{id:'aE',x:30,y:0}],parameters:{bend:.7,damping:6,restoration:5,gravity:0,elasticity:0}},
 {id:'B',name:'B',targetId:'aJ',nodes:[{id:'bR',x:20,y:0},{id:'bJ',x:20,y:10},{id:'bE',x:20,y:20}],parameters:{bend:.7,damping:6,restoration:5,gravity:0,elasticity:0}},
],layers:[]});
const xy = (doc,id) => { const n=[doc.pivot,...doc.groups.flatMap(g=>g.nodes)].find(n=>n.id===id);return [n.x,n.y]; };
const unchangedAfterThrow = (fn,doc) => {const before=structuredClone(doc);assert.throws(fn);assert.deepEqual(doc,before);};

test('R moves freely; moving a parent J moves a child R only, preserving child joints',()=>{
 const d=fixture();
 const movedRoot=moveNodes(d,['aR'],{x:3,y:4});
 assert.deepEqual(xy(movedRoot,'aR'),[13,4]); assert.deepEqual(xy(movedRoot,'aJ'),[20,0]);
 const movedJoint=moveNodes(d,['aJ'],{x:0,y:5});
 assert.deepEqual(xy(movedJoint,'aJ'),[20,5]); assert.deepEqual(xy(movedJoint,'bR'),[20,5]);
 assert.deepEqual(xy(movedJoint,'bJ'),[20,10]); assert.deepEqual(xy(movedJoint,'bE'),[20,20]);
});

test('P alone holds every node fixed and recomputes free root offsets',()=>{
 const d=fixture(),before=restGeometry(d),next=moveNodes(d,['P'],{x:2,y:3}),after=restGeometry(next);
 assert.deepEqual(next.groups,validateDocument(d).groups);
 assert.deepEqual(before.A.rootOffset,{x:10,y:0}); assert.deepEqual(after.A.rootOffset,{x:8,y:-3});
 assert.deepEqual(after.A.bindingInput,d.groups[0].nodes.map(({x,y})=>({x,y})));
});

test('mixed P/parent/child selection moves shared geometry once; duplicate shared-point aliases deduplicate',()=>{
 const d=fixture(),next=moveNodes(d,['P','aJ','bJ'],{x:2,y:0});
 assert.deepEqual(xy(next,'P'),[2,0]); assert.deepEqual(xy(next,'aJ'),[22,0]);
 assert.deepEqual(xy(next,'bR'),[22,0]); assert.deepEqual(xy(next,'bJ'),[22,10]);
 const aliases=moveNodes(d,['aJ','bR','aJ'],{x:2,y:0});
 assert.deepEqual(xy(aliases,'aJ'),[22,0]);assert.deepEqual(xy(aliases,'bR'),[22,0]);
 assert.deepEqual(xy(aliases,'bJ'),[20,10]);
});

test('attachRoot rejects self and descendant cycles without changing input',()=>{
 const d=fixture();
 unchangedAfterThrow(()=>attachRoot(d,'aR','aJ'),d);
 unchangedAfterThrow(()=>attachRoot(d,'aR','bJ'),d);
});

test('delete reconnects interior joints and terminal suffixes while preserving surviving joints',()=>{
 const d=fixture(),unreferenced=fixture();unreferenced.groups[1].targetId=null;
 assert.deepEqual(deleteNodes(unreferenced,['aE']).groups[0].nodes.map(n=>n.id),['aR','aJ']);
 assert.deepEqual(deleteNodes(unreferenced,['aJ','aE']).groups[0].nodes.map(n=>n.id),['aR']);
 const middle=deleteNodes(unreferenced,['aJ']);
 assert.deepEqual(middle.groups[0].nodes,[unreferenced.groups[0].nodes[0],unreferenced.groups[0].nodes[2]]);
 assert.deepEqual(middle.groups[1],validateDocument(unreferenced).groups[1]);
 const referenced=deleteNodes(d,['aJ']);
 assert.equal(referenced.groups[1].targetId,'aR');assert.deepEqual(xy(referenced,'bR'),xy(d,'aR'));
 for(const id of ['aR','aE','bJ','bE'])assert.deepEqual(xy(referenced,id),xy(d,id));
 const jointInput=fixture();jointInput.groups[1].targetId=null;
 const joint=deleteNodes(jointInput,['bJ','bE']);assert.deepEqual(joint.groups[1].nodes.map(n=>n.id),['bR']);
});

test('multi-delete references reconnect to the nearest surviving predecessor independently of selection and group order',()=>{
 const d=fixture();d.groups[0].nodes.push({id:'aF',x:40,y:0});
 d.groups.push({id:'C',name:'C',targetId:'bR',nodes:[{id:'cR',x:20,y:0},{id:'cJ',x:25,y:15}]});
 d.groups.reverse();const before=structuredClone(d);
 const next=deleteNodes(d,['aE','aJ','aE']);
 assert.deepEqual(next.groups.find(g=>g.id==='A').nodes.map(n=>n.id),['aR','aF']);
 assert.equal(next.groups.find(g=>g.id==='B').targetId,'aR');
 assert.equal(next.groups.find(g=>g.id==='C').targetId,'bR');
 for(const id of ['bR','cR'])assert.deepEqual(xy(next,id),xy(d,'aR'));
 for(const id of ['aR','aF','bJ','bE','cJ'])assert.deepEqual(xy(next,id),xy(d,id));
 assert.deepEqual(d,before);
});

test('deletion can retarget multiple groups and a referenced tail, without losing groups or layers',()=>{
 const d=fixture();d.groups[1].targetId='aE';d.groups[1].nodes[0]={id:'bR',x:30,y:0};
 d.groups.push({id:'C',name:'C',targetId:'aE',nodes:[{id:'cR',x:30,y:0},{id:'cJ',x:35,y:15}]});
 d.layers=[{id:'layerA',groupId:'A'},{id:'layerB',groupId:'B'}];
 const next=deleteNodes(d,['aE']);
 for(const id of ['B','C']){assert.equal(next.groups.find(g=>g.id===id).targetId,'aJ');assert.deepEqual(xy(next,id.toLowerCase()+'R'),xy(d,'aJ'));}
 assert.equal(next.groups.length,3);assert.deepEqual(next.layers,d.layers);
});

test('deletion spanning connected groups retargets references after all selected joints are removed',()=>{
 const d=fixture();d.groups.push({id:'C',name:'C',targetId:'bJ',nodes:[{id:'cR',x:20,y:10},{id:'cJ',x:25,y:15}]});
 const next=deleteNodes(d,['bJ','aJ']);
 assert.equal(next.groups[1].targetId,'aR');assert.equal(next.groups[2].targetId,'bR');
 for(const id of ['bR','cR'])assert.deepEqual(xy(next,id),xy(d,'aR'));
 for(const id of ['aR','aE','bE','cJ'])assert.deepEqual(xy(next,id),xy(d,id));
});

test('separate deletion runs use their own predecessors; clearing joints preserves externally connected groups',()=>{
 const d=fixture();d.groups[0].nodes.push({id:'aF',x:40,y:0},{id:'aG',x:50,y:0});
 d.groups[1].targetId='aF';d.groups[1].nodes[0]={id:'bR',x:40,y:0};
 const separated=deleteNodes(d,['aJ','aF']);
 assert.deepEqual(separated.groups[0].nodes.map(n=>n.id),['aR','aE','aG']);
 assert.equal(separated.groups[1].targetId,'aE');assert.deepEqual(xy(separated,'bR'),xy(d,'aE'));
 const cleared=deleteNodes(d,d.groups[0].nodes.slice(1).map(n=>n.id));
 assert.deepEqual(cleared.groups[0].nodes,[d.groups[0].nodes[0]]);assert.equal(cleared.groups[1].targetId,'aR');
 assert.deepEqual(xy(cleared,'bR'),xy(d,'aR'));assert.deepEqual(cleared.groups[1].nodes.slice(1),d.groups[1].nodes.slice(1));
});

test('delete rejects P, roots, missing nodes and newly zero-length segments atomically',()=>{
 for(const ids of [['P'],['aR'],['aJ','bR'],['missing']]){const d=fixture();unchangedAfterThrow(()=>deleteNodes(d,ids),d);}
 const folded=fixture();folded.groups[1].targetId=null;folded.groups[0].nodes[2].x=10;
 unchangedAfterThrow(()=>deleteNodes(folded,['aJ']),folded);
 const connected=fixture();connected.groups[1].nodes[1]={id:'bJ',x:10,y:0};
 assert.throws(()=>deleteNodes(connected,['aJ']),/相邻骨节不能重合/);
 unchangedAfterThrow(()=>deleteNodes(connected,['aJ']),connected);
});

test('one deletion transaction restores every removed joint and retargeted root in one undo/redo',()=>{
 const h=new SkeletonHistory(fixture()),before=structuredClone(h.document);
 h.commit(deleteNodes(h.document,['aJ']),{before:{selected:['aJ']},after:{selected:[]}});
 const after=structuredClone(h.document);assert.equal(h.past.length,1);
 assert.deepEqual(h.undo(),{selected:['aJ']});assert.deepEqual(h.document,before);
 assert.deepEqual(h.redo(),{selected:[]});assert.deepEqual(h.document,after);
});

test('empty and root-only documents are valid; appendNode only extends a group end',()=>{
 assert.deepEqual(validateDocument({pivot:{id:'P',x:0,y:0},groups:[],layers:[]}).groups,[]);
 const rootOnly={pivot:{id:'P',x:0,y:0},groups:[{id:'A',name:'A',targetId:null,nodes:[{id:'r',x:1,y:1}]}],layers:[]};
 assert.equal(validateDocument(rootOnly).groups[0].nodes.length,1);
 assert.equal(appendNode(rootOnly,'r',{x:2,y:1},'e').groups[0].nodes.length,2);
 unchangedAfterThrow(()=>appendNode(fixture(),'aJ',{x:22,y:0},'fork'),fixture());
});

test('history commits once, undoes once, rejects stale revisions, and ignores no-ops',()=>{
 const h=new SkeletonHistory(fixture()),next=moveNodes(h.document,['aR'],{x:1,y:0});
 assert.equal(h.commit(next,{before:'move',after:'move'}),true);assert.equal(h.revision,1);assert.equal(h.past.length,1);
 assert.equal(h.undo(),'move');assert.deepEqual(h.document,validateDocument(fixture()));assert.equal(h.revision,2);
 assert.throws(()=>h.commit(next,{},1),/修订/);
 const fresh=new SkeletonHistory(fixture());assert.equal(fresh.commit(fixture()),false);assert.equal(fresh.past.length,0);assert.equal(fresh.revision,0);
});

test('same-point propagation reaches a grandchild root without translating either child chain',()=>{
 const d=fixture();d.groups.push({id:'C',name:'C',targetId:'bR',nodes:[{id:'cR',x:20,y:0},{id:'cJ',x:25,y:15}]});
 const next=moveNodes(d,['aJ','bR','cR'],{x:1,y:2});
 for(const id of ['aJ','bR','cR'])assert.deepEqual(xy(next,id),[21,2]);
 for(const id of ['bJ','bE','cJ'])assert.deepEqual(xy(next,id),xy(d,id));
});

test('constraint propagation producing a zero segment rejects the entire edit',()=>{
 const d=fixture();unchangedAfterThrow(()=>moveNodes(d,['aJ'],{x:0,y:10}),d);
 assert.deepEqual(xy(d,'aJ'),[20,0]);assert.deepEqual(xy(d,'bR'),[20,0]);
});

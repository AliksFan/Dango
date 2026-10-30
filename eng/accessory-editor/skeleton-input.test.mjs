import test,{before,after,beforeEach} from 'node:test';
import assert from 'node:assert/strict';
import {chromium} from 'playwright-core';
import {createS0Server} from './s0/server.mjs';

let server,browser,page;const errors=[],requests=[];
before(async()=>{
 server=await createS0Server();browser=await chromium.launch({channel:'msedge',headless:true});
 page=await browser.newPage({viewport:{width:1280,height:850}});page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>requests.push(r.url()));
});
after(async()=>{await browser?.close();if(server)await new Promise(r=>server.close(r));});
beforeEach(async()=>{await page.goto(`http://127.0.0.1:${server.address().port}/`);await page.waitForFunction(()=>!!window.s0);});
const read=()=>page.evaluate(()=>window.s0.inspect());
const node=(s,id)=>[s.document.pivot,...s.document.groups.flatMap(g=>g.nodes)].find(n=>n.id===id);
const group=(s,id)=>s.document.groups.find(g=>g.id===id);
// Browser device-to-SVG matrices retain subpixel floating point error.
const nearPoint=(actual,expected)=>{assert.ok(Math.abs(actual.x-expected.x)<.001,`x: ${actual.x} vs ${expected.x}`);assert.ok(Math.abs(actual.y-expected.y)<.001,`y: ${actual.y} vs ${expected.y}`);};
async function position(p){return page.evaluate(p=>{const q=new DOMPoint(p.x,p.y).matrixTransform(document.querySelector('#surface').getScreenCTM());return {x:q.x,y:q.y};},p);}
async function clickAt(p,options){const q=await position(p);await page.mouse.click(q.x,q.y,options);}
async function clickNode(id,options){await clickAt(node(await read(),id),options);}
async function drag(from,to,{finish=true}={}) {const a=await position(from),b=await position(to);await page.mouse.move(a.x,a.y);await page.mouse.down();await page.mouse.move(b.x,b.y,{steps:6});if(finish)await page.mouse.up();}
async function menu(id,action) {await clickNode(id,{button:'right'});await page.locator(`[data-action="${action}"]`).click();}

test('real clicks select existing points without appending; Alt cycles overlapping P/R/J',async()=>{
 await page.locator('#draw').click();await clickNode('A-J3');let s=await read();assert.deepEqual(s.state.selected,['A-J3']);assert.equal(group(s,'A').nodes.length,4);assert.equal(s.undo,0);
 await page.keyboard.down('Alt');await clickNode('P');await page.keyboard.up('Alt');s=await read();assert.deepEqual(s.state.selected,['P']);
 await page.keyboard.down('Alt');await clickNode('P');await page.keyboard.up('Alt');s=await read();assert.deepEqual(s.state.selected,['A-R']);assert.equal(s.undo,0);
});
test('draw appends once, selects the new point, and dragging it stays in modify with one undo',async()=>{
 await page.locator('#draw').click();await clickNode('A-J3');await clickAt({x:610,y:390});let s=await read();assert.equal(group(s,'A').nodes.length,5);const id=s.state.selected[0];assert.equal(s.state.drawStart,id);assert.equal(s.undo,1);
 await drag(node(s,id),{x:640,y:410});s=await read();assert.equal(s.state.tool,'modify');assert.equal(group(s,'A').nodes.length,5);assert.equal(s.undo,2);nearPoint(node(s,id),{x:640,y:410});
 await page.keyboard.press('Control+z');s=await read();assert.equal(group(s,'A').nodes.length,5);nearPoint(node(s,id),{x:610,y:390});assert.equal(s.undo,1);
 await page.keyboard.press('Control+z');s=await read();assert.equal(group(s,'A').nodes.length,4);
});
test('free R drag and P nudges leave ordinary nodes and synthetic image layers fixed',async()=>{
 await page.locator('[data-group="A"]').click();await page.locator('#surface').focus();const initial=await read();
 await drag(node(initial,'A-R'),{x:190,y:105});let s=await read();nearPoint(node(s,'A-R'),{x:190,y:105});assert.deepEqual(group(s,'A').nodes.slice(1),group(initial,'A').nodes.slice(1));assert.deepEqual(s.document.layers,initial.document.layers);assert.equal(s.undo,1);
 await page.keyboard.press('Control+z');await page.keyboard.press('p');await page.keyboard.press('ArrowRight');s=await read();assert.equal(s.document.pivot.x,151);assert.deepEqual(s.document.groups,initial.document.groups);assert.deepEqual(s.geometry.A.rootOffset,{x:-1,y:0});
});
test('parent joint nudge propagates only the attached child root',async()=>{
 await clickNode('A-J1');const initial=await read();await page.keyboard.press('ArrowRight');const s=await read();assert.equal(node(s,'A-J1').x,281);assert.equal(node(s,'B-R').x,281);assert.deepEqual(group(s,'B').nodes.slice(1),group(initial,'B').nodes.slice(1));assert.equal(s.undo,1);
});
test('attached R keyboard nudge moves the same shared joint as its parent J',async()=>{
 await page.locator('[data-group="B"]').click();await page.locator('#surface').focus();const initial=await read();
 await page.keyboard.press('ArrowLeft');const s=await read();assert.equal(node(s,'A-J1').x,279);assert.equal(node(s,'B-R').x,279);
 assert.deepEqual(group(s,'B').nodes.slice(1),group(initial,'B').nodes.slice(1));assert.equal(s.undo,1);
});
test('P and attached-root mixed selection deduplicates their shared joint instead of conflicting',async()=>{
 await page.locator('[data-group="B"]').click();await page.locator('#surface').focus();await page.keyboard.down('Shift');await clickNode('P');await page.keyboard.up('Shift');const before=await read();assert.deepEqual(before.state.selected,['B-R','P']);
 await page.keyboard.press('ArrowRight');const s=await read();assert.equal(node(s,'P').x,before.document.pivot.x+1);assert.equal(node(s,'A-J1').x,before.document.groups[0].nodes[1].x+1);assert.equal(node(s,'B-R').x,node(s,'A-J1').x);assert.deepEqual(s.state.selected,before.state.selected);assert.equal(s.undo,1);
});
test('blank drag box selects, Shift box appends, multi-selection never chooses a draw start',async()=>{
 await drag({x:380,y:270},{x:530,y:350});let s=await read();assert.deepEqual(s.state.selected,['A-J2','A-J3']);assert.equal(s.undo,0);
 await page.keyboard.down('Shift');await drag({x:305,y:410},{x:335,y:448});await page.keyboard.up('Shift');s=await read();assert.deepEqual(s.state.selected,['A-J2','A-J3','B-J2']);
 await page.keyboard.press('Shift+k');await clickAt({x:650,y:470});s=await read();assert.equal(group(s,'A').nodes.length,4);assert.equal(group(s,'B').nodes.length,3);assert.match(s.message,/多选|单个末端/);assert.equal(s.undo,0);
});
test('dragging a selected chain preserves the set and does not trigger automatic root attachment',async()=>{
 await page.locator('[data-group="A"]').click();await menu('A-R','select-chain');const before=await read();assert.equal(before.state.selected.length,4);
 await drag(node(before,'A-J2'),{x:420,y:305});const s=await read();assert.deepEqual(s.state.selected,before.state.selected);assert.equal(group(s,'A').targetId,null);assert.equal(s.undo,1);
 for(const n of group(before,'A').nodes)nearPoint(node(s,n.id),{x:n.x+20,y:n.y+15});
 nearPoint(node(s,'B-R'),{x:300,y:235});assert.deepEqual(group(s,'B').nodes.slice(1),group(before,'B').nodes.slice(1));
 await page.keyboard.press('Control+z');assert.deepEqual((await read()).document,before.document);
});
test('P/parent/attached-root mixed drag moves each selected point once and keeps child J fixed',async()=>{
 await drag({x:130,y:120},{x:300,y:240});const before=await read();assert.deepEqual(before.state.selected,['P','A-R','A-J1','B-R']);
 await drag(node(before,'P'),{x:170,y:155});const s=await read();assert.deepEqual(s.state.selected,before.state.selected);
 for(const id of before.state.selected){const n=node(before,id);nearPoint(node(s,id),{x:n.x+20,y:n.y+15});}
 assert.deepEqual(group(s,'B').nodes.slice(1),group(before,'B').nodes.slice(1));assert.equal(s.undo,1);
});
test('right click targets context without replacing selection or active group',async()=>{
 await page.locator('[data-group="B"]').click();const before=await read();await clickNode('A-J3',{button:'right'});const s=await read();assert.deepEqual(s.state,before.state);assert.equal(await page.locator('[data-action="delete"]').textContent(),'删除此骨节');
 await page.locator('[data-action="delete"]').click();const deleted=await read();assert.equal(group(deleted,'A').nodes.length,3);assert.deepEqual(deleted.state.selected,['B-R']);assert.equal(deleted.undo,1);
});
test('sticky attach previews and commits the same root-only move in one undo',async()=>{
 await page.locator('[data-group="B"]').click();const before=await read();await menu('B-R','attach');const target=node(before,'A-J3'),p=await position(target);await page.mouse.move(p.x,p.y);
 let s=await read();assert.equal(s.pending,'attach');assert.equal(group(s,'B').targetId,'A-J1');assert.equal(s.preview.groups[1].targetId,'A-J3');const preview=s.preview;
 await page.mouse.click(p.x,p.y);s=await read();assert.deepEqual(s.document,preview);assert.equal(s.pending,null);assert.equal(s.undo,1);assert.deepEqual(group(s,'B').nodes.slice(1),group(before,'B').nodes.slice(1));assert.equal(s.state.activeGroup,'B');
 await page.keyboard.press('Control+z');s=await read();assert.deepEqual(s.document,before.document);assert.equal(s.undo,0);
});
test('invalid sticky clicks stay active; Esc/right cancel restore references and selection',async()=>{
 await page.locator('[data-group="B"]').click();const before=await read();await menu('B-R','attach');await clickNode('B-J1');let s=await read();assert.equal(s.pending,'attach');assert.deepEqual(s.document,before.document);assert.equal(s.undo,0);
 await page.keyboard.press('Escape');s=await read();assert.equal(s.pending,null);assert.deepEqual(s.document,before.document);assert.deepEqual(s.state,before.state);
 await menu('B-R','attach');await clickAt({x:650,y:100},{button:'right'});s=await read();assert.equal(s.pending,null);assert.equal(await page.locator('#menu').isVisible(),false);assert.deepEqual(s.document,before.document);
});
test('attached R drag without a valid new attach candidate moves its shared joint; cancel restores it',async()=>{
 await page.locator('[data-group="B"]').click();const before=await read();await drag(node(before,'B-R'),{x:650,y:100});let s=await read();assert.notDeepEqual(s.document,before.document);assert.equal(node(s,'B-R').x,node(s,'A-J1').x);assert.equal(node(s,'B-R').y,node(s,'A-J1').y);assert.equal(s.undo,1);
 await page.keyboard.press('Control+z');s=await read();assert.deepEqual(s.document,before.document);
 await drag(node(s,'B-R'),node(s,'A-J3'),{finish:false});s=await read();assert.equal(s.candidate.id,'A-J3');const preview=s.preview;await page.mouse.up();s=await read();assert.deepEqual(s.document,preview);assert.equal(s.undo,1);
});
test('Alt switches overlapping attachment candidates once; movement and commit retain the preview identity',async()=>{
 await page.locator('#new-group').click();const s=await read(),root=group(s,s.state.activeGroup).nodes[0].id;await menu(root,'attach');const p=await position({x:280,y:220});await page.mouse.move(p.x,p.y);const first=(await read()).candidate.id;
 await page.keyboard.down('Alt');let preview=await read();assert.notEqual(preview.candidate.id,first);const chosen=preview.candidate.id;
 await page.mouse.move(p.x+.2,p.y+.2);assert.equal((await read()).candidate.id,chosen);
 await page.mouse.click(p.x,p.y);assert.equal((await read()).pending,'attach','Alt selects a candidate without committing it');await page.keyboard.up('Alt');preview=await read();
 await page.mouse.click(p.x,p.y);const after=await read();assert.equal(group(after,s.state.activeGroup).targetId,chosen);assert.deepEqual(after.document,preview.preview);
 // Same candidate freeze on drag release, including release with Alt held.
 await page.keyboard.press('Control+z');const free=await read();await drag(node(free,root),{x:280,y:220},{finish:false});const beforeAlt=(await read()).candidate.id;
 await page.keyboard.down('Alt');const dragPreview=await read();assert.notEqual(dragPreview.candidate.id,beforeAlt);await page.mouse.up();await page.keyboard.up('Alt');assert.deepEqual((await read()).document,dragPreview.preview);
});
test('Esc, capture loss, blur and group switches cancel uncommitted gestures',async()=>{
 await clickNode('A-J3');const before=await read();await drag(node(before,'A-J3'),{x:560,y:360},{finish:false});await page.keyboard.press('Escape');await page.mouse.up();let s=await read();assert.deepEqual(s.document,before.document);assert.equal(s.pending,null);assert.equal(s.undo,0);
 await drag(node(s,'A-J3'),{x:560,y:360},{finish:false});await page.evaluate(()=>{const svg=document.querySelector('#surface');svg.releasePointerCapture(1);});await page.mouse.up();s=await read();assert.deepEqual(s.document,before.document);assert.equal(s.undo,0);
 await drag(node(s,'A-J3'),{x:560,y:360},{finish:false});await page.evaluate(()=>window.dispatchEvent(new Event('blur')));await page.mouse.up();s=await read();assert.deepEqual(s.document,before.document);assert.equal(s.undo,0);
 await page.locator('[data-group="B"]').click();await menu('B-R','attach');await page.locator('[data-group="A"]').click();await clickAt({x:650,y:100});s=await read();assert.equal(s.pending,null);assert.equal(group(s,'B').targetId,'A-J1');assert.equal(s.state.activeGroup,'A');assert.equal(s.undo,0);
});
test('Delete and context deletion reconnect an interior chain identically, with one undo and redo',async()=>{
 await clickNode('A-J2');const before=await read();await page.keyboard.press('Delete');let s=await read();
 const deleted=s.document;assert.deepEqual(group(s,'A').nodes,group(before,'A').nodes.filter(n=>n.id!=='A-J2'));assert.equal(s.undo,1);
 await page.keyboard.press('Control+z');s=await read();assert.deepEqual(s.document,before.document);assert.deepEqual(s.state.selected,before.state.selected);
 await page.keyboard.press('Control+Shift+z');s=await read();assert.deepEqual(s.document,deleted);
 await page.keyboard.press('Control+z');await menu('A-J2','delete');s=await read();assert.deepEqual(s.document,deleted);assert.equal(s.undo,1);
 await page.keyboard.press('Control+z');assert.deepEqual((await read()).document,before.document);
});
test('Delete reconnects externally referenced joints and multi-selection without moving ordinary survivors',async()=>{
 await clickNode('A-J1');const before=await read();await page.keyboard.press('Delete');let s=await read();
 assert.equal(group(s,'B').targetId,'A-R');nearPoint(node(s,'B-R'),node(before,'A-R'));
 for(const id of ['A-R','A-J2','A-J3','B-J1','B-J2'])assert.deepEqual(node(s,id),node(before,id));assert.equal(s.undo,1);
 await page.keyboard.press('Control+z');assert.deepEqual((await read()).document,before.document);
 await clickNode('A-J1');await page.keyboard.down('Shift');await clickNode('A-J2');await page.keyboard.up('Shift');const selection=(await read()).state.selected;
 await menu('A-J1','delete');s=await read();assert.deepEqual(group(s,'A').nodes.map(n=>n.id),['A-R','A-J3']);assert.equal(group(s,'B').targetId,'A-R');assert.equal(s.undo,1);
 await page.keyboard.press('Control+z');s=await read();assert.deepEqual(s.document,before.document);assert.deepEqual(s.state.selected,selection);
});
test('Delete still rejects P atomically and permits tail deletion',async()=>{
 await page.locator('#surface').focus();await page.keyboard.press('p');const before=await read();await page.keyboard.press('Delete');let s=await read();
 assert.deepEqual(s.document,before.document);assert.deepEqual(s.state.selected,before.state.selected);assert.match(s.message,/P 和骨架根/);assert.equal(s.undo,0);
 await clickNode('A-J3');await page.keyboard.press('Delete');s=await read();assert.equal(group(s,'A').nodes.length,3);assert.equal(s.undo,1);
 await page.keyboard.press('Control+z');assert.deepEqual((await read()).document,before.document);
});
test('new empty group has one root; text focus and Enter cannot mutate or submit the specimen',async()=>{
 await page.locator('#new-group').click();let s=await read();assert.equal(s.document.groups.length,3);assert.equal(s.document.groups[2].nodes.length,1);assert.equal(s.document.groups[2].targetId,null);assert.equal(s.document.layers.length,3);
 const before=s.document;await page.locator('#note').fill('K p test');await page.keyboard.press('ArrowLeft');await page.keyboard.press('Delete');await page.keyboard.press('Enter');s=await read();assert.deepEqual(s.document,before);assert.equal(s.state.tool,'modify');
 await page.locator('#surface').focus();await page.keyboard.press('Shift+k');await page.keyboard.press('Enter');s=await read();assert.equal(s.state.drawStart,null);assert.equal(s.undo,1);assert.deepEqual(s.document,before);
});
test('canvas shortcuts and text/IME input stay inside the editor input domain',async()=>{
 await page.evaluate(()=>{window.hostKeys=[];document.addEventListener('keydown',e=>window.hostKeys.push(e.key));});
 await clickNode('A-J3');await page.keyboard.press('ArrowRight');await page.keyboard.press('Shift+k');await page.keyboard.press('Enter');await page.locator('#note').fill('text');await page.keyboard.press('Delete');await page.keyboard.press('p');
 assert.deepEqual(await page.evaluate(()=>window.hostKeys),[]);
});
test('external revisions cancel actual drag and sticky pointer gestures without rolling back the new revision',async()=>{
 // Isolated consumer fixture: revision injection represents another editor command,
 // while gesture input itself still comes through browser pointer events.
 await page.evaluate(async()=>{
  const {SkeletonHistory,moveNodes,deleteNodes,locate}=await import('/skeleton-commands.js'),{installSkeletonInput}=await import('/skeleton-input.js');
  const scope=document.createElement('section');scope.dataset.skeletonScope='';scope.style='position:fixed;inset:0;background:white;z-index:10';
  scope.innerHTML='<svg id="revision-surface" tabindex="0" width="800" height="540" viewBox="0 0 800 540"></svg><button id="revision-attach">挂接</button>';document.body.append(scope);
  const history=new SkeletonHistory(window.s0.inspect().document),surface=scope.querySelector('svg'),controller=installSkeletonInput({surface,history,onChange(frame){if(frame.context?.target?.kind==='node')locate(frame.committed,frame.context.target.id);}});
  scope.querySelector('button').onclick=()=>{controller.command('attach');surface.focus();};
  window.lifecycle={history,controller,change:()=>history.commit(moveNodes(history.document,['P'],{x:1,y:0})),remove:id=>history.commit(deleteNodes(history.document,[id]))};
 });
 const r=await page.locator('#revision-surface').boundingBox();
 await page.mouse.click(r.x+510,r.y+330);await page.mouse.move(r.x+510,r.y+330);await page.mouse.down();await page.mouse.move(r.x+550,r.y+360,{steps:4});
 await page.evaluate(()=>window.lifecycle.change());let s=await page.evaluate(()=>window.lifecycle.controller.inspect());assert.equal(s.pending,null);assert.equal(s.document.pivot.x,151);const revised=s.document;await page.mouse.up();assert.deepEqual((await page.evaluate(()=>window.lifecycle.controller.inspect())).document,revised);
 await page.mouse.click(r.x+150,r.y+140,{button:'right'});await page.locator('#revision-attach').click();s=await page.evaluate(()=>window.lifecycle.controller.inspect());assert.equal(s.pending,'attach');
 await page.mouse.move(r.x+320,r.y+430);await page.evaluate(()=>window.lifecycle.change());s=await page.evaluate(()=>window.lifecycle.controller.inspect());assert.equal(s.pending,null);const after=s.document;await page.mouse.click(r.x+320,r.y+430);assert.deepEqual((await page.evaluate(()=>window.lifecycle.controller.inspect())).document,after);
 await page.mouse.click(r.x+510,r.y+330);await page.mouse.click(r.x+510,r.y+330,{button:'right'});
 await page.evaluate(()=>window.lifecycle.remove('A-J3'));s=await page.evaluate(()=>window.lifecycle.controller.inspect());assert.deepEqual(s.state.selected,[]);assert.equal(s.state.drawStart,null);
 await page.evaluate(()=>{window.lifecycle.controller.command('delete');window.lifecycle.controller.setTool('draw');});s=await page.evaluate(()=>window.lifecycle.controller.inspect());assert.equal(group(s,'A').nodes.length,3);assert.equal(s.pending,null);
 await page.evaluate(()=>window.lifecycle.controller.dispose());
});
test('zoom and temporary Space pan keep arrow movement in output image pixels',async()=>{
 await clickNode('A-J3');const before=await read(),p=await position(node(before,'A-J3'));await page.mouse.move(p.x,p.y);await page.mouse.wheel(0,-240);await page.keyboard.press('Shift+ArrowRight');let s=await read();assert.equal(node(s,'A-J3').x,520);assert.equal(node(s,'A-J3').y,330);
 const doc=s.document;await page.keyboard.down('Space');await drag({x:600,y:300},{x:640,y:330});await page.keyboard.up('Space');s=await read();assert.deepEqual(s.document,doc);assert.notDeepEqual(s.view,before.view);
});
test('sample requests remain local and no JS runtime errors occurred',async()=>{
 assert.deepEqual(errors,[]);assert.ok(requests.length);assert.ok(requests.every(url=>url.startsWith(`http://127.0.0.1:${server.address().port}/`)));
});

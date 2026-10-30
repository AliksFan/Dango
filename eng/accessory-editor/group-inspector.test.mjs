import test,{before,after,beforeEach} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';import path from 'node:path';import http from 'node:http';import {fileURLToPath} from 'node:url';
import {chromium} from 'playwright-core';

const here=path.dirname(fileURLToPath(import.meta.url)),root=path.resolve(here,'../../src/Dango.Desktop/Accessories/Editing/Web');
let server,browser,page;
before(async()=>{
 server=http.createServer((req,res)=>{const file=path.join(root,req.url==='/'?'index.html':decodeURIComponent(req.url).replace(/^\/+/,''));res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html');try{res.end(fs.readFileSync(file));}catch{res.writeHead(404);res.end();}});await new Promise(r=>server.listen(0,'127.0.0.1',r));
 browser=await chromium.launch({channel:'msedge',headless:true});page=await browser.newPage({viewport:{width:1280,height:850}});
});
after(async()=>{await browser?.close();if(server)await new Promise(r=>server.close(r));});
beforeEach(async()=>{
 await page.setViewportSize({width:1280,height:850});await page.goto(`http://127.0.0.1:${server.address().port}/`);await page.waitForFunction(()=>!!window.DangoMiniPaint,{},{timeout:10000});
 await page.evaluate(async()=>{const c=document.createElement('canvas');c.width=400;c.height=300;c.getContext('2d').fillStyle='#885e3e';c.getContext('2d').fillRect(40,40,300,220);await DangoMiniPaint.load({width:400,height:300,pngBase64:c.toDataURL().split(',')[1]});});
});

async function loadLayers(groups,assignments){
 await page.evaluate(async({groups,assignments})=>{
  const p=DangoMiniPaint.exportProject(),asset=p.assets[0],template=p.layers[0];p.layers=[];p.skeleton.layers=[];p.skeleton.groups=[];p.renderPieces=[];
  for(let i=1;i<=assignments.length;i++){const groupId=assignments[i-1];p.layers.push({...template,id:i,name:`图层 ${i}`,order:i,assetId:asset.id});p.skeleton.layers.push({id:String(i),groupId});}
  let previous=Symbol('start');for(const groupId of assignments){if(groupId!==previous)p.renderPieces.push({groupId,assetId:asset.id});previous=groupId;}
  const parameters={bend:.5,damping:5,restoration:5,gravity:0,elasticity:0};
  p.skeleton.groups=groups.map((g,i)=>({id:g.id,name:g.name,targetId:null,nodes:[{id:`${g.id}-root`,x:40+i*20,y:50}],parameters:{...parameters}}));
  await DangoMiniPaint.load(p);
 },{groups,assignments});
}
async function revealLayer(id){
 const row=page.locator(`[data-image-layer="${id}"]`);
 const group=await row.getAttribute('data-layer-group-name');
 if(!await row.isVisible()){
  const toggle=row.locator('xpath=ancestor::*[contains(@class,"layer-group")][1]').locator('[data-group-toggle]');
  if(await toggle.count()&&await toggle.getAttribute('aria-expanded')==='false')await toggle.click();
 }
 await row.scrollIntoViewIfNeeded();assert.equal(await row.isVisible(),true,`Layer ${id} must be reachable by scrolling`);
}

test('inspector switches form a right-side vertical rail with usable targets, arrow navigation and collapse restore',async()=>{
 const inspect=()=>page.evaluate(()=>{const p=document.querySelector('#properties_panel'),r=p.getBoundingClientRect(),body=p.querySelector('.panel-body').getBoundingClientRect(),tabs=document.querySelector('.inspector-tabs'),t=tabs.getBoundingClientRect();return {panel:{left:r.left,right:r.right},bodyRight:body.right,tabs:{left:t.left,right:t.right,top:t.top,bottom:t.bottom},orientation:tabs.getAttribute('aria-orientation'),buttons:[...tabs.querySelectorAll('[role="tab"]')].map(b=>{const q=b.getBoundingClientRect();return {id:b.id,top:q.top,left:q.left,right:q.right,width:q.width,height:q.height,selected:b.getAttribute('aria-selected')};})};});
 let state=await inspect();assert.equal(state.orientation,'vertical');assert.ok(state.tabs.left>=state.bodyRight-1&&state.tabs.right<=state.panel.right,'tab rail belongs at the right edge beside the inspector content');assert.ok(state.buttons.every(b=>b.width>=32&&b.height>=32),'both controls need 32px hit targets');assert.ok(state.buttons[1].top>state.buttons[0].top);
 assert.equal(state.buttons[0].id,'layers_tab');assert.equal(state.buttons[1].id,'skeleton_tab');
 await page.locator('#layers_tab').focus();await page.keyboard.press('ArrowDown');assert.equal(await page.locator('#skeleton_tab').getAttribute('aria-selected'),'true');await page.keyboard.press('ArrowUp');assert.equal(await page.locator('#layers_tab').getAttribute('aria-selected'),'true');
 const before=state.panel;await page.locator('[data-panel="properties_panel"]').click();assert.equal(await page.locator('#properties_panel').getAttribute('data-expanded'),'false');
 await page.locator('#skeleton_tab').click();state=await inspect();assert.equal(await page.locator('#properties_panel').getAttribute('data-expanded'),'true');assert.ok(state.panel.right>before.right-1,'expanded inspector restores its right edge');assert.equal(state.buttons[0].id,'layers_tab');assert.equal(await page.locator('#skeleton_tab').getAttribute('aria-selected'),'true');
});

test('grouped layers are actual nested children, ungrouped layers stay at root, and ownership text follows selection',async()=>{
 await loadLayers([{id:'A',name:'甲组'},{id:'B',name:'乙组'}],['A','A',null,'B']);await page.locator('#layers_tab').click();
 const tree=await page.evaluate(()=>({groups:[...document.querySelectorAll('#layers > .layer-group')].map(g=>({id:g.dataset.layerGroup,title:g.querySelector('.skeleton-group-row')?.textContent,children:[...g.querySelectorAll(':scope > .layer-group-children > [data-image-layer]')].map(n=>n.dataset.imageLayer),count:g.querySelector('.skeleton-group-row')?.textContent})),root:[...document.querySelectorAll('#layers > [data-image-layer]')].map(n=>n.dataset.imageLayer)}));
 assert.deepEqual(tree.groups.map(g=>g.id).sort(),['A','B']);assert.deepEqual(tree.groups.find(g=>g.id==='A').children.sort(),['1','2']);assert.deepEqual(tree.groups.find(g=>g.id==='B').children,['4']);assert.deepEqual(tree.root,['3']);
 assert.match(tree.groups.find(g=>g.id==='A').count,/甲组/);assert.match(tree.groups.find(g=>g.id==='A').count,/2/);
 await page.waitForFunction(()=>{const p=document.querySelector('#properties_panel .panel-body'),r=p.getBoundingClientRect();return [...p.querySelectorAll('button,input,select')].filter(n=>n.checkVisibility()&&!n.closest('#layers')).every(n=>{const q=n.getBoundingClientRect();return q.left>=r.left-1&&q.right<=r.right+1&&q.bottom<=r.bottom+1;});});
 assert.equal(await page.locator('#layer_parameters #details_base').count(),1,'base layer parameters belong to their separate right-hand page');
 await revealLayer('1');await page.locator('[data-image-layer="1"] #layer_name').click();assert.match(await page.locator('#layer_ownership').textContent(),/甲组/);
 await revealLayer('3');await page.locator('[data-image-layer="3"] #layer_name').click();assert.match(await page.locator('#layer_ownership').textContent(),/未分组/);
 assert.ok(await page.locator('[data-image-layer="1"]').evaluate(n=>n.closest('.layer-group')?.dataset.layerGroup==='A'),'a misleading flat row must fail ownership hierarchy');
});

test('minimum viewport keeps sixteen grouped layers reachable in one scrolling list',async()=>{
 await page.setViewportSize({width:784,height:548});await page.waitForFunction(()=>document.querySelector('.unified-editor')?.dataset.short==='true');
 await loadLayers([{id:'A',name:'长组'}],Array(16).fill('A'));await page.locator('#layers_tab').click();
 assert.equal(await page.locator('#layers_previous,#layers_next').count(),0);
 const initial=await page.evaluate(()=>{const list=document.querySelector('#layers'),actions=document.querySelector('.layer-actions');return {overflow:getComputedStyle(list).overflowY,height:list.clientHeight,scroll:list.scrollHeight,actionTop:actions.getBoundingClientRect().top};});
 assert.ok(initial.scroll>initial.height);assert.match(initial.overflow,/auto|scroll/);
 for(let id=1;id<=16;id++){await revealLayer(String(id));assert.equal(await page.locator(`[data-image-layer="${id}"]`).evaluate(n=>n.closest('.layer-group').dataset.layerGroup),'A');}
 await page.locator('#layers').hover();await page.mouse.wheel(0,800);await page.waitForFunction(()=>document.querySelector('#layers').scrollTop>0);
 const final=await page.evaluate(()=>{const list=document.querySelector('#layers'),panel=document.querySelector('#properties_panel'),body=panel.querySelector('.panel-body'),tabs=document.querySelector('.inspector-tabs');return {scroll:list.scrollTop,horizontal:list.scrollWidth-list.clientWidth,actionTop:document.querySelector('.layer-actions').getBoundingClientRect().top,tabsRight:tabs.getBoundingClientRect().right,panelRight:panel.getBoundingClientRect().right,bodyScroll:body.scrollHeight-body.clientHeight};});
 assert.ok(final.scroll>0);assert.ok(final.horizontal<=1);assert.equal(final.actionTop,initial.actionTop);assert.ok(final.tabsRight<=final.panelRight+1);assert.ok(final.bodyScroll<=1,'only list scrolls, not the whole inspector');
});

test('assign, detach and undo preserve nested DOM ownership and the composite PNG',async()=>{
 await loadLayers([{id:'A',name:'甲组'}],[null]);const before=await page.evaluate(()=>({png:DangoMiniPaint.exportPng().pngBase64,project:DangoMiniPaint.exportProject()}));
 await page.locator('#layers_tab').click();await page.locator('[data-group-id="A"]').click();assert.equal(await page.locator('#layers_tab').getAttribute('aria-selected'),'true','selecting a folder must keep its member layers visible');
 await page.locator('#layer_properties_tab').click();const assignDetails=page.locator('#assign_group').locator('xpath=ancestor::details[1]');if(!await assignDetails.evaluate(n=>n.open))await assignDetails.locator(':scope > summary').click();await page.locator('#assign_group').click();
 let state=await page.evaluate(()=>({groupId:DangoMiniPaint.getSkeleton().layers.find(l=>l.id==='1').groupId,parent:document.querySelector('[data-image-layer="1"]')?.closest('.layer-group')?.dataset.layerGroup,png:DangoMiniPaint.exportPng().pngBase64}));assert.equal(state.groupId,'A');assert.equal(state.parent,'A');assert.equal(state.png,before.png);
 const ungroupDetails=page.locator('#ungroup_layer').locator('xpath=ancestor::details[1]');if(!await ungroupDetails.evaluate(n=>n.open))await ungroupDetails.locator(':scope > summary').click();await page.locator('#ungroup_layer').click();state=await page.evaluate(()=>({groupId:DangoMiniPaint.getSkeleton().layers.find(l=>l.id==='1').groupId,parent:document.querySelector('[data-image-layer="1"]')?.parentElement?.id,png:DangoMiniPaint.exportPng().pngBase64}));assert.equal(state.groupId,null);assert.equal(state.parent,'layers');assert.equal(state.png,before.png);
 await page.locator('#undo_button').click();state=await page.evaluate(()=>({groupId:DangoMiniPaint.getSkeleton().layers.find(l=>l.id==='1').groupId,parent:document.querySelector('[data-image-layer="1"]')?.closest('.layer-group')?.dataset.layerGroup,png:DangoMiniPaint.exportPng().pngBase64}));assert.equal(state.groupId,'A');assert.equal(state.parent,'A');assert.equal(state.png,before.png);
 await page.locator('#undo_button').click();state=await page.evaluate(()=>({groupId:DangoMiniPaint.getSkeleton().layers.find(l=>l.id==='1').groupId,parent:document.querySelector('[data-image-layer="1"]')?.parentElement?.id,png:DangoMiniPaint.exportPng().pngBase64}));assert.equal(state.groupId,null);assert.equal(state.parent,'layers');assert.equal(state.png,before.png);
});

test('right-side page choices switch between layers, skeleton parameters and layer parameters',async()=>{
 assert.equal(await page.locator('#parameter_dock').count(),0);
 const controls=await page.locator('.inspector-tabs button:visible').evaluateAll(nodes=>nodes.map(n=>n.id));
 assert.deepEqual(controls,['layers_tab','skeleton_tab','layer_properties_tab']);
 await page.locator('#skeleton_tab').click();
 assert.equal(await page.locator('#skeleton_tab').getAttribute('aria-selected'),'true');
 assert.equal(await page.locator('#skeleton_panel').isVisible(),true);
 assert.equal(await page.locator('#layers_panel').isVisible(),false);
 assert.equal(await page.locator('#properties_panel #skeleton_panel').count(),1);
 await page.locator('#layer_properties_tab').click();assert.equal(await page.locator('#layer_parameters').isVisible(),true);
});

test('six layer actions stay on one visible, unclipped row at minimum and standard widths',async()=>{
 await page.locator('#layers_tab').click();
 for(const width of [784,1280]){
  await page.setViewportSize({width,height:850});
  const state=await page.evaluate(()=>{
   const body=document.querySelector('#properties_panel .panel-body'),bounds=body.getBoundingClientRect(),ids=['insert_layer','new_group','layer_duplicate','layer_raster','layer_down','layer_up'];
   const buttons=ids.map(id=>{const n=document.querySelector(`#${id}`),r=n?.getBoundingClientRect();return {id,visible:!!n?.checkVisibility(),top:r?.top,left:r?.left,right:r?.right,width:r?.width,height:r?.height,bodyLeft:bounds.left,bodyRight:bounds.right};});
   return {buttons};
  });
  assert.ok(state.buttons.every(b=>b.visible),`${width}px viewport shows all six actions`);
  assert.ok(state.buttons.every(b=>Math.abs(b.top-state.buttons[0].top)<1),`${width}px viewport keeps all six actions on one row`);
  assert.ok(state.buttons.every(b=>b.left>=b.bodyLeft-1&&b.right<=b.bodyRight+1),`${width}px viewport does not clip actions`);
 }
});

test('row trash buttons sit at the right edge; visibility and exact-group deletion preserve members with undo',async()=>{
 await loadLayers([{id:'A',name:'甲组'},{id:'B',name:'乙组'}],['A','B']);
 await page.locator('#layers_tab').click();await revealLayer('1');
 const visibility=page.locator('[data-image-layer="1"] .visibility');
 await visibility.click();
 assert.equal(await visibility.getAttribute('data-editor-icon'),'eyeClosed');
 assert.match(await visibility.getAttribute('aria-label'),/图层/);
 assert.equal((await page.evaluate(()=>DangoMiniPaint.getSkeleton().layers[0].id)), '1');
 await visibility.click();
 assert.equal(await visibility.getAttribute('data-editor-icon'),'eye');

 const rowGeometry=await page.evaluate(()=>{
  const group=document.querySelector('[data-layer-group="B"] > .skeleton-group-row'),layer=document.querySelector('[data-image-layer="2"]');
  const groupTrash=group.querySelector('[data-delete-group="B"]'),layerTrash=layer.querySelector('.delete');
  const gr=group.getBoundingClientRect(),lr=layer.getBoundingClientRect();
  return {groupLast:group.lastElementChild===groupTrash,layerLast:layer.lastElementChild===layerTrash,groupRight:groupTrash.getBoundingClientRect().right-group.getBoundingClientRect().left,groupWidth:gr.width,layerRight:layerTrash.getBoundingClientRect().right-layer.getBoundingClientRect().left,layerWidth:lr.width,groupPath:groupTrash.querySelector('svg path')?.getAttribute('d'),layerPath:layerTrash.querySelector('svg path')?.getAttribute('d')};
 });
 assert.equal(rowGeometry.groupLast,true,'group trash is the final group-row child');assert.equal(rowGeometry.layerLast,true,'layer trash is the final layer-row child');
 assert.ok(Math.abs(rowGeometry.groupWidth-rowGeometry.groupRight)<1,'group trash is aligned to the row right edge');assert.ok(Math.abs(rowGeometry.layerWidth-rowGeometry.layerRight)<1,'layer trash is aligned to the row right edge');
 assert.ok(rowGeometry.groupPath);assert.equal(rowGeometry.groupPath,rowGeometry.layerPath,'group and layer trash buttons reuse the same SVG path');

 await page.locator('[data-group-id="A"]').click();
 const deleteButton=page.locator('[data-delete-group="B"]');
 assert.equal(await deleteButton.isVisible(),true,'each group row owns a direct visible delete action');
 await deleteButton.click();
 let state=await page.evaluate(()=>({groups:DangoMiniPaint.getSkeleton().groups.map(g=>g.id),layers:DangoMiniPaint.getSkeleton().layers.map(l=>l.id),owners:DangoMiniPaint.getSkeleton().layers.map(l=>l.groupId)}));
 assert.deepEqual(state,{groups:['A'],layers:['1','2'],owners:['A',null]});
 await page.locator('#undo_button').click();
 state=await page.evaluate(()=>({groups:DangoMiniPaint.getSkeleton().groups.map(g=>g.id),layers:DangoMiniPaint.getSkeleton().layers.map(l=>l.id),owners:DangoMiniPaint.getSkeleton().layers.map(l=>l.groupId)}));
 assert.deepEqual(state,{groups:['A','B'],layers:['1','2'],owners:['A','B']});
});

import test,{before,after,beforeEach} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';import path from 'node:path';import http from 'node:http';import {fileURLToPath} from 'node:url';
import {chromium} from 'playwright-core';
const here=path.dirname(fileURLToPath(import.meta.url)),root=process.env.DANGO_EDITOR_TEST_RESOURCES||path.resolve(here,'../../src/Dango.Desktop/Accessories/Editing/Web');
let server,browser,page;
before(async()=>{server=http.createServer((req,res)=>{const file=path.join(root,req.url==='/'?'index.html':decodeURIComponent(req.url).replace(/^\/+/,''));res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html');try{res.end(fs.readFileSync(file));}catch{res.writeHead(404);res.end();}});await new Promise(r=>server.listen(0,'127.0.0.1',r));browser=await chromium.launch({channel:'msedge',headless:true});page=await browser.newPage({viewport:{width:1104,height:668}});});
after(async()=>{await browser?.close();if(server)await new Promise(r=>server.close(r));});
beforeEach(async()=>{await page.setViewportSize({width:1104,height:668});await page.goto(`http://127.0.0.1:${server.address().port}/`);await page.waitForFunction(()=>!!window.DangoMiniPaint);await page.evaluate(async()=>{const c=document.createElement('canvas');c.width=400;c.height=300;c.getContext('2d').fillRect(50,50,300,200);await DangoMiniPaint.load({width:400,height:300,pngBase64:c.toDataURL().split(',')[1]});const s=DangoMiniPaint.getSkeleton();s.groups=[{id:'A',name:'测试组',targetId:null,nodes:[{id:'AR',x:200,y:150}],parameters:{bend:.5,damping:6,restoration:5,gravity:0,elasticity:0}}];s.layers[0].groupId='A';await DangoMiniPaint.setSkeleton(s,{reset:true});});});

test('fine parameters remain expanded and usable when their static heading is clicked',async()=>{
 await page.locator('[data-group-id="A"]').click();
 await page.locator('#skeleton_tab').click();
 assert.equal(await page.locator('.fine-parameters > summary').count(),0);
 for(let i=0;i<3;i++){
  await page.locator('.fine-heading').click();
  await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));
  assert.equal(await page.locator('input[type=number][data-skeleton-param]').evaluateAll(nodes=>nodes.every(n=>n.checkVisibility())),true);
  await page.locator('[data-skeleton-param="damping"][type="number"]').fill(String(7+i));await page.locator('[data-skeleton-param="damping"][type="number"]').press('Tab');
 }
 assert.equal((await page.evaluate(()=>DangoMiniPaint.getSkeleton().groups[0].parameters.damping)),9);
});

test('three right-hand pages keep the whole layer page for layers and both parameter pages in the inspector',async()=>{
 await page.locator('[data-group-id="A"]').click();
 await page.locator('#skeleton_tab').click();
 assert.equal(await page.locator('#layers_panel').isVisible(),false);assert.equal(await page.locator('#properties_panel #group_fields').isVisible(),true);
 assert.equal(await page.locator('#parameter_dock').count(),0);assert.equal(await page.locator('#group_name').count(),0);
 await page.locator('#layer_properties_tab').click();assert.equal(await page.locator('#properties_panel #details_base').isVisible(),true);assert.equal(await page.locator('#skeleton_panel').isVisible(),false);
 assert.ok(await page.locator('#toggle_details .reset').count()>0);assert.ok(await page.locator('#toggle_details .reset').evaluateAll(nodes=>nodes.every(n=>n.querySelector('svg path')&&n.getAttribute('aria-label')==='复位')));
 assert.ok(await page.locator('#toggle_details .reset').evaluateAll(nodes=>nodes.every(n=>getComputedStyle(n).color!=='rgba(0, 0, 0, 0)')),'reset icons must have visible ink');
 await page.locator('#layers_tab').click();assert.equal(await page.locator('#details_base').isVisible(),false);
 const geometry=await page.evaluate(()=>{const body=document.querySelector('#properties_panel .panel-body'),list=document.querySelector('#layers');return {body:body.getBoundingClientRect().toJSON(),list:list.getBoundingClientRect().toJSON()};});
 assert.ok(geometry.list.height>=geometry.body.height-110,'remaining layer-page space belongs to the list');
});

test('collapsed palette and object rails share their edge and all three page icons remain reachable',async()=>{
 const expanded=await page.evaluate(()=>['colors_panel','properties_panel'].map(id=>document.getElementById(id).getBoundingClientRect().left));assert.ok(Math.abs(expanded[0]-expanded[1])<1,'expanded panels also share the same left edge');
 await page.locator('[data-panel="colors_panel"]').click();await page.locator('[data-panel="properties_panel"]').click();
 await page.waitForFunction(()=>{const a=document.querySelector('#colors_panel').getBoundingClientRect(),b=document.querySelector('#properties_panel').getBoundingClientRect();return Math.abs(a.left-b.left)<1;});
 const state=await page.evaluate(()=>{const p=document.querySelector('[data-panel="colors_panel"]').getBoundingClientRect(),tabs=[...document.querySelectorAll('.inspector-tabs button')].map(n=>({id:n.id,r:n.getBoundingClientRect().toJSON(),visible:n.checkVisibility()}));return {p:p.toJSON(),tabs};});
 assert.deepEqual(state.tabs.map(t=>t.id),['layers_tab','skeleton_tab','layer_properties_tab']);assert.ok(state.tabs.every(t=>t.visible&&Math.abs(t.r.x-state.p.x)<1&&t.r.right<=1104));
 await page.locator('#layer_properties_tab').click();assert.equal(await page.locator('#layer_parameters').isVisible(),true);
});

test('minimum window keeps basic and fine skeleton values visible together without scrolling',async()=>{
 await page.setViewportSize({width:784,height:548});await page.locator('[data-group-id="A"]').click();await page.locator('#skeleton_tab').click();
 for(let repeat=0;repeat<2;repeat++){
  await page.locator('.fine-heading').click();
  await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));
  const clipped=await page.evaluate(()=>{const p=document.querySelector('#properties_panel .panel-body'),r=p.getBoundingClientRect();return [...p.querySelectorAll('button,input,select,summary')].filter(n=>n.checkVisibility()).filter(n=>{const q=n.getBoundingClientRect();return q.right>r.right+1||q.bottom>r.bottom+1;}).map(n=>n.outerHTML.slice(0,120));});
  assert.deepEqual(clipped,[]);assert.equal(await page.locator('#properties_panel #group_fields').count(),1);
  assert.equal(await page.locator('input[type=number][data-skeleton-param]').evaluateAll(nodes=>nodes.every(n=>n.checkVisibility())),true);
 }
});

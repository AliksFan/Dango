import test,{before,after,beforeEach} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';import path from 'node:path';import http from 'node:http';import {fileURLToPath} from 'node:url';
import {build} from 'esbuild';import {chromium} from 'playwright-core';
import {controlledOverrides} from './controlled-overrides.mjs';
import {moveGroup} from './layer-drag.js';
const here=path.dirname(fileURLToPath(import.meta.url));let server,browserServer,browser,page;
before(async()=>{
 const built=await build({plugins:[controlledOverrides],absWorkingDir:here,entryPoints:['entry.js'],bundle:true,target:'chrome126',outfile:path.join(here,'test-output/bundle.js'),inject:['./jquery-inject.js'],alias:{'dango-notify':path.join(here,'notify.js')},write:false});
 const files=new Map(built.outputFiles.map(f=>[path.extname(f.path)==='.css'?'theme.css':'bundle.js',f.text.replace(/url\([^)]*\)/g,'none')]));files.set('index.html',fs.readFileSync(path.join(here,'index.html'),'utf8'));
 server=http.createServer((req,res)=>{const name=req.url==='/'?'index.html':req.url.slice(1);res.setHeader('Content-Type',name.endsWith('.js')?'text/javascript':name.endsWith('.css')?'text/css':'text/html');if(files.has(name))res.end(files.get(name));else{res.writeHead(404);res.end();}});await new Promise(r=>server.listen(0,'127.0.0.1',r));
 browserServer=await chromium.launchServer({...(process.env.DANGO_BROWSER_EXECUTABLE?{executablePath:process.env.DANGO_BROWSER_EXECUTABLE}:{channel:'msedge'}),headless:true});browser=await chromium.connect(browserServer.wsEndpoint());page=await browser.newPage({viewport:{width:1280,height:1000}});
});
// Edge can outlive Browser.close's grace period on Windows. Terminate only this
// fixture's owned headless server instead of timing out while another app is active.
after(async()=>{try{await page?.close();await browserServer?.kill();}finally{if(server){const closed=new Promise(r=>server.close(r));server.closeAllConnections();await closed;}}},{timeout:10000});
beforeEach(async()=>{await page.setViewportSize({width:1280,height:1000});await page.goto(`http://127.0.0.1:${server.address().port}/`);await page.waitForFunction(()=>!!window.DangoMiniPaint);});
async function fixture(assignments,groupIds=['A','B']){
 await page.evaluate(async({assignments,groupIds})=>{
  const canvas=document.createElement('canvas');canvas.width=canvas.height=32;const ctx=canvas.getContext('2d');ctx.fillStyle='red';ctx.fillRect(0,0,32,32);await DangoMiniPaint.load({width:32,height:32,pngBase64:canvas.toDataURL().split(',')[1]});
  const p=DangoMiniPaint.exportProject(),template=p.layers[0];p.assets=[];p.layers=[];p.skeleton.layers=[];p.renderPieces=[];
  assignments.forEach((groupId,i)=>{ctx.fillStyle=['red','lime','blue','yellow','magenta','cyan'][i%6];ctx.fillRect(0,0,32,32);const assetId=`asset-${i+1}`;p.assets.push({id:assetId,pngBase64:canvas.toDataURL().split(',')[1]});p.layers.push({...template,id:i+1,order:i+1,name:`图层 ${i+1}`,assetId});p.skeleton.layers.push({id:String(i+1),groupId});if(i===0||assignments[i-1]!==groupId)p.renderPieces.push({groupId,assetId});});
  p.skeleton.groups=groupIds.map(id=>({id,name:`组${id}`,targetId:null,nodes:[{id:`${id}-R`,x:16,y:16}],parameters:{bend:.5,damping:5,restoration:5,gravity:0,elasticity:0}}));await DangoMiniPaint.load(p);
 },{assignments,groupIds});await page.locator('#layers_tab').click();await page.waitForTimeout(70);
}
async function state(){return page.evaluate(async()=>{const p=DangoMiniPaint.exportProject(),image=new Image();image.src='data:image/png;base64,'+DangoMiniPaint.exportPng().pngBase64;await image.decode();const canvas=document.createElement('canvas');canvas.width=canvas.height=32;const ctx=canvas.getContext('2d');ctx.drawImage(image,0,0);return {layers:p.skeleton.layers,pieces:p.renderPieces.map(p=>p.groupId),pixel:[...ctx.getImageData(16,16,1,1).data]};});}
async function drag(source,target,y=.5){const a=await page.locator(source).boundingBox(),b=await page.locator(target).boundingBox();assert.ok(a&&b,'drag endpoints must be visible');await page.mouse.move(a.x+a.width*.55,a.y+a.height*.5);await page.mouse.down();await page.mouse.move(a.x+a.width*.55+8,a.y+a.height*.5,{steps:4});await page.mouse.move(b.x+b.width*.55,b.y+b.height*y,{steps:12});await page.mouse.move(b.x+b.width*.55,b.y+b.height*y);await page.mouse.up();await page.waitForTimeout(70);}
test('actual top-edge drop detaches a grouped layer at the front, updates composite and undoes/redoes once',async()=>{
 await fixture(['A','A','B']);const before=await state();await drag('[data-image-layer="1"] #layer_name','[data-group-target="B"]',.03);
 const next=await state();assert.deepEqual(next.layers,[{id:'2',groupId:'A'},{id:'3',groupId:'B'},{id:'1',groupId:null}]);assert.deepEqual(next.pixel,[255,0,0,255]);assert.deepEqual(next.pieces,['A','B',null]);
 await page.locator('#undo_button').click();assert.deepEqual(await state(),before);await page.locator('#redo_button').click();assert.deepEqual(await state(),next);
});
test('actual group drag moves the whole block above another group and changes real canvas occlusion',async()=>{
 await fixture(['A','A','B','B']);const before=await state();await drag('[data-group-id="A"]','[data-group-target="B"]',.1);
 const next=await state();assert.deepEqual(next.layers,[{id:'3',groupId:'B'},{id:'4',groupId:'B'},{id:'1',groupId:'A'},{id:'2',groupId:'A'}]);assert.deepEqual(next.pixel,[0,255,0,255]);assert.deepEqual(next.pieces,['B','A']);
 await page.locator('#undo_button').click();assert.deepEqual(await state(),before);await page.locator('#redo_button').click();assert.deepEqual(await state(),next);
});
test('actual member drag respects visual before/after order, transfers groups and enters empty groups',async()=>{
 await fixture(['A','A','B'],['A','B','C']);await drag('[data-image-layer="1"] #layer_name','[data-image-layer="2"]',.1);assert.deepEqual((await state()).layers.map(l=>l.id),['2','1','3']);
 await drag('[data-image-layer="1"] #layer_name','[data-image-layer="2"]',.9);assert.deepEqual((await state()).layers.map(l=>l.id),['1','2','3']);
 await drag('[data-image-layer="1"] #layer_name','[data-group-target="B"]',.6);assert.deepEqual((await state()).layers,[{id:'2',groupId:'A'},{id:'3',groupId:'B'},{id:'1',groupId:'B'}]);
 await drag('[data-image-layer="1"] #layer_name','[data-group-target="C"]',.6);assert.equal((await state()).layers.find(l=>l.id==='1').groupId,'C');
});
test('unsupported empty-group placement next to a root layer is explicitly rejected without mutating the document',()=>{
 const doc={version:1,pivot:{id:'P',x:0,y:0},groups:[{id:'A',name:'空组',nodes:[{id:'AR',x:0,y:0}],targetId:null}],layers:[{id:'1',groupId:null}]},before=structuredClone(doc);
 assert.throws(()=>moveGroup(doc,'A','layer:1',true),/空组目前只能/);assert.deepEqual(doc,before);
});
test('actual unsupported empty-group drag leaves the project intact and shows one visible reason even when native drop is suppressed',async()=>{
 await fixture([null],['A']);const before=await state();
 await page.evaluate(()=>{window.rejectedDrops=0;window.rejectionNotices=[];document.querySelector('#layers_base').addEventListener('drop',()=>window.rejectedDrops++);new MutationObserver(records=>{const text=document.querySelector('#editor_status')?.textContent||'';if(text.startsWith('空组目前只能')&&records.some(r=>r.target.id==='editor_status'||r.target.parentElement?.id==='editor_status'))window.rejectionNotices.push(text);}).observe(document.body,{childList:true,subtree:true,characterData:true});});
 await drag('[data-group-id="A"]','[data-image-layer="1"]',.1);assert.deepEqual(await state(),before);
 const feedback=await page.evaluate(()=>({text:document.querySelector('#editor_status')?.textContent||'',visible:document.querySelector('#editor_status')?.checkVisibility()||false,notices:window.rejectionNotices,drops:window.rejectedDrops}));
 assert.match(feedback.text,/空组目前只能/);assert.equal(feedback.visible,true);assert.equal(feedback.notices.length,1,'one notice per completed rejection, regardless of repeated dragover events');
 assert.equal(feedback.drops,0,'this browser must exercise the native suppression path');
});
test('an invalid hover followed by a valid group target withdraws the rejection',async()=>{
 await fixture([null],['A','B']);const root=await page.locator('[data-image-layer="1"]').boundingBox();await beginDrag('[data-group-id="A"]');await page.mouse.move(root.x+root.width*.5,root.y+2,{steps:8});await page.mouse.move(root.x+root.width*.5+1,root.y+2);await endDrag('[data-group-target="B"]',.9);
 assert.deepEqual(await page.locator('#layers > .layer-group').evaluateAll(nodes=>nodes.map(n=>n.dataset.layerGroup)),['B','A']);assert.equal(await page.evaluate(()=>document.querySelector('#editor_status')?.textContent||''),'');
});
test('empty group order survives project reload and its first member receives the chosen stacking position',async()=>{
 await fixture(['A','B'],['A','B','C']);await drag('[data-group-id="C"]','[data-group-target="B"]',.1);
 const groupOrder=()=>page.locator('#layers > .layer-group').evaluateAll(nodes=>nodes.map(n=>n.dataset.layerGroup));
 assert.deepEqual(await groupOrder(),['C','B','A']);await page.evaluate(async()=>DangoMiniPaint.load(DangoMiniPaint.exportProject()));await page.waitForTimeout(50);assert.deepEqual(await groupOrder(),['C','B','A']);
 await drag('[data-image-layer="1"] #layer_name','[data-group-target="C"]',.6);const next=await state();assert.deepEqual(next.layers,[{id:'2',groupId:'B'},{id:'1',groupId:'C'}]);assert.deepEqual(next.pixel,[255,0,0,255]);
});
async function beginDrag(selector){const a=await page.locator(selector).boundingBox();assert.ok(a);await page.mouse.move(a.x+a.width*.5,a.y+a.height*.5);await page.mouse.down();await page.mouse.move(a.x+a.width*.5+8,a.y+a.height*.5,{steps:4});}
async function dragToBottom(){const b=await page.locator('#layers').boundingBox();assert.ok(b);await page.mouse.move(b.x+b.width*.5,b.y+b.height-3,{steps:8});await page.mouse.move(b.x+b.width*.5+1,b.y+b.height-3);await page.waitForFunction(()=>{const n=document.querySelector('#layers');return n.scrollTop>=n.scrollHeight-n.clientHeight-1;},null,{timeout:6000});}
async function endDrag(selector,y=.1){const b=await page.locator(selector).boundingBox();assert.ok(b);await page.mouse.move(b.x+b.width*.5,b.y+b.height*y,{steps:8});await page.mouse.move(b.x+b.width*.5+1,b.y+b.height*y);await page.mouse.up();await page.waitForTimeout(70);}

test('group collapse is accessible session state and changes neither project, PNG nor undo history',async()=>{
 await fixture(['A','A','B']);const snapshot=()=>page.evaluate(()=>({project:DangoMiniPaint.exportProject(),png:DangoMiniPaint.exportPng(),undo:document.querySelector('#undo_button').disabled,redo:document.querySelector('#redo_button').disabled})),before=await snapshot();
 const toggle=page.locator('[data-group-toggle="A"]');await toggle.click();assert.equal(await toggle.getAttribute('aria-expanded'),'false');assert.equal(await page.locator('[data-image-layer="1"]').isVisible(),false);assert.deepEqual(await snapshot(),before);
 await page.locator('[data-group-id="B"]').click();assert.equal(await toggle.getAttribute('aria-expanded'),'false','unrelated selection and tree repaint retain folding');await toggle.focus();await page.keyboard.press('Enter');assert.equal(await toggle.getAttribute('aria-expanded'),'true');assert.equal(await page.locator('[data-image-layer="1"]').isVisible(),true);await page.keyboard.press('Space');assert.equal(await toggle.getAttribute('aria-expanded'),'false');assert.deepEqual(await snapshot(),before);await toggle.dblclick();assert.equal(await page.locator('#layer_rename_dialog').isVisible(),false);assert.equal(await toggle.getAttribute('aria-expanded'),'false');
});

test('long layer list scrolls independently with fixed tools and preserves position through redraw and undo',async()=>{
 await page.setViewportSize({width:784,height:548});await fixture(Array(16).fill('A'),['A']);assert.equal(await page.locator('#layers_previous,#layers_next,#layers_page').count(),0);
 const before=await page.locator('.layer-actions').boundingBox(),zoom=await page.locator('#zoom_100').textContent(),list=await page.locator('#layers').boundingBox();await page.mouse.move(list.x+list.width*.5,list.y+list.height*.5);await page.mouse.wheel(0,470);await page.waitForFunction(()=>document.querySelector('#layers').scrollTop>0);assert.equal(await page.locator('#zoom_100').textContent(),zoom,'list wheel input must not zoom the canvas');
 assert.deepEqual(await page.locator('.layer-actions').boundingBox(),before);assert.equal(await page.evaluate(()=>scrollY),0);assert.equal(await page.locator('#layers [data-image-layer]').count(),16);
 const current=await page.locator('#layers').evaluate(n=>n.scrollTop),id=await page.evaluate(()=>{const list=document.querySelector('#layers'),r=list.getBoundingClientRect();return [...list.querySelectorAll('[data-image-layer]')].find(n=>{const b=n.getBoundingClientRect();return b.top>r.top+10&&b.bottom<r.bottom-10;}).dataset.imageLayer;});
 await page.locator(`[data-image-layer="${id}"] .visibility`).click();assert.equal(await page.locator('#layers').evaluate(n=>n.scrollTop),current);await page.locator('#undo_button').click();assert.equal(await page.locator('#layers').evaluate(n=>n.scrollTop),current);
});

test('actual layer drag auto-scrolls across the viewport, preserves real stacking and undoes/redoes once',async()=>{
 await page.setViewportSize({width:784,height:548});await fixture(Array(16).fill('A'),['A']);const before=await state();await beginDrag('[data-image-layer="16"] #layer_name');await dragToBottom();await endDrag('[data-image-layer="1"]',.1);
 const next=await state(),expected=before.layers.filter(l=>l.id!=='16');expected.splice(1,0,{id:'16',groupId:'A'});assert.deepEqual(next.layers,expected);assert.deepEqual(next.pixel,[0,0,255,255]);await page.locator('#undo_button').click();assert.deepEqual(await state(),before);await page.locator('#redo_button').click();assert.deepEqual(await state(),next);
});

test('actual group drag auto-scrolls and reorders every member even when its target group is collapsed',async()=>{
 await page.setViewportSize({width:784,height:548});await fixture([...Array(8).fill('A'),...Array(8).fill('B')],['A','B']);await page.locator('[data-group-toggle="A"]').evaluate(n=>n.click());const before=await state();await beginDrag('[data-group-id="B"]');await dragToBottom();await endDrag('[data-group-target="A"]',.9);
 const next=await state();assert.deepEqual(next.layers,[...before.layers.slice(8),...before.layers.slice(0,8)]);assert.deepEqual(next.pixel,[0,255,0,255]);assert.equal(await page.locator('[data-group-toggle="A"]').getAttribute('aria-expanded'),'false');await page.locator('#undo_button').click();assert.deepEqual(await state(),before);await page.locator('#redo_button').click();assert.deepEqual(await state(),next);
});

test('actual hover opens a collapsed group without committing history before a layer is dropped into it',async()=>{
 await fixture(['A','A','B']);await page.locator('[data-group-toggle="B"]').click();const before=await state();await beginDrag('[data-image-layer="1"] #layer_name');const b=await page.locator('[data-group-target="B"]').boundingBox();await page.mouse.move(b.x+b.width*.5,b.y+b.height*.6,{steps:8});await page.mouse.move(b.x+b.width*.5+1,b.y+b.height*.6);
 await page.waitForFunction(()=>document.querySelector('[data-group-toggle="B"]').getAttribute('aria-expanded')==='true');assert.deepEqual(await state(),before);await page.mouse.up();await page.waitForTimeout(70);assert.equal((await state()).layers.find(l=>l.id==='1').groupId,'B');await page.locator('#undo_button').click();assert.deepEqual(await state(),before);
});

test('actual upward layer drag can be canceled without a document action',async()=>{
 await page.setViewportSize({width:784,height:548});await fixture(Array(16).fill('A'),['A']);await page.locator('#layers').evaluate(n=>n.scrollTop=n.scrollHeight);const before=await state(),b=await page.locator('#layers').boundingBox();await beginDrag('[data-image-layer="1"] #layer_name');await page.mouse.move(b.x+b.width*.5,b.y+3,{steps:8});await page.mouse.move(b.x+b.width*.5+1,b.y+3);
 await page.waitForFunction(()=>{const n=document.querySelector('#layers');return n.scrollTop<n.scrollHeight-n.clientHeight-40;});await page.keyboard.press('Escape');await page.mouse.up();await page.mouse.move(b.x-20,b.y+40);await page.waitForTimeout(180);assert.deepEqual(await state(),before);assert.equal(await page.locator('[data-drop-position]').count(),0);
});

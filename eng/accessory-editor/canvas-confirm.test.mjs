import test,{before,after,beforeEach} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';import path from 'node:path';import http from 'node:http';import {fileURLToPath} from 'node:url';
import {chromium} from 'playwright-core';
const here=path.dirname(fileURLToPath(import.meta.url)),root=path.resolve(here,'../../src/Dango.Desktop/Accessories/Editing/Web');
let server,browser,page;const errors=[];
before(async()=>{
 server=http.createServer((req,res)=>{const file=path.join(root,req.url==='/'?'index.html':req.url);res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html');try{res.end(fs.readFileSync(file));}catch{res.writeHead(404);res.end();}});await new Promise(r=>server.listen(0,'127.0.0.1',r));
 browser=await chromium.launch({channel:'msedge',headless:true});
});
after(async()=>{await browser?.close();if(server)await new Promise(r=>server.close(r));});
beforeEach(async()=>{await page?.close();page=await browser.newPage({viewport:{width:1280,height:850}});page.on('pageerror',e=>errors.push(e.message));await page.goto(`http://127.0.0.1:${server.address().port}/`);await page.waitForFunction(()=>!!window.DangoMiniPaint);await page.evaluate(async()=>{const c=document.createElement('canvas');c.width=400;c.height=300;const ctx=c.getContext('2d');ctx.fillStyle='#885e3e';ctx.fillRect(0,0,400,300);await DangoMiniPaint.load({width:400,height:300,pngBase64:c.toDataURL().split(',')[1]});});});
const snapshot=()=>page.evaluate(()=>DangoMiniPaint.exportSnapshot());
async function cropBox(){await page.locator('#crop').click();const points=await page.evaluate(()=>{const m=document.querySelector('#geometry_overlay').getScreenCTM();return [[50,40],[250,200]].map(([x,y])=>{const p=new DOMPoint(x,y).matrixTransform(m);return {x:p.x,y:p.y};});});await page.mouse.move(points[0].x,points[0].y);await page.mouse.down();await page.mouse.move(points[1].x,points[1].y,{steps:8});await page.mouse.up();}
test('click crop, drag, Enter confirms the crop once and supports undo/redo',async()=>{
 const before=await snapshot();await cropBox();await page.keyboard.press('Enter');await page.waitForFunction(()=>{try{return DangoMiniPaint.exportProject().width===200;}catch{return false;}});
 const after=await snapshot();assert.equal(after.project.height,160);assert.equal(after.project.layers[0].x,-50);assert.equal(after.project.layers[0].y,-40);assert.deepEqual(after.project.skeleton.pivot,{id:'P',x:150,y:110});
 await page.keyboard.press('Enter');assert.deepEqual(await snapshot(),after);
 await page.locator('#undo_button').click();assert.deepEqual(await snapshot(),before);await page.locator('#redo_button').click();assert.deepEqual(await snapshot(),after);assert.deepEqual(errors,[]);
});
test('crop Enter leaves inputs, IME, repeated/modified keys, other buttons and modal dialogs alone',async()=>{
 await cropBox();const before=await snapshot();
 for(const data of [{isComposing:true},{keyCode:229},{repeat:true},{ctrlKey:true},{altKey:true},{shiftKey:true},{metaKey:true}]){await page.locator('body').dispatchEvent('keydown',{key:'Enter',...data});assert.deepEqual(await snapshot(),before);}
 await page.locator('body').dispatchEvent('compositionstart');await page.locator('body').dispatchEvent('keydown',{key:'Enter'});assert.deepEqual(await snapshot(),before);await page.locator('body').dispatchEvent('compositionend');
 for(const selector of ['#accessory_name','#undo_button']){await page.locator(selector).dispatchEvent('keydown',{key:'Enter'});assert.deepEqual(await snapshot(),before);}
 await page.evaluate(()=>{const b=document.createElement('button');b.id='test_apply';b.textContent='应用';b.onclick=()=>{b.dataset.clicked='true';};document.body.append(b);});await page.locator('#test_apply').focus();await page.keyboard.press('Enter');assert.equal(await page.locator('#test_apply').getAttribute('data-clicked'),'true');assert.deepEqual(await snapshot(),before);
 await page.locator('.image-options>summary').click();await page.locator('#canvas_expand').click();await page.locator('#canvas_expand_dialog [name=width]').fill('440');await page.locator('#canvas_expand_dialog [name=height]').fill('340');await page.locator('#canvas_expand_dialog [name=height]').dispatchEvent('keydown',{key:'Enter',isComposing:true});assert.equal(await page.locator('#canvas_expand_dialog').evaluate(n=>n.open),true);assert.deepEqual(await snapshot(),before);
 await page.locator('#canvas_expand_dialog [name=height]').press('Enter');await page.waitForFunction(()=>!document.querySelector('#canvas_expand_dialog').open);const expanded=await snapshot();assert.equal(expanded.project.width,440);assert.equal(expanded.project.height,340);assert.deepEqual(errors,[]);
});
test('empty crop selection and unrelated active tools do not consume Enter',async()=>{
 await page.locator('#crop').click();const before=await snapshot();await page.keyboard.press('Enter');assert.deepEqual(await snapshot(),before);await page.locator('#brush').click();await page.keyboard.press('Enter');assert.deepEqual(await snapshot(),before);assert.deepEqual(errors,[]);
});
test('a crop toolbar button retaining focus also confirms a valid selection',async()=>{
 await cropBox();await page.locator('#tools_container #crop').focus();await page.keyboard.press('Enter');await page.waitForFunction(()=>{try{return DangoMiniPaint.exportProject().width===200;}catch{return false;}});assert.equal((await snapshot()).project.height,160);assert.deepEqual(errors,[]);
});
test('text Enter retains multiline input and commits when leaving text editing',async()=>{
 await page.locator('#text').click();const point=await page.evaluate(()=>{const p=new DOMPoint(50,40).matrixTransform(document.querySelector('#geometry_overlay').getScreenCTM());return {x:p.x,y:p.y};});await page.mouse.click(point.x,point.y);await page.keyboard.type('First');await page.keyboard.press('Enter');await page.keyboard.type('Second');assert.equal(await page.evaluate(()=>document.activeElement.id),'text_tool_keyboard_input');await page.locator('#select').click();const text=(await snapshot()).project.layers.find(l=>l.type==='text');assert.equal(text.data.length,2);assert.deepEqual(errors,[]);
});

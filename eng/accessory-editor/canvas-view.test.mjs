import test,{before,after,beforeEach} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';import path from 'node:path';import http from 'node:http';import {fileURLToPath} from 'node:url';
import {chromium} from 'playwright-core';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../src/Dango.Desktop/Accessories/Editing/Web');
let server,browser,page;
before(async()=>{
 server=http.createServer((req,res)=>{const f=path.join(root,req.url==='/'?'index.html':decodeURIComponent(req.url).replace(/^\//,''));res.setHeader('Content-Type',f.endsWith('.js')?'text/javascript':f.endsWith('.css')?'text/css':'text/html');try{res.end(fs.readFileSync(f));}catch{res.writeHead(404);res.end();}});await new Promise(r=>server.listen(0,'127.0.0.1',r));
 browser=await chromium.launch({channel:'msedge',headless:true});page=await browser.newPage({viewport:{width:1280,height:850}});
});
after(async()=>{await browser?.close();if(server)await new Promise(r=>server.close(r));});
beforeEach(async()=>{
 await page.goto(`http://127.0.0.1:${server.address().port}/`);await page.waitForFunction(()=>!!window.DangoMiniPaint);
 await page.evaluate(async()=>{const c=document.createElement('canvas');c.width=400;c.height=300;const x=c.getContext('2d');x.fillStyle='#f00000';x.fillRect(80,70,60,40);await DangoMiniPaint.load({width:400,height:300,pngBase64:c.toDataURL().split(',')[1]});});
 await page.locator('#geometry_modify').click();
});
async function state(){return page.evaluate(()=>({project:DangoMiniPaint.exportProject(),png:DangoMiniPaint.exportPng(),revision:DangoMiniPaint.getEditSequence(),undo:document.querySelector('#undo_button').disabled,redo:document.querySelector('#redo_button').disabled}));}
async function geometry(){return page.evaluate(()=>{const c=document.querySelector('#canvas_minipaint'),b=document.querySelector('#canvas_minipaint_background'),s=document.querySelector('#geometry_overlay'),r=c.getBoundingClientRect(),q=b.getBoundingClientRect(),m=s.getScreenCTM(),p=new DOMPoint(0,0).matrixTransform(m),end=new DOMPoint(400,300).matrixTransform(m);return {background:{x:q.x,y:q.y,w:q.width,h:q.height},world:{x:p.x,y:p.y,w:end.x-p.x,h:end.y-p.y},viewport:{x:r.x,y:r.y,w:r.width,h:r.height},scale:m.a};});}
async function synced(){const g=await geometry();for(const k of ['x','y','w','h'])assert.ok(Math.abs(g.background[k]-g.world[k])<1.1,`checker ${k} must follow document transform: ${JSON.stringify(g)}`);return g;}
async function point(p){return page.evaluate(p=>{const q=new DOMPoint(p.x,p.y).matrixTransform(document.querySelector('#geometry_overlay').getScreenCTM());return {x:q.x,y:q.y};},p);}
async function imageSynced(){assert.equal(await page.evaluate(()=>{const c=document.querySelector('#canvas_minipaint'),r=c.getBoundingClientRect(),s=document.querySelector('#geometry_overlay'),p=new DOMPoint(100,90).matrixTransform(s.getScreenCTM()),x=Math.floor((p.x-r.left)*c.width/r.width),y=Math.floor((p.y-r.top)*c.height/r.height),v=c.getContext('2d').getImageData(x,y,1,1).data;return v[0]>220&&v[1]<20&&v[2]<20&&v[3]===255;}),true,'image pixels must follow the same world transform as skeleton/checker');}
test('hand pan moves image, finite checker boundary and skeleton together without document edits',async()=>{
 const before=await state(),start=await synced();await page.locator('#image_pan').click();const p=await point({x:200,y:180});await page.mouse.move(p.x,p.y);await page.mouse.down();await page.mouse.move(p.x+65,p.y+45,{steps:4});await page.mouse.up();const end=await synced();assert.ok(Math.abs(end.background.x-start.background.x-65)<2,'hand drag must move whole document even at fit scale');assert.ok(Math.abs(end.background.y-start.background.y-45)<2);await imageSynced();assert.deepEqual(await state(),before);
});
test('click zoom, top zoom buttons and wheel share a finite document view and leave exports/history unchanged',async()=>{
 const before=await state();let last=await synced();await page.locator('#image_zoom').click();const p=await point({x:200,y:180});await page.mouse.click(p.x,p.y);await page.waitForFunction(scale=>document.querySelector('#geometry_overlay').getScreenCTM().a>scale,last.scale);let next=await synced();assert.ok(next.scale>last.scale);await imageSynced();last=next;
 await page.locator('#zoom_more').click();await page.waitForTimeout(80);next=await synced();assert.ok(next.scale>last.scale);await imageSynced();last=next;
 await page.locator('#zoom_less').click();await page.waitForTimeout(80);next=await synced();assert.ok(next.scale<last.scale);await imageSynced();last=next;
 await page.mouse.move(p.x,p.y);await page.mouse.wheel(0,150);await page.waitForTimeout(80);next=await synced();assert.ok(next.scale<last.scale);await imageSynced();assert.ok(next.background.w<next.viewport.w-40&&next.background.h<next.viewport.h-40,'checker must have workspace around document, never cover full viewport');assert.deepEqual(await state(),before);
});
test('skeleton hit mapping remains correct after pan and zoom',async()=>{
 await page.locator('#image_pan').click();const p=await point({x:200,y:150});await page.mouse.move(p.x,p.y);await page.mouse.down();await page.mouse.move(p.x+40,p.y+30);await page.mouse.up();await page.locator('#zoom_more').click();await page.waitForTimeout(80);await page.locator('#geometry_modify').click();const pivot=await point({x:200,y:150});await page.mouse.click(pivot.x,pivot.y);await page.keyboard.press('ArrowRight');await page.waitForFunction(()=>DangoMiniPaint.getSkeleton().pivot.x===201);assert.equal((await page.evaluate(()=>DangoMiniPaint.getSkeleton())).pivot.y,150);
});

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
beforeEach(async()=>{await page?.close();page=await browser.newPage({viewport:{width:1280,height:850}});page.on('pageerror',e=>errors.push(e.message));await page.goto(`http://127.0.0.1:${server.address().port}/`);await page.waitForFunction(()=>!!window.DangoMiniPaint);});
const settled=()=>page.waitForFunction(()=>{try{DangoMiniPaint.exportProject();return true;}catch{return false;}});
async function pixel(png,x,y){return page.evaluate(async({png,x,y})=>{const image=new Image();image.src='data:image/png;base64,'+png;await image.decode();const c=document.createElement('canvas');c.width=image.width;c.height=image.height;const ctx=c.getContext('2d');ctx.drawImage(image,0,0);return [...ctx.getImageData(x,y,1,1).data];},{png,x,y});}
for(const scaled of [false,true])for(const tool of ['erase','magic_erase','fill'])test(`${tool} edits native-open image layers with null original dimensions${scaled?' after layer scaling':''}`,async()=>{
 // AccessoryUnifiedEditorSession/CreateOpenProject uses nullable OriginalWidth/Height.
 // This fixture preserves that supported native envelope instead of the direct-PNG shortcut.
 const before=await page.evaluate(async scaled=>{
  const c=document.createElement('canvas');c.width=64;c.height=48;const x=c.getContext('2d');x.fillStyle='#c29d6b';x.fillRect(0,0,32,48);x.fillStyle='#305b82';x.fillRect(32,0,32,48);
  await DangoMiniPaint.load({width:64,height:48,pngBase64:c.toDataURL().split(',')[1]});
  const project=DangoMiniPaint.exportProject();project.layers[0].width_original=null;project.layers[0].height_original=null;project.renderPieces=[];
  if(scaled){project.width=256;project.height=192;Object.assign(project.layers[0],{x:32,y:24,width:128,height:96});project.skeleton.pivot={id:'P',x:128,y:96};}
  await DangoMiniPaint.load(project);return DangoMiniPaint.exportSnapshot();
 },scaled);
 const at={x:scaled?64:16,y:scaled?72:24},other={x:scaled?128:48,y:at.y};
 if(tool==='magic_erase')assert.equal(await page.locator('#magic_erase').getAttribute('aria-label'),'魔棒 W');
 await page.locator('#'+tool).click();const screen=await page.evaluate(p=>{const q=new DOMPoint(p.x,p.y).matrixTransform(document.querySelector('#geometry_overlay').getScreenCTM());return {x:q.x,y:q.y};},at);await page.mouse.click(screen.x,screen.y);await settled();
 if(tool==='magic_erase'){assert.equal((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,before.png.pngBase64,'magic wand selects without editing');await page.keyboard.press('Delete');await page.waitForFunction(original=>DangoMiniPaint.exportPng().pngBase64!==original,before.png.pngBase64);await settled();}
 const changed=await page.evaluate(()=>DangoMiniPaint.exportSnapshot());
 if(tool==='fill')assert.deepEqual((await pixel(changed.png.pngBase64,at.x,at.y)).slice(0,3),[136,94,62]);
 else assert.equal((await pixel(changed.png.pngBase64,at.x,at.y))[3],0,'the requested raster edit must change real output pixels');
 assert.deepEqual(await pixel(changed.png.pngBase64,other.x,other.y),[48,91,130,255]);
 const layer=changed.project.layers[0];assert.equal(layer.width_original,64);assert.equal(layer.height_original,48,'source dimensions must come from decoded pixels, not displayed layer size');
 assert.ok(!/操作未完成|正在完成操作/.test(await page.evaluate(()=>document.querySelector('#editor_status')?.textContent||'')));
 if(tool!=='fill')assert.ok(layer.restoreAssetId,'successful erasing retains the original pixels for restoration');
 await page.locator('#undo_button').click();await settled();assert.equal((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,before.png.pngBase64);
 await page.locator('#redo_button').click();await settled();assert.equal((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,changed.png.pngBase64);
 await page.evaluate(p=>DangoMiniPaint.load(p),changed.project);assert.equal((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,changed.png.pngBase64);
 assert.deepEqual(errors,[]);
});

for(const diameter of [4,24])test(`restore diameter ${diameter} from current tool parameters controls exported pixels`,async()=>{
 await page.evaluate(async()=>{const c=document.createElement('canvas');c.width=64;c.height=48;const x=c.getContext('2d');x.fillStyle='#c29d6b';x.fillRect(0,0,64,48);await DangoMiniPaint.load({width:64,height:48,pngBase64:c.toDataURL().split(',')[1]});});
 const at=async(x,y)=>page.evaluate(p=>{const q=new DOMPoint(p.x,p.y).matrixTransform(document.querySelector('#geometry_overlay').getScreenCTM());return {x:q.x,y:q.y};},{x,y});
 await page.locator('#magic_erase').click();let p=await at(32,24);await page.mouse.click(p.x,p.y);const original=(await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64;await page.keyboard.press('Delete');await page.waitForFunction(before=>DangoMiniPaint.exportPng().pngBase64!==before,original);await settled();assert.equal((await pixel((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,32,24))[3],0);
 await page.keyboard.press('Escape');await page.keyboard.press('Shift+E');await page.locator('#restore_size').fill(String(diameter));await page.keyboard.press('Tab');
 p=await at(32,24);await page.mouse.click(p.x,p.y);await settled();const restored=await page.evaluate(()=>DangoMiniPaint.exportPng());assert.deepEqual(await pixel(restored.pngBase64,32,24),[194,157,107,255]);assert.equal((await pixel(restored.pngBase64,40,24))[3],diameter===4?0:255);assert.deepEqual(errors,[]);
});

import test,{before,after,beforeEach} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import {fileURLToPath} from 'node:url';
import {chromium} from 'playwright-core';

const here=path.dirname(fileURLToPath(import.meta.url));
const root=path.resolve(here,'../../src/Dango.Desktop/Accessories/Editing/Web');
let server,browser,page;
const errors=[];

before(async()=>{
 server=http.createServer((req,res)=>{
  const file=path.join(root,req.url==='/'?'index.html':decodeURIComponent(req.url.split('?')[0]));
  res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html');
  try{res.end(fs.readFileSync(file));}catch{res.writeHead(404);res.end();}
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 browser=await chromium.launch({channel:'msedge',headless:true});
});

after(async()=>{
 await browser?.close();
 if(server)await new Promise(resolve=>server.close(resolve));
});

beforeEach(async()=>{
 await page?.close();
 page=await browser.newPage({viewport:{width:1280,height:850}});
 page.on('pageerror',error=>errors.push(error.message));
 await page.goto(`http://127.0.0.1:${server.address().port}/`);
 await page.waitForFunction(()=>!!window.DangoMiniPaint);
 errors.length=0;
});

const settled=()=>page.waitForFunction(()=>{
 try{DangoMiniPaint.exportProject();return true;}catch{return false;}
});

async function makeProject(){
 return page.evaluate(async()=>{
  const image=document.createElement('canvas');image.width=64;image.height=48;
  const ctx=image.getContext('2d');ctx.fillStyle='#c82832';ctx.fillRect(0,0,32,48);ctx.fillStyle='#1432c8';ctx.fillRect(32,0,32,48);
  await DangoMiniPaint.load({width:64,height:48,pngBase64:image.toDataURL().split(',')[1]});
  return DangoMiniPaint.exportSnapshot();
 });
}

async function screenPoint(x,y){
 return page.evaluate(({x,y})=>{
  const overlay=document.querySelector('#geometry_overlay');
  if(overlay){const point=new DOMPoint(x,y).matrixTransform(overlay.getScreenCTM());return {x:point.x,y:point.y};}
  const canvas=document.querySelector('#canvas_minipaint'),rect=canvas.getBoundingClientRect();
  return {x:rect.left+x/64*rect.width,y:rect.top+y/48*rect.height};
 },{x,y});
}

async function clickPixel(x,y){const p=await screenPoint(x,y);assert.ok(Number.isFinite(p.x)&&Number.isFinite(p.y),`invalid canvas click: ${JSON.stringify(p)}`);await page.mouse.click(p.x,p.y);}

async function pixel(pngBase64,x,y){
 return page.evaluate(async({pngBase64,x,y})=>{
  const image=new Image();image.src='data:image/png;base64,'+pngBase64;await image.decode();
  const canvas=document.createElement('canvas');canvas.width=image.width;canvas.height=image.height;
  const ctx=canvas.getContext('2d');ctx.drawImage(image,0,0);return [...ctx.getImageData(x,y,1,1).data];
 },{pngBase64,x,y});
}

async function selectRed(){
 await page.locator('#magic_erase').click();
 const before=(await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64;
 const point=await screenPoint(12,18);await clickPixel(12,18);await settled();
 const selection=await page.locator('#magic_erase').getAttribute('data-selection-pixels');
 assert.ok(Number(selection)>0,`magic click creates a pixel selection; point=${JSON.stringify(point)} status=${await page.locator('#editor_status').textContent()}`);
 assert.equal((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,before,'selecting pixels does not change exported image');
}

test('magic click is non-destructive; Delete clears only the selected pixels and undo/redo restore exact exports',async()=>{
 const before=await makeProject();
 await selectRed();
 await page.keyboard.press('Delete');await settled();
 const deleted=(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()));
 assert.equal((await pixel(deleted.png.pngBase64,12,18))[3],0,'selected pixel is deleted');
 assert.deepEqual(await pixel(deleted.png.pngBase64,48,18),[20,50,200,255],'unselected color remains byte-identical');
 await page.locator('#undo_button').click();await settled();
 assert.equal((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,before.png.pngBase64,'undo restores the original exported image');
 await page.locator('#redo_button').click();await settled();
 assert.equal((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,deleted.png.pngBase64,'redo reapplies the constrained deletion');
 assert.deepEqual(errors,[]);
});

test('Esc clears selection; fill and eraser edits stay within it',async()=>{
 await makeProject();await selectRed();
 await page.locator('#fill').click();await clickPixel(48,18);await settled();
 let output=(await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64;
 assert.deepEqual(await pixel(output,48,18),[20,50,200,255],'filling directly outside the selection cannot alter those pixels');
 await clickPixel(12,18);await settled();
 output=(await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64;
 assert.deepEqual((await pixel(output,12,18)).slice(0,3),[136,94,62],'fill changes the selected region');
 assert.deepEqual(await pixel(output,48,18),[20,50,200,255],'fill leaves pixels outside selection unchanged');
 await page.locator('#erase').click();await clickPixel(31,18);await settled();
 output=(await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64;
 assert.deepEqual(await pixel(output,33,18),[20,50,200,255],'an eraser stroke crossing the selection edge cannot erase outside it');
 await clickPixel(12,18);await settled();
 output=(await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64;
 assert.equal((await pixel(output,12,18))[3],0,'eraser clears selected pixels');
 assert.deepEqual(await pixel(output,48,18),[20,50,200,255],'eraser leaves pixels outside selection unchanged');
 await page.keyboard.press('Escape');await settled();
 assert.equal(await page.locator('#magic_erase').getAttribute('data-selection-pixels'),'0','Escape releases the selection');
 await page.locator('#fill').click();await clickPixel(48,18);await settled();
 output=(await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64;
 assert.deepEqual((await pixel(output,48,18)).slice(0,3),[136,94,62],'fill can edit the former outside region after cancellation');
 assert.deepEqual(errors,[]);
});

test('unsupported tools cannot edit through the active mask; Escape releases them',async()=>{
 await makeProject();await selectRed();
 const original=(await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64;
 for(const tool of ['brush','text','select','crop']){
  await page.locator('#'+tool).click();await clickPixel(48,18);await settled();
  assert.equal((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,original,`${tool} cannot change pixels while a selection is active`);
  assert.ok(Number(await page.locator('#magic_erase').getAttribute('data-selection-pixels'))>0,`${tool} leaves the active selection intact`);
 }
 await page.keyboard.press('Escape');await page.locator('#brush').click();await clickPixel(48,18);await settled();
 assert.notEqual((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,original,'brush is usable after Escape');
 assert.deepEqual(errors,[]);
});

test('Delete still erases the active selection after switching to the restore tool',async()=>{
 const before=await makeProject();await selectRed();
 await page.locator('#restore_pixels').click();
 assert.equal(await page.locator('#restore_pixels').getAttribute('aria-pressed'),'true');
 await page.keyboard.press('Delete');await settled();
 const deleted=(await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64;
 assert.equal((await pixel(deleted,12,18))[3],0,'restore mode retains a deletable pixel selection');
 assert.deepEqual(await pixel(deleted,48,18),[20,50,200,255],'other pixels remain unchanged');
 await page.locator('#undo_button').click();await settled();
 assert.equal((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,before.png.pngBase64);
 await page.locator('#redo_button').click();await settled();
 assert.equal((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,deleted);
 assert.deepEqual(errors,[]);
});

test('rotation and scale invalidate the old pixel map; changing layers cannot reuse its mask',async()=>{
 const original=await makeProject();await selectRed();
 await page.locator('#layers_base #layer_duplicate').click();await settled();
 assert.equal(await page.locator('#magic_erase').getAttribute('data-selection-pixels'),'0','duplicating and activating another layer cancels the old selection');
 await page.locator('#fill').click();await clickPixel(48,18);await settled();
 let output=(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()));
 assert.deepEqual((await pixel(output.png.pngBase64,48,18)).slice(0,3),[136,94,62],'the active duplicate can edit pixels outside the former selection');
 await page.evaluate(project=>DangoMiniPaint.load(project),original.project);await settled();await selectRed();
 await page.evaluate(async()=>{const project=DangoMiniPaint.exportProject();Object.assign(project.layers[0],{x:4,y:3,width:56,height:42,rotate:18});await DangoMiniPaint.load(project);});await settled();
 assert.equal(await page.locator('#magic_erase').getAttribute('data-selection-pixels'),'0','rotation and scale invalidate the captured pixel map');
 assert.deepEqual(errors,[]);
});

// A native WPF-rendered built-in PNG can be supplied by the desktop validation
// run. Keep this optional so the browser suite needs no desktop build or WPF.
test('native built-in image: parameter focus then canvas selection/Delete preserves other colours',{
 skip:!process.env.DANGO_MAGIC_BUILTIN_PNG,
},async()=>{
 const pngBase64=fs.readFileSync(process.env.DANGO_MAGIC_BUILTIN_PNG).toString('base64');
 const before=await page.evaluate(async pngBase64=>{
  await DangoMiniPaint.load({width:512,height:512,pngBase64});
  const project=DangoMiniPaint.exportProject();
  project.layers[0].width_original=null;project.layers[0].height_original=null;project.renderPieces=[];
  await DangoMiniPaint.load(project);return DangoMiniPaint.exportSnapshot();
 },pngBase64);
 await page.locator('#magic_erase').click();
 const tolerance=page.locator('#action_attributes input[type=number]').first();
 await tolerance.fill('1');await tolerance.press('Tab');await tolerance.focus();
 await clickPixel(256,448);await settled();
 assert.ok(Number(await page.locator('#magic_erase').getAttribute('data-selection-pixels'))>0);
 assert.equal((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,before.png.pngBase64);
 const otherBefore=await pixel(before.png.pngBase64,160,352);
 assert.ok((await pixel(before.png.pngBase64,256,448))[3]>0,'native stem seed is opaque');
 assert.ok(otherBefore[3]>0,'native leaf comparison is opaque');
 await page.keyboard.press('Delete');await settled();
 const deleted=(await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64;
 assert.equal((await pixel(deleted,256,448))[3],0);
 assert.deepEqual(await pixel(deleted,160,352),otherBefore,'another colour on the native image is unchanged');
 await page.locator('#undo_button').click();await settled();
 assert.equal((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,before.png.pngBase64);
 await page.locator('#redo_button').click();await settled();
 assert.equal((await page.evaluate(()=>DangoMiniPaint.exportPng())).pngBase64,deleted);
 assert.deepEqual(errors,[]);
});

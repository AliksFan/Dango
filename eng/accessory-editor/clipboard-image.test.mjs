import test,{before,after,beforeEach} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';import path from 'node:path';import http from 'node:http';import {fileURLToPath} from 'node:url';
import {chromium} from 'playwright-core';
import {imageSize} from './clipboard-image.js';
const here=path.dirname(fileURLToPath(import.meta.url)),root=path.resolve(here,'../../src/Dango.Desktop/Accessories/Editing/Web');
let server,browser,page;
before(async()=>{
 server=http.createServer((req,res)=>{const file=path.join(root,req.url==='/'?'index.html':req.url);res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html');try{res.end(fs.readFileSync(file));}catch{res.writeHead(404);res.end();}});await new Promise(r=>server.listen(0,'127.0.0.1',r));
 browser=await chromium.launch({channel:'msedge',headless:true});
});
after(async()=>{await page?.close();await browser?.close();if(server)await new Promise(r=>server.close(r));});
beforeEach(async()=>{await page?.close();page=await browser.newPage({viewport:{width:1280,height:850}});await page.goto(`http://127.0.0.1:${server.address().port}/`);await page.waitForFunction(()=>!!window.DangoMiniPaint);await page.evaluate(async()=>{const c=document.createElement('canvas');c.width=64;c.height=48;c.getContext('2d').fillStyle='#305b82';c.getContext('2d').fillRect(0,0,64,48);await DangoMiniPaint.load({width:64,height:48,pngBase64:c.toDataURL().split(',')[1]});});});
const settled=()=>page.waitForFunction(()=>{try{DangoMiniPaint.exportProject();return true;}catch{return false;}});
async function paste({type='image/png',width=16,height=12,target='#canvas_minipaint',bad=false,count=1,noise=false,gesture='paste'}={}){
 return page.evaluate(async({type,width,height,target,bad,count,noise,gesture})=>{
  const c=document.createElement('canvas');c.width=width;c.height=height;const x=c.getContext('2d');x.fillStyle='#c29d6b';x.fillRect(0,0,width,height);
  if(noise){const image=x.createImageData(width,height);let seed=123456;for(let i=0;i<image.data.length;i++){seed^=seed<<13;seed^=seed>>>17;seed^=seed<<5;image.data[i]=seed&255;}x.putImageData(image,0,0);}
  const blob=bad?new Blob(['invalid image'],{type}):await new Promise(r=>c.toBlob(r,type));
  const data=new DataTransfer();for(let i=0;i<count;i++)data.items.add(new File([blob],'fixture',{type}));
  if(gesture==='drop'){
   const zone=document.querySelector(target),over=new DragEvent('dragover',{dataTransfer:data,bubbles:true,cancelable:true}),drop=new DragEvent('drop',{dataTransfer:data,bubbles:true,cancelable:true});
   zone.dispatchEvent(over);zone.dispatchEvent(drop);return {dragover:over.defaultPrevented,drop:drop.defaultPrevented};
  }
  // No system clipboard is read or modified. Exercise the actual installed
  // paste event adapter with the Files provided by a clipboard gesture.
  const event=new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true});document.querySelector(target).dispatchEvent(event);return event.defaultPrevented;
 },{type,width,height,target,bad,count,noise,gesture});
}
const drop=options=>paste({...options,gesture:'drop'});
async function dropped(){await page.waitForFunction(()=>document.querySelector('#editor_status')?.textContent==='已导入为新图层。');await settled();}
async function pasted(){await page.waitForFunction(()=>document.querySelector('#editor_status')?.textContent==='已粘贴为新图层。');await settled();}
async function pixel(png,x,y){return page.evaluate(async({png,x,y})=>{const i=new Image();i.src='data:image/png;base64,'+png;await i.decode();const c=document.createElement('canvas');c.width=i.width;c.height=i.height;c.getContext('2d').drawImage(i,0,0);return [...c.getContext('2d').getImageData(x,y,1,1).data];},{png,x,y});}

for(const type of ['image/png','image/jpeg'])test(`${type} paste preserves project and pixels, saves, undoes and redoes`,async()=>{
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.locator('#new_group').click();await settled();
 const before=await page.evaluate(()=>DangoMiniPaint.exportSnapshot());
 assert.equal(await paste({type}),true);await pasted();
 const after=await page.evaluate(()=>DangoMiniPaint.exportSnapshot());
 assert.equal(after.project.layers.length,2);assert.deepEqual(after.project.layers[0],before.project.layers[0]);
 const layer=after.project.layers[1];assert.equal(layer.type,'image');assert.equal(layer.name,'粘贴图片');assert.deepEqual([layer.x,layer.y,layer.width,layer.height],[24,18,16,12]);
 assert.equal(after.project.skeleton.layers.find(l=>l.id===String(layer.id)).groupId,after.project.skeleton.groups[0].id,'new layer follows current group');
 assert.deepEqual(await pixel(after.png.pngBase64,0,0),[48,91,130,255]);
 const center=await pixel(after.png.pngBase64,32,24);assert.ok(Math.abs(center[0]-194)<3&&Math.abs(center[1]-157)<3&&Math.abs(center[2]-107)<3);
 await page.keyboard.press('Control+z');await settled();assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),before);
 await page.keyboard.press('Control+Shift+z');await settled();assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),after);
 await page.evaluate(p=>DangoMiniPaint.load(p),after.project);assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),after);assert.deepEqual(errors,[]);
});

test('large image fits existing canvas while retaining decoded source size',async()=>{
 assert.equal(await paste({width:128,height:48}),true);await pasted();const project=await page.evaluate(()=>DangoMiniPaint.exportProject());assert.deepEqual([project.width,project.height],[64,48]);const layer=project.layers[1];assert.deepEqual([layer.width,layer.height,layer.y,layer.width_original,layer.height_original],[64,24,12,128,48]);
});

test('sole empty layer is preserved and paste adds one undo step',async()=>{
 const before=await page.evaluate(async()=>{const project=DangoMiniPaint.exportProject();Object.assign(project.layers[0],{type:null,width:null,height:null,width_original:null,height_original:null,data:null});delete project.layers[0].assetId;project.assets=[];project.renderPieces=[];await DangoMiniPaint.load(project);return DangoMiniPaint.exportSnapshot();});
 await paste();await pasted();assert.equal((await page.evaluate(()=>DangoMiniPaint.exportProject())).layers.length,2);
 await page.keyboard.press('Control+z');await settled();assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),before);
});

for(const options of [{bad:true},{type:'image/gif'},{count:2},{width:4097,height:1}])test(`invalid paste ${JSON.stringify(options)} preserves document/history`,async()=>{
 const before=await page.evaluate(()=>DangoMiniPaint.exportSnapshot());assert.equal(await paste(options),true);await page.waitForFunction(()=>!!document.querySelector('#editor_status')?.textContent);await settled();assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),before);assert.equal(await page.locator('#undo_button').isDisabled(),true);
});

test('text/name inputs and contenteditable keep their native paste behavior',async()=>{
 const before=await page.evaluate(()=>DangoMiniPaint.exportSnapshot());assert.equal(await paste({target:'#accessory_name'}),false);
 await page.evaluate(()=>{const input=document.createElement('div');input.id='editable_fixture';input.contentEditable='plaintext-only';document.body.append(input);});assert.equal(await paste({target:'#editable_fixture'}),false);
 const consumed=await page.evaluate(()=>{const data=new DataTransfer();data.setData('text/plain','fixture text');const event=new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true});document.querySelector('#canvas_minipaint').dispatchEvent(event);return event.defaultPrevented;});assert.equal(consumed,false);assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),before);
});

test('layer limit rejects paste without replacing a layer or consuming history',async()=>{
 await page.evaluate(async()=>{const p=DangoMiniPaint.exportProject(),source=p.layers[0];p.layers=Array.from({length:16},(_,i)=>({...source,id:i+1,order:i+1,name:'fixture '+i}));p.skeleton.layers=p.layers.map(l=>({id:String(l.id),groupId:null}));p.renderPieces=[];await DangoMiniPaint.load(p);});
 const before=await page.evaluate(()=>DangoMiniPaint.exportSnapshot());await paste();await page.waitForFunction(()=>!!document.querySelector('#editor_status')?.textContent);await settled();assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),before);assert.equal(await page.locator('#undo_button').isDisabled(),true);
});

test('total layer pixel budget rejects paste while preserving layers and history',async()=>{
 await page.evaluate(async()=>{const p=DangoMiniPaint.exportProject(),source=p.layers[0];p.layers=Array.from({length:8},(_,i)=>({...source,id:i+1,order:i+1,name:'fixture '+i,width:4000,height:2000}));p.skeleton.layers=p.layers.map(l=>({id:String(l.id),groupId:null}));p.renderPieces=[];await DangoMiniPaint.load(p);});
 const before=await page.evaluate(()=>DangoMiniPaint.exportSnapshot());await paste();await page.waitForFunction(()=>document.querySelector('#editor_status')?.textContent.includes('图片资源已达上限'));await settled();assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),before);assert.equal(await page.locator('#undo_button').isDisabled(),true);
});

test('decoded plus encoded undo payload budget rejects a large noisy image atomically',async()=>{
 const before=await page.evaluate(()=>DangoMiniPaint.exportSnapshot());await paste({width:2200,height:2200,noise:true});await page.waitForFunction(()=>document.querySelector('#editor_status')?.textContent.includes('撤销资源不足'));await settled();assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),before);assert.equal(await page.locator('#undo_button').isDisabled(),true);
});

async function pauseDecode(options){
 await page.evaluate(()=>{const decode=Image.prototype.decode;Image.prototype.decode=async function(){if(this.src.startsWith('blob:')){window.pasteDecodeStarted=true;await new Promise(resolve=>{window.releasePasteDecode=resolve;});}return decode.call(this);};});
 await paste(options);await page.waitForFunction(()=>window.pasteDecodeStarted===true);
}
test('pending decode blocks snapshot/confirm export and document load until insertion is complete',async()=>{
 const before=await page.evaluate(()=>DangoMiniPaint.exportProject());await pauseDecode();
 const guards=await page.evaluate(async before=>{
  const results=[];for(const operation of [()=>DangoMiniPaint.exportProject(),()=>DangoMiniPaint.exportPng(),()=>DangoMiniPaint.exportSnapshot(),()=>DangoMiniPaint.load(before)]){try{await operation();results.push(false);}catch(error){results.push(error.message.includes('正在粘贴'));}}return results;
 },before);assert.deepEqual(guards,[true,true,true,true]);
 await page.evaluate(()=>window.releasePasteDecode());await pasted();assert.equal((await page.evaluate(()=>DangoMiniPaint.exportProject())).layers.length,2);
});

test('an intervening project edit rejects stale decoded paste without undoing that edit',async()=>{
 const before=await page.evaluate(()=>DangoMiniPaint.exportProject());await pauseDecode();
 await page.locator('#new_group').click();await page.evaluate(()=>window.releasePasteDecode());
 await page.waitForFunction(()=>document.querySelector('#editor_status')?.textContent.includes('工程已变化'));await settled();
 const after=await page.evaluate(()=>DangoMiniPaint.exportProject());assert.equal(after.layers.length,1);assert.deepEqual(after.layers,before.layers);assert.equal(after.skeleton.groups.length,1);
 await page.keyboard.press('Control+z');await settled();assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportProject()),before);
});

test('header parser rejects mismatched and truncated data before decoding',()=>{
 assert.throws(()=>imageSize(Uint8Array.from([255,216,255,192,0,17,8]),'image/jpeg'));
 assert.throws(()=>imageSize(new Uint8Array(33),'image/png'));
});

for(const type of ['image/png','image/jpeg'])test(`${type} file drag/drop prevents navigation and shares group, export and undo history`,async()=>{
 await page.locator('#new_group').click();await settled();const before=await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),url=page.url();
 assert.deepEqual(await drop({type}),{dragover:true,drop:true});await dropped();
 const after=await page.evaluate(()=>DangoMiniPaint.exportSnapshot());assert.equal(page.url(),url);assert.equal(after.project.layers.length,2);assert.deepEqual(after.project.layers[0],before.project.layers[0]);
 const layer=after.project.layers[1];assert.equal(layer.name,'导入图片');assert.deepEqual([layer.x,layer.y,layer.width,layer.height],[24,18,16,12]);assert.equal(after.project.skeleton.layers.find(l=>l.id===String(layer.id)).groupId,after.project.skeleton.groups[0].id);
 const center=await pixel(after.png.pngBase64,32,24);assert.ok(Math.abs(center[0]-194)<3&&Math.abs(center[1]-157)<3&&Math.abs(center[2]-107)<3);
 await page.keyboard.press('Control+z');await settled();assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),before);
 await page.keyboard.press('Control+Shift+z');await settled();assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),after);
 await page.evaluate(p=>DangoMiniPaint.load(p),after.project);assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),after);
});

for(const options of [{bad:true},{type:'image/gif'},{width:4097,height:1},{count:2}])test(`invalid file drop ${JSON.stringify(options)} is rejected atomically`,async()=>{
 const before=await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),url=page.url();assert.deepEqual(await drop(options),{dragover:true,drop:true});await page.waitForFunction(()=>!!document.querySelector('#editor_status')?.textContent);await settled();assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),before);assert.equal(page.url(),url);assert.equal(await page.locator('#undo_button').isDisabled(),true);
 if(options.count===2)assert.equal(await page.locator('#editor_status').textContent(),'请每次拖入一张图片。');
});

test('file drop outside canvas and into text inputs is canceled without importing; text and internal drags stay native',async()=>{
 const before=await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),url=page.url();
 for(const target of ['#accessory_name','#layers_panel']){assert.deepEqual(await drop({target}),{dragover:true,drop:true});assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),before);}
 const native=await page.evaluate(()=>{const results=[];for(const target of ['#accessory_name','#layers_panel']){const data=new DataTransfer();data.setData('text/plain','fixture');const over=new DragEvent('dragover',{dataTransfer:data,bubbles:true,cancelable:true}),drop=new DragEvent('drop',{dataTransfer:data,bubbles:true,cancelable:true});const zone=document.querySelector(target);zone.dispatchEvent(over);zone.dispatchEvent(drop);results.push([over.defaultPrevented,drop.defaultPrevented]);}return results;});assert.deepEqual(native,[[false,false],[false,false]]);assert.equal(page.url(),url);
});

test('file drop obeys the existing layer budget and leaves undo history unchanged on rejection',async()=>{
 await page.evaluate(async()=>{const p=DangoMiniPaint.exportProject(),source=p.layers[0];p.layers=Array.from({length:16},(_,i)=>({...source,id:i+1,order:i+1,name:'fixture '+i}));p.skeleton.layers=p.layers.map(l=>({id:String(l.id),groupId:null}));p.renderPieces=[];await DangoMiniPaint.load(p);});
 const before=await page.evaluate(()=>DangoMiniPaint.exportSnapshot());await drop();await page.waitForFunction(()=>!!document.querySelector('#editor_status')?.textContent);await settled();assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),before);assert.equal(await page.locator('#undo_button').isDisabled(),true);
});

test('pending file drop blocks confirm/export and rejects a second image import',async()=>{
 await pauseDecode({gesture:'drop'});assert.deepEqual(await drop(),{dragover:true,drop:true});
 assert.equal(await page.evaluate(async()=>{try{await DangoMiniPaint.exportSnapshot();return false;}catch(error){return error.message.includes('正在导入');}}),true);
 await page.evaluate(()=>window.releasePasteDecode());await dropped();assert.equal((await page.evaluate(()=>DangoMiniPaint.exportProject())).layers.length,2);
});

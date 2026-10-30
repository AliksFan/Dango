import test,{before,after,beforeEach} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';import path from 'node:path';import http from 'node:http';import {fileURLToPath} from 'node:url';
import {build} from 'esbuild';import {chromium} from 'playwright-core';
import {controlledOverrides} from './controlled-overrides.mjs';
const here=path.dirname(fileURLToPath(import.meta.url)),root=path.resolve(here,'../../src/Dango.Desktop/Accessories/Editing/Web');
let server,browser,page;
before(async()=>{
 // Build only in memory. Do not rewrite the production bundle or manifest.
 const compiled=await build({plugins:[controlledOverrides],absWorkingDir:here,entryPoints:['entry.js'],bundle:true,minify:false,target:'chrome126',outfile:'bundle.js',inject:['./jquery-inject.js'],alias:{'dango-notify':path.join(here,'notify.js')},write:false});
 const bundle=compiled.outputFiles.find(f=>f.path.endsWith('.js')).contents;
 server=http.createServer((req,res)=>{const file=path.join(root,req.url==='/'?'index.html':req.url);res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html');try{res.end(req.url==='/bundle.js'?bundle:fs.readFileSync(file));}catch{res.writeHead(404);res.end();}});await new Promise(r=>server.listen(0,'127.0.0.1',r));
 browser=await chromium.launch({channel:'msedge',headless:true});
});
after(async()=>{await page?.close();await browser?.close();if(server)await new Promise(r=>server.close(r));});
beforeEach(async()=>{
 await page?.close();page=await browser.newPage({viewport:{width:1280,height:850}});
 const errors=[];page.on('pageerror',error=>errors.push(error.message));
 await page.addInitScript(()=>{
  const identity={protocol:2,sessionId:'a'.repeat(32),requestId:'b'.repeat(32),revision:1};let listener,copy;
  const fake=window.clipboardFixture={identity,fragment:null,outgoing:[],fail:false,ready:false};
  fake.send=(type,payload)=>listener?.({data:{...identity,type,payload}});
  const bytesOf=v=>new TextEncoder().encode(JSON.stringify(v)),b64=b=>btoa(String.fromCharCode(...b)),hash=async b=>[...new Uint8Array(await crypto.subtle.digest('SHA-256',b))].map(v=>v.toString(16).padStart(2,'0')).join('');
  fake.hash=hash;fake.bytesOf=bytesOf;fake.b64=b64;
  window.chrome={webview:{addEventListener:(name,fn)=>{listener=fn;},removeEventListener(){},postMessage:async v=>{
   fake.outgoing.push(v);
   if(v.type==='Ready')fake.ready=true;
   if(v.type==='ClipboardCopy')copy={clipboardId:v.payload.clipboardId,length:v.payload.bytes,chunks:[]};
   if(v.type==='ClipboardChunk'){copy.chunks.push(...Uint8Array.from(atob(v.payload.base64),c=>c.charCodeAt(0)));if(copy.chunks.length===copy.length){if(!fake.fail)fake.fragment=JSON.parse(new TextDecoder().decode(new Uint8Array(copy.chunks)));fake.send('ClipboardResult',{clipboardId:copy.clipboardId,ok:!fake.fail,bytes:0,sha256:''});}}
   if(v.type==='ClipboardPaste'){
    const clipboardId=v.payload.clipboardId;
    if(fake.fail){fake.send('ClipboardResult',{clipboardId,ok:false,bytes:0,sha256:''});return;}
    if(!fake.fragment){fake.send('ClipboardResult',{clipboardId,ok:true,bytes:0,sha256:''});return;}
    const bytes=bytesOf(fake.fragment);fake.send('ClipboardResult',{clipboardId,ok:true,bytes:bytes.length,sha256:await hash(bytes)});
    for(let at=0,index=0;at<bytes.length;at+=128*1024,index++)fake.send('ClipboardChunk',{clipboardId,index,base64:b64(bytes.subarray(at,at+128*1024))});
   }
  }}};
 });
 await page.goto(`http://127.0.0.1:${server.address().port}/`);await page.waitForFunction(()=>!!window.DangoMiniPaint,{},{timeout:5000}).catch(error=>{throw Error(errors.join('\n')||error.message);});
 await page.evaluate(async()=>{
  const c=document.createElement('canvas');c.width=64;c.height=48;const ctx=c.getContext('2d');ctx.fillStyle='#c82832';ctx.fillRect(0,0,32,48);ctx.fillStyle='#1432c8';ctx.fillRect(32,0,32,48);
  await DangoMiniPaint.load({width:64,height:48,pngBase64:c.toDataURL().split(',')[1]});
  const f=clipboardFixture,bytes=f.bytesOf({project:DangoMiniPaint.exportProject()});f.send('Open',{bytes:bytes.length,sha256:await f.hash(bytes)});f.send('Chunk',{index:0,base64:f.b64(bytes)});
 });await page.waitForFunction(()=>clipboardFixture.ready);
});
const snapshot=()=>page.evaluate(()=>DangoMiniPaint.exportSnapshot());
const settled=()=>page.waitForFunction(()=>{try{DangoMiniPaint.exportProject();return true;}catch{return false;}});
async function copy(target='#canvas_minipaint'){
 const prevented=await page.evaluate(target=>{if(document.querySelector('#editor_status'))document.querySelector('#editor_status').textContent='';const e=new ClipboardEvent('copy',{bubbles:true,cancelable:true});document.querySelector(target).dispatchEvent(e);return e.defaultPrevented;},target);
 if(prevented)await page.waitForFunction(()=>document.querySelector('#editor_status').textContent.includes('已复制')||document.querySelector('#editor_status').textContent.includes('未完成'));return prevented;
}
async function paste(target='#canvas_minipaint'){
 return page.evaluate(async target=>{if(document.querySelector('#editor_status'))document.querySelector('#editor_status').textContent='';const canvas=document.createElement('canvas');canvas.width=2;canvas.height=2;const blob=await new Promise(r=>canvas.toBlob(r)),data=new DataTransfer();data.items.add(new File([blob],'fixture.png',{type:'image/png'}));const e=new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true});document.querySelector(target).dispatchEvent(e);return e.defaultPrevented;},target);
}
async function pasted(){await page.waitForFunction(()=>document.querySelector('#editor_status').textContent.includes('已粘贴'));await settled();}
async function grouped(){
 await page.evaluate(async()=>{const p=DangoMiniPaint.exportProject();p.layers=p.layers.slice(0,1);p.layers.push({...structuredClone(p.layers[0]),id:2,order:2,name:'成员二',opacity:60});p.skeleton.groups=[{id:'parent',name:'外组',targetId:null,nodes:[{id:'parent-r',x:20,y:20}],parameters:{bend:.7,damping:6,restoration:5,gravity:0,elasticity:0}},{id:'source',name:'复制组',targetId:'parent-r',nodes:[{id:'source-r',x:20,y:20},{id:'source-j',x:32,y:30}],parameters:{bend:.4,damping:3,restoration:7,gravity:19,elasticity:.2}}];p.skeleton.layers=[{id:'1',groupId:'source'},{id:'2',groupId:'source'}];p.renderPieces=p.renderPieces.map(r=>({...r,groupId:'source'}));await DangoMiniPaint.load(p);});
}

test('copy keeps edit sequence/history; internal paste creates new layer and one exact undo/redo',async()=>{
 const before=await snapshot(),sequence=await page.evaluate(()=>DangoMiniPaint.getEditSequence());assert.equal(await copy(),true);
 assert.deepEqual(await snapshot(),before);assert.equal(await page.evaluate(()=>DangoMiniPaint.getEditSequence()),sequence);assert.equal(await page.locator('#undo_button').isDisabled(),true);
 await paste();await pasted();const after=await snapshot();assert.equal(after.project.layers.length,2);assert.notEqual(after.project.layers[1].id,after.project.layers[0].id);assert.equal(after.project.layers[1].type,'image');
 await page.keyboard.press('Control+z');await settled();assert.deepEqual(await snapshot(),before);await page.keyboard.press('Control+Shift+z');await settled();assert.deepEqual(await snapshot(),after);
});

test('selected group copies every member and parameters, detaches external roots and remaps every identity',async()=>{
 await grouped();await page.locator('[data-group-id=source]').click();const before=await snapshot();await copy();
 const fragment=await page.evaluate(()=>clipboardFixture.fragment);assert.equal(fragment.project.layers.length,2);assert.equal(fragment.project.skeleton.groups.length,1);assert.equal(fragment.project.skeleton.groups[0].targetId,null);assert.equal(fragment.project.skeleton.groups[0].nodes[0].x,20);
 await paste();await pasted();const after=await snapshot(),group=after.project.skeleton.groups.at(-1);assert.equal(after.project.layers.length,4);assert.notEqual(group.id,'source');assert.deepEqual(group.parameters,fragment.project.skeleton.groups[0].parameters);assert.equal(group.targetId,null);assert.ok(group.nodes.every(n=>!['source-r','source-j'].includes(n.id)));assert.equal(after.project.skeleton.layers.filter(l=>l.groupId===group.id).length,2);
 await page.keyboard.press('Control+z');await settled();assert.deepEqual(await snapshot(),before);await page.keyboard.press('Control+Shift+z');await settled();assert.deepEqual(await snapshot(),after);
});

test('clicking one member after group selection copies only that editable member',async()=>{
 await grouped();await page.locator('#geometry_modify').click();await page.locator('#layers_tab').click();await page.locator('[data-group-id=source]').click();await page.locator('#layer_name[data-id="1"]').click();await copy();const fragment=await page.evaluate(()=>clipboardFixture.fragment);assert.equal(fragment.project.layers.length,1);assert.equal(fragment.project.layers[0].id,1);assert.deepEqual(fragment.project.skeleton.groups,[]);assert.equal(fragment.project.skeleton.layers[0].groupId,null);
});

test('vector layers retain editable content through paste and saved-project reload',async()=>{
 await page.evaluate(async()=>{const p=DangoMiniPaint.exportProject(),l=p.layers[0];Object.assign(l,{type:'brush',is_vector:true,rotate:null,data:[[[10,10,1],[20,20,1],[30,15,1]]],params:{size:4,pressure:false},render_function:['brush','render']});delete l.assetId;await DangoMiniPaint.load(p);});
 const before=await snapshot();await copy();await paste();await pasted();const after=await snapshot(),layer=after.project.layers.at(-1);assert.equal(layer.type,'brush');assert.equal(layer.is_vector,true);assert.deepEqual(layer.data,before.project.layers[0].data);assert.deepEqual(layer.params,before.project.layers[0].params);
 await page.evaluate(p=>DangoMiniPaint.load(p),after.project);assert.deepEqual(await snapshot(),after);
});

test('image restore source survives editable copy; a group outside a smaller canvas is rejected atomically',async()=>{
 await page.evaluate(async()=>{const p=DangoMiniPaint.exportProject();p.layers[0].restoreAssetId=p.layers[0].assetId;await DangoMiniPaint.load(p);});await copy();await paste();await pasted();assert.ok((await snapshot()).project.layers.at(-1).restoreAssetId);
 await grouped();await page.locator('[data-group-id=source]').click();await copy();await page.evaluate(async()=>{const c=document.createElement('canvas');c.width=16;c.height=16;await DangoMiniPaint.load({width:16,height:16,pngBase64:c.toDataURL().split(',')[1]});});const before=await snapshot();await paste();await page.waitForFunction(()=>document.querySelector('#editor_status').textContent.includes('请先扩大画布'));await settled();assert.deepEqual(await snapshot(),before);assert.equal(await page.locator('#undo_button').isDisabled(),true);
});

test('empty group and empty layer structure paste without invented image layers',async()=>{
 await page.locator('#new_group').click();await settled();const group=(await snapshot()).project.skeleton.groups[0];const before=await snapshot();await copy();assert.equal((await page.evaluate(()=>clipboardFixture.fragment)).project.layers.length,0);
 await paste();await pasted();const after=await snapshot();assert.equal(after.project.layers.length,before.project.layers.length);assert.equal(after.project.skeleton.groups.length,2);assert.deepEqual(after.project.skeleton.groups[1].parameters,group.parameters);
 await page.keyboard.press('Control+z');await settled();assert.deepEqual(await snapshot(),before);
 await page.evaluate(async()=>{const p=DangoMiniPaint.exportProject(),l=p.layers[0];Object.assign(l,{type:null,width:null,height:null,width_original:null,height_original:null,data:null});delete l.assetId;p.assets=[];p.renderPieces=[];await DangoMiniPaint.load(p);});await copy();await paste();await pasted();const blank=(await snapshot()).project;assert.equal(blank.layers.length,2);assert.equal(blank.layers[1].type,null);
});

test('magic copy exports only selected source pixels without overlay and keeps the image editable',async()=>{
 await page.locator('#magic_erase').click();const point=await page.evaluate(()=>{const p=new DOMPoint(12,18).matrixTransform(document.querySelector('#geometry_overlay').getScreenCTM());return {x:p.x,y:p.y};});await page.mouse.click(point.x,point.y);await settled();const before=await snapshot();await copy();
 const pixels=await page.evaluate(async()=>{const f=clipboardFixture.fragment,i=new Image();i.src='data:image/png;base64,'+f.png.pngBase64;await i.decode();const c=document.createElement('canvas');c.width=64;c.height=48;const x=c.getContext('2d');x.drawImage(i,0,0);return [[...x.getImageData(12,18,1,1).data],[...x.getImageData(50,18,1,1).data]];});assert.deepEqual(pixels,[[200,40,50,255],[0,0,0,0]]);assert.deepEqual(await snapshot(),before);
 const source=await page.evaluate(async()=>{const p=clipboardFixture.fragment.project,l=p.layers[0],i=new Image();i.src='data:image/png;base64,'+p.assets.find(a=>a.id===l.assetId).pngBase64;await i.decode();const c=document.createElement('canvas');c.width=64;c.height=48;const x=c.getContext('2d');x.drawImage(i,0,0);return {outside:[...x.getImageData(50,18,1,1).data],restore:l.restoreAssetId??null};});assert.deepEqual(source,{outside:[0,0,0,0],restore:null});
 await paste();await pasted();assert.equal((await snapshot()).project.layers.length,2);
});

test('inputs/IME stay native; malformed fragments, clipboard failure and layer budget preserve the complete document',async()=>{
 const before=await snapshot(),count=await page.evaluate(()=>clipboardFixture.outgoing.length);assert.equal(await copy('#accessory_name'),false);assert.equal(await paste('#accessory_name'),false);assert.equal(await page.evaluate(()=>clipboardFixture.outgoing.length),count);
 await copy();await page.evaluate(()=>{clipboardFixture.fragment.project.layers[0].assetId='https://invalid.example/image.png';});await paste();await page.waitForFunction(()=>document.querySelector('#editor_status').textContent.includes('无效'));await settled();assert.deepEqual(await snapshot(),before);
 await page.evaluate(()=>{clipboardFixture.fail=true;});await copy();assert.deepEqual(await snapshot(),before);await page.evaluate(()=>{clipboardFixture.fail=false;});await copy();
 await page.evaluate(async()=>{const p=DangoMiniPaint.exportProject(),l=p.layers[0];p.layers=Array.from({length:16},(_,i)=>({...structuredClone(l),id:i+1,order:i+1}));p.skeleton.layers=p.layers.map(l=>({id:String(l.id),groupId:null}));await DangoMiniPaint.load(p);});const full=await snapshot();await paste();await page.waitForFunction(()=>document.querySelector('#editor_status').textContent.includes('图层已达上限'));await settled();assert.deepEqual(await snapshot(),full);
});

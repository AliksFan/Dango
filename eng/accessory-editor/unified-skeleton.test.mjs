import test,{before,after,beforeEach} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';import path from 'node:path';import http from 'node:http';import {fileURLToPath} from 'node:url';
import {chromium} from 'playwright-core';
import {analyzeMirror} from './mirror-oracle.mjs';
const here=path.dirname(fileURLToPath(import.meta.url)),root=path.resolve(here,'../../src/Dango.Desktop/Accessories/Editing/Web');
let server,browser,page;const errors=[];
before(async()=>{
 server=http.createServer((req,res)=>{const file=path.join(root,req.url==='/'?'index.html':req.url);res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html');try{res.end(fs.readFileSync(file));}catch{res.writeHead(404);res.end();}});await new Promise(r=>server.listen(0,'127.0.0.1',r));
 browser=await chromium.launch({channel:'msedge',headless:true});page=await browser.newPage({viewport:{width:1280,height:850}});page.on('pageerror',e=>errors.push(e.message));
});
after(async()=>{await browser?.close();if(server)await new Promise(r=>server.close(r));});
beforeEach(async()=>{
 await page.goto(`http://127.0.0.1:${server.address().port}/`);await page.waitForFunction(()=>!!window.DangoMiniPaint,{},{timeout:5000});
 await page.evaluate(async()=>{const c=document.createElement('canvas');c.width=400;c.height=300;const ctx=c.getContext('2d');ctx.fillStyle='#885e3e';ctx.fillRect(40,40,300,220);await DangoMiniPaint.load({width:400,height:300,pngBase64:c.toDataURL().split(',')[1]});});
});
const read=()=>page.evaluate(()=>DangoMiniPaint.getSkeleton());
const snapshot=()=>page.evaluate(()=>DangoMiniPaint.exportSnapshot());
async function at(p){return page.evaluate(p=>{const q=new DOMPoint(p.x,p.y).matrixTransform(document.querySelector('#geometry_overlay').getScreenCTM());return {x:q.x,y:q.y};},p);}
async function click(p,options){const q=await at(p);await page.mouse.click(q.x,q.y,options);}
async function drag(from,to){const a=await at(from),b=await at(to);await page.mouse.move(a.x,a.y);await page.mouse.down();await page.mouse.move(b.x,b.y,{steps:8});await page.mouse.up();}
async function fixture(){await page.evaluate(async()=>{
 const s=DangoMiniPaint.getSkeleton();s.pivot={id:'P',x:90,y:70};s.groups=[{id:'A',name:'甲',targetId:null,nodes:[{id:'AR',x:90,y:70},{id:'AJ',x:180,y:120},{id:'AE',x:270,y:150}]},{id:'B',name:'乙',targetId:'AJ',nodes:[{id:'BR',x:180,y:120},{id:'BJ',x:220,y:220}]}];s.layers[0].groupId='A';await DangoMiniPaint.setSkeleton(s,{reset:true});
});}
test('formal miniPaint layout has edge tools, a short palette and independent object inspector',async()=>{
 const boxes=await page.evaluate(()=>Object.fromEntries(['tools_container','geometry_modify','geometry_draw','colors_panel','properties_panel'].map(id=>{const r=document.getElementById(id).getBoundingClientRect();return [id,{x:r.x,y:r.y,width:r.width,height:r.height}];})));
 assert.ok(boxes.geometry_modify.x<boxes.properties_panel.x);assert.ok(boxes.colors_panel.y<boxes.properties_panel.y);assert.ok(boxes.colors_panel.height<boxes.properties_panel.height);
 await page.locator('[data-panel="colors_panel"]').click();assert.equal(await page.locator('#colors_panel').getAttribute('data-expanded'),'false');assert.equal(await page.locator('#properties_panel').getAttribute('data-expanded'),'true');assert.ok(await page.locator('#geometry_draw').isVisible());
});
test('new group/root, real draw clicks and original miniPaint brush share a single undo history',async()=>{
 await page.locator('#new_group').click();let s=await read();assert.equal(s.groups.length,1);assert.equal(s.groups[0].nodes.length,1);await page.locator('#geometry_draw').click();await click({x:280,y:210});s=await read();assert.equal(s.groups[0].nodes.length,2);const drawn=structuredClone(s);
 await page.locator('#brush').click();const from=await at({x:60,y:55}),to=await at({x:100,y:75});const pixelsBefore=(await snapshot()).png.pngBase64;await page.mouse.move(from.x,from.y);await page.mouse.down();await page.mouse.move(to.x,to.y,{steps:5});await page.mouse.up();
 assert.notEqual((await snapshot()).png.pngBase64,pixelsBefore);await page.locator('#undo_button').click();assert.equal((await snapshot()).png.pngBase64,pixelsBefore);assert.deepEqual((await read()).groups,drawn.groups);
 // miniPaint tool switches themselves are historical actions; one more undo removes
 // the brush-tool switch, the next removes the last skeleton edit.
 await page.locator('#undo_button').click();await page.locator('#undo_button').click();assert.equal((await read()).groups[0].nodes.length,1);
});
test('dragging an attached root in the real editor moves the other connection endpoint once',async()=>{
 await fixture();await page.locator('[data-group-id="B"]').click();await page.locator('#geometry_modify').click();const before=await read();await drag({x:180,y:120},{x:200,y:140});const s=await read();
 for(const n of [s.groups[0].nodes[1],s.groups[1].nodes[0]]){assert.ok(Math.abs(n.x-200)<.001);assert.ok(Math.abs(n.y-140)<.001);}assert.deepEqual(s.groups[1].nodes[1],before.groups[1].nodes[1]);
 await page.locator('#undo_button').click();assert.deepEqual(await read(),before);
});
test('group parameters and name survive schema2 roundtrip; malformed source keeps the full current project',async()=>{
 await fixture();await page.locator('[data-group-id="B"]').click();await page.locator('#skeleton_tab').click();await page.locator('[type="number"][data-skeleton-param="elasticity"]').fill('50');await page.locator('[type="number"][data-skeleton-param="elasticity"]').press('Tab');await page.locator('#accessory_name').fill('双向彩带');await page.locator('#accessory_name').press('Tab');
 let saved=await snapshot();assert.equal(saved.project.name,'双向彩带');assert.equal(saved.project.skeleton.groups[1].parameters.elasticity,.5);assert.equal(saved.project.renderPieces.length,1);assert.equal(saved.project.renderPieces[0].groupId,'A');
 await page.evaluate(p=>DangoMiniPaint.load(p),saved.project);const reopened=await snapshot();assert.deepEqual(reopened.project.skeleton,saved.project.skeleton);assert.equal(reopened.png.pngBase64,saved.png.pngBase64);
 const result=await page.evaluate(async()=>{const before=DangoMiniPaint.exportProject(),bad=structuredClone(before);bad.skeleton.groups[1].targetId='missing';try{await DangoMiniPaint.load(bad);return false;}catch{return JSON.stringify(DangoMiniPaint.exportProject())===JSON.stringify(before);}});assert.equal(result,true);
});
test('layer grouping changes real rendering slices and undo restores membership',async()=>{
 await page.locator('#new_group').click();const s=await read();await page.locator('#layer_properties_tab').click();await page.locator('#assign_group').click();assert.equal((await snapshot()).project.renderPieces[0].groupId,s.groups[0].id);
 await page.locator('#undo_button').click();assert.equal((await snapshot()).project.renderPieces[0].groupId,null);
});
test('whole-image transforms keep all connected nodes, layer pixels and undo atomic',async()=>{
 await fixture();const before=await snapshot();await page.evaluate(()=>DangoMiniPaint.transformCanvas('flipX'));let after=await snapshot();assert.equal(after.project.skeleton.pivot.x,310);assert.equal(after.project.skeleton.groups[0].nodes[1].x,220);assert.equal(after.project.skeleton.groups[1].nodes[0].x,220);
 await page.locator('#undo_button').click();after=await snapshot();assert.deepEqual(after.project.skeleton,before.project.skeleton);assert.equal(after.png.pngBase64,before.png.pngBase64);
});
test('a non-top active group receives newly created image layers inside its own contiguous block',async()=>{
 await page.evaluate(async()=>{const p=DangoMiniPaint.exportProject(),layer={...p.layers[0],id:2,order:2,name:'上层'};p.layers.push(layer);const parameters={bend:.7,damping:6,restoration:5,gravity:0,elasticity:0};p.skeleton.groups=[{id:'A',name:'甲',targetId:null,nodes:[{id:'AR',x:90,y:70}],parameters},{id:'B',name:'乙',targetId:null,nodes:[{id:'BR',x:280,y:180}],parameters}];p.skeleton.layers=[{id:'1',groupId:'A'},{id:'2',groupId:'B'}];p.renderPieces=[{groupId:'A',assetId:p.assets[0].id},{groupId:'B',assetId:p.assets[0].id}];await DangoMiniPaint.load(p);});
 await page.locator('[data-group-id="A"]').click();await page.locator('#layers_tab').click();await page.locator('#insert_layer').click();let s=await read();assert.deepEqual(s.layers.map(l=>l.groupId),['A','A','B']);assert.equal(s.layers[0].id,'1');assert.equal(s.layers[2].id,'2');
 await page.locator('#undo_button').click();s=await read();assert.deepEqual(s.layers,[{id:'1',groupId:'A'},{id:'2',groupId:'B'}]);await page.locator('#redo_button').click();assert.deepEqual((await read()).layers.map(l=>l.groupId),['A','A','B']);
});
test('equal-order layers use miniPaint paint order in the composite PNG and independent rendering slices',async()=>{
 const result=await page.evaluate(async()=>{
  const p=DangoMiniPaint.exportProject(),c=document.createElement('canvas');c.width=400;c.height=300;const ctx=c.getContext('2d');ctx.fillStyle='red';ctx.fillRect(0,0,400,300);const red=c.toDataURL().split(',')[1];ctx.fillStyle='blue';ctx.fillRect(0,0,400,300);const blue=c.toDataURL().split(',')[1];p.assets=[{id:'asset-1',pngBase64:red},{id:'asset-2',pngBase64:blue}];p.layers=[{...p.layers[0],id:1,order:1,assetId:'asset-1'},{...p.layers[0],id:2,order:1,assetId:'asset-2'}];p.skeleton.layers=[{id:'2',groupId:null},{id:'1',groupId:null}];p.renderPieces=[{groupId:null,assetId:'asset-1'}];await DangoMiniPaint.load(p);const s=await DangoMiniPaint.exportSnapshot(),piece=s.project.assets.find(a=>a.id===s.project.renderPieces[0].assetId);return {equal:s.png.pngBase64===piece.pngBase64,order:s.project.skeleton.layers.map(l=>l.id)};
 });assert.equal(result.equal,true);assert.deepEqual(result.order,['2','1']);
});
test('asynchronous whole-image preparation blocks stale transforms, resets and incomplete exports',async()=>{
 const before=await read();const result=await page.evaluate(async()=>{
  const decode=Image.prototype.decode;let release;const barrier=new Promise(r=>release=r);Image.prototype.decode=function(){return decode.call(this).then(()=>barrier);};
  const operation=DangoMiniPaint.transformCanvas('flipX');let second=false,reset=false,exported=false;
  try{await DangoMiniPaint.transformCanvas('flipY');}catch{second=true;}try{await DangoMiniPaint.setSkeleton(DangoMiniPaint.getSkeleton(),{reset:true});}catch{reset=true;}try{await DangoMiniPaint.exportSnapshot();}catch{exported=true;}
  release();try{await operation;}finally{Image.prototype.decode=decode;}return {second,reset,exported};
 });assert.deepEqual(result,{second:true,reset:true,exported:true});const after=await read();assert.equal(after.pivot.x,400-before.pivot.x);assert.deepEqual(after.groups,before.groups);
});
test('first erase baseline is atomic on image load failure and complete undo',async()=>{
 await page.locator('#erase').click();const before=await snapshot();await page.evaluate(()=>{window.originalImageSource=Object.getOwnPropertyDescriptor(HTMLImageElement.prototype,'src');Object.defineProperty(HTMLImageElement.prototype,'src',{...originalImageSource,set(){queueMicrotask(()=>this.dispatchEvent(new Event('error')));}});});
 const from=await at({x:100,y:100}),to=await at({x:115,y:110});await page.mouse.move(from.x,from.y);await page.mouse.down();await page.mouse.move(to.x,to.y);await page.mouse.up();
 await page.waitForFunction(()=>{try{DangoMiniPaint.exportProject();return true;}catch{return false;}});await page.evaluate(()=>Object.defineProperty(HTMLImageElement.prototype,'src',originalImageSource));assert.deepEqual(await snapshot(),before);
 await page.mouse.move(from.x,from.y);await page.mouse.down();await page.mouse.move(to.x,to.y);await page.mouse.up();await page.waitForFunction(()=>{try{return !!DangoMiniPaint.exportProject().layers[0].restoreAssetId;}catch{return false;}});
 await page.locator('#undo_button').click();await page.waitForFunction(()=>{try{return !DangoMiniPaint.exportProject().layers[0].restoreAssetId;}catch{return false;}});assert.deepEqual(await snapshot(),before);
});
test('an in-flight image undo rejects a second undo and keeps complete snapshots blocked',async()=>{
 await page.locator('#erase').click();const from=await at({x:90,y:90}),to=await at({x:110,y:110});await page.mouse.move(from.x,from.y);await page.mouse.down();await page.mouse.move(to.x,to.y);await page.mouse.up();await page.waitForFunction(()=>{try{return !!DangoMiniPaint.exportProject().layers[0].restoreAssetId;}catch{return false;}});
 await page.evaluate(()=>{const descriptor=Object.getOwnPropertyDescriptor(HTMLImageElement.prototype,'src');window.waitingImageLoad=false;Object.defineProperty(HTMLImageElement.prototype,'src',{...descriptor,set(value){window.waitingImageLoad=true;window.releaseImageLoad=()=>{Object.defineProperty(HTMLImageElement.prototype,'src',descriptor);descriptor.set.call(this,value);};}});});
 await page.locator('#undo_button').click();await page.waitForFunction(()=>window.waitingImageLoad);await page.locator('#undo_button').click();assert.equal(await page.evaluate(async()=>{try{await DangoMiniPaint.exportSnapshot();return false;}catch{return true;}}),true);
 await page.evaluate(()=>window.releaseImageLoad());await page.waitForFunction(()=>{try{return !DangoMiniPaint.exportProject().layers[0].restoreAssetId;}catch{return false;}});assert.equal(await page.locator('#undo_button').isDisabled(),false,'erase tool selection remains before the one completed undo');
});
test('layer drag snapshots cannot resurrect a group after an intervening undo',async()=>{
 await page.locator('#new_group').click();const id=(await read()).groups[0].id;
 await page.evaluate(id=>{const transfer=new DataTransfer();window.layerDragTransfer=transfer;const row=document.querySelector('[data-image-layer="1"]');row.dispatchEvent(new DragEvent('dragstart',{bubbles:true,dataTransfer:transfer}));document.querySelector(`[data-group-target="${id}"]`).dispatchEvent(new DragEvent('dragover',{bubbles:true,cancelable:true,dataTransfer:transfer}));},id);
 await page.locator('#undo_button').click();const before=await read();assert.equal(before.groups.length,0);await page.locator('#layers_base').dispatchEvent('drop',{dataTransfer:await page.evaluateHandle(()=>window.layerDragTransfer)});assert.deepEqual(await read(),before);
});
test('rotated brush and asymmetrically flipped image keep correct world-space pixels through transforms',async()=>{
 const cases=await page.evaluate(async()=>{
  const base=DangoMiniPaint.exportProject(),results=[];
  for(const type of ['brush','image'])for(const kind of ['flipX','flipY','left']){
   const p=structuredClone(base),l={...p.layers[0],x:120,y:100,width:80,height:40,rotate:30,flipX:true,flipY:false};
   if(type==='brush'){Object.assign(l,{type:'brush',is_vector:true,render_function:['brush','render'],params:{size:4,pressure:false},data:[[[10,10,4],[60,20,4]]],color:'#885e3e'});delete l.assetId;delete l.restoreAssetId;}
   p.layers=[l];await DangoMiniPaint.load(p);const before=DangoMiniPaint.exportPng();await DangoMiniPaint.transformCanvas(kind);const actual=DangoMiniPaint.exportPng(),afterLayer=DangoMiniPaint.exportProject().layers[0];
   const world=(layer,x,y)=>{const cx=layer.x+layer.width/2,cy=layer.y+layer.height/2,dx=(x-layer.width/2)*(layer.flipX?-1:1),dy=(y-layer.height/2)*(layer.flipY?-1:1),a=(layer.rotate||0)*Math.PI/180;return {x:cx+dx*Math.cos(a)-dy*Math.sin(a),y:cy+dx*Math.sin(a)+dy*Math.cos(a)};};
   let controlError=0;for(let i=0;i<2;i++){
    const oldPoint=type==='brush'?l.data[0][i]:[l.width*(i?.8:.2),l.height*(i?.7:.3)],oldWorld=world(l,...oldPoint),expectedWorld=kind==='flipX'?{x:400-oldWorld.x,y:oldWorld.y}:kind==='flipY'?{x:oldWorld.x,y:300-oldWorld.y}:{x:oldWorld.y,y:400-oldWorld.x};
    let nextPoint;if(type==='brush')nextPoint=afterLayer.data[0][i];else{const u=oldPoint[0]/l.width,v=oldPoint[1]/l.height;nextPoint=kind==='flipX'?[(1-u)*afterLayer.width,v*afterLayer.height]:kind==='flipY'?[u*afterLayer.width,(1-v)*afterLayer.height]:[v*afterLayer.width,(1-u)*afterLayer.height];}
    const actualWorld=world(afterLayer,...nextPoint);controlError=Math.max(controlError,Math.hypot(actualWorld.x-expectedWorld.x,actualWorld.y-expectedWorld.y));
   }
   const decode=async data=>{const image=new Image();image.src='data:image/png;base64,'+data;await image.decode();return image;},c=document.createElement('canvas');c.width=actual.width;c.height=actual.height;const ctx=c.getContext('2d');
   if(kind==='flipX'){ctx.translate(400,0);ctx.scale(-1,1);}else if(kind==='flipY'){ctx.translate(0,300);ctx.scale(1,-1);}else{ctx.translate(0,400);ctx.rotate(-Math.PI/2);}ctx.drawImage(await decode(before.pngBase64),0,0);const expected=Array.from(ctx.getImageData(0,0,c.width,c.height).data);
   ctx.setTransform(1,0,0,1,0,0);ctx.clearRect(0,0,c.width,c.height);ctx.drawImage(await decode(actual.pngBase64),0,0);results.push({type,kind,width:c.width,height:c.height,controlError,expected,actual:Array.from(ctx.getImageData(0,0,c.width,c.height).data)});
  }return results;
 });for(const item of cases){const stats=analyzeMirror(item.expected,item.actual,item.width,item.height);assert.ok(item.controlError<1e-6,`${item.type}/${item.kind}: exact world geometry must match`);assert.equal(stats.outsideEdgeBand,0);assert.equal(stats.interiorMismatch,0);assert.ok(stats.massA>0&&stats.massB>0);assert.ok(Math.abs(stats.massA-stats.massB)/stats.massA<.05);
  // Re-rasterizing a rotated vector can bias edge coverage within the pixel grid.
  // Geometry above is exact; permit at most half a raster pixel in the silhouette centroid.
  assert.ok(stats.centroidDelta<.5,`${item.type}/${item.kind}: raster coverage center`);
 }
});
test('canvas expansion preserves pixels and shifts all rest coordinates in one reversible action',async()=>{
 await fixture();const before=await snapshot();await page.locator('.image-options>summary').click();await page.locator('#canvas_expand').click();await page.locator('#canvas_expand_dialog [name=width]').fill('440');await page.locator('#canvas_expand_dialog [name=height]').fill('340');await page.locator('#canvas_expand_dialog [type=submit]').click();
 const after=await snapshot();assert.equal(after.png.width,440);assert.equal(after.png.height,340);assert.equal(after.project.skeleton.pivot.x,110);assert.equal(after.project.skeleton.pivot.y,90);assert.equal(after.project.skeleton.groups[0].nodes[1].x,200);assert.equal(after.project.skeleton.groups[1].nodes[0].x,200);
 await page.locator('#undo_button').click();assert.deepEqual(await snapshot(),before);
});
test('formal minimum and portrait layouts keep tools reachable without scrolling object properties',async()=>{
 await fixture();await page.locator('[data-group-id="B"]').click();
 for(const size of [{width:784,height:548},{width:840,height:750},{width:800,height:850}]){
  await page.setViewportSize(size);const result=await page.evaluate(()=>{
   const bone=document.getElementById('geometry_modify').getBoundingClientRect(),panel=document.querySelector('#properties_panel .panel-body'),bounds=panel.getBoundingClientRect();return {toolLeft:bone.left,toolRight:bone.right,width:innerWidth,bodyBottom:bounds.bottom,viewportHeight:innerHeight,client:panel.clientHeight,scroll:getComputedStyle(panel).overflowY};
  });assert.ok(result.toolLeft>=0&&result.toolRight<=result.width);assert.ok(result.bodyBottom<=result.viewportHeight);assert.ok(result.client>0);assert.equal(result.scroll,'hidden');
 }
 await page.setViewportSize({width:1280,height:850});
});
test('no JavaScript errors across integrated image and skeleton operations',()=>assert.deepEqual(errors,[]));

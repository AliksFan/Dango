import test,{before,after,beforeEach} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';import path from 'node:path';import http from 'node:http';import {fileURLToPath} from 'node:url';
import {build} from 'esbuild';import {chromium} from 'playwright-core';import {controlledOverrides} from './controlled-overrides.mjs';
const here=path.dirname(fileURLToPath(import.meta.url));let server,browser,page;const errors=[];
before(async()=>{
 const result=await build({plugins:[controlledOverrides],absWorkingDir:here,entryPoints:['entry.js'],bundle:true,target:'chrome126',outfile:path.join(here,'test-memory/bundle.js'),inject:['./jquery-inject.js'],alias:{'dango-notify':path.join(here,'notify.js')},write:false});
 const resources=new Map(result.outputFiles.map(f=>[f.path.endsWith('.css')?'/theme.css':'/bundle.js',f.contents]));
 resources.set('/',fs.readFileSync(path.join(here,'index.html')));
 // Allows this standalone module gate to run before the integration owner adds the CSS import.
 if(!fs.readFileSync(path.join(here,'entry.js'),'utf8').includes("'./text-toolbar.css'"))resources.set('/theme.css',Buffer.concat([Buffer.from(resources.get('/theme.css')),fs.readFileSync(path.join(here,'text-toolbar.css'))]));
 server=http.createServer((req,res)=>{res.setHeader('Content-Type',req.url.endsWith('.js')?'text/javascript':req.url.endsWith('.css')?'text/css':'text/html');const bytes=resources.get(req.url);if(!bytes)res.writeHead(404);res.end(bytes);});await new Promise(r=>server.listen(0,'127.0.0.1',r));
 browser=await chromium.launch(process.env.DANGO_BROWSER_EXECUTABLE?{executablePath:process.env.DANGO_BROWSER_EXECUTABLE,headless:true}:{channel:'msedge',headless:true});
});
after(async()=>{await browser?.close();if(server)await new Promise(r=>server.close(r));});
beforeEach(async()=>{await page?.close();errors.length=0;page=await browser.newPage({viewport:{width:1104,height:668}});page.on('pageerror',e=>errors.push(e.message));await page.goto(`http://127.0.0.1:${server.address().port}/`);await page.waitForFunction(()=>!!window.DangoMiniPaint);await page.evaluate(async()=>{const c=document.createElement('canvas');c.width=400;c.height=300;await DangoMiniPaint.load({width:400,height:300,pngBase64:c.toDataURL().split(',')[1]});});await page.locator('#text').click();});
const settled=()=>page.waitForFunction(()=>{try{DangoMiniPaint.exportProject();return true;}catch{return false;}});
async function createText(){const p=await page.evaluate(()=>{const q=new DOMPoint(80,90).matrixTransform(document.querySelector('#geometry_overlay').getScreenCTM());return {x:q.x,y:q.y};});await page.mouse.click(p.x,p.y);await page.keyboard.type('Dango');await page.keyboard.press('Control+a');await page.locator('#font').focus();await settled();}
const spans=()=>page.evaluate(()=>DangoMiniPaint.exportProject().layers.find(l=>l.type==='text').data.flat());
async function redPixels(){return page.evaluate(async()=>{const image=new Image();image.src='data:image/png;base64,'+DangoMiniPaint.exportPng().pngBase64;await image.decode();const c=document.createElement('canvas');c.width=image.width;c.height=image.height;const ctx=c.getContext('2d');ctx.drawImage(image,0,0);const p=ctx.getImageData(0,0,c.width,c.height).data;let count=0;for(let i=0;i<p.length;i+=4)if(p[i]===196&&p[i+1]===50&&p[i+2]===25&&p[i+3]>0)count++;return count;});}

for(const size of [{width:1104,height:668},{width:784,height:548}])test(`text controls remain visible and advanced options stay in the popover at ${size.width}`,async()=>{
 await page.setViewportSize(size);await page.locator('#text_more').waitFor();
 for(const id of ['font','size','bold','italic','underline']){assert.equal(await page.locator('#action_attributes #'+id).isVisible(),true,id);assert.equal(await page.locator('#text_more #'+id).count(),0,id+' must stay outside advanced options');}
 for(const id of ['strikethrough','stroke','stroke_size','kerning','leading'])assert.equal(await page.locator('#action_attributes #'+id).isVisible(),false,id);
 const controls=await page.locator('#action_attributes').evaluate(n=>[...n.children].filter(c=>c.checkVisibility()).map(c=>({x:c.getBoundingClientRect().x,y:c.getBoundingClientRect().y,right:c.getBoundingClientRect().right})));
 assert.ok(controls.every(c=>Math.abs(c.y-controls[0].y)<2&&c.right<=innerWidthForTest(size)));
 await page.locator('.image-options > summary').click();
 await page.locator('#text_more summary').click();assert.equal(await page.locator('#stroke_size').isVisible(),true);
 await page.waitForFunction(()=>document.querySelector('#text_more').style.getPropertyValue('--popover-left')!=='');
 assert.equal(await page.locator('.image-options').getAttribute('open'),null);
 const popover=await page.locator('.text-more-body').boundingBox();assert.ok(popover.x>=0&&popover.x+popover.width<=size.width&&popover.y+popover.height<=size.height,'more options stay inside the window');
 await page.keyboard.press('Escape');assert.equal(await page.locator('#text_more').getAttribute('open'),null);
 await page.locator('#text_more summary').click();await page.locator('#brush').click();assert.equal(await page.locator('#text_more').count(),0);assert.equal(await page.locator('#action_attributes .size').isVisible(),true);
 await page.locator('#text').click();assert.equal(await page.locator('#text_more').count(),1);assert.equal(await page.locator('#text_more').getAttribute('open'),null);assert.deepEqual(errors,[]);
});
function innerWidthForTest(size){return size.width;}

for(const size of [{width:1104,height:668},{width:784,height:548}])test(`full toolbar geometry stays stable from brush to text at ${size.width}`,async()=>{
 await page.setViewportSize(size);
 const geometry=async()=>{await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));return page.evaluate(()=>{const rect=n=>{const r=n.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,right:r.right};};return {nav:rect(document.querySelector('#main_menu')),bottom:getComputedStyle(document.querySelector('.unified-editor')).getPropertyValue('--toolbar-bottom'),groups:[...document.querySelectorAll('#main_menu>.toolbar-group')].map(rect)};});};
 await page.locator('#brush').click();const brush=await geometry();await page.locator('#text').click();const text=await geometry();
 assert.equal(text.nav.height,brush.nav.height,'switching to text must not add a toolbar row');assert.equal(text.bottom,brush.bottom);
 assert.deepEqual(text.groups.slice(0,2),brush.groups.slice(0,2),'document and view groups retain their positions and dimensions');
 assert.equal(text.groups[2].height,40,'the text parameter group stays on one line');
 assert.ok(text.groups[2].right<=size.width-132,'text controls must not enter the native window-button region');
});

test('native text input, formatting, palette color and advanced parameters change saved text and exported pixels',async()=>{
 await createText();assert.equal((await spans()).map(s=>s.text).join(''),'Dango');
 const before=await page.evaluate(()=>DangoMiniPaint.exportPng().pngBase64);
 await page.locator('#bold').click();await settled();assert.ok((await spans()).every(s=>s.meta.bold===true));
 await page.locator('#size input').fill('36');await page.locator('#size input').press('Tab');await settled();assert.ok((await spans()).every(s=>s.meta.size===36));
 await page.locator('#font').selectOption('Courier New');await settled();assert.ok((await spans()).every(s=>s.meta.family==='Courier New'));
 await page.locator('#color_hex').fill('#c43219');await page.locator('#color_hex').press('Tab');await settled();assert.ok((await spans()).every(s=>s.meta.fill_color.toLowerCase()==='#c43219'));
 assert.ok(await redPixels()>20,'the shared palette must change actual rendered text pixels');
 await page.locator('#text_more summary').click();
 for(const [id,value,meta]of [['stroke_size','1.5','stroke_size'],['kerning','3','kerning'],['leading','5','leading']]){await page.locator('#'+id+' input').fill(value);await page.locator('#'+id+' input').press('Tab');await settled();assert.ok((await spans()).every(s=>s.meta[meta]===Number(value)),meta);}
 await page.locator('#strikethrough').click();await settled();assert.ok((await spans()).every(s=>s.meta.strikethrough===true));
 assert.notEqual(await page.evaluate(()=>DangoMiniPaint.exportPng().pngBase64),before);assert.deepEqual(errors,[]);
});

test('palette color selected before insertion colors new text pixels',async()=>{
 await page.locator('#color_hex').fill('#c43219');await page.locator('#color_hex').press('Tab');
 await createText();assert.ok((await spans()).every(s=>s.meta.fill_color.toLowerCase()==='#c43219'));assert.ok(await redPixels()>20);assert.deepEqual(errors,[]);
});

test('entering text tool preserves existing text pixels and image data stays untouched by palette changes',async()=>{
 await page.locator('#color_hex').fill('#c43219');await page.locator('#color_hex').press('Tab');
 assert.equal(await page.evaluate(()=>DangoMiniPaint.exportProject().layers[0].data),null,'choosing text palette must not create text data on the image layer');
 await createText();await page.locator('#brush').click();
 await page.locator('#color_hex').fill('#254b83');await page.locator('#color_hex').press('Tab');await settled();
 const before=await page.evaluate(()=>({png:DangoMiniPaint.exportPng().pngBase64,project:DangoMiniPaint.exportProject()}));
 await page.locator('#text').click();await settled();
 assert.equal(await page.evaluate(()=>DangoMiniPaint.exportPng().pngBase64),before.png);assert.deepEqual(await spans(),before.project.layers.find(l=>l.type==='text').data.flat());assert.deepEqual(errors,[]);
});

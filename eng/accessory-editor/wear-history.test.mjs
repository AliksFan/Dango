import test from 'node:test';import assert from 'node:assert/strict';
import fs from 'node:fs';import path from 'node:path';import http from 'node:http';import {fileURLToPath} from 'node:url';
import {before,after,beforeEach} from 'node:test';import {chromium} from 'playwright-core';
const here=path.dirname(fileURLToPath(import.meta.url)),root=path.resolve(here,'../../src/Dango.Desktop/Accessories/Editing/Web');
const wear={bodyAnchorX:.5,bodyAnchorY:0,sizePercent:100,baseAngleDegrees:0,connectionStrength:100,maxConnectionTravelPercent:75,rotationDamping:60,lockRotation:true,returnStrength:60,maxSwingDegrees:45,limitSwing:true,gravityInfluence:0,layer:'Front'};
let server,browser,page;
before(async()=>{server=http.createServer((req,res)=>{const file=path.join(root,req.url==='/'?'index.html':decodeURIComponent(req.url).replace(/^\/+/,''));res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html');try{res.end(fs.readFileSync(file));}catch{res.writeHead(404);res.end();}});await new Promise(r=>server.listen(0,'127.0.0.1',r));browser=await chromium.launch({channel:'msedge',headless:true});page=await browser.newPage({viewport:{width:1104,height:668}});});
after(async()=>{await browser?.close();if(server){server.closeAllConnections();await new Promise(r=>server.close(r));}});
beforeEach(async()=>{await page.goto(`http://127.0.0.1:${server.address().port}/`);await page.waitForFunction(()=>!!window.DangoMiniPaint);await page.evaluate(w=>{window.wearChanges=[];window.projectChanges=0;window.addEventListener('dango-wear-changed',e=>wearChanges.push(e.detail));window.addEventListener('dango-skeleton-changed',()=>projectChanges++);window.dispatchEvent(new CustomEvent('dango-wear-state',{detail:w}));},wear);});
test('wearing and native anchor edits use one history while image pixels and geometry remain unchanged',async()=>{
 const before=await page.evaluate(()=>DangoMiniPaint.exportSnapshot());await page.locator('#dynamic_preview_tab').click();
 await page.locator('#wear_connectionStrength').fill('37');await page.locator('#wear_connectionStrength').press('Enter');try{await page.waitForFunction(()=>DangoMiniPaint.getWearParameters().connectionStrength===37,{},{timeout:3000});}catch(error){throw Error(JSON.stringify(await page.evaluate(()=>({wear:DangoMiniPaint.getWearParameters(),status:document.querySelector('#editor_status')?.textContent,fields:[...document.querySelectorAll('#simulation_wear_form input,#simulation_wear_form select')].map(n=>({name:n.name,value:n.value,checked:n.checked,disabled:n.disabled})),revision:DangoMiniPaint.getEditSequence()}))));}
 await page.evaluate(w=>window.dispatchEvent(new CustomEvent('dango-wear-edit-request',{detail:{...w,bodyAnchorX:.25,bodyAnchorY:.8}})),wear);
 await page.waitForFunction(()=>DangoMiniPaint.getWearParameters().bodyAnchorY===.8);
 assert.equal(await page.evaluate(()=>DangoMiniPaint.getWearParameters().connectionStrength),37,'A native anchor response must not overwrite a more recent browser wearing edit.');
 assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),before);assert.equal(await page.evaluate(()=>projectChanges),0);
 await page.evaluate(()=>DangoMiniPaint.undo());await page.waitForFunction(()=>DangoMiniPaint.getWearParameters().bodyAnchorY===0);
 assert.equal(await page.evaluate(()=>DangoMiniPaint.getWearParameters().connectionStrength),37);
 await page.evaluate(()=>DangoMiniPaint.undo());await page.waitForFunction(()=>DangoMiniPaint.getWearParameters().connectionStrength===100);
 await page.evaluate(()=>DangoMiniPaint.redo());await page.waitForFunction(()=>DangoMiniPaint.getWearParameters().connectionStrength===37);
 assert.equal(await page.evaluate(()=>projectChanges),0);assert.equal(await page.evaluate(()=>wearChanges.length),5);
});
test('simulation blocks image shortcuts and rejects unsettled fields before mode switching or confirmation',async()=>{
 const before=await page.evaluate(()=>DangoMiniPaint.exportSnapshot());await page.locator('#dynamic_preview_tab').click();
 await page.locator('#dynamic_preview_tab').blur();for(const key of ['b','k','v','Delete'])await page.keyboard.press(key);
 assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.exportSnapshot()),before);assert.equal(await page.locator('.sidebar_left').isVisible(),false);
 await page.locator('#wear_connectionStrength').fill('101');
 const error=await page.evaluate(async()=>{try{await DangoMiniPaint.exportSnapshot();return null;}catch(e){return e.message;}});assert.ok(error);
 await page.locator('#palette_tab').click();assert.equal(await page.locator('#dynamic_preview_tab').getAttribute('aria-selected'),'true');
 await page.evaluate(w=>window.dispatchEvent(new CustomEvent('dango-wear-state',{detail:w})),wear);await page.locator('#palette_tab').click();assert.equal(await page.locator('.middle_area').isVisible(),true);
});
test('invalid wearing fields never enter history or get sent to native',async()=>{
 const revision=await page.evaluate(()=>DangoMiniPaint.getEditSequence());
 await page.evaluate(w=>{window.dispatchEvent(new CustomEvent('dango-wear-edit-request',{detail:{...w,order:1}}));window.dispatchEvent(new CustomEvent('dango-wear-change-request',{detail:{...w,sizePercent:401}}));},wear);
 assert.equal(await page.evaluate(()=>DangoMiniPaint.getEditSequence()),revision);assert.equal(await page.evaluate(()=>wearChanges.length),0);assert.deepEqual(await page.evaluate(()=>DangoMiniPaint.getWearParameters()),wear);
});
test('legal saved fractional parameters remain editable, exportable and can return to edit mode',async()=>{
 const fractional={...wear,sizePercent:100.5,connectionStrength:37.5,maxConnectionTravelPercent:65.25,baseAngleDegrees:-12.75};
 await page.evaluate(w=>window.dispatchEvent(new CustomEvent('dango-wear-state',{detail:w})),fractional);
 await page.locator('#dynamic_preview_tab').click();
 assert.equal(await page.locator('#wear_sizePercent').evaluate(n=>n.checkValidity()),true);
 await page.evaluate(()=>DangoMiniPaint.exportSnapshot());
 await page.locator('#wear_gravityInfluence').fill('12.5');await page.locator('#wear_gravityInfluence').press('Enter');
 await page.waitForFunction(()=>DangoMiniPaint.getWearParameters().gravityInfluence===12.5);
 await page.locator('#palette_tab').click();assert.equal(await page.locator('.middle_area').isVisible(),true);
 assert.equal(await page.evaluate(()=>DangoMiniPaint.getWearParameters().sizePercent),100.5);
});

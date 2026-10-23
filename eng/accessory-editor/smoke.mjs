// Background Chromium checks only; does not establish WPF or human acceptance.
import path from 'node:path';
import assert from 'node:assert/strict';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {chromium} from 'playwright-core';
const here=path.dirname(fileURLToPath(import.meta.url));
const browser=await chromium.launch({channel:'msedge',headless:true});
try {
  const page=await browser.newPage({viewport:{width:1200,height:800}});
  const errors=[],requests=[];
  page.on('pageerror',error=>errors.push(error.message));
  page.on('request',request=>requests.push(request.url()));
  await page.goto(pathToFileURL(path.resolve(here,'../../src/Dango.Desktop/Accessories/Editing/Web/index.html')).href);
  await page.waitForFunction(()=>window.DangoMiniPaint,{timeout:15000});
  const result=await page.evaluate(async()=>{
    const api=window.DangoMiniPaint,c=document.createElement('canvas');c.width=32;c.height=32;
    const ctx=c.getContext('2d');ctx.fillStyle='#ab3412';ctx.fillRect(0,0,16,32);
    const initial=c.toDataURL().split(',')[1];
    await api.load({width:32,height:32,pngBase64:initial});
    const first=api.exportPng(),project=api.exportProject();
    await api.load(project);const second=api.exportPng();
    let rejected=0;
    for(const invalid of [{width:5000,height:1,pngBase64:initial},{width:32,height:32,pngBase64:'https://example.invalid/image.png'},{...project,layers:[{...project.layers[0],type:'media'}]}]){
      try{await api.load(invalid);}catch{rejected++;}
    }
    let shapeRejected=0;
    const invalidLayers=[
      {width:0},{height:-1},{width:'32'},{width:null},{opacity:undefined},{opacity:'100'},
      {type:'brush',assetId:undefined,is_vector:true,data:null,render_function:['brush','render'],params:{size:4,pressure:false}},
      {type:'brush',assetId:undefined,is_vector:true,data:[[[1,2,3]]],render_function:['brush','render','extra'],params:{size:4,pressure:false}},
      {type:'text',is_vector:true,data:[],render_function:['text','render'],params:{}}
    ];
    for(const patch of invalidLayers){const invalid={...project,layers:[{...project.layers[0],...patch}]};try{await api.load(invalid);}catch{shapeRejected++;}if(api.exportPng().pngBase64!==second.pngBase64)throw Error('Rejected shape changed prior image');}
    return {equal:first.pngBase64===second.pngBase64,unchanged:api.exportPng().pngBase64===second.pngBase64,rejected,shapeRejected,layers:project.layers.length,assets:project.assets.length,png:first.pngBase64.startsWith('iVBORw0KGgo')};
  });
  assert.deepEqual(result,{equal:true,unchanged:true,rejected:3,shapeRejected:9,layers:1,assets:1,png:true});
  await page.locator('#brush').click();
  const box=await page.locator('#canvas_minipaint').boundingBox();
  await page.mouse.move(box.x+2,box.y+3);await page.mouse.down();await page.mouse.move(box.x+25,box.y+25,{steps:8});await page.mouse.up();
  const drawn=await page.evaluate(()=>{const p=window.DangoMiniPaint.exportProject();return {types:p.layers.map(l=>l.type),count:p.layers.length};});
  assert.ok(drawn.types.includes('brush'),'Brush must create editable stroke data');
  await page.evaluate(async()=>{const api=window.DangoMiniPaint,p=api.exportProject();const broken={...p,layers:[{...p.layers[1],data:null}]};try{await api.load(broken);throw Error('Malformed brush accepted');}catch(e){if(e.message==='Malformed brush accepted')throw e;}});
  await page.locator('#undo_button').click();
  await page.waitForFunction(()=>!window.DangoMiniPaint.exportProject().layers.some(l=>l.type==='brush'));
  await page.locator('#redo_button').click();
  await page.waitForFunction(()=>window.DangoMiniPaint.exportProject().layers.some(l=>l.type==='brush'));
  await page.evaluate(async()=>{const p=window.DangoMiniPaint.exportProject();await window.DangoMiniPaint.load(p);window.DangoMiniPaint.exportPng();});
  const capBefore=await page.evaluate(async()=>{const api=window.DangoMiniPaint,p=api.exportProject(),brush=p.layers.find(l=>l.type==='brush');await api.load({...p,assets:[],layers:Array.from({length:16},(_,i)=>({...brush,id:i+1,order:i+1,name:'笔画 '+i}))});return JSON.stringify(api.exportProject());});
  await page.mouse.move(box.x+3,box.y+3);await page.mouse.down();await page.mouse.move(box.x+22,box.y+20,{steps:6});await page.mouse.up();
  assert.equal(await page.evaluate(()=>JSON.stringify(window.DangoMiniPaint.exportProject())),capBefore,'Rejected layer-cap stroke must preserve all project data');
  await page.locator('#undo_button').click();
  assert.equal(await page.evaluate(()=>JSON.stringify(window.DangoMiniPaint.exportProject())),capBefore,'Rejected stroke must not create undo history');
  assert.deepEqual(errors,[]);
  assert.ok(requests.every(url=>url.startsWith('file:')||url.startsWith('data:')),'Unexpected network resource request');
  console.log(JSON.stringify({browser:await browser.version(),api:result,brush:drawn,requests:requests.length,pageErrors:errors.length}));
} finally {await browser.close();}

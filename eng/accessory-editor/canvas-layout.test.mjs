import test,{before,after,beforeEach} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';import path from 'node:path';import http from 'node:http';import {fileURLToPath} from 'node:url';
import {chromium} from 'playwright-core';

const here=path.dirname(fileURLToPath(import.meta.url)),root=path.resolve(here,'../../src/Dango.Desktop/Accessories/Editing/Web');
let server,browser,page;
before(async()=>{
 server=http.createServer((req,res)=>{const file=path.join(root,req.url==='/'?'index.html':decodeURIComponent(req.url).replace(/^\/+/,''));res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html');try{res.end(fs.readFileSync(file));}catch{res.writeHead(404);res.end();}});await new Promise(r=>server.listen(0,'127.0.0.1',r));
 browser=await chromium.launch({channel:'msedge',headless:true});page=await browser.newPage({viewport:{width:1280,height:850}});
});
after(async()=>{await browser?.close();if(server)await new Promise(r=>server.close(r));});
beforeEach(async()=>{
 await page.setViewportSize({width:1280,height:850});
 await page.goto(`http://127.0.0.1:${server.address().port}/`);await page.waitForFunction(()=>!!window.DangoMiniPaint,{},{timeout:10000});
 await page.evaluate(async()=>{const c=document.createElement('canvas');c.width=400;c.height=300;c.getContext('2d').fillStyle='#ffffff';c.getContext('2d').fillRect(0,0,400,300);await DangoMiniPaint.load({width:400,height:300,pngBase64:c.toDataURL().split(',')[1]});});
});

test('canvas remains the main work surface while the short palette and two independent panels collapse without changing view',async()=>{
 const initial=await page.evaluate(()=>{const rect=s=>{const r=document.querySelector(s).getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height};};return {canvas:rect('#canvas_minipaint'),tools:rect('#tools_container'),colors:rect('#colors_panel'),props:rect('#properties_panel'),zoom:document.querySelector('#zoom_100').textContent};});
 assert.ok(initial.tools.w<=40);assert.ok(initial.canvas.w>initial.tools.w*8);assert.ok(initial.colors.w<initial.props.w+20);assert.ok(initial.colors.h<initial.props.h);
 const coverPoint={x:Math.max(initial.canvas.x,initial.props.x)+2,y:Math.max(initial.canvas.y,initial.props.y)+Math.min(50,initial.props.h/2)};
 const beforeTarget=await page.evaluate(p=>document.elementFromPoint(p.x,p.y)?.closest('#properties_panel')?.id||document.elementFromPoint(p.x,p.y)?.id,coverPoint);assert.equal(beforeTarget,'properties_panel','expanded inspector must actually cover a point of the work surface');
 const viewBefore=await page.evaluate(()=>({zoom:document.querySelector('#zoom_100').textContent,scroll:[scrollX,scrollY],canvas:[document.querySelector('#canvas_minipaint').width,document.querySelector('#canvas_minipaint').height]}));
 await page.locator('[data-panel="colors_panel"]').click();assert.equal(await page.locator('#colors_panel').getAttribute('data-expanded'),'false');assert.equal(await page.locator('#properties_panel').getAttribute('data-expanded'),'true');
 await page.locator('[data-panel="properties_panel"]').click();assert.equal(await page.locator('#properties_panel').getAttribute('data-expanded'),'false');
 const viewAfter=await page.evaluate(()=>({zoom:document.querySelector('#zoom_100').textContent,scroll:[scrollX,scrollY],canvas:[document.querySelector('#canvas_minipaint').width,document.querySelector('#canvas_minipaint').height]}));assert.deepEqual(viewAfter,viewBefore);
 await page.waitForFunction(p=>!document.elementFromPoint(p.x,p.y)?.closest('#properties_panel'),coverPoint);
 const afterTarget=await page.evaluate(p=>{const e=document.elementFromPoint(p.x,p.y);return e?.closest('#canvas_minipaint,#main_wrapper,#canvas_wrapper')?.id||e?.id;},coverPoint);assert.ok(['canvas_minipaint','main_wrapper','canvas_wrapper'].includes(afterTarget),`collapsed inspector should expose the work surface at its old overlap point (got ${afterTarget})`);
});

test('tool rail uses compact icons and hover/focus names restore the active usage hint',async()=>{
 const tool=page.locator('#brush');assert.match(await tool.getAttribute('aria-label'),/画笔(?:\s|$)/);assert.equal(await tool.getAttribute('data-icon-only'),'true');
 await tool.click();await page.waitForFunction(()=>/画笔|绘制/.test(document.querySelector('#geometry_status').textContent));assert.match(await page.locator('#geometry_status').textContent(),/画笔|绘制/);
 await page.locator('#fill').hover();assert.match(await page.locator('#geometry_status').textContent(),/填充/);
 await page.locator('#canvas_minipaint').hover();assert.match(await page.locator('#geometry_status').textContent(),/画笔|绘制/);
 await page.locator('#fill').focus();assert.match(await page.locator('#geometry_status').textContent(),/填充/);
 assert.match(await page.locator('#fill').getAttribute('aria-label'),/填充/);await page.keyboard.press('Tab');assert.ok(await page.evaluate(()=>document.activeElement?.getAttribute('aria-label')||document.activeElement?.title));
});

test('palette selection changes real brush and fill pixels in exported PNG',async()=>{
 const paletteToggle=page.locator('[data-panel="colors_panel"]');if(await page.locator('#colors_panel').getAttribute('data-expanded')!=='true')await paletteToggle.click();
 await page.locator('#toggle_color_picker_section_button').click();await page.locator('#color_hex').waitFor({state:'visible'});
 const hex=page.locator('#color_hex');await hex.fill('#e12b54');await hex.press('Tab');await page.locator('#brush').click();
 const brushStart=await at({x:95,y:90}),brushEnd=await at({x:105,y:90});await page.mouse.move(brushStart.x,brushStart.y);await page.mouse.down();await page.mouse.move(brushEnd.x,brushEnd.y,{steps:3});await page.mouse.up();
 const brushPixel=await page.evaluate(async()=>{const png=await DangoMiniPaint.exportPng(),img=new Image();img.src='data:image/png;base64,'+png.pngBase64;await img.decode();const c=document.createElement('canvas');c.width=img.width;c.height=img.height;const x=c.getContext('2d');x.drawImage(img,0,0);return [...x.getImageData(100,90,1,1).data];});
 const brushState=await page.evaluate(()=>DangoMiniPaint.exportProject().layers.map(l=>({type:l.type,color:l.color,points:l.data?.length||0})));
 assert.deepEqual(brushPixel.slice(0,3),[225,43,84],`selected palette color must reach real brush output before fill (layers=${JSON.stringify(brushState)})`);
 const fillPoint=await at({x:220,y:180});await page.locator('#fill').click();await page.mouse.click(fillPoint.x,fillPoint.y);
 await page.waitForFunction(async()=>{const png=await DangoMiniPaint.exportPng(),img=new Image();img.src='data:image/png;base64,'+png.pngBase64;await img.decode();const c=document.createElement('canvas');c.width=img.width;c.height=img.height;const x=c.getContext('2d');x.drawImage(img,0,0);return [...x.getImageData(220,180,1,1).data].slice(0,3).join(',')==='225,43,84';},{},{timeout:5000});
});

test('palette header keeps color, hex and all three modes together while the picker fills the available body',async()=>{
 for(const viewport of [{width:1280,height:850},{width:784,height:548}]){
  await page.setViewportSize(viewport);
  await page.locator('#toggle_color_picker_section_button').click();
  await page.waitForFunction(()=>{const p=document.querySelector('#palette_page>.colors'),g=document.querySelector('#color_section_picker .ui_color_picker_gradient');return g&&Math.abs(g.getBoundingClientRect().height-p.clientHeight)<2;});
  const result=await page.evaluate(()=>{const panel=document.querySelector('#colors_panel'),header=panel.querySelector('.palette-controls'),r=header.getBoundingClientRect(),picker=document.querySelector('.ui_color_picker_gradient').getBoundingClientRect(),body=panel.querySelector('#palette_page>.colors').getBoundingClientRect(),ids=['selected_color_sample','color_hex','toggle_color_picker_section_button','toggle_color_channels_section_button','toggle_color_swatches_section_button'];return {title:header.querySelector('.panel-title')?.textContent,label:document.querySelector('#color_hex_label')?.textContent,controls:ids.map(id=>{const n=document.getElementById(id),q=n.getBoundingClientRect();return {id,inHeader:header.contains(n),visible:n.checkVisibility(),inside:q.left>=r.left&&q.right<=r.right&&q.top>=r.top&&q.bottom<=r.bottom};}),picker:{height:picker.height,bottom:picker.bottom},body:{height:body.height,bottom:body.bottom}};});
  assert.equal(result.title,undefined);assert.equal(result.label,undefined);assert.ok(result.controls.every(c=>c.inHeader&&c.visible&&c.inside),JSON.stringify(result));assert.ok(result.picker.height>=96&&result.picker.bottom<=result.body.bottom,'freed vertical space belongs to the actual color picker');
  await page.locator('#color_hex').fill('#35a9c1');await page.locator('#color_hex').press('Tab');
  await page.locator('#toggle_color_channels_section_button').click();assert.equal(await page.locator('#color_hex').isVisible(),true);assert.equal(await page.locator('#rgb_r input').inputValue(),'53');assert.equal(await page.locator('#rgb_g input').inputValue(),'169');
  await page.locator('#toggle_color_swatches_section_button').click();assert.equal(await page.locator('#color_hex').inputValue(),'#35a9c1');
 }
});

async function at(p){return page.evaluate(p=>{const q=new DOMPoint(p.x,p.y).matrixTransform(document.querySelector('#geometry_overlay').getScreenCTM());return {x:q.x,y:q.y};},p);}

test('narrow tool groups wrap as whole groups and keep every control inside the viewport',async()=>{
 await page.setViewportSize({width:784,height:548});await page.waitForFunction(()=>document.querySelector('.unified-editor')?.dataset.short==='true');
 const result=await page.evaluate(()=>{const nav=document.querySelector('#main_menu'),groups=[...nav.querySelectorAll(':scope > .toolbar-group')];return {nav:nav.getBoundingClientRect().toJSON(),groups:groups.map(g=>({top:g.getBoundingClientRect().top,bottom:g.getBoundingClientRect().bottom,left:g.getBoundingClientRect().left,right:g.getBoundingClientRect().right,scroll:g.scrollWidth,client:g.clientWidth,children:[...g.querySelectorAll('button,input,summary')].map(c=>{const r=c.getBoundingClientRect();return {right:r.right,bottom:r.bottom,visible:c.checkVisibility()};})}))};});
 assert.ok(result.groups.some(g=>g.top>result.groups[0].top),'toolbar groups should wrap onto additional rows');for(const g of result.groups)for(const c of g.children)if(c.visible){assert.ok(c.right<=result.nav.right+1);assert.ok(c.bottom<=result.nav.bottom+1);if(g.top<38)assert.ok(c.right<=result.nav.right-132,'tools must not occupy the native window-control cutout');}assert.ok(result.groups.every(g=>g.scroll<=g.client+1));
 for(const id of ['toggle_color_picker_section_button','toggle_color_channels_section_button','toggle_color_swatches_section_button']){
  await page.locator('#'+id).click();
  const bounds=await page.evaluate(()=>{const p=document.querySelector('#colors_panel .panel-body'),r=p.getBoundingClientRect();return {scroll:p.scrollHeight,client:p.clientHeight,clipped:[...p.querySelectorAll('input,button,[tabindex]')].filter(n=>n.checkVisibility()&&(n.getBoundingClientRect().bottom>r.bottom+1||n.getBoundingClientRect().right>r.right+1)).map(n=>n.id)};});
  assert.ok(bounds.scroll<=bounds.client+1,JSON.stringify(bounds));assert.deepEqual(bounds.clipped,[]);
 }
});

test('sixteen layer identities remain reachable through list scrolling',async()=>{
 await page.setViewportSize({width:784,height:548});await page.waitForFunction(()=>document.querySelector('.unified-editor')?.dataset.short==='true');
 await page.evaluate(async()=>{const p=DangoMiniPaint.exportProject(),asset=p.assets[0],template=p.layers[0];p.layers=[];p.skeleton.layers=[];p.skeleton.groups=[];p.renderPieces=[{groupId:null,assetId:asset.id}];for(let i=1;i<=16;i++){p.layers.push({...template,id:i,name:`图层 ${i}`,order:i,assetId:asset.id});p.skeleton.layers.push({id:String(i),groupId:null});}const parameters={bend:.5,damping:5,restoration:5,gravity:0,elasticity:0};for(let i=1;i<=12;i++)p.skeleton.groups.push({id:`G${i}`,name:`组 ${i}`,targetId:null,nodes:[{id:`G${i}R`,x:20+i,y:20}],parameters:{...parameters}});await DangoMiniPaint.load(p);});
 await page.locator('#layers_tab').click();assert.equal(await page.locator('#layers_previous,#layers_next').count(),0);
 const names=[];for(let id=1;id<=16;id++){const row=page.locator(`[data-image-layer="${id}"]`);await row.scrollIntoViewIfNeeded();names.push((await row.locator('.layer_name').textContent()).trim());}
 assert.equal(new Set(names).size,16);assert.ok(names.some(n=>n.includes('图层 1'))&&names.some(n=>n.includes('图层 16')));
 await page.locator('#skeleton_tab').click();assert.equal(await page.locator('#bone_group_picker option').count(),12);
});

test('right-hand numeric parameter group stays editable and one undo restores its prior value',async()=>{
 await page.setViewportSize({width:784,height:548});await page.waitForFunction(()=>document.querySelector('.unified-editor')?.dataset.short==='true');
 await page.locator('#new_group').click();await page.locator('[data-group-id]').first().click();await page.locator('#skeleton_tab').click();
 await page.locator('#properties_panel #group_fields').waitFor({state:'visible'});
 await page.locator('[data-skeleton-param="bend"][type="number"]').fill('0.73');await page.locator('[data-skeleton-param="bend"][type="number"]').press('Tab');
 const moved=await page.locator('#properties_panel #group_fields').count();assert.equal(moved,1);
 assert.equal((await page.evaluate(()=>DangoMiniPaint.getSkeleton().groups[0].parameters.bend)),.73);
 await page.locator('#undo_button').click();assert.notEqual((await page.evaluate(()=>DangoMiniPaint.getSkeleton().groups[0].parameters.bend)),.73);
 // A real rejected group command must retain its reason without covering tool help.
 const first=await page.evaluate(async()=>{const s=DangoMiniPaint.getSkeleton(),g=s.groups[0];s.groups.push({id:'dependent',name:'连接组',targetId:g.nodes[0].id,nodes:[{id:'dependent-R',x:g.nodes[0].x,y:g.nodes[0].y}],parameters:{...g.parameters}});await DangoMiniPaint.setSkeleton(s);return g.id;});
 await page.locator('#bone_group_picker').selectOption(first);await page.locator('#layers_tab').click();await page.locator(`[data-delete-group="${first}"]`).click();
 assert.ok((await page.locator('#editor_status').textContent()).trim());await page.locator('#fill').hover();assert.match(await page.locator('#geometry_status').textContent(),/填充/);
 const feedback=await page.locator('#editor_status').boundingBox(),help=await page.locator('#geometry_status').boundingBox();assert.ok(feedback.y+feedback.height<=help.y+.5);
});

test('left rail owns image adjustments, restoration and canvas tools at both supported sizes',async()=>{
 await page.addStyleTag({content:fs.readFileSync(path.join(here,'canvas-layout.css'),'utf8')});
 for(const viewport of [{width:1280,height:850},{width:784,height:548}]){
  await page.setViewportSize(viewport);await page.waitForFunction(()=>document.querySelector('.unified-editor').dataset.short===String(innerHeight-26<600));
  const rail=await page.evaluate(()=>{const left=document.querySelector('.sidebar_left'),restore=document.querySelector('#restore_pixels'),view=document.querySelector('#view_tools');return {image:left.contains(document.querySelector('.image-options')),afterErase:document.querySelector('#erase').nextElementSibling===restore,view:['image_pan','image_zoom','canvas_expand'].every(id=>view.contains(document.getElementById(id))),bounds:[...left.querySelectorAll('button,summary,.item')].filter(n=>n.checkVisibility()).map(n=>{const r=n.getBoundingClientRect();return {id:n.id,bottom:r.bottom,right:r.right};})};});
  assert.ok(rail.image&&rail.afterErase&&rail.view,JSON.stringify(rail));assert.ok(rail.bounds.every(r=>r.bottom<=viewport.height-26&&r.right<=viewport.width),JSON.stringify(rail));
  await page.keyboard.press('Shift+C');assert.equal(await page.locator('.image-options').getAttribute('open'),'');
  const imageButtons=await page.locator('.image-options>div>button').evaluateAll(nodes=>nodes.map(n=>n.getBoundingClientRect().toJSON()));assert.equal(imageButtons.length,3);assert.ok(imageButtons.every(r=>Math.abs(r.top-imageButtons[0].top)<1),'all three image operations share one row');
  const popover=await page.locator('.image-options>div').boundingBox(),entry=await page.locator('.image-options>summary').boundingBox(),leftRail=await page.locator('.sidebar_left').boundingBox();
  assert.ok(popover.y>=0&&popover.y+popover.height<=viewport.height-26);
  assert.ok(Math.abs(popover.x-(leftRail.x+leftRail.width))<1,`image adjustment popover sits flush beside the rail without covering its tools: ${JSON.stringify({popover,entry,leftRail})}`);
  assert.ok(Math.abs(popover.y-entry.y)<1,`image adjustment popover aligns with its entry: ${JSON.stringify({popover,entry,leftRail})}`);
  assert.ok(Math.abs(popover.width-(imageButtons.at(-1).right-imageButtons[0].left+22))<1,'popover width follows its three operations without an empty column');
  assert.equal(await page.locator('.image-options #canvas_expand').count(),0);
  await page.keyboard.press('Escape');await page.locator('#canvas_expand').click();assert.equal(await page.locator('#canvas_expand_dialog').evaluate(n=>n.open),true);await page.locator('#canvas_expand_dialog [data-cancel]').click();
  await page.locator('#restore_pixels').click();assert.equal(await page.locator('#restore_pixels').getAttribute('aria-pressed'),'true');
  assert.equal(await page.locator('#restore_size').isVisible(),true);assert.equal(await page.locator('#action_attributes').isVisible(),false);
  await page.locator('#restore_size').fill('12');await page.keyboard.press('Tab');await page.keyboard.press('Escape');await page.locator('#brush').click();
  await page.keyboard.press('Shift+E');assert.equal(await page.locator('#restore_pixels').getAttribute('aria-pressed'),'true');assert.equal(await page.locator('.image-options').getAttribute('open'),null);
  assert.equal(await page.locator('#restore_size').inputValue(),'12');
  await page.keyboard.press('Escape');await page.locator('#image_pan').click();assert.equal(await page.locator('#restore_pixels').getAttribute('aria-pressed'),'false');await page.locator('#erase').click();assert.equal(await page.locator('#restore_size').isVisible(),false);assert.equal(await page.locator('#action_attributes').isVisible(),true);await page.keyboard.press('Escape');
 }
});

test('restore has one selected tool and uses the same rail surface color',async()=>{
 await page.locator('#brush').click();
 const base=await page.locator('#erase').evaluate(n=>getComputedStyle(n).backgroundColor);assert.equal(await page.locator('#restore_pixels').evaluate(n=>getComputedStyle(n).backgroundColor),base);
 await page.keyboard.press('Shift+E');await page.waitForFunction(()=>document.querySelector('#restore_pixels').getAttribute('aria-pressed')==='true');
 assert.equal(await page.locator('#tools_container .item[aria-pressed=true]').count(),0);assert.equal(await page.locator('#restore_size').isVisible(),true);assert.equal(await page.locator('#tool_options>summary').count(),0);
 await page.locator('#restore_pixels').click();await page.waitForFunction(()=>document.querySelector('#brush').getAttribute('aria-pressed')==='true');assert.equal(await page.locator('#restore_pixels').getAttribute('aria-pressed'),'false');assert.equal(await page.locator('#restore_size').isVisible(),false);
 await page.locator('#text').click();const p=await at({x:110,y:110});await page.mouse.click(p.x,p.y);await page.waitForFunction(()=>document.activeElement?.id==='text_tool_keyboard_input');await page.locator('#text_tool_keyboard_input').fill('文字 E');await page.locator('#text_tool_keyboard_input').press('Shift+E');assert.equal(await page.locator('#restore_pixels').getAttribute('aria-pressed'),'false');await page.locator('#brush').click();
});

test('expanded top parameter groups remain aligned and editable for image tools in narrow windows',async()=>{
 for(const viewport of [{width:1280,height:850},{width:784,height:548}]){await page.setViewportSize(viewport);for(const tool of ['brush','erase','fill','text']){
  await page.locator('#'+tool).click();await page.waitForFunction(id=>document.querySelector('#'+id).getAttribute('aria-pressed')==='true',tool);
  const bounds=await page.locator('#tool_options').evaluate(n=>{const nav=document.querySelector('#main_menu').getBoundingClientRect();return {summary:n.querySelector(':scope > summary')!==null,items:[...n.querySelectorAll('#action_attributes>.item')].filter(n=>n.checkVisibility()).map(n=>{const r=n.getBoundingClientRect();return {left:r.left,right:r.right,top:r.top,bottom:r.bottom,navBottom:nav.bottom,navRight:nav.right-132};})};});assert.equal(bounds.summary,false);assert.ok(bounds.items.every(r=>r.left>=0&&r.right<=bounds.items[0].navRight+1&&r.bottom<=r.navBottom+1),JSON.stringify({viewport,tool,bounds}));
 }}
});

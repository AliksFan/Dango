import app from './vendor/src/js/app.js';
import config from './vendor/src/js/config.js';
import notify from './notify.js';
import {selectSimilarPixels,selectionBoundary,constrainPixels,eraseSelectedPixels} from './selection-mask.js';

// This is a pixel selection, separate from miniPaint's rectangular layer handles.
// Raster eraser/fill/restore respect it. Vector brush/text and layer transforms
// fail closed while selected; Escape always releases that constraint.
export function installMagicSelection({geometryApi,limits}){
 const {Layers,State,GUI}=app,tool=GUI.GUI_tools.tools_modules.magic_erase.object;
 let selection=null,deleting=false;
 const signature=l=>[l.x,l.y,l.width,l.height,l.rotate||0,!!l.flipX,!!l.flipY,config.WIDTH,config.HEIGHT].join('|');
 function clear(message){selection=null;document.getElementById('magic_erase').dataset.selectionPixels='0';config.need_render=true;if(message)notify.message(message);}
 function current(){if(!selection)return null;const l=config.layer;if(l!==selection.layer||l.link!==selection.image||signature(l)!==selection.signature){clear('图层或画布已变化，像素选区已取消。');return null;}return selection;}
 function canvasFor(image){const c=document.createElement('canvas');c.width=image.naturalWidth;c.height=image.naturalHeight;c.getContext('2d').drawImage(image,0,0);return c;}
 function constrain(canvas,s){if(canvas.width!==s.width||canvas.height!==s.height)throw Error('像素尺寸已变化，请先按 Esc 取消选区。');const result=canvasFor(s.layer.link),ctx=result.getContext('2d'),original=ctx.getImageData(0,0,s.width,s.height),candidate=canvas.getContext('2d').getImageData(0,0,s.width,s.height);constrainPixels(candidate.data,original.data,s.mask);ctx.putImageData(candidate,0,0);return result;}
 function setSelection(mouse){
  if(deleting)return;
  try{
   geometryApi.ensureSettled();const l=config.layer;
   if(l.type!=='image'||l.is_vector)throw Error('请先将当前图层光栅化，再建立像素选区。');
   const width=l.link.naturalWidth,height=l.link.naturalHeight;
   if(width*height>limits.maxPixels)throw Error('选区像素超出资源上限。');
   const a=-(l.rotate||0)*Math.PI/180,dx=mouse.x-l.x-l.width/2,dy=mouse.y-l.y-l.height/2;
   let x=dx*Math.cos(a)-dy*Math.sin(a),y=dx*Math.sin(a)+dy*Math.cos(a);if(l.flipX)x=-x;if(l.flipY)y=-y;
   x=(x+l.width/2)*width/l.width;y=(y+l.height/2)*height/l.height;
   if(x<0||y<0||x>=width||y>=height){notify.message('请点击当前图层范围内的像素。');return;}
   const params=tool.getParams(),source=canvasFor(l.link).getContext('2d').getImageData(0,0,width,height);
   const selected=selectSimilarPixels(source.data,width,height,x,y,params.power,params.contiguous);
   const overlay=document.createElement('canvas');overlay.width=width;overlay.height=height;const ctx=overlay.getContext('2d'),pixels=ctx.createImageData(width,height),boundary=selectionBoundary(selected.mask,width,height);
   for(let i=0;i<selected.mask.length;i++)if(selected.mask[i]){const k=i*4;if(boundary[i]){const shade=(Math.floor((i%width)/3)+Math.floor(i/width/3))%2?255:0;pixels.data.set([shade,shade,shade,255],k);}else pixels.data.set([155,100,45,35],k);}
   ctx.putImageData(pixels,0,0);selection={...selected,width,height,layer:l,image:l.link,signature:signature(l),overlay,seed:{x,y}};
   document.getElementById('magic_erase').dataset.selectionPixels=String(selected.count);config.need_render=true;
   notify.message(`已选 ${selected.count} 像素 · Delete 删除 · Esc 取消；橡皮、填充和恢复仅作用于选区。`);
  }catch(error){notify.error(error.message);}
 }
 // Override both entry points: the original mousedown rejected rotation and
 // magic_erase immediately changed the image. Neither path may erase on click.
 tool.mousedown=e=>{const mouse=tool.get_mouse_info(e);if(mouse.click_valid&&config.mouse_lock!==true)setSelection(mouse);};
 tool.magic_erase=setSelection;
 const params=config.TOOLS.find(t=>t.name==='magic_erase');params.attributes={power:15,contiguous:true};params.on_update='on_params_update';
 tool.on_params_update=()=>{const s=current();if(!s)return;const l=s.layer,a=(l.rotate||0)*Math.PI/180;let x=(s.seed.x/s.width-.5)*l.width,y=(s.seed.y/s.height-.5)*l.height;if(l.flipX)x=-x;if(l.flipY)y=-y;setSelection({x:l.x+l.width/2+x*Math.cos(a)-y*Math.sin(a),y:l.y+l.height/2+x*Math.sin(a)+y*Math.cos(a)});notify.message('已按当前图像和容差重新选取；Delete 删除 · Esc 取消。');};
 const overlay=Layers.render_overlay.bind(Layers);
 Layers.render_overlay=()=>{overlay();const s=current();if(!s)return;const l=s.layer,ctx=Layers.ctx;ctx.save();ctx.translate(l.x+l.width/2,l.y+l.height/2);ctx.rotate((l.rotate||0)*Math.PI/180);ctx.scale(l.flipX?-1:1,l.flipY?-1:1);ctx.imageSmoothingEnabled=false;ctx.drawImage(s.overlay,-l.width/2,-l.height/2,l.width,l.height);ctx.restore();};
 const renderObject=Layers.render_object.bind(Layers);
 Layers.render_object=(ctx,l,...args)=>{const s=current(),draft=l.link_canvas;if(s&&s.layer===l&&draft){l.link_canvas=constrain(draft,s);try{return renderObject(ctx,l,...args);}finally{l.link_canvas=draft;}}return renderObject(ctx,l,...args);};
 const update=app.Actions.Update_layer_image_action.prototype.do;
 app.Actions.Update_layer_image_action.prototype.do=async function(){const s=current();if(s&&this.layer_id===s.layer.id&&!this.after&&this.canvas&&!deleting)this.canvas=constrain(this.canvas,s);const result=await update.call(this);if(s&&selection===s&&this.layer_id===s.layer.id)s.image=s.layer.link;return result;};
 async function eraseSelection(){const s=current();if(!s||deleting)return;try{geometryApi.ensureSettled();deleting=true;const canvas=canvasFor(s.layer.link),ctx=canvas.getContext('2d'),pixels=ctx.getImageData(0,0,s.width,s.height);eraseSelectedPixels(pixels.data,s.mask);ctx.putImageData(pixels,0,0);const action=new app.Actions.Update_layer_image_action(canvas,s.layer.id);action.preserveRestoreBaseline=true;const result=await State.do_action(action);if(result?.status!=='aborted'){s.image=s.layer.link;notify.message('已删除选区像素，可撤销；Esc 取消选区。');}}catch(error){notify.error(error.message);}finally{deleting=false;}}
 document.addEventListener('keydown',e=>{if(e.isComposing||e.target.closest('input,textarea,select,[contenteditable=true],[role=dialog],[role=menu]'))return;if(!current())return;const mode=geometryApi.layout?.getMode();if(mode&&mode!=='pan'&&mode!=='zoom')return;if(e.key==='Escape'||e.key==='Delete'||e.key==='Backspace'){e.preventDefault();e.stopImmediatePropagation();if(e.key==='Escape')clear('已取消像素选区。');else eraseSelection();}else if(['select','crop'].includes(config.TOOL.name)&&['ArrowUp','ArrowDown','ArrowLeft','ArrowRight','Enter'].includes(e.key)){e.preventDefault();e.stopImmediatePropagation();notify.message('请先按 Esc 取消像素选区，再变换图层或裁剪。');}},true);
 // Block tools which create vectors or transform the selected layer. Switching
 // tools itself keeps the selection; it is never silently ignored by editing.
 const blocked=new Set(['brush','text','select','crop']);
 for(const name of ['pointerdown','mousedown','touchstart'])document.addEventListener(name,e=>{if(!current()||!e.target.closest('#canvas_wrapper'))return;const view=geometryApi.layout?.getMode();if(view==='pan'||view==='zoom')return;if(blocked.has(config.TOOL.name)){e.preventDefault();e.stopImmediatePropagation();notify.message('此工具暂不支持像素选区；请按 Esc 取消选区后操作。');}},true);
 // The commit boundary also covers toolbar/Enter canvas operations whose
 // listeners may have been registered before the selection's pointer guard.
 const perform=State.do_action.bind(State);
 State.do_action=(action,...args)=>{if(current()&&String(action.action_id).startsWith('canvas_')){notify.error('请先按 Esc 取消像素选区，再调整整张画布。');return Promise.resolve({status:'aborted',reason:'Active pixel selection'});}return perform(action,...args);};
 window.addEventListener('dango-skeleton-changed',()=>current());
 const settled=geometryApi.ensureSettled;geometryApi.ensureSettled=(...args)=>{if(deleting)throw Error('请先结束选区操作。');return settled(...args);};
 return {clear,copyCanvas(){const s=current();if(!s)return null;const canvas=canvasFor(s.layer.link),ctx=canvas.getContext('2d'),pixels=ctx.getImageData(0,0,s.width,s.height);for(let i=0;i<s.mask.length;i++)if(!s.mask[i])pixels.data.fill(0,i*4,i*4+4);ctx.putImageData(pixels,0,0);return canvas;},inspect:()=>{const s=current();return s?{layerId:s.layer.id,width:s.width,height:s.height,count:s.count}:null;}};
}

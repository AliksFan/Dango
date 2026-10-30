// Controlled adaptation points. Upstream sources remain unchanged.
import config from './vendor/src/js/config.js';
import app from './vendor/src/js/app.js';
import {cloneGeometry,validateGeometry,transformGeometry} from './geometry.js';
import notify from './notify.js';
import zoomView from './vendor/src/js/libs/zoomView.js';
export function installGeometry({ensureReady,checkSize}) {
 let geometry={pivotX:.5,pivotY:.5,nodes:[],requiresRebind:false},mode='',selected=0,drag=null,pointerDown=false,pending=0;
 const State=app.State,Layers=app.Layers,GUI=app.GUI;
 const overlay=document.createElement('canvas');overlay.id='geometry_overlay';overlay.setAttribute('aria-label','根部与中心线画布');document.getElementById('canvas_wrapper').append(overlay);
 const status=document.getElementById('geometry_status');
 const emit=()=>{render();window.dispatchEvent(new CustomEvent('dango-geometry-changed',{detail:cloneGeometry(geometry)}));};
 const baseAction=(id,doFn,undoFn)=>({action_id:id,action_description:id,async do(){await doFn();},async undo(){await undoFn();},async free(){}});
 const getGeometry=()=>cloneGeometry(geometry);
 const setGeometry=async(g,options={})=>{ensureReady();const next=validateGeometry(g),before=getGeometry();next.requiresRebind=[{x:next.pivotX,y:next.pivotY},...next.nodes].some(p=>p.x<0||p.x>1||p.y<0||p.y>1);if(options.reset){geometry=next;drag=null;mode='';selected=0;emit();return;}const result=await State.do_action(baseAction('geometry',()=>{geometry=cloneGeometry(next);emit();},()=>{geometry=cloneGeometry(before);emit();}));if(result?.status==='aborted')throw Error('坐标操作未完成，请重试。');return result;};
 window.addEventListener('dango-editor-mode',e=>{if(e.detail!=='geometry'){mode='';render();}});
 const clear=()=>{window.dispatchEvent(new CustomEvent('dango-editor-mode',{detail:'idle'}));geometry={pivotX:.5,pivotY:.5,nodes:[],requiresRebind:false};drag=null;mode='';pointerDown=false;selected=0;emit();};
 const settled=(options={})=>{if((pointerDown&&!options.allowPointer)||drag||pending||Object.values(GUI.GUI_tools.tools_modules).some(t=>t.object.focused||t.object.strokePending||t.object.event_links?.length||t.object.tmpCanvas||t.object.is_mousedown_canvas&&config.mouse.is_drag)||document.querySelector('.text-input'))throw Error('请先结束画布操作，再试一次。');};
 document.addEventListener('pointerdown',e=>{if(e.target.closest('#main_wrapper'))pointerDown=true;},true);
 document.addEventListener('pointerup',()=>{pointerDown=false;},true);window.addEventListener('blur',()=>{pointerDown=false;});
 const original=State.do_action.bind(State);State.do_action=async(...args)=>{if(pending&&!args[0]?.action_id?.startsWith('canvas_')){notify.error('正在完成操作，请稍候。');return {status:'aborted'};}pending++;try{const result=await original(...args);emit();return result;}finally{pending--;}};
 for(const method of ['undo_action','redo_action']){const fn=State[method].bind(State);State[method]=async(...args)=>{if(pointerDown||pending||drag){notify.error('请先结束画布操作。');return;}pending++;try{await fn(...args);emit();}finally{pending--;}};}
 State.undo=()=>State.undo_action();State.redo=()=>State.redo_action();
 function render(){
  const source=document.getElementById('canvas_minipaint');overlay.width=source.width;overlay.height=source.height;overlay.style.width=source.style.width||source.width+'px';overlay.style.height=source.style.height||source.height+'px';overlay.style.pointerEvents=mode?'auto':'none';
  const ctx=overlay.getContext('2d'),w=overlay.width,h=overlay.height;ctx.lineWidth=2;ctx.strokeStyle='#885e3e';ctx.fillStyle='#fdfcf9';
  const pts=geometry.nodes.length?geometry.nodes:[{x:geometry.pivotX,y:geometry.pivotY}];ctx.beginPath();pts.forEach((p,i)=>i?ctx.lineTo(zoomView.toScreen({x:p.x*config.WIDTH,y:p.y*config.HEIGHT}).x,zoomView.toScreen({x:p.x*config.WIDTH,y:p.y*config.HEIGHT}).y):ctx.moveTo(zoomView.toScreen({x:p.x*config.WIDTH,y:p.y*config.HEIGHT}).x,zoomView.toScreen({x:p.x*config.WIDTH,y:p.y*config.HEIGHT}).y));ctx.stroke();
  pts.forEach((raw,i)=>{const q=zoomView.toScreen({x:raw.x*config.WIDTH,y:raw.y*config.HEIGHT}),p={x:q.x/w,y:q.y/h};ctx.beginPath();ctx.arc(p.x*w,p.y*h,i===0?7:5,0,Math.PI*2);ctx.fill();ctx.stroke();ctx.fillStyle='#443d34';ctx.fillText(i===0?'根':String(i),p.x*w+9,p.y*h-7);ctx.fillStyle='#fdfcf9';});
  status.textContent=geometry.requiresRebind?'坐标已超出画布，请重新定位或撤销。':mode==='root'?'点击或拖动根部':mode==='chain'?`中心线 ${geometry.nodes.length}/17 · 点击添点，拖动调整`:'根部与中心线随整图操作更新';
  document.getElementById('geometry_root').setAttribute('aria-pressed',String(mode==='root'));document.getElementById('geometry_chain').setAttribute('aria-pressed',String(mode==='chain'));
 }
 const paint=Layers.render.bind(Layers);Layers.render=(force)=>{const changed=config.need_render;const result=paint(force);if(force&&changed)render();return result;};
 new ResizeObserver(render).observe(document.getElementById('canvas_minipaint'));
 function point(e){const r=overlay.getBoundingClientRect();const q=zoomView.toWorld((e.clientX-r.left)*overlay.width/r.width,(e.clientY-r.top)*overlay.height/r.height);return {x:q.x/config.WIDTH,y:q.y/config.HEIGHT};}
 function repaired(g){g.requiresRebind=[{x:g.pivotX,y:g.pivotY},...g.nodes].some(p=>p.x<0||p.x>1||p.y<0||p.y>1);return g;}
 function move(g,index,p){if(index===0){g.pivotX=p.x;g.pivotY=p.y;if(g.nodes.length)g.nodes[0]={...p};}else g.nodes[index]={...p};return repaired(g);}
 overlay.addEventListener('pointerdown',e=>{e.preventDefault();e.stopPropagation();const p=point(e),before=getGeometry();let next=getGeometry();if(mode==='root'){selected=0;move(next,0,p);}else {const r=overlay.getBoundingClientRect();const hit=next.nodes.findIndex(n=>Math.hypot((n.x-p.x)*config.WIDTH*config.ZOOM,(n.y-p.y)*config.HEIGHT*config.ZOOM)<12);if(hit>=0)selected=hit;else {if(next.nodes.length===17){notify.error('最多 17 个控制点。');return;}if(!next.nodes.length)next.nodes.push({x:next.pivotX,y:next.pivotY});if(next.nodes.length===17)return;next.nodes.push(p);selected=next.nodes.length-1;}}drag={before};geometry=next;overlay.setPointerCapture(e.pointerId);render();});
 overlay.addEventListener('pointermove',e=>{if(!drag)return;e.preventDefault();geometry=move(getGeometry(),selected,point(e));render();});
 async function finish(e,cancel=false){if(!drag)return;e.preventDefault();const before=drag.before,next=getGeometry();drag=null;pointerDown=false;geometry=before;try{if(!cancel)await setGeometry(repaired(next));else emit();}catch(error){emit();notify.error(error.message);}}
 overlay.addEventListener('pointerup',e=>finish(e));overlay.addEventListener('pointercancel',e=>finish(e,true));
 document.getElementById('geometry_root').onclick=()=>{mode=mode==='root'?'':'root';window.dispatchEvent(new CustomEvent('dango-editor-mode',{detail:'geometry'}));render();};document.getElementById('geometry_chain').onclick=()=>{mode=mode==='chain'?'':'chain';window.dispatchEvent(new CustomEvent('dango-editor-mode',{detail:'geometry'}));render();};
 document.getElementById('geometry_delete').onclick=async()=>{try{settled();const next=getGeometry();if(!next.nodes.length)return;if(selected===0)throw Error('根部不能删除；可清空中心线。');next.nodes.splice(selected,1);selected=Math.max(0,selected-1);await setGeometry(repaired(next));}catch(e){notify.error(e.message);}};
 document.getElementById('geometry_clear').onclick=()=>setGeometry({...getGeometry(),nodes:[]}).catch(e=>notify.error(e.message));
 document.getElementById('tools_container').addEventListener('click',()=>{mode='';render();});
 // Text reflection keeps its editable spans. Reflection is in the layer's rotated local frame.
 const renderObject=Layers.render_object.bind(Layers);Layers.render_object=(ctx,layer,preview)=>{if(!layer.flipX&&!layer.flipY)return renderObject(ctx,layer,preview);const cx=layer.x+layer.width/2,cy=layer.y+layer.height/2,a=(layer.rotate||0)*Math.PI/180;ctx.save();ctx.translate(cx,cy);ctx.rotate(a);ctx.scale(layer.flipX?-1:1,layer.flipY?-1:1);ctx.rotate(-a);ctx.translate(-cx,-cy);try{return renderObject(ctx,layer,preview);}finally{ctx.restore();}};
 async function imageTransform(image,kind){const c=document.createElement('canvas'),w=image.naturalWidth,h=image.naturalHeight;c.width=kind==='left'?h:w;c.height=kind==='left'?w:h;const ctx=c.getContext('2d');if(kind==='flipX'){ctx.translate(w,0);ctx.scale(-1,1);}else if(kind==='flipY'){ctx.translate(0,h);ctx.scale(1,-1);}else {ctx.translate(0,w);ctx.rotate(-Math.PI/2);}ctx.drawImage(image,0,0);const result=new Image();result.src=c.toDataURL('image/png');await result.decode();return result;}
 async function transform(kind,crop){
  ensureReady();settled();pending++;try{const w=config.WIDTH,h=config.HEIGHT;if(kind==='crop'){if(!crop||![crop.x,crop.y,crop.width,crop.height].every(Number.isInteger)||crop.x<0||crop.y<0||crop.x+crop.width>w||crop.y+crop.height>h)throw Error('裁剪区域无效');checkSize(crop.width,crop.height);}
  const nw=kind==='left'?h:kind==='crop'?crop.width:w,nh=kind==='left'?w:kind==='crop'?crop.height:h;
  const before={layers:config.layers,width:w,height:h,geometry:getGeometry(),selected:config.layer.id};
  const after={...before,width:nw,height:nh,geometry:transformGeometry(geometry,kind,w,h,crop),layers:[]};
  for(const l of before.layers){const n={...l};if(l.type===null){after.layers.push(n);continue;}const cx=l.x+l.width/2,cy=l.y+l.height/2;let x,y;
   if(kind==='crop'){n.x-=crop.x;n.y-=crop.y;after.layers.push(n);continue;}
   if(kind==='flipX'){x=w-cx;y=cy;}else if(kind==='flipY'){x=cx;y=h-cy;}else{x=cy;y=w-cx;}
   if(l.type==='image'){n.link=await imageTransform(l.link,kind);if(l.link_restore)n.link_restore=await imageTransform(l.link_restore,kind);if(kind==='left'){n.width=l.height;n.height=l.width;n.width_original=l.height_original;n.height_original=l.width_original;}else n.rotate=-(l.rotate||0);}
   else if(l.type==='brush'){const bw=l.width,bh=l.height;n.data=l.data.map(group=>group.map(([px,py,s])=>kind==='flipX'?[bw-px,py,s]:kind==='flipY'?[px,bh-py,s]:[py,bw-px,s]));if(kind==='left'){n.width=bh;n.height=bw;}}
   else {if(kind==='left')n.rotate=((l.rotate||0)+270)%360;else{n.rotate=-(l.rotate||0);n[kind]=!l[kind];}}
   n.x=x-n.width/2;n.y=y-n.height/2;after.layers.push(n);
  }
  function apply(s){config.layers=s.layers;config.layer=s.layers.find(l=>l.id===s.selected)||s.layers[0];config.WIDTH=s.width;config.HEIGHT=s.height;geometry=cloneGeometry(s.geometry);GUI.prepare_canvas();GUI.GUI_layers.render_layers();config.need_render=true;emit();}
  const action=baseAction('canvas_'+kind,()=>apply(after),()=>apply(before));action.before=before;action.after=after;
  const result=await State.do_action(action);if(result?.status==='aborted')throw Error('整图操作失败，已保留当前工程。');if(geometry.requiresRebind)notify.error('裁剪移除了根部或控制点，请重新定位或撤销。');return getGeometry();}finally{pending--;}
 }
 for(const [id,kind]of [['flip_x','flipX'],['flip_y','flipY'],['rotate','left']])document.getElementById(id).onclick=()=>transform(kind).catch(e=>notify.error(e.message));
 const cropTool=GUI.GUI_tools.tools_modules.crop.object;cropTool.on_params_update=async()=>{try{const s=cropTool.selection;await transform('crop',{x:Math.round(s.x),y:Math.round(s.y),width:Math.round(s.width),height:Math.round(s.height)});cropTool.Base_selection.reset_selection();}catch(e){notify.error(e.message);}};
 render();return {getGeometry,setGeometry,resetGeometry:clear,transformCanvas:transform,ensureSettled:settled,track:async fn=>{pending++;try{return await fn();}finally{pending--;}}};
}

import app from './vendor/src/js/app.js';
import config from './vendor/src/js/config.js';
import zoomView from './vendor/src/js/libs/zoomView.js';
import notify from './notify.js';
export function installRestore({geometryApi,limits}){
 let active=false,stroke=null;
 const button=document.getElementById('restore_pixels'),State=app.State;
 const update=app.Actions.Update_layer_image_action.prototype.do;
 app.Actions.Update_layer_image_action.prototype.do=async function(){
  const layer=app.Layers.get_layer(this.layer_id);
  if(layer&&!layer.link_restore&&['erase','magic_erase'].includes(config.TOOL.name)){
   const pixels=config.layers.reduce((n,l)=>n+(l.link?.naturalWidth||0)*(l.link?.naturalHeight||0)+(l.link_restore?.naturalWidth||0)*(l.link_restore?.naturalHeight||0),0)+layer.link.naturalWidth*layer.link.naturalHeight;
   if(pixels>limits.maxLayerPixels)throw Error('恢复原图超出资源预算');
   // Keep exact un-erased pixels; no color synthesis or flattened document copy.
   layer.link_restore=layer.link;
  }
  return update.call(this);
 };
 window.addEventListener('dango-editor-mode',e=>{if(e.detail!=='restore'){active=false;button.setAttribute('aria-pressed','false');}});
 button.onclick=()=>{active=!active;if(active)window.dispatchEvent(new CustomEvent('dango-editor-mode',{detail:'restore'}));button.setAttribute('aria-pressed',String(active));notify.message(active?'恢复当前图层被擦除的像素；拖动画布涂抹。':'已退出恢复');};
 document.getElementById('tools_container').addEventListener('click',()=>{active=false;button.setAttribute('aria-pressed','false');});
 function position(e){const rect=document.getElementById('canvas_minipaint').getBoundingClientRect(),p=zoomView.toWorld(e.clientX-rect.left,e.clientY-rect.top),l=stroke.layer,a=-(l.rotate||0)*Math.PI/180,cx=l.x+l.width/2,cy=l.y+l.height/2,dx=p.x-cx,dy=p.y-cy;let x=dx*Math.cos(a)-dy*Math.sin(a),y=dx*Math.sin(a)+dy*Math.cos(a);if(l.flipX)x=-x;if(l.flipY)y=-y;return {x:(x+l.width/2)*l.link.naturalWidth/l.width,y:(y+l.height/2)*l.link.naturalHeight/l.height};}
 function dab(e){const p=position(e),ctx=stroke.canvas.getContext('2d'),l=stroke.layer,r=Number(document.getElementById('restore_size').value)/2*l.link.naturalWidth/l.width,previous=stroke.last||p;const distance=Math.hypot(p.x-previous.x,p.y-previous.y),steps=Math.max(1,Math.ceil(distance/Math.max(1,r/2))),pixels=ctx.getImageData(0,0,stroke.canvas.width,stroke.canvas.height),source=stroke.source;
  for(let i=0;i<=steps;i++){const cx=previous.x+(p.x-previous.x)*i/steps,cy=previous.y+(p.y-previous.y)*i/steps;for(let y=Math.max(0,Math.floor(cy-r));y<Math.min(stroke.canvas.height,Math.ceil(cy+r));y++)for(let x=Math.max(0,Math.floor(cx-r));x<Math.min(stroke.canvas.width,Math.ceil(cx+r));x++)if(Math.hypot(x+.5-cx,y+.5-cy)<=r){const index=(y*stroke.canvas.width+x)*4;pixels.data.set(source.subarray(index,index+4),index);}}
  ctx.putImageData(pixels,0,0);stroke.last=p;l.link_canvas=stroke.canvas;config.need_render=true;
 }
 document.addEventListener('pointerdown',e=>{if(!active||!e.target.closest('#canvas_wrapper'))return;e.preventDefault();e.stopImmediatePropagation();try{geometryApi.ensureSettled({allowPointer:true});const size=Number(document.getElementById('restore_size').value);if(!Number.isFinite(size)||size<1||size>300)throw Error('恢复笔径须为 1–300。');const l=config.layer;if(l.type!=='image'||!l.link_restore)throw Error('当前图层没有可恢复的擦除像素。');const c=document.createElement('canvas');c.width=l.link.naturalWidth;c.height=l.link.naturalHeight;c.getContext('2d').drawImage(l.link,0,0);const original=document.createElement("canvas");original.width=c.width;original.height=c.height;original.getContext("2d").drawImage(l.link_restore,0,0);stroke={layer:l,canvas:c,source:original.getContext("2d").getImageData(0,0,c.width,c.height).data};document.getElementById('canvas_minipaint').setPointerCapture(e.pointerId);dab(e);}catch(error){notify.error(error.message);}},true);
 document.addEventListener('pointermove',e=>{if(!stroke)return;e.preventDefault();e.stopImmediatePropagation();dab(e);},true);
 document.addEventListener('pointerup',async e=>{if(!stroke)return;e.preventDefault();e.stopImmediatePropagation();const current=stroke;stroke=null;delete current.layer.link_canvas;await State.do_action(new app.Actions.Update_layer_image_action(current.canvas,current.layer.id));},true);
 document.addEventListener('pointercancel',()=>{if(stroke){delete stroke.layer.link_canvas;stroke=null;config.need_render=true;}},true);
 // Stop compatibility mouse events from reaching miniPaint's ordinary tools.
 for(const name of ['mousedown','mousemove','mouseup'])document.addEventListener(name,e=>{if(active&&e.target.closest('#canvas_wrapper')){e.preventDefault();e.stopImmediatePropagation();}},true);
 const settled=geometryApi.ensureSettled;geometryApi.ensureSettled=(options)=>{if(stroke)throw Error('请先结束恢复笔画。');settled(options);};
}

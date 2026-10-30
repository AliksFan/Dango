import zoomView from './vendor/src/js/libs/zoomView.js';

// Keep the existing zoomView as the sole world/screen transform owner. Canvas
// pixels and the SVG use its coordinates; the checker is only a document backdrop.
export function installCanvasView({GUI,Layers,config,onRender,canZoom=()=>true}){
 const canvas=document.getElementById('canvas_minipaint'),wrapper=document.getElementById('canvas_wrapper'),host=document.getElementById('main_wrapper'),background=document.getElementById('canvas_minipaint_background'),preview=GUI.GUI_preview;
 function sync(){
  const p=zoomView.toScreen({x:0,y:0}),scale=zoomView.getScale();
  Object.assign(background.style,{left:p.x+'px',top:p.y+'px',width:config.WIDTH*scale+'px',height:config.HEIGHT*scale+'px'});
  background.style.setProperty('background-size',20*scale+'px '+20*scale+'px','important');
  document.getElementById('zoom_100').textContent=Math.round(scale*100)+'%';document.getElementById('zoom_range').value=scale*100;
 }
 const renderBackground=GUI.render_canvas_background.bind(GUI);
 GUI.render_canvas_background=(id,gap)=>{const result=renderBackground(id,gap);if(id==='canvas_minipaint')sync();return result;};
 GUI.prepare_canvas=()=>{
  const w=Math.max(1,host.clientWidth),h=Math.max(1,host.clientHeight),oldWidth=canvas.width,oldHeight=canvas.height;
  if(w!==oldWidth||h!==oldHeight){canvas.width=w;canvas.height=h;zoomView.move((w-oldWidth)/2,(h-oldHeight)/2);}
  config.visible_width=w;config.visible_height=h;wrapper.style.width=w+'px';wrapper.style.height=h+'px';
  canvas.getContext('2d').imageSmoothingEnabled=zoomView.getScale()<1;
  GUI.render_canvas_background('canvas_minipaint');GUI.check_canvas_offset();config.need_render=true;
 };
 const init=Layers.init_zoom_lib.bind(Layers);
 Layers.init_zoom_lib=()=>{init();zoomView.reset(config.ZOOM,(canvas.width-config.WIDTH*config.ZOOM)/2,(canvas.height-config.HEIGHT*config.ZOOM)/2);sync();};
 const afterRender=Layers.after_render.bind(Layers);
 Layers.pre_render=()=>{
  const ctx=Layers.ctx,p=zoomView.toScreen({x:0,y:0}),scale=zoomView.getScale();ctx.save();zoomView.canvasDefault();ctx.clearRect(0,0,canvas.width,canvas.height);
  // Enlarging the viewport must not reveal layer pixels outside the document.
  ctx.beginPath();ctx.rect(p.x,p.y,config.WIDTH*scale,config.HEIGHT*scale);ctx.clip();
 };
 Layers.after_render=()=>{afterRender();Layers.ctx.restore();zoomView.canvasDefault();sync();onRender();};
 function redraw(){config.ZOOM=zoomView.getScale();Layers.last_zoom=config.ZOOM;canvas.getContext('2d').imageSmoothingEnabled=config.ZOOM<1;config.need_render=true;}
 function zoomAt(clientX,clientY,factor){const r=canvas.getBoundingClientRect();zoomView.scaleAt((clientX-r.left)*canvas.width/r.width,(clientY-r.top)*canvas.height/r.height,factor);redraw();}
 preview.zoom=async value=>{
  const current=zoomView.getScale(),next=value===1?current*1.2:value===-1?current/1.2:Number(value)/100;
  if(!Number.isFinite(next)||next<=0)return false;
  zoomView.scaleAt(canvas.width/2,canvas.height/2,next/current);redraw();return true;
 };
 host.addEventListener('wheel',e=>{e.preventDefault();e.stopImmediatePropagation();if(canZoom())zoomAt(e.clientX,e.clientY,e.deltaY>0?1/1.12:1.12);},{capture:true,passive:false});
 GUI.prepare_canvas();Layers.init_zoom_lib();redraw();
 return {zoom:e=>zoomAt(e.clientX,e.clientY,e.deltaY>0?1/1.12:1.12),pan:d=>{const r=canvas.getBoundingClientRect();zoomView.move(d.x*canvas.width/r.width,d.y*canvas.height/r.height);redraw();}};
}

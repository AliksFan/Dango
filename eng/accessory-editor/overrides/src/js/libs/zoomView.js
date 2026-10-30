// Dango canvas viewport adapter. Scale plus translation, with bounded pan.
// Replaces the upstream externally credited transform implementation.
let context, scale=1, x=0,y=0;
let bounds={top:0,left:0,right:256,bottom:256};
function constrain() {
  if(!context)return;
  const w=context.canvas.width,h=context.canvas.height;
  const minimum=Math.min(w/(bounds.right-bounds.left),h/(bounds.bottom-bounds.top));
  scale=Math.max(scale,minimum||0.01);
  x=Math.min(0,Math.max(w-(bounds.right-bounds.left)*scale,x));
  y=Math.min(0,Math.max(h-(bounds.bottom-bounds.top)*scale,y));
}
export default {
  canvasDefault(){context.setTransform(1,0,0,1,0,0);},
  apply(){constrain();context.setTransform(scale,0,0,scale,x,y);},
  getPosition(){constrain();return {x,y};},getContext(){return context;},getBounds(){return {...bounds};},getScale(){return scale;},
  setContext(value){context=value;},setBounds(top,left,right,bottom){bounds={top,left,right,bottom};},
  toWorld(px,py){constrain();return {x:(px-x)/scale,y:(py-y)/scale};},
  toScreen(p,target={}){constrain();target.x=p.x*scale+x;target.y=p.y*scale+y;return target;},
  scaleAt(px,py,amount){x=px+(x-px)*amount;y=py+(y-py)*amount;scale*=amount;constrain();},
  move(dx,dy){x+=dx;y+=dy;constrain();}
};

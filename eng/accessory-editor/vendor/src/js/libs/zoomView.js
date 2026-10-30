// Dango canvas viewport adapter. The document can move freely inside its viewport.
// Replaces the upstream externally credited transform implementation.
let context, scale=1, x=0,y=0;
let bounds={top:0,left:0,right:256,bottom:256};
function constrain() {
  scale=Math.max(0.01,Math.min(32,scale));
}
export default {
  canvasDefault(){context.setTransform(1,0,0,1,0,0);},
  apply(){constrain();context.setTransform(scale,0,0,scale,x,y);},
  getPosition(){constrain();return {x,y};},getContext(){return context;},getBounds(){return {...bounds};},getScale(){return scale;},
  setContext(value){context=value;},setBounds(top,left,right,bottom){bounds={top,left,right,bottom};},
  reset(value=1,px=0,py=0){scale=value;x=px;y=py;constrain();},
  toWorld(px,py){constrain();return {x:(px-x)/scale,y:(py-y)/scale};},
  toScreen(p,target={}){constrain();target.x=p.x*scale+x;target.y=p.y*scale+y;return target;},
  scaleAt(px,py,amount){if(!Number.isFinite(amount)||amount<=0)return;const next=Math.max(0.01,Math.min(32,scale*amount)),ratio=next/scale;x=px+(x-px)*ratio;y=py+(y-py)*ratio;scale=next;},
  move(dx,dy){x+=dx;y+=dy;constrain();}
};

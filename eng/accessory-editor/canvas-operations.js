import config from './vendor/src/js/config.js';
import app from './vendor/src/js/app.js';
import {copy,validateDocument} from './skeleton-commands.js';

// Pixel operations and skeleton coordinates enter one miniPaint action.
export function installCanvasOperations({getSkeleton,setLiveSkeleton,ensureReady,ensureSettled,checkSize,beginPreparation}) {
 const {Layers,GUI,State}=app;
 const original=Layers.render_object.bind(Layers);
 function renderOriginal(ctx,layer,preview){if(layer.type!=='brush'||!layer.rotate)return original(ctx,layer,preview);const cx=layer.x+layer.width/2,cy=layer.y+layer.height/2;ctx.save();ctx.translate(cx,cy);ctx.rotate(layer.rotate*Math.PI/180);ctx.translate(-cx,-cy);try{return original(ctx,layer,preview);}finally{ctx.restore();}}
 Layers.render_object=(ctx,layer,preview)=>{
  if(!layer.flipX&&!layer.flipY)return renderOriginal(ctx,layer,preview);
  const cx=layer.x+layer.width/2,cy=layer.y+layer.height/2,a=(layer.rotate||0)*Math.PI/180;
  ctx.save();ctx.translate(cx,cy);ctx.rotate(a);ctx.scale(layer.flipX?-1:1,layer.flipY?-1:1);ctx.rotate(-a);ctx.translate(-cx,-cy);
  try{return renderOriginal(ctx,layer,preview);}finally{ctx.restore();}
 };
 async function imageTransform(image,kind){
  const c=document.createElement('canvas'),w=image.naturalWidth,h=image.naturalHeight;c.width=kind==='left'?h:w;c.height=kind==='left'?w:h;
  const ctx=c.getContext('2d');if(kind==='flipX'){ctx.translate(w,0);ctx.scale(-1,1);}else if(kind==='flipY'){ctx.translate(0,h);ctx.scale(1,-1);}else{ctx.translate(0,w);ctx.rotate(-Math.PI/2);}ctx.drawImage(image,0,0);
  const result=new Image();result.src=c.toDataURL('image/png');await result.decode();return result;
 }
 async function transformCanvas(kind,crop){
  ensureReady();ensureSettled();const w=config.WIDTH,h=config.HEIGHT;
  if(!['flipX','flipY','left','crop','expand'].includes(kind))throw Error('未知整图操作。');
  if(kind==='crop'||kind==='expand'){
   if(!crop||![crop.x,crop.y,crop.width,crop.height].every(Number.isInteger))throw Error('画布区域无效。');checkSize(crop.width,crop.height);
   if(kind==='crop'&&(crop.x<0||crop.y<0||crop.x+crop.width>w||crop.y+crop.height>h))throw Error('裁剪区域无效。');
   if(kind==='expand'&&(crop.x>0||crop.y>0||crop.x+crop.width<w||crop.y+crop.height<h))throw Error('扩展必须包含原图。');
  }
  const nw=kind==='left'?h:['crop','expand'].includes(kind)?crop.width:w,nh=kind==='left'?w:['crop','expand'].includes(kind)?crop.height:h;
  const releasePreparation=beginPreparation();
  try {
  const before={layers:config.layers,width:w,height:h,skeleton:getSkeleton(),selected:config.layer.id};
  const after={...before,width:nw,height:nh,layers:[],skeleton:copy(before.skeleton)};
  const map=p=>kind==='flipX'?{x:w-p.x,y:p.y}:kind==='flipY'?{x:p.x,y:h-p.y}:kind==='left'?{x:p.y,y:w-p.x}:{x:p.x-crop.x,y:p.y-crop.y};
  Object.assign(after.skeleton.pivot,map(after.skeleton.pivot));for(const g of after.skeleton.groups)for(const n of g.nodes)Object.assign(n,map(n));validateDocument(after.skeleton);
  for(const l of before.layers){
   const n={...l};if(l.type===null){after.layers.push(n);continue;}
   if(kind==='crop'||kind==='expand'){n.x-=crop.x;n.y-=crop.y;after.layers.push(n);continue;}
   const center=map({x:l.x+l.width/2,y:l.y+l.height/2});
   if(l.type==='image'){
    n.link=await imageTransform(l.link,kind);if(l.link_restore)n.link_restore=await imageTransform(l.link_restore,kind);
    if(kind==='left'){n.width=l.height;n.height=l.width;n.width_original=l.height_original;n.height_original=l.width_original;n.flipX=l.flipY;n.flipY=l.flipX;}else n.rotate=-(l.rotate||0);
   }else if(l.type==='brush'){
    n.data=l.data.map(group=>group.map(([x,y,s])=>kind==='flipX'?[l.width-x,y,s]:kind==='flipY'?[x,l.height-y,s]:[y,l.width-x,s]));if(kind==='left'){n.width=l.height;n.height=l.width;n.flipX=l.flipY;n.flipY=l.flipX;}else n.rotate=-(l.rotate||0);
   }else if(kind==='left')n.rotate=((l.rotate||0)+270)%360;
   else{n.rotate=-(l.rotate||0);n[kind]=!l[kind];}
   n.x=center.x-n.width/2;n.y=center.y-n.height/2;after.layers.push(n);
  }
  function apply(s){config.layers=s.layers;config.layer=s.layers.find(l=>l.id===s.selected)||s.layers[0];config.WIDTH=s.width;config.HEIGHT=s.height;setLiveSkeleton(s.skeleton);GUI.prepare_canvas();Layers.init_zoom_lib();GUI.GUI_layers.render_layers();config.need_render=true;}
  // Release and acquire State.do_action in the same JS job. No UI action can
  // interleave between asynchronous decode preparation and the atomic commit.
  releasePreparation();
  const result=await State.do_action({action_id:'canvas_'+kind,before,after,async do(){apply(after);},async undo(){apply(before);},async free(){}});
  if(result?.status==='aborted')throw Error('整图操作失败，草稿已保留。');return getSkeleton();
  } finally { releasePreparation(); }
 }
 return transformCanvas;
}

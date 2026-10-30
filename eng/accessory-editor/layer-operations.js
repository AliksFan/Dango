// Shared transforms use miniPaint layer records, never a second scene model.
const radians=a=>a*Math.PI/180;
export function selectionBounds(layers){
 const points=layers.filter(l=>l.type!==null).flatMap(l=>{const a=radians(l.rotate||0),c=Math.cos(a),s=Math.sin(a),cx=l.x+l.width/2,cy=l.y+l.height/2;return [[-1,-1],[1,-1],[1,1],[-1,1]].map(([x,y])=>({x:cx+c*x*l.width/2-s*y*l.height/2,y:cy+s*x*l.width/2+c*y*l.height/2}));});
 if(!points.length)return null;const x=Math.min(...points.map(p=>p.x)),y=Math.min(...points.map(p=>p.y));return {x,y,width:Math.max(...points.map(p=>p.x))-x,height:Math.max(...points.map(p=>p.y))-y};
}
export function checkLayerBudget(layers){
 if(!layers.length||layers.length>16)throw Error('图层数量须为 1–16，操作超过上限。');
 let pixels=0;
 for(const l of layers){if(l.type===null)continue;if(![l.x,l.y,l.width,l.height,l.rotate||0].every(Number.isFinite)||Math.abs(l.x)>8192||Math.abs(l.y)>8192||l.width<.001||l.height<.001||l.width>4096||l.height>4096||Math.ceil(l.width)*Math.ceil(l.height)>16000000)throw Error('图层变换超出合法范围。');pixels+=Math.ceil(l.width)*Math.ceil(l.height);}
 if(pixels>64000000)throw Error('图层像素超出资源上限。');return layers;
}
export function transformSelection(layers,{dx=0,dy=0,scale=1,angle=0,cx,cy}={}){
 if(!Number.isFinite(scale)||scale<=0||![dx,dy,angle].every(Number.isFinite))throw Error('图层变换超出合法范围。');
 const box=selectionBounds(layers);if(!box)throw Error('请选择含内容的图层。');cx??=box.x+box.width/2;cy??=box.y+box.height/2;const c=Math.cos(radians(angle)),s=Math.sin(radians(angle));
 const next=layers.map(l=>{if(l.type===null)return {...l};const x=(l.x+l.width/2-cx)*scale,y=(l.y+l.height/2-cy)*scale,n={...l,width:l.width*scale,height:l.height*scale,rotate:((l.rotate||0)+angle+540)%360-180};n.x=cx+c*x-s*y+dx-n.width/2;n.y=cy+s*x+c*y+dy-n.height/2;
  if(scale!==1&&l.type==='brush'){n.params={...l.params,size:l.params.size*scale};n.data=l.data.map(line=>line.map(([x,y,p])=>[x*scale,y*scale,p*scale]));if(n.params.size<1||n.params.size>999||n.data.some(line=>line.some(([x,y,p])=>Math.abs(x)>8192||Math.abs(y)>8192||p<.001||p>2048)))throw Error('画笔缩放超出合法范围。');}
  if(scale!==1&&l.type==='text'){n.data=l.data.map(line=>line.map(span=>{const meta={...span.meta};for(const [key,defaultValue]of [['size',40],['kerning',0],['leading',0],['stroke_size',0]])meta[key]=(meta[key]??defaultValue)*scale;if(meta.size<1||meta.size>512||Math.abs(meta.kerning)>999||Math.abs(meta.leading)>999||meta.stroke_size>64)throw Error('文字缩放超出合法范围。');return {...span,meta};}));n._needs_update_data=true;}
  return n;});return checkLayerBudget(next);
}
export function alignSelection(layers,kind){const box=selectionBounds(layers);if(!box)throw Error('请选择含内容的图层。');return layers.map(l=>{const b=selectionBounds([l]);if(!b)return {...l};const dx=kind==='left'?box.x-b.x:kind==='center'?box.x+box.width/2-b.x-b.width/2:kind==='right'?box.x+box.width-b.x-b.width:0,dy=kind==='top'?box.y-b.y:kind==='middle'?box.y+box.height/2-b.y-b.height/2:kind==='bottom'?box.y+box.height-b.y-b.height:0;return {...l,x:l.x+dx,y:l.y+dy};});}
export function mergePlan(skeleton,layers,ids){
 const selected=new Set(ids.map(String)),members=skeleton.layers.filter(l=>selected.has(l.id)),indexes=members.map(l=>skeleton.layers.indexOf(l));
 if(members.length<2||members.length!==selected.size)throw Error('请选择至少两个连续图层。');
 if(indexes.at(-1)-indexes[0]+1!==members.length)throw Error('只能合并连续图层。');
 if(members.some(l=>l.groupId!==members[0].groupId))throw Error('只能合并同一骨架组的图层（或均未分组）。');
 const chosen=members.map(m=>layers.find(l=>String(l.id)===m.id));if(chosen.some(l=>!l||l.composition!=='source-over'))throw Error('依赖背景的混合模式不能保留外观，请先调整图层。');
 return {ids:members.map(l=>l.id),groupId:members[0].groupId,layers:chosen,index:indexes[0]};
}

export function installLayerOperations({config,State,Layers,GUI,getSkeleton,getRevision=()=>0,setSkeleton,getSelection,setSelection,ensureSettled,onError,onRender}){
 let preparing=false;
 const selected=()=>{const ids=new Set(getSelection().map(String));return config.layers.filter(l=>ids.has(String(l.id)));};
 async function commit(after,nextSkeleton=getSkeleton(),afterIds=getSelection(),actionId='multi_layer'){
  checkLayerBudget(after);
  let assetPixels=0,pieces=0,previousGroup;const byId=new Map(after.map(l=>[String(l.id),l]));
  for(const l of after)for(const image of [l.link,l.link_restore])if(l.type==='image'&&image)assetPixels+=(image.naturalWidth||image.width||0)*(image.naturalHeight||image.height||0);
  for(const member of nextSkeleton.layers){const l=byId.get(member.id);if(!l||l.type===null||!l.visible||l.opacity<=0)continue;if(!pieces||previousGroup!==member.groupId){pieces++;previousGroup=member.groupId;}}
  if(assetPixels+pieces*config.WIDTH*config.HEIGHT>64000000)throw Error('图层与绘制片超出资源上限。');
  const before=config.layers,beforeSkeleton=getSkeleton(),beforeId=config.layer?.id,beforeIds=getSelection(),afterId=afterIds[0];
  function apply(layers,skeleton,id,ids){config.layers=layers;config.layer=layers.find(l=>String(l.id)===String(id))||layers[0];setSkeleton(skeleton);setSelection(ids);config.need_render=true;GUI.GUI_layers.render_layers();onRender();}
  const action={action_id:actionId,before,after,async do(){apply(after,nextSkeleton,afterId,afterIds);},async undo(){apply(before,beforeSkeleton,beforeId,beforeIds);},async free(){this.before=null;this.after=null;}};
  const result=await State.do_action(action);if(result?.status==='aborted')throw Error('图层操作未完成，原图已保留。');return result;
 }
 async function update(changes,id='multi_transform'){const map=new Map(changes.map(l=>[String(l.id),l]));return commit(config.layers.map(l=>map.get(String(l.id))||l),getSkeleton(),getSelection(),id);}
 async function execute(kind,value){
  if(preparing)throw Error('正在处理图层，请稍候。');ensureSettled();const current=selected();if(!current.length)throw Error('请先选择图层。');
  if(kind==='opacity'){if(!Number.isFinite(value)||value<0||value>100)throw Error('透明度须为 0–100。');return update(current.map(l=>({...l,opacity:value})),'multi_opacity');}
  if(kind==='visible')return update(current.map(l=>({...l,visible:!current.every(l=>l.visible)})),'multi_visible');
  if(kind.startsWith('align-'))return update(alignSelection(current,kind.slice(6)),'multi_align');
  if(kind==='delete'){
   const ids=new Set(current.map(l=>String(l.id))),doc=getSkeleton();let next=config.layers.filter(l=>!ids.has(String(l.id)));if(!next.length){const first=current[0],blank={...first,name:'空白图层',type:null,x:0,y:0,width:0,height:0,rotate:0,opacity:100,visible:true,is_vector:false,render_function:null,data:null,params:{},status:null,filters:[],link:null,link_canvas:null};delete blank.link_restore;next=[blank];doc.layers=doc.layers.filter(l=>l.id===String(first.id));}else doc.layers=doc.layers.filter(l=>!ids.has(l.id));return commit(next,doc,[String(next[0].id)],'multi_delete');
  }
  if(kind==='duplicate'){
   if(config.layers.length+current.length>16)throw Error('复制将超过 16 个图层上限。');const doc=getSkeleton(),ids=new Set(current.map(l=>String(l.id))),copies=[],next=[],members=[];let id=Math.max(Layers.auto_increment,...config.layers.map(l=>l.id+1));
   for(const member of doc.layers){const l=config.layers.find(l=>String(l.id)===member.id);next.push(l);members.push(member);if(ids.has(member.id)){if(id>1000000)throw Error('图层标识达到上限。');const n={...l,id:id++,name:(l.name+' 副本').slice(0,128),params:structuredClone(l.params),data:structuredClone(l.data)};next.push(n);copies.push(String(n.id));members.push({...member,id:String(n.id)});}}
   doc.layers=members;const result=await commit(next.map((l,i)=>({...l,order:i+1})),doc,copies,'multi_duplicate');Layers.auto_increment=id;return result;
  }
  if(kind==='merge'||kind==='merge-down'){
   const doc=getSkeleton();let ids=current.map(l=>String(l.id));if(kind==='merge-down'){if(ids.length!==1)throw Error('向下合并请只选择一个图层。');const index=doc.layers.findIndex(l=>l.id===ids[0]);if(index<=0)throw Error('当前已是最下层。');ids=[doc.layers[index-1].id,ids[0]];}
   const plan=mergePlan(doc,config.layers,ids);if(config.WIDTH*config.HEIGHT>16000000)throw Error('合并画布超出资源上限。');
   // Two full-canvas buffers plus the resulting decoded PNG must fit the history budget.
   State.check_memory_budget?.(config.WIDTH*config.HEIGHT*12);preparing=true;
   try{const revision=getRevision(),layersAtStart=config.layers,canvas=document.createElement('canvas'),temp=document.createElement('canvas');canvas.width=temp.width=config.WIDTH;canvas.height=temp.height=config.HEIGHT;const ctx=canvas.getContext('2d');Layers.render_objects(ctx,temp,plan.layers.slice().reverse());const png=canvas.toDataURL('image/png');if(png.length>Math.ceil(32*1024*1024/3)*4+64)throw Error('合并图片超出资源上限。');const image=new Image();image.src=png;await image.decode();preparing=false;ensureSettled();if(layersAtStart!==config.layers||revision!==getRevision())throw Error('工程已变化，请重新合并。');
    const base=plan.layers[0],merged={...base,name:'合并图层',type:'image',x:0,y:0,width:config.WIDTH,height:config.HEIGHT,width_original:config.WIDTH,height_original:config.HEIGHT,rotate:0,flipX:false,flipY:false,opacity:100,visible:plan.layers.some(l=>l.visible),is_vector:false,render_function:null,data:null,params:{},status:null,filters:[],link:image,link_canvas:null};delete merged.link_restore;
    const chosen=new Set(plan.ids),next=[];for(const member of doc.layers){if(member.id===String(base.id))next.push(merged);else if(!chosen.has(member.id))next.push(config.layers.find(l=>String(l.id)===member.id));}doc.layers=doc.layers.filter(l=>!chosen.has(l.id)||l.id===String(base.id));
    return await commit(next.map((l,i)=>({...l,order:i+1})),doc,[String(base.id)],'multi_merge');
   }finally{preparing=false;}
  }
  throw Error('未知图层操作。');
 }
 return {execute,update,selected,isPreparing:()=>preparing};
}

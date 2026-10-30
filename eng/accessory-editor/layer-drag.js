import {copy,validateDocument,transferLayer} from './skeleton-commands.js';

// miniPaint stores paint order back-to-front; the tree is displayed front-to-back.
export function layerUnits(doc){
 const units=[];
 for(const layer of doc.layers.slice().reverse()){
  const key=layer.groupId?'group:'+layer.groupId:'layer:'+layer.id;
  if(units.at(-1)?.key!==key)units.push({key,groupId:layer.groupId,layers:[]});
  units.at(-1).layers.push(layer);
 }
 for(let i=0;i<doc.groups.length;i++){
  const group=doc.groups[i];if(units.some(u=>u.key==='group:'+group.id))continue;
  const next=doc.groups.slice(i+1).find(g=>units.some(u=>u.key==='group:'+g.id));
  const at=next?units.findIndex(u=>u.key==='group:'+next.id):units.length;
  units.splice(at,0,{key:'group:'+group.id,groupId:group.id,layers:[]});
 }
 return units;
}
export function moveGroup(doc,id,key,before){
 const next=copy(doc),units=layerUnits(next),from=units.findIndex(u=>u.key==='group:'+id);
 if(from<0)throw Error('图层组已不存在。');if(key==='group:'+id)return next;
 const [unit]=units.splice(from,1);let to=key?units.findIndex(u=>u.key===key):units.length;
 if(to<0)throw Error('插入位置已不存在。');if(key&&!before)to++;
 units.splice(to,0,unit);next.layers=units.slice().reverse().flatMap(u=>u.layers.slice().reverse());
 next.groups=units.filter(u=>u.groupId).map(u=>next.groups.find(g=>g.id===u.groupId));
 if(layerUnits(next).map(u=>u.key).join(',')!==units.map(u=>u.key).join(','))throw Error('空组目前只能相对其他组排序；请先加入图层，再移动到未分组图层旁。');
 return validateDocument(next);
}
export function installLayerDrag({container,getDocument,getRevision,ensureSettled,apply,onError}){
 let source=null,preview=null,revision=-1,scrollFrame=null,lastPoint=null,expandTimer=null,expandTarget=null,rejection=null,rejectionNotice=null;
 function clearMarker(){container.querySelectorAll('[data-drop-position]').forEach(n=>{delete n.dataset.dropPosition;n.classList.remove('layer-drop-target');});preview=null;}
 function stopScroll(){cancelAnimationFrame(scrollFrame);scrollFrame=null;lastPoint=null;}
 function stopExpand(){clearTimeout(expandTimer);expandTimer=null;expandTarget=null;}
 function finish(){stopScroll();stopExpand();clearMarker();source=null;rejection=null;}
 function clearRejection(){rejectionNotice?.dismiss?.();rejectionNotice=null;rejection=null;}
 function reportRejection(message){if(rejection===message)return;clearRejection();rejection=message;rejectionNotice=onError(message);}
 function proposal(event){
  const doc=getDocument(),row=event.target.closest('[data-image-layer]'),header=event.target.closest('[data-group-target]'),list=container.querySelector('#layers');
  const units=layerUnits(doc),visible=[...list.children].filter(n=>!n.hidden);
  let key,groupId=null,before=true,marker,position;
  if(header){key='group:'+header.dataset.groupTarget;marker=header;const rect=header.getBoundingClientRect();before=event.clientY<rect.top+rect.height/2;
   if(source.kind==='layer'&&event.clientY>=rect.top+rect.height*.25){groupId=header.dataset.groupTarget;position='inside';}
   else position=before?'before':'after';
  }else if(row){const layer=doc.layers.find(l=>l.id===row.dataset.imageLayer);if(!layer)throw Error('图层已不存在。');const rect=row.getBoundingClientRect();before=event.clientY<rect.top+rect.height/2;
   key=layer.groupId?'group:'+layer.groupId:'layer:'+layer.id;groupId=source.kind==='layer'?layer.groupId:null;marker=source.kind==='group'&&layer.groupId?row.closest('.layer-group'):row;position=before?'before':'after';
  }else{
   if(!list.contains(event.target)&&event.target.id!=='layer_ownership'&&event.target!==list)throw Error('请选择图层列表中的插入位置。');
   if(!visible.length)throw Error('列表中没有插入位置。');
   const first=visible[0],last=visible.at(-1),rect=list.getBoundingClientRect();before=event.clientY<rect.top+rect.height/2;marker=before?first:last;
   key=marker.dataset.layerGroup?'group:'+marker.dataset.layerGroup:'layer:'+marker.dataset.imageLayer;position=before?'before':'after';
  }
  if(source.kind==='group')return {next:moveGroup(doc,source.id,key,before),marker,position};
  const remaining=doc.layers.filter(l=>l.id!==source.id);let index;
  const rootIndex=()=>{const at=units.findIndex(u=>u.key===key),lower=units.slice(at+(before?0:1)).flatMap(u=>u.layers).find(l=>l.id!==source.id);return lower?remaining.findIndex(l=>l.id===lower.id)+1:0;};
  if(header&&position==='inside'){
   const members=remaining.filter(l=>l.groupId===groupId);index=members.length?remaining.indexOf(members.at(-1))+1:rootIndex();
  }else if(row){const target=remaining.find(l=>l.id===row.dataset.imageLayer);if(!target)return {next:doc,marker,position};index=remaining.indexOf(target)+(before?1:0);
  }else{
   index=rootIndex();
  }
  return {next:transferLayer(doc,source.id,groupId,remaining[index]?.id??null),marker,position};
 }
 function showProposal(event){
  clearMarker();try{preview=proposal(event);clearRejection();preview.marker.dataset.dropPosition=preview.position;preview.marker.classList.add('layer-drop-target');return true;}catch(error){reportRejection(error.message);return false;}
 }
 function refreshAtPointer(){if(!lastPoint)return;const target=document.elementFromPoint(lastPoint.clientX,lastPoint.clientY);if(target&&container.contains(target))showProposal({...lastPoint,target});else clearMarker();}
 function edgeSpeed(){
  const list=container.querySelector('#layers');if(!list||!lastPoint)return 0;const r=list.getBoundingClientRect(),{clientX:x,clientY:y}=lastPoint;
  if(x<r.left||x>r.right||y<r.top||y>r.bottom)return 0;
  const edge=Math.min(30,r.height/3);return y<r.top+edge?-420*(1-(y-r.top)/edge):y>r.bottom-edge?420*(1-(r.bottom-y)/edge):0;
 }
 function startScroll(){
  if(scrollFrame!==null)return;let previous=performance.now();
  function tick(now){
   scrollFrame=null;if(!source||revision!==getRevision()){stopScroll();return;}
   const speed=edgeSpeed(),list=container.querySelector('#layers');if(!speed||!list)return;
   const before=list.scrollTop;list.scrollTop+=speed*Math.min(40,now-previous)/1000;previous=now;
   if(before!==list.scrollTop){stopExpand();refreshAtPointer();scrollFrame=requestAnimationFrame(tick);}
  }
  scrollFrame=requestAnimationFrame(tick);
 }
 function scheduleExpand(event){
  const header=event.target.closest('[data-group-target]'),toggle=header?.querySelector('[data-group-toggle][aria-expanded=false]');
  if(source.kind!=='layer'||!toggle||preview?.position!=='inside'){stopExpand();return;}
  if(expandTarget===toggle)return;stopExpand();expandTarget=toggle;expandTimer=setTimeout(()=>{if(source&&revision===getRevision()){toggle.click();refreshAtPointer();}stopExpand();},650);
 }
 container.addEventListener('dragstart',e=>{
  if(e.target.closest('[data-group-toggle]')){e.preventDefault();return;}
  const row=e.target.closest('[data-image-layer]'),group=e.target.closest('[data-group-target]');if(!row&&!group)return;
  try{ensureSettled();}catch(error){e.preventDefault();onError(error.message);return;}
  clearRejection();source={kind:row?'layer':'group',id:row?row.dataset.imageLayer:group.dataset.groupTarget};revision=getRevision();e.dataTransfer.setData('application/x-dango-'+source.kind,source.id);e.dataTransfer.effectAllowed='move';
 });
 container.addEventListener('dragover',e=>{
  if(!source)return;e.preventDefault();if(revision!==getRevision()){clearMarker();reportRejection('工程修订已变化，拖放已取消。');e.dataTransfer.dropEffect='none';stopScroll();stopExpand();return;}
  lastPoint={clientX:e.clientX,clientY:e.clientY};e.dataTransfer.dropEffect=showProposal(e)?'move':'none';scheduleExpand(e);if(edgeSpeed())startScroll();else{cancelAnimationFrame(scrollFrame);scrollFrame=null;}
 });
 container.addEventListener('drop',e=>{
  if(!source)return;e.preventDefault();try{ensureSettled();if(revision!==getRevision())throw Error('工程修订已变化，拖放已取消。');const result=proposal(e);if(JSON.stringify(result.next)!==JSON.stringify(getDocument()))apply(result.next);}catch(error){reportRejection(error.message);}finally{finish();}
 });
 container.addEventListener('dragleave',e=>{if(!container.contains(e.relatedTarget)){stopScroll();stopExpand();clearMarker();}});
 // Native HTML drag suppresses `drop` when dropEffect is none. Report a refusal
 // once while hovering that target, and withdraw it upon reaching a valid one.
 window.addEventListener('dragend',finish,true);window.addEventListener('blur',finish);
 document.addEventListener('keydown',e=>{if(e.key==='Escape')finish();},true);
}

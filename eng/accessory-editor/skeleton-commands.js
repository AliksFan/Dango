// Shared, DOM-free editing commands. S0 uses isolated in-memory documents only.
// These defensive sample limits are not the S1 product resource contract.
export const SAMPLE_LIMITS = Object.freeze({groups:12, nodes:96, history:40});
export const copy = value => structuredClone(value);
export const GROUP_DEFAULTS=Object.freeze({bend:.7,damping:6,restoration:5,gravity:0,elasticity:0});
// Same inclusive 1e-6 output-pixel threshold as the independent native validator.
const samePoint = (a,b) => Math.hypot(a.x-b.x,a.y-b.y)<=1e-6;
const validId=id=>typeof id==='string'&&/^[A-Za-z0-9_-]{1,64}$/.test(id);
export function allNodes(doc) { return [doc.pivot,...doc.groups.flatMap(g=>g.nodes)]; }
export function locate(doc,id) {
 if(id===doc.pivot.id) return {node:doc.pivot,group:null,index:-1};
 for(const group of doc.groups) { const index=group.nodes.findIndex(n=>n.id===id); if(index>=0)return {node:group.nodes[index],group,index}; }
 throw Error('节点已不存在。');
}
export function connectedIds(doc,id){function canonical(key){const s=locate(doc,key);return s.group&&s.index===0&&s.group.targetId?canonical(s.group.targetId):key;}const key=canonical(id);return allNodes(doc).filter(n=>canonical(n.id)===key).map(n=>n.id);}
function groupOrder(doc) {
 const visiting=new Set(),done=new Set(),result=[];
 function visit(group) {
  if(visiting.has(group.id))throw Error('不能挂到本组或后代组：会形成循环。');
  if(done.has(group.id))return;
  visiting.add(group.id);
  if(group.targetId) { const target=locate(doc,group.targetId); if(!target.group)throw Error('骨架根只能挂到其他组的 R/J。'); visit(target.group); }
  visiting.delete(group.id);done.add(group.id);result.push(group);
 }
 doc.groups.forEach(visit);return result;
}
function propagate(doc) {
 for(const g of groupOrder(doc))if(g.targetId) { const p=locate(doc,g.targetId).node;Object.assign(g.nodes[0],{x:p.x,y:p.y}); }
 return doc;
}
export function validateDocument(input) {
 const doc=copy(input), ids=new Set();doc.version??=1;if(doc.version!==1||doc.pivot?.id!=='P')throw Error('骨架版本或配饰根无效。');
 if(!Array.isArray(doc.groups)||doc.groups.length>SAMPLE_LIMITS.groups)throw Error('样板组数超出临时上限。');
 if(allNodes(doc).length>SAMPLE_LIMITS.nodes)throw Error('样板节点数超出临时上限。');
 for(const n of allNodes(doc)) {
  if(!validId(n.id)||ids.has(n.id)||![n.x,n.y].every(Number.isFinite)||Math.max(Math.abs(n.x),Math.abs(n.y))>8192)throw Error('节点标识或坐标无效。');
  ids.add(n.id);
 }
 const groupIds=new Set();
 for(const g of doc.groups) {
  if(groupIds.has(g.id)||!validId(g.id)||!g.nodes.length||typeof g.name!=='string'||!g.name.trim()||g.name.length>128||/[\u0000-\u001f\u007f-\u009f<>]/.test(g.name))throw Error('组名称或根无效。');groupIds.add(g.id);
  g.parameters={...GROUP_DEFAULTS,...g.parameters};
  for(const [key,max] of [['bend',1],['damping',30],['restoration',30],['gravity',100],['elasticity',.5]])if(!Number.isFinite(g.parameters[key])||g.parameters[key]<0||g.parameters[key]>max)throw Error('骨架参数超出范围。');
  for(let i=1;i<g.nodes.length;i++)if(samePoint(g.nodes[i],g.nodes[i-1]))throw Error('相邻骨节不能重合：整笔操作未提交。');
 }
 groupOrder(doc);
 for(const g of doc.groups)if(g.targetId&&!samePoint(g.nodes[0],locate(doc,g.targetId).node))throw Error('挂接根必须与父目标同点。');
 const layerIds=new Set(),closed=new Set();let prior=null;
 for(const layer of doc.layers||[]) {
  if(!validId(layer.id)||layerIds.has(layer.id))throw Error('图层标识无效。');layerIds.add(layer.id);
  if(layer.groupId&&!groupIds.has(layer.groupId))throw Error('图层引用了不存在的组。');
  if(layer.groupId!==prior){if(prior)closed.add(prior);if(layer.groupId&&closed.has(layer.groupId))throw Error('组内图层必须连续；请显式重排。');prior=layer.groupId;}
 }
 return doc;
}
// Geometry snapshots invalidate future binding inputs; no S1 skin/binding algorithm here.
export function restGeometry(doc) {
 const result={};
 for(const g of groupOrder(doc)) {
  const segments=g.nodes.slice(1).map((n,i)=>({length:Math.hypot(n.x-g.nodes[i].x,n.y-g.nodes[i].y),angle:Math.atan2(n.y-g.nodes[i].y,n.x-g.nodes[i].x)}));
  let parentAngle=0;
  if(g.targetId) { const p=locate(doc,g.targetId),rest=result[p.group.id];parentAngle=p.index===0?rest.rootAngle:rest.segments[p.index-1].angle; }
  const rootAngle=segments[0]?.angle??parentAngle;
  result[g.id]={rootOffset:g.targetId?null:{x:g.nodes[0].x-doc.pivot.x,y:g.nodes[0].y-doc.pivot.y},rootAngle,relativeAngle:rootAngle-parentAngle,
   segments:segments.map((s,i)=>({...s,relativeAngle:s.angle-(i?segments[i-1].angle:rootAngle)})),bindingInput:g.nodes.map(n=>({x:n.x,y:n.y}))};
 }
 return result;
}
export function moveNodes(input,ids,delta) {
 const doc=copy(input),shared=new Set();
 function canonical(id){const s=locate(doc,id);return s.group&&s.index===0&&s.group.targetId?canonical(s.group.targetId):id;}
 for(const id of ids)shared.add(canonical(id));
 for(const id of shared){const {node}=locate(doc,id);node.x+=delta.x;node.y+=delta.y;}
 propagate(doc);
 return validateDocument(doc);
}
export function attachRoot(input,rootId,targetId) {
 const doc=copy(input),source=locate(doc,rootId),target=locate(doc,targetId);
 if(!source.group||source.index!==0)throw Error('只有骨架根可以挂接。');
 if(!target.group||source.group.id===target.group.id)throw Error('请选择其他组的 R/J。');
 source.group.targetId=targetId;propagate(doc);return validateDocument(doc);
}
export function detachRoot(input,rootId) {
 const doc=copy(input),s=locate(doc,rootId);if(!s.group||s.index!==0)throw Error('请选择骨架根。');
 s.group.targetId=null;return validateDocument(doc);
}
export function appendNode(input,fromId,point,id) {
 const doc=copy(input),from=locate(doc,fromId);
 if(!from.group||from.index!==from.group.nodes.length-1)throw Error('请选择单个末端或空组根续画；内部节点不能分叉。');
 from.group.nodes.push({id,...point});return validateDocument(doc);
}
export function insertNode(input,groupId,index,point,id) {
 const doc=copy(input),g=doc.groups.find(g=>g.id===groupId);
 if(!g||index<1||index>=g.nodes.length)throw Error('骨段已不存在。');
 g.nodes.splice(index,0,{id,...point});return validateDocument(doc);
}
export function deleteNodes(input,ids) {
 const doc=copy(input),selected=new Set(ids);
 for(const id of selected) { const s=locate(doc,id);if(!s.group||s.index===0)throw Error('P 和骨架根不能单独删除。'); }
 // Resolve against the full original chains before changing any group. A deleted
 // run reconnects to its nearest surviving predecessor, including external roots.
 const predecessors=new Map();
 for(const g of doc.groups) {
  let previous=g.nodes[0].id;
  for(const n of g.nodes) { if(selected.has(n.id))predecessors.set(n.id,previous);else previous=n.id; }
  g.nodes=g.nodes.filter(n=>!selected.has(n.id));
 }
 for(const g of doc.groups)if(predecessors.has(g.targetId))g.targetId=predecessors.get(g.targetId);
 // Move only reconnected roots and their same-point root aliases. Ordinary
 // surviving joints remain fixed; validation rejects any resulting zero segment.
 propagate(doc);
 return validateDocument(doc);
}
export function createGroup(input,{id,rootId,name,targetId=null}) {
 const doc=copy(input),p=targetId?locate(doc,targetId).node:doc.pivot;
 if(targetId&&!locate(doc,targetId).group&&targetId!==doc.pivot.id)throw Error('来源节点无效。');
 doc.groups.push({id,name,targetId:targetId===doc.pivot.id?null:targetId,nodes:[{id:rootId,x:p.x,y:p.y}],parameters:{...GROUP_DEFAULTS}});return validateDocument(doc);
}
export function deleteGroup(input,id){const doc=copy(input),g=doc.groups.find(g=>g.id===id);if(!g)throw Error('组已不存在。');const nodes=new Set(g.nodes.map(n=>n.id));if(doc.groups.some(other=>other.id!==id&&nodes.has(other.targetId)))throw Error('其他组仍连接此组，请先改接或解除。');doc.groups=doc.groups.filter(g=>g.id!==id);doc.layers.forEach(l=>{if(l.groupId===id)l.groupId=null;});return validateDocument(doc);}
export function groupLayers(input,ids,groupId){const doc=copy(input),selected=new Set(ids);if(groupId&&!doc.groups.some(g=>g.id===groupId))throw Error('请选择已有组。');const indices=doc.layers.map((l,i)=>selected.has(l.id)?i:-1).filter(i=>i>=0);if(!indices.length||indices.at(-1)-indices[0]+1!==indices.length)throw Error('请选择连续图层。');if(groupId&&indices.some(i=>doc.layers[i].groupId&&doc.layers[i].groupId!==groupId))throw Error('不能跨组归组，请先显式脱组。');for(const i of indices)doc.layers[i].groupId=groupId;return validateDocument(doc);}
export function detachLayers(input,ids){const doc=copy(input),selected=new Set(ids),picked=doc.layers.filter(l=>selected.has(l.id));if(!picked.length||!picked[0].groupId||picked.some(l=>l.groupId!==picked[0].groupId))throw Error('请选择同组图层脱组。');const groupId=picked[0].groupId;doc.layers=doc.layers.filter(l=>!selected.has(l.id));let end=doc.layers.findLastIndex(l=>l.groupId===groupId);if(end<0)end=Math.max(0,input.layers.findIndex(l=>selected.has(l.id)))-1;doc.layers.splice(end+1,0,...picked.map(l=>({...l,groupId:null})));return validateDocument(doc);}
export function reorderGroup(input,id,direction){const doc=copy(input),units=[];for(const layer of doc.layers){const key=layer.groupId||'layer:'+layer.id;if(units.at(-1)?.key!==key)units.push({key,layers:[]});units.at(-1).layers.push(layer);}const at=units.findIndex(u=>u.key===id),to=at+direction;if(at<0||to<0||to>=units.length)throw Error('组已到叠放边界，或组内尚无图层。');[units[at],units[to]]=[units[to],units[at]];doc.layers=units.flatMap(u=>u.layers);return validateDocument(doc);}
export function transferLayer(input,id,groupId,beforeId=null){const doc=copy(input),layer=doc.layers.find(l=>l.id===id);if(!layer)throw Error('图层已不存在。');doc.layers=doc.layers.filter(l=>l.id!==id);let index=beforeId?doc.layers.findIndex(l=>l.id===beforeId):doc.layers.length;if(index<0)index=doc.layers.length;doc.layers.splice(index,0,{...layer,groupId});return validateDocument(doc);}
export class SkeletonHistory {
 constructor(doc) { this.document=validateDocument(doc);this.revision=0;this.past=[];this.future=[];this.listeners=new Set(); }
 subscribe(listener) { this.listeners.add(listener);return ()=>this.listeners.delete(listener); }
 changed() { for(const listener of this.listeners)listener(); }
 commit(next,meta={},revision=this.revision) {
  if(revision!==this.revision)throw Error('工程修订已变化，本次操作已取消。');
  next=validateDocument(next);if(JSON.stringify(next)===JSON.stringify(this.document))return false;
  this.past.push({before:copy(this.document),after:copy(next),meta:copy(meta)});
  if(this.past.length>SAMPLE_LIMITS.history)this.past.shift();this.future=[];this.document=next;this.revision++;this.changed();return true;
 }
 undo() { const entry=this.past.pop();if(!entry)return null;this.future.push(entry);this.document=copy(entry.before);this.revision++;this.changed();return entry.meta.before||null; }
 redo() { const entry=this.future.pop();if(!entry)return null;this.past.push(entry);this.document=copy(entry.after);this.revision++;this.changed();return entry.meta.after||null; }
}

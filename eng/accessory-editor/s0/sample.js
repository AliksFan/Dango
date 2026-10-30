import {SkeletonHistory,restGeometry,locate} from '../skeleton-commands.js';
import {installSkeletonInput} from '../skeleton-input.js';

const fixture={pivot:{id:'P',x:150,y:140},groups:[
 {id:'A',name:'组 A',targetId:null,nodes:[{id:'A-R',x:150,y:140},{id:'A-J1',x:280,y:220},{id:'A-J2',x:400,y:290},{id:'A-J3',x:510,y:330}]},
 {id:'B',name:'组 B',targetId:'A-J1',nodes:[{id:'B-R',x:280,y:220},{id:'B-J1',x:290,y:340},{id:'B-J2',x:320,y:430}]}
],layers:[{id:'layer-a',groupId:'A',x:125,y:125,width:430,height:70},{id:'layer-b',groupId:'B',x:260,y:245,width:80,height:210},{id:'loose',groupId:null,x:585,y:350,width:115,height:100}]};
const history=new SkeletonHistory(fixture),surface=document.querySelector('#surface'),menu=document.querySelector('#menu');
const $=id=>document.getElementById(id),esc=text=>String(text).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const icon=path=>`<svg viewBox="0 0 20 20" aria-hidden="true"><path d="${path}"/></svg>`;
let controller;
function render(frame) {
 const {document:doc,state,view,gesture,context}=frame;
 surface.setAttribute('viewBox',`${view.x} ${view.y} ${view.width} ${view.height}`);
 const parts=['<defs><pattern id="loose-fill" width="8" height="8" patternUnits="userSpaceOnUse"><path d="m0 8 8-8" fill="none" stroke="var(--home-line)"/></pattern></defs>'];
 for(const layer of doc.layers)parts.push(`<rect class="art ${layer.groupId?'':'loose-art'}" x="${layer.x}" y="${layer.y}" width="${layer.width}" height="${layer.height}" rx="18"/><text x="${layer.x+12}" y="${layer.y+25}">${layer.groupId?esc(`图层 ${layer.groupId}`):'未分组层'}</text>`);
 for(const g of doc.groups)parts.push(`<polyline class="bone ${g.id===state.activeGroup?'active-bone':''}" points="${g.nodes.map(n=>`${n.x},${n.y}`).join(' ')}"/>`);
 const nodes=[{node:doc.pivot,label:'P',root:false,pivot:true},...doc.groups.flatMap(g=>g.nodes.map((node,i)=>({node,label:`${g.name} ${i?'J'+i:'R'}`,root:i===0,attached:!!g.targetId&&i===0})))];
 nodes.sort((a,b)=>Number(state.selected.includes(a.node.id))-Number(state.selected.includes(b.node.id)));
 for(const {node:n,label,root,pivot,attached} of nodes) {
  const selected=state.selected.includes(n.id),cls=`node ${selected?'selected':''}`;
  if(pivot)parts.push(`<rect class="${cls}" x="${n.x-9}" y="${n.y-9}" width="18" height="18"/>`);
  else if(root)parts.push(`<path class="${cls}" d="M${n.x} ${n.y-9}l9 9-9 9-9-9Z"/>`);
  else parts.push(`<circle class="${cls}" cx="${n.x}" cy="${n.y}" r="6"/>`);
  const offset=pivot?32:attached?27:root?-16:-11;parts.push(`<text x="${n.x+(pivot?-22:12)}" y="${n.y+offset}">${esc(label)}${attached?' ↳':''}</text>`);
 }
 if(gesture?.candidate) { const n=locate(doc,gesture.candidate.id).node;parts.push(`<circle class="candidate ${gesture.candidate.valid?'':'invalid'}" cx="${n.x}" cy="${n.y}" r="16"/>`); }
 if(gesture?.ghost) { const g=gesture.ghost;parts.push(`<circle class="ghost" cx="${g.x}" cy="${g.y}" r="10"/><text x="${g.x+14}" y="${g.y-12}">R 预览</text>`); }
 if(gesture?.marquee) { const b=gesture.marquee;parts.push(`<rect class="marquee" x="${b.x}" y="${b.y}" width="${b.width}" height="${b.height}"/>`); }
 surface.innerHTML=`<g pointer-events="none">${parts.join('')}</g>`;
 $('status').textContent=frame.message;$('hover').textContent=frame.hover.join(' / ');$('selection').textContent=`已选 ${state.selected.length} 点 · 撤销 ${frame.undo} 步`;
 $('undo').disabled=!frame.undo;$('redo').disabled=!frame.redo;
 for(const tool of ['modify','draw'])$(tool).setAttribute('aria-pressed',String(state.tool===tool));
 $('groups').replaceChildren(...doc.groups.map(g=>{
  const b=document.createElement('button');b.dataset.group=g.id;b.setAttribute('aria-pressed',String(g.id===state.activeGroup));b.append(document.createTextNode(g.name));
  const s=document.createElement('small');s.textContent=g.targetId?`挂接 ${g.targetId} · ${g.nodes.length-1} 骨节`:`自由根 · ${g.nodes.length-1} 骨节`;b.append(s);b.onclick=()=>{controller.setActiveGroup(g.id);surface.focus({preventScroll:true});};return b;
 }));
 menu.hidden=!context;
 if(context) {
  menu.replaceChildren();const t=context.target,s=t?.kind==='node'?locate(frame.committed,t.id):null;
  const label=document.createElement('div');label.className='menu-target';label.textContent=s?(s.group?`${s.group.name} ${s.index?'J'+s.index:'R'}`:'配饰根 P'):t?'骨段':'空白';menu.append(label);
  const actions=t?.kind==='segment'?[['insert','插入骨节','M3 10h14M10 4v12']]:s?.group?[
   ['create','从此处创建组','M3 10h14M10 3v14'],['select-chain','选中所在骨架','M3 3h14v14H3Z'],
   ...(s.index===0?[['attach','挂接骨架根','m7 13 6-6M7 7l-3 3a3 3 0 0 0 4 4l2-2m0-4 2-2a3 3 0 0 1 4 4l-3 3'],...(s.group.targetId?[['detach','解除挂接','M3 3l14 14M3 10h4m6 0h4']]:[]),['clear','清空骨节，保留根','M4 6h12M7 6v11h6V6M8 3h4']]:[['delete',state.selected.includes(s.node.id)?'删除所选节点':'删除此骨节','M4 6h12M7 6v11h6V6M8 3h4']])
  ]:s?[['create','从此处创建组','M3 10h14M10 3v14'],['select-all','选中全部骨架','M3 3h14v14H3Z']]:[['create','创建空组','M3 10h14M10 3v14']];
  for(const [action,title,path] of actions) { const b=document.createElement('button');b.setAttribute('role','menuitem');b.dataset.action=action;b.innerHTML=icon(path)+esc(title);b.onclick=()=>controller.command(action);menu.append(b); }
  // Keep commands inside the viewport without changing the context target.
  menu.style.left=Math.max(4,Math.min(context.x,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(4,Math.min(context.y,innerHeight-menu.offsetHeight-8))+'px';
 }
}
controller=installSkeletonInput({surface,history,onChange:render});
for(const tool of ['modify','draw'])$(tool).onclick=()=>{controller.setTool(tool);surface.focus({preventScroll:true});};
$('new-group').onclick=()=>controller.newGroup();$('undo').onclick=()=>controller.historyStep();$('redo').onclick=()=>controller.historyStep(true);
menu.addEventListener('keydown',e=>{if(e.key==='Escape'){e.preventDefault();controller.closeContext();surface.focus({preventScroll:true});}});
document.querySelector('main').addEventListener('pointerdown',e=>{if(!menu.hidden&&!menu.contains(e.target)&&e.target!==surface)controller.closeContext();});
window.addEventListener('pagehide',()=>controller.dispose(),{once:true});
// Read-only inspection for browser tests; mutations must enter through DOM events.
window.s0=Object.freeze({inspect:()=>({...controller.inspect(),geometry:restGeometry(history.document)})});

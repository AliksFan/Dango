import {copy,allNodes,locate,connectedIds,moveNodes,attachRoot,detachRoot,appendNode,insertNode,deleteNodes,createGroup} from './skeleton-commands.js';

// DOM adapter shared by the S0 specimen and the future editor integration.
// All previews are derived from a gesture snapshot; only finish() enters history.
export function installSkeletonInput({surface,history,onChange,onPan,onZoom}) {
 let enabled=true;
 let state={selected:[],activeGroup:history.document.groups[0]?.id||null,tool:'modify',drawStart:null};
 let pending=null,context=null,hover=[],message='选择节点，或在空白处框选。',space=false,serial=0,cycle={key:'',index:-1};
 let view={x:0,y:0,width:800,height:540};
 const scope=surface.closest('[data-skeleton-scope]');
 const listeners=[];
 const listen=(target,type,fn,options)=>{target.addEventListener(type,fn,options);listeners.push(()=>target.removeEventListener(type,fn,options));};
 const session=()=>copy(state);
 const doc=()=>history.document;
 const point=e=>{const p=new DOMPoint(e.clientX,e.clientY).matrixTransform(surface.getScreenCTM().inverse());return {x:p.x,y:p.y};};
 const screen=p=>new DOMPoint(p.x,p.y).matrixTransform(surface.getScreenCTM());
 const distance=(a,b)=>{const x=screen(a),y=screen(b);return Math.hypot(x.x-y.x,x.y-y.y);};
 const identity=id=>{const p=locate(doc(),id);return p.group?`${p.group.name} · ${p.index===0?'R':`J${p.index}`}`:'配饰根 P';};
 const hits=p=>allNodes(doc()).filter(n=>distance(n,p)<=11);
 function hit(p,alt=false) {
  const candidates=hits(p);if(!candidates.length)return null;
  if(alt) { const key=candidates.map(n=>n.id).join('|');cycle={key,index:cycle.key===key?(cycle.index+1)%candidates.length:0};return candidates[cycle.index]; }
  return candidates.find(n=>state.selected.includes(n.id))||candidates.find(n=>locate(doc(),n.id).group?.id===state.activeGroup)||candidates[0];
 }
 function candidate(p,rootId) {
  const sameJoint=new Set(connectedIds(pending.base,rootId));
  const candidates=hits(p).filter(n=>!sameJoint.has(n.id)&&n.id!==doc().pivot.id).map(n=>{
   try{return {id:n.id,valid:true,next:attachRoot(pending.base,rootId,n.id)};}catch(e){return {id:n.id,valid:false,error:e.message};}
  });
  if(!candidates.length){pending.candidateChoice=null;pending.advanceCandidate=false;return null;}
  let choice=candidates.find(c=>c.id===pending.candidateChoice)||candidates.find(c=>c.valid)||candidates[0];
  if(pending.advanceCandidate){choice=candidates[(candidates.indexOf(choice)+1)%candidates.length];pending.advanceCandidate=false;}
  pending.candidateChoice=choice.id;return choice;
 }
 function emit() {
  onChange({document:pending?.preview||doc(),committed:doc(),state:session(),view:copy(view),context:copy(context),hover:[...hover],message,
   gesture:pending?{kind:pending.kind,marquee:pending.marquee,ghost:pending.ghost,candidate:pending.candidate&&{id:pending.candidate.id,valid:pending.candidate.valid},error:pending.error}:null,
   revision:history.revision,undo:history.past.length,redo:history.future.length});
 }
 function release() { if(pending?.pointerId!==undefined&&surface.hasPointerCapture(pending.pointerId))surface.releasePointerCapture(pending.pointerId); }
 function normalizeState() {
  const ids=new Set(allNodes(doc()).map(n=>n.id));state.selected=state.selected.filter(id=>ids.has(id));
  if(!doc().groups.some(g=>g.id===state.activeGroup))state.activeGroup=null;
  const start=state.drawStart&&ids.has(state.drawStart)?locate(doc(),state.drawStart):null;
  if(state.selected.length!==1||state.selected[0]!==state.drawStart||!start?.group||start.index!==start.group.nodes.length-1)state.drawStart=null;
 }
 function cancel(reason='操作已取消。') {
  const before=pending?.before;const old=pending;pending=null;
  if(old?.pointerId!==undefined&&surface.hasPointerCapture(old.pointerId))surface.releasePointerCapture(old.pointerId);
  if(before)state=copy(before);if(old?.kind==='pan')view=old.view;context=null;normalizeState();message=reason;emit();
 }
 function fresh() { if(pending&&pending.revision!==history.revision){cancel('修订已变化，已取消旧手势。');return false;}return true; }
 function commit(next,before,after=session(),revision=history.revision) {
  history.commit(next,{before,after},revision);state=copy(after);state.selected=state.selected.filter(id=>allNodes(doc()).some(n=>n.id===id));
 }
 function select(id,shift=false) {
  if(shift)state.selected=state.selected.includes(id)?state.selected.filter(x=>x!==id):[...state.selected,id];else state.selected=[id];
  const p=locate(doc(),id);if(p.group)state.activeGroup=p.group.id;
  const sole=state.selected.length===1?locate(doc(),state.selected[0]):null;
  state.drawStart=sole?.group&&sole.index===sole.group.nodes.length-1?sole.node.id:null;
 }
 function updateMove(e) {
  const p=point(e);pending.last=p;pending.lastClient={clientX:e.clientX,clientY:e.clientY};pending.error=null;pending.ghost=null;pending.candidate=null;
  try {
   const selected=state.selected,source=selected.length===1?locate(pending.base,selected[0]):null;
   if(source?.group&&source.index===0) {
    const c=candidate(p,source.node.id);pending.candidate=c;
    if(c?.valid) { pending.preview=c.next;message=`松手挂接：${source.group.name} R → ${identity(c.id)} · Alt 切换候选`; }
    else { pending.preview=moveNodes(pending.base,selected,{x:p.x-pending.start.x,y:p.y-pending.start.y});message=c?.error||(source.group.targetId?'移动共享连接点；连接两端保持同点。':'只移动根；普通骨节与图像保持原位。'); }
   } else {
    pending.preview=moveNodes(pending.base,selected,{x:p.x-pending.start.x,y:p.y-pending.start.y});message='移动所选节点；引用子根按同点约束更新。';
   }
  } catch(error) { pending.preview=null;pending.error=error.message;message=error.message; }
 }
 function updateAttach(e) {
  if(!fresh())return;pending.lastClient={clientX:e.clientX,clientY:e.clientY};const p=point(e),c=candidate(p,pending.rootId);pending.candidate=c;pending.preview=c?.valid?c.next:null;pending.ghost=c?.valid?null:{id:pending.rootId,...p};
  message=c?.valid?`左击挂接到 ${identity(c.id)} · Alt 切换候选 · Esc / 右键取消`:c?.error||'移动到其他组 R/J，左击提交 · Esc / 右键取消';emit();
 }
 listen(surface,'pointerdown',e=>{
  if(!enabled)return;
  if(e.button!==0)return;e.preventDefault();surface.focus({preventScroll:true});context=null;
  if(!fresh())return;
  if(pending?.kind==='attach') {
   updateAttach(e);if(!e.altKey&&pending?.candidate?.valid){const before=pending.before,next=pending.preview,revision=pending.revision;const after=session();pending=null;commit(next,before,after,revision);message='挂接完成。';emit();}return;
  }
  if(pending)return;
  const p=point(e),before=session(),pan=space||state.tool==='pan',n=pan?null:hit(p,e.altKey),wasSelected=n&&state.selected.includes(n.id);
  // Preserve a selected set until a click is distinguished from a drag.
  const deferredSelect=wasSelected&&!e.altKey;
  if(n) { if(!deferredSelect)select(n.id,e.shiftKey);message=`${identity(n.id)}${hits(p).length>1?' · Alt+左击轮选重叠点':''}`; }
  pending={kind:pan?'pan':'pointer',base:copy(doc()),revision:history.revision,before,start:p,client:{x:e.clientX,y:e.clientY},pointerId:e.pointerId,hit:n?.id,wasSelected,deferredSelect,shift:e.shiftKey,moved:false,view:copy(view)};
  surface.setPointerCapture(e.pointerId);emit();
 });
 listen(surface,'pointermove',e=>{
  if(!enabled)return;
  if(!fresh())return;
  if(pending?.kind==='attach'){updateAttach(e);return;}
  const p=point(e);hover=hits(p).map(n=>identity(n.id));
  if(!pending){emit();return;}
  if(pending.pointerId!==e.pointerId)return;
  if(Math.hypot(e.clientX-pending.client.x,e.clientY-pending.client.y)>=4)pending.moved=true;
  if(!pending.moved)return;
  if(pending.kind==='pan') {
   if(onPan){const prior=pending.panClient||pending.client;onPan({x:e.clientX-prior.x,y:e.clientY-prior.y});pending.panClient={x:e.clientX,y:e.clientY};emit();return;}
   const r=surface.getBoundingClientRect(),scale=Math.min(r.width/pending.view.width,r.height/pending.view.height);
   view={...pending.view,x:pending.view.x-(e.clientX-pending.client.x)/scale,y:pending.view.y-(e.clientY-pending.client.y)/scale};emit();return;
  }
  if(pending.hit&&pending.wasSelected&&state.selected.includes(pending.hit)) {
   pending.kind='move';if(state.tool==='draw')state.tool='modify';state.drawStart=null;updateMove(e);
  } else if(!pending.hit) {
   pending.kind='marquee';pending.marquee={x:Math.min(p.x,pending.start.x),y:Math.min(p.y,pending.start.y),width:Math.abs(p.x-pending.start.x),height:Math.abs(p.y-pending.start.y)};
   const b=pending.marquee,inside=allNodes(doc()).filter(n=>n.x>=b.x&&n.x<=b.x+b.width&&n.y>=b.y&&n.y<=b.y+b.height).map(n=>n.id);
   state.selected=[...new Set([...(pending.shift?pending.before.selected:[]),...inside])];state.drawStart=null;
  }
  emit();
 });
 listen(surface,'pointerup',e=>{
  if(!pending||pending.kind==='attach'||e.button!==0||pending.pointerId!==e.pointerId||!fresh())return;
  // Re-hit at release: preview never grants a stale candidate permission to commit.
  if(pending.kind==='move')updateMove(e);
  const operation=pending;release();pending=null;
  try {
   if(operation.kind==='move') {
    if(operation.error)throw Error(operation.error);
    if(operation.preview)commit(operation.preview,operation.before,session(),operation.revision);
    message=operation.preview?'移动完成。':'已恢复原挂接。';
   } else if(operation.kind==='pointer'&&operation.hit&&!operation.moved&&operation.deferredSelect) {
    select(operation.hit,operation.shift);
   } else if(operation.kind==='pointer'&&!operation.hit&&!operation.moved) {
    if(state.tool==='draw'&&!operation.shift) {
     if(state.selected.length!==1||!state.drawStart)throw Error('请选择单个末端或空组根续画；多选不自动续画。');
     const id=`node-${crypto.randomUUID().replaceAll('-','')}`,next=appendNode(doc(),state.drawStart,point(e),id),after={...session(),selected:[id],drawStart:id};commit(next,operation.before,after,operation.revision);message='已追加骨节。';
    } else if(!operation.shift) { state.selected=[];state.drawStart=null;message='选择已清除。'; }
   }
  }catch(error){state=copy(operation.before);message=error.message;}
  emit();
 });
 listen(surface,'pointercancel',()=>{if(pending)cancel('指针操作已取消。');});
 listen(surface,'lostpointercapture',()=>{if(pending)cancel('指针捕获已丢失，已恢复操作前状态。');});
 function segmentAt(p) {
  for(const g of doc().groups)for(let i=1;i<g.nodes.length;i++) {
   const a=g.nodes[i-1],b=g.nodes[i],length=(b.x-a.x)**2+(b.y-a.y)**2,t=Math.max(0,Math.min(1,((p.x-a.x)*(b.x-a.x)+(p.y-a.y)*(b.y-a.y))/length)),q={x:a.x+t*(b.x-a.x),y:a.y+t*(b.y-a.y)};
   if(t>0&&t<1&&distance(p,q)<7)return {kind:'segment',groupId:g.id,index:i,point:q};
  }
  return null;
 }
 listen(surface,'contextmenu',e=>{
  if(!enabled)return;
  e.preventDefault();if(pending){cancel('已取消临时操作。');return;}
  const p=point(e),n=hit(p);context={target:n?{kind:'node',id:n.id}:segmentAt(p),x:e.clientX,y:e.clientY,revision:history.revision};emit();
 });
 listen(window,'blur',()=>{space=false;if(pending)cancel('窗口失焦，已恢复操作前状态。');});
 listen(document,'visibilitychange',()=>{if(document.hidden&&pending)cancel('窗口隐藏，已取消操作。');});
 const isInput=target=>target instanceof Element&&!!target.closest('input,textarea,select,button,[role=button],[contenteditable=true],[role=menu],[role=dialog]');
 listen(scope,'keydown',e=>{
  if(!enabled)return;
  // This editor owns its input domain, including native text/IME editing.
  e.stopPropagation();
  if(e.isComposing||isInput(e.target))return;
  if(!scope.contains(document.activeElement))return;
  const key=e.key.toLowerCase(),control=e.ctrlKey||e.metaKey;
  if(e.key==='Alt'&&!e.repeat&&pending?.lastClient&&(pending.kind==='attach'||pending.kind==='move')) {
   pending.advanceCandidate=true;if(pending.kind==='attach')updateAttach(pending.lastClient);else{updateMove(pending.lastClient);emit();}return;
  }
  if(e.key===' '){e.preventDefault();space=true;return;}
  if(e.key==='Escape'){e.preventDefault();e.stopPropagation();if(pending)cancel();else{context=null;state.drawStart=null;message='已结束临时操作。';emit();}return;}
  if(context)return;
  if(control&&['z','y'].includes(key)) { e.preventDefault();if(pending)cancel();const restored=key==='y'||e.shiftKey?history.redo():history.undo();if(restored)state=restored;message='历史已更新。';emit();return; }
  if(pending)return;
  if(control||e.altKey)return;
  if(key==='k'){e.preventDefault();setTool(e.shiftKey?'draw':'modify');return;}
  if(key==='p'){e.preventDefault();setTool('modify');select(doc().pivot.id);message='配饰根 P';emit();return;}
  if(e.key==='Enter'){e.preventDefault();state.drawStart=null;message='续画已结束；没有提交整个编辑器。';emit();return;}
  try {
   const before=session();
   if(e.key==='Delete') { e.preventDefault();if(!state.selected.length)return;const next=deleteNodes(doc(),state.selected),after={...session(),selected:[],drawStart:null};commit(next,before,after);message='删除完成。'; }
   else if(['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(e.key)) {
    e.preventDefault();if(!state.selected.length)return;const amount=e.shiftKey?10:1,delta={x:e.key==='ArrowLeft'?-amount:e.key==='ArrowRight'?amount:0,y:e.key==='ArrowUp'?-amount:e.key==='ArrowDown'?amount:0};commit(moveNodes(doc(),state.selected,delta),before);message='已按图像像素微调。';
   }else return;
  }catch(error){message=error.message;}emit();
 });
 listen(scope,'keyup',e=>{e.stopPropagation();if(e.key===' ')space=false;});
 for(const type of ['pointerdown','pointermove','pointerup','contextmenu'])listen(scope,type,e=>{if(enabled)e.stopPropagation();});
 const unsubscribe=history.subscribe(()=>{context=null;fresh();normalizeState();emit();});
 listen(surface,'wheel',e=>{
  if(!enabled)return;
  if(onZoom){e.preventDefault();if(!pending)onZoom(e);return;}
  e.preventDefault();if(pending)return;const p=point(e),factor=e.deltaY>0?1.12:1/1.12,newWidth=Math.max(200,Math.min(1600,view.width*factor)),ratio=newWidth/view.width;
  view={x:p.x-(p.x-view.x)*ratio,y:p.y-(p.y-view.y)*ratio,width:newWidth,height:view.height*ratio};emit();
 },{passive:false});
 function setTool(tool) {
  if(pending)cancel();context=null;state.tool=tool;const s=state.selected.length===1?locate(doc(),state.selected[0]):null;state.drawStart=tool==='draw'&&s?.group&&s.index===s.group.nodes.length-1?s.node.id:null;
  message=tool==='draw'?'选末端后点击空白续画；拖已选节点切换修改。':'选择节点，或在空白处框选。';emit();
 }
 function setActiveGroup(id) {
  if(pending)cancel('切组已取消临时操作。');context=null;const g=doc().groups.find(g=>g.id===id);state.activeGroup=g?.id||null;state.selected=g?[g.nodes[0].id]:[];state.drawStart=state.tool==='draw'&&g?.nodes.length===1?g.nodes[0].id:null;emit();
 }
 function command(action) {
  const c=context;if(!c)return;context=null;
  if(c.revision!==history.revision){message='菜单目标已过期，请重新打开。';emit();return;}
  const before=session(),target=c.target;
  try {
   const s=target?.kind==='node'?locate(doc(),target.id):null;
   if(action==='attach') {
    if(!s?.group||s.index!==0)throw Error('请选择根。');
    state.activeGroup=s.group.id;state.selected=[s.node.id];state.drawStart=null;
    pending={kind:'attach',rootId:s.node.id,before,base:copy(doc()),revision:history.revision};message='移动到其他组 R/J，左击提交 · Esc / 右键取消';
   } else if(action==='select-chain') { state.selected=s.group.nodes.map(n=>n.id);state.activeGroup=s.group.id;state.drawStart=null; }
   else if(action==='select-all') { state.selected=doc().groups.flatMap(g=>g.nodes.map(n=>n.id));state.drawStart=null; }
   else if(action==='delete'||action==='clear') {
    const ids=action==='clear'?s.group.nodes.slice(1).map(n=>n.id):state.selected.includes(s.node.id)?state.selected:[s.node.id];
    const next=deleteNodes(doc(),ids),after={...session(),selected:state.selected.filter(id=>!ids.includes(id)),drawStart:null,activeGroup:s.group.id};commit(next,before,after);message='删除完成。';
   } else if(action==='detach') { commit(detachRoot(doc(),s.node.id),before,{...session(),selected:[s.node.id],activeGroup:s.group.id});message='已解除挂接，保持初始位置。'; }
   else if(action==='create') {
    const id=`group-${crypto.randomUUID().replaceAll('-','')}`,rootId=`${id}-R`,next=createGroup(doc(),{id,rootId,name:`新组 ${++serial}`,targetId:s?.node.id||null}),after={...session(),activeGroup:id,selected:[rootId],drawStart:state.tool==='draw'?rootId:null};commit(next,before,after);message='已创建空组和唯一根。';
   } else if(action==='insert') {
    const id=`node-${crypto.randomUUID().replaceAll('-','')}`,next=insertNode(doc(),target.groupId,target.index,target.point,id);commit(next,before,{...session(),selected:[id],activeGroup:target.groupId,drawStart:null});message='已插入骨节。';
   }
  }catch(error){state=before;message=error.message;}
  emit();surface.focus({preventScroll:true});
 }
 function newGroup(){if(pending)cancel();context={revision:history.revision,target:null};command('create');}
 function historyStep(redo=false){if(pending)cancel();const s=redo?history.redo():history.undo();if(s)state=s;context=null;emit();surface.focus({preventScroll:true});}
 emit();
 return {setTool,setActiveGroup,command,newGroup,historyStep,cancel,restoreSession(value){state=copy(value);normalizeState();emit();},setTemporaryPan(value){space=value;},setEnabled(value){if(!value&&pending)cancel();enabled=value;emit();},selectPivot(){state.tool='modify';select(doc().pivot.id);emit();},closeContext(){context=null;emit();},refresh(){fresh();emit();},
  inspect:()=>({document:copy(doc()),preview:copy(pending?.preview||null),candidate:pending?.candidate?{id:pending.candidate.id,valid:pending.candidate.valid}:null,state:session(),revision:history.revision,undo:history.past.length,redo:history.future.length,pending:pending?.kind||null,message,view:copy(view)}),
  dispose(){if(pending)cancel();unsubscribe();listeners.forEach(fn=>fn());}};
}

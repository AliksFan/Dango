import app from './vendor/src/js/app.js';
import config from './vendor/src/js/config.js';
import zoomView from './vendor/src/js/libs/zoomView.js';
import notify from './notify.js';
import {copy,allNodes,locate,validateDocument,deleteGroup,groupLayers,detachLayers,reorderGroup} from './skeleton-commands.js';
import {installLayerDrag,layerUnits} from './layer-drag.js';
import {installSkeletonInput} from './skeleton-input.js';
import {installCanvasOperations} from './canvas-operations.js';
import {installCanvasView} from './canvas-view.js';
import {installListRename} from './list-rename.js';
import {installCanvasConfirm} from './canvas-confirm.js';
import {installLayerOperations} from './layer-operations.js';
import {installMultiLayerSelection} from './multi-layer-selection.js';

const $=id=>document.getElementById(id),escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const icon=path=>`<svg viewBox="0 0 20 20" aria-hidden="true"><path d="${path}"/></svg>`;
export function installSkeletonEditor({ensureReady,checkSize}){
 const {State,Layers,GUI}=app;let skeleton,name='配饰',revision=0,activeActions=0,mode='',controller,frame,layerSelection=new Set(),parameterDraft=null,treeKey='',painting=false,lastAction=Promise.resolve(),temporaryPan=null;
 const collapsedGroups=new Set(); // Session-only tree state; never part of the project or undo history.
 let clipboardGroup=null,clipboardLayerSelected=false,inspectorSkeletonVisible=false,multiSelection=null,layerOperations=null;
 const subscribers=new Set(),surface=document.createElementNS('http://www.w3.org/2000/svg','svg');surface.id='geometry_overlay';surface.tabIndex=0;surface.setAttribute('role','application');surface.setAttribute('aria-label','配饰骨架画布');$('canvas_wrapper').append(surface);
 surface.addEventListener('pointerdown',()=>{if(mode&&mode!=='pan'&&mode!=='zoom'){clipboardGroup=null;clipboardLayerSelected=false;}});
 const menu=$('skeleton_menu');
 for(const input of document.querySelectorAll('[type=number][data-skeleton-param]'))input.step='any';
 const layerOrder=()=>Layers.get_sorted_layers().slice().reverse();
 const empty=()=>({version:1,pivot:{id:'P',x:config.WIDTH/2,y:config.HEIGHT/2},groups:[],layers:layerOrder().map(l=>({id:String(l.id),groupId:null}))});
 skeleton=validateDocument(empty());
 const changed=(projectChanged=true)=>{revision++;for(const f of subscribers)f();render();if(projectChanged)window.dispatchEvent(new Event('dango-skeleton-changed'));};
 const live=value=>{skeleton=validateDocument(value);};
 function syncMembership(){
  const map=new Map(skeleton.layers.map(l=>[l.id,l.groupId])),active=controller?.inspect().state.activeGroup||null;
  let ordered=layerOrder();const created=ordered.filter(l=>!map.has(String(l.id)));
  if(active&&created.length){ordered=ordered.filter(l=>map.has(String(l.id)));const at=ordered.findLastIndex(l=>map.get(String(l.id))===active);ordered.splice(at<0?ordered.length:at+1,0,...created);ordered.forEach((l,i)=>{l.order=i+1;});}
  const next=copy(skeleton);next.layers=ordered.map(l=>({id:String(l.id),groupId:map.has(String(l.id))?map.get(String(l.id)):active}));live(next);
 }
 // Decorate actual miniPaint actions once. Image edits, groups, geometry, parameters
 // and name are restored together by miniPaint's existing undo/redo stack.
 const perform=State.do_action.bind(State);
 State.do_action=async(action,options)=>{
  if(activeActions||multiSelection?.pending||layerOperations?.isPreparing()){notify.error('正在完成操作，请稍候。');return {status:'aborted'};}
  const before=copy(skeleton),beforeName=name,beforeOrders=config.layers.map(l=>({id:l.id,order:l.order})),doAction=action.do.bind(action),undoAction=action.undo.bind(action);let after,afterName,afterOrders;
  const restoreOrders=orders=>{for(const entry of orders){const layer=config.layers.find(l=>l.id===entry.id);if(layer)layer.order=entry.order;}};
  action.skeletonBefore=before;
  action.do=async()=>{
   await doAction();
   try{if(after){restoreOrders(afterOrders);live(after);name=afterName;}else{syncMembership();after=copy(skeleton);afterName=name;afterOrders=config.layers.map(l=>({id:l.id,order:l.order}));action.skeletonAfter=after;}}
   catch(error){await undoAction();restoreOrders(beforeOrders);live(before);name=beforeName;throw error;}
   changed(action.action_id!=='wear_parameters');GUI.GUI_layers.render_layers();
  };
  action.undo=async()=>{await undoAction();restoreOrders(beforeOrders);live(before);name=beforeName;changed(action.action_id!=='wear_parameters');GUI.GUI_layers.render_layers();};
  activeActions++;
  try{const result=await perform(action,options);if(result?.status==='aborted')await action.free();return result;}finally{activeActions--;render();}
 };
 const history={get document(){return skeleton;},get revision(){return revision;},get past(){return {length:State.action_history_index};},get future(){return {length:State.action_history.length-State.action_history_index};},
  subscribe(fn){subscribers.add(fn);return ()=>subscribers.delete(fn);},
  commit(next,meta={},expected=revision){
   if(expected!==revision||activeActions)throw Error('工程修订已变化，请重新操作。');next=validateDocument(next);if(JSON.stringify(next)===JSON.stringify(skeleton))return false;
   const after=copy(next),beforeSession=copy(meta.before||controller.inspect().state);lastAction=State.do_action({action_id:'skeleton',before:copy(skeleton),after,async do(){live(after);},async undo(){},async free(){}}).then(result=>{if(result?.status==='aborted'){controller.restoreSession(beforeSession);notify.error('操作未完成，草稿已保留。');}});return true;
  },undo(){State.undo();return null;},redo(){State.redo();return null;}};
 const canvasView=installCanvasView({GUI,Layers,config,onRender:render,canZoom:()=>!controller?.inspect().pending});
 function zoom(e){canvasView.zoom(e);}
 controller=installSkeletonInput({surface,history,onChange:value=>{frame=value;render();},onPan:canvasView.pan,onZoom:zoom});
 controller.setEnabled(false);
 function activate(tool){mode=tool;controller.setEnabled(true);controller.setTool(tool);window.dispatchEvent(new CustomEvent('dango-editor-mode',{detail:'skeleton'}));window.dispatchEvent(new CustomEvent('dango-editor-inspect',{detail:'skeleton'}));surface.focus({preventScroll:true});render();}
 window.addEventListener('dango-editor-mode',e=>{if(e.detail!=='skeleton'){mode='';controller.setEnabled(false);render();}});
 window.addEventListener('dango-inspector-view',e=>{inspectorSkeletonVisible=e.detail?.page==='skeleton'&&e.detail?.expanded===true;render();});
 $('geometry_modify').onclick=()=>activate('modify');$('geometry_draw').onclick=()=>activate('draw');
 for(const id of ['geometry_modify','geometry_draw'])$(id).setAttribute('aria-label',$(id).title);
 $('geometry_modify').querySelector('path').setAttribute('d','M3 15l7-7M1 13l4 4M8 6l4 4M15 2v8m-2-6 2-2 2 2m-4 4 2 2 2-2');
 document.querySelector('.image-options>summary').title='图像调整 Shift+C';
 const toolIcons={select:['选择','V','M4 3v13l4-4 4 5 3-2-4-5 5-1Z'],brush:['画笔','B','m5 14 9-10 3 3-10 9-4 1Z'],erase:['橡皮','E','m3 12 8-9 6 6-8 8H6Z'],magic_erase:['魔棒','W','m4 16 9-9m-9-3 1-2m10 1 2 2m-3 11 2 1M8 3l1-2'],fill:['填充','G','m3 9 7-7 7 7-7 7Z M4 9h12m0 6 1 3h2l-1-3'],text:['文字','T','M3 4h14M10 4v13M7 17h6'],crop:['裁剪','C','M5 2v13h13M2 5h13v13']};
 for(const [id,[label,key,path]]of Object.entries(toolIcons)){const b=$(id);if(b){b.innerHTML=icon(path)+`<span>${label}</span><kbd>${key}</kbd>`;b.title=`${label} ${key}`;b.setAttribute('aria-label',b.title);b.addEventListener('keydown',e=>{if(!e.isComposing&&(e.key==='Enter'||e.key===' ')){e.preventDefault();e.stopImmediatePropagation();b.click();}},true);}}
 const viewTools=document.createElement('div');viewTools.id='view_tools';$('skeleton_tools').after(viewTools);
 for(const [id,label,key,path]of [['pan','平移','H','M6 11V5a1 1 0 0 1 2 0v5-7a1 1 0 0 1 2 0v7-6a1 1 0 0 1 2 0v6-4a1 1 0 0 1 2 0v7l-2 5H7l-4-6 2-1 2 3'],['zoom','缩放','Z','M14 14l4 4M15 9a6 6 0 1 1-12 0 6 6 0 0 1 12 0']]){
  const b=document.createElement('button');b.id='image_'+id;b.innerHTML=icon(path)+`<span>${label}</span><kbd>${key}</kbd>`;b.title=`${label} ${key}`;b.setAttribute('aria-label',b.title);b.onclick=()=>{window.dispatchEvent(new CustomEvent('dango-editor-mode',{detail:id}));mode=id;controller.setEnabled(id==='pan');if(id==='pan')controller.setTool('pan');surface.focus({preventScroll:true});render();};viewTools.append(b);
 }
 surface.addEventListener('pointerdown',e=>{if(mode==='zoom'&&e.button===0){e.preventDefault();e.stopImmediatePropagation();zoom({clientX:e.clientX,clientY:e.clientY,deltaY:e.shiftKey?1:-1});}},{capture:true});
 $('tools_container').addEventListener('click',()=>{mode='';controller.setEnabled(false);render();});
 function render(){
  if(!controller||painting)return;painting=true;
  try{
   const canvas=$('canvas_minipaint');surface.setAttribute('width',canvas.width);surface.setAttribute('height',canvas.height);surface.style.width=canvas.style.width||canvas.width+'px';surface.style.height=canvas.style.height||canvas.height+'px';
   const origin=zoomView.toWorld(0,0),scale=zoomView.getScale()||1;surface.setAttribute('viewBox',`${origin.x} ${origin.y} ${canvas.width/scale} ${canvas.height/scale}`);surface.style.pointerEvents=mode?'auto':'none';surface.style.visibility=mode||inspectorSkeletonVisible?'visible':'hidden';
   const doc=frame?.document||skeleton,selected=frame?.state.selected||[],active=frame?.state.activeGroup,parts=[];
   document.querySelector('.unified-editor').dataset.skeletonActive=String(!!mode);document.querySelector('.submenu').hidden=!!mode;
   for(const g of doc.groups)parts.push(`<polyline class="skeleton-bone ${g.id===active?'active':''}" points="${g.nodes.map(n=>`${n.x},${n.y}`).join(' ')}"/>`);
   const points=[{n:doc.pivot,label:'P',root:false,pivot:true},...doc.groups.flatMap(g=>g.nodes.map((n,i)=>({n,label:`${g.name} ${i?'J'+i:'R'}`,root:i===0,attached:i===0&&g.targetId})))];
   points.sort((a,b)=>Number(selected.includes(a.n.id))-Number(selected.includes(b.n.id)));
   for(const {n,label,root,pivot,attached} of points){
    const q=zoomView.toScreen(n),r=(root||pivot?8:5)/scale,cls=`skeleton-node ${selected.includes(n.id)?'selected':''}`;
    parts.push(pivot?`<rect class="${cls}" x="${n.x-r}" y="${n.y-r}" width="${r*2}" height="${r*2}"/>`:root?`<path class="${cls}" d="M${n.x} ${n.y-r}l${r} ${r}-${r} ${r}-${r}-${r}Z"/>`:`<circle class="${cls}" cx="${n.x}" cy="${n.y}" r="${r}"/>`);
    parts.push(`<text class="skeleton-label" font-size="${12/scale}" x="${n.x+(pivot?-20:12)/scale}" y="${n.y+(pivot?30:attached?24:-12)/scale}">${escape(label)}</text>`);
   }
   const gesture=frame?.gesture;
   if(gesture?.candidate){const n=locate(doc,gesture.candidate.id).node;parts.push(`<circle class="skeleton-candidate" cx="${n.x}" cy="${n.y}" r="${16/scale}"/>`);}
   if(gesture?.ghost){const n=gesture.ghost;parts.push(`<circle class="skeleton-ghost" cx="${n.x}" cy="${n.y}" r="${9/scale}"/>`);}
   if(gesture?.marquee){const b=gesture.marquee;parts.push(`<rect class="skeleton-marquee" x="${b.x}" y="${b.y}" width="${b.width}" height="${b.height}"/>`);}
   surface.innerHTML=`<g pointer-events="none">${['modify','draw'].includes(mode)||inspectorSkeletonVisible?parts.join(''):''}</g>`;
   $('geometry_status').textContent=requiresRebind()?'有节点超出画布，请调整或撤销。':mode==='pan'?'拖动画布；只改变查看位置。':mode==='zoom'?'点击放大 · Shift+点击缩小。':mode?(frame?.message||'选择骨架节点。'):'';
   for(const t of ['modify','draw'])$('geometry_'+t).setAttribute('aria-pressed',String(mode&&frame?.state.tool===t));
   for(const t of ['pan','zoom'])$('image_'+t)?.setAttribute('aria-pressed',String(mode===t));
   if(document.activeElement!==$('accessory_name'))$('accessory_name').value=name;
   $('undo_button').disabled=!State.can_undo();$('redo_button').disabled=!State.can_redo();
   renderProperties();renderTree();renderMenu();window.dispatchEvent(new Event('dango-editor-view'));
  }finally{painting=false;}
 }
 function renderProperties(){
  const id=frame?.state.activeGroup,g=skeleton.groups.find(g=>g.id===id);$('group_empty').hidden=!!g;$('group_fields').hidden=!g;
  for(const id of ['assign_group','group_up','group_down'])if($(id))$(id).disabled=!g;
  if(!g){$('group_heading').textContent='';return;}$('group_heading').textContent=g.name;const target=g.targetId?locate(skeleton,g.targetId):null;$('root_source').textContent=target?`连接 ${target.group.name} ${target.index?'J'+target.index:'R'}`:'自由根 · 相对 P';
  for(const input of document.querySelectorAll('[data-skeleton-param]'))if(document.activeElement!==input){const factor=input.dataset.skeletonParam==='elasticity'?100:1;input.value=g.parameters[input.dataset.skeletonParam]*factor;}
  $('fine_summary').textContent=`阻尼 ${g.parameters.damping} · 恢复 ${g.parameters.restoration}`;
 }
 const originalLayers=GUI.GUI_layers.render_layers.bind(GUI.GUI_layers);
 function decorateLayers(){
  const groups=new Map(skeleton.groups.map(g=>[g.id,g])),blocks=new Map(),list=$('layers');if(!list)return;
  function groupBlock(g){
   const block=document.createElement('section');block.className='layer-group';block.dataset.layerGroup=g.id;block.setAttribute('role','group');block.setAttribute('aria-label',`图层组 ${g.name}`);
   const members=document.createElement('div');members.className='layer-group-children';members.id='layer-group-children-'+g.id;members.hidden=collapsedGroups.has(g.id);
   block.append(groupRow(g,skeleton.layers.filter(l=>l.groupId===g.id).length),members);blocks.set(g.id,block);return block;
  }
  for(const row of [...list.querySelectorAll(':scope > .item')]){
   const button=row.querySelector('#layer_name'),id=button?.dataset.id;if(!id)continue;row.dataset.imageLayer=id;row.draggable=true;const gid=skeleton.layers.find(l=>l.id===id)?.groupId;
   row.dataset.layerGroupName=gid?groups.get(gid).name:'';row.title=gid?`属于图层组：${groups.get(gid).name}`:'未分组图层';button.title=gid?`${groups.get(gid).name} / ${button.textContent}`:`未分组 / ${button.textContent}`;
   if(gid){if(!blocks.has(gid))list.insertBefore(groupBlock(groups.get(gid)),row);blocks.get(gid).querySelector('.layer-group-children').append(row);}
   row.classList.toggle('grouped-layer',!!gid);const check=document.createElement('input');check.type='checkbox';check.checked=layerSelection.has(id);check.dataset.layerSelect=id;check.setAttribute('aria-label',`选择图层 ${button.textContent}`);row.prepend(check);
   row.append(row.querySelector('.delete'));
  }
  for(const unit of layerUnits(skeleton)){if(unit.groupId&&!blocks.has(unit.groupId))groupBlock(groups.get(unit.groupId));const node=unit.groupId?blocks.get(unit.groupId):list.querySelector(`[data-image-layer="${unit.layers[0].id}"]`);if(node)list.append(node);}window.dispatchEvent(new Event('dango-editor-tree'));
 }
 function groupRow(g,count){
  const row=document.createElement('div');row.className='skeleton-group-row';row.dataset.groupTarget=g.id;row.draggable=true;
  const toggle=document.createElement('button');toggle.type='button';toggle.className='group-toggle';toggle.dataset.groupToggle=g.id;toggle.setAttribute('aria-controls','layer-group-children-'+g.id);
  function updateToggle(){const collapsed=collapsedGroups.has(g.id);toggle.setAttribute('aria-expanded',String(!collapsed));toggle.title=`${collapsed?'展开':'收起'}图层组 ${g.name}`;toggle.setAttribute('aria-label',toggle.title);toggle.innerHTML=icon(collapsed?'m8 5 5 5-5 5':'m5 8 5 5 5-5');}
  updateToggle();toggle.onclick=e=>{e.stopPropagation();collapsedGroups.has(g.id)?collapsedGroups.delete(g.id):collapsedGroups.add(g.id);row.parentElement.querySelector('.layer-group-children').hidden=collapsedGroups.has(g.id);updateToggle();};
  const b=document.createElement('button');b.className='group-select';b.dataset.groupId=g.id;b.dataset.groupName=g.name;b.innerHTML=icon('M2 5h6l2 2h8v10H2Z M2 5V3h6l2 2h8v2')+`<span class="group-title">${escape(g.name)}</span><span class="group-count">${count}层</span>`;b.title=`图层组 ${g.name}，${count} 个成员`;b.setAttribute('aria-label',b.title);b.setAttribute('aria-pressed',String(frame?.state.activeGroup===g.id));b.onclick=()=>{clipboardGroup=g.id;clipboardLayerSelected=false;controller.setActiveGroup(g.id);render();window.dispatchEvent(new CustomEvent('dango-editor-inspect',{detail:'layers'}));};
  const remove=document.createElement('button');remove.type='button';remove.className='group-delete';remove.dataset.deleteGroup=g.id;remove.dataset.groupName=g.name;remove.onclick=()=>run(doc=>deleteGroup(doc,g.id));
  row.append(toggle,b,remove);return row;
 }
 GUI.GUI_layers.render_layers=()=>{const scroll=$('layers')?.scrollTop||0;originalLayers();decorateLayers();$('layers').scrollTop=scroll;};
 function renderTree(){const key=JSON.stringify([skeleton.groups.map(g=>[g.id,g.name]),skeleton.layers,config.layer?.id,[...layerSelection],frame?.state.activeGroup]);if(key===treeKey)return;treeKey=key;GUI.GUI_layers.render_layers();}
 const getLayerSelection=()=>{const valid=new Set(config.layers.map(l=>String(l.id)));layerSelection=new Set([...layerSelection].filter(id=>valid.has(id)));return layerSelection.size?[...layerSelection]:config.layer?[String(config.layer.id)]:[];};
 const setLayerSelection=ids=>{layerSelection=new Set(ids.map(String));treeKey='';};
 layerOperations=installLayerOperations({config,State,Layers,GUI,getSkeleton:()=>copy(skeleton),getRevision:()=>revision,setSkeleton:live,getSelection:getLayerSelection,setSelection:setLayerSelection,ensureSettled,onError:message=>notify.error(message),onRender:render});
 multiSelection=installMultiLayerSelection({config,Layers,GUI,zoomView,operations:layerOperations,getSelection:getLayerSelection,setSelection:ids=>{setLayerSelection(ids);const target=config.layers.find(l=>String(l.id)===ids[0]);if(target)config.layer=target;render();},isImageMode:()=>!mode,ensureSettled,onRender:render,onError:message=>notify.error(message),onRename:id=>listRename.open({kind:'layer',id})});
 $('layers_base').addEventListener('click',e=>{
  const check=e.target.closest('[data-layer-select]');if(check){if(multiSelection.pending){e.preventDefault();e.stopImmediatePropagation();return;}e.stopImmediatePropagation();const id=check.dataset.layerSelect;if(check.checked)layerSelection.add(id);else layerSelection.delete(id);render();return;}
  const b=e.target.closest('#layer_name');if(b){if(multiSelection.pending){e.preventDefault();e.stopImmediatePropagation();return;}clipboardGroup=null;clipboardLayerSelected=true;if(e.shiftKey||e.ctrlKey||e.metaKey){e.preventDefault();e.stopImmediatePropagation();layerSelection.has(b.dataset.id)?layerSelection.delete(b.dataset.id):layerSelection.add(b.dataset.id);}else layerSelection=new Set([b.dataset.id]);const gid=skeleton.layers.find(l=>l.id===b.dataset.id)?.groupId;controller.setActiveGroup(gid||null);render();window.dispatchEvent(new CustomEvent('dango-editor-inspect',{detail:'layers'}));}
 },true);
 function run(fn,options={}){try{ensureSettled(options);history.commit(fn(copy(skeleton)));}catch(e){notify.error(e.message);}}
 $('layers_base').addEventListener('dblclick',e=>{if(e.target.closest('[data-group-toggle]')){e.preventDefault();e.stopImmediatePropagation();}},true);
 const listRename=installListRename({container:$('layers_base'),onError:message=>notify.error(message),
  resolve(target){ensureSettled();const item=target.kind==='group'?skeleton.groups.find(g=>g.id===target.id):config.layers.find(l=>String(l.id)===String(target.id));if(!item)throw Error('该图层或图层组已不存在。');return {...target,id:String(target.id),name:item.name,revision};},
  async commit(target,value){ensureSettled();if(target.revision!==revision)throw Error('工程修订已变化，请取消后重新命名。');
   if(target.kind==='group'){const next=copy(skeleton),group=next.groups.find(g=>g.id===target.id);if(!group)throw Error('该图层组已不存在。');group.name=value;history.commit(validateDocument(next),{},target.revision);await lastAction;if(skeleton.groups.find(g=>g.id===target.id)?.name!==value)throw Error('操作未完成，名称草稿已保留。');}
   else{const layer=config.layers.find(l=>String(l.id)===target.id);if(!layer)throw Error('该图层已不存在。');const nextName=GUI.GUI_layers.Layer_rename.validate_name(value);if(nextName===layer.name)return;const result=await State.do_action(new app.Actions.Bundle_action('rename_layer','Rename Layer',[new app.Actions.Refresh_layers_gui_action('undo'),new app.Actions.Update_layer_action(layer.id,{name:nextName}),new app.Actions.Refresh_layers_gui_action('do')]));if(result?.status==='aborted')throw Error('操作未完成，名称草稿已保留。');}
  }});
 // Supersede miniPaint's native rename entry too, so its bubbling dblclick and
 // any existing rename command can never create a second English popup.
 GUI.GUI_layers.Layer_rename.rename=id=>listRename.open({kind:'layer',id:String(id??config.layer.id)});
 $('new_group').onclick=()=>{clipboardLayerSelected=false;controller.newGroup();clipboardGroup=controller.inspect().state.activeGroup;};
 $('assign_group').onclick=()=>run(doc=>groupLayers(doc,[...(layerSelection.size?layerSelection:[String(config.layer.id)])],frame.state.activeGroup));
 function orderAction(next){const beforeLayers=config.layers,afterLayers=next.layers.map((l,index)=>({...config.layers.find(layer=>String(layer.id)===l.id),order:index+1})),selected=config.layer.id;State.do_action({action_id:'group_layer_order',beforeLayers,afterLayers,async do(){config.layers=afterLayers;config.layer=afterLayers.find(l=>l.id===selected)||afterLayers[0];live(next);config.need_render=true;},async undo(){config.layers=beforeLayers;config.layer=beforeLayers.find(l=>l.id===selected)||beforeLayers[0];config.need_render=true;},async free(){}});}
 $('ungroup_layer').title='脱组并移到原组相邻边界，可撤销';$('ungroup_layer').onclick=()=>{try{ensureSettled();orderAction(detachLayers(skeleton,[...(layerSelection.size?layerSelection:[String(config.layer.id)])]));}catch(e){notify.error(e.message);}};
 for(const [id,label,direction]of [['group_up','组上移',1],['group_down','组下移',-1]]){const b=document.createElement('button');b.id=id;b.textContent=label;b.onclick=()=>{try{ensureSettled();orderAction(reorderGroup(skeleton,frame.state.activeGroup,direction));}catch(e){notify.error(e.message);}};document.querySelector('.group-actions > div').append(b);}
 installLayerDrag({container:$('layers_base'),getDocument:()=>copy(skeleton),getRevision:()=>revision,ensureSettled,apply:orderAction,onError:message=>notify.error(message)});
 $('accessory_name').addEventListener('change',e=>{const value=e.target.value.trim();if(!value||value.length>128||/[\u0000-\u001f\u007f-\u009f]/.test(value)){notify.error('名称须为 1–128 个合法字符。');return;}State.do_action({action_id:'accessory_name',async do(){name=value;},async undo(){},async free(){}});});
 for(const input of document.querySelectorAll('[data-skeleton-param]')){
  input.addEventListener('input',()=>{
   if(activeActions){notify.error('正在完成操作，请稍候。');return;}
   if(input.value.trim()===''||!input.checkValidity()){notify.error('请输入合法范围内的数值。');return;}
   const g=skeleton.groups.find(g=>g.id===frame.state.activeGroup);if(!g)return;
   if(!parameterDraft)parameterDraft={before:copy(skeleton),revision};
   try{const next=copy(skeleton);next.groups.find(x=>x.id===g.id).parameters[input.dataset.skeletonParam]=Number(input.value)/(input.dataset.skeletonParam==='elasticity'?100:1);live(next);render();}catch(e){notify.error(e.message);}
  });
  input.addEventListener('change',()=>{if(input.value.trim()===''||!input.checkValidity()){notify.error('请输入合法范围内的数值。');return;}if(!parameterDraft)return;const next=copy(skeleton),draft=parameterDraft;parameterDraft=null;live(draft.before);try{history.commit(next,{},draft.revision);}catch(e){notify.error(e.message);render();}});
 }
 window.addEventListener('blur',()=>{if(parameterDraft){live(parameterDraft.before);parameterDraft=null;}if(temporaryPan!==null){mode=temporaryPan.mode;temporaryPan=null;controller.setTemporaryPan(false);controller.setEnabled(!!mode&&mode!=='zoom');}render();});
 function renderMenu(){
  const c=frame?.context;menu.hidden=!c;if(!c)return;menu.replaceChildren();const t=c.target,s=t?.kind==='node'?locate(skeleton,t.id):null;
  const target=document.createElement('div');target.className='menu-target';target.textContent=s?(s.group?`${s.group.name} ${s.index?'J'+s.index:'R'}`:'配饰根 P'):'骨段';menu.append(target);
  const actions=t?.kind==='segment'?[['insert','插入骨节']]:s?.group?[['create','从此处创建图层组'],['select-chain','选中所在骨架'],...(s.index===0?[['attach','挂接骨架根'],...(s.group.targetId?[['detach','解除挂接']]:[]),['clear','清空骨节，保留根']]:[['delete',frame.state.selected.includes(s.node.id)?'删除所选节点':'删除此骨节']])]:[['create','创建图层组'],['select-all','选中全部骨架']];
  for(const [action,label] of actions){const b=document.createElement('button');b.setAttribute('role','menuitem');b.dataset.skeletonAction=action;b.innerHTML=icon(action==='delete'||action==='clear'?'M4 6h12M7 6v11h6V6M8 3h4':action==='select-chain'||action==='select-all'?'M3 3h14v14H3Z':'M3 10h14M10 3v14')+escape(label);b.onclick=()=>controller.command(action);menu.append(b);}
  menu.style.left=Math.max(4,Math.min(c.x,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(4,Math.min(c.y,innerHeight-menu.offsetHeight-8))+'px';
 }
 function ensureSettled(){
  if(temporaryPan!==null)throw Error('请先松开空格，结束临时平移。');
  if(multiSelection?.pending)throw Error('请先完成或取消多图层变换。');
  if(layerOperations?.isPreparing())throw Error('正在准备合并图层，请稍候。');
  if($('accessory_name').value!==name||[...document.querySelectorAll('[data-skeleton-param]')].some(i=>!i.closest('[hidden]')&&(i.value.trim()===''||!i.checkValidity())))throw Error('请先确认或修正当前名称／数值输入。');
  if(activeActions||parameterDraft||controller.inspect().pending||Object.values(GUI.GUI_tools.tools_modules).some(t=>t.object.focused||t.object.strokePending||t.object.event_links?.length||t.object.tmpCanvas||t.object.is_mousedown_canvas&&config.mouse.is_drag)||document.querySelector('.text-input'))throw Error('请先结束画布或参数操作，再试一次。');
 }
 function requiresRebind(){return allNodes(skeleton).some(p=>p.x<0||p.x>config.WIDTH||p.y<0||p.y>config.HEIGHT);}
 const transformCanvas=installCanvasOperations({getSkeleton:()=>copy(skeleton),setLiveSkeleton:live,ensureReady,ensureSettled,checkSize,beginPreparation(){activeActions++;let held=true;return ()=>{if(held){held=false;activeActions--;render();}};}});
 const expand=document.createElement('button');expand.id='canvas_expand';expand.innerHTML=icon('M3 8V3h5m4 0h5v5m0 4v5h-5M8 17H3v-5')+'画布扩展';document.querySelector('.image-options>div').prepend(expand);
 const dialog=document.createElement('dialog');dialog.id='canvas_expand_dialog';dialog.innerHTML='<form><strong>透明画布扩展</strong><label>宽度 <input name="width" type="number" min="1" max="4096" required></label><label>高度 <input name="height" type="number" min="1" max="4096" required></label><p role="status"></p><div><button type="button" data-cancel>取消</button><button type="submit">确认扩展</button></div></form>';document.body.append(dialog);
 expand.onclick=()=>{dialog.querySelector('[name=width]').value=config.WIDTH;dialog.querySelector('[name=height]').value=config.HEIGHT;dialog.querySelector('[role=status]').textContent='在四周补充透明区域。';dialog.showModal();};
 dialog.querySelector('[data-cancel]').onclick=()=>dialog.close();dialog.querySelector('form').onsubmit=async e=>{e.preventDefault();const width=Number(dialog.querySelector('[name=width]').value),height=Number(dialog.querySelector('[name=height]').value);try{await transformCanvas('expand',{width,height,x:-Math.floor((width-config.WIDTH)/2),y:-Math.floor((height-config.HEIGHT)/2)});dialog.close();}catch(error){dialog.querySelector('[role=status]').textContent=error.message;}};
 let expandComposing=false;
 dialog.addEventListener('compositionstart',()=>{expandComposing=true;});dialog.addEventListener('compositionend',()=>{expandComposing=false;});
 dialog.addEventListener('keydown',e=>{e.stopPropagation();if(e.key==='Enter'&&(expandComposing||e.isComposing||e.keyCode===229||e.repeat))e.preventDefault();if(e.key==='Escape'){e.preventDefault();dialog.close();}});
 menu.addEventListener('keydown',e=>{e.stopPropagation();if(e.key==='Escape'){e.preventDefault();controller.closeContext();surface.focus({preventScroll:true});}});
 document.querySelector('[data-skeleton-scope]').addEventListener('pointerdown',e=>{if(!menu.hidden&&e.target!==surface&&!menu.contains(e.target))controller.closeContext();});
 for(const [id,kind]of [['flip_x','flipX'],['flip_y','flipY'],['rotate','left']])$(id).onclick=()=>transformCanvas(kind).catch(e=>notify.error(e.message));
 const cropTool=GUI.GUI_tools.tools_modules.crop.object;cropTool.on_params_update=async()=>{try{const s=cropTool.selection;await transformCanvas('crop',{x:Math.round(s.x),y:Math.round(s.y),width:Math.round(s.width),height:Math.round(s.height)});cropTool.Base_selection.reset_selection();}catch(e){notify.error(e.message);}};
 installCanvasConfirm({config,cropTool,isCanvasMode:()=>!mode,onError:message=>notify.error(message)});
 const paint=Layers.render.bind(Layers);Layers.render=force=>{const result=paint(force);if(force)render();return result;};new ResizeObserver(render).observe($('canvas_minipaint'));
 document.addEventListener('keydown',e=>{
  if(e.isComposing||e.target.closest('input,textarea,select,[contenteditable=true],[role=menu],[role=dialog]'))return;
  const key=e.key.toLowerCase(),ctrl=e.ctrlKey||e.metaKey;
  if(e.key===' '&&e.target.closest('button,[role=button]'))return;
  if(e.key===' '&&!ctrl){if(temporaryPan===null){try{ensureSettled();temporaryPan={mode};mode='pan';controller.setTemporaryPan(true);controller.setEnabled(true);render();}catch{}}e.preventDefault();e.stopImmediatePropagation();return;}
  if(ctrl&&(key==='z'||key==='y')){e.preventDefault();e.stopImmediatePropagation();if(controller.inspect().pending)controller.cancel();key==='y'||e.shiftKey?State.redo():State.undo();return;}
  if(!ctrl&&!e.altKey&&(key==='k'||key==='p')){e.preventDefault();e.stopImmediatePropagation();activate(key==='k'&&e.shiftKey?'draw':'modify');if(key==='p')controller.selectPivot();return;}
  if(!ctrl&&!e.altKey&&key==='c'&&e.shiftKey){e.preventDefault();e.stopImmediatePropagation();const options=document.querySelector('.image-options');options.open=!options.open;return;}
  const tool={v:'select',b:'brush',e:e.shiftKey?'restore_pixels':'erase',w:'magic_erase',g:'fill',t:'text',c:'crop',h:'image_pan',z:'image_zoom'}[key];
  if(tool&&!ctrl&&!e.altKey){e.preventDefault();e.stopImmediatePropagation();$(tool)?.click();}
 },true);
 document.addEventListener('keyup',e=>{if(e.key===' '&&temporaryPan!==null){mode=temporaryPan.mode;temporaryPan=null;controller.setTemporaryPan(false);controller.setEnabled(!!mode&&mode!=='zoom');render();}},true);
 for(const input of document.querySelectorAll('#accessory_name,[type=number][data-skeleton-param]'))input.addEventListener('keydown',e=>{if(e.key==='Enter'&&!e.isComposing){e.preventDefault();e.stopPropagation();input.dispatchEvent(new Event('change',{bubbles:true}));}});
 for(const method of ['undo_action','redo_action']){const fn=State[method].bind(State);State[method]=async()=>{if(activeActions||parameterDraft||layerOperations?.isPreparing()){notify.error('请先结束当前操作。');return;}multiSelection?.cancel();if(controller.inspect().pending)controller.cancel();const index=State.action_history_index;activeActions++;try{await fn();}catch(error){State.action_history_index=index;notify.error('撤销或重做未完成，当前草稿仍保留。');}finally{activeActions--;render();}};}
 State.undo=()=>State.undo_action();State.redo=()=>State.redo_action();
 render();
 return {getSkeleton:()=>copy(skeleton),getEditSequence:()=>revision,getName:()=>name,setName:value=>{name=value;render();},
  getClipboardTarget:()=>{const id=!clipboardLayerSelected&&mode&&mode!=='pan'&&mode!=='zoom'?frame?.state.activeGroup:clipboardGroup;return id&&skeleton.groups.some(g=>g.id===id)?{kind:'group',id}:{kind:'layer',id:config.layer.id};},
  async insertClipboard(layers,next){
   ensureReady();ensureSettled();next=validateDocument(next);
   const before=config.layers,selected=config.layer,increment=Layers.auto_increment,after=[...before,...layers],newGroup=next.groups.find(g=>!skeleton.groups.some(old=>old.id===g.id))?.id||null;
   const result=await State.do_action({action_id:'paste_editable',action_description:'粘贴图层',layers,
    async do(){config.layers=after;config.layer=layers.at(-1)||selected;Layers.auto_increment=Math.max(increment,...after.map(l=>l.id+1));live(next);config.need_render=true;},
    async undo(){config.layers=before;config.layer=selected;Layers.auto_increment=increment;config.need_render=true;},async free(){this.layers=null;}});
   if(result?.status!=='aborted'){clipboardGroup=newGroup;clipboardLayerSelected=!newGroup;controller.setActiveGroup(newGroup||next.layers.find(l=>l.id===String(config.layer.id))?.groupId||null);render();}return result;
  },
  layout:{getMode:()=>mode,getMessage:()=>requiresRebind()?'有节点超出画布，请调整或撤销。':frame?.gesture||frame?.context?frame?.message:multiSelection?.message||'',closeContext:()=>controller.closeContext()},
  async setSkeleton(value,options={}){ensureReady();if(activeActions||parameterDraft)throw Error('请先结束当前操作。');const next=validateDocument(value);if(options.reset){controller.cancel();live(next);changed();}else{history.commit(next);await lastAction;}render();},
  importState(value,importedName){clipboardGroup=null;clipboardLayerSelected=false;controller.cancel();live(value);name=importedName;changed();controller.setActiveGroup(skeleton.layers.find(l=>l.id===String(config.layer.id))?.groupId||null);render();},
  resetGeometry(){clipboardGroup=null;clipboardLayerSelected=false;controller.cancel();mode='';controller.setEnabled(false);live(empty());layerSelection.clear();changed();},
  getGeometry(){const g=skeleton.groups[0];return {pivotX:skeleton.pivot.x/config.WIDTH,pivotY:skeleton.pivot.y/config.HEIGHT,nodes:g?g.nodes.map(n=>({x:n.x/config.WIDTH,y:n.y/config.HEIGHT})):[],requiresRebind:requiresRebind()};},
  async setGeometry(value,options={}){const doc=empty();doc.pivot={id:'P',x:value.pivotX*config.WIDTH,y:value.pivotY*config.HEIGHT};if(value.nodes.length)doc.groups.push({id:'legacy-chain',name:'骨架',targetId:null,nodes:value.nodes.map((n,i)=>({id:i?'legacy-'+i:'legacy-R',x:n.x*config.WIDTH,y:n.y*config.HEIGHT}))});if(options.reset){controller.cancel();live(doc);changed();render();}else history.commit(doc);},
  transformCanvas,ensureSettled,requiresRebind,track:async fn=>{activeActions++;try{return await fn();}finally{activeActions--;}}};
}

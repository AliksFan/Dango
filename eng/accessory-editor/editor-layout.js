// Layout and view state only. miniPaint retains its tools, controls and history.
import app from './vendor/src/js/app.js';
import config from './vendor/src/js/config.js';
import {installTextToolbar} from './text-toolbar.js';
import {installDynamicPreview,installWindowDrag} from './dynamic-preview.js';

const $=id=>document.getElementById(id);
const paths={
 palette:'M10 2a8 8 0 1 0 0 16h2a2 2 0 0 0 0-4h-1a1 1 0 0 1 0-2h3a4 4 0 0 0 4-4c0-4-4-6-8-6ZM5 7h.1M8 5h.1M13 5h.1',
 layers:'m2 6 8-4 8 4-8 4Zm0 5 8 4 8-4M2 16l8 4 8-4',
 bone:'m4 15 10-10M2 13l4 4M12 3l4 4',close:'m5 5 10 10M15 5 5 15',
 right:'m7 4 6 6-6 6',left:'m13 4-6 6 6 6',plus:'M10 3v14M3 10h14',
 duplicate:'M7 7h10v10H7ZM3 13V3h10',raster:'M3 3h14v14H3ZM3 8h14M8 3v14M13 3v14M3 13h14',
 up:'m4 12 6-6 6 6',down:'m4 8 6 6 6-6',trash:'M4 6h12M7 6v11h6V6M8 3h4',
 assign:'M3 5h7l2 2h5v10H3ZM6 11h8m-3-3 3 3-3 3',detach:'M3 5h7l2 2h5v10H3Zm3 6h8m-5-3-3 3 3 3',
 undo:'m7 4-4 4 4 4M3 8h8a5 5 0 0 1 0 10',redo:'m13 4 4 4-4 4M17 8H9a5 5 0 0 0 0 10',
 minus:'M4 10h12',fit:'M7 3H3v4m10-4h4v4M3 13v4h4m10-4v4h-4',
 sliders:'M3 5h14M3 10h14M3 15h14M7 3v4m6 1v4m-5 1v4',
 flipX:'M10 2v16M7 5 2 10l5 5Zm6 0 5 5-5 5Z',flipY:'M2 10h16M5 7l5-5 5 5Zm0 6 5 5 5-5Z',
 rotate:'M4 8a6 6 0 1 1 1 8M2 3v5h5',restore:'m4 15 8-11 4 3-8 10H4Zm8 5a5 5 0 0 0 5-5m-3 1 3-1 1 3',
 eye:'M2 10s3-6 8-6 8 6 8 6-3 6-8 6-8-6-8-6Zm8-3a3 3 0 1 0 0 6 3 3 0 0 0 0-6',
 eyeClosed:'M3 8s3 5 7 5 7-5 7-5M4 11l-2 3m6-1-1 4m5-4 1 4m3-6 2 3',
 layerAdd:'M4 2h8l4 4v12H4ZM12 2v4h4M7 12h6m-3-3v6',
 groupAdd:'M2 5V3h6l2 2h8v12H2ZM7 11h6m-3-3v6',
 reset:'M4 8a6 6 0 1 1 1 8M2 3v5h5',picker:'m3 16 8-8 3 3-8 8H3Zm8-8 3-4 3 3-3 4',
 channels:'M4 3v14M10 3v14M16 3v14M2 7h4m2 6h4m2-8h4',swatches:'M3 3h6v6H3Zm8 0h6v6h-6ZM3 11h6v6H3Zm8 0h6v6h-6',
 check:'m4 10 4 4 8-9',image:'M3 3h14v14H3ZM5 13l4-5 3 3 2-2 2 4',
 circle:'M10 3a7 7 0 1 0 0 14 7 7 0 0 0 0-14',square:'M4 4h12v12H4Z',
 pressure:'m3 13 3-4 3 2 5-7 3 3M3 17h14',bold:'M6 3h5a4 4 0 0 1 0 7H6Zm0 7h6a4 4 0 0 1 0 7H6Z',
 italic:'M9 3h7M4 17h7M12 3 8 17',underline:'M5 3v7a5 5 0 0 0 10 0V3M3 18h14',strike:'M14 4c-5-4-12 1-5 5l3 2c6 4-1 8-6 4M2 10h16',crop:'M5 2v13h13M2 5h13v13'
};
const svg=key=>`<svg viewBox="0 0 20 20" aria-hidden="true"><path d="${paths[key]||paths.sliders}"/></svg>`;
function iconButton(button,label,key){
 if(!button)return;
 button.title=label;button.setAttribute('aria-label',label);button.dataset.iconOnly='true';
 if(button.dataset.editorIcon!==key||!button.querySelector('svg')){button.innerHTML=svg(key);button.dataset.editorIcon=key;}
}
const el=(tag,cls)=>{const n=document.createElement(tag);if(cls)n.className=cls;return n;};

export function installEditorLayout({getMode,getMessage,closeContext}){
 const root=document.querySelector('.unified-editor'),nav=$('main_menu'),left=document.querySelector('.sidebar_left'),rail=document.querySelector('.sidebar_right');
 const imageOptions=document.querySelector('.image-options'),oldSubmenu=document.querySelector('.submenu');
 let tab='layers',hover=null,scheduled=false,layingOut=false,simulation=false,editTab='layers';
 root.dataset.layout='canvas';
 // Move the existing, wired widgets. No second color picker or parameter model.
 const colorBody=$('toggle_colors').closest('.colors'),layerDetails=$('details_base');
 const oldLayers=$('layers_panel'),oldSkeleton=$('skeleton_panel'),newGroup=$('new_group'),groupHeading=$('group_heading');
 oldLayers.querySelector('header').remove();oldSkeleton.querySelector('header').remove();
 oldLayers.querySelector('.panel-body').classList.remove('panel-body');oldSkeleton.querySelector('.panel-body').classList.remove('panel-body');
 oldLayers.setAttribute('role','tabpanel');oldSkeleton.setAttribute('role','tabpanel');
 oldLayers.setAttribute('aria-label','图层和组');oldSkeleton.setAttribute('aria-label','骨架参数');
 const makePanel=(id,label,key)=>{
  const panel=el('section','canvas-panel');panel.id=id;panel.dataset.expanded='true';
  const header=el('header'),title=el('span');title.className='panel-title';title.textContent=label;
  const toggle=el('button','panel-toggle');toggle.type='button';toggle.dataset.panel=id;toggle.setAttribute('aria-expanded','true');iconButton(toggle,`收起${label}`,'right');
  header.append(title,toggle);const body=el('div','panel-body');panel.append(header,body);
  toggle.onclick=()=>{closeContext();const open=panel.dataset.expanded!=='true';panel.dataset.expanded=String(open);toggle.setAttribute('aria-expanded',String(open));iconButton(toggle,`${open?'收起':'展开'}${label}`,open?'right':key);body.inert=!open;if(id==='properties_panel')inspectorView();schedule();};
  rail.append(panel);return {panel,header,body};
 };
 rail.replaceChildren();const colors=makePanel('colors_panel','调色盘','palette'),properties=makePanel('properties_panel','对象','layers');
 colors.body.append(colorBody);colorBody.querySelector('h2')?.remove();
 const colorControls=$('toggle_colors').querySelector(':scope > .ui_flex_group'),colorButtons=colorControls.querySelector('.ui_button_group'),colorSample=$('selected_color_sample'),hex=$('color_hex'),hexRow=hex.parentElement;
 colors.header.querySelector('.panel-title').remove();colorButtons.classList.add('palette-tabs');
 colors.header.prepend(colorSample,hex,colorButtons);colorControls.remove();hexRow.remove();
 hex.removeAttribute('aria-labelledby');hex.setAttribute('aria-label','颜色色值');hex.title='颜色色值';
 colorSample.title='当前颜色';colorSample.setAttribute('aria-label','当前颜色，切换调色');colorSample.setAttribute('role','button');colorSample.tabIndex=0;
 colorSample.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();colorSample.click();}});
 const tabs=el('div','inspector-tabs');tabs.setAttribute('role','tablist');tabs.setAttribute('aria-label','对象属性');tabs.setAttribute('aria-orientation','vertical');
 const layerTab=el('button'),boneTab=el('button'),propertyTab=el('button');layerTab.id='layers_tab';boneTab.id='skeleton_tab';propertyTab.id='layer_properties_tab';
 oldLayers.setAttribute('aria-labelledby',layerTab.id);oldSkeleton.setAttribute('aria-labelledby',boneTab.id);
 const tabEntries=[[layerTab,'layers_panel','图层','layers','layers'],[boneTab,'skeleton_panel','骨架参数','bone','skeleton'],[propertyTab,'layer_parameters','图层参数','sliders','properties']];
 for(const [b,id,label,key,value]of tabEntries){b.type='button';b.setAttribute('role','tab');b.setAttribute('aria-controls',id);iconButton(b,label,key);b.onclick=()=>inspectTab(value);}
 tabs.append(layerTab,boneTab,propertyTab);properties.header.classList.add('inspector-rail');properties.header.prepend(tabs);properties.header.querySelector('.panel-title').remove();
 properties.body.append(oldLayers,oldSkeleton);oldLayers.className='inspector-page';oldSkeleton.className='inspector-page';
 const layerActions=el('div','layer-actions');for(const b of [...$('layers_base').children].filter(n=>n.tagName==='BUTTON'))layerActions.append(b);
 layerActions.querySelector('#insert_layer').after(newGroup);
 $('layers_base').prepend(layerActions); // Keep miniPaint's delegated layer actions in their original owner.
 const ownership=el('div','layer-ownership');ownership.id='layer_ownership';ownership.setAttribute('role','status');layerActions.after(ownership);
 const groupList=el('div','bone-group-list');groupList.id='bone_group_list';groupList.setAttribute('aria-label','选择骨架组');
 oldSkeleton.prepend(groupList);groupHeading.hidden=true;groupList.append(groupHeading);
 const groupPicker=el('select');groupPicker.id='bone_group_picker';groupPicker.setAttribute('aria-label','活动骨架组');groupList.append(groupPicker);
 groupPicker.onchange=()=>{const target=document.querySelector(`[data-group-id="${CSS.escape(groupPicker.value)}"]`);target?.click();selectTab('skeleton');};
 // Top controls and each parameter group wrap intact, retaining all labels and values.
 const documentGroup=el('div','toolbar-group');documentGroup.dataset.toolbarGroup='document';
 documentGroup.append($('accessory_name'),$('undo_button'),$('redo_button'));
 const viewGroup=el('div','toolbar-group');viewGroup.dataset.toolbarGroup='view';viewGroup.append($('toggle_preview'));
 const toolOptions=el('div','toolbar-group tool-parameters');toolOptions.id='tool_options';toolOptions.dataset.toolbarGroup='tool';toolOptions.setAttribute('role','group');toolOptions.setAttribute('aria-label','当前工具参数');toolOptions.append(oldSubmenu);
 nav.replaceChildren(documentGroup,viewGroup,toolOptions);imageOptions.classList.add('top-options');
 // Keep image operations in the left rail; move the already wired controls intact.
 $('erase').after($('restore_pixels'));$('view_tools').append($('canvas_expand'));left.append(imageOptions);
 const restoreParameter=$('restore_size').closest('label');restoreParameter.id='restore_parameter';oldSubmenu.append(restoreParameter);
 let customTool='';
 function syncToolParameters(){const active=$('restore_pixels').getAttribute('aria-pressed')==='true';root.dataset.restoreActive=String(active);root.dataset.customTool=active?'restore':customTool;restoreParameter.hidden=!active;for(const tool of document.querySelectorAll('#tools_container .item'))tool.setAttribute('aria-pressed',String(!active&&!customTool&&tool.classList.contains('active')));}
 window.addEventListener('dango-editor-mode',e=>{customTool=e.detail==='restore'?'':e.detail;syncToolParameters();});
 $('tools_container').addEventListener('click',e=>{if(e.target.closest('.item')){customTool='';syncToolParameters();}});
 new MutationObserver(syncToolParameters).observe($('tools_container'),{attributes:true,attributeFilter:['class'],subtree:true});
 new MutationObserver(()=>{syncToolParameters();schedule();}).observe($('restore_pixels'),{attributes:true,attributeFilter:['aria-pressed']});
 syncToolParameters();
 imageOptions.querySelector('summary').innerHTML=svg('image');imageOptions.querySelector('summary').setAttribute('aria-label','图像调整 Shift+C');
 const groupActions=document.querySelector('.group-actions');groupActions.open=true;
 const layerParameters=el('section','inspector-page layer-parameters-page');layerParameters.id='layer_parameters';layerParameters.setAttribute('role','tabpanel');layerParameters.setAttribute('aria-labelledby',propertyTab.id);
 layerParameters.append(layerDetails,groupActions);properties.body.append(layerParameters);
 // Color sections use the existing miniPaint controls and update path, one compact page at a time.
 const colorTabs=['toggle_color_picker_section_button','toggle_color_channels_section_button','toggle_color_swatches_section_button'].map($);
 let changingColorTab=false;
 for(const button of colorTabs)button.addEventListener('click',e=>{if(!changingColorTab&&button.getAttribute('aria-pressed')==='true'){e.stopImmediatePropagation();}},true);
 for(const button of colorTabs)button.addEventListener('click',()=>{
  if(changingColorTab)return;changingColorTab=true;
  for(const other of colorTabs)if(other!==button&&other.getAttribute('aria-pressed')==='true')other.click();
  changingColorTab=false;schedule();
 });
 changingColorTab=true;for(const b of colorTabs.slice(1))if(b.getAttribute('aria-pressed')==='true')b.click();changingColorTab=false;
 if(colorTabs[0].getAttribute('aria-pressed')!=='true')colorTabs[0].click();
 function inspectorView(){window.dispatchEvent(new CustomEvent('dango-inspector-view',{detail:{page:tab,expanded:properties.panel.dataset.expanded==='true'}}));}
 function selectTab(value){
  tab=value;for(const [b,,,,id]of tabEntries){const active=value===id;b.setAttribute('aria-selected',String(active));b.tabIndex=active?0:-1;}
  oldLayers.hidden=value!=='layers';oldSkeleton.hidden=value!=='skeleton';layerParameters.hidden=value!=='properties';properties.panel.dataset.page=value;
  inspectorView();schedule();
 }
 function inspectTab(value){if(simulation&&value!=='skeleton')return;if(properties.panel.dataset.expanded!=='true')properties.header.querySelector('.panel-toggle').click();selectTab(value);}
 tabs.addEventListener('keydown',e=>{if(['ArrowUp','ArrowDown','Home','End'].includes(e.key)){e.preventDefault();const current=tabEntries.findIndex(entry=>entry[4]===tab),index=e.key==='Home'?0:e.key==='End'?2:(current+(e.key==='ArrowDown'?1:2))%3;inspectTab(tabEntries[index][4]);tabEntries[index][0].focus();}});
 window.addEventListener('dango-editor-inspect',e=>inspectTab(e.detail));
 const icons={undo_button:['撤销 Ctrl+Z','undo'],redo_button:['重做 Ctrl+Shift+Z','redo'],zoom_less:['缩小','minus'],zoom_more:['放大','plus'],zoom_fit:['适合画布','fit'],
  new_group:['创建图层组','groupAdd'],insert_layer:['新建图层','layerAdd'],layer_duplicate:['复制图层','duplicate'],layer_raster:['光栅化图层','raster'],layer_up:['上移图层','up'],layer_down:['下移图层','down'],
  assign_group:['归入当前组','assign'],ungroup_layer:['脱组','detach'],group_up:['上移组','up'],group_down:['下移组','down'],
  flip_x:['整图水平翻转','flipX'],flip_y:['整图垂直翻转','flipY'],rotate:['整图左转','rotate'],restore_pixels:['恢复像素 Shift+E','restore'],canvas_expand:['透明画布扩展','fit'],
  toggle_color_picker_section_button:['调色','picker'],toggle_color_channels_section_button:['颜色通道','channels'],toggle_color_swatches_section_button:['色板','swatches']};
 function decorate(){
  for(const [id,[label,key]]of Object.entries(icons))iconButton($(id),label,key);
  for(const b of document.querySelectorAll('#tools_container .item,#skeleton_tools button,#view_tools button')){b.querySelectorAll('span,kbd').forEach(n=>n.hidden=true);b.dataset.iconOnly='true';}
  for(const b of document.querySelectorAll('#layers .visibility')){const visible=b.classList.contains('visible');iconButton(b,visible?'隐藏图层':'显示图层',visible?'eye':'eyeClosed');b.setAttribute('aria-pressed',String(visible));}
  for(const b of document.querySelectorAll('#layers .delete'))iconButton(b,'删除图层','trash');
  for(const b of document.querySelectorAll('#layers [data-delete-group]'))iconButton(b,`删除图层组 ${b.dataset.groupName}，保留图层`,'trash');
  for(const b of document.querySelectorAll('#toggle_details .reset'))iconButton(b,'复位','reset');
  for(const b of document.querySelectorAll('#action_attributes button:not(.increase_number):not(.decrease_number)')){
   const label=b.getAttribute('aria-label')||b.title||b.textContent.trim();
   const key=Object.entries({'圆笔':'circle','硬边':'square','压感':'pressure','粗体':'bold','斜体':'italic','下划线':'underline','删除线':'strike','裁剪':'crop'}).find(([text])=>label.includes(text))?.[1]||'check';
   if(!b.dataset.editorIcon)iconButton(b,label,key);
  }
 }
 const usage={select:'选择：点击图层内容并拖动；Shift 多选。',brush:'画笔：按住左键绘制，先在右上选择颜色。',erase:'橡皮：拖动擦除当前图层像素。',magic_erase:'魔棒：点击选择相近颜色，Delete 删除选区，Esc 取消。',fill:'填充：点击当前图层区域填色。',text:'文字：点击画布输入文字，Enter 换行，切换工具结束编辑。',crop:'裁剪：拖出裁剪框，按 Enter 或在工具参数中确认。',modify:'修改骨架：点击／框选节点后拖动；Shift 追加，右击打开节点命令。',draw:'绘制骨架：选择根或末端后点击续画；拖动已有节点转为修改。',pan:'平移：拖动画布；按住空格可临时平移。',zoom:'缩放：点击放大，Shift＋点击缩小；滚轮直接缩放。',restore:'恢复像素：拖动恢复当前图层曾被擦除的原始像素。'};
 const toolIds={select:'select',brush:'brush',erase:'erase',magic_erase:'magic_erase',fill:'fill',text:'text',crop:'crop',geometry_modify:'modify',geometry_draw:'draw',image_pan:'pan',image_zoom:'zoom',restore_pixels:'restore'};
 const help=$('geometry_status');help.setAttribute('aria-label','工具使用提示');
 function updateHelp(){
  if(simulation){const hints={default:'WASD 控制团子，空格跳跃，也可直接拖动；左侧可选挂点、平移、缩放或刷新团子。',pick:'选挂点：鼠标虚影参考位置，点击确定，Esc 取消。',pan:'平移：拖动查看模拟空间，不改变团子或配饰参数。',zoom:'缩放：点击放大，Shift＋点击缩小；滚轮缩放模拟视图。'};
   help.textContent=hover?({选挂点:hints.pick,平移:hints.pan,缩放:hints.zoom,刷新团子:'刷新团子：恢复初始位置、速度与摆动，保留配饰参数和当前视角。'}[hover]||hover):(hints[simulationTool]||hints.default);return;}
  const mode=getMode(),message=getMessage();
  const key=hover||($('restore_pixels').getAttribute('aria-pressed')==='true'?'restore':mode||config.TOOL?.name);
  help.textContent=hover?(usage[key]||hover):message||usage[key]||'选择左侧工具开始编辑。';
 }
 const helpTarget='button,summary,[role=button]';
 root.addEventListener('pointerover',e=>{const b=e.target.closest(helpTarget);if(b){hover=toolIds[b.id]||b.title||null;updateHelp();}});
 root.addEventListener('pointerout',e=>{const b=e.target.closest(helpTarget);if(b&&!b.contains(e.relatedTarget)){hover=null;updateHelp();}});
 root.addEventListener('focusin',e=>{const b=e.target.closest(helpTarget);if(b){hover=toolIds[b.id]||b.title||null;updateHelp();}});
 root.addEventListener('focusout',()=>{hover=null;updateHelp();});
 window.addEventListener('dango-editor-view',()=>{updateHelp();schedule();});
 window.addEventListener('dango-editor-mode',()=>{hover=null;updateHelp();schedule();});
 let simulationTool='default';
 window.addEventListener('dango-simulation-state',e=>{simulationTool=e.detail.tool||'default';updateHelp();});
 let currentGroupKey='';
 function treeChanged(){
  decorate();
  const groups=[...document.querySelectorAll('[data-group-id]')].map(b=>[b.dataset.groupId,b.dataset.groupName||b.textContent,b.getAttribute('aria-pressed')==='true']);
  const selected=$('layers').querySelector(`[data-image-layer="${config.layer?.id}"]`),groupName=selected?.dataset.layerGroupName;
  ownership.textContent=`${groupName?`组：${groupName}`:'未分组'} / ${config.layer?.name||''}`;ownership.title=ownership.textContent;
  const key=JSON.stringify(groups);if(key!==currentGroupKey){currentGroupKey=key;groupPicker.replaceChildren();if(!groups.length){const o=el('option');o.textContent='尚无骨架组';o.value='';groupPicker.append(o);}for(const [id,label,active]of groups){const o=el('option');o.value=id;o.textContent=label;o.selected=active;groupPicker.append(o);}groupPicker.disabled=!groups.length;}
  schedule();
 }
 window.addEventListener('dango-editor-tree',treeChanged);
 function layout(){
  if(layingOut)return;layingOut=true;
  try{
   const h=root.clientHeight,small=h<600;
   root.dataset.short=String(small);
   const top=nav.getBoundingClientRect().bottom-root.getBoundingClientRect().top+8;
   root.style.setProperty('--toolbar-bottom',top+'px');
   const available=h-top-8,paletteHeight=simulation?200:Math.min(216,Math.max(small?152:176,Math.round(available*.30)));
   colors.panel.style.height=paletteHeight+'px';properties.panel.style.top=(top+paletteHeight+8)+'px';
   // Color channels and text-layer options are taller groups. Move the entire group, not fields.
   const colorTall=$('toggle_color_channels_section_button').getAttribute('aria-pressed')==='true';
   if(colorTall)colors.panel.dataset.tall='true';else delete colors.panel.dataset.tall;
   for(const details of document.querySelectorAll('.top-options[open]')){
    const body=[...details.children].find(n=>n.tagName!=='SUMMARY');if(!body)continue;
    const r=details.getBoundingClientRect(),width=body.getBoundingClientRect().width;
    details.style.setProperty('--popover-left',(Math.max(8,Math.min(r.left,innerWidth-width-8))-r.left)+'px');
    const height=body.getBoundingClientRect().height;
    details.style.setProperty('--popover-top',(Math.max(8,Math.min(r.bottom+6,innerHeight-height-34))-r.top)+'px');
   }
   updateHelp();
  }finally{layingOut=false;}
 }
 function schedule(){if(scheduled)return;scheduled=true;requestAnimationFrame(()=>{scheduled=false;layout();});}
 const controlObserver=new MutationObserver(()=>{decorate();schedule();});controlObserver.observe($('action_attributes'),{childList:true,subtree:true});
 controlObserver.observe($('toggle_details'),{childList:true,subtree:true});
 root.addEventListener('toggle',event=>{
  const details=event.target;if(!details.matches('.top-options'))return;
  if(details.open)for(const other of document.querySelectorAll('.top-options[open]'))if(other!==details)other.open=false;schedule();
 },true);
 document.addEventListener('pointerdown',e=>{for(const d of document.querySelectorAll('.top-options[open]'))if(!d.contains(e.target))d.open=false;},true);
 document.addEventListener('keydown',e=>{if(e.key==='Escape'&&!e.isComposing){const open=document.querySelector('.top-options[open]');if(open){e.preventDefault();e.stopImmediatePropagation();open.open=false;open.querySelector('summary').focus();}}},true);
 new ResizeObserver(schedule).observe(root);new ResizeObserver(schedule).observe(nav);
 colors.body.addEventListener('click',schedule);
 window.addEventListener('dango-editor-simulation',e=>{
  simulation=e.detail===true;closeContext();hover=null;
  left.inert=simulation;document.querySelector('.middle_area').inert=simulation;toolOptions.inert=simulation;
  oldLayers.inert=simulation;layerParameters.inert=simulation;
  if(simulation){editTab=tab;for(const d of document.querySelectorAll('.top-options[open]'))d.open=false;selectTab('skeleton');}
  else selectTab(editTab);
  layout();window.dispatchEvent(new Event('dango-editor-view'));
 });
 installTextToolbar();installDynamicPreview(colors);installWindowDrag(root);
 selectTab('layers');decorate();treeChanged();layout();
}

// Simulation is drawn by the native body owner in the central viewport.
const icon=path=>`<svg viewBox="0 0 20 20" aria-hidden="true"><path d="${path}"/></svg>`;
const make=(tag,cls)=>{const e=document.createElement(tag);if(cls)e.className=cls;return e;};
export const hostCommand=(command,x=0,y=0)=>window.dispatchEvent(new CustomEvent('dango-host-command',{detail:{command,x,y}}));
const emit=(name,detail)=>window.dispatchEvent(new CustomEvent(name,{detail}));

export function installDynamicPreview(panel){
 const {panel:root,header,body}=panel,editor=root.closest('.unified-editor'),toggle=header.querySelector('.panel-toggle');
 const palette=make('section','palette-page'),controls=make('div','palette-controls');palette.id='palette_page';
 for(const child of [...header.children])if(child!==toggle)controls.append(child);
 palette.append(controls,...body.children);body.replaceChildren(palette);
 const preview=make('section','dynamic-preview-page');preview.id='dynamic_preview_page';preview.hidden=true;
 const toolbar=make('aside','simulation-tools');toolbar.id='simulation_tools';toolbar.hidden=true;toolbar.setAttribute('role','toolbar');toolbar.setAttribute('aria-label','模拟工具栏');toolbar.setAttribute('aria-orientation','vertical');editor.append(toolbar);
 const tools=[['pick','选挂点',1,'M10 2v16M2 10h16M10 5a5 5 0 1 0 0 10 5 5 0 0 0 0-10'],['pan','平移',2,'M10 2v16M2 10h16m-11-3-3 3 3 3m10-6 3 3-3 3M7 5l3-3 3 3m-6 10 3 3 3-3'],['zoom','缩放',3,'M9 3a6 6 0 1 0 0 12A6 6 0 0 0 9 3Zm4 10 5 5M6 9h6M9 6v6']];
 const toolButtons=new Map();let nativeTool='default';
 for(const [key,label,code,path]of tools){const b=make('button');b.type='button';b.title=label;b.setAttribute('aria-label',label);b.dataset.iconOnly='true';b.innerHTML=icon(path);b.onclick=()=>hostCommand('simulation-tool',nativeTool===key?0:code);toolbar.append(b);toolButtons.set(key,b);}
 const refresh=make('button','simulation-refresh');refresh.type='button';refresh.title='刷新团子';refresh.setAttribute('aria-label','刷新团子');refresh.dataset.iconOnly='true';refresh.innerHTML=icon('M4 8a6 6 0 1 1 1 8M2 3v5h5');refresh.onclick=()=>hostCommand('simulation-refresh');toolbar.append(refresh);
 const message=make('div','dynamic-preview-message');message.setAttribute('role','status');
 const form=make('form','simulation-wear-form');form.id='simulation_wear_form';form.setAttribute('aria-label','佩戴参数');
 const fields=new Map();
 const definitions=[['connectionStrength','连接强度',0,100],['maxConnectionTravelPercent','最大行程 %',0,200],['layer','显示层级','select'],['sizePercent','大小 %',10,400],['baseAngleDegrees','角度 °',-180,180],['gravityInfluence','整体重力',0,100],['lockRotation','锁定转动','checkbox'],['rotationDamping','转动阻尼',0,100],['returnStrength','整体回正',0,100],['limitSwing','限制摆角','checkbox'],['maxSwingDegrees','最大摆角 °',0,180]];
 for(const [key,label,min,max]of definitions){const row=make('label','simulation-wear-field'),text=make('span');text.textContent=label;const input=make(min==='select'?'select':'input');input.id='wear_'+key;input.name=key;input.setAttribute('aria-label',label);
  if(min==='select'){for(const [value,label]of [['Front','团子前'],['Behind','团子后']]){const option=make('option');option.value=value;option.textContent=label;input.append(option);}}
  else {input.type=min==='checkbox'?'checkbox':'number';if(input.type==='number'){input.min=min;input.max=max;input.step='any';}}
  row.append(text,input);form.append(row);fields.set(key,input);
 }
 let wear=null,lastRequested=null;
 function dependencies(){for(const key of ['rotationDamping','returnStrength','limitSwing','maxSwingDegrees'])fields.get(key).disabled=!wear||fields.get('lockRotation').checked||(key==='maxSwingDegrees'&&!fields.get('limitSwing').checked);}
 function syncWear(value){wear=value?{...value}:null;lastRequested=null;for(const [key,input]of fields){input.disabled=!wear;if(wear){if(input.type==='checkbox')input.checked=!!wear[key];else input.value=wear[key]??'';}}dependencies();}
 form.addEventListener('submit',e=>e.preventDefault());
 form.addEventListener('keydown',e=>{if(e.key==='Enter'&&!e.isComposing&&e.target.matches('input[type=number]')){e.preventDefault();e.target.dispatchEvent(new Event('change',{bubbles:true}));}});
 form.addEventListener('change',()=>{if(!wear||!form.reportValidity())return;const values={...wear};for(const [key,input]of fields)values[key]=input.type==='checkbox'?input.checked:input.type==='number'?Number(input.value):input.value;dependencies();const encoded=JSON.stringify(values);if(encoded===JSON.stringify(wear)||encoded===lastRequested)return;lastRequested=encoded;emit('dango-wear-change-request',values);});
 window.addEventListener('dango-wear-state',e=>syncWear(e.detail));syncWear(null);
 form.append(message);preview.append(form);body.append(preview);
 const stage=make('div','simulation-viewport');stage.id='simulation_viewport';stage.hidden=true;stage.setAttribute('aria-label','团子物理预览空间');editor.append(stage);
 const tabs=make('div','upper-tabs');tabs.setAttribute('role','tablist');tabs.setAttribute('aria-label','调色与预览');tabs.setAttribute('aria-orientation','vertical');
 const tab=(id,label,path,target)=>{const b=make('button');b.id=id;b.type='button';b.title=label;b.setAttribute('role','tab');b.setAttribute('aria-label',label);b.setAttribute('aria-controls',target);b.dataset.iconOnly='true';b.innerHTML=icon(path);tabs.append(b);return b;};
 const paletteTab=tab('palette_tab','调色盘','M10 2a8 8 0 1 0 0 16h2a2 2 0 0 0 0-4h-1a1 1 0 0 1 0-2h3a4 4 0 0 0 4-4c0-4-4-6-8-6Z',palette.id);
 const previewTab=tab('dynamic_preview_tab','动态预览','m7 4 10 6-10 6Z',preview.id);
 for(const [section,b]of [[palette,paletteTab],[preview,previewTab]]){section.setAttribute('role','tabpanel');section.setAttribute('aria-labelledby',b.id);}
 header.classList.add('upper-rail');header.replaceChildren(tabs,toggle);
 let page='palette',visible=false,lastViewport='',scheduled=false;
 function viewport(){scheduled=false;const active=page==='preview'&&!document.hidden;
  const er=editor.getBoundingClientRect(),nav=document.getElementById('main_menu').getBoundingClientRect(),panels=[...editor.querySelectorAll('.canvas-panel')].map(p=>p.getBoundingClientRect());
  const edge=Math.min(er.right-8,...panels.map(r=>r.left-8));
  const left=er.left+56,top=Math.max(er.top+8,nav.bottom+8),right=edge,bottom=er.bottom-8;
  toolbar.hidden=!active;toolbar.style.top=(top-er.top)+'px';
  const rectangle={x:Math.round(left),y:Math.round(top),width:Math.max(0,Math.floor(right-left)),height:Math.max(0,Math.floor(bottom-top)),visible:active&&right>left&&bottom>top};
  stage.hidden=!active;
  const encoded=JSON.stringify(rectangle);if(encoded!==lastViewport){lastViewport=encoded;stage.style.left=(left-er.left)+'px';stage.style.top=(top-er.top)+'px';stage.style.width=rectangle.width+'px';stage.style.height=rectangle.height+'px';emit('dango-simulation-viewport',rectangle);}
  if(visible!==active){visible=active;hostCommand(active?'preview-show':'preview-hide');}
 }
 function schedule(){if(scheduled)return;scheduled=true;requestAnimationFrame(viewport);}
 function select(value){const changed=page!==value;if(changed&&!window.dispatchEvent(new CustomEvent('dango-editor-simulation-changing',{detail:value==='preview',cancelable:true})))return;page=value;root.dataset.upperPage=value;editor.dataset.simulation=String(value==='preview');palette.hidden=value!=='palette';preview.hidden=value!=='preview';for(const [b,key]of [[paletteTab,'palette'],[previewTab,'preview']]){b.setAttribute('aria-selected',String(value===key));b.tabIndex=value===key?0:-1;}if(root.dataset.expanded!=='true')toggle.click();if(changed)emit('dango-editor-simulation',value==='preview');window.dispatchEvent(new Event('dango-editor-view'));viewport();}
 paletteTab.onclick=()=>select('palette');previewTab.onclick=()=>select('preview');
 tabs.addEventListener('keydown',e=>{if(['ArrowUp','ArrowDown','Home','End'].includes(e.key)){e.preventDefault();const value=e.key==='Home'?'palette':e.key==='End'?'preview':page==='palette'?'preview':'palette';select(value);(value==='palette'?paletteTab:previewTab).focus();}});
 new MutationObserver(schedule).observe(editor,{attributes:true,subtree:true,attributeFilter:['data-expanded','style']});
 const resize=new ResizeObserver(schedule);resize.observe(editor);resize.observe(document.getElementById('main_menu'));for(const p of editor.querySelectorAll('.canvas-panel'))resize.observe(p);
 window.addEventListener('dango-editor-view',schedule);document.addEventListener('visibilitychange',viewport);window.addEventListener('pagehide',()=>{emit('dango-simulation-viewport',{x:0,y:0,width:0,height:0,visible:false});if(visible)hostCommand('preview-hide');});
 window.addEventListener('dango-simulation-state',e=>{const state=e.detail||{};nativeTool=tools.some(([key])=>key===state.tool)?state.tool:'default';for(const [key,b]of toolButtons)b.setAttribute('aria-pressed',String(key===nativeTool));message.textContent=state.error||(state.updating?'正在更新配饰…':'');message.title=state.error||'';});
 window.addEventListener('dango-editor-host-ready',()=>{lastViewport='';if(page==='preview'){visible=false;viewport();}});
 select('palette');return {select,viewport};
}

export function installWindowDrag(root){
 const surface=make('div','editor-window-drag');surface.id='editor_window_drag';surface.title='拖动窗口';root.append(surface);
 surface.addEventListener('pointerdown',e=>{if(e.button===0){e.preventDefault();hostCommand('window-drag');}});
}

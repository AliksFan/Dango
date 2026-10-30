// Rearrange miniPaint's existing controls; retain its text selection and history paths.
import app from './vendor/src/js/app.js';
import config from './vendor/src/js/config.js';

export function installTextToolbar(){
 const attributes=document.getElementById('action_attributes');
 const labels={font:'字体',size:'字号',bold:'加粗',italic:'斜体',underline:'下划线',strikethrough:'删除线',stroke:'描边颜色',stroke_size:'描边',kerning:'字距',leading:'行距'};
 const icons={bold:'M6 3h5a4 4 0 0 1 0 7H6Zm0 7h6a4 4 0 0 1 0 7H6Z',italic:'M9 3h7M4 17h7M12 3 8 17',underline:'M5 3v7a5 5 0 0 0 10 0V3M3 18h14',strikethrough:'M14 4c-5-4-12 1-5 5l3 2c6 4-1 8-6 4M2 10h16'};
 let advancedOpen=false;
 function arrange(){
  const active=config.TOOL?.name==='text';
  if(!active){delete attributes.dataset.textToolbar;return;}
  attributes.dataset.textToolbar='true';
  if(attributes.querySelector('#text_more'))return;
  const advanced=document.createElement('details');advanced.id='text_more';advanced.className='top-options text-more';advanced.open=advancedOpen;
  const summary=document.createElement('summary');summary.title='更多文字参数';summary.setAttribute('aria-label','更多文字参数');
  summary.innerHTML='<svg viewBox="0 0 20 20" aria-hidden="true"><path d="M4 9v2m6-2v2m6-2v2"/></svg>';
  const body=document.createElement('div');body.className='text-more-body';body.setAttribute('role','group');body.setAttribute('aria-label','更多文字参数');advanced.append(summary,body);
  for(const [key,label]of Object.entries(labels)){
   const control=attributes.querySelector('#'+key);if(!control)continue;
   control.setAttribute('aria-label',label);control.title=label;
   control.querySelector('input')?.setAttribute('aria-label',label);
   if(icons[key]){control.dataset.iconOnly='true';control.dataset.editorIcon=key;control.innerHTML=`<svg viewBox="0 0 20 20" aria-hidden="true"><path d="${icons[key]}"/></svg>`;}
   const item=control.closest('.item');const caption=item?.querySelector('label');if(caption)caption.textContent=label;
   if(['strikethrough','stroke','stroke_size','kerning','leading'].includes(key))body.append(item);
  }
  const fill=attributes.querySelector('.item.fill');if(fill)fill.hidden=true;
  attributes.append(advanced);
  advanced.addEventListener('toggle',()=>{advancedOpen=advanced.open;});
 }
 // The palette remains the single fill-color control. Observe its committed color sample,
 // which all existing picker/channel/swatch paths update after changing config.COLOR.
 let color=config.COLOR;
 const sample=document.getElementById('selected_color_sample');
 const syncColor=()=>{
  if(config.TOOL?.name!=='text'){color=config.COLOR;return;}
  const data=app.GUI.GUI_tools.action_data();
  if(color===config.COLOR)return;
  color=config.COLOR;data.attributes.fill=color;
  if(config.layer?.type==='text')app.GUI.GUI_tools.tools_modules.text.object.on_params_update({key:'fill',value:color});
 };
 new MutationObserver(syncColor).observe(sample,{attributes:true,attributeFilter:['style']});
 document.getElementById('tools_container').addEventListener('click',e=>{
  if(e.target.closest('#text')){
   color=config.COLOR;
   if(config.layer?.type!=='text')app.GUI.GUI_tools.action_data().attributes.fill=color;
  }
  else advancedOpen=false;
 });
 new MutationObserver(arrange).observe(attributes,{childList:true});
 arrange();
}

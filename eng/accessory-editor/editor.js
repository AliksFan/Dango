// E0 adapter only. Native owns filesystem, message validation, saves, and physics.
import app from './vendor/src/js/app.js';
import config from './vendor/src/js/config.js';
import './vendor/src/js/core/components/index.js';
import BaseGui from './vendor/src/js/core/base-gui.js';
import BaseLayers from './vendor/src/js/core/base-layers.js';
import BaseTools from './vendor/src/js/core/base-tools.js';
import BaseState from './vendor/src/js/core/base-state.js';
import * as Actions from './vendor/src/js/actions/index.js';
import {installSkeletonEditor} from './skeleton-editor.js';
import {installEditorLayout} from './editor-layout.js';
import {validateDocument,allNodes} from './skeleton-commands.js';
import {installRestore} from './restore-editor.js';
import {installMagicSelection} from './magic-selection.js';
import {installLabels} from './labels.js';
import {installEditorTransport} from './transport.js';
import imageStore from './vendor/src/js/actions/store/image-store.js';
import {retainedBytes,requireBudget} from './history-budget.js';
import notify from './notify.js';
import {validateLayer} from './project-schema.js';
import {installClipboardImage} from './clipboard-image.js';
import {installWearHistory} from './wear-history.js';
import {installSimulationInput} from './simulation-input.js';

const limits=Object.freeze({maxSide:4096,maxPixels:16000000,maxLayers:16,maxLayerPixels:64000000,maxProjectBytes:64*1024*1024,maxHistory:20,maxHistoryBytes:64*1024*1024});
let ready=false,busy=false,geometryApi;
const safeTypes=new Set([null,'image','brush','text']);
const fields=['id','parent_id','name','type','x','y','width','height','width_original','height_original','visible','is_vector','hide_selection_if_active','opacity','order','composition','rotate','data','params','status','color','render_function','flipX','flipY'];
function ensureReady(){if(!ready||busy)throw Error('Editor unavailable');}
function checkSize(w,h){if(!Number.isInteger(w)||!Number.isInteger(h)||w<1||h<1||w>limits.maxSide||h>limits.maxSide||w*h>limits.maxPixels)throw Error('Canvas budget exceeded');}
function plain(value,depth=0,counter={n:0},field='') {
  if(++counter.n>250000||depth>20)throw Error('Project shape exceeded');
  if(value===null||typeof value==='boolean')return value;
  if(typeof value==='number'){if(!Number.isFinite(value)||Math.abs(value)>1e9)throw Error('Invalid numeric value');return value;}
  if(typeof value==='string'){if(value.length>limits.maxProjectBytes||!['name','text'].includes(field)&&/^(?:https?:|file:|javascript:|blob:)/i.test(value))throw Error('External reference rejected');return value;}
  if(Array.isArray(value))return value.map(v=>plain(v,depth+1,counter,field));
  if(typeof value!=='object')throw Error('Invalid project data');
  const output={};for(const [k,v]of Object.entries(value)){if(['__proto__','constructor','prototype','link'].includes(k))throw Error('Invalid project key');if(v!==undefined)output[k]=plain(v,depth+1,counter,k);}return output;
}
function pngHeader(base64){
  if(typeof base64!=='string'||base64.length>Math.ceil(32*1024*1024/3)*4||base64.length<44||base64.length%4!==0||(base64.length/4*3-(base64.endsWith('==')?2:base64.endsWith('=')?1:0))>32*1024*1024||!/^iVBORw0KGgo[A-Za-z0-9+/=]*$/.test(base64))throw Error('Expected normalized PNG within 32 MiB');
  const binary=atob(base64.slice(0,44)),bytes=Uint8Array.from(binary,c=>c.charCodeAt(0)),view=new DataView(bytes.buffer);if(bytes.length<33||view.getUint32(8)!==13||view.getUint32(12)!==0x49484452)throw Error('Invalid PNG header');const width=view.getUint32(16),height=view.getUint32(20);checkSize(width,height);return {width,height};
}
async function decodePng(base64) {
  pngHeader(base64);
  // Hidden WebView2 controllers can finish loading while decode() waits for a
  // presentation frame indefinitely. Load/error also covers hidden documents.
  const image=new Image();await new Promise((resolve,reject)=>{
    image.onload=()=>resolve();image.onerror=()=>reject(Error('PNG image could not be loaded'));
    image.src='data:image/png;base64,'+base64;
  });checkSize(image.naturalWidth,image.naturalHeight);return image;
}
function canvasPng(canvas){return canvas.toDataURL('image/png').split(',')[1];}
async function clearHistory(){for(const action of app.State.action_history)await action.free();app.State.action_history=[];app.State.action_history_index=0;await imageStore.delete_all();}

async function load(input) {
  ensureReady();geometryApi?.ensureSettled();busy=true;
  try {
    const value=plain(input);checkSize(value.width,value.height);
    if(JSON.stringify(value).length>limits.maxProjectBytes)throw Error('Project byte budget exceeded');
    let layers=[],nextSkeleton,nextName='配饰';
    if(value.pngBase64!==undefined) {
      const image=await decodePng(value.pngBase64);
      if(image.naturalWidth!==value.width||image.naturalHeight!==value.height)throw Error('Image dimensions differ');
      layers=[{id:1,name:'图层 1',type:'image',link:image,x:0,y:0,width:value.width,height:value.height,width_original:value.width,height_original:value.height,visible:true,opacity:100,order:1,composition:'source-over',rotate:0,data:null,params:{},filters:[],is_vector:false,render_function:null,status:null,color:config.COLOR,parent_id:0,hide_selection_if_active:false}];
    } else {
      if(value.schemaVersion!==2||!Array.isArray(value.layers)||!Array.isArray(value.assets)||!value.layers.length||value.layers.length>limits.maxLayers||value.assets.length>48||!Array.isArray(value.renderPieces))throw Error('Unsupported local project');
      nextSkeleton=validateDocument(value.skeleton);nextName=value.name;
      if(typeof nextName!=='string'||!nextName.trim()||nextName.length>128||/[\u0000-\u001f\u007f-\u009f]/.test(nextName)||allNodes(nextSkeleton).some(n=>n.x<0||n.y<0||n.x>value.width||n.y>value.height))throw Error('Invalid skeleton or name');
      const sortedIds=value.layers.slice().sort((a,b)=>b.order-a.order).reverse().map(l=>String(l.id));
      if(JSON.stringify(nextSkeleton.layers.map(l=>l.id))!==JSON.stringify(sortedIds))throw Error('Skeleton layer order differs');
      for(const layer of value.layers)validateLayer(layer);
      const checkedIds=new Set();let declaredPixels=0;for(const a of value.assets){if(!/^asset-[0-9]+$/.test(a.id)||checkedIds.has(a.id))throw Error('Invalid asset reference');checkedIds.add(a.id);const size=pngHeader(a.pngBase64);declaredPixels+=size.width*size.height;if(declaredPixels>limits.maxLayerPixels)throw Error('Layer pixel budget exceeded');}
      const expectedPieces=[];for(const layer of value.layers.slice().sort((a,b)=>b.order-a.order).reverse().filter(l=>l.visible!==false&&l.opacity>0&&l.type!==null)){const groupId=nextSkeleton.layers.find(l=>l.id===String(layer.id)).groupId??null;if(!expectedPieces.length||expectedPieces.at(-1)!==groupId)expectedPieces.push(groupId);}
      const sourceOnly=value.renderPieces.length===0&&nextSkeleton.groups.length===0;
      if(!sourceOnly&&(value.renderPieces.length!==expectedPieces.length||value.renderPieces.some((p,i)=>!p||Object.keys(p).sort().join(',')!=='assetId,groupId'||p.groupId!==expectedPieces[i]||!checkedIds.has(p.assetId))))throw Error('Invalid render piece order or asset');
      const assets=new Map();let pixels=0;
      for(const a of value.assets){if(!/^asset-[0-9]+$/.test(a.id)||assets.has(a.id))throw Error('Invalid asset reference');const image=await decodePng(a.pngBase64);pixels+=image.naturalWidth*image.naturalHeight;if(pixels>limits.maxLayerPixels)throw Error('Layer pixel budget exceeded');assets.set(a.id,image);}
      const ids=new Set();
      for(const source of value.layers){
        if(!safeTypes.has(source.type)||!Number.isInteger(source.id)||source.id<1||ids.has(source.id))throw Error('Invalid layer');ids.add(source.id);
        const layer={};for(const k of fields)if(source[k]!==undefined)layer[k]=source[k];
        if(typeof layer.name!=='string'||layer.name.length>128||/[<>]/.test(layer.name))throw Error('Invalid layer name');
        if(layer.composition!=='source-over'||(layer.type==='image'&&layer.is_vector===true)||layer.opacity<0||layer.opacity>100)throw Error('Unsupported layer settings');
        if(layer.render_function!==null&&(!Array.isArray(layer.render_function)||layer.render_function[0]!==layer.type||layer.render_function[1]!=='render'))throw Error('Unsupported renderer');
        if(layer.type==='image'){if(!assets.has(source.assetId))throw Error('Missing asset');layer.link=assets.get(source.assetId);layer.data=null;
          // Native projects allow omitted source-size metadata. Raster tools need
          // decoded pixel dimensions, independently of the displayed layer size.
          layer.width_original??=layer.link.naturalWidth;layer.height_original??=layer.link.naturalHeight;
          if(source.restoreAssetId!==undefined){if(!assets.has(source.restoreAssetId))throw Error('Missing restore asset');layer.link_restore=assets.get(source.restoreAssetId);if(layer.link_restore.naturalWidth!==layer.link.naturalWidth||layer.link_restore.naturalHeight!==layer.link.naturalHeight)throw Error('Restore dimensions differ');}}
        layer.filters=[];layers.push(layer);
      }
      if(layers.reduce((n,l)=>n+Math.max(0,l.width??0)*Math.max(0,l.height??0),0)>limits.maxLayerPixels)throw Error('Layer pixel budget exceeded');
    }
    // Decode and validate before changing the current document.
    await clearHistory();config.WIDTH=value.width;config.HEIGHT=value.height;config.layers=layers;config.layer=layers[0];app.Layers.auto_increment=Math.max(...layers.map(l=>l.id))+1;
    const brush=app.GUI.GUI_tools.tools_modules.brush.object;brush.params_hash=false;brush.event_links=[];brush.data_index=0;brush.blockedStroke=false;brush.strokePending=false;
    geometryApi?.resetGeometry();if(nextSkeleton)geometryApi.importState(nextSkeleton,nextName);else geometryApi?.setName(nextName);app.GUI.prepare_canvas();app.Layers.init_zoom_lib();app.GUI.GUI_layers.render_layers();config.need_render=true;
    return {schemaVersion:2,width:config.WIDTH,height:config.HEIGHT};
  } finally {busy=false;}
}
function exportProject() {
  ensureReady();geometryApi?.ensureSettled();const assets=[],layers=[];let assetPixels=0;const asset=image=>{assetPixels+=image.naturalWidth*image.naturalHeight;if(assetPixels>limits.maxLayerPixels)throw Error("Layer pixel budget exceeded");const c=document.createElement("canvas");c.width=image.naturalWidth;c.height=image.naturalHeight;c.getContext("2d").drawImage(image,0,0);const id="asset-"+(assets.length+1);assets.push({id,pngBase64:canvasPng(c)});return id;};
  for(const source of config.layers){
    if(!safeTypes.has(source.type))throw Error('Unsupported layer');
    const layer={};for(const k of fields)if(source[k]!==undefined)layer[k]=source[k];
    layer.status=null; // Pointer-draft flags are page state, never project data.
    if(source.type==='image'){layer.assetId=asset(source.link);if(source.link_restore)layer.restoreAssetId=asset(source.link_restore);layer.data=null;}
    layers.push(layer);
  }
  if(geometryApi.requiresRebind())throw Error('有节点超出画布，请重新定位或撤销。');
  const skeleton=geometryApi.getSkeleton(),renderPieces=[],ordered=app.Layers.get_sorted_layers().slice().reverse().filter(l=>l.visible!==false&&l.opacity>0&&l.type!==null);
  let current;
  for(const source of ordered){
    const groupId=skeleton.layers.find(l=>l.id===String(source.id))?.groupId??null;
    if(!current||current.groupId!==groupId){const canvas=document.createElement('canvas');canvas.width=config.WIDTH;canvas.height=config.HEIGHT;current={groupId,canvas};renderPieces.push(current);}
    const ctx=current.canvas.getContext('2d');ctx.save();ctx.globalAlpha=source.opacity/100;ctx.globalCompositeOperation='source-over';app.Layers.render_object(ctx,source);ctx.restore();
  }
  const pieces=renderPieces.map(piece=>{assetPixels+=config.WIDTH*config.HEIGHT;if(assetPixels>limits.maxLayerPixels)throw Error('绘制片像素超出总资源上限。');const id='asset-'+(assets.length+1);assets.push({id,pngBase64:canvasPng(piece.canvas)});return {groupId:piece.groupId,assetId:id};});
  const result={schemaVersion:2,width:config.WIDTH,height:config.HEIGHT,name:geometryApi.getName(),skeleton,layers:plain(layers),assets,renderPieces:pieces};
  if(layers.length>limits.maxLayers||JSON.stringify(result).length>limits.maxProjectBytes)throw Error('Project export budget exceeded');return result;
}
function exportPng(){ensureReady();geometryApi?.ensureSettled();checkSize(config.WIDTH,config.HEIGHT);const canvas=document.createElement('canvas');canvas.width=config.WIDTH;canvas.height=config.HEIGHT;app.Layers.convert_layers_to_canvas(canvas.getContext('2d'));return {width:canvas.width,height:canvas.height,pngBase64:canvasPng(canvas)};}

window.addEventListener('load',()=>{
  const Layers=new BaseLayers(),Tools=new BaseTools(true),GUI=new BaseGui(),State=new BaseState();
  Object.assign(app,{Actions,Config:config,GUI,Layers,State,Tools});
  State.action_history_max=limits.maxHistory;
  const liveImages=()=>new Set(config.layers.flatMap(l=>[l.link,l.link_restore]).filter(Boolean));
  State.check_memory_budget=extra=>requireBudget(retainedBytes(State.action_history,liveImages())+imageStore.bytes,extra,limits.maxHistoryBytes);
  State.can_add_payload=extra=>{try{State.check_memory_budget(extra);return true;}catch{return false;}};
  const perform=State.do_action.bind(State);
  State.do_action=async(action,options)=>{
    function estimate(a){
      let bytes=retainedBytes(a);
      if(a.actions_to_do)for(const child of a.actions_to_do)bytes+=estimate(child);
      if(a.action_id==='update_layer'){const layer=Layers.get_layer(a.layer_id);for(const k of Object.keys(a.settings??{}))bytes+=retainedBytes(layer?.[k]);}
      if(['delete_layer','clear_layer'].includes(a.action_id))bytes+=retainedBytes(Layers.get_layer(a.layer_id));
      if(a.action_id==='reset_layers')bytes+=retainedBytes(config.layers);
      return bytes;
    }
    try{State.check_memory_budget(estimate(action)+retainedBytes(config.layer,liveImages())+4096);}catch(error){notify.error('撤销资源不足，已保留当前结果。');return {status:'aborted',reason:'Undo budget exceeded'};}
    const result=await perform(action,options);if(result?.status==='aborted')notify.error('操作未完成，已保留当前结果。');return result;
  };
  const titles={select:'↖ 选层',brush:'╱ 画笔',erase:'− 橡皮',magic_erase:'⌁ 魔棒',fill:'▱ 填充',text:'T 文字',crop:'⌗ 整图裁剪'};for(const tool of config.TOOLS)tool.title=titles[tool.name];
  GUI.init();Layers.init();
  for(const [id,label]of Object.entries({insert_layer:'＋ 新层',layer_duplicate:'▣ 复制',layer_raster:'▧ 光栅',layer_down:'↓ 下移',layer_up:'↑ 上移'})){const button=document.getElementById(id);if(button){button.textContent=label;button.title=label;button.setAttribute('aria-label',label);}}

  // Bound layer creation at the action boundary, including duplicate and tool-created layers.
  const insert=Actions.Insert_layer_action.prototype.do;
  const pixelCount=layer=>Math.max(0,layer.width??0)*Math.max(0,layer.height??0);
  Actions.Insert_layer_action.prototype.do=async function(){if(config.layers.length>=limits.maxLayers||config.layers.reduce((n,l)=>n+pixelCount(l),0)+pixelCount(this.settings??{})>limits.maxLayerPixels)throw Error('Layer budget exceeded');return insert.call(this);};
  const update=Actions.Update_layer_action.prototype.do;
  Actions.Update_layer_action.prototype.do=async function(){const layer=Layers.get_layer(this.layer_id);const candidate={...layer,...this.settings};if(candidate.width!=null&&candidate.height!=null&&candidate.width>0&&candidate.height>0){checkSize(Math.ceil(candidate.width),Math.ceil(candidate.height));if(config.layers.reduce((n,l)=>n+pixelCount(l),0)-pixelCount(layer)+pixelCount(candidate)>limits.maxLayerPixels)throw Error('Layer pixel budget exceeded');}return update.call(this);};
  const configure=Actions.Update_config_action.prototype.do;
  Actions.Update_config_action.prototype.do=async function(){checkSize(this.settings.WIDTH??config.WIDTH,this.settings.HEIGHT??config.HEIGHT);return configure.call(this);};
  installLabels(GUI);
  geometryApi=installSkeletonEditor({ensureReady,checkSize});
  installRestore({geometryApi,limits});
  const magicSelection=installMagicSelection({geometryApi,limits});
  let wearHistory;
  installSimulationInput({ensureSettled:()=>{ensureReady();geometryApi.ensureSettled();wearHistory?.ensureSettled();},onError:message=>notify.error(message)});
  installEditorLayout(geometryApi.layout);
  wearHistory=installWearHistory({State,ensureReady,ensureSettled:geometryApi.ensureSettled,onError:message=>notify.error(message)});
  document.getElementById('redo_button').onclick=()=>State.redo();
  document.getElementById('undo_button').onclick=()=>State.undo();
  ready=true;
  const exportSnapshot=async()=>{ensureReady();geometryApi.ensureSettled();wearHistory.ensureSettled();return {project:exportProject(),png:exportPng()};};
  window.DangoMiniPaint=Object.freeze({apiVersion:2,limits,load,exportProject,exportPng,exportSnapshot,getEditSequence:geometryApi.getEditSequence,setSkeleton:geometryApi.setSkeleton,getSkeleton:geometryApi.getSkeleton,setGeometry:geometryApi.setGeometry,getGeometry:geometryApi.getGeometry,transformCanvas:geometryApi.transformCanvas,undo:()=>State.undo(),redo:()=>State.redo(),getWearParameters:wearHistory.get});
  const clipboardPort=installEditorTransport(window.DangoMiniPaint);
  installClipboardImage({ensureReady,checkSize,geometryApi,limits,exportProject,magicSelection,clipboardPort});
  window.dispatchEvent(new Event('dango-editor-ready'));
});

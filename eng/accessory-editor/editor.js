// E0 adapter only. Native owns filesystem, message validation, saves, and physics.
import app from './vendor/src/js/app.js';
import config from './vendor/src/js/config.js';
import './vendor/src/js/core/components/index.js';
import BaseGui from './vendor/src/js/core/base-gui.js';
import BaseLayers from './vendor/src/js/core/base-layers.js';
import BaseTools from './vendor/src/js/core/base-tools.js';
import BaseState from './vendor/src/js/core/base-state.js';
import * as Actions from './vendor/src/js/actions/index.js';
import Flip from './vendor/src/js/modules/image/flip.js';
import Rotate from './vendor/src/js/modules/image/rotate.js';
import imageStore from './vendor/src/js/actions/store/image-store.js';
import {retainedBytes,requireBudget} from './history-budget.js';
import notify from './notify.js';
import {validateLayer} from './project-schema.js';

const limits=Object.freeze({maxSide:4096,maxPixels:16000000,maxLayers:16,maxLayerPixels:64000000,maxProjectBytes:64*1024*1024,maxHistory:20,maxHistoryBytes:64*1024*1024});
let ready=false,busy=false;
const safeTypes=new Set([null,'image','brush','text']);
const fields=['id','parent_id','name','type','x','y','width','height','width_original','height_original','visible','is_vector','hide_selection_if_active','opacity','order','composition','rotate','data','params','status','color','render_function'];
function ensureReady(){if(!ready||busy)throw Error('Editor unavailable');}
function checkSize(w,h){if(!Number.isInteger(w)||!Number.isInteger(h)||w<1||h<1||w>limits.maxSide||h>limits.maxSide||w*h>limits.maxPixels)throw Error('Canvas budget exceeded');}
function plain(value,depth=0,counter={n:0}) {
  if(++counter.n>250000||depth>20)throw Error('Project shape exceeded');
  if(value===null||typeof value==='boolean')return value;
  if(typeof value==='number'){if(!Number.isFinite(value)||Math.abs(value)>1e9)throw Error('Invalid numeric value');return value;}
  if(typeof value==='string'){if(value.length>limits.maxProjectBytes||/^(?:https?:|file:|javascript:|blob:)/i.test(value))throw Error('External reference rejected');return value;}
  if(Array.isArray(value))return value.map(v=>plain(v,depth+1,counter));
  if(typeof value!=='object')throw Error('Invalid project data');
  const output={};for(const [k,v]of Object.entries(value)){if(['__proto__','constructor','prototype','link'].includes(k))throw Error('Invalid project key');output[k]=plain(v,depth+1,counter);}return output;
}
async function decodePng(base64) {
  if(typeof base64!=='string'||base64.length>limits.maxProjectBytes||!/^iVBORw0KGgo[A-Za-z0-9+/=]*$/.test(base64))throw Error('Expected normalized PNG');
  const binary=atob(base64), bytes=Uint8Array.from(binary,c=>c.charCodeAt(0));
  if(bytes.length<24)throw Error('Invalid PNG');
  const view=new DataView(bytes.buffer);checkSize(view.getUint32(16),view.getUint32(20));
  const image=new Image();image.src='data:image/png;base64,'+base64;await image.decode();checkSize(image.naturalWidth,image.naturalHeight);return image;
}
function canvasPng(canvas){return canvas.toDataURL('image/png').split(',')[1];}
async function clearHistory(){for(const action of app.State.action_history)await action.free();app.State.action_history=[];app.State.action_history_index=0;await imageStore.delete_all();}

async function load(input) {
  ensureReady();busy=true;
  try {
    const value=plain(input);checkSize(value.width,value.height);
    if(JSON.stringify(value).length>limits.maxProjectBytes)throw Error('Project byte budget exceeded');
    let layers=[];
    if(value.pngBase64!==undefined) {
      const image=await decodePng(value.pngBase64);
      if(image.naturalWidth!==value.width||image.naturalHeight!==value.height)throw Error('Image dimensions differ');
      layers=[{id:1,name:'图层 1',type:'image',link:image,x:0,y:0,width:value.width,height:value.height,width_original:value.width,height_original:value.height,visible:true,opacity:100,order:1,composition:'source-over',rotate:0,data:null,params:{},filters:[],is_vector:false,render_function:null,status:null,color:config.COLOR,parent_id:0,hide_selection_if_active:false}];
    } else {
      if(value.schemaVersion!==1||!Array.isArray(value.layers)||!Array.isArray(value.assets)||!value.layers.length||value.layers.length>limits.maxLayers||value.assets.length>limits.maxLayers)throw Error('Unsupported local project');
      for(const layer of value.layers)validateLayer(layer);
      const assets=new Map();let pixels=0;
      for(const a of value.assets){if(!/^asset-[0-9]+$/.test(a.id)||assets.has(a.id))throw Error('Invalid asset reference');const image=await decodePng(a.pngBase64);pixels+=image.naturalWidth*image.naturalHeight;if(pixels>limits.maxLayerPixels)throw Error('Layer pixel budget exceeded');assets.set(a.id,image);}
      const ids=new Set();
      for(const source of value.layers){
        if(!safeTypes.has(source.type)||!Number.isInteger(source.id)||source.id<1||ids.has(source.id))throw Error('Invalid layer');ids.add(source.id);
        const layer={};for(const k of fields)if(source[k]!==undefined)layer[k]=source[k];
        if(typeof layer.name!=='string'||layer.name.length>128||/[<>]/.test(layer.name))throw Error('Invalid layer name');
        if(layer.composition!=='source-over'||(layer.type==='image'&&layer.is_vector===true)||layer.opacity<0||layer.opacity>100)throw Error('Unsupported layer settings');
        if(layer.render_function!==null&&(!Array.isArray(layer.render_function)||layer.render_function[0]!==layer.type||layer.render_function[1]!=='render'))throw Error('Unsupported renderer');
        if(layer.type==='image'){if(!assets.has(source.assetId))throw Error('Missing asset');layer.link=assets.get(source.assetId);layer.data=null;}
        layer.filters=[];layers.push(layer);
      }
      if(layers.reduce((n,l)=>n+Math.max(0,l.width??0)*Math.max(0,l.height??0),0)>limits.maxLayerPixels)throw Error('Layer pixel budget exceeded');
    }
    // Decode and validate before changing the current document.
    await clearHistory();config.WIDTH=value.width;config.HEIGHT=value.height;config.layers=layers;config.layer=layers[0];app.Layers.auto_increment=Math.max(...layers.map(l=>l.id))+1;
    const brush=app.GUI.GUI_tools.tools_modules.brush.object;brush.params_hash=false;brush.event_links=[];brush.data_index=0;brush.blockedStroke=false;brush.strokePending=false;
    app.GUI.prepare_canvas();app.Layers.init_zoom_lib();app.GUI.GUI_layers.render_layers();config.need_render=true;
    return {schemaVersion:1,width:config.WIDTH,height:config.HEIGHT};
  } finally {busy=false;}
}
function exportProject() {
  ensureReady();const assets=[],layers=[];
  for(const source of config.layers){
    if(!safeTypes.has(source.type))throw Error('Unsupported layer');
    const layer={};for(const k of fields)if(source[k]!==undefined)layer[k]=source[k];
    layer.status=null; // Pointer-draft flags are page state, never project data.
    if(source.type==='image'){const canvas=document.createElement('canvas');canvas.width=source.link.naturalWidth;canvas.height=source.link.naturalHeight;canvas.getContext('2d').drawImage(source.link,0,0);layer.assetId='asset-'+(assets.length+1);assets.push({id:layer.assetId,pngBase64:canvasPng(canvas)});layer.data=null;}
    layers.push(layer);
  }
  const result={schemaVersion:1,width:config.WIDTH,height:config.HEIGHT,layers:plain(layers),assets};
  if(layers.length>limits.maxLayers||JSON.stringify(result).length>limits.maxProjectBytes)throw Error('Project export budget exceeded');return result;
}
function exportPng(){ensureReady();checkSize(config.WIDTH,config.HEIGHT);const canvas=document.createElement('canvas');canvas.width=config.WIDTH;canvas.height=config.HEIGHT;app.Layers.convert_layers_to_canvas(canvas.getContext('2d'));return {width:canvas.width,height:canvas.height,pngBase64:canvasPng(canvas)};}

window.addEventListener('load',()=>{
  const Layers=new BaseLayers(),Tools=new BaseTools(true),GUI=new BaseGui(),State=new BaseState();
  Object.assign(app,{Actions,Config:config,GUI,Layers,State,Tools});
  State.action_history_max=limits.maxHistory;
  const liveImages=()=>new Set(config.layers.map(l=>l.link).filter(Boolean));
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
  GUI.init();Layers.init();
  // Bound layer creation at the action boundary, including duplicate and tool-created layers.
  const insert=Actions.Insert_layer_action.prototype.do;
  const pixelCount=layer=>Math.max(0,layer.width??0)*Math.max(0,layer.height??0);
  Actions.Insert_layer_action.prototype.do=async function(){if(config.layers.length>=limits.maxLayers||config.layers.reduce((n,l)=>n+pixelCount(l),0)+pixelCount(this.settings??{})>limits.maxLayerPixels)throw Error('Layer budget exceeded');return insert.call(this);};
  const update=Actions.Update_layer_action.prototype.do;
  Actions.Update_layer_action.prototype.do=async function(){const layer=Layers.get_layer(this.layer_id);const candidate={...layer,...this.settings};if(candidate.width!=null&&candidate.height!=null&&candidate.width>0&&candidate.height>0){checkSize(Math.ceil(candidate.width),Math.ceil(candidate.height));if(config.layers.reduce((n,l)=>n+pixelCount(l),0)-pixelCount(layer)+pixelCount(candidate)>limits.maxLayerPixels)throw Error('Layer pixel budget exceeded');}return update.call(this);};
  const configure=Actions.Update_config_action.prototype.do;
  Actions.Update_config_action.prototype.do=async function(){checkSize(this.settings.WIDTH??config.WIDTH,this.settings.HEIGHT??config.HEIGHT);return configure.call(this);};
  const flip=new Flip(),rotate=new Rotate();
  document.getElementById('flip_x').onclick=()=>flip.horizontal();
  document.getElementById('flip_y').onclick=()=>flip.vertical();
  document.getElementById('rotate').onclick=()=>rotate.left();
  document.getElementById('redo_button').onclick=()=>State.redo();
  document.getElementById('undo_button').onclick=()=>State.undo();
  ready=true;
  window.DangoMiniPaint=Object.freeze({apiVersion:1,limits,load,exportProject,exportPng});
  window.dispatchEvent(new Event('dango-editor-ready'));
});

import app from './vendor/src/js/app.js';
import config from './vendor/src/js/config.js';
import notify from './notify.js';
import {validateFragment,selectFragment,remapFragment,pngSize} from './editable-clipboard.js';
import {validateLayer} from './project-schema.js';

const maxBytes=32*1024*1024;
const supported=new Set(['image/png','image/jpeg']);
const message='仅支持 PNG 或 JPEG 图片（最长边 4096，最多 1600 万像素、32 MiB）。';

// Read dimensions before giving encoded data to the browser decoder.
export function imageSize(bytes,type){
 const view=new DataView(bytes.buffer,bytes.byteOffset,bytes.byteLength);
 if(type==='image/png'&&bytes.length>=33&&view.getUint32(0)===0x89504e47&&view.getUint32(4)===0x0d0a1a0a&&view.getUint32(8)===13&&view.getUint32(12)===0x49484452)return {width:view.getUint32(16),height:view.getUint32(20)};
 if(type==='image/jpeg'&&bytes.length>=4&&view.getUint16(0)===0xffd8){
  let at=2;
  while(at+1<bytes.length){
   if(bytes[at++]!==0xff)break;
   while(at<bytes.length&&bytes[at]===0xff)at++;
   const marker=bytes[at++];
   if(marker===0xda||marker===0xd9)break;
   if(marker===0x01||marker>=0xd0&&marker<=0xd7)continue;
   if(at+2>bytes.length)break;
   const length=view.getUint16(at);
   if(length<2||at+length>bytes.length)break;
   if([0xc0,0xc1,0xc2].includes(marker)&&length>=8)return {width:view.getUint16(at+5),height:view.getUint16(at+3)};
   at+=length;
  }
 }
 throw Error(message);
}

async function decode(file,checkSize){
 if(!supported.has(file.type)||file.size<1||file.size>maxBytes)throw Error(message);
 const size=imageSize(new Uint8Array(await file.arrayBuffer()),file.type);
 try{checkSize(size.width,size.height);}catch{throw Error(message);}
 const url=URL.createObjectURL(file),source=new Image();
 let canvas;
 try{
  source.src=url;await source.decode();
  if(source.naturalWidth!==size.width||source.naturalHeight!==size.height)throw Error(message);
  canvas=document.createElement('canvas');canvas.width=size.width;canvas.height=size.height;
  canvas.getContext('2d').drawImage(source,0,0);
  const png=canvas.toDataURL('image/png');
  const base64=png.slice('data:image/png;base64,'.length);
  if(base64.length/4*3-(base64.endsWith('==')?2:base64.endsWith('=')?1:0)>maxBytes)throw Error(message);
  const image=new Image();image.src=png;await image.decode();return image;
 }catch{throw Error(message);}finally{URL.revokeObjectURL(url);if(canvas){canvas.width=1;canvas.height=1;}}
}

// A normal miniPaint history action, without its image-import autoresize and
// first-empty-layer replacement. The skeleton decorator owns group membership.
function insertAction(layer,limits,source){
 let previous,increment;
 const redraw=()=>{app.Layers.render();app.GUI.GUI_layers.render_layers();};
 return {action_id:source==='drop'?'drop_image':'paste_image',action_description:source==='drop'?'导入图片':'粘贴图片',layer,
  async do(){
   if(config.layers.length>=limits.maxLayers)throw Error('图层已达上限，请先移除不需要的图层。');
   previous=config.layer;increment=app.Layers.auto_increment;
   config.layers.push(this.layer);config.layer=this.layer;app.Layers.auto_increment=this.layer.id+1;redraw();
  },
  async undo(){config.layers.splice(config.layers.indexOf(this.layer),1);config.layer=previous;app.Layers.auto_increment=increment;redraw();},
  async free(){previous=null;this.layer=null;}
 };
}

export function installClipboardImage({ensureReady,checkSize,geometryApi,limits,exportProject,magicSelection,clipboardPort}){
 let pending=false,decoding=false,pendingMessage='';
 const ensureSettled=geometryApi.ensureSettled.bind(geometryApi);
 geometryApi.ensureSettled=()=>{if(decoding)throw Error(pendingMessage);ensureSettled();};
 const protectedInput=event=>event.target.closest?.('input,textarea,select,[contenteditable]:not([contenteditable=false]),[role=textbox],dialog,[role=dialog]')||document.querySelector('dialog[open],.text-input');
 const usage=project=>project.assets.reduce((sum,a)=>{const s=pngSize(a.pngBase64,limits);return sum+s.width*s.height;},0);
 function checkAdditional(project,pixels,displayPixels,encoded,layers){
  if(config.layers.length+layers>limits.maxLayers)throw Error('图层已达上限，请先移除不需要的图层。');
  if(usage(project)+pixels+config.WIDTH*config.HEIGHT>limits.maxLayerPixels||config.layers.reduce((sum,l)=>sum+(l.width??0)*(l.height??0),0)+displayPixels>limits.maxLayerPixels)throw Error('图片资源已达上限，请先移除不需要的图层。');
  const scanlines=(config.WIDTH*4+1)*config.HEIGHT,maxPng=scanlines+Math.ceil(scanlines/65535)*5+100;
  if(JSON.stringify(project).length+encoded+Math.ceil(maxPng/3)*4+4096>limits.maxProjectBytes)throw Error('工程资源已达上限，请先移除不需要的图层。');
 }
 async function copyCurrent(){
  if(pending){notify.error(pendingMessage);return;}
  try{
   ensureReady();ensureSettled();
   const project=exportProject(),mask=magicSelection?.copyCanvas(),target=mask?{kind:'layer',id:config.layer.id}:geometryApi.getClipboardTarget();
   const ids=new Set(target.kind==='group'?project.skeleton.layers.filter(l=>l.groupId===target.id).map(l=>l.id):[String(target.id)]);
   const canvas=document.createElement('canvas');canvas.width=config.WIDTH;canvas.height=config.HEIGHT;const ctx=canvas.getContext('2d');
   for(const l of app.Layers.get_sorted_layers().slice().reverse().filter(l=>ids.has(String(l.id)))){ctx.save();ctx.globalAlpha=l.opacity/100;ctx.globalCompositeOperation='source-over';app.Layers.render_object(ctx,mask&&l===config.layer?{...l,link:mask,link_canvas:null}:l);ctx.restore();}
   if(mask){const layer=project.layers.find(l=>l.id===config.layer.id),asset=project.assets.find(a=>a.id===layer.assetId);asset.pngBase64=mask.toDataURL('image/png').split(',')[1];delete layer.restoreAssetId;}
   const snapshot=selectFragment(project,target,{width:canvas.width,height:canvas.height,pngBase64:canvas.toDataURL('image/png').split(',')[1]},limits);
   pending=true;pendingMessage='正在复制，请稍候。';await clipboardPort.copyClipboard(snapshot);notify.success('已复制，可粘贴为图层或图片。');
  }catch(error){notify.error(error.message);}finally{pending=false;}
 }
 async function pasteFragment(value,revision,layers){
  const fragment=validateFragment(value,limits),p=fragment.project;
  decoding=false;const project=exportProject();decoding=true;
  checkAdditional(project,usage(p),p.layers.reduce((sum,l)=>sum+(l.width??0)*(l.height??0),0),JSON.stringify(fragment).length,p.layers.length);
  const prepared=remapFragment(fragment,geometryApi.getSkeleton(),Math.max(app.Layers.auto_increment,...config.layers.map(l=>l.id+1))),images=new Map();
  if(prepared.skeleton.groups.flatMap(g=>g.nodes).some(n=>n.x<0||n.y<0||n.x>config.WIDTH||n.y>config.HEIGHT))throw Error('复制骨架超出当前画布，请先扩大画布。');
  for(const [index,layer]of prepared.layers.entries()){layer.order=Math.max(0,...config.layers.map(l=>l.order))+index+1;try{validateLayer(layer);}catch{throw Error('粘贴图层参数或标识超出上限。');}}
  // Decode only referenced image sources; render pieces are validation/export data.
  for(const id of new Set(p.layers.flatMap(l=>[l.assetId,l.restoreAssetId]).filter(Boolean))){const asset=p.assets.find(a=>a.id===id),bytes=Uint8Array.from(atob(asset.pngBase64),c=>c.charCodeAt(0));images.set(id,await decode(new File([bytes],'clipboard.png',{type:'image/png'}),checkSize));}
  ensureReady();ensureSettled();if(layers!==config.layers||revision!==geometryApi.getEditSequence())throw Error('工程已变化，请重新粘贴。');
  for(const layer of prepared.layers){if(layer.type==='image'){layer.link=images.get(layer.assetId);if(layer.restoreAssetId)layer.link_restore=images.get(layer.restoreAssetId);layer.width_original??=layer.link.naturalWidth;layer.height_original??=layer.link.naturalHeight;}delete layer.assetId;delete layer.restoreAssetId;layer.filters=[];}
  // New memberships are applied in the same miniPaint action as image layers.
  decoding=false;const result=await geometryApi.insertClipboard(prepared.layers,prepared.skeleton);if(result?.status!=='aborted')notify.success('已粘贴可编辑图层，可撤销。');
 }
 document.addEventListener('copy',event=>{if(event.defaultPrevented||event.isComposing||protectedInput(event))return;event.preventDefault();event.stopImmediatePropagation();copyCurrent();},true);
 function importFiles(files,source){
  if(pending){notify.error(pendingMessage);return;}
  if(files.length!==1){notify.error(source==='drop'?'请每次拖入一张图片。':'请每次粘贴一张图片。');return;}
  const file=files[0];
  if(!file||!supported.has(file.type)){notify.error(message);return;}
  pendingMessage=source==='drop'?'正在导入图片，请稍候。':'正在粘贴图片，请稍候。';
  pending=true;decoding=true;
  (async()=>{
   ensureReady();ensureSettled();
   const revision=geometryApi.getEditSequence(),layers=config.layers;
   const image=await decode(file,checkSize);
   ensureReady();ensureSettled();
   if(layers!==config.layers||revision!==geometryApi.getEditSequence())throw Error('工程已变化，请重新导入图片。');
   // All remaining preparation and insertion are synchronous up to the normal
   // history action's active-operation guard, so no export can miss this layer.
   decoding=false;
   const project=exportProject(),pixels=image.naturalWidth*image.naturalHeight;
   // Include existing source/restore/render assets and a possible new render
   // piece. Conservative PNG bounds keep the new project exportable.
   const assetPixels=project.assets.reduce((sum,asset)=>{const size=imageSize(Uint8Array.from(atob(asset.pngBase64.slice(0,44)),c=>c.charCodeAt(0)),'image/png');return sum+size.width*size.height;},0);
   if(assetPixels+pixels+config.WIDTH*config.HEIGHT>limits.maxLayerPixels||config.layers.reduce((sum,l)=>sum+(l.width??0)*(l.height??0),0)+pixels>limits.maxLayerPixels)throw Error('图片资源已达上限，请先移除不需要的图层。');
   const scanlines=(config.WIDTH*4+1)*config.HEIGHT,maxPng=scanlines+Math.ceil(scanlines/65535)*5+100;
   if(JSON.stringify(project).length+image.src.length+Math.ceil(maxPng/3)*4+4096>limits.maxProjectBytes)throw Error('工程资源已达上限，请先移除不需要的图层。');
   const scale=Math.min(1,config.WIDTH/image.naturalWidth,config.HEIGHT/image.naturalHeight),width=image.naturalWidth*scale,height=image.naturalHeight*scale;
   const layer={id:app.Layers.auto_increment,parent_id:0,name:source==='drop'?'导入图片':'粘贴图片',type:'image',link:image,x:(config.WIDTH-width)/2,y:(config.HEIGHT-height)/2,width,height,width_original:image.naturalWidth,height_original:image.naturalHeight,visible:true,is_vector:false,hide_selection_if_active:false,opacity:100,order:Math.max(0,...config.layers.map(l=>l.order))+1,composition:'source-over',rotate:0,data:null,params:{},status:null,color:config.COLOR,filters:[],render_function:null};
   const result=await app.State.do_action(insertAction(layer,limits,source));
   if(result?.status==='aborted')return;
   notify.success(source==='drop'?'已导入为新图层。':'已粘贴为新图层。');
  })().catch(error=>notify.error(error.message)).finally(()=>{pending=false;decoding=false;});
 }
 document.addEventListener('paste',event=>{
  if(event.defaultPrevented||protectedInput(event))return;
  // The explicit paste gesture supplies image Files only. Never request general
  // clipboard access, read HTML/text, fetch copied URLs or poll the clipboard.
  const images=[...(event.clipboardData?.items??[])].filter(item=>item.kind==='file'&&item.type.startsWith('image/'));
  if(!images.length)return;
  event.preventDefault();event.stopImmediatePropagation();
  if(pending){notify.error(pendingMessage);return;}
  const files=images.map(item=>item.getAsFile());
  pending=true;decoding=true;pendingMessage='正在粘贴图片，请稍候。';
  let handedOff=false;
  (async()=>{ensureReady();ensureSettled();const revision=geometryApi.getEditSequence(),layers=config.layers;
   const fragment=await clipboardPort.pasteClipboard();
   ensureReady();ensureSettled();if(layers!==config.layers||revision!==geometryApi.getEditSequence())throw Error('工程已变化，请重新粘贴。');
   if(fragment)await pasteFragment(fragment,revision,layers);else{pending=false;decoding=false;handedOff=true;importFiles(files,'paste');}
  })().catch(error=>notify.error(error.message)).finally(()=>{if(!handedOff){pending=false;decoding=false;}});
 },true);
 const hasFiles=event=>[...(event.dataTransfer?.types??[])].includes('Files');
 const onCanvas=event=>!!event.target.closest?.('#main_wrapper');
 document.addEventListener('dragover',event=>{
  if(!hasFiles(event))return;
  // File drags never navigate or replace the editor document. Internal layer
  // reordering and ordinary text drags retain their existing handlers.
  event.preventDefault();event.stopImmediatePropagation();
  event.dataTransfer.dropEffect=onCanvas(event)&&!protectedInput(event)&&!pending?'copy':'none';
 },true);
 document.addEventListener('drop',event=>{
  if(!hasFiles(event))return;
  event.preventDefault();event.stopImmediatePropagation();
  if(!onCanvas(event)||protectedInput(event)){notify.error('请将图片文件拖到画布。');return;}
  // Files are supplied by this one user gesture; never inspect paths, text,
  // HTML, copied URLs or directory contents.
  importFiles([...event.dataTransfer.files],'drop');
 },true);
}

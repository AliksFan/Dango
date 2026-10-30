import {validateLayer} from './project-schema.js';
import {validateDocument,allNodes} from './skeleton-commands.js';

const object=(v,keys)=>v&&typeof v==='object'&&!Array.isArray(v)&&Object.keys(v).length===keys.length&&keys.every(k=>Object.hasOwn(v,k));
const layerKeys=new Set(['id','parent_id','name','type','x','y','width','height','width_original','height_original','visible','is_vector','hide_selection_if_active','opacity','order','composition','rotate','data','params','status','color','render_function','flipX','flipY','assetId','restoreAssetId']);
const ordered=layers=>layers.slice().sort((a,b)=>b.order-a.order).reverse();
export function pngSize(value,limits){
 if(typeof value!=='string'||value.length<44||value.length>Math.ceil(32*1024*1024/3)*4||value.length%4||!/^iVBORw0KGgo[A-Za-z0-9+/]*={0,2}$/.test(value))throw Error('复制图片格式或大小无效。');
 const bytes=Uint8Array.from(atob(value.slice(0,44)),c=>c.charCodeAt(0)),v=new DataView(bytes.buffer);
 const width=v.getUint32(16),height=v.getUint32(20);
 if(v.getUint32(8)!==13||v.getUint32(12)!==0x49484452||!width||!height||width>limits.maxSide||height>limits.maxSide||width*height>limits.maxPixels)throw Error('复制图片尺寸超过上限。');
 return {width,height};
}
export function validateFragment(snapshot,limits){
 if(JSON.stringify(snapshot).length>limits.maxProjectBytes||!object(snapshot,['project','png']))throw Error('可编辑剪贴板格式无效或超出上限。');
 const p=snapshot.project,png=snapshot.png;
 if(!object(p,['schemaVersion','width','height','name','skeleton','layers','assets','renderPieces'])||p.schemaVersion!==2||!object(png,['width','height','pngBase64']))throw Error('可编辑剪贴板格式无效。');
 const size=pngSize(png.pngBase64,limits);
 if(size.width!==p.width||size.height!==p.height||png.width!==p.width||png.height!==p.height||typeof p.name!=='string'||!p.name.trim()||p.name.length>128)throw Error('可编辑剪贴板尺寸或名称无效。');
 if(!Array.isArray(p.layers)||p.layers.length>limits.maxLayers||!Array.isArray(p.assets)||p.assets.length>48||!Array.isArray(p.renderPieces)||p.renderPieces.length>limits.maxLayers||p.layers.length===0&&(p.skeleton?.groups?.length!==1||p.skeleton?.layers?.length!==0||p.assets.length!==0||p.renderPieces.length!==0))throw Error('复制内容超过图层上限。');
 const ids=new Set(),assets=new Map();let pixels=0;
 for(const a of p.assets){if(!object(a,['id','pngBase64'])||!/^asset-[0-9]+$/.test(a.id)||assets.has(a.id))throw Error('复制图片引用无效。');const s=pngSize(a.pngBase64,limits);pixels+=s.width*s.height;if(pixels>limits.maxLayerPixels)throw Error('复制图片资源超过上限。');assets.set(a.id,s);}
 for(const l of p.layers){if(!l||Object.keys(l).some(k=>!layerKeys.has(k)))throw Error('复制图层字段无效。');try{validateLayer(l);}catch{throw Error('复制图层参数无效。');}if(ids.has(l.id))throw Error('复制图层标识重复。');ids.add(l.id);if(l.type==='image'&&!assets.has(l.assetId)||l.restoreAssetId!==undefined&&(!assets.has(l.restoreAssetId)||JSON.stringify(assets.get(l.restoreAssetId))!==JSON.stringify(assets.get(l.assetId))))throw Error('复制图片引用无效。');}
 const skeleton=validateDocument(p.skeleton);
 if(!object(skeleton,['version','pivot','groups','layers'])||!object(skeleton.pivot,['id','x','y'])||skeleton.groups.some(g=>!object(g,['id','name','targetId','nodes','parameters'])||g.nodes.some(n=>!object(n,['id','x','y']))||!object(g.parameters,['bend','damping','restoration','gravity','elasticity']))||skeleton.layers.some(l=>!object(l,['id','groupId']))||allNodes(skeleton).some(n=>n.x<0||n.y<0||n.x>p.width||n.y>p.height))throw Error('复制骨架无效。');
 if(JSON.stringify(skeleton.layers.map(l=>l.id))!==JSON.stringify(ordered(p.layers).map(l=>String(l.id))))throw Error('复制图层顺序无效。');
 const pieces=[];for(const l of ordered(p.layers).filter(l=>l.type!==null&&l.visible&&l.opacity>0)){const gid=skeleton.layers.find(m=>m.id===String(l.id)).groupId;if(!pieces.length||pieces.at(-1)!==gid)pieces.push(gid);}
 if(p.renderPieces.length!==pieces.length||p.renderPieces.some((r,i)=>!object(r,['groupId','assetId'])||r.groupId!==pieces[i]||!assets.has(r.assetId)))throw Error('复制绘制片无效。');
 return structuredClone(snapshot);
}

// Detach external attachments while retaining copied roots' current positions.
export function selectFragment(project,target,png,limits){
 const p=structuredClone(project),ids=new Set(target.kind==='group'?p.skeleton.layers.filter(l=>l.groupId===target.id).map(l=>l.id):[String(target.id)]);
 p.layers=p.layers.filter(l=>ids.has(String(l.id)));if(!p.layers.length&&!(target.kind==='group'&&p.skeleton.groups.some(g=>g.id===target.id)))throw Error('当前图层已不存在。');
 p.skeleton.layers=p.skeleton.layers.filter(l=>ids.has(l.id));
 p.skeleton.groups=target.kind==='group'?p.skeleton.groups.filter(g=>g.id===target.id):[];
 const nodes=new Set(p.skeleton.groups.flatMap(g=>g.nodes.map(n=>n.id)));for(const g of p.skeleton.groups)if(!nodes.has(g.targetId))g.targetId=null;
 if(target.kind!=='group')for(const l of p.skeleton.layers)l.groupId=null;
 const used=new Set(p.layers.flatMap(l=>[l.assetId,l.restoreAssetId]).filter(Boolean));p.assets=p.assets.filter(a=>used.has(a.id));p.renderPieces=[];
 if(p.layers.some(l=>l.visible&&l.opacity>0&&l.type!==null)){const id='asset-'+(Math.max(0,...p.assets.map(a=>Number(a.id.slice(6))))+1);p.assets.push({id,pngBase64:png.pngBase64});p.renderPieces=[{groupId:target.kind==='group'?target.id:null,assetId:id}];}
 return validateFragment({project:p,png},limits);
}

export function remapFragment(fragment,skeleton,nextLayerId,uuid=()=>crypto.randomUUID().replaceAll('-','')){
 const p=structuredClone(fragment.project),nodeIds=new Map(),groupIds=new Map(),layerIds=new Map();
 if(!Number.isInteger(nextLayerId)||nextLayerId<1||nextLayerId+p.layers.length-1>1000000)throw Error('图层标识已达上限。');
 for(const g of p.skeleton.groups){groupIds.set(g.id,'group-'+uuid());for(const n of g.nodes)nodeIds.set(n.id,'node-'+uuid());}
 for(const l of p.layers)layerIds.set(String(l.id),nextLayerId++);
 const groups=p.skeleton.groups.map(g=>({...g,id:groupIds.get(g.id),targetId:nodeIds.get(g.targetId)||null,nodes:g.nodes.map(n=>({...n,id:nodeIds.get(n.id)}))}));
 const layers=ordered(p.layers).map(l=>({...l,id:layerIds.get(String(l.id)),parent_id:0}));
 const members=p.skeleton.layers.map(l=>({id:String(layerIds.get(l.id)),groupId:groupIds.get(l.groupId)||null}));
 return {layers,skeleton:validateDocument({...structuredClone(skeleton),groups:[...skeleton.groups,...groups],layers:[...skeleton.layers,...members]})};
}

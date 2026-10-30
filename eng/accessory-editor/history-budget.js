// Conservative retained JS payload accounting, excluding live DOM/renderer state.
export function retainedBytes(value,seen=new Set()) {
  if(value===null||value===undefined)return 0;
  if(typeof value==='string')return 32+value.length*2;
  if(typeof value==='number'||typeof value==='boolean')return 16;
  if(typeof value!=='object'||seen.has(value))return 0;
  seen.add(value);
  if(value.nodeType!==undefined)return value.tagName==='IMG'?64+(value.naturalWidth||0)*(value.naturalHeight||0)*4+(value.src?.length||0)*2:0;
  if(typeof value.getContext==='function')return 0;
  let bytes=64;
  for(const [key,item] of Object.entries(value)){
    // Runtime singletons/references are not owned undo payloads.
    if(['reference_layer','object','canvas','Base_layers','Base_gui'].includes(key))continue;
    bytes+=32+key.length*2+retainedBytes(item,seen);
  }
  return bytes;
}
export function requireBudget(retained,added,maximum){if(!Number.isFinite(retained+added)||retained+added>maximum)throw Error('Undo payload byte budget exceeded');}

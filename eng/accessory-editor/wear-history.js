// Wearing controls participate in miniPaint's existing history, without exporting image pixels.
export const wearKeys=['bodyAnchorX','bodyAnchorY','sizePercent','baseAngleDegrees','connectionStrength','maxConnectionTravelPercent','rotationDamping','lockRotation','returnStrength','maxSwingDegrees','limitSwing','gravityInfluence','layer'];
const ranges={bodyAnchorX:[0,1],bodyAnchorY:[0,1],sizePercent:[10,400],baseAngleDegrees:[-180,180],connectionStrength:[0,100],maxConnectionTravelPercent:[0,200],rotationDamping:[0,100],returnStrength:[0,100],maxSwingDegrees:[0,180],gravityInfluence:[0,100]};
export function validateWear(value){
 if(!value||typeof value!=='object'||Array.isArray(value)||Object.keys(value).length!==wearKeys.length||!wearKeys.every(key=>Object.hasOwn(value,key)))throw Error('佩戴参数字段无效。');
 for(const [key,[min,max]]of Object.entries(ranges))if(typeof value[key]!=='number'||!Number.isFinite(value[key])||value[key]<min||value[key]>max)throw Error('佩戴参数超出有效范围。');
 if(typeof value.lockRotation!=='boolean'||typeof value.limitSwing!=='boolean'||!['Front','Behind'].includes(value.layer))throw Error('佩戴参数状态无效。');
 return Object.fromEntries(wearKeys.map(key=>[key,value[key]]));
}
export function installWearHistory({State,ensureReady,ensureSettled,onError}){
 let current=null,updatingView=false;
 const show=value=>{updatingView=true;try{window.dispatchEvent(new CustomEvent('dango-wear-state',{detail:{...value}}));}finally{updatingView=false;}};
 window.addEventListener('dango-wear-state',event=>{if(!updatingView){try{current=validateWear(event.detail);}catch{/* Invalid host values are ignored before mutation. */}}});
 async function commit(value){try{
  ensureReady();ensureSettled();const after=validateWear(value);if(!current)throw Error('佩戴参数尚未就绪。');
  const before={...current};if(JSON.stringify(before)===JSON.stringify(after)){show(current);return;}
  const apply=values=>{current={...values};show(current);window.dispatchEvent(new CustomEvent('dango-wear-changed',{detail:{...current}}));};
  const result=await State.do_action({action_id:'wear_parameters',action_description:'修改佩戴参数',async do(){apply(after);},async undo(){apply(before);},async free(){}});
  if(result?.status==='aborted'){show(current);throw Error('佩戴参数未修改，请重试。');}
 }catch(error){if(current)show(current);onError(error.message);}}
 window.addEventListener('dango-wear-change-request',event=>void commit(event.detail));
 window.addEventListener('dango-wear-edit-request',event=>{
  try{const picked=validateWear(event.detail);if(!current)throw Error('佩戴参数尚未就绪。');
   // A native pick edits only A. Other values in that response may predate an
   // already committed browser edit travelling through the other IPC direction.
   void commit({...current,bodyAnchorX:picked.bodyAnchorX,bodyAnchorY:picked.bodyAnchorY});
  }catch(error){onError(error.message);}
 });
 function ensureWearSettled(){const form=document.getElementById('simulation_wear_form');if(!form||form.closest('[hidden]')||!current)return;
  for(const input of form.querySelectorAll('input,select')){if(!input.checkValidity())throw Error('请先修正佩戴参数。');const value=input.type==='checkbox'?input.checked:input.type==='number'?Number(input.value):input.value;if(value!==current[input.name])throw Error('请先按 Enter 或离开输入框确认佩戴参数。');}
 }
 return {get:()=>current&&{...current},ensureSettled:ensureWearSettled};
}

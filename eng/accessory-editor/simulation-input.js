// Native simulation owns pointer input inside its viewport; image hotkeys stay dormant.
export function installSimulationInput({ensureSettled,onError}){
 let active=false;
 const editable=target=>target?.closest?.('input,textarea,select,[contenteditable=true],[role=dialog],[role=menu]');
 let lastFocus;
 function reportFocus(){if(!active)return;const blocked=document.hidden||(document.hasFocus()&&!!editable(document.activeElement))||[...document.querySelectorAll('[role=dialog],[role=menu],dialog[open]')].some(n=>n.checkVisibility());if(blocked===lastFocus)return;lastFocus=blocked;window.dispatchEvent(new CustomEvent('dango-host-command',{detail:{command:'simulation-focus',x:blocked?1:0,y:0}}));}
 const scheduleFocus=()=>queueMicrotask(reportFocus);
 window.addEventListener('dango-editor-simulation-changing',event=>{try{ensureSettled();}catch(error){event.preventDefault();onError(error.message);}});
 window.addEventListener('dango-editor-simulation',event=>{active=!!event.detail;lastFocus=undefined;reportFocus();});
 window.addEventListener('dango-editor-host-ready',()=>{lastFocus=undefined;reportFocus();});
 for(const type of ['focusin','focusout'])document.addEventListener(type,scheduleFocus);
 // A browser button can take native HWND focus without changing the previous
 // reservation value. Reassert the lease after its action, even when it is 0.
 document.addEventListener('click',event=>{if(active&&!editable(event.target)){lastFocus=undefined;scheduleFocus();}});
 for(const type of ['focus','blur'])window.addEventListener(type,scheduleFocus);
 document.addEventListener('visibilitychange',scheduleFocus);
 new MutationObserver(scheduleFocus).observe(document.body,{subtree:true,childList:true,attributes:true,attributeFilter:['hidden','aria-hidden','open']});
 const release=target=>{if(active&&target?.matches?.('input,select')&&target.checkValidity())target.blur();};
 document.addEventListener('change',event=>{if(event.target.matches('input[type=checkbox],select'))release(event.target);});
 document.addEventListener('pointerup',event=>{if(event.target.matches('input[type=range]'))release(event.target);});
 window.addEventListener('keydown',event=>{
  if(!active)return;
  if(editable(event.target)){if(event.key==='Enter'&&!event.isComposing&&event.target.matches('input[type=number]'))queueMicrotask(()=>release(event.target));return;}
  if(event.target?.closest?.('[role=tablist]'))return;
  const key=event.key.toLowerCase(),ctrl=event.ctrlKey||event.metaKey;
  if(ctrl&&(key==='z'||key==='y'))return;
  if(event.key==='Tab'||(event.target?.closest?.('button,[role=button]')&&['Enter',' '].includes(event.key)))return;
  if(event.key==='Escape')window.dispatchEvent(new CustomEvent('dango-host-command',{detail:{command:'simulation-tool',x:0,y:0}}));
  event.preventDefault();event.stopImmediatePropagation();
 },true);
 for(const type of ['paste','copy','cut','drop','dragover'])window.addEventListener(type,event=>{if(active&&!editable(event.target)){event.preventDefault();event.stopImmediatePropagation();}},true);
}

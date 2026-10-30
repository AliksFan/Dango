// Canvas confirmation belongs to the active tool, never the window's Apply.
export function installCanvasConfirm({config,cropTool,isCanvasMode,onError}){
 let composing=false;
 document.addEventListener('compositionstart',()=>{composing=true;},true);
 document.addEventListener('compositionend',()=>{composing=false;},true);
 document.addEventListener('keydown',event=>{
  if(event.key!=='Enter'||event.defaultPrevented||event.repeat||composing||event.isComposing||event.keyCode===229||event.ctrlKey||event.metaKey||event.altKey||event.shiftKey)return;
  if(config.TOOL.name!=='crop'||!isCanvasMode()||config.mouse.is_drag)return;
  const target=event.target;
  if(target.closest('input,textarea,select,[contenteditable]:not([contenteditable=false]),dialog,[role=dialog],[role=menu],summary,a[href]'))return;
  // Clicking the tool then dragging can leave focus on its toolbar button.
  const button=target.closest('button,[role=button]');
  if(button&&button.id!=='crop')return;
  if(document.querySelector('dialog[open],#popups .popup,[role=dialog][aria-modal=true]'))return;
  const {x,y,width,height}=cropTool.selection;
  if(![x,y,width,height].every(Number.isFinite)||width<=0||height<=0)return;
  event.preventDefault();event.stopImmediatePropagation();
  Promise.resolve(cropTool.on_params_update()).catch(error=>onError(error.message));
 },true);
}

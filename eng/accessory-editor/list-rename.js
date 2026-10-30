// List names share one dialog; commands keep the captured kind, ID and revision.
export function installListRename({container,resolve,commit,onError}){
 const dialog=document.createElement('dialog');dialog.id='layer_rename_dialog';dialog.setAttribute('aria-labelledby','layer_rename_title');dialog.setAttribute('role','dialog');
 dialog.innerHTML='<form novalidate><strong id="layer_rename_title">重命名</strong><label for="layer_rename_input">名称</label><input id="layer_rename_input" name="name" autocomplete="off" required><p role="status" aria-live="polite"></p><div class="rename-actions"><button type="button" data-cancel>取消</button><button type="submit">确认</button></div></form>';
 const menu=document.createElement('div');menu.id='layer_rename_menu';menu.setAttribute('role','menu');menu.hidden=true;menu.innerHTML='<button type="button" role="menuitem">重命名</button>';document.body.append(dialog,menu);
 const input=dialog.querySelector('input'),status=dialog.querySelector('[role=status]');let pending=null,menuTarget=null,composing=false,busy=false,returnFocus=null;
 function closeMenu(){menu.hidden=true;menuTarget=null;}
 function open(target){try{const captured=resolve(target);if(!captured)return;closeMenu();pending=captured;returnFocus=document.activeElement;dialog.querySelector('strong').textContent=captured.kind==='group'?'重命名图层组':'重命名图层';input.value=captured.name;status.textContent='';composing=false;dialog.showModal();input.focus();input.select();}catch(error){onError(error.message);}}
 function targetOf(event){const row=event.target.closest('[data-image-layer],[data-group-target]');return row?.dataset.imageLayer?{kind:'layer',id:row.dataset.imageLayer}:row?{kind:'group',id:row.dataset.groupTarget}:null;}
 container.addEventListener('dblclick',event=>{const target=targetOf(event);if(!target||event.target.closest('input,.visibility,.delete,.group-delete'))return;event.preventDefault();event.stopImmediatePropagation();open(target);},true);
 container.addEventListener('contextmenu',event=>{const target=targetOf(event);if(!target)return;event.preventDefault();event.stopImmediatePropagation();menuTarget=target;menu.hidden=false;menu.style.left=Math.max(4,Math.min(event.clientX,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(4,Math.min(event.clientY,innerHeight-menu.offsetHeight-8))+'px';menu.querySelector('button').focus();},true);
 menu.querySelector('button').onclick=()=>{if(menuTarget)open(menuTarget);};
 document.addEventListener('pointerdown',event=>{if(!menu.contains(event.target))closeMenu();},true);
 menu.addEventListener('keydown',event=>{event.stopPropagation();if(event.key==='Escape'){event.preventDefault();closeMenu();container.querySelector('button')?.focus();}});
 input.addEventListener('compositionstart',()=>{composing=true;});input.addEventListener('compositionend',()=>{composing=false;});
 dialog.addEventListener('keydown',event=>{event.stopPropagation();if(event.key==='Escape'){event.preventDefault();if(!busy)dialog.close();}else if(event.key==='Enter'&&(composing||event.isComposing||event.keyCode===229)){event.preventDefault();}},true);
 dialog.querySelector('[data-cancel]').onclick=()=>{if(!busy)dialog.close();};
 dialog.addEventListener('cancel',event=>{if(busy)event.preventDefault();});
 dialog.addEventListener('close',()=>{pending=null;returnFocus?.isConnected&&returnFocus.focus({preventScroll:true});});
 dialog.querySelector('form').onsubmit=async event=>{event.preventDefault();if(busy||composing||!pending)return;const value=input.value.trim();if(!value||value.length>128||/[\u0000-\u001f\u007f-\u009f]/.test(value)){status.textContent='名称须为 1–128 个合法字符。';return;}busy=true;try{await commit(pending,value);dialog.close();}catch(error){status.textContent=error.message;}finally{busy=false;}};
 return {open};
}

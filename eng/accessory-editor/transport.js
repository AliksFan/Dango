// Bounded modal transport. Files, persistence and parameter authority remain native.
import {validateWear} from './wear-history.js';
const messageLimit=256*1024,chunkLimit=128*1024,transferLimit=64*1024*1024;
const timeout=60000,encoder=new TextEncoder(),decoder=new TextDecoder('utf-8',{fatal:true});
const object=(v,names)=>v!==null&&typeof v==='object'&&!Array.isArray(v)&&Object.keys(v).length===names.length&&names.every(k=>Object.hasOwn(v,k));
const hash=async bytes=>Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),b=>b.toString(16).padStart(2,'0')).join('');
const finite=(v,min,max)=>typeof v==='number'&&Number.isFinite(v)&&v>=min&&v<=max;
function geometry(value){
  if(!object(value,['pivotX','pivotY','nodes','requiresRebind'])||typeof value.requiresRebind!=='boolean'||!Array.isArray(value.nodes)||value.nodes.length>17)throw Error('Invalid geometry');
  const min=value.requiresRebind?-8192:0,max=value.requiresRebind?8192:1;
  if(!finite(value.pivotX,min,max)||!finite(value.pivotY,min,max)||value.nodes.some(p=>!object(p,['x','y'])||!finite(p.x,min,max)||!finite(p.y,min,max)))throw Error('Invalid geometry');
  if(value.nodes.length&&(value.nodes[0].x!==value.pivotX||value.nodes[0].y!==value.pivotY))throw Error('Invalid root');
  return value;
}
function fromBase64(value){
  if(typeof value!=='string'||value.length===0||value.length>Math.ceil(chunkLimit/3)*4||value.length%4||!/^[A-Za-z0-9+/]*={0,2}$/.test(value))throw Error('Invalid chunk');
  const binary=atob(value);if(binary.length<1||binary.length>chunkLimit)throw Error('Invalid chunk');
  return Uint8Array.from(binary,c=>c.charCodeAt(0));
}
function toBase64(bytes){let result='';for(let i=0;i<bytes.length;i+=8192)result+=String.fromCharCode(...bytes.subarray(i,i+8192));return btoa(result);}

export function installEditorTransport(api,channel=window.chrome?.webview){
  const unavailable=()=>Promise.reject(Error('当前环境不支持系统剪贴板；请在 Dango 编辑器中使用复制粘贴。'));
  if(!channel?.addEventListener||!channel?.postMessage)return {dispose(){},copyClipboard:unavailable,pasteClipboard:()=>Promise.resolve(null)};
  let identity=null,transfer=null,loaded=false,closed=false,exporting=false,queue=Promise.resolve(),timer=null,epoch=0,changedTimer=null,latestSequence=0;
  let clipboard=null,clipboardSequence=0;
  const clipboardError=()=>{if(clipboard){clearTimeout(clipboard.timer);clipboard.reject(Error('剪贴板操作未完成，请重试。'));clipboard=null;}};
  const clipboardDone=value=>{const current=clipboard;clipboard=null;clearTimeout(current.timer);current.resolve(value);};
  async function clipboardRequest(kind,snapshot){
    if(!loaded||closed||exporting||transfer||clipboard)throw Error('请先结束当前操作，再使用剪贴板。');
    let bytes,sha256;
    if(kind==='copy'){bytes=encoder.encode(JSON.stringify(snapshot));if(bytes.length<1||bytes.length>transferLimit)throw Error('复制内容超过资源上限。');sha256=await hash(bytes);}
    if(!loaded||closed||exporting||transfer||clipboard)throw Error('请先结束当前操作，再使用剪贴板。');
    return new Promise((resolve,reject)=>{const clipboardId=++clipboardSequence;clipboard={clipboardId,kind,resolve,reject,timer:setTimeout(clipboardError,timeout),transfer:null};
      try{if(kind==='paste')send('ClipboardPaste',{clipboardId});else{send('ClipboardCopy',{clipboardId,bytes:bytes.length,sha256});for(let i=0,index=0;i<bytes.length;i+=chunkLimit,index++)send('ClipboardChunk',{clipboardId,index,base64:toBase64(bytes.subarray(i,i+chunkLimit))});}}catch{clipboardError();}
    });
  }
  const same=v=>identity&&v.sessionId===identity.sessionId&&v.requestId===identity.requestId&&v.revision===identity.revision;
  const stopTimer=()=>{if(timer!==null)clearTimeout(timer);timer=null;};
  const send=(type,payload)=>{if(closed||!identity)return;const value={...identity,type,payload};if(encoder.encode(JSON.stringify(value)).length>messageLimit)throw Error('Message too large');channel.postMessage(value);};
  function fail(){clipboardError();epoch++;transfer=null;exporting=false;stopTimer();send('Error',{message:'编辑操作未完成，当前草稿仍保留。请重试或取消。'});}
  const arm=()=>{stopTimer();timer=setTimeout(fail,timeout);};
  async function receive(value){
    if(closed||!object(value,['protocol','sessionId','requestId','revision','type','payload'])||value.protocol!==2||
      !/^[a-f0-9]{32}$/.test(value.sessionId)||!/^[a-f0-9]{32}$/.test(value.requestId)||
      !Number.isSafeInteger(value.revision)||value.revision<0||typeof value.type!=='string'||
      encoder.encode(JSON.stringify(value)).length>messageLimit)return;
    if(value.type==='Open'){
      if(identity&&!same(value))return;
      if(!object(value.payload,['bytes','sha256'])||!Number.isInteger(value.payload.bytes)||value.payload.bytes<1||value.payload.bytes>transferLimit||!/^[a-f0-9]{64}$/.test(value.payload.sha256))throw Error('Invalid transfer');
      identity={protocol:2,sessionId:value.sessionId,requestId:value.requestId,revision:value.revision};
      clipboardError();epoch++;loaded=false;transfer={length:value.payload.bytes,hash:value.payload.sha256,chunks:[],received:0,index:0};arm();return;
    }
    if(!same(value))return;
    if(value.type==='SimulationState'){
      const p=value.payload;
      if(!object(p,['tool','error','updating'])||!['default','pick','pan','zoom'].includes(p.tool)||typeof p.updating!=='boolean'||typeof p.error!=='string'||p.error.length>256)return;
      window.dispatchEvent(new CustomEvent('dango-simulation-state',{detail:p}));return;
    }
    if(value.type==='WearState'||value.type==='WearEdit'){
      let wear;try{wear=validateWear(value.payload);}catch{return;}
      window.dispatchEvent(new CustomEvent(value.type==='WearState'?'dango-wear-state':'dango-wear-edit-request',{detail:wear}));return;
    }
    if(value.type==='Cancel'){clipboardError();epoch++;closed=true;transfer=null;stopTimer();clearTimeout(changedTimer);return;}
    if(value.type==='ClipboardResult'){
      if(!clipboard)return;
      const p=value.payload;
      if(p?.clipboardId!==clipboard.clipboardId)return;
      if(!object(p,['clipboardId','ok','bytes','sha256'])||p.ok!==true||!Number.isInteger(p.bytes)||p.bytes<0||p.bytes>transferLimit||typeof p.sha256!=='string'){clipboardError();return;}
      if(p.bytes===0){if(p.sha256!==''||clipboard.transfer){clipboardError();return;}clipboardDone(null);return;}
      if(clipboard.kind!=='paste'||clipboard.transfer||!/^[a-f0-9]{64}$/.test(p.sha256)){clipboardError();return;}
      clipboard.transfer={length:p.bytes,hash:p.sha256,chunks:[],received:0,index:0};return;
    }
    if(value.type==='ClipboardChunk'){
      const current=clipboard,t=current?.transfer,p=value.payload;if(!t)return;
      if(p?.clipboardId!==current.clipboardId)return;
      try{
        if(!object(p,['clipboardId','index','base64'])||p.index!==t.index)throw Error('Invalid clipboard chunk');
        const bytes=fromBase64(p.base64);if(t.received+bytes.length>t.length)throw Error('Invalid clipboard length');t.chunks.push(bytes);t.received+=bytes.length;t.index++;
        if(t.received===t.length){const all=new Uint8Array(t.length);let offset=0;for(const b of t.chunks){all.set(b,offset);offset+=b.length;}if(await hash(all)!==t.hash)throw Error('Invalid clipboard hash');if(clipboard===current)clipboardDone(JSON.parse(decoder.decode(all)));}
      }catch{clipboardError();}return;
    }
    if(value.type==='Chunk'){
      if(!transfer||!object(value.payload,['index','base64'])||value.payload.index!==transfer.index)throw Error('Invalid chunk');
      const bytes=fromBase64(value.payload.base64);
      if(transfer.received+bytes.length>transfer.length)throw Error('Invalid length');
      transfer.chunks.push(bytes);transfer.received+=bytes.length;transfer.index++;
      if(transfer.received!==transfer.length)return;
      const current=transfer,operation=epoch;transfer=null;
      const all=new Uint8Array(current.length);let offset=0;for(const chunk of current.chunks){all.set(chunk,offset);offset+=chunk.length;}
      if(await hash(all)!==current.hash)throw Error('Invalid hash');
      if(closed||operation!==epoch)return;
      const data=JSON.parse(decoder.decode(all));if(!object(data,['project']))throw Error('Invalid document');
      await api.load(data.project);if(closed||operation!==epoch)return;
      loaded=true;stopTimer();send('Ready',{});window.dispatchEvent(new Event('dango-editor-host-ready'));return;
    }
    if(!loaded||transfer||exporting)return;
    if(value.type==='Confirm'||value.type==='Preview'){
      if(!object(value.payload,[]))throw Error('Invalid command');
      const operation=++epoch;exporting=true;arm();
      try{
        const editSequence=api.getEditSequence();if(!Number.isSafeInteger(editSequence)||editSequence<0)throw Error('Invalid edit sequence');
        const snapshot=await api.exportSnapshot();if(!object(snapshot,['project','png']))throw Error('Invalid snapshot');
        if(closed||operation!==epoch)return;
        const bytes=encoder.encode(JSON.stringify(snapshot));if(bytes.length<1||bytes.length>transferLimit)throw Error('Export budget exceeded');
        const sha256=await hash(bytes);if(closed||operation!==epoch)return;
        send(value.type,{bytes:bytes.length,sha256,editSequence});
        for(let i=0,index=0;i<bytes.length;i+=chunkLimit,index++)send('Chunk',{index,base64:toBase64(bytes.subarray(i,i+chunkLimit))});
      }finally{if(operation===epoch){exporting=false;stopTimer();}}
    }
  }
  const onMessage=event=>{queue=queue.then(()=>receive(event.data)).catch(fail);};
  const onChanged=()=>{
    if(closed||!loaded)return;
    latestSequence=api.getEditSequence();
    if(!Number.isSafeInteger(latestSequence)||latestSequence<0){fail();return;}
    // A hash/export that began before the latest edit may not return old content.
    if(exporting)fail();
    clearTimeout(changedTimer);changedTimer=setTimeout(()=>{changedTimer=null;if(!closed&&loaded)send('Changed',{editSequence:latestSequence});},200);
  };
  channel.addEventListener('message',onMessage);
  window.addEventListener('dango-skeleton-changed',onChanged);
  const onHostCommand=event=>{if(!loaded||closed)return;const p=event.detail;if(!object(p,['command','x','y'])||!['window-drag','preview-show','preview-hide','simulation-tool','simulation-focus','simulation-refresh'].includes(p.command)||p.y!==0||(p.command==='simulation-tool'?(!Number.isInteger(p.x)||p.x<0||p.x>3):p.command==='simulation-focus'?![0,1].includes(p.x):p.x!==0))return;send('ViewCommand',p);};
  const onWearChanged=event=>{if(!loaded||closed)return;let wear;try{wear=validateWear(event.detail);}catch{return;}send('WearChanged',wear);};
  const onViewport=event=>{if(!loaded||closed)return;const p=event.detail;if(!object(p,['x','y','width','height','visible'])||!['x','y','width','height'].every(k=>finite(p[k],0,8192))||typeof p.visible!=='boolean')return;send('SimulationViewport',p);};
  window.addEventListener('dango-host-command',onHostCommand);
  window.addEventListener('dango-wear-changed',onWearChanged);window.addEventListener('dango-simulation-viewport',onViewport);
  return {copyClipboard:snapshot=>clipboardRequest('copy',snapshot),pasteClipboard:()=>clipboardRequest('paste'),dispose(){clipboardError();epoch++;closed=true;transfer=null;stopTimer();clearTimeout(changedTimer);channel.removeEventListener('message',onMessage);window.removeEventListener('dango-skeleton-changed',onChanged);window.removeEventListener('dango-host-command',onHostCommand);window.removeEventListener('dango-wear-changed',onWearChanged);window.removeEventListener('dango-simulation-viewport',onViewport);}};
}

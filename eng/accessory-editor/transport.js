// Bounded modal transport. Files, persistence and parameter authority remain native.
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
  if(!channel?.addEventListener||!channel?.postMessage)return {dispose(){}};
  let identity=null,transfer=null,loaded=false,closed=false,exporting=false,queue=Promise.resolve(),timer=null,epoch=0;
  const same=v=>identity&&v.sessionId===identity.sessionId&&v.requestId===identity.requestId&&v.revision===identity.revision;
  const stopTimer=()=>{if(timer!==null)clearTimeout(timer);timer=null;};
  const send=(type,payload)=>{if(closed||!identity)return;const value={...identity,type,payload};if(encoder.encode(JSON.stringify(value)).length>messageLimit)throw Error('Message too large');channel.postMessage(value);};
  function fail(){epoch++;transfer=null;exporting=false;stopTimer();send('Error',{message:'编辑操作未完成，当前草稿仍保留。请重试或取消。'});}
  const arm=()=>{stopTimer();timer=setTimeout(fail,timeout);};
  async function receive(value){
    if(closed||!object(value,['protocol','sessionId','requestId','revision','type','payload'])||value.protocol!==1||
      !/^[a-f0-9]{32}$/.test(value.sessionId)||!/^[a-f0-9]{32}$/.test(value.requestId)||
      !Number.isSafeInteger(value.revision)||value.revision<0||typeof value.type!=='string'||
      encoder.encode(JSON.stringify(value)).length>messageLimit)return;
    if(value.type==='Open'){
      if(identity&&!same(value))return;
      if(!object(value.payload,['bytes','sha256'])||!Number.isInteger(value.payload.bytes)||value.payload.bytes<1||value.payload.bytes>transferLimit||!/^[a-f0-9]{64}$/.test(value.payload.sha256))throw Error('Invalid transfer');
      identity={protocol:1,sessionId:value.sessionId,requestId:value.requestId,revision:value.revision};
      epoch++;loaded=false;transfer={length:value.payload.bytes,hash:value.payload.sha256,chunks:[],received:0,index:0};arm();return;
    }
    if(!same(value))return;
    if(value.type==='Cancel'){epoch++;closed=true;transfer=null;stopTimer();return;}
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
      const data=JSON.parse(decoder.decode(all));if(!object(data,['project','geometry']))throw Error('Invalid document');
      geometry(data.geometry);await api.load(data.project);if(closed||operation!==epoch)return;
      await api.setGeometry(data.geometry,{reset:true});if(closed||operation!==epoch)return;
      loaded=true;stopTimer();send('Ready',{});return;
    }
    if(!loaded||transfer||exporting)return;
    if(value.type==='SetGeometry'){geometry(value.payload);await api.setGeometry(value.payload);return;}
    if(value.type==='Confirm'||value.type==='Preview'){
      if(!object(value.payload,[]))throw Error('Invalid command');
      const operation=++epoch;exporting=true;arm();
      try{
        const snapshot=await api.exportSnapshot();geometry(snapshot.geometry);
        if(closed||operation!==epoch)return;
        if(snapshot.geometry.requiresRebind)throw Error('Rebinding required');
        const bytes=encoder.encode(JSON.stringify(snapshot));if(bytes.length<1||bytes.length>transferLimit)throw Error('Export budget exceeded');
        const sha256=await hash(bytes);if(closed||operation!==epoch)return;
        send(value.type,{bytes:bytes.length,sha256});
        for(let i=0,index=0;i<bytes.length;i+=chunkLimit,index++)send('Chunk',{index,base64:toBase64(bytes.subarray(i,i+chunkLimit))});
      }finally{if(operation===epoch){exporting=false;stopTimer();}}
    }
  }
  const onMessage=event=>{queue=queue.then(()=>receive(event.data)).catch(fail);};
  const onGeometry=()=>{if(loaded&&!transfer&&!exporting)try{send('Preview',{geometry:geometry(api.getGeometry())});}catch{fail();}};
  channel.addEventListener('message',onMessage);
  window.addEventListener('dango-geometry-changed',onGeometry);
  return {dispose(){epoch++;closed=true;transfer=null;stopTimer();channel.removeEventListener('message',onMessage);window.removeEventListener('dango-geometry-changed',onGeometry);}};
}

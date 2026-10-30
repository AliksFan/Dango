import test from 'node:test';import assert from 'node:assert/strict';import {createHash} from 'node:crypto';
import {installEditorTransport} from './transport.js';
const identity={protocol:2,sessionId:'a'.repeat(32),requestId:'b'.repeat(32),revision:4};
const sha=b=>createHash('sha256').update(b).digest('hex');
const flush=async predicate=>{for(let i=0;i<1000;i++){if(predicate())return;await new Promise(r=>setTimeout(r,1));}assert.fail('Clipboard transport did not settle');};
async function fixture(){
 globalThis.window=new EventTarget();
 class Channel extends EventTarget{messages=[];postMessage(v){this.messages.push(structuredClone(v));}}
 const channel=new Channel(),api={async load(){},getEditSequence:()=>0},port=installEditorTransport(api,channel);
 const send=(type,payload,patch={})=>{if(type.startsWith('Clipboard')&&payload.clipboardId===undefined)payload={clipboardId:channel.messages.findLast(m=>m.type==='ClipboardPaste'||m.type==='ClipboardCopy')?.payload.clipboardId,...payload};channel.dispatchEvent(new MessageEvent('message',{data:{...identity,type,payload,...patch}}));};
 const bytes=Buffer.from('{"project":{}}');send('Open',{bytes:bytes.length,sha256:sha(bytes)});send('Chunk',{index:0,base64:bytes.toString('base64')});await flush(()=>channel.messages.at(-1)?.type==='Ready');channel.messages=[];
 return {port,channel,send};
}
test('explicit copy has bounded ordered chunks and does not change/export the project',async()=>{
 const f=await fixture();try{
  const content={project:{synthetic:'虚构'.repeat(150000)},png:{pngBase64:'fixture'}},pending=f.port.copyClipboard(content);
  await flush(()=>f.channel.messages.at(-1)?.type==='ClipboardChunk');const header=f.channel.messages[0],chunks=f.channel.messages.slice(1),bytes=Buffer.concat(chunks.map(c=>Buffer.from(c.payload.base64,'base64')));
  assert.equal(header.type,'ClipboardCopy');assert.equal(header.payload.bytes,bytes.length);assert.equal(header.payload.sha256,sha(bytes));assert.deepEqual(JSON.parse(bytes),content);assert.ok(chunks.length>1);
  chunks.forEach((c,i)=>{assert.equal(c.type,'ClipboardChunk');assert.equal(c.payload.index,i);assert.ok(Buffer.byteLength(JSON.stringify(c))<=256*1024);});
  f.send('ClipboardResult',{ok:true,bytes:0,sha256:''});assert.equal(await pending,null);
 }finally{f.port.dispose();}
});
test('paste rejects wrong identity, bad hash and out-of-order chunks without falling back to other clipboard data',async()=>{
 const f=await fixture();try{
  let pending=f.port.pasteClipboard();await flush(()=>f.channel.messages.length===1);assert.equal(f.channel.messages[0].type,'ClipboardPaste');
  f.send('ClipboardResult',{ok:false,bytes:0,sha256:''},{sessionId:'c'.repeat(32)});await new Promise(r=>setTimeout(r,5));
  const bytes=Buffer.from('{"project":{"synthetic":true},"png":{}}');f.send('ClipboardResult',{ok:true,bytes:bytes.length,sha256:sha(bytes)});f.send('ClipboardChunk',{index:0,base64:bytes.toString('base64')});assert.deepEqual(await pending,JSON.parse(bytes));
  pending=f.port.pasteClipboard();const rejected=assert.rejects(pending,/未完成/);f.send('ClipboardResult',{clipboardId:1,ok:true,bytes:0,sha256:''});f.send('ClipboardChunk',{clipboardId:1,index:0,base64:bytes.toString('base64')});f.send('ClipboardResult',{ok:true,bytes:bytes.length,sha256:'0'.repeat(64)});f.send('ClipboardChunk',{index:0,base64:bytes.toString('base64')});await rejected;
  pending=f.port.pasteClipboard();const rejectedOrder=assert.rejects(pending,/未完成/);f.send('ClipboardResult',{ok:true,bytes:bytes.length,sha256:sha(bytes)});f.send('ClipboardChunk',{index:1,base64:bytes.toString('base64')});await rejectedOrder;
 }finally{f.port.dispose();}
});
test('clipboard failure, cancel and dispose reject pending operations; no polling or unsolicited requests',async()=>{
 const f=await fixture();assert.deepEqual(f.channel.messages,[]);const pending=f.port.pasteClipboard(),rejected=assert.rejects(pending,/未完成/);f.send('ClipboardResult',{ok:false,bytes:0,sha256:''});await rejected;
 const second=f.port.pasteClipboard(),disposed=assert.rejects(second,/未完成/);f.port.dispose();await disposed;
 const unsupported=installEditorTransport({},null);await assert.rejects(unsupported.copyClipboard({}),/不支持/);assert.equal(await unsupported.pasteClipboard(),null);
});

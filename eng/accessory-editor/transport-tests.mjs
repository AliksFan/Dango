import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {installEditorTransport} from './transport.js';
globalThis.window=new EventTarget();
const geometry={pivotX:.5,pivotY:.25,nodes:[{x:.5,y:.25},{x:.6,y:.8}],requiresRebind:false};
const identity={protocol:1,sessionId:'a'.repeat(32),requestId:'b'.repeat(32),revision:4};
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
const flush=async predicate=>{const until=performance.now()+5000;while(performance.now()<until){if(predicate())return;await new Promise(r=>setTimeout(r,1));}assert.fail('Transport did not settle');};
class Channel extends EventTarget{messages=[];postMessage(value){this.messages.push(structuredClone(value));}}
const channel=new Channel();
let loads=0,currentGeometry=geometry,releaseExport=null;
const snapshot={project:{fixture:'虚构内容'.repeat(40000)},png:{width:8,height:8,pngBase64:'fixture-only'},geometry};
const api={async load(){loads++;},async setGeometry(value){currentGeometry=structuredClone(value);},getGeometry(){return structuredClone(currentGeometry);},
  async exportSnapshot(){if(releaseExport)await new Promise(r=>{releaseExport=r;});return structuredClone(snapshot);}};
const transport=installEditorTransport(api,channel);
const send=(type,payload,patch={})=>channel.dispatchEvent(new MessageEvent('message',{data:{...identity,type,payload,...patch}}));
const documentBytes=Buffer.from(JSON.stringify({project:{width:8,height:8},geometry}));
async function open(bytes=documentBytes,hash=sha(bytes)){
  send('Open',{bytes:bytes.length,sha256:hash});
  send('Chunk',{index:0,base64:bytes.toString('base64')});
  await flush(()=>channel.messages.at(-1)?.type==='Ready'||channel.messages.at(-1)?.type==='Error');
}
await open();assert.equal(loads,1);assert.equal(channel.messages.at(-1).type,'Ready');
const before=channel.messages.length;
send('Confirm',{}, {sessionId:'c'.repeat(32)});await new Promise(r=>setImmediate(r));assert.equal(channel.messages.length,before);
send('Confirm',{});await flush(()=>channel.messages.some(m=>m.type==='Chunk'));
const header=channel.messages.find(m=>m.type==='Confirm'),chunks=channel.messages.filter(m=>m.type==='Chunk');
assert.ok(chunks.length>1,'UTF-8 fixture must cross a chunk boundary');
assert.deepEqual(chunks.map(m=>m.payload.index),chunks.map((_,i)=>i));
const result=Buffer.concat(chunks.map(m=>Buffer.from(m.payload.base64,'base64')));
assert.equal(result.length,header.payload.bytes);assert.equal(sha(result),header.payload.sha256);
assert.deepEqual(JSON.parse(result),snapshot);
for(const m of channel.messages)assert.ok(Buffer.byteLength(JSON.stringify(m))<=256*1024);
channel.messages=[];
await open(documentBytes,'0'.repeat(64));assert.equal(loads,1);assert.equal(channel.messages.at(-1).type,'Error');
channel.messages=[];
await open();assert.equal(loads,2);
channel.messages=[];
send('SetGeometry',{...geometry,pivotX:NaN});await flush(()=>channel.messages.at(-1)?.type==='Error');assert.equal(currentGeometry.pivotX,.5);
channel.messages=[];
send('Open',{bytes:documentBytes.length,sha256:sha(documentBytes)});
send('Chunk',{index:1,base64:documentBytes.toString('base64')});await flush(()=>channel.messages.at(-1)?.type==='Error');assert.equal(loads,2);
channel.messages=[];
await open();channel.messages=[];releaseExport=true;send('Confirm',{});await flush(()=>typeof releaseExport==='function');
const release=releaseExport;transport.dispose();release();await new Promise(r=>setImmediate(r));assert.equal(channel.messages.length,0,'Disposed modal must suppress late export callbacks');
send('Confirm',{});await new Promise(r=>setImmediate(r));assert.equal(channel.messages.length,0);
console.log('PASS: UTF-8/chunk/hash roundtrip, message limits, stale identity, invalid geometry/hash/order, retry and disposed late callbacks.');

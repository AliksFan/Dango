import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {installEditorTransport} from './transport.js';
globalThis.window=new EventTarget();
const identity={protocol:2,sessionId:'a'.repeat(32),requestId:'b'.repeat(32),revision:4};
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
const flush=async predicate=>{const until=performance.now()+5000;while(performance.now()<until){if(predicate())return;await new Promise(r=>setTimeout(r,1));}assert.fail('Transport did not settle');};
class Channel extends EventTarget{messages=[];postMessage(value){this.messages.push(structuredClone(value));}}
const channel=new Channel();let loads=0,loadedProject=null,releaseExport=null,exports=0,editSequence=0;
const snapshot={project:{fixture:'虚构内容'.repeat(40000)},png:{width:8,height:8,pngBase64:'fixture-only'}};
const api={async load(value){loads++;loadedProject=structuredClone(value);},getEditSequence(){return editSequence;},async exportSnapshot(){exports++;if(releaseExport)await new Promise(r=>{releaseExport=r;});return structuredClone(snapshot);}};
const transport=installEditorTransport(api,channel);
const send=(type,payload,patch={})=>channel.dispatchEvent(new MessageEvent('message',{data:{...identity,type,payload,...patch}}));
const project={width:8,height:8,fixture:'local-only'};
const documentBytes=Buffer.from(JSON.stringify({project}));
const delay=ms=>new Promise(r=>setTimeout(r,ms));
const changed=()=>window.dispatchEvent(new Event('dango-skeleton-changed'));
async function open(bytes=documentBytes,hash=sha(bytes),patch={}){
 send('Open',{bytes:bytes.length,sha256:hash},{...patch});
 send('Chunk',{index:0,base64:bytes.toString('base64')},{...patch});
 await flush(()=>channel.messages.at(-1)?.type==='Ready'||channel.messages.at(-1)?.type==='Error');
}
editSequence=1;changed();await delay(230);assert.equal(channel.messages.length,0,'edits before project load must not notify native');
await open();assert.equal(loads,1);assert.equal(channel.messages.at(-1).type,'Ready');assert.deepEqual(loadedProject,project);
assert.deepEqual(channel.messages.at(-1),{...identity,type:'Ready',payload:{}});

// Debounced edit notifications report the latest local sequence only.
channel.messages=[];editSequence=2;changed();await delay(55);editSequence=3;changed();await delay(55);editSequence=4;changed();await delay(230);
assert.deepEqual(channel.messages,[{...identity,type:'Changed',payload:{editSequence:4}}]);

// Wrong identity and stale revision cannot issue commands or load a project.
let before=channel.messages.length;
send('Confirm',{}, {sessionId:'c'.repeat(32)});await new Promise(r=>setImmediate(r));assert.equal(channel.messages.length,before);
send('Open',{bytes:documentBytes.length,sha256:sha(documentBytes)},{revision:identity.revision+1});
send('Chunk',{index:0,base64:documentBytes.toString('base64')},{revision:identity.revision+1});
await new Promise(r=>setImmediate(r));assert.equal(loads,1);assert.equal(channel.messages.length,before);

send('Confirm',{});await flush(()=>channel.messages.some(m=>m.type==='Chunk'));
const header=channel.messages.find(m=>m.type==='Confirm'),chunks=channel.messages.filter(m=>m.type==='Chunk');
assert.ok(chunks.length>1,'UTF-8 snapshot must cross a chunk boundary');
assert.deepEqual(chunks.map(m=>m.payload.index),chunks.map((_,i)=>i));
const result=Buffer.concat(chunks.map(m=>Buffer.from(m.payload.base64,'base64')));
assert.equal(result.length,header.payload.bytes);assert.equal(sha(result),header.payload.sha256);
assert.deepEqual(JSON.parse(result),snapshot);
assert.deepEqual(Object.keys(JSON.parse(result)).sort(),['png','project']);
for(const m of channel.messages)assert.ok(Buffer.byteLength(JSON.stringify(m))<=256*1024);

// Malformed hash, out-of-order chunk and overlength data are real rejected inputs.
channel.messages=[];await open(documentBytes,'0'.repeat(64));assert.equal(loads,1);assert.equal(channel.messages.at(-1).type,'Error');
channel.messages=[];
send('Open',{bytes:documentBytes.length,sha256:sha(documentBytes)});
send('Chunk',{index:1,base64:documentBytes.toString('base64')});await flush(()=>channel.messages.at(-1)?.type==='Error');assert.equal(loads,1);
channel.messages=[];
send('Open',{bytes:documentBytes.length-1,sha256:sha(documentBytes)});
send('Chunk',{index:0,base64:documentBytes.toString('base64')});await flush(()=>channel.messages.at(-1)?.type==='Error');assert.equal(loads,1);

// Correct identity can retry after an invalid transfer; obsolete geometry command has no API path.
channel.messages=[];await open();assert.equal(loads,2);
const beforeGeometryCommand=exports;send('SetGeometry',{pivotX:NaN});await new Promise(r=>setImmediate(r));assert.equal(exports,beforeGeometryCommand);

// An edit during an in-flight snapshot invalidates the old epoch before it can emit Confirm/Chunk.
channel.messages=[];releaseExport=true;send('Confirm',{});await flush(()=>typeof releaseExport==='function');
editSequence=5;changed();assert.deepEqual(channel.messages.map(m=>m.type),['Error']);await delay(230);
assert.deepEqual(channel.messages.map(m=>m.type),['Error','Changed']);
const release=releaseExport;releaseExport=null;release();await delay(20);
assert.deepEqual(channel.messages.map(m=>m.type),['Error','Changed'],'stale export must not emit an old Confirm header or chunks');
assert.ok(channel.messages.every(m=>m.type!=='Confirm'&&m.type!=='Chunk'));

// Cancel closes the session, and disposal suppresses a late async export callback.
const cancelledChannel=new Channel(),cancelled=installEditorTransport(api,cancelledChannel);
const sendCancelled=(type,payload)=>cancelledChannel.dispatchEvent(new MessageEvent('message',{data:{...identity,type,payload}}));
const loadsBeforeCancel=loads;sendCancelled('Open',{bytes:documentBytes.length,sha256:sha(documentBytes)});sendCancelled('Chunk',{index:0,base64:documentBytes.toString('base64')});await flush(()=>cancelledChannel.messages.at(-1)?.type==='Ready');
assert.equal(loads,loadsBeforeCancel+1);cancelledChannel.messages=[];changed();sendCancelled('Cancel',{});await delay(230);assert.equal(cancelledChannel.messages.length,0,'Cancel must clear a pending Changed timer');cancelled.dispose();

const disposedChannel=new Channel(),disposed=installEditorTransport(api,disposedChannel);
disposedChannel.dispatchEvent(new MessageEvent('message',{data:{...identity,type:'Open',payload:{bytes:documentBytes.length,sha256:sha(documentBytes)}}}));
disposedChannel.dispatchEvent(new MessageEvent('message',{data:{...identity,type:'Chunk',payload:{index:0,base64:documentBytes.toString('base64')}}}));await flush(()=>disposedChannel.messages.at(-1)?.type==='Ready');
disposedChannel.messages=[];changed();disposed.dispose();await delay(230);assert.equal(disposedChannel.messages.length,0,'dispose must remove the edit listener and clear its pending timer');

channel.messages=[];releaseExport=true;send('Confirm',{});await flush(()=>typeof releaseExport==='function');
const lateRelease=releaseExport;releaseExport=null;transport.dispose();lateRelease();await delay(20);assert.equal(channel.messages.length,0,'Disposed modal must suppress late export callbacks');
send('Confirm',{});await new Promise(r=>setImmediate(r));assert.equal(channel.messages.length,0);
console.log('PASS: protocol 2 project-only open and project+png snapshot, UTF-8/chunk/hash roundtrip, bounded messages, wrong identity/revision, invalid hash/order/length, Changed debounce/latest sequence, stale-export cancellation, Cancel/dispose timers and late callbacks.');

import test from 'node:test';import assert from 'node:assert/strict';import {createHash} from 'node:crypto';
import {installEditorTransport} from './transport.js';
test('simulation commands, state and viewport use the loaded identity and stop after dispose',async()=>{
 globalThis.window=new EventTarget();const channel=new EventTarget();channel.sent=[];channel.postMessage=v=>channel.sent.push(v);
 const identity={protocol:2,sessionId:'a'.repeat(32),requestId:'b'.repeat(32),revision:1};
 const api={async load(){},getEditSequence:()=>0};const port=installEditorTransport(api,channel);
 const command=()=>window.dispatchEvent(new CustomEvent('dango-host-command',{detail:{command:'window-drag',x:0,y:0}}));command();assert.equal(channel.sent.length,0);
 const send=(type,payload,patch={})=>channel.dispatchEvent(new MessageEvent('message',{data:{...identity,type,payload,...patch}}));
 const bytes=Buffer.from(JSON.stringify({project:{}}));send('Open',{bytes:bytes.length,sha256:createHash('sha256').update(bytes).digest('hex')});send('Chunk',{index:0,base64:bytes.toString('base64')});
 const deadline=performance.now()+5000;while(!channel.sent.length&&performance.now()<deadline)await new Promise(r=>setTimeout(r,5));
 assert.equal(channel.sent.at(-1)?.type,'Ready','Open must finish its asynchronous digest/load before view commands are accepted.');command();assert.equal(channel.sent.at(-1).type,'ViewCommand');assert.equal(channel.sent.at(-1).sessionId,identity.sessionId);
 let frames=0;window.addEventListener('dango-simulation-state',()=>frames++);const payload={tool:'pick',error:'',updating:false};
 send('SimulationState',payload,{sessionId:'f'.repeat(32)});send('SimulationState',{...payload,tool:'jump'});send('SimulationState',payload);await new Promise(r=>setTimeout(r,10));assert.equal(frames,1);
 window.dispatchEvent(new CustomEvent('dango-host-command',{detail:{command:'simulation-tool',x:3,y:0}}));assert.equal(channel.sent.at(-1).payload.x,3);
 for(const x of [1,0]){window.dispatchEvent(new CustomEvent('dango-host-command',{detail:{command:'simulation-focus',x,y:0}}));assert.equal(channel.sent.at(-1).payload.command,'simulation-focus');assert.equal(channel.sent.at(-1).payload.x,x);}
 const beforeInvalidFocus=channel.sent.length;for(const x of [2,-1,true])window.dispatchEvent(new CustomEvent('dango-host-command',{detail:{command:'simulation-focus',x,y:0}}));assert.equal(channel.sent.length,beforeInvalidFocus);
 window.dispatchEvent(new CustomEvent('dango-host-command',{detail:{command:'simulation-refresh',x:0,y:0}}));assert.equal(channel.sent.at(-1).payload.command,'simulation-refresh');const beforeInvalidRefresh=channel.sent.length;window.dispatchEvent(new CustomEvent('dango-host-command',{detail:{command:'simulation-refresh',x:1,y:0}}));assert.equal(channel.sent.length,beforeInvalidRefresh);
 window.dispatchEvent(new CustomEvent('dango-simulation-viewport',{detail:{x:8,y:64,width:600,height:400,visible:true}}));assert.equal(channel.sent.at(-1).type,'SimulationViewport');
 const count=channel.sent.length;port.dispose();command();send('SimulationState',payload);await new Promise(r=>setTimeout(r,10));assert.equal(frames,1);assert.equal(channel.sent.length,count);
});

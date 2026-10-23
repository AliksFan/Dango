import assert from 'node:assert/strict';
import {retainedBytes,requireBudget} from './history-budget.js';
import app from './vendor/src/js/app.js';
import store from './vendor/src/js/actions/store/image-store.js';
import {Update_layer_image_action} from './vendor/src/js/actions/update-layer-image.js';
const imageBefore='data:image/png;base64,synthetic-before';
const imageAfter='data:image/png;base64,synthetic-after';
const layer={type:'image',link:{src:imageBefore}};
const canvas={toDataURL(){return imageAfter;}};
app.Layers={get_layer(){return layer;}};
app.State={check_memory_budget(extra){requireBudget(store.bytes,extra,128);}};
const rejected=new Update_layer_image_action(canvas,1);
await assert.rejects(()=>rejected.do(),/budget/);
assert.equal(layer.link.src,imageBefore);
assert.equal(store.bytes,0);
app.State.check_memory_budget=extra=>requireBudget(store.bytes,extra,4096);
const ImageType=globalThis.Image;
globalThis.Image=class {async decode(){}};
try {
  const action=new Update_layer_image_action(canvas,1);
  await action.do();assert.equal(layer.link.src,imageAfter);
  await action.undo();assert.equal(layer.link.src,imageBefore);
  await action.do();assert.equal(layer.link.src,imageAfter);
  await action.free();assert.equal(store.bytes,0);
}finally {globalThis.Image=ImageType;}
const vector={data:Array.from({length:100},()=>[1,2,3])};
assert.ok(retainedBytes(vector)>5000);
assert.throws(()=>requireBudget(retainedBytes(vector),1,1024),/budget/);
console.log('PASS: reduced undo quota rejects before pixel mutation; undo/redo/free releases image bytes; vector payload accounting exceeds undersized cap.');

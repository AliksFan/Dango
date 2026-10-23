// Dango undo data stays in this page's memory; no IndexedDB or disk persistence.
import app from '../../app.js';
const images=new Map(); let sequence=0,bytes=0;
const byteLimit=64*1024*1024;
const cost=data=>64+2*data.length;
export default {
  async init() {},
  get bytes(){return bytes;},
  check_add(values){const added=values.reduce((n,v)=>n+cost(v),0);if(values.some(v=>typeof v!=='string')||bytes+added>byteLimit)throw Error('Undo image byte budget exceeded');app.State?.check_memory_budget?.(added);},
  async add(data) {this.check_add([data]);const id=String(++sequence);images.set(id,data);bytes+=cost(data);return id;},
  async get(id) { if(!images.has(id))throw Error('Missing undo image');return images.get(id); },
  async delete(id) {if(images.has(id)){bytes-=cost(images.get(id));images.delete(id);}},
  async delete_all() {images.clear();bytes=0;}
};

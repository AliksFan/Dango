// Dango image undo action. Preflight stored byte limit before changing pixels.
import app from '../app.js';
import config from '../config.js';
import imageStore from './store/image-store.js';
import {Base_action} from './base.js';
// Load completion remains usable when WebView2 is hidden; decode() can wait for presentation.
async function loadStoredPng(src){const image=new Image();await new Promise((resolve,reject)=>{image.onload=resolve;image.onerror=()=>reject(Error('Stored PNG could not be loaded'));image.src=src;});return image;}
export class Update_layer_image_action extends Base_action {
  constructor(canvas,layer_id){super('update_layer_image','Update Layer Image');this.canvas=canvas;this.layer_id=layer_id??config.layer.id;this.before=null;this.after=null;this.reference_layer=null;}
  async do(){
    const layer=app.Layers.get_layer(this.layer_id);if(!layer||layer.type!=='image')throw Error('Expected image layer');
    if(!this.after){
      const before=layer.link.src,after=this.canvas.toDataURL('image/png');
      imageStore.check_add([before,after]);
      this.before=await imageStore.add(before);this.after=await imageStore.add(after);this.canvas=null;
    }
    const image=await loadStoredPng(await imageStore.get(this.after));
    this.reference_layer=layer;layer.link=image;config.need_render=true;super.do();
  }
  async undo(){if(this.reference_layer){this.reference_layer.link=await loadStoredPng(await imageStore.get(this.before));config.need_render=true;}super.undo();}
  async free(){if(this.before)await imageStore.delete(this.before);if(this.after)await imageStore.delete(this.after);this.before=this.after=null;this.canvas=null;this.reference_layer=null;}
}

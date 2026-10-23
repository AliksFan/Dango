// Stable subset validation. Run before decoding assets or mutating the editor.
const fonts=new Set(['Arial','Segoe UI','Microsoft YaHei','Courier New','Times New Roman','Verdana','monospace']);
const color=v=>typeof v==='string'&&/^#[0-9a-f]{6}$/i.test(v);
const number=(v,min,max)=>typeof v==='number'&&Number.isFinite(v)&&v>=min&&v<=max;
const record=v=>v!==null&&typeof v==='object'&&!Array.isArray(v);
function keys(value,allowed){return record(value)&&Object.keys(value).every(k=>allowed.includes(k));}
function requireValue(condition){if(!condition)throw Error('Unsupported layer shape');}
export function validateLayer(layer) {
  requireValue(record(layer));
  requireValue([null,'image','brush','text'].includes(layer.type));
  requireValue(Number.isInteger(layer.id)&&layer.id>0&&layer.id<=1000000);
  requireValue(typeof layer.name==='string'&&layer.name.length<=128&&!/[<>]/.test(layer.name));
  requireValue(number(layer.x,-8192,8192)&&number(layer.y,-8192,8192));
  requireValue(number(layer.opacity,0,100)&&typeof layer.visible==='boolean'&&typeof layer.is_vector==='boolean');
  requireValue(number(layer.order,0,1000000)&&layer.composition==='source-over');
  requireValue(layer.rotate===null&&layer.type==='brush'||number(layer.rotate,-360,360));
  requireValue(layer.status===null);
  requireValue(layer.parent_id===undefined||layer.parent_id===0);
  requireValue(layer.hide_selection_if_active===undefined||typeof layer.hide_selection_if_active==='boolean');
  requireValue(color(layer.color));
  if(layer.type===null){
    requireValue([null,0].includes(layer.width)&&[null,0].includes(layer.height));
    requireValue(layer.data===null&&layer.render_function===null&&keys(layer.params,[])&&!layer.is_vector);return;
  }
  requireValue(number(layer.width,0.001,4096)&&number(layer.height,0.001,4096)&&Math.ceil(layer.width)*Math.ceil(layer.height)<=16000000);
  for(const key of ['width_original','height_original'])requireValue(layer[key]===null||number(layer[key],1,4096));
  if(layer.type==='image'){
    requireValue(!layer.is_vector&&layer.render_function===null&&layer.data===null&&keys(layer.params,[]));
    requireValue(typeof layer.assetId==='string'&&/^asset-[0-9]+$/.test(layer.assetId));return;
  }
  requireValue(layer.is_vector&&Array.isArray(layer.render_function)&&layer.render_function.length===2&&layer.render_function[0]===layer.type&&layer.render_function[1]==='render');
  if(layer.type==='brush'){
    requireValue(keys(layer.params,['size','pressure'])&&number(layer.params.size,1,999)&&typeof layer.params.pressure==='boolean');
    requireValue(Array.isArray(layer.data)&&layer.data.length>0&&layer.data.length<=1024);
    let points=0;
    for(const group of layer.data){requireValue(Array.isArray(group)&&group.length>0);points+=group.length;requireValue(points<=32768);for(const point of group)requireValue(Array.isArray(point)&&point.length===3&&number(point[0],-8192,8192)&&number(point[1],-8192,8192)&&number(point[2],0.001,2048));}
    return;
  }
  const modes={boundary:['dynamic','box'],kerning:['metrics','none'],text_direction:['ltr','rtl'],wrap_direction:['ttb'],halign:['left','center','right'],valign:['top','middle','bottom'],wrap:['letter','word']};
  requireValue(keys(layer.params,Object.keys(modes)));
  for(const [key,values] of Object.entries(modes))requireValue(values.includes(layer.params[key]));
  requireValue(Array.isArray(layer.data)&&layer.data.length>0&&layer.data.length<=2048);
  let characters=0,spans=0;
  for(const line of layer.data){requireValue(Array.isArray(line)&&line.length>0);for(const span of line){
    requireValue(keys(span,['text','meta'])&&typeof span.text==='string');characters+=span.text.length;spans++;requireValue(characters<=65536&&spans<=8192);
    requireValue(keys(span.meta,['family','size','kerning','leading','bold','italic','underline','strikethrough','fill_color','stroke_color','stroke_size']));
    for(const [key,value]of Object.entries(span.meta)){
      if(key==='family')requireValue(fonts.has(value));
      else if(key==='size')requireValue(number(value,1,512));
      else if(key==='kerning'||key==='leading')requireValue(number(value,-999,999));
      else if(key==='stroke_size')requireValue(number(value,0,64));
      else if(key.endsWith('_color'))requireValue(color(value));
      else requireValue(typeof value==='boolean');
    }
  }}
}

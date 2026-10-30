// Run only against an already audited checkout of the exact pinned upstream commit.
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import crypto from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url));
const upstream = path.resolve(process.argv[2] ?? '');
const commit = 'a79733eb803fc97084ef0ee4faa96b031e69e1c0';
if (execFileSync('git', ['-C', upstream, 'rev-parse', 'HEAD'], {encoding:'utf8'}).trim() !== commit) throw Error('Unexpected upstream commit');
const tools = ['select','brush','erase','magic_erase','fill','text','crop'];
const sourceHashes = {};
function method(source, name, replacement) {
  const expression = new RegExp('\\n\\t'+name+'\\([^\\n]*\\)\\s*\\{[\\s\\S]*?\\n\\t\\}');
  if (!expression.test(source)) throw Error('Missing method '+name);
  return source.replace(expression, '\n\t'+replacement);
}
function rewrite(name, text) {
  if (name === 'src/js/config.js') {
    // Never copy embedded credentials, even temporarily, into the maintained source tree.
    text = text.replace(/^.*config\.(pixabay_key|google_webfonts_key).*\r?\n/gm, '');
    const sandbox = {};
    vm.runInNewContext(text.replace(/export default config;?/, '')+';this.result=config;', sandbox);
    const c = sandbox.result;
    c.TOOLS = c.TOOLS.filter(t=>tools.includes(t.name));
    c.FONTS = ['Arial','Segoe UI','Microsoft YaHei','Courier New','Times New Roman','Verdana','monospace'];
    c.WIDTH=256;c.HEIGHT=256;c.COLOR='#885e3e';c.TRANSPARENCY=true;c.guides_enabled=false;
    c.enable_autoresize_by_default=false;c.themes=['light'];c.TOOL=c.TOOLS.find(t=>t.name==='brush');
    for (const t of c.TOOLS) for (const [key,v] of Object.entries(t.attributes??{})) {
      if (v && typeof v==='object') delete v.icon;
      if (typeof v?.values==='function') v.values=c.FONTS;
    }
    return '// Curated local miniPaint configuration; no upstream credentials.\nconst config='+JSON.stringify(c,null,2)+';\nexport default config;\n';
  }
  if (name === 'src/js/core/base-gui.js') {
    text=method(text,'load_modules',`load_modules() { this.modules = {}; }`);
    text=method(text,'init_service_worker',`init_service_worker() { /* Local host owns lifecycle. */ }`);
    text=method(text,'autodetect_dimensions',`autodetect_dimensions() { /* Native supplies vetted dimensions. */ }`);
    text=method(text,'load_translations',`load_translations() { /* Local labels only. */ }`);
  }
  if(name==='src/js/core/gui/gui-tools.js') {
    text=tools.map((t,i)=>`import Tool${i} from './../../tools/${t}.js';`).join('\n')+'\n'+text;
    text=method(text,'load_plugins',`load_plugins() {
      const ctx=document.getElementById('canvas_minipaint').getContext('2d');
      const registry={${tools.map((t,i)=>`${t}:Tool${i}`).join(',')}};
      for(const [key, Tool] of Object.entries(registry)) {
        const object=new Tool(ctx); this.tools_modules[key]={key,full_key:key,name:object.name,title:key,object};
        if(object.load) object.load();
      }
    }`);
    text=text.replace('itemDom.title = title;', 'itemDom.title = title; itemDom.textContent = title; itemDom.setAttribute("role","button"); itemDom.tabIndex=0; itemDom.addEventListener("keydown",e=>{if(e.key==="Enter"||e.key===" "){e.preventDefault();itemDom.click();}});');
  }
  if(name==='src/js/tools/text.js') {
    text=text.replace(/^import WebFont.*\r?\n/m,'');
    const start=text.indexOf('function load_font_family('), end=text.indexOf('\n/**',start);
    text=text.slice(0,start)+`function load_font_family({family}, callback) { fontLoadMap.set(family,true); if(callback) requestAnimationFrame(callback); }\n`+text.slice(end);
    const a=text.indexOf('class Google_fonts'), b=text.indexOf('class Text_class');
    if(a<0||b<a) throw Error('Font class boundary changed');
    text=text.slice(0,a)+`class Google_fonts_search_class { show(){ alertify.error('Only installed system fonts are supported.'); } }\n\n`+text.slice(b);
    text=text.replace('insert_text(text, line, character) {',`insert_text(text, line, character) {
      if(text.length>65536 || JSON.stringify(this.lines).length+text.length>131072 || (app.State?.can_add_payload && !app.State.can_add_payload(text.length*8+4096))) { alertify.error('Text or undo budget exceeded.'); return {line,character}; }
    `);
  }
  if(name==='src/js/tools/brush.js') {
    text="import alertify from 'dango-notify';\n"+text;
    text=text.replace(/\/\*\*(?:(?!\*\/)[\s\S])*author: Manoj Verma[\s\S]*?\*\//,'// Dango renders the sampled stroke as round connected segments.');
    text=method(text,'render_stabilized',`render_stabilized(ctx, points) {
      if(!points.length)return;
      ctx.lineJoin='round';ctx.lineCap='round';ctx.beginPath();ctx.moveTo(points[0][0],points[0][1]);
      if(points.length===1){ctx.arc(points[0][0],points[0][1],(points[0][2]||ctx.lineWidth)/2,0,Math.PI*2);ctx.fill();return;}
      for(const point of points.slice(1))ctx.lineTo(point[0],point[1]);ctx.stroke();
    }`);
    text=text.replace('mousedown_action(e, index, event_identifier) {',`async mousedown_action(e, index, event_identifier) {
      this.blockedStroke=false;this.strokePending=false;
      const requiresNew=config.layer.type!==this.name || this.get_params_hash()!==this.params_hash;
      if(config.layers.length>=16 && requiresNew) {this.blockedStroke=true;alertify.error('图层资源不足，已保留当前结果。');return;}
      if(!app.State.can_add_payload(1048576)) {this.blockedStroke=true;alertify.error('撤销资源不足，已保留当前结果。');return;}
    `);
    const strokeStart=text.indexOf('async mousedown_action('),strokeEnd=text.indexOf('\n\tmousemove_action(',strokeStart);
    let stroke=text.slice(strokeStart,strokeEnd);
    stroke=stroke.replaceAll('app.State.do_action(', 'this.strokePending=true;\n\t\t\tconst outcome=await app.State.do_action(');
    stroke=stroke.replace(/(\n\t\t\t\);)/g,"$1\n\t\t\tthis.strokePending=false;if(outcome?.status!=='completed'){this.blockedStroke=true;return;}");
    text=text.slice(0,strokeStart)+stroke+text.slice(strokeEnd);
    text=text.replaceAll('current_group.push([mouse_x',`if(config.layer.data.reduce((n,g)=>n+g.length,0)>=32768 || !app.State.can_add_payload(512)) {alertify.error('Stroke or undo budget exceeded.');return;}
      current_group.push([mouse_x`);
    text=text.replace('mousemove_action(e, index) {',`mousemove_action(e, index) { if(this.blockedStroke||this.strokePending||config.layer.type!=='brush')return;`);
    text=text.replace('mouseup_action(e, index) {',`mouseup_action(e, index) { if(this.blockedStroke||this.strokePending)return;`);
    text=text.replace('width: max_x - min_x,','width: Math.max(1, max_x - min_x),').replace('height: max_y - min_y,','height: Math.max(1, max_y - min_y),');
  }
  if(name==='src/js/core/gui/gui-colors.js') {
    let n=0; text=text.replace(/<svg[\s\S]*?<\/svg>/g,()=>['颜色','通道','色板'][n++]);
  }
  if(name==='src/js/libs/helpers.js') {
    // Remove uncertain credited snippets; independently implement their numeric interfaces.
    text=text.replace(/\/\*[\s\S]*?\*\//g,'').replace(/^.*credits?:.*\r?\n/gim,'');
    text=method(text,'darkenColor',`darkenColor(color, delta) { const value=color.replace(/^#/,''); return '#'+[0,2,4].map(i=>Math.max(0,Math.min(255,parseInt(value.slice(i,i+2),16)+delta)).toString(16).padStart(2,'0')).join(''); }`);
    text=method(text,'hslToRgb',`hslToRgb(h,s,l) { const c=(1-Math.abs(2*l-1))*s; const rgb=this.hsvToRgb(h,c/(l+c/2)||0,l+c/2); return {r:Math.round(rgb.r),g:Math.round(rgb.g),b:Math.round(rgb.b)}; }`);
    text=method(text,'rgbToHsv',`rgbToHsv(r,g,b) { const v=Math.max(r,g,b), d=v-Math.min(r,g,b); let h=0; if(d) h=(v===r?(g-b)/d:v===g?2+(b-r)/d:4+(r-g)/d)/6; return {h:(h+1)%1,s:v?d/v:0,v:v/255}; }`);
    text=method(text,'rgbToHsl',`rgbToHsl(r,g,b) { const q=this.rgbToHsv(r,g,b), l=q.v*(1-q.s/2); return {h:q.h,s:l===0||l===1?0:(q.v-l)/Math.min(l,1-l),l}; }`);
    text=method(text,'hsvToRgb',`hsvToRgb(h,s,v) { const channel=n=>{const k=(n+h*6)%6;return 255*v*(1-s*Math.max(0,Math.min(k,4-k,1)));}; return {r:channel(5),g:channel(3),b:channel(1)}; }`);
    text=method(text,'throttle',`throttle(fn, wait, options={}) { let next=0,timer=null; return function(...args) { const now=Date.now(); if(now>=next && options.leading!==false) { next=now+wait; return fn.apply(this,args); } if(options.trailing!==false) { clearTimeout(timer); timer=setTimeout(()=>{next=Date.now()+wait;fn.apply(this,args);},Math.max(0,next-now)||wait); } }; }`);
    text=method(text,'getCookie',`getCookie(name) { return this.constructor.memory?.[name] ?? null; }`);
    text=method(text,'setCookie',`setCookie(name,value) { (this.constructor.memory ??= {})[name]=value; }`);
    text='// Adapted from miniPaint (MIT); color conversions and throttle replaced by Dango.\n'+text;
  }
  if(name==='src/js/actions/index.js') text=text.replace(/^export .*Stop_animation_action.*\r?\n/gm,'');
  return text.replace(/from\s+['"][^'"]*node_modules\/alertifyjs\/build\/alertify\.min\.js['"]/g,"from 'dango-notify'");
}
const seen=new Set();
function copy(name) {
  name=name.replaceAll('\\','/'); if(seen.has(name))return;seen.add(name);
  const override=path.join(here,'overrides',name);
  const original=fs.readFileSync(path.join(upstream,name),'utf8');
  sourceHashes[name]=crypto.createHash('sha256').update(original).digest('hex');
  let text=fs.existsSync(override)?fs.readFileSync(override,'utf8'):rewrite(name,original);
  const imports=text.replace(/\/\*[\s\S]*?\*\//g,'').replace(/^\s*\/\/.*$/gm,'');
  for(const match of imports.matchAll(/(?:from\s*|import\s*)['"]([^'"]+)['"]/g)) {
    const spec=match[1]; if(!spec.startsWith('.'))continue;
    let target=path.posix.normalize(path.posix.join(path.posix.dirname(name),spec));
    if(!path.posix.extname(target))target+='.js';
    if(target.startsWith('node_modules/'))throw Error('Uncurated dependency '+target);
    copy(target);
  }
  if(name.endsWith('.css'))text=text.replace(/url\([^)]*\)/g,'none');
  const dest=path.join(here,'vendor',name);fs.mkdirSync(path.dirname(dest),{recursive:true});fs.writeFileSync(dest,text.replaceAll('\r\n','\n'));
}
for(const name of ['src/js/app.js','src/js/config.js','src/js/core/base-gui.js','src/js/core/base-layers.js','src/js/core/base-tools.js','src/js/core/base-state.js','src/js/core/components/index.js','src/js/actions/index.js','src/js/modules/image/flip.js','src/js/modules/image/rotate.js'])copy(name);
for(const name of ['reset','utility','component','layout','popup'])copy(`src/css/${name}.css`);
fs.copyFileSync(path.join(upstream,'MIT-LICENSE.txt'),path.join(here,'MINIPAINT-LICENSE.txt'));
fs.writeFileSync(path.join(here,'upstream-sources.json'),JSON.stringify({repository:'https://github.com/viliusle/miniPaint',commit,version:'4.14.3',files:sourceHashes},null,2)+'\n');
console.log(`Imported ${seen.size} curated source files (credentials excluded).`);

import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {build} from 'esbuild';
import {controlledOverrides} from './controlled-overrides.mjs';
const here=path.dirname(fileURLToPath(import.meta.url));
const output=path.resolve(here,'../../src/Dango.Desktop/Accessories/Editing/Web');
fs.mkdirSync(path.join(output,'licenses'),{recursive:true});
const result=await build({plugins:[controlledOverrides],absWorkingDir:here,entryPoints:['entry.js'],bundle:true,minify:true,legalComments:'inline',target:'chrome126',outfile:path.join(output,'bundle.js'),inject:['./jquery-inject.js'],alias:{'dango-notify':path.join(here,'notify.js')},metafile:true,write:false});
for(const file of result.outputFiles){
  const name=file.path.endsWith('.css')?'theme.css':'bundle.js';
  const bytes=name==='theme.css'?Buffer.from(file.text.replace(/url\([^)]*\)/g,'none')):file.contents;
  fs.writeFileSync(path.join(output,name),bytes);
}
fs.copyFileSync(path.join(here,'index.html'),path.join(output,'index.html'));
for(const [source,name] of [['MINIPAINT-LICENSE.txt','MIT-minipaint.txt'],['node_modules/jquery/LICENSE.txt','MIT-jquery.txt']])fs.copyFileSync(path.join(here,source),path.join(output,'licenses',name));
fs.copyFileSync(path.join(here,'NOTICE.txt'),path.join(output,'NOTICE.txt'));
const files=[];
function list(directory,prefix=''){for(const item of fs.readdirSync(directory,{withFileTypes:true})){const rel=prefix+item.name;if(item.isDirectory())list(path.join(directory,item.name),rel+'/');else if(rel!=='manifest.json'){const bytes=fs.readFileSync(path.join(directory,item.name));files.push({path:rel,sha256:crypto.createHash('sha256').update(bytes).digest('hex'),bytes:bytes.length});}}}
list(output);files.sort((a,b)=>a.path.localeCompare(b.path,'en'));
fs.writeFileSync(path.join(output,'manifest.json'),JSON.stringify({sourceCommit:'a79733eb803fc97084ef0ee4faa96b031e69e1c0',version:'4.14.3-dango-e0.1',files},null,2)+'\n');
fs.writeFileSync(path.join(here,'bundle-inputs.json'),JSON.stringify(Object.keys(result.metafile.inputs).sort(),null,2)+'\n');
console.log(`Built ${files.length} shipped resources.`);

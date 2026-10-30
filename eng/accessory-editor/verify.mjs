import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
const here=path.dirname(fileURLToPath(import.meta.url));
const output=path.resolve(here,'../../src/Dango.Desktop/Accessories/Editing/Web');
const manifest=JSON.parse(fs.readFileSync(path.join(output,'manifest.json'),'utf8'));
assert.equal(manifest.sourceCommit,'a79733eb803fc97084ef0ee4faa96b031e69e1c0');
const expected=['NOTICE.txt','bundle.js','index.html','licenses/MIT-jquery.txt','licenses/MIT-minipaint.txt','theme.css'].sort();
assert.deepEqual(manifest.files.map(f=>f.path).sort(),expected);
const actual=[];function walk(dir,prefix=''){for(const entry of fs.readdirSync(dir,{withFileTypes:true})){const rel=prefix+entry.name;if(entry.isDirectory())walk(path.join(dir,entry.name),rel+'/');else actual.push(rel);}}
walk(output);assert.deepEqual(actual.filter(f=>f!=='manifest.json').sort(),expected);
for(const f of manifest.files){const bytes=fs.readFileSync(path.join(output,f.path));assert.equal(bytes.length,f.bytes);assert.equal(crypto.createHash('sha256').update(bytes).digest('hex'),f.sha256);}
const inputs=JSON.parse(fs.readFileSync(path.join(here,'bundle-inputs.json'),'utf8'));
assert.deepEqual(inputs.filter(p=>p.startsWith('node_modules/')),['node_modules/jquery/dist/jquery.slim.js']);
const forbidden=/imagefilters|huxtable|vintage|glfx|color-thief|webfontloader|hermite|pica|gif\.js|pixabay_key|google_webfonts_key|require\.context|indexedDB|localStorage|sessionStorage/i;
const sourceInputs=inputs.filter(p=>p.endsWith('.js')&&!p.startsWith('node_modules/'));
for(const name of sourceInputs){const source=fs.readFileSync(path.join(here,name),'utf8').replace(/\/\*[\s\S]*?\*\//g,'').replace(/^\s*\/\/.*$/gm,'');assert.equal(forbidden.test(source),false,'Disallowed source: '+name);}
for(const name of sourceInputs)assert.equal(/stackoverflow|gist\.github|credits?\s*:/i.test(fs.readFileSync(path.join(here,name),'utf8')),false,'Unresolved external snippet: '+name);
const css=fs.readFileSync(path.join(output,'theme.css'),'utf8');assert.equal(/url\(|@import|@font-face/i.test(css),false);
const html=fs.readFileSync(path.join(output,'index.html'),'utf8');assert.ok(html.includes("connect-src 'none'"));assert.ok(html.includes("default-src 'none'"));
assert.ok(fs.readFileSync(path.join(output,'licenses/MIT-minipaint.txt'),'utf8').includes('Permission is hereby granted'));
assert.ok(fs.readFileSync(path.join(output,'licenses/MIT-jquery.txt'),'utf8').includes('Permission is hereby granted'));
console.log(`PASS: ${manifest.files.length} hashes, exact resource inventory, source/dependency allowlist, local-only CSP and no CSS assets.`);

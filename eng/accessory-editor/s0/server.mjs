import http from 'node:http';
import fs from 'node:fs';
import {fileURLToPath} from 'node:url';
const files=new Map([
 ['/',new URL('./index.html',import.meta.url)],['/sample.js',new URL('./sample.js',import.meta.url)],['/sample.css',new URL('./sample.css',import.meta.url)],
 ['/theme.css',new URL('../theme.css',import.meta.url)],['/skeleton-commands.js',new URL('../skeleton-commands.js',import.meta.url)],['/skeleton-input.js',new URL('../skeleton-input.js',import.meta.url)]
]);
export async function createS0Server() {
 const server=http.createServer((req,res)=>{
  const file=files.get(req.url);if(!file||req.method!=='GET'){res.writeHead(404);res.end();return;}
  res.setHeader('Content-Type',file.pathname.endsWith('.js')?'text/javascript':file.pathname.endsWith('.css')?'text/css':'text/html; charset=utf-8');res.setHeader('Cache-Control','no-store');res.setHeader('X-Content-Type-Options','nosniff');
  res.end(fs.readFileSync(file));
 });
 await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(0,'127.0.0.1',resolve);});return server;
}
if(process.argv[1]===fileURLToPath(import.meta.url)) {
 const server=await createS0Server();console.log(`S0 session-only specimen: http://127.0.0.1:${server.address().port}/`);
 for(const signal of ['SIGINT','SIGTERM'])process.once(signal,()=>server.close());
}

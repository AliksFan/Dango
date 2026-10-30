export const MAX_NODES=17;
export function cloneGeometry(g){return {pivotX:g.pivotX,pivotY:g.pivotY,nodes:g.nodes.map(p=>({...p})),requiresRebind:!!g.requiresRebind};}
export function validateGeometry(input){
 const g=cloneGeometry(input); if(!Number.isFinite(g.pivotX)||!Number.isFinite(g.pivotY)||!Array.isArray(input.nodes)||g.nodes.length>MAX_NODES)throw Error('根部或中心线无效');
 const points=[{x:g.pivotX,y:g.pivotY},...g.nodes];
 if(points.some(p=>!Number.isFinite(p.x)||!Number.isFinite(p.y)||Math.abs(p.x)>8192||Math.abs(p.y)>8192))throw Error('坐标无效');
 if(!g.requiresRebind&&points.some(p=>p.x<0||p.x>1||p.y<0||p.y>1))throw Error('坐标超出画布');
 if(g.nodes.length&& (g.nodes[0].x!==g.pivotX||g.nodes[0].y!==g.pivotY))throw Error('中心线首点必须为根部');
 for(let i=1;i<g.nodes.length;i++)if(Math.hypot(g.nodes[i].x-g.nodes[i-1].x,g.nodes[i].y-g.nodes[i-1].y)<1e-8)throw Error('相邻控制点不能重合');
 return g;
}
export function transformGeometry(g,kind,w,h,crop){
 const map=p=>kind==='flipX'?{x:1-p.x,y:p.y}:kind==='flipY'?{x:p.x,y:1-p.y}:kind==='left'?{x:p.y,y:1-p.x}:{x:(p.x*w-crop.x)/crop.width,y:(p.y*h-crop.y)/crop.height};
 const p=map({x:g.pivotX,y:g.pivotY}),nodes=g.nodes.map(map);
 const outside=[p,...nodes].some(p=>p.x<0||p.x>1||p.y<0||p.y>1);
 return {...p,pivotX:p.x,pivotY:p.y,nodes,requiresRebind:g.requiresRebind||outside};
}

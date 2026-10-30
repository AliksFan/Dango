// Native image pixels, independent of canvas zoom, layer bounds, or DOM state.
export function selectSimilarPixels(data,width,height,x,y,tolerance=15,contiguous=true){
 if(!Number.isInteger(width)||!Number.isInteger(height)||width<1||height<1||data.length!==width*height*4)throw Error('Invalid selection image');
 if(!Number.isFinite(tolerance)||tolerance<0||tolerance>100)throw Error('容差须为 0–100。');
 const mask=new Uint8Array(width*height);let count=0;
 x=Math.floor(x);y=Math.floor(y);
 if(x<0||y<0||x>=width||y>=height)return {mask,count};
 const seed=y*width+x,k=seed*4,threshold=tolerance*255/100;
 const matches=index=>{const i=index*4;if(data[k+3]===0)return data[i+3]===0;if(data[i+3]===0)return false;return Math.abs(data[i]-data[k])<=threshold&&Math.abs(data[i+1]-data[k+1])<=threshold&&Math.abs(data[i+2]-data[k+2])<=threshold&&Math.abs(data[i+3]-data[k+3])<=threshold;};
 if(!contiguous){for(let i=0;i<mask.length;i++)if(matches(i)){mask[i]=1;count++;}return {mask,count};}
 const queue=new Uint32Array(width*height);let read=0,write=1;queue[0]=seed;mask[seed]=1;count=1;
 const visit=i=>{if(mask[i]||!matches(i))return;mask[i]=1;queue[write++]=i;count++;};
 while(read<write){const i=queue[read++],column=i%width;if(column>0)visit(i-1);if(column+1<width)visit(i+1);if(i>=width)visit(i-width);if(i+width<mask.length)visit(i+width);}
 return {mask,count};
}

export function selectionBoundary(mask,width,height){
 const boundary=new Uint8Array(mask.length);
 for(let i=0;i<mask.length;i++)if(mask[i]){const x=i%width;if(!x||x===width-1||i<width||i>=width*(height-1)||!mask[i-1]||!mask[i+1]||!mask[i-width]||!mask[i+width])boundary[i]=1;}
 return boundary;
}

export function constrainPixels(candidate,original,mask){
 if(candidate.length!==original.length||candidate.length!==mask.length*4)throw Error('Selection dimensions changed');
 for(let i=0;i<mask.length;i++)if(!mask[i])candidate.set(original.subarray(i*4,i*4+4),i*4);
 return candidate;
}

export function eraseSelectedPixels(data,mask){
 if(data.length!==mask.length*4)throw Error('Selection dimensions changed');
 for(let i=0;i<mask.length;i++)if(mask[i])data.fill(0,i*4,i*4+4);
 return data;
}

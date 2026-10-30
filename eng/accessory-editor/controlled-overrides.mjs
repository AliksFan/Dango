// Precisely scoped upstream correction; original vendored bytes stay auditable.
import fs from 'node:fs/promises';
export const controlledOverrides={name:'dango-controlled-text-fix',setup(build){build.onLoad({filter:/[\\/]vendor[\\/]src[\\/]js[\\/]tools[\\/]text\.js$/},async args=>{const source=await fs.readFile(args.path,'utf8'),before='let options = options || {};';if(source.split(before).length!==2)throw Error('Expected frozen miniPaint text-render correction point');return {contents:source.replace(before,'const options = {};'),loader:'js'};});}};

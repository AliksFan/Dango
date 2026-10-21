import csv
import json
from pathlib import Path
from collections import Counter

root=Path(__file__).resolve().parent
rows=list(csv.DictReader((root/'trace.csv').open()))
cols=('sequence','input_tick','substep','event','elastic','bending','max_bending','max_row','min_derivative','theta','nx','ny','depth','core_x','core_y','solver_site','iterations','residual')
def brief(row):
    return {c:row[c] for c in cols}
print('events',len(rows),dict(Counter(r['event'] for r in rows)))
print('last',json.dumps(brief(rows[-1])))
for level in (1,10,100,1000,10000):
    first=next((r for r in rows if float(r['max_bending'])>level),None)
    print('first_bend_above',level,json.dumps(brief(first)) if first else 'none')
projections=[]
for a,b in zip(rows,rows[1:]):
    if a['event'].endswith('_before') and b['event']==a['event'].replace('_before','_after'):
        projections.append((float(b['bending'])-float(a['bending']),a,b))
for delta,a,b in sorted(projections,reverse=True,key=lambda x:x[0])[:8]:
    print('projection_delta',delta,'before',json.dumps(brief(a)),'after',json.dumps(brief(b)))
print('tail')
for r in rows[-25:]:
    print(json.dumps(brief(r)))

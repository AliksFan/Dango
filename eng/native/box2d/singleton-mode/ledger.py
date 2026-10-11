"""Recompute actual/fixed ledgers and compare fixed output to frozen reference."""
import argparse, copy, hashlib, importlib.util, json, pathlib, struct

parser = argparse.ArgumentParser()
for name in ['fixed', 'diagnostic', 'layout', 'output']: parser.add_argument('--'+name, required=True)
args = parser.parse_args()
here = pathlib.Path(__file__).resolve().parent
repo = here.parents[3]
spec = importlib.util.spec_from_file_location('retained_endpoint_ledger', here.parent/'endpoint-selection/ledger.py')
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
layout = json.loads(pathlib.Path(args.layout).read_text())
sha = lambda p: hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()

def data(blob, solver, floating, capped, scale):
    read = lambda offset, kind='d': struct.unpack_from('<'+kind, blob, offset)[0]
    vector = lambda offset: list(struct.unpack_from('<2d', blob, offset))
    fv = lambda offset: list(struct.unpack_from('<2f', blob, offset))
    sl, st, rl = layout['solver'], layout['state'], layout['row']
    result = {'nodes': [], 'activeRows': [], 'radialLength': read(solver+st['radial_length'], 'f'), 'capScale': scale}
    for i in range(17):
        result['nodes'].append({'position': fv(solver+st['positions']+8*i),
            'inverseMass': read(solver+st['inverse_mass']+4*i, 'f'),
            'input': vector(solver+sl['input_velocities']+16*i),
            'double': vector(solver+sl['velocities']+16*i),
            'float': fv(floating+136+8*i), 'capped': fv(capped+136+8*i)})
    for i in range(160):
        offset = solver+sl['rows']+i*rl['bytes']
        if not read(offset+rl['active'], 'B'): continue
        count = read(offset+rl['count'], 'i')
        result['activeRows'].append({'id': read(offset+rl['id'], 'i'), 'multiplier': read(offset+rl['multiplier']),
            'gradients': [{'node': read(offset+rl['ids']+4*j, 'i'), 'gradient': vector(offset+rl['gradients']+16*j)} for j in range(count)]})
    return result

fixed = pathlib.Path(args.fixed).read_bytes(); assert len(fixed) == 109528
actual = pathlib.Path(args.diagnostic).read_bytes(); assert len(actual) == 262144
fd = data(fixed, 0, 65904, 66276, 1.)
ad = data(actual, 30008, 4932, 5304, struct.unpack_from('<f', actual, 3804)[0])
reference = json.loads((repo/'artifacts/round-material/endpoint-selection/third-failure/fixed-mode-reference/reference-result.json').read_text())
result = {'kind': 'singleton-independent-double-float-cap-ledger', 'stages': {},
    'fixedReferenceCompleteFieldsEqual': {name: fd[name] == reference[name] for name in ['nodes', 'activeRows', 'radialLength', 'capScale']},
    'fixedAndActualCompleteFieldsEqual': fd == ad,
    'activeCapScaleLessThanOneVerified': False}
for name, values in [('fixed', fd), ('actual', ad)]:
    stages = {'double': module.contact_ledger(values, 'double'), 'float': module.contact_ledger(values, 'float'), 'cap': module.cap_ledger(values)}
    metric_equal = {}
    for report_name, offset in ([('cachedFloat', 22824), ('blockFloat', 23088)] if name == 'fixed' else [('cachedFloat', 52832), ('blockFloat', 95912)]):
        metric_equal[report_name] = {field: stages['float'][field] == struct.unpack_from('<d', fixed if name == 'fixed' else actual, offset+relative)[0] for field, relative in layout['report'].items() if field != 'bytes'}
    corrupt = copy.deepcopy(values); corrupt['nodes'][0]['double'][0] += .001
    stages['velocityNegativeRejected'] = not module.contact_ledger(corrupt, 'double')['passed']
    corrupt = copy.deepcopy(values); corrupt['activeRows'][0]['multiplier'] += .001
    stages['multiplierNegativeRejected'] = not module.contact_ledger(corrupt, 'double')['passed']
    stages['metricEquality'] = metric_equal
    stages['passed'] = all(stages[k]['passed'] for k in ['double', 'float', 'cap']) and stages['velocityNegativeRejected'] and stages['multiplierNegativeRejected'] and all(all(m.values()) for m in metric_equal.values())
    result['stages'][name] = stages
result['passed'] = all(result['fixedReferenceCompleteFieldsEqual'].values()) and result['fixedAndActualCompleteFieldsEqual'] and all(s['passed'] for s in result['stages'].values())
result['inputHashes'] = {key: sha(getattr(args, key)) for key in ['fixed', 'diagnostic', 'layout']}
with open(args.output, 'x', encoding='utf-8') as f: json.dump(result, f, indent=2); f.write('\n')
print(json.dumps({'passed': result['passed'], 'fixedReference': result['fixedReferenceCompleteFieldsEqual'], 'fixedActual': result['fixedAndActualCompleteFieldsEqual']}))
raise SystemExit(0 if result['passed'] else 1)

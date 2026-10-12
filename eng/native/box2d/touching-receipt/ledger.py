"""Independent full binary comparison and impulse/row accounting; no worlds."""
import copy, hashlib, json, math, pathlib, struct

here = pathlib.Path(__file__).resolve().parent
repo = here.parents[3]
own = repo/'artifacts/round-material/touching-receipt'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read_json = lambda p: json.loads(p.read_text())
layout = read_json(own/'layout-v2/layout.json')
timings = read_json(repo/'artifacts/round-material/singleton-mode/layout-v5/layout-full.json')['timingRanges']
namespace = {'__name__': 'independent_accounting'}
accounting_source = here.parent/'endpoint-selection/ledger.py'
exec(compile(accounting_source.read_bytes(), str(accounting_source), 'exec'), namespace)
contact_ledger, cap_ledger = namespace['contact_ledger'], namespace['cap_ledger']
actual_path = own/'probe-v2-fourth/fourth-fixed-event-diagnostic.bin'
test_path = own/'touching-v2-fixed-diagnostic.bin'
reference_path = own/'reference-full-v2/full-output.bin'
actual, test, reference = (p.read_bytes() for p in [actual_path, test_path, reference_path])
assert len(actual) == len(test) == layout['diagnostic']['bytes'] == 200736
assert len(reference) == 65904+372+372+336

def normalize(blob, report_bases, pointer=False):
    result = bytearray(blob)
    if pointer: result[:8] = b'\0'*8
    for base in report_bases:
        for offset, size in timings: result[base+offset:base+offset+size] = b'\0'*size
    return result

def normalize_test_identity(blob):
    result = normalize(blob, [53096, 95912], True)
    result[26:28] = b'\0'*2
    return result

block = layout['diagnostic']['block']
block_size = layout['block']['bytes']
block_report = layout['block']['report']
full_equal = {
    'completeBlockExceptOriginalTimings': normalize(actual[block:block+block_size], [block_report]) == normalize(reference[:block_size], [block_report]),
    'uncappedStateAll372Bytes': actual[4932:4932+372] == reference[65904:65904+372],
    'finalStateAll372Bytes': actual[5304:5304+372] == reference[66276:66276+372],
    'capReportAll336Bytes': actual[29672:29672+336] == reference[66648:66648+336],
    'actualDllAndTestAllBytesExceptSourcePointerWorldGenerationAndOriginalTimings': normalize_test_identity(actual) == normalize_test_identity(test),
    'testOnlyFixtureWorldGenerationDifferenceExpected': struct.unpack_from('<H', actual, 26)[0] == 0 and struct.unpack_from('<H', test, 26)[0] == 1,
}
assert all(full_equal.values()), full_equal

def identity_receipt(blob):
    get = lambda offset, fmt: struct.unpack_from('<'+fmt, blob, offset)[0]
    world = {'index1': get(24, 'H'), 'generation': get(26, 'H')}
    shape = lambda start: {'index1': get(start, 'i'), 'world0': get(start+4, 'H'), 'generation': get(start+6, 'H')}
    ground = shape(28)
    node = next(i for i in range(16) if blob[200716+i])
    node_shape, node_body = shape(36+node*8), shape(164+node*8)
    base = 296+node*112
    certificate = {name: get(base+offset, fmt) for name, offset, fmt in [
        ('contact',0,'i'),('point',4,'i'),('node',12,'i'),('shapeA',16,'i'),('shapeB',20,'i'),
        ('bodyA',24,'i'),('bodyB',28,'i'),('shapeGenA',32,'H'),('shapeGenB',34,'H'),
        ('bodyGenA',36,'H'),('bodyGenB',38,'H'),('sequence',40,'Q'),('step',48,'Q')]}
    ground_a = certificate['shapeA'] == ground['index1']-1
    checks = {
        'groundNodeBodyWorldIndicesMatchWorld': all(item['world0'] == world['index1']-1 for item in [ground,node_shape,node_body]),
        'certificateGroundShapeAndGenerationMatch': certificate['shapeA' if ground_a else 'shapeB'] == ground['index1']-1 and certificate['shapeGenA' if ground_a else 'shapeGenB'] == ground['generation'],
        'certificateNodeShapeAndGenerationMatch': certificate['shapeB' if ground_a else 'shapeA'] == node_shape['index1']-1 and certificate['shapeGenB' if ground_a else 'shapeGenA'] == node_shape['generation'],
        'certificateNodeBodyAndGenerationMatch': certificate['bodyB' if ground_a else 'bodyA'] == node_body['index1']-1 and certificate['bodyGenB' if ground_a else 'bodyGenA'] == node_body['generation'],
        'certificateSequenceAndStepMatchCurrentView': certificate['sequence'] == get(8,'Q') and certificate['step'] == get(16,'Q'),
        'actualWritebackPhase7Authenticated': get(3776,'i') == 7 and get(3780,'i') == 1,
    }
    return {'world':world, 'ground':ground, 'nodeShape':node_shape, 'nodeBody':node_body,
        'certificate':certificate, 'checks':checks, 'passed':all(checks.values()),
        'liveRuntimeProof': 'Each actual phase7 calls dango_tr_context_check; dango_tr_revalidate compares exact world generation to fresh current source, reruns dango_tr_filter/dango_tr_qualify/dango_rh_live, and dango_rh_live validates b2World_IsValid and actual contact/shape/body IDs. Source generation mutations reject in compiled guard tests; no cross-process equality is treated as authorization.'}

identities = {'actualDll':identity_receipt(actual), 'testExe':identity_receipt(test)}
assert all(v['passed'] for v in identities.values()), identities

def decode(blob):
    get = lambda offset, fmt='d': struct.unpack_from('<'+fmt, blob, offset)[0]
    vector = lambda offset, fmt='d': list(struct.unpack_from('<2'+fmt, blob, offset))
    sl, st, rl, sp = (layout[k] for k in ['solver_full', 'state', 'row_full', 'support'])
    solver = block
    data = {'nodes': [], 'activeRows': [], 'allRows': [], 'support': [],
            'radialLength': get(solver+st['radial_length'], 'f'), 'capScale': get(3804, 'f')}
    for i in range(17):
        data['nodes'].append({'position': vector(solver+st['positions']+8*i, 'f'),
            'inverseMass': get(solver+st['inverse_mass']+4*i, 'f'),
            'input': vector(solver+sl['input_velocities']+16*i),
            'double': vector(solver+sl['velocities']+16*i),
            'float': vector(4932+136+8*i, 'f'), 'capped': vector(5304+136+8*i, 'f')})
    for i in range(160):
        start = solver+sl['rows']+i*rl['bytes']
        row = {name: get(start+rl[name], 'i') for name in ['id', 'kind', 'node', 'bound', 'count']}
        row.update({'active': bool(get(start+rl['active'], 'B')), 'gap': get(start+rl['gap']),
            'multiplier': get(start+rl['multiplier']), 'gradients': [
            {'node': get(start+rl['ids']+4*j, 'i'), 'gradient': vector(start+rl['gradients']+16*j)} for j in range(row['count'])]})
        data['allRows'].append(row)
        if row['active']: data['activeRows'].append(row)
    for i in range(16):
        start = solver+sl['support']+i*sp['bytes']
        data['support'].append({name: get(start+sp[name]) for name in ['base_normal', 'base_tangent', 'friction', 'total_normal_telemetry', 'receipt_cone_slack']})
    return data

def rows_check(data, stage):
    metrics = {key: 0. for key in ['maximum_linear_closing', 'maximum_relative_linear_closing', 'maximum_angular_closing', 'maximum_relative_linear_complementarity', 'maximum_angular_complementarity', 'maximum_relative_friction_rate', 'maximum_friction_cone_excess']}
    checks = []
    scale = data['radialLength']
    for row in data['allRows']:
        index, multiplier = row['id'], row['multiplier']
        legal = math.isfinite(multiplier) and (row['active'] or multiplier == 0) and (index >= 144 or multiplier >= 0)
        if not row['active']: checks.append(legal); continue
        rate = sum(sum(term['gradient'][axis]*data['nodes'][term['node']][stage][axis] for axis in range(2)) for term in row['gradients'])
        legal = legal and math.isfinite(rate)
        if index < 144:
            angular = row['kind'] in (3, 4)
            normalized = rate if angular else rate/scale
            closing = max(0., -normalized)
            complementary = max(0., normalized) if multiplier > 0 else 0.
            if angular:
                metrics['maximum_angular_closing'] = max(metrics['maximum_angular_closing'], closing)
                metrics['maximum_angular_complementarity'] = max(metrics['maximum_angular_complementarity'], complementary)
            else:
                metrics['maximum_linear_closing'] = max(metrics['maximum_linear_closing'], max(0., -rate))
                metrics['maximum_relative_linear_closing'] = max(metrics['maximum_relative_linear_closing'], closing)
                metrics['maximum_relative_linear_complementarity'] = max(metrics['maximum_relative_linear_complementarity'], complementary)
            legal = legal and closing <= 1e-5 and complementary <= 1e-5
        else:
            node = index-144; support = data['support'][node]
            physical = support['friction']*(support['base_normal']+data['allRows'][128+node]['multiplier'])
            slack = support['receipt_cone_slack']; bound = physical+slack
            total = support['base_tangent']+multiplier
            epsilon = 32*2**-52*max(bound, abs(total))
            excess = max(0., abs(total)-physical)
            violation = 0.
            if bound > 0:
                if total <= -bound+epsilon: violation = max(0., -rate)
                elif total >= bound-epsilon: violation = max(0., rate)
                else: violation = abs(rate)
            metrics['maximum_friction_cone_excess'] = max(metrics['maximum_friction_cone_excess'], excess)
            metrics['maximum_relative_friction_rate'] = max(metrics['maximum_relative_friction_rate'], violation/scale)
            legal = legal and math.isfinite(bound) and bound >= 0 and excess <= slack+epsilon and violation/scale <= 1e-5
        checks.append(legal)
    return {'passed': all(checks), 'rowsChecked': len(checks), 'metrics': metrics}

data = decode(actual)
stages = {stage: contact_ledger(data, stage) for stage in ['double', 'float']}
stages['cap'] = cap_ledger(data)
rows = {stage: rows_check(data, stage) for stage in ['double', 'float', 'capped']}
metric_equal = {}
ledger_fields = ['energy_before', 'energy_after', 'energy_allowance', 'momentum_residual', 'momentum_allowance', 'angular_momentum_residual', 'angular_momentum_allowance', 'maximum_relative_velocity_ledger_residual', 'maximum_relative_velocity_ledger_allowance']
report_offsets = dict(layout['report'], **layout['report_extra'])
for name, base in [('contactCachedFloat', block+layout['solver_full']['report']), ('blockFinalFloat', 95912)]:
    metric_equal[name] = {key: stages['float'][key] == struct.unpack_from('<d', actual, base+report_offsets[key])[0] for key in ledger_fields}
    metric_equal[name].update({key: rows['float']['metrics'][key] == struct.unpack_from('<d', actual, base+report_offsets[key])[0] for key in rows['float']['metrics']})
metric_equal['capFinalRows'] = {key: rows['capped']['metrics'][key] == struct.unpack_from('<d', actual, 29672+72+report_offsets[key])[0] for key in rows['capped']['metrics']}
negative = {}
corrupt = copy.deepcopy(data); corrupt['nodes'][0]['double'][0] += .001
negative['velocityChangeRejected'] = not contact_ledger(corrupt, 'double')['passed']
corrupt = copy.deepcopy(data); corrupt['activeRows'][0]['multiplier'] += .001
negative['multiplierChangeRejected'] = not contact_ledger(corrupt, 'double')['passed']
corrupt = copy.deepcopy(data); corrupt['allRows'][147]['multiplier'] -= 1.
negative['coneExcessRejected'] = not rows_check(corrupt, 'float')['passed']
pending_node = next(i for i in range(16) if actual[200716+i])
support = data['support'][pending_node]
raw_checks = {'onePending': struct.unpack_from('<i', actual, 200712)[0] == 1,
    'zeroExtraConeSlack': support['receipt_cone_slack'] == 0,
    'bothContactRowsActive': all(data['allRows'][base+pending_node]['active'] for base in [128, 144]),
    'gapExactlyZero': data['allRows'][128+pending_node]['gap'] == 0,
    'capScaleOne': data['capScale'] == 1.}
result = {'kind': 'touching-independent-fixed-complete-bits-and-ledgers', 'fullFieldEquality': full_equal,
    'normalization': {'sourcePointer': [0, 8], 'worldGeneration': [26, 2], 'worldGenerationReason': 'Test creates and destroys an independent genuine two-contact world before the candidate world; actual DLL does not.', 'blockReportTimingRanges': timings, 'fullDiagnosticReportBases': [53096, 95912],
        'fullReferenceBlockReportBase': 23088, 'allOtherBytesCompared': True},
    'pendingNodeObservedNotRuntimeHardcoded': pending_node, 'rawChecks': raw_checks,
    'separateLiveIdentityReceipts': identities,
    'stages': stages, 'rowChecks': rows, 'exportedFloatMetricEquality': metric_equal, 'failureCapableNegatives': negative,
    'doubleReportNote': 'Transient double report is not exported; complete 17-node ledger and 160-row checks recomputed independently.',
    'positiveActiveCapScaleLessThanOneVerified': False,
    'inputHashes': {str(p.relative_to(repo)): sha(p) for p in [actual_path, test_path, reference_path, accounting_source, own/'layout-v2/layout.json']}}
result['passed'] = all(full_equal.values()) and all(raw_checks.values()) and all(s['passed'] for s in stages.values()) and all(s['passed'] for s in rows.values()) and all(all(m.values()) for m in metric_equal.values()) and all(negative.values())
with (own/'ledger-fourth-v2-identity.json').open('x', encoding='utf-8') as stream: json.dump(result, stream, indent=2); stream.write('\n')
print(json.dumps({'passed': result['passed'], 'fullFieldEquality': full_equal, 'stages': {k:v['passed'] for k,v in stages.items()}, 'rows': {k:v['passed'] for k,v in rows.items()}, 'metrics': metric_equal}))
raise SystemExit(0 if result['passed'] else 1)

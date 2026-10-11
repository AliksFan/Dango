"""Third fixed managed-input prefix and current/history lifecycle; no matrix."""
import argparse, ctypes as C, hashlib, json, pathlib, struct

parser = argparse.ArgumentParser()
for name in ['dll', 'layout', 'output']:
    parser.add_argument('--' + name, required=True)
args = parser.parse_args()
repo = pathlib.Path(__file__).resolve().parents[4]
third = repo / 'artifacts/round-material/endpoint-selection/third-failure'
inputs = third / 'managed-input'
out = pathlib.Path(args.output).resolve()
out.mkdir(exist_ok=False)
sha = lambda p: hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
expected = {'config-164.bin': '10aa957243bdeb1d42ee9c095877d238f523ad1f4f945c6691be46fce6122062',
            'controls.bin': 'ba29b57d10eb962a4f87a98ab3d697165b835a6e43b3b7086cb21a8bef00b328',
            'timestep-4.bin': 'f0add3bc38030fcc6e34bd258818830c410477695f25b177103a1ec9c497abd0',
            'manifest.json': '0d2268ee3ede8733cff46eaab9143caab66458f38fca8393d5ed6b55c1617815'}
assert all(sha(inputs / p) == h for p, h in expected.items())
layout = json.loads(pathlib.Path(args.layout).read_text())
tl = layout['trace']; trace_bytes = tl['bytes']
lib = C.CDLL(str(pathlib.Path(args.dll).resolve()))
I, F, V, U, P = C.c_int, C.c_float, C.c_void_p, C.c_uint64, C.POINTER
lib.dango_physics_world_create.argtypes = [V, I, P(V)]
lib.dango_physics_world_add.argtypes = [V, F, F, P(I)]
lib.dango_physics_world_step.argtypes = [V, F, P(I), P(F), I, P(I), P(F), I, P(I), P(I), P(I)]
lib.dango_physics_world_reset.argtypes = [V]
lib.dango_physics_world_destroy.argtypes = [V]
lib.dango_material_contact_get_diagnostic.argtypes = [V, V]
lib.dango_singleton_mode_get_trace.argtypes = [V, V, I, P(I)]
lib.dango_singleton_mode_get_last_event.argtypes = [V, V, I, P(I), P(I)]
lib.dango_singleton_mode_get_status.argtypes = [V, P(U), P(U), P(I)]
config = C.create_string_buffer((inputs / 'config-164.bin').read_bytes(), 164)
controls = (inputs / 'controls.bin').read_bytes(); assert len(controls) == 3463 * 24
dt = F.from_buffer_copy((inputs / 'timestep-4.bin').read_bytes())
handle, entity = V(), I()
assert lib.dango_physics_world_create(config, 1, C.byref(handle)) == 0
assert lib.dango_physics_world_add(handle, 0, 0, C.byref(entity)) == 0

def trace(history=False):
    buf, size, current = C.create_string_buffer(trace_bytes), I(), I(-1)
    if history:
        rc = lib.dango_singleton_mode_get_last_event(handle, buf, len(buf), C.byref(size), C.byref(current))
    else:
        rc = lib.dango_singleton_mode_get_trace(handle, buf, len(buf), C.byref(size))
    assert rc == 0 and size.value == trace_bytes
    return buf.raw, current.value

def status():
    modes, neighbors, size = U(), U(), I()
    assert lib.dango_singleton_mode_get_status(handle, C.byref(modes), C.byref(neighbors), C.byref(size)) == 0
    assert size.value == trace_bytes
    return modes.value, neighbors.value

def facts(raw):
    result = {}
    for field in tl:
        if field in ['bytes', 'original_failure']: continue
        fmt = 'B' if field in ['original_failure_available', 'qualified', 'next_mode_valid', 'block_passed', 'adapter_passed', 'substep_passed', 'snapshot_published'] else ('Q' if field in ['substep', 'sequence', 'frame_token'] else 'i')
        result[field] = struct.unpack_from('<' + fmt, raw, tl[field])[0]
    return result

def normalized(raw):
    data = bytearray(raw)
    for offset, size in layout['timingRanges']: data[offset:offset+size] = bytes(size)
    return bytes(data)

completed = mismatches = fields = 0
with (third / 'v5-rows.bin').open('rb') as old, (out / 'third-rows.bin').open('xb') as rows:
    for frame in range(3463):
        control = (F * 6).from_buffer_copy(controls[frame*24:(frame+1)*24])
        row, sid, count, contacts, latch = (F * 42)(), I(), I(), I(), I()
        rc = lib.dango_physics_world_step(handle, dt, C.byref(entity), control, 1, C.byref(sid), row, 42, C.byref(count), C.byref(contacts), C.byref(latch))
        raw = bytes(row); rows.write(raw); before = old.read(168); assert len(before) == 168
        if frame < 3462:
            fields += 42
            mismatches += sum(raw[i:i+4] != before[i:i+4] for i in range(0, 168, 4))
        assert rc == 0 and latch.value == 0 and count.value == 1 and sid.value == entity.value, (frame+1, rc, latch.value)
        completed += 1
diagnostic = C.create_string_buffer(262144)
assert lib.dango_material_contact_get_diagnostic(handle, diagnostic) == 0
(out / 'third-diagnostic-buffer.bin').write_bytes(diagnostic.raw)
current, _ = trace(); last, is_current = trace(True)
(out / 'third-current-trace.bin').write_bytes(current); (out / 'third-last-event.bin').write_bytes(last)
f = facts(current)
old_report = (third / 'v5-failure-diagnostic-buffer.bin').read_bytes()[95912:95912+42808]
original = current[tl['original_failure']:tl['original_failure']+42808]
original_equal = normalized(original) == normalized(old_report)
assert fields == 145404 and mismatches == 0 and original_equal
assert last == current and is_current == 1 and status() == (1, 0)
assert all(f[k] for k in ['original_failure_available', 'qualified', 'next_mode_valid', 'block_passed', 'adapter_passed', 'substep_passed', 'snapshot_published'])
assert f['changed_row'] == 142 and f['next_mode'] == 1 and f['mode_attempts'] == 1 and f['budget_kind'] == 2
assert f['substep'] == 55408 and f['frame_token'] == 3463 and f['final_error'] == 0
life = {}
small, size, marker = C.create_string_buffer(b'Z'*64, 64), I(), I(47)
life['smallCurrentNoWrite'] = lib.dango_singleton_mode_get_trace(handle, small, 64, C.byref(size)) == -410 and small.raw == b'Z'*64 and size.value == trace_bytes
life['smallHistoryNoWrite'] = lib.dango_singleton_mode_get_last_event(handle, small, 64, C.byref(size), C.byref(marker)) == -410 and small.raw == b'Z'*64 and marker.value == 47
life['nullArgumentsRejected'] = (lib.dango_singleton_mode_get_trace(None, small, 64, C.byref(size)) == -410 and
    lib.dango_singleton_mode_get_trace(handle, None, trace_bytes, C.byref(size)) == -410 and
    lib.dango_singleton_mode_get_trace(handle, small, 64, None) == -410 and
    lib.dango_singleton_mode_get_last_event(handle, small, 64, C.byref(size), None) == -410 and
    lib.dango_singleton_mode_get_status(handle, None, C.byref(U()), C.byref(size)) == -410)
# One explicitly bounded extra frame, repeat the last frozen control only.
assert lib.dango_physics_world_step(handle, dt, C.byref(entity), control, 1, C.byref(sid), row, 42, C.byref(count), C.byref(contacts), C.byref(latch)) == 0 and latch.value == 0
after, _ = trace(); retained, historical = trace(True)
(out / '3464-current-trace.bin').write_bytes(after); (out / '3464-last-event.bin').write_bytes(retained)
life['historicalSeparated'] = not facts(after)['original_failure_available'] and historical == 0 and retained == last and status() == (1, 0)
assert lib.dango_physics_world_reset(handle) == 0
reset, _ = trace(); reset_last, reset_current = trace(True)
life['resetClears'] = not any(reset) and not any(reset_last) and reset_current == 0 and status() == (0, 0)
assert lib.dango_physics_world_destroy(handle) == 0
assert lib.dango_physics_world_create(config, 1, C.byref(handle)) == 0
fresh, _ = trace(); fresh_last, fresh_current = trace(True)
life['newWorldClears'] = not any(fresh) and not any(fresh_last) and fresh_current == 0 and status() == (0, 0)
assert lib.dango_physics_world_destroy(handle) == 0
assert all(life.values()), life
result = {'kind': 'singleton-actual-dll-third-bounded', 'passed': True, 'completedFrames': completed,
          'extraLifecycleFrames': 1, 'dllSha256': sha(args.dll), 'prefixFieldsCompared': fields,
          'prefixBitMismatches': mismatches, 'completeOriginalReportEqualExceptTiming': original_equal,
          'event': f, 'lifecycle': life, 'inputHashes': expected, 'layoutSha256': sha(args.layout),
          'threadStack': 'unmodified Python host thread; no enlarged stack',
          'scope': 'fixed prefix and lifecycle only; no 6660, S1, S2 or S3 matrix'}
(out / 'result.json').write_text(json.dumps(result, indent=2)+'\n')
print(json.dumps(result, indent=2))

"""Bounded real-DLL probes. No physical matrix, material or threshold changes."""
import argparse,ctypes as C,hashlib,json,pathlib,struct,subprocess,sys

parser=argparse.ArgumentParser()
parser.add_argument('--dll',required=True);parser.add_argument('--first-reference',required=True)
parser.add_argument('--output',required=True)
args=parser.parse_args();here=pathlib.Path(__file__).resolve().parent;repo=here.parents[3]
output=pathlib.Path(args.output).resolve();output.mkdir(exist_ok=False)
receipt=repo/'artifacts/round-material/receipt-history';second=receipt/'second-failure'
dll=pathlib.Path(args.dll).resolve();sha=lambda path:hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()
first_command=[sys.executable,str(here.parent/'receipt-history/dll_probe.py'),'--dll',str(dll),'--config',str(receipt/'input-recovery/config-164.bin'),'--reference',args.first_reference,'--output',str(output/'first.json'),'--rows',str(output/'first-rows.bin')]
first=subprocess.run(first_command,capture_output=True)
(output/'first-stdout.bin').write_bytes(first.stdout);(output/'first-stderr.bin').write_bytes(first.stderr)
if first.returncode:raise RuntimeError(('first-prefix',first.returncode,first.stderr.decode(errors='replace')))
lib=C.CDLL(str(dll));I=C.c_int;F=C.c_float;V=C.c_void_p;U=C.c_uint64;P=C.POINTER
lib.dango_physics_world_create.argtypes=[V,I,P(V)];lib.dango_physics_world_add.argtypes=[V,F,F,P(I)]
lib.dango_physics_world_step.argtypes=[V,F,P(I),P(F),I,P(I),P(F),I,P(I),P(I),P(I)]
lib.dango_physics_world_reset.argtypes=[V];lib.dango_physics_world_destroy.argtypes=[V]
lib.dango_material_contact_get_diagnostic.argtypes=[V,V]
lib.dango_endpoint_selection_get_trace.argtypes=[V,V,I,P(I)]
lib.dango_endpoint_selection_get_status.argtypes=[V,P(U),P(U),P(I)]
raw_config=(second/'config-164.bin').read_bytes();assert sha(second/'config-164.bin')=='3dbc84f998b93f0c1b76de55bb5902ad42a1277f2cefa0881cb40f6df8965cfa'
controls=(second/'controls.bin').read_bytes();assert len(controls)==4089*24
config=C.create_string_buffer(raw_config,164);handle=V();entity=I()
assert lib.dango_physics_world_create(config,1,C.byref(handle))==0
assert lib.dango_physics_world_add(handle,0,0,C.byref(entity))==0
counts=U();original_failures=U();trace_bytes=I()
assert lib.dango_endpoint_selection_get_status(handle,C.byref(counts),C.byref(original_failures),C.byref(trace_bytes))==0
assert counts.value==0 and original_failures.value==0 and trace_bytes.value==19112
mismatches=0;fields=0;completed=0
with (second/'candidate-rows.bin').open('rb') as baseline,(output/'second-rows.bin').open('xb') as rows:
    for frame in range(4089):
        control=(F*6).from_buffer_copy(controls[frame*24:(frame+1)*24])
        row=(F*42)();sid=I();count=I();contacts=I();latch=I()
        error=lib.dango_physics_world_step(handle,F(.0166666),C.byref(entity),control,1,C.byref(sid),row,42,C.byref(count),C.byref(contacts),C.byref(latch))
        current=bytes(row);rows.write(current);expected=baseline.read(168);assert len(expected)==168
        if frame<4088:
            fields+=42;mismatches+=sum(current[index:index+4]!=expected[index:index+4] for index in range(0,168,4))
        if error or latch.value or count.value!=1 or sid.value!=entity.value:break
        completed+=1
diagnostic=C.create_string_buffer(262144);assert lib.dango_material_contact_get_diagnostic(handle,diagnostic)==0
(output/'second-diagnostic-buffer.bin').write_bytes(diagnostic.raw)
assert lib.dango_endpoint_selection_get_status(handle,C.byref(counts),C.byref(original_failures),C.byref(trace_bytes))==0
trace=C.create_string_buffer(trace_bytes.value);size=I()
assert lib.dango_endpoint_selection_get_trace(handle,trace,len(trace),C.byref(size))==0 and size.value==len(trace)
(output/'second-last-original-failure-trace.bin').write_bytes(trace.raw)
layout=json.loads((second/'candidate-null-layout.json').read_text());n=layout['null_report'];base=24
read=lambda offset,fmt='i':struct.unpack_from('<'+fmt,trace.raw,base+n[offset])[0]
facts={'originalFailureAvailable':bool(trace.raw[0]),'neighborConstraintsPassed':bool(trace.raw[1]),'originalFailuresInLastSolve':struct.unpack_from('<i',trace.raw,4)[0],'neighborAttemptsInLastSolve':struct.unpack_from('<i',trace.raw,8)[0],'neighborAlpha':struct.unpack_from('<d',trace.raw,16)[0],'status':read('status'),'mode':read('mode_index'),'dimension':read('dimension'),'rank':read('rank'),'nullity':read('nullity'),'emptyRow':read('empty_row_id'),'lower':read('lower','d'),'upper':read('upper','d'),'originalAlpha':read('selected_alpha','d')}
sentinel=C.create_string_buffer(b'Z'*64,64);bad_size=I()
small_rejected=lib.dango_endpoint_selection_get_trace(handle,sentinel,64,C.byref(bad_size))==-410 and bytes(sentinel)==b'Z'*64 and bad_size.value==19112
null_rejected=lib.dango_endpoint_selection_get_trace(None,trace,len(trace),C.byref(size))==-410 and lib.dango_endpoint_selection_get_status(None,C.byref(counts),C.byref(original_failures),C.byref(size))==-410
assert lib.dango_endpoint_selection_get_status(handle,C.byref(counts),C.byref(original_failures),C.byref(trace_bytes))==0
attempts=counts.value;failures=original_failures.value
assert lib.dango_physics_world_reset(handle)==0
assert lib.dango_endpoint_selection_get_status(handle,C.byref(counts),C.byref(original_failures),C.byref(trace_bytes))==0
reset_counts=counts.value==0 and original_failures.value==0
assert lib.dango_endpoint_selection_get_trace(handle,trace,len(trace),C.byref(size))==0
reset_trace=not any(trace.raw)
assert lib.dango_physics_world_destroy(handle)==0
new_handle=V();assert lib.dango_physics_world_create(config,1,C.byref(new_handle))==0
assert lib.dango_endpoint_selection_get_status(new_handle,C.byref(counts),C.byref(original_failures),C.byref(trace_bytes))==0
new_world_counts=counts.value==0 and original_failures.value==0
assert lib.dango_endpoint_selection_get_trace(new_handle,trace,len(trace),C.byref(size))==0
new_world_trace=not any(trace.raw);assert lib.dango_physics_world_destroy(new_handle)==0
result={'kind':'endpoint-actual-dll-bounded-prefix','scope':'native prefix/fixed/lifecycle gates only; not a 6660-frame former-failure gate or physical matrix','dllSha256':sha(dll),'first':json.loads((output/'first.json').read_text()),'second':{'completedFrames':completed,'attemptedFrame':frame+1,'error':error,'latch':latch.value,'prefixFieldsCompared':fields,'prefixBitMismatches':mismatches,'neighborAttemptsTotal':attempts,'originalFailuresTotal':failures,'lastOriginalFailure':facts},'traceAbi':{'bytes':19112,'boolOriginalAvailable':0,'boolNeighborConstraintsPassed':1,'originalFailures':4,'neighborAttempts':8,'neighborAlpha':16,'completeOriginalNullReport':24,'originalNullBytes':19088,'diagnosticBytes':157832,'oldDiagnosticPrefixBytes':138720},'lifecycle':{'undersizedOutputRejectedWithoutWrite':small_rejected,'nullHandleRejected':null_rejected,'resetCountsCleared':reset_counts,'resetTraceCleared':reset_trace,'newWorldCountsCleared':new_world_counts,'newWorldTraceCleared':new_world_trace},'inputHashes':{p.name:sha(p) for p in [second/'config-164.bin',second/'controls.bin',second/'candidate-rows.bin',second/'candidate-null-layout.json']}}
result['passed']=result['first']['passed'] and completed==4089 and fields==171696 and mismatches==0 and error==0 and latch.value==0 and attempts==1 and failures==1 and facts['originalFailureAvailable'] and facts['neighborConstraintsPassed'] and facts['status']==6 and facts['emptyRow']==132 and facts['originalAlpha']==facts['lower'] and facts['neighborAlpha']>facts['originalAlpha'] and all(result['lifecycle'].values())
with (output/'result.json').open('x',encoding='utf-8') as f:json.dump(result,f,indent=2);f.write('\n')
print(json.dumps(result,indent=2));raise SystemExit(0 if result['passed'] else 1)

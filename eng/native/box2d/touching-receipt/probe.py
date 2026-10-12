"""Fourth exact managed boundary, bounded real DLL and event lifecycle."""
import argparse,ctypes as C,pathlib,json,struct,hashlib
ap=argparse.ArgumentParser();ap.add_argument('--dll',required=True);ap.add_argument('--output',required=True);a=ap.parse_args()
here=pathlib.Path(__file__).resolve().parent;repo=here.parents[3];base=repo/'artifacts/round-material/singleton-mode/fourth-failure';m=base/'managed-input';out=pathlib.Path(a.output).resolve();out.mkdir(exist_ok=False)
sha=lambda p:hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
assert sha(m/'manifest.json')=='9b7c40cb3beeda338cbeb5137e6980b538c6dc5f30f057b2a296c2a42cac9148'
frozen=json.loads((m/'manifest.json').read_text())
for f in frozen['blobs']:assert sha(m/f['name']).upper()==f['sha256']
I,F,V,U,P=C.c_int,C.c_float,C.c_void_p,C.c_uint64,C.POINTER;lib=C.CDLL(str(pathlib.Path(a.dll).resolve()))
lib.dango_physics_world_create.argtypes=[V,I,P(V)];lib.dango_physics_world_add.argtypes=[V,F,F,P(I)];lib.dango_physics_world_step.argtypes=[V,F,P(I),P(F),I,P(I),P(F),I,P(I),P(I),P(I)];lib.dango_physics_world_destroy.argtypes=[V];lib.dango_physics_world_reset.argtypes=[V]
lib.dango_touching_receipt_get_status.argtypes=[V,P(I),P(I)];lib.dango_touching_receipt_get_events.argtypes=[V,P(U),P(I)];lib.dango_touching_receipt_get_last.argtypes=[V,V,I,P(I),P(I)];lib.dango_material_contact_get_diagnostic.argtypes=[V,V]
lib.dango_singleton_mode_get_status.argtypes=[V,P(U),P(U),P(I)];lib.dango_endpoint_selection_get_status.argtypes=[V,P(U),P(U),P(I)]
config=C.create_string_buffer((m/'config-164.bin').read_bytes(),164);controls=(m/'controls.bin').read_bytes();dt=F.from_buffer_copy((m/'timestep-4.bin').read_bytes());anchor=struct.unpack('<2f',(m/'initial-anchor-8.bin').read_bytes());h,id=V(),I()
assert lib.dango_physics_world_create(config,1,C.byref(h))==0 and lib.dango_physics_world_add(h,*anchor,C.byref(id))==0
def status(handle):
    pending,size,events=I(),I(),U();assert lib.dango_touching_receipt_get_status(handle,C.byref(pending),C.byref(size))==0 and size.value==200736;assert lib.dango_touching_receipt_get_events(handle,C.byref(events),C.byref(size))==0;return pending.value,events.value
assert status(h)==(0,0);fields=0;mismatches=0
with (base/'singleton-rows.bin').open('rb') as baseline,(out/'fourth-rows.bin').open('xb') as output:
    for frame in range(4446):
        control=(F*6).from_buffer_copy(controls[frame*24:(frame+1)*24]);row=(F*42)();sid,count,contacts,latch=I(),I(),I(),I()
        error=lib.dango_physics_world_step(h,dt,C.byref(id),control,1,C.byref(sid),row,42,C.byref(count),C.byref(contacts),C.byref(latch));assert error==0 and latch.value==0 and count.value==1 and sid.value==id.value
        b=bytes(row);output.write(b);old=baseline.read(168)
        if frame<4445:fields+=42;mismatches+=sum(b[i:i+4]!=old[i:i+4] for i in range(0,168,4))
assert fields==186690 and mismatches==0
pending,events=status(h);assert events==1 and pending==0
buf=C.create_string_buffer(200736);size,current=I(),I()
assert lib.dango_touching_receipt_get_last(h,buf,len(buf),C.byref(size),C.byref(current))==0 and current.value==0
(out/'fourth-fixed-event-diagnostic.bin').open('xb').write(buf.raw)
assert struct.unpack_from('<Q',buf.raw,3808)[0]==71125 and struct.unpack_from('<i',buf.raw,200712)[0]==1
diag=C.create_string_buffer(262144);assert lib.dango_material_contact_get_diagnostic(h,diag)==0;(out/'fourth-frame-diagnostic-buffer.bin').open('xb').write(diag.raw)
mode,neighbor,failures=U(),U(),U();trace_size=I();assert lib.dango_singleton_mode_get_status(h,C.byref(mode),C.byref(neighbor),C.byref(trace_size))==0 and mode.value==neighbor.value==0;assert lib.dango_endpoint_selection_get_status(h,C.byref(neighbor),C.byref(failures),C.byref(trace_size))==0 and failures.value==0
bad=C.create_string_buffer(b'Z'*64,64);small=lib.dango_touching_receipt_get_last(h,bad,64,C.byref(size),C.byref(current))==-410 and bytes(bad)==b'Z'*64 and size.value==200736
null=lib.dango_touching_receipt_get_last(None,buf,len(buf),C.byref(size),C.byref(current))==-410 and lib.dango_touching_receipt_get_status(None,C.byref(size),C.byref(current))==-410
assert small and null
for i in range(6):
    row=(F*42)();control=(F*6)();sid,count,contacts,latch=I(),I(),I(),I();assert lib.dango_physics_world_step(h,dt,C.byref(id),control,1,C.byref(sid),row,42,C.byref(count),C.byref(contacts),C.byref(latch))==0 and latch.value==0
assert lib.dango_physics_world_reset(h)==0 and status(h)==(0,0)
assert lib.dango_touching_receipt_get_last(h,buf,len(buf),C.byref(size),C.byref(current))==0 and not any(buf.raw) and current.value==0
assert lib.dango_physics_world_destroy(h)==0
new=V();assert lib.dango_physics_world_create(config,1,C.byref(new))==0 and status(new)==(0,0)
assert lib.dango_touching_receipt_get_last(new,buf,len(buf),C.byref(size),C.byref(current))==0 and not any(buf.raw);assert lib.dango_physics_world_destroy(new)==0
result={'kind':'touching-real-dll-fourth-finite-prefix','passed':True,'dllSha256':sha(a.dll),'completedFrames':4446,'prefixFieldsCompared':fields,'prefixBitMismatches':mismatches,'touchingEvents':events,'fixedEventSubstep':71125,'fixedEventHistoricalAfterFrame':True,'extraLifecycleFrames':6,'modeAttempts':mode.value,'neighborAttempts':neighbor.value,'originalFailures':failures.value,'lifecycle':{'smallBufferRejectedWithoutWrite':small,'nullHandleRejected':null,'resetCleared':True,'newWorldCleared':True},'capScaleLessThanOnePositiveVerified':False,'managedManifestSha256':sha(m/'manifest.json'),'scope':'Native finite exact prefix only; no6660/S1/S2/S3/matrix/manual acceptance'}
with (out/'result.json').open('x',encoding='utf-8') as f:json.dump(result,f,indent=2);f.write('\n')
print(json.dumps(result))

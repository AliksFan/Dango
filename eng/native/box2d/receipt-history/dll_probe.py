"""Fresh actual-DLL trajectory and comparison to the source executable output."""
import argparse,ctypes,hashlib,json,math,pathlib,struct
ap=argparse.ArgumentParser(); ap.add_argument('--dll',required=True); ap.add_argument('--config',required=True); ap.add_argument('--reference',required=True); ap.add_argument('--output',required=True); ap.add_argument('--rows',required=True); a=ap.parse_args()
lib=ctypes.CDLL(str(pathlib.Path(a.dll).resolve())); P=ctypes.POINTER; I=ctypes.c_int; F=ctypes.c_float; V=ctypes.c_void_p; U=ctypes.c_uint64
lib.dango_physics_world_create.argtypes=[V,I,P(V)]; lib.dango_physics_world_add.argtypes=[V,F,F,P(I)]; lib.dango_physics_world_step.argtypes=[V,F,P(I),P(F),I,P(I),P(F),I,P(I),P(I),P(I)]; lib.dango_physics_world_destroy.argtypes=[V]; lib.dango_receipt_history_get_status.argtypes=[V,P(I),P(I),P(U),P(U)]
raw_config=pathlib.Path(a.config).read_bytes(); assert len(raw_config)==164
config=ctypes.create_string_buffer(raw_config,len(raw_config)); h=V(); entity=I()
assert lib.dango_physics_world_create(config,1,ctypes.byref(h))==0
assert lib.dango_physics_world_add(h,0,5/3,ctypes.byref(entity))==0
controls=(F*6)(); row=(F*42)(); sid=I(); count=I(); contacts=I(); latch=I(); phase=I(); status=I(); events=U(); seq=U()
completed=0; mismatches=0; error=0; values_compared=0
with open(a.reference,'rb') as ref, open(a.rows,'xb') as out:
    for i in range(6000):
        controls[3]=1 if i>=600 and i%600<480 else 0; controls[4]=-3.25+3.75*math.sin(i*.043); controls[5]=10/3-25/6*math.cos(i*.071)
        error=lib.dango_physics_world_step(h,F(.0166666),ctypes.byref(entity),controls,1,ctypes.byref(sid),row,42,ctypes.byref(count),ctypes.byref(contacts),ctypes.byref(latch))
        current=bytes(row); out.write(current); expected=ref.read(168); assert len(expected)==168
        mismatches+=sum(current[j:j+4]!=expected[j:j+4] for j in range(0,168,4)); values_compared+=42
        if error or latch.value or count.value!=1 or sid.value!=entity.value: break
        completed+=1
assert lib.dango_receipt_history_get_status(h,ctypes.byref(phase),ctypes.byref(status),ctypes.byref(events),ctypes.byref(seq))==0
assert lib.dango_physics_world_destroy(h)==0
result={'schema':1,'kind':'actual-dll-changing-6000','completedFrames':completed,'attemptedFrame':i+1,'error':error,'latch':latch.value,'fieldsCompared':values_compared,'bitMismatches':mismatches,'historyEvents':events.value,'historyPhase':phase.value,'historyStatus':status.value,'sequence':seq.value,'dllSha256':hashlib.sha256(pathlib.Path(a.dll).read_bytes()).hexdigest(),'configSha256':hashlib.sha256(raw_config).hexdigest(),'referenceRowsSha256':hashlib.sha256(pathlib.Path(a.reference).read_bytes()).hexdigest(),'actualRowsSha256':hashlib.sha256(pathlib.Path(a.rows).read_bytes()).hexdigest(),'passed':completed==6000 and error==0 and latch.value==0 and mismatches==0 and events.value==1 and status.value==1}
with open(a.output,'x',encoding='utf-8') as f: json.dump(result,f,indent=2); f.write('\n')
print(json.dumps(result)); raise SystemExit(0 if result['passed'] else 1)

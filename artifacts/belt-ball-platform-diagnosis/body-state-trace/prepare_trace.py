from pathlib import Path
import shutil
import hashlib
import json

root = Path(__file__).resolve().parents[3]
out = Path(__file__).resolve().parent
src = out / 'src'
src.mkdir(exist_ok=True)
manifest = {}
for p in (root / 'eng/native/box2d').iterdir():
    if p.is_file() and p.suffix in ('.c', '.h'):
        shutil.copy2(p, src / p.name)
        manifest[p.name] = hashlib.sha256(p.read_bytes()).hexdigest()
(out / 'source-hashes.json').write_text(json.dumps(manifest, indent=2))

def edit(name, old, new):
    p = src / name
    s = p.read_text()
    assert s.count(old) == 1, (name, old, s.count(old))
    p.write_text(s.replace(old, new))

decl = '\nstatic void state_trace(const char*,const belt_body*,double,double,double,double);\n'
edit('dango_belt.c', '} belt_body;', '} belt_body;' + decl)
edit('dango_belt.c', 'b->solver_diag[15]=2;if(!belt_integrate(b,h,f))goto fault;',
     'b->solver_diag[15]=2;state_trace("before_integrate",b,0,0,0,0);if(!belt_integrate(b,h,f)){state_trace("integrate_failed",b,0,0,0,0);goto fault;}state_trace("after_integrate",b,0,0,0,0);')
edit('dango_belt.c', 'if(!BELT_CONTACT_DISPATCH(b))goto fault;b->solver_diag[15]=4;',
     'if(!BELT_CONTACT_DISPATCH(b)){state_trace("contact_failed",b,0,0,0,0);goto fault;}state_trace("after_contact",b,0,0,0,0);b->solver_diag[15]=4;')
edit('dango_belt_room.c', 'double elastic=belt_elastic_energy(b),gravity=belt_gravity_energy(b);memcpy(b->q,trial,sizeof trial);',
     'state_trace("static_before",b,theta,normal.x,normal.y,penetration);double elastic=belt_elastic_energy(b),gravity=belt_gravity_energy(b);memcpy(b->q,trial,sizeof trial);state_trace("static_after",b,theta,normal.x,normal.y,penetration);')
edit('dango_belt_room_collection_impl.h', 'double elastic=belt_elastic_energy(b),gravity=belt_gravity_energy(b),by=a->ball.y;memcpy(b->q,trial,sizeof trial);',
     'state_trace("ball_before",b,theta,n.x,n.y,depth);double elastic=belt_elastic_energy(b),gravity=belt_gravity_energy(b),by=a->ball.y;memcpy(b->q,trial,sizeof trial);state_trace("ball_after",b,theta,n.x,n.y,depth);')

(out / 'CMakeLists.txt').write_text('cmake_minimum_required(VERSION 3.22)\nproject(body_state_trace C)\nset(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")\nadd_executable(trace_body trace.c)\ntarget_compile_features(trace_body PRIVATE c_std_17)\nadd_executable(inspect_stage inspect-stage.c)\ntarget_compile_features(inspect_stage PRIVATE c_std_17)\n')
print('Prepared diagnostic source copy; production files unchanged')

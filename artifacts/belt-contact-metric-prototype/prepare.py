"""Build an isolated candidate from the bound production sources; preserve old evidence."""
from pathlib import Path
import csv
import hashlib
import json
import re
import shutil

out = Path(__file__).resolve().parent
root = out.parents[1]
src = out / 'src'
src.mkdir(exist_ok=True)
manifest = {}
for p in (root / 'eng/native/box2d').iterdir():
    if p.is_file() and p.suffix in ('.c', '.h'):
        shutil.copy2(p, src / p.name)
        manifest[p.name] = hashlib.sha256(p.read_bytes()).hexdigest()

def edit(name, old, new):
    p = src / name
    text = p.read_text()
    assert text.count(old) == 1, (name, text.count(old))
    p.write_text(text.replace(old, new))

edit('dango_belt_room.c', 'static int room_project(room_world*', 'static int room_mass_project(room_world*')
edit('dango_belt_room.c', 'static double room_wall_theta(', '#include "../metric.h"\nstatic double room_wall_theta(')
edit('dango_belt_room_collection_impl.h', 'static int collection_project_body_ball(room_world*', 'static int collection_mass_project_body_ball(room_world*')
edit('dango_belt_room_collection_impl.h', '/* Segment distance for a swept relative disk pair. */', '#include "../metric-ball.h"\n/* Segment distance for a swept relative disk pair. */')

trace = root / 'artifacts/belt-ball-platform-diagnosis/body-state-trace'
frozen = json.loads((trace / 'source-hashes.json').read_text())
assert all(frozen[k] == manifest[k] for k in manifest), 'Production drift from frozen sources'
cases = []
with (trace / 'trace.csv').open(newline='') as f:
    for r in csv.DictReader(f):
        name = f"state-{int(r['sequence']):06d}-static_before.bin"
        if r['event'] == 'static_before' and (trace / name).exists():
            cases.append('{"' + name + '",' + ','.join(r[k] for k in ['theta', 'nx', 'ny', 'depth']) + '}')
(out / 'cases.h').write_text('static const frozen_case cases[]={\n' + ',\n'.join(cases) + '\n};\n')
original = (root / 'artifacts/belt-ball-platform-diagnosis/native-repro/repro-rebased.c').read_text()
scalars = re.search(r'const float actualscalars\[39\]=\{([^}]+)\}', original).group(1)
(out / 'actual-config.h').write_text('static const float actual_scalars[39]={' + scalars + '};\n')
manifest['frozen_cases'] = len(cases)
manifest['state_sizes'] = {p.name: p.stat().st_size for p in trace.glob('state-*.bin')}
(out / 'source-binding.json').write_text(json.dumps(manifest, indent=2))
print(f'Prepared isolated source copy, {len(cases)} frozen projections; production unchanged')

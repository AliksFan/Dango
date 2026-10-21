"""Run the bounded background prototype checks and freeze their exact inputs."""
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
import argparse
import hashlib
import json
import subprocess
import time

out = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('run_id')
args = parser.parse_args()
run = out / 'runs' / args.run_id
run.mkdir(parents=True, exist_ok=False)
product = '../belt-page-completion/review-app/dango_belt_product.dll'
checks = [
    ('frozen-mass', 'metric_0', ['frozen']),
    ('frozen-metric', 'metric_1', ['frozen']),
    ('stage-mass', 'metric_0', ['stage']),
    ('stage-metric', 'metric_1', ['stage']),
    ('properties', 'metric_1', ['properties']),
    ('replay-mass', 'metric_0', ['replay']),
    ('replay-metric', 'metric_1', ['replay']),
    ('collection-contracts', 'metric_1', ['contracts']),
    ('combined-corner', 'metric_1', ['--combined-corner']),
    ('room-contracts', 'room_metric', []),
    ('platform-nine', 'room_metric', ['--platform']),
    ('walls', 'wall_metric', []),
    ('frozen-product-equivalence', 'room_metric', [product]),
]
(out / 'artifacts/belt-room-f7-static-r2/walls').mkdir(parents=True, exist_ok=True)

def check(case):
    name, exe, arguments = case
    command = [str(out / 'build/Release' / (exe + '.exe')), *arguments]
    start = time.perf_counter()
    with (run / (name + '.log')).open('wb') as log:
        completed = subprocess.run(command, cwd=out, stdout=log, stderr=subprocess.STDOUT)
    result = dict(name=name, executable=exe, arguments=arguments,
                  exit_code=completed.returncode, elapsed_seconds=time.perf_counter() - start)
    print(json.dumps(result), flush=True)
    return result

with ThreadPoolExecutor(max_workers=3) as pool:
    futures = [pool.submit(check, case) for case in checks]
    results = [future.result() for future in as_completed(futures)]

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

root = out.parents[1]
paths = list(out.glob('*.h')) + list(out.glob('*.c')) + list(out.glob('*.py')) + [out / 'CMakeLists.txt', out / 'source-binding.json']
paths += list((out / 'build/Release').glob('*.exe'))
trace = root / 'artifacts/belt-ball-platform-diagnosis/body-state-trace'
paths += list(trace.glob('state-*.bin')) + [trace / 'trace.csv', trace / 'source-hashes.json']
paths += [out / product, root / 'artifacts/belt-ball-platform-diagnosis/native-wall-build/Release/dango_belt_room.dll']
paths += list(run.glob('*.log'))
binding = {str(p.relative_to(root)).replace('\\', '/'): sha(p) for p in paths if p.is_file()}
summary = dict(source_head='6976168155b63f0903e960840dfa4308ca13ab6a',
               scope='isolated numerical prototype, no production changes or foreground operations',
               checks=sorted(results, key=lambda r: r['name']), hashes=binding)
(run / 'summary.json').write_text(json.dumps(summary, indent=2))
raise SystemExit(any(r['exit_code'] for r in results))

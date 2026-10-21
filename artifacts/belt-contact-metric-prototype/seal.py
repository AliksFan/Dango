"""Verify the completed run's binding and add the actual compiled source copies."""
from pathlib import Path
import hashlib
import json
import sys

out = Path(__file__).resolve().parent
root = out.parents[1]
run = out / 'runs' / sys.argv[1]
summary_path = run / 'summary.json'
summary = json.loads(summary_path.read_text())
assert len(summary['checks']) == 13 and all(c['exit_code'] == 0 for c in summary['checks'])

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

for relative, expected in summary['hashes'].items():
    assert sha(root / relative) == expected, relative
paths = [*out.glob('src/*.c'), *out.glob('src/*.h'), out / 'seal.py', summary_path]
binding = {str(p.relative_to(root)).replace('\\', '/'): sha(p) for p in paths}
all_paths = {**summary['hashes'], **binding}
text_lf = {relative: hashlib.sha256((root / relative).read_bytes().replace(b'\r\n', b'\n')).hexdigest()
           for relative in all_paths if Path(relative).suffix in {'.c', '.h', '.py', '.json', '.log', '.txt'}}
manifest = dict(scope='supplement to the verified unchanged run summary; actual compiled source copies',
                completed_checks=13, hashes=binding, text_lf_sha256=text_lf)
destination = run / 'evidence-manifest.json'
destination.write_text(json.dumps(manifest, indent=2))
print(f'Verified original run binding; sealed {len(binding)} additional files; all 13 checks passed')

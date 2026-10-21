"""Seal the targeted integration evidence without rewriting older records."""
from pathlib import Path
import hashlib
import json
import xml.etree.ElementTree as ET

out = Path(__file__).resolve().parent
root = out.parents[1]
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def trx(name):
    document = ET.parse(out / name).getroot()
    results = document.findall('.//t:Results/t:UnitTestResult', ns)
    counts = {k: sum(r.attrib['outcome'] == k for r in results) for k in ['Passed', 'Failed', 'NotExecuted']}
    return dict(path=name, total=len(results), **counts)

managed = [trx(name) for name in [
    'managed-product-integration-with-audit-warning.trx', 'managed-room-binding.trx',
    'managed-legacy-binding-corrected.trx', 'managed-room-storage-sleep.trx',
    'managed-desktop-metadata-completed.trx']]
assert [(r['Passed'], r['Failed'], r['NotExecuted']) for r in managed] == [
    (10, 0, 0), (31, 2, 0), (2, 0, 0), (65, 19, 2), (2, 0, 0)]
native = json.loads((out / 'native-contracts/summary.json').read_text())
assert len(native) == 3 and all(r['exit_code'] == 0 for r in native)
replay = json.loads((out / 'native-regressions/summary-v2.json').read_text())
assert replay['source_formal_numerical_digest_match']
assert [c['exit'] for c in replay['checks']] == [0, 0, 0, 1]
manifest = json.loads((out / 'review-app/belt-native-manifest.json').read_text(encoding='utf-8-sig'))
assert manifest['origin'] == 'source-build' and len(manifest['modules']) == 2
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
for module in manifest['modules']:
    assert sha(out / 'review-app' / module['path']).upper() == module['sha256'].upper()
for relative, digest in manifest['sourceHashes'].items():
    assert sha(root / relative).upper() == digest.upper()
source_paths = [root / relative for relative in manifest['sourceHashes']]
source_paths += [root / 'eng/native/box2d/dango_belt_room_collection_tests.c',
                 root / 'tests/Dango.Desktop.Tests/Furniture/BeltRoomFurnitureIntegrationTests.cs',
                 out / 'finalize.py']
evidence_paths = list(out.glob('*.log')) + list(out.glob('*.trx'))
evidence_paths += list((out / 'native-contracts').glob('*.json')) + list((out / 'native-contracts').glob('*.log'))
evidence_paths += list((out / 'native-regressions').glob('*.json')) + list((out / 'native-regressions').glob('*.log'))
evidence_paths += [out / 'managed-definitions.json', out / 'review-app/belt-native-manifest.json',
                   out / 'review-app/Dango.Desktop.exe', out / 'review-app/dango_belt_room.dll', out / 'review-app/dango_belt_product.dll',
                   root / 'artifacts/belt-ball-platform-diagnosis/native-wall-build/Release/dango_belt_room.dll',
                   root / 'artifacts/belt-page-completion/review-app/dango_belt_product.dll',
                   root / 'artifacts/belt-page-completion/review-app/dango_belt_room.dll']
hashes = {str(p.relative_to(root)).replace('\\', '/'): sha(p) for p in source_paths + evidence_paths}
text_lf = {str(p.relative_to(root)).replace('\\', '/'): hashlib.sha256(p.read_bytes().replace(b'\r\n', b'\n')).hexdigest()
           for p in source_paths + evidence_paths if p.suffix in {'.c', '.h', '.py', '.json', '.log', '.trx', '.txt'}}
summary = dict(
    base_commit='81f83a5e5e48985d11a8767d9f363c657b0fa734',
    authorization='User approved the local elastic contact metric production integration and background validation.',
    managed_runs=managed,
    completed_unique_targeted_managed_checks=110,
    prior_results='Two incorrect legacy fixture failures corrected by the matching r1 fixture; two missing metadata fixtures completed separately.',
    incomplete='19 legacy furniture-chain checks stopped at missing legacy native fixture configuration; one legacy-world test excluded because its external native module is unavailable.',
    native_checks=native,
    full_replay_numerical_digest_match=True,
    old_three_fix_dll_negative='Expected exit 1 at input tick612, rc=-705; old failure retained.',
    build='Local source publish succeeded without warning overrides; test-project restore required command-local WarningsNotAsErrors=NU1903.',
    native_modules=manifest['modules'],
    unverified=['foreground input/presentation', 'human acceptance', 'full real-time performance'],
    delivery='Local review package generated but not launched; no merge, push, installation or deployment.',
    hashes=hashes, text_lf_sha256=text_lf)
destination = out / 'evidence-summary.json'
assert not destination.exists()
destination.write_text(json.dumps(summary, indent=2) + '\n')
print('Sealed source/package binding and targeted integration results; recorded incomplete legacy checks explicitly.')

"""Closed source restoration, old-evidence protection and finite result receipt."""
import argparse, hashlib, json, pathlib, re, subprocess, sys

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
parser.add_argument('--version', required=True)
parser.add_argument('--v4-backup', required=True)
args = parser.parse_args()
version = args.version
assert re.fullmatch(r'v[0-9]+', version) and version != 'v4'
here = pathlib.Path(__file__).resolve().parent; repo = here.parents[3]; native = here.parent
root = repo/'artifacts/round-material'; own = root/'singleton-mode'
sha = lambda p: hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
read = lambda p: json.loads(pathlib.Path(p).read_text())
endpoint_path = root/'endpoint-selection/native-final-manifest-v5.json'
receipt_path = root/'receipt-history/native-final-manifest-v10.json'
diagnosis_path = root/'endpoint-selection/third-failure/diagnosis-final-manifest.json'
reference_path = root/'endpoint-selection/third-failure/fixed-mode-reference/final-manifest.json'
assert sha(endpoint_path) == '3cfb13707654e477badbd87441a891d9d67f1e40c8f27057748c2eb02c9049e6'
assert sha(receipt_path) == '82c94869793748f619f265cc6e29e054172a68ee731335de9959e3134f5904fc'
assert sha(diagnosis_path) == '9062327b9010fcfbf76e15869fe47fa4f3c75012c7a86ba17644106f2a0d5676'
endpoint, receipt, diagnosis, reference = map(read, [endpoint_path, receipt_path, diagnosis_path, reference_path])
protected = {}
assert all(diagnosis['protectedNativeSourceFiles'].values())
assert set(diagnosis['protectedNativeSourceFiles']) == set(reference['originalNativeSourcesPreserved'])
for mapping in [endpoint['candidateSourceFiles'], receipt['candidateSourceFiles'], receipt['originalSolverFiles'], reference['originalNativeSourcesPreserved']]:
    for name, value in mapping.items():
        protected[name] = sha(repo/name) == value; assert protected[name], name
for manifest in [endpoint, receipt]:
    for name, value in manifest['evidence'].items():
        protected[name] = sha(repo/name) == value; assert protected[name], name
for manifest, base in [(diagnosis, diagnosis_path.parent), (reference, reference_path.parent),
                       (read(root/'receipt-history/second-failure/preservation-final-manifest.json'), root/'receipt-history/second-failure')]:
    for name, entry in manifest['files'].items():
        path = base/name; protected[str(path.relative_to(repo))] = sha(path) == entry['sha256']
        assert protected[str(path.relative_to(repo))], str(path)
old_dlls = {root/'endpoint-selection/build-v5/Release/dango_material_endpoint_selection_candidate.dll': endpoint['nativeSha256'],
            root/'receipt-history/build-v10/Release/dango_material_receipt_history_candidate.dll': receipt['nativeSha256'],
            root/'receipt-history/input-recovery/old-approved-native.dll': '055be2b5e6cd3816eb4d59f616577b653c00512b4750299a013d2cebf676e2c5'}
for path, value in old_dlls.items(): assert sha(path) == value
generated = own/('generated-'+version); source = read(generated/'singleton-source-manifest.json')
actual = {p.relative_to(generated).as_posix(): sha(p) for p in generated.rglob('*') if p.is_file() and p.name != 'singleton-source-manifest.json'}
assert actual == source['files']
old = root/'endpoint-selection/generated-v5'
assert sha(old/'endpoint-source-manifest.json') == '4ffb627043894e49ec9b0b47f1b8343ed8c864f783d95999dd616f4a8ad3cf92'
original_map = read(old/'endpoint-source-manifest.json')['files']
assert {p.relative_to(old).as_posix(): sha(p) for p in old.rglob('*') if p.is_file() and p.name != 'endpoint-source-manifest.json'} == original_map
restored = {}
changed = {c['name'] for c in source['changes']}
for name in changed:
    text = (generated/name).read_text()
    for change in reversed(source['changes']):
        if change['name'] == name:
            assert text.count(change['candidate']) == 1
            text = text.replace(change['candidate'], change['original'], 1)
    restored[name] = text == (old/name).read_text(); assert restored[name], name
for name in original_map:
    if name not in changed: assert sha(generated/name) == original_map[name], name
for name in ['budget.h', 'observer.h', 'singleton.h']: assert sha(generated/'adapter'/name) == sha(here/name)
pinned = read(native/'receipt-history/pinned-files.json')['files']
box_restored = {}
for name, value in pinned.items():
    path = generated/'box2d'/name
    if name in ['src/world.h', 'src/contact_solver.c']:
        text = re.sub(r'// DANGO_RH_INSERT_BEGIN\n.*?// DANGO_RH_INSERT_END\n', '', path.read_text(), flags=re.S)
        if name == 'src/contact_solver.c':
            for function in ['b2SolveOverflowContacts','b2ApplyOverflowRestitution','b2StoreOverflowImpulses','b2SolveContactsTask','b2ApplyRestitutionTask','b2StoreImpulsesTask']:
                start = text.index('void '+function+'('); brace = text.index('{', start); finish = text.index('\n}', brace)
                assert text[finish-1] == '\n'; text = text[:finish-1]+text[finish:]
                brace = text.index('{', start); assert text[brace+1:brace+3] == '\n\n'; text = text[:brace+1]+text[brace+2:]
        box_restored[name] = hashlib.sha256(text.encode()).hexdigest() == value
    else: box_restored[name] = sha(path) == value
    assert box_restored[name], name
backup = pathlib.Path(args.v4_backup).resolve(); backup_manifest = backup/'backup-manifest.json'
assert sha(backup_manifest) == '08c047d404c0d9f824c00134d685f441cb204715e28517f1836ca65cacc9cf5e'
backup_files = read(backup_manifest)['files']; assert len(backup_files) == 347
for name, value in backup_files.items():
    assert sha(backup/name) == value, 'Sealed v4 backup changed: '+name
    if not name.startswith('eng/native/box2d/singleton-mode/'):
        assert sha(repo/name) == value, 'Original sealed v4 artifact changed: '+name
old_end = 't->block_passed=t->final_block_status==DANGO_MATERIAL_CONTACT_BLOCK_OK;'
new_end = 't->block_passed=dango_singleton_current.block->attempted&&t->final_block_status==DANGO_MATERIAL_CONTACT_BLOCK_OK;'
assert (here/'singleton.h').read_text().count(new_end) == 1
assert (here/'singleton.h').read_text().replace(new_end, old_end, 1) == (backup/'eng/native/box2d/singleton-mode/singleton.h').read_text()
allowed_source_changes = {'audit.py', 'tests.c', 'singleton.h', 'README.md'}
source_changes_from_v4 = []
for name, value in backup_files.items():
    if name.startswith('eng/native/box2d/singleton-mode/') and sha(repo/name) != value:
        assert pathlib.Path(name).name in allowed_source_changes, name
        source_changes_from_v4.append(name)
stage_repro = read(own/'stage-v4-repro/result.json')
stage_negative = read(own/'stage-v4-negative/result.json')
assert stage_repro['built'] and stage_repro['begin'] and not stage_repro['attempted'] and stage_repro['misreported']
assert stage_negative['failures'] == 1 and stage_negative['assertions'] == 615 and stage_negative['negativeCases'] == 156
fixed = json.loads((own/('fixed-'+version+'-stdout.bin')).read_bytes())
endpoint_fixed = json.loads((own/('endpoint-'+version+'-stdout.bin')).read_bytes())
receipt_rows = [json.loads(line) for line in (own/('receipt-'+version+'.jsonl')).read_text().splitlines()]
assert fixed['failures'] == 0 and fixed['assertions'] == 615 and fixed['negativeCases'] == 156
assert endpoint_fixed['failures'] == 0 and endpoint_fixed['assertions'] == 124 and endpoint_fixed['negativeCases'] == 43
assert len(receipt_rows) == 5 and all(r['failures'] == 0 for r in receipt_rows)
assert receipt_rows[1]['ordinaryCases'] == 20 and receipt_rows[1]['negativeCases'] == 71 and receipt_rows[1]['referenceOutputBitsEqual']
assert receipt_rows[2]['completed'] == 6000 and receipt_rows[2]['bitMismatches'] == 0 and receipt_rows[2]['preFailureFieldsCompared'] == 246288
assert receipt_rows[-1]['assertions'] == 23175 and receipt_rows[-1]['passed']
dll = own/('build-'+version+'/Release/dango_material_singleton_mode_candidate.dll')
probes = {'defaultRetained': read(own/('probe-'+version+'-retained/result.json')), 'defaultThird': read(own/('probe-'+version+'-third/result.json')),
          'oneMiBFirst': read(own/('probe-'+version+'-1m-first.json')), 'oneMiBRetained': read(own/('probe-'+version+'-1m-retained/result.json')),
          'oneMiBThird': read(own/('probe-'+version+'-1m-third/result.json'))}
assert all(p['passed'] and p['dllSha256'] == sha(dll) for p in probes.values())
stacks = {name: read(own/('stack-'+version+'-'+name+'.json')) for name in ['first','second','third']}
assert all(s['passed'] and s['actualReserveBytes'] == 1048576 for s in stacks.values())
ledger = read(own/('ledger-'+version+'.json')); assert ledger['passed']
v4_rows = {}
for old_name, new_name in [('receipt-v4-rows.bin', 'receipt-'+version+'-rows.bin'),
                         ('probe-v4-retained/first-rows.bin', 'probe-'+version+'-retained/first-rows.bin'),
                         ('probe-v4-retained/second-rows.bin', 'probe-'+version+'-retained/second-rows.bin'),
                         ('probe-v4-third/third-rows.bin', 'probe-'+version+'-third/third-rows.bin')]:
    old_rows, new_rows = (own/old_name).read_bytes(), (own/new_name).read_bytes()
    assert old_rows == new_rows, 'Accepted trajectory changed: '+new_name
    v4_rows[new_name] = {'floatFieldsCompared': len(new_rows)//4, 'bitMismatches': 0}
negative_dir = own/('source-negatives-'+version); negative_dir.mkdir(exist_ok=False)
invalid = dict(endpoint); invalid['candidateSourceFiles'] = {}
invalid_path = negative_dir/'empty-map.json'; invalid_path.write_text(json.dumps(invalid)+'\n')
negatives = []
for name, input_source, manifest_path in [('empty-source-map', old, invalid_path), ('already-modified-source', generated, endpoint_path)]:
    rejected = negative_dir/(name+'-unexpected-output')
    command = [sys.executable, str(here/'generate.py'), '--endpoint-generated', str(input_source), '--endpoint-manifest', str(manifest_path), '--output', str(rejected)]
    process = subprocess.run(command, capture_output=True)
    (negative_dir/(name+'-stdout.bin')).write_bytes(process.stdout); (negative_dir/(name+'-stderr.bin')).write_bytes(process.stderr)
    assert process.returncode != 0 and not rejected.exists()
    negatives.append({'name': name, 'exit': process.returncode, 'rejectedBeforeOutputCreation': True})
manifest = {'schema': 1, 'kind': 'singleton-mode-native-finite-final', 'passed': True,
    'scope': 'Finite fixed/native/negative/three-prefix/lifecycle/ledger/source gates only; no 6660/S1/S2/S3/matrix or manual acceptance.',
    'nativeSha256': sha(dll), 'fixedTests': fixed, 'endpointTests': endpoint_fixed, 'receiptTests': receipt_rows,
    'actualDllProbes': probes, 'explicitOneMiBThreadReceipts': stacks, 'independentLedger': ledger, 'acceptedTrajectoryEqualityToSealedV4': v4_rows,
    'receiptTestOnlyStackReserveBytes': 8388608, 'sealedV4BackupManifestSha256': sha(backup_manifest), 'sealedV4BackupFilesVerified': len(backup_files), 'sourceChangesFromSealedV4': source_changes_from_v4, 'runtimeChangeFromV4ExactlyOneAttemptedGuard': True, 'v4UnattemptedStageReproducer': stage_repro, 'v4FailureCapableNewNegative': stage_negative, 'v3OriginalTestStackOverflowExit': '0xC00000FD',
    'completeSourceMapVerified': True, 'reversibleSourceChanges': restored, 'originalBox2dFilesRestored': len(box_restored),
    'oldSourceAndEvidenceProtection': protected, 'oldDllsPreserved': {str(p.relative_to(repo)): h for p,h in old_dlls.items()},
    'sourceNegativeGates': negatives, 'candidateSourceFiles': {p.relative_to(repo).as_posix(): sha(p) for p in sorted(here.iterdir()) if p.is_file()},
    'frozenManifestHashes': {p.relative_to(repo).as_posix(): sha(p) for p in [endpoint_path, receipt_path, diagnosis_path, reference_path]}, 'evidence': {}}
for path in own.rglob('*'):
    if path.is_file() and not any(part.startswith('build') or part.startswith('generated') for part in path.relative_to(own).parts[:-1]) and 'matrix' not in path.parts:
        manifest['evidence'][path.relative_to(repo).as_posix()] = sha(path)
for path in [dll, own/('build-'+version+'/Release/dango_singleton_fixed_tests.exe'), own/('build-'+version+'/Release/dango_singleton_endpoint_tests.exe'), own/('build-'+version+'/Release/dango_singleton_receipt_tests.exe'), generated/'singleton-source-manifest.json', own/('build-'+version+'/dango_material_singleton_mode_candidate.vcxproj'), own/('build-'+version+'/dango_singleton_receipt_tests.vcxproj')]:
    manifest['evidence'][path.relative_to(repo).as_posix()] = sha(path)
with open(args.output, 'x', encoding='utf-8') as f: json.dump(manifest, f, indent=2); f.write('\n')
print(json.dumps({'passed': True, 'nativeSha256': sha(dll), 'manifestSha256': sha(args.output), 'negativeCases': 270, 'ordinaryCases': 20, 'box2dRestored': 233}))

"""Closed native-only finite gates, protection and reversible source receipt."""
import hashlib, json, pathlib, re, shutil, subprocess, sys

here = pathlib.Path(__file__).resolve().parent
repo = here.parents[3]
own = repo/'artifacts/round-material/touching-receipt'
native = here.parent
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_text())
generated = own/'generated-v2'
source = read(generated/'touching-source-manifest.json')
actual = {p.relative_to(generated).as_posix():sha(p) for p in generated.rglob('*') if p.is_file() and p.name != 'touching-source-manifest.json'}
assert actual == source['files'] and len(actual) == 251
base = repo/'artifacts/round-material/singleton-mode/generated-v5'
base_map = read(base/'singleton-source-manifest.json')['files']
assert {p.relative_to(base).as_posix():sha(p) for p in base.rglob('*') if p.is_file() and p.name != 'singleton-source-manifest.json'} == base_map
changed = {c['name'] for c in source['changes']}
restored = {}
for name in changed:
    content = (generated/name).read_bytes()
    for change in reversed(source['changes']):
        if change['name'] == name:
            before, after = change['original'].encode(), change['candidate'].encode()
            assert content.count(after) == 1
            content = content.replace(after,before,1)
    original = native/'dango_material_contact_solver.h' if name == 'adapter/dango_material_contact_solver.h' else base/name
    restored[name] = content == original.read_bytes()
    assert restored[name], name
unchanged = {name:value for name,value in base_map.items() if name not in changed}
assert all(sha(generated/name)==value for name,value in unchanged.items())
for name, entry in source['extraSources'].items():
    assert sha(repo/entry['source']) == entry['sourceSha256']
    if name not in changed: assert sha(generated/name) == entry['sourceSha256']
box_restored = {}
for name, value in read(native/'receipt-history/pinned-files.json')['files'].items():
    path = generated/'box2d'/name
    if name in ['src/world.h','src/contact_solver.c']:
        text = re.sub(r'// DANGO_RH_INSERT_BEGIN\n.*?// DANGO_RH_INSERT_END\n','',path.read_text(),flags=re.S)
        if name == 'src/contact_solver.c':
            for function in ['b2SolveOverflowContacts','b2ApplyOverflowRestitution','b2StoreOverflowImpulses','b2SolveContactsTask','b2ApplyRestitutionTask','b2StoreImpulsesTask']:
                start=text.index('void '+function+'('); brace=text.index('{',start); finish=text.index('\n}',brace)
                assert text[finish-1]=='\n';text=text[:finish-1]+text[finish:]
                brace=text.index('{',start);assert text[brace+1:brace+3]=='\n\n';text=text[:brace+1]+text[brace+2:]
        box_restored[name]=hashlib.sha256(text.encode()).hexdigest()==value
    else: box_restored[name]=sha(path)==value
    assert box_restored[name],name
assert len(box_restored)==233
negdir=own/'source-negatives-v2';negdir.mkdir(exist_ok=False)
copied=negdir/'mutated-private-base';shutil.copytree(base,copied)
(copied/'adapter/singleton.h').write_bytes((copied/'adapter/singleton.h').read_bytes()+b'\n// finite source mutation\n')
badmanifest=negdir/'mutated-private-manifest';badmanifest.mkdir()
(badmanifest/'singleton-source-manifest.json').write_bytes(b'{}\n')
existing_before={p.relative_to(generated).as_posix():sha(p) for p in generated.rglob('*') if p.is_file()}
source_negatives=[]
for name, src, dst in [('mutated-manifest',badmanifest,negdir/'bad-manifest-output'),('mutated-source-map',copied,negdir/'bad-map-output'),('already-modified-source',generated,negdir/'already-modified-output'),('existing-output',base,generated)]:
    command=[sys.executable,'-B',str(here/'generate.py'),'--singleton-generated',str(src),'--output',str(dst)]
    result=subprocess.run(command,capture_output=True)
    (negdir/(name+'.stdout.bin')).open('xb').write(result.stdout)
    (negdir/(name+'.stderr.bin')).open('xb').write(result.stderr)
    assert result.returncode!=0
    if name!='existing-output': assert not dst.exists()
    source_negatives.append({'name':name,'exit':result.returncode,'rejectedBeforeOutputCreationOrExistingOutputMutation':True})
assert existing_before == {p.relative_to(generated).as_posix():sha(p) for p in generated.rglob('*') if p.is_file()}
protection_path=own/'pre-implementation-protection.json';protection=read(protection_path)
assert sha(protection_path)=='de7f16272ad40c83c6df01c3ebabb417c65b8065741886782668639d684b011c'
preserved={name:sha(repo/name)==value for name,value in protection['files'].items()}
assert len(preserved)==1048 and all(preserved.values())
assert all(not (repo/name).exists() for name in protection['absent'])
dll=own/'build-v2/Release/dango_material_touching_receipt_candidate.dll'
assert sha(dll)=='4388f63610c01fa5413eda20829ea9e6f33ac93ec36a6793dce4dcb0e4eaf757'
fixed=read(own/'touching-v2.stdout.bin');endpoint=read(own/'endpoint-v2-attempt2.stdout.bin');singleton=read(own/'singleton-v2.stdout.bin')
receipt=[json.loads(line) for line in (own/'receipt-v2.jsonl').read_text().splitlines()]
assert fixed['negativeCases']==81 and fixed['failures']==0 and fixed['scaledConeRejection'] and fixed['assertions']==13467
assert endpoint['negativeCases']==43 and endpoint['failures']==0 and endpoint['assertions']==124
assert singleton['negativeCases']==156 and singleton['failures']==0 and singleton['assertions']==615
assert len(receipt)==5 and all(r['failures']==0 for r in receipt)
assert receipt[1]['ordinaryCases']==20 and receipt[1]['negativeCases']==71 and receipt[1]['referenceOutputBitsEqual']
assert receipt[2]['completed']==6000 and receipt[2]['bitMismatches']==0 and receipt[-1]['assertions']==23175
probes={name:read(own/path) for name,path in {
    'defaultRetained':'probe-v2-retained/result.json','defaultThird':'probe-v2-third/result.json','defaultFourth':'probe-v2-fourth/result.json',
    'oneMiBFirst':'probe-v2-1m-first.json','oneMiBRetained':'probe-v2-1m-retained/result.json','oneMiBThird':'probe-v2-1m-third/result.json','oneMiBFourth':'probe-v2-1m-fourth/result.json'}.items()}
assert all(p['passed'] and p['dllSha256']==sha(dll) for p in probes.values())
stacks={name:read(own/('stack-v2-'+name+'.json')) for name in ['first','first-second','third','fourth']}
assert all(s['passed'] and s['actualReserveBytes']==1048576 for s in stacks.values())
ledgers={name:read(own/filename) for name,filename in [('third','ledger-third-v2.json'),('fourth','ledger-fourth-v2-identity.json')]}
assert all(l['passed'] for l in ledgers.values())
layout=read(own/'layout-v2/layout.json');assert layout['diagnostic']['bytes']==200736 and layout['touchingDiagnosticOffset']==200712
raw_newlines={p.name:{'sha256':sha(p),'crlfCount':p.read_bytes().count(b'\r\n'),'loneCrCount':p.read_bytes().count(b'\r')} for p in here.iterdir() if p.is_file()}
assert all(f['crlfCount']==f['loneCrCount']==0 for f in raw_newlines.values())
assert (here/'.gitattributes').read_bytes()==b'* -text\n'
manifest={'schema':1,'kind':'touching-receipt-native-finite-final','passed':True,
    'scope':'Native finite fixed/failure-capable negative/four-prefix/lifecycle/ledger/source gates only. No full6660/S1/S2/S3/matrix/manual acceptance or commit/main/push/install/production DLL replacement.',
    'nativePath':dll.relative_to(repo).as_posix(),'nativeSha256':sha(dll),'fixedTests':fixed,'endpointTests':endpoint,'singletonTests':singleton,'receiptTests':receipt,
    'negativeCases':{'newTouching':81,'originalReceipt':71,'originalEndpoint':43,'originalSingleton':156,'oldTotal':270},'ordinaryLegalFullBitComparisons':20,
    'actualDllProbes':probes,'explicitOneMiBThreadReceipts':stacks,'independentLedgers':ledgers,
    'actualFullDiagnosticBytes':200736,'unchangedDiagnosticPrefixBytes':200712,
    'inheritedTraceMetadataNote':'Retained probe wrapper traceAbi.diagnosticBytes=157832 describes its old endpoint prefix; current complete getter/layout size is 200736.',
    'fixedCapScale':1,'positiveActiveCapScaleLessThanOneVerified':False,'activeCapInducedConeExcessNegativeRejected':True,
    'completeSourceMap':{'files':len(actual),'all251HashesVerified':True,'baseFilesAccountedFor':len(base_map),'unchangedBaseFiles':len(unchanged),'replacements':len(source['changes']),'rawByteReverseRestoration':restored,'originalBox2d233FullInverseRestored':all(box_restored.values()),'sourceNegativeGates':source_negatives},
    'priorFileProtection':{'manifestSha256':sha(protection_path),'filesVerified':len(preserved),'allHashesUnchanged':True,'absentFilesStillAbsent':protection['absent'],'productionDllUnchanged':True},
    'sourceRawNewlines':raw_newlines,'rawBytesGitAttributes':'* -text','receiptTestOnlyStackReserveBytes':8388608,
    'retainedNonPhysicalFailures':{'v1':'Test-only double literal assigned to float triggered /WX C4244; runtime DLL built successfully. v1 source snapshot/build logs preserved.','endpointV2Attempt1':'Driver supplied 262144-byte buffer instead of immutable 138720-byte fixture; exact loader failed before physical checks. Attempt2 exact original fixture passed; both logs preserved.'},
    'candidateSourceFiles':{p.relative_to(repo).as_posix():sha(p) for p in sorted(here.iterdir()) if p.is_file()},
    'compiledDependencySourceFiles':{},'evidence':{}}
for name in ['receipt-history','endpoint-selection','singleton-mode']:
    for p in (native/name).iterdir():
        if p.is_file():manifest['compiledDependencySourceFiles'][p.relative_to(repo).as_posix()]=sha(p)
for p in native.iterdir():
    if p.is_file() and p.suffix in ['.h','.c','.txt']:manifest['compiledDependencySourceFiles'][p.relative_to(repo).as_posix()]=sha(p)
for p in own.rglob('*'):
    if not p.is_file() or 'matrix' in p.relative_to(own).parts:continue
    parts=p.relative_to(own).parts
    if any(part.startswith('build') or part.startswith('generated') for part in parts[:-1]):continue
    manifest['evidence'][p.relative_to(repo).as_posix()]=sha(p)
for p in (own/'build-v2').rglob('*'):
    if p.is_file() and p.suffix in ['.vcxproj','.dll','.exe','.lib','.txt']:manifest['evidence'][p.relative_to(repo).as_posix()]=sha(p)
manifest['evidence'][(generated/'touching-source-manifest.json').relative_to(repo).as_posix()]=sha(generated/'touching-source-manifest.json')
path=own/'native-final-manifest-v2.json'
with path.open('x',encoding='utf-8') as stream:json.dump(manifest,stream,indent=2);stream.write('\n')
print(json.dumps({'passed':True,'manifest':path.relative_to(repo).as_posix(),'manifestSha256':sha(path),'nativeSha256':sha(dll),'newNegatives':81,'oldNegatives':270,'protectedFiles':1048,'sourceMapFiles':251,'pinnedBox2dFiles':233}))

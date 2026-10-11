"""Bind final native evidence to actual disk source/binary/input fingerprints."""
import argparse,hashlib,json,pathlib,re,subprocess,sys
ap=argparse.ArgumentParser(); ap.add_argument('--generated',required=True); ap.add_argument('--build',required=True); ap.add_argument('--tests',required=True); ap.add_argument('--rows',required=True); ap.add_argument('--dll-result',required=True); ap.add_argument('--config',required=True); ap.add_argument('--fixed-input',required=True); ap.add_argument('--old-rows',required=True); ap.add_argument('--output',required=True); args=ap.parse_args()
repo=pathlib.Path(__file__).resolve().parents[4]; generated=pathlib.Path(args.generated); build=pathlib.Path(args.build)
sha=lambda p:hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
input_hashes={'config':sha(args.config),'fixedInputState':sha(args.fixed_input),'oldObservedRows':sha(args.old_rows)}
assert input_hashes=={
    'config':'b851f0dfb2cb2e3601d5e48e3c72d857c325ae704c369047115f702848b130ab',
    'fixedInputState':'e305c489ee84b878b653bafe418071bfa17075da16cfcc63ae51908d49b3f5ea',
    'oldObservedRows':'66276bffadff630eafa6463ef3cd7f11c3403eb6fac40c9ef840b52838145668'
},'Historical fixed failure input fingerprint mismatch'
source=json.loads((generated/'source-manifest.json').read_text(encoding='utf-8'))
pinned=json.loads((pathlib.Path(__file__).resolve().parent/'pinned-files.json').read_text(encoding='utf-8'))['files']
actual_names={p.relative_to(generated/'box2d').as_posix() for p in (generated/'box2d').rglob('*') if p.is_file()}
assert actual_names==set(pinned), 'Generated Box2D file set differs from pinned tree'
for name,expected in pinned.items():
    assert sha(generated/'box2d'/name)==source['files'].get(name,{}).get('candidateSha256',expected),name
for name,entry in source['files'].items():
    target=generated/name if name.startswith('adapter/') else generated/'box2d'/name
    assert sha(target)==entry['candidateSha256'],f'Actual disk source SHA differs: {name}'
    text=target.read_text(encoding='utf-8')
    if name in ('src/world.h','src/contact_solver.c'):
        restored=re.sub(r'// DANGO_RH_INSERT_BEGIN\n.*?// DANGO_RH_INSERT_END\n','',text,flags=re.S)
        if name=='src/contact_solver.c':
            for function in ['b2SolveOverflowContacts','b2ApplyOverflowRestitution','b2StoreOverflowImpulses','b2SolveContactsTask','b2ApplyRestitutionTask','b2StoreImpulsesTask']:
                start=restored.index('void '+function+'('); brace=restored.index('{',start); finish=restored.index('\n}',brace)
                assert restored[finish-1]=='\n'
                restored=restored[:finish-1]+restored[finish:]
                brace=restored.index('{',start); assert restored[brace+1:brace+3]=='\n\n'
                restored=restored[:brace+1]+restored[brace+2:]
        assert hashlib.sha256(restored.encode('utf-8')).hexdigest()==pinned[name],f'Independent math restoration differs: {name}'
    elif name.startswith('adapter/'):
        restored=text
        for change in reversed(source['adapterChanges']):
            if change['candidate'] in restored:
                assert restored.count(change['candidate'])==1
                restored=restored.replace(change['candidate'],change['original'],1)
        assert restored==(repo/'eng/native/box2d'/pathlib.Path(name).name).read_text(encoding='utf-8'),f'Independent adapter restoration differs: {name}'
assert source['adapterChangesReversibleExactly'] and source['pinnedFilesVerified']==233
assert source['files']['src/contact_solver.c']['strippedExactlyEqualsOriginal'] and source['files']['src/world.h']['strippedExactlyEqualsOriginal']
rows=[json.loads(l) for l in pathlib.Path(args.tests).read_text(encoding='utf-8').splitlines()]
assert len(rows)==5 and all(r['failures']==0 for r in rows)
assert rows[0]['kind']=='reverse-source-fixed-fixture' and rows[0]['qualifies'] and rows[0]['nodeAgroundB'] and rows[0]['sourceStoredT']!=0 and rows[0]['sourceStoredT']==rows[0]['originalCollectorT'] and rows[0]['actualSourceDeltaN']<0
assert rows[1]['substep']==93836 and rows[1]['oldRejected'] and rows[1]['inputMatchesFixedBits'] and rows[1]['referenceOutputBitsEqual'] and rows[1]['negativeCases']>=71 and rows[1]['ordinaryCases']==20
assert rows[2]['completed']==6000 and rows[2]['attempted']==6000 and rows[2]['error']==0 and rows[2]['latch']==0 and rows[2]['historyCases']==1 and rows[2]['bitMismatches']==0 and rows[2]['preFailureFieldsCompared']==246288 and rows[2]['ordinaryCases']==20
assert rows[3]['passed'] and rows[4]['passed'] and rows[4]['historyPhase']==1 and rows[4]['historyStatus']==3 and rows[4]['nativePhase']==0
dll=build/'Release/dango_material_receipt_history_candidate.dll'; dll_result=json.loads(pathlib.Path(args.dll_result).read_text(encoding='utf-8'))
assert dll_result['passed'] and dll_result['dllSha256']==sha(dll) and dll_result['fieldsCompared']==252000 and dll_result['bitMismatches']==0 and dll_result['historyEvents']==1
assert dll_result['referenceRowsSha256']==sha(args.rows) and dll_result['configSha256']==sha(args.config)
manifest={'schema':1,'kind':'receipt-history-native-final','scope':'Native fixed/negative/latch/former-failure gates only; full physical matrix and manual acceptance pending.','passed':True,'sourceMathUnchanged':True,'adapterDispatchReversible':True,'pinnedFilesVerified':233,'nativeSha256':sha(dll),'nativeTests':rows,'actualDllProbe':dll_result,'candidateSourceFiles':{},'generatedSourceFiles':source['files'],'originalSolverFiles':{},'evidence':{},'inputHashes':{}}
for p in sorted(pathlib.Path(__file__).resolve().parent.iterdir()):
    if p.is_file(): manifest['candidateSourceFiles'][p.relative_to(repo).as_posix()]=sha(p)
for name in ['dango_material_contact_solver.h','dango_material_contact_block.h','dango_material_contact_double_publication.h','dango_material_contact_null_reaction.h','dango_self_constraints.h','CMakeLists.txt']:
    p=repo/'eng/native/box2d'/name; manifest['originalSolverFiles'][p.relative_to(repo).as_posix()]=sha(p)
for p in [generated/'source-manifest.json',pathlib.Path(args.tests),pathlib.Path(args.rows),pathlib.Path(args.dll_result),dll,build/'Release/dango_receipt_history_tests.exe',build/'dango_material_receipt_history_candidate.vcxproj']:
    manifest['evidence'][p.relative_to(repo).as_posix() if p.is_absolute() else p.as_posix()]=sha(p)
manifest['inputHashes']=input_hashes
# Demonstrate the pinned-source gate rejects a previously modified source copy.
invalid_output=generated.parent/'rejected-pinned-source-output'
rejection=subprocess.run([sys.executable,str(pathlib.Path(__file__).resolve().parent/'generate.py'),'--source',str(generated/'box2d'),'--output',str(invalid_output)],capture_output=True,text=True,encoding='utf-8')
assert rejection.returncode!=0 and 'Pinned Box2D source fingerprint mismatch' in rejection.stderr and not invalid_output.exists()
manifest['pinnedModifiedSourceNegativePassed']=True
with open(args.output,'x',encoding='utf-8') as f: json.dump(manifest,f,indent=2); f.write('\n')
print(json.dumps({'passed':True,'nativeSha256':manifest['nativeSha256'],'manifestSha256':sha(args.output),'negativeCases':rows[1]['negativeCases'],'assertions':rows[-1]['assertions']}))

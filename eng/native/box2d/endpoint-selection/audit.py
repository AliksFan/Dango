"""Independent on-disk source restoration, negative import and finite-gate audit."""
import argparse,hashlib,json,pathlib,re,subprocess,sys

parser=argparse.ArgumentParser()
for name in ['generated','build','fixed','receipt','probe','independent','layout','output']:parser.add_argument('--'+name,required=True)
args=parser.parse_args();here=pathlib.Path(__file__).resolve().parent;repo=here.parents[3]
native=here.parent;generated=pathlib.Path(args.generated).resolve();build=pathlib.Path(args.build).resolve()
receipt_root=repo/'artifacts/round-material/receipt-history';accepted_path=receipt_root/'native-final-manifest-v10.json'
own=repo/'artifacts/round-material/endpoint-selection';sha=lambda path:hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()
assert sha(accepted_path)=='82c94869793748f619f265cc6e29e054172a68ee731335de9959e3134f5904fc'
accepted=json.loads(accepted_path.read_text());source=json.loads((generated/'endpoint-source-manifest.json').read_text())
assert source['receiptManifestSha256']==sha(accepted_path) and source['receiptSourceManifestSha256']=='8492595eb68225d1ea35646a87bfc1624551a56aba2aec4d8e84db7846ff6bf9'
old_protection={name:sha(repo/name)==value for name,value in {**accepted['candidateSourceFiles'],**accepted['originalSolverFiles']}.items()}
assert all(old_protection.values())
preservation=json.loads((receipt_root/'second-failure/preservation-final-manifest.json').read_text())
failure_protection={name:sha(receipt_root/'second-failure'/name)==item['sha256'] for name,item in preservation['files'].items()}
assert all(failure_protection.values())
assert sha(receipt_root/'build-v10/Release/dango_material_receipt_history_candidate.dll')==accepted['nativeSha256']
assert sha(receipt_root/'input-recovery/old-approved-native.dll')=='055be2b5e6cd3816eb4d59f616577b653c00512b4750299a013d2cebf676e2c5'
actual={path.relative_to(generated).as_posix():sha(path) for path in generated.rglob('*') if path.is_file() and path.name!='endpoint-source-manifest.json'}
assert actual==source['files'],'Endpoint generated source file set/fingerprint differs'
receipt_source=receipt_root/'generated-v10';restorations={}
for name in {change['name'] for change in source['changes']}:
    text=(generated/name).read_text()
    for change in reversed(source['changes']):
        if change['name']==name:
            assert text.count(change['candidate'])==1
            text=text.replace(change['candidate'],change['original'],1)
    original=(receipt_source/name).read_text() if (receipt_source/name).exists() else (native/pathlib.Path(name).name).read_text()
    restorations[name]=text==original;assert restorations[name],name
for name in ['dango_material_contact_block.h','dango_material_contact_double_publication.h']:
    assert sha(generated/'adapter'/name)==sha(native/name),name
assert sha(generated/'adapter/selection.h')==sha(here/'selection.h')
assert sha(generated/'adapter/endpoint-adapter.inc')==sha(here/'adapter.inc')
pinned=json.loads((native/'receipt-history/pinned-files.json').read_text())['files'];box_restoration={}
for name,value in pinned.items():
    path=generated/'box2d'/name;text=None
    if name in ['src/world.h','src/contact_solver.c']:
        text=re.sub(r'// DANGO_RH_INSERT_BEGIN\n.*?// DANGO_RH_INSERT_END\n','',path.read_text(),flags=re.S)
        if name=='src/contact_solver.c':
            for function in ['b2SolveOverflowContacts','b2ApplyOverflowRestitution','b2StoreOverflowImpulses','b2SolveContactsTask','b2ApplyRestitutionTask','b2StoreImpulsesTask']:
                start=text.index('void '+function+'(');brace=text.index('{',start);finish=text.index('\n}',brace)
                assert text[finish-1]=='\n';text=text[:finish-1]+text[finish:]
                brace=text.index('{',start);assert text[brace+1:brace+3]=='\n\n';text=text[:brace+1]+text[brace+2:]
        box_restoration[name]=hashlib.sha256(text.encode()).hexdigest()==value
    else:box_restoration[name]=sha(path)==value
    assert box_restoration[name],name
fixed=json.loads(pathlib.Path(args.fixed).read_text());rows=[json.loads(line) for line in pathlib.Path(args.receipt).read_text().splitlines()]
probe=json.loads(pathlib.Path(args.probe).read_text());independent=json.loads(pathlib.Path(args.independent).read_text())
assert fixed['failures']==0 and fixed['assertions']>=124 and fixed['negativeCases']>=43
assert fixed['diagnosticBytes']==157832 and fixed['traceBytes']==19112 and fixed['oldPrefixBytes']==138720
assert len(rows)==5 and all(row['failures']==0 for row in rows)
assert rows[0]['qualifies'] and rows[0]['nodeAgroundB']
assert rows[1]['negativeCases']==71 and rows[1]['ordinaryCases']==20 and rows[1]['referenceOutputBitsEqual']
assert rows[2]['completed']==6000 and rows[2]['bitMismatches']==0 and rows[2]['preFailureFieldsCompared']==246288
assert rows[3]['passed'] and rows[4]['passed'] and rows[4]['assertions']==23175
dll=build/'Release/dango_material_endpoint_selection_candidate.dll'
assert probe['passed'] and probe['dllSha256']==sha(dll)
assert probe['first']['fieldsCompared']==252000 and probe['first']['bitMismatches']==0
assert probe['second']['completedFrames']==4089 and probe['second']['prefixFieldsCompared']==171696 and probe['second']['prefixBitMismatches']==0 and probe['second']['neighborAttemptsTotal']==1
assert independent['passed'] and independent['double']['passed'] and independent['float']['passed'] and independent['cap']['passed']
probe_folder=pathlib.Path(args.probe).parent
assert independent['inputHashes']['diagnostic']==sha(probe_folder/'second-diagnostic-buffer.bin')
assert independent['inputHashes']['layout']==sha(args.layout)
negative_dir=own/'source-negatives-v5';negative_dir.mkdir(exist_ok=False)
invalid=json.loads(accepted_path.read_text());invalid['candidateSourceFiles']={};invalid['originalSolverFiles']={}
invalid_path=negative_dir/'invalid-empty-source-map-manifest.json';invalid_path.write_text(json.dumps(invalid,indent=2)+'\n')
negative_runs=[]
for name,input_source,manifest_path in [('empty-source-map',receipt_source,invalid_path),('already-modified-private-source',generated,accepted_path)]:
    rejected=negative_dir/(name+'-unexpected-output')
    process=subprocess.run([sys.executable,str(here/'generate.py'),'--receipt-generated',str(input_source),'--receipt-manifest',str(manifest_path),'--output',str(rejected)],capture_output=True)
    (negative_dir/(name+'-stdout.bin')).write_bytes(process.stdout);(negative_dir/(name+'-stderr.bin')).write_bytes(process.stderr)
    assert process.returncode!=0 and not rejected.exists()
    if name=='empty-source-map':assert b'Approved receipt manifest byte fingerprint mismatch' in process.stderr
    else:assert b'adapter/dango_material_contact_adapter.h' in process.stderr
    negative_runs.append({'name':name,'rejected':True,'exit':process.returncode,'createdOutput':False})
manifest={'schema':1,'kind':'endpoint-selection-native-final','nativeRevision':'endpoint-selection-v1','scope':'Finite native/fixed/negative/prefix/lifecycle gates only; 6660-frame former-failure gates, full matrix and manual acceptance pending.','passed':True,'nativeSha256':sha(dll),'fixedTests':fixed,'receiptNativeTests':rows,'actualDllProbe':probe,'independentLedger':independent,'sourceChangesReversibleExactly':restorations,'originalBox2d233FilesRestored':len(box_restoration),'oldSourceProtection':old_protection,'oldFailure50FilesProtection':failure_protection,'sourceNegativeGates':negative_runs,'candidateSourceFiles':{path.relative_to(repo).as_posix():sha(path) for path in sorted(here.iterdir()) if path.is_file()},'evidence':{}}
evidence=[pathlib.Path(args.fixed),pathlib.Path(args.receipt),pathlib.Path(args.probe),pathlib.Path(args.independent),pathlib.Path(args.layout),dll,build/'Release/dango_endpoint_fixed_tests.exe',build/'Release/dango_endpoint_receipt_tests.exe',build/'dango_material_endpoint_selection_candidate.vcxproj',generated/'endpoint-source-manifest.json',probe_folder/'second-last-original-failure-trace.bin',probe_folder/'second-diagnostic-buffer.bin',probe_folder/'first-rows.bin',probe_folder/'second-rows.bin',own/'receipt-rows-v5.bin']
evidence.extend(path for path in negative_dir.iterdir() if path.is_file())
for path in evidence:manifest['evidence'][path.resolve().relative_to(repo).as_posix()]=sha(path)
with open(args.output,'x',encoding='utf-8') as stream:json.dump(manifest,stream,indent=2);stream.write('\n')
print(json.dumps({'passed':True,'nativeSha256':manifest['nativeSha256'],'manifestSha256':sha(args.output),'negativeCases':114,'assertions':fixed['assertions']+rows[-1]['assertions'],'ordinaryCases':20,'secondPrefixFields':171696}))

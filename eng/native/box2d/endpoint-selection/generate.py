"""Verified private endpoint copy atop the immutable receipt-history source."""
import argparse,hashlib,json,pathlib,shutil

parser=argparse.ArgumentParser()
parser.add_argument('--receipt-generated',required=True)
parser.add_argument('--receipt-manifest',required=True)
parser.add_argument('--output',required=True)
args=parser.parse_args()
here=pathlib.Path(__file__).resolve().parent;repo=here.parents[3];native=here.parent
source=pathlib.Path(args.receipt_generated).resolve();destination=pathlib.Path(args.output).resolve()
sha=lambda path:hashlib.sha256(path.read_bytes()).hexdigest()
accepted_path=pathlib.Path(args.receipt_manifest)
if sha(accepted_path)!='82c94869793748f619f265cc6e29e054172a68ee731335de9959e3134f5904fc':
    raise ValueError('Approved receipt manifest byte fingerprint mismatch')
accepted=json.loads(accepted_path.read_text())
receipt=json.loads((source/'source-manifest.json').read_text())
assert accepted['nativeSha256']=='01e9ad9ccc19e98f60f2b3c5a3987e59f4113e2b88e05e4ded94ef9644bf37e4'
for name,value in accepted['candidateSourceFiles'].items(): assert sha(repo/name)==value,name
for name,value in accepted['originalSolverFiles'].items(): assert sha(repo/name)==value,name
assert sha(source/'source-manifest.json')=='8492595eb68225d1ea35646a87bfc1624551a56aba2aec4d8e84db7846ff6bf9'
pinned=json.loads((native/'receipt-history/pinned-files.json').read_text())['files']
actual={p.relative_to(source/'box2d').as_posix():sha(p) for p in sorted((source/'box2d').rglob('*')) if p.is_file()}
expected={name:receipt['files'].get(name,{}).get('candidateSha256',value) for name,value in pinned.items()}
assert actual==expected,'Receipt Box2D source fingerprint mismatch'
for name,entry in receipt['files'].items():
    path=source/name if name.startswith('adapter/') else source/'box2d'/name
    assert sha(path)==entry['candidateSha256'],name
if destination.exists(): raise ValueError('Output must be new; preserve every prior candidate')
shutil.copytree(source,destination)
changes=[]
def replace(name,old,new):
    path=destination/name;text=path.read_text()
    assert text.count(old)==1,(name,old)
    path.write_text(text.replace(old,new,1),encoding='utf-8',newline='\n')
    changes.append({'name':name,'original':old,'candidate':new})
for name in ['dango_material_contact_null_reaction.h','dango_material_contact_block.h','dango_material_contact_double_publication.h']:
    shutil.copyfile(native/name,destination/'adapter'/name)
replace('adapter/dango_material_contact_null_reaction.h','static inline bool dango_mcn_interval_select(', 'static inline bool dango_endpoint_original_select(')
replace('adapter/dango_material_contact_null_reaction.h','static inline bool dango_mcn_selected(', '#include "selection.h"\n\nstatic inline bool dango_mcn_selected(')
replace('adapter/dango_material_contact_adapter.h','} dango_material_contact_diagnostic;', '    dango_endpoint_trace endpoint; // new tail; old field offsets remain\n} dango_material_contact_diagnostic;')
replace('adapter/dango_material_contact_adapter.h',' #include "adapter.inc"', ' #include "adapter.inc"\n #include "endpoint-adapter.inc"')
replace('adapter/dango_material_contact_adapter.h','dango_rha_solve(body,diagnostic)', 'dango_endpoint_adapter_solve(body,diagnostic)')
replace('adapter/dango_collision_review.c','    dango_material_contact_diagnostic candidate_diagnostic;', '    dango_material_contact_diagnostic candidate_diagnostic;\n    dango_endpoint_trace endpoint_last;\n    uint64_t endpoint_neighbor_attempts_total,endpoint_original_failures_total;')
replace('adapter/dango_collision_review.c','            if(!p->error) p->receipt_history_events+=(uint64_t)p->candidate_diagnostic.history.history_count;', '            if(p->candidate_diagnostic.endpoint.original_failure_available) p->endpoint_last=p->candidate_diagnostic.endpoint;\n            p->endpoint_neighbor_attempts_total+=(uint64_t)p->candidate_diagnostic.endpoint.neighbor_attempts;\n            p->endpoint_original_failures_total+=(uint64_t)p->candidate_diagnostic.endpoint.original_failures;\n            if(!p->error) p->receipt_history_events+=(uint64_t)p->candidate_diagnostic.history.history_count;')
replace('adapter/dango_collision_review.c','p->receipt_history_events=0;', 'p->receipt_history_events=0;\n    memset(&p->endpoint_last,0,sizeof p->endpoint_last); p->endpoint_neighbor_attempts_total=p->endpoint_original_failures_total=0;')
shutil.copyfile(here/'selection.h',destination/'adapter/selection.h')
shutil.copyfile(here/'adapter.inc',destination/'adapter/endpoint-adapter.inc')
manifest={'kind':'endpoint-source-copy','receiptManifestSha256':sha(pathlib.Path(args.receipt_manifest)),'receiptSourceManifestSha256':sha(source/'source-manifest.json'),'changes':changes,'files':{}}
for path in sorted(destination.rglob('*')):
    if path.is_file():
        name=path.relative_to(destination).as_posix();manifest['files'][name]=sha(path)
for name in {change['name'] for change in changes}:
    restored=(destination/name).read_text()
    for change in reversed(changes):
        if change['name']==name:
            assert restored.count(change['candidate'])==1
            restored=restored.replace(change['candidate'],change['original'],1)
    original=(source/name).read_text() if (source/name).exists() else (native/pathlib.Path(name).name).read_text()
    assert restored==original, name+' restoration differs'
manifest['changesReversibleExactly']=True
(destination/'endpoint-source-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(json.dumps({'passed':True,'sourceChangesReversible':True,'files':len(manifest['files']),'output':str(destination)}))

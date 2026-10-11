"""Closed verified private composition of accepted v5 and singleton entry."""
import argparse,hashlib,json,pathlib,shutil
parser=argparse.ArgumentParser();parser.add_argument('--endpoint-generated',required=True);parser.add_argument('--endpoint-manifest',required=True);parser.add_argument('--output',required=True)
args=parser.parse_args();here=pathlib.Path(__file__).resolve().parent;repo=here.parents[3];native=here.parent
source=pathlib.Path(args.endpoint_generated).resolve();destination=pathlib.Path(args.output).resolve();sha=lambda p:hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
accepted_path=pathlib.Path(args.endpoint_manifest).resolve()
assert sha(accepted_path)=='3cfb13707654e477badbd87441a891d9d67f1e40c8f27057748c2eb02c9049e6','Approved v5 manifest byte fingerprint mismatch'
accepted=json.loads(accepted_path.read_text());assert accepted['nativeSha256']=='d70d1348c76f27e4eb8a122c771da68d41a446ef71e1f32c0bb4b51e9ca6fbc0'
for name,value in accepted['candidateSourceFiles'].items():assert sha(repo/name)==value,name
receipt_path=repo/'artifacts/round-material/receipt-history/native-final-manifest-v10.json';assert sha(receipt_path)=='82c94869793748f619f265cc6e29e054172a68ee731335de9959e3134f5904fc'
receipt=json.loads(receipt_path.read_text())
for name,value in {**receipt['candidateSourceFiles'],**receipt['originalSolverFiles']}.items():assert sha(repo/name)==value,name
assert sha(source/'endpoint-source-manifest.json')=='4ffb627043894e49ec9b0b47f1b8343ed8c864f783d95999dd616f4a8ad3cf92'
base=json.loads((source/'endpoint-source-manifest.json').read_text())
actual={p.relative_to(source).as_posix():sha(p) for p in source.rglob('*') if p.is_file() and p.name!='endpoint-source-manifest.json'}
assert actual==base['files'],'Complete accepted v5 source map mismatch'
assert not destination.exists(),'Output must be new; retain every prior candidate'
shutil.copytree(source,destination);changes=[]
def replace(name,old,new):
 path=destination/name;text=path.read_text();assert text.count(old)==1,(name,old)
 path.write_text(text.replace(old,new,1),encoding='utf-8',newline='\n');changes.append({'name':name,'original':old,'candidate':new})
block='adapter/dango_material_contact_block.h';text=(source/block).read_text()
start=text.index('    dango_material_contact_solver probe = *reference;',text.index('static inline bool dango_mcb_null_next_mode('))
end=text.index('\n}\n#endif',start);body=text[start:end]
tail='    dango_material_contact_block_mode next = DANGO_MATERIAL_CONTACT_BLOCK_ENABLED;\n    int change = dango_mcb_next_mode(&probe, reference, report, &next);\n    if (change < 0) return false;\n    report->modes[change] = next;\n    return true;'
assert body.endswith(tail)
core=body[:-len(tail)].replace('return false;','return -1;')
helper='static inline int dango_singleton_certified_particular_mode(const dango_material_contact_solver* reference,\n    const dango_material_contact_block_report* report,dango_material_contact_block_mode* next_mode)\n{\n    const dango_material_contact_null_report* nr = &report->null_reaction;\n'+core+'    return dango_mcb_next_mode(&probe, reference, report, next_mode);\n}\n\n'
replace(block,'static inline bool dango_mcb_null_next_mode(',helper+'static inline bool dango_mcb_null_next_mode(')
replacement='    dango_material_contact_block_mode next = DANGO_MATERIAL_CONTACT_BLOCK_ENABLED;\n    int change = dango_singleton_certified_particular_mode(reference, report, &next);\n    if (change < 0) return false;\n    report->modes[change] = next;\n    return true;'
replace(block,body,replacement)
replace(block,'}\n#endif\n\n#if defined(DANGO_MATERIAL_CONTACT_RANK_MODE_CANDIDATE)','}\n#include "singleton.h"\n#endif\n\n#if defined(DANGO_MATERIAL_CONTACT_RANK_MODE_CANDIDATE)')
replace(block,'                if (dango_mcb_null_next_mode(&reference, &block->report)) continue;','                if (dango_mcb_null_next_mode(&reference, &block->report)) continue;\n                if (dango_singleton_next_mode(&reference, &block->report)) continue;')
replace(block,'        if (!rows_ok || !dango_mcb_modes_valid(&block->contact, &rebuilt, block->contact.velocities, &block->report) ||','''#ifdef DANGO_SINGLETON_TEST_BEFORE_FINAL
        DANGO_SINGLETON_TEST_BEFORE_FINAL(block);
#endif
        if (!rows_ok || !dango_mcb_modes_valid(&block->contact, &rebuilt, block->contact.velocities, &block->report) ||''')
selection='adapter/selection.h'
replace(selection,'typedef struct dango_endpoint_trace','#include "budget.h"\n\ntypedef struct dango_endpoint_trace')
replace(selection,'static inline bool dango_mcn_interval_select(','static inline bool dango_singleton_v5_select(')
replace(selection,'trace->neighbor_attempts != 0 || !dango_endpoint_provenance(report)','trace->neighbor_attempts != 0 || !dango_singleton_neighbor_available() || !dango_endpoint_provenance(report)')
replace(selection,'    ++trace->neighbor_attempts;','    ++trace->neighbor_attempts;\n    dango_singleton_neighbor_spent();')
replace(selection,'    return true;\n}\n#endif\n','    return true;\n}\n#include "observer.h"\n#endif\n')
replace('adapter/endpoint-adapter.inc','!dango_endpoint_begin(&diagnostic->endpoint, &diagnostic->block.report.null_reaction)','!dango_singleton_begin(&diagnostic->singleton,&diagnostic->endpoint,&diagnostic->block)')
replace('adapter/endpoint-adapter.inc','    bool solved = dango_rha_solve(body, diagnostic);\n    dango_endpoint_end();','    diagnostic->singleton.substep=diagnostic->substeps; diagnostic->singleton.sequence=diagnostic->history.sequence;\n    bool solved = dango_rha_solve(body, diagnostic);\n    dango_singleton_end(solved);')
replace('adapter/dango_material_contact_adapter.h','} dango_material_contact_diagnostic;','    dango_singleton_trace singleton; // new tail; retained v5 prefix unchanged\n} dango_material_contact_diagnostic;')
collision='adapter/dango_collision_review.c'
replace(collision,'    uint64_t endpoint_neighbor_attempts_total,endpoint_original_failures_total;','    uint64_t endpoint_neighbor_attempts_total,endpoint_original_failures_total;\n    dango_singleton_trace singleton_last;\n    uint64_t singleton_frames,singleton_mode_attempts_total;')
replace(collision,'    if (!p->error)\n    {\n        int small_steps','    if (!p->error)\n    {\n        ++p->singleton_frames;\n        int small_steps')
replace(collision,'            if(p->candidate_diagnostic.endpoint.original_failure_available) p->endpoint_last=p->candidate_diagnostic.endpoint;','            p->candidate_diagnostic.singleton.frame_token=p->singleton_frames;\n            p->candidate_diagnostic.singleton.final_phase=p->candidate_diagnostic.phase;\n            p->candidate_diagnostic.singleton.final_error=p->error;\n            p->candidate_diagnostic.singleton.substep_passed=p->error==0;\n            p->singleton_mode_attempts_total+=(uint64_t)p->candidate_diagnostic.singleton.mode_attempts;\n            if(p->candidate_diagnostic.singleton.original_failure_available) p->singleton_last=p->candidate_diagnostic.singleton;\n            if(p->candidate_diagnostic.endpoint.original_failure_available) p->endpoint_last=p->candidate_diagnostic.endpoint;')
replace(collision,'        if (!p->error) memcpy(p->snapshot,candidate,sizeof candidate);','        if (!p->error) {\n            memcpy(p->snapshot,candidate,sizeof candidate);\n            p->candidate_diagnostic.singleton.snapshot_published=true;\n            if(p->singleton_last.original_failure_available&&p->singleton_last.frame_token==p->singleton_frames) p->singleton_last.snapshot_published=true;\n        }')
replace(collision,'    memset(&p->endpoint_last,0,sizeof p->endpoint_last); p->endpoint_neighbor_attempts_total=p->endpoint_original_failures_total=0;','    memset(&p->endpoint_last,0,sizeof p->endpoint_last); p->endpoint_neighbor_attempts_total=p->endpoint_original_failures_total=0;\n    memset(&p->singleton_last,0,sizeof p->singleton_last);p->singleton_frames=p->singleton_mode_attempts_total=0;')
for name in ['budget.h','observer.h','singleton.h']:shutil.copyfile(here/name,destination/'adapter'/name)
for name in {c['name'] for c in changes}:
 restored=(destination/name).read_text()
 for c in reversed(changes):
  if c['name']==name:assert restored.count(c['candidate'])==1;restored=restored.replace(c['candidate'],c['original'],1)
 assert restored==(source/name).read_text(),name+' reverse restoration mismatch'
manifest={'kind':'closed-singleton-source-copy','endpointManifestSha256':sha(accepted_path),'endpointSourceManifestSha256':sha(source/'endpoint-source-manifest.json'),'changes':changes,'changesReversibleExactly':True,'files':{p.relative_to(destination).as_posix():sha(p) for p in sorted(destination.rglob('*')) if p.is_file()}}
(destination/'singleton-source-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(json.dumps({'passed':True,'files':len(manifest['files']),'reversibleExactly':True}))

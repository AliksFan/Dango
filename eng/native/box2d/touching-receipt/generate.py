"""Reversible private composition; no writes to prior sources or evidence."""
import argparse,pathlib,json,hashlib,shutil
ap=argparse.ArgumentParser();ap.add_argument('--singleton-generated',required=True);ap.add_argument('--output',required=True);args=ap.parse_args()
here=pathlib.Path(__file__).resolve().parent;repo=here.parents[3];native=here.parent
src=pathlib.Path(args.singleton_generated).resolve();dst=pathlib.Path(args.output).resolve();sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
approved=repo/'artifacts/round-material/singleton-mode/native-final-manifest-v5.json'
assert sha(approved)=='38c649bdac8aacc141ed7e855e4aa0a61b897f7c863140ee5c3f66273a46aabd'
base_path=src/'singleton-source-manifest.json';assert sha(base_path)=='9eb2a92944cc8fd5ff9764f3f6dceb83fbf3a9591bebff0bc5176f216c5db7e9'
base=json.loads(base_path.read_text());actual={p.relative_to(src).as_posix():sha(p) for p in src.rglob('*') if p.is_file() and p.name!='singleton-source-manifest.json'}
assert actual==base['files'];approved_data=json.loads(approved.read_text())
for name,value in approved_data['candidateSourceFiles'].items():assert sha(repo/name)==value,name
assert not dst.exists();shutil.copytree(src,dst);changes=[];extra={}
def replace(name,old,new):
    p=dst/name;text=p.read_text();assert text.count(old)==1,(name,old)
    p.write_text(text.replace(old,new,1),encoding='utf-8',newline='\n');changes.append({'name':name,'original':old,'candidate':new})
solver='adapter/dango_material_contact_solver.h';shutil.copyfile(native/'dango_material_contact_solver.h',dst/solver)
extra[solver]={'source':(native/'dango_material_contact_solver.h').relative_to(repo).as_posix(),'sourceSha256':sha(native/'dango_material_contact_solver.h')}
replace(solver,'static inline bool dango_material_contact_build(','''static inline bool dango_tr_import_receipt(const dango_constraint_state*,const dango_material_ground_receipt*,int,int);
static inline bool dango_material_contact_build(''')
replace(solver,'        if (!isfinite(bound) || fabs(receipt->tangent_impulse) > bound + receipt_roundoff)',
'''        bool outcone = fabs(receipt->tangent_impulse) > bound + receipt_roundoff;
        bool touching = outcone && dango_tr_import_receipt(state,receipts,receipt_count,i);
        if (!isfinite(bound) || (outcone && !touching))''')
replace(solver,'support->receipt_cone_slack = fmax(0, fabs(receipt->tangent_impulse) - bound);','support->receipt_cone_slack = touching ? 0 : fmax(0, fabs(receipt->tangent_impulse) - bound);')
adapter='adapter/dango_material_contact_adapter.h'
replace(adapter,'#include "retirement.h"','#include "retirement.h"\n#include "touching.h"')
replace(adapter,'} dango_material_contact_diagnostic;','    dango_tr_view touching; // separate current classification, retained prefix unchanged\n} dango_material_contact_diagnostic;')
replace(adapter,'static int dango_material_contact_substep(','static int dango_tr_material_contact_substep(')
tail='''
static int dango_material_contact_substep(dango_self_soft_body* body,const dango_self_physics_config* config,
    float dt,const float* controls,dango_material_contact_diagnostic* diagnostic)
{
    dango_tr_end();
    int result=dango_tr_material_contact_substep(body,config,dt,controls,diagnostic);
    dango_tr_end();
    return result;
}
'''
replace(adapter,'    diagnostic->phase = 6; return 0;\n}\n\n#endif','    diagnostic->phase = 6; return 0;\n}\n'+tail+'\n#endif')
collision='adapter/dango_collision_review.c'
replace(collision,'    uint64_t singleton_frames,singleton_mode_attempts_total;','    uint64_t singleton_frames,singleton_mode_attempts_total;\n    dango_material_contact_diagnostic touching_last;\n    uint64_t touching_events;')
replace(collision,'            if(!p->error) p->receipt_history_events+=(uint64_t)p->candidate_diagnostic.history.history_count;','            if(!p->error) p->receipt_history_events+=(uint64_t)p->candidate_diagnostic.history.history_count;\n            if(!p->error && p->candidate_diagnostic.touching.pending_count) { p->touching_last=p->candidate_diagnostic; ++p->touching_events; }')
replace(collision,'            if(p->singleton_last.original_failure_available&&p->singleton_last.frame_token==p->singleton_frames) p->singleton_last.snapshot_published=true;','            if(p->singleton_last.original_failure_available&&p->singleton_last.frame_token==p->singleton_frames) p->singleton_last.snapshot_published=true;\n            if(p->touching_events && p->touching_last.singleton.frame_token==p->singleton_frames) p->touching_last.singleton.snapshot_published=true;')
replace(collision,'    memset(&p->singleton_last,0,sizeof p->singleton_last);p->singleton_frames=p->singleton_mode_attempts_total=0;','    memset(&p->singleton_last,0,sizeof p->singleton_last);p->singleton_frames=p->singleton_mode_attempts_total=0;\n    memset(&p->touching_last,0,sizeof p->touching_last);p->touching_events=0;')
for n in ['touching.h','adapter.inc']:
    shutil.copyfile(here/n,dst/'adapter'/n);extra['adapter/'+n]={'source':(here/n).relative_to(repo).as_posix(),'sourceSha256':sha(here/n)}
for n in {c['name'] for c in changes}:
    restored=(dst/n).read_text()
    for c in reversed(changes):
        if c['name']==n:assert restored.count(c['candidate'])==1;restored=restored.replace(c['candidate'],c['original'],1)
    original=(native/'dango_material_contact_solver.h') if n==solver else src/n
    assert restored==original.read_text(),n
unchanged={p.relative_to(src).as_posix():sha(p) for p in src.rglob('*') if p.is_file() and p.relative_to(src).as_posix() not in {c['name'] for c in changes}}
assert all(sha(dst/n)==v for n,v in unchanged.items())
manifest={'kind':'closed-touching-receipt-source-copy','baseManifestSha256':sha(base_path),'baseNativeManifestSha256':sha(approved),'changes':changes,'extraSources':extra,'changesReversibleExactly':True,'allBaseFilesAccountedFor':len(actual),'unchangedBaseFiles':unchanged,'files':{p.relative_to(dst).as_posix():sha(p) for p in sorted(dst.rglob('*')) if p.is_file()}}
(dst/'touching-source-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(json.dumps({'passed':True,'files':len(manifest['files']),'reversibleExactly':True}))

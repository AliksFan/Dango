"""Candidate-only source copy. Insertions are reversible; pinned tree is immutable."""
import argparse,hashlib,json,pathlib,shutil,re

ap=argparse.ArgumentParser(); ap.add_argument('--source',required=True); ap.add_argument('--output',required=True); args=ap.parse_args()
here=pathlib.Path(__file__).resolve().parent; repo=here.parents[3]
src=pathlib.Path(args.source).resolve(); dst=pathlib.Path(args.output).resolve()
expected=json.loads((here/'pinned-files.json').read_text(encoding='utf-8'))['files']
actual={p.relative_to(src).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(src.rglob('*')) if p.is_file()}
if actual!=expected: raise ValueError('Pinned Box2D source fingerprint mismatch')
if dst.exists(): raise ValueError('Output must be new; preserve existing evidence')
shutil.copytree(src,dst/'box2d'); (dst/'adapter').mkdir()
manifest={'sourceCommit':'8c661469c9507d3ad6fbd2fea3f1aa71669c2fe3','pinnedFilesVerified':len(expected),'files':{}}
begin='// DANGO_RH_INSERT_BEGIN\n'; end='// DANGO_RH_INSERT_END\n'
def block(s): return begin+s+'\n'+end
def save_verified(rel,old,new):
    stripped=re.sub(re.escape(begin)+r'.*?'+re.escape(end),'',new,flags=re.S)
    assert stripped==old,rel+' original math/code differs after stripping insertions'
    target=dst/'box2d'/rel; target.write_bytes(new.encode('utf-8'))
    manifest['files'][rel]={'sourceSha256':hashlib.sha256((src/rel).read_bytes()).hexdigest(),'candidateSha256':hashlib.sha256(target.read_bytes()).hexdigest(),'strippedExactlyEqualsOriginal':True}
old=(src/'src/world.h').read_text(encoding='utf-8'); anchor='\tb2ArenaAllocator arena;'
assert old.count(anchor)==1
new=old.replace(anchor,block('#ifdef DANGO_RECEIPT_HISTORY_CANDIDATE\n\tvoid* dangoReceiptObserver;\n#endif')+anchor)
save_verified('src/world.h',old,new)
old=(src/'src/contact_solver.c').read_text(encoding='utf-8'); new=old
overflow='''#include "capture.h"
static void dango_rh_overflow(b2StepContext* context,int stage)
{
    b2GraphColor* color=context->graph->colors+B2_OVERFLOW_INDEX;
    for(int i=0;i<color->contactSims.count;++i) {
        const b2ContactConstraint* c=color->overflowConstraints+i;
        const b2ContactSim* s=color->contactSims.data+i;
        for(int p=0;p<s->manifold.pointCount;++p)
            dango_rh_event(context->world,s,p,stage,c->points[p].normalImpulse,c->points[p].tangentImpulse,c->friction,c->normal.x,c->normal.y,0,0);
    }
}'''
anchor='// contact separation for sub-stepping'; assert new.count(anchor)==1
new=new.replace(anchor,block(overflow)+anchor)
simd='''static void dango_rh_simd_point(b2StepContext* context,const b2ContactConstraintSIMD* c,int p,int stage,const float* delta)
{
    int offset=(int)(c-context->simdContactConstraints)*B2_SIMD_WIDTH;
    const float* n=p?(const float*)&c->normalImpulse2:(const float*)&c->normalImpulse1;
    const float* t=p?(const float*)&c->tangentImpulse2:(const float*)&c->tangentImpulse1;
    for(int lane=0;lane<B2_SIMD_WIDTH;++lane) {
        const b2ContactSim* s=context->contacts[offset+lane];
        if(!s || p>=s->manifold.pointCount) continue;
        dango_rh_event(context->world,s,p,stage,n[lane],t[lane],((const float*)&c->friction)[lane],((const float*)&c->normal.X)[lane],((const float*)&c->normal.Y)[lane],delta?delta[lane]:0,0);
    }
}
static void dango_rh_simd(b2StepContext* context,int start,int end,int color,int stage)
{
    const b2ContactConstraintSIMD* base=color<0?context->simdContactConstraints:context->graph->colors[color].simdConstraints;
    for(int i=start;i<end;++i) for(int p=0;p<2;++p) dango_rh_simd_point(context,base+i,p,stage,NULL);
}'''
anchor='void b2WarmStartContactsTask('; assert new.count(anchor)==1
new=new.replace(anchor,block(simd)+anchor)
def insert_ends(name,before,after):
    global new
    a=new.index('void '+name+'('); brace=new.index('{',a); finish=new.index('\n}',brace)
    new=new[:finish]+'\n'+block(after)+new[finish:]
    new=new[:brace+1]+'\n'+block(before)+new[brace+1:]
insert_ends('b2SolveOverflowContacts','', 'if(!useBias) dango_rh_overflow(context,21);')
insert_ends('b2ApplyOverflowRestitution','dango_rh_overflow(context,30);','dango_rh_overflow(context,31);')
insert_ends('b2StoreOverflowImpulses','dango_rh_overflow(context,40);','dango_rh_overflow(context,41);')
insert_ends('b2SolveContactsTask','','if(!useBias) dango_rh_simd(context,startIndex,endIndex,colorIndex,21);')
insert_ends('b2ApplyRestitutionTask','dango_rh_simd(context,startIndex,endIndex,colorIndex,30);','dango_rh_simd(context,startIndex,endIndex,colorIndex,31);')
insert_ends('b2StoreImpulsesTask','dango_rh_simd(context,startIndex,endIndex,-1,40);','dango_rh_simd(context,startIndex,endIndex,-1,41);')
def in_function(name,anchor,addition):
    global new
    a=new.index('void '+name+'('); b=new.index('\n}',new.index('{',a))+2
    body=new[a:b]; assert body.count(anchor)==1,(name,anchor)
    body=body.replace(anchor,anchor+block(addition))
    new=new[:a]+body+new[b:]
in_function('b2ApplyOverflowRestitution','\t\t\t\timpulse = newImpulse - cp->normalImpulse;\n','dango_rh_event(context->world,color->contactSims.data+i,j,35,cp->normalImpulse,cp->tangentImpulse,constraint->friction,normal.x,normal.y,impulse,0);')
for point in (1,2):
    in_function('b2ApplyRestitutionTask',f'\t\t\tb2FloatW impulse = b2SubW( newImpulse, c->normalImpulse{point} );\n',f'dango_rh_simd_point(context,c,{point-1},35,(const float*)&impulse);')
# Added newline wrappers are stripped along with their markers.
stripped=re.sub(re.escape(begin)+r'.*?'+re.escape(end),'',new,flags=re.S)
# insert_ends introduced one separator newline outside each block; remove them.
for name in ['b2SolveOverflowContacts','b2ApplyOverflowRestitution','b2StoreOverflowImpulses','b2SolveContactsTask','b2ApplyRestitutionTask','b2StoreImpulsesTask']:
    a=stripped.index('void '+name+'('); brace=stripped.index('{',a); finish=stripped.index('\n}',brace)
    stripped=stripped[:finish-1]+stripped[finish:] if stripped[finish-1]=='\n' else stripped
    brace=stripped.index('{',a); assert stripped[brace+1:brace+3]=='\n\n'; stripped=stripped[:brace+1]+stripped[brace+2:]
assert stripped==old,'contact solver restoration differs'
(dst/'box2d/src/contact_solver.c').write_bytes(new.encode('utf-8'))
manifest['files']['src/contact_solver.c']={'sourceSha256':hashlib.sha256((src/'src/contact_solver.c').read_bytes()).hexdigest(),'candidateSha256':hashlib.sha256((dst/'box2d/src/contact_solver.c').read_bytes()).hexdigest(),'strippedExactlyEqualsOriginal':True}

adapter=(repo/'eng/native/box2d/dango_material_contact_adapter.h').read_text(encoding='utf-8')
changes=[]
def replace(s,old,new):
    assert s.count(old)==1,old
    changes.append({'original':old,'candidate':new}); return s.replace(old,new)
adapter=replace(adapter,'typedef struct dango_material_contact_diagnostic',' #include "retirement.h"\ntypedef struct dango_material_contact_diagnostic')
adapter=replace(adapter,'    int phase, error, position_iterations, receipt_count;','    dango_rh_view history;\n    int history_phase,history_status; // status 0 unchecked,1 valid,2 raw structure,3 classification/source,4 revalidation; phase 2 filter,3 presolve,4 double,5 float,6 cap,7 writeback\n    int phase, error, position_iterations, receipt_count;')
adapter=replace(adapter,'static bool dango_mca_geometry',' #include "adapter.inc"\nstatic bool dango_mca_geometry')
adapter=replace(adapter,'dango_material_contact_block_build(&state, diagnostic->receipts, diagnostic->receipt_count,\n        dt, &diagnostic->block, &diagnostic->block_report)','dango_rha_build(body,diagnostic,&state,dt)')
adapter=replace(adapter,'dango_material_contact_block_solve(&diagnostic->block,\n        &diagnostic->uncapped, &diagnostic->block_report)','dango_rha_solve(body,diagnostic)')
adapter=replace(adapter,'    // A common positive scale preserves every homogeneous rate inequality.','    if(!dango_rha_gate(body,diagnostic,&state,6)) return dango_mca_failure(diagnostic,-96);\n    // A common positive scale preserves every homogeneous rate inequality.')
adapter=replace(adapter,'    // No candidate position/velocity is written before all stages pass.','    if(!dango_rha_gate(body,diagnostic,&state,7)) return dango_mca_failure(diagnostic,-96);\n    // No candidate position/velocity is written before all stages pass.')
restored=adapter
for change in reversed(changes): restored=restored.replace(change['candidate'],change['original'],1)
assert restored==(repo/'eng/native/box2d/dango_material_contact_adapter.h').read_text(encoding='utf-8')
(dst/'adapter/dango_material_contact_adapter.h').write_bytes(adapter.encode('utf-8'))
adapter_change_count=len(changes)
collision=(repo/'eng/native/box2d/dango_collision_review.c').read_text(encoding='utf-8')
collision=replace(collision,'    dango_material_contact_diagnostic candidate_diagnostic;','    dango_rh_capture receipt_history;\n    uint64_t receipt_history_events;\n    dango_material_contact_diagnostic candidate_diagnostic;')
collision=replace(collision,'            b2World_Step(p->bodies[0]->world_id,small_dt,1);','            if(!dango_rh_begin(&p->receipt_history,p->bodies[0]->world_id,p->bodies[0]->ground_shape_id,p->bodies[0]->node_shape_ids,p->bodies[0]->node_body_ids)) { dango_rha_capture_rejected(&p->candidate_diagnostic); p->error=-96; break; }\n            b2World_Step(p->bodies[0]->world_id,small_dt,1);')
collision=replace(collision,'                );\n#if defined(DANGO_MATERIAL_IDLE_TRACE)','                );\n            if(!p->error) p->receipt_history_events+=(uint64_t)p->candidate_diagnostic.history.history_count;\n#if defined(DANGO_MATERIAL_IDLE_TRACE)')
collision=replace(collision,'    for (int slot=0;slot<p->capacity;++slot) if (p->active[slot]) dango_self_destroy_rig(p->bodies[slot]);','    dango_rh_end(&p->receipt_history);\n    for (int slot=0;slot<p->capacity;++slot) if (p->active[slot]) dango_self_destroy_rig(p->bodies[slot]);')
collision=replace(collision,'    memset(&p->candidate_diagnostic,0,sizeof p->candidate_diagnostic);','    memset(&p->candidate_diagnostic,0,sizeof p->candidate_diagnostic);\n    uint64_t sequence=p->receipt_history.sequence;\n    memset(&p->receipt_history,0,sizeof p->receipt_history); p->receipt_history.sequence=sequence; p->receipt_history_events=0;')
restored=collision
for change in reversed(changes[adapter_change_count:]): restored=restored.replace(change['candidate'],change['original'],1)
assert restored==(repo/'eng/native/box2d/dango_collision_review.c').read_text(encoding='utf-8')
(dst/'adapter/dango_collision_review.c').write_bytes(collision.encode('utf-8'))
# Local include resolution intentionally selects the private copies.
for name in ['dango_box2d_bridge.c','dango_material_idle_diagnostics.c']:
    shutil.copyfile(repo/'eng/native/box2d'/name,dst/'adapter'/name)
manifest['adapterChanges']=changes
manifest['adapterChangesReversibleExactly']=True
for name in ['dango_material_contact_adapter.h','dango_collision_review.c','dango_box2d_bridge.c','dango_material_idle_diagnostics.c']:
    manifest['files']['adapter/'+name]={'sourceSha256':hashlib.sha256((repo/'eng/native/box2d'/name).read_bytes()).hexdigest(),'candidateSha256':hashlib.sha256((dst/'adapter'/name).read_bytes()).hexdigest()}
(dst/'source-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'pinnedFilesVerified':len(expected),'contactMathUnchanged':True,'worldOriginalUnchanged':True,'output':str(dst)}))

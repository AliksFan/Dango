#define _CRT_SECURE_NO_WARNINGS
#include <stdio.h>
#include <stdlib.h>
#include "dango_self_physics_internal.h"
static void rh_fixture(dango_self_soft_body*,void*,const void*,double);
#define DANGO_RH_TEST_BEFORE_BUILD(body,d,state,dt) rh_fixture(body,d,state,dt)
#include "candidate.c"
#include "world.h"
#include "contact.h"
#include "body.h"
#include "solver_set.h"
#include "contact_solver.h"
static int assertions, failures, negative_cases, ordinary_cases, history_cases;
static dango_constraint_state expected_fixed;
static FILE* fixture_output;
static int inject_failure;
static bool fixture_running;
static float fixture_maximum_speed;
#define CHECK(c) do { ++assertions; if(!(c)) { ++failures; fprintf(stderr,"FAIL %d: %s\n",__LINE__,#c); } } while(0)
#define NEGATIVE(c) do { ++negative_cases; CHECK(!(c)); } while(0)
#define FIELD_BITS(a,b,field) (memcmp(&(a)->field,&(b)->field,sizeof((a)->field))==0)
static bool report_bits_equal(const dango_material_contact_report* a,const dango_material_contact_report* b)
{
#define MATCH(field) if(!FIELD_BITS(a,b,field)) return false
    MATCH(status); MATCH(sweeps); MATCH(active_shape_rows); MATCH(active_normal_rows); MATCH(active_tangent_rows);
    MATCH(row_visits); MATCH(cone_retractions); MATCH(geometry_residual);
    MATCH(maximum_linear_closing); MATCH(maximum_relative_linear_closing); MATCH(maximum_angular_closing);
    MATCH(maximum_relative_linear_complementarity); MATCH(maximum_angular_complementarity);
    MATCH(maximum_relative_friction_rate); MATCH(maximum_friction_cone_excess); MATCH(maximum_receipt_cone_slack);
    MATCH(energy_before); MATCH(energy_after); MATCH(energy_allowance);
    MATCH(momentum_before.x); MATCH(momentum_before.y); MATCH(momentum_after.x); MATCH(momentum_after.y);
    MATCH(external_impulse.x); MATCH(external_impulse.y); MATCH(momentum_residual); MATCH(momentum_allowance);
    MATCH(angular_momentum_before); MATCH(angular_momentum_after); MATCH(external_torque);
    MATCH(angular_momentum_residual); MATCH(angular_momentum_allowance);
    MATCH(maximum_relative_velocity_ledger_residual); MATCH(maximum_relative_velocity_ledger_allowance);
    MATCH(float_output_checked); MATCH(ledger_evaluated);
#undef MATCH
    return true;
}
static bool cap_bits_equal(const dango_material_contact_cap_report* a,const dango_material_contact_cap_report* b)
{
    return FIELD_BITS(a,b,status) && FIELD_BITS(a,b,scale) && FIELD_BITS(a,b,maximum_before) && FIELD_BITS(a,b,maximum_after) &&
        FIELD_BITS(a,b,energy_before) && FIELD_BITS(a,b,energy_after) && FIELD_BITS(a,b,energy_allowance) &&
        FIELD_BITS(a,b,cap_momentum_change.x) && FIELD_BITS(a,b,cap_momentum_change.y) && FIELD_BITS(a,b,cap_angular_momentum_change) &&
        FIELD_BITS(a,b,contact_output_checked) && FIELD_BITS(a,b,exact_common_scale_checked) && report_bits_equal(&a->final_rows,&b->final_rows);
}
static void reverse_source_fixture(void)
{
    // Real identities/manifold, then one fixed-input invocation of the ORIGINAL
    // overflow kernels. Impulse certificates come only from generated source
    // hooks. This is a source fixture, not an accepted reverse-world trajectory.
    b2WorldDef wd=b2DefaultWorldDef(); wd.gravity=b2Vec2_zero; wd.restitutionThreshold=.1f;
    b2WorldId id=b2CreateWorld(&wd); dango_self_soft_body toy={0}; toy.world_id=id;
    b2Circle node_circle={{0,0},.036f}; b2ShapeDef sd=b2DefaultShapeDef(); sd.material.friction=.72f; sd.material.restitution=.5f;
    for(int n=0;n<16;++n) {
        b2BodyDef bd=b2DefaultBodyDef(); bd.type=b2_dynamicBody; bd.fixedRotation=true; bd.position=(b2Vec2){n==15?0:(float)(100+n*10),-.685f};
        toy.node_body_ids[n]=b2CreateBody(id,&bd);
        if(n==15) {
            // Capsule-circle is a primary official contact type with capsule A,
            // unlike circle-circle where static proxy sorting puts ground A.
            b2Capsule node_capsule={{0,-.004f},{0,.004f},.032f};
            toy.node_shape_ids[n]=b2CreateCapsuleShape(toy.node_body_ids[n],&sd,&node_capsule);
            CHECK(b2Shape_GetType(toy.node_shape_ids[n])==b2_capsuleShape);
        } else toy.node_shape_ids[n]=b2CreateCircleShape(toy.node_body_ids[n],&sd,&node_circle);
    }
    b2BodyDef ground_def=b2DefaultBodyDef(); ground_def.position=(b2Vec2){0,-10.72f}; b2BodyId ground_body=b2CreateBody(id,&ground_def);
    b2Circle ground_circle={{0,0},10}; sd.material.friction=1;
    toy.ground_shape_id=b2CreateCircleShape(ground_body,&sd,&ground_circle);
    b2World_Step(id,.001f,1);
    dango_rh_capture capture={0}; CHECK(dango_rh_begin(&capture,id,toy.ground_shape_id,toy.node_shape_ids,toy.node_body_ids));
    b2World_Step(id,.001f,1);
    b2World* world=b2GetWorldFromId(id); b2ContactSim* sim=NULL;
    for(int i=0;i<world->contacts.count;++i) {
        b2Contact* contact=world->contacts.data+i;
        if(contact->contactId>=0 && contact->shapeIdA==toy.node_shape_ids[15].index1-1 && contact->shapeIdB==toy.ground_shape_id.index1-1) { sim=b2GetContactSim(world,contact); break; }
    }
    CHECK(sim!=NULL); if(!sim) { dango_rh_end(&capture); b2DestroyWorld(id); return; }
    CHECK(sim->manifold.pointCount==1 && sim->manifold.normal.x==0 && sim->manifold.normal.y==-1);
    memset(capture.points,0,sizeof capture.points);
    b2Body* node=b2GetBodyFullId(world,toy.node_body_ids[15]); b2SolverSet* awake=b2SolverSetArray_Get(&world->solverSets,b2_awakeSet);
    awake->bodyStates.data[node->localIndex]=b2_identityBodyState; awake->bodyStates.data[node->localIndex].linearVelocity=(b2Vec2){5,0};
    b2ContactConstraint constraint={0}; constraint.indexA=node->localIndex; constraint.indexB=B2_NULL_INDEX; constraint.normal=sim->manifold.normal; constraint.invMassA=1/b2Body_GetMass(toy.node_body_ids[15]); constraint.friction=sim->friction; constraint.restitution=.5f; constraint.pointCount=1;
    constraint.points[0].normalImpulse=.0192780085f; constraint.points[0].normalMass=1/constraint.invMassA; constraint.points[0].tangentMass=constraint.points[0].normalMass; constraint.points[0].relativeVelocity=-2; constraint.points[0].totalNormalImpulse=constraint.points[0].normalImpulse;
    b2ConstraintGraph graph={0}; graph.colors[B2_OVERFLOW_INDEX].overflowConstraints=&constraint; graph.colors[B2_OVERFLOW_INDEX].contactSims.data=sim; graph.colors[B2_OVERFLOW_INDEX].contactSims.count=1;
    b2StepContext context={0}; context.world=world; context.graph=&graph; context.inv_h=1000;
    b2SolveOverflowContacts(&context,false);
    // Fixed relative velocity supplied to the next original source stage,
    // analogous to a later coupled constraint changing this body's velocity.
    awake->bodyStates.data[node->localIndex].linearVelocity.y=1.05f;
    b2ApplyOverflowRestitution(&context); b2StoreOverflowImpulses(&context);
    dango_material_contact_diagnostic raw={0}; CHECK(dango_mca_receipts(&toy,&raw));
    CHECK(raw.receipt_count==1 && raw.receipts[0].node==15);
    dango_rh_point* point=capture.points+15; CHECK(dango_rh_live(&capture,point));
    CHECK(point->delta_n<0 && point->stored_t!=0 && point->ny==-1 && raw.receipts[0].tangent_impulse==(double)point->stored_t);
    dango_constraint_state detached=expected_fixed;
    CHECK(dango_rh_qualify(&capture,raw.receipts,&detached));
    dango_material_ground_receipt wrong=raw.receipts[0]; wrong.tangent_impulse=-wrong.tangent_impulse;
    NEGATIVE(dango_rh_qualify(&capture,&wrong,&detached));
    float ny=point->ny; point->ny=1; NEGATIVE(dango_rh_qualify(&capture,raw.receipts,&detached)); point->ny=ny;
    fprintf(fixture_output,"{\"kind\":\"reverse-source-fixed-fixture\",\"nodeAgroundB\":true,\"originalCollectorT\":%.17g,\"sourceStoredT\":%.17g,\"actualSourceDeltaN\":%.17g,\"qualifies\":true,\"failures\":%d}\n",raw.receipts[0].tangent_impulse,(double)point->stored_t,(double)point->delta_n,failures);
    dango_rh_end(&capture); b2DestroyWorld(id);
}
static void rh_fixture_impl(dango_self_soft_body* body,void* opaque,const void* input,double dt);
static void rh_fixture(dango_self_soft_body* body,void* opaque,const void* input,double dt)
{
    // Keep the reentrancy guard outside the large counterfactual stack frame.
    if(!fixture_running) rh_fixture_impl(body,opaque,input,dt);
}
static void rh_fixture_impl(dango_self_soft_body* body,void* opaque,const void* input,double dt)
{
    dango_material_contact_diagnostic* d=opaque; const dango_constraint_state* s=input;
    dango_rh_capture* c=dango_rh_get(body->world_id);
    if(inject_failure) { c->invalid=true; d->receipt_count=1; d->receipts[0]=(dango_material_ground_receipt){15,-1,0,1,0}; inject_failure=0; return; }
    bool out=false; for(int i=0;i<d->receipt_count;++i) out|=dango_rh_outcone(d->receipts+i);
    if(!out && (ordinary_cases>=20 || d->receipt_count==0)) return;
    dango_rh_view v; bool filtered=dango_rh_filter(c,s,d->receipts,d->receipt_count,&v); CHECK(filtered); if(!filtered) return;
    dango_material_contact_block reference,actual; dango_material_contact_block_report rr,ar;
    dango_constraint_state ro,ao;
    if(!out) {
        ++ordinary_cases;
        CHECK(dango_material_contact_block_build(s,d->receipts,d->receipt_count,dt,&reference,&rr));
        CHECK(dango_material_contact_block_solve(&reference,&ro,&rr));
        dango_material_contact_diagnostic candidate_diag=*d; candidate_diag.damped=*s;
        fixture_running=true; CHECK(dango_rha_build(body,&candidate_diag,s,dt)); CHECK(dango_rha_solve(body,&candidate_diag)); fixture_running=false;
        actual=candidate_diag.block; ar=candidate_diag.block_report; ao=candidate_diag.uncapped;
        CHECK(memcmp(&ro,&ao,sizeof ro)==0);
        for(int i=0;i<17;++i) { const dango_material_contact_vec2 *r=reference.contact.velocities+i,*a=actual.contact.velocities+i; CHECK(FIELD_BITS(r,a,x) && FIELD_BITS(r,a,y)); }
        for(int i=0;i<160;++i) {
            const dango_material_contact_row *r=reference.contact.rows+i,*a=actual.contact.rows+i;
            CHECK(r->active==a->active && r->id==a->id && r->kind==a->kind && r->node==a->node && r->bound==a->bound && r->count==a->count && FIELD_BITS(r,a,gap) && FIELD_BITS(r,a,effective_inverse_mass) && FIELD_BITS(r,a,multiplier) && memcmp(r->ids,a->ids,sizeof r->ids)==0);
            for(int n=0;n<4;++n) CHECK(FIELD_BITS(r,a,gradients[n].x) && FIELD_BITS(r,a,gradients[n].y));
        }
        for(int i=0;i<16;++i) { const dango_material_contact_support *r=reference.contact.support+i,*a=actual.contact.support+i; CHECK(FIELD_BITS(r,a,receipt_present) && FIELD_BITS(r,a,touching) && FIELD_BITS(r,a,base_normal) && FIELD_BITS(r,a,base_tangent) && FIELD_BITS(r,a,friction) && FIELD_BITS(r,a,total_normal_telemetry) && FIELD_BITS(r,a,receipt_cone_slack)); }
        CHECK(rr.active_count==ar.active_count && rr.matrix_dimension==ar.matrix_dimension && rr.mode_history_count==ar.mode_history_count && rr.linear_solves==ar.linear_solves && rr.full_checks==ar.full_checks && rr.float_checks==ar.float_checks);
        CHECK(memcmp(rr.active_row_ids,ar.active_row_ids,sizeof rr.active_row_ids)==0 && memcmp(rr.modes,ar.modes,sizeof rr.modes)==0 && memcmp(rr.mode_history,ar.mode_history,sizeof rr.mode_history)==0);
        CHECK(report_bits_equal(&rr.final_rows,&ar.final_rows));
        float maximum=0; for(int i=0;i<17;++i) maximum=fmaxf(maximum,b2Length(ro.velocities[i])); float limit=fixture_maximum_speed,scale=maximum>limit?(limit*.99999f)/maximum:1.0f;
        dango_constraint_state rc=ro,ac=ao; for(int i=0;i<17;++i) { rc.velocities[i]=(b2Vec2){(float)(reference.contact.velocities[i].x*(double)scale),(float)(reference.contact.velocities[i].y*(double)scale)}; ac.velocities[i]=(b2Vec2){(float)(actual.contact.velocities[i].x*(double)scale),(float)(actual.contact.velocities[i].y*(double)scale)}; }
        dango_material_contact_cap_report rcap,acap; CHECK(dango_material_contact_block_validate_after_double_scale(&reference,&ro,&rc,scale,limit,&rcap)); CHECK(dango_rha_gate(body,&candidate_diag,&ac,6)); CHECK(dango_material_contact_block_validate_after_double_scale(&actual,&ao,&ac,scale,limit,&acap)); CHECK(dango_rha_gate(body,&candidate_diag,&ac,7));
        CHECK(memcmp(&rc,&ac,sizeof rc)==0 && cap_bits_equal(&rcap,&acap));
        CHECK(v.history_count==0 && v.current_count==d->receipt_count); return;
    }
    ++history_cases;
    CHECK(d->substeps==93836 && history_cases==1);
    CHECK(memcmp(s,&expected_fixed,sizeof *s)==0);
    CHECK(v.history_count==1 && v.retired[15] && v.current_count==3);
    CHECK(!dango_material_contact_block_build(s,d->receipts,d->receipt_count,dt,&reference,&rr));
    CHECK(rr.status==DANGO_MATERIAL_CONTACT_BLOCK_BUILD && rr.final_rows.status==DANGO_MATERIAL_CONTACT_RECEIPT && rr.linear_solves==0);
    dango_material_ground_receipt kept[16]; int kept_count=0;
    for(int i=0;i<d->receipt_count;++i) if(d->receipts[i].node!=15) kept[kept_count++]=d->receipts[i];
    CHECK(dango_material_contact_block_build(s,kept,kept_count,dt,&reference,&rr));
    CHECK(dango_material_contact_block_build(s,v.current,v.current_count,dt,&actual,&ar));
    CHECK(dango_material_contact_block_solve(&reference,&ro,&rr)); CHECK(dango_material_contact_block_solve(&actual,&ao,&ar));
    CHECK(memcmp(&ro,&ao,sizeof ro)==0 && memcmp(reference.contact.velocities,actual.contact.velocities,sizeof reference.contact.velocities)==0);
    for(int i=0;i<160;++i) CHECK(reference.contact.rows[i].multiplier==actual.contact.rows[i].multiplier);
    CHECK(dango_rh_revalidate(&v,d->receipts,d->receipt_count,&ao,&actual.contact));
    dango_material_contact_diagnostic capture_rejection={0}; dango_rha_capture_rejected(&capture_rejection);
    CHECK(capture_rejection.history_phase==1 && capture_rejection.history_status==3 && capture_rejection.error==-96 && capture_rejection.block_report.status==DANGO_MATERIAL_CONTACT_BLOCK_BUILD && capture_rejection.report.status==DANGO_MATERIAL_CONTACT_RECEIPT);
    dango_rh_point saved=c->points[15]; dango_rh_capture source_saved=*c;
    dango_material_ground_receipt raw[16]; memcpy(raw,d->receipts,sizeof raw);
    int index=-1; for(int i=0;i<d->receipt_count;++i) if(raw[i].node==15) index=i; CHECK(index>=0); if(index<0) return;
    // Whole valid live certificate in the wrong node slot. Its own shape/body,
    // feature and N/T/friction remain correct; only receipt-node binding differs.
    dango_rh_point prior_slot=c->points[12]; c->points[12]=saved;
    dango_material_ground_receipt wrong_slot=raw[index]; wrong_slot.node=12;
    CHECK(dango_rh_live(c,c->points+12));
    CHECK((double)s->positions[12].y-(double)(s->ground+s->radius)>(double)DANGO_CONSTRAINT_TOLERANCE*(double)s->radial_length);
    NEGATIVE(dango_rh_qualify(c,&wrong_slot,s)); c->points[12]=prior_slot;
    NEGATIVE(dango_rh_filter(NULL,s,raw,d->receipt_count,&v));
#define POINT_BAD(field,value) do { c->points[15].field=(value); NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); c->points[15]=saved; } while(0)
    POINT_BAD(sequence,saved.sequence+1); POINT_BAD(step,saved.step+1); POINT_BAD(shape_gen_a,(uint16_t)(saved.shape_gen_a+1)); POINT_BAD(shape_gen_b,(uint16_t)(saved.shape_gen_b+1)); POINT_BAD(body_gen_a,(uint16_t)(saved.body_gen_a+1)); POINT_BAD(body_gen_b,(uint16_t)(saved.body_gen_b+1));
    POINT_BAD(shape_a,saved.shape_b); POINT_BAD(contact,saved.contact+1); POINT_BAD(point,1); POINT_BAD(point_id,saved.point_id+1); POINT_BAD(nx,.1f); POINT_BAD(ny,-saved.ny); POINT_BAD(friction,saved.friction*2); POINT_BAD(stage,40); POINT_BAD(delta_count,0); POINT_BAD(delta_count,2); POINT_BAD(delta_t,.001f); POINT_BAD(delta_n,-saved.delta_n); POINT_BAD(delta_n,saved.delta_n*.5f); POINT_BAD(stored_t,saved.stored_t*.5f); POINT_BAD(friction_n,0); POINT_BAD(invalid,true);
#undef POINT_BAD
    c->world.generation++; NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); *c=source_saved;
    c->world.index1=0; NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); *c=source_saved;
    c->sequence++; NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); *c=source_saved;
    c->step++; NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); *c=source_saved;
    c->invalid=true; NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); *c=source_saved;
    c->ground.generation++; NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); *c=source_saved;
    c->ground=c->nodes[14]; NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); *c=source_saved;
    c->ground.world0++; NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); *c=source_saved;
    b2WorldDef foreign_def=b2DefaultWorldDef(); b2WorldId foreign=b2CreateWorld(&foreign_def);
    b2BodyDef foreign_body_def=b2DefaultBodyDef(); b2BodyId foreign_body=b2CreateBody(foreign,&foreign_body_def);
    b2ShapeDef foreign_shape_def=b2DefaultShapeDef(); b2Circle foreign_circle={{0,0},1}; b2ShapeId foreign_ground=b2CreateCircleShape(foreign_body,&foreign_shape_def,&foreign_circle);
    CHECK(b2Shape_IsValid(foreign_ground));
    NEGATIVE(dango_rh_begin(c,c->world,foreign_ground,c->nodes,c->bodies));
    c->ground=foreign_ground; NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); *c=source_saved;
    b2DestroyWorld(foreign);
    c->nodes[15].generation++; NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); *c=source_saved;
    c->bodies[15].generation++; NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); *c=source_saved;
    b2World* actual_world=b2GetWorldFromId(c->world); b2ContactSim* actual_sim=b2GetContactSim(actual_world,actual_world->contacts.data+saved.contact);
    int points=actual_sim->manifold.pointCount; actual_sim->manifold.pointCount=2;
    NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); actual_sim->manifold.pointCount=points;
    dango_constraint_state touching=*s; touching.positions[15].y=touching.ground+touching.radius;
    NEGATIVE(dango_rh_filter(c,&touching,raw,d->receipt_count,&v));
    dango_constraint_state boundary=*s; boundary.ground=0; boundary.radius=0; boundary.radial_length=1; boundary.positions[15].y=DANGO_CONSTRAINT_TOLERANCE;
    NEGATIVE(dango_rh_qualify(c,raw+index,&boundary));
    boundary.positions[15].y=nextafterf(DANGO_CONSTRAINT_TOLERANCE,-INFINITY); NEGATIVE(dango_rh_qualify(c,raw+index,&boundary));
    boundary.positions[15].y=nextafterf(DANGO_CONSTRAINT_TOLERANCE,INFINITY); CHECK(dango_rh_qualify(c,raw+index,&boundary));
    raw[index].normal_impulse=-1; NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); memcpy(raw,d->receipts,sizeof raw);
    raw[index].tangent_impulse=NAN; NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); memcpy(raw,d->receipts,sizeof raw);
    raw[index].node=raw[0].node; NEGATIVE(dango_rh_filter(c,s,raw,d->receipt_count,&v)); memcpy(raw,d->receipts,sizeof raw);
    NEGATIVE(dango_rh_filter(c,s,raw,17,&v));
    CHECK(dango_rh_filter(c,s,raw,d->receipt_count,&v));
    dango_rh_view original_view=v;
    raw[index].total_normal_impulse+=1; NEGATIVE(dango_rh_revalidate(&v,raw,d->receipt_count,&ao,&actual.contact)); memcpy(raw,d->receipts,sizeof raw);
    raw[index].normal_impulse+=1; NEGATIVE(dango_rh_revalidate(&v,raw,d->receipt_count,&ao,&actual.contact)); memcpy(raw,d->receipts,sizeof raw);
    v.history_count=0; NEGATIVE(dango_rh_revalidate(&v,raw,d->receipt_count,&ao,&actual.contact)); v=original_view;
    v.retired[15]=false; NEGATIVE(dango_rh_revalidate(&v,raw,d->receipt_count,&ao,&actual.contact)); v=original_view;
    NEGATIVE(dango_rh_revalidate(&v,raw,d->receipt_count,&touching,&actual.contact));
    dango_material_contact_solver solver=actual.contact;
#define SOLVER_BAD(code) do { code; NEGATIVE(dango_rh_revalidate(&v,raw,d->receipt_count,&ao,&solver)); solver=actual.contact; } while(0)
    SOLVER_BAD(solver.support[15].base_normal=1); SOLVER_BAD(solver.support[15].base_tangent=-1); SOLVER_BAD(solver.support[15].friction=1); SOLVER_BAD(solver.support[15].receipt_cone_slack=.001); SOLVER_BAD(solver.support[15].total_normal_telemetry=1); SOLVER_BAD(solver.support[15].receipt_present=true); SOLVER_BAD(solver.rows[143].active=true); SOLVER_BAD(solver.rows[159].active=true); SOLVER_BAD(solver.rows[143].multiplier=1); SOLVER_BAD(solver.rows[159].multiplier=1);
    SOLVER_BAD(solver.input_velocities[15].y+=1); SOLVER_BAD(solver.input_velocities[15].x+=(double)raw[index].tangent_impulse*s->inverse_mass[15]); SOLVER_BAD(solver.receipts[0].total_normal_impulse+=1);
#undef SOLVER_BAD
    dango_constraint_state tampered=ao; tampered.velocities[15].x+=1; dango_material_contact_report rejected;
    NEGATIVE(dango_material_contact_validate(&actual.contact,&tampered,&rejected));
    // A later live step cannot reuse this certificate, even if geometry matches.
    c->sequence++; NEGATIVE(dango_rh_revalidate(&v,raw,d->receipt_count,&ao,&actual.contact)); *c=source_saved;
    c->step++; c->points[15].step=c->step; NEGATIVE(dango_rh_revalidate(&v,raw,d->receipt_count,&ao,&actual.contact)); *c=source_saved;
    c->world.generation++; c->sequence=v.sequence; NEGATIVE(dango_rh_revalidate(&v,raw,d->receipt_count,&ao,&actual.contact)); *c=source_saved;
    dango_material_contact_diagnostic publication={0}; publication.history=v; publication.block=actual;
    c->points[15].delta_t=.01f;
    NEGATIVE(dango_rha_gate(body,&publication,&ao,4)); CHECK(publication.history_phase==4 && publication.history_status==4 && publication.block.report.status==DANGO_MATERIAL_CONTACT_BLOCK_OK);
    publication.history=v; NEGATIVE(dango_rha_gate(body,&publication,&ao,6)); CHECK(publication.history_phase==6 && publication.history_status==4);
    publication.history=v; NEGATIVE(dango_rha_gate(body,&publication,&ao,7)); CHECK(publication.history_phase==7 && publication.history_status==4); *c=source_saved;
    fprintf(fixture_output,"{\"substep\":%llu,\"oldRejected\":true,\"inputMatchesFixedBits\":true,\"referenceOutputBitsEqual\":true,\"ordinaryCases\":%d,\"negativeCases\":%d,\"assertions\":%d,\"failures\":%d,\"gap\":%.17g,\"tolerance\":%.17g}\n",d->substeps,ordinary_cases,negative_cases,assertions,failures,(double)s->positions[15].y-(double)(s->ground+s->radius),(double)DANGO_CONSTRAINT_TOLERANCE*(double)s->radial_length);
    fflush(fixture_output);
}

int main(int argc,char** argv)
{
    if(argc!=6) return 2;
    dango_self_physics_config config; FILE* f=fopen(argv[1],"rb"); if(!f) return 2;
    if(fread(&config,sizeof config,1,f)!=1 || fgetc(f)!=EOF) return 2; fclose(f); fixture_maximum_speed=config.maximum_speed;
    f=fopen(argv[2],"rb"); if(!f) return 2; if(fread(&expected_fixed,sizeof expected_fixed,1,f)!=1 || fgetc(f)!=EOF) return 2; fclose(f);
    FILE* baseline=fopen(argv[3],"rb"); if(!baseline) return 2;
    FILE* rows=fopen(argv[4],"wbx"); fixture_output=fopen(argv[5],"wx"); if(!rows || !fixture_output) return 2;
    reverse_source_fixture();
    void* handle=NULL; int id=0; CHECK(dango_physics_world_create(&config,1,&handle)==0); CHECK(dango_physics_world_add(handle,0,(float)(5.0/3.0),&id)==0);
    float controls[6]={0},row[42]={0}; int state_id=0,count=0,contacts=0,latched=0,error=0,completed=0,attempted=0; unsigned mismatches=0;
    for(int i=0;i<6000;++i) {
        attempted=i+1; controls[3]=i>=600 && i%600<480?1.0f:0.0f; controls[4]=(float)(-3.25+3.75*sin(i*.043)); controls[5]=(float)(10.0/3.0-25.0/6.0*cos(i*.071));
        error=dango_physics_world_step(handle,.0166666f,&id,controls,1,&state_id,row,42,&count,&contacts,&latched);
        if(fwrite(row,sizeof row,1,rows)!=1) {error=-600;break;}
        if(i<5864) { float old[42]; CHECK(fread(old,sizeof old,1,baseline)==1); for(int n=0;n<42;++n) mismatches+=memcmp(old+n,row+n,sizeof(float))!=0; }
        if(error || latched || count!=1 || state_id!=id) break; ++completed;
    }
    CHECK(mismatches==0); CHECK(ordinary_cases==20 && history_cases==1 && negative_cases>=50);
    fprintf(fixture_output,"{\"kind\":\"trajectory\",\"completed\":%d,\"attempted\":%d,\"error\":%d,\"latch\":%d,\"preFailureFieldsCompared\":246288,\"bitMismatches\":%u,\"ordinaryCases\":%d,\"historyCases\":%d,\"negativeCases\":%d,\"assertions\":%d,\"failures\":%d}\n",completed,attempted,error,latched,mismatches,ordinary_cases,history_cases,negative_cases,assertions,failures); fflush(fixture_output);
    printf("completed=%d attempted=%d error=%d latch=%d history=%d negatives=%d mismatches=%u failures=%d\n",completed,attempted,error,latched,history_cases,negative_cases,mismatches,failures);
    // Explicit failure injection checks unchanged frame publication/latch/reset.
    if(completed==6000 && !failures) {
        collision_review* world=handle; float last[42]; memcpy(last,world->snapshot,sizeof last);
        inject_failure=1; memset(controls,0,sizeof controls);
        CHECK(dango_physics_world_step(handle,.0166666f,&id,controls,1,&state_id,row,42,&count,&contacts,&latched)==0);
        CHECK(latched==-96 && world->error==-96 && memcmp(last,row,sizeof last)==0);
        dango_material_contact_diagnostic failed=world->candidate_diagnostic;
        CHECK(failed.phase==4 && failed.error==-96 && failed.history_phase==2 && failed.history_status==2 && failed.block_report.status==DANGO_MATERIAL_CONTACT_BLOCK_BUILD && failed.block_report.final_rows.status==DANGO_MATERIAL_CONTACT_RECEIPT && failed.block_report.linear_solves==0);
        b2Vec2 positions[17],velocities[17];
        for(int n=0;n<17;++n) { b2BodyId bid=dango_mca_id(world->bodies[0],n); positions[n]=b2Body_GetPosition(bid); velocities[n]=b2Body_GetLinearVelocity(bid); CHECK(memcmp(positions+n,failed.input.positions+n,sizeof(b2Vec2))==0 && memcmp(velocities+n,failed.input.velocities+n,sizeof(b2Vec2))==0); }
        uint64_t seq=world->receipt_history.sequence;
        for(int i=0;i<3;++i) {
            controls[0]=i==0?1.0f:0.0f; controls[3]=i==1?1.0f:0.0f; controls[4]=2; controls[5]=3;
            CHECK(dango_physics_world_step(handle,.0166666f,&id,controls,1,&state_id,row,42,&count,&contacts,&latched)==0 && latched==-96 && memcmp(last,row,sizeof last)==0);
            CHECK(memcmp(&world->candidate_diagnostic,&failed,sizeof failed)==0 && world->receipt_history.sequence==seq);
            for(int n=0;n<17;++n) { b2BodyId bid=dango_mca_id(world->bodies[0],n); b2Vec2 p=b2Body_GetPosition(bid),v=b2Body_GetLinearVelocity(bid); CHECK(memcmp(&p,positions+n,sizeof p)==0 && memcmp(&v,velocities+n,sizeof v)==0); }
        }
        dango_rh_capture old_source=world->receipt_history; b2ShapeId old_shape=world->bodies[0]->node_shape_ids[15];
        CHECK(dango_physics_world_reset(handle)==0 && world->error==0);
        CHECK(!b2Shape_IsValid(old_shape)); CHECK(!dango_rh_live(&old_source,old_source.points+15));
        CHECK(dango_rh_get(world->bodies[0]->world_id)==NULL);
        memset(controls,0,sizeof controls); CHECK(dango_physics_world_step(handle,.0166666f,&id,controls,1,&state_id,row,42,&count,&contacts,&latched)==0 && latched==0);
        fprintf(fixture_output,"{\"kind\":\"latch-reset\",\"passed\":%s,\"assertions\":%d,\"failures\":%d}\n",failures?"false":"true",assertions,failures);
        memcpy(last,world->snapshot,sizeof last);
        b2World* actual_world=b2GetWorldFromId(world->bodies[0]->world_id); int workers=actual_world->workerCount; actual_world->workerCount=2;
        CHECK(dango_physics_world_step(handle,.0166666f,&id,controls,1,&state_id,row,42,&count,&contacts,&latched)==0 && latched==-96 && memcmp(row,last,sizeof last)==0); actual_world->workerCount=workers;
        int hp=0,hs=0; uint64_t he=0,hseq=0;
        CHECK(dango_receipt_history_get_status(handle,&hp,&hs,&he,&hseq)==0 && hp==1 && hs==3);
        CHECK(world->candidate_diagnostic.phase==0 && world->candidate_diagnostic.error==-96 && world->candidate_diagnostic.block_report.status==DANGO_MATERIAL_CONTACT_BLOCK_BUILD && world->candidate_diagnostic.report.status==DANGO_MATERIAL_CONTACT_RECEIPT && world->candidate_diagnostic.block_report.linear_solves==0);
        fprintf(fixture_output,"{\"kind\":\"capture-begin-rejection\",\"historyPhase\":%d,\"historyStatus\":%d,\"nativePhase\":%d,\"passed\":%s,\"assertions\":%d,\"failures\":%d}\n",hp,hs,world->candidate_diagnostic.phase,failures?"false":"true",assertions,failures);
        CHECK(dango_physics_world_reset(handle)==0); latched=0;
    }
    fclose(baseline); fclose(rows); fclose(fixture_output); dango_physics_world_destroy(handle);
    return completed==6000 && !error && !latched && !failures?0:1;
}

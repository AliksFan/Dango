#define _CRT_SECURE_NO_WARNINGS
#include <stdio.h>
#include <stdlib.h>
#include "dango_self_physics_internal.h"
static void fixture(dango_self_soft_body*,void*,const void*,double);
#define DANGO_RH_TEST_BEFORE_BUILD(body,d,state,dt) fixture(body,d,state,dt)
#include "candidate.c"
#include "world.h"
#include "contact.h"
#include "body.h"
#include "solver_set.h"
#include "contact_solver.h"
static unsigned assertions,failures,negatives;
static bool running,observed;
static dango_constraint_state expected,contact,limited;
static dango_material_contact_diagnostic prepared;
static unsigned char raw[262144];
static dango_material_contact_block original,solved;
static dango_material_contact_block_report original_report;
static dango_material_contact_cap_report cap;
static void check(bool ok,int line,const char* text){++assertions;if(!ok){++failures;fprintf(stderr,"FAIL %d %s\n",line,text);}}
#define CHECK(c) check((c),__LINE__,#c)
#define NEG(c) do{++negatives;CHECK(!(c));}while(0)
static bool read_exact(const char* path,void* data,size_t bytes)
{FILE* f=fopen(path,"rb");if(!f)return false;bool ok=fread(data,1,bytes,f)==bytes&&fgetc(f)==EOF;fclose(f);return ok;}
static void bind(dango_self_soft_body* body,dango_rh_capture* source,const dango_constraint_state* s,const dango_material_ground_receipt* receipts,int count)
{
 dango_tr_end();CHECK(dango_tr_filter(source,s,receipts,count,&prepared.history,&prepared.touching));
 CHECK(dango_tr_begin(body,dango_tra_source,dango_tra_receipts,&prepared.history,&prepared.touching));
}
static void guard_tests(dango_self_soft_body* body,dango_rh_capture* c,const dango_constraint_state* s,const dango_material_ground_receipt* r,int count,double dt)
{
 int n=r[0].node;dango_rh_point saved=c->points[n];dango_rh_capture source_saved=*c;
 dango_rh_view v;dango_tr_view t;
 CHECK(count==1);CHECK(dango_tr_qualify(c,r,s));NEG(dango_rh_filter(c,s,r,count,&v));NEG(dango_tr_filter(NULL,s,r,count,&v,&t));
#define BAD_POINT(field,value) do{c->points[n].field=(value);NEG(dango_tr_filter(c,s,r,count,&v,&t));c->points[n]=saved;}while(0)
 BAD_POINT(node,(n+1)%16);BAD_POINT(sequence,saved.sequence+1);BAD_POINT(step,saved.step+1);BAD_POINT(shape_a,saved.shape_b);BAD_POINT(shape_b,saved.shape_a);BAD_POINT(body_a,saved.body_b);BAD_POINT(body_b,saved.body_a);
 BAD_POINT(shape_gen_a,(uint16_t)(saved.shape_gen_a+1));BAD_POINT(shape_gen_b,(uint16_t)(saved.shape_gen_b+1));BAD_POINT(body_gen_a,(uint16_t)(saved.body_gen_a+1));BAD_POINT(body_gen_b,(uint16_t)(saved.body_gen_b+1));
 BAD_POINT(contact,saved.contact+1);BAD_POINT(point,1);BAD_POINT(point_id,saved.point_id+1);BAD_POINT(nx,.1f);BAD_POINT(ny,-saved.ny);BAD_POINT(friction,saved.friction*2);BAD_POINT(stage,40);BAD_POINT(delta_count,0);BAD_POINT(delta_count,2);
 BAD_POINT(delta_n,0);BAD_POINT(delta_n,-saved.delta_n);BAD_POINT(delta_n,saved.delta_n*.5f);BAD_POINT(delta_t,.01f);BAD_POINT(friction_n,0);BAD_POINT(friction_t,saved.friction_t*2);BAD_POINT(rest_n,saved.rest_n*.5f);BAD_POINT(rest_t,saved.rest_t*.5f);BAD_POINT(stored_n,saved.stored_n*.5f);BAD_POINT(stored_t,saved.stored_t*.5f);BAD_POINT(invalid,true);
#undef BAD_POINT
#define BAD_SOURCE(code) do{code;NEG(dango_tr_filter(c,s,r,count,&v,&t));*c=source_saved;}while(0)
 BAD_SOURCE(c->world.generation++);BAD_SOURCE(c->world.index1=0);BAD_SOURCE(c->sequence++);BAD_SOURCE(c->step++);BAD_SOURCE(c->ground.generation++);BAD_SOURCE(c->ground.world0++);BAD_SOURCE(c->nodes[n].generation++);BAD_SOURCE(c->bodies[n].generation++);BAD_SOURCE(c->bound=false);BAD_SOURCE(c->invalid=true);
#undef BAD_SOURCE
 b2World* world=b2GetWorldFromId(c->world);b2Contact* contact_record=world->contacts.data+saved.contact;b2ContactSim* sim=b2GetContactSim(world,contact_record);int points=sim->manifold.pointCount;
 sim->manifold.pointCount=2;NEG(dango_tr_filter(c,s,r,count,&v,&t));sim->manifold.pointCount=points;
 dango_constraint_state changed=*s;changed.positions[n].y=nextafterf(changed.positions[n].y,INFINITY);NEG(dango_tr_qualify(c,r,&changed));NEG(dango_tr_filter(c,&changed,r,count,&v,&t));changed=*s;changed.positions[n].y=nextafterf(changed.positions[n].y,-INFINITY);NEG(dango_tr_filter(c,&changed,r,count,&v,&t));
 dango_material_ground_receipt wrong=r[0],duplicates[2]={r[0],r[0]};NEG(dango_tr_filter(c,s,duplicates,2,&v,&t));wrong.normal_impulse=-1;NEG(dango_tr_filter(c,s,&wrong,1,&v,&t));wrong=r[0];wrong.tangent_impulse=-wrong.tangent_impulse;NEG(dango_tr_filter(c,s,&wrong,1,&v,&t));
 dango_tr_end();NEG(dango_material_contact_block_build(s,r,count,dt,&original,&original_report));CHECK(original_report.status==2&&original_report.final_rows.status==4);
 bind(body,c,s,r,count);CHECK(dango_material_contact_block_build(s,r,count,dt,&prepared.block,&prepared.block_report));
 CHECK(prepared.block.contact.support[n].receipt_present&&prepared.block.contact.support[n].touching&&prepared.block.contact.support[n].receipt_cone_slack==0&&prepared.block.contact.rows[128+n].active&&prepared.block.contact.rows[144+n].active);
#define BAD_VIEW(code) do{dango_rh_view h=prepared.history;dango_tr_view tr=prepared.touching;code;NEG(dango_tr_context_check(s,NULL));prepared.history=h;prepared.touching=tr;}while(0)
 BAD_VIEW(prepared.touching.pending_count=0);BAD_VIEW(prepared.touching.pending_count=2);BAD_VIEW(prepared.touching.pending[n]=false);BAD_VIEW(prepared.history.retired[n]=true);BAD_VIEW(prepared.history.raw[0].normal_impulse+=.01);BAD_VIEW(prepared.history.current[0].tangent_impulse+=.01);BAD_VIEW(prepared.history.certificates[n].sequence++);
#undef BAD_VIEW
#define BAD_SOLVER(code) do{dango_material_contact_solver copy=prepared.block.contact;code;NEG(dango_tr_context_check(s,&prepared.block.contact));prepared.block.contact=copy;}while(0)
 BAD_SOLVER(prepared.block.contact.support[n].receipt_cone_slack=.01);BAD_SOLVER(prepared.block.contact.support[n].base_normal+=.01);BAD_SOLVER(prepared.block.contact.support[n].base_tangent+=.01);BAD_SOLVER(prepared.block.contact.support[n].total_normal_telemetry+=.01);BAD_SOLVER(prepared.block.contact.rows[128+n].active=false);BAD_SOLVER(prepared.block.contact.rows[144+n].active=false);BAD_SOLVER(prepared.block.contact.support[n].touching=false);BAD_SOLVER(prepared.block.contact.receipts[0].tangent_impulse+=.01);BAD_SOLVER(prepared.block.contact.input_velocities[0].x+=.01);BAD_SOLVER(prepared.block.contact.geometry.positions[n].x+=.01f);
#undef BAD_SOLVER
 c->points[n].sequence++;NEG(dango_tr_context_check(s,NULL));c->points[n]=saved;
 // The allowance remains tied to true raw N/T even when telemetry grows.
 dango_material_ground_receipt telemetry=r[0];telemetry.total_normal_impulse*=2;
 NEG(dango_tr_import_receipt(s,&telemetry,1,0));
 CHECK(dango_tr_context_check(s,&prepared.block.contact));dango_tr_end();NEG(dango_tr_import_receipt(s,r,count,0));
}
static void fixture(dango_self_soft_body* body,void* opaque,const void* input,double dt)
{
 if(running)return;dango_material_contact_diagnostic* d=opaque;const dango_constraint_state* s=input;
 if(d->substeps!=71125)return;observed=true;running=true;
 CHECK(memcmp(s,&expected,sizeof expected)==0);CHECK(d->receipt_count==1);
 dango_rh_capture* c=dango_rh_get(body->world_id);guard_tests(body,c,s,d->receipts,d->receipt_count,dt);
 prepared=*d;prepared.damped=*s;CHECK(dango_rha_build(body,&prepared,s,dt));CHECK(dango_rha_solve(body,&prepared));
 CHECK(prepared.touching.pending_count==1&&prepared.history.history_count==0&&prepared.history.current_count==1);
 contact=prepared.uncapped;solved=prepared.block;
 CHECK(dango_rha_gate(body,&prepared,&contact,6));limited=contact;
 CHECK(dango_material_contact_block_validate_after_double_scale(&prepared.block,&contact,&limited,1,12,&cap));CHECK(dango_rha_gate(body,&prepared,&limited,7));
 dango_material_contact_report report;dango_constraint_state bad=contact;bad.velocities[0].x+=.001f;NEG(dango_material_contact_validate(&prepared.block.contact,&bad,&report));
 double lambda=prepared.block.contact.rows[131].multiplier;prepared.block.contact.rows[131].multiplier+=.01;NEG(dango_material_contact_validate(&prepared.block.contact,&contact,&report));prepared.block.contact.rows[131].multiplier=lambda;
 // A canonical active cap is constructed only as a rejection test. Scaling
 // current corrective impulses shrinks normal support and recreates raw cone
 // excess; neither that excess nor the subsequent cap is silently accepted.
 dango_material_contact_block scaled=prepared.block;float maximum=0;for(int i=0;i<17;++i)maximum=fmaxf(maximum,b2Length(contact.velocities[i]));float limit=maximum*.01f,scale=(limit*.99999f)/maximum;
 scaled.contact.rows[131].multiplier*=scale;scaled.contact.rows[147].multiplier*=scale;
 dango_material_contact_vec2 velocities[17];bad=contact;for(int i=0;i<17;++i){velocities[i]=dango_mc_mul(scale,prepared.block.contact.velocities[i]);bad.velocities[i]=(b2Vec2){(float)velocities[i].x,(float)velocities[i].y};}
 dango_material_contact_solver reference;CHECK(dango_material_contact_build(s,d->receipts,d->receipt_count,dt,&reference,&report));
 NEG(dango_mc_inspect_rows(&scaled.contact,&reference,velocities,&report));CHECK(report.status==DANGO_MATERIAL_CONTACT_FRICTION&&report.maximum_friction_cone_excess>0);
 dango_material_contact_cap_report rejected;NEG(dango_material_contact_block_validate_after_double_scale(&scaled,&contact,&bad,scale,limit,&rejected));
 for(int phase=3;phase<=7;++phase){
  dango_rh_point point=c->points[d->receipts[0].node];c->points[d->receipts[0].node].sequence++;NEG(dango_rha_gate(body,&prepared,&contact,phase));c->points[d->receipts[0].node]=point;
  CHECK(dango_rha_gate(body,&prepared,&contact,phase));
 }
 dango_tr_end();running=false;
}
static void multi_source_fixture(void)
{
 // Two genuine single-point contacts run through unchanged original overflow
 // kernels. Both separately qualify; only the whole-import cardinality fails.
 b2WorldDef wd=b2DefaultWorldDef();wd.gravity=b2Vec2_zero;wd.restitutionThreshold=.1f;
 b2WorldId id=b2CreateWorld(&wd);dango_self_soft_body toy={0};toy.world_id=id;
 b2BodyDef ground_def=b2DefaultBodyDef();ground_def.position=(b2Vec2){0,-10.72f};b2BodyId ground=b2CreateBody(id,&ground_def);
 b2ShapeDef sd=b2DefaultShapeDef();sd.material.friction=1;sd.material.restitution=.5f;b2Polygon floor=b2MakeBox(100,10);toy.ground_shape_id=b2CreatePolygonShape(ground,&sd,&floor);
 sd.material.friction=.72f;b2Circle circle={{0,0},.036f};
 for(int n=0;n<16;++n){b2BodyDef bd=b2DefaultBodyDef();bd.type=b2_dynamicBody;bd.fixedRotation=true;bd.position=(b2Vec2){n==14?-.2f:n==15?.2f:1000+(float)n*10,-.685f};toy.node_body_ids[n]=b2CreateBody(id,&bd);toy.node_shape_ids[n]=b2CreateCircleShape(toy.node_body_ids[n],&sd,&circle);}
 b2World_Step(id,.001f,1);dango_rh_capture capture={0};CHECK(dango_rh_begin(&capture,id,toy.ground_shape_id,toy.node_shape_ids,toy.node_body_ids));b2World_Step(id,.001f,1);memset(capture.points,0,sizeof capture.points);
 b2World* world=b2GetWorldFromId(id);b2SolverSet* awake=b2SolverSetArray_Get(&world->solverSets,b2_awakeSet);
 for(int n=14;n<=15;++n){
  b2ContactSim* sim=NULL;for(int i=0;i<world->contacts.count;++i){b2Contact* c=world->contacts.data+i;if(c->contactId>=0&&((c->shapeIdA==toy.ground_shape_id.index1-1&&c->shapeIdB==toy.node_shape_ids[n].index1-1)||(c->shapeIdB==toy.ground_shape_id.index1-1&&c->shapeIdA==toy.node_shape_ids[n].index1-1))){sim=b2GetContactSim(world,c);break;}}
  CHECK(sim!=NULL);if(!sim)continue;CHECK(sim->manifold.pointCount==1&&sim->manifold.normal.x==0);
  b2Body* node=b2GetBodyFullId(world,toy.node_body_ids[n]);awake->bodyStates.data[node->localIndex]=b2_identityBodyState;awake->bodyStates.data[node->localIndex].linearVelocity=(b2Vec2){5,0};bool ground_a=sim->shapeIdA==toy.ground_shape_id.index1-1;
  b2ContactConstraint constraint={0};constraint.indexA=ground_a?B2_NULL_INDEX:node->localIndex;constraint.indexB=ground_a?node->localIndex:B2_NULL_INDEX;constraint.normal=sim->manifold.normal;constraint.invMassA=ground_a?0:1/b2Body_GetMass(toy.node_body_ids[n]);constraint.invMassB=ground_a?1/b2Body_GetMass(toy.node_body_ids[n]):0;constraint.friction=sim->friction;constraint.restitution=.5f;constraint.pointCount=1;
  constraint.points[0].normalImpulse=.0192780085f;constraint.points[0].normalMass=1/(constraint.invMassA+constraint.invMassB);constraint.points[0].tangentMass=constraint.points[0].normalMass;constraint.points[0].relativeVelocity=-2;constraint.points[0].totalNormalImpulse=constraint.points[0].normalImpulse;
  b2ConstraintGraph graph={0};graph.colors[B2_OVERFLOW_INDEX].overflowConstraints=&constraint;graph.colors[B2_OVERFLOW_INDEX].contactSims.data=sim;graph.colors[B2_OVERFLOW_INDEX].contactSims.count=1;b2StepContext step={0};step.world=world;step.graph=&graph;step.inv_h=1000;
  b2SolveOverflowContacts(&step,false);awake->bodyStates.data[node->localIndex].linearVelocity.y=1.05f;b2ApplyOverflowRestitution(&step);b2StoreOverflowImpulses(&step);
 }
 dango_material_contact_diagnostic raw_receipts={0};CHECK(dango_mca_receipts(&toy,&raw_receipts));CHECK(raw_receipts.receipt_count==2);
 dango_constraint_state touching=expected;for(int n=14;n<=15;++n)touching.positions[n].y=touching.ground+touching.radius;
 for(int i=0;i<raw_receipts.receipt_count;++i)CHECK(dango_tr_qualify(&capture,raw_receipts.receipts+i,&touching));
 dango_rh_view view;dango_tr_view classification;NEG(dango_tr_filter(&capture,&touching,raw_receipts.receipts,raw_receipts.receipt_count,&view,&classification));
 dango_rh_end(&capture);b2DestroyWorld(id);
}
int main(int argc,char** argv)
{
 if(argc!=7)return 2;dango_self_physics_config config;float dt,anchor[2],controls[4446][6];
 CHECK(read_exact(argv[1],&config,sizeof config));CHECK(read_exact(argv[2],&dt,sizeof dt));CHECK(read_exact(argv[3],anchor,sizeof anchor));CHECK(read_exact(argv[4],controls,sizeof controls));CHECK(read_exact(argv[5],&expected,sizeof expected));
 if(failures)return 1;multi_source_fixture();if(failures)return 1;void* handle=NULL;int id=0;CHECK(dango_physics_world_create(&config,1,&handle)==0);CHECK(dango_physics_world_add(handle,anchor[0],anchor[1],&id)==0);
 for(int f=0;f<4446;++f){int sid=0,count=0,contacts=0,latch=0;float row[42];CHECK(dango_physics_world_step(handle,dt,&id,controls[f],1,&sid,row,42,&count,&contacts,&latch)==0);CHECK(latch==0&&sid==id&&count==1);if(failures)break;CHECK(!dango_tr_current.body);}
 CHECK(observed);collision_review* w=handle;CHECK(w->touching_events>=1);
 CHECK(w->touching_last.substeps==71125&&w->touching_last.touching.pending_count==1&&w->touching_last.singleton.snapshot_published);
 FILE* dump=fopen(argv[6],"wbx");CHECK(dump!=NULL);if(dump){CHECK(fwrite(&w->touching_last,sizeof w->touching_last,1,dump)==1);CHECK(fclose(dump)==0);}
 CHECK(memcmp(&contact,&w->touching_last.uncapped,sizeof contact)==0);CHECK(memcmp(solved.contact.velocities,w->touching_last.block.contact.velocities,sizeof solved.contact.velocities)==0);
 CHECK(dango_physics_world_destroy(handle)==0&&!dango_tr_current.body);
 printf("{\"kind\":\"touching-runtime-fixed-negative\",\"assertions\":%u,\"negativeCases\":%u,\"failures\":%u,\"diagnosticBytes\":%zu,\"ordinaryPrefixBytes\":200712,\"candidateFixedSubstep\":71125,\"capScale\":1,\"capScaleLessThanOnePositiveVerified\":false,\"scaledConeRejection\":true}\n",assertions,negatives,failures,sizeof prepared);
 return failures?1:0;
}

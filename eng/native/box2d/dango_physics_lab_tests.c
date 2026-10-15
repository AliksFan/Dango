#include <stdio.h>
#include <math.h>
#include <string.h>
#include "dango_physics_module.h"
int dango_physics_lab_version(void);
int dango_physics_world_get_lab_stats(void*,int,float*,int);
int dango_physics_world_get_drive_review(void*,int,float*,int);
int dango_physics_world_step_lab(void*,float,const int*,const float*,int,int,float,int*,float*,int,int*,int*,int*);
#define CHECK(c) do { if(!(c)) { fprintf(stderr,"FAIL %d: %s\n",__LINE__,#c); return 1; } } while(0)

static double capturedX,capturedY,capturedTorque,capturedNodeMagnitude;
static int capturedCalls;
static b2BodyId captureCore;
static b2Vec2 captureOrigin;
static void lab_capture(b2BodyId id,b2Vec2 force,bool wake) {
    b2Vec2 p=b2Body_GetWorldCenterOfMass(id);
    capturedX+=force.x; capturedY+=force.y;
    capturedTorque+=(p.x-captureOrigin.x)*force.y-(p.y-captureOrigin.y)*force.x;
    if(memcmp(&id,&captureCore,sizeof id)) capturedNodeMagnitude+=hypot(force.x,force.y);
    ++capturedCalls;
    if(!wake) capturedCalls=-100000;
}
#define b2Body_ApplyForceToCenter lab_capture
#include "dango_physics_lab.h"
#undef b2Body_ApplyForceToCenter
    static const dango_self_physics_config config={
        .node_count=16,.substep_count=4,.radial_rest_length=.68f,.radial_minimum_ratio=.65f,.radial_maximum_ratio=1.22f,
        .perimeter_minimum_ratio=.73f,.perimeter_maximum_ratio=1.07f,.radial_hertz=5.2f,.perimeter_hertz=20.1f,
        .radial_damping=1.85f,.perimeter_damping=1.85f,.area_strength=4.8f,.ground_deformation_strength=3.5f,
        .repulsion_distance=.09f,.repulsion_strength=.3f,.repulsion_maximum_force=3.5f,.core_exclusion_ratio=.25f,
        .angle_strength=10,.local_angle_balance_strength=1.7f,.top_inward_force=.5f,.gravity=9.8f,.core_mass=.12f,.node_mass=.015f,
        .drop_height=.42f,.ground_height=-.72f,.node_radius=.045f,.friction=.72f,.restitution=.02f,.contact_hertz=28,
        .linear_damping=1.8f,.drag_stiffness=10,.drag_damping=2.2f,.drag_maximum_force=8,.horizontal_force=2.4f,
        .maximum_horizontal_speed=3.2f,.air_control_ratio=.35f,.downward_force=2.2f,.jump_impulse=.42f,
        .jump_cooldown_seconds=.45f,.maximum_speed=12,.minimum_area=.42f
    };


static int captured_contract(void) {
    b2WorldDef wd=b2DefaultWorldDef(); wd.gravity=(b2Vec2){0,0};
    b2WorldId world=b2CreateWorld(&wd); dango_self_soft_body body={0}; body.node_count=16;
    for(int i=0;i<17;++i) {
        b2BodyDef bd=b2DefaultBodyDef(); bd.type=b2_dynamicBody;
        double a=6.283185307179586*(i-1)/16;
        bd.position=i==0 ? (b2Vec2){3,4} : (b2Vec2){(float)(3+.68*cos(a)),(float)(4+.68*sin(a))};
        b2BodyId id=b2CreateBody(world,&bd);
        b2ShapeDef sd=b2DefaultShapeDef(); sd.density=i==0 ? 10.0f : 1.0f+(float)i*.03f;
        b2Circle circle={{0,0},.05f}; b2CreateCircleShape(id,&sd,&circle);
        if(i==0) body.core_body_id=id; else body.node_body_ids[i-1]=id;
    }
    captureCore=body.core_body_id; captureOrigin=b2Body_GetWorldCenterOfMass(captureCore);
    dango_lab_state lab={0}; lab.ramp=lab.stretch=1; float c[6]={0};
    CHECK(!dango_lab_forces(&body,&config,&lab,.001f,c));
    CHECK(capturedCalls==32 && fabs(capturedX)<1e-7 && fabs(capturedY)<1e-7 && fabs(capturedTorque)<1e-6);
    CHECK(capturedNodeMagnitude>0 && capturedNodeMagnitude<=config.downward_force+1e-6);
    CHECK(fabs(lab.stats[9]-capturedNodeMagnitude)<1e-6);
    capturedX=capturedY=capturedTorque=capturedNodeMagnitude=0;capturedCalls=0;
    lab.ramp=lab.stretch=0;
    CHECK(!dango_lab_forces(&body,&config,&lab,.001f,c) && capturedCalls==0);
    c[0]=1;lab.mode=1;
    CHECK(!dango_lab_forces(&body,&config,&lab,.001f,c) && capturedCalls==17);
    CHECK(fabs(capturedX-lab.stats[3])<1e-6 && fabs(capturedY)<1e-7);
    const float biases[]={-1,-.43f,.37f,1};
    for(int k=0;k<4;++k) {
        capturedX=capturedY=capturedTorque=capturedNodeMagnitude=0;capturedCalls=0;
        lab.mode=3;lab.bias=biases[k];
        CHECK(!dango_lab_forces(&body,&config,&lab,.001f,c));
        double expected=4+biases[k]*.68;
        CHECK(capturedCalls>=1 && capturedCalls<=2);
        CHECK(fabs(capturedX-lab.stats[3])<1e-6 && capturedY==0);
        CHECK(fabs(lab.stats[7]-expected)<1e-6);
        CHECK(fabs(capturedTorque+(expected-captureOrigin.y)*capturedX)<1e-6);
        CHECK(fabs(lab.stats[6]+(lab.stats[7]-lab.stats[8])*lab.stats[4])<1e-6);
    }
    capturedX=capturedY=capturedTorque=capturedNodeMagnitude=0;capturedCalls=0;
    c[0]=0;c[2]=1;lab.ramp=.4f;lab.stretch=1;lab.jump_accepted=true;
    CHECK(!dango_lab_forces(&body,&config,&lab,.01f,c));
    CHECK(lab.ramp==.4f && capturedCalls==0);
    lab.jump_accepted=false;
    CHECK(!dango_lab_forces(&body,&config,&lab,.01f,c));
    CHECK(lab.ramp>.4f && capturedCalls==32 && capturedNodeMagnitude>0);
    b2DestroyWorld(world); return 0;
}
static int step(void* w,int id,float* c,int mode,float bias,float* state,float* stats) {
 int sid=0,n=0,contact=0,error=0;
 int result=dango_physics_world_step_lab(w,1.0f/60,&id,c,1,mode,bias,&sid,state,42,&n,&contact,&error);
 if(result || error || n!=1 || sid!=id) return 1;
 return dango_physics_world_get_lab_stats(w,id,stats,16);
}
int main(void) {
 CHECK(dango_physics_lab_version()==1); CHECK(!captured_contract());
 void* a=NULL; void* b=NULL; int ia=0,ib=0,sid=0,n=0,contact=0,error=0;
 CHECK(!dango_physics_world_create(&config,1,&a) && !dango_physics_world_create(&config,1,&b));
 CHECK(!dango_physics_world_add(a,0,2,&ia) && !dango_physics_world_add(b,0,2,&ib));
 float c[9]={0},state[42],other[42],stats[16];
 for(int frame=0;frame<240;++frame) {
  c[0]=frame>20 && frame<80 ? 1.0f : 0;
  c[1]=frame>100 && frame<130 ? 1.0f : 0; c[2]=frame==160 ? 1.0f : 0;
  CHECK(!dango_physics_world_step(a,1.0f/60,&ia,c,1,&sid,state,42,&n,&contact,&error) && !error);
  CHECK(!step(b,ib,c,0,0,other,stats)); CHECK(!memcmp(state,other,sizeof state));
 }
 CHECK(!dango_physics_world_reset(a) && !dango_physics_world_reset(b)); memset(c,0,sizeof c);
 for(int frame=0;frame<360;++frame) {
  c[0]=frame>20 && frame<80 ? 1.0f : frame>180 && frame<230 ? -1.0f : 0;
  c[1]=frame>100 && frame<130 ? 1.0f : 0; c[2]=frame==160 ? 1.0f : 0;
  CHECK(!dango_physics_world_step(a,1.0f/60,&ia,c,1,&sid,state,42,&n,&contact,&error) && !error);
  CHECK(!step(b,ib,c,3,0,other,stats)); CHECK(!memcmp(state,other,sizeof state));
 }
 CHECK(!dango_physics_world_reset(a) && !dango_physics_world_reset(b)); memset(c,0,sizeof c);
 for(int i=0;i<120;++i) {
  CHECK(!dango_physics_world_step(a,1.0f/60,&ia,c,1,&sid,state,42,&n,&contact,&error) && !error);
  CHECK(!step(b,ib,c,1,0,other,stats)); CHECK(!memcmp(state,other,sizeof state));
 }
 CHECK(!dango_physics_world_reset(a)); c[0]=1; c[1]=c[2]=0;
 CHECK(!step(a,ia,c,1,0,state,stats));
 CHECK(fabsf(stats[3]-stats[4])<1e-6f && fabsf(stats[6])<1e-6f && fabsf(stats[7]-stats[8])<1e-6f);
 CHECK(!dango_physics_world_reset(a)); CHECK(!step(a,ia,c,2,1,state,stats));
 CHECK(stats[7]>stats[8] && stats[6]<0 && fabsf(stats[3]-stats[4])<1e-6f);
 float upper=stats[7];
 CHECK(!dango_physics_world_reset(a)); CHECK(!step(a,ia,c,2,-1,state,stats));
 CHECK(stats[7]<stats[8] && stats[6]>0 && fabsf(stats[7]-upper)>.01f);
 CHECK(!dango_physics_world_reset(a)); memset(c,0,sizeof c); c[6]=c[7]=1;
 for(int i=0;i<12;++i) CHECK(!step(a,ia,c,0,0,state,stats));
 CHECK(stats[2]>.999f && stats[9]>0 && stats[9]<=config.downward_force+1e-6f);
 CHECK(fabsf(stats[10])+fabsf(stats[11])+fabsf(stats[12])<1e-6f);
 float ramp=stats[2];
 c[2]=c[6]=c[7]=0; CHECK(!step(a,ia,c,0,0,state,stats)); CHECK(stats[2]<ramp && stats[2]>0);
 for(int i=0;i<13;++i) CHECK(!step(a,ia,c,0,0,state,stats)); CHECK(stats[2]==0 && stats[9]==0);
 c[6]=1; CHECK(!step(a,ia,c,0,0,state,stats)); CHECK(stats[2]>0);
 c[8]=1; CHECK(!step(a,ia,c,0,0,state,stats)); CHECK(stats[2]==0 && stats[9]==0);
 float before[102],after[102],safeStats[16];
 CHECK(!dango_physics_world_get_drive_review(a,ia,before,102)); memcpy(safeStats,stats,sizeof stats);
 n=123;contact=456;error=789;c[8]=NAN;
 CHECK(dango_physics_world_step_lab(a,1.0f/60,&ia,c,1,1,0,&sid,state,42,&n,&contact,&error)==-410);
 CHECK(n==123 && contact==456 && error==789);
 CHECK(!dango_physics_world_get_drive_review(a,ia,after,102) && !memcmp(before,after,sizeof before));
 CHECK(!dango_physics_world_get_lab_stats(a,ia,stats,16) && !memcmp(safeStats,stats,sizeof stats));
 c[8]=0; CHECK(!step(a,ia,c,0,0,state,stats)); CHECK(stats[2]>0);
 float old[6]={0}; CHECK(!dango_physics_world_step(a,1.0f/60,&ia,old,1,&sid,state,42,&n,&contact,&error));
 CHECK(!dango_physics_world_get_lab_stats(a,ia,stats,16) && stats[2]==0);
 /* Grounded accepted jump: S+W are absent for the entire frame, ramp frozen. */
 CHECK(!dango_physics_world_reset(a) && !dango_physics_world_reset(b)); memset(c,0,sizeof c);
 for(int i=0;i<300;++i) {
  CHECK(!step(a,ia,c,0,0,state,stats)); CHECK(!step(b,ib,c,0,0,other,stats));
 }
 c[6]=1;
 for(int i=0;i<3;++i) {
  CHECK(!step(a,ia,c,0,0,state,stats)); CHECK(!step(b,ib,c,0,0,other,stats));
 }
 float savedRamp=stats[2];
 float jumpOnly[6]={0,0,1,0,0,0};
 CHECK(!dango_physics_world_step(a,1.0f/60,&ia,jumpOnly,1,&sid,state,42,&n,&contact,&error) && !error);
 c[1]=c[2]=1;
 CHECK(!step(b,ib,c,0,0,other,stats));
 CHECK(!memcmp(state,other,sizeof state) && stats[2]==savedRamp && stats[9]==0);
 /* Airborne rejected jump retains S, and W is allowed to ramp and apply. */
 CHECK(!dango_physics_world_reset(a) && !dango_physics_world_reset(b)); memset(c,0,sizeof c);
 c[1]=c[2]=1;
 CHECK(!dango_physics_world_step(a,1.0f/60,&ia,c,1,&sid,state,42,&n,&contact,&error) && !error);
 CHECK(!step(b,ib,c,0,0,other,stats)); CHECK(!memcmp(state,other,sizeof state));
 CHECK(!dango_physics_world_reset(b)); c[6]=1;
 CHECK(!step(b,ib,c,0,0,other,stats)); CHECK(stats[2]>0 && stats[9]>0);
 CHECK(!dango_physics_world_destroy(a) && !dango_physics_world_destroy(b));
 puts("physics lab: legacy parity, distributed force, signed bias, actual point height and 360-frame zero-bias parity, accepted/rejected jump arbitration, stretch ramp/budget/cancel, atomic rejection PASS"); return 0;
}

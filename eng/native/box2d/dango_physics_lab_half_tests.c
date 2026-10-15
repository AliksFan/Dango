#include "dango_self_physics_internal.h"
#include <windows.h>
static b2BodyId nodeIds[17];static b2Vec2 capturedForces[17];static unsigned called;
static void capture_detail(b2BodyId id,b2Vec2 f){for(int i=0;i<17;++i)if(!memcmp(&id,&nodeIds[i],sizeof id)){capturedForces[i]=b2Add(capturedForces[i],f);called|=1u<<i;}}
#define DANGO_LAB_CAPTURE_DETAIL capture_detail
#define main old_lab_main
#include "dango_physics_lab_tests.c"
#undef main
int dango_physics_lab_region_fraction(void);
typedef int (*create_fn)(const dango_self_physics_config*,int,void**);
typedef int (*add_fn)(void*,float,float,int*);
typedef int (*step_fn)(void*,float,const int*,const float*,int,int,float,int*,float*,int,int*,int*,int*);
typedef int (*destroy_fn)(void*);
static int actual_half_capture(void) {
 b2WorldDef wd=b2DefaultWorldDef();wd.gravity=(b2Vec2){0};b2WorldId w=b2CreateWorld(&wd);
 dango_self_soft_body body={0};body.node_count=16;body.grounded=true;double masses[17];b2Vec2 p[17];
 for(int i=0;i<17;++i) {
  double a=6.283185307179586*(i-1)/16;p[i]=i==0 ? (b2Vec2){0} : (b2Vec2){(float)(.68*cos(a)),(float)(.68*sin(a))};
  b2BodyDef bd=b2DefaultBodyDef();bd.type=b2_dynamicBody;bd.position=p[i];nodeIds[i]=b2CreateBody(w,&bd);
  b2ShapeDef sd=b2DefaultShapeDef();sd.density=1+(float)i*.2f;b2Circle c={{0},.05f};b2CreateCircleShape(nodeIds[i],&sd,&c);masses[i]=b2Body_GetMass(nodeIds[i]);
  if(i==0)body.core_body_id=nodeIds[i];else body.node_body_ids[i-1]=nodeIds[i];
 }
 float bounds[4];CHECK(dango_region_bounds(p,config.node_radius,bounds));
 for(int dir=-1;dir<=2;++dir) {
  memset(capturedForces,0,sizeof capturedForces);called=0;capturedX=capturedY=capturedTorque=0;
  dango_lab_state lab={0};lab.mode=4;float c[6]={0};
  if(dir<0)c[0]=-1;else if(dir==1)c[0]=1;else if(dir==0)lab.stretch=lab.ramp=1;else c[1]=1;
  CHECK(!dango_lab_forces(&body,&config,&lab,.001f,c));
  int direction=dir==-1 ? -1 : dir==1 ? 1 : 0;unsigned expected=0;double selectedMass=0;
  for(int i=0;i<16;++i)if(direction<0 ? p[i+1].x<=(bounds[0]+bounds[2])*.5f : direction>0 ? p[i+1].x>=(bounds[0]+bounds[2])*.5f : p[i+1].y>=(bounds[1]+bounds[3])*.5f){expected|=1u<<i;selectedMass+=masses[i+1];}
  CHECK(called==(expected<<1));double total=direction ? config.horizontal_force : config.downward_force;
  CHECK(fabs(hypot(capturedX,capturedY)-total)<1e-6);CHECK(direction || (dir==0 ? capturedY>0 : capturedY<0));
  for(int i=0;i<16;++i)if(expected&(1u<<i))CHECK(fabs(hypot(capturedForces[i+1].x,capturedForces[i+1].y)-total*masses[i+1]/selectedMass)<1e-6);
 }
 b2DestroyWorld(w);return 0;
}
static int unchanged_core(void) {
 HMODULE dll=LoadLibraryA("dango_physics_lab.dll");CHECK(dll!=NULL);
 create_fn create=(create_fn)(void*)GetProcAddress(dll,"dango_physics_world_create");
 add_fn add=(add_fn)(void*)GetProcAddress(dll,"dango_physics_world_add");
 step_fn legacy_step=(step_fn)(void*)GetProcAddress(dll,"dango_physics_world_step_lab");
 destroy_fn destroy=(destroy_fn)(void*)GetProcAddress(dll,"dango_physics_world_destroy");CHECK(create && add && legacy_step && destroy);
 dango_self_physics_config cfg=config;cfg.radial_hertz=1.82f;cfg.perimeter_hertz=15.075f;cfg.top_inward_force=cfg.ground_deformation_strength=0;
 void* old=NULL;void* half=NULL;int oldId=0,halfId=0;CHECK(!create(&cfg,1,&old) && !dango_physics_world_create(&cfg,1,&half));
 CHECK(!add(old,0,2,&oldId) && !dango_physics_world_add(half,0,2,&halfId));
 float c[9]={0},oldState[42],halfState[42];int sid=0,n=0,contacts=0,error=0;
 CHECK(dango_physics_world_step_lab(half,1.0f/60,&halfId,c,1,5,0,&sid,halfState,42,&n,&contacts,&error)==-410);
 for(int frame=0;frame<600;++frame) {
  memset(c,0,sizeof c);c[0]=frame>100 && frame<180 ? 1.0f : 0;c[6]=frame>250 && frame<300 ? 1.0f : 0;c[1]=frame>350 && frame<380 ? 1.0f : 0;c[2]=frame==450 ? 1.0f : 0;
  CHECK(!legacy_step(old,1.0f/60,&oldId,c,1,0,0,&sid,oldState,42,&n,&contacts,&error) && !error);
  CHECK(!dango_physics_world_step_lab(half,1.0f/60,&halfId,c,1,0,0,&sid,halfState,42,&n,&contacts,&error) && !error);
  CHECK(!memcmp(oldState,halfState,sizeof oldState));
 }
 CHECK(!destroy(old) && !dango_physics_world_destroy(half));FreeLibrary(dll);return 0;
}
int main(void){CHECK(dango_physics_lab_region_fraction()==2);CHECK(!actual_half_capture());CHECK(!unchanged_core());puts("half-region captured-force and 600-frame bitwise core comparison passed");return 0;}

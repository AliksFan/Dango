#define main source_contract_main
#include "dango_belt_tests.c"
#undef main
#ifdef _WIN32
#include <windows.h>
#endif
static float product_cal[11]={1.4f,1,1,4,1.4f,1,1,1,1.8f,35,60};
static dango_belt_config body_config(double size,double soft,double build){
 dango_belt_config c=config;double scale=size/100,radial=soft<=50?.55-soft*.004:.35-(soft-50)*.002;
 double perimeter=soft<=50?.90-soft*.003:.75-(soft-50)*.004;
 c.radial_rest_length=(float)(.68*scale);c.node_radius=(float)(.045*scale);c.minimum_area=(float)(.42*scale*scale);
 c.core_mass=(float)(.12*scale*scale);c.node_mass=(float)(.015*scale*scale);
 double r=build<50?.5+.5*build/50:1+.6*(build-50)/50;
 double p=build<50?.7+.3*build/50:1+.15*(build-50)/50;
 double a=build<50?1:1+.15*(build-50)/50;
 c.radial_hertz=(float)(5.2*radial*r);c.perimeter_hertz=(float)(20.1*perimeter*p);c.area_strength=(float)(4.8*a);
 c.radial_damping=(float)(1.85-(build<50?.05*fmax(0,(soft-50)/50)*(1-build/50)*fmax(0,fmin(1,(size-100)/20)):0));return c;
}
static int product_create(const dango_belt_config* c,void** h){int rc=dango_belt_create_calibrated(c,0,0,1,product_cal,11,h);if(rc)return rc;
 if((rc=dango_belt_set_shape_guard(*h,1))||(rc=dango_belt_set_speed_saturation(*h,1))||(rc=dango_belt_set_radial_drive(*h,1))||(rc=dango_belt_set_original_ground_jump(*h,1)))return rc;return 0;}
static double com_velocity(const belt_body* b,int axis){return (b->mass[axis]*b->v[axis]+b->mass[34+axis]*b->v[34+axis])/(b->mass[axis]+b->mass[34+axis]);}
static int hot_contract(int mutation){
 double edit_energy=0;
 for(int state=0;state<3;++state){void* h=NULL;dango_belt_config c=body_config(100,50,50);CHECK(product_create(&c,&h)==0);float input[9]={0};
  for(int tick=0;tick<240;++tick)CHECK(dango_belt_step(h,.0166666f,input,9)==0);
  belt_body* b=belt_lookup(h);if(state==1){input[2]=1;CHECK(dango_belt_step(h,.0166666f,input,9)==0);input[2]=0;CHECK(!b->grounded);}
  if(state==2){input[3]=1;input[4]=(float)b->q[34];input[5]=(float)(b->q[35]+b->radius+.03);CHECK(dango_belt_step(h,.0166666f,input,9)==0);CHECK(b->dragging);}
  for(int i=0;i<24;++i){dango_belt_config next=body_config(i%2?120:80,i%4<2?0:100,i%3?100:0);belt_body before=*b;
   double old_bounds[4],new_bounds[4],foot;belt_bounds(before.q,old_bounds,&foot);double vx=com_velocity(b,0),vy=com_velocity(b,1);
   CHECK(dango_belt_reconfigure(h,&next,product_cal,11)==0);
   edit_energy+=fabs(belt_kinetic(b)+belt_elastic_energy(b)+belt_gravity_energy(b)-belt_kinetic(&before)-belt_elastic_energy(&before)-belt_gravity_energy(&before));
   if(mutation==1&&i==0)memcpy(b->q,before.q,sizeof b->q); /* real geometric-migration counterexample */
   belt_bounds(b->q,new_bounds,&foot);double ratio=b->radius/before.radius;
   CHECK(fabs((new_bounds[2]-new_bounds[0])/(old_bounds[2]-old_bounds[0])-ratio)<1e-10);
   CHECK(fabs(new_bounds[1]-old_bounds[1])<1e-10);CHECK(fabs(com_velocity(b,0)-vx)<1e-12&&fabs(com_velocity(b,1)-vy)<1e-12);
   CHECK(b->solver_diag[1]==before.solver_diag[1]&&b->guard[2]==before.guard[2]&&b->dragging==before.dragging&&b->grounded==before.grounded);
   CHECK(b->mass[0]==16.0*next.node_mass&&b->mass[34]==next.core_mass);
   /* Material-only changes preserve q/v exactly. */
   before=*b;next.radial_hertz=next.radial_hertz*.9f;CHECK(dango_belt_reconfigure(h,&next,product_cal,11)==0);
   CHECK(memcmp(before.q,b->q,sizeof b->q)==0&&memcmp(before.v,b->v,sizeof b->v)==0);
   before=*b;dango_belt_config bad=next;bad.core_mass=NAN;CHECK(dango_belt_reconfigure(h,&bad,product_cal,11)<0);
   if(mutation==2)b->cfg.core_mass=999; /* partial publication must be detected */
   CHECK(memcmp(&before,b,sizeof before)==0);
   float badcal[11];memcpy(badcal,product_cal,sizeof badcal);badcal[4]=-1;CHECK(dango_belt_reconfigure(h,&next,badcal,11)<0);CHECK(memcmp(&before,b,sizeof before)==0);
   /* A geometrically valid candidate outside current bounds must roll back. */
   double saved_left=b->left;b->left=b->q[0];before=*b;CHECK(dango_belt_reconfigure(h,&next,product_cal,11)<0);CHECK(memcmp(&before,b,sizeof before)==0);b->left=saved_left;
   CHECK(dango_belt_step(h,.0166666f,input,9)==0);
  }
  CHECK(dango_belt_destroy(h)==0);printf("hot configuration state=%d, 24 endpoint roundtrips and atomic rejection PASS\n",state);
 }
 printf("sum absolute parameter-edit mechanical energy changes=%.12g (external editing work, no free-evolution equivalence claim)\n",edit_energy);return 0;
}
static int sweep_contract(void){
 for(int state=0;state<3;++state)for(int axis=0;axis<3;++axis){void* h=NULL;dango_belt_config c=body_config(100,50,50);CHECK(product_create(&c,&h)==0);float input[9]={0};
  for(int tick=0;tick<240;++tick)CHECK(dango_belt_step(h,.0166666f,input,9)==0);belt_body* b=belt_lookup(h);
  if(state==1){input[2]=1;CHECK(dango_belt_step(h,.0166666f,input,9)==0);input[2]=0;}
  if(state==2){input[3]=1;input[4]=(float)b->q[34];input[5]=(float)(b->q[35]+b->radius+.03);}
  for(int i=0;i<=40;++i){double value=i<=20?i*5:(40-i)*5;dango_belt_config next=body_config(axis==0?80+.4*value:100,axis==1?value:50,axis==2?value:50);
   CHECK(dango_belt_reconfigure(h,&next,product_cal,11)==0);CHECK(dango_belt_step(h,.0166666f,input,9)==0);
  }
  CHECK(dango_belt_destroy(h)==0);printf("single-axis sweep state=%d axis=%d complete outward/return PASS\n",state,axis);
 }
 return 0;
}
#ifdef _WIN32
typedef int (__cdecl *create_fn)(const dango_belt_config*,float,float,int,const float*,int,void**);
typedef int (__cdecl *flag_fn)(void*,int);
typedef int (__cdecl *step_fn)(void*,float,const float*,int);
typedef int (__cdecl *read_fn)(void*,float*,int,float*,int,float*,int);
typedef int (__cdecl *double_fn)(void*,double*,int);
typedef int (__cdecl *float_fn)(void*,float*,int);
typedef int (__cdecl *destroy_fn)(void*);
static int frozen_equivalence(const char* path){
 HMODULE dll=LoadLibraryA(path);CHECK(dll);
 create_fn create=(create_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_create_calibrated");
 flag_fn guard=(flag_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_set_shape_guard"),speed=(flag_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_set_speed_saturation"),radial=(flag_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_set_radial_drive"),jump=(flag_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_set_original_ground_jump");
 step_fn step=(step_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_step");read_fn read=(read_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_read");destroy_fn destroy=(destroy_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_destroy");
 const char* double_exports[]={"dango_belt_read_snapshot","dango_belt_read_solver_diagnostics","dango_belt_read_speed_limit","dango_belt_read_radial_drive","dango_belt_read_rear_drive","dango_belt_read_force_ledger"};
 int counts[]={200,24,12,16,16,1024};double_fn ours[]={dango_belt_read_snapshot,dango_belt_read_solver_diagnostics,dango_belt_read_speed_limit,dango_belt_read_radial_drive,dango_belt_read_rear_drive,dango_belt_read_force_ledger};double_fn theirs[6];for(int k=0;k<6;++k){theirs[k]=(double_fn)(uintptr_t)GetProcAddress(dll,double_exports[k]);CHECK(theirs[k]);}
 float_fn diagnostics=(float_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_read_diagnostics"),shape=(float_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_read_shape_guard");CHECK(create&&guard&&speed&&radial&&jump&&step&&read&&destroy&&diagnostics&&shape);
 int successes=0,failures=0;
 for(int corner=0;corner<9;++corner){dango_belt_config c=corner==8?body_config(100,50,50):body_config(corner&1?120:80,corner&2?100:0,corner&4?100:0);void *a=NULL,*z=NULL;
  CHECK(product_create(&c,&a)==0&&create(&c,0,0,1,product_cal,11,&z)==0);CHECK(guard(z,1)==0&&speed(z,1)==0&&radial(z,1)==0&&jump(z,1)==0);
  for(int tick=0;tick<480;++tick){float input[9]={0};input[0]=tick>=160&&tick<220?1.f:tick>=220&&tick<280?-1.f:0;input[6]=tick>=280&&tick<310?1.f:0;input[1]=tick>=310&&tick<340?1.f:0;input[2]=tick==400?1.f:0;
   int x=dango_belt_step(a,.0166666f,input,9),y=step(z,.0166666f,input,9);CHECK(x==y);if(x)failures++;else successes++;
   float sa[68],sz[68],ca[512],cz[512],ta[64],tz[64];CHECK(dango_belt_read(a,sa,68,ca,512,ta,64)==0&&read(z,sz,68,cz,512,tz,64)==0);CHECK(memcmp(sa,sz,sizeof sa)==0&&memcmp(ca,cz,sizeof ca)==0&&memcmp(ta,tz,sizeof ta)==0);
   for(int k=0;k<6;++k){double da[1024],dz[1024];CHECK(ours[k](a,da,counts[k])==0&&theirs[k](z,dz,counts[k])==0);CHECK(memcmp(da,dz,(size_t)counts[k]*sizeof(double))==0);}
   float da[48],dz[48];CHECK(dango_belt_read_diagnostics(a,da,48)==0&&diagnostics(z,dz,48)==0&&memcmp(da,dz,sizeof da)==0);CHECK(dango_belt_read_shape_guard(a,da,24)==0&&shape(z,dz,24)==0&&memcmp(da,dz,24*sizeof(float))==0);
  }
  CHECK(dango_belt_destroy(a)==0&&destroy(z)==0);printf("C455 corner=%d 480 complete channel bitwise equivalence PASS\n",corner);
 }
 FreeLibrary(dll);printf("C455 successful=%d rejected=%d (matching rejected attempts retained)\n",successes,failures);return 0;
}
#endif
int main(int argc,char** argv){
 if(argc>1&&strcmp(argv[1],"--negative-size")==0)return hot_contract(1);
 if(argc>1&&strcmp(argv[1],"--negative-rollback")==0)return hot_contract(2);
 CHECK(strcmp(dango_belt_product_model(),"continuous-belt-v1")==0&&dango_belt_product_version()==1&&dango_belt_product_capabilities()==127);
 CHECK(hot_contract(0)==0);
 CHECK(sweep_contract()==0);
#ifdef _WIN32
 if(argc>1)CHECK(frozen_equivalence(argv[1])==0);
#endif
 puts("product belt identity, state migration and frozen source contracts PASS");return 0;
}

#include "dango_belt_room.c"
#include <stdio.h>
#ifdef _WIN32
#include <windows.h>
#endif
#define CHECK(c) do{if(!(c)){fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#c);return 1;}}while(0)
static dango_belt_config config={
 .node_count=16,.substep_count=4,.radial_rest_length=.68f,.radial_minimum_ratio=.65f,.radial_maximum_ratio=1.22f,
 .perimeter_minimum_ratio=.73f,.perimeter_maximum_ratio=1.07f,.radial_hertz=1.82f,.perimeter_hertz=15.075f,
 .radial_damping=1.85f,.perimeter_damping=1.85f,.area_strength=4.8f,.local_angle_balance_strength=1.7f,
 .gravity=9.8f,.core_mass=.12f,.node_mass=.015f,.drop_height=.42f,.ground_height=-.72f,.node_radius=.045f,
 .friction=.72f,.drag_stiffness=10,.drag_damping=2.2f,.drag_maximum_force=8,.horizontal_force=2.4f,
 .maximum_horizontal_speed=3.2f,.air_control_ratio=.35f,.downward_force=2.2f,.jump_impulse=.42f,.jump_cooldown_seconds=.45f,
 .maximum_speed=12,.minimum_area=.42f,.linear_damping=1.8f};
static float calibration[11]={1.4f,1,1,4,1.4f,1,1,1,1.8f,35,60};
static dango_belt_config configuration(int corner){
 dango_belt_config c=config;double size=corner==8?100:(corner&1?120:80),soft=corner==8?50:(corner&2?100:0),build=corner==8?50:(corner&4?100:0),scale=size/100;
 double radial=soft<=50?.55-soft*.004:.35-(soft-50)*.002,perimeter=soft<=50?.90-soft*.003:.75-(soft-50)*.004;
 c.radial_rest_length=(float)(.68*scale);c.node_radius=(float)(.045*scale);c.minimum_area=(float)(.42*scale*scale);c.core_mass=(float)(.12*scale*scale);c.node_mass=(float)(.015*scale*scale);
 double r=build<50?.5+.5*build/50:1+.6*(build-50)/50,p=build<50?.7+.3*build/50:1+.15*(build-50)/50,a=build<50?1:1+.15*(build-50)/50;
 c.radial_hertz=(float)(5.2*radial*r);c.perimeter_hertz=(float)(20.1*perimeter*p);c.area_strength=(float)(4.8*a);c.radial_damping=(float)(1.85-(build<50?.05*fmax(0,(soft-50)/50)*(1-build/50)*fmax(0,fmin(1,(size-100)/20)):0));return c;
}
static int create_room(int corner,float x,float y,void** output){dango_belt_config c=configuration(corner);return dango_belt_room_create(&c,calibration,11,x,y,-20,20,output);}
static int atomic_contract(void){
 void* h=NULL;CHECK(create_room(8,0,1.5f,&h)==0);dango_belt_room_frame before,after,sentinel;CHECK(dango_belt_room_read_frame(h,&before,sizeof before)==0);memset(&sentinel,0xA7,sizeof sentinel);after=sentinel;CHECK(dango_belt_room_read_frame(h,&after,sizeof after-1)==-700);CHECK(memcmp(&sentinel,&after,sizeof after)==0);
 dango_belt_room_input invalid={.body_id=1};invalid.controls[0]=2;CHECK(dango_belt_room_step(h,.0166666f,&invalid,1)==-700);CHECK(!dango_belt_room_read_frame(h,&after,sizeof after)&&!memcmp(&before,&after,sizeof after));invalid.controls[0]=0;invalid.controls[2]=.5f;CHECK(dango_belt_room_step(h,.0166666f,&invalid,1)==-700);CHECK(!dango_belt_room_read_frame(h,&after,sizeof after)&&!memcmp(&before,&after,sizeof after));invalid.controls[2]=0;CHECK(!dango_belt_room_step(h,.0166666f,&invalid,1));
 dango_belt_room_platform p={1,0,.4,3,.2,.72};CHECK(dango_belt_room_set_platform(h,&p,0)==0);CHECK(dango_belt_room_read_frame(h,&before,sizeof before)==0);CHECK(before.scene_revision==1&&before.platform.id==1);
 p.x=1;CHECK(dango_belt_room_set_platform(h,&p,0)==-708);CHECK(dango_belt_room_read_frame(h,&after,sizeof after)==0&&memcmp(&before,&after,sizeof after)==0);
 p.x=0;p.y=before.body_snapshot[3];CHECK(dango_belt_room_set_platform(h,&p,1)<0);CHECK(dango_belt_room_read_frame(h,&after,sizeof after)==0&&memcmp(&before,&after,sizeof after)==0);
 CHECK(dango_belt_room_remove_platform(h,1,1)==0);p.y=.4;CHECK(dango_belt_room_set_platform(h,&p,2)==-708);p.id=2;CHECK(dango_belt_room_set_platform(h,&p,2)==0);
 CHECK(dango_belt_room_destroy(h)==0);CHECK(dango_belt_room_destroy(h)==-701);printf("ABI/layout atomicity, capacity-no-write, overlap/stale revision/tombstone PASS\n");return 0;
}
static int certificate_contract(void){
 void* h=NULL;CHECK(create_room(8,0,1.5f,&h)==0);room_world* w=room_lookup(h);belt_body* b=room_body(w);w->platform_enabled=1;w->platform=(dango_belt_room_platform){1,0,.4,3,.2,.72};double q[BELT_D],theta=0;memcpy(q,b->q,sizeof q);q[1]=q[35]=.4;CHECK(room_certificate(w,q,q,&theta,0)==0);
 /* Both endpoints clear, but actual Fourier interpolation traverses the slab. */
 double a[BELT_D],z[BELT_D];memcpy(a,b->q,sizeof a);memcpy(z,b->q,sizeof z);a[1]=a[35]=2;z[1]=z[35]=-2;CHECK(room_certificate(w,a,z,&theta,1)==0);
 w->platform=(dango_belt_room_platform){1,0,b->q[1],.1,.1,.72};CHECK(!room_containment(w,b->q)&&w->diag[7]==4);
 memset(w->diag,0,sizeof w->diag);w->platform=(dango_belt_room_platform){1,0,.4,3,.2,.72};w->diag[4]=ROOM_GEOMETRY_BUDGET;CHECK(room_certificate(w,b->q,b->q,&theta,0)==-1&&w->diag[2]==-706);
 CHECK(dango_belt_room_destroy(h)==0);printf("continuous interior/swept crossing, enclosed platform and budget refusal PASS\n");return 0;
}
static int rollback_contract(void){
 void* h=NULL;CHECK(create_room(8,0,0,&h)==0);room_world* w=room_lookup(h);belt_body* b=room_body(w);b->left=-.001;b->right=.001;belt_body before=*b;uint64_t tick=w->tick;dango_belt_room_input input={.body_id=1};int rc=dango_belt_room_step(h,.0166666f,&input,1);CHECK(rc<0);CHECK(memcmp(&before,b,sizeof before)==0&&w->tick==tick&&w->fault==rc);CHECK(dango_belt_room_step(h,.0166666f,&input,1)==-703);CHECK(dango_belt_room_destroy(h)==0);printf("physical bounds rejection full private-state rollback/fault latch PASS\n");return 0;
}
static int corner_contract(void){
 void* h=NULL;CHECK(create_room(8,0,1.5f,&h)==0);room_world* w=room_lookup(h);belt_body* b=room_body(w);w->platform_enabled=1;w->platform=(dango_belt_room_platform){UINT64_C(9007199254740993),-.5,1.25,1,.5,.72};w->revision=17;
 double radius=.7,phase=.01,center=(radius+1.8e-6)/sqrt(2.0);memset(b->q,0,sizeof b->q);memset(b->v,0,sizeof b->v);b->q[0]=b->q[34]=center;b->q[1]=b->q[35]=1.5+center;b->q[2]=radius*cos(phase);b->q[4]=-radius*sin(phase);b->q[3]=radius*sin(phase);b->q[5]=radius*cos(phase);b->v[0]=b->v[34]=b->v[1]=b->v[35]=-.1;
 room_row rows[512];int count=room_rows(w,b,rows),corners=0;CHECK(count>=0);for(int i=0;i<count;++i)if(rows[i].platform&&rows[i].n.x>.6&&rows[i].n.y>.6)corners++;CHECK(corners>0);memcpy(w->old_q,b->q,sizeof b->q);room_active=w;CHECK(belt_room_contact(b));room_active=NULL;
 uint64_t support=(uint64_t)(uint32_t)w->diag[18]|((uint64_t)(uint32_t)w->diag[19]<<32);CHECK(support==w->platform.id);CHECK(w->diag[9]<=2e-6);belt_read_stats(b);dango_belt_room_frame frame;CHECK(!dango_belt_room_read_frame(h,&frame,sizeof frame)&&frame.body_snapshot[82]==1&&frame.stats[14]==1);CHECK(!dango_belt_room_remove_platform(h,w->platform.id,17));CHECK(!dango_belt_room_read_frame(h,&frame,sizeof frame)&&frame.body_snapshot[82]==0&&frame.stats[14]==0&&frame.stats[25]==0&&frame.diagnostics[18]==0&&frame.diagnostics[19]==0);CHECK(dango_belt_room_destroy(h)==0);printf("rotated circle 1.8e-6 corner activation, uint64 support identity and immediate remove support consistency PASS\n");return 0;
}
static void dump_array(FILE* file,const char* key,const double* values,int count){fprintf(file,"\"%s\":[",key);for(int i=0;i<count;++i)fprintf(file,"%s%.17g",i?",":"",values[i]);fprintf(file,"]");}
static void dump_failure(room_world* w,int corner,const float* controls){char path[160];snprintf(path,sizeof path,"artifacts/belt-room-f7/native/platform-failure-corner-%d-variant-%d.json",corner,DANGO_BELT_ROOM_NEGATIVE);FILE* file=NULL;
#ifdef _WIN32
 fopen_s(&file,path,"wb");
#else
 file=fopen(path,"wb");
#endif
 if(!file)return;double snapshot[200],input[9];dango_belt_read_snapshot(w->body_handle,snapshot,200);for(int i=0;i<9;++i)input[i]=controls[i];fprintf(file,"{\"corner\":%d,\"lastSuccessfulTick\":%llu,\"sceneRevision\":%llu,\"fault\":%d,",corner,(unsigned long long)w->tick,(unsigned long long)w->revision,w->fault);dump_array(file,"diagnostics",w->diag,24);fprintf(file,",");dump_array(file,"lastSuccessfulSnapshot",snapshot,200);fprintf(file,",");dump_array(file,"input",input,9);fprintf(file,",\"inputHistory\":[");for(uint64_t tick=0;tick<=w->tick;++tick){if(tick)fprintf(file,",");fprintf(file,"[");for(int i=0;i<9;++i)fprintf(file,"%s%.17g",i?",":"",input[i]);fprintf(file,"]");}fprintf(file,"],");dump_array(file,"attemptQ",room_trace_attempt_body.q,36);fprintf(file,",");dump_array(file,"attemptV",room_trace_attempt_body.v,36);fprintf(file,",");dump_array(file,"certificateFrom",room_trace_from,36);fprintf(file,",");dump_array(file,"certificateTo",room_trace_to,36);fprintf(file,",");dump_array(file,"certificateCell",room_trace_cell,6);fprintf(file,"}\n");fclose(file);
 snprintf(path,sizeof path,"artifacts/belt-room-f7/native/platform-last-safe-body-corner-%d-variant-%d.bin",corner,DANGO_BELT_ROOM_NEGATIVE);
#ifdef _WIN32
 fopen_s(&file,path,"wb");
#else
 file=fopen(path,"wb");
#endif
 if(file){fwrite(&room_trace_safe_body,sizeof room_trace_safe_body,1,file);fclose(file);}
}
static int public_bounds_rollback(void){
 void* h=NULL;dango_belt_config c=configuration(8);CHECK(!dango_belt_room_create(&c,calibration,11,0,0,-2,2,&h));dango_belt_room_frame before,after;dango_belt_room_input input={.body_id=1};input.controls[0]=1;int rc=0,tick;
 for(tick=0;tick<480;++tick){CHECK(!dango_belt_room_read_frame(h,&before,sizeof before));rc=dango_belt_room_step(h,.0166666f,&input,1);if(rc)break;}
 CHECK(rc==DANGO_BELT_ROOM_REJECTED);CHECK(!dango_belt_room_read_frame(h,&after,sizeof after));CHECK(!memcmp(before.body_snapshot,after.body_snapshot,sizeof before.body_snapshot)&&!memcmp(before.scaffold,after.scaffold,sizeof before.scaffold)&&!memcmp(before.curve,after.curve,sizeof before.curve)&&!memcmp(before.stats,after.stats,sizeof before.stats));CHECK(before.tick==after.tick&&before.scene_revision==after.scene_revision&&after.fault==rc);CHECK(dango_belt_room_step(h,.0166666f,&input,1)==-703);CHECK(!dango_belt_room_destroy(h));printf("public walking-to-boundary physical failure tick=%d full snapshot rollback PASS\n",tick);return 0;
}
static int platform_contract(void){
 int failures=0;
 for(int corner=0;corner<9;++corner){void* h=NULL;CHECK(create_room(corner,0,1.5f,&h)==0);dango_belt_room_platform p={1,0,.4,3,.2,.72};CHECK(dango_belt_room_set_platform(h,&p,0)==0);room_world* w=room_lookup(h);belt_body* b=room_body(w);dango_belt_room_input input={.body_id=1};int tick,rc=0;
  for(tick=0;tick<360;++tick){rc=dango_belt_room_step(h,.0166666f,&input,1);if(rc)break;double theta=0;CHECK(room_certificate(w,b->q,b->q,&theta,0)==1);}
  printf("platform corner=%d completed=%d rc=%d reason=%.0f geometryWork=%.0f thetaDepth=%.0f timeDepth=%.0f penetration=%.12g residual=%.12g support=%.0f refusalLine=%d\n",corner,tick,rc,w->diag[7],w->diag[4],w->diag[5],w->diag[6],w->diag[8],w->diag[9],w->diag[18],room_refusal_line);
  if(rc||!b->grounded||w->diag[18]!=1)failures++;if(rc)dump_failure(w,corner,input.controls);
  if(!rc){double height=b->q[35];CHECK(dango_belt_room_remove_platform(h,1,1)==0);CHECK(!b->grounded);for(int i=0;i<30;++i)if(dango_belt_room_step(h,.0166666f,&input,1)){failures++;break;}if(!(b->q[35]<height))failures++;}
  CHECK(dango_belt_room_destroy(h)==0);
 }
 CHECK(failures==0);printf("nine configurations finite platform standing and support removal PASS\n");return 0;
}
static int scenario_contract(void){
 const char* names[]={"edge","side","bottom","corner","jump-onto","drag-pressure"};int failures=0;
 for(int kind=0;kind<6;++kind)for(int corner=0;corner<9;++corner){float x=kind==1?-2.5f:kind==3?-1.3f:kind==4?-2.3f:0,y=kind==2?-.42f:(kind==1||kind==4?0:1.5f);void* h=NULL;CHECK(!create_room(corner,x,y,&h));dango_belt_room_platform p={1,0,kind==2?1.2:kind==1?-.1:.4,kind==1||kind==3||kind==4?1:3,kind==1?1:.2,.72};int setup=dango_belt_room_set_platform(h,&p,0);if(setup){printf("scenario=%s corner=%d SETUP_REJECTED=%d behavior=UNVERIFIED\n",names[kind],corner,setup);failures++;CHECK(!dango_belt_room_destroy(h));continue;}
  room_world* w=room_lookup(h);dango_belt_room_input input={.body_id=1};int rc=0,tick,observed=0;double start_x=room_body(w)->q[34];
  for(tick=0;tick<240;++tick){memset(input.controls,0,sizeof input.controls);if(kind==0&&tick>=120)input.controls[0]=1;if(kind==1||kind==3||kind==4)input.controls[0]=1;if(kind==2&&tick==120)input.controls[2]=1;if(kind==4&&tick==60)input.controls[2]=1;if(kind==5&&tick>=120){input.controls[3]=1;input.controls[4]=0;input.controls[5]=.2f;}
   rc=dango_belt_room_step(h,.0166666f,&input,1);if(rc)break;room_world probe=*w;room_row rows[512];int count=room_rows(&probe,room_body(w),rows);if(count<0){rc=-707;break;}
   for(int i=0;i<count;++i)if(rows[i].platform){if(kind==1&&fabs(rows[i].n.x)>.7)observed=1;if(kind==2&&rows[i].n.y<-.7)observed=1;if(kind==3&&fabs(rows[i].n.x)>.05&&fabs(rows[i].n.y)>.05)observed=1;if(kind==4&&rows[i].n.y>.7)observed=1;if(kind==5&&tick>=120&&room_body(w)->dragging)observed=1;}
   if(kind==0&&tick>=120&&room_body(w)->q[34]>start_x+.5&&w->diag[18]==0)observed=1;
  }
  printf("scenario=%s corner=%d completed=%d rc=%d observed=%d reason=%.0f work=%.0f depth=%.0f residual=%.12g\n",names[kind],corner,tick,rc,observed,w->diag[7],w->diag[4],w->diag[5],w->diag[9]);if(rc||!observed)failures++;CHECK(!dango_belt_room_destroy(h));
 }
 CHECK(failures==0);return 0;
}
#ifdef _WIN32
typedef int (__cdecl *create_fn)(const dango_belt_config*,float,float,int,const float*,int,void**);
typedef int (__cdecl *flag_fn)(void*,int);
typedef int (__cdecl *step_fn)(void*,float,const float*,int);
typedef int (__cdecl *read_fn)(void*,float*,int,float*,int,float*,int);
typedef int (__cdecl *double_fn)(void*,double*,int);
typedef int (__cdecl *float_fn)(void*,float*,int);
typedef int (__cdecl *destroy_fn)(void*);
static int equivalence(const char* path){
 HMODULE dll=LoadLibraryA(path);CHECK(dll);create_fn create=(create_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_create_calibrated");flag_fn guard=(flag_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_set_shape_guard"),speed=(flag_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_set_speed_saturation"),radial=(flag_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_set_radial_drive"),jump=(flag_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_set_original_ground_jump"),bounds=NULL;
 (void)bounds;step_fn step=(step_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_step");read_fn read=(read_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_read");destroy_fn destroy=(destroy_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_destroy");
 const char* names[]={"dango_belt_read_snapshot","dango_belt_read_solver_diagnostics","dango_belt_read_speed_limit","dango_belt_read_radial_drive","dango_belt_read_rear_drive","dango_belt_read_force_ledger"};int counts[]={200,24,12,16,16,1024};double_fn local[]={dango_belt_read_snapshot,dango_belt_read_solver_diagnostics,dango_belt_read_speed_limit,dango_belt_read_radial_drive,dango_belt_read_rear_drive,dango_belt_read_force_ledger},remote[6];for(int i=0;i<6;++i){remote[i]=(double_fn)(uintptr_t)GetProcAddress(dll,names[i]);CHECK(remote[i]);}
 float_fn energy=(float_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_read_diagnostics"),shape=(float_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_read_shape_guard");CHECK(create&&guard&&speed&&radial&&jump&&step&&read&&destroy&&energy&&shape);
 for(int corner=0;corner<9;++corner){void *h=NULL,*other=NULL;dango_belt_config c=configuration(corner);CHECK(dango_belt_room_create(&c,calibration,11,0,0,-1e6f,1e6f,&h)==0);CHECK(create(&c,0,0,1,calibration,11,&other)==0);CHECK(!guard(other,1)&&!speed(other,1)&&!radial(other,1)&&!jump(other,1));room_world* w=room_lookup(h);
  for(int tick=0;tick<480;++tick){dango_belt_room_input input={.body_id=1};input.controls[0]=tick>=160&&tick<220?1.f:tick>=220&&tick<280?-1.f:0;input.controls[6]=tick>=280&&tick<310?1.f:0;input.controls[1]=tick>=310&&tick<340?1.f:0;input.controls[2]=tick==400?1.f:0;
   int x=dango_belt_room_step(h,.0166666f,&input,1),y=step(other,.0166666f,input.controls,9);CHECK(x==y);CHECK(x==0);float sa[68],sz[68],ca[512],cz[512],ta[64],tz[64];CHECK(!dango_belt_read(w->body_handle,sa,68,ca,512,ta,64)&&!read(other,sz,68,cz,512,tz,64));CHECK(!memcmp(sa,sz,sizeof sa)&&!memcmp(ca,cz,sizeof ca)&&!memcmp(ta,tz,sizeof ta));
   for(int k=0;k<6;++k){double da[1024],dz[1024];CHECK(!local[k](w->body_handle,da,counts[k])&&!remote[k](other,dz,counts[k])&&!memcmp(da,dz,(size_t)counts[k]*sizeof(double)));}float da[48],dz[48];CHECK(!dango_belt_read_diagnostics(w->body_handle,da,48)&&!energy(other,dz,48)&&!memcmp(da,dz,sizeof da));CHECK(!dango_belt_read_shape_guard(w->body_handle,da,24)&&!shape(other,dz,24)&&!memcmp(da,dz,24*sizeof(float)));
  }
  CHECK(!dango_belt_room_destroy(h)&&!destroy(other));printf("room no-platform corner=%d 480 all-channel bitwise equivalence PASS\n",corner);
 }
 FreeLibrary(dll);return 0;
}
typedef int (__cdecl *room_create_fn)(const dango_belt_config*,const float*,int,float,float,float,float,void**);
typedef int (__cdecl *room_step_fn)(void*,float,const dango_belt_room_input*,int);
typedef int (__cdecl *room_read_fn)(void*,dango_belt_room_frame*,int);
typedef int (__cdecl *room_platform_fn)(void*,const dango_belt_room_platform*,uint64_t);
static int dll_failure_replay(const char* path){
 HMODULE dll=LoadLibraryA(path);CHECK(dll);room_create_fn create=(room_create_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_room_create");room_step_fn step=(room_step_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_room_step");room_read_fn read=(room_read_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_room_read_frame");room_platform_fn platform=(room_platform_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_room_set_platform");destroy_fn destroy=(destroy_fn)(uintptr_t)GetProcAddress(dll,"dango_belt_room_destroy");CHECK(create&&step&&read&&platform&&destroy);CHECK(!GetProcAddress(dll,"dango_belt_product_version"));
 for(int corner=1;corner<=3;corner+=2){void* h=NULL;dango_belt_config c=configuration(corner);CHECK(!create(&c,calibration,11,0,1.5f,-20,20,&h));dango_belt_room_platform p={1,0,.4,3,.2,.72};CHECK(!platform(h,&p,0));dango_belt_room_input input={.body_id=1};dango_belt_room_frame before,after;int rc=0;for(int i=0;i<480;++i){CHECK(!read(h,&before,sizeof before));rc=step(h,.0166666f,&input,1);if(rc)break;}CHECK(rc==-707);CHECK(!read(h,&after,sizeof after));CHECK(after.tick==(uint64_t)(corner==1?59:75)&&after.diagnostics[1]==(double)(after.tick+1)&&after.diagnostics[7]==3&&after.diagnostics[5]==20);CHECK(!memcmp(before.body_snapshot,after.body_snapshot,sizeof before.body_snapshot)&&!memcmp(before.scaffold,after.scaffold,sizeof before.scaffold)&&!memcmp(before.curve,after.curve,sizeof before.curve)&&!memcmp(before.stats,after.stats,sizeof before.stats)&&!memcmp(&before.platform,&after.platform,sizeof before.platform));printf("actual room DLL corner=%d successfulWorldTick=%llu rejectedAttempt=%.0f rc=%d depth=%.0f last-safe frame unchanged REPRODUCED TECHNICAL FAIL\n",corner,(unsigned long long)after.tick,after.diagnostics[1],rc,after.diagnostics[5]);CHECK(!destroy(h));}
 FreeLibrary(dll);return 0;
}
#endif
int main(int argc,char** argv){
#ifdef _WIN32
 if(argc==3&&strcmp(argv[1],"--replay-dll")==0)return dll_failure_replay(argv[2]);
#endif
 if(argc>1&&strcmp(argv[1],"--platform")==0)return platform_contract();
 if(argc>1&&strcmp(argv[1],"--scenarios")==0)return scenario_contract();
 CHECK(atomic_contract()==0);CHECK(certificate_contract()==0);CHECK(rollback_contract()==0);CHECK(corner_contract()==0);CHECK(public_bounds_rollback()==0);
#ifdef _WIN32
 if(argc>1)CHECK(equivalence(argv[1])==0);
#else
 (void)argc;(void)argv;
#endif
 return 0;
}

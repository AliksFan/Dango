#include "dango_belt_room.c"
#include <stdio.h>
#include "dango_belt_action_test_fixture.h"
#define CHECK(c) do{if(!(c)){fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#c);return 1;}}while(0)

static int test_muscle_step(void* h,float dt,const dango_belt_room_input* in,int count,float time,float strength,int profile){
 return profile==2?dango_belt_room_step_muscles_pattern(h,dt,in,count,time,strength,2,1,0,UINT32_C(0x1234)):
  dango_belt_room_step_muscles_profile(h,dt,in,count,time,strength,profile);
}

static int verify_pairs(const belt_body* b){
 const double* ledger=b->force_ledger;int n=(int)ledger[4];double fx=0,fy=0,torque=0;
 CHECK(n==2*(int)b->muscle_diag[6]&&n>=2&&n<=50);
 if(b->muscle_profile!=2&&(int)b->muscle_diag[24]!=2)CHECK(n==16);
 for(int i=0;i<n;i+=2){
  const double* a=ledger+8+6*i;const double* z=a+6;
  CHECK(a[4]==6&&z[4]==6);CHECK(fabs(a[2]+z[2])<1e-13&&fabs(a[3]+z[3])<1e-13);
  double dx=z[0]-a[0],dy=z[1]-a[1];
  CHECK(dx*a[2]+dy*a[3]>=0);CHECK(fabs(dx*a[3]-dy*a[2])<1e-12);
  fx+=a[2]+z[2];fy+=a[3]+z[3];torque+=a[0]*a[3]-a[1]*a[2]+z[0]*z[3]-z[1]*z[2];
 }
 CHECK(fabs(fx)<1e-12&&fabs(fy)<1e-12&&fabs(torque)<1e-12);
 return 0;
}

static int force_contract(void){
 void* handle=0;CHECK(!dango_belt_room_create(&preview_config,preview_calibration,11,0,10,-5,5,&handle));
 belt_body original=*room_body(room_lookup(handle));
 /* A non-circular body prevents a symmetry-only cancellation from passing. */
 original.q[2]*=1.07;original.q[5]*=.94;original.q[10]=.015;
 for(int k=0;k<8;++k){
  belt_body body=original;double angle=k*.47,cs=cos(angle),sn=sin(angle);
  for(int j=0;j<BELT_D;j+=2){double x=body.q[j],y=body.q[j+1];body.q[j]=cs*x-sn*y;body.q[j+1]=sn*x+cs*y;}
  body.q[0]+=1;body.q[1]+=20;body.q[34]+=1;body.q[35]+=20;body.muscle_strength=1;
  belt_force_ledger_begin(&body,1,0);double force[BELT_D]={0};CHECK(belt_muscle_force_at(&body,body.q,force,.23));
  CHECK(!verify_pairs(&body));CHECK(fabs(force[0]+force[34])<1e-12&&fabs(force[1]+force[35])<1e-12);
  double torque=0;for(int j=0;j<BELT_D;j+=2)torque+=body.q[j]*force[j+1]-body.q[j+1]*force[j];CHECK(fabs(torque)<1e-11);
  CHECK(body.muscle_diag[11]>0&&body.muscle_diag[11]<=preview_config.horizontal_force*.25);
 }
 CHECK(!dango_belt_room_destroy(handle));return 0;
}

static int shake_and_release(float strength,int profile){
 void* handles[2]={0};dango_belt_room_frame frames[2],baseline;
 for(int j=0;j<2;++j)CHECK(!dango_belt_room_create(&preview_config,preview_calibration,11,0,0,-5,5,&handles[j]));
 double upper=0,middle=0,top=0,foot=0,drift=0,gap=0,work=0,after=0;int active=0;double last_diag[32];
 dango_belt_room_input input={0};input.body_id=1;
 for(int i=0;i<360;++i){
  int shaking=i>=120&&i<165;float time=(float)((i-120)/60.0);
  CHECK(!dango_belt_room_step(handles[0],.0166666f,&input,1));
  CHECK(!test_muscle_step(handles[1],.0166666f,&input,1,shaking?time:0.f,shaking?strength:0.f,profile));
  for(int j=0;j<2;++j)CHECK(!dango_belt_room_read_frame(handles[j],&frames[j],sizeof frames[j]));
  if(i==119)baseline=frames[1];
  if(i<120){CHECK(!memcmp(frames[0].curve,frames[1].curve,sizeof frames[0].curve));CHECK(!memcmp(frames[0].body_snapshot,frames[1].body_snapshot,sizeof frames[0].body_snapshot));}
  if(i>=120){
   CHECK(!dango_belt_room_read_muscle_diagnostics(handles[1],last_diag,32));
   belt_body* b=room_body(room_lookup(handles[1]));
   CHECK(b->muscle_strength==0&&b->muscle_time==0&&b->muscle_profile==0&&b->dragging==0);
   CHECK(b->muscle_axis_x==0&&b->muscle_axis_y==0&&b->muscle_seed==0);
   double ledger[1024];CHECK(!dango_belt_room_read_active_forces(handles[1],ledger,1024));
   CHECK(!memcmp(ledger,b->force_ledger,sizeof ledger));
   if(shaking)CHECK((int)last_diag[24]==profile);
   if(last_diag[5]){active++;CHECK(!verify_pairs(b));CHECK(last_diag[9]<1e-10);work+=last_diag[10];}
   else CHECK(last_diag[11]==0);
   double core=fabs(frames[1].stats[0]-baseline.stats[0]);drift=fmax(drift,core);gap=fmax(gap,frames[1].stats[12]);
   for(int p=0;p<256;++p){
    double dx=frames[1].curve[2*p]-baseline.curve[2*p],dy=frames[1].curve[2*p+1]-baseline.curve[2*p+1],distance=hypot(dx,dy);
    double fraction=(baseline.curve[2*p+1]-baseline.stats[9])/(baseline.stats[11]-baseline.stats[9]);
    if(fraction>=.3&&fraction<=.68)middle=fmax(middle,distance);
    if(fraction>=.78)top=fmax(top,distance);
    if(baseline.curve[2*p+1]>baseline.stats[9]+.3*(baseline.stats[11]-baseline.stats[9]))upper=fmax(upper,distance);
    if(baseline.curve[2*p+1]<baseline.stats[9]+.035)foot=fmax(foot,distance);
    if(i==359)after=fmax(after,distance);
   }
   if(!shaking)CHECK(last_diag[5]==0&&last_diag[4]==0);
  }
 }
 printf("muscle shake profile=%d strength=%.2f: edge=%.9g middle=%.9g top=%.9g foot=%.9g coreDrift=%.9g peakGap=%.9g after=%.9g work=%.9g activeFrames=%d\n",profile,(double)strength,upper,middle,top,foot,drift,gap,after,work,active);
 CHECK(active>30&&upper>.004&&upper<.15&&foot<upper*.7&&drift<.02&&gap<.003&&after<.004&&work>0);
 /* Invalid calls must not modify either physics or held/diagnostic state. */
 belt_body* b=room_body(room_lookup(handles[1]));belt_body before=*b;room_world world_before=*room_lookup(handles[1]);
 CHECK(dango_belt_room_step_muscles(handles[1],.0166666f,&input,1,NAN,1)==DANGO_BELT_ROOM_ARGUMENT);
 CHECK(dango_belt_room_step_muscles(handles[1],.0166666f,&input,1,.2f,2.01f)==DANGO_BELT_ROOM_ARGUMENT);
 CHECK(dango_belt_room_step_muscles_profile(handles[1],.0166666f,&input,1,.2f,1.44f,2)==DANGO_BELT_ROOM_ARGUMENT);
 double no_write[1024];for(int i=0;i<1024;++i)no_write[i]=42;
 CHECK(dango_belt_room_read_active_forces(handles[1],no_write,1023)==DANGO_BELT_ROOM_ARGUMENT);
 for(int i=0;i<1024;++i)CHECK(no_write[i]==42);
 input.controls[3]=1;CHECK(dango_belt_room_step_muscles(handles[1],.0166666f,&input,1,.2f,1)==DANGO_BELT_ROOM_ARGUMENT);
 CHECK(!memcmp(b,&before,sizeof before)&&!memcmp(room_lookup(handles[1]),&world_before,sizeof world_before));
 for(int j=0;j<2;++j)CHECK(!dango_belt_room_destroy(handles[j]));return 0;
}

static int free_body_work(int profile){
 dango_belt_config cfg=preview_config;cfg.gravity=0;cfg.linear_damping=0;float cal[11];memcpy(cal,preview_calibration,sizeof cal);cal[8]=0;
 void* handle=0;CHECK(!dango_belt_room_create(&cfg,cal,11,0,10,-5,5,&handle));
 dango_belt_room_input input={0};input.body_id=1;belt_body* b=room_body(room_lookup(handle));
 for(int i=0;i<120;++i)CHECK(!dango_belt_room_step(handle,.0166666f,&input,1));
 double original[3],maximum=0,angular=0;belt_momentum(b,original);
 for(int i=0;i<45;++i){
  CHECK(!test_muscle_step(handle,.0166666f,&input,1,(float)(i/60.0),.72f,profile));
  double momentum[3];belt_momentum(b,momentum);maximum=fmax(maximum,hypot(momentum[0]-original[0],momentum[1]-original[1]));
  CHECK(maximum<1e-6);angular=fmax(angular,fabs(momentum[2]-original[2]));
  CHECK(fabs(b->diag[27]-b->muscle_diag[10])<1e-10);
 }
 printf("free-body internal muscle: maximum linear momentum change=%.9g; angular integration drift=%.9g; actual actuator work matches integrator ledger\n",maximum,angular);
 CHECK(angular<1e-5);
 CHECK(!dango_belt_room_destroy(handle));return 0;
}

static int repeated_shakes(float strength,int profile){
 void* handle=0;CHECK(!dango_belt_room_create(&preview_config,preview_calibration,11,0,0,-5,5,&handle));
 dango_belt_room_input input={0};input.body_id=1;dango_belt_room_frame frame;
 for(int i=0;i<120;++i)CHECK(!dango_belt_room_step(handle,.0166666f,&input,1));
 CHECK(!dango_belt_room_read_frame(handle,&frame,sizeof frame));double x=frame.stats[0],maximum=0;
 for(int cycle=0;cycle<10;++cycle)for(int i=0;i<63;++i){
  CHECK(!test_muscle_step(handle,.0166666f,&input,1,i<45?(float)(i/60.0):0.f,i<45?strength:0.f,profile));
  CHECK(!dango_belt_room_read_frame(handle,&frame,sizeof frame));CHECK(frame.stats[12]<.003);
  maximum=fmax(maximum,fabs(frame.stats[0]-x));
 }
 printf("ten consecutive shakes strength=%.2f: maximum core drift=%.9g m; real contact and safety gates held\n",(double)strength,maximum);
 CHECK(maximum<.05);CHECK(!dango_belt_room_destroy(handle));return 0;
}

static int fault_rollback(int profile){
 void* handle=0;CHECK(!dango_belt_room_create(&preview_config,preview_calibration,11,0,0,-5,5,&handle));
 dango_belt_room_input input={0};input.body_id=1;
 for(int i=0;i<120;++i)CHECK(!dango_belt_room_step(handle,.0166666f,&input,1));
 room_world* world=room_lookup(handle);belt_body* body=room_body(world);
 body->speed_saturation=0;body->cfg.maximum_speed=1e-8f;belt_body before=*body;uint64_t tick=world->tick;
 CHECK(test_muscle_step(handle,.0166666f,&input,1,.2f,1.44f,profile)<0);
 CHECK(world->fault!=0&&world->tick==tick);
 CHECK(!memcmp(body->q,before.q,sizeof before.q)&&!memcmp(body->v,before.v,sizeof before.v));
 CHECK(!memcmp(body->force_ledger,before.force_ledger,sizeof before.force_ledger));
 CHECK(body->muscle_strength==0&&body->muscle_time==0&&body->muscle_profile==0);
 CHECK(dango_belt_room_step(handle,.0166666f,&input,1)==DANGO_BELT_ROOM_FAULT);
 CHECK(!dango_belt_room_destroy(handle));return 0;
}

static int random_force_contract(void){
 void* handle=0;CHECK(!dango_belt_room_create(&preview_config,preview_calibration,11,0,10,-5,5,&handle));
 belt_body original=*room_body(room_lookup(handle));original.q[2]*=1.07;original.q[5]*=.94;original.q[10]=.015;
 for(int k=0;k<8;++k){
  belt_body b=original;double angle=.47*k,cs=cos(angle),sn=sin(angle);
  for(int j=0;j<BELT_D;j+=2){double x=b.q[j],y=b.q[j+1];b.q[j]=cs*x-sn*y;b.q[j+1]=sn*x+cs*y;}
  b.q[0]+=1;b.q[1]+=20;b.q[34]+=1;b.q[35]+=20;b.muscle_strength=1.44;b.muscle_profile=2;b.muscle_axis_x=cs;b.muscle_axis_y=sn;b.muscle_seed=1234;
  belt_force_ledger_begin(&b,1,0);double f[BELT_D]={0};CHECK(belt_muscle_force_at(&b,b.q,f,.23));CHECK(!verify_pairs(&b));
  double proj[BELT_D];for(int j=0;j<BELT_D;j+=2){proj[j]=cs*b.q[j]+sn*b.q[j+1];proj[j+1]=-sn*b.q[j]+cs*b.q[j+1];}
  double bounds[4],foot;belt_bounds(proj,bounds,&foot);CHECK(fabs(b.muscle_diag[30]-b.muscle_diag[29]-.75*(bounds[3]-bounds[1]))<1e-11);
  double extrema[64],cuts[66];int ne=belt_roots(proj,1,1,0,extrema),nc=0;cuts[nc++]=0;
  for(int i=0;i<ne;++i)if(extrema[i]>1e-9&&extrema[i]<2*BELT_PI-1e-9)cuts[nc++]=extrema[i];cuts[nc++]=2*BELT_PI;
  for(int row=1;row<10;++row){double level=bounds[1]+row*.1*(bounds[3]-bounds[1]),exact[64],section[64];
   int nexact=belt_roots(proj,1,0,level,exact),nsection=belt_muscle_section_roots(proj,cuts,nc,level,section);CHECK(nexact==nsection);
   for(int i=0;i<nexact;++i)CHECK(fabs(exact[i]-section[i])<1e-8);
  }
  CHECK(fabs(b.muscle_diag[28]-.08*BELT_PI*b.radius)<1e-13);double previous=-INFINITY,sum=0;
  for(int i=0;i<(int)b.force_ledger[4];i+=2){const double* a=b.force_ledger+8+6*i;const double* z=a+6;
   double level=-sn*a[0]+cs*a[1];CHECK(level>b.muscle_diag[29]&&level<b.muscle_diag[30]);
   CHECK(fabs(-sn*(z[0]-a[0])+cs*(z[1]-a[1]))<1e-9);CHECK(fabs(-sn*a[2]+cs*a[3])<1e-10);
   if(isfinite(previous))CHECK(fabs(level-previous-b.muscle_diag[28])<1e-9);previous=level;sum+=hypot(a[2],a[3]);
  }
  CHECK(sum<=1.44*preview_config.horizontal_force*.25*8+1e-12);
  belt_body replay=b;belt_force_ledger_begin(&replay,1,0);double again[BELT_D]={0};CHECK(belt_muscle_force_at(&replay,replay.q,again,.23));
  CHECK(!memcmp(f,again,sizeof f)&&!memcmp(b.force_ledger,replay.force_ledger,sizeof b.force_ledger));
  replay.muscle_seed++;belt_force_ledger_begin(&replay,1,0);memset(again,0,sizeof again);CHECK(belt_muscle_force_at(&replay,replay.q,again,.23));CHECK(memcmp(f,again,sizeof f));
 }
 for(int row=-12;row<=12;++row){double left=belt_muscle_random_gain(1234,row,.3125-1e-8),right=belt_muscle_random_gain(1234,row,.3125+1e-8);CHECK(fabs(left-right)<1e-10&&left>=.6&&left<=1);}
 CHECK(!dango_belt_room_destroy(handle));puts("random sections: asymmetric/rotated axis, band/spacing, pair budget, seed and smooth knots PASS");return 0;
}

static int random_replay_and_arguments(void){
 void* handles[3]={0};for(int j=0;j<3;++j)CHECK(!dango_belt_room_create(&preview_config,preview_calibration,11,0,0,-5,5,&handles[j]));
 dango_belt_room_input input={0};input.body_id=1;int different=0;
 for(int i=0;i<180;++i){
  dango_belt_room_frame frame[3];
  for(int j=0;j<3;++j){CHECK(!dango_belt_room_step_muscles_pattern(handles[j],.0166666f,&input,1,i>=120&&i<165?(float)((i-120)/60.):0.f,i>=120&&i<165?1.44f:0.f,2,1,0,j<2?1234:5678));CHECK(!dango_belt_room_read_frame(handles[j],&frame[j],sizeof frame[j]));}
  CHECK(!memcmp(frame[0].curve,frame[1].curve,sizeof frame[0].curve));CHECK(!memcmp(frame[0].body_snapshot,frame[1].body_snapshot,sizeof frame[0].body_snapshot));
  different|=memcmp(frame[0].curve,frame[2].curve,sizeof frame[0].curve)!=0;
 }
 CHECK(different);belt_body* b=room_body(room_lookup(handles[0]));belt_body before=*b;room_world world_before=*room_lookup(handles[0]);
 CHECK(dango_belt_room_step_muscles_pattern(handles[0],.0166666f,&input,1,.2f,1.44f,2,NAN,0,1234)==DANGO_BELT_ROOM_ARGUMENT);
 CHECK(dango_belt_room_step_muscles_pattern(handles[0],.0166666f,&input,1,.2f,1.44f,2,0,0,1234)==DANGO_BELT_ROOM_ARGUMENT);
 CHECK(!memcmp(b,&before,sizeof before)&&!memcmp(room_lookup(handles[0]),&world_before,sizeof world_before));
 for(int j=0;j<3;++j)CHECK(!dango_belt_room_destroy(handles[j]));puts("random pattern full-step seed replay/difference and invalid-axis no-write PASS");return 0;
}

int main(void){
 CHECK(!random_force_contract());CHECK(!random_replay_and_arguments());
 CHECK(!free_body_work(2));CHECK(!fault_rollback(2));CHECK(!shake_and_release(1.44f,2));CHECK(!repeated_shakes(1.44f,2));
 CHECK(!force_contract());CHECK(!free_body_work(0));CHECK(!free_body_work(1));CHECK(!fault_rollback(0));CHECK(!fault_rollback(1));
 const float candidates[]={.72f,1.08f,1.44f};
 for(int i=0;i<3;++i){CHECK(!shake_and_release(candidates[i],0));CHECK(!repeated_shakes(candidates[i],0));}
 CHECK(!shake_and_release(1.44f,1));CHECK(!repeated_shakes(1.44f,1));
 puts("internal muscle force pairs, real deformation/contact, zero-input equality and release PASS");return 0;
}

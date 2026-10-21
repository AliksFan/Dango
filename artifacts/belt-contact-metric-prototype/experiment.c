#define main existing_test_main
#include "src/dango_belt_room_collection_tests.c"
#undef main
#include "actual-config.h"
typedef struct frozen_case {const char* path;double theta,nx,ny,depth;} frozen_case;
#include "cases.h"
static const char* trace_dir="../belt-ball-platform-diagnosis/body-state-trace/";
static int load(const char* name,room_world* w,belt_body* b){char path[512];sprintf_s(path,sizeof path,"%s%s",trace_dir,name);FILE* f=NULL;fopen_s(&f,path,"rb");if(!f)return 0;int ok=fread(w,sizeof *w,1,f)==1&&fread(b,sizeof *b,1,f)==1&&fgetc(f)==EOF;fclose(f);return ok;}
static void measure(const belt_body* b,double* bend,double* md){belt_residual r[BELT_MAX_RESIDUALS];int n=belt_residuals(b,b->q,r,md);*bend=0;if(n<0){*bend=INFINITY;return;}for(int i=1;i<2*BELT_Q;i+=2)*bend+=.5*r[i].k*r[i].value*r[i].value;}
static int frozen(void){
 printf("FROZEN variant%d cases%zu bodySize%zu worldSize%zu\n",EXPERIMENT_METRIC,sizeof cases/sizeof *cases,sizeof(belt_body),sizeof(room_world));
 for(int i=0;i<sizeof cases/sizeof *cases;++i){const frozen_case* c=&cases[i];room_world w;belt_body b;CHECK(load(c->path,&w,&b));double before,after,md0,md1;measure(&b,&before,&md0);belt_v point=belt_eval(b.q,c->theta,0);memset(b.guard,0,sizeof b.guard);memset(w.diag,0,sizeof w.diag);
  int ok=room_project(&w,&b,c->theta,(belt_v){c->nx,c->ny},c->depth);measure(&b,&after,&md1);belt_v delta=belt_sub(belt_eval(b.q,c->theta,0),point);
  printf("PROJECT %s ok%d bending%.17g -> %.17g md%.17g -> %.17g progress%.17g request%.17g\n",c->path,ok,before,after,md0,md1,delta.x*c->nx+delta.y*c->ny,c->depth+2e-9);
  CHECK(ok&&fabs(delta.x*c->nx+delta.y*c->ny-c->depth-2e-9)<1e-10);
  if(EXPERIMENT_METRIC)CHECK(after<=before*1.001);
 }
 room_world w;belt_body b;CHECK(load("state-024780-before_integrate.bin",&w,&b));double h=(double).0166666f/4,force[BELT_D]={0};force[1]=-b.mass[1]*b.cfg.gravity;force[35]=-b.mass[35]*b.cfg.gravity;int ok=belt_integrate(&b,h,force);
 printf("OLD_HIGH_ENERGY_INTEGRATE ok%d site%.0f residual%.17g\n",ok,b.solver_diag[3],b.solver_diag[7]);CHECK(!ok&&b.solver_diag[3]==5);return 0;
}
static int stage(void){
 room_world w;belt_body b;CHECK(load("state-024767-static_before.bin",&w,&b));double before,after,md0,md1;measure(&b,&before,&md0);
 /* The snapshot precedes the first position write, after its original guard
    check. Retain those counters and conservatively charge the repeated check. */
 int ok=collection_contact(&w,&b);measure(&b,&after,&md1);
 printf("FROZEN_STAGE variant%d contact%d bending%.17g -> %.17g md%.17g -> %.17g geomWork%.0f guardWork%.0f contactResidual%.17g\n",EXPERIMENT_METRIC,ok,before,after,md0,md1,w.diag[4],b.guard[4],w.diag[9]);CHECK(ok);
 belt_body actual;room_world actual_world;CHECK(load("state-024780-before_integrate.bin",&actual_world,&actual));
 int samples;double maximum,coarse,speed=belt_speed_certificate(&b,&samples,&maximum,&coarse);CHECK(belt_limit_speed(&b,&speed)&&speed<=b.cfg.maximum_speed);
 /* Continue the next actual substep prelude, including disk advance and
    calibration damping. Controls at input tick612 are all zero. */
 room_active=&w;belt_room_before_integrate(&b);room_active=NULL;
 float controls[9]={0};belt_apply_calibration_damping(&b,(double).0166666f/4,controls,false);
 double vdelta=0;for(int j=0;j<BELT_D;++j)vdelta=fmax(vdelta,fabs(actual.v[j]-b.v[j]));printf("FROZEN_STAGE velocityDifferenceToOriginalNextInput %.17g\n",vdelta);
 if(!EXPERIMENT_METRIC)CHECK(!memcmp(b.q,actual.q,sizeof b.q)&&!memcmp(b.v,actual.v,sizeof b.v));
 double h=(double).0166666f/4,force[BELT_D]={0};force[1]=-b.mass[1]*b.cfg.gravity;force[35]=-b.mass[35]*b.cfg.gravity;
 /* Diagnostic continuation only: no tick is accepted from this partial stage. */
 ok=belt_integrate(&b,h,force);printf("FROZEN_STAGE_NEXT_INTEGRATE variant%d ok%d site%.0f residual%.17g iterations%.0f\n",EXPERIMENT_METRIC,ok,b.solver_diag[3],b.solver_diag[7],b.solver_diag[5]);
 return EXPERIMENT_METRIC?!ok:ok;
}
static int properties(void){
 room_world w;belt_body b;CHECK(load("state-024767-static_before.bin",&w,&b));belt_body translated=b;translated.q[0]+=2;translated.q[34]+=2;translated.q[1]-=3;translated.q[35]-=3;
 double z[BELT_D],zt[BELT_D],k,kt;CHECK(metric_direction(&b,1.7,(belt_v){.6,.8},z,&k)&&metric_direction(&translated,1.7,(belt_v){.6,.8},zt,&kt));
 double relative=0;for(int j=0;j<BELT_D;++j)relative=fmax(relative,fabs(z[j]-zt[j])/(1+fabs(z[j])));CHECK(relative<1e-8);printf("PROPERTIES translationDirectionRelativeError %.17g\n",relative);
 const double theta=1.7,depth=.001;belt_v n={.6,.8};double before_q[BELT_D],before_v[BELT_D];memcpy(before_q,b.q,sizeof before_q);memcpy(before_v,b.v,sizeof before_v);dango_ball_state prior=w.items[1].ball;
 belt_v p0=belt_eval(b.q,theta,0);CHECK(collection_project_body_ball(&w,&b,1,theta,n,depth));belt_v dp=belt_sub(belt_eval(b.q,theta,0),p0);
 double ball_dx=w.items[1].ball.x-prior.x,ball_dy=w.items[1].ball.y-prior.y;
 double progress=(ball_dx-dp.x)*n.x+(ball_dy-dp.y)*n.y,mx=0,my=0;
 for(int j=0;j<BELT_D;j+=2){if(j==0||j==34){mx+=b.mass[j]*(b.q[j]-before_q[j]);my+=b.mass[j+1]*(b.q[j+1]-before_q[j+1]);}}
 mx+=w.items[1].definition.ball.mass*ball_dx;my+=w.items[1].definition.ball.mass*ball_dy;
 CHECK(fabs(progress-depth-2e-9)<1e-10&&hypot(mx,my)<1e-10);CHECK(!memcmp(before_v,b.v,sizeof before_v));CHECK(w.items[1].ball.vx==prior.vx&&w.items[1].ball.vy==prior.vy&&w.items[1].ball.omega==prior.omega);
 printf("PROPERTIES bodyBallProgress %.17g massWeightedTranslation %.17g %.17g velocitiesUnchanged PASS\n",progress,mx,my);
 room_world unchanged=w;belt_body ub=b;CHECK(!room_project(&w,&b,theta,n,NAN));CHECK(!memcmp(b.q,ub.q,sizeof b.q)&&!memcmp(b.v,ub.v,sizeof b.v));CHECK(!memcmp(w.items,unchanged.items,sizeof w.items));puts("PROPERTIES nonfinite refusal no kinematic writes PASS");
 return 0;
}
static int replay(void){
 dango_belt_config actual=cfg;memcpy(&actual.radial_rest_length,actual_scalars,sizeof actual_scalars);void* h=NULL;CHECK(!dango_belt_room_create(&actual,cal,11,0,0,-256,256,&h));CHECK(!dango_belt_room_set_solid_walls(h,1,0)&&!dango_belt_room_enable_collection(h,1));
 dango_belt_room_input in={.body_id=1};for(int i=0;i<180;++i)CHECK(!dango_belt_room_step(h,.0166666f,&in,1));room_world* w=room_lookup(h);belt_body* b=room_body(w);
 CHECK(!dango_belt_room_set_bounds(h,(float)-16.666666666666668,(float)12.666666666666666,w->revision));dango_belt_room_definition p={.kind=1,.x=6.827849252112726,.y=.8933278319903329,.width=272.0/72,.height=21.0/72,.friction=.72};CHECK(!add(h,p)&&!add(h,ball(1.1999147569060984,3.183787110963082,66.0/72)));
 int rc=0,t,top=0;double maxbend=0,maxwork=0;for(t=0;t<1000;++t){in.controls[0]=t<600?1.f:0;in.controls[2]=t>=60&&(t-60)%65==0?1.f:0;rc=dango_belt_room_step(h,.0166666f,&in,1);double bend,md;measure(b,&bend,&md);maxbend=fmax(maxbend,bend);maxwork=fmax(maxwork,w->diag[4]);
  if(t>=605&&t<=615)printf("TICK %d rc%d bend%.17g md%.17g elasticWork%.17g residual%.17g solveIterations%.0f\n",t,rc,bend,md,w->diag[12],b->solver_diag[7],b->stats[56]);
  if(rc){printf("REPLAY_FAIL variant%d tick%d rc%d site%.0f residual%.17g bend%.17g work%.0f\n",EXPERIMENT_METRIC,t,rc,b->solver_diag[3],b->solver_diag[7],bend,w->diag[4]);break;}
  if(w->items[1].surface_contact.touching&&w->items[1].surface_contact.object_id==1&&w->items[1].surface_contact.ny>=.7){if(!top)printf("FIRST_TOP %d\n",t);top++;}
 }
 printf("REPLAY_END variant%d ticks%d rc%d top%d maxBend%.17g maxGeomWork%.0f\n",EXPERIMENT_METRIC,t,rc,top,maxbend,maxwork);CHECK(!dango_belt_room_destroy(h));return EXPERIMENT_METRIC?(rc||!top):!(rc&&t==612);
}
int main(int argc,char** argv){setvbuf(stdout,NULL,_IONBF,0);if(argc<2)return 2;if(!strcmp(argv[1],"frozen"))return frozen();if(!strcmp(argv[1],"stage"))return stage();if(!strcmp(argv[1],"properties"))return properties();if(!strcmp(argv[1],"replay"))return replay();if(!strcmp(argv[1],"contracts"))return existing_test_main(1,argv);return existing_test_main(argc,argv);}

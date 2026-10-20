#define DANGO_BELT_ROOM_COLLECTION_TESTS 1
#define DANGO_BELT_ROOM_TRACE 1
#include "dango_belt_room.c"
#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#endif
#include <stdio.h>
#define CHECK(c) do {if(!(c)){fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#c);return 1;}}while(0)
static const dango_belt_config cfg={
 .node_count=16,.substep_count=4,.radial_rest_length=.68f,.radial_minimum_ratio=.65f,.radial_maximum_ratio=1.22f,
 .perimeter_minimum_ratio=.73f,.perimeter_maximum_ratio=1.07f,.radial_hertz=1.82f,.perimeter_hertz=15.075f,
 .radial_damping=1.85f,.perimeter_damping=1.85f,.area_strength=4.8f,.local_angle_balance_strength=1.7f,
 .gravity=9.8f,.core_mass=.12f,.node_mass=.015f,.drop_height=.42f,.ground_height=-.72f,.node_radius=.045f,
 .friction=.72f,.drag_stiffness=10,.drag_damping=2.2f,.drag_maximum_force=8,.horizontal_force=2.4f,
 .maximum_horizontal_speed=3.2f,.air_control_ratio=.35f,.downward_force=2.2f,.jump_impulse=.42f,.jump_cooldown_seconds=.45f,
 .maximum_speed=12,.minimum_area=.42f,.linear_damping=1.8f};
static const float cal[11]={1.4f,1,1,4,1.4f,1,1,1,1.8f,35,60};
static int create(float x,void** handle){int rc=dango_belt_room_create(&cfg,cal,11,x,0,-20,20,handle);if(!rc)rc=dango_belt_room_set_solid_walls(*handle,1,0);if(!rc)rc=dango_belt_room_enable_collection(*handle,1);return rc;}
static dango_belt_room_definition platform(double x,double top,double width){return (dango_belt_room_definition){.kind=1,.x=x,.y=top-.1,.width=width,.height=.2,.friction=.72};}
static dango_belt_room_definition ball(double x,double y,double r){return (dango_belt_room_definition){.kind=2,.ball={x,y,r,.02,.45,.35,.25,.15}};}
static int add(void* h,dango_belt_room_definition d){room_world* w=room_lookup(h);dango_belt_room_collection_probe p;dango_belt_room_collection_token t;int rc=dango_belt_room_prepare_furniture_change(h,1,0,w->revision,&d,&p,sizeof p,&t);if(rc||p.rejection){fprintf(stderr,"add rc%d rejection%d gap%.9g\n",rc,p.rejection,p.minimum_gap);return 1;}return dango_belt_room_apply_prepared_furniture_change(h,&t);}
static int step(void* h,int count){dango_belt_room_input input={.body_id=1};for(int i=0;i<count;++i){int rc=dango_belt_room_step(h,.0166666f,&input,1);if(rc){room_world* w=room_lookup(h);fprintf(stderr,"step fault%d tick%llu geom%.0f residual%.9g KE%.9g/%.9g reason%.0f\n",rc,(unsigned long long)w->tick,w->diag[4],w->diag[9],w->diag[10],w->diag[11],w->diag[7]);return rc;}}return 0;}
static int api(void){
 void* h=NULL;CHECK(!create(-8,&h));room_world* w=room_lookup(h);CHECK(sizeof(dango_belt_room_collection_frame)==16968&&dango_belt_room_capabilities()==255);
 dango_belt_room_definition d=platform(3,2,1);dango_belt_room_collection_probe p;dango_belt_room_collection_token t;
 CHECK(!dango_belt_room_prepare_furniture_change(h,1,0,w->revision,&d,&p,sizeof p,&t)&&!p.rejection);room_world before=*w;belt_body body=*room_body(w);
 CHECK(step(h,1)==DANGO_BELT_ROOM_BUSY&&!memcmp(&before,w,sizeof before)&&!memcmp(&body,room_body(w),sizeof body));
 dango_belt_room_collection_token wrong=t;wrong.config_revision++;CHECK(dango_belt_room_apply_prepared_furniture_change(h,&wrong)==DANGO_BELT_ROOM_REVISION&&!memcmp(&before,w,sizeof before));
 CHECK(!dango_belt_room_cancel_prepared_furniture_change(h,&t)&&!dango_belt_room_cancel_prepared_furniture_change(h,&t));CHECK(!w->collection_count&&w->tick==0&&w->revision==2);
 CHECK(!add(h,d));CHECK(w->collection_count==1&&w->revision==3&&w->last_applied_token!=0);CHECK(!add(h,ball(5,3,.4)));
 dango_belt_room_collection_frame f;memset(&f,0xa5,sizeof f);dango_belt_room_collection_frame copy=f;CHECK(dango_belt_room_read_collection_frame(h,&f,sizeof f-1)==DANGO_BELT_ROOM_ARGUMENT&&!memcmp(&f,&copy,sizeof f));
 CHECK(!dango_belt_room_read_collection_frame(h,&f,sizeof f)&&f.item_count==2&&f.items[1].ball.scene_revision==f.base.scene_revision&&f.items[1].body_contact.step==f.base.tick);
 CHECK(dango_belt_room_set_platform(h,NULL,w->revision)==DANGO_BELT_ROOM_REJECTED);CHECK(!dango_belt_room_destroy(h));puts("collection ABI/token/busy/no-write PASS");return 0;
}
static int empty_equivalence(void){void *a=NULL,*b=NULL;CHECK(!dango_belt_room_create(&cfg,cal,11,0,0,-20,20,&a));CHECK(!dango_belt_room_create(&cfg,cal,11,0,0,-20,20,&b));CHECK(!dango_belt_room_enable_collection(b,0));
 for(int i=0;i<240;++i){CHECK(!step(a,1)&&!step(b,1));belt_body* ba=room_body(room_lookup(a));belt_body* bb=room_body(room_lookup(b));CHECK(!memcmp(ba,bb,sizeof *ba));}
 CHECK(!dango_belt_room_destroy(a)&&!dango_belt_room_destroy(b));puts("empty collection exact private body equivalence 240 ticks PASS");return 0;}
static int surface(void){void* h=NULL;CHECK(!create(-8,&h));CHECK(!add(h,platform(4,.2,2)));CHECK(!add(h,platform(-4,.4,2)));CHECK(!add(h,ball(4,3,.4)));room_world* w=room_lookup(h);
 CHECK(!step(h,480));dango_belt_room_collection_item* a=&w->items[2];CHECK(a->ball.integrations==1920&&a->ball.y>=.6-ROOM_PENETRATION&&a->ball.y<.602);CHECK(w->items[0].definition.x==4&&w->items[1].definition.x==-4);
 dango_belt_room_collection_probe probe;dango_belt_room_collection_token token;dango_ball_state state=a->ball;CHECK(!dango_belt_room_prepare_furniture_change(h,3,w->items[0].instance,w->revision,NULL,&probe,sizeof probe,&token)&&!probe.rejection);CHECK(!dango_belt_room_apply_prepared_furniture_change(h,&token));CHECK(!memcmp(&state,&a->ball,sizeof state)&&!a->surface_contact.touching&&!a->surface_contact.normal_impulse);CHECK(!step(h,30)&&a->ball.y<.5);
 CHECK(!dango_belt_room_destroy(h));puts("multi-platform physical landing, immediate stale-contact invalidation and support removal fall PASS");return 0;}
static int pair(void){void* h=NULL;CHECK(!create(-8,&h));CHECK(!add(h,ball(2,-.32,.4)));CHECK(!add(h,ball(4,-.32,.4)));room_world* w=room_lookup(h);w->items[0].ball.vx=2;double max_v=0;
 for(int i=0;i<180;++i){CHECK(!step(h,1));max_v=fmax(max_v,w->items[1].ball.vx);}CHECK(max_v>.25);CHECK(!dango_belt_room_destroy(h));printf("ball pair real momentum transfer max_v=%.9g PASS\n",max_v);return 0;}
static int thirty(void){void* h=NULL;CHECK(!create(-18,&h));for(int row=0;row<2;++row)for(int i=0;i<15;++i)CHECK(!add(h,ball(-14+1.9*i,2+2.05*row,66.0/72)));room_world* w=room_lookup(h);CHECK(w->collection_count==30);CHECK(!step(h,240));for(int i=0;i<30;++i)CHECK(w->items[i].ball.integrations==960);CHECK(!dango_belt_room_destroy(h));puts("30 actual-product disks simultaneous two-row drop 240 ticks PASS");return 0;}
static int body_platform(void){void* h=NULL;CHECK(!dango_belt_room_create(&cfg,cal,11,0,1.5f,-20,20,&h));CHECK(!dango_belt_room_set_solid_walls(h,1,0)&&!dango_belt_room_enable_collection(h,1));
 dango_belt_room_definition p={.kind=1,.x=0,.y=.4-21.0/144,.width=272.0/72,.height=21.0/72,.friction=.72};CHECK(!add(h,p));p.x=6;CHECK(!add(h,p));p.x=-6;CHECK(!add(h,p));CHECK(!step(h,360));room_world* w=room_lookup(h);belt_body* b=room_body(w);double bounds[4],foot;belt_bounds(b->q,bounds,&foot);CHECK(b->grounded&&w->diag[18]==1&&bounds[1]>=.4-ROOM_PENETRATION);
 dango_belt_room_collection_probe probe;dango_belt_room_collection_token token;CHECK(!dango_belt_room_prepare_furniture_change(h,3,1,w->revision,NULL,&probe,sizeof probe,&token)&&!probe.rejection);CHECK(!dango_belt_room_apply_prepared_furniture_change(h,&token)&&!b->grounded&&!w->diag[18]);CHECK(!step(h,180));belt_bounds(b->q,bounds,&foot);CHECK(bounds[1]<-.6&&b->grounded);CHECK(!dango_belt_room_destroy(h));puts("actual-size collection platform body landing/support/delete/floor fall PASS");return 0;}
static int ball_walls(void){void* h=NULL;CHECK(!create(-8,&h));CHECK(!add(h,ball(19,-.32,.4)));room_world* w=room_lookup(h);w->items[0].ball.vx=3;double minimum=0;
 for(int i=0;i<180;++i){CHECK(!step(h,1));minimum=fmin(minimum,w->items[0].ball.vx);CHECK(w->items[0].ball.x+.4<=20);}CHECK(minimum<-.1);CHECK(!dango_belt_room_destroy(h));
 CHECK(!create(-8,&h));w=room_lookup(h);CHECK(!dango_belt_room_set_solid_walls(h,0,w->revision));CHECK(!add(h,ball(19,-.32,.4)));w->items[0].ball.vx=3;int rc=0;dango_belt_room_collection_item before[30];belt_body body;
 for(int i=0;i<180;++i){memcpy(before,w->items,sizeof before);body=*room_body(w);rc=step(h,1);if(rc)break;}CHECK(rc==DANGO_BELT_ROOM_REJECTED&&!memcmp(before,w->items,sizeof before)&&!memcmp(&body,room_body(w),sizeof body));CHECK(!dango_belt_room_destroy(h));puts("disk solid wall bounce and guard-only full atomic refusal PASS");return 0;}
static int body_ball(double radius,int ticks){void* h=NULL;CHECK(!create(0,&h));CHECK(!step(h,240));CHECK(!add(h,ball(0,3,radius)));room_world* w=room_lookup(h);belt_body* b=room_body(w);double max_impulse=0,max_body_change=0;
 for(int i=0;i<ticks;++i){double vy=b->v[35];int rc=step(h,1);if(rc){fprintf(stderr,"circle failure: center %.17g %.17g radius %.17g theta %.17g half %.17g time %.17g %.17g depths %d %d\n",collection_failure_ball.ball.x,collection_failure_ball.ball.y,collection_failure_radius,collection_failure_cell.center,collection_failure_cell.half,collection_failure_cell.lo,collection_failure_cell.hi,collection_failure_cell.theta_depth,collection_failure_cell.time_depth);
   FILE* evidence=NULL;
#ifdef _WIN32
   fopen_s(&evidence,"artifacts/belt-room-f7-collection-r3/body-ball-current-failure.bin","wb");
#else
   evidence=fopen("artifacts/belt-room-f7-collection-r3/body-ball-current-failure.bin","wb");
#endif
   if(evidence){fwrite(&collection_failure_body,sizeof collection_failure_body,1,evidence);fwrite(&collection_failure_ball,sizeof collection_failure_ball,1,evidence);fwrite(&collection_failure_cell,sizeof collection_failure_cell,1,evidence);fwrite(collection_failure_from,sizeof collection_failure_from,1,evidence);fwrite(collection_failure_to,sizeof collection_failure_to,1,evidence);fclose(evidence);}
   double theta,box[4];belt_v nn;double gap=collection_circle_closest(collection_failure_from,(belt_v){0,0},collection_failure_radius,&theta,&nn);room_cell_bounds(collection_failure_from,collection_failure_to,&collection_failure_cell,box);fprintf(stderr,"actual min gap %.17g at %.17g cell lower distance %.17g threshold %.17g\n",gap,theta,hypot(fmax(0,fmax(box[0],-box[2])),fmax(0,fmax(box[1],-box[3]))),collection_failure_radius-ROOM_PENETRATION);return 1;}
  max_impulse=fmax(max_impulse,w->items[0].body_contact.normal_impulse);if(w->items[0].body_contact.touching)max_body_change=fmax(max_body_change,fabs(b->v[35]-vy));}
 CHECK(max_impulse>1e-4&&max_body_change>1e-4);CHECK(!dango_belt_room_destroy(h));printf("Fourier/disk radius %.9g %d ticks double-sided impulse %.9g coreDeltaVy %.9g PASS\n",radius,ticks,max_impulse,max_body_change);return 0;}
static int geometry(void){void* h=NULL;CHECK(!create(0,&h));room_world* w=room_lookup(h);belt_body* b=room_body(w);double theta=0;belt_v on=belt_eval(b->q,1.2,0);room_world proof=*w;memset(proof.diag,0,sizeof proof.diag);
 CHECK(collection_circle_certificate(&proof,b->q,b->q,on,on,.2,&theta,0)==0);proof=*w;memset(proof.diag,0,sizeof proof.diag);
 belt_v left={b->q[34]-3,b->q[35]},right={b->q[34]+3,b->q[35]};CHECK(collection_circle_certificate(&proof,b->q,b->q,left,right,.2,&theta,1)==0);proof=*w;CHECK(!collection_center_outside(&proof,b->q,(belt_v){b->q[34],b->q[35]}));
 uint32_t random=12345;for(int i=0;i<200;++i){double a[BELT_D],c[BELT_D];memcpy(a,b->q,sizeof a);memcpy(c,b->q,sizeof c);random=random*1664525u+1013904223u;double angle=(double)(random&65535)/65535*2*BELT_PI;random=random*1664525u+1013904223u;double half=.000001+(double)(random&65535)/65535*.15;
  a[0]-=.9*cos(angle);a[1]-=.9*sin(angle);c[0]-=1.1*cos(angle+.1);c[1]-=1.1*sin(angle+.1);room_cell cell={angle,half,.1,.9,0,0};double lower=collection_radial_lower(a,c,&cell);
  for(int k=0;k<=32;++k)for(int t=0;t<=8;++t){double q[BELT_D],time=.1+.8*t/8;for(int j=0;j<BELT_D;++j)q[j]=(1-time)*a[j]+time*c[j];belt_v p=belt_eval(q,angle-half+2*half*k/32,0);CHECK(lower<=belt_dot(p,p)+1e-13);}}
 CHECK(!dango_belt_room_destroy(h));puts("Fourier radial outward-bound samples, intrusion, swept tunnel and containment rejection PASS");return 0;}
static int contact_reports(void){void* h=NULL;CHECK(!create(-8,&h));CHECK(!add(h,ball(2,-.32,.4))&&!add(h,ball(2.8,-.32,.4)));room_world* w=room_lookup(h);belt_body* b=room_body(w);w->items[0].ball.vx=1;w->items[1].ball.vx=0;w->surface_report_weight[0]=w->surface_report_weight[1]=-1;
 collection_row rows[2];int n=0;CHECK(collection_add_row(w,b,rows,&n,0,1,0,(belt_v){1,0},0,.35,.15,0,-4,0));rows[0].body.pn=.01;collection_contact_record(w,b,&rows[0]);
 CHECK(w->items[0].surface_contact.object_id==2&&w->items[1].surface_contact.object_id==1&&w->items[0].surface_contact.nx==-1&&w->items[1].surface_contact.nx==1);
 CHECK(w->items[0].surface_contact.ball_speed_before==1&&w->items[1].surface_contact.ball_speed_before==0&&w->items[0].surface_contact.normal_speed_before==-1&&fabs(w->items[0].surface_contact.gap)<1e-12);
 CHECK(collection_add_row(w,b,rows,&n,0,1,-1,(belt_v){0,1},0,.45,.25,0,0,1));rows[1].body.pn=.001;collection_contact_record(w,b,&rows[1]);CHECK(w->items[1].surface_contact.object_id==1&&w->items[1].surface_contact.nx==1&&w->items[1].surface_contact.normal_speed_before==-1);
 rows[1].body.pn=.02;collection_contact_record(w,b,&rows[1]);CHECK(w->items[1].surface_contact.object_id==0&&w->items[1].surface_contact.nx==0&&w->items[1].surface_contact.ny==1&&w->items[1].surface_contact.normal_speed_before==0&&w->items[1].surface_contact.ball_speed_before==0);
 CHECK(!dango_belt_room_destroy(h));puts("pair reports both real IDs/opposite normals/own before-speed/gap and coherent floor+pair representative PASS");return 0;}
static int rollback(void){void* h=NULL;CHECK(!create(-8,&h));CHECK(!add(h,ball(4,3,.4)));CHECK(!step(h,20));room_world* w=room_lookup(h);room_world before=*w;belt_body body=*room_body(w);collection_test_fault=1;CHECK(step(h,1)==DANGO_BELT_ROOM_REJECTED);CHECK(!memcmp(&body,room_body(w),sizeof body));
 CHECK(!memcmp(before.items,w->items,sizeof before.items)&&!memcmp(before.touches,w->touches,sizeof before.touches)&&!memcmp(before.pair_touch,w->pair_touch,sizeof before.pair_touch)&&before.tick==w->tick&&before.revision==w->revision);CHECK(step(h,1)==DANGO_BELT_ROOM_FAULT);CHECK(!dango_belt_room_destroy(h));puts("fourth-substep whole world rollback/fault latch PASS");return 0;}
#ifdef _WIN32
static int actual_dll(const char* path,int saved){HMODULE module=LoadLibraryA(path);CHECK(module);
 int (*native_create)(const dango_belt_config*,const float*,int,float,float,float,float,void**)=NULL;
 int (*native_set_solid_walls)(void*,int,uint64_t)=NULL;int (*native_enable_collection)(void*,uint64_t)=NULL;
 int (*native_step)(void*,float,const dango_belt_room_input*,int)=NULL;int (*native_read_collection_frame)(void*,dango_belt_room_collection_frame*,int)=NULL;
 int (*native_prepare_furniture_change)(void*,int,uint64_t,uint64_t,const dango_belt_room_definition*,dango_belt_room_collection_probe*,int,dango_belt_room_collection_token*)=NULL;
 int (*native_apply_prepared_furniture_change)(void*,const dango_belt_room_collection_token*)=NULL;int (*native_destroy)(void*)=NULL;
#define LOAD(name) do {FARPROC proc=GetProcAddress(module,"dango_belt_room_" #name);CHECK(proc);memcpy(&native_##name,&proc,sizeof proc);}while(0)
 LOAD(create);LOAD(set_solid_walls);LOAD(enable_collection);LOAD(step);LOAD(read_collection_frame);LOAD(prepare_furniture_change);LOAD(apply_prepared_furniture_change);LOAD(destroy);
#undef LOAD
 if(saved){const float starts[3]={6,5,5};const int jumps[3]={20,90,120};for(int c=0;c<3;++c){void* h=NULL;CHECK(!native_create(&cfg,cal,11,starts[c],0,-15,15,&h));CHECK(!native_set_solid_walls(h,1,0)&&!native_enable_collection(h,1));dango_belt_room_input input={.body_id=1};
  for(int i=0;i<180;++i)CHECK(!native_step(h,.0166666f,&input,1));dango_belt_room_definition p={.kind=1,.x=(1715.6051461521163-1080)/72,.y=(126.65960390330395-10.5)/72,.width=272.0/72,.height=21.0/72,.friction=.72};dango_belt_room_collection_probe probe;dango_belt_room_collection_token token;CHECK(!native_prepare_furniture_change(h,1,0,2,&p,&probe,sizeof probe,&token)&&!probe.rejection);CHECK(!native_apply_prepared_furniture_change(h,&token));
  for(int i=0;i<600;++i){input.controls[0]=i<jumps[c]+45?1.f:0;input.controls[2]=i==jumps[c]?1.f:0;CHECK(!native_step(h,.0166666f,&input,1));dango_belt_room_collection_frame f;CHECK(!native_read_collection_frame(h,&f,sizeof f));CHECK(f.base.tick==(uint64_t)i+181&&!f.base.fault&&f.base.scene_revision==3&&f.item_count==1&&f.items[0].instance==1);}
  CHECK(!native_destroy(h));printf("actual DLL saved x%.0f jump%d 600 successful coherent frames PASS\n",starts[c],jumps[c]);}FreeLibrary(module);return 0;}
 void* h=NULL;CHECK(!native_create(&cfg,cal,11,0,0,-20,20,&h));CHECK(!native_set_solid_walls(h,1,0)&&!native_enable_collection(h,1));dango_belt_room_input input={.body_id=1};
 for(int i=0;i<240;++i)CHECK(!native_step(h,.0166666f,&input,1));dango_belt_room_definition d=ball(0,3,66.0/72);dango_belt_room_collection_probe probe;dango_belt_room_collection_token token;CHECK(!native_prepare_furniture_change(h,1,0,2,&d,&probe,sizeof probe,&token)&&!probe.rejection);CHECK(!native_apply_prepared_furniture_change(h,&token));
 double maximum=0;for(int i=0;i<3600;++i){CHECK(!native_step(h,.0166666f,&input,1));dango_belt_room_collection_frame f;CHECK(!native_read_collection_frame(h,&f,sizeof f));CHECK(f.base.tick==(uint64_t)i+241&&f.base.scene_revision==3&&f.item_count==1&&!f.base.fault);CHECK(f.items[0].ball.step==f.base.tick&&f.items[0].body_contact.step==f.base.tick&&f.items[0].surface_contact.scene_revision==f.base.scene_revision);maximum=fmax(maximum,f.items[0].body_contact.normal_impulse);}
 CHECK(maximum>1e-4&&!native_destroy(h));FreeLibrary(module);printf("actual DLL default disk 3600 ticks coherent successful frames/real body impulse %.9g PASS\n",maximum);return 0;}
#endif
static int saved_platform_case(float x,int jump){
 void* h=NULL;CHECK(!dango_belt_room_create(&cfg,cal,11,x,0,-15,15,&h));
 CHECK(!dango_belt_room_set_solid_walls(h,1,0)&&!dango_belt_room_enable_collection(h,1));CHECK(!step(h,180));
 dango_belt_room_definition p={.kind=1,.x=(1715.6051461521163-1080)/72,.y=(126.65960390330395-10.5)/72,.width=272.0/72,.height=21.0/72,.friction=.72};CHECK(!add(h,p));
 room_world* w=room_lookup(h);int tick,rc=0;
 for(tick=0;tick<600;++tick){dango_belt_room_input input={.body_id=1};input.controls[0]=tick<jump+45?1.f:0;input.controls[2]=tick==jump?1.f:0;rc=dango_belt_room_step(h,.0166666f,&input,1);if(rc)break;
  room_world proof=*w;proof.platform=collection_platform(&w->items[0]);memset(proof.diag,0,sizeof proof.diag);double theta=0;CHECK(room_certificate(&proof,room_body(w)->q,room_body(w)->q,&theta,0)==1&&room_containment(&proof,room_body(w)->q));}
 if(rc){fprintf(stderr,"refusal line=%d contact=",room_refusal_line);for(int j=0;j<9;++j)fprintf(stderr," %.17g",room_trace_contact[j]);fprintf(stderr,"\n");fprintf(stderr,"saved approach tick=%d accepted=%llu rc=%d body=%.0f reason=%.0f work=%.0f cell=%.17g %.17g %.17g %.17g %.0f %.0f\n",tick,(unsigned long long)w->tick,rc,w->diag[3],w->diag[7],w->diag[4],room_trace_cell[0],room_trace_cell[1],room_trace_cell[2],room_trace_cell[3],room_trace_cell[4],room_trace_cell[5]);
  for(int endpoint=0;endpoint<2;++endpoint){const double* q=endpoint?room_trace_to:room_trace_from;fprintf(stderr,"%s={",endpoint?"to":"from");for(int j=0;j<BELT_D;++j)fprintf(stderr,"%s%.17g",j?",":"",q[j]);fprintf(stderr,"}\n");}}
 CHECK(!rc&&tick==600);CHECK(!dango_belt_room_destroy(h));printf("saved x%.0f jump%d platform approach 600 ticks PASS\n",x,jump);return 0;
}
int main(int argc,char** argv){
 if(argc>1&&!strcmp(argv[1],"--saved-platform"))return saved_platform_case(6,20)||saved_platform_case(5,90)||saved_platform_case(5,120);
#ifdef _WIN32
 if(argc>2&&!strcmp(argv[1],"--dll"))return actual_dll(argv[2],0);
 if(argc>2&&!strcmp(argv[1],"--saved-platform-dll"))return actual_dll(argv[2],1);
#endif
 if(argc>1&&!strcmp(argv[1],"--reports"))return contact_reports();if(argc>1&&!strcmp(argv[1],"--body"))return body_ball(.4,240);if(argc>1&&!strcmp(argv[1],"--long"))return body_ball(66.0/72,3600);if(argc>1&&!strcmp(argv[1],"--thirty"))return thirty();if(argc>1&&!strcmp(argv[1],"--platform"))return body_platform();if(argc>1&&!strcmp(argv[1],"--rollback"))return rollback();if(api()||empty_equivalence()||geometry()||contact_reports()||surface()||pair()||ball_walls()||body_platform()||body_ball(.4,240)||rollback())return 1;puts("R3 targeted native contracts PASS");return 0;}

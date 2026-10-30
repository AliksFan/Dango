#include "dango_belt_room.c"
#include <stdio.h>

#define CHECK(c) do { if (!(c)) { fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#c); return 1; } } while (0)
static const dango_belt_config wall_config={
 .node_count=16,.substep_count=4,.radial_rest_length=.68f,.radial_minimum_ratio=.65f,.radial_maximum_ratio=1.22f,
 .perimeter_minimum_ratio=.73f,.perimeter_maximum_ratio=1.07f,.radial_hertz=1.82f,.perimeter_hertz=15.075f,
 .radial_damping=1.85f,.perimeter_damping=1.85f,.area_strength=4.8f,.local_angle_balance_strength=1.7f,
 .gravity=9.8f,.core_mass=.12f,.node_mass=.015f,.drop_height=.42f,.ground_height=-.72f,.node_radius=.045f,
 .friction=.72f,.drag_stiffness=10,.drag_damping=2.2f,.drag_maximum_force=8,.horizontal_force=2.4f,
 .maximum_horizontal_speed=3.2f,.air_control_ratio=.35f,.downward_force=2.2f,.jump_impulse=.42f,.jump_cooldown_seconds=.45f,
 .maximum_speed=12,.minimum_area=.42f,.linear_damping=1.8f};
static const float wall_calibration[11]={1.4f,1,1,4,1.4f,1,1,1,1.8f,35,60};
static int wall_create(float x,float y,float left,float right,void** h){
 return dango_belt_room_create(&wall_config,wall_calibration,11,x,y,left,right,h);
}
static FILE* wall_log(const char* name){
 char path[256];FILE* file=NULL;
 snprintf(path,sizeof path,"artifacts/belt-room-f7-static-r2/walls/%s.csv",name);
#ifdef _WIN32
 fopen_s(&file,path,"wb");
#else
 file=fopen(path,"wb");
#endif
 if(file)fprintf(file,"tick,rc,core_x,core_y,left_gap,right_gap,wall_rows,floor_rows,platform_rows,normal_impulse,grounded,support\n");
 return file;
}
/* Rebuild the actual continuous-extremum manifold. Samples rendered in the
   frame are deliberately not used as a contact or containment oracle. */
static int wall_observe(room_world* w,int* walls,int* floors,int* platforms,double* bounds){
 room_world probe=*w;room_row rows[512];belt_body* b=room_body(w);double foot;
 belt_bounds(b->q,bounds,&foot);int n=room_rows(&probe,b,rows);if(n<0)return n;
 *walls=*floors=*platforms=0;
 for(int i=0;i<n;++i){if(rows[i].platform==2)(*walls)++;else if(rows[i].platform==1)(*platforms)++;else (*floors)++;}
 return 0;
}
static void wall_record(FILE* file,room_world* w,int rc,int walls,int floors,int platforms,const double* bounds){
 belt_body* b=room_body(w);
 if(file)fprintf(file,"%llu,%d,%.17g,%.17g,%.17g,%.17g,%d,%d,%d,%.17g,%d,%.0f\n",
  (unsigned long long)w->tick,rc,b->q[34],b->q[35],bounds[0]-b->left,b->right-bounds[2],walls,floors,platforms,w->diag[20],b->grounded,w->diag[18]);
}
static int wall_same_physics(const dango_belt_room_frame* a,const dango_belt_room_frame* b){
 return !memcmp(a->body_snapshot,b->body_snapshot,sizeof a->body_snapshot)&&!memcmp(a->scaffold,b->scaffold,sizeof a->scaffold)&&
  !memcmp(a->curve,b->curve,sizeof a->curve)&&!memcmp(a->stats,b->stats,sizeof a->stats)&&!memcmp(&a->platform,&b->platform,sizeof a->platform);
}
static int wall_atomic(void){
 void* h=NULL;CHECK(!wall_create(0,1.5f,-5,5,&h));room_world* w=room_lookup(h);belt_body* b=room_body(w);
 CHECK((dango_belt_room_capabilities()&63)==63&&sizeof(dango_belt_room_frame)==4472);
 dango_belt_room_frame frame;CHECK(!dango_belt_room_read_frame(h,&frame,sizeof frame)&&frame.diagnostics[23]==0);
 belt_body body=*b;room_world world=*w;
 CHECK(dango_belt_room_set_solid_walls(h,2,0)==-700);CHECK(!memcmp(&body,b,sizeof body)&&!memcmp(&world,w,sizeof world));
 CHECK(dango_belt_room_set_solid_walls(h,1,1)==-708);CHECK(!memcmp(&body,b,sizeof body)&&!memcmp(&world,w,sizeof world));
 CHECK(!dango_belt_room_set_solid_walls(h,0,0));CHECK(!memcmp(&body,b,sizeof body)&&!memcmp(&world,w,sizeof world));
 CHECK(!dango_belt_room_set_solid_walls(h,1,0));CHECK(w->revision==1&&w->tick==0&&!memcmp(&body,b,sizeof body));
 CHECK(!dango_belt_room_read_frame(h,&frame,sizeof frame)&&frame.diagnostics[23]==1);
 world=*w;CHECK(!dango_belt_room_set_solid_walls(h,1,1));CHECK(!memcmp(&world,w,sizeof world));
 CHECK(!dango_belt_room_set_bounds(h,-4,4,1));CHECK(w->revision==2&&w->tick==0);body=*b;world=*w;
 const float bad[][2]={{NAN,4},{-4,INFINITY},{4,-4},{-.1f,.1f},{.5f,4},{-4,-.5f}};
 for(int i=0;i<6;++i){CHECK(dango_belt_room_set_bounds(h,bad[i][0],bad[i][1],2)<0);CHECK(!memcmp(&body,b,sizeof body)&&!memcmp(&world,w,sizeof world));}
 CHECK(dango_belt_room_set_bounds(h,-5,5,1)==-708);CHECK(!memcmp(&body,b,sizeof body)&&!memcmp(&world,w,sizeof world));
 dango_belt_room_platform p={1,3,.4,1,.2,.72};CHECK(!dango_belt_room_set_platform(h,&p,2));body=*b;world=*w;
 CHECK(dango_belt_room_set_bounds(h,-3,3,3)==-705);CHECK(!memcmp(&body,b,sizeof body)&&!memcmp(&world,w,sizeof world));
 CHECK(!dango_belt_room_set_solid_walls(h,0,3));CHECK(w->revision==4&&w->tick==0);
 /* Corrupt only the fixture's guard to exercise enable's current-envelope
    validation independently of set_bounds, which already prevents this. */
 b->left=.5;body=*b;world=*w;CHECK(dango_belt_room_set_solid_walls(h,1,4)==-705);CHECK(!memcmp(&body,b,sizeof body)&&!memcmp(&world,w,sizeof world));
 b->left=-4;w->platform.x=5;body=*b;world=*w;
 CHECK(dango_belt_room_set_solid_walls(h,1,4)==-705);CHECK(!memcmp(&body,b,sizeof body)&&!memcmp(&world,w,sizeof world));
 w->platform.x=3;w->revision=UINT64_MAX;body=*b;world=*w;
 CHECK(dango_belt_room_set_solid_walls(h,1,UINT64_MAX)==-708);CHECK(!memcmp(&body,b,sizeof body)&&!memcmp(&world,w,sizeof world));
 w->fault=-705;world=*w;CHECK(dango_belt_room_set_solid_walls(h,1,UINT64_MAX)==-703);CHECK(!memcmp(&body,b,sizeof body)&&!memcmp(&world,w,sizeof world));
 CHECK(!dango_belt_room_destroy(h));CHECK(dango_belt_room_set_solid_walls(h,1,0)==-701);
 printf("wall mode/bounds revision, ABI, no-op and complete no-write contract PASS\n");return 0;
}
static int wall_guard_rollback(void){
 void* h=NULL;CHECK(!wall_create(0,0,-2.5f,2.5f,&h));room_world* w=room_lookup(h);belt_body* b=room_body(w);
 dango_belt_room_input input={.body_id=1};input.controls[0]=1;int rc=0,tick;dango_belt_room_frame before,after;belt_body body;
 for(tick=0;tick<600;++tick){body=*b;CHECK(!dango_belt_room_read_frame(h,&before,sizeof before));rc=dango_belt_room_step(h,.0166666f,&input,1);if(rc)break;}
 CHECK(rc==-705&&tick<600);CHECK(!memcmp(&body,b,sizeof body));CHECK(!dango_belt_room_read_frame(h,&after,sizeof after));
 CHECK(wall_same_physics(&before,&after)&&before.tick==after.tick&&before.scene_revision==after.scene_revision&&after.fault==rc&&after.diagnostics[23]==0);
 CHECK(dango_belt_room_step(h,.0166666f,&input,1)==-703);CHECK(!dango_belt_room_destroy(h));
 printf("default guard real driven refusal tick=%d, complete body/frame rollback and latch PASS\n",tick);
 CHECK(!wall_create(0,0,-2.5f,2.5f,&h));CHECK(!dango_belt_room_set_solid_walls(h,1,0));w=room_lookup(h);b=room_body(w);
 /* Deliberately impossible private fixture exercises the real refusal path;
    the public bounds setter must reject this layout before writing it. */
 b->left=-.001;b->right=.001;body=*b;CHECK(!dango_belt_room_read_frame(h,&before,sizeof before));
 CHECK(dango_belt_room_step(h,.0166666f,&input,1)==-705);CHECK(!memcmp(&body,b,sizeof body));
 CHECK(!dango_belt_room_read_frame(h,&after,sizeof after));CHECK(wall_same_physics(&before,&after)&&after.tick==before.tick&&after.scene_revision==before.scene_revision&&after.diagnostics[23]==1);
 CHECK(!dango_belt_room_destroy(h));printf("enabled mode remains visible after real rejected step and full rollback PASS\n");return 0;
}
static int wall_drive(int direction,int platform,int negative){
 void* h=NULL;CHECK(!wall_create(0,platform?1.5f:0,-2.5f,2.5f,&h));room_world* w=room_lookup(h);belt_body* b=room_body(w);
 CHECK(!dango_belt_room_set_solid_walls(h,1,0));
 if(platform){dango_belt_room_platform p={1,0,.4,4.8,.2,.72};CHECK(!dango_belt_room_set_platform(h,&p,1));}
 char name[64];snprintf(name,sizeof name,"%s-%s%s",platform?"platform":"floor",direction>0?"right":"left",negative?"-no-contact":"");FILE* log=wall_log(name);CHECK(log);
 dango_belt_room_input input={.body_id=1};input.controls[0]=(float)direction;int contact=0,combined=0;double start=b->q[34],last_bounds[4]={0};
 for(int tick=0;tick<600;++tick){int rc=dango_belt_room_step(h,.0166666f,&input,1),walls=0,floors=0,platforms=0;CHECK(!wall_observe(w,&walls,&floors,&platforms,last_bounds));wall_record(log,w,rc,walls,floors,platforms,last_bounds);
  if(rc){fprintf(stderr,"%s refused at tick=%d rc=%d reason=%.0f\n",name,tick,rc,w->diag[7]);fclose(log);dango_belt_room_destroy(h);return 1;}
  CHECK(last_bounds[0]>=b->left&&last_bounds[2]<=b->right);
  if(walls&&w->diag[20]>0)contact++;
  if(walls&&(platform?platforms:floors))combined++;
 }
 CHECK(contact>0&&combined>0&&direction*(b->q[34]-start)>.5);
 double contact_x=b->q[34];input.controls[0]=(float)-direction;
 for(int tick=0;tick<120;++tick){int rc=dango_belt_room_step(h,.0166666f,&input,1),walls=0,floors=0,platforms=0;CHECK(!wall_observe(w,&walls,&floors,&platforms,last_bounds));wall_record(log,w,rc,walls,floors,platforms,last_bounds);CHECK(!rc);}
 CHECK(direction*(contact_x-b->q[34])>.3);CHECK(direction>0?b->right-last_bounds[2]>.05:last_bounds[0]-b->left>.05);
 fclose(log);CHECK(!dango_belt_room_destroy(h));printf("%s 600 driven ticks contact=%d simultaneous=%d plus 120 reverse-away PASS\n",name,contact,combined);return 0;
}
static int wall_air(void){
 void* h=NULL;CHECK(!wall_create(1.75f,5,-2.5f,2.5f,&h));CHECK(!dango_belt_room_set_solid_walls(h,1,0));room_world* w=room_lookup(h);belt_body* b=room_body(w);
 FILE* log=wall_log("air-right");CHECK(log);dango_belt_room_input input={.body_id=1};input.controls[0]=1;int contact=0;
 for(int tick=0;tick<45;++tick){int rc=dango_belt_room_step(h,.0166666f,&input,1),walls=0,floors=0,platforms=0;double bounds[4];CHECK(!wall_observe(w,&walls,&floors,&platforms,bounds));wall_record(log,w,rc,walls,floors,platforms,bounds);CHECK(!rc);
  CHECK(bounds[1]>b->cfg.ground_height+ROOM_ACTIVATION&&floors==0&&platforms==0);CHECK(!b->grounded&&w->diag[18]==0&&w->diag[19]==0);
  if(walls&&w->diag[20]>0)contact++;
 }
 CHECK(contact>0);fclose(log);CHECK(!dango_belt_room_destroy(h));printf("air side-wall actual reaction=%d frames without floor, ground or platform support PASS\n",contact);return 0;
}
static int wall_far_equivalence(void){
 void *a=NULL,*z=NULL;CHECK(!wall_create(0,0,-100,100,&a)&&!wall_create(0,0,-100,100,&z));CHECK(!dango_belt_room_set_solid_walls(z,1,0));room_world *wa=room_lookup(a),*wz=room_lookup(z);
 for(int tick=0;tick<240;++tick){dango_belt_room_input input={.body_id=1};input.controls[0]=tick>=40&&tick<80?1.f:tick>=80&&tick<120?-1.f:0;input.controls[6]=tick>=120&&tick<150?1.f:0;input.controls[1]=tick>=150&&tick<180?1.f:0;input.controls[2]=tick==200?1.f:0;
  CHECK(!dango_belt_room_step(a,.0166666f,&input,1)&&!dango_belt_room_step(z,.0166666f,&input,1));
  /* The entire private body includes Fourier q/v, scaffold/stat sources,
     energy/shape/solver/speed diagnostics and all drive/force ledgers. */
  CHECK(!memcmp(room_body(wa),room_body(wz),sizeof(belt_body)));
  dango_belt_room_frame fa,fz;CHECK(!dango_belt_room_read_frame(a,&fa,sizeof fa)&&!dango_belt_room_read_frame(z,&fz,sizeof fz));
  CHECK(wall_same_physics(&fa,&fz)&&fa.tick==fz.tick&&fa.diagnostics[23]==0&&fz.diagnostics[23]==1);
 }
 CHECK(!dango_belt_room_destroy(a)&&!dango_belt_room_destroy(z));printf("far walls disabled/enabled 240 ticks every private physical channel bitwise equal PASS\n");return 0;
}
int main(int argc,char** argv){
 setvbuf(stdout,NULL,_IONBF,0);int negative=argc>1&&!strcmp(argv[1],"--no-contact");
 if(negative){room_negative_mode=1;return wall_drive(1,0,1);}
 if(argc>1&&!strcmp(argv[1],"--atomic"))return wall_atomic();
 if(argc>1&&!strcmp(argv[1],"--air"))return wall_air();
 if(argc>1&&!strcmp(argv[1],"--equivalence"))return wall_far_equivalence();
 if(argc>1&&!strcmp(argv[1],"--platform"))return wall_drive(1,1,0);
 if(argc>1&&!strcmp(argv[1],"--drive"))return wall_drive(argc>2?atoi(argv[2]):1,0,0);
 CHECK(!wall_atomic());CHECK(!wall_guard_rollback());CHECK(!wall_far_equivalence());CHECK(!wall_drive(-1,0,0));CHECK(!wall_drive(1,0,0));CHECK(!wall_air());CHECK(!wall_drive(1,1,0));
 printf("F7 solid walls focused native contracts PASS\n");return 0;
}

#include "dango_belt_room.c"
#include <stdio.h>

#define CHECK(c) do { if (!(c)) { fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#c); return 1; } } while (0)
static const dango_belt_config preview_config={
 .node_count=16,.substep_count=4,.radial_rest_length=.68f,.radial_minimum_ratio=.65f,.radial_maximum_ratio=1.22f,
 .perimeter_minimum_ratio=.73f,.perimeter_maximum_ratio=1.07f,.radial_hertz=1.82f,.perimeter_hertz=15.075f,
 .radial_damping=1.85f,.perimeter_damping=1.85f,.area_strength=4.8f,.local_angle_balance_strength=1.7f,
 .gravity=9.8f,.core_mass=.12f,.node_mass=.015f,.drop_height=.42f,.ground_height=-.72f,.node_radius=.045f,
 .friction=.72f,.drag_stiffness=10,.drag_damping=2.2f,.drag_maximum_force=8,.horizontal_force=2.4f,
 .maximum_horizontal_speed=3.2f,.air_control_ratio=.35f,.downward_force=2.2f,.jump_impulse=.42f,.jump_cooldown_seconds=.45f,
 .maximum_speed=12,.minimum_area=.42f,.linear_damping=1.8f};
static const float preview_calibration[11]={1.4f,1,1,4,1.4f,1,1,1,1.8f,35,60};
static int same_physics(const dango_belt_room_frame* a,const dango_belt_room_frame* b){
 return !memcmp(a->body_snapshot,b->body_snapshot,sizeof a->body_snapshot)&&
  !memcmp(a->scaffold,b->scaffold,sizeof a->scaffold)&&!memcmp(a->curve,b->curve,sizeof a->curve)&&
  !memcmp(a->stats,b->stats,sizeof a->stats)&&!memcmp(a->diagnostics,b->diagnostics,sizeof a->diagnostics);
}
int main(void){
 void* handles[3]={0};dango_belt_room_frame frames[3];double peaks[3]={0};int airborne[3]={0},landed[3]={0};
 for(int j=0;j<3;++j)CHECK(!dango_belt_room_create(&preview_config,preview_calibration,11,0,0,-5,5,&handles[j]));
 CHECK(!dango_belt_room_set_preview_jump_speed_scale(handles[1],1));
 CHECK(!dango_belt_room_set_preview_jump_speed_scale(handles[2],.4f));
 room_world* w=room_lookup(handles[2]);belt_body* body=room_body(w);
 CHECK(room_body(room_lookup(handles[0]))->jump_speed_scale==1);
 belt_body before=*body;room_world world_before=*w;
 const float invalid[]={NAN,INFINITY,-INFINITY,0,.099f,1.001f};
 for(int i=0;i<6;++i){
  CHECK(dango_belt_room_set_preview_jump_speed_scale(handles[2],invalid[i])==DANGO_BELT_ROOM_ARGUMENT);
  CHECK(!memcmp(body,&before,sizeof before)&&!memcmp(w,&world_before,sizeof world_before));
 }
 CHECK(!dango_belt_room_set_preview_jump_speed_scale(handles[2],.1f));
 CHECK(!dango_belt_room_set_preview_jump_speed_scale(handles[2],.4f));
 for(int i=0;i<400;++i){
  dango_belt_room_input input={0};input.body_id=1;
  /* Same charge/launch input as the experiment; only the owned scale differs. */
  if(i>=120&&i<147){input.controls[1]=.35f;input.controls[8]=1;}
  if(i==147){input.controls[2]=1;input.controls[8]=1;}
  for(int j=0;j<3;++j){
   CHECK(!dango_belt_room_step(handles[j],.0166666f,&input,1));
   CHECK(!dango_belt_room_read_frame(handles[j],&frames[j],sizeof frames[j]));
   CHECK(!frames[j].fault);
   if(i>=147){
    peaks[j]=fmax(peaks[j],frames[j].stats[12]);
    if(!frames[j].stats[14]&&frames[j].stats[12]>.003)airborne[j]=1;
    if(airborne[j]&&frames[j].stats[14])landed[j]=1;
   }
  }
  CHECK(same_physics(&frames[0],&frames[1]));
  if(i==147){
   CHECK(body->diag[20]==1);
   double expected=(body->mass[1]+body->mass[35])*(double)7.7f*(double).4f;
   CHECK(fabs(body->diag[26]-expected)<1e-10&&fabs(body->diag[21]-expected)<1e-10);
   CHECK(body->diag[25]>0); /* Real energy increase at the jump impulse. */
  }
 }
 CHECK(peaks[0]>.5&&peaks[2]>.015&&peaks[2]<peaks[0]*.4);
 for(int j=0;j<3;++j)CHECK(airborne[j]&&landed[j]&&frames[j].stats[14]);
 w->fault=DANGO_BELT_ROOM_REJECTED;before=*body;world_before=*w;
 CHECK(dango_belt_room_set_preview_jump_speed_scale(handles[2],.5f)==DANGO_BELT_ROOM_FAULT);
 CHECK(!memcmp(body,&before,sizeof before)&&!memcmp(w,&world_before,sizeof world_before));
 for(int j=0;j<3;++j){CHECK(!dango_belt_room_destroy(handles[j]));CHECK(dango_belt_room_set_preview_jump_speed_scale(handles[j],1)==DANGO_BELT_ROOM_HANDLE);}
 printf("preview jump PASS: default/explicit-one bitwise equal for 400 ticks; peak default=%.9g m, scale .4=%.9g m; real impulse/energy, landing, isolation, invalid/fault/dead no-write\n",peaks[0],peaks[2]);
 return 0;
}

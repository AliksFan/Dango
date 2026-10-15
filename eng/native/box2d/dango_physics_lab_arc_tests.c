#include <stdio.h>
#include "dango_arc_math.h"
#include "dango_self_constraints.h"
#include "dango_physics_lab_api.h"
#define CHECK(c) do {if(!(c)){fprintf(stderr,"FAIL %d: %s\n",__LINE__,#c);return 1;}}while(0)
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
static void natural(dango_arc_v* p) {
 for(int i=0;i<16;++i){double a=6.283185307179586*i/16;p[i]=(dango_arc_v){.68*cos(a),.68*sin(a)};}p[16]=(dango_arc_v){0};
}
static int material_tests(void) {
 dango_arc_v p[17],v[17]={{0}},f[17],fd[17];double stats[7];natural(p);
 CHECK(dango_arc_material(p,v,.045,.68,30,.012,0,0,f,stats));CHECK(stats[0]+stats[1]<1e-25);
 for(int i=0;i<17;++i)CHECK(hypot(f[i].x,f[i].y)<1e-10);
 p[0].x-=.08;p[1].y+=.015;p[16].x=.012;
 CHECK(dango_arc_material(p,v,.045,.68,0,.012,0,0,fd,stats));CHECK(stats[1]>1e-4 && hypot(fd[0].x,fd[0].y)>.01);
 CHECK(dango_arc_material(p,v,.045,.68,30,.012,0,0,f,stats));CHECK(stats[0]+stats[1]>.001);CHECK(f[0].x>0);
 CHECK(fabs(stats[3])+fabs(stats[4])+fabs(stats[5])<1e-10);
 /* Rotation/translation and cyclic relabeling preserve energy and rotate the
    complete generalized force, including the core reaction. */
 dango_arc_v transformed[17],rotated[17];double transformedStats[7],angle=.371,co=cos(angle),si=sin(angle);
 for(int i=0;i<17;++i){int source=i==16 ? 16 : (i+5)%16;transformed[i]=(dango_arc_v){3+co*p[source].x-si*p[source].y,4+si*p[source].x+co*p[source].y};}
 CHECK(dango_arc_material(transformed,v,.045,.68,30,.012,0,0,rotated,transformedStats));
 CHECK(fabs(transformedStats[0]-stats[0])+fabs(transformedStats[1]-stats[1])<1e-12);
 for(int i=0;i<17;++i){int source=i==16 ? 16 : (i+5)%16;CHECK(hypot(rotated[i].x-(co*f[source].x-si*f[source].y),rotated[i].y-(si*f[source].x+co*f[source].y))<1e-10);}
 for(int i=0;i<17;++i)for(int axis=0;axis<2;++axis) {
  double* coordinate=axis ? &p[i].y : &p[i].x;double save=*coordinate,h=1e-6,plus[7],minus[7];
  *coordinate=save+h;CHECK(dango_arc_material(p,v,.045,.68,30,.012,0,0,fd,plus));
  *coordinate=save-h;CHECK(dango_arc_material(p,v,.045,.68,30,.012,0,0,fd,minus));*coordinate=save;
  double numerical=-((plus[0]+plus[1])-(minus[0]+minus[1]))/(2*h),analytic=axis ? f[i].y : f[i].x;
  CHECK(fabs(numerical-analytic)<2e-6*(1+fabs(analytic)));
 }
 for(int i=0;i<17;++i)v[i]=(dango_arc_v){.13*sin(i*.7),.22*cos(i*.9)};
 double damp[7];CHECK(dango_arc_material(p,v,.045,.68,30,.012,.4,.0002,fd,damp));double power=0;
 for(int i=0;i<17;++i)power+=arc_dot(arc_sub(fd[i],f[i]),v[i]);
 CHECK(power<0 && fabs(power-damp[2])<1e-9);
 double areaE,area;CHECK(dango_arc_area(p,.045,2,3,fd,&areaE,&area));
 CHECK(areaE>0);
 for(int i=0;i<17;++i)for(int axis=0;axis<2;++axis) {
  double* coordinate=axis ? &p[i].y : &p[i].x;double save=*coordinate,h=1e-6,ep,em,ignored;dango_arc_v tmp[17];
  *coordinate=save+h;CHECK(dango_arc_area(p,.045,2,3,tmp,&ep,&ignored));
  *coordinate=save-h;CHECK(dango_arc_area(p,.045,2,3,tmp,&em,&ignored));*coordinate=save;
  CHECK(fabs(-(ep-em)/(2*h)-(axis ? fd[i].y : fd[i].x))<1e-7);
 }
 return 0;
}
static int local_recovery_test(void) {
 dango_arc_v p[17],v[17]={{0}},f[17];double stats[7];natural(p);p[0].x-=.08;
 CHECK(dango_arc_material(p,v,.045,.68,30,.012,.4,.0002,f,stats));double initial=stats[0]+stats[1];
 double peakSpeed=0;
 /* Other controls/core are fixed supports. The pressed local span is released
    and integrated dynamically; support reactions do zero work. */
 for(int step=0;step<10000;++step) {
  CHECK(dango_arc_material(p,v,.045,.68,30,.012,.4,.0002,f,stats));
  v[0]=arc_add(v[0],arc_mul(.0001/.015,f[0]));p[0]=arc_add(p[0],arc_mul(.0001,v[0]));peakSpeed=fmax(peakSpeed,v[0].x);
 }
 printf("local recovery: residual displacement %.9g, peak outward speed %.9g, initial/final energy %.9g/%.9g\n",fabs(p[0].x-.68),peakSpeed,initial,stats[0]+stats[1]);
 CHECK(peakSpeed>.01 && fabs(p[0].x-.68)<.005);CHECK(stats[0]+stats[1]<initial*.01);return 0;
}
static int contact_tests(void) {
 dango_constraint_state s={0};float stats[32]={0},envelope[32]={0};s.arc_stats=stats;s.envelope_stats=envelope;
 s.radial_length=1;s.radial_min=.1f;s.radial_max=3;s.envelope_friction=.6f;
 for(int i=0;i<16;++i){float a=6.283185307179586f*i/16;s.positions[i]=(b2Vec2){cosf(a),1+sinf(a)};}
 s.positions[16]=(b2Vec2){0,1};for(int i=10;i<=14;++i)s.positions[i].y=-.01f;
 for(int i=0;i<17;++i){s.inverse_mass[i]=1;s.velocities[i]=(b2Vec2){.3f,-1};}
 b2Vec2 oldVelocity[17];memcpy(oldVelocity,s.velocities,sizeof oldVelocity);
 CHECK(isfinite(dango_arc_contact(&s,true,DANGO_CONSTRAINT_CORRECT_POSITION)));
 CHECK(!memcmp(oldVelocity,s.velocities,sizeof oldVelocity));CHECK(stats[7]>=3);
 for(int iteration=0;iteration<30;++iteration)dango_arc_contact(&s,true,DANGO_CONSTRAINT_CORRECT_POSITION);
 /* Independently exercise an exact flat contact patch after the penetration
    repair test. Repair may legitimately round the initially penetrated patch. */
 for(int i=10;i<=14;++i)s.positions[i].y=0;
 float before=dango_envelope_energy(&s);
 for(int iteration=0;iteration<64;++iteration)CHECK(isfinite(dango_arc_contact(&s,true,DANGO_CONSTRAINT_CORRECT_VELOCITY)));
 CHECK(dango_envelope_energy(&s)<=before+1e-6f);CHECK(stats[9]>0 && stats[7]>=2);
 CHECK(fabsf(envelope[15])+fabsf(envelope[16])+fabsf(envelope[17])<1e-4f);
 for(int row=0;row<32;++row)CHECK(fabsf(s.arc_tangent[row])<=s.envelope_friction*s.arc_normal[row]+1e-6f);
 dango_envelope_point foot;CHECK(dango_envelope_geometry(s.positions,0,&foot));CHECK(foot.point.y>=-1e-5f);
 /* Independent dense curve oracle for the minimum. */
 double minimum=INFINITY;
 for(int i=0;i<16;++i)for(int j=0;j<=1000;++j){double b[3],d[3],dd[3];arc_basis(j/1000.0,b,d,dd);double y=0;for(int k=0;k<3;++k)y+=b[k]*s.positions[(i+15+k)%16].y;minimum=fmin(minimum,y);}
 CHECK(fabs(minimum-foot.point.y)<1e-7);
 (void)dango_constraint_solve;
 return 0;
}
int dango_physics_lab_ground_model(void);
int dango_physics_lab_region_fraction(void);
int dango_physics_world_get_lab_ground_stats(void*,int,float*,int);
static double world_com_vx(void* w,int id) {
 float review[102];if(dango_physics_world_get_drive_review(w,id,review,102))return NAN;
 double momentum=0,mass=0;for(int i=0;i<17;++i){momentum+=review[6*i+2]*review[6*i+4];mass+=review[6*i+4];}return momentum/mass;
}
static int airborne_contract(void) {
 dango_self_physics_config cfg=config;cfg.linear_damping=0;cfg.top_inward_force=cfg.ground_deformation_strength=0;
 void* w=NULL;int id=0,sid=0,n=0,contacts=0,error=0;CHECK(!dango_physics_world_create(&cfg,1,&w));CHECK(!dango_physics_world_add(w,0,2,&id));
 float c[9]={1,0,1},state[42],stats[16],review[102];
 CHECK(!dango_physics_world_step_lab(w,1.0f/60,&id,c,1,1,0,&sid,state,42,&n,&contacts,&error) && !error);
 CHECK(state[39]==0);CHECK(!dango_physics_world_get_lab_stats(w,id,stats,16));CHECK(fabsf(stats[3]-cfg.horizontal_force*cfg.air_control_ratio)<1e-6f);
 CHECK(!dango_physics_world_get_drive_review(w,id,review,102));CHECK(review[3]<0); /* Airborne Space did not launch. */
 double vx=world_com_vx(w,id);CHECK(vx>.02);memset(c,0,sizeof c);
 for(int frame=0;frame<10;++frame)CHECK(!dango_physics_world_step_lab(w,1.0f/60,&id,c,1,1,0,&sid,state,42,&n,&contacts,&error) && !error);
 CHECK(state[39]==0 && fabs(world_com_vx(w,id)-vx)<1e-4); /* No ground dry/static friction in air. */
 bool landed=false;for(int frame=0;frame<180;++frame){CHECK(!dango_physics_world_step_lab(w,1.0f/60,&id,c,1,1,0,&sid,state,42,&n,&contacts,&error) && !error);if(state[39]!=0){landed=true;break;}}
 CHECK(landed);c[2]=c[6]=1;
 CHECK(!dango_physics_world_step_lab(w,1.0f/60,&id,c,1,1,0,&sid,state,42,&n,&contacts,&error) && !error);
 CHECK(!dango_physics_world_get_lab_stats(w,id,stats,16));
 /* Accepted Space freezes the prior zero W ramp for the entire frame despite
    a new W request. A rejected Space would advance it and apply W forces. */
 CHECK(stats[15]==1 && stats[2]==0 && stats[9]==0);
 CHECK(!dango_physics_world_destroy(w));puts("airborne drive/jump rejection, no floor damping, and first landed jump arbitration passed");return 0;
}
static int world_tests(bool window_defaults) {
 CHECK(dango_physics_lab_ground_model()==2 && dango_physics_lab_arc_model()==1 && dango_physics_lab_region_fraction()==2);
 dango_self_physics_config cfg=config;
 if(window_defaults){cfg.radial_hertz=1.82f;cfg.perimeter_hertz=15.075f;cfg.top_inward_force=0;cfg.ground_deformation_strength=0;}
 void* w=NULL;int id=0;CHECK(!dango_physics_world_create(&cfg,1,&w));CHECK(!dango_physics_world_add(w,0,2,&id));
 float c[9]={0},state[42],stats[32];int sid=0,n=0,contacts=0,error=0;
 CHECK(dango_physics_world_step_lab(w,1.0f/60,&id,c,1,5,0,&sid,state,42,&n,&contacts,&error)==-410);
 int max_contacts=0;double maxRepair=0;
 for(int frame=0;frame<1080;++frame) {
  memset(c,0,sizeof c);c[0]=frame>=300 && frame<420 ? 1.0f : frame>=540 && frame<600 ? -1.0f : 0;
  c[6]=frame>=660 && frame<720 ? 1.0f : 0;c[1]=frame>=780 && frame<840 ? 1.0f : 0;c[2]=frame==900 ? 1.0f : 0;
  int result=dango_physics_world_step_lab(w,1.0f/60,&id,c,1,4,0,&sid,state,42,&n,&contacts,&error);
  if(result || error){dango_physics_world_get_lab_arc_stats(w,id,stats,32);fprintf(stderr,"world frame %d result %d error %d\n",frame,result,error);for(int i=0;i<32;++i)fprintf(stderr,"stat%d %.9g\n",i,(double)stats[i]);return 1;}
  CHECK(n==1 && sid==id);CHECK(!dango_physics_world_get_lab_arc_stats(w,id,stats,32));
  float groundStats[32];CHECK(!dango_physics_world_get_lab_ground_stats(w,id,groundStats,32));
  CHECK(groundStats[31]==2 && groundStats[13]==0 && groundStats[14]==0);
  CHECK(fabsf(groundStats[5]-cfg.friction)<1e-6f);
  CHECK(groundStats[4]<=cfg.friction*groundStats[3]+1e-5f);
  for(int i=0;i<32;++i)CHECK(isfinite(stats[i]));CHECK(stats[2]<=1e-6f && stats[15]==0);
  CHECK(stats[11]<=1e-5f);max_contacts=stats[7]>max_contacts ? (int)stats[7] : max_contacts;maxRepair=fmax(maxRepair,stats[16]);
  CHECK(stats[8]<=1e-4f && stats[24]<=1e-6f && stats[25]<=1e-6f);
 }
 CHECK(max_contacts>=2);printf("world %s:1080 frames, max contacts %d, peak positive material repair energy %.9g\n",window_defaults ? "window" : "old",max_contacts,maxRepair);
 CHECK(!dango_physics_world_destroy(w));return 0;
}
int main(void) {CHECK(!material_tests());CHECK(!local_recovery_test());CHECK(!contact_tests());CHECK(!airborne_contract());CHECK(!world_tests(false));CHECK(!world_tests(true));puts("elastic arc tests passed");return 0;}

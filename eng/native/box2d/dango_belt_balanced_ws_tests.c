#include "dango_belt.c"
#include <stdio.h>
#define CHECK(c) do{if(!(c)){fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#c);return 1;}}while(0)
static dango_belt_config config={
 .node_count=16,.substep_count=1,.radial_rest_length=.68f,.radial_minimum_ratio=.65f,.radial_maximum_ratio=1.22f,
 .perimeter_minimum_ratio=.73f,.perimeter_maximum_ratio=1.07f,.radial_hertz=1.82f,.perimeter_hertz=15.075f,
 .radial_damping=1.85f,.perimeter_damping=1.85f,.area_strength=4.8f,.local_angle_balance_strength=1.7f,
 .gravity=0,.core_mass=.12f,.node_mass=.015f,.drop_height=.42f,.ground_height=-100,.node_radius=.045f,
 .friction=.72f,.drag_stiffness=10,.drag_damping=2.2f,.drag_maximum_force=8,.horizontal_force=2.4f,
 .maximum_horizontal_speed=3.2f,.air_control_ratio=.35f,.downward_force=2.2f,.jump_impulse=.42f,.jump_cooldown_seconds=.45f,
 .maximum_speed=12,.minimum_area=.42f};
/* Independently inspect reported physical loads, rather than accepting a zero
   stats field or a COM velocity correction as proof of balanced application. */
static int check_ledger(belt_body* b,const double* oldq,double upper){
 double bounds[4],foot;belt_bounds(oldq,bounds,&foot);double mid=(bounds[1]+bounds[3])*.5;
 double top=0,bottom=0,net=0,torque=0;int nt=0,nb=0;
 for(int i=0;i<(int)b->force_ledger[4];++i){const double* r=b->force_ledger+8+6*i;CHECK(r[4]==2);CHECK(r[2]==0);net+=r[3];torque+=r[0]*r[3];
  if(r[1]>=mid){top+=r[3];nt++;CHECK(r[3]*upper>0);}else {bottom+=r[3];nb++;CHECK(r[3]*upper<0);}}
 CHECK(nt>0&&nb>0);CHECK(fabs(top-upper)<1e-12&&fabs(bottom+upper)<1e-12);CHECK(fabs(net)<1e-12);
 CHECK(fabs(b->stats[19])<1e-12&&fabs(b->stats[58])<1e-12);CHECK(fabs(b->stats[24]-torque)<1e-12);return 0;
}
static int free_contract(int asymmetric,int w){
 void* h=NULL;CHECK(dango_belt_create(&config,0,0,1,&h)==0);belt_body* b=belt_lookup(h);
 if(asymmetric){b->q[2]*=.96;b->q[5]*=1.02;b->q[6]=.025;b->q[7]=.018;b->q[34]+=.013;}
 b->v[0]=b->v[34]=.17;b->v[1]=b->v[35]=-.23;double initial[3];belt_momentum(b,initial);
 float c[9]={0};c[w?6:1]=1;
 for(int tick=0;tick<24;++tick){belt_body shadow=*b;double oldq[BELT_D];memcpy(oldq,b->q,sizeof oldq);CHECK(dango_belt_step(h,1.0f/240,c,9)==0);
  double upper=b->cfg.downward_force*(b->ramp-c[1]);CHECK(check_ledger(b,oldq,upper)==0);
  double exact[BELT_D]={0},action[2];CHECK(belt_region_force(&shadow,exact,1,1,upper,action));CHECK(belt_region_force(&shadow,exact,1,-1,-upper,action));
  CHECK(exact[34]==0&&exact[35]==0&&fabs(exact[1])<1e-14);CHECK(belt_integrate(&shadow,1.0f/240,exact));
  for(int j=0;j<BELT_D;++j)CHECK(fabs(shadow.q[j]-b->q[j])<1e-13&&fabs(shadow.v[j]-b->v[j])<1e-13);
  double p[3];belt_momentum(b,p);CHECK(fabs(p[0]-initial[0])<1e-9&&fabs(p[1]-initial[1])<1e-9);
  CHECK(b->stats[25]==0&&b->stats[31]==0&&b->stats[34]<=2e-7&&b->stats[56]<=40);
  double lhs=b->diag[3]+b->diag[5]-b->diag[2]-b->diag[4];CHECK(fabs(lhs-b->diag[27]-b->diag[28])<1e-10);
 }
 if(w){double ramp=b->ramp;c[6]=0;CHECK(dango_belt_step(h,1.0f/240,c,9)==0);CHECK(fabs(b->ramp-(ramp-(1.0f/240)/.20))<1e-14);}
 c[8]=1;CHECK(dango_belt_step(h,1.0f/240,c,9)==0);CHECK(b->ramp==0&&b->force_ledger[4]==0);
 CHECK(dango_belt_destroy(h)==0);return 0;
}
static int opposing_keys_preserve_other_channels(int radial){
 void* h=NULL;CHECK(dango_belt_create(&config,0,0,1,&h)==0);belt_body* b=belt_lookup(h);
 if(radial){CHECK(dango_belt_set_shape_guard(h,1)==0);CHECK(dango_belt_set_radial_drive(h,1)==0);}
 b->ramp=.6;double before[3];belt_momentum(b,before);float c[9]={1,1,0,0,0,0,1,1,0};
 CHECK(dango_belt_step(h,1.0f/240,c,9)==0);double fx=0;int horizontal=0;
 for(int i=0;i<(int)b->force_ledger[4];++i){const double* r=b->force_ledger+8+6*i;CHECK(r[4]!=2);fx+=r[2];horizontal+=r[4]==(radial?5:1);}
 /* An independent positive horizontal budget and measured momentum prevent a
    full-cancel reference from accidentally making this regression pass. */
 CHECK(horizontal>0&&fx>.1&&b->stats[18]>.1&&b->ramp==0);
 CHECK(fabs(fx-b->stats[18])<1e-12);double after[3];belt_momentum(b,after);CHECK(after[0]>before[0]);CHECK(fabs(after[0]-before[0]-(1.0f/240)*fx)<1e-9);
 c[3]=1;c[4]=(float)b->q[0];c[5]=(float)(b->q[1]+b->radius+.1);b->grounded=1;b->cooldown=1;c[2]=1;
 CHECK(dango_belt_step(h,1.0f/240,c,9)==0);CHECK(b->diag[20]==1);int drag=0;
 for(int i=0;i<(int)b->force_ledger[4];++i){const double* r=b->force_ledger+8+6*i;CHECK(r[4]!=2);drag+=r[4]==4;}CHECK(drag==1);
 c[2]=c[3]=0;c[8]=1;CHECK(dango_belt_step(h,1.0f/240,c,9)==0);CHECK(b->stats[18]==0&&b->force_ledger[4]==0);
 CHECK(dango_belt_destroy(h)==0);return 0;
}
static int jump_and_rollback_contract(void){
 void* h=NULL;CHECK(dango_belt_create(&config,0,0,1,&h)==0);belt_body* b=belt_lookup(h);b->grounded=1;b->cooldown=1;
 float c[9]={0};c[6]=c[2]=1;CHECK(dango_belt_step(h,1.0f/240,c,9)==0);CHECK(b->diag[20]==1&&b->force_ledger[4]==0&&b->ramp==0);
 c[2]=0;CHECK(dango_belt_step(h,1.0f/240,c,9)==0);CHECK(b->force_ledger[4]>0);
 b->cfg.maximum_speed=.000001f;belt_body before=*b;CHECK(dango_belt_step(h,1.0f/240,c,9)<0);
 CHECK(memcmp(before.q,b->q,sizeof b->q)==0&&memcmp(before.v,b->v,sizeof b->v)==0&&memcmp(before.force_ledger,b->force_ledger,sizeof b->force_ledger)==0);
 CHECK(dango_belt_destroy(h)==0);return 0;
}
int main(void){for(int asymmetric=0;asymmetric<2;++asymmetric)for(int w=0;w<2;++w)CHECK(free_contract(asymmetric,w)==0);
 CHECK(opposing_keys_preserve_other_channels(0)==0);CHECK(opposing_keys_preserve_other_channels(1)==0);
 CHECK(jump_and_rollback_contract()==0);puts("balanced W/S upper/lower totals, asymmetric field/torque, free momentum, work/gates, envelopes, opposing keys preserve horizontal momentum/drag/jump, full cancellation and ledger rollback PASS");return 0;}

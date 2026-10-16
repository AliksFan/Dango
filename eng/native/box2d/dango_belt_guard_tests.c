#include "dango_belt.c"
#include <stdio.h>
#define CHECK(c) do{if(!(c)){fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#c);return 1;}}while(0)
/* Algebra/interface fixtures, not a material-range calibration sweep. */
static dango_belt_config fixture={.node_count=16,.substep_count=4,.radial_rest_length=.68f,
 .radial_hertz=1.82f,.perimeter_hertz=15.075f,.radial_damping=1.85f,.perimeter_damping=1.85f,
 .area_strength=4.8f,.local_angle_balance_strength=1.7f,.core_mass=.12f,.node_mass=.015f,
 .ground_height=-100,.node_radius=.045f,.friction=.72f,.maximum_horizontal_speed=3.2f,.maximum_speed=12};
static void circle(double* q){memset(q,0,BELT_D*sizeof(double));q[2]=q[5]=1;}
static int geometry_contract(void){double q[BELT_D],to[BELT_D];belt_guard_result r;circle(q);
 CHECK(belt_guard_certify(q,q,1,10000,&r));CHECK(fabs(r.winding-1)<1e-12);CHECK(r.distance_lower>.04);
 q[2]=1.7;q[5]=.18;CHECK(belt_guard_certify(q,q,1,10000,&r));
 /* A star-shaped nonconvex radial indentation stays legal. */
 circle(q);q[2]=.9;q[5]=1.1;q[10]=.1;q[13]=.1;CHECK(belt_guard_certify(q,q,1,10000,&r));
 circle(q);q[9]=.8;CHECK(belt_area(q,NULL)>3);CHECK(!belt_guard_certify(q,q,1,10000,&r));
 circle(q);q[34]=1.2;CHECK(!belt_guard_certify(q,q,1,10000,&r));
 /* Positive sampled cross misses a narrow negative interval between samples. */
 circle(q);q[34]=1.00003*cos(BELT_PI/256);q[35]=1.00003*sin(BELT_PI/256);
 for(int i=0;i<256;++i)CHECK(belt_cross(belt_guard_relative(q,2*BELT_PI*i/256),belt_eval(q,2*BELT_PI*i/256,1))>0);
 CHECK(!belt_guard_certify(q,q,1,10000,&r));
 circle(q);memcpy(to,q,sizeof to);to[2]=to[5]=-1;CHECK(belt_guard_certify(to,to,1,10000,&r));CHECK(!belt_guard_certify(q,to,1,10000,&r));
 circle(q);CHECK(!belt_guard_certify(q,q,1,0,&r)&&r.reason==BELT_GUARD_BUDGET);
 circle(q);q[34]=.959999;CHECK(!belt_guard_certify(q,q,1,10000,&r)&&r.reason==BELT_GUARD_UNCERTAIN&&r.depth==20&&r.work<10000);
 memset(q,0,sizeof q);q[6]=q[9]=1;CHECK(!belt_guard_certify(q,q,1,10000,&r)&&r.reason==BELT_GUARD_WINDING);
 puts("continuous circle/flattening/nonconvex, positive-area loop, core-outside, missed sample, crossing path, budget and winding PASS");return 0;
}
static double energy_gradient(belt_body* b,double* gradient){belt_residual r[BELT_MAX_RESIDUALS];double md,e=0;int n=belt_residuals(b,b->q,r,&md);if(n<0)return INFINITY;memset(gradient,0,BELT_D*sizeof(double));for(int i=n-128;i<n;++i){e+=.5*r[i].k*r[i].value*r[i].value;for(int j=0;j<BELT_D;++j)gradient[j]+=r[i].k*r[i].value*r[i].j[j];}return e;}
static int force_contract(void){void* h=NULL;CHECK(dango_belt_create(&fixture,0,0,1,&h)==0);CHECK(dango_belt_set_shape_guard(h,1)==0);belt_body* b=belt_lookup(h);
 b->q[2]=.4*b->radius;b->q[5]=.2*b->radius;b->q[34]=.03*b->radius;double gradient[BELT_D],scratch[BELT_D],e=energy_gradient(b,gradient);CHECK(e>0&&isfinite(e));
 for(int j=0;j<BELT_D;++j){double old=b->q[j],fd=1e-7;b->q[j]=old+fd;double ep=energy_gradient(b,scratch);b->q[j]=old-fd;double em=energy_gradient(b,scratch);b->q[j]=old;CHECK(fabs((ep-em)/(2*fd)-gradient[j])<1e-5*(1+fabs(gradient[j])));}
 CHECK(fabs(gradient[0]+gradient[34])+fabs(gradient[1]+gradient[35])<1e-10);double torque=0;for(int j=0;j<BELT_D;j+=2)torque+=b->q[j]*gradient[j+1]-b->q[j+1]*gradient[j];CHECK(fabs(torque)<1e-10);
 CHECK(fabs(belt_guard_energy(b)-e)<1e-12);CHECK(belt_elastic_energy(b)>=e);CHECK(dango_belt_destroy(h)==0);
 puts("active barrier analytic gradients, internal force/torque and elastic inclusion PASS");return 0;
}
static int interface_contract(void){void* h=NULL;volatile size_t config_size=sizeof(dango_belt_config);CHECK(config_size==164);CHECK(dango_belt_create(&fixture,0,0,1,&h)==0);float guard[24];CHECK(dango_belt_read_shape_guard(h,guard,23)==-600);CHECK(dango_belt_set_shape_guard(h,2)==-600);CHECK(dango_belt_set_shape_guard(h,1)==0);
 belt_body* b=belt_lookup(h);float c[9]={0};CHECK(dango_belt_step(h,1.0f/60,c,9)==0);CHECK(dango_belt_set_shape_guard(h,0)==-600);CHECK(dango_belt_read_shape_guard(h,guard,24)==0);CHECK(guard[0]==1&&guard[1]==1&&guard[2]==1&&guard[3]==0&&guard[22]==4);
 double snapshot[200],snapshot_again[200],solve[24];CHECK(dango_belt_read_snapshot(h,snapshot,199)==-600);CHECK(dango_belt_read_snapshot(h,snapshot,200)==0);CHECK(dango_belt_read_snapshot(h,snapshot_again,200)==0);CHECK(memcmp(snapshot,snapshot_again,sizeof snapshot)==0);CHECK(snapshot[0]==1&&snapshot[1]==200);CHECK(memcmp(snapshot+2,b->q,sizeof b->q)==0&&memcmp(snapshot+38,b->v,sizeof b->v)==0);CHECK(snapshot[87]==16&&snapshot[88]==4);CHECK(dango_belt_read_solver_diagnostics(h,solve,23)==-600);CHECK(dango_belt_read_solver_diagnostics(h,solve,24)==0&&solve[0]==1&&solve[1]==1&&solve[2]==0&&solve[3]==0);
 /* Inject a bad initial state to make guard failure deterministic. Old arrays,
    controls, velocity and integration state must roll back as one frame. */
 b->q[34]=b->q[0]+2*b->radius;belt_body backup=*b;CHECK(dango_belt_step(h,1.0f/60,c,9)==-605);CHECK(memcmp(b->q,backup.q,sizeof b->q)==0);CHECK(memcmp(b->v,backup.v,sizeof b->v)==0);CHECK(memcmp(b->stats,backup.stats,sizeof b->stats)==0);CHECK(memcmp(b->diag,backup.diag,sizeof b->diag)==0);CHECK(b->ramp==backup.ramp&&b->cooldown==backup.cooldown&&b->grounded==backup.grounded&&b->dragging==backup.dragging);CHECK(dango_belt_read_shape_guard(h,guard,24)==0&&guard[2]==2&&guard[3]==-605&&guard[10]==4);CHECK(dango_belt_read_solver_diagnostics(h,solve,24)==0&&solve[1]==2&&solve[2]==-605&&solve[3]==19);CHECK(dango_belt_destroy(h)==0);
 /* Zero external work: guard strain release has to appear in the ordinary
    elastic energy and existing stage ledger, never as an unreported repair. */
 CHECK(dango_belt_create(&fixture,0,0,1,&h)==0);CHECK(dango_belt_set_shape_guard(h,1)==0);b=belt_lookup(h);b->q[34]=b->q[0]+.84*b->radius;double e0=belt_guard_energy(b);CHECK(e0>0);CHECK(dango_belt_step(h,1.0f/60,c,9)==0);CHECK(b->guard[16]>0);double lhs=b->diag[3]+b->diag[5]+b->diag[7]-b->diag[2]-b->diag[4]-b->diag[6];double rhs=b->diag[25]+b->diag[10]+b->diag[11]+b->diag[27]+b->diag[28]+b->diag[14]+b->diag[15]+b->diag[7]-b->diag[6];CHECK(fabs(lhs-rhs)<1e-8);CHECK(b->guard[15]<e0);CHECK(dango_belt_destroy(h)==0);
 puts("additive ABI, before-step immutability, failed-frame atomicity, recovery and energy ledger PASS");return 0;
}
static int ground_and_legacy_contract(void){void* h=NULL;CHECK(dango_belt_create(&fixture,0,0,1,&h)==0);CHECK(dango_belt_set_shape_guard(h,1)==0);belt_body* b=belt_lookup(h);
 b->cfg.ground_height=0;b->q[1]=b->radius*.8;b->q[35]=b->q[1]-b->radius*.94;double gap_before=b->q[1]-b->q[35];double energy_before=belt_elastic_energy(b);CHECK(belt_contact_solve(b));CHECK(b->guard[18]>0);CHECK(fabs((b->q[1]-b->q[35])-gap_before)<1e-12);CHECK(fabs(b->diag[15]-(belt_elastic_energy(b)-energy_before))<1e-10);CHECK(belt_guard_path(b,b->q,b->q,3));
 b->guard[4]=BELT_GUARD_FRAME_BUDGET;CHECK(!belt_guard_path(b,b->q,b->q,1)&&b->guard[9]==BELT_GUARD_BUDGET);CHECK(dango_belt_destroy(h)==0);
 void* a=NULL;void* c=NULL;CHECK(dango_belt_create(&fixture,0,0,1,&a)==0);CHECK(dango_belt_create(&fixture,0,0,1,&c)==0);CHECK(dango_belt_set_shape_guard(c,0)==0);float controls[9]={0};CHECK(dango_belt_step(a,1.0f/60,controls,9)==0);CHECK(dango_belt_step(c,1.0f/60,controls,9)==0);belt_body* aa=belt_lookup(a);belt_body* cc=belt_lookup(c);CHECK(memcmp(aa->q,cc->q,sizeof aa->q)==0&&memcmp(aa->v,cc->v,sizeof aa->v)==0&&memcmp(aa->diag,cc->diag,sizeof aa->diag)==0&&memcmp(aa->stats,cc->stats,sizeof aa->stats)==0);CHECK(dango_belt_destroy(a)==0);CHECK(dango_belt_destroy(c)==0);
 puts("guarded floor fallback, projection elastic ledger, fixed work cap and disabled-mode trajectory PASS");return 0;
}
int main(void){CHECK(geometry_contract()==0);CHECK(force_contract()==0);CHECK(interface_contract()==0);CHECK(ground_and_legacy_contract()==0);puts("all guard contracts PASS");return 0;}

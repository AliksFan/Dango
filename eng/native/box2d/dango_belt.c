#include "dango_belt.h"
#include "dango_belt_math.h"
#include "dango_belt_guard.h"
#include "dango_belt_basis_cache.h"
#include <stdint.h>
#include <stdlib.h>
#include <stdbool.h>
#include <float.h>
#define BELT_MAX_RESIDUALS 320
#define BELT_GUARD_FRAME_BUDGET 131072
typedef struct belt_residual {double value,k,c,scalar_curvature,j[BELT_D];} belt_residual;
typedef struct belt_body {
 dango_belt_config cfg;double q[BELT_D],v[BELT_D],mass[BELT_D];
 double radius,scale,left,right,ramp,cooldown,drag_theta;
 /* Per-instance experiment input; default preserves the accepted jump exactly. */
 double jump_speed_scale;
 /* Transient single-step muscle input; never retained by the room API. */
 double muscle_time,muscle_strength,muscle_diag[32];
 int muscle_profile;
 double muscle_axis_x,muscle_axis_y;
 uint32_t muscle_seed;
 int short_bones,grounded,dragging,original_ground_jump,has_stepped;double stats[64],cal[11],diag[48];
 int shape_guard,speed_saturation,whale_drive,radial_drive;double radial_diag[16],rear_diag[16];double whale_gain,whale_diag[16],force_ledger[1024];double guard[24],solver_diag[24],speed_limit[12];
} belt_body;
#ifdef DANGO_BELT_ROOM_HOOKS
static void belt_room_before_integrate(belt_body* b);
static int belt_room_contact(belt_body* b);
#define BELT_CONTACT_DISPATCH belt_room_contact
#else
#define BELT_CONTACT_DISPATCH belt_contact_solve
#endif
static int belt_solver_fail(belt_body* b,int site){b->solver_diag[3]=site;return 0;}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#define belt_guard_path belt_guard_path_profile_impl
#endif
static int belt_guard_path(belt_body* b,const double* from,const double* to,int stage){
 if(!b->shape_guard)return 1;belt_guard_result result;int remaining=BELT_GUARD_FRAME_BUDGET-(int)b->guard[4];
 int ok=belt_guard_certify(from,to,b->radius,remaining,&result);b->guard[4]+=result.work;b->guard[5]+=1;b->guard[6]=fmax(b->guard[6],result.depth);b->guard[7]+=result.leaves;
 if(!ok){b->guard[8]+=1;b->guard[9]=result.reason;b->guard[10]=stage;return 0;}
 b->guard[11]=result.cross_lower;b->guard[12]=result.distance_lower;b->guard[13]=result.winding;return 1;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#undef belt_guard_path
static int belt_guard_path(belt_body* b,const double* from,const double* to,int stage){double start=belt_profile_now();int result=belt_guard_path_profile_impl(b,from,to,stage);belt_profile_finish(stage==1?BELT_PROFILE_GUARD_NEWTON:stage==2?BELT_PROFILE_GUARD_INTEGRATED:stage==3?BELT_PROFILE_GUARD_GROUND:BELT_PROFILE_GUARD_INITIAL,start);return result;}
#endif

static struct {uintptr_t token;belt_body* body;} belt_handles[64];
static uintptr_t belt_next_token=1;
static belt_body* belt_lookup(void* handle){uintptr_t token=(uintptr_t)handle;for(int i=0;i<64;++i)if(belt_handles[i].token==token&&token)return belt_handles[i].body;return NULL;}
static double belt_kinetic(const belt_body* b){double e=0;for(int j=0;j<BELT_D;++j)e+=.5*b->mass[j]*b->v[j]*b->v[j];return e;}
static void belt_momentum(const belt_body* b,double* p){p[0]=b->mass[0]*b->v[0]+b->mass[34]*b->v[34];p[1]=b->mass[1]*b->v[1]+b->mass[35]*b->v[35];p[2]=0;for(int j=0;j<BELT_D;j+=2)p[2]+=b->mass[j]*(b->q[j]*b->v[j+1]-b->q[j+1]*b->v[j]);}
static double belt_gravity_energy(const belt_body* b){return b->cfg.gravity*(b->mass[1]*b->q[1]+b->mass[35]*b->q[35]);}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#define belt_speed_certificate belt_speed_certificate_profile_impl
#endif
static double belt_speed_certificate(const belt_body* b,int* sample_count,double* sample_maximum,double* coarse_upper){double maximum=0,lipschitz=0;for(int i=0;i<256;++i){belt_v v=belt_eval(b->v,2*BELT_PI*i/256,0);maximum=fmax(maximum,hypot(v.x,v.y));}
 for(int n=1;n<=BELT_K;++n)lipschitz+=n*(hypot(b->v[4*n-2],b->v[4*n-1])+hypot(b->v[4*n],b->v[4*n+1]));
 /* Every material point is <=pi/256 from a sampled point. This is an
    explicit continuous upper bound, not an assumed sampled maximum. */
 double core=hypot(b->v[34],b->v[35]),upper=fmax(core,maximum+lipschitz*BELT_PI/256),roundoff=0;int count=256;
 if(b->shape_guard){double amplitude=core;for(int j=0;j<BELT_B;++j)amplitude+=hypot(b->v[2*j],b->v[2*j+1]);roundoff=64*DBL_EPSILON*(1+amplitude+lipschitz);upper+=roundoff;}if(coarse_upper)*coarse_upper=upper;
 /* A coarse continuous upper bound above the limit is not evidence of true
    overspeed. Only opted-in guarded calls refine, with <=16384 total points.
    The nested grid retains every earlier point; each level has the same
    Fourier Lipschitz proof. A sampled violation or unresolved cap still fails. */
 while(b->shape_guard&&upper>b->cfg.maximum_speed&&((maximum<=b->cfg.maximum_speed&&core<=b->cfg.maximum_speed)||b->speed_saturation)&&count<16384){count*=2;for(int i=1;i<count;i+=2){belt_v v=belt_eval(b->v,2*BELT_PI*i/count,0);maximum=fmax(maximum,hypot(v.x,v.y));}upper=fmax(core,maximum+lipschitz*BELT_PI/count)+roundoff;}
 if(sample_count)*sample_count=count;if(sample_maximum)*sample_maximum=fmax(core,maximum);return upper;}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#undef belt_speed_certificate
static double belt_speed_certificate(const belt_body* b,int* sample_count,double* sample_maximum,double* coarse_upper){double start=belt_profile_now();double result=belt_speed_certificate_profile_impl(b,sample_count,sample_maximum,coarse_upper);belt_profile_finish(BELT_PROFILE_SPEED_CERTIFICATE,start);return result;}
#endif

static double belt_speed_bound(const belt_body* b){return belt_speed_certificate(b,NULL,NULL,NULL);}
static int belt_limit_speed(belt_body* b,double* upper){
 if(!b->speed_saturation)return 1;if(!isfinite(*upper))return belt_solver_fail(b,21);
 b->speed_limit[5]=fmax(b->speed_limit[5],*upper);double before=belt_kinetic(b),p0[3],p1[3],factor=1;belt_momentum(b,p0);
 /* A common positive scaling preserves contact normal inequalities and all
    internal velocity ratios. Every correction is checked against the same
    continuous upper bound; the threshold itself is never enlarged. */
 for(int attempt=0;attempt<4&&*upper>b->cfg.maximum_speed;++attempt){double inward=nextafter((double)b->cfg.maximum_speed,0),scale=nextafter(inward/(*upper),0);if(!isfinite(scale)||scale<=0||scale>=1)return belt_solver_fail(b,21);
  for(int j=0;j<BELT_D;++j)b->v[j]*=scale;factor*=scale;int samples=0;double maximum=0,coarse=0;*upper=belt_speed_certificate(b,&samples,&maximum,&coarse);b->solver_diag[19]=samples;b->solver_diag[20]=maximum;b->solver_diag[21]=*upper;if(samples>256){b->solver_diag[22]=coarse;b->solver_diag[23]=*upper;}if(!isfinite(*upper))return belt_solver_fail(b,21);
 }
 if(*upper>b->cfg.maximum_speed)return belt_solver_fail(b,21);double delta=belt_kinetic(b)-before;belt_momentum(b,p1);if(!isfinite(delta)||delta>0)return belt_solver_fail(b,21);
 if(factor<1)b->speed_limit[3]+=1;b->speed_limit[4]=fmin(b->speed_limit[4],factor);b->speed_limit[6]=fmax(b->speed_limit[6],*upper);b->speed_limit[7]+=delta;for(int j=0;j<3;++j)b->speed_limit[8+j]+=p1[j]-p0[j];b->diag[18]+=delta;return 1;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#define belt_residuals belt_residuals_profile_impl
#endif
static int belt_residuals(const belt_body* b,const double* q,belt_residual* r,double* min_derivative){
 const belt_basis_grids* grids=belt_get_basis_grids();
 int count=0;double dtheta=2*BELT_PI/BELT_Q,wr=2*BELT_PI*b->cfg.radial_hertz,wp=2*BELT_PI*b->cfg.perimeter_hertz;
 double reduced=b->cfg.core_mass*b->cfg.node_mass/(b->cfg.core_mass+b->cfg.node_mass);
 double kr=reduced*wr*wr*b->cal[0],cr=2*b->cfg.radial_damping*reduced*wr*b->cal[1];
 double edge=2*b->cfg.radial_rest_length*sin(BELT_PI/16),kp=.5*b->cfg.node_mass*wp*wp;
 double ea=kp*edge,ei=b->cfg.local_angle_balance_strength*b->cfg.node_mass*b->cfg.radial_rest_length*b->cfg.radial_rest_length;
 double time=wp>0?2*b->cfg.perimeter_damping/wp:0;*min_derivative=INFINITY;
 for(int i=0;i<BELT_Q;++i){const double* db=grids->material[1][i];const double* ddb=grids->material[2][i];
  belt_v u=belt_eval_basis(q,db),a=belt_eval_basis(q,ddb);double s=hypot(u.x,u.y);*min_derivative=fmin(*min_derivative,s);
  if(!(s>b->radius*.02)||!isfinite(s))return -1;
  belt_residual* stretch=&r[count++];memset(stretch,0,sizeof *stretch);stretch->value=s-b->radius;stretch->k=ea/b->radius*dtheta*b->cal[2];stretch->c=ea/b->radius*dtheta*time*b->cal[7];
  belt_residual* bend=&r[count++];memset(bend,0,sizeof *bend);double cross=belt_cross(u,a),s3=s*s*s;
  bend->value=cross/s3-1/b->radius;bend->k=ei*b->radius*dtheta*b->cal[3];bend->c=ei*b->radius*dtheta*time*b->cal[7];
  belt_v gs=belt_mul(1/s,u),gu=belt_sub(belt_mul(1/s3,(belt_v){a.y,-a.x}),belt_mul(3*cross/(s3*s*s),u));
  belt_v ga=belt_mul(1/s3,(belt_v){-u.y,u.x});
  for(int j=0;j<BELT_B;++j){stretch->j[2*j]=gs.x*db[j];stretch->j[2*j+1]=gs.y*db[j];bend->j[2*j]=gu.x*db[j]+ga.x*ddb[j];bend->j[2*j+1]=gu.y*db[j]+ga.y*ddb[j];}
 }
 for(int i=0;i<16;++i){const double* basis=grids->radial[i];belt_v p=belt_eval_basis(q,basis),core={q[34],q[35]},d=belt_sub(p,core);double length=hypot(d.x,d.y);
  if(length<1e-6)return -2;belt_residual* radial=&r[count++];memset(radial,0,sizeof *radial);radial->value=b->scale*length-b->cfg.radial_rest_length;radial->k=kr;radial->c=cr;
  belt_v g=belt_mul(b->scale/length,d);for(int j=0;j<BELT_B;++j){radial->j[2*j]=basis[j]*g.x;radial->j[2*j+1]=basis[j]*g.y;}radial->j[34]=-g.x;radial->j[35]=-g.y;
  if(b->short_bones){const double* next=grids->radial[i+1];d=belt_sub(belt_eval_basis(q,next),p);length=hypot(d.x,d.y);if(length<1e-6)return -3;
   belt_residual* short_edge=&r[count++];memset(short_edge,0,sizeof *short_edge);short_edge->value=b->scale*length-edge;short_edge->k=kp*b->cal[5];short_edge->c=2*b->cfg.perimeter_damping*.5*b->cfg.node_mass*wp*b->cal[6];g=belt_mul(b->scale/length,d);
   for(int j=0;j<BELT_B;++j){short_edge->j[2*j]=(next[j]-basis[j])*g.x;short_edge->j[2*j+1]=(next[j]-basis[j])*g.y;}}
 }
 double target=BELT_PI*b->radius*b->radius;belt_residual* area=&r[count++];memset(area,0,sizeof *area);double actual=belt_area(q,area->j);if(actual<=target*.05)return -4;
 double old_target=8*b->cfg.radial_rest_length*b->cfg.radial_rest_length*sin(2*BELT_PI/16),area_scale=old_target/target;
 area->value=fmin(0,actual-target);if(actual>=target)memset(area->j,0,sizeof area->j);area->k=b->cfg.area_strength/(old_target*b->cfg.radial_rest_length*sin(2*BELT_PI/16))*area_scale*area_scale*b->cal[4];
 if(b->shape_guard){
  /* Smooth one-sided divergent barriers. Both scalars are rigid-objective;
     their exact gradients include equal core reactions. No hidden damping. */
  for(int i=0;i<BELT_Q;++i){const double* basis=grids->material[0][i];const double* db=grids->material[1][i];
   belt_v rel=belt_sub(belt_eval_basis(q,basis),(belt_v){q[34],q[35]}),u=belt_eval_basis(q,db);double length=hypot(rel.x,rel.y),cross=belt_cross(rel,u);
   for(int kind=0;kind<2;++kind){belt_residual* barrier=&r[count++];memset(barrier,0,sizeof *barrier);
    double unit=kind?b->radius*b->radius:b->radius,x=(kind?cross:length)/unit,hard=kind?1e-4:.04,on=kind?.12:.18;
    if(x<=hard)return -5;barrier->k=(b->cfg.core_mass+16*b->cfg.node_mass)*pow(2*BELT_PI*6,2)*b->radius*b->radius/BELT_Q;
    if(x>=on)continue;double d=on-x,gap=x-hard;barrier->value=d*d/gap;double slope=(-2*d/gap-d*d/(gap*gap))/unit;
    /* E=.5*k*r(x)^2, r=A^2/g-2A+g, g=x-hard, A=on-hard.
       The positive scalar term k*r*r'' was missing from the GN curvature.
       Its ratio to k*r'^2 is 2*A^2/(A+g)^2. Only the solver metric changes;
       energy, force, physical k/c and activation limits are unchanged. */
    double active_span=on-hard;barrier->scalar_curvature=2*active_span*active_span/((active_span+gap)*(active_span+gap));
    belt_v gr=kind?(belt_v){u.y,-u.x}:belt_mul(1/length,rel),gu=kind?(belt_v){-rel.y,rel.x}:(belt_v){0,0};
    for(int j=0;j<BELT_B;++j){barrier->j[2*j]=slope*(gr.x*basis[j]+gu.x*db[j]);barrier->j[2*j+1]=slope*(gr.y*basis[j]+gu.y*db[j]);}barrier->j[34]=-slope*gr.x;barrier->j[35]=-slope*gr.y;
   }
  }
 }
 return count;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#undef belt_residuals
static int belt_residuals(const belt_body* b,const double* q,belt_residual* r,double* min_derivative){double start=belt_profile_now();int result=belt_residuals_profile_impl(b,q,r,min_derivative);belt_profile_finish(BELT_PROFILE_RESIDUALS,start);return result;}
#endif

static double belt_elastic_energy(const belt_body* b){belt_residual r[BELT_MAX_RESIDUALS];double md,e=0;int n=belt_residuals(b,b->q,r,&md);if(n<0)return INFINITY;for(int i=0;i<n;++i)e+=.5*r[i].k*r[i].value*r[i].value;return e;}
static int belt_material_domain_valid(const belt_body* b,const double* q){belt_residual r[BELT_MAX_RESIDUALS];double md;if(belt_residuals(b,q,r,&md)<0)return 0;double old_area_scale=8*b->cfg.radial_rest_length*b->cfg.radial_rest_length*sin(2*BELT_PI/16)/(BELT_PI*b->radius*b->radius);return belt_area(q,NULL)*old_area_scale>=b->cfg.minimum_area;}
static double belt_guard_energy(const belt_body* b){if(!b->shape_guard)return 0;belt_residual r[BELT_MAX_RESIDUALS];double md,e=0;int n=belt_residuals(b,b->q,r,&md);if(n<0)return INFINITY;for(int i=n-2*BELT_Q;i<n;++i)e+=.5*r[i].k*r[i].value*r[i].value;return e;}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#define belt_objective belt_objective_profile_impl
#endif
static double belt_objective(const belt_body* b,const double* q,const double* old,const double* predicted,const belt_residual* previous,double dt,double* gradient,double hessian[BELT_D][BELT_D],double* elastic,double* damping,double* min_derivative){
 belt_residual r[BELT_MAX_RESIDUALS];int n=belt_residuals(b,q,r,min_derivative);if(n<0)return INFINITY;double objective=0;*elastic=*damping=0;
 if(gradient)memset(gradient,0,BELT_D*sizeof(double));if(hessian)memset(hessian,0,BELT_D*BELT_D*sizeof(double));
 for(int j=0;j<BELT_D;++j){double delta=q[j]-predicted[j],k=b->mass[j]/(dt*dt);objective+=.5*k*delta*delta;if(gradient)gradient[j]=k*delta;if(hessian)hessian[j][j]=k;}
 for(int i=0;i<n;++i){double delta=r[i].value-previous[i].value,elastic_i=.5*r[i].k*r[i].value*r[i].value,damping_i=.5*r[i].c/dt*delta*delta;
  objective+=elastic_i+damping_i;*elastic+=elastic_i;*damping+=damping_i;
  if(gradient){double force=r[i].k*r[i].value+r[i].c/dt*delta,k=r[i].k*(1+r[i].scalar_curvature)+r[i].c/dt;
   for(int j=0;j<BELT_D;++j){gradient[j]+=force*r[i].j[j];if(hessian)for(int l=0;l<=j;++l)hessian[j][l]+=k*r[i].j[j]*r[i].j[l];}}
 }
 (void)old;return objective;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#undef belt_objective
static double belt_objective(const belt_body* b,const double* q,const double* old,const double* predicted,const belt_residual* previous,double dt,double* gradient,double hessian[BELT_D][BELT_D],double* elastic,double* damping,double* min_derivative){double start=belt_profile_now();double result=belt_objective_profile_impl(b,q,old,predicted,previous,dt,gradient,hessian,elastic,damping,min_derivative);belt_profile_finish(hessian?BELT_PROFILE_OBJECTIVE_MATRIX:BELT_PROFILE_OBJECTIVE_VALUE,start);return result;}
#endif

/* Lower-triangular Powell-damped BFGS. The accepted-point secant captures
   geometric curvature omitted by GN without altering objective or forces.
   Powell damping preserves SPD; every numerical refusal resets to current GN. */
static int belt_bfgs_update(double matrix[BELT_D][BELT_D],const double* s,const double* y){
 double bs[BELT_D]={0},adjusted[BELT_D],sbs=0,sy=0;
 for(int j=0;j<BELT_D;++j){for(int l=0;l<BELT_D;++l)bs[j]+=(j>=l?matrix[j][l]:matrix[l][j])*s[l];sbs+=s[j]*bs[j];sy+=s[j]*y[j];}
 if(!isfinite(sbs)||!isfinite(sy)||sbs<=1e-28)return 0;double theta=sy<.2*sbs?.8*sbs/(sbs-sy):1;
 sy=0;for(int j=0;j<BELT_D;++j){adjusted[j]=theta*y[j]+(1-theta)*bs[j];sy+=s[j]*adjusted[j];}if(!isfinite(sy)||sy<=0)return 0;
 for(int j=0;j<BELT_D;++j)for(int l=0;l<=j;++l){matrix[j][l]+=adjusted[j]*adjusted[l]/sy-bs[j]*bs[l]/sbs;if(!isfinite(matrix[j][l]))return 0;}return 1;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#define belt_integrate belt_integrate_profile_impl
#endif
static int belt_integrate(belt_body* b,double dt,const double* force){
 double energy_before=belt_kinetic(b)+belt_elastic_energy(b),p_before[3];belt_momentum(b,p_before);
 double old[BELT_D],predicted[BELT_D],gradient[BELT_D],matrix[BELT_D][BELT_D],step[BELT_D];memcpy(old,b->q,sizeof old);
 belt_residual previous[BELT_MAX_RESIDUALS];double derivative;int previous_count=belt_residuals(b,old,previous,&derivative);if(previous_count<0)return belt_solver_fail(b,30-previous_count);
 for(int j=0;j<BELT_D;++j)predicted[j]=old[j]+dt*b->v[j]+dt*dt*force[j]/b->mass[j];
 double elastic=0,damping=0,residual=INFINITY;int iteration;
 double quasi[BELT_D][BELT_D],gn[BELT_D][BELT_D],last_q[BELT_D],last_gradient[BELT_D];bool history=false,accelerate=false,acceleration_blocked=false;double previous_residual=INFINITY;
 for(iteration=0;iteration<40;++iteration){double value=belt_objective(b,b->q,old,predicted,previous,dt,gradient,matrix,&elastic,&damping,&derivative);b->solver_diag[5]=iteration;b->solver_diag[8]=value;if(!isfinite(value))return belt_solver_fail(b,2);
  residual=0;for(int j=0;j<BELT_D;++j){residual=fmax(residual,fabs(gradient[j])*dt*dt/b->mass[j]);step[j]=-gradient[j];}
  b->solver_diag[7]=residual;if(residual<2e-9)break;
  if(b->shape_guard){
   /* Retain the original trajectory while GN is sufficient. In the latter
      half of the fixed budget, use its observed convergence rate to detect
      a predicted residual-gate failure and enable secant acceleration. */
   if(!accelerate&&!acceleration_blocked&&iteration>=20&&residual>2e-7&&isfinite(previous_residual)&&previous_residual>0){double ratio=fmin(1,residual/previous_residual);if(residual*pow(ratio,40-iteration)>2e-7)accelerate=true;}
   previous_residual=residual;memcpy(gn,matrix,sizeof gn);
   if(accelerate){if(history){double s[BELT_D],y[BELT_D];for(int j=0;j<BELT_D;++j){s[j]=b->q[j]-last_q[j];y[j]=gradient[j]-last_gradient[j];}
     if(belt_bfgs_update(quasi,s,y))b->solver_diag[17]+=1;else{memcpy(quasi,gn,sizeof quasi);b->solver_diag[18]+=1;}}
    else memcpy(quasi,gn,sizeof quasi);memcpy(matrix,quasi,sizeof matrix);memcpy(last_q,b->q,sizeof last_q);memcpy(last_gradient,gradient,sizeof last_gradient);history=true;
   }
  }
  if(!belt_cholesky(matrix,step,step)){
   if(!b->shape_guard)return belt_solver_fail(b,3);memcpy(quasi,gn,sizeof quasi);memcpy(matrix,gn,sizeof matrix);for(int j=0;j<BELT_D;++j)step[j]=-gradient[j];b->solver_diag[18]+=1;if(!belt_cholesky(matrix,step,step))return belt_solver_fail(b,3);
  }
  double directional=0;for(int j=0;j<BELT_D;++j)directional+=gradient[j]*step[j];b->solver_diag[9]=directional;bool accepted=false;
  if(b->shape_guard&&(!isfinite(directional)||directional>=0)){memcpy(quasi,gn,sizeof quasi);memcpy(matrix,gn,sizeof matrix);for(int j=0;j<BELT_D;++j)step[j]=-gradient[j];b->solver_diag[18]+=1;if(!belt_cholesky(matrix,step,step))return belt_solver_fail(b,3);directional=0;for(int j=0;j<BELT_D;++j)directional+=gradient[j]*step[j];b->solver_diag[9]=directional;if(!isfinite(directional)||directional>=0)return belt_solver_fail(b,3);}
  int factor_offset=0;bool retried_gn=false;
  for(int ls=0;ls<20;++ls){double trial[BELT_D],factor=ldexp(1.0,-(ls-factor_offset)),e,d,md;for(int j=0;j<BELT_D;++j)trial[j]=b->q[j]+factor*step[j];
   b->solver_diag[6]=ls;b->solver_diag[11]=factor;if(!belt_guard_path(b,b->q,trial,1)){if(b->guard[9]==BELT_GUARD_BUDGET)return belt_solver_fail(b,7);
    /* Secant curvature is not a feasibility model. A refused quasi-Newton
       proposal retries the current GN direction within the same 20 trials. */
    if(b->shape_guard&&accelerate&&!retried_gn&&iteration>0){memcpy(quasi,gn,sizeof quasi);memcpy(matrix,gn,sizeof matrix);for(int j=0;j<BELT_D;++j)step[j]=-gradient[j];b->solver_diag[18]+=1;if(!belt_cholesky(matrix,step,step))return belt_solver_fail(b,3);directional=0;for(int j=0;j<BELT_D;++j)directional+=gradient[j]*step[j];b->solver_diag[9]=directional;retried_gn=true;factor_offset=ls+1;accelerate=false;acceleration_blocked=true;history=false;}
    continue;}
   double next=belt_objective(b,trial,old,predicted,previous,dt,NULL,NULL,&e,&d,&md);b->solver_diag[10]=next;
   if(isfinite(next)&&next<=value+1e-4*factor*directional){memcpy(b->q,trial,sizeof trial);accepted=true;break;}}
  if(!accepted){
   /* The current point is already certified. If it meets the same residual
      gate used after 40 iterations, keep it when the bounded descent search
      cannot improve it. No rejected/uphill trial is ever accepted. The 2e-9
      target remains the normal early exit; 2e-7 is the unchanged hard gate. */
   if(b->shape_guard&&residual<=2e-7){b->solver_diag[16]+=1;break;}
   return belt_solver_fail(b,4);
  }
 }
 /* At the iteration cap q already contains the last accepted update, whereas
    residual still describes its predecessor. Evaluate the actual endpoint;
    this adds no solve iteration and retains the same acceptance threshold. */
 if(b->shape_guard&&iteration==40){double value=belt_objective(b,b->q,old,predicted,previous,dt,gradient,NULL,&elastic,&damping,&derivative);b->solver_diag[8]=value;if(!isfinite(value))return belt_solver_fail(b,2);residual=0;for(int j=0;j<BELT_D;++j)residual=fmax(residual,fabs(gradient[j])*dt*dt/b->mass[j]);b->solver_diag[7]=residual;}
 if(residual>2e-7)return belt_solver_fail(b,5);if(!belt_guard_path(b,old,b->q,2))return belt_solver_fail(b,6);for(int j=0;j<BELT_D;++j)b->v[j]=(b->q[j]-old[j])/dt;
 b->stats[32]=elastic;b->stats[33]+=damping;b->stats[34]=fmax(b->stats[34],residual);b->stats[54]=derivative;b->stats[56]=fmax(b->stats[56],b->shape_guard&&iteration==40?40:iteration+1);
 belt_residual current[BELT_MAX_RESIDUALS];int count=belt_residuals(b,b->q,current,&derivative);double material[BELT_D]={0};
 double guard_force[2]={0};for(int i=0;i<count;++i){double delta=current[i].value-previous[i].value,rate=delta/dt,linear_delta=0;for(int j=0;j<BELT_D;++j)linear_delta+=current[i].j[j]*(b->q[j]-old[j]);b->diag[8]+=current[i].c*rate*linear_delta;b->diag[9]+=current[i].c*rate*delta;double f=-(current[i].k*current[i].value+current[i].c/dt*(current[i].value-previous[i].value));for(int j=0;j<BELT_D;++j)material[j]+=f*current[i].j[j];
  if(b->shape_guard&&i>=count-2*BELT_Q){guard_force[0]+=f*current[i].j[34];guard_force[1]+=f*current[i].j[35];b->guard[17]+=f*linear_delta;}}
 if(b->shape_guard)b->guard[16]=fmax(b->guard[16],hypot(guard_force[0],guard_force[1]));
 double work=0,torque=0;for(int j=0;j<BELT_D;++j)work+=force[j]*(b->q[j]-old[j]);for(int j=0;j<BELT_D;j+=2)torque+=b->q[j]*force[j+1]-b->q[j+1]*force[j];b->diag[27]+=work;b->diag[28]+=belt_kinetic(b)+belt_elastic_energy(b)-energy_before-work;double p_after[3];belt_momentum(b,p_after);b->diag[44]+=p_after[0]-p_before[0]-dt*(force[0]+force[34]);b->diag[45]+=p_after[1]-p_before[1]-dt*(force[1]+force[35]);b->diag[46]+=p_after[2]-p_before[2]-dt*torque;
 b->stats[59]=material[0]+material[34];b->stats[60]=material[1]+material[35];b->stats[61]=0;for(int j=0;j<BELT_D;j+=2)b->stats[61]+=b->q[j]*material[j+1]-b->q[j+1]*material[j];return 1;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#undef belt_integrate
static int belt_integrate(belt_body* b,double dt,const double* force){double start=belt_profile_now();int result=belt_integrate_profile_impl(b,dt,force);belt_profile_finish(BELT_PROFILE_INTEGRATE,start);return result;}
#endif

/* Read-only active-force telemetry. The enclosing frame backup makes a
   refused substep invisible; the final successful substep replaces the ledger. */
static void belt_force_ledger_begin(belt_body* b,double attempt,int substep){memset(b->force_ledger,0,sizeof b->force_ledger);b->force_ledger[0]=1;b->force_ledger[1]=1024;b->force_ledger[2]=attempt;b->force_ledger[3]=substep;b->force_ledger[6]=6;b->force_ledger[7]=64;}
static void belt_force_ledger_add(belt_body* b,double x,double y,double fx,double fy,int kind,double material){
 int n=(int)b->force_ledger[4];if(n>=97)return;double* row=b->force_ledger+8+6*n;row[0]=x;row[1]=y;row[2]=fx;row[3]=fy;row[4]=kind;row[5]=material;b->force_ledger[4]=n+1;b->force_ledger[5]=(double)((int)b->force_ledger[5]|(1<<(kind-1)));
}
#include "dango_belt_muscle.h"
/* Exact normalized material-interval integral: the selected world half is
   located on the continuous curve, never on the sixteen scaffold points. */
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#define belt_region_force belt_region_force_profile_impl
#endif
static int belt_region_force(belt_body* b,double* force,int axis,int sign,double amount,double* action){
 double bounds[4],foot;belt_bounds(b->q,bounds,&foot);double middle=.5*(bounds[axis]+bounds[axis+2]),roots[64],cuts[66];int n=belt_roots(b->q,axis,0,middle,roots),used=0;cuts[used++]=0;
 for(int i=0;i<n;++i)if(roots[i]>1e-9&&roots[i]<2*BELT_PI-1e-9)cuts[used++]=roots[i];cuts[used++]=2*BELT_PI;
 double integrals[BELT_B]={0},measure=0;
 for(int i=0;i<used-1;++i){double a=cuts[i],z=cuts[i+1];belt_v p=belt_eval(b->q,.5*(a+z),0);if(sign*((axis?p.y:p.x)-middle)<0)continue;
  measure+=z-a;integrals[0]+=z-a;for(int k=1;k<=BELT_K;++k){integrals[2*k-1]+=(sin(k*z)-sin(k*a))/k;integrals[2*k]+=(cos(k*a)-cos(k*z))/k;}}
 if(measure<=0)return 0;belt_v mean={0};for(int j=0;j<BELT_B;++j){double w=integrals[j]/measure;force[2*j+axis]+=amount*w;mean.x+=b->q[2*j]*w;mean.y+=b->q[2*j+1]*w;}
 action[0]=mean.x;action[1]=mean.y;
 /* Reporting only: subdivide the same selected analytic intervals into sixteen
    material bins. Their integrated centroids and loads preserve net torque. */
 for(int part=0;part<16;++part){double lo=2*BELT_PI*part/16,hi=2*BELT_PI*(part+1)/16,segment[BELT_B]={0},weight=0;
  for(int i=0;i<used-1;++i){double a=fmax(lo,cuts[i]),z=fmin(hi,cuts[i+1]);if(z<=a)continue;belt_v p=belt_eval(b->q,.5*(cuts[i]+cuts[i+1]),0);if(sign*((axis?p.y:p.x)-middle)<0)continue;
   weight+=z-a;segment[0]+=z-a;for(int k=1;k<=BELT_K;++k){segment[2*k-1]+=(sin(k*z)-sin(k*a))/k;segment[2*k]+=(cos(k*a)-cos(k*z))/k;}}
  if(weight>0){belt_v position={0};for(int j=0;j<BELT_B;++j){position.x+=b->q[2*j]*segment[j]/weight;position.y+=b->q[2*j+1]*segment[j]/weight;}double load=amount*weight/measure;belt_force_ledger_add(b,position.x,position.y,axis?0:load,axis?load:0,axis?2:1,part);}}
 return 1;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#undef belt_region_force
static int belt_region_force(belt_body* b,double* force,int axis,int sign,double amount,double* action){double start=belt_profile_now();int result=belt_region_force_profile_impl(b,force,axis,sign,amount,action);belt_profile_finish(BELT_PROFILE_FORCES,start);return result;}
#endif

/* Eight actual point loads, formed from consecutive groups of eight old
   64-point radial reference samples. References are never mapped or reported
   as loads; each group preserves Fx and scales Fy by two thirds at its material midpoint. */
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#define belt_radial_force belt_radial_force_profile_impl
#endif
static int belt_radial_force(belt_body* b,double* force,int sign,double budget){
 if((sign!=1&&sign!=-1)||!isfinite(budget)||budget<=0)return 0;
 double bounds[4],foot;belt_bounds(b->q,bounds,&foot);double middle=.5*(bounds[0]+bounds[2]),roots[64],cuts[66],lo[65],hi[65],measure=0;int n=belt_roots(b->q,0,0,middle,roots),used=0,intervals=0;cuts[used++]=0;
 for(int i=0;i<n;++i)if(roots[i]>1e-9&&roots[i]<2*BELT_PI-1e-9)cuts[used++]=roots[i];cuts[used++]=2*BELT_PI;
 for(int i=0;i<used-1;++i){belt_v p=belt_eval(b->q,.5*(cuts[i]+cuts[i+1]),0);if(sign*(p.x-middle)<0)continue;lo[intervals]=cuts[i];hi[intervals++]=cuts[i+1];measure+=cuts[i+1]-cuts[i];}
 if(!isfinite(measure)||measure<=0)return 0;
 double groupx[8]={0},groupy[8]={0},positions[8],refx=0,refy=0,netx=0,nety=0,torque=0,magnitudes=0,load=budget/64;int interval=0;double before=0;
 for(int i=0;i<64;++i){double distance=measure*(i+.5)/64;while(interval<intervals-1&&distance>before+hi[interval]-lo[interval]){before+=hi[interval]-lo[interval];++interval;}
  double theta=lo[interval]+distance-before;belt_v p=belt_eval(b->q,theta,0);double rx=p.x-b->q[34],ry=p.y-b->q[35],length=hypot(rx,ry);
  if(!isfinite(length)||length<=1e-8*b->radius)return 0;double fx=load*rx/length,fy=load*ry/length;if(!isfinite(fx)||!isfinite(fy))return 0;
  groupx[i/8]+=fx;groupy[i/8]+=(2.0/3)*fy;refx+=fx;refy+=fy;}
 interval=0;before=0;
 for(int i=0;i<8;++i){double distance=measure*(i+.5)/8;while(interval<intervals-1&&distance>before+hi[interval]-lo[interval]){before+=hi[interval]-lo[interval];++interval;}positions[i]=lo[interval]+distance-before;}
 for(int i=0;i<8;++i){double basis[BELT_B];belt_basis(positions[i],0,basis);belt_v p=belt_eval_basis(b->q,basis);double fx=groupx[i],fy=groupy[i];
  for(int j=0;j<BELT_B;++j){force[2*j]+=basis[j]*fx;force[2*j+1]+=basis[j]*fy;}belt_force_ledger_add(b,p.x,p.y,fx,fy,5,positions[i]);
  netx+=fx;nety+=fy;torque+=(p.x-b->q[34])*fy-(p.y-b->q[35])*fx;magnitudes+=hypot(fx,fy);}
 b->radial_diag[3]+=1;b->radial_diag[4]=8;b->radial_diag[5]=budget;b->radial_diag[6]=magnitudes;b->radial_diag[7]=netx;b->radial_diag[8]=nety;b->radial_diag[9]=fmax(b->radial_diag[9],fabs(torque));b->radial_diag[10]=fmax(b->radial_diag[10],fabs(netx-refx));b->radial_diag[11]=fmax(b->radial_diag[11],fabs(nety-(2.0/3)*refy));b->radial_diag[12]=refx;b->radial_diag[15]=refy;return 1;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#undef belt_radial_force
static int belt_radial_force(belt_body* b,double* force,int sign,double budget){double start=belt_profile_now();int result=belt_radial_force_profile_impl(b,force,sign,budget);belt_profile_finish(BELT_PROFILE_FORCES,start);return result;}
#endif


/* External forces at existing scaffold endpoints; J^T preserves virtual work. */
static void belt_rear_clear_last(belt_body* b){for(int i=4;i<=8;++i)b->rear_diag[i]=0;b->rear_diag[11]=b->rear_diag[14]=b->rear_diag[15]=0;}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#define belt_rear_force belt_rear_force_profile_impl
#endif
static int belt_rear_force(belt_body* b,double* force,int sign,double source_fy){
 belt_rear_clear_last(b);if((sign!=1&&sign!=-1)||!isfinite(source_fy))return 0;double budget=fabs(source_fy)/3;if(budget==0)return 1;
 double bounds[4],foot;belt_bounds(b->q,bounds,&foot);double mid=.5*(bounds[0]+bounds[2]);int selected[16],count=0;
 for(int i=0;i<16;++i){belt_v outer=belt_eval(b->q,2*BELT_PI*i/16,0);double x=b->scale*outer.x+(1-b->scale)*b->q[34];if(sign*(x-mid)<-1e-9*b->radius)selected[count++]=i;}
 if(count==0)return 0;double fy=-budget/count,nety=0,torque=0;
 for(int k=0;k<count;++k){double theta=2*BELT_PI*selected[k]/16,basis[BELT_B];belt_basis(theta,0,basis);belt_v outer=belt_eval_basis(b->q,basis),point={b->scale*outer.x+(1-b->scale)*b->q[34],b->scale*outer.y+(1-b->scale)*b->q[35]};
  for(int j=0;j<BELT_B;++j)force[2*j+1]+=b->scale*basis[j]*fy;force[35]+=(1-b->scale)*fy;
  belt_force_ledger_add(b,point.x,point.y,0,fy,6,theta);nety+=fy;torque+=(point.x-b->q[34])*fy;
 }
 b->rear_diag[3]+=1;b->rear_diag[4]=count;b->rear_diag[5]=budget;b->rear_diag[6]=-nety;b->rear_diag[8]=nety;b->rear_diag[9]=fmax(b->rear_diag[9],fabs(torque));b->rear_diag[10]=fmax(b->rear_diag[10],fabs(-nety-budget));b->rear_diag[11]=source_fy;b->rear_diag[14]=sign;b->rear_diag[15]=mid;return 1;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#undef belt_rear_force
static int belt_rear_force(belt_body* b,double* force,int sign,double source_fy){double start=belt_profile_now();int result=belt_rear_force_profile_impl(b,force,sign,source_fy);belt_profile_finish(BELT_PROFILE_FORCES,start);return result;}
#endif


/* Rear-localized material traction only: no target geometry, mass, or extra
   energy term. Fixed preview: tail pull .18, front support .40, tail squeeze
   4.0 and lift .08. Rigid force/torque components are projected out below. */
static double belt_whale_weight(double value){double t=fmax(0,fmin(1,value));return t*t*(3-2*t);}
static belt_v belt_whale_raw(double horizontal_force,double gain,double x,double y){
 belt_v result={0};if(gain==0)return result;
 double sign=gain>0?1:-1,a=fabs(gain),q=sign*x;
 double rear=belt_whale_weight((-q-.15)/.60),front=belt_whale_weight((q-.05)/.55);
 result.x=-sign*horizontal_force*a*.18*rear;
 result.y=horizontal_force*a*((.40*front-4.0*rear)*y+.08*q*q*q);return result;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#define belt_whale_force belt_whale_force_profile_impl
#endif
static int belt_whale_force(belt_body* b,double* force){
 double bounds[4],foot;belt_bounds(b->q,bounds,&foot);double hx=.5*(bounds[2]-bounds[0]),hy=.5*(bounds[3]-bounds[1]);
 if(!isfinite(hx)||!isfinite(hy)||hx<=1e-8*b->radius||hy<=1e-8*b->radius)return 0;
 double cx=.5*(bounds[0]+bounds[2]),cy=.5*(bounds[1]+bounds[3]);belt_v p[64],t[64],meanp={0},meant={0};
 for(int i=0;i<64;++i){p[i]=belt_eval(b->q,2*BELT_PI*i/64,0);double x=(p[i].x-cx)/hx,y=(p[i].y-cy)/hy;
  t[i]=belt_whale_raw(b->cfg.horizontal_force,b->whale_gain,x,y);
  meanp.x+=p[i].x/64;meanp.y+=p[i].y/64;meant.x+=t[i].x/64;meant.y+=t[i].y/64;}
 double inertia=0,torque=0;for(int i=0;i<64;++i){p[i].x-=meanp.x;p[i].y-=meanp.y;t[i].x-=meant.x;t[i].y-=meant.y;inertia+=(p[i].x*p[i].x+p[i].y*p[i].y)/64;torque+=(p[i].x*t[i].y-p[i].y*t[i].x)/64;}
 if(!isfinite(inertia)||inertia<=1e-16*b->radius*b->radius||!isfinite(torque))return 0;
 double omega=torque/inertia,netx=0,nety=0,nett=0;
 for(int i=0;i<64;++i){double basis[BELT_B];belt_basis(2*BELT_PI*i/64,0,basis);t[i].x+=omega*p[i].y;t[i].y-=omega*p[i].x;
  if(!isfinite(t[i].x)||!isfinite(t[i].y))return 0;netx+=t[i].x/64;nety+=t[i].y/64;nett+=(p[i].x*t[i].y-p[i].y*t[i].x)/64;
  belt_force_ledger_add(b,p[i].x+meanp.x,p[i].y+meanp.y,t[i].x/64,t[i].y/64,3,2*BELT_PI*i/64);b->whale_diag[9]=fmax(b->whale_diag[9],hypot(t[i].x,t[i].y));for(int j=0;j<BELT_B;++j){force[2*j]+=basis[j]*t[i].x/64;force[2*j+1]+=basis[j]*t[i].y/64;}}
 b->whale_diag[5]=fmax(b->whale_diag[5],fabs(netx));b->whale_diag[6]=fmax(b->whale_diag[6],fabs(nety));b->whale_diag[7]=fmax(b->whale_diag[7],fabs(nett));
 for(int j=0;j<BELT_D;++j)b->whale_diag[10]=fmax(b->whale_diag[10],fabs(force[j]));b->whale_diag[4]+=1;return 1;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#undef belt_whale_force
static int belt_whale_force(belt_body* b,double* force){double start=belt_profile_now();int result=belt_whale_force_profile_impl(b,force);belt_profile_finish(BELT_PROFILE_FORCES,start);return result;}
#endif

static int belt_whale_update(belt_body* b,double h,const float* c,bool accepted,double fy,double* force){
 int reason=(!b->grounded?1:0)|(b->dragging?2:0)|(accepted?4:0)|((c[1]!=0||c[6]!=0||b->ramp!=0||fy!=0)?8:0)|(c[8]!=0?16:0);b->whale_diag[11]=reason;
 double target=reason?0:c[0],delta=target-b->whale_gain,limit=h/(target==0?.20:.15);if(c[8]!=0)b->whale_gain=0;else b->whale_gain+=fmax(-limit,fmin(limit,delta));
 b->whale_diag[3]=b->whale_diag[13]=b->whale_gain;return reason||b->whale_gain==0?1:belt_whale_force(b,force);
}

typedef struct belt_contact {double basis[BELT_B],normal,tangent;} belt_contact;
static int belt_contact_rows(const belt_body* b,belt_contact* rows,double tolerance){
 double roots[64];int nr=belt_roots(b->q,1,1,0,roots),n=0;
 for(int i=0;i<nr;++i){belt_v p=belt_eval(b->q,roots[i],0),dd=belt_eval(b->q,roots[i],2);if(p.y-b->cfg.ground_height<=tolerance&&dd.y>=-1e-9){memset(&rows[n],0,sizeof rows[n]);belt_basis(roots[i],0,rows[n++].basis);}}
 return n;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#define belt_contact_solve belt_contact_solve_profile_impl
#endif
static int belt_contact_solve(belt_body* b){
 double elastic_before=belt_elastic_energy(b),gravity_before=belt_gravity_energy(b),p_projection[3];belt_momentum(b,p_projection);
 double bounds[4],foot;belt_bounds(b->q,bounds,&foot);double before_gap=bounds[1]-b->cfg.ground_height;b->stats[51]=fmax(b->stats[51],-before_gap);
 /* Position-only projection, repeatedly rebuilding true curve extrema. */
 for(int it=0;it<32;++it){double penetration=b->cfg.ground_height-bounds[1];if(penetration<=2e-9)break;
  double basis[BELT_B];belt_basis(foot,0,basis);double effective=0;for(int j=0;j<BELT_B;++j)effective+=basis[j]*basis[j]/b->mass[2*j+1];double lambda=penetration/effective;
  double trial[BELT_D];memcpy(trial,b->q,sizeof trial);for(int j=0;j<BELT_B;++j)trial[2*j+1]+=lambda*basis[j]/b->mass[2*j+1];
  int guarded=belt_guard_path(b,b->q,trial,3);if(guarded&&b->shape_guard&&!belt_material_domain_valid(b,trial)){guarded=0;b->guard[8]+=1;b->guard[9]=BELT_GUARD_MATERIAL_DOMAIN;b->guard[10]=3;}
  if(!guarded){
   if(b->guard[9]==BELT_GUARD_BUDGET)return belt_solver_fail(b,8);
   /* A common translation of core and belt is always shape-safe and resolves
      the complete floor in one step. Its potential work is recorded below. */
   memcpy(trial,b->q,sizeof trial);trial[1]+=penetration;trial[35]+=penetration;b->guard[18]+=1;
   if(!belt_guard_path(b,b->q,trial,3)||!belt_material_domain_valid(b,trial))return belt_solver_fail(b,9);
  }
  for(int j=0;j<BELT_D;++j)b->stats[35]=fmax(b->stats[35],fabs(trial[j]-b->q[j]));memcpy(b->q,trial,sizeof trial);
  /* Projection changes only y coefficients. Refresh y extrema after every
     actual update, including the final permitted update; x is unchanged. */
  belt_bounds_y(b->q,bounds,&foot);}
 b->stats[31]=fmax(0,b->cfg.ground_height-bounds[1]);b->solver_diag[13]=b->stats[31];if(b->stats[31]>1e-7)return belt_solver_fail(b,10);
 b->diag[15]+=belt_elastic_energy(b)-elastic_before;b->diag[16]+=belt_gravity_energy(b)-gravity_before;b->diag[17]=fmax(b->diag[17],b->stats[35]);double p_projected[3];belt_momentum(b,p_projected);b->diag[19]+=p_projected[2]-p_projection[2];
 belt_contact rows[64];int n=belt_contact_rows(b,rows,2e-6);b->stats[25]=n;b->grounded=n>0;
 double before=belt_kinetic(b),normal=0,tangent=0,residual=0;int it;
 /* Non-associated Coulomb: normal Signorini complementarity first, then
    tangential maximum dissipation at that normal multiplier. All blocks
    read and update the same generalized velocity until a fixed point. */
 for(it=0;it<300;++it){residual=0;for(int i=0;i<n;++i){double kn=0,vn=0,vt=0;for(int j=0;j<BELT_B;++j){double w=rows[i].basis[j];kn+=w*w/b->mass[2*j];vt+=w*b->v[2*j];vn+=w*b->v[2*j+1];}
   double pn=rows[i].normal,pt=rows[i].tangent,mu=b->cfg.friction,next_n=fmax(0,pn-vn/kn),next_t=fmax(-mu*next_n,fmin(mu*next_n,pt-vt/kn));
   double dn=next_n-pn,dt=next_t-pt;residual=fmax(residual,kn*hypot(dn,dt));rows[i].normal=next_n;rows[i].tangent=next_t;
   for(int j=0;j<BELT_B;++j){double w=rows[i].basis[j]/b->mass[2*j];b->v[2*j]+=w*dt;b->v[2*j+1]+=w*dn;}}
  if(residual<2e-8)break;}
 b->solver_diag[12]=residual;if(residual>2e-6)return belt_solver_fail(b,11);
 for(int i=0;i<n;++i){normal+=rows[i].normal;tangent+=fabs(rows[i].tangent);b->stats[53]=fmax(b->stats[53],fabs(rows[i].tangent)-b->cfg.friction*rows[i].normal);}
 double after=belt_kinetic(b);b->stats[26]+=before;b->stats[27]+=after;b->stats[28]+=normal;b->stats[29]+=tangent;b->stats[30]=fmax(b->stats[30],residual);b->stats[52]+=after-before;
 b->diag[12]+=before;b->diag[13]+=after;b->diag[14]+=after-before;double p_contact[3];belt_momentum(b,p_contact);for(int j=0;j<3;++j)b->diag[35+j]+=p_contact[j]-p_projected[j];
 if(!(after<=before+1e-9*(1+before)))return belt_solver_fail(b,12);return 1;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#undef belt_contact_solve
static int belt_contact_solve(belt_body* b){double start=belt_profile_now();int result=belt_contact_solve_profile_impl(b);belt_profile_finish(BELT_PROFILE_CONTACT_TOTAL,start);return result;}
#endif

#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#define belt_read_stats belt_read_stats_profile_impl
#endif
static void belt_read_stats(belt_body* b){
 double* s=b->stats,total=b->cfg.core_mass+16.0*b->cfg.node_mass,ring=16.0*b->cfg.node_mass,bounds[4],foot;belt_bounds(b->q,bounds,&foot);
 s[0]=b->q[34];s[1]=b->q[35];s[2]=b->q[0];s[3]=b->q[1];s[4]=(ring*b->q[0]+b->cfg.core_mass*b->q[34])/total;s[5]=(ring*b->q[1]+b->cfg.core_mass*b->q[35])/total;
 s[6]=total;s[7]=belt_area(b->q,NULL);for(int i=0;i<4;++i)s[8+i]=bounds[i];s[12]=bounds[1]-b->cfg.ground_height;s[13]=belt_speed_bound(b);
 s[14]=b->grounded;s[16]=b->cooldown;s[17]=b->ramp;s[37]=1;s[38]=BELT_D;s[39]=0;s[40]=b->short_bones;s[41]=ring;s[42]=belt_kinetic(b);
 s[43]=ring*b->v[0]+b->cfg.core_mass*b->v[34];s[44]=ring*b->v[1]+b->cfg.core_mass*b->v[35];s[45]=b->cfg.core_mass*(b->q[34]*b->v[35]-b->q[35]*b->v[34]);
 for(int j=0;j<BELT_B;++j)s[45]+=b->mass[2*j]*(b->q[2*j]*b->v[2*j+1]-b->q[2*j+1]*b->v[2*j]);
 s[46]=b->v[34];s[47]=b->v[35];s[48]=b->v[0];s[49]=b->v[1];s[55]=INFINITY;
 for(int i=0;i<16;++i){belt_v p=belt_eval(b->q,2*BELT_PI*i/16,0);s[55]=fmin(s[55],b->scale*hypot(p.x-b->q[34],p.y-b->q[35]));}s[62]=b->radius;s[63]=BELT_K;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#undef belt_read_stats
static void belt_read_stats(belt_body* b){double start=belt_profile_now();belt_read_stats_profile_impl(b);belt_profile_finish(BELT_PROFILE_FRAME_STATS,start);}
#endif

int dango_belt_version(void){return 1;}
const char* dango_belt_calibration_schema(void){return
 "{\"schemaVersion\":1,\"parameterCount\":11,\"diagnosticCount\":48,\"capabilities\":{\"originalGroundJump\":{\"export\":\"dango_belt_set_original_ground_jump\",\"defaultEnabled\":false,\"speed\":7.7,\"speedUnit\":\"m/s\",\"groundSlack\":0.035,\"mutable\":\"before-first-successful-step\"}},"
 "\"parameters\":[\"radialStiffnessScale\",\"radialDampingScale\",\"beltStretchScale\",\"beltBendScale\",\"areaScale\",\"shortBoneStiffnessScale\",\"shortBoneDampingScale\",\"shapeDampingScale\",\"overallDamping\",\"groundedBrake\",\"groundedHorizontalDamping\"],"
 "\"defaults\":[1,1,1,1,1,1,1,1,0,0,0],\"units\":[\"ratio\",\"ratio\",\"ratio\",\"ratio\",\"ratio\",\"ratio\",\"ratio\",\"ratio\",\"1/s\",\"m/s2\",\"1/s\"],"
 "\"diagnostics\":[\"schemaVersion\",\"substeps\",\"initialKE\",\"finalKE\",\"initialElastic\",\"finalElastic\",\"initialGravityPotential\",\"finalGravityPotential\",\"shapeDampingEndpointWorkRemoved\",\"shapeDampingStrainDissipation\",\"overallDampingKEDelta\",\"groundedBrakeKEDelta\",\"contactKEBeforeSum\",\"contactKEAfterSum\",\"contactKEDelta\",\"projectionElasticDelta\",\"projectionGravityDelta\",\"projectionMaxCoordinateDelta\",\"speedLimitKEDelta\",\"projectionAngularMomentumDelta\",\"jumpAccepted\",\"jumpActualImpulseY\",\"jumpMomentumDeltaY\",\"jumpMomentumBeforeY\",\"jumpMomentumAfterY\",\"jumpKEDelta\",\"jumpRequestedImpulseY\",\"externalForceEndpointWork\",\"integrationEnergyDeltaMinusExternalWork\",\"initialPx\",\"initialPy\",\"initialL\",\"finalPx\",\"finalPy\",\"finalL\",\"contactDeltaPx\",\"contactDeltaPy\",\"contactDeltaL\",\"overallDampingDeltaPx\",\"overallDampingDeltaPy\",\"overallDampingDeltaL\",\"groundedBrakeDeltaPx\",\"groundedBrakeDeltaPy\",\"groundedBrakeDeltaL\",\"integrationMomentumResidualX\",\"integrationMomentumResidualY\",\"integrationAngularMomentumResidual\",\"duration\"],"
 "\"unsupportedLegacyConfig\":[\"radial_minimum_ratio\",\"radial_maximum_ratio\",\"perimeter_minimum_ratio\",\"perimeter_maximum_ratio\",\"ground_deformation_strength\",\"repulsion_distance\",\"repulsion_strength\",\"repulsion_maximum_force\",\"core_exclusion_ratio\",\"angle_strength\",\"top_inward_force\",\"restitution\",\"contact_hertz\",\"linear_damping\"]}";}
int dango_belt_read_diagnostics(void* handle,float* output,int count){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!output||count<48)return -600;for(int i=0;i<48;++i)output[i]=(float)b->diag[i];return 0;}
int dango_belt_read_shape_guard(void* handle,float* output,int count){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!output||count<24)return -600;for(int i=0;i<24;++i)output[i]=(float)b->guard[i];return 0;}
int dango_belt_read_speed_limit(void* handle,double* output,int count){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!output||count<12)return -600;memcpy(output,b->speed_limit,sizeof b->speed_limit);return 0;}
int dango_belt_set_whale_drive(void* handle,int enabled){belt_body* b=belt_lookup(handle);if(!b)return -601;if((enabled!=0&&enabled!=1)||b->has_stepped||(enabled&&(!b->shape_guard||b->radial_drive)))return -600;b->whale_drive=enabled;b->whale_gain=0;memset(b->whale_diag,0,sizeof b->whale_diag);b->whale_diag[0]=1;b->whale_diag[1]=enabled;return 0;}
int dango_belt_set_radial_drive(void* handle,int enabled){belt_body* b=belt_lookup(handle);if(!b)return -601;if((enabled!=0&&enabled!=1)||b->has_stepped||(enabled&&(!b->shape_guard||b->whale_drive)))return -600;b->radial_drive=enabled;memset(b->radial_diag,0,sizeof b->radial_diag);b->radial_diag[0]=3;b->radial_diag[1]=enabled;memset(b->rear_diag,0,sizeof b->rear_diag);b->rear_diag[0]=1;b->rear_diag[1]=enabled;return 0;}
int dango_belt_read_rear_drive(void* handle,double* output,int count){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!output||count<16)return -600;memcpy(output,b->rear_diag,sizeof b->rear_diag);return 0;}
int dango_belt_read_radial_drive(void* handle,double* output,int count){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!output||count<16)return -600;memcpy(output,b->radial_diag,sizeof b->radial_diag);return 0;}
int dango_belt_read_force_ledger(void* handle,double* output,int count){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!output||count<1024)return -600;memcpy(output,b->force_ledger,sizeof b->force_ledger);return 0;}
int dango_belt_read_whale_drive(void* handle,double* output,int count){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!output||count<16)return -600;memcpy(output,b->whale_diag,sizeof b->whale_diag);return 0;}
int dango_belt_set_speed_saturation(void* handle,int enabled){belt_body* b=belt_lookup(handle);if(!b)return -601;if((enabled!=0&&enabled!=1)||b->has_stepped||(enabled&&!b->shape_guard))return -600;b->speed_saturation=enabled;memset(b->speed_limit,0,sizeof b->speed_limit);b->speed_limit[0]=1;b->speed_limit[1]=enabled;b->speed_limit[4]=1;return 0;}
int dango_belt_read_solver_diagnostics(void* handle,double* output,int count){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!output||count<24)return -600;for(int i=0;i<24;++i)output[i]=isfinite(b->solver_diag[i])?b->solver_diag[i]:copysign(DBL_MAX,b->solver_diag[i]);return 0;}
int dango_belt_read_snapshot(void* handle,double* output,int count){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!output||count<200)return -600;double snapshot[200]={0};snapshot[0]=1;snapshot[1]=200;memcpy(snapshot+2,b->q,sizeof b->q);memcpy(snapshot+38,b->v,sizeof b->v);
 snapshot[74]=b->radius;snapshot[75]=b->scale;snapshot[76]=b->left;snapshot[77]=b->right;snapshot[78]=b->ramp;snapshot[79]=b->cooldown;snapshot[80]=b->drag_theta;snapshot[81]=b->short_bones;snapshot[82]=b->grounded;snapshot[83]=b->dragging;snapshot[84]=b->original_ground_jump;snapshot[85]=b->has_stepped;snapshot[86]=b->shape_guard;snapshot[87]=b->cfg.node_count;snapshot[88]=b->cfg.substep_count;
 const float* scalar=&b->cfg.radial_rest_length;for(int i=0;i<39;++i)snapshot[89+i]=scalar[i];memcpy(snapshot+128,b->cal,sizeof b->cal);snapshot[139]=b->guard[2];memcpy(snapshot+140,b->mass,sizeof b->mass);snapshot[176]=b->speed_saturation;snapshot[177]=b->whale_drive;snapshot[178]=b->whale_gain;snapshot[179]=b->radial_drive;memcpy(output,snapshot,sizeof snapshot);return 0;}
int dango_belt_set_shape_guard(void* handle,int enabled){belt_body* b=belt_lookup(handle);if(!b)return -601;if((enabled!=0&&enabled!=1)||b->has_stepped||(!enabled&&(b->speed_saturation||b->whale_drive||b->radial_drive)))return -600;
 if(enabled){belt_guard_result result;if(!belt_guard_certify(b->q,b->q,b->radius,BELT_GUARD_FRAME_BUDGET,&result))return result.reason==BELT_GUARD_BUDGET?-606:-605;}
 b->shape_guard=enabled;memset(b->guard,0,sizeof b->guard);b->guard[0]=1;b->guard[1]=enabled;b->guard[19]=.04*b->radius;b->guard[20]=1e-4*b->radius*b->radius;b->guard[21]=BELT_GUARD_FRAME_BUDGET;return 0;}
int dango_belt_set_original_ground_jump(void* handle,int enabled){belt_body* b=belt_lookup(handle);if(!b)return -601;if((enabled!=0&&enabled!=1)||b->has_stepped)return -600;b->original_ground_jump=enabled;return 0;}
int dango_belt_create(const dango_belt_config* cfg,float x,float y,int short_bones,void** output){
 if(!cfg||!output||!isfinite(x)||!isfinite(y)||(short_bones!=0&&short_bones!=1))return -600;
 const float* scalar=&cfg->radial_rest_length;for(size_t j=0;j<(sizeof(*cfg)-2*sizeof(int))/sizeof(float);++j)if(!isfinite(scalar[j]))return -602;
 if(cfg->node_count!=16||cfg->substep_count<1||cfg->substep_count>16||cfg->radial_rest_length<=0||cfg->node_mass<=0||cfg->core_mass<=0||cfg->gravity<0||cfg->friction<0||cfg->radial_hertz<0||cfg->perimeter_hertz<0||cfg->radial_damping<0||cfg->perimeter_damping<0||cfg->area_strength<0||cfg->local_angle_balance_strength<0||cfg->node_radius<0||cfg->drag_stiffness<0||cfg->drag_damping<0||cfg->drag_maximum_force<0||cfg->horizontal_force<0||cfg->downward_force<0||cfg->jump_impulse<0||cfg->jump_cooldown_seconds<0||cfg->maximum_horizontal_speed<=0||cfg->maximum_speed<=0||cfg->air_control_ratio<0||cfg->air_control_ratio>1)return -602;
 int slot=-1;for(int i=0;i<64;++i)if(!belt_handles[i].body){slot=i;break;}if(slot<0)return -604;belt_body* b=calloc(1,sizeof *b);if(!b)return -604;
 b->cfg=*cfg;b->rear_diag[0]=1;b->whale_diag[0]=1;b->radial_diag[0]=3;belt_force_ledger_begin(b,0,-1);for(int j=0;j<8;++j)b->cal[j]=1;b->short_bones=short_bones;b->radius=((double)cfg->radial_rest_length+cfg->node_radius)*.5*(cos(BELT_PI/16)+.75+.25*cos(BELT_PI/8));b->scale=cfg->radial_rest_length/b->radius;
 if(!isfinite(b->radius)||b->radius<=1e-8||b->radius>FLT_MAX/4||!isfinite(b->scale)||16.0*cfg->node_mass+cfg->core_mass>FLT_MAX){free(b);return -602;}
 b->q[0]=b->q[34]=x;b->q[1]=b->q[35]=(double)y+cfg->drop_height;b->q[2]=b->q[5]=b->radius;b->cooldown=cfg->jump_cooldown_seconds;b->left=-1e6;b->right=1e6;b->guard[0]=1;b->guard[19]=.04*b->radius;b->guard[20]=1e-4*b->radius*b->radius;b->guard[21]=BELT_GUARD_FRAME_BUDGET;b->speed_limit[0]=1;b->speed_limit[4]=1;b->jump_speed_scale=1;
 for(int j=0;j<34;++j)b->mass[j]=16.0*cfg->node_mass*(j<2?1:.5);b->mass[34]=b->mass[35]=cfg->core_mass;belt_read_stats(b);for(int i=0;i<64;++i)if(!isfinite(b->stats[i])||fabs(b->stats[i])>FLT_MAX){free(b);return -602;}
 uintptr_t token=belt_next_token++;if(!token)token=belt_next_token++;belt_handles[slot].token=token;belt_handles[slot].body=b;*output=(void*)token;return 0;
}
int dango_belt_destroy(void* handle){for(int i=0;i<64;++i)if(belt_handles[i].token==(uintptr_t)handle&&belt_handles[i].body){free(belt_handles[i].body);belt_handles[i].body=NULL;return 0;}return -601;}
int dango_belt_create_calibrated(const dango_belt_config* cfg,float x,float y,int short_bones,const float* p,int count,void** output){if(!p||count!=11||!output)return -600;for(int i=0;i<11;++i)if(!isfinite(p[i])||p[i]<0||p[i]>10000)return -602;void* h=NULL;int result=dango_belt_create(cfg,x,y,short_bones,&h);if(result)return result;belt_body* b=belt_lookup(h);for(int i=0;i<11;++i)b->cal[i]=p[i];b->diag[0]=1;b->diag[4]=b->diag[5]=belt_elastic_energy(b);b->diag[6]=b->diag[7]=belt_gravity_energy(b);*output=h;return 0;}
int dango_belt_product_version(void){return 1;}
const char* dango_belt_product_model(void){return "continuous-belt-v1";}
const char* dango_belt_product_tuning(void){return "C455-F-v1";}
int dango_belt_product_capabilities(void){return 127;}
int dango_belt_reconfigure(void* handle,const dango_belt_config* cfg,const float* cal,int count){
 belt_body* b=belt_lookup(handle);if(!b)return -601;
 /* Deliberately restricted to the approved single-body, unchanged-ground domain. */
 if(!cfg||cfg->node_count!=16||cfg->substep_count!=4||cfg->ground_height!=b->cfg.ground_height)return -600;
 void* validation=NULL;int rc=dango_belt_create_calibrated(cfg,0,0,b->short_bones,cal,count,&validation);if(rc)return rc;
 belt_body candidate=*b;const belt_body* validated=belt_lookup(validation);
 candidate.cfg=*cfg;candidate.radius=validated->radius;candidate.scale=validated->scale;
 memcpy(candidate.mass,validated->mass,sizeof candidate.mass);memcpy(candidate.cal,validated->cal,sizeof candidate.cal);
 dango_belt_destroy(validation);
 double ratio=candidate.radius/b->radius;
 if(ratio!=1){
  double old_bounds[4],new_bounds[4],foot;belt_bounds(b->q,old_bounds,&foot);
  double old_mass=b->mass[0]+b->mass[34],new_mass=candidate.mass[0]+candidate.mass[34];
  for(int axis=0;axis<2;++axis){
   double center=(b->mass[axis]*b->q[axis]+b->mass[34+axis]*b->q[34+axis])/old_mass;
   double velocity=(b->mass[axis]*b->v[axis]+b->mass[34+axis]*b->v[34+axis])/old_mass;
   candidate.q[axis]=center+ratio*(b->q[axis]-center);candidate.q[34+axis]=center+ratio*(b->q[34+axis]-center);
   candidate.v[axis]=velocity+ratio*(b->v[axis]-velocity);candidate.v[34+axis]=velocity+ratio*(b->v[34+axis]-velocity);
   for(int j=2+axis;j<34;j+=2){candidate.q[j]*=ratio;candidate.v[j]*=ratio;}
   /* Mass follows new config; preserve the old COM velocity, not momentum. */
   double shift=velocity-(candidate.mass[axis]*candidate.v[axis]+candidate.mass[34+axis]*candidate.v[34+axis])/new_mass;
   candidate.v[axis]+=shift;candidate.v[34+axis]+=shift;
  }
  belt_bounds(candidate.q,new_bounds,&foot);double lift=old_bounds[1]-new_bounds[1];candidate.q[1]+=lift;candidate.q[35]+=lift;
 }
 for(int j=0;j<BELT_D;++j)if(!isfinite(candidate.q[j])||!isfinite(candidate.v[j]))return -602;
 if(!belt_material_domain_valid(&candidate,candidate.q))return -605;
 double bounds[4],foot;belt_bounds(candidate.q,bounds,&foot);
 if(bounds[0]<candidate.left||bounds[2]>candidate.right||bounds[1]<candidate.cfg.ground_height-.00001)return -605;
 if(belt_speed_certificate(&candidate,NULL,NULL,NULL)>candidate.cfg.maximum_speed)return -605;
 if(candidate.shape_guard){belt_guard_result result;if(!belt_guard_certify(candidate.q,candidate.q,candidate.radius,BELT_GUARD_FRAME_BUDGET,&result))return result.reason==BELT_GUARD_BUDGET?-606:-605;
  candidate.guard[11]=result.cross_lower;candidate.guard[12]=result.distance_lower;candidate.guard[13]=result.winding;
  candidate.guard[19]=.04*candidate.radius;candidate.guard[20]=1e-4*candidate.radius*candidate.radius;
 }
 /* No cached contact Jacobians exist: contact rows are rebuilt every substep.
    Existing successful-step telemetry and tick remain associated with that step. */
 belt_read_stats(&candidate);for(int j=0;j<64;++j)if(!isfinite(candidate.stats[j])||fabs(candidate.stats[j])>FLT_MAX)return -602;
 *b=candidate;return 0;
}
static void belt_apply_calibration_damping(belt_body* b,double h,const float* c,bool jump){
 if(b->cal[8]>0){double before=belt_kinetic(b),p0[3],p1[3],factor=1/(1+h*b->cal[8]);belt_momentum(b,p0);for(int j=0;j<BELT_D;++j)b->v[j]*=factor;belt_momentum(b,p1);b->diag[10]+=belt_kinetic(b)-before;for(int j=0;j<3;++j)b->diag[38+j]+=p1[j]-p0[j];}
 if(b->cal[9]==0&&b->cal[10]==0)return;
 double bounds[4],foot;belt_bounds(b->q,bounds,&foot);bool active=c[0]!=0||c[1]!=0||c[2]!=0||c[3]!=0||c[6]!=0||c[8]!=0||jump||b->muscle_strength>0;
 if((b->cal[9]>0||b->cal[10]>0)&&!active&&bounds[1]<=b->cfg.ground_height+.001){double before=belt_kinetic(b),p0[3],p1[3];belt_momentum(b,p0);double total=b->mass[0]+b->mass[34],delta=fmax(-b->cal[9]*h,fmin(b->cal[9]*h,p0[0]/total)),factor=exp(-b->cal[10]*h);b->v[0]-=delta;b->v[34]-=delta;for(int j=0;j<BELT_D;j+=2)b->v[j]*=factor;belt_momentum(b,p1);b->diag[11]+=belt_kinetic(b)-before;for(int j=0;j<3;++j)b->diag[41+j]+=p1[j]-p0[j];}
}
static bool belt_try_jump(belt_body* b,bool requested,double h){
 bool eligible=b->grounded;double cooldown=b->cooldown;
 if(b->original_ground_jump){double bounds[4],foot;belt_bounds(b->q,bounds,&foot);eligible=eligible||bounds[1]-b->cfg.ground_height<=.035;cooldown+=h;}
 double jump_speed=(double)7.7f*b->jump_speed_scale;
 double request=b->original_ground_jump?(b->mass[1]+b->mass[35])*jump_speed:b->cfg.jump_impulse;b->diag[26]=requested?request:0;
 bool accepted=requested&&eligible&&cooldown>=b->cfg.jump_cooldown_seconds;if(!accepted)return false;
 double before=belt_kinetic(b),core_vy=b->v[35],p0[3],p1[3];belt_momentum(b,p0);
 if(b->original_ground_jump){b->v[1]+=jump_speed;b->v[35]+=jump_speed;}else b->v[35]+=b->cfg.jump_impulse/b->cfg.core_mass;
 belt_momentum(b,p1);b->diag[20]=1;b->diag[21]=b->original_ground_jump?p1[1]-p0[1]:b->cfg.core_mass*(b->v[35]-core_vy);b->diag[22]=p1[1]-p0[1];b->diag[23]=p0[1];b->diag[24]=p1[1];b->diag[25]=belt_kinetic(b)-before;b->cooldown=0;b->grounded=0;b->stats[15]=1;return true;
}
int dango_belt_set_bounds(void* handle,float left,float right){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!isfinite(left)||!isfinite(right)||right-left<4*b->radius)return -600;b->left=left;b->right=right;return 0;}
int dango_belt_read(void* handle,float* scaffold,int sc,float* curve,int cc,float* stats,int ss){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!scaffold||!curve||!stats||sc<68||cc<512||ss<64)return -600;
 scaffold[0]=(float)b->q[34];scaffold[1]=(float)b->q[35];scaffold[2]=(float)b->v[34];scaffold[3]=(float)b->v[35];
 for(int i=0;i<16;++i){belt_v p=belt_eval(b->q,2*BELT_PI*i/16,0),v=belt_eval(b->v,2*BELT_PI*i/16,0);int j=4*(i+1);scaffold[j]=(float)(b->scale*p.x+(1-b->scale)*b->q[34]);scaffold[j+1]=(float)(b->scale*p.y+(1-b->scale)*b->q[35]);scaffold[j+2]=(float)(b->scale*v.x+(1-b->scale)*b->v[34]);scaffold[j+3]=(float)(b->scale*v.y+(1-b->scale)*b->v[35]);}
 for(int i=0;i<256;++i){belt_v p=belt_eval(b->q,2*BELT_PI*i/256,0);curve[2*i]=(float)p.x;curve[2*i+1]=(float)p.y;}for(int i=0;i<64;++i)stats[i]=(float)b->stats[i];return 0;
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#define dango_belt_step dango_belt_step_profile_impl
#endif
int dango_belt_step(void* handle,float dt,const float* c,int count){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!c||count!=9||!isfinite(dt)||dt<=0||dt>1.0f/30)return -600;
 for(int i=0;i<9;++i)if(!isfinite(c[i]))return -600;if(fabsf(c[0])>1||c[1]<0||c[1]>1||c[6]<0||c[6]>1)return -600;const int flags[4]={2,3,7,8};for(int i=0;i<4;++i)if(c[flags[i]]!=0&&c[flags[i]]!=1)return -600;
 belt_body backup=*b;if(b->radial_drive){double tick=b->solver_diag[1]+1;memset(b->radial_diag,0,sizeof b->radial_diag);b->radial_diag[0]=3;b->radial_diag[1]=1;b->radial_diag[2]=tick;memset(b->rear_diag,0,sizeof b->rear_diag);b->rear_diag[0]=b->rear_diag[1]=1;b->rear_diag[2]=tick;}if(b->whale_drive){double tick=b->whale_diag[2]+1;memset(b->whale_diag,0,sizeof b->whale_diag);b->whale_diag[0]=b->whale_diag[1]=1;b->whale_diag[2]=tick;b->whale_diag[12]=b->whale_gain;}double attempt=b->solver_diag[1]+1;memset(b->solver_diag,0,sizeof b->solver_diag);b->solver_diag[0]=1;b->solver_diag[1]=attempt;b->solver_diag[4]=b->solver_diag[5]=b->solver_diag[6]=-1;memset(b->speed_limit,0,sizeof b->speed_limit);b->speed_limit[0]=1;b->speed_limit[1]=b->speed_saturation;b->speed_limit[2]=attempt;b->speed_limit[4]=1;
 if(b->shape_guard){double tick=b->guard[2]+1;memset(b->guard,0,sizeof b->guard);b->guard[0]=1;b->guard[1]=1;b->guard[2]=tick;b->guard[19]=.04*b->radius;b->guard[20]=1e-4*b->radius*b->radius;b->guard[21]=BELT_GUARD_FRAME_BUDGET;if(!belt_guard_path(b,b->q,b->q,4)){belt_solver_fail(b,19);goto fault;}b->guard[14]=belt_guard_energy(b);}
 memset(b->stats,0,sizeof b->stats);memset(b->diag,0,sizeof b->diag);b->diag[0]=1;b->diag[2]=belt_kinetic(b);b->diag[4]=belt_elastic_energy(b);b->diag[6]=belt_gravity_energy(b);belt_momentum(b,&b->diag[29]);b->diag[47]=dt;double h=dt/b->cfg.substep_count;
 memset(b->muscle_diag,0,sizeof b->muscle_diag);b->muscle_diag[0]=1;b->muscle_diag[1]=32;b->muscle_diag[2]=attempt;b->muscle_diag[3]=b->muscle_time;b->muscle_diag[4]=b->muscle_strength;b->muscle_diag[24]=b->muscle_profile;b->muscle_diag[25]=b->muscle_seed;b->muscle_diag[26]=b->muscle_axis_x;b->muscle_diag[27]=b->muscle_axis_y;
 bool vertical_cancel=c[1]>0&&c[6]>0;
 bool accepted=belt_try_jump(b,c[2]!=0,h);
 if(c[3]!=0&&!b->dragging){double bounds[4],foot;belt_bounds(b->q,bounds,&foot);double roots[64];int n=belt_roots(b->q,1,1,0,roots);double high=-INFINITY;for(int i=0;i<n;++i){belt_v p=belt_eval(b->q,roots[i],0);if(p.y>high){high=p.y;b->drag_theta=roots[i];}}}b->dragging=c[3]!=0;
 for(int small=0;small<b->cfg.substep_count;++small){b->solver_diag[4]=small;b->solver_diag[15]=1;if(!b->original_ground_jump||!accepted||small>0)b->cooldown+=h;if(c[8]!=0||vertical_cancel)b->ramp=0;else if(!accepted){double delta=c[6]-b->ramp,step=h/(delta>0?.15:.20);b->ramp+=fmax(-step,fmin(step,delta));if(c[1]>0)b->ramp=0;}
  belt_force_ledger_begin(b,attempt,small);double f[BELT_D]={0};f[1]=-b->mass[1]*b->cfg.gravity;f[35]=-b->mass[35]*b->cfg.gravity;
  double fx=0;if((c[0]<0&&b->v[34]>-b->cfg.maximum_horizontal_speed)||(c[0]>0&&b->v[34]<b->cfg.maximum_horizontal_speed))fx=c[0]*b->cfg.horizontal_force*(b->grounded?1:b->cfg.air_control_ratio);
  double fy=accepted||c[8]!=0||vertical_cancel?0:b->cfg.downward_force*(b->ramp-c[1]);if(c[8]!=0)fx=0;double hp[2]={b->q[0],b->q[1]},vp[2]={b->q[0],b->q[1]},lower[2]={b->q[0],b->q[1]};
  double rear_force[BELT_D]={0},radial_force[BELT_D]={0},radial_old[BELT_D],radial_x=0,radial_y=0;if(b->radial_drive){memcpy(radial_old,b->q,sizeof radial_old);if(fx!=0){if(!belt_radial_force(b,radial_force,fx>0?1:-1,fabs(fx))){belt_solver_fail(b,23);goto fault;}radial_x=radial_force[0];radial_y=radial_force[1];if(!belt_rear_force(b,rear_force,fx>0?1:-1,radial_y)){belt_solver_fail(b,24);goto fault;}for(int j=0;j<BELT_D;++j)f[j]+=radial_force[j]+rear_force[j];hp[0]=b->q[34];hp[1]=b->q[35];}else {belt_rear_clear_last(b);b->radial_diag[4]=b->radial_diag[5]=b->radial_diag[6]=b->radial_diag[7]=b->radial_diag[8]=b->radial_diag[12]=b->radial_diag[15]=0;}}
  /* W/S is a deforming pair, never a net translational load. Each world half
     independently receives the same total magnitude, even on asymmetric curves.
     Keep its real couple; no COM velocity or torque correction is applied. */
  if((!b->radial_drive&&fx!=0&&!belt_region_force(b,f,0,fx>0?1:-1,fx,hp))||(fy!=0&&(!belt_region_force(b,f,1,1,fy,vp)||!belt_region_force(b,f,1,-1,-fy,lower)))){belt_solver_fail(b,20);goto fault;}
  double whale_force[BELT_D]={0},whale_old[BELT_D];if(b->whale_drive){if(!belt_whale_update(b,h,c,accepted,fy,whale_force)){belt_solver_fail(b,22);goto fault;}memcpy(whale_old,b->q,sizeof whale_old);for(int j=0;j<BELT_D;++j)f[j]+=whale_force[j];}
  double total=b->cfg.core_mass+16.0*b->cfg.node_mass,cx=(b->mass[0]*b->q[0]+b->cfg.core_mass*b->q[34])/total,cy=(b->mass[1]*b->q[1]+b->cfg.core_mass*b->q[35])/total;
  double vertical_torque=(vp[0]-lower[0])*fy;
  b->stats[18]=fx;b->stats[19]=0;b->stats[20]=hp[0];b->stats[21]=hp[1];b->stats[22]=vp[0];b->stats[23]=vp[1];b->stats[24]=-(hp[1]-cy)*fx+vertical_torque;b->stats[57]=fx;b->stats[58]=0;if(b->radial_drive){double netx=radial_x+rear_force[0]+rear_force[34],nety=radial_y+rear_force[1]+rear_force[35];b->stats[18]=b->stats[57]=netx;b->stats[19]=b->stats[58]=nety;double actual_torque=0;for(int j=0;j<BELT_D;j+=2)actual_torque+=b->q[j]*(radial_force[j+1]+rear_force[j+1])-b->q[j+1]*(radial_force[j]+rear_force[j]);b->stats[24]=actual_torque-cx*nety+cy*netx+vertical_torque;}
  if(b->dragging){double basis[BELT_B];belt_basis(b->drag_theta,0,basis);belt_v p=belt_eval_basis(b->q,basis),v=belt_eval_basis(b->v,basis),force={b->cfg.drag_stiffness*(c[4]-p.x)-b->cfg.drag_damping*v.x,b->cfg.drag_stiffness*(c[5]-p.y)-b->cfg.drag_damping*v.y};double magnitude=hypot(force.x,force.y);if(magnitude>b->cfg.drag_maximum_force)force=belt_mul(b->cfg.drag_maximum_force/magnitude,force);belt_force_ledger_add(b,p.x,p.y,force.x,force.y,4,b->drag_theta);for(int j=0;j<BELT_B;++j){f[2*j]+=basis[j]*force.x;f[2*j+1]+=basis[j]*force.y;}}
#ifdef DANGO_BELT_ROOM_HOOKS
  belt_room_before_integrate(b);
#endif
  belt_apply_calibration_damping(b,h,c,accepted);b->solver_diag[15]=2;
  if(b->muscle_strength>0){if(!belt_integrate_muscles(b,h,f,b->muscle_time+small*h)){if(b->solver_diag[3]==0)belt_solver_fail(b,25);goto fault;}}
  else if(!belt_integrate(b,h,f))goto fault;
  if(b->radial_drive){for(int j=0;j<BELT_D;++j){b->radial_diag[13]+=radial_force[j]*(b->q[j]-radial_old[j]);b->rear_diag[12]+=rear_force[j]*(b->q[j]-radial_old[j]);}}if(b->whale_drive){for(int j=0;j<BELT_D;++j)b->whale_diag[8]+=whale_force[j]*(b->q[j]-whale_old[j]);b->whale_diag[14]=fmax(b->whale_diag[14],b->guard[4]);}b->solver_diag[15]=3;if(!BELT_CONTACT_DISPATCH(b))goto fault;b->solver_diag[15]=4;{int samples=0;double maximum=0,coarse=0;b->solver_diag[14]=belt_speed_certificate(b,&samples,&maximum,&coarse);b->solver_diag[19]=samples;b->solver_diag[20]=maximum;b->solver_diag[21]=b->solver_diag[14];if(samples>256){b->solver_diag[22]=coarse;b->solver_diag[23]=b->solver_diag[14];}}if(!belt_limit_speed(b,&b->solver_diag[14]))goto fault;if(b->solver_diag[14]>b->cfg.maximum_speed){belt_solver_fail(b,13);goto fault;}
  double bounds[4],foot;belt_bounds(b->q,bounds,&foot);if(bounds[0]<b->left||bounds[2]>b->right){belt_solver_fail(b,14);goto fault;}
  b->stats[50]+=1;
  if(b->shape_guard)b->guard[22]+=1;
 }
 b->solver_diag[15]=5;{belt_residual final[BELT_MAX_RESIDUALS];double derivative;int n=belt_residuals(b,b->q,final,&derivative);if(n<0){belt_solver_fail(b,15);goto fault;}b->stats[32]=0;for(int i=0;i<n;++i)b->stats[32]+=.5*final[i].k*final[i].value*final[i].value;b->stats[54]=derivative;
  double old_area_scale=8*b->cfg.radial_rest_length*b->cfg.radial_rest_length*sin(2*BELT_PI/16)/(BELT_PI*b->radius*b->radius);if(belt_area(b->q,NULL)*old_area_scale<b->cfg.minimum_area){belt_solver_fail(b,16);goto fault;}}
 b->diag[1]=b->cfg.substep_count;b->diag[3]=belt_kinetic(b);b->diag[5]=belt_elastic_energy(b);b->diag[7]=belt_gravity_energy(b);belt_momentum(b,&b->diag[32]);belt_read_stats(b);for(int i=0;i<64;++i)if(!isfinite(b->stats[i])||fabs(b->stats[i])>FLT_MAX){belt_solver_fail(b,17);goto fault;}for(int i=0;i<48;++i)if(!isfinite(b->diag[i])||fabs(b->diag[i])>FLT_MAX){belt_solver_fail(b,18);goto fault;}if(b->shape_guard){b->guard[15]=belt_guard_energy(b);b->guard[3]=0;}if(b->whale_drive)b->whale_diag[14]=fmax(b->whale_diag[14],b->guard[4]);if(b->radial_drive){b->radial_diag[14]=b->guard[4];b->rear_diag[13]=b->guard[4];}b->has_stepped=1;return 0;
fault:{int result=-603;double diagnostics[24],solver[24],speed[12];memcpy(diagnostics,b->guard,sizeof diagnostics);memcpy(solver,b->solver_diag,sizeof solver);memcpy(speed,b->speed_limit,sizeof speed);if(b->shape_guard){result=b->guard[9]==BELT_GUARD_BUDGET?-606:-605;if(diagnostics[9]==0)diagnostics[9]=BELT_GUARD_SOLVE;diagnostics[3]=result;}solver[2]=result;double whale_attempt=b->whale_diag[2],radial_attempt=b->radial_diag[2];*b=backup;if(b->radial_drive){b->radial_diag[2]=radial_attempt;b->rear_diag[2]=radial_attempt;}if(b->whale_drive)b->whale_diag[2]=whale_attempt;memcpy(b->solver_diag,solver,sizeof solver);memcpy(b->speed_limit,speed,sizeof speed);if(b->shape_guard)memcpy(b->guard,diagnostics,sizeof diagnostics);return result;}
}
#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
#undef dango_belt_step
int dango_belt_step(void* handle,float dt,const float* c,int count){double start=belt_profile_now();int result=dango_belt_step_profile_impl(handle,dt,c,count);belt_profile_finish(BELT_PROFILE_STEP_TOTAL,start);return result;}
#endif


#if defined(DANGO_BELT_PROFILE) && DANGO_BELT_PROFILE
/* Optional profile-only ABI. Invalid inputs leave caller buffers and body intact.
   Tables belong to the calling thread and may include several handles. */
DANGO_BELT_API int dango_belt_reset_profile(void* handle){
 if(!belt_lookup(handle))return -601;belt_profile_reset();return 0;
}
DANGO_BELT_API int dango_belt_read_profile(void* handle,double* output,int count){
 if(!belt_lookup(handle))return -601;if(!output||count<48)return -600;belt_profile_read(output);return 0;
}
#endif

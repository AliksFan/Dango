#include "dango_belt.h"
#include "dango_belt_math.h"
#include <stdint.h>
#include <stdlib.h>
#include <stdbool.h>
#include <float.h>
#define BELT_MAX_RESIDUALS 180
typedef struct belt_residual {double value,k,c,j[BELT_D];} belt_residual;
typedef struct belt_body {
 dango_belt_config cfg;double q[BELT_D],v[BELT_D],mass[BELT_D];
 double radius,scale,left,right,ramp,cooldown,drag_theta;
 int short_bones,grounded,dragging;double stats[64];
} belt_body;
static struct {uintptr_t token;belt_body* body;} belt_handles[64];
static uintptr_t belt_next_token=1;
static belt_body* belt_lookup(void* handle){uintptr_t token=(uintptr_t)handle;for(int i=0;i<64;++i)if(belt_handles[i].token==token&&token)return belt_handles[i].body;return NULL;}
static double belt_kinetic(const belt_body* b){double e=0;for(int j=0;j<BELT_D;++j)e+=.5*b->mass[j]*b->v[j]*b->v[j];return e;}
static double belt_speed_bound(const belt_body* b){double maximum=0,lipschitz=0;for(int i=0;i<256;++i){belt_v v=belt_eval(b->v,2*BELT_PI*i/256,0);maximum=fmax(maximum,hypot(v.x,v.y));}
 for(int n=1;n<=BELT_K;++n)lipschitz+=n*(hypot(b->v[4*n-2],b->v[4*n-1])+hypot(b->v[4*n],b->v[4*n+1]));
 /* Every material point is <=pi/256 from a sampled point. This is an
    explicit continuous upper bound, not an assumed sampled maximum. */
 return fmax(hypot(b->v[34],b->v[35]),maximum+lipschitz*BELT_PI/256);}
static int belt_residuals(const belt_body* b,const double* q,belt_residual* r,double* min_derivative){
 int count=0;double dtheta=2*BELT_PI/BELT_Q,wr=2*BELT_PI*b->cfg.radial_hertz,wp=2*BELT_PI*b->cfg.perimeter_hertz;
 double reduced=b->cfg.core_mass*b->cfg.node_mass/(b->cfg.core_mass+b->cfg.node_mass);
 double kr=reduced*wr*wr,cr=2*b->cfg.radial_damping*reduced*wr;
 double edge=2*b->cfg.radial_rest_length*sin(BELT_PI/16),kp=.5*b->cfg.node_mass*wp*wp;
 double ea=kp*edge,ei=b->cfg.local_angle_balance_strength*b->cfg.node_mass*b->cfg.radial_rest_length*b->cfg.radial_rest_length;
 double time=wp>0?2*b->cfg.perimeter_damping/wp:0;*min_derivative=INFINITY;
 for(int i=0;i<BELT_Q;++i){double t=dtheta*i,db[BELT_B],ddb[BELT_B];belt_basis(t,1,db);belt_basis(t,2,ddb);
  belt_v u=belt_eval_basis(q,db),a=belt_eval_basis(q,ddb);double s=hypot(u.x,u.y);*min_derivative=fmin(*min_derivative,s);
  if(!(s>b->radius*.02)||!isfinite(s))return -1;
  belt_residual* stretch=&r[count++];memset(stretch,0,sizeof *stretch);stretch->value=s-b->radius;stretch->k=ea/b->radius*dtheta;stretch->c=stretch->k*time;
  belt_residual* bend=&r[count++];memset(bend,0,sizeof *bend);double cross=belt_cross(u,a),s3=s*s*s;
  bend->value=cross/s3-1/b->radius;bend->k=ei*b->radius*dtheta;bend->c=bend->k*time;
  belt_v gs=belt_mul(1/s,u),gu=belt_sub(belt_mul(1/s3,(belt_v){a.y,-a.x}),belt_mul(3*cross/(s3*s*s),u));
  belt_v ga=belt_mul(1/s3,(belt_v){-u.y,u.x});
  for(int j=0;j<BELT_B;++j){stretch->j[2*j]=gs.x*db[j];stretch->j[2*j+1]=gs.y*db[j];bend->j[2*j]=gu.x*db[j]+ga.x*ddb[j];bend->j[2*j+1]=gu.y*db[j]+ga.y*ddb[j];}
 }
 for(int i=0;i<16;++i){double basis[BELT_B];belt_basis(2*BELT_PI*i/16,0,basis);belt_v p=belt_eval_basis(q,basis),core={q[34],q[35]},d=belt_sub(p,core);double length=hypot(d.x,d.y);
  if(length<1e-6)return -1;belt_residual* radial=&r[count++];memset(radial,0,sizeof *radial);radial->value=b->scale*length-b->cfg.radial_rest_length;radial->k=kr;radial->c=cr;
  belt_v g=belt_mul(b->scale/length,d);for(int j=0;j<BELT_B;++j){radial->j[2*j]=basis[j]*g.x;radial->j[2*j+1]=basis[j]*g.y;}radial->j[34]=-g.x;radial->j[35]=-g.y;
  if(b->short_bones){double next[BELT_B];belt_basis(2*BELT_PI*(i+1)/16,0,next);d=belt_sub(belt_eval_basis(q,next),p);length=hypot(d.x,d.y);if(length<1e-6)return -1;
   belt_residual* short_edge=&r[count++];memset(short_edge,0,sizeof *short_edge);short_edge->value=b->scale*length-edge;short_edge->k=kp;short_edge->c=2*b->cfg.perimeter_damping*.5*b->cfg.node_mass*wp;g=belt_mul(b->scale/length,d);
   for(int j=0;j<BELT_B;++j){short_edge->j[2*j]=(next[j]-basis[j])*g.x;short_edge->j[2*j+1]=(next[j]-basis[j])*g.y;}}
 }
 double target=BELT_PI*b->radius*b->radius;belt_residual* area=&r[count++];memset(area,0,sizeof *area);double actual=belt_area(q,area->j);if(actual<=target*.05)return -1;
 double old_target=8*b->cfg.radial_rest_length*b->cfg.radial_rest_length*sin(2*BELT_PI/16),area_scale=old_target/target;
 area->value=fmin(0,actual-target);if(actual>=target)memset(area->j,0,sizeof area->j);area->k=b->cfg.area_strength/(old_target*b->cfg.radial_rest_length*sin(2*BELT_PI/16))*area_scale*area_scale;return count;
}
static double belt_objective(const belt_body* b,const double* q,const double* old,const double* predicted,const belt_residual* previous,double dt,double* gradient,double hessian[BELT_D][BELT_D],double* elastic,double* damping,double* min_derivative){
 belt_residual r[BELT_MAX_RESIDUALS];int n=belt_residuals(b,q,r,min_derivative);if(n<0)return INFINITY;double objective=0;*elastic=*damping=0;
 if(gradient)memset(gradient,0,BELT_D*sizeof(double));if(hessian)memset(hessian,0,BELT_D*BELT_D*sizeof(double));
 for(int j=0;j<BELT_D;++j){double delta=q[j]-predicted[j],k=b->mass[j]/(dt*dt);objective+=.5*k*delta*delta;if(gradient)gradient[j]=k*delta;if(hessian)hessian[j][j]=k;}
 for(int i=0;i<n;++i){double delta=r[i].value-previous[i].value,elastic_i=.5*r[i].k*r[i].value*r[i].value,damping_i=.5*r[i].c/dt*delta*delta;
  objective+=elastic_i+damping_i;*elastic+=elastic_i;*damping+=damping_i;
  if(gradient){double force=r[i].k*r[i].value+r[i].c/dt*delta,k=r[i].k+r[i].c/dt;
   for(int j=0;j<BELT_D;++j){gradient[j]+=force*r[i].j[j];if(hessian)for(int l=0;l<=j;++l)hessian[j][l]+=k*r[i].j[j]*r[i].j[l];}}
 }
 (void)old;return objective;
}
static int belt_integrate(belt_body* b,double dt,const double* force){
 double old[BELT_D],predicted[BELT_D],gradient[BELT_D],matrix[BELT_D][BELT_D],step[BELT_D];memcpy(old,b->q,sizeof old);
 belt_residual previous[BELT_MAX_RESIDUALS];double derivative;if(belt_residuals(b,old,previous,&derivative)<0)return 0;
 for(int j=0;j<BELT_D;++j)predicted[j]=old[j]+dt*b->v[j]+dt*dt*force[j]/b->mass[j];
 double elastic=0,damping=0,residual=INFINITY;int iteration;
 for(iteration=0;iteration<40;++iteration){double value=belt_objective(b,b->q,old,predicted,previous,dt,gradient,matrix,&elastic,&damping,&derivative);if(!isfinite(value))return 0;
  residual=0;for(int j=0;j<BELT_D;++j){residual=fmax(residual,fabs(gradient[j])*dt*dt/b->mass[j]);step[j]=-gradient[j];}
  if(residual<2e-9)break;if(!belt_cholesky(matrix,step,step))return 0;
  double directional=0;for(int j=0;j<BELT_D;++j)directional+=gradient[j]*step[j];bool accepted=false;
  for(int ls=0;ls<20;++ls){double trial[BELT_D],factor=ldexp(1.0,-ls),e,d,md;for(int j=0;j<BELT_D;++j)trial[j]=b->q[j]+factor*step[j];
   double next=belt_objective(b,trial,old,predicted,previous,dt,NULL,NULL,&e,&d,&md);
   if(isfinite(next)&&next<=value+1e-4*factor*directional){memcpy(b->q,trial,sizeof trial);accepted=true;break;}}
  if(!accepted)return 0;
 }
 if(residual>2e-7)return 0;for(int j=0;j<BELT_D;++j)b->v[j]=(b->q[j]-old[j])/dt;
 b->stats[32]=elastic;b->stats[33]+=damping;b->stats[34]=fmax(b->stats[34],residual);b->stats[54]=derivative;b->stats[56]=fmax(b->stats[56],iteration+1);
 belt_residual current[BELT_MAX_RESIDUALS];int count=belt_residuals(b,b->q,current,&derivative);double material[BELT_D]={0};
 for(int i=0;i<count;++i){double f=-(current[i].k*current[i].value+current[i].c/dt*(current[i].value-previous[i].value));for(int j=0;j<BELT_D;++j)material[j]+=f*current[i].j[j];}
 b->stats[59]=material[0]+material[34];b->stats[60]=material[1]+material[35];b->stats[61]=0;for(int j=0;j<BELT_D;j+=2)b->stats[61]+=b->q[j]*material[j+1]-b->q[j+1]*material[j];return 1;
}
/* Exact normalized material-interval integral: the selected world half is
   located on the continuous curve, never on the sixteen scaffold points. */
static int belt_region_force(belt_body* b,double* force,int axis,int sign,double amount,double* action){
 double bounds[4],foot;belt_bounds(b->q,bounds,&foot);double middle=.5*(bounds[axis]+bounds[axis+2]),roots[64],cuts[66];int n=belt_roots(b->q,axis,0,middle,roots),used=0;cuts[used++]=0;
 for(int i=0;i<n;++i)if(roots[i]>1e-9&&roots[i]<2*BELT_PI-1e-9)cuts[used++]=roots[i];cuts[used++]=2*BELT_PI;
 double integrals[BELT_B]={0},measure=0;
 for(int i=0;i<used-1;++i){double a=cuts[i],z=cuts[i+1];belt_v p=belt_eval(b->q,.5*(a+z),0);if(sign*((axis?p.y:p.x)-middle)<0)continue;
  measure+=z-a;integrals[0]+=z-a;for(int k=1;k<=BELT_K;++k){integrals[2*k-1]+=(sin(k*z)-sin(k*a))/k;integrals[2*k]+=(cos(k*a)-cos(k*z))/k;}}
 if(measure<=0)return 0;belt_v mean={0};for(int j=0;j<BELT_B;++j){double w=integrals[j]/measure;force[2*j+axis]+=amount*w;mean.x+=b->q[2*j]*w;mean.y+=b->q[2*j+1]*w;}
 action[0]=mean.x;action[1]=mean.y;return 1;
}
typedef struct belt_contact {double basis[BELT_B],normal,tangent;} belt_contact;
static int belt_contact_rows(const belt_body* b,belt_contact* rows,double tolerance){
 double roots[64];int nr=belt_roots(b->q,1,1,0,roots),n=0;
 for(int i=0;i<nr;++i){belt_v p=belt_eval(b->q,roots[i],0),dd=belt_eval(b->q,roots[i],2);if(p.y-b->cfg.ground_height<=tolerance&&dd.y>=-1e-9){memset(&rows[n],0,sizeof rows[n]);belt_basis(roots[i],0,rows[n++].basis);}}
 return n;
}
static int belt_contact_solve(belt_body* b){
 double bounds[4],foot;belt_bounds(b->q,bounds,&foot);double before_gap=bounds[1]-b->cfg.ground_height;b->stats[51]=fmax(b->stats[51],-before_gap);
 /* Position-only projection, repeatedly rebuilding true curve extrema. */
 for(int it=0;it<32;++it){belt_bounds(b->q,bounds,&foot);double penetration=b->cfg.ground_height-bounds[1];if(penetration<=2e-9)break;
  double basis[BELT_B];belt_basis(foot,0,basis);double effective=0;for(int j=0;j<BELT_B;++j)effective+=basis[j]*basis[j]/b->mass[2*j+1];double lambda=penetration/effective;
  for(int j=0;j<BELT_B;++j){double delta=lambda*basis[j]/b->mass[2*j+1];b->q[2*j+1]+=delta;b->stats[35]=fmax(b->stats[35],fabs(delta));}}
 belt_bounds(b->q,bounds,&foot);b->stats[31]=fmax(0,b->cfg.ground_height-bounds[1]);if(b->stats[31]>1e-7)return 0;
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
 if(residual>2e-6)return 0;
 for(int i=0;i<n;++i){normal+=rows[i].normal;tangent+=fabs(rows[i].tangent);b->stats[53]=fmax(b->stats[53],fabs(rows[i].tangent)-b->cfg.friction*rows[i].normal);}
 double after=belt_kinetic(b);b->stats[26]+=before;b->stats[27]+=after;b->stats[28]+=normal;b->stats[29]+=tangent;b->stats[30]=fmax(b->stats[30],residual);b->stats[52]+=after-before;
 return after<=before+1e-9*(1+before);
}
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
int dango_belt_version(void){return 1;}
int dango_belt_create(const dango_belt_config* cfg,float x,float y,int short_bones,void** output){
 if(!cfg||!output||!isfinite(x)||!isfinite(y)||(short_bones!=0&&short_bones!=1))return -600;
 const float* scalar=&cfg->radial_rest_length;for(size_t j=0;j<(sizeof(*cfg)-2*sizeof(int))/sizeof(float);++j)if(!isfinite(scalar[j]))return -602;
 if(cfg->node_count!=16||cfg->substep_count<1||cfg->substep_count>16||cfg->radial_rest_length<=0||cfg->node_mass<=0||cfg->core_mass<=0||cfg->gravity<0||cfg->friction<0||cfg->radial_hertz<0||cfg->perimeter_hertz<0||cfg->radial_damping<0||cfg->perimeter_damping<0||cfg->area_strength<0||cfg->local_angle_balance_strength<0||cfg->node_radius<0||cfg->drag_stiffness<0||cfg->drag_damping<0||cfg->drag_maximum_force<0||cfg->horizontal_force<0||cfg->downward_force<0||cfg->jump_impulse<0||cfg->jump_cooldown_seconds<0||cfg->maximum_horizontal_speed<=0||cfg->maximum_speed<=0||cfg->air_control_ratio<0||cfg->air_control_ratio>1)return -602;
 int slot=-1;for(int i=0;i<64;++i)if(!belt_handles[i].body){slot=i;break;}if(slot<0)return -604;belt_body* b=calloc(1,sizeof *b);if(!b)return -604;
 b->cfg=*cfg;b->short_bones=short_bones;b->radius=((double)cfg->radial_rest_length+cfg->node_radius)*.5*(cos(BELT_PI/16)+.75+.25*cos(BELT_PI/8));b->scale=cfg->radial_rest_length/b->radius;
 if(!isfinite(b->radius)||b->radius<=1e-8||b->radius>FLT_MAX/4||!isfinite(b->scale)||16.0*cfg->node_mass+cfg->core_mass>FLT_MAX){free(b);return -602;}
 b->q[0]=b->q[34]=x;b->q[1]=b->q[35]=(double)y+cfg->drop_height;b->q[2]=b->q[5]=b->radius;b->cooldown=cfg->jump_cooldown_seconds;b->left=-1e6;b->right=1e6;
 for(int j=0;j<34;++j)b->mass[j]=16.0*cfg->node_mass*(j<2?1:.5);b->mass[34]=b->mass[35]=cfg->core_mass;belt_read_stats(b);for(int i=0;i<64;++i)if(!isfinite(b->stats[i])||fabs(b->stats[i])>FLT_MAX){free(b);return -602;}
 uintptr_t token=belt_next_token++;if(!token)token=belt_next_token++;belt_handles[slot].token=token;belt_handles[slot].body=b;*output=(void*)token;return 0;
}
int dango_belt_destroy(void* handle){for(int i=0;i<64;++i)if(belt_handles[i].token==(uintptr_t)handle&&belt_handles[i].body){free(belt_handles[i].body);belt_handles[i].body=NULL;return 0;}return -601;}
int dango_belt_set_bounds(void* handle,float left,float right){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!isfinite(left)||!isfinite(right)||right-left<4*b->radius)return -600;b->left=left;b->right=right;return 0;}
int dango_belt_read(void* handle,float* scaffold,int sc,float* curve,int cc,float* stats,int ss){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!scaffold||!curve||!stats||sc<68||cc<512||ss<64)return -600;
 scaffold[0]=(float)b->q[34];scaffold[1]=(float)b->q[35];scaffold[2]=(float)b->v[34];scaffold[3]=(float)b->v[35];
 for(int i=0;i<16;++i){belt_v p=belt_eval(b->q,2*BELT_PI*i/16,0),v=belt_eval(b->v,2*BELT_PI*i/16,0);int j=4*(i+1);scaffold[j]=(float)(b->scale*p.x+(1-b->scale)*b->q[34]);scaffold[j+1]=(float)(b->scale*p.y+(1-b->scale)*b->q[35]);scaffold[j+2]=(float)(b->scale*v.x+(1-b->scale)*b->v[34]);scaffold[j+3]=(float)(b->scale*v.y+(1-b->scale)*b->v[35]);}
 for(int i=0;i<256;++i){belt_v p=belt_eval(b->q,2*BELT_PI*i/256,0);curve[2*i]=(float)p.x;curve[2*i+1]=(float)p.y;}for(int i=0;i<64;++i)stats[i]=(float)b->stats[i];return 0;
}
int dango_belt_step(void* handle,float dt,const float* c,int count){belt_body* b=belt_lookup(handle);if(!b)return -601;if(!c||count!=9||!isfinite(dt)||dt<=0||dt>1.0f/30)return -600;
 for(int i=0;i<9;++i)if(!isfinite(c[i]))return -600;if(fabsf(c[0])>1||c[1]<0||c[1]>1||c[6]<0||c[6]>1)return -600;const int flags[4]={2,3,7,8};for(int i=0;i<4;++i)if(c[flags[i]]!=0&&c[flags[i]]!=1)return -600;
 belt_body backup=*b;memset(b->stats,0,sizeof b->stats);double h=dt/b->cfg.substep_count;
 bool accepted=c[2]!=0&&b->grounded&&b->cooldown>=b->cfg.jump_cooldown_seconds;if(accepted){b->v[35]+=b->cfg.jump_impulse/b->cfg.core_mass;b->cooldown=0;b->grounded=0;b->stats[15]=1;}
 if(c[3]!=0&&!b->dragging){double bounds[4],foot;belt_bounds(b->q,bounds,&foot);double roots[64];int n=belt_roots(b->q,1,1,0,roots);double high=-INFINITY;for(int i=0;i<n;++i){belt_v p=belt_eval(b->q,roots[i],0);if(p.y>high){high=p.y;b->drag_theta=roots[i];}}}b->dragging=c[3]!=0;
 for(int small=0;small<b->cfg.substep_count;++small){b->cooldown+=h;if(c[8]!=0)b->ramp=0;else if(!accepted){double delta=c[6]-b->ramp,step=h/(delta>0?.15:.20);b->ramp+=fmax(-step,fmin(step,delta));if(c[1]>0)b->ramp=0;}
  double f[BELT_D]={0};f[1]=-b->mass[1]*b->cfg.gravity;f[35]=-b->mass[35]*b->cfg.gravity;
  double fx=0;if((c[0]<0&&b->v[34]>-b->cfg.maximum_horizontal_speed)||(c[0]>0&&b->v[34]<b->cfg.maximum_horizontal_speed))fx=c[0]*b->cfg.horizontal_force*(b->grounded?1:b->cfg.air_control_ratio);
  double fy=accepted||c[8]!=0?0:b->cfg.downward_force*(b->ramp-c[1]);if(c[8]!=0)fx=0;double hp[2]={b->q[0],b->q[1]},vp[2]={b->q[0],b->q[1]};
  if(fx!=0&&!belt_region_force(b,f,0,fx>0?1:-1,fx,hp))goto fault;if(fy!=0&&!belt_region_force(b,f,1,1,fy,vp))goto fault;
  double total=b->cfg.core_mass+16.0*b->cfg.node_mass,cx=(b->mass[0]*b->q[0]+b->cfg.core_mass*b->q[34])/total,cy=(b->mass[1]*b->q[1]+b->cfg.core_mass*b->q[35])/total;
  b->stats[18]=fx;b->stats[19]=fy;b->stats[20]=hp[0];b->stats[21]=hp[1];b->stats[22]=vp[0];b->stats[23]=vp[1];b->stats[24]=-(hp[1]-cy)*fx+(vp[0]-cx)*fy;b->stats[57]=fx;b->stats[58]=fy;
  if(b->dragging){double basis[BELT_B];belt_basis(b->drag_theta,0,basis);belt_v p=belt_eval_basis(b->q,basis),v=belt_eval_basis(b->v,basis),force={b->cfg.drag_stiffness*(c[4]-p.x)-b->cfg.drag_damping*v.x,b->cfg.drag_stiffness*(c[5]-p.y)-b->cfg.drag_damping*v.y};double magnitude=hypot(force.x,force.y);if(magnitude>b->cfg.drag_maximum_force)force=belt_mul(b->cfg.drag_maximum_force/magnitude,force);for(int j=0;j<BELT_B;++j){f[2*j]+=basis[j]*force.x;f[2*j+1]+=basis[j]*force.y;}}
  if(!belt_integrate(b,h,f)||!belt_contact_solve(b)||belt_speed_bound(b)>b->cfg.maximum_speed)goto fault;
  double bounds[4],foot;belt_bounds(b->q,bounds,&foot);if(bounds[0]<b->left||bounds[2]>b->right)goto fault;
  b->stats[50]+=1;
 }
 {belt_residual final[BELT_MAX_RESIDUALS];double derivative;int n=belt_residuals(b,b->q,final,&derivative);if(n<0)goto fault;b->stats[32]=0;for(int i=0;i<n;++i)b->stats[32]+=.5*final[i].k*final[i].value*final[i].value;b->stats[54]=derivative;
  double old_area_scale=8*b->cfg.radial_rest_length*b->cfg.radial_rest_length*sin(2*BELT_PI/16)/(BELT_PI*b->radius*b->radius);if(belt_area(b->q,NULL)*old_area_scale<b->cfg.minimum_area)goto fault;}
 belt_read_stats(b);for(int i=0;i<64;++i)if(!isfinite(b->stats[i])||fabs(b->stats[i])>FLT_MAX)goto fault;return 0;
fault:*b=backup;return -603;
}

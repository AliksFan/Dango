#define DANGO_BELT_EMBEDDED 1
#include "dango_belt_room.h"
#define DANGO_BELT_ROOM_HOOKS 1
#include "dango_belt.c"

_Static_assert(sizeof(dango_belt_room_input)==48,"room input ABI");
_Static_assert(sizeof(dango_belt_room_platform)==48,"room platform ABI");
_Static_assert(sizeof(dango_belt_room_frame)==4472,"room frame ABI");
#define ROOM_GEOMETRY_BUDGET 262144
#define ROOM_PENETRATION 1e-7
#define ROOM_ACTIVATION 2e-6
#define ROOM_POSITION_LIMIT .01
#ifndef DANGO_BELT_ROOM_NEGATIVE
#define DANGO_BELT_ROOM_NEGATIVE 0
#endif
typedef struct room_world {
 void* body_handle;uint64_t generation,tick,revision,max_platform_id;
 int platform_enabled,fault;dango_belt_room_platform platform;
 double old_q[BELT_D],diag[24],last_theta;
} room_world;
static struct {uintptr_t token;room_world* world;} room_handles[16];
static uint64_t room_next_generation=1;
static room_world* room_active;
static volatile int room_negative_mode=DANGO_BELT_ROOM_NEGATIVE;
#ifdef DANGO_BELT_ROOM_TRACE
static int room_refusal_line;
static double room_trace_from[BELT_D],room_trace_to[BELT_D],room_trace_cell[6];
static belt_body room_trace_attempt_body;
static belt_body room_trace_safe_body;
#endif
static room_world* room_lookup(void* handle){for(int i=0;i<16;++i)if(room_handles[i].world&&room_handles[i].token==(uintptr_t)handle)return room_handles[i].world;return NULL;}
static belt_body* room_body(room_world* w){return belt_lookup(w->body_handle);}
static int room_refuse_impl(room_world* w,int code,int reason,int line){
#ifdef DANGO_BELT_ROOM_TRACE
 room_refusal_line=line;
 if(room_active==w)room_trace_attempt_body=*room_body(w);
#else
 (void)line;
#endif
 w->diag[2]=code;w->diag[7]=reason;return 0;}
#define room_refuse(w,c,r) room_refuse_impl(w,c,r,__LINE__)
static void room_edges(const dango_belt_room_platform* p,double* e){e[0]=p->x-p->width*.5;e[1]=p->y-p->height*.5;e[2]=p->x+p->width*.5;e[3]=p->y+p->height*.5;}
static double room_gap(belt_v p,const double* e,belt_v* normal){
 double dx=p.x-fmax(e[0],fmin(e[2],p.x)),dy=p.y-fmax(e[1],fmin(e[3],p.y)),d=hypot(dx,dy);
 if(d>0){*normal=(belt_v){dx/d,dy/d};return d;}
 double nearest=p.x-e[0];*normal=(belt_v){-1,0};
 if(e[2]-p.x<nearest){nearest=e[2]-p.x;*normal=(belt_v){1,0};}
 if(p.y-e[1]<nearest){nearest=p.y-e[1];*normal=(belt_v){0,-1};}
 if(e[3]-p.y<nearest){nearest=e[3]-p.y;*normal=(belt_v){0,1};}
 return -nearest;
}
/* Determine the first entry face of the material segment, not its exit face. */
static belt_v room_entry_normal(belt_v from,belt_v to,const double* e,belt_v fallback){
 double enter=-INFINITY,leave=INFINITY;belt_v n=fallback;
 for(int axis=0;axis<2;++axis){double a=axis?from.y:from.x,d=(axis?to.y:to.x)-a;
  if(fabs(d)<1e-15){if(a<e[axis]||a>e[axis+2])return fallback;continue;}
  double lo=(e[axis]-a)/d,hi=(e[axis+2]-a)/d;belt_v near=axis?(belt_v){0,-1}:(belt_v){-1,0};
  if(lo>hi){double swap=lo;lo=hi;hi=swap;near=belt_mul(-1,near);}
  if(lo>enter){enter=lo;n=near;}leave=fmin(leave,hi);
 }
 return enter>=-1e-8&&enter<=1&&enter<=leave?n:fallback;
}
typedef struct room_cell {double center,half,lo,hi;int theta_depth,time_depth;} room_cell;
/* Outward coordinate bounds for an entire theta x interpolation-time cell.
   Time is affine, so its coordinate extrema occur at the time endpoints. */
static void room_cell_bounds(const double* a,const double* b,const room_cell* c,double* out){
 out[0]=out[1]=INFINITY;out[2]=out[3]=-INFINITY;
 for(int endpoint=0;endpoint<2;++endpoint){double q[BELT_D],s=endpoint?c->hi:c->lo;
  for(int j=0;j<BELT_D;++j)q[j]=(1-s)*a[j]+s*b[j];
  belt_v p=belt_eval(q,c->center,0),v=belt_eval(q,c->center,1);double second[2]={0},amplitude[2]={0};
  for(int axis=0;axis<2;++axis){amplitude[axis]=fabs(q[axis]);for(int n=1;n<=BELT_K;++n){double term=fabs(q[4*n-2+axis])+fabs(q[4*n+axis]);second[axis]+=n*n*term;amplitude[axis]+=term;}
   double delta=fabs(axis?v.y:v.x)*c->half+.5*second[axis]*c->half*c->half;
   double roundoff=128*DBL_EPSILON*(1+amplitude[axis]+delta+second[axis]);double value=axis?p.y:p.x;
   out[axis]=fmin(out[axis],nextafter(value-delta-roundoff,-INFINITY));out[axis+2]=fmax(out[axis+2],nextafter(value+delta+roundoff,INFINITY));
  }
 }
}
/* Returns 1 only with a continuous proof, 0 with an interior witness, -1 on
   bounded uncertainty/budget. Samples are never a passing certificate. */
static int room_certificate(room_world* w,const double* from,const double* to,double* theta,int swept){
#ifdef DANGO_BELT_ROOM_TRACE
 memcpy(room_trace_from,from,sizeof room_trace_from);memcpy(room_trace_to,to,sizeof room_trace_to);
#endif
 double e[4];room_edges(&w->platform,e);room_cell stack[64];int pending=1;stack[0]=(room_cell){BELT_PI,BELT_PI,0,swept?1:0,0,0};
 while(pending){if(w->diag[4]>=ROOM_GEOMETRY_BUDGET){room_refuse(w,DANGO_BELT_ROOM_BUDGET,2);return -1;}w->diag[4]+=1;
  room_cell c=stack[--pending];w->diag[5]=fmax(w->diag[5],c.theta_depth);w->diag[6]=fmax(w->diag[6],c.time_depth);double box[4];room_cell_bounds(from,to,&c,box);
#ifdef DANGO_BELT_ROOM_TRACE
  room_trace_cell[0]=c.center;room_trace_cell[1]=c.half;room_trace_cell[2]=c.lo;room_trace_cell[3]=c.hi;room_trace_cell[4]=c.theta_depth;room_trace_cell[5]=c.time_depth;
#endif
  if(box[2]<=e[0]+ROOM_PENETRATION||box[0]>=e[2]-ROOM_PENETRATION||box[3]<=e[1]+ROOM_PENETRATION||box[1]>=e[3]-ROOM_PENETRATION)continue;
  double q[BELT_D],s=.5*(c.lo+c.hi);for(int j=0;j<BELT_D;++j)q[j]=(1-s)*from[j]+s*to[j];belt_v p=belt_eval(q,c.center,0),n;double gap=room_gap(p,e,&n);
  if(gap<-ROOM_PENETRATION){*theta=c.center;w->diag[8]=fmax(w->diag[8],-gap);return 0;}
  if(c.theta_depth>=20&&(!swept||c.time_depth>=16)){room_refuse(w,DANGO_BELT_ROOM_UNCERTAIN,3);return -1;}
  double theta_width=fmax(box[2]-box[0],box[3]-box[1]);belt_v p0=belt_eval(from,c.center,0),p1=belt_eval(to,c.center,0);double time_width=hypot(p1.x-p0.x,p1.y-p0.y)*(c.hi-c.lo);
  if(swept&&c.time_depth<16&&(c.theta_depth>=20||time_width>theta_width*.5)){
   double mid=.5*(c.lo+c.hi);room_cell right=c;right.lo=mid;right.time_depth++;c.hi=mid;c.time_depth++;stack[pending++]=right;stack[pending++]=c;
  }else{room_cell right=c;right.center+=c.half*.5;right.half*=.5;right.theta_depth++;c.center-=c.half*.5;c.half*=.5;c.theta_depth++;stack[pending++]=right;stack[pending++]=c;}
 }
 return 1;
}
/* Chord winding with an analytic Fourier interpolation-error tube. If the
   test point is outside every tube, the polygon and curve are homotopic around
   it; this is not an unbounded sampled-point containment assumption. */
static int room_containment(room_world* w,const double* q){
 double second=0;for(int n=1;n<=BELT_K;++n)second+=n*n*(hypot(q[4*n-2],q[4*n-1])+hypot(q[4*n],q[4*n+1]));
 double h=2*BELT_PI/256,tube=second*h*h/8+128*DBL_EPSILON*(1+belt_guard_norm_bound(q,0));belt_v point={w->platform.x,w->platform.y};int winding=0;
 for(int i=0;i<256;++i){belt_v a=belt_eval(q,i*h,0),b=belt_eval(q,(i+1)*h,0),edge=belt_sub(b,a),rel=belt_sub(point,a);double len2=belt_dot(edge,edge),s=len2?fmax(0,fmin(1,belt_dot(rel,edge)/len2)):0;
  if(hypot(rel.x-s*edge.x,rel.y-s*edge.y)<=tube){room_refuse(w,DANGO_BELT_ROOM_UNCERTAIN,3);return 0;}
  double cross=belt_cross(edge,rel);if(a.y<=point.y&&b.y>point.y&&cross>0)winding++;if(a.y>point.y&&b.y<=point.y&&cross<0)winding--;
 }
 if(winding){room_refuse(w,DANGO_BELT_ROOM_REJECTED,4);return 0;}return 1;
}
typedef struct room_row {double basis[BELT_B];belt_v n;double pn,pt,mu;int platform;} room_row;
static double room_gap_derivative(const double* q,double theta,const double* e){belt_v normal;room_gap(belt_eval(q,theta,0),e,&normal);return belt_dot(normal,belt_eval(q,theta,1));}
static double room_corner_derivative(const double* q,double theta,belt_v corner){return belt_dot(belt_sub(belt_eval(q,theta,0),corner),belt_eval(q,theta,1));}
/* Complete active-corner root isolation. A cell is discarded only by a
   conservative distance/Voronoi/root bound, or proven monotonicity. Near
   multiple unresolved roots fail at the frozen depth/work cap. */
static int room_corner_roots(room_world* w,const double* q,double* angles,int* count){
 double e[4];room_edges(&w->platform,e);double u=0,a=0,j=0;for(int n=1;n<=BELT_K;++n){double term=hypot(q[4*n-2],q[4*n-1])+hypot(q[4*n],q[4*n+1]);u+=n*term;a+=n*n*term;j+=n*n*n*term;}
 for(int corner_index=0;corner_index<4;++corner_index){int right=corner_index&1,top=corner_index&2;belt_v corner={right?e[2]:e[0],top?e[3]:e[1]};double radius=hypot(q[0]-corner.x,q[1]-corner.y)+belt_guard_norm_bound(q,0)+hypot(q[0]-q[34],q[1]-q[35]);double second=3*u*a+radius*j;
  room_cell stack[32];int pending=1;stack[0]=(room_cell){BELT_PI,BELT_PI,0,0,0,0};
  while(pending){if(w->diag[4]>=ROOM_GEOMETRY_BUDGET)return room_refuse(w,DANGO_BELT_ROOM_BUDGET,2);w->diag[4]++;room_cell c=stack[--pending];w->diag[5]=fmax(w->diag[5],c.theta_depth);double box[4];room_cell_bounds(q,q,&c,box);
   if((right?box[2]<corner.x:box[0]>corner.x)||(top?box[3]<corner.y:box[1]>corner.y))continue;
   double dx=fmax(box[0]-corner.x,fmax(0,corner.x-box[2])),dy=fmax(box[1]-corner.y,fmax(0,corner.y-box[3]));if(hypot(dx,dy)>ROOM_ACTIVATION)continue;
   belt_v r=belt_sub(belt_eval(q,c.center,0),corner),v=belt_eval(q,c.center,1),acc=belt_eval(q,c.center,2);double f=belt_dot(r,v),df=belt_dot(v,v)+belt_dot(r,acc),roundoff=256*DBL_EPSILON*(1+radius+u+a+j+fabs(f)+fabs(df));
   if(fabs(f)>(fabs(df)+.5*second*c.half)*c.half+roundoff)continue;
   if(fabs(df)>second*c.half+roundoff){double lo=c.center-c.half,hi=c.center+c.half,fl=room_corner_derivative(q,lo,corner),fh=room_corner_derivative(q,hi,corner);
    if((fl>roundoff&&fh>roundoff)||(fl<-roundoff&&fh<-roundoff))continue;
    if(df<0)continue; /* Unique maximum, never an active distance minimum. */
    if(fabs(fl)<=roundoff)hi=lo;else if(fabs(fh)<=roundoff)lo=hi;else for(int k=0;k<52;++k){double mid=.5*(lo+hi),fm=room_corner_derivative(q,mid,corner);if(fm>0)hi=mid;else lo=mid;}
    if(*count>=512)return room_refuse(w,DANGO_BELT_ROOM_UNCERTAIN,3);angles[(*count)++]=.5*(lo+hi);continue;
   }
   if(c.theta_depth>=20)return room_refuse(w,DANGO_BELT_ROOM_UNCERTAIN,3);room_cell next=c;next.center+=c.half*.5;next.half*=.5;next.theta_depth++;c.center-=c.half*.5;c.half*=.5;c.theta_depth++;stack[pending++]=next;stack[pending++]=c;
  }
 }
 return 1;
}
static int room_rows(room_world* w,belt_body* b,room_row* rows){
 belt_contact ground[64];int n=belt_contact_rows(b,ground,ROOM_ACTIVATION);
 for(int i=0;i<n;++i){memset(&rows[i],0,sizeof rows[i]);memcpy(rows[i].basis,ground[i].basis,sizeof rows[i].basis);rows[i].n=(belt_v){0,1};rows[i].mu=b->cfg.friction;}
 double angles[512],roots[64],e[4];room_edges(&w->platform,e);int count=0;
 /* Face extrema are analytic Fourier roots. Only minima of the signed gap
    enter the contact manifold; near-contact samples are not constraint rows. */
 for(int axis=0;axis<2;++axis){int nr=belt_roots(b->q,axis,1,0,roots);for(int i=0;i<nr;++i)angles[count++]=roots[i];}
 int face_count=count;
 if(!room_corner_roots(w,b->q,angles,&count))return -1;
 for(int i=0;i<count&&n<512;++i){belt_v p=belt_eval(b->q,angles[i],0),normal;double gap=room_gap(p,e,&normal);if(gap>ROOM_ACTIVATION)continue;
  if(i<face_count&&fabs(room_gap_derivative(b->q,angles[i],e))>256*DBL_EPSILON*(1+belt_guard_norm_bound(b->q,1)))continue;
  if(belt_dot(normal,belt_eval(b->q,angles[i],2))<0&&fabs(normal.x*normal.y)<1e-12)continue;
  int duplicate=0;for(int k=0;k<n;++k)if(rows[k].platform&&hypot(rows[k].n.x-normal.x,rows[k].n.y-normal.y)<1e-10){double basis[BELT_B];belt_basis(angles[i],0,basis);if(fabs(basis[1]-rows[k].basis[1])+fabs(basis[2]-rows[k].basis[2])<1e-9){duplicate=1;break;}}if(duplicate)continue;
  room_row* row=&rows[n++];memset(row,0,sizeof *row);belt_basis(angles[i],0,row->basis);row->n=normal;row->mu=fmin(b->cfg.friction,w->platform.friction);row->platform=1;
 }
 return n;
}
static int room_project(room_world* w,belt_body* b,double theta,belt_v normal,double penetration){
 double basis[BELT_B],effective=0,trial[BELT_D];belt_basis(theta,0,basis);
 for(int j=0;j<BELT_B;++j)effective+=basis[j]*basis[j]/b->mass[2*j];if(!(effective>0)||!isfinite(penetration))return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);
 double lambda=(penetration+2e-9)/effective,max_change=0;memcpy(trial,b->q,sizeof trial);
 for(int j=0;j<BELT_B;++j){double delta=lambda*basis[j]/b->mass[2*j];max_change=fmax(max_change,fabs(delta*normal.x));max_change=fmax(max_change,fabs(delta*normal.y));}
 double scale=max_change>ROOM_POSITION_LIMIT?ROOM_POSITION_LIMIT/max_change:1;
 for(int j=0;j<BELT_B;++j){double delta=scale*lambda*basis[j]/b->mass[2*j];trial[2*j]+=delta*normal.x;trial[2*j+1]+=delta*normal.y;}
 if(!belt_guard_path(b,b->q,trial,3)||!belt_material_domain_valid(b,trial)){
  if(b->guard[9]==BELT_GUARD_BUDGET)return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);
  /* Shape-preserving candidate only: final all-surface and swept certificates
     remain mandatory. This is never an unconditional floor-style fallback. */
  memcpy(trial,b->q,sizeof trial);double shift=fmin(ROOM_POSITION_LIMIT,penetration+2e-9);trial[0]+=shift*normal.x;trial[1]+=shift*normal.y;trial[34]+=shift*normal.x;trial[35]+=shift*normal.y;max_change=shift;
  if(!belt_guard_path(b,b->q,trial,3)||!belt_material_domain_valid(b,trial))return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);
 }
 double elastic=belt_elastic_energy(b),gravity=belt_gravity_energy(b);memcpy(b->q,trial,sizeof trial);double de=belt_elastic_energy(b)-elastic,dg=belt_gravity_energy(b)-gravity;
 w->diag[12]+=de;w->diag[13]+=dg;w->diag[14]=fmax(w->diag[14],fmin(max_change,ROOM_POSITION_LIMIT));w->diag[15]+=fmax(0,de+dg);w->last_theta=theta;return 1;
}
static void belt_room_before_integrate(belt_body* b){if(room_active&&room_active->platform_enabled)memcpy(room_active->old_q,b->q,sizeof b->q);}
static int belt_room_contact(belt_body* b){
 room_world* w=room_active;if(!w||!w->platform_enabled)return belt_contact_solve(b);
 if(room_negative_mode==1)return belt_contact_solve(b);
 double e[4];room_edges(&w->platform,e);int clean=0;
 for(int iteration=0;iteration<32;++iteration){w->diag[16]+=1;double bounds[4],foot;belt_bounds(b->q,bounds,&foot);
  if(bounds[1]<b->cfg.ground_height-2e-9){if(!room_project(w,b,foot,(belt_v){0,1},b->cfg.ground_height-bounds[1]))return 0;continue;}
  double theta=0;int ok=room_certificate(w,b->q,b->q,&theta,0);if(ok<0)return 0;
  if(ok){ok=room_certificate(w,w->old_q,b->q,&theta,1);if(ok<0)return 0;}
  if(ok){if(!room_containment(w,b->q))return 0;clean=1;break;}
  /* Use the deepest true face extremum when available. A certificate witness
     alone can be arbitrarily close to the threshold and is a poor projection
     pivot; it remains the mandatory fallback for non-face geometry. */
  belt_v ignored;double deepest=room_gap(belt_eval(b->q,theta,0),e,&ignored);for(int axis=0;axis<2;++axis){double roots[64];int nr=belt_roots(b->q,axis,1,0,roots);for(int i=0;i<nr;++i){double gap=room_gap(belt_eval(b->q,roots[i],0),e,&ignored);if(gap<deepest){deepest=gap;theta=roots[i];}}}
  belt_v p=belt_eval(b->q,theta,0),old=belt_eval(w->old_q,theta,0),normal;room_gap(p,e,&normal);normal=room_entry_normal(old,p,e,normal);
  double gap=normal.x<-.5?e[0]-p.x:normal.x>.5?p.x-e[2]:normal.y<-.5?e[1]-p.y:p.y-e[3];
  if(gap>=0)return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);if(!room_project(w,b,theta,normal,-gap))return 0;
 }
 if(!clean)return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);
 room_row rows[512];int n=room_rows(w,b,rows);if(n<0)return 0;double before=belt_kinetic(b),residual=0;int iteration;
 for(iteration=0;iteration<300;++iteration){residual=0;for(int i=0;i<n;++i){room_row* r=&rows[i];double k=0,vn=0,vt=0;belt_v t={-r->n.y,r->n.x};for(int j=0;j<BELT_B;++j){double weight=r->basis[j];k+=weight*weight/b->mass[2*j];vn+=weight*(r->n.x*b->v[2*j]+r->n.y*b->v[2*j+1]);vt+=weight*(t.x*b->v[2*j]+t.y*b->v[2*j+1]);}
   double next_n=fmax(0,r->pn-vn/k),next_t=fmax(-r->mu*next_n,fmin(r->mu*next_n,r->pt-vt/k)),dn=next_n-r->pn,dt=next_t-r->pt;residual=fmax(residual,k*hypot(dn,dt));r->pn=next_n;r->pt=next_t;
   for(int j=0;j<BELT_B;++j){double weight=r->basis[j]/b->mass[2*j];b->v[2*j]+=weight*(r->n.x*dn+t.x*dt);b->v[2*j+1]+=weight*(r->n.y*dn+t.y*dt);}}
  if(residual<2e-8)break;
 }
 double after=belt_kinetic(b);w->diag[9]=fmax(w->diag[9],residual);w->diag[10]+=before;w->diag[11]+=after;w->diag[17]+=iteration<300?iteration+1:300;
 if(residual>2e-6||!(after<=before+1e-9*(1+before)))return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);
 b->grounded=0;w->diag[18]=w->diag[19]=0;
 for(int i=0;i<n;++i){room_row* r=&rows[i];w->diag[20]+=r->pn;w->diag[21]+=fabs(r->pt);if(!r->platform)b->grounded=1;else if(r->n.y>=.7&&b->q[35]>e[3]&&b->v[35]<.5){b->grounded=1;w->diag[18]=(double)(uint32_t)w->platform.id;w->diag[19]=(double)(uint32_t)(w->platform.id>>32);}}
 b->stats[25]=n;b->stats[26]+=before;b->stats[27]+=after;b->stats[30]=fmax(b->stats[30],residual);b->diag[12]+=before;b->diag[13]+=after;b->diag[14]+=after-before;w->diag[22]+=1;return 1;
}
int dango_belt_room_version(void){return DANGO_BELT_ROOM_ABI;}
int dango_belt_room_capabilities(void){return 15;}
int dango_belt_room_frame_size(void){return (int)sizeof(dango_belt_room_frame);}
const char* dango_belt_room_model(void){return "continuous-belt-room-v1";}
const char* dango_belt_room_tuning(void){return "C455-F-v1";}
const char* dango_belt_room_contact_model(void){return "belt-solid-aabb-v1";}
int dango_belt_room_create(const dango_belt_config* cfg,const float* cal,int count,float x,float y,float left,float right,void** output){
 if(!output||!cfg||!cal||count!=11||cfg->substep_count!=4||!isfinite(left)||!isfinite(right)||left>=right)return DANGO_BELT_ROOM_ARGUMENT;
 static const float selected[11]={1.4f,1,1,4,1.4f,1,1,1,0,35,60};for(int i=0;i<11;++i)if(cal[i]!=(i==8?cfg->linear_damping:selected[i]))return DANGO_BELT_ROOM_CONFIG;
 int slot=-1;for(int i=0;i<16;++i)if(!room_handles[i].world){slot=i;break;}if(slot<0||room_next_generation==UINT64_MAX)return DANGO_BELT_ROOM_CAPACITY;
 room_world* w=calloc(1,sizeof *w);if(!w)return DANGO_BELT_ROOM_CAPACITY;int rc=dango_belt_create_calibrated(cfg,x,y,1,cal,count,&w->body_handle);
 if(!rc)rc=dango_belt_set_shape_guard(w->body_handle,1);if(!rc)rc=dango_belt_set_speed_saturation(w->body_handle,1);if(!rc)rc=dango_belt_set_radial_drive(w->body_handle,1);if(!rc)rc=dango_belt_set_original_ground_jump(w->body_handle,1);if(!rc)rc=dango_belt_set_bounds(w->body_handle,left,right);
 if(rc){if(w->body_handle)dango_belt_destroy(w->body_handle);free(w);return DANGO_BELT_ROOM_CONFIG;}
 w->generation=room_next_generation++;w->diag[0]=1;uintptr_t token=(uintptr_t)(UINT64_C(0x4252000000000000)|w->generation);room_handles[slot].token=token;room_handles[slot].world=w;*output=(void*)token;return 0;
}
int dango_belt_room_step(void* handle,float dt,const dango_belt_room_input* input,int count){
 room_world* w=room_lookup(handle);if(!w)return DANGO_BELT_ROOM_HANDLE;if(w->fault)return DANGO_BELT_ROOM_FAULT;
 if(!input||count!=1||input->body_id!=1||input->reserved||dt!=.0166666f)return DANGO_BELT_ROOM_ARGUMENT;for(int i=0;i<9;++i)if(!isfinite(input->controls[i]))return DANGO_BELT_ROOM_ARGUMENT;
 const float* c=input->controls;if(fabsf(c[0])>1||c[1]<0||c[1]>1||c[6]<0||c[6]>1)return DANGO_BELT_ROOM_ARGUMENT;const int flags[4]={2,3,7,8};for(int i=0;i<4;++i)if(c[flags[i]]!=0&&c[flags[i]]!=1)return DANGO_BELT_ROOM_ARGUMENT;if(w->tick==UINT64_MAX)return DANGO_BELT_ROOM_REVISION;
 belt_body* b=room_body(w);belt_body backup=*b;room_world world_backup=*w;
#ifdef DANGO_BELT_ROOM_TRACE
 room_trace_safe_body=backup;
#endif
 double attempt=w->diag[1]+1;memset(w->diag,0,sizeof w->diag);w->diag[0]=1;w->diag[1]=attempt;room_active=w;int rc=dango_belt_step(w->body_handle,dt,input->controls,9);room_active=NULL;
 if(rc){
#ifdef DANGO_BELT_ROOM_TRACE
 if(!w->diag[2])room_trace_attempt_body=*b;
#endif
 double rejected[24];memcpy(rejected,w->diag,sizeof rejected);int code=rejected[2]?(int)rejected[2]:DANGO_BELT_ROOM_REJECTED;rejected[2]=code;rejected[3]=rc;*b=backup;*w=world_backup;memcpy(w->diag,rejected,sizeof rejected);w->fault=code;
#if DANGO_BELT_ROOM_NEGATIVE == 2
 b->q[0]+=1;
#endif
 return code;}
 w->tick++;return 0;
}
int dango_belt_room_read_frame(void* handle,dango_belt_room_frame* output,int bytes){
 room_world* w=room_lookup(handle);if(!w)return DANGO_BELT_ROOM_HANDLE;if(!output||bytes<(int)sizeof *output)return DANGO_BELT_ROOM_ARGUMENT;
 dango_belt_room_frame frame;memset(&frame,0,sizeof frame);frame.size=sizeof frame;frame.abi=1;frame.generation=w->generation;frame.tick=w->tick;frame.scene_revision=w->revision;frame.body_id=frame.config_revision=1;frame.platform_enabled=w->platform_enabled;frame.fault=w->fault;if(w->platform_enabled)frame.platform=w->platform;
 if(dango_belt_read_snapshot(w->body_handle,frame.body_snapshot,200)||dango_belt_read(w->body_handle,frame.scaffold,68,frame.curve,512,frame.stats,64))return DANGO_BELT_ROOM_REJECTED;memcpy(frame.diagnostics,w->diag,sizeof frame.diagnostics);memcpy(output,&frame,sizeof frame);return 0;
}
static int room_platform_valid(room_world* w,const dango_belt_room_platform* p){
 if(!p||!p->id||!isfinite(p->x)||!isfinite(p->y)||!isfinite(p->width)||!isfinite(p->height)||!isfinite(p->friction)||p->width<.1||p->width>16||p->height<.1||p->height>16||p->friction<0||p->friction>1.5)return 0;
 double e[4];room_edges(p,e);belt_body* b=room_body(w);for(int i=0;i<4;++i)if(fabs(e[i])>1024)return 0;return e[0]>=b->left&&e[2]<=b->right&&e[1]>=b->cfg.ground_height;
}
int dango_belt_room_set_platform(void* handle,const dango_belt_room_platform* p,uint64_t revision){
 room_world* w=room_lookup(handle);if(!w)return DANGO_BELT_ROOM_HANDLE;if(w->fault)return DANGO_BELT_ROOM_FAULT;if(revision!=w->revision||w->revision==UINT64_MAX)return DANGO_BELT_ROOM_REVISION;if(!room_platform_valid(w,p))return DANGO_BELT_ROOM_ARGUMENT;
 if(w->platform_enabled?p->id!=w->platform.id:p->id<=w->max_platform_id)return DANGO_BELT_ROOM_REVISION;
 room_world candidate=*w;candidate.platform=*p;candidate.platform_enabled=1;memset(candidate.diag,0,sizeof candidate.diag);double theta=0;belt_body* b=room_body(w);int proof=room_certificate(&candidate,b->q,b->q,&theta,0);if(proof!=1||!room_containment(&candidate,b->q))return candidate.diag[2]?(int)candidate.diag[2]:DANGO_BELT_ROOM_REJECTED;
 w->platform=*p;w->platform_enabled=1;w->max_platform_id=p->id;w->revision++;b->grounded=0;double bounds[4],foot;belt_bounds(b->q,bounds,&foot);b->grounded=bounds[1]<=b->cfg.ground_height+ROOM_ACTIVATION;w->diag[18]=w->diag[19]=0;b->stats[25]=0;belt_read_stats(b);return 0;
}
int dango_belt_room_remove_platform(void* handle,uint64_t id,uint64_t revision){
 room_world* w=room_lookup(handle);if(!w)return DANGO_BELT_ROOM_HANDLE;if(w->fault)return DANGO_BELT_ROOM_FAULT;if(!w->platform_enabled||id!=w->platform.id||revision!=w->revision||w->revision==UINT64_MAX)return DANGO_BELT_ROOM_REVISION;
 w->platform_enabled=0;memset(&w->platform,0,sizeof w->platform);w->revision++;belt_body* b=room_body(w);double bounds[4],foot;belt_bounds(b->q,bounds,&foot);b->grounded=bounds[1]<=b->cfg.ground_height+ROOM_ACTIVATION;w->diag[18]=w->diag[19]=0;b->stats[25]=0;belt_read_stats(b);return 0;
}
int dango_belt_room_destroy(void* handle){room_world* w=room_lookup(handle);if(!w)return DANGO_BELT_ROOM_HANDLE;for(int i=0;i<16;++i)if(room_handles[i].world==w){dango_belt_destroy(w->body_handle);free(w);room_handles[i].world=NULL;room_handles[i].token=0;return 0;}return DANGO_BELT_ROOM_HANDLE;}

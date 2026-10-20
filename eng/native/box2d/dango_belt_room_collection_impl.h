#ifndef DANGO_BELT_ROOM_COLLECTION_IMPL_H
#define DANGO_BELT_ROOM_COLLECTION_IMPL_H
_Static_assert(sizeof(dango_belt_room_definition)==112,"collection definition ABI");
_Static_assert(sizeof(dango_ball_state)==88,"collection ball ABI");
_Static_assert(sizeof(dango_ball_contact)==104,"collection contact ABI");
_Static_assert(sizeof(dango_belt_room_collection_item)==416,"collection item ABI");
_Static_assert(sizeof(dango_belt_room_collection_frame)==16968,"collection frame ABI");
_Static_assert(sizeof(dango_belt_room_collection_probe)==48,"collection probe ABI");
_Static_assert(sizeof(dango_belt_room_collection_token)==40,"collection token ABI");
#define COLLECTION_ROWS 16384
#ifndef DANGO_BELT_ROOM_COLLECTION_NEGATIVE
#define DANGO_BELT_ROOM_COLLECTION_NEGATIVE 0
#endif
static volatile int collection_negative_mode=DANGO_BELT_ROOM_COLLECTION_NEGATIVE;
#ifdef DANGO_BELT_ROOM_COLLECTION_TESTS
static int collection_test_fault;
static room_cell collection_failure_cell;
static double collection_failure_from[BELT_D],collection_failure_to[BELT_D],collection_failure_radius;
static dango_belt_room_collection_item collection_failure_ball;
static belt_body collection_failure_body;
#endif
typedef struct collection_row {
 room_row body;int body_sign,a,b,surface,owner;double target,vn_before,vt_before,speed_before,speed_other,gap;
} collection_row;
static dango_belt_room_platform collection_platform(const dango_belt_room_collection_item* i){
 const dango_belt_room_definition* d=&i->definition;
 return (dango_belt_room_platform){i->instance,d->x,d->y,d->width,d->height,d->friction};
}
static int collection_definition_valid(const dango_belt_room_definition* d){
 if(!d||d->reserved)return 0;
 if(d->kind==1)return isfinite(d->x)&&isfinite(d->y)&&fabs(d->x)<=1024&&fabs(d->y)<=1024&&
  isfinite(d->width)&&d->width>=.1&&d->width<=16&&isfinite(d->height)&&d->height>=.1&&d->height<=16&&
  isfinite(d->friction)&&d->friction>=0&&d->friction<=1.5;
 if(d->kind!=2)return 0;
 const dango_ball_definition* b=&d->ball;
 return isfinite(b->x)&&isfinite(b->y)&&fabs(b->x)<=1024&&fabs(b->y)<=1024&&isfinite(b->radius)&&b->radius>=.05&&b->radius<=4&&
  isfinite(b->mass)&&b->mass>=.001&&b->mass<=10&&isfinite(b->floor_friction)&&b->floor_friction>=0&&b->floor_friction<=1.5&&
  isfinite(b->dango_friction)&&b->dango_friction>=0&&b->dango_friction<=1.5&&isfinite(b->floor_restitution)&&b->floor_restitution>=0&&b->floor_restitution<=1&&
  isfinite(b->dango_restitution)&&b->dango_restitution>=0&&b->dango_restitution<=1;
}
static double collection_circle_rectangle(belt_v c,double radius,const dango_belt_room_platform* p,belt_v* n){double e[4];room_edges(p,e);return room_gap(c,e,n)-radius;}
static double collection_pair_gap(const dango_belt_room_collection_item* a,const dango_belt_room_collection_item* b,int live){
 if(a->definition.kind==1&&b->definition.kind==1)return fmax(fabs(a->definition.x-b->definition.x)-(a->definition.width+b->definition.width)*.5,
  fabs(a->definition.y-b->definition.y)-(a->definition.height+b->definition.height)*.5);
 if(a->definition.kind==2&&b->definition.kind==2){double ax=live?a->ball.x:a->definition.ball.x,ay=live?a->ball.y:a->definition.ball.y;
  double bx=live?b->ball.x:b->definition.ball.x,by=live?b->ball.y:b->definition.ball.y;return hypot(ax-bx,ay-by)-a->definition.ball.radius-b->definition.ball.radius;}
 const dango_belt_room_collection_item* ball=a->definition.kind==2?a:b;const dango_belt_room_collection_item* platform=a->definition.kind==1?a:b;
 dango_belt_room_platform p=collection_platform(platform);belt_v n;return collection_circle_rectangle((belt_v){live?ball->ball.x:ball->definition.ball.x,live?ball->ball.y:ball->definition.ball.y},ball->definition.ball.radius,&p,&n);
}
static int collection_bounds_valid(room_world* w,double left,double right){
 belt_body* b=room_body(w);
 for(int i=0;i<30;++i)if(w->items[i].instance){const dango_belt_room_collection_item* a=&w->items[i];
  if(a->definition.kind==1){dango_belt_room_platform p=collection_platform(a);double e[4];room_edges(&p,e);if(e[0]<left||e[2]>right||e[1]<b->cfg.ground_height)return 0;}
  else {double r=a->definition.ball.radius;if(a->ball.x-r<left||a->ball.x+r>right||a->ball.y-r<b->cfg.ground_height-ROOM_PENETRATION)return 0;}}
 return 1;
}
/* The polygon is used only with its analytic interpolation tube, as in the
   static-platform containment proof. A point in a tube is uncertain. */
static int collection_center_outside(room_world* w,const double* q,belt_v point){
 double second=0;for(int n=1;n<=BELT_K;++n)second+=n*n*(hypot(q[4*n-2],q[4*n-1])+hypot(q[4*n],q[4*n+1]));
 double h=2*BELT_PI/256,tube=second*h*h/8+128*DBL_EPSILON*(1+belt_guard_norm_bound(q,0));int winding=0;
 for(int i=0;i<256;++i){belt_v a=belt_eval(q,i*h,0),b=belt_eval(q,(i+1)*h,0),edge=belt_sub(b,a),rel=belt_sub(point,a);double len=belt_dot(edge,edge),s=len?fmax(0,fmin(1,belt_dot(rel,edge)/len)):0;
  if(hypot(rel.x-s*edge.x,rel.y-s*edge.y)<=tube)return room_refuse(w,DANGO_BELT_ROOM_UNCERTAIN,3);
  double cross=belt_cross(edge,rel);if(a.y<=point.y&&b.y>point.y&&cross>0)winding++;if(a.y>point.y&&b.y<=point.y&&cross<0)winding--;}
 return !winding;
}
static double collection_circle_derivative(const double* q,double theta,belt_v c){return belt_dot(belt_sub(belt_eval(q,theta,0),c),belt_eval(q,theta,1));}
/* Candidate location only. The independent interval proof decides admission. */
static double collection_circle_closest(const double* q,belt_v c,double radius,double* theta,belt_v* normal){
 double best=INFINITY,angle=0,lo=0,fl=collection_circle_derivative(q,0,c);
 for(int i=0;i<=256;++i){double hi=2*BELT_PI*i/256,fh=collection_circle_derivative(q,hi,c),candidate=hi;
  if(i&&fl<=0&&fh>=0){double a=lo,b=hi;for(int k=0;k<48;++k){double m=(a+b)*.5;if(collection_circle_derivative(q,m,c)>0)b=m;else a=m;}candidate=(a+b)*.5;}
  belt_v d=belt_sub(c,belt_eval(q,candidate,0));double length=hypot(d.x,d.y);if(length<best){best=length;angle=candidate;}lo=hi;fl=fh;}
 belt_v d=belt_sub(c,belt_eval(q,angle,0));*normal=best>1e-12?belt_mul(1/best,d):(belt_v){1,0};*theta=angle;return best-radius;
}
/* A radial Taylor bound retains x/y correlation near disk tangency. Center
   position and theta derivative are affine in time; squared radius and its
   theta derivative are quadratic, so their time extrema are analytic. */
static double collection_radial_lower(const double* a,const double* b,const room_cell* cell){
 belt_v pa=belt_eval(a,cell->center,0),pb=belt_eval(b,cell->center,0),da=belt_eval(a,cell->center,1),db=belt_eval(b,cell->center,1);
 belt_v pd=belt_sub(pb,pa),dd=belt_sub(db,da),p0=belt_add(pa,belt_mul(cell->lo,pd)),p1=belt_add(pa,belt_mul(cell->hi,pd)),path=belt_sub(p1,p0);
 double path2=belt_dot(path,path),t=path2?fmax(0,fmin(1,-belt_dot(p0,path)/path2)):0;belt_v nearest=belt_add(p0,belt_mul(t,path));double minimum=belt_dot(nearest,nearest);
 double quadratic=2*belt_dot(pd,dd),linear=2*(belt_dot(pa,dd)+belt_dot(pd,da)),constant=2*belt_dot(pa,da);
 double first=fmax(fabs((quadratic*cell->lo+linear)*cell->lo+constant),fabs((quadratic*cell->hi+linear)*cell->hi+constant));
 if(quadratic!=0){double stationary=-linear/(2*quadratic);if(stationary>cell->lo&&stationary<cell->hi)first=fmax(first,fabs((quadratic*stationary+linear)*stationary+constant));}
 /* guard_norm_bound(q,0) is relative to the body core. Here q is relative to
    the disk, so use the absolute Fourier constant and harmonic amplitudes. */
 double na=hypot(a[0],a[1]),nb=hypot(b[0],b[1]);for(int n=1;n<=BELT_K;++n){na+=hypot(a[4*n-2],a[4*n-1])+hypot(a[4*n],a[4*n+1]);nb+=hypot(b[4*n-2],b[4*n-1])+hypot(b[4*n],b[4*n+1]);}
 double norm=nextafter(fmax(na,nb)*(1+128*DBL_EPSILON)+128*DBL_EPSILON,INFINITY),d1=nextafter(fmax(belt_guard_norm_bound(a,1),belt_guard_norm_bound(b,1))*(1+128*DBL_EPSILON)+128*DBL_EPSILON,INFINITY),d2=nextafter(fmax(belt_guard_norm_bound(a,2),belt_guard_norm_bound(b,2))*(1+128*DBL_EPSILON)+128*DBL_EPSILON,INFINITY);
 first=nextafter(first+128*DBL_EPSILON*(1+norm*d1),INFINITY);
 double second=nextafter(2*(d1*d1+norm*d2)*(1+128*DBL_EPSILON),INFINITY),variation=first*cell->half+.5*second*cell->half*cell->half,roundoff=256*DBL_EPSILON*(1+norm*norm+variation);
 return nextafter(minimum-variation-roundoff,-INFINITY);
}
/* Bounds the true Fourier boundary relative to a linearly moving disk center,
   over the entire theta/time cell. No sampled contour can pass this test. */
static int collection_circle_certificate(room_world* w,const double* from,const double* to,belt_v ca,belt_v cb,double radius,double* theta,int swept){
 double a[BELT_D],b[BELT_D];memcpy(a,from,sizeof a);memcpy(b,to,sizeof b);a[0]-=ca.x;a[1]-=ca.y;b[0]-=cb.x;b[1]-=cb.y;
 room_cell stack[64];int pending=1;stack[0]=(room_cell){BELT_PI,BELT_PI,0,swept?1:0,0,0};double allowed=radius-ROOM_PENETRATION;
 while(pending){if(w->diag[4]>=ROOM_GEOMETRY_BUDGET){room_refuse(w,DANGO_BELT_ROOM_BUDGET,2);return -1;}w->diag[4]++;
  room_cell c=stack[--pending];double box[4];room_cell_bounds(a,b,&c,box);w->diag[5]=fmax(w->diag[5],c.theta_depth);w->diag[6]=fmax(w->diag[6],c.time_depth);
  double dx=fmax(0,fmax(box[0],-box[2])),dy=fmax(0,fmax(box[1],-box[3]));if(hypot(dx,dy)>=allowed||collection_radial_lower(a,b,&c)>=nextafter(allowed*allowed,INFINITY))continue;
  double q[BELT_D],s=(c.lo+c.hi)*.5;for(int j=0;j<BELT_D;++j)q[j]=(1-s)*a[j]+s*b[j];belt_v p=belt_eval(q,c.center,0);double gap=hypot(p.x,p.y)-radius;
  if(gap<-ROOM_PENETRATION){*theta=c.center;w->diag[8]=fmax(w->diag[8],-gap);return 0;}
  if(c.theta_depth>=20&&(!swept||c.time_depth>=16)){
#ifdef DANGO_BELT_ROOM_COLLECTION_TESTS
   collection_failure_cell=c;memcpy(collection_failure_from,a,sizeof a);memcpy(collection_failure_to,b,sizeof b);collection_failure_radius=radius;
#endif
   room_refuse(w,DANGO_BELT_ROOM_UNCERTAIN,3);return -1;}
  belt_v p0=belt_eval(a,c.center,0),p1=belt_eval(b,c.center,0);double tw=hypot(p1.x-p0.x,p1.y-p0.y)*(c.hi-c.lo),width=fmax(box[2]-box[0],box[3]-box[1]);
  if(swept&&c.time_depth<16&&(c.theta_depth>=20||tw>width*.5)){double m=(c.lo+c.hi)*.5;room_cell r=c;r.lo=m;r.time_depth++;c.hi=m;c.time_depth++;stack[pending++]=r;stack[pending++]=c;}
  else {room_cell r=c;r.center+=c.half*.5;r.half*=.5;r.theta_depth++;c.center-=c.half*.5;c.half*=.5;c.theta_depth++;stack[pending++]=r;stack[pending++]=c;}}
 return 1;
}
static double collection_ball_ke(const dango_belt_room_collection_item* i){double m=i->definition.ball.mass,r=i->definition.ball.radius;return .5*m*(i->ball.vx*i->ball.vx+i->ball.vy*i->ball.vy)+.25*m*r*r*i->ball.omega*i->ball.omega;}
static double collection_ke(room_world* w,belt_body* b){double e=belt_kinetic(b);for(int i=0;i<30;++i)if(w->items[i].instance&&w->items[i].definition.kind==2)e+=collection_ball_ke(&w->items[i]);return e;}
static int collection_ball_valid(const dango_belt_room_collection_item* i){
 const dango_ball_state* s=&i->ball;return isfinite(s->x)&&isfinite(s->y)&&isfinite(s->vx)&&isfinite(s->vy)&&isfinite(s->angle)&&isfinite(s->omega)&&fabs(s->x)<=1024&&fabs(s->y)<=1024&&hypot(s->vx,s->vy)<=12&&fabs(s->omega)<=40;
}
static void collection_before_integrate(room_world* w,belt_body* b){
 const double h=(double).0166666f/4;w->collection_error=0;
 for(int i=0;i<30;++i)if(w->items[i].instance&&w->items[i].definition.kind==2){dango_belt_room_collection_item* a=&w->items[i];
  if(b->solver_diag[4]==0){a->body_contact=(dango_ball_contact){0};a->surface_contact=(dango_ball_contact){0};w->body_report_weight[i]=w->surface_report_weight[i]=-1;}
  w->ball_old[i][0]=a->ball.x;w->ball_old[i][1]=a->ball.y;if(!collection_ball_valid(a)||a->ball.integrations==UINT64_MAX){w->collection_error=1;continue;}
  a->ball.vy-=.5*b->cfg.gravity*h;a->ball.x+=a->ball.vx*h;a->ball.y+=a->ball.vy*h;a->ball.angle+=a->ball.omega*h;a->ball.integrations++;a->ball.sleeping=0;
  if(!collection_ball_valid(a))w->collection_error=1;}
}
/* Symmetric mass-metric position projection for the actual Fourier point and
   disk. Both writes commit only after the unchanged body path/material guards. */
static int collection_project_body_ball(room_world* w,belt_body* b,int slot,double theta,belt_v n,double depth){
 dango_belt_room_collection_item* a=&w->items[slot];double basis[BELT_B],k=1/a->definition.ball.mass,trial[BELT_D];belt_basis(theta,0,basis);
 for(int j=0;j<BELT_B;++j)k+=basis[j]*basis[j]/b->mass[2*j];double lambda=(depth+2e-9)/k,maxchange=lambda/a->definition.ball.mass;
 for(int j=0;j<BELT_B;++j)maxchange=fmax(maxchange,fabs(lambda*basis[j]/b->mass[2*j]));if(maxchange>ROOM_POSITION_LIMIT)lambda*=ROOM_POSITION_LIMIT/maxchange;
 memcpy(trial,b->q,sizeof trial);for(int j=0;j<BELT_B;++j){trial[2*j]-=lambda*basis[j]/b->mass[2*j]*n.x;trial[2*j+1]-=lambda*basis[j]/b->mass[2*j]*n.y;}
 if(!belt_guard_path(b,b->q,trial,3)||!belt_material_domain_valid(b,trial))return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);
 double elastic=belt_elastic_energy(b),gravity=belt_gravity_energy(b),by=a->ball.y;memcpy(b->q,trial,sizeof trial);
 a->ball.x+=lambda/a->definition.ball.mass*n.x;a->ball.y+=lambda/a->definition.ball.mass*n.y;
 double de=belt_elastic_energy(b)-elastic,dg=belt_gravity_energy(b)-gravity+.5*a->definition.ball.mass*b->cfg.gravity*(a->ball.y-by);
 w->diag[12]+=de;w->diag[13]+=dg;w->diag[14]=fmax(w->diag[14],fmin(maxchange,ROOM_POSITION_LIMIT));w->diag[15]+=fmax(0,de+dg);return 1;
}
/* Segment distance for a swept relative disk pair. */
static double collection_swept_distance(belt_v a,belt_v b,double* fraction){belt_v d=belt_sub(b,a);double dd=belt_dot(d,d),t=dd?fmax(0,fmin(1,-belt_dot(a,d)/dd)):0;*fraction=t;belt_v p=belt_add(a,belt_mul(t,d));return hypot(p.x,p.y);}
static double collection_point_segment(belt_v p,belt_v a,belt_v b){belt_v d=belt_sub(b,a),rel=belt_sub(p,a);double dd=belt_dot(d,d),t=dd?fmax(0,fmin(1,belt_dot(rel,d)/dd)):0;return hypot(rel.x-t*d.x,rel.y-t*d.y);}
static double collection_segments_distance(belt_v a,belt_v b,belt_v c,belt_v d){belt_v u=belt_sub(b,a),v=belt_sub(d,c),w=belt_sub(c,a);double determinant=belt_cross(u,v);
 if(fabs(determinant)>1e-15){double s=belt_cross(w,v)/determinant,t=belt_cross(w,u)/determinant;if(s>=0&&s<=1&&t>=0&&t<=1)return 0;}
 return fmin(fmin(collection_point_segment(a,c,d),collection_point_segment(b,c,d)),fmin(collection_point_segment(c,a,b),collection_point_segment(d,a,b)));}
static double collection_swept_rectangle(belt_v a,belt_v b,const dango_belt_room_platform* p){double e[4];room_edges(p,e);belt_v ignored;if(room_gap(a,e,&ignored)<=0||room_gap(b,e,&ignored)<=0)return 0;
 belt_v corners[4]={{e[0],e[1]},{e[2],e[1]},{e[2],e[3]},{e[0],e[3]}};double best=INFINITY;for(int i=0;i<4;++i)best=fmin(best,collection_segments_distance(a,b,corners[i],corners[(i+1)%4]));return best;}
static int collection_body_platform_position(room_world* w,belt_body* b,const dango_belt_room_platform* p,int correct){
 dango_belt_room_platform saved=w->platform;w->platform=*p;double theta=0,e[4];room_edges(p,e);belt_v n;double deepest=INFINITY;
 for(int i=0;i<256;++i){double t=2*BELT_PI*i/256,g=room_gap(belt_eval(b->q,t,0),e,&n);if(g<deepest){deepest=g;theta=t;}}
 int result=room_certificate(w,b->q,b->q,&theta,0),swept=0;if(result==1){result=room_certificate(w,w->old_q,b->q,&theta,1);swept=result==0;}
 if(result==1&&!room_containment(w,b->q))result=-1;
 if(result==0&&correct){belt_v now=belt_eval(b->q,theta,0),old=belt_eval(w->old_q,theta,0);double gap=room_gap(now,e,&n);
  if(swept||gap<0){n=room_entry_normal(old,now,e,n);gap=n.x<-.5?e[0]-now.x:n.x>.5?now.x-e[2]:n.y<-.5?e[1]-now.y:now.y-e[3];}
  if(gap>=ROOM_CLEARANCE)result=-1;else result=room_project(w,b,theta,n,ROOM_CLEARANCE-gap)?2:-1;}
 w->platform=saved;return result;
}
static int collection_positions(room_world* w,belt_body* b){
 for(int iteration=0;iteration<=32;++iteration){int corrected=0,body_corrected=0;unsigned char moved[30]={0};w->diag[16]+=iteration<32;double bounds[4],foot;belt_bounds(b->q,bounds,&foot);
  if(bounds[1]<b->cfg.ground_height-2e-9){if(iteration==32||!room_project(w,b,foot,(belt_v){0,1},b->cfg.ground_height-bounds[1]+ROOM_CLEARANCE))return 0;continue;}
  if(w->solid_walls&&(bounds[0]<b->left||bounds[2]>b->right)){int right=bounds[2]>b->right;double depth=right?bounds[2]-b->right:b->left-bounds[0];if(iteration==32||!room_project(w,b,room_wall_theta(b->q,right),(belt_v){right?-1:1,0},depth+ROOM_CLEARANCE))return 0;continue;}
  for(int i=0;i<30&&!corrected;++i)if(w->items[i].instance&&w->items[i].definition.kind==1){dango_belt_room_platform p=collection_platform(&w->items[i]);int rc=collection_body_platform_position(w,b,&p,iteration<32);if(rc<1)return 0;corrected=rc==2;}
  if(corrected)continue;
  /* A world round visits every independent disk. Each disk and the body may
     move at most once in a round, preserving the original .01 coordinate cap
     without incorrectly charging thirty independent projections as 30 rounds. */
  for(int i=0;i<30;++i)if(w->items[i].instance&&w->items[i].definition.kind==2){dango_belt_room_collection_item* a=&w->items[i];double r=a->definition.ball.radius,theta=0;belt_v center={a->ball.x,a->ball.y},normal;
   if(!body_corrected&&!moved[i]){double gap=collection_circle_closest(b->q,center,r,&theta,&normal);
   int outside=collection_center_outside(w,b->q,center);if(!outside){if(w->diag[2])return 0;gap=-gap-2*r;normal=belt_mul(-1,normal);}
   int rc=collection_circle_certificate(w,b->q,b->q,center,center,r,&theta,0);if(rc<0){
#ifdef DANGO_BELT_ROOM_COLLECTION_TESTS
    collection_failure_ball=*a;collection_failure_body=*b;
#endif
    return 0;}
   int swept=0;if(rc){rc=collection_circle_certificate(w,w->old_q,b->q,(belt_v){w->ball_old[i][0],w->ball_old[i][1]},center,r,&theta,1);if(rc<0){
#ifdef DANGO_BELT_ROOM_COLLECTION_TESTS
    collection_failure_ball=*a;collection_failure_body=*b;
#endif
    return 0;}swept=rc==0;}
   if(!rc||!outside){if(iteration==32)return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);if(swept){belt_v oldrel=belt_sub((belt_v){w->ball_old[i][0],w->ball_old[i][1]},belt_eval(w->old_q,theta,0));double length=hypot(oldrel.x,oldrel.y);if(length>1e-12)normal=belt_mul(1/length,oldrel);gap=belt_dot(belt_sub(center,belt_eval(b->q,theta,0)),normal)-r;}
    else {gap=collection_circle_closest(b->q,center,r,&theta,&normal);if(!outside){gap=-gap-2*r;normal=belt_mul(-1,normal);}}
    if(gap>=ROOM_CLEARANCE||!collection_project_body_ball(w,b,i,theta,normal,ROOM_CLEARANCE-gap))return 0;corrected=body_corrected=1;moved[i]=1;continue;}}
   if(moved[i])continue;
   belt_v n={0,1};double depth=b->cfg.ground_height+r-a->ball.y;
   if(w->solid_walls&&b->left+r-a->ball.x>depth){depth=b->left+r-a->ball.x;n=(belt_v){1,0};}
   if(w->solid_walls&&a->ball.x+r-b->right>depth){depth=a->ball.x+r-b->right;n=(belt_v){-1,0};}
   for(int j=0;j<30;++j)if(w->items[j].instance&&w->items[j].definition.kind==1){dango_belt_room_platform p=collection_platform(&w->items[j]);belt_v pn;double pg=collection_circle_rectangle(center,r,&p,&pn);
    if(-pg>depth){depth=-pg;n=pn;}belt_v old={w->ball_old[i][0],w->ball_old[i][1]};if(collection_swept_rectangle(old,center,&p)<r-ROOM_PENETRATION&&pg>=-ROOM_PENETRATION){double e[4];room_edges(&p,e);room_gap(old,e,&pn);double ox=fmax(e[0],fmin(e[2],old.x)),oy=fmax(e[1],fmin(e[3],old.y));double needed=r-belt_dot(belt_sub(center,(belt_v){ox,oy}),pn);if(needed>depth){depth=needed;n=pn;}}}
   if(depth>ROOM_PENETRATION){if(iteration==32)return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);double delta=fmin(ROOM_POSITION_LIMIT,depth+ROOM_CLEARANCE);a->ball.x+=delta*n.x;a->ball.y+=delta*n.y;w->diag[13]+=.5*a->definition.ball.mass*b->cfg.gravity*delta*n.y;w->diag[14]=fmax(w->diag[14],delta);w->diag[15]+=fmax(0,.5*a->definition.ball.mass*b->cfg.gravity*delta*n.y);corrected=1;moved[i]=1;continue;}
   for(int j=i+1;j<30;++j)if(!moved[j]&&w->items[j].instance&&w->items[j].definition.kind==2){dango_belt_room_collection_item* other=&w->items[j];belt_v rel={other->ball.x-a->ball.x,other->ball.y-a->ball.y},old={w->ball_old[j][0]-w->ball_old[i][0],w->ball_old[j][1]-w->ball_old[i][1]};double rr=r+other->definition.ball.radius,length=hypot(rel.x,rel.y),t;
    double sweep=collection_swept_distance(old,rel,&t);if(length<rr-ROOM_PENETRATION||sweep<rr-ROOM_PENETRATION){if(iteration==32)return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);belt_v direction=length>1e-12?belt_mul(1/length,rel):(belt_v){1,0};if(length>=rr){double ol=hypot(old.x,old.y);if(ol>1e-12)direction=belt_mul(1/ol,old);}
     double distance=rr+ROOM_CLEARANCE-belt_dot(rel,direction),ma=a->definition.ball.mass,mb=other->definition.ball.mass,lambda=distance/(1/ma+1/mb),largest=fmax(lambda/ma,lambda/mb);if(largest>ROOM_POSITION_LIMIT)lambda*=ROOM_POSITION_LIMIT/largest;
     a->ball.x-=lambda/ma*direction.x;a->ball.y-=lambda/ma*direction.y;other->ball.x+=lambda/mb*direction.x;other->ball.y+=lambda/mb*direction.y;w->diag[14]=fmax(w->diag[14],fmin(largest,ROOM_POSITION_LIMIT));corrected=1;moved[i]=moved[j]=1;break;}}
  }
  if(!corrected)return 1;
 }
 return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);
}
static void collection_row_values(room_world* w,belt_body* b,const collection_row* r,double* kn,double* kt,double* vn,double* vt){
 belt_v n=r->body.n,t={-n.y,n.x};*kn=*kt=*vn=*vt=0;
 if(r->body_sign){for(int j=0;j<BELT_B;++j){double v=r->body.basis[j];*kn+=v*v/b->mass[2*j];*vn+=r->body_sign*v*(n.x*b->v[2*j]+n.y*b->v[2*j+1]);*vt+=r->body_sign*v*(t.x*b->v[2*j]+t.y*b->v[2*j+1]);}*kt=*kn;}
 for(int k=0;k<2;++k){int index=k?r->b:r->a;if(index<0)continue;const dango_belt_room_collection_item* a=&w->items[index];double sign=k?-1:1,m=a->definition.ball.mass,radius=a->definition.ball.radius,inertia=.5*m*radius*radius;
  *kn+=1/m;*kt+=1/m+radius*radius/inertia;*vn+=sign*(n.x*a->ball.vx+n.y*a->ball.vy);*vt+=sign*(t.x*a->ball.vx+t.y*a->ball.vy)-radius*a->ball.omega;}
}
static void collection_row_apply(room_world* w,belt_body* b,const collection_row* r,double dn,double dt){
 belt_v n=r->body.n,t={-n.y,n.x};if(r->body_sign)for(int j=0;j<BELT_B;++j){double weight=r->body_sign*r->body.basis[j]/b->mass[2*j];b->v[2*j]+=weight*(n.x*dn+t.x*dt);b->v[2*j+1]+=weight*(n.y*dn+t.y*dt);}
 for(int k=0;k<2;++k){int index=k?r->b:r->a;if(index<0)continue;dango_belt_room_collection_item* a=&w->items[index];double sign=k?-1:1,m=a->definition.ball.mass,radius=a->definition.ball.radius,inertia=.5*m*radius*radius;
  a->ball.vx+=sign*(n.x*dn+t.x*dt)/m;a->ball.vy+=sign*(n.y*dn+t.y*dt)/m;a->ball.omega-=radius*dt/inertia;}
}
static int collection_add_row(room_world* w,belt_body* b,collection_row* rows,int* count,int body_sign,int a,int other,belt_v n,double theta,double mu,double restitution,int was,int surface,int owner){
 if(*count>=COLLECTION_ROWS)return room_refuse(w,DANGO_BELT_ROOM_BUDGET,2);collection_row* r=&rows[(*count)++];memset(r,0,sizeof *r);r->body_sign=body_sign;r->a=a;r->b=other;r->body.n=n;r->body.mu=mu;r->surface=surface;r->owner=owner;
 if(body_sign)belt_basis(theta,0,r->body.basis);double kn,kt,vn,vt;collection_row_values(w,b,r,&kn,&kt,&vn,&vt);r->target=!was&&vn<=-.5?-vn*restitution:0;r->vn_before=vn;r->vt_before=vt;r->speed_before=a>=0?hypot(w->items[a].ball.vx,w->items[a].ball.vy):0;
 if(other>=0)r->speed_other=hypot(w->items[other].ball.vx,w->items[other].ball.vy);
 if(a>=0){const dango_belt_room_collection_item* disk=&w->items[a];double radius=disk->definition.ball.radius;
  if(body_sign==-1){belt_v p=belt_eval(b->q,theta,0);r->gap=hypot(disk->ball.x-p.x,disk->ball.y-p.y)-radius;}
  else if(other>=0)r->gap=hypot(disk->ball.x-w->items[other].ball.x,disk->ball.y-w->items[other].ball.y)-radius-w->items[other].definition.ball.radius;
  else if(surface==0)r->gap=disk->ball.y-radius-b->cfg.ground_height;
  else if(surface==-2)r->gap=disk->ball.x-radius-b->left;
  else if(surface==-3)r->gap=b->right-disk->ball.x-radius;
  else if(surface>0){dango_belt_room_platform p=collection_platform(&w->items[surface-1]);belt_v ignored;r->gap=collection_circle_rectangle((belt_v){disk->ball.x,disk->ball.y},radius,&p,&ignored);}}
 return 1;
}
static int collection_build_rows(room_world* w,belt_body* b,collection_row* rows,int* count){
 *count=0;room_row base[512];int old_enabled=w->platform_enabled;dango_belt_room_platform old=w->platform;w->platform_enabled=0;int n=room_rows(w,b,base);w->platform_enabled=old_enabled;
 if(n<0)return 0;for(int i=0;i<n;++i){collection_row* r=&rows[(*count)++];memset(r,0,sizeof *r);r->body=base[i];r->body_sign=1;r->a=r->b=-1;r->surface=base[i].platform==2?-2:0;r->owner=-1;}
 for(int slot=0;slot<30;++slot)if(w->items[slot].instance&&w->items[slot].definition.kind==1){w->platform=collection_platform(&w->items[slot]);w->platform_enabled=1;n=room_rows(w,b,base);w->platform=old;w->platform_enabled=old_enabled;
  if(n<0)return 0;for(int i=0;i<n;++i)if(base[i].platform==1){if(*count>=COLLECTION_ROWS)return room_refuse(w,DANGO_BELT_ROOM_BUDGET,2);collection_row* r=&rows[(*count)++];memset(r,0,sizeof *r);r->body=base[i];r->body_sign=1;r->a=r->b=-1;r->surface=slot+1;r->owner=-1;}}
 unsigned char nexttouch[30][32]={{0}},nextpair[30][30]={{0}};
 for(int i=0;i<30;++i)if(w->items[i].instance&&w->items[i].definition.kind==2){dango_belt_room_collection_item* a=&w->items[i];double r=a->definition.ball.radius,theta;belt_v center={a->ball.x,a->ball.y},normal;double gap=collection_circle_closest(b->q,center,r,&theta,&normal);
  if(gap<=ROOM_ACTIVATION){if(!collection_add_row(w,b,rows,count,-1,i,-1,normal,theta,fmin(b->cfg.friction,a->definition.ball.dango_friction),a->definition.ball.dango_restitution,w->touches[i][31],31,i))return 0;nexttouch[i][31]=1;}
  if(a->ball.y-r-b->cfg.ground_height<=ROOM_ACTIVATION){if(!collection_add_row(w,b,rows,count,0,i,-1,(belt_v){0,1},0,a->definition.ball.floor_friction,a->definition.ball.floor_restitution,w->touches[i][30],0,i))return 0;nexttouch[i][30]=1;}
  if(w->solid_walls){if(a->ball.x-r-b->left<=ROOM_ACTIVATION&&!collection_add_row(w,b,rows,count,0,i,-1,(belt_v){1,0},0,a->definition.ball.floor_friction,a->definition.ball.floor_restitution,0,-2,i))return 0;
   if(b->right-a->ball.x-r<=ROOM_ACTIVATION&&!collection_add_row(w,b,rows,count,0,i,-1,(belt_v){-1,0},0,a->definition.ball.floor_friction,a->definition.ball.floor_restitution,0,-3,i))return 0;}
  for(int j=0;j<30;++j)if(w->items[j].instance&&w->items[j].definition.kind==1){dango_belt_room_platform p=collection_platform(&w->items[j]);gap=collection_circle_rectangle(center,r,&p,&normal);if(gap<=ROOM_ACTIVATION){
    if(!collection_add_row(w,b,rows,count,0,i,-1,normal,0,fmin(a->definition.ball.floor_friction,p.friction),a->definition.ball.floor_restitution,w->touches[i][j],j+1,i))return 0;nexttouch[i][j]=1;}}
  for(int j=i+1;j<30;++j)if(w->items[j].instance&&w->items[j].definition.kind==2){dango_belt_room_collection_item* other=&w->items[j];belt_v d={other->ball.x-a->ball.x,other->ball.y-a->ball.y};double length=hypot(d.x,d.y);gap=length-r-other->definition.ball.radius;
   if(gap<=ROOM_ACTIVATION){normal=length>1e-12?belt_mul(1/length,d):(belt_v){1,0};if(!collection_add_row(w,b,rows,count,0,j,i,normal,0,sqrt(a->definition.ball.dango_friction*other->definition.ball.dango_friction),fmin(a->definition.ball.dango_restitution,other->definition.ball.dango_restitution),w->pair_touch[i][j],-4,i))return 0;nextpair[i][j]=nextpair[j][i]=1;}}
 }
 memcpy(w->touches,nexttouch,sizeof nexttouch);memcpy(w->pair_touch,nextpair,sizeof nextpair);return 1;
}
static void collection_contact_record(room_world* w,belt_body* b,const collection_row* r){
 if(r->body_sign==1){if(r->surface==0)b->grounded=1;else if(r->surface>0){const dango_belt_room_collection_item* p=&w->items[r->surface-1];double top=p->definition.y+p->definition.height*.5;
   if(r->body.n.y>=.7&&b->q[35]>top&&b->v[35]<.5){b->grounded=1;w->diag[18]=(double)(uint32_t)p->instance;w->diag[19]=(double)(uint32_t)(p->instance>>32);}}return;}
 if(r->a>=0&&r->b>=0){for(int k=0;k<2;++k){int owner=k?r->b:r->a,other=k?r->a:r->b;double sign=k?-1:1;dango_ball_contact* pair=&w->items[owner].surface_contact;
   if(r->body.pn>w->surface_report_weight[owner]){w->surface_report_weight[owner]=r->body.pn;pair->normal_speed_before=r->vn_before;pair->tangent_speed_before=r->vt_before;pair->ball_speed_before=k?r->speed_other:r->speed_before;
    pair->object_id=(int)w->items[other].instance;pair->nx=sign*r->body.n.x;pair->ny=sign*r->body.n.y;pair->gap=r->gap;}
   pair->touching=1;pair->normal_impulse+=r->body.pn;pair->tangent_impulse+=r->body.pt;}return;}
 if(r->owner<0)return;dango_belt_room_collection_item* a=&w->items[r->owner];dango_ball_contact* c=r->body_sign==-1?&a->body_contact:&a->surface_contact;
 double* weight=r->body_sign==-1?&w->body_report_weight[r->owner]:&w->surface_report_weight[r->owner];
 if(r->body.pn>*weight){*weight=r->body.pn;c->normal_speed_before=r->vn_before;c->tangent_speed_before=r->vt_before;c->ball_speed_before=r->speed_before;c->nx=r->body.n.x;c->ny=r->body.n.y;c->gap=r->gap;c->object_id=r->body_sign==-1?1:r->surface>0?(int)w->items[r->surface-1].instance:r->surface;
  if(r->body_sign==-1){double theta=atan2(r->body.basis[2],r->body.basis[1]);if(theta<0)theta+=2*BELT_PI;double material=theta/(2*BELT_PI)*256;c->sample=(int)material;c->fraction=material-c->sample;}}
 c->touching=1;c->normal_impulse+=r->body.pn;c->tangent_impulse+=r->body.pt;
}
static int belt_room_contact_empty_collection(room_world* w,belt_body* b){int enabled=w->collection_enabled;w->collection_enabled=0;int rc=belt_room_contact(b);w->collection_enabled=enabled;return rc;}
static int collection_contact(room_world* w,belt_body* b){
#ifdef DANGO_BELT_ROOM_COLLECTION_TESTS
 if(collection_test_fault&&b->solver_diag[4]==3){for(int i=0;i<30;++i)if(w->items[i].instance&&w->items[i].definition.kind==2){w->items[i].ball.x=NAN;w->collection_error=1;break;}collection_test_fault=0;}
#endif
 if(w->collection_error)return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);
 if(collection_negative_mode==1)return belt_contact_solve(b);
 if(!w->collection_count)return belt_room_contact_empty_collection(w,b);
 if(!collection_positions(w,b)){if(!w->diag[2])room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);return 0;}
 int platforms=0,balls=0;for(int i=0;i<30;++i)if(w->items[i].instance){if(w->items[i].definition.kind==1)platforms++;else balls++;}
 int capacity=512*(platforms+1)+balls*(platforms+4)+balls*(balls-1)/2;if(capacity>COLLECTION_ROWS)return room_refuse(w,DANGO_BELT_ROOM_BUDGET,2);
 collection_row* rows=calloc((size_t)capacity,sizeof *rows);if(!rows)return room_refuse(w,DANGO_BELT_ROOM_CAPACITY,2);int n=0;
 double floor_normal[30]={0},floor_tangent[30]={0};
 if(!collection_build_rows(w,b,rows,&n)){free(rows);return 0;}double before=collection_ke(w,b),body_before=belt_kinetic(b),residual=0;int iteration;
 for(iteration=0;iteration<300;++iteration){residual=0;for(int i=0;i<n;++i){collection_row* r=&rows[i];double kn,kt,vn,vt;collection_row_values(w,b,r,&kn,&kt,&vn,&vt);
   double pn=fmax(0,r->body.pn+(r->target-vn)/kn),pt=fmax(-r->body.mu*pn,fmin(r->body.mu*pn,r->body.pt-vt/kt)),dn=pn-r->body.pn,dt=pt-r->body.pt;residual=fmax(residual,fmax(kn*fabs(dn),kt*fabs(dt)));r->body.pn=pn;r->body.pt=pt;collection_row_apply(w,b,r,dn,dt);}
  if(residual<2e-8)break;}
 double after=collection_ke(w,b);w->diag[9]=fmax(w->diag[9],residual);w->diag[10]+=before;w->diag[11]+=after;w->diag[17]+=iteration<300?iteration+1:300;
 if(!isfinite(after)||residual>2e-6||after>before+1e-9*(1+before)){free(rows);return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);}
 b->grounded=0;w->diag[18]=w->diag[19]=0;for(int i=0;i<n;++i){w->diag[20]+=rows[i].body.pn;w->diag[21]+=fabs(rows[i].body.pt);if(!rows[i].body_sign&&rows[i].a>=0&&rows[i].b<0&&rows[i].surface==0){floor_normal[rows[i].a]+=rows[i].body.pn;floor_tangent[rows[i].a]+=rows[i].body.pt;}collection_contact_record(w,b,&rows[i]);}free(rows);
 /* Accepted floor rolling resistance, bounded by the friction budget. Balls
    remain active in this increment; body sleep is coordinated by the owner. */
 for(int i=0;i<30;++i)if(w->items[i].instance&&w->items[i].definition.kind==2){dango_belt_room_collection_item* a=&w->items[i];double r=a->definition.ball.radius,m=a->definition.ball.mass,inertia=.5*m*r*r;
  if(w->touches[i][30]&&a->definition.ball.floor_friction>0&&fabs(a->ball.vx+r*a->ball.omega)<.002){double limit=.02*m*.5*b->cfg.gravity*r*((double).0166666f/4)/(inertia+m*r*r);
   double available=fmax(0,a->definition.ball.floor_friction*floor_normal[i]-fabs(floor_tangent[i]));limit=fmin(limit,available/(m*r));double delta=fmax(-limit,fmin(limit,a->ball.omega));a->ball.omega-=delta;a->ball.vx+=r*delta;a->surface_contact.tangent_impulse-=m*r*delta;}
  if(!collection_ball_valid(a)||a->ball.x-r<b->left||a->ball.x+r>b->right)return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);}
 /* Rolling is contact work too: publish and enforce the same joint KE gate
    after the complete accepted rolling update, rather than hiding its delta. */
 double final_ke=collection_ke(w,b);w->diag[11]+=final_ke-after;
 if(!isfinite(final_ke)||final_ke>before+1e-9*(1+before))return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);
 double body_after=belt_kinetic(b);b->stats[25]=n;b->stats[26]+=body_before;b->stats[27]+=body_after;b->stats[30]=fmax(b->stats[30],residual);b->diag[12]+=body_before;b->diag[13]+=body_after;b->diag[14]+=body_after-body_before;w->diag[22]++;return 1;
}
int dango_belt_room_collection_version(void){return DANGO_BELT_ROOM_COLLECTION_ABI;}
int dango_belt_room_collection_frame_size(void){return sizeof(dango_belt_room_collection_frame);}
const char* dango_belt_room_collection_model(void){return "belt-room-furniture-r3-v1";}
int dango_belt_room_enable_collection(void* handle,uint64_t revision){
 room_world* w=room_lookup(handle);if(!w)return DANGO_BELT_ROOM_HANDLE;if(w->fault)return DANGO_BELT_ROOM_FAULT;if(w->prepared)return DANGO_BELT_ROOM_BUSY;
 if(revision!=w->revision||w->revision==UINT64_MAX)return DANGO_BELT_ROOM_REVISION;if(w->platform_enabled)return DANGO_BELT_ROOM_REJECTED;
 if(w->collection_enabled)return 0;w->collection_enabled=1;w->revision++;return 0;
}
int dango_belt_room_read_collection_frame(void* handle,dango_belt_room_collection_frame* output,int bytes){
 room_world* w=room_lookup(handle);if(!w)return DANGO_BELT_ROOM_HANDLE;if(!output||bytes<(int)sizeof *output)return DANGO_BELT_ROOM_ARGUMENT;
 dango_belt_room_collection_frame frame;memset(&frame,0,sizeof frame);int rc=dango_belt_room_read_frame(handle,&frame.base,sizeof frame.base);if(rc)return rc;
 frame.last_applied_token=w->last_applied_token;frame.enabled=w->collection_enabled;frame.item_count=w->collection_count;int count=0;
 for(int i=0;i<30;++i)if(w->items[i].instance){dango_belt_room_collection_item* a=&frame.items[count++];*a=w->items[i];
  if(a->definition.kind==2){a->ball.scene_revision=w->revision;a->ball.step=w->tick;a->body_contact.scene_revision=a->surface_contact.scene_revision=w->revision;a->body_contact.step=a->surface_contact.step=w->tick;}}
 memcpy(output,&frame,sizeof frame);return 0;
}
static void collection_probe_change(room_world* w,int kind,uint64_t target,uint64_t revision,const dango_belt_room_definition* definition,dango_belt_room_collection_probe* result,int* slot){
 *result=(dango_belt_room_collection_probe){w->generation,w->revision,w->tick,1,1024,0,0};*slot=-1;
 if(w->fault){result->rejection=8;return;}if(!w->collection_enabled){result->rejection=2;return;}if(revision!=w->revision){result->rejection=3;return;}
 if(kind<1||kind>3||(kind==1?target!=0:target==0)){result->rejection=4;return;}
 for(int i=0;i<30;++i)if(kind==1?!w->items[i].instance:w->items[i].instance==target){*slot=i;break;}
 if(kind==1&&w->collection_count>=30){result->rejection=9;return;}if(*slot<0){result->rejection=4;return;}if(kind==3)return;
 if(!collection_definition_valid(definition)||(kind==2&&definition->kind!=w->items[*slot].definition.kind)){result->rejection=1;return;}
 belt_body* b=room_body(w);dango_belt_room_collection_item a={0};a.instance=target?target:w->next_instance+1;a.definition=*definition;
 if(definition->kind==2)a.ball=(dango_ball_state){.id=(int)a.instance,.enabled=1,.x=definition->ball.x,.y=definition->ball.y};
 double left,right,bottom,top;if(definition->kind==1){left=definition->x-definition->width*.5;right=definition->x+definition->width*.5;bottom=definition->y-definition->height*.5;top=definition->y+definition->height*.5;}
 else {left=definition->ball.x-definition->ball.radius;right=definition->ball.x+definition->ball.radius;bottom=definition->ball.y-definition->ball.radius;top=definition->ball.y+definition->ball.radius;}
 if(left<b->left||right>b->right||bottom<b->cfg.ground_height||top>b->cfg.ground_height+6+1e-6){result->rejection=6;return;}
 room_world proof=*w;memset(proof.diag,0,sizeof proof.diag);double theta=0;int ok;
 if(definition->kind==1){proof.platform=collection_platform(&a);proof.platform.width+=2*(ROOM_PENETRATION-1e-6);proof.platform.height+=2*(ROOM_PENETRATION-1e-6);ok=room_certificate(&proof,b->q,b->q,&theta,0);
  if(ok==1)ok=room_containment(&proof,b->q);}
 else {belt_v c={a.ball.x,a.ball.y},n;result->minimum_gap=collection_circle_closest(b->q,c,definition->ball.radius,&theta,&n);ok=collection_circle_certificate(&proof,b->q,b->q,c,c,definition->ball.radius+ROOM_PENETRATION-1e-6,&theta,0);
  if(ok==1)ok=collection_center_outside(&proof,b->q,c);}
 if(ok!=1){result->rejection=proof.diag[2]?8:7;return;}
 for(int i=0;i<30;++i)if(i!=*slot&&w->items[i].instance){double gap=collection_pair_gap(&a,&w->items[i],0);result->minimum_gap=fmin(result->minimum_gap,gap);
  if(gap<-1e-6){result->rejection=7;return;}gap=collection_pair_gap(&a,&w->items[i],1);result->minimum_gap=fmin(result->minimum_gap,gap);if(gap<-1e-6){result->rejection=7;return;}}
}
int dango_belt_room_probe_furniture_change(void* handle,int kind,uint64_t target,uint64_t revision,const dango_belt_room_definition* definition,dango_belt_room_collection_probe* result,int bytes){
 room_world* w=room_lookup(handle);if(!w)return DANGO_BELT_ROOM_HANDLE;if(!result||bytes!=(int)sizeof *result)return DANGO_BELT_ROOM_ARGUMENT;if(w->prepared)return DANGO_BELT_ROOM_BUSY;int slot;collection_probe_change(w,kind,target,revision,definition,result,&slot);return 0;
}
int dango_belt_room_prepare_furniture_change(void* handle,int kind,uint64_t target,uint64_t revision,const dango_belt_room_definition* definition,dango_belt_room_collection_probe* result,int bytes,dango_belt_room_collection_token* token){
 room_world* w=room_lookup(handle);if(!w)return DANGO_BELT_ROOM_HANDLE;if(!result||!token||bytes!=(int)sizeof *result)return DANGO_BELT_ROOM_ARGUMENT;if(w->prepared)return DANGO_BELT_ROOM_BUSY;
 int slot;collection_probe_change(w,kind,target,revision,definition,result,&slot);*token=(dango_belt_room_collection_token){0};if(result->rejection)return 0;
 if(w->revision==UINT64_MAX||w->next_token==UINT64_MAX||(kind==1&&w->next_instance>=INT32_MAX))return DANGO_BELT_ROOM_REVISION;
 w->candidate=(dango_belt_room_collection_item){0};if(kind!=3){w->candidate.instance=kind==1?w->next_instance+1:target;w->candidate.definition=*definition;
  if(definition->kind==2)w->candidate.ball=(dango_ball_state){.id=(int)w->candidate.instance,.enabled=1,.x=definition->ball.x,.y=definition->ball.y};}
 w->prepared_kind=kind;w->prepared_slot=slot;w->token=(dango_belt_room_collection_token){w->generation,++w->next_token,w->revision,w->tick,1};w->prepared=1;*token=w->token;return 0;
}
int dango_belt_room_apply_prepared_furniture_change(void* handle,const dango_belt_room_collection_token* token){
 room_world* w=room_lookup(handle);if(!w)return DANGO_BELT_ROOM_HANDLE;if(!token)return DANGO_BELT_ROOM_ARGUMENT;if(w->fault)return DANGO_BELT_ROOM_FAULT;
 if(!w->prepared||memcmp(token,&w->token,sizeof *token)||token->generation!=w->generation||token->scene_revision!=w->revision||token->tick!=w->tick||token->config_revision!=1)return DANGO_BELT_ROOM_REVISION;
 int slot=w->prepared_slot;w->items[slot]=w->candidate;w->ball_sleep[slot]=0;for(int i=0;i<30;++i){w->pair_touch[i][slot]=w->pair_touch[slot][i]=0;w->touches[i][slot]=0;}memset(w->touches[slot],0,sizeof w->touches[slot]);
 for(int i=0;i<30;++i)if(w->items[i].instance&&w->items[i].definition.kind==2){w->items[i].body_contact=(dango_ball_contact){0};w->items[i].surface_contact=(dango_ball_contact){0};}
 if(w->prepared_kind==1){w->collection_count++;w->next_instance++;}else if(w->prepared_kind==3)w->collection_count--;
 w->last_applied_token=token->token_id;w->prepared=0;w->revision++;belt_body* b=room_body(w);double bounds[4],foot;belt_bounds(b->q,bounds,&foot);b->grounded=bounds[1]<=b->cfg.ground_height+ROOM_ACTIVATION;w->diag[18]=w->diag[19]=0;
 /* Removing/moving a support invalidates its old qualification immediately.
    Rebuild remaining static top supports without advancing the world. */
 for(int i=0;i<30;++i)if(w->items[i].instance&&w->items[i].definition.kind==1){dango_belt_room_platform saved=w->platform;int enabled=w->platform_enabled;w->platform=collection_platform(&w->items[i]);w->platform_enabled=1;room_row rows[512];int n=room_rows(w,b,rows);double top=w->platform.y+w->platform.height*.5;w->platform=saved;w->platform_enabled=enabled;
  for(int j=0;j<n;++j)if(rows[j].platform==1&&rows[j].n.y>=.7&&b->q[35]>top&&b->v[35]<.5){b->grounded=1;w->diag[18]=(double)(uint32_t)w->items[i].instance;w->diag[19]=(double)(uint32_t)(w->items[i].instance>>32);}}
 b->stats[25]=0;belt_read_stats(b);return 0;
}
int dango_belt_room_cancel_prepared_furniture_change(void* handle,const dango_belt_room_collection_token* token){
 room_world* w=room_lookup(handle);if(!w)return DANGO_BELT_ROOM_HANDLE;if(!token)return DANGO_BELT_ROOM_ARGUMENT;if(token->generation!=w->generation)return DANGO_BELT_ROOM_REVISION;
 if(w->prepared){if(memcmp(token,&w->token,sizeof *token))return DANGO_BELT_ROOM_REVISION;w->prepared=0;}return 0;
}
#endif

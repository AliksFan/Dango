#ifndef DANGO_ROUND_BALL_H
#define DANGO_ROUND_BALL_H

#include <math.h>
#include <stdbool.h>
#include <stdint.h>

#ifndef DANGO_ROUND_BALL_TEST_VARIANT
#define DANGO_ROUND_BALL_TEST_VARIANT 0
#endif

/* Internal admission contract. All lengths/ball velocities are in q=T p,
   T=diag(4/3,1), never DIP. The original Dango coordinates remain p. */
typedef struct dango_ball_vec { double x,y; } dango_ball_vec;
typedef struct dango_ball_definition
{
    double x,y,radius,mass,floor_friction,dango_friction,floor_restitution,dango_restitution;
} dango_ball_definition;
typedef struct dango_ball_state
{
    int id,enabled,sleeping;
    uint64_t scene_revision,step,integrations;
    double x,y,vx,vy,angle,omega;
} dango_ball_state;
typedef struct dango_ball_contact
{
    int object_id,touching,sample;
    uint64_t scene_revision,step;
    double fraction,nx,ny,gap,normal_impulse,tangent_impulse;
    double normal_speed_before,tangent_speed_before;
    double ball_speed_before;
} dango_ball_contact;
typedef struct dango_ball_snapshot
{
    uint64_t scene_revision,step;
    dango_ball_state ball;
    dango_ball_contact dango_contact,floor_contact;
} dango_ball_snapshot;
typedef struct dango_ball_geometry
{
    double core[2],nodes[32],node_radius,ball_x,ball_y,ball_radius;
} dango_ball_geometry;
typedef struct dango_ball_geometry_result
{
    int sample,normalized,max_node,max_axis;
    double fraction,x,y,nx,ny,gap;
    double normal_gradient[34],tangent_gradient[34]; /* nodes 0..15, core 16 */
} dango_ball_geometry_result;
typedef struct dango_ball_impulse { double normal,tangent; } dango_ball_impulse;
typedef struct dango_ball_point_input
{
    double nx,ny,dango_mass,ball_mass,radius,inertia;
    double dvx,dvy,bvx,bvy,omega,friction,restitution;
} dango_ball_point_input;
typedef struct dango_ball_point_result
{
    double knn,knt,ktt,vn_before,vt_before,target;
    double jn,jt,dvx,dvy,bvx,bvy,omega,vn_after,vt_after;
} dango_ball_point_result;

/* Solve the full effective-mass matrix, then the Coulomb boundary if sticking
   is infeasible. Positive impulse separates the ball from the Dango/floor. */
static inline bool dango_ball_contact_impulse(double knn,double knt,double ktt,
    double vn,double vt,double target,double friction,dango_ball_impulse* out)
{
    double determinant=knn*ktt-knt*knt;
    if (!out || !isfinite(determinant) || determinant<=0 || knn<=0 || ktt<=0 ||
        !isfinite(vn) || !isfinite(vt) || !isfinite(target) || !isfinite(friction) || friction<0) return false;
    double a=target-vn;
#if DANGO_ROUND_BALL_TEST_VARIANT == 5
    /* Deliberately wrong admission executable: one normal solve followed by
       a tangent solve omits the coupled normal re-solve. */
    out->normal=fmax(0,a/knn);
    double limit=friction*out->normal;
    out->tangent=fmax(-limit,fmin(limit,-(vt+knt*out->normal)/ktt));
    return true;
#else
    double b=-vt;
    double jn=(ktt*a-knt*b)/determinant,jt=(knn*b-knt*a)/determinant;
    if (jn>=0 && fabs(jt)<=friction*jn)
    { out->normal=jn; out->tangent=jt; return true; }
    if (a<=0) { out->normal=0; out->tangent=0; return true; }
    /* Slip sign is the post-normal velocity; its cross term can differ from
       the incoming tangent sign in an anisotropic Dango contact. */
    double free_t=vt+knt*a/knn;
    double sign=free_t<0 ? -1 : 1;
    double denominator=knn-friction*sign*knt;
    if (denominator<=0) return false;
    out->normal=a/denominator; out->tangent=-friction*sign*out->normal;
    return isfinite(out->normal) && isfinite(out->tangent);
#endif
}

static inline double dango_ball_restitution_target(double closing,double restitution,bool first_impact)
{ return first_impact && closing>=.5 ? closing*restitution : 0; }

typedef struct dango_ball_mesh
{
    dango_ball_vec local[16],expanded[16],points[128];
    double scale,maximum,radius;
    int max_node,max_axis;
} dango_ball_mesh;
static inline dango_ball_vec dango_ball_vadd(dango_ball_vec a,dango_ball_vec b)
{ return (dango_ball_vec){a.x+b.x,a.y+b.y}; }
static inline dango_ball_vec dango_ball_vsub(dango_ball_vec a,dango_ball_vec b)
{ return (dango_ball_vec){a.x-b.x,a.y-b.y}; }
static inline dango_ball_vec dango_ball_vscale(double s,dango_ball_vec v)
{ return (dango_ball_vec){s*v.x,s*v.y}; }
static inline double dango_ball_dot(dango_ball_vec a,dango_ball_vec b)
{ return a.x*b.x+a.y*b.y; }

static inline bool dango_ball_mesh_build(const dango_ball_geometry* input,dango_ball_mesh* mesh)
{
    *mesh=(dango_ball_mesh){0}; mesh->radius=input->node_radius;
    for (int n=0;n<16;++n)
    {
        dango_ball_vec p={input->nodes[2*n]-input->core[0],input->nodes[2*n+1]-input->core[1]};
        double length=hypot(p.x,p.y);
        if (!isfinite(length) || length<1e-6) return false;
        mesh->local[n]=p; mesh->expanded[n]=dango_ball_vscale(1+mesh->radius/length,p);
        for (int axis=0;axis<2;++axis)
        {
            double value=fabs(axis ? mesh->expanded[n].y : mesh->expanded[n].x);
            if (value>mesh->maximum) { mesh->maximum=value; mesh->max_node=n; mesh->max_axis=axis; }
        }
    }
    mesh->scale=fmin(1,.98/mesh->maximum);
    for (int s=0;s<128;++s)
    {
        int n=s/8; double t=(double)(s%8)/8;
        dango_ball_vec point=dango_ball_vscale(.5*(1-t)*(1-t),mesh->expanded[(n+15)%16]);
        point=dango_ball_vadd(point,dango_ball_vscale(.5+t-t*t,mesh->expanded[n]));
        point=dango_ball_vadd(point,dango_ball_vscale(.5*t*t,mesh->expanded[(n+1)%16]));
        mesh->points[s]=(dango_ball_vec){(4.0/3)*(input->core[0]+point.x*mesh->scale*.9),
            input->core[1]+point.y*mesh->scale*(58.0/60)};
    }
    for (int s=0;s<128;++s)
    {
        dango_ball_vec edge=dango_ball_vsub(mesh->points[(s+1)%128],mesh->points[s]);
        dango_ball_vec next=dango_ball_vsub(mesh->points[(s+2)%128],mesh->points[(s+1)%128]);
        if (!isfinite(mesh->points[s].x) || !isfinite(mesh->points[s].y) ||
            dango_ball_dot(edge,edge)<1e-12 || edge.x*next.y-edge.y*next.x<-1e-8) return false;
    }
    return true;
}
static inline dango_ball_vec dango_ball_expand_gradient(const dango_ball_mesh* mesh,int n,dango_ball_vec g)
{
    dango_ball_vec p=mesh->local[n]; double l=hypot(p.x,p.y);
    return dango_ball_vsub(dango_ball_vscale(1+mesh->radius/l,g),
        dango_ball_vscale(mesh->radius*dango_ball_dot(p,g)/(l*l*l),p));
}
static inline void dango_ball_sample_gradient(const dango_ball_mesh* mesh,int sample,
    dango_ball_vec normal,dango_ball_vec* gradient)
{
    for (int n=0;n<17;++n) gradient[n]=(dango_ball_vec){0};
    int n=sample/8; double t=(double)(sample%8)/8;
    int nodes[3]={(n+15)%16,n,(n+1)%16};
    double weights[3]={.5*(1-t)*(1-t),.5+t-t*t,.5*t*t};
#if DANGO_ROUND_BALL_TEST_VARIANT != 3
    normal.x*=4.0/3;
#endif
    dango_ball_vec mapped={.9*normal.x,(58.0/60)*normal.y},sum={0};
    for (int j=0;j<3;++j)
    {
        int k=nodes[j]; sum=dango_ball_vadd(sum,dango_ball_vscale(weights[j],mesh->expanded[k]));
        gradient[k]=dango_ball_vadd(gradient[k],dango_ball_expand_gradient(mesh,k,
            dango_ball_vscale(mesh->scale*weights[j],mapped)));
    }
    if (mesh->scale<1)
    {
        int k=mesh->max_node; double component=mesh->max_axis ? mesh->expanded[k].y : mesh->expanded[k].x;
        double derivative=-mesh->scale/mesh->maximum*dango_ball_dot(mapped,sum)*(component<0 ? -1 : 1);
        dango_ball_vec g=mesh->max_axis ? (dango_ball_vec){0,derivative} : (dango_ball_vec){derivative,0};
        gradient[k]=dango_ball_vadd(gradient[k],dango_ball_expand_gradient(mesh,k,g));
    }
    gradient[16]=normal;
    for (int k=0;k<16;++k) gradient[16]=dango_ball_vsub(gradient[16],gradient[k]);
}
static inline bool dango_ball_geometry_contact(const dango_ball_geometry* input,dango_ball_geometry_result* result)
{
    if (!input || !result || !isfinite(input->ball_x) || !isfinite(input->ball_y) ||
        !isfinite(input->ball_radius) || input->ball_radius<=0) return false;
    dango_ball_mesh mesh; if (!dango_ball_mesh_build(input,&mesh)) return false;
    *result=(dango_ball_geometry_result){0};
    dango_ball_vec center={input->ball_x,input->ball_y},closest={0};
    double best=INFINITY; bool inside=true;
    for (int s=0;s<128;++s)
    {
        dango_ball_vec a=mesh.points[s],edge=dango_ball_vsub(mesh.points[(s+1)%128],a);
        dango_ball_vec relative=dango_ball_vsub(center,a);
        if (edge.x*relative.y-edge.y*relative.x<0) inside=false;
        double fraction=fmax(0,fmin(1,dango_ball_dot(relative,edge)/dango_ball_dot(edge,edge)));
        dango_ball_vec point=dango_ball_vadd(a,dango_ball_vscale(fraction,edge));
        dango_ball_vec distance=dango_ball_vsub(center,point);
        double squared=dango_ball_dot(distance,distance);
        if (squared<best) { best=squared; closest=point; result->sample=s; result->fraction=fraction; }
    }
    double distance=sqrt(best);
    dango_ball_vec normal;
    if (distance>1e-10) normal=dango_ball_vscale((inside ? -1 : 1)/distance,dango_ball_vsub(center,closest));
    else
    {
        dango_ball_vec edge=dango_ball_vsub(mesh.points[(result->sample+1)%128],mesh.points[result->sample]);
        double length=hypot(edge.x,edge.y); normal=(dango_ball_vec){edge.y/length,-edge.x/length};
    }
    result->x=closest.x; result->y=closest.y; result->nx=normal.x; result->ny=normal.y;
    result->gap=(inside ? -distance : distance)-input->ball_radius;
    result->normalized=mesh.scale<1; result->max_node=mesh.max_node; result->max_axis=mesh.max_axis;
    for (int axis=0;axis<2;++axis)
    {
        dango_ball_vec direction=axis ? (dango_ball_vec){-normal.y,normal.x} : normal;
        dango_ball_vec a[17],b[17];
        dango_ball_sample_gradient(&mesh,result->sample,direction,a);
        dango_ball_sample_gradient(&mesh,(result->sample+1)%128,direction,b);
        double* output=axis ? result->tangent_gradient : result->normal_gradient;
        for (int n=0;n<17;++n)
        {
            output[2*n]=(1-result->fraction)*a[n].x+result->fraction*b[n].x;
            output[2*n+1]=(1-result->fraction)*a[n].y+result->fraction*b[n].y;
        }
    }
    return isfinite(result->gap);
}

#ifdef DANGO_ROUND_BALL_CONTRACT_TESTS
/* These are linked only into the isolated executable, never the module ABI. */
int dango_round_ball_test_point(const dango_ball_point_input*,dango_ball_point_result*);
int dango_round_ball_test_floor(const dango_ball_point_input*,dango_ball_point_result*);
int dango_round_ball_test_geometry(const dango_ball_geometry*,dango_ball_geometry_result*);
int dango_round_ball_test_world_geometry(void* handle,int entity_id,double ball_x,double ball_y,
    double radius,dango_ball_geometry*,dango_ball_geometry_result*);
int dango_round_ball_test_add(void*,const dango_ball_definition*,int*);
int dango_round_ball_test_remove(void*,int);
int dango_round_ball_test_read(void*,dango_ball_snapshot*);
int dango_round_ball_test_dango_frame(void*,int entity_id,float* values,int capacity);
/* Initial fixture velocities are only legal before the world's first Step. */
int dango_round_ball_test_initial_velocity(void*,double vx,double vy,double omega);
/* Mutates real state after exactly four successful substeps on the next Step:
   mode 1 ball NaN, mode 2 collapse Dango geometry (also works after removal). */
int dango_round_ball_test_fault(void*,int mode);
int dango_round_ball_test_owner_count(void);
#endif

#endif

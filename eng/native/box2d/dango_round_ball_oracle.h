#ifndef DANGO_ROUND_BALL_ORACLE_H
#define DANGO_ROUND_BALL_ORACLE_H

/* Test-only independent double oracle. It never calls the native contact helpers. */
#include <math.h>
#include <stdbool.h>
#include <stddef.h>

typedef struct rb_oracle_vec { double x, y; } rb_oracle_vec;
typedef struct rb_oracle_contact
{
    rb_oracle_vec dango_velocity, ball_velocity;
    double omega, normal_impulse, tangent_impulse;
    double normal_speed, tangent_speed, energy_before, energy_after;
    double knn, knt, ktt;
    bool sticking;
} rb_oracle_contact;

static rb_oracle_vec rb_o_add(rb_oracle_vec a, rb_oracle_vec b)
{ return (rb_oracle_vec){a.x+b.x,a.y+b.y}; }
static rb_oracle_vec rb_o_sub(rb_oracle_vec a, rb_oracle_vec b)
{ return (rb_oracle_vec){a.x-b.x,a.y-b.y}; }
static rb_oracle_vec rb_o_scale(double s, rb_oracle_vec a)
{ return (rb_oracle_vec){s*a.x,s*a.y}; }
static double rb_o_dot(rb_oracle_vec a, rb_oracle_vec b)
{ return a.x*b.x+a.y*b.y; }
static double rb_o_length(rb_oracle_vec a)
{ return sqrt(rb_o_dot(a,a)); }
static rb_oracle_vec rb_o_map(rb_oracle_vec p)
{ return (rb_oracle_vec){p.x*(4.0/3.0),p.y}; }
static double rb_o_energy(rb_oracle_vec vd, rb_oracle_vec vb, double omega, bool floor)
{ return (floor ? 0 : .5*.36*rb_o_dot(vd,vd))+.5*.09*rb_o_dot(vb,vb)+.5*.0072*omega*omega; }

static bool rb_o_contact(rb_oracle_vec vd, rb_oracle_vec vb, double omega,
    rb_oracle_vec n, double friction, double restitution, bool floor,
    rb_oracle_contact* result)
{
    rb_oracle_vec t={-n.y,n.x}, mapped_n=rb_o_map(n), mapped_t=rb_o_map(t);
    rb_oracle_vec relative=rb_o_sub(vb,rb_o_map(vd));
    double vn=rb_o_dot(n,relative), vt=rb_o_dot(t,relative)-.4*omega;
    double knn=1/.09+(floor ? 0 : rb_o_dot(mapped_n,mapped_n)/.36);
    double knt=floor ? 0 : rb_o_dot(mapped_n,mapped_t)/.36;
    double ktt=1/.09+.4*.4/.0072+(floor ? 0 : rb_o_dot(mapped_t,mapped_t)/.36);
    double jn=0,jt=0; bool sticking=false;
    if (vn<0)
    {
        double normal_target=-(1+restitution)*vn;
        double determinant=knn*ktt-knt*knt;
        jn=(normal_target*ktt+knt*vt)/determinant;
        jt=(-knn*vt-knt*normal_target)/determinant;
        sticking=jn>=0 && fabs(jt)<=friction*jn;
        if (!sticking)
        {
            bool found=false;
            for (int sign=-1;sign<=1;sign+=2)
            {
                double candidate_n=normal_target/(knn-sign*friction*knt);
                double candidate_t=-sign*friction*candidate_n;
                double candidate_speed=vt+knt*candidate_n+ktt*candidate_t;
                if (candidate_n>=0 && sign*candidate_speed>=-1e-12)
                { jn=candidate_n; jt=candidate_t; found=true; break; }
            }
            if (!found) return false;
        }
    }
    result->knn=knn; result->knt=knt; result->ktt=ktt;
    result->normal_impulse=jn; result->tangent_impulse=jt; result->sticking=sticking;
    result->energy_before=rb_o_energy(vd,vb,omega,floor);
    result->dango_velocity=floor ? vd : rb_o_sub(vd,
        rb_o_scale(1/.36,rb_o_add(rb_o_scale(jn,mapped_n),rb_o_scale(jt,mapped_t))));
    result->ball_velocity=rb_o_add(vb,
        rb_o_scale(1/.09,rb_o_add(rb_o_scale(jn,n),rb_o_scale(jt,t))));
    result->omega=omega-.4*jt/.0072;
    result->normal_speed=vn+knn*jn+knt*jt;
    result->tangent_speed=vt+knt*jn+ktt*jt;
    result->energy_after=rb_o_energy(result->dango_velocity,result->ball_velocity,result->omega,floor);
    return true;
}

typedef struct rb_oracle_mesh
{
    rb_oracle_vec core, expanded[16];
    double scale, maximum, runner_up;
} rb_oracle_mesh;

static bool rb_o_mesh(const float positions[34], rb_oracle_mesh* mesh)
{
    mesh->core=(rb_oracle_vec){positions[32],positions[33]};
    mesh->maximum=0; mesh->runner_up=0;
    for (int i=0;i<16;++i)
    {
        rb_oracle_vec local=rb_o_sub((rb_oracle_vec){positions[2*i],positions[2*i+1]},mesh->core);
        double length=rb_o_length(local); if (length<1e-6) return false;
        mesh->expanded[i]=rb_o_scale(1+.045/length,local);
        double components[2]={fabs(mesh->expanded[i].x),fabs(mesh->expanded[i].y)};
        for (int axis=0;axis<2;++axis)
            if (components[axis]>mesh->maximum)
            { mesh->runner_up=mesh->maximum; mesh->maximum=components[axis]; }
            else if (components[axis]>mesh->runner_up) mesh->runner_up=components[axis];
    }
    mesh->scale=fmin(1,.98/mesh->maximum);
    return true;
}

static rb_oracle_vec rb_o_curve_point(const rb_oracle_mesh* mesh, int node, double u)
{
    double weights[3]={.5*(1-u)*(1-u),.5+u-u*u,.5*u*u};
    int nodes[3]={(node+15)%16,node,(node+1)%16};
    rb_oracle_vec local={0,0};
    for (int i=0;i<3;++i) local=rb_o_add(local,rb_o_scale(weights[i],mesh->expanded[nodes[i]]));
    return rb_o_map(rb_o_add(mesh->core,
        (rb_oracle_vec){local.x*mesh->scale*.9,local.y*mesh->scale*(58.0/60.0)}));
}

typedef struct rb_oracle_geometry
{
    double gap, parameter;
    int segment;
    rb_oracle_vec nearest, normal;
} rb_oracle_geometry;

/* Independent dense quadratic-curve oracle, with its own closest segment scan. */
static bool rb_o_geometry(const float positions[34], rb_oracle_vec center,
    double radius, int samples_per_node, rb_oracle_geometry* result)
{
    rb_oracle_mesh mesh; if (!rb_o_mesh(positions,&mesh)) return false;
    double best=HUGE_VAL;
    for (int node=0;node<16;++node) for (int sample=0;sample<samples_per_node;++sample)
    {
        rb_oracle_vec a=rb_o_curve_point(&mesh,node,(double)sample/samples_per_node);
        rb_oracle_vec b=rb_o_curve_point(&mesh,node,(double)(sample+1)/samples_per_node);
        rb_oracle_vec edge=rb_o_sub(b,a), from_a=rb_o_sub(center,a);
        double length_squared=rb_o_dot(edge,edge);
        if (length_squared<=0) return false;
        double parameter=fmax(0,fmin(1,rb_o_dot(from_a,edge)/length_squared));
        rb_oracle_vec nearest=rb_o_add(a,rb_o_scale(parameter,edge));
        rb_oracle_vec separation=rb_o_sub(center,nearest);
        double distance_squared=rb_o_dot(separation,separation);
        if (distance_squared<best)
        {
            best=distance_squared; result->nearest=nearest;
            result->segment=node*samples_per_node+sample; result->parameter=parameter;
            double distance=sqrt(distance_squared);
            if (distance<=0) return false;
            result->normal=rb_o_scale(1/distance,separation); result->gap=distance-radius;
        }
    }
    return isfinite(best);
}

#endif

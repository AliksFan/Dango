// Test-only ground-contact candidate. Never included by the production DLL.
#ifndef DANGO_GROUND_CONTACT_EVALUATION_H
#define DANGO_GROUND_CONTACT_EVALUATION_H
#include "dango_self_constraints.h"

static float ground_friction_project(float* position, float reference, float inverse_mass,
    float normal_impulse, float friction, float dt, float* lambda)
{
    if (inverse_mass<=0 || normal_impulse<=0 || friction<=0 || dt<=0) return 0;
    float limit=friction*normal_impulse*dt;
    float next=fmaxf(-limit,fminf(limit,*lambda-(*position-reference)/inverse_mass));
    float change=(next-*lambda)*inverse_mass;
    *position+=change;
    *lambda=next;
    return fabsf(change);
}

static float ground_friction_velocity(float velocity, float inverse_mass, float normal_impulse, float friction)
{
    if (inverse_mass<=0 || normal_impulse<=0 || friction<=0) return velocity;
    float change=friction*normal_impulse*inverse_mass;
    return velocity>0 ? fmaxf(0,velocity-change) : fminf(0,velocity+change);
}

// Accumulate the friction impulse, so repeated sweeps cannot spend the cone twice.
static float ground_friction_velocity_project(float* velocity, float inverse_mass,
    float normal_impulse, float friction, float* impulse)
{
    if (inverse_mass<=0) return 0;
    float limit=friction*fmaxf(0,normal_impulse);
    float next=fmaxf(-limit,fminf(limit,*impulse-*velocity/inverse_mass));
    float change=(next-*impulse)*inverse_mass;
    *velocity+=change;
    *impulse=next;
    return fabsf(change);
}

// Same geometric constraints as the baseline. Count only the actual upward
// momentum supplied by the plane, not a guessed weight or proximity force.
static float ground_geometry_sweep(dango_constraint_state* s, float* normal_impulse, bool reverse)
{
    float residual=0;
    for (int ordinal=0;ordinal<16;++ordinal)
    {
        int i=reverse ? 15-ordinal : ordinal;
        float length=b2Distance(s->positions[i],s->positions[16]);
        if (!isfinite(length) || length<.00001f) return INFINITY;
        residual=fmaxf(residual,dango_constraint_distance(s,16,i,s->radial_length*s->radial_min,s->radial_length*s->radial_max,true));
        residual=fmaxf(residual,dango_constraint_distance(s,i,(i+1)%16,s->perimeter_length*s->perimeter_min,s->perimeter_length*s->perimeter_max,true));
        residual=fmaxf(residual,dango_constraint_angle(s,i,false,true));
        residual=fmaxf(residual,dango_constraint_angle(s,i,true,true));
    }
    for (int i=0;i<16;++i)
    {
        float penetration=fmaxf(0,s->ground+s->radius-s->positions[i].y);
        residual=fmaxf(residual,penetration/s->radial_length);
        if (penetration>0)
        {
            s->positions[i].y+=penetration;
            if (s->velocities[i].y<0)
            {
                normal_impulse[i]-=s->velocities[i].y/s->inverse_mass[i];
                s->velocities[i].y=0;
            }
        }
    }
    return residual;
}

static void ground_angle_velocity(dango_constraint_state* s, int index, bool display)
{
    int ids[4]={(index+15)%16,index,(index+1)%16,16};
    b2Vec2 p[3];
    for (int i=0;i<3;++i) p[i]=display ? dango_constraint_display_point(s,ids[i]) : s->positions[ids[i]];
    b2Vec2 e=b2Sub(p[1],p[0]),f=b2Sub(p[2],p[1]);
    float e2=b2LengthSquared(e),f2=b2LengthSquared(f);
    if (e2<1e-10f || f2<1e-10f) return;
    float turn=atan2f(b2Cross(e,f),b2Dot(e,f));
    bool lower=turn<=.002f+4*DANGO_CONSTRAINT_TOLERANCE;
    bool upper=turn>=1.5687963268f-4*DANGO_CONSTRAINT_TOLERANCE;
    if (!lower && !upper) return;
    b2Vec2 gradients[4]={{-e.y/e2,e.x/e2},{0,0},{-f.y/f2,f.x/f2},{0,0}};
    gradients[1]=b2Neg(b2Add(gradients[0],gradients[2]));
    if (display) for (int i=0;i<3;++i)
    {
        b2Vec2 radial=b2Sub(s->positions[ids[i]],s->positions[16]);
        float length=b2Length(radial);
        b2Vec2 unit=b2MulSV(1/length,radial);
        b2Vec2 extra=b2MulSV(s->radius/length,b2MulSub(gradients[i],b2Dot(unit,gradients[i]),unit));
        gradients[i]=b2Add(gradients[i],extra); gradients[3]=b2Sub(gradients[3],extra);
    }
    // Zero positional error: only remove velocity closing an active constraint.
    dango_constraint_correct(s,ids,gradients,display ? 4 : 3,0,lower,true);
}

static void ground_distance_velocity(dango_constraint_state* s,int a,int b,float minimum,float maximum)
{
    b2Vec2 d=b2Sub(s->positions[b],s->positions[a]);
    float length=b2Length(d),margin=4*DANGO_CONSTRAINT_TOLERANCE*s->radial_length;
    bool lower=length<=minimum+margin,upper=length>=maximum-margin;
    if (!lower && !upper) return;
    b2Vec2 unit=b2MulSV(1/length,d),gradients[2]={b2Neg(unit),unit};
    int ids[2]={a,b};
    dango_constraint_correct(s,ids,gradients,2,0,lower,true);
}

static bool ground_velocity_solve(dango_constraint_state* s,float* normal_impulses,float friction,float* output_tangent,const float* initial_tangent)
{
    float tangent[16]={0};
    if (initial_tangent) for (int i=0;i<16;++i) tangent[i]=initial_tangent[i];
    for (int iteration=0;iteration<96;++iteration)
    {
        b2Vec2 before[17];
        for (int i=0;i<17;++i) before[i]=s->velocities[i];
        for (int ordinal=0;ordinal<16;++ordinal)
        {
            int i=iteration%2 ? 15-ordinal : ordinal;
            ground_distance_velocity(s,16,i,s->radial_length*s->radial_min,s->radial_length*s->radial_max);
            ground_distance_velocity(s,i,(i+1)%16,s->perimeter_length*s->perimeter_min,s->perimeter_length*s->perimeter_max);
            ground_angle_velocity(s,i,false); ground_angle_velocity(s,i,true);
        }
        for (int i=0;i<16;++i) if (s->positions[i].y-s->radius-s->ground<=.005f && normal_impulses[i]>0)
        {
            if (s->velocities[i].y<0)
            {
                normal_impulses[i]-=s->velocities[i].y/s->inverse_mass[i]; s->velocities[i].y=0;
            }
            ground_friction_velocity_project(&s->velocities[i].x,s->inverse_mass[i],normal_impulses[i],friction,&tangent[i]);
        }
        float change=0;
        for (int i=0;i<17;++i) change=fmaxf(change,b2Distance(before[i],s->velocities[i]));
        if (change<=.00001f)
        {
            if (output_tangent) for (int i=0;i<16;++i) output_tangent[i]=tangent[i];
            return true;
        }
    }
    return false;
}
#endif

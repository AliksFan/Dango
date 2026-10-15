#ifndef DANGO_PHYSICS_LAB_H
#define DANGO_PHYSICS_LAB_H
#include "dango_self_physics_internal.h"
#include <math.h>
#include <string.h>
typedef struct dango_lab_state { float ramp, stretch, active, cancel; int mode; bool jump_accepted; float bias, stats[16]; } dango_lab_state;
/* Lab-only: all inputs have been atomically validated by the public entry. */
static int dango_lab_forces(dango_self_soft_body* body,const dango_self_physics_config* cfg,
    dango_lab_state* s,float dt,const float* c)
{
    b2BodyId ids[17]; b2Vec2 pos[17]; double mass[17],total=0,cx=0,cy=0,radius=0;
    ids[0]=body->core_body_id;
    for(int i=0;i<=body->node_count;++i) {
        if(i) ids[i]=body->node_body_ids[i-1];
        if(!b2Body_IsValid(ids[i])) return -497;
        pos[i]=b2Body_GetWorldCenterOfMass(ids[i]); mass[i]=b2Body_GetMass(ids[i]);
        if(!isfinite(mass[i]) || mass[i]<=0 || !isfinite(pos[i].x) || !isfinite(pos[i].y)) return -497;
        total+=mass[i]; cx+=mass[i]*pos[i].x; cy+=mass[i]*pos[i].y;
    }
    cx/=total; cy/=total;
    for(int i=0;i<=body->node_count;++i) radius=fmax(radius,hypot(pos[i].x-cx,pos[i].y-cy));
    if(!isfinite(radius) || radius<=0) return -497;
    if(s->cancel) s->ramp=0;
    else if(!s->jump_accepted) {
        float delta=s->stretch-s->ramp;
        float step=dt/(delta>0 ? .15f : .20f);
        s->ramp+=fmaxf(-step,fminf(step,delta));
    }
    memset(s->stats,0,sizeof s->stats);
    float* out=s->stats;
    out[0]=(float)s->mode; out[1]=s->bias; out[2]=s->ramp; out[8]=(float)cy;
    out[7]=(float)cy; out[13]=cfg->downward_force; out[14]=s->active; out[15]=s->stretch;
    b2Vec2 velocity=b2Body_GetLinearVelocity(body->core_body_id);
    float force=0;
    if((c[0]<0 && velocity.x > -cfg->maximum_horizontal_speed) || (c[0]>0 && velocity.x < cfg->maximum_horizontal_speed))
        force=c[0]*cfg->horizontal_force*(body->grounded ? 1.0f : cfg->air_control_ratio);
    out[3]=force;
    if(s->mode==0 || (s->mode==3 && s->bias==0)) {
        out[4]=force; out[6]=(float)(-(pos[0].y-cy)*force); out[7]=pos[0].y;
    } else if(s->mode==3) {
        double minY=pos[1].y,maxY=pos[1].y;
        for(int i=2;i<=body->node_count;++i) { minY=fmin(minY,pos[i].y); maxY=fmax(maxY,pos[i].y); }
        double target=pos[0].y+s->bias*(s->bias>=0 ? maxY-pos[0].y : pos[0].y-minY);
        int lower=-1,upper=-1;
        /* Core first, then stable node order. Strict comparisons preserve ties. */
        for(int i=0;i<=body->node_count;++i) {
            if(pos[i].y<=target && (lower<0 || pos[i].y>pos[lower].y)) lower=i;
            if(pos[i].y>=target && (upper<0 || pos[i].y<pos[upper].y)) upper=i;
        }
        if(lower<0 || upper<0) return -497;
        out[7]=(float)target;
        if(force!=0) {
            float lowForce=force,highForce=0;
            if(pos[upper].y!=pos[lower].y) {
                lowForce=(float)(force*(pos[upper].y-target)/(pos[upper].y-pos[lower].y));
                highForce=force-lowForce;
            }
            double actual=0,momentY=0,torque=0;
            if(lowForce!=0) {
                b2Body_ApplyForceToCenter(ids[lower],(b2Vec2){lowForce,0},true);
                actual+=lowForce; momentY+=pos[lower].y*lowForce; torque-=(pos[lower].y-cy)*lowForce;
            }
            if(highForce!=0) {
                b2Body_ApplyForceToCenter(ids[upper],(b2Vec2){highForce,0},true);
                actual+=highForce; momentY+=pos[upper].y*highForce; torque-=(pos[upper].y-cy)*highForce;
            }
            out[4]=(float)actual; out[6]=(float)torque; out[7]=(float)(momentY/actual);
        }
    } else if(force!=0) {
        double weights[17],sum=0,fy=0;
        for(int i=0;i<=body->node_count;++i) {
            weights[i]=mass[i]*(1+(s->mode==2 ? s->bias*(pos[i].y-cy)/radius : 0));
            weights[i]=fmax(0,weights[i]); sum+=weights[i];
        }
        if(!isfinite(sum) || sum<=0) return -497;
        double actual=0,torque=0;
        for(int i=0;i<=body->node_count;++i) {
            float f=(float)(force*weights[i]/sum);
            b2Body_ApplyForceToCenter(ids[i],(b2Vec2){f,0},true);
            actual+=f; torque-=(pos[i].y-cy)*f; fy+=pos[i].y*f;
        }
        out[4]=(float)actual; out[6]=(float)torque; out[7]=(float)(fy/actual);
    }
    if(s->ramp>0 && !s->jump_accepted && !s->cancel) {
        b2Vec2 forces[16]; double used=0,nodeMass=0;
        for(int i=1;i<=body->node_count;++i) nodeMass+=mass[i];
        for(int i=1;i<=body->node_count;++i) {
            double x=pos[i].x-pos[0].x,y=pos[i].y-pos[0].y,len=hypot(x,y);
            if(!isfinite(len) || len<=1e-9) return -497;
            double weight=2*(y/len)*(y/len)-1;
            double f=cfg->downward_force*s->ramp*(mass[i]/nodeMass)*weight;
            forces[i-1]=(b2Vec2){(float)(f*x/len),(float)(f*y/len)};
            used+=hypot(forces[i-1].x,forces[i-1].y);
        }
        double scale=used>cfg->downward_force ? cfg->downward_force/used : 1;
        for(int i=1;i<=body->node_count;++i) {
            b2Vec2 f={(float)(forces[i-1].x*scale),(float)(forces[i-1].y*scale)};
            b2Body_ApplyForceToCenter(ids[i],f,true);
            b2Body_ApplyForceToCenter(ids[0],(b2Vec2){-f.x,-f.y},true);
            out[9]+=sqrtf(f.x*f.x+f.y*f.y);
            out[12]+=(pos[i].x-pos[0].x)*f.y-(pos[i].y-pos[0].y)*f.x;
        }
    }
    return 0;
}
#endif

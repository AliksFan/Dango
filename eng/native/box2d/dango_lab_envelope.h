#ifndef DANGO_LAB_ENVELOPE_H
#define DANGO_LAB_ENVELOPE_H
/* Isolated flat-floor candidate. Analytic quadratic B-spline extrema; no polygon or samples.
   Tangential velocity is the selected material point's fixed-t virtual velocity. */
typedef struct dango_envelope_point { b2Vec2 point, normal[17], tangent[17]; int segment; float t, maximum_y; } dango_envelope_point;
static bool dango_envelope_geometry(const b2Vec2* p,float radius,dango_envelope_point* out)
{
    b2Vec2 q[16],u[16]; float len[16];
    for(int i=0;i<16;++i) {
        b2Vec2 d=b2Sub(p[i],p[16]); len[i]=b2Length(d);
        if(!isfinite(len[i]) || len[i]<1e-8f) return false;
        u[i]=b2MulSV(1/len[i],d); q[i]=b2Add(p[i],b2MulSV(radius,u[i]));
    }
    *out=(dango_envelope_point){0}; out->point.y=INFINITY; out->maximum_y=-INFINITY;
    for(int i=0;i<16;++i) {
        b2Vec2 a=b2MulSV(.5f,b2Add(b2Sub(q[(i+15)%16],b2MulSV(2,q[i])),q[(i+1)%16]));
        b2Vec2 b=b2Sub(q[i],q[(i+15)%16]); b2Vec2 c=b2MulSV(.5f,b2Add(q[(i+15)%16],q[i]));
        float ts[3]={0,1,-1}; if(fabsf(a.y)>1e-12f) ts[2]=-b.y/(2*a.y);
        for(int k=0;k<3;++k) if(ts[k]>=0 && ts[k]<=1) {
            float t=ts[k]; b2Vec2 pt=b2Add(c,b2MulSV(t,b2Add(b,b2MulSV(t,a))));
            out->maximum_y=fmaxf(out->maximum_y,pt.y);
            if(pt.y<out->point.y) { out->point=pt; out->segment=i; out->t=t; }
        }
    }
    float t=out->t; float weights[3]={.5f*(1-t)*(1-t),.5f+t-t*t,.5f*t*t};
    for(int k=0;k<3;++k) {
        int i=(out->segment+15+k)%16; float f=radius/len[i],w=weights[k];
        /* Dq/Dp=I+r/L*(I-u*uT), Dq/Dcore=-r/L*(I-u*uT). */
        b2Vec2 gn={-w*f*u[i].x*u[i].y,w*(1+f*(1-u[i].y*u[i].y))};
        b2Vec2 gt={w*(1+f*(1-u[i].x*u[i].x)),-w*f*u[i].x*u[i].y};
        out->normal[i]=b2Add(out->normal[i],gn); out->tangent[i]=b2Add(out->tangent[i],gt);
        out->normal[16]=b2Add(out->normal[16],b2Sub((b2Vec2){0,w},gn));
        out->tangent[16]=b2Add(out->tangent[16],b2Sub((b2Vec2){w,0},gt));
    }
    return isfinite(out->point.x) && isfinite(out->point.y);
}
static float dango_envelope_energy(const dango_constraint_state* s)
{
    double e=0; for(int i=0;i<17;++i) e+=.5*b2LengthSquared(s->velocities[i])/s->inverse_mass[i]; return (float)e;
}
static void dango_envelope_impulse(dango_constraint_state* s,const b2Vec2* g,float j,b2Vec2 point,bool tangent)
{
    float* stats=s->envelope_stats; b2Vec2 momentum={0}; float torque=0;
    for(int i=0;i<17;++i) {
        b2Vec2 impulse=b2MulSV(j,g[i]); momentum=b2Add(momentum,impulse); torque+=b2Cross(s->positions[i],impulse);
        s->velocities[i]=b2Add(s->velocities[i],b2MulSV(s->inverse_mass[i],impulse));
    }
    if(stats) { stats[15]+=momentum.x-(tangent ? j : 0); stats[16]+=momentum.y-(tangent ? 0 : j);
        stats[17]+=torque-(tangent ? -point.y*j : point.x*j); }
}
static float dango_envelope_contact(dango_constraint_state* s,bool correct,dango_constraint_correction_mode mode)
{
    dango_envelope_point p; if(!dango_envelope_geometry(s->positions,s->radius,&p)) return INFINITY;
    float gap=p.point.y-s->ground,penetration=fmaxf(0,-gap),tol=DANGO_CONSTRAINT_TOLERANCE*s->radial_length;
    float* out=s->envelope_stats;
    if(out) { out[0]=p.point.x;out[1]=p.point.y;out[2]=gap;out[5]=s->envelope_friction;out[9]=gap<=.035f ? 1.0f : 0.0f;out[10]=gap<=tol ? 1.0f : 0.0f;out[19]=fminf(out[19],gap);out[25]=(float)p.segment;out[26]=p.t;out[28]=p.maximum_y;out[31]=1; }
    if(!correct || gap>tol) return penetration/s->radial_length;
    float kn=0,kt=0,vn=0,vt=0;
    for(int i=0;i<17;++i) { kn+=s->inverse_mass[i]*b2LengthSquared(p.normal[i]); kt+=s->inverse_mass[i]*b2LengthSquared(p.tangent[i]); vn+=b2Dot(p.normal[i],s->velocities[i]); }
    if(kn<=0 || kt<=0 || !isfinite(kn+kt+vn)) return INFINITY;
    if(mode!=DANGO_CONSTRAINT_CORRECT_POSITION) {
        float j=fmaxf(0,-vn/kn),before=dango_envelope_energy(s);
        if(j>0) { dango_envelope_impulse(s,p.normal,j,p.point,false);s->envelope_normal+=j; }
        if(out) {out[3]+=j;out[11]+=before;out[12]+=dango_envelope_energy(s);out[21]=vn;out[22]=vn+j*kn;}
        for(int i=0;i<17;++i)vt+=b2Dot(p.tangent[i],s->velocities[i]);
        /* Spend one cumulative Coulomb budget per solve, never a fresh budget per sweep. */
        float budget=fmaxf(0,s->envelope_friction*s->envelope_normal-s->envelope_tangent_used);
        float jt=b2ClampFloat(-vt/kt,-budget,budget);before=dango_envelope_energy(s);
        if(jt!=0) {dango_envelope_impulse(s,p.tangent,jt,p.point,true);s->envelope_tangent_used+=fabsf(jt);}
        if(out) {out[4]+=fabsf(jt);out[13]+=before;out[14]+=dango_envelope_energy(s);out[23]=vt;out[24]=vt+jt*kt;}
    }
    if(mode!=DANGO_CONSTRAINT_CORRECT_VELOCITY && penetration>0) {
        float lambda=penetration/kn,norm=0;
        for(int i=0;i<17;++i) { b2Vec2 d=b2MulSV(s->inverse_mass[i]*lambda,p.normal[i]); s->positions[i]=b2Add(s->positions[i],d);float length=b2Length(d);norm+=length*length;if(out)out[7]=fmaxf(out[7],length); }
        if(out)out[6]+=sqrtf(norm);
    }
    return penetration/s->radial_length;
}
#endif

#ifndef DANGO_ARC_MATH_H
#define DANGO_ARC_MATH_H
#include <math.h>
#include <stdbool.h>
#include <string.h>
/* Sixteen periodic quadratic B-spline spans. The same radially expanded
   controls are consumed by the renderer, contact, and material energy. */
typedef struct dango_arc_v { double x,y; } dango_arc_v;
static inline dango_arc_v arc_add(dango_arc_v a,dango_arc_v b) { return (dango_arc_v){a.x+b.x,a.y+b.y}; }
static inline dango_arc_v arc_sub(dango_arc_v a,dango_arc_v b) { return (dango_arc_v){a.x-b.x,a.y-b.y}; }
static inline dango_arc_v arc_mul(double s,dango_arc_v a) { return (dango_arc_v){s*a.x,s*a.y}; }
static inline double arc_dot(dango_arc_v a,dango_arc_v b) { return a.x*b.x+a.y*b.y; }
static inline double arc_cross(dango_arc_v a,dango_arc_v b) { return a.x*b.y-a.y*b.x; }
static inline bool arc_controls(const dango_arc_v* p,double radius,dango_arc_v* q,dango_arc_v* u,double* lengths)
{
    for(int i=0;i<16;++i) {
        dango_arc_v d=arc_sub(p[i],p[16]); double l=hypot(d.x,d.y);
        if(!isfinite(l) || l<1e-8)return false;
        lengths[i]=l;u[i]=arc_mul(1/l,d);q[i]=arc_add(p[i],arc_mul(radius,u[i]));
    }
    return true;
}
static inline void arc_basis(double t,double* b,double* d,double* dd)
{
    b[0]=.5*(1-t)*(1-t);b[1]=.5+t-t*t;b[2]=.5*t*t;
    d[0]=t-1;d[1]=1-2*t;d[2]=t;
    dd[0]=1;dd[1]=-2;dd[2]=1;
}
static inline void arc_pull(const dango_arc_v* u,const double* l,double radius,const dango_arc_v* fq,dango_arc_v* f)
{
    memset(f,0,17*sizeof *f);
    for(int i=0;i<16;++i) {
        dango_arc_v extra=arc_mul(radius/l[i],arc_sub(fq[i],arc_mul(arc_dot(u[i],fq[i]),u[i])));
        f[i]=arc_add(fq[i],extra);f[16]=arc_sub(f[16],extra);
    }
}
/* Conservative forces are the analytic negative gradient of the quadrature
   energy. Rayleigh damping uses the identical strain/curvature Jacobians.
   Rest metric and curvature are evaluated on the natural regular ring, not
   refitted to the current deformation. No force clipping hides this gradient. */
static inline bool dango_arc_material(const dango_arc_v* p,const dango_arc_v* velocity,
    double radius,double rest_radius,double axial,double bending,double axial_damping,double bending_damping,
    dango_arc_v* force,double* stats)
{
    static const double ts[3]={.1127016653792583115,.5,.8872983346207416885};
    static const double ws[3]={5.0/18,4.0/9,5.0/18};
    dango_arc_v q[16],u[16],fq[16]={{0}},qv[16],natural[16];double lengths[16];
    memset(stats,0,7*sizeof *stats);stats[6]=INFINITY;
    if(!arc_controls(p,radius,q,u,lengths))return false;
    for(int i=0;i<16;++i) {
        double a=6.2831853071795864769*i/16;
        natural[i]=(dango_arc_v){(rest_radius+radius)*cos(a),(rest_radius+radius)*sin(a)};
        dango_arc_v v=arc_sub(velocity[i],velocity[16]);
        qv[i]=arc_add(velocity[i],arc_mul(radius/lengths[i],arc_sub(v,arc_mul(arc_dot(v,u[i]),u[i]))));
    }
    for(int i=0;i<16;++i)for(int k=0;k<3;++k) {
        double b[3],d[3],dd[3];arc_basis(ts[k],b,d,dd);
        dango_arc_v v={0},a={0},v0={0},a0={0},dv={0},da={0};
        for(int j=0;j<3;++j) {
            int n=(i+15+j)%16;v=arc_add(v,arc_mul(d[j],q[n]));a=arc_add(a,arc_mul(dd[j],q[n]));
            v0=arc_add(v0,arc_mul(d[j],natural[n]));a0=arc_add(a0,arc_mul(dd[j],natural[n]));
            dv=arc_add(dv,arc_mul(d[j],qv[n]));da=arc_add(da,arc_mul(dd[j],qv[n]));
        }
        double s=hypot(v.x,v.y),s0=hypot(v0.x,v0.y);stats[6]=fmin(stats[6],s);
        if(s<1e-7 || !isfinite(s))return false;
        double s3=s*s*s,c=arc_cross(v,a),curvature=c/s3,rest=arc_cross(v0,a0)/(s0*s0*s0);
        dango_arc_v gs=arc_mul(1/s,v);
        dango_arc_v kv=arc_sub(arc_mul(1/s3,(dango_arc_v){a.y,-a.x}),arc_mul(3*c/(s3*s*s),v));
        dango_arc_v ka=arc_mul(1/s3,(dango_arc_v){-v.y,v.x});
        double strain=s-s0,dk=curvature-rest,sdot=arc_dot(gs,dv),kdot=arc_dot(kv,dv)+arc_dot(ka,da);
        stats[0]+=.5*ws[k]*axial*strain*strain/s0;
        stats[1]+=.5*ws[k]*bending*dk*dk*s0;
        stats[2]-=ws[k]*(axial_damping*sdot*sdot/s0+bending_damping*kdot*kdot*s0);
        double cs=ws[k]*(axial*strain+axial_damping*sdot)/s0;
        double ck=ws[k]*(bending*dk+bending_damping*kdot)*s0;
        for(int j=0;j<3;++j) {
            int n=(i+15+j)%16;
            dango_arc_v gradient=arc_add(arc_mul(cs*d[j],gs),arc_mul(ck,arc_add(arc_mul(d[j],kv),arc_mul(dd[j],ka))));
            fq[n]=arc_sub(fq[n],gradient);
        }
    }
    arc_pull(u,lengths,radius,fq,force);
    for(int i=0;i<17;++i) {
        if(!isfinite(force[i].x+force[i].y))return false;
        stats[3]+=force[i].x;stats[4]+=force[i].y;stats[5]+=arc_cross(p[i],force[i]);
    }
    return isfinite(stats[0]+stats[1]+stats[2]);
}
static inline bool dango_arc_area(const dango_arc_v* p,double radius,double target,double coefficient,
    dango_arc_v* force,double* energy,double* actual_area)
{
    static const double ts[3]={.1127016653792583115,.5,.8872983346207416885};
    static const double ws[3]={5.0/18,4.0/9,5.0/18};
    dango_arc_v q[16],u[16],g[16]={{0}};double l[16],area=0;
    if(!arc_controls(p,radius,q,u,l))return false;
    for(int i=0;i<16;++i)for(int k=0;k<3;++k) {
        double b[3],d[3],dd[3];arc_basis(ts[k],b,d,dd);dango_arc_v point={0},v={0};
        for(int j=0;j<3;++j){int n=(i+15+j)%16;point=arc_add(point,arc_mul(b[j],q[n]));v=arc_add(v,arc_mul(d[j],q[n]));}
        area+=.5*ws[k]*arc_cross(point,v);
        for(int j=0;j<3;++j) {
            int n=(i+15+j)%16;
            g[n]=arc_add(g[n],arc_mul(.5*ws[k],arc_add(arc_mul(b[j],(dango_arc_v){v.y,-v.x}),arc_mul(d[j],(dango_arc_v){-point.y,point.x}))));
        }
    }
    if(!isfinite(area) || area<=0)return false;
    double compression=fmax(0,target-area),pressure=coefficient*compression;
    *energy=.5*pressure*compression;*actual_area=area;
    for(int i=0;i<16;++i)g[i]=arc_mul(pressure,g[i]);
    arc_pull(u,l,radius,g,force);return true;
}
#endif

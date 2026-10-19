#ifndef DANGO_BELT_GUARD_H
#define DANGO_BELT_GUARD_H
#include "dango_belt_math.h"
#include <stdint.h>
/* Conservative, continuous Fourier certificate. All interpolation times are
   covered analytically; only theta is bisected. A refusal is not a proof of
   invalidity. Floating point roundoff has an explicit outward allowance. */
enum { BELT_GUARD_CROSS=1,BELT_GUARD_CLEARANCE=2,BELT_GUARD_BUDGET=3,
 BELT_GUARD_WINDING=4,BELT_GUARD_NONFINITE=5,BELT_GUARD_SOLVE=6,BELT_GUARD_GROUND=7,BELT_GUARD_UNCERTAIN=8,BELT_GUARD_MATERIAL_DOMAIN=9 };
typedef struct belt_guard_result {int reason,work,leaves,depth;double cross_lower,distance_lower,winding;} belt_guard_result;
static double belt_guard_quadratic_min(double a,double b,double c){double m=fmin(c,a+b+c);if(a>0){double s=-b/(2*a);if(s>0&&s<1)m=fmin(m,(a*s+b)*s+c);}return m;}
static inline double belt_guard_norm_bound(const double* q,int derivative){
 double sum=derivative?0:hypot(q[0]-q[34],q[1]-q[35]);
 for(int n=1;n<=BELT_K;++n){double f=pow((double)n,derivative);sum+=f*(hypot(q[4*n-2],q[4*n-1])+hypot(q[4*n],q[4*n+1]));}return sum;
}
/* Per-certificate q-local values only. Each harmonic keeps the original
   two hypot calls and their addition; each derivative keeps its own ordered
   sum and original pow expression. The original bound above remains the oracle. */
static void belt_guard_norm_prepare(const double* q,double terms[BELT_K+1]){
 terms[0]=hypot(q[0]-q[34],q[1]-q[35]);
 for(int n=1;n<=BELT_K;++n)terms[n]=hypot(q[4*n-2],q[4*n-1])+hypot(q[4*n],q[4*n+1]);
}
static double belt_guard_norm_prepared(const double terms[BELT_K+1],int derivative){
 double sum=derivative?0:terms[0];
 for(int n=1;n<=BELT_K;++n){double f=pow((double)n,derivative);sum+=f*terms[n];}return sum;
}
static inline belt_v belt_guard_relative(const double* q,double t){return belt_sub(belt_eval(q,t,0),(belt_v){q[34],q[35]});}
/* This bounded cache stores basis values only, never q-dependent geometry.
   Exact double bits and derivative identify an entry. A collision recomputes
   through the original basis routine; copy out so later misses cannot alias a
   caller's already-computed local basis. */
#define BELT_GUARD_BASIS_CACHE_SIZE 1024
#if defined(_WIN32)
#define BELT_GUARD_CACHE_TLS __declspec(thread)
#else
#define BELT_GUARD_CACHE_TLS _Thread_local
#endif
typedef struct belt_guard_basis_entry {
 uint64_t angle_bits;int derivative,valid;double basis[BELT_B];
} belt_guard_basis_entry;
static BELT_GUARD_CACHE_TLS belt_guard_basis_entry belt_guard_basis_cache[BELT_GUARD_BASIS_CACHE_SIZE];
#undef BELT_GUARD_CACHE_TLS
static unsigned int belt_guard_basis_slot(uint64_t bits,int derivative){
 uint64_t hash=bits^((uint64_t)(unsigned int)derivative*UINT64_C(0x9e3779b97f4a7c15));
 hash^=hash>>30;hash*=UINT64_C(0xbf58476d1ce4e5b9);hash^=hash>>27;
 return (unsigned int)(hash&(BELT_GUARD_BASIS_CACHE_SIZE-1));
}
static void belt_guard_basis_cached(double t,int derivative,double* output){
 uint64_t bits;memcpy(&bits,&t,sizeof bits);
 belt_guard_basis_entry* entry=&belt_guard_basis_cache[belt_guard_basis_slot(bits,derivative)];
 if(entry->valid&&entry->angle_bits==bits&&entry->derivative==derivative){memcpy(output,entry->basis,sizeof entry->basis);return;}
 belt_basis(t,derivative,output);
 entry->angle_bits=bits;entry->derivative=derivative;memcpy(entry->basis,output,sizeof entry->basis);entry->valid=1;
}
static belt_v belt_guard_relative_cached(const double* q,double t){
 double basis[BELT_B];belt_guard_basis_cached(t,0,basis);
 return belt_sub(belt_eval_basis(q,basis),(belt_v){q[34],q[35]});
}
typedef struct belt_guard_interval {double center,half;int depth;} belt_guard_interval;
static int belt_guard_certify(const double* from,const double* to,double radius,int budget,belt_guard_result* out){
 memset(out,0,sizeof *out);out->cross_lower=out->distance_lower=INFINITY;
 for(int j=0;j<BELT_D;++j)if(!isfinite(from[j])||!isfinite(to[j])){out->reason=BELT_GUARD_NONFINITE;return 0;}
 double from_norm[BELT_K+1],to_norm[BELT_K+1];
 belt_guard_norm_prepare(from,from_norm);belt_guard_norm_prepare(to,to_norm);
 double rmax=fmax(belt_guard_norm_prepared(from_norm,0),belt_guard_norm_prepared(to_norm,0));
 double umax=fmax(belt_guard_norm_prepared(from_norm,1),belt_guard_norm_prepared(to_norm,1));
 double amax=fmax(belt_guard_norm_prepared(from_norm,2),belt_guard_norm_prepared(to_norm,2));
 double jmax=fmax(belt_guard_norm_prepared(from_norm,3),belt_guard_norm_prepared(to_norm,3));
 double second=umax*amax+rmax*jmax,hard_cross=radius*radius*1e-4,hard_distance=radius*.04;
 double roundoff=1e-11*(1+rmax*rmax+umax*umax+second);
 belt_guard_interval stack[32];int pending=1;stack[0]=(belt_guard_interval){BELT_PI,BELT_PI,0};double winding=0;
 while(pending){if(out->work>=budget){out->reason=BELT_GUARD_BUDGET;return 0;}++out->work;
  belt_guard_interval v=stack[--pending];double t=v.center,h=v.half;out->depth=out->depth>v.depth?out->depth:v.depth;
  /* Basis depends only on this exact t and derivative. Keep all six
     independent q evaluations and their original accumulation/subtraction order. */
  double basis[BELT_B],db[BELT_B],ddb[BELT_B];
  belt_guard_basis_cached(t,0,basis);belt_v r=belt_sub(belt_eval_basis(from,basis),(belt_v){from[34],from[35]});
  belt_guard_basis_cached(t,1,db);belt_v u=belt_eval_basis(from,db);
  belt_guard_basis_cached(t,2,ddb);belt_v a=belt_eval_basis(from,ddb);
  belt_v dr=belt_sub(belt_sub(belt_eval_basis(to,basis),(belt_v){to[34],to[35]}),r),du=belt_sub(belt_eval_basis(to,db),u),da=belt_sub(belt_eval_basis(to,ddb),a);
  double center_cross=belt_guard_quadratic_min(belt_cross(dr,du),belt_cross(r,du)+belt_cross(dr,u),belt_cross(r,u));
  double d2=belt_guard_quadratic_min(belt_dot(dr,dr),2*belt_dot(r,dr),belt_dot(r,r)),dmin=sqrt(fmax(0,d2));
  double derivative=fabs(belt_cross(r,a))+fabs(belt_cross(r,da)+belt_cross(dr,a))+fabs(belt_cross(dr,da));
  double cross_lower=center_cross-derivative*h-.5*second*h*h-roundoff;
  double distance_lower=dmin-umax*h-roundoff;
  if(center_cross<=hard_cross+roundoff){out->reason=BELT_GUARD_CROSS;return 0;}
  if(dmin<=hard_distance+roundoff){out->reason=BELT_GUARD_CLEARANCE;return 0;}
  if(cross_lower>hard_cross&&distance_lower>hard_distance&&umax*h<.5*dmin){
   out->cross_lower=fmin(out->cross_lower,cross_lower);out->distance_lower=fmin(out->distance_lower,distance_lower);++out->leaves;
   belt_v lo=belt_guard_relative_cached(to,t-h),hi=belt_guard_relative_cached(to,t+h);winding+=atan2(belt_cross(lo,hi),belt_dot(lo,hi));continue;
  }
  if(v.depth>=20||pending>29){out->reason=BELT_GUARD_UNCERTAIN;return 0;}
  stack[pending++]=(belt_guard_interval){t+h*.5,h*.5,v.depth+1};stack[pending++]=(belt_guard_interval){t-h*.5,h*.5,v.depth+1};
 }
 out->winding=winding/(2*BELT_PI);if(fabs(out->winding-1)>1e-7){out->reason=BELT_GUARD_WINDING;return 0;}return 1;
}
#endif

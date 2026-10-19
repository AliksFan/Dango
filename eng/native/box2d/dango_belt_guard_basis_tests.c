/* Frozen certify implementation from ad59ed2 is the byte-level oracle.
   This test exercises geometry certificates only, not a material-range scan. */
#include "dango_belt_guard.h"
#include <stdint.h>
#include <stdio.h>
#define CHECK(c) do{if(!(c)){fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#c);return 1;}}while(0)
static int belt_guard_certify_reference(const double* from,const double* to,double radius,int budget,belt_guard_result* out){
 memset(out,0,sizeof *out);out->cross_lower=out->distance_lower=INFINITY;
 for(int j=0;j<BELT_D;++j)if(!isfinite(from[j])||!isfinite(to[j])){out->reason=BELT_GUARD_NONFINITE;return 0;}
 double rmax=fmax(belt_guard_norm_bound(from,0),belt_guard_norm_bound(to,0));
 double umax=fmax(belt_guard_norm_bound(from,1),belt_guard_norm_bound(to,1));
 double amax=fmax(belt_guard_norm_bound(from,2),belt_guard_norm_bound(to,2));
 double jmax=fmax(belt_guard_norm_bound(from,3),belt_guard_norm_bound(to,3));
 double second=umax*amax+rmax*jmax,hard_cross=radius*radius*1e-4,hard_distance=radius*.04;
 double roundoff=1e-11*(1+rmax*rmax+umax*umax+second);
 belt_guard_interval stack[32];int pending=1;stack[0]=(belt_guard_interval){BELT_PI,BELT_PI,0};double winding=0;
 while(pending){if(out->work>=budget){out->reason=BELT_GUARD_BUDGET;return 0;}++out->work;
  belt_guard_interval v=stack[--pending];double t=v.center,h=v.half;out->depth=out->depth>v.depth?out->depth:v.depth;
  belt_v r=belt_guard_relative(from,t),u=belt_eval(from,t,1),a=belt_eval(from,t,2);
  belt_v dr=belt_sub(belt_guard_relative(to,t),r),du=belt_sub(belt_eval(to,t,1),u),da=belt_sub(belt_eval(to,t,2),a);
  double center_cross=belt_guard_quadratic_min(belt_cross(dr,du),belt_cross(r,du)+belt_cross(dr,u),belt_cross(r,u));
  double d2=belt_guard_quadratic_min(belt_dot(dr,dr),2*belt_dot(r,dr),belt_dot(r,r)),dmin=sqrt(fmax(0,d2));
  double derivative=fabs(belt_cross(r,a))+fabs(belt_cross(r,da)+belt_cross(dr,a))+fabs(belt_cross(dr,da));
  double cross_lower=center_cross-derivative*h-.5*second*h*h-roundoff;
  double distance_lower=dmin-umax*h-roundoff;
  if(center_cross<=hard_cross+roundoff){out->reason=BELT_GUARD_CROSS;return 0;}
  if(dmin<=hard_distance+roundoff){out->reason=BELT_GUARD_CLEARANCE;return 0;}
  if(cross_lower>hard_cross&&distance_lower>hard_distance&&umax*h<.5*dmin){
   out->cross_lower=fmin(out->cross_lower,cross_lower);out->distance_lower=fmin(out->distance_lower,distance_lower);++out->leaves;
   belt_v lo=belt_guard_relative(to,t-h),hi=belt_guard_relative(to,t+h);winding+=atan2(belt_cross(lo,hi),belt_dot(lo,hi));continue;
  }
  if(v.depth>=20||pending>29){out->reason=BELT_GUARD_UNCERTAIN;return 0;}
  stack[pending++]=(belt_guard_interval){t+h*.5,h*.5,v.depth+1};stack[pending++]=(belt_guard_interval){t-h*.5,h*.5,v.depth+1};
 }
 out->winding=winding/(2*BELT_PI);if(fabs(out->winding-1)>1e-7){out->reason=BELT_GUARD_WINDING;return 0;}return 1;
}

static unsigned int cases_checked;
static int compare(const double* from,const double* to,double radius,int budget,int expected){
 belt_guard_result actual,original;
 /* Distinct poison makes incomplete initialization, including padding, visible. */
 memset(&actual,0xa5,sizeof actual);memset(&original,0x5a,sizeof original);
 int a=belt_guard_certify(from,to,radius,budget,&actual);
 int b=belt_guard_certify_reference(from,to,radius,budget,&original);
 CHECK(a==b);CHECK(memcmp(&actual,&original,sizeof actual)==0);
 if(expected>=0)CHECK(actual.reason==expected);
 CHECK(a==(actual.reason==0));++cases_checked;return 0;
}
static void circle(double* q){memset(q,0,BELT_D*sizeof(double));q[2]=q[5]=1;}
static uint32_t random_state=0x12345678u;
static double random_signed(void){random_state=1664525u*random_state+1013904223u;return (double)(random_state>>8)/8388608.0-1;}
static int norm_contract(void){
 const double scales[]={0.0,1e-200,1.0,1e200};
 for(size_t scale=0;scale<sizeof scales/sizeof scales[0];++scale)
  for(int fixture=0;fixture<16;++fixture){
   double q[BELT_D],terms[BELT_K+1];
   for(int j=0;j<BELT_D;++j)q[j]=scales[scale]*random_signed();
   belt_guard_norm_prepare(q,terms);
   for(int derivative=0;derivative<4;++derivative){
    double original=belt_guard_norm_bound(q,derivative),actual=belt_guard_norm_prepared(terms,derivative);
    CHECK(memcmp(&actual,&original,sizeof actual)==0);
   }
  }
 return 0;
}
static int cache_contract(void){
 /* Pigeonhole search deliberately finds two distinct exact angles in one slot. */
 int seen[BELT_GUARD_BASIS_CACHE_SIZE];for(int i=0;i<BELT_GUARD_BASIS_CACHE_SIZE;++i)seen[i]=-1;
 double first=0,second=0;int collision=0;
 for(int i=0;i<=BELT_GUARD_BASIS_CACHE_SIZE;++i){
  double t=2*BELT_PI*(i+1)/4096;uint64_t bits;memcpy(&bits,&t,sizeof bits);
  unsigned int slot=belt_guard_basis_slot(bits,0);
  if(seen[slot]>=0){first=2*BELT_PI*(seen[slot]+1)/4096;second=t;collision=1;break;}
  seen[slot]=i;
 }
 CHECK(collision);CHECK(memcmp(&first,&second,sizeof first)!=0);
 const double angles[]={0.0,-0.0,BELT_PI,2*BELT_PI,first,second,first,second,first};
 for(int repeat=0;repeat<3;++repeat)for(size_t i=0;i<sizeof angles/sizeof angles[0];++i)
  for(int derivative=0;derivative<3;++derivative){
   double expected[BELT_B],actual[BELT_B];belt_basis(angles[i],derivative,expected);
   belt_guard_basis_cached(angles[i],derivative,actual);CHECK(memcmp(actual,expected,sizeof actual)==0);
  }
 /* Explicitly evict the same derivative's entry and check the original caller
    copy survives that collision as well as subsequent derivative lookups. */
 double kept[BELT_B],expected[BELT_B],scratch[BELT_B];belt_basis(first,0,expected);
 belt_guard_basis_cached(first,0,kept);belt_guard_basis_cached(second,0,scratch);
 CHECK(memcmp(kept,expected,sizeof kept)==0);belt_guard_basis_cached(first,0,scratch);
 CHECK(memcmp(scratch,expected,sizeof scratch)==0);
 /* The same cached angles evaluate independent shapes, including invalid ones. */
 double q[BELT_D],to[BELT_D];circle(q);memcpy(to,q,sizeof to);to[34]=.7;
 CHECK(compare(q,to,1,10000,-1)==0);q[2]=q[5]=.03;
 CHECK(compare(q,q,1,10000,BELT_GUARD_CLEARANCE)==0);circle(q);
 CHECK(compare(q,to,1,10000,-1)==0);CHECK(compare(q,q,1,10000,0)==0);
 return 0;
}
int main(void){
 CHECK(norm_contract()==0);CHECK(cache_contract()==0);
 double q[BELT_D],to[BELT_D];circle(q);
 CHECK(compare(q,q,1,10000,0)==0);
 q[2]=1.7;q[5]=.18;CHECK(compare(q,q,1,10000,0)==0);
 circle(q);q[2]=.9;q[5]=1.1;q[10]=q[13]=.1;CHECK(compare(q,q,1,10000,0)==0);
 circle(q);q[9]=.8;CHECK(compare(q,q,1,10000,-1)==0);
 circle(q);q[34]=1.2;CHECK(compare(q,q,1,10000,-1)==0);
 circle(q);q[34]=1.00003*cos(BELT_PI/256);q[35]=1.00003*sin(BELT_PI/256);
 CHECK(compare(q,q,1,10000,-1)==0);
 circle(q);memcpy(to,q,sizeof to);to[2]=to[5]=-1;
 CHECK(compare(to,to,1,10000,0)==0);
 CHECK(compare(q,to,1,10000,BELT_GUARD_CROSS)==0);
 CHECK(compare(q,q,1,0,BELT_GUARD_BUDGET)==0);
 CHECK(compare(q,q,1,1,BELT_GUARD_BUDGET)==0);
 circle(q);q[34]=.959999;CHECK(compare(q,q,1,10000,BELT_GUARD_UNCERTAIN)==0);
 memset(q,0,sizeof q);q[6]=q[9]=1;CHECK(compare(q,q,1,10000,BELT_GUARD_WINDING)==0);
 memset(q,0,sizeof q);CHECK(compare(q,q,1,10000,BELT_GUARD_CROSS)==0);
 circle(q);q[2]=q[5]=.03;CHECK(compare(q,q,1,10000,BELT_GUARD_CLEARANCE)==0);
 circle(q);memcpy(to,q,sizeof to);q[0]=NAN;CHECK(compare(q,to,1,10000,BELT_GUARD_NONFINITE)==0);
 circle(q);to[35]=INFINITY;CHECK(compare(q,to,1,10000,BELT_GUARD_NONFINITE)==0);
 memcpy(to,q,sizeof to);to[2]=-INFINITY;CHECK(compare(q,to,1,10000,BELT_GUARD_NONFINITE)==0);
 const int budgets[]={0,1,7,31,128,10000};
 for(int fixture=0;fixture<24;++fixture){
  circle(q);q[0]=random_signed();q[1]=random_signed();q[34]=q[0]+.6*random_signed();q[35]=q[1]+.6*random_signed();
  for(int j=2;j<34;++j)q[j]+=.025*random_signed();
  memcpy(to,q,sizeof to);for(int j=0;j<BELT_D;++j)to[j]+=.05*random_signed();
  for(size_t i=0;i<sizeof budgets/sizeof budgets[0];++i){
   CHECK(compare(q,to,1,budgets[i],-1)==0);
   CHECK(compare(q,q,1,budgets[i],-1)==0);
  }
 }
 printf("guard basis reuse vs frozen original full-result byte equivalence PASS cases=%u\n",cases_checked);return 0;
}

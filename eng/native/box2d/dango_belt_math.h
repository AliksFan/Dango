#ifndef DANGO_BELT_MATH_H
#define DANGO_BELT_MATH_H
#include <math.h>
#include <string.h>
#define BELT_K 8
#define BELT_B 17
#define BELT_D 36
#define BELT_Q 64
#define BELT_PI 3.1415926535897932384626433832795
typedef struct belt_v {double x,y;} belt_v;
static inline double belt_dot(belt_v a,belt_v b){return a.x*b.x+a.y*b.y;}
static inline double belt_cross(belt_v a,belt_v b){return a.x*b.y-a.y*b.x;}
static inline belt_v belt_add(belt_v a,belt_v b){return (belt_v){a.x+b.x,a.y+b.y};}
static inline belt_v belt_sub(belt_v a,belt_v b){return (belt_v){a.x-b.x,a.y-b.y};}
static inline belt_v belt_mul(double s,belt_v a){return (belt_v){s*a.x,s*a.y};}
static inline void belt_basis(double t,int derivative,double* b){
 b[0]=derivative?0:1;
 for(int n=1;n<=BELT_K;++n){double c=cos(n*t),s=sin(n*t),f=derivative==2?-n*n:1;
  b[2*n-1]=derivative==1?-n*s:f*c;b[2*n]=derivative==1?n*c:f*s;}
}
static inline belt_v belt_eval_basis(const double* q,const double* b){belt_v p={0};for(int j=0;j<BELT_B;++j){p.x+=b[j]*q[2*j];p.y+=b[j]*q[2*j+1];}return p;}
static inline belt_v belt_eval(const double* q,double t,int derivative){double b[BELT_B];belt_basis(t,derivative,b);return belt_eval_basis(q,b);}
static inline double belt_area(const double* q,double* gradient){
 double a=0;if(gradient)memset(gradient,0,BELT_D*sizeof(double));
 for(int n=1;n<=BELT_K;++n){int i=4*n-2;double f=BELT_PI*n;
  a+=f*(q[i]*q[i+3]-q[i+1]*q[i+2]);
  if(gradient){gradient[i]=f*q[i+3];gradient[i+3]=f*q[i];gradient[i+1]=-f*q[i+2];gradient[i+2]=-f*q[i+1];}}
 return a;
}
static inline double belt_polynomial_value(const double* p,int degree,double z){double f=p[degree];for(int i=degree-1;i>=0;--i)f=f*z+p[i];return f;}
/* Derivative recursion partitions a polynomial into monotone intervals.
   Checking stationary endpoints also retains even-multiplicity roots. */
static int belt_polynomial_roots(const double* input,int degree,double* roots){
 double p[17],scale=0;for(int i=0;i<=degree;++i)scale=fmax(scale,fabs(input[i]));if(scale<1e-25)return 0;
 for(int i=0;i<=degree;++i)p[i]=input[i]/scale;while(degree>0&&fabs(p[degree])<1e-15)--degree;if(degree==0)return 0;
 if(degree==1){double z=-p[0]/p[1];if(z>=-1-1e-12&&z<=1+1e-12){roots[0]=fmax(-1,fmin(1,z));return 1;}return 0;}
 double derivative[17]={0},stationary[17],cuts[19];for(int i=1;i<=degree;++i)derivative[i-1]=i*p[i];int ns=belt_polynomial_roots(derivative,degree-1,stationary),nc=0,n=0;cuts[nc++]=-1;
 for(int i=0;i<ns;++i)if(stationary[i]>-1+1e-13&&stationary[i]<1-1e-13)cuts[nc++]=stationary[i];cuts[nc++]=1;
 for(int i=0;i<nc;++i){double z=cuts[i],f=belt_polynomial_value(p,degree,z);
  if(fabs(f)<2e-13&&(n==0||z-roots[n-1]>1e-9))roots[n++]=z;
  if(i+1<nc){double hi=cuts[i+1],fh=belt_polynomial_value(p,degree,hi);if(f*fh<0&&fabs(f)>2e-13&&fabs(fh)>2e-13){double lo=z,fl=f;for(int k=0;k<52;++k){double mid=.5*(lo+hi),fm=belt_polynomial_value(p,degree,mid);if(fl*fm<=0)hi=mid;else{lo=mid;fl=fm;}}double r=.5*(lo+hi);if(n==0||r-roots[n-1]>1e-9)roots[n++]=r;}}
 }return n;
}
/* Two bounded tangent-half-angle charts avoid infinity at theta=pi.
   Each degree<=16 polynomial is isolated through its derivative roots,
   rather than relying on fixed-grid sign changes. */
static inline int belt_roots(const double* q,int axis,int derivative,double level,double* roots){
 int used=0;for(int chart=0;chart<2;++chart){double center=(chart+.5)*BELT_PI,p[17]={0};
  double cpoly[17]={1},spoly[17]={0};
  for(int n=0;n<=BELT_K;++n){double a=0,b=0;if(n==0)a=derivative?0:q[axis];else{a=q[4*n-2+axis];b=q[4*n+axis];if(derivative==1){double old=a;a=n*b;b=-n*old;}else if(derivative==2){a*=-n*n;b*=-n*n;}}
   if(n==0)a-=level;double ca=cos(n*center),sa=sin(n*center),ar=a*ca+b*sa,br=-a*sa+b*ca;
   double base[17]={0};for(int j=0;j<=2*n;++j)base[j]=ar*cpoly[j]+br*spoly[j];
   for(int k=0;k<BELT_K-n;++k)for(int j=2*(n+k)+2;j>=2;--j)base[j]+=base[j-2];
   for(int j=0;j<=16;++j)p[j]+=base[j];
   if(n<BELT_K){double nextc[17]={0},nexts[17]={0};for(int j=0;j<=2*n;++j){nextc[j]+=cpoly[j];nextc[j+2]-=cpoly[j];nextc[j+1]-=2*spoly[j];nexts[j]+=spoly[j];nexts[j+2]-=spoly[j];nexts[j+1]+=2*cpoly[j];}memcpy(cpoly,nextc,sizeof cpoly);memcpy(spoly,nexts,sizeof spoly);}
  }
  double zs[32];int nr=belt_polynomial_roots(p,16,zs);for(int i=0;i<nr;++i){double t=center+2*atan(zs[i]);if(t>=2*BELT_PI-1e-10)t=0;if(t<1e-10&&chart==1)continue;if(used==0||fabs(t-roots[used-1])>1e-7)roots[used++]=t;}
 }
 return used;
}
static inline void belt_bounds(const double* q,double* bounds,double* foot){
 belt_v start=belt_eval(q,0,0);bounds[0]=bounds[2]=start.x;bounds[1]=bounds[3]=start.y;*foot=0;
 for(int axis=0;axis<2;++axis){double roots[64];int count=belt_roots(q,axis,1,0,roots);
  for(int i=0;i<count;++i){belt_v p=belt_eval(q,roots[i],0);double v=axis?p.y:p.x;
   if(v<bounds[axis]){bounds[axis]=v;if(axis)*foot=roots[i];}if(v>bounds[axis+2])bounds[axis+2]=v;}}
}
static inline int belt_cholesky(double a[BELT_D][BELT_D],double* rhs,double* result){
 for(int i=0;i<BELT_D;++i)for(int j=0;j<=i;++j){double s=a[i][j];for(int k=0;k<j;++k)s-=a[i][k]*a[j][k];if(i==j){if(!(s>1e-16)||!isfinite(s))return 0;a[i][j]=sqrt(s);}else a[i][j]=s/a[j][j];}
 double y[BELT_D];for(int i=0;i<BELT_D;++i){double s=rhs[i];for(int j=0;j<i;++j)s-=a[i][j]*y[j];y[i]=s/a[i][i];}
 for(int i=BELT_D-1;i>=0;--i){double s=y[i];for(int j=i+1;j<BELT_D;++j)s-=a[j][i]*result[j];result[i]=s/a[i][i];}return 1;
}
#endif

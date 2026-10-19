#include "dango_belt_math.h"
#include "dango_belt_roots_reference.h"
#include <float.h>
#include <stdint.h>
#include <stdio.h>
#define CHECK(c) do{if(!(c)){fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#c);return 1;}}while(0)

static unsigned int cases_checked;
static uint32_t random_state=0x12345678u;
static double random_signed(void){random_state=1664525u*random_state+1013904223u;return (double)(random_state>>8)/8388608.0-1;}

static int chart_contract(void){
 const belt_root_charts* cached=belt_get_root_charts();
 CHECK(cached->initialized==1);CHECK(belt_get_root_charts()==cached);
 for(int chart=0;chart<2;++chart){double center=(chart+.5)*BELT_PI;
  double cpoly[17]={1},spoly[17]={0};
  for(int n=0;n<=BELT_K;++n){
   const belt_root_chart_basis* actual=&cached->basis[chart][n];double ca=cos(n*center),sa=sin(n*center);
   CHECK(memcmp(cpoly,actual->cpoly,sizeof cpoly)==0);
   CHECK(memcmp(spoly,actual->spoly,sizeof spoly)==0);
   CHECK(memcmp(&ca,&actual->ca,sizeof ca)==0);CHECK(memcmp(&sa,&actual->sa,sizeof sa)==0);
   if(n<BELT_K){double nextc[17]={0},nexts[17]={0};for(int j=0;j<=2*n;++j){nextc[j]+=cpoly[j];nextc[j+2]-=cpoly[j];nextc[j+1]-=2*spoly[j];nexts[j]+=spoly[j];nexts[j+2]-=spoly[j];nexts[j+1]+=2*cpoly[j];}memcpy(cpoly,nextc,sizeof cpoly);memcpy(spoly,nexts,sizeof spoly);}
  }
 }
 return 0;
}

static int compare(const double* q,int axis,int derivative,double level){
 double actual[64],expected[64];
 /* Same poison also checks every untouched output slot, not merely root count. */
 memset(actual,0xa5,sizeof actual);memset(expected,0xa5,sizeof expected);
 int a=belt_roots(q,axis,derivative,level,actual);
 int b=belt_roots_reference(q,axis,derivative,level,expected);
 CHECK(a==b);CHECK(a>=0&&a<=64);CHECK(memcmp(actual,expected,sizeof actual)==0);
 ++cases_checked;return 0;
}

static int all_arguments(const double* q){
 const double levels[]={0.0,-0.0,.5,-.5,1,-1,1e-26,1e6};
 for(int axis=0;axis<2;++axis)for(int derivative=0;derivative<3;++derivative)
  for(size_t level=0;level<sizeof levels/sizeof levels[0];++level)
   CHECK(compare(q,axis,derivative,levels[level])==0);
 return 0;
}

static int memo_contract(void){
 double a[BELT_D]={0},b[BELT_D];a[2]=a[5]=1;
 for(int axis=0;axis<2;++axis)for(int derivative=0;derivative<3;++derivative){
  CHECK(compare(a,axis,derivative,0)==0);CHECK(compare(a,axis,derivative,0)==0);
  /* Every q byte participates, including core fields unused by roots math. */
  for(int j=0;j<BELT_D;++j){
   memcpy(b,a,sizeof b);uint64_t bits;memcpy(&bits,&b[j],sizeof bits);bits^=UINT64_C(1);memcpy(&b[j],&bits,sizeof bits);
   CHECK(compare(b,axis,derivative,0)==0);
   CHECK(memcmp(belt_root_memo[axis][derivative].q,b,sizeof b)==0);
   CHECK(compare(a,axis,derivative,0)==0);
   CHECK(memcmp(belt_root_memo[axis][derivative].q,a,sizeof a)==0);
  }
  /* A/B/A evicts a single slot and must recompute A, never retain q pointers. */
  memcpy(b,a,sizeof b);b[6]=.02;
  CHECK(compare(a,axis,derivative,0)==0);CHECK(compare(b,axis,derivative,0)==0);
  CHECK(compare(a,axis,derivative,0)==0);CHECK(memcmp(belt_root_memo[axis][derivative].q,a,sizeof a)==0);
  const uint64_t levels[]={UINT64_C(0),UINT64_C(0x8000000000000000),UINT64_C(1),
   UINT64_C(0x3fe0000000000000),UINT64_C(0x3fe0000000000001),
   UINT64_C(0x7ff8000000000001),UINT64_C(0x7ff8000000000002)};
  for(size_t i=0;i<sizeof levels/sizeof levels[0];++i){
   double level;memcpy(&level,&levels[i],sizeof level);
   CHECK(compare(a,axis,derivative,level)==0);CHECK(compare(a,axis,derivative,level)==0);
   CHECK(memcmp(&belt_root_memo[axis][derivative].level,&level,sizeof level)==0);
  }
 }
 /* Different caller output/tail poison on a repeated exact hit. A memo stores
    only owned root values and writes exactly used roots, including used==0. */
 for(int empty=0;empty<2;++empty){
  double q[BELT_D]={0};if(!empty)q[2]=q[5]=1;
  for(int repeat=0;repeat<3;++repeat){
   double actual[64],expected[64];int poison=repeat==0?0xa5:repeat==1?0x5a:0x6b;
   memset(actual,poison,sizeof actual);memset(expected,poison,sizeof expected);
   int used=belt_roots(q,0,0,0,actual),original=belt_roots_reference(q,0,0,0,expected);
   CHECK(used==original);CHECK(empty?used==0:used>0);CHECK(memcmp(actual,expected,sizeof actual)==0);
   memset(actual,0xff,sizeof actual);
  }
 }
 /* Distinct signed-zero/NaN payloads in q also replace rather than match. */
 const uint64_t values[]={UINT64_C(0),UINT64_C(0x8000000000000000),
  UINT64_C(0x7ff8000000000001),UINT64_C(0x7ff8000000000002)};
 for(size_t i=0;i<sizeof values/sizeof values[0];++i){
  memcpy(b,a,sizeof b);memcpy(&b[0],&values[i],sizeof b[0]);
  CHECK(compare(b,0,0,0)==0);CHECK(compare(b,0,0,0)==0);
  CHECK(memcmp(belt_root_memo[0][0].q,b,sizeof b)==0);
 }
 /* Unsupported selectors retain the old computation path and no memo mutation.
    axis2 remains within the allocated q buffer, so the old oracle is defined. */
 belt_root_memo_entry retained[2][3];memcpy(retained,belt_root_memo,sizeof retained);
 CHECK(compare(a,2,0,0)==0);CHECK(compare(a,0,-1,0)==0);CHECK(compare(a,1,3,0)==0);
 CHECK(memcmp(retained,belt_root_memo,sizeof retained)==0);
 return 0;
}

int main(void){
 CHECK(chart_contract()==0);
 CHECK(memo_contract()==0);
 belt_root_charts retained;memcpy(&retained,belt_get_root_charts(),sizeof retained);
 double q[BELT_D]={0};CHECK(all_arguments(q)==0);
 /* Constant, ordinary circle, repeated/even roots, chart boundaries and degree16. */
 q[0]=.5;q[1]=-.5;CHECK(all_arguments(q)==0);
 q[0]=q[1]=0;q[2]=q[5]=1;CHECK(all_arguments(q)==0);
 CHECK(compare(q,0,0,nextafter(1,0))==0);CHECK(compare(q,0,0,nextafter(1,INFINITY))==0);
 memset(q,0,sizeof q);q[30]=q[33]=1;CHECK(all_arguments(q)==0);
 memset(q,0,sizeof q);q[2]=q[5]=1e-26;CHECK(all_arguments(q)==0);
 for(int fixture=0;fixture<32;++fixture){
  for(int j=0;j<BELT_D;++j)q[j]=random_signed()*(j<2?3:.025);
  q[2]+=.6;q[5]+=.6;CHECK(all_arguments(q)==0);
 }
 /* Every supported axis and derivative sees invalid levels and coefficients;
    this remains equivalence to the old routine rather than adding acceptance. */
 const double invalid[]={NAN,INFINITY,-INFINITY,DBL_MAX,-DBL_MAX};
 for(size_t value=0;value<sizeof invalid/sizeof invalid[0];++value){
  memset(q,0,sizeof q);q[2]=q[5]=1;
  for(int axis=0;axis<2;++axis)for(int derivative=0;derivative<3;++derivative)
   CHECK(compare(q,axis,derivative,invalid[value])==0);
  for(int j=0;j<BELT_D;++j){
   memset(q,0,sizeof q);q[2]=q[5]=1;q[j]=invalid[value];
   for(int axis=0;axis<2;++axis)for(int derivative=0;derivative<3;++derivative)
    CHECK(compare(q,axis,derivative,0)==0);
  }
 }
 CHECK(memcmp(&retained,belt_get_root_charts(),sizeof retained)==0);
 CHECK(chart_contract()==0);
 printf("root chart basis and complete roots vs frozen original byte equivalence PASS cases=%u\n",cases_checked);
 return 0;
}

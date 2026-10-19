#include "dango_belt_basis_cache.h"
#include <stdint.h>
#include <stdio.h>
#define CHECK(c) do{if(!(c)){fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#c);return 1;}}while(0)
static uint32_t random_state=0x12345678u;
static double random_signed(void){random_state=1664525u*random_state+1013904223u;return (double)(random_state>>8)/8388608.0-1;}
static int basis_contract(void){
 const belt_basis_grids* grids=belt_get_basis_grids();double original[BELT_B],dtheta=2*BELT_PI/BELT_Q;
 for(int derivative=0;derivative<3;++derivative)for(int i=0;i<BELT_Q;++i){
  belt_basis(dtheta*i,derivative,original);
  CHECK(memcmp(original,grids->material[derivative][i],sizeof original)==0);
 }
 for(int i=0;i<=16;++i){belt_basis(2*BELT_PI*i/16,0,original);CHECK(memcmp(original,grids->radial[i],sizeof original)==0);}
 CHECK(memcmp(grids->radial[0],grids->radial[16],sizeof original)!=0);
 CHECK(belt_get_basis_grids()==grids);
 return 0;
}
static int extrema_contract(void){
 for(int fixture=0;fixture<32;++fixture){
  double q[BELT_D]={0};q[0]=3*random_signed();q[1]=3*random_signed();q[2]=.6;q[5]=.6;
  for(int j=6;j<34;++j)q[j]=.015*random_signed();q[34]=q[0];q[35]=q[1];
  double reused[4],original[4],foot=0,original_foot=0;belt_bounds(q,reused,&foot);
  for(int update=0;update<4;++update){
   /* Mirrors the projection's odd-coefficient-only invalidation boundary. */
   for(int j=1;j<BELT_D;j+=2)q[j]+=.01*random_signed();
   belt_bounds_y(q,reused,&foot);belt_bounds(q,original,&original_foot);
   CHECK(memcmp(reused,original,sizeof original)==0);
   CHECK(memcmp(&foot,&original_foot,sizeof foot)==0);
  }
 }
 return 0;
}
int main(void){CHECK(basis_contract()==0);CHECK(extrema_contract()==0);puts("original-angle basis grids including radial endpoint16 and y-only extrema byte equivalence PASS");return 0;}

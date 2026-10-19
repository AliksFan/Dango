#ifndef DANGO_BELT_BASIS_CACHE_H
#define DANGO_BELT_BASIS_CACHE_H
#include "dango_belt_math.h"
#if defined(_WIN32)
#define BELT_BASIS_CACHE_TLS __declspec(thread)
#else
#define BELT_BASIS_CACHE_TLS _Thread_local
#endif
/* Only q-independent material grids are cached. Preserve both original angle
   expressions, including the radial endpoint at 2*pi rather than aliasing 0. */
typedef struct belt_basis_grids {
 double material[3][BELT_Q][BELT_B],radial[17][BELT_B];
 int initialized;
} belt_basis_grids;
static BELT_BASIS_CACHE_TLS belt_basis_grids belt_cached_basis;
static const belt_basis_grids* belt_get_basis_grids(void){
 if(!belt_cached_basis.initialized){
  double dtheta=2*BELT_PI/BELT_Q;
  for(int derivative=0;derivative<3;++derivative)
   for(int i=0;i<BELT_Q;++i)belt_basis(dtheta*i,derivative,belt_cached_basis.material[derivative][i]);
  for(int i=0;i<=16;++i)belt_basis(2*BELT_PI*i/16,0,belt_cached_basis.radial[i]);
  belt_cached_basis.initialized=1;
 }
 return &belt_cached_basis;
}
#undef BELT_BASIS_CACHE_TLS
#endif

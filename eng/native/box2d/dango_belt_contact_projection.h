/* Shared unchanged SPD contact metric from the room solver. */
static int belt_contact_projection_direction(const belt_body* b,double theta,belt_v normal,double* direction,double* effective){
 const double h=(double).0166666f/4;double basis[BELT_B],jacobian[BELT_D]={0},matrix[BELT_D][BELT_D],gradient[BELT_D],elastic,damping,derivative;
 if(!isfinite(theta)||!isfinite(normal.x)||!isfinite(normal.y))return 0;
 belt_residual previous[BELT_MAX_RESIDUALS];if(belt_residuals(b,b->q,previous,&derivative)<0)return 0;
 if(!isfinite(belt_objective(b,b->q,b->q,b->q,previous,h,gradient,matrix,&elastic,&damping,&derivative)))return 0;
 for(int j=0;j<BELT_D;++j)for(int k=0;k<=j;++k)matrix[j][k]*=h*h;
 belt_basis(theta,0,basis);for(int j=0;j<BELT_B;++j){jacobian[2*j]=basis[j]*normal.x;jacobian[2*j+1]=basis[j]*normal.y;}
 if(!belt_cholesky(matrix,jacobian,direction))return 0;*effective=0;
 for(int j=0;j<BELT_D;++j){if(!isfinite(direction[j]))return 0;*effective+=jacobian[j]*direction[j];}
 return isfinite(*effective)&&*effective>0;
}

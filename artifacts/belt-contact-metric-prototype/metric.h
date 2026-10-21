/* Experimental numerical metric only. Not included by production sources. */
static int metric_direction(belt_body* b,double theta,belt_v n,double* z,double* effective){
 const double h=(double).0166666f/4;double basis[BELT_B],j[BELT_D]={0},a[BELT_D][BELT_D],g[BELT_D],e,d,md;
 belt_residual previous[BELT_MAX_RESIDUALS];if(belt_residuals(b,b->q,previous,&md)<0)return 0;
 if(!isfinite(belt_objective(b,b->q,b->q,b->q,previous,h,g,a,&e,&d,&md)))return 0;
 for(int k=0;k<BELT_D;++k)for(int l=0;l<=k;++l)a[k][l]*=h*h;
 belt_basis(theta,0,basis);for(int k=0;k<BELT_B;++k){j[2*k]=basis[k]*n.x;j[2*k+1]=basis[k]*n.y;}
 if(!belt_cholesky(a,j,z))return 0;*effective=0;
 for(int k=0;k<BELT_D;++k){if(!isfinite(z[k]))return 0;*effective+=j[k]*z[k];}
 return isfinite(*effective)&&*effective>0;
}
static int room_project(room_world* w,belt_body* b,double theta,belt_v n,double depth){
 if(!EXPERIMENT_METRIC)return room_mass_project(w,b,theta,n,depth);
 double z[BELT_D],k,trial[BELT_D];if(!isfinite(depth)||!metric_direction(b,theta,n,z,&k))return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);
 double lambda=(depth+2e-9)/k,maximum=0;for(int j=0;j<BELT_D;++j)maximum=fmax(maximum,fabs(lambda*z[j]));
 if(maximum>ROOM_POSITION_LIMIT)lambda*=ROOM_POSITION_LIMIT/maximum;
 for(int j=0;j<BELT_D;++j)trial[j]=b->q[j]+lambda*z[j];
 if(!belt_guard_path(b,b->q,trial,3)||!belt_material_domain_valid(b,trial)){
  if(b->guard[9]==BELT_GUARD_BUDGET)return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);
  /* Preserve the existing guarded common-translation candidate. Final global
     endpoint and swept certificates remain mandatory, exactly as before. */
  memcpy(trial,b->q,sizeof trial);double shift=fmin(ROOM_POSITION_LIMIT,depth+2e-9);trial[0]+=shift*n.x;trial[1]+=shift*n.y;trial[34]+=shift*n.x;trial[35]+=shift*n.y;maximum=shift;
  if(!belt_guard_path(b,b->q,trial,3)||!belt_material_domain_valid(b,trial))return room_refuse(w,DANGO_BELT_ROOM_REJECTED,1);
 }
 double elastic=belt_elastic_energy(b),gravity=belt_gravity_energy(b);memcpy(b->q,trial,sizeof trial);
 double de=belt_elastic_energy(b)-elastic,dg=belt_gravity_energy(b)-gravity;
 w->diag[12]+=de;w->diag[13]+=dg;w->diag[14]=fmax(w->diag[14],fmin(maximum,ROOM_POSITION_LIMIT));w->diag[15]+=fmax(0,de+dg);w->last_theta=theta;return 1;
}

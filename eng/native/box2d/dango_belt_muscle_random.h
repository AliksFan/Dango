#ifndef DANGO_BELT_MUSCLE_RANDOM_H
#define DANGO_BELT_MUSCLE_RANDOM_H

/* Pure indexed noise: retries and follower trials cannot consume randomness. */
static double belt_muscle_random_value(uint32_t seed,int row,uint32_t knot){
 uint32_t x=seed^((uint32_t)(row+13)*UINT32_C(0x9e3779b9))^(knot*UINT32_C(0x85ebca6b));
 x^=x>>16;x*=UINT32_C(0x7feb352d);x^=x>>15;x*=UINT32_C(0x846ca68b);x^=x>>16;
 return .6+.4*(double)(x>>8)/16777215.;
}
static double belt_muscle_random_gain(uint32_t seed,int row,double time){
 double t=fmax(0,time)*6.4;uint32_t k=(uint32_t)floor(t);double s=belt_muscle_smooth(t-k);
 return (1-s)*belt_muscle_random_value(seed,row,k)+s*belt_muscle_random_value(seed,row,k+1);
}

/* Exact derivative roots partition the Fourier curve into monotone sections.
   Reuse that partition for every row instead of isolating 25 degree-16
   polynomials. Safeguarded Newton always retains a bracket on the true curve. */
static int belt_muscle_section_roots(const double* q,const double* cuts,int nc,double level,double* roots){
 int used=0;
 for(int i=0;i<nc-1;++i){
  double lo=cuts[i],hi=cuts[i+1],fl=belt_eval(q,lo,0).y-level,fh=belt_eval(q,hi,0).y-level;
  if(fabs(fl)<1e-12&&(used==0||fabs(lo-roots[used-1])>1e-8))roots[used++]=lo;
  if(fl*fh>=0||fabs(fl)<1e-12||fabs(fh)<1e-12)continue;
  double t=lo+(hi-lo)*fabs(fl)/(fabs(fl)+fabs(fh));int converged=0;
  for(int k=0;k<60;++k){
   double f=belt_eval(q,t,0).y-level;if(fabs(f)<1e-12){converged=1;break;}
   if(fl*f<=0)hi=t;else{lo=t;fl=f;}
   double derivative=belt_eval(q,t,1).y,next=t-f/derivative;
   t=isfinite(next)&&next>lo+.01*(hi-lo)&&next<hi-.01*(hi-lo)?next:.5*(lo+hi);
  }
  if(!converged)return -1;roots[used++]=t;
 }
 return used;
}

/* Spatial sections slide over material. Unlike fixed mirrored material points,
   their chord remains parallel to the supplied eye axis even when asymmetric. */
static int belt_muscle_random_force_at(belt_body* b,const double* q,double* force,double time,double envelope){
 double ax=b->muscle_axis_x,ay=b->muscle_axis_y,norm=hypot(ax,ay);
 if(!isfinite(norm)||norm<.5)return 0;ax/=norm;ay/=norm;
 double projected[BELT_D];for(int j=0;j<BELT_D;j+=2){projected[j]=ax*q[j]+ay*q[j+1];projected[j+1]=-ay*q[j]+ax*q[j+1];}
 double bounds[4],foot;belt_bounds(projected,bounds,&foot);
 double extrema[64],cuts[66];int ne=belt_roots(projected,1,1,0,extrema),nc=0;cuts[nc++]=0;
 for(int i=0;i<ne;++i)if(extrema[i]>1e-9&&extrema[i]<2*BELT_PI-1e-9)cuts[nc++]=extrema[i];cuts[nc++]=2*BELT_PI;
 double low=bounds[1]+.125*(bounds[3]-bounds[1]),high=bounds[3]-.125*(bounds[3]-bounds[1]);
 double spacing=.08*BELT_PI*b->radius,center=projected[35],weights[25],sum=0;
 for(int r=0;r<25;++r){double h=center+(r-12)*spacing;weights[r]=belt_muscle_smooth(fmin(h-low,high-h)/(.5*spacing));sum+=weights[r];}
 if(!isfinite(sum)||sum<=0)return 0;
 b->muscle_diag[25]=(double)b->muscle_seed;b->muscle_diag[26]=ax;b->muscle_diag[27]=ay;
 b->muscle_diag[28]=spacing;b->muscle_diag[29]=low;b->muscle_diag[30]=high;
 double minimum_weight=1,torque=0;int pairs=0;
 for(int r=0;r<25;++r){
  if(weights[r]<=0)continue;
  double h=center+(r-12)*spacing,roots[64],xs[64];int nr=belt_muscle_section_roots(projected,cuts,nc,h,roots);
  if(nr<2||nr%2)return 0;
  for(int i=0;i<nr;++i)xs[i]=belt_eval(projected,roots[i],0).x;
  for(int i=1;i<nr;++i){double x=xs[i],t=roots[i];int j=i;while(j>0&&xs[j-1]>x){xs[j]=xs[j-1];roots[j]=roots[j-1];--j;}xs[j]=x;roots[j]=t;}
  int selected=-1;for(int i=0;i+1<nr;i+=2)if(xs[i]<projected[34]&&xs[i+1]>projected[34]){if(selected>=0)return 0;selected=i;}
  if(selected<0)return 0;
  double a=roots[selected],z=roots[selected+1],ba[BELT_B],bz[BELT_B];belt_basis(a,0,ba);belt_basis(z,0,bz);
  belt_v pa=belt_eval_basis(q,ba),pz=belt_eval_basis(q,bz),d=belt_sub(pz,pa);double length=hypot(d.x,d.y);
  if(!isfinite(length)||length<.05*b->radius||fabs(-ay*d.x+ax*d.y)>1e-8*b->radius)return 0;
  double ground_weight=belt_muscle_smooth((fmin(pa.y,pz.y)-b->cfg.ground_height-.03*b->radius)/(.6*b->radius));
  double pulse=.5+.5*sin(2*BELT_PI*3.2*time-(r-12)*1.4);
  /* Horizontal sections deform more efficiently than the old diagonal fibres;
     half of their eight-pair budget keeps the unchanged deformation guard. */
  double tension=b->muscle_strength*b->cfg.horizontal_force*.25*envelope*pulse*ground_weight*belt_muscle_random_gain(b->muscle_seed,r-12,time)*4*weights[r]/sum;
  belt_v pull=belt_mul(tension/length,d);
  for(int j=0;j<BELT_B;++j){force[2*j]+=(ba[j]-bz[j])*pull.x;force[2*j+1]+=(ba[j]-bz[j])*pull.y;}
  belt_force_ledger_add(b,pa.x,pa.y,pull.x,pull.y,6,a);belt_force_ledger_add(b,pz.x,pz.y,-pull.x,-pull.y,6,z);
  b->muscle_diag[11]=fmax(b->muscle_diag[11],tension);if(pairs<8)b->muscle_diag[16+pairs]=tension;
  minimum_weight=fmin(minimum_weight,ground_weight);++pairs;
 }
 for(int j=0;j<BELT_D;j+=2)torque+=q[j]*force[j+1]-q[j+1]*force[j];
 b->muscle_diag[5]++;b->muscle_diag[6]=pairs;b->muscle_diag[7]=force[0]+force[34];b->muscle_diag[8]=force[1]+force[35];
 b->muscle_diag[9]=fmax(b->muscle_diag[9],fabs(torque));b->muscle_diag[12]=minimum_weight;
 return 1;
}
#endif

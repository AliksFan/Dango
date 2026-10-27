#ifndef DANGO_BELT_MUSCLE_H
#define DANGO_BELT_MUSCLE_H

static double belt_muscle_smooth(double x){x=fmax(0,fmin(1,x));return x*x*(3-2*x);}

/* Eight contractile fibres between body parts. Each equal/opposite pair
   acts along its current chord: zero resultant AND zero torque before contact.
   No world-space target, pinned foot, velocity edit or contour edit is used. */
static int belt_muscle_force_at(belt_body* b,const double* q,double* force,double time){
 double envelope=belt_muscle_smooth(time/.10)*belt_muscle_smooth((.75-time)/.18);
 b->muscle_diag[13]=envelope;if(envelope<=0||b->muscle_strength<=0)return 1;
 double torque=0,minimum_weight=1;int pairs=0;
 for(int bank=0;bank<2;++bank)for(int i=0;i<4;++i){
  double a=(.55+.04*i)*BELT_PI,z=(1.85+.04*i)*BELT_PI;
  /* A small material shift softens the upper-side bulge, preserving the burst. */
  if(b->muscle_profile==1)a=(.57+.04*i)*BELT_PI;
  if(bank){a=BELT_PI-a;z=BELT_PI-z;}
  double ba[BELT_B],bz[BELT_B];belt_basis(a,0,ba);belt_basis(z,0,bz);
  belt_v pa=belt_eval_basis(q,ba),pz=belt_eval_basis(q,bz),d=belt_sub(pz,pa);double length=hypot(d.x,d.y);
  if(!isfinite(length)||length<.05*b->radius)return 0;
  /* Attenuate the entire pair near actual ground, preserving pair balance. */
  double height=fmin(pa.y,pz.y)-b->cfg.ground_height;
  double weight=belt_muscle_smooth((height-.03*b->radius)/(.6*b->radius));
  double activation=.5+.5*sin(2*BELT_PI*3.2*time-i*1.4);
  double tension=b->muscle_strength*b->cfg.horizontal_force*.25*envelope*activation*weight;
  belt_v pull=belt_mul(tension/length,d);
  for(int j=0;j<BELT_B;++j){force[2*j]+=(ba[j]-bz[j])*pull.x;force[2*j+1]+=(ba[j]-bz[j])*pull.y;}
  belt_force_ledger_add(b,pa.x,pa.y,pull.x,pull.y,6,a);
  belt_force_ledger_add(b,pz.x,pz.y,-pull.x,-pull.y,6,z);
  b->muscle_diag[11]=fmax(b->muscle_diag[11],tension);b->muscle_diag[16+bank*4+i]=tension;
  minimum_weight=fmin(minimum_weight,weight);pairs++;
 }
 for(int j=0;j<BELT_D;j+=2)torque+=q[j]*force[j+1]-q[j+1]*force[j];
 b->muscle_diag[5]++;b->muscle_diag[6]=pairs;b->muscle_diag[7]=force[0]+force[34];b->muscle_diag[8]=force[1]+force[35];
 b->muscle_diag[9]=fmax(b->muscle_diag[9],fabs(torque));b->muscle_diag[12]=minimum_weight;
 return 1;
}

/* Follower tension is evaluated at the implicitly accepted geometry, keeping
   each pair central as its attachment points move during the substep. Trial
   solves restart from the same state; only the converged solve/ledger survives. */
static int belt_integrate_muscles(belt_body* b,double h,const double* external,double time){
 belt_body initial=*b;double guess[BELT_D];memcpy(guess,b->q,sizeof guess);
 double work=initial.guard[4],calls=initial.guard[5],depth=initial.guard[6],leaves=initial.guard[7],refusals=initial.guard[8];
 for(int iteration=0;iteration<10;++iteration){
  *b=initial;double muscle[BELT_D]={0},total[BELT_D];
  /* Discarded pose trials still consume the same frame's guard budget. */
  b->guard[4]=work;b->guard[5]=calls;b->guard[6]=depth;b->guard[7]=leaves;b->guard[8]=refusals;
  if(!belt_muscle_force_at(b,guess,muscle,time))return 0;
  for(int j=0;j<BELT_D;++j)total[j]=external[j]+muscle[j];
  if(!belt_integrate(b,h,total))return 0;
  work=b->guard[4];calls=b->guard[5];depth=b->guard[6];leaves=b->guard[7];refusals=b->guard[8];
  belt_body probe=*b;double next[BELT_D]={0},error=0,maximum=0;
  if(!belt_muscle_force_at(&probe,b->q,next,time))return 0;
  for(int j=0;j<BELT_D;++j){error=fmax(error,fabs(next[j]-muscle[j]));maximum=fmax(maximum,fabs(next[j]));}
  if(error<1e-9*(1+maximum)){
   for(int j=0;j<BELT_D;++j)b->muscle_diag[10]+=muscle[j]*(b->q[j]-initial.q[j]);
   b->muscle_diag[14]+=iteration+1;b->muscle_diag[15]=fmax(b->muscle_diag[15],error);return 1;
  }
  memcpy(guess,b->q,sizeof guess);
 }
 return 0;
}
#endif

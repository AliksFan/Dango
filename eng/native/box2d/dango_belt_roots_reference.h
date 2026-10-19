/* Frozen pre-chart-cache belt_roots. Only the function name differs. */
static int belt_roots_reference(const double* q,int axis,int derivative,double level,double* roots){
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

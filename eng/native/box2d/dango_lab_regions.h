#ifndef DANGO_LAB_REGIONS_H
#define DANGO_LAB_REGIONS_H
/* Physical positions only. q is precisely the radially expanded render control
   polygon; extrema are analytical on each closed quadratic B-spline arc. */
static bool dango_region_bounds(const b2Vec2* pos,float radius,float* bounds)
{
    b2Vec2 q[16];
    for(int i=0;i<16;++i) {
        b2Vec2 d=b2Sub(pos[i+1],pos[0]);float length=b2Length(d);
        if(!isfinite(length)) return false;
        q[i]=length>.0001f ? b2Add(pos[i+1],b2MulSV(radius/length,d)) : pos[i+1];
    }
    bounds[0]=bounds[1]=INFINITY;bounds[2]=bounds[3]=-INFINITY;
    for(int i=0;i<16;++i) {
        b2Vec2 a=b2MulSV(.5f,b2Add(b2Sub(q[(i+15)%16],b2MulSV(2,q[i])),q[(i+1)%16]));
        b2Vec2 b=b2Sub(q[i],q[(i+15)%16]),c=b2MulSV(.5f,b2Add(q[(i+15)%16],q[i]));
        float ts[4]={0,1,-1,-1};
        if(fabsf(a.x)>1e-12f)ts[2]=-b.x/(2*a.x);
        if(fabsf(a.y)>1e-12f)ts[3]=-b.y/(2*a.y);
        for(int k=0;k<4;++k)if(ts[k]>=0 && ts[k]<=1) {
            b2Vec2 p=b2Add(c,b2MulSV(ts[k],b2Add(b,b2MulSV(ts[k],a))));
            bounds[0]=fminf(bounds[0],p.x);bounds[1]=fminf(bounds[1],p.y);
            bounds[2]=fmaxf(bounds[2],p.x);bounds[3]=fmaxf(bounds[3],p.y);
        }
    }
    return bounds[2]>bounds[0] && bounds[3]>bounds[1];
}
/* direction -1 left, +1 right, 0 upper third. Point mode selects the
   closest node inside the region to its rectangle center; strict < keeps ties. */
static unsigned dango_region_select(const b2Vec2* pos,const float* b,int direction,bool point)
{
    float x0=b[0],y0=b[1],x1=b[2],y1=b[3];
#if defined(DANGO_LAB_HALF)
    (void)point;
    if(direction<0)x1=(x0+x1)*.5f;
    else if(direction>0)x0=(x0+x1)*.5f;
    else y0=(y0+y1)*.5f;
#else
    if(direction<0)x1=x0+(x1-x0)/3;
    else if(direction>0)x0=x1-(x1-x0)/3;
    else y0=y1-(y1-y0)/3;
#endif
    unsigned mask=0;int nearest=-1;double best=INFINITY;
    for(int i=0;i<16;++i) {
        b2Vec2 p=pos[i+1];
        if(p.x<x0 || p.x>x1 || p.y<y0 || p.y>y1)continue;
        mask|=1u<<i;
        double dx=p.x-(x0+x1)*.5,dy=p.y-(y0+y1)*.5,d=dx*dx+dy*dy;
        if(d<best){best=d;nearest=i;}
    }
#if defined(DANGO_LAB_HALF)
    return mask;
#else
    return point && nearest>=0 ? 1u<<nearest : mask;
#endif
}
static int dango_region_apply(const b2BodyId* ids,const b2Vec2* pos,const double* mass,
    unsigned mask,b2Vec2 force,double cx,double cy,float* stats,int pointOffset)
{
    double sum=0;for(int i=0;i<16;++i)if(mask&(1u<<i))sum+=mass[i+1];
    if(sum<=0)return -497;
    double px=0,py=0;
    for(int i=0;i<16;++i)if(mask&(1u<<i)) {
        float w=(float)(mass[i+1]/sum);b2Vec2 f=b2MulSV(w,force);
        b2Body_ApplyForceToCenter(ids[i+1],f,true);
        px+=w*pos[i+1].x;py+=w*pos[i+1].y;
        stats[10]+=f.x;stats[11]+=f.y;
        stats[12]+=(float)((pos[i+1].x-cx)*f.y-(pos[i+1].y-cy)*f.x);
    }
    stats[pointOffset]=(float)px;stats[pointOffset+1]=(float)py;
    return 0;
}
#endif

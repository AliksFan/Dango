#ifndef DANGO_POSITION_PROJECTION_H
#define DANGO_POSITION_PROJECTION_H
#include <float.h>
#include "dango_self_constraints.h"

// Prescribed-only R1 candidate. Legacy and authority use the original solver.
typedef struct r1_stats
{
    unsigned long long radial_candidates, angle_rows, active_sets, singular_sets;
    unsigned long long corrections;
    int iterations, failure_kind, failure_node;
    double maximum_row_move;
    bool bound_valid;
} r1_stats;

// Variants: 0 candidate, 1 no floor, 2 no radial, 3 no angles,
// 4 moved core, 5 premature success. These are deliberate negative controls.
static bool r1_apply(dango_constraint_state* s, const int* ids, int count,
    double delta[3][2], r1_stats* stats)
{
    double largest=0, limit=(double)(0.02f*s->radial_length);
    for(int i=0;i<count;++i) largest=fmax(largest,hypot(delta[i][0],delta[i][1]));
    if(!isfinite(largest) || !(limit>0)) return false;
    double scale=largest>limit ? limit/largest : 1;
    // Check actual float displacement too; a rounded endpoint must not exceed
    // the original bound. No extra retry or solver iteration is performed.
    b2Vec2 next[3];
    for(int i=0;i<count;++i) {
        next[i]=(b2Vec2){(float)(s->positions[ids[i]].x+scale*delta[i][0]),
            (float)(s->positions[ids[i]].y+scale*delta[i][1])};
        double actual=hypot((double)next[i].x-s->positions[ids[i]].x,
            (double)next[i].y-s->positions[ids[i]].y);
        stats->maximum_row_move=fmax(stats->maximum_row_move,actual);
        if(!b2IsValidVec2(next[i]) || actual>limit) {
            stats->bound_valid=false; return false;
        }
    }
    for(int i=0;i<count;++i) s->positions[ids[i]]=next[i];
    ++stats->corrections;
    return true;
}

static bool r1_order(const dango_constraint_state* s,int n,b2Vec2 candidate)
{
    b2Vec2 core=s->positions[16],p=b2Sub(s->positions[(n+15)%16],core),
        q=b2Sub(candidate,core),r=b2Sub(s->positions[(n+1)%16],core);
    return b2Cross(p,q)>0 && b2Cross(q,r)>0;
}

static bool r1_radial(dango_constraint_state* s,int n,int variant,r1_stats* stats)
{
    if(variant==2) return true;
    b2Vec2 core=s->positions[16],p=s->positions[n],v=b2Sub(p,core);
    float length=b2Length(v),minimum=s->radial_length*s->radial_min,
        maximum=s->radial_length*s->radial_max;
    if(!isfinite(length) || length<0.00001f || s->inverse_mass[16]!=0 ||
        !(s->inverse_mass[n]>0)) return false;
    float error=length<minimum ? length-minimum : fmaxf(0,length-maximum);
    if(error==0) return true;
    if(variant==1) {
        (void)dango_constraint_distance_mode(s,16,n,minimum,maximum,true,DANGO_CONSTRAINT_CORRECT_POSITION);
        return true;
    }
    double boundary=error<0 ? minimum : maximum;
    float floor=s->ground+s->radius;
    b2Vec2 target={(float)(core.x+boundary*v.x/length),(float)(core.y+boundary*v.y/length)};
    bool found=false;
    double cost=DBL_MAX;
    if(target.y>=floor && r1_order(s,n,target)) {
        ++stats->radial_candidates; found=true;
    } else {
        double h=(double)floor-core.y, square=boundary*boundary-h*h;
        if(square>=0 && isfinite(square)) {
            double x=sqrt(square);
            for(int sign=-1;sign<=1;sign+=2) {
                b2Vec2 candidate={(float)(core.x+sign*x),floor};
                ++stats->radial_candidates;
                double c=((double)candidate.x-p.x)*((double)candidate.x-p.x)+
                    ((double)candidate.y-p.y)*((double)candidate.y-p.y);
                if(b2IsValidVec2(candidate) && r1_order(s,n,candidate) && c<cost) {
                    found=true; cost=c; target=candidate;
                }
            }
        }
    }
    if(!found) return false;
    double delta[3][2]={{(double)target.x-p.x,(double)target.y-p.y},{0,0},{0,0}};
    int ids[1]={n};
    return r1_apply(s,ids,1,delta,stats);
}

static bool r1_linear(double matrix[4][5],int count,double* result)
{
    for(int col=0;col<count;++col) {
        int pivot=col;
        for(int row=col+1;row<count;++row)
            if(fabs(matrix[row][col])>fabs(matrix[pivot][col])) pivot=row;
        if(!isfinite(matrix[pivot][col]) || fabs(matrix[pivot][col])<=1e-12) return false;
        for(int j=col;j<=count;++j) {
            double swap=matrix[col][j]; matrix[col][j]=matrix[pivot][j]; matrix[pivot][j]=swap;
        }
        double denominator=matrix[col][col];
        for(int j=col;j<=count;++j) matrix[col][j]/=denominator;
        for(int row=0;row<count;++row) if(row!=col) {
            double factor=matrix[row][col];
            for(int j=col;j<=count;++j) matrix[row][j]-=factor*matrix[col][j];
        }
    }
    for(int i=0;i<count;++i) { result[i]=matrix[i][count]; if(!isfinite(result[i])) return false; }
    return true;
}

static bool r1_angle(dango_constraint_state* s,int n,bool display,int variant,r1_stats* stats)
{
    if(variant==3) return true;
    int ids[3]={(n+15)%16,n,(n+1)%16};
    b2Vec2 p[3];
    for(int i=0;i<3;++i) p[i]=display ? dango_constraint_display_point(s,ids[i]) : s->positions[ids[i]];
    b2Vec2 e=b2Sub(p[1],p[0]),f=b2Sub(p[2],p[1]);
    float e2=b2Dot(e,e),f2=b2Dot(f,f);
    if(e2<1e-10f || f2<1e-10f || !isfinite(e2+f2)) return false;
    float turn=atan2f(b2Cross(e,f),b2Dot(e,f));
    float error=turn<0.002f ? turn-0.002f : fmaxf(0,turn-1.5687963268f);
    if(error==0) return true;
    if(variant==1) {
        (void)dango_constraint_angle_mode(s,n,display,true,DANGO_CONSTRAINT_CORRECT_POSITION);
        return true;
    }
    ++stats->angle_rows;
    b2Vec2 gradients[3]={{-e.y/e2,e.x/e2},{0,0},{-f.y/f2,f.x/f2}};
    gradients[1]=b2Neg(b2Add(gradients[0],gradients[2]));
    if(display) for(int i=0;i<3;++i) {
        b2Vec2 radial=b2Sub(s->positions[ids[i]],s->positions[16]);
        float length=b2Length(radial);
        if(length<0.00001f || !isfinite(length)) return false;
        b2Vec2 unit=b2MulSV(1/length,radial);
        b2Vec2 tangent=b2MulSub(gradients[i],b2Dot(unit,gradients[i]),unit);
        gradients[i]=b2Add(gradients[i],b2MulSV(s->radius/length,tangent));
    }
    double a[4][6]={{0}},b[4]={fabs((double)error),0,0,0},w[6];
    double sign=error<0 ? 1 : -1;
    for(int i=0;i<3;++i) {
        if(!(s->inverse_mass[ids[i]]>0) || !isfinite(s->inverse_mass[ids[i]])) return false;
        a[0][2*i]=sign*gradients[i].x; a[0][2*i+1]=sign*gradients[i].y;
        a[i+1][2*i+1]=1;
        b[i+1]=(double)(s->ground+s->radius)-s->positions[ids[i]].y;
        w[2*i]=w[2*i+1]=s->inverse_mass[ids[i]];
    }
    double best[6]={0},cost=DBL_MAX;
    bool found=false;
    for(int mask=0;mask<16;++mask) {
        ++stats->active_sets;
        int rows[4],count=0;
        for(int row=0;row<4;++row) if(mask&(1<<row)) rows[count++]=row;
        double matrix[4][5]={{0}},lambda[4]={0},d[6]={0};
        for(int i=0;i<count;++i) {
            for(int j=0;j<count;++j) for(int k=0;k<6;++k)
                matrix[i][j]+=a[rows[i]][k]*w[k]*a[rows[j]][k];
            matrix[i][count]=b[rows[i]];
        }
        if(!r1_linear(matrix,count,lambda)) { ++stats->singular_sets; continue; }
        bool valid=true;
        for(int i=0;i<count;++i) {
            if(lambda[i]<-1e-12) valid=false;
            for(int k=0;k<6;++k) d[k]+=w[k]*a[rows[i]][k]*lambda[i];
        }
        for(int row=0;row<4;++row) {
            double value=0;
            for(int k=0;k<6;++k) value+=a[row][k]*d[k];
            if(!isfinite(value) || value<b[row]-1e-12) valid=false;
        }
        double candidate_cost=0;
        for(int k=0;k<6;++k) candidate_cost+=d[k]*d[k]/(2*w[k]);
        if(valid && isfinite(candidate_cost) && candidate_cost<cost) {
            found=true; cost=candidate_cost;
            for(int k=0;k<6;++k) best[k]=d[k];
        }
    }
    if(!found) return false;
    double delta[3][2];
    for(int i=0;i<3;++i) { delta[i][0]=best[2*i]; delta[i][1]=best[2*i+1]; }
    return r1_apply(s,ids,3,delta,stats);
}

static bool r1_sweep(dango_constraint_state* s,int iteration,int variant,r1_stats* stats)
{
    for(int ordinal=0;ordinal<16;++ordinal) {
        int n=iteration%2 ? 15-ordinal : ordinal;
        stats->failure_node=n; stats->failure_kind=0;
        if(!r1_radial(s,n,variant,stats)) return false;
        stats->failure_kind=1;
        b2Vec2 perimeter_before[2]={s->positions[n],s->positions[(n+1)%16]};
        float residual=dango_constraint_distance_mode(s,n,(n+1)%16,
            s->perimeter_length*s->perimeter_min,s->perimeter_length*s->perimeter_max,true,
            DANGO_CONSTRAINT_CORRECT_POSITION);
        if(!isfinite(residual)) return false;
        for(int endpoint=0;endpoint<2;++endpoint) {
            b2Vec2 after=s->positions[(n+endpoint)%16],before=perimeter_before[endpoint];
            double move=hypot((double)after.x-before.x,(double)after.y-before.y);
            stats->maximum_row_move=fmax(stats->maximum_row_move,move);
            if(!isfinite(move) || move>(double)(0.02f*s->radial_length)) {
                stats->bound_valid=false; return false;
            }
        }
        for(int display=0;display<2;++display) {
            stats->failure_kind=2+display;
            if(!r1_angle(s,n,display!=0,variant,stats)) return false;
        }
    }
    if(variant!=1) for(int n=0;n<16;++n)
        s->positions[n].y=fmaxf(s->positions[n].y,s->ground+s->radius);
    stats->failure_kind=-1; stats->failure_node=-1;
    return true;
}
// Keep each original velocity row's pre-position gradient and closing direction.
// Position-only operations above cannot turn geometry corrections into energy.
static bool r1_prescribed_solve(dango_constraint_state* s)
{
    r1_stats stats={0}; stats.bound_valid=true;
    for(int iteration=0;iteration<96;++iteration) {
        for(int ordinal=0;ordinal<16;++ordinal) {
            int n=iteration%2 ? 15-ordinal : ordinal;
            float radial=dango_constraint_distance_mode(s,16,n,s->radial_length*s->radial_min,
                s->radial_length*s->radial_max,true,DANGO_CONSTRAINT_CORRECT_VELOCITY);
            if(!isfinite(radial) || !r1_radial(s,n,0,&stats)) return false;
            float perimeter=dango_constraint_distance_mode(s,n,(n+1)%16,
                s->perimeter_length*s->perimeter_min,s->perimeter_length*s->perimeter_max,true,
                DANGO_CONSTRAINT_CORRECT_BOTH);
            if(!isfinite(perimeter)) return false;
            for(int display=0;display<2;++display) {
                float angle=dango_constraint_angle_mode(s,n,display!=0,true,DANGO_CONSTRAINT_CORRECT_VELOCITY);
                if(!isfinite(angle) || !r1_angle(s,n,display!=0,0,&stats)) return false;
            }
        }
        for(int n=0;n<16;++n) {
            float penetration=fmaxf(0,s->ground+s->radius-s->positions[n].y);
            if(penetration>0) {
                s->positions[n].y+=penetration;
                s->velocities[n].y=fmaxf(0,s->velocities[n].y);
            }
        }
        if(dango_constraint_sweep(s,false)<=DANGO_CONSTRAINT_TOLERANCE) return true;
    }
    return false;
}
#endif

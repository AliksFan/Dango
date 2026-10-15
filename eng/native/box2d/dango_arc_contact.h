#ifndef DANGO_ARC_CONTACT_H
#define DANGO_ARC_CONTACT_H
/* Two stable row slots per span: its start seam and its analytic local
   minimum. A shared seam occurs once. A horizontal span needs both endpoints
   (the next span owns its end), rather than an arbitrary global winning foot. */
static bool dango_arc_contact_point(const dango_constraint_state* s,int row,dango_envelope_point* out)
{
    b2Vec2 q[16],u[16];float length[16];
    for(int i=0;i<16;++i) {
        b2Vec2 d=b2Sub(s->positions[i],s->positions[16]);length[i]=b2Length(d);
        if(!isfinite(length[i]) || length[i]<1e-8f)return false;
        u[i]=b2MulSV(1/length[i],d);q[i]=b2Add(s->positions[i],b2MulSV(s->radius,u[i]));
    }
    int segment=row/2,ids[3]={(segment+15)%16,segment,(segment+1)%16};
    b2Vec2 a=b2MulSV(.5f,b2Add(b2Sub(q[ids[0]],b2MulSV(2,q[ids[1]])),q[ids[2]]));
    b2Vec2 b=b2Sub(q[ids[1]],q[ids[0]]);float t=0;
    if(row%2) {
        if(a.y<=1e-10f)return false;
        t=-b.y/(2*a.y);if(t<=1e-6f || t>=1-1e-6f)return false;
    }
    float weights[3]={.5f*(1-t)*(1-t),.5f+t-t*t,.5f*t*t};
    *out=(dango_envelope_point){0};out->segment=segment;out->t=t;
    for(int j=0;j<3;++j) {
        int i=ids[j];float f=s->radius/length[i],w=weights[j];
        out->point=b2Add(out->point,b2MulSV(w,q[i]));
        b2Vec2 gn={-w*f*u[i].x*u[i].y,w*(1+f*(1-u[i].y*u[i].y))};
        b2Vec2 gt={w*(1+f*(1-u[i].x*u[i].x)),-w*f*u[i].x*u[i].y};
        out->normal[i]=b2Add(out->normal[i],gn);out->tangent[i]=b2Add(out->tangent[i],gt);
        out->normal[16]=b2Add(out->normal[16],b2Sub((b2Vec2){0,w},gn));
        out->tangent[16]=b2Add(out->tangent[16],b2Sub((b2Vec2){w,0},gt));
    }
    return true;
}
static float dango_arc_contact(dango_constraint_state* s,bool correct,dango_constraint_correction_mode mode)
{
    float residual=0,tolerance=DANGO_CONSTRAINT_TOLERANCE*s->radial_length;
    if(s->envelope_stats) {
        dango_envelope_point foot;if(!dango_envelope_geometry(s->positions,s->radius,&foot))return INFINITY;
        s->envelope_stats[0]=foot.point.x;s->envelope_stats[1]=foot.point.y;s->envelope_stats[2]=foot.point.y-s->ground;
        s->envelope_stats[5]=s->envelope_friction;
    }
    int contacts=0;float minGap=INFINITY;
    for(int row=0;row<32;++row) {
        dango_envelope_point p;if(!dango_arc_contact_point(s,row,&p))continue;
        float gap=p.point.y-s->ground,penetration=fmaxf(0,-gap);minGap=fminf(minGap,gap);
        residual=fmaxf(residual,penetration/s->radial_length);
        if(gap>tolerance)continue;
        ++contacts;if(!correct)continue;
        float kn=0,kt=0,vn=0,vt=0,knt=0;
        for(int i=0;i<17;++i) {kn+=s->inverse_mass[i]*b2LengthSquared(p.normal[i]);kt+=s->inverse_mass[i]*b2LengthSquared(p.tangent[i]);knt+=s->inverse_mass[i]*b2Dot(p.normal[i],p.tangent[i]);vn+=b2Dot(p.normal[i],s->velocities[i]);vt+=b2Dot(p.tangent[i],s->velocities[i]);}
        if(kn<=0 || kt<=0)return INFINITY;
        if(mode!=DANGO_CONSTRAINT_CORRECT_POSITION) {
            float before=dango_envelope_energy(s),previous=s->arc_normal[row],previousT=s->arc_tangent[row];
            double mu=s->envelope_friction,bn=vn-kn*previous-knt*previousT,bt=vt-knt*previous-kt*previousT;
            double candidates[4][2]={{0}},det=(double)kn*kt-(double)knt*knt;
            bool valid[4]={true,false,true,true};
            if(det>1e-12){candidates[1][0]=(-kt*bn+knt*bt)/det;candidates[1][1]=(knt*bn-kn*bt)/det;
                valid[1]=candidates[1][0]>=0 && fabs(candidates[1][1])<=mu*candidates[1][0];}
            for(int side=0;side<2;++side) {
                double direction=side==0 ? -mu : mu,denominator=kn+2*direction*knt+direction*direction*kt;
                candidates[side+2][0]=fmax(0,-(bn+direction*bt)/denominator);
                candidates[side+2][1]=direction*candidates[side+2][0];
            }
            double bestN=previous,bestT=previousT,bestChange=0;
            for(int candidate=0;candidate<4;++candidate)if(valid[candidate]) {
                double dn=candidates[candidate][0]-previous,dt=candidates[candidate][1]-previousT;
                double change=dn*vn+dt*vt+.5*(kn*dn*dn+2*knt*dn*dt+kt*dt*dt);
                if(change<bestChange){bestChange=change;bestN=candidates[candidate][0];bestT=candidates[candidate][1];}
            }
            float next=(float)bestN,nextT=(float)bestT,impulse=next-previous,jt=nextT-previousT;
            s->arc_normal[row]=next;s->arc_tangent[row]=nextT;
            /* The Coulomb cone and off-diagonal effective mass are solved as
               one atomic contact block. There is no independent friction step. */
            if(impulse!=0)dango_envelope_impulse(s,p.normal,impulse,p.point,false);
            if(jt!=0)dango_envelope_impulse(s,p.tangent,jt,p.point,true);
            float after=dango_envelope_energy(s);
            if(s->arc_stats) {s->arc_stats[11]+=after-before;s->arc_stats[17]+=before;s->arc_stats[18]+=after;
                s->arc_stats[24]=fmaxf(s->arc_stats[24],after-before);s->arc_stats[25]=fmaxf(s->arc_stats[25],fabsf(nextT)-s->envelope_friction*next);}
            if(s->envelope_stats) {s->envelope_stats[11]+=before;s->envelope_stats[12]+=after;
                s->envelope_stats[21]=vn;s->envelope_stats[22]=vn+kn*impulse+knt*jt;
                s->envelope_stats[23]=vt;s->envelope_stats[24]=vt+knt*impulse+kt*jt;}
            if(after>before+fmaxf(1e-7f,1e-5f*before)) {
                if(s->arc_stats){s->arc_stats[27]=3;s->arc_stats[28]=after-before;}
                return INFINITY;
            }
        }
        if(mode!=DANGO_CONSTRAINT_CORRECT_VELOCITY && penetration>0) {
            float lambda=penetration/kn,norm=0;
            for(int i=0;i<17;++i) {
                b2Vec2 move=b2MulSV(s->inverse_mass[i]*lambda,p.normal[i]);s->positions[i]=b2Add(s->positions[i],move);
                float amount=b2Length(move);norm+=amount*amount;
                if(s->envelope_stats)s->envelope_stats[7]=fmaxf(s->envelope_stats[7],amount);
                if(s->arc_stats)s->arc_stats[12]=fmaxf(s->arc_stats[12],amount);
            }
            if(s->envelope_stats)s->envelope_stats[6]+=sqrtf(norm);
        }
    }
    if(s->arc_stats) {s->arc_stats[7]=(float)contacts;s->arc_stats[23]=fmaxf(s->arc_stats[23],fmaxf(0,-minGap));
        float normal=0,tangent=0;for(int row=0;row<32;++row){normal+=s->arc_normal[row];tangent+=fabsf(s->arc_tangent[row]);}
        s->arc_stats[9]=normal;s->arc_stats[10]=tangent;}
    if(s->envelope_stats) {s->envelope_stats[10]=(float)contacts;s->envelope_stats[19]=fminf(s->envelope_stats[19],minGap);s->envelope_stats[31]=2;}
    return residual;
}
#endif

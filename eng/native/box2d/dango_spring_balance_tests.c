// Isolated fixed-dependency diagnostic. Not part of the production DLL.
#include <math.h>
#include <stdio.h>
#include <string.h>
#include "box2d/box2d.h"
#define CHECK(c) do { if (!(c)) { fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#c); return 1; } } while (0)

int main(int argc,char** argv)
{
    bool disabled=argc==2 && strcmp(argv[1],"disabled")==0;
    const float ma=.12f,mb=.015f,hertz=5.2f,load=.01f,rest=.68f,dt=1.0f/240;
    const float omega=6.28318530718f*hertz;
    const float expected=rest+load/((ma*mb/(ma+mb))*omega*omega);
    bool passed=true;
    for (int constrained=0;constrained<2;++constrained) for (int cold=0;cold<2;++cold)
    {
        const float cap=rest+.0004f;
        float max_correction=0,max_speed_correction=0;
        b2WorldDef wd=b2DefaultWorldDef(); wd.gravity=b2Vec2_zero; wd.enableSleep=false;
        b2WorldId world=b2CreateWorld(&wd);
        b2BodyDef bd=b2DefaultBodyDef(); bd.type=b2_dynamicBody; bd.fixedRotation=true;
        b2BodyId a=b2CreateBody(world,&bd); bd.position.x=rest;
        b2BodyId b=b2CreateBody(world,&bd);
        b2Circle circle={b2Vec2_zero,.045f};
        b2ShapeDef sd=b2DefaultShapeDef(); sd.filter.maskBits=0;
        b2CreateCircleShape(a,&sd,&circle); b2CreateCircleShape(b,&sd,&circle);
        b2MassData am=b2Body_GetMassData(a),bm=b2Body_GetMassData(b); am.mass=ma;bm.mass=mb;
        b2Body_SetMassData(a,am); b2Body_SetMassData(b,bm);
        b2DistanceJointDef jd=b2DefaultDistanceJointDef(); jd.bodyIdA=a;jd.bodyIdB=b;
        jd.length=rest;jd.enableSpring=!disabled;jd.hertz=hertz;jd.dampingRatio=1.85f;
        jd.enableLimit=true;jd.minLength=rest*.65f;jd.maxLength=rest*1.22f;
        b2JointId joint=b2CreateDistanceJoint(world,&jd);
        for (int step=0;step<36000;++step)
        {
            if (cold) b2DistanceJoint_SetLengthRange(joint,jd.minLength,jd.maxLength);
            b2Body_ApplyForceToCenter(a,(b2Vec2){-load,0},true);
            b2Body_ApplyForceToCenter(b,(b2Vec2){load,0},true);
            b2World_Step(world,dt,1);
            if (constrained)
            {
                b2Vec2 pa=b2Body_GetPosition(a),pb=b2Body_GetPosition(b);
                float excess=pb.x-pa.x-cap;
                if (excess>0)
                {
                    pa.x+=excess*mb/(ma+mb);pb.x-=excess*ma/(ma+mb);
                    b2Body_SetTransform(a,pa,b2Rot_identity);b2Body_SetTransform(b,pb,b2Rot_identity);
                    b2Vec2 va=b2Body_GetLinearVelocity(a),vb=b2Body_GetLinearVelocity(b);
                    float relative=fmaxf(0,vb.x-va.x);
                    va.x+=relative*mb/(ma+mb);vb.x-=relative*ma/(ma+mb);
                    b2Body_SetLinearVelocity(a,va);b2Body_SetLinearVelocity(b,vb);
                    if (step>=35400) { max_correction=fmaxf(max_correction,excess);max_speed_correction=fmaxf(max_speed_correction,relative); }
                }
            }
        }
        float length=b2Distance(b2Body_GetPosition(a),b2Body_GetPosition(b));
        float speed=b2Length(b2Sub(b2Body_GetLinearVelocity(b),b2Body_GetLinearVelocity(a)));
        float target=constrained ? cap : expected;
        bool valid=isfinite(length) && fabsf(length-target)<2e-5f && speed<.0001f;
        printf("spring_balance constrained=%d cold=%d disabled=%d expected=%.9g length=%.9g speed=%.9g position_correction=%.9g speed_correction=%.9g pass=%d\n",
            constrained,cold,disabled,(double)target,(double)length,(double)speed,(double)max_correction,(double)max_speed_correction,valid);
        passed=passed && valid;
        b2DestroyWorld(world);
    }
    CHECK(passed);
    return 0;
}

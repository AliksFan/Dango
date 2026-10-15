#include <stdio.h>
#include "dango_ground_contact_evaluation.h"
#define CHECK(c) do { if (!(c)) { fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#c); return 1; } } while (0)

int main(void)
{
    float x=.001f,lambda=0;
    ground_friction_project(&x,0,100,.01f,.72f,.01f,&lambda);
    CHECK(fabsf(x)<1e-7f);
    CHECK(fabsf(lambda)<=.72f*.01f*.01f);
    x=.1f; lambda=0;
    ground_friction_project(&x,0,100,.01f,.72f,.01f,&lambda);
    CHECK(x>.09f && x<.1f);
    CHECK(fabsf(lambda+.000072f)<1e-8f);
    CHECK(ground_friction_project(&x,0,100,.01f,.72f,.01f,&lambda)<1e-8f);
    x=.1f; lambda=0;
    ground_friction_project(&x,0,100,0,.72f,.01f,&lambda);
    CHECK(x==.1f && lambda==0);
    ground_friction_project(&x,0,100,.01f,0,.01f,&lambda);
    CHECK(x==.1f && lambda==0);
    for (int sign=-1;sign<=1;sign+=2)
    {
        float velocity=(float)sign*2;
        float result=ground_friction_velocity(velocity,100,.01f,.72f);
        CHECK(result*velocity>=0 && result*result<=velocity*velocity);
        CHECK(fabsf(result-velocity)<=.720001f && fabsf(result)<fabsf(velocity));
        CHECK(ground_friction_velocity(velocity,100,0,.72f)==velocity);
        CHECK(ground_friction_velocity(velocity,100,.01f,0)==velocity);
    }
    CHECK(ground_friction_velocity(.1f,100,.01f,.72f)==0);
    dango_constraint_state s={0};
    s.radial_length=.68f; s.perimeter_length=2*.68f*sinf(3.14159265f/16);
    s.radial_min=.65f; s.radial_max=1.22f; s.perimeter_min=.73f; s.perimeter_max=1.07f;
    s.radius=.045f; s.ground=-.6f;
    for (int i=0;i<17;++i)
    {
        s.inverse_mass[i]=i==16 ? 1/.12f : 1/.015f;
        if (i<16) s.positions[i]=(b2Vec2){.68f*cosf((float)i*6.2831853f/16),.68f*sinf((float)i*6.2831853f/16)};
    }
    CHECK(dango_constraint_solve(&s));
    for (int mode=0;mode<3;++mode)
    {
        dango_constraint_state test=s;
        float normal[16]={0},tangent[16]={0};
        b2Vec2 momentum_before={0},momentum_after={0};
        float energy_before=0,energy_after=0,normal_before=0;
        for (int i=0;i<17;++i)
        {
            test.velocities[i]=(b2Vec2){2,-.1f};
            momentum_before=b2MulAdd(momentum_before,1/test.inverse_mass[i],test.velocities[i]);
            energy_before+=.5f*b2LengthSquared(test.velocities[i])/test.inverse_mass[i];
            if (i<16 && test.positions[i].y-test.radius-test.ground<.005f && mode!=2) normal[i]=.01f;
            if (i<16) normal_before+=normal[i];
        }
        float friction=mode==1 ? 0 : .72f;
        CHECK(ground_velocity_solve(&test,normal,friction,tangent,NULL));
        float sum_tangent=0,sum_normal=0;
        for (int i=0;i<17;++i)
        {
            CHECK(b2Distance(test.positions[i],s.positions[i])==0);
            momentum_after=b2MulAdd(momentum_after,1/test.inverse_mass[i],test.velocities[i]);
            energy_after+=.5f*b2LengthSquared(test.velocities[i])/test.inverse_mass[i];
            if (i<16)
            {
                CHECK(fabsf(tangent[i])<=friction*normal[i]+1e-7f);
                sum_tangent+=tangent[i]; sum_normal+=normal[i];
            }
        }
        CHECK(energy_after<=energy_before+1e-6f);
        CHECK(fabsf(momentum_after.x-momentum_before.x-sum_tangent)<1e-6f);
        CHECK(fabsf(momentum_after.y-momentum_before.y-(sum_normal-normal_before))<1e-6f);
        if (mode!=0) CHECK(fabsf(momentum_after.x-momentum_before.x)<1e-6f);
        else CHECK(momentum_after.x>0 && momentum_after.x<momentum_before.x);
    }
    puts("PASS: bounded positional friction, saturation/sliding, zero support/friction, dissipative velocity impulse");
    puts("PASS: full rig velocity cone, energy, position invariance, plane reaction and zero-friction/no-support horizontal momentum");
    return 0;
}

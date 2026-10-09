/* Isolated native admission executable. No fixture hooks enter the module ABI. */
#include <stdio.h>
#include <string.h>
#include "dango_physics_module.h"
#include "dango_round_ball.h"
#include "dango_round_ball_oracle.h"

#ifndef DANGO_ROUND_BALL_CONTRACT_TESTS
#error Round-ball admission tests require the isolated test target.
#endif

static int rb_cases,rb_checks,rb_failures;
static const double rb_pi=3.1415926535897932384626433832795;

static void rb_check(bool condition,const char* group,const char* field,double actual,double expected)
{
    ++rb_checks;
    if (!condition)
    {
        ++rb_failures;
        fprintf(stderr,"FAIL group=%s field=%s actual=%.17g expected=%.17g\n",group,field,actual,expected);
    }
}
static void rb_close(double actual,double expected,double absolute,double relative,
    const char* group,const char* field)
{
    rb_check(isfinite(actual) && fabs(actual-expected)<=absolute+relative*fabs(expected),
        group,field,actual,expected);
}
static void rb_analytic(double actual,double expected,const char* group,const char* field)
{ rb_close(actual,expected,1e-6,1e-5,group,field); }

static rb_oracle_vec rb_result_dango(const dango_ball_point_result* result)
{ return (rb_oracle_vec){result->dvx,result->dvy}; }
static rb_oracle_vec rb_result_ball(const dango_ball_point_result* result)
{ return (rb_oracle_vec){result->bvx,result->bvy}; }

static void rb_point_case(dango_ball_point_input input,bool floor,const char* label)
{
    ++rb_cases;
    dango_ball_point_result actual={0}; rb_oracle_contact expected={0};
    rb_oracle_vec vd={input.dvx,input.dvy},vb={input.bvx,input.bvy},n={input.nx,input.ny};
    double closing=-rb_o_dot(n,rb_o_sub(vb,rb_o_map(vd)));
    double effective_restitution=closing>=.5 ? input.restitution : 0;
    bool oracle_ok=rb_o_contact(vd,vb,input.omega,n,input.friction,effective_restitution,floor,&expected);
    rb_check(oracle_ok,label,"independent-oracle-valid",oracle_ok,1);
    int code=floor ? dango_round_ball_test_floor(&input,&actual) : dango_round_ball_test_point(&input,&actual);
    rb_check(code==0,label,"native-contact-return",code,0);
    if (!oracle_ok || code) return;
    rb_analytic(actual.knn,expected.knn,label,"Knn");
    rb_analytic(actual.knt,expected.knt,label,"Knt");
    rb_analytic(actual.ktt,expected.ktt,label,"Ktt");
    rb_analytic(actual.jn,expected.normal_impulse,label,"Jn");
    rb_analytic(actual.jt,expected.tangent_impulse,label,"Jt");
    rb_analytic(actual.dvx,expected.dango_velocity.x,label,"Dango-vx");
    rb_analytic(actual.dvy,expected.dango_velocity.y,label,"Dango-vy");
    rb_analytic(actual.bvx,expected.ball_velocity.x,label,"ball-vx");
    rb_analytic(actual.bvy,expected.ball_velocity.y,label,"ball-vy");
    rb_analytic(actual.omega,expected.omega,label,"ball-omega");
    rb_close(actual.vn_after,expected.normal_speed,1e-6,0,label,"normal-speed-residual");
    rb_close(actual.vt_after,expected.tangent_speed,1e-6,0,label,"tangent-speed-residual");
    rb_check(actual.jn>=-1e-12 && fabs(actual.jt)<=input.friction*actual.jn+1e-12,
        label,"Coulomb-cone",fabs(actual.jt),input.friction*actual.jn);
    double measured_energy=rb_o_energy(rb_result_dango(&actual),rb_result_ball(&actual),actual.omega,floor);
    rb_close(measured_energy,expected.energy_after,1e-7,1e-5,label,"kinetic-energy");
    rb_check(measured_energy<=expected.energy_before+1e-7,
        label,"no-unforced-energy-gain",measured_energy,expected.energy_before);
    rb_oracle_vec delta_ball=rb_o_scale(.09,rb_o_sub(rb_result_ball(&actual),vb));
    if (!floor)
    {
        rb_oracle_vec delta_dango=rb_o_scale(.36,rb_o_sub(rb_result_dango(&actual),vd));
        double reaction_error=rb_o_length(rb_o_add(delta_dango,rb_o_map(delta_ball)));
        rb_close(reaction_error,0,1e-6,0,label,"generalized-reaction");
    }
    rb_close(.0072*(actual.omega-input.omega),-.4*actual.jt,1e-7,1e-5,label,"tangent-torque");
    double normal_work=actual.jn*actual.vn_before;
    double tangent_work=actual.jt*actual.vt_before;
    double quadratic=.5*(actual.knn*actual.jn*actual.jn+2*actual.knt*actual.jn*actual.jt+actual.ktt*actual.jt*actual.jt);
    rb_close(measured_energy-expected.energy_before,normal_work+tangent_work+quadratic,
        1e-7,1e-5,label,"discrete-impulse-work");
    if (input.friction==0)
    {
        rb_close(actual.omega,input.omega,1e-6,0,label,"zero-normal-ball-torque");
        double analytic_loss=-(1-effective_restitution*effective_restitution)*closing*closing/(2*expected.knn);
        rb_close(measured_energy-expected.energy_before,analytic_loss,1e-7,1e-5,label,"normal-energy-loss");
    }
}

static dango_ball_point_input rb_input(double nx,double ny)
{
    dango_ball_point_input input={0};
    input.nx=nx; input.ny=ny; input.dango_mass=.36; input.ball_mass=.09;
    input.radius=.4; input.inertia=.0072; input.dvx=.75*nx; input.dvy=ny;
    return input;
}

static void rb_analytic_contracts(void)
{
    const double normals[8][2]={{1,0},{-1,0},{0,1},{0,-1},{.6,.8},{-.6,.8},{.6,-.8},{-.6,-.8}};
    const double frictions[3]={0,.35,1}, restitutions[3]={0,.15,1};
    for (int direction=0;direction<8;++direction)
        for (int friction=0;friction<3;++friction) for (int restitution=0;restitution<3;++restitution)
        {
            char label[96]; snprintf(label,sizeof label,"A6/A7 direction=%d mu=%.2f e=%.2f",direction,frictions[friction],restitutions[restitution]);
            dango_ball_point_input input=rb_input(normals[direction][0],normals[direction][1]);
            input.friction=frictions[friction]; input.restitution=restitutions[restitution];
            rb_point_case(input,false,label);
        }
    for (int friction=0;friction<3;++friction) for (int restitution=0;restitution<3;++restitution)
    {
        char label[96]; snprintf(label,sizeof label,"A7 initial-slip mu=%.2f e=%.2f",frictions[friction],restitutions[restitution]);
        dango_ball_point_input input=rb_input(.6,.8); input.dvx=3; input.dvy=0;
        input.friction=frictions[friction]; input.restitution=restitutions[restitution];
        rb_point_case(input,false,label);
    }
    for (int direction=0;direction<8;++direction)
    {
        dango_ball_point_input input=rb_input(normals[direction][0],normals[direction][1]);
        input.omega=3; rb_point_case(input,false,"A6 normal-preserves-existing-spin");
    }
    dango_ball_point_result actual={0}; dango_ball_point_input input=rb_input(1,0);
    int code=dango_round_ball_test_point(&input,&actual); ++rb_cases;
    rb_check(code==0,"A6 hard-coded-anchor","return",code,0);
    if (!code)
    {
        rb_analytic(actual.jn,.0623076923077,"A6 hard-coded-anchor","Jn");
        rb_analytic(actual.dvx,.519230769231,"A6 hard-coded-anchor","Dango-vx");
        rb_analytic(actual.bvx,.692307692308,"A6 hard-coded-anchor","ball-vx");
    }
    input=rb_input(.6,.8); input.friction=.35; code=dango_round_ball_test_point(&input,&actual); ++rb_cases;
    rb_check(code==0,"A7 hard-coded-anchor","return",code,0);
    if (!code)
    {
        rb_analytic(actual.jn,.0683154211447,"A7 hard-coded-anchor","Jn");
        rb_analytic(actual.jt,.00188952761810,"A7 hard-coded-anchor","Jt");
        rb_analytic(actual.omega,-.104973756561,"A7 hard-coded-anchor","omega");
    }
}

static void rb_floor_contracts(void)
{
    const double frictions[3]={.45,.10,0};
    const double velocities[3]={2.0/3.0,.9,1}, omegas[3]={-5.0/3.0,-.5,0}, slides[3]={0,.7,1};
    for (int i=0;i<3;++i)
    {
        dango_ball_point_input input=rb_input(0,1); input.dvx=0; input.dvy=0;
        input.bvx=1; input.bvy=-1; input.friction=frictions[i];
        rb_point_case(input,true,"ground rolling/sliding oracle");
        dango_ball_point_result actual={0}; int code=dango_round_ball_test_floor(&input,&actual); ++rb_cases;
        rb_check(code==0,"ground hard-coded-anchor","return",code,0);
        if (!code)
        {
            rb_analytic(actual.jn,.09,"ground hard-coded-anchor","Jn");
            rb_analytic(actual.bvx,velocities[i],"ground hard-coded-anchor","vx");
            rb_analytic(actual.omega,omegas[i],"ground hard-coded-anchor","omega");
            rb_close(actual.bvx+.4*actual.omega,slides[i],1e-6,0,"ground hard-coded-anchor","slip-speed");
        }
    }
    const double speeds[3]={.499999,.5,.500001};
    const double floor_targets[3]={0,.125,.12500025},dango_targets[3]={0,.075,.07500015};
    for (int i=0;i<3;++i)
    {
        dango_ball_point_input input=rb_input(0,1); input.dvx=0; input.dvy=0;
        input.bvy=-speeds[i]; input.restitution=.25;
        rb_point_case(input,true,"A8 ground threshold");
        dango_ball_point_result actual={0}; int code=dango_round_ball_test_floor(&input,&actual); ++rb_cases;
        rb_check(code==0,"A8 ground threshold anchor","return",code,0);
        if (!code) rb_close(actual.bvy,floor_targets[i],1e-6,0,"A8 ground threshold anchor","rebound-speed");
        input.restitution=.15;
        rb_point_case(input,false,"A8 Dango threshold");
        code=dango_round_ball_test_point(&input,&actual); ++rb_cases;
        rb_check(code==0,"A8 Dango threshold anchor","return",code,0);
        if (!code) rb_close(actual.vn_after,dango_targets[i],1e-6,0,"A8 Dango threshold anchor","relative-rebound-speed");
    }
    dango_ball_point_input still=rb_input(0,1); still.dvx=0; still.dvy=0; still.friction=.45; still.restitution=.25;
    double sum=0,start=rb_o_energy((rb_oracle_vec){0,0},(rb_oracle_vec){0,0},0,true);
    for (int step=0;step<960;++step)
    {
        dango_ball_point_result actual={0}; int code=dango_round_ball_test_floor(&still,&actual);
        rb_check(code==0,"A8 unforced still contact","return",code,0);
        if (code) break;
        sum+=fabs(actual.jn)+fabs(actual.jt); still.bvx=actual.bvx; still.bvy=actual.bvy; still.omega=actual.omega;
    }
    ++rb_cases; rb_close(sum,0,1e-6,0,"A8 unforced still contact","960-step-extra-impulse");
    double finish=rb_o_energy((rb_oracle_vec){0,0},(rb_oracle_vec){still.bvx,still.bvy},still.omega,true);
    rb_check(finish<=start+1e-7,"A8 unforced still contact","960-step-energy-growth",finish,start);
    rb_close(dango_ball_restitution_target(.500001,.25,false),0,0,0,"A8 continuing-contact-target","target");
}

static void rb_pack_geometry(const dango_ball_geometry* input,float positions[34])
{
    for (int i=0;i<32;++i) positions[i]=(float)input->nodes[i];
    positions[32]=(float)input->core[0]; positions[33]=(float)input->core[1];
}
static void rb_fixture(dango_ball_geometry* geometry,double a,double b)
{
    memset(geometry,0,sizeof *geometry); geometry->node_radius=.045; geometry->ball_radius=.4;
    geometry->core[0]=.07; geometry->core[1]=-.03;
    for (int i=0;i<16;++i)
    {
        double angle=2*rb_pi*i/16;
        geometry->nodes[2*i]=(double)(float)(geometry->core[0]+a*(1+.06*cos(angle))*cos(angle));
        geometry->nodes[2*i+1]=(double)(float)(geometry->core[1]+b*sin(angle));
    }
    geometry->core[0]=(double)(float)geometry->core[0]; geometry->core[1]=(double)(float)geometry->core[1];
}
static void rb_ball_at_segment(dango_ball_geometry* geometry,int segment,double parameter,double gap)
{
    float positions[34]; rb_pack_geometry(geometry,positions); rb_oracle_mesh mesh={0};
    if (!rb_o_mesh(positions,&mesh)) return;
    int node=segment/8; double u=(double)(segment%8)/8;
    rb_oracle_vec a=rb_o_curve_point(&mesh,node,u),b=rb_o_curve_point(&mesh,node,u+1.0/8.0);
    rb_oracle_vec edge=rb_o_sub(b,a); double length=rb_o_length(edge);
    rb_oracle_vec normal={edge.y/length,-edge.x/length};
    rb_oracle_vec center=rb_o_add(rb_o_add(a,rb_o_scale(parameter,edge)),rb_o_scale(geometry->ball_radius+gap,normal));
    geometry->ball_x=center.x; geometry->ball_y=center.y;
}
static bool rb_geometry_call(const dango_ball_geometry* input,dango_ball_geometry_result* actual,const char* label)
{
    int code=dango_round_ball_test_geometry(input,actual);
    rb_check(code==0,label,"native-display-geometry-return",code,0); return code==0;
}
static void rb_geometry_oracle(const dango_ball_geometry* input,const dango_ball_geometry_result* actual,const char* label)
{
    float positions[34]; rb_pack_geometry(input,positions); rb_oracle_geometry expected={0};
    bool valid=rb_o_geometry(positions,(rb_oracle_vec){input->ball_x,input->ball_y},input->ball_radius,64,&expected);
    rb_check(valid,label,"independent-dense-curve-valid",valid,1);
    if (valid) rb_close(actual->gap,expected.gap,.001,0,label,"dense-curve-gap");
    rb_close(hypot(input->ball_x-actual->x,input->ball_y-actual->y)-input->ball_radius,
        actual->gap,1e-6,0,label,"closest-point-gap-consistency");
    rb_close(actual->nx*actual->nx+actual->ny*actual->ny,1,1e-6,0,label,"unit-normal");
    rb_check(actual->sample>=0 && actual->sample<128,label,"closest-segment-range",actual->sample,127);
    rb_check(actual->fraction>=0 && actual->fraction<=1,label,"closest-segment-parameter",actual->fraction,.37);
}

static void rb_derivatives(dango_ball_geometry input,const char* label)
{
    dango_ball_geometry_result actual={0}; if (!rb_geometry_call(&input,&actual,label)) return;
    float positions[34]; rb_pack_geometry(&input,positions); rb_oracle_mesh mesh={0};
    bool valid=rb_o_mesh(positions,&mesh);
    rb_check(valid,label,"oracle-mesh-valid",valid,1); if (!valid) return;
    rb_check(mesh.maximum-mesh.runner_up>=.02,label,"unique-maximum-margin",mesh.maximum-mesh.runner_up,.02);
    rb_check(fabs(mesh.maximum-.98)>=.02,label,"normalization-branch-margin",fabs(mesh.maximum-.98),.02);
    for (int degree=0;degree<34;++degree) for (int scale=0;scale<2;++scale)
    {
        double h=scale ? 1.0/2048.0 : 1.0/1024.0;
        dango_ball_geometry plus=input,minus=input;
        double* positive=degree<32 ? &plus.nodes[degree] : &plus.core[degree-32];
        double* negative=degree<32 ? &minus.nodes[degree] : &minus.core[degree-32];
        *positive+=h; *negative-=h;
        dango_ball_geometry_result upper={0},lower={0};
        bool upper_ok=rb_geometry_call(&plus,&upper,label),lower_ok=rb_geometry_call(&minus,&lower,label);
        if (!upper_ok || !lower_ok) continue;
        double expected=(upper.gap-lower.gap)/(2*h);
        char field[64]; snprintf(field,sizeof field,"gap-gradient[%d] h=2^-%d",degree,scale ? 11 : 10);
        rb_close(-actual.normal_gradient[degree],expected,5e-4,2e-3,label,field);
        /* Tangential impulses act on the material point, so freeze its native
           segment/fraction and direction while independently rebuilding it. */
        float plus_positions[34],minus_positions[34];
        rb_pack_geometry(&plus,plus_positions); rb_pack_geometry(&minus,minus_positions);
        rb_oracle_mesh plus_mesh={0},minus_mesh={0};
        bool meshes=rb_o_mesh(plus_positions,&plus_mesh) && rb_o_mesh(minus_positions,&minus_mesh);
        rb_check(meshes,label,"independent-tangent-FD-meshes",meshes,1);
        if (meshes)
        {
            int node=actual.sample/8; double u=(double)(actual.sample%8)/8;
            rb_oracle_vec plus_point=rb_o_add(rb_o_scale(1-actual.fraction,rb_o_curve_point(&plus_mesh,node,u)),
                rb_o_scale(actual.fraction,rb_o_curve_point(&plus_mesh,node,u+1.0/8.0)));
            rb_oracle_vec minus_point=rb_o_add(rb_o_scale(1-actual.fraction,rb_o_curve_point(&minus_mesh,node,u)),
                rb_o_scale(actual.fraction,rb_o_curve_point(&minus_mesh,node,u+1.0/8.0)));
            rb_oracle_vec tangent={-actual.ny,actual.nx};
            double tangent_expected=rb_o_dot(tangent,rb_o_sub(plus_point,minus_point))/(2*h);
            snprintf(field,sizeof field,"material-tangent-gradient[%d] h=2^-%d",degree,scale ? 11 : 10);
            rb_close(actual.tangent_gradient[degree],tangent_expected,5e-4,2e-3,label,field);
        }
    }
    ++rb_cases;
}

static void rb_geometric_contracts(void)
{
    for (int fixture=0;fixture<3;++fixture)
    {
        dango_ball_geometry input;
        rb_fixture(&input,fixture==0 ? .68 : fixture==1 ? 1.10 : .935/1.06,fixture==1 ? .82 : .68);
        const char* label=fixture==0 ? "A4/A5 unnormalized geometry fixture" : fixture==1 ?
            "A4/A5 test-only strong-deformation normalized geometry" : "A4 normalization boundary fixture";
        for (int segment=0;segment<128;++segment)
        {
            rb_ball_at_segment(&input,segment,.37,.01); dango_ball_geometry_result actual={0}; ++rb_cases;
            if (rb_geometry_call(&input,&actual,label))
            {
                rb_geometry_oracle(&input,&actual,label);
                rb_check(actual.fraction>.05 && actual.fraction<.95,label,"actual-segment-interior",actual.fraction,.37);
                rb_check(actual.sample==segment,label,"constructed-segment-reached",actual.sample,segment);
                if (fixture<2) rb_check(actual.normalized==fixture,label,"normalization-branch",actual.normalized,fixture);
            }
        }
        for (int direction=0;direction<8;++direction)
        {
            float positions[34]; rb_pack_geometry(&input,positions); rb_oracle_mesh mesh={0};
            if (!rb_o_mesh(positions,&mesh)) continue;
            rb_oracle_vec core_q=rb_o_map(mesh.core); double angle=direction*rb_pi/4;
            input.ball_x=core_q.x+1.8*cos(angle); input.ball_y=core_q.y+1.8*sin(angle);
            dango_ball_geometry_result actual={0}; ++rb_cases;
            if (rb_geometry_call(&input,&actual,label)) rb_geometry_oracle(&input,&actual,label);
        }
        rb_ball_at_segment(&input,19,.37,.01);
        if (fixture<2) rb_derivatives(input,label);
        else
        {
            /* The .98 branch is nonsmooth: compare each side only within its own branch. */
            const double h=1.0/1024.0;
            for (int side=-1;side<=1;side+=2)
            {
                dango_ball_geometry inner=input,outer=input;
                inner.nodes[0]+=side*h/16; outer.nodes[0]+=side*(h+h/16);
                dango_ball_geometry_result first={0},second={0}; ++rb_cases;
                bool first_ok=rb_geometry_call(&inner,&first,label),second_ok=rb_geometry_call(&outer,&second,label);
                if (first_ok && second_ok)
                {
                    rb_check(first.normalized==(side>0),label,"one-sided-normalization-branch",first.normalized,side>0);
                    double derivative=(second.gap-first.gap)/(side*h);
                    rb_close(-first.normal_gradient[0],derivative,5e-4,2e-3,label,"one-sided-gap-gradient");
                }
            }
        }
    }
}

static dango_self_physics_config rb_original_config(void)
{
    dango_self_physics_config config={
        .node_count=16,.substep_count=4,.radial_rest_length=.68f,.top_inward_force=.5f,
        .radial_minimum_ratio=.65f,.radial_maximum_ratio=1.22f,
        .perimeter_minimum_ratio=.73f,.perimeter_maximum_ratio=1.07f,
        .radial_hertz=5.2f,.perimeter_hertz=20.1f,.radial_damping=1.85f,.perimeter_damping=1.85f,
        .area_strength=4.8f,.ground_deformation_strength=3.5f,
        .repulsion_distance=.09f,.repulsion_strength=.3f,.repulsion_maximum_force=3.5f,
        .core_exclusion_ratio=.25f,.angle_strength=10,.local_angle_balance_strength=1.7f,
        .gravity=9.8f,.core_mass=.12f,.node_mass=.015f,.drop_height=.42f,
        .ground_height=-.72f,.node_radius=.045f,.friction=.72f,.restitution=.02f,
        .contact_hertz=28,.linear_damping=1.8f,.drag_stiffness=10,.drag_damping=2.2f,.drag_maximum_force=8,
        .horizontal_force=2.4f,.maximum_horizontal_speed=3.2f,.air_control_ratio=.35f,.downward_force=2.2f,
        .jump_impulse=.42f,.jump_cooldown_seconds=.45f,.maximum_speed=12,.minimum_area=.42f
    }; return config;
}

static void rb_real_geometry_contracts(void)
{
    dango_self_physics_config config=rb_original_config(); void* handle=NULL; int entity_id=0;
    int code=dango_physics_world_create(&config,1,&handle);
    rb_check(code==0 && handle!=NULL,"A4 actual-rig geometry","create",code,0); if (code || !handle) return;
    code=dango_physics_world_add(handle,0,0,&entity_id);
    rb_check(code==0,"A4 actual-rig geometry","add",code,0);
    if (code) { dango_physics_world_destroy(handle); return; }
    for (int stage=0;stage<3;++stage)
    {
        int iterations=stage==0 ? 1 : stage==1 ? 60 : 40;
        for (int step=0;step<iterations;++step)
        {
            float controls[6]={0}; controls[1]=stage==1 ? 1.0f : 0;
            controls[3]=stage==2 ? 1.0f : 0; controls[4]=-.8f; controls[5]=1.8f;
            float states[42]={0}; int state_id=0,count=0,contacts=0,error=0;
            code=dango_physics_world_step(handle,1.0f/60,&entity_id,controls,1,&state_id,states,42,&count,&contacts,&error);
            rb_check(code==0 && error==0 && count==1,"A4 actual-rig geometry","normal-world-Step",error ? error : code,0);
            if (code || error || count!=1) break;
        }
        dango_ball_geometry input={0}; dango_ball_geometry_result actual={0};
        code=dango_round_ball_test_world_geometry(handle,entity_id,3,3,.4,&input,&actual);
        rb_check(code==0,"A4 actual-rig geometry","read-real-positions",code,0); if (code) continue;
        for (int segment=0;segment<128;++segment)
        {
            rb_ball_at_segment(&input,segment,.37,.01); ++rb_cases;
            code=dango_round_ball_test_world_geometry(handle,entity_id,input.ball_x,input.ball_y,.4,&input,&actual);
            rb_check(code==0,"A4 actual-rig geometry","same-native-display-path",code,0);
            if (!code) rb_geometry_oracle(&input,&actual,"A4 actual-rig geometry");
        }
    }
    code=dango_physics_world_destroy(handle);
    rb_check(code==0,"A4 actual-rig geometry","destroy",code,0);
}

typedef struct rb_test_world
{
    void* handle;
    int dango_id,ball_id,contacts,error;
    float frame[42];
    dango_ball_snapshot snapshot;
} rb_test_world;

static dango_ball_definition rb_definition(double x,double y)
{
    return (dango_ball_definition){.x=x,.y=y,.radius=.4,.mass=.09,
        .floor_friction=.45,.dango_friction=.35,.floor_restitution=.25,.dango_restitution=.15};
}

static bool rb_world_create_bounds(rb_test_world* world,float x,float y,float left,float right,const char* label)
{
    memset(world,0,sizeof *world); dango_self_physics_config config=rb_original_config();
    int code=dango_physics_world_create(&config,3,&world->handle);
    rb_check(code==0 && world->handle,label,"create-real-native-world",code,0);
    if (code || !world->handle) return false;
    code=dango_physics_world_set_horizontal_bounds(world->handle,left,right);
    rb_check(code==0,label,"fixed-world-boundaries",code,0);
    code=dango_physics_world_add(world->handle,x,y,&world->dango_id);
    rb_check(code==0,label,"add-original-rig",code,0); return code==0;
}

static bool rb_world_create(rb_test_world* world,float x,float y,const char* label)
{ return rb_world_create_bounds(world,x,y,-4,4,label); }

static void rb_world_destroy(rb_test_world* world,const char* label)
{
    if (world->handle)
    {
        int code=dango_physics_world_destroy(world->handle);
        rb_check(code==0,label,"release-world",code,0); world->handle=NULL;
    }
}

static bool rb_world_add_ball(rb_test_world* world,dango_ball_definition definition,const char* label)
{
    int code=dango_round_ball_test_add(world->handle,&definition,&world->ball_id);
    rb_check(code==0,label,"create-real-ball",code,0);
    if (code) return false;
    code=dango_round_ball_test_read(world->handle,&world->snapshot);
    rb_check(code==0,label,"read-created-safe-state",code,0);
    rb_check(world->snapshot.ball.enabled && world->snapshot.ball.id==world->ball_id,
        label,"created-ball-identity",world->snapshot.ball.id,world->ball_id); return code==0;
}

static bool rb_world_step(rb_test_world* world,const float controls[6],bool normal,const char* label)
{
    int state_id=0,count=0,code=dango_physics_world_step(world->handle,1.0f/60,&world->dango_id,
        controls,1,&state_id,world->frame,42,&count,&world->contacts,&world->error);
    rb_check(code==0,label,"normal-Step-entry",code,0);
    rb_check(count==1 && state_id==world->dango_id,label,"published-rig-identity",state_id,world->dango_id);
    int read_code=dango_round_ball_test_read(world->handle,&world->snapshot);
    rb_check(read_code==0,label,"read-common-successful-snapshot",read_code,0);
    if (normal) rb_check(world->error==0,label,"no-fault-latch",world->error,0);
    return code==0 && count==1 && read_code==0 && (!normal || world->error==0);
}

static void rb_world_state_contract(rb_test_world* world,const char* label,double* max_penetration)
{
    bool finite=true; for (int i=0;i<42;++i) finite=finite && isfinite(world->frame[i]);
    rb_check(finite,label,"all-original-42-slots-finite",finite,1);
    rb_check(world->frame[34]>=.42f,label,"original-area-boundary",world->frame[34],.42);
    rb_check(world->frame[37]<=12.00001f,label,"original-node-speed-boundary",world->frame[37],12);
    dango_ball_snapshot* s=&world->snapshot;
    rb_check(isfinite(s->ball.x) && isfinite(s->ball.y) && isfinite(s->ball.vx) && isfinite(s->ball.vy) &&
        isfinite(s->ball.angle) && isfinite(s->ball.omega),label,"all-ball-state-finite",s->ball.vx,0);
    rb_check(hypot(s->ball.vx,s->ball.vy)<=12 && fabs(s->ball.omega)<=40,
        label,"ball-speed-fault-boundaries",hypot(s->ball.vx,s->ball.vy),12);
    rb_check(s->ball.scene_revision==s->scene_revision && s->ball.step==s->step &&
        s->dango_contact.scene_revision==s->scene_revision && s->dango_contact.step==s->step &&
        s->floor_contact.scene_revision==s->scene_revision && s->floor_contact.step==s->step,
        label,"common-scene-and-step",(double)s->ball.step,(double)s->step);
    if (s->ball.enabled)
    {
        dango_ball_geometry geometry={0}; dango_ball_geometry_result contact={0};
        int code=dango_round_ball_test_world_geometry(world->handle,world->dango_id,s->ball.x,s->ball.y,.4,&geometry,&contact);
        rb_check(code==0,label,"recompute-real-contact-geometry",code,0);
        if (!code)
        {
            double penetration=fmax(0,-contact.gap);
            *max_penetration=fmax(*max_penetration,penetration);
            rb_check(penetration<=1.0/30,label,"q-contact-penetration-budget",penetration,1.0/30);
            if (s->dango_contact.touching)
                rb_check(contact.gap<=1.0/30,label,"touching-q-gap-budget",contact.gap,1.0/30);
        }
        double floor_gap=s->ball.y-.4-(double)(float)-.72;
        rb_check(floor_gap>=-1.0/30,label,"q-floor-penetration-budget",floor_gap,-1.0/30);
        if (s->floor_contact.touching)
            rb_check(floor_gap<=1.0/30,label,"resting-q-floor-gap-budget",floor_gap,1.0/30);
    }
}

static void rb_push_and_fall_contracts(void)
{
    for (int scenario=0;scenario<3;++scenario)
    {
        const char* label=scenario==0 ? "I1 left push" : scenario==1 ? "I1 right push" : "I1 ball fall";
        rb_test_world world; double max_penetration=0,impulse=0,first_x=0,max_speed=0;
        bool created=rb_world_create(&world,scenario==1 ? 2.0f : -2.0f,0,label);
        if (!created) { rb_world_destroy(&world,label); continue; }
        dango_ball_definition definition=rb_definition(scenario==2 ? 2 : 0,scenario==2 ? 2 : -.32);
        if (!rb_world_add_ball(&world,definition,label)) { rb_world_destroy(&world,label); continue; }
        first_x=world.snapshot.ball.x; bool dango_contact=false,floor_contact=false,moved=false;
        for (int step=0;step<900;++step)
        {
            float controls[6]={0}; if (scenario<2 && step<300) controls[0]=scenario==0 ? 1.0f : -1.0f;
            if (!rb_world_step(&world,controls,true,label)) break;
            rb_world_state_contract(&world,label,&max_penetration);
            dango_contact=dango_contact || world.snapshot.dango_contact.touching;
            floor_contact=floor_contact || world.snapshot.floor_contact.touching;
            impulse+=world.snapshot.dango_contact.normal_impulse;
            max_speed=fmax(max_speed,hypot(world.snapshot.ball.vx,world.snapshot.ball.vy));
            if (scenario<2 && (scenario==0 ? world.snapshot.ball.x-first_x : first_x-world.snapshot.ball.x)>.1) moved=true;
        }
        if (scenario<2)
        {
            rb_check(dango_contact,label,"real-contact-was-reached",dango_contact,1);
            rb_check(impulse>1e-6,label,"real-contact-impulse-not-absent",impulse,1e-6);
            rb_check(moved,label,"ball-moved-in-push-direction",moved,1);
        }
        rb_check(floor_contact,label,"real-floor-contact-was-reached",floor_contact,1);
        printf("SCENE %s max_penetration=%.17g max_ball_speed=%.17g real_dango_impulse=%.17g\n",label,max_penetration,max_speed,impulse);
        ++rb_cases; rb_world_destroy(&world,label);
    }
}

static void rb_high_speed_contracts(void)
{
    const double normals[8][2]={{1,0},{-1,0},{0,1},{0,-1},{.6,.8},{-.6,.8},{.6,-.8},{-.6,-.8}};
    for (int direction=0;direction<8;++direction)
    {
        char label[64]; snprintf(label,sizeof label,"I1/I2 direction=%d real approach",direction);
        rb_test_world world; if (!rb_world_create(&world,0,2,label)) { rb_world_destroy(&world,label); continue; }
        double nx=normals[direction][0],ny=normals[direction][1];
        dango_ball_definition definition=rb_definition(1.4*nx,2.42+1.4*ny);
        if (!rb_world_add_ball(&world,definition,label)) { rb_world_destroy(&world,label); continue; }
        int code=dango_round_ball_test_initial_velocity(world.handle,-9.4*nx,-9.4*ny,0);
        rb_check(code==0,label,"bounded-ball-initial-fixture",code,0);
        bool contacted=false,high_speed=false; double impulse=0,max_penetration=0,max_speed=0,contact_speed=0,closing_speed=0;
        for (int step=0;step<900;++step)
        {
            float controls[6]={0}; if (step<120 && direction<2) controls[0]=nx>0 ? 1.0f : -1.0f;
            if (!rb_world_step(&world,controls,true,label)) break;
            double speed=hypot(world.snapshot.ball.vx,world.snapshot.ball.vy);
            if (world.snapshot.dango_contact.normal_impulse>1e-6)
            {
                double measured_contact_speed=world.snapshot.dango_contact.ball_speed_before;
                if (measured_contact_speed>=8 && measured_contact_speed<=12) high_speed=true;
                contact_speed=fmax(contact_speed,measured_contact_speed);
                closing_speed=fmax(closing_speed,-world.snapshot.dango_contact.normal_speed_before);
            }
            max_speed=fmax(max_speed,speed); rb_world_state_contract(&world,label,&max_penetration);
            contacted=contacted || world.snapshot.dango_contact.normal_impulse>1e-6;
            impulse+=world.snapshot.dango_contact.normal_impulse;
        }
        rb_check(high_speed,label,"actual-contact-ball-speed-at-least-8",contact_speed,8);
        rb_check(contacted && impulse>1e-6,label,"actual-high-speed-contact-impulse",impulse,1e-6);
        rb_check(closing_speed>0,label,"actual-contact-relative-closing-speed",closing_speed,0);
        printf("SCENE %s max_penetration=%.17g max_ball_speed=%.17g contact_ball_speed=%.17g actual_relative_closing_speed=%.17g normal_impulse=%.17g\n",
            label,max_penetration,max_speed,contact_speed,closing_speed,impulse);
        ++rb_cases; rb_world_destroy(&world,label);
    }
    /* Real collision response against an independently stepped original rig. */
    rb_test_world actual,baseline; const char* label="I2 real Dango reaction";
    bool actual_ok=rb_world_create(&actual,0,3,label),baseline_ok=rb_world_create(&baseline,0,3,label);
    if (actual_ok && baseline_ok && rb_world_add_ball(&actual,rb_definition(-2,3.42),label))
    {
        int code=dango_round_ball_test_initial_velocity(actual.handle,8.2,0,0);
        rb_check(code==0,label,"incoming-ball-before-first-Step",code,0);
        bool contact=false,response=false;
        for (int step=0;step<900;++step)
        {
            float controls[6]={0};
            bool actual_step=rb_world_step(&actual,controls,true,label),baseline_step=rb_world_step(&baseline,controls,true,label);
            if (!actual_step || !baseline_step) break;
            if (!contact && actual.snapshot.dango_contact.normal_impulse>1e-6)
            {
                contact=true; double distance=0;
                for (int field=0;field<34;++field) distance=fmax(distance,fabs((double)actual.frame[field]-baseline.frame[field]));
                response=distance>1e-6;
                rb_check(response,label,"Dango-reaction-after-real-contact",distance,1e-6);
            }
        }
        rb_check(contact,label,"real-reaction-contact-reached",contact,1);
        rb_check(response,label,"real-Dango-reaction-present",response,1); ++rb_cases;
    }
    rb_world_destroy(&actual,label); rb_world_destroy(&baseline,label);
}

static void rb_real_rolling_contracts(void)
{
    for (int zero_friction=0;zero_friction<2;++zero_friction)
    {
        const char* label=zero_friction ? "I3 real frictionless sliding" : "I3 real rolling";
        rb_test_world world; if (!rb_world_create(&world,-3,0,label)) { rb_world_destroy(&world,label); continue; }
        dango_ball_definition definition=rb_definition(0,-.32); definition.floor_friction=zero_friction ? 0 : .45;
        if (!rb_world_add_ball(&world,definition,label)) { rb_world_destroy(&world,label); continue; }
        int code=dango_round_ball_test_initial_velocity(world.handle,1,0,0);
        rb_check(code==0,label,"bounded-slide-initial-fixture",code,0);
        bool interval=false; double max_penetration=0;
        for (int step=0;step<900;++step)
        {
            float controls[6]={0}; if (!rb_world_step(&world,controls,true,label)) break;
            rb_world_state_contract(&world,label,&max_penetration);
            if (step>=30 && step<120 && world.snapshot.floor_contact.touching && fabs(world.snapshot.ball.vx)>.1)
            {
                double slip=world.snapshot.ball.vx+.4*world.snapshot.ball.omega;
                if (zero_friction)
                {
                    rb_close(world.snapshot.ball.omega,0,1e-6,0,label,"zero-friction-no-invented-spin");
                    rb_close(world.snapshot.ball.angle,0,1e-10,0,label,"real-angle-not-invented-from-sliding-displacement");
                    rb_check(fabs(slip)>.1,label,"real-sliding-velocity",slip,.1);
                }
                else rb_close(slip,0,.002,0,label,"real-no-slip-rolling-interval");
                interval=true;
            }
        }
        rb_check(interval,label,"real-measured-ground-interval",interval,1); ++rb_cases;
        rb_world_destroy(&world,label);
    }
    const char* label="I3 actual airborne spin independent of horizontal displacement";
    rb_test_world world;
    if (rb_world_create(&world,-3,0,label) && rb_world_add_ball(&world,rb_definition(2,2),label))
    {
        int code=dango_round_ball_test_initial_velocity(world.handle,0,0,3);
        rb_check(code==0,label,"bounded-actual-spin-initial-fixture",code,0);
        float controls[6]={0}; bool sampled=false; double maximum_penetration=0;
        for (int step=0;step<900;++step)
        {
            if (!rb_world_step(&world,controls,true,label)) break;
            rb_world_state_contract(&world,label,&maximum_penetration);
            if (step<15)
            {
                rb_check(!world.snapshot.floor_contact.touching && !world.snapshot.dango_contact.touching,
                    label,"actual-free-flight-before-contact",world.snapshot.floor_contact.touching,0);
                rb_close(world.snapshot.ball.x,2,1e-10,0,label,"horizontal-position-stays-fixed");
                rb_close(world.snapshot.ball.omega,3,1e-6,0,label,"free-flight-native-omega");
                rb_close(world.snapshot.ball.angle,(step+1)*3*(double)(1.0f/60),1e-10,0,label,"angle-integrates-native-spin-without-horizontal-motion");
                sampled=true;
            }
        }
        rb_check(sampled,label,"actual-free-flight-spin-observed",sampled,1); ++rb_cases;
    }
    rb_world_destroy(&world,label);
}

static void rb_ball_top_jump_contracts(void)
{
    const char* label="I4 actual ball-top Space restriction"; rb_test_world quiet,space;
    bool quiet_ok=rb_world_create(&quiet,0,1.7f,label),space_ok=rb_world_create(&space,0,1.7f,label);
    if (quiet_ok && space_ok && rb_world_add_ball(&quiet,rb_definition(0,-.32),label) &&
        rb_world_add_ball(&space,rb_definition(0,-.32),label))
    {
        bool reached=false; int qualified_steps=0;
        for (int step=0;step<900;++step)
        {
            float zero[6]={0},pressed[6]={0}; if (reached) pressed[2]=1;
            bool quiet_step=rb_world_step(&quiet,zero,true,label),space_step=rb_world_step(&space,pressed,true,label);
            if (!quiet_step || !space_step) break;
            bool on_top=quiet.snapshot.dango_contact.touching && quiet.snapshot.dango_contact.ny<-.7 &&
                quiet.frame[38]==0 && quiet.frame[39]==0;
            if (on_top && step>=27) reached=true;
            if (reached && on_top && qualified_steps<30)
            {
                rb_check(quiet.snapshot.floor_contact.touching && space.snapshot.floor_contact.touching,
                    label,"real-Dango-ball-floor-sandwich-contact",quiet.snapshot.floor_contact.touching,1);
                rb_check(space.frame[38]==0 && space.frame[39]==0,label,"ball-top-does-not-grant-ground",space.frame[39],0);
                rb_check(memcmp(quiet.frame,space.frame,sizeof quiet.frame)==0,
                    label,"Space-and-held-Space-have-no-extra-launch",space.frame[33],quiet.frame[33]);
                rb_close(space.snapshot.ball.y,quiet.snapshot.ball.y,1e-10,0,label,"same-real-ball-reaction");
                ++qualified_steps;
            }
        }
        rb_check(reached,label,"real-ball-top-contact-without-original-ground",reached,1);
        rb_check(qualified_steps>=2,label,"tap-and-continuing-Space-observed",qualified_steps,2); ++rb_cases;
    }
    rb_world_destroy(&quiet,label); rb_world_destroy(&space,label);
    rb_test_world floor; label="I4 original-floor launch positive control";
    if (rb_world_create(&floor,-2,0,label))
    {
        float controls[6]={0}; bool settled=true;
        for (int step=0;step<180;++step) if (!rb_world_step(&floor,controls,true,label)) { settled=false; break; }
        rb_check(settled && floor.frame[39]!=0,label,"original-ground-qualified",floor.frame[39],1);
        double before=floor.frame[33]; controls[2]=1;
        bool stepped=rb_world_step(&floor,controls,true,label);
        rb_check(stepped && floor.frame[33]>before+.001,label,"original-floor-Space-still-launches",floor.frame[33],before+.001);
        ++rb_cases;
    }
    rb_world_destroy(&floor,label);
}

static void rb_sleep_contracts(void)
{
    const char* label="I5 real sleep hold and contact wake"; rb_test_world world;
    if (!rb_world_create(&world,-2,0,label)) { rb_world_destroy(&world,label); return; }
    if (!rb_world_add_ball(&world,rb_definition(3,-.32),label)) { rb_world_destroy(&world,label); return; }
    bool sleeping=false; float controls[6]={0};
    for (int step=0;step<600;++step)
    {
        if (!rb_world_step(&world,controls,true,label)) break;
        if (world.snapshot.ball.sleeping) { sleeping=true; break; }
    }
    rb_check(sleeping,label,"true-sleep-within-ten-seconds",sleeping,1);
    uint64_t integrations=world.snapshot.ball.integrations,first_step=world.snapshot.step;
    double first_core=world.frame[32]; bool active=false; float direction=1;
    for (int step=0;step<1800 && sleeping;++step)
    {
        if (world.frame[32]>-1.3f) direction=-1; else if (world.frame[32]<-2.7f) direction=1;
        controls[0]=direction; if (!rb_world_step(&world,controls,true,label)) break;
        rb_check(world.snapshot.ball.sleeping && world.snapshot.ball.integrations==integrations,
            label,"thirty-second-integrations-truly-stopped",(double)world.snapshot.ball.integrations,(double)integrations);
        active=active || fabs(world.frame[32]-first_core)>.1;
    }
    rb_check(active && world.snapshot.step>=first_step+1800,label,"active-Dango-world-continues-during-sleep",
        (double)(world.snapshot.step-first_step),1800);
    bool woke=false,moved=false; double max_penetration=0;
    for (int step=0;step<900 && sleeping;++step)
    {
        controls[0]=1; if (!rb_world_step(&world,controls,true,label)) break;
        rb_world_state_contract(&world,label,&max_penetration);
        if (world.snapshot.dango_contact.normal_impulse>1e-6 && !world.snapshot.ball.sleeping) woke=true;
        if (woke && world.snapshot.ball.integrations>integrations && hypot(world.snapshot.ball.vx,world.snapshot.ball.vy)>.02) moved=true;
    }
    rb_check(woke,label,"actual-sleeping-ball-contact-wakes",woke,1);
    rb_check(moved,label,"wake-resumes-integration-and-motion",moved,1); ++rb_cases;
    rb_world_destroy(&world,label);
}

static void rb_safe_snapshot_contracts(void)
{
    for (int scenario=0;scenario<3;++scenario)
    {
        const char* label=scenario==0 ? "I6 creation-first-Step fault" : scenario==1 ? "I6 removed-ball fault" : "I6 Reset-first-Step fault";
        rb_test_world world; if (!rb_world_create(&world,-2,0,label)) { rb_world_destroy(&world,label); continue; }
        if (!rb_world_add_ball(&world,rb_definition(3,1),label)) { rb_world_destroy(&world,label); continue; }
        float controls[6]={0};
        if (scenario>0) for (int step=0;step<20;++step) if (!rb_world_step(&world,controls,true,label)) break;
        int code=0;
        if (scenario==1)
        {
            code=dango_round_ball_test_remove(world.handle,world.ball_id);
            rb_check(code==0,label,"remove-before-real-fault",code,0);
        }
        if (scenario==2)
        {
            code=dango_physics_world_reset(world.handle); rb_check(code==0,label,"Reset-before-real-fault",code,0);
        }
        dango_ball_snapshot safe={0}; float safe_frame[42]={0};
        code=dango_round_ball_test_read(world.handle,&safe); rb_check(code==0,label,"safe-common-snapshot",code,0);
        code=dango_round_ball_test_dango_frame(world.handle,world.dango_id,safe_frame,42);
        rb_check(code==0,label,"safe-published-42-slots-without-Step",code,0);
        code=dango_round_ball_test_fault(world.handle,scenario==1 ? 2 : 1);
        rb_check(code==0,label,"arm-real-four-substep-corruption",code,0);
        bool stepped=rb_world_step(&world,controls,false,label); int latched=world.error;
        rb_check(stepped && latched!=0,label,"real-corruption-latches-failure",latched,1);
        rb_check(memcmp(world.frame,safe_frame,sizeof safe_frame)==0,label,"Dango-last-safe-frame-frozen",world.frame[33],safe_frame[33]);
        rb_check(memcmp(&world.snapshot,&safe,sizeof safe)==0,label,"ball-and-diagnostics-same-safe-revision",(double)world.snapshot.scene_revision,(double)safe.scene_revision);
        rb_check(world.snapshot.ball.enabled==(scenario!=1),label,"topology-not-reverted",world.snapshot.ball.enabled,scenario!=1);
        for (int repeat=0;repeat<3;++repeat)
        {
            controls[0]=1; controls[2]=1; rb_world_step(&world,controls,false,label);
            rb_check(world.error==latched && memcmp(world.frame,safe_frame,sizeof safe_frame)==0 &&
                memcmp(&world.snapshot,&safe,sizeof safe)==0,label,"ordinary-actions-cannot-pass-latch",world.error,latched);
        }
        code=dango_physics_world_reset(world.handle); rb_check(code==0,label,"explicit-Reset-recovers",code,0);
        controls[0]=0; controls[2]=0; rb_world_step(&world,controls,true,label);
        rb_check(world.snapshot.ball.enabled==(scenario!=1),label,"Reset-does-not-resurrect-removed-ball",world.snapshot.ball.enabled,scenario!=1);
        if (scenario!=1) rb_check(world.snapshot.ball.id==world.ball_id,label,"Reset-preserves-surviving-ID",world.snapshot.ball.id,world.ball_id);
        ++rb_cases; rb_world_destroy(&world,label);
    }
}

static void rb_lifecycle_contracts(void)
{
    int owners=dango_round_ball_test_owner_count(),bytes=dango_physics_get_allocated_bytes();
    for (int iteration=0;iteration<100;++iteration)
    {
        const char* label="I7 100 real lifecycle cycles"; rb_test_world world;
        if (!rb_world_create(&world,-2,0,label)) { rb_world_destroy(&world,label); continue; }
        rb_check(dango_round_ball_test_owner_count()==owners+1,label,"one-real-owner",dango_round_ball_test_owner_count(),owners+1);
        dango_ball_definition definition=rb_definition(3,-.32);
        if (!rb_world_add_ball(&world,definition,label)) { rb_world_destroy(&world,label); continue; }
        dango_ball_snapshot safe=world.snapshot,after={0}; int invalid_id=0;
        int code=dango_round_ball_test_add(world.handle,&definition,&invalid_id);
        rb_check(code!=0,label,"second-ball-rejected",code,1);
        code=dango_physics_world_add(world.handle,1,0,&invalid_id);
        rb_check(code!=0,label,"second-Dango-rejected-with-ball",code,1);
        code=dango_physics_world_set_static_platform(world.handle,0,1,2,.3f,.72f);
        rb_check(code!=0,label,"platform-and-ball-conflict-rejected",code,1);
        dango_round_ball_test_read(world.handle,&after);
        rb_check(memcmp(&safe,&after,sizeof safe)==0,label,"failed-conflicts-preserve-entire-snapshot",after.ball.id,safe.ball.id);
        int first_id=world.ball_id; code=dango_round_ball_test_remove(world.handle,first_id);
        rb_check(code==0,label,"remove-original-ID",code,0);
        code=dango_round_ball_test_remove(world.handle,first_id); rb_check(code!=0,label,"old-ID-invalid-after-remove",code,1);
        if (!rb_world_add_ball(&world,definition,label)) { rb_world_destroy(&world,label); continue; }
        rb_check(world.ball_id>first_id,label,"recreate-uses-new-ID",world.ball_id,first_id+1);
        int surviving_id=world.ball_id; uint64_t revision=world.snapshot.scene_revision;
        code=dango_physics_world_reset(world.handle); rb_check(code==0,label,"Reset-live-ball",code,0);
        dango_round_ball_test_read(world.handle,&after);
        rb_check(after.ball.id==surviving_id && after.scene_revision>revision,label,"Reset-keeps-ID-and-advances-revision",after.ball.id,surviving_id);
        rb_close(after.ball.x,definition.x,0,0,label,"Reset-restores-birth-x");
        rb_close(after.ball.y,definition.y,0,0,label,"Reset-restores-birth-y");
        code=dango_round_ball_test_remove(world.handle,surviving_id); rb_check(code==0,label,"remove-reset-ball",code,0);
        code=dango_physics_world_reset(world.handle); rb_check(code==0,label,"Reset-after-remove",code,0);
        dango_round_ball_test_read(world.handle,&after);
        rb_check(!after.ball.enabled,label,"Reset-never-revives-removed-ID",after.ball.enabled,0);
        /* Rejections before ball creation must not alter scene revision either. */
        safe=after; dango_ball_definition bad=definition; bad.x=NAN;
        code=dango_round_ball_test_add(world.handle,&bad,&invalid_id); rb_check(code!=0,label,"nonfinite-create-rejected",code,1);
        bad=definition; bad.x=-8; code=dango_round_ball_test_add(world.handle,&bad,&invalid_id);
        rb_check(code!=0,label,"out-of-bounds-create-rejected",code,1);
        bad=definition; bad.x=-2*(4.0/3); bad.y=.42;
        code=dango_round_ball_test_add(world.handle,&bad,&invalid_id);
        rb_check(code!=0,label,"real-Dango-overlap-create-rejected",code,1);
        dango_round_ball_test_read(world.handle,&after);
        rb_check(memcmp(&safe,&after,sizeof safe)==0,label,"invalid-create-has-no-partial-state",after.ball.enabled,0);
        code=dango_physics_world_set_static_platform(world.handle,0,1,2,.3f,.72f);
        rb_check(code==0,label,"create-original-platform-without-ball",code,0);
        code=dango_round_ball_test_add(world.handle,&definition,&invalid_id);
        rb_check(code!=0,label,"existing-platform-rejects-ball",code,1);
        code=dango_physics_world_clear_static_platform(world.handle);
        rb_check(code==0,label,"clear-test-platform",code,0);
        int second_id=0; code=dango_physics_world_add(world.handle,1,0,&second_id);
        rb_check(code==0,label,"original-multiple-Dango-capacity-restored",code,0);
        code=dango_round_ball_test_add(world.handle,&definition,&invalid_id);
        rb_check(code!=0,label,"multiple-Dango-world-rejects-ball",code,1);
        ++rb_cases; rb_world_destroy(&world,label);
        rb_check(dango_round_ball_test_owner_count()==owners,label,"owner-count-returned-to-baseline",dango_round_ball_test_owner_count(),owners);
        rb_check(dango_physics_get_allocated_bytes()==bytes,label,"native-bytes-returned-to-baseline",dango_physics_get_allocated_bytes(),bytes);
    }
}

static void rb_sleeping_floor_boundary_contract(void)
{
    const char* label="I5/I7 clear-bounds cannot strand a truly sleeping ball";
    rb_test_world world; if (!rb_world_create(&world,-2,0,label)) { rb_world_destroy(&world,label); return; }
    int code=dango_physics_world_set_horizontal_bounds(world.handle,-20,20);
    rb_check(code==0,label,"widen-real-original-floor",code,0);
    if (!rb_world_add_ball(&world,rb_definition(12,-.32),label)) { rb_world_destroy(&world,label); return; }
    float controls[6]={0}; bool sleeping=false;
    for (int step=0;step<600;++step)
    {
        if (!rb_world_step(&world,controls,true,label)) break;
        if (world.snapshot.ball.sleeping && world.snapshot.floor_contact.touching) { sleeping=true; break; }
    }
    rb_check(sleeping,label,"real-wide-floor-sleep-reached",sleeping,1);
    dango_ball_snapshot safe=world.snapshot; float safe_frame[42]={0};
    code=dango_round_ball_test_dango_frame(world.handle,world.dango_id,safe_frame,42);
    rb_check(code==0,label,"capture-unchanged-published-Dango",code,0);
    code=dango_physics_world_clear_horizontal_bounds(world.handle);
    rb_check(code!=0,label,"clear-that-strands-ball-is-rejected",code,-410);
    dango_ball_snapshot after={0}; dango_round_ball_test_read(world.handle,&after);
    rb_check(memcmp(&safe,&after,sizeof safe)==0,label,"rejected-clear-keeps-whole-safe-snapshot",
        (double)after.scene_revision,(double)safe.scene_revision);
    float after_frame[42]={0};
    dango_round_ball_test_dango_frame(world.handle,world.dango_id,after_frame,42);
    rb_check(memcmp(safe_frame,after_frame,sizeof safe_frame)==0,label,"rejected-clear-keeps-published-42-slots",after_frame[33],safe_frame[33]);
    bool stepped=rb_world_step(&world,controls,true,label);
    rb_check(stepped && world.snapshot.ball.sleeping && world.snapshot.floor_contact.touching &&
        world.snapshot.ball.integrations==safe.ball.integrations,label,"original-wide-floor-remains-real-support",
        world.snapshot.floor_contact.touching,1);
    bool woke=false;
    for (int step=0;step<600;++step)
    {
        controls[0]=1; if (!rb_world_step(&world,controls,true,label)) break;
        if (world.snapshot.dango_contact.normal_impulse>1e-6 && !world.snapshot.ball.sleeping &&
            world.snapshot.ball.integrations>safe.ball.integrations && hypot(world.snapshot.ball.vx,world.snapshot.ball.vy)>.02)
        { woke=true; break; }
    }
    rb_check(woke,label,"retained-wide-floor-lets-real-Dango-contact-wake-ball",woke,1);
    printf("SCENE %s sleeping_before_clear=%d clear_return=%d preserved_revision=%llu woke=%d\n",
        label,sleeping,code,(unsigned long long)after.scene_revision,woke);
    ++rb_cases; rb_world_destroy(&world,label);
}

static void rb_translated_floor_contracts(void)
{
    for (int valid_position=0;valid_position<3;++valid_position)
    {
        const char* label=valid_position==1 ? "I1/I7 ball uses the translated real floor with valid joint births" :
            valid_position==2 ? "I7 translated floor rejects original Dango birth outside real floor" :
            "I7 old-origin ball rejected after real floor translation";
        rb_test_world world; if (!rb_world_create(&world,-2,0,label)) { rb_world_destroy(&world,label); continue; }
        int code=dango_physics_world_clear_horizontal_bounds(world.handle);
        rb_check(code==0,label,"original-no-ball-clear",code,0);
        code=dango_physics_world_translate_horizontal(world.handle,20);
        rb_check(code==0,label,"original-no-ball-real-floor-translation",code,0);
        if (valid_position==1)
        {
            /* The accepted old Translate keeps Reset anchors unchanged. A valid
               joint Reset fixture therefore needs a newly recorded Dango birth
               inside the translated real floor, rather than changing old rules. */
            int old_id=world.dango_id;
            code=dango_physics_world_remove(world.handle,old_id);
            rb_check(code==0,label,"remove-old-Dango-with-origin-Reset-birth",code,0);
            code=dango_physics_world_add(world.handle,18,0,&world.dango_id);
            rb_check(code==0 && world.dango_id!=old_id,label,"new-real-Dango-birth-inside-translated-floor",code,0);
        }
        dango_ball_snapshot safe={0},after={0}; dango_round_ball_test_read(world.handle,&safe);
        dango_ball_definition definition=rb_definition(valid_position ? 32 : 0,valid_position==1 ? .7 : -.32);
        code=dango_round_ball_test_add(world.handle,&definition,&world.ball_id);
        if (valid_position!=1)
        {
            rb_check(code!=0,label,valid_position==2 ? "joint-Dango-birth-outside-real-floor-rejected" :
                "old-origin-outside-real-floor-rejected",code,-410);
            dango_round_ball_test_read(world.handle,&after);
            rb_check(memcmp(&safe,&after,sizeof safe)==0,label,"invalid-origin-create-has-no-partial-state",after.ball.enabled,0);
        }
        else
        {
            rb_check(code==0,label,"ball-inside-translated-real-floor-created",code,0);
            bool floor_contact=false,sleeping=false; float controls[6]={0};
            if (!code) for (int step=0;step<900;++step)
            {
                if (!rb_world_step(&world,controls,true,label)) break;
                floor_contact=floor_contact || world.snapshot.floor_contact.touching;
                sleeping=sleeping || world.snapshot.ball.sleeping;
                if (world.snapshot.floor_contact.touching)
                {
                    rb_close(world.snapshot.ball.y-.4,(double)(float)-.72,1.0/30,0,label,"actual-translated-floor-contact-height");
                    rb_check(world.snapshot.ball.x>=16 && world.snapshot.ball.x<=112.0/3,
                        label,"ball-supported-by-translated-real-floor-extent",world.snapshot.ball.x,32);
                }
            }
            rb_check(floor_contact,label,"actual-translated-floor-landing-reached",floor_contact,1);
            rb_check(sleeping,label,"actual-translated-floor-native-sleep-reached",sleeping,1);
            printf("SCENE %s create_return=%d actual_landing=%d true_sleep=%d\n",label,code,floor_contact,sleeping);
        }
        ++rb_cases; rb_world_destroy(&world,label);
    }
}

static void rb_joint_birth_reset_contract(void)
{
    const char* label="I6/I7 ball creation preserves joint birth Reset safety";
    rb_test_world world; if (!rb_world_create(&world,0,0,label)) { rb_world_destroy(&world,label); return; }
    bool walked=false; float controls[6]={0};
    for (int step=0;step<240;++step)
    {
        controls[0]=1; if (!rb_world_step(&world,controls,true,label)) break;
        if (world.frame[32]>1.5f) { walked=true; break; }
    }
    rb_check(walked,label,"original-rig-walked-away-by-legal-controls",world.frame[32],1.5);
    controls[0]=0; for (int step=0;step<40;++step) if (!rb_world_step(&world,controls,true,label)) break;
    dango_ball_definition definition=rb_definition(0,-.32);
    dango_ball_geometry geometry={0}; dango_ball_geometry_result current={0};
    int code=dango_round_ball_test_world_geometry(world.handle,world.dango_id,definition.x,definition.y,.4,&geometry,&current);
    rb_check(code==0 && current.gap>0,label,"proposed-ball-clear-of-current-real-rig",current.gap,0);
    double current_gap=current.gap;
    dango_ball_snapshot safe={0},after={0}; dango_round_ball_test_read(world.handle,&safe);
    int creation=dango_round_ball_test_add(world.handle,&definition,&world.ball_id);
    rb_check(creation!=0,label,"joint-birth-overlap-is-rejected-before-create",creation,-410);
    dango_round_ball_test_read(world.handle,&after);
    rb_check(memcmp(&safe,&after,sizeof safe)==0,label,"birth-conflict-rejection-has-no-partial-state",
        after.ball.enabled,0);
    int reset=dango_physics_world_reset(world.handle);
    rb_check(reset==0,label,"explicit-joint-Reset-remains-usable",reset,0);
    double reset_gap=0;
    if (!creation)
    {
        dango_round_ball_test_read(world.handle,&after);
        code=dango_round_ball_test_world_geometry(world.handle,world.dango_id,after.ball.x,after.ball.y,.4,&geometry,&current);
        rb_check(code==0,label,"recompute-actual-published-Reset-geometry",code,0); reset_gap=current.gap;
        rb_check(code==0 && reset_gap>=-1.0/30,label,"Reset-never-publishes-penetrating-safe-state",reset_gap,-1.0/30);
    }
    else
    {
        dango_round_ball_test_read(world.handle,&after);
        rb_check(!after.ball.enabled,label,"rejected-birth-ball-not-resurrected-by-Reset",after.ball.enabled,0);
    }
    printf("SCENE %s current_real_gap=%.17g create_return=%d Reset_return=%d actual_Reset_gap=%.17g\n",
        label,current_gap,creation,reset,reset_gap);
    ++rb_cases; rb_world_destroy(&world,label);
}

static void rb_birth_boundary_contracts(void)
{
    for (int dango_birth=0;dango_birth<2;++dango_birth) for (int clear=0;clear<2;++clear)
    {
        char label[128]; snprintf(label,sizeof label,"I6/I7 %s birth containment before %s",
            dango_birth ? "Dango" : "ball",clear ? "clear-bounds" : "set-bounds");
        rb_test_world world;
        if (!rb_world_create_bounds(&world,dango_birth ? 12.0f : -2.0f,0,-20,20,label))
        { rb_world_destroy(&world,label); continue; }
        double birth_x=dango_birth ? 0 : clear ? 12 : 8;
        dango_ball_definition definition=rb_definition(birth_x,-.32);
        if (!dango_birth) definition.floor_friction=0;
        if (!rb_world_add_ball(&world,definition,label)) { rb_world_destroy(&world,label); continue; }
        float controls[6]={0}; bool current_contained=false;
        if (!dango_birth)
        {
            int code=dango_round_ball_test_initial_velocity(world.handle,-2,0,0);
            rb_check(code==0,label,"bounded-initial-ball-motion",code,0);
            for (int step=0;step<120;++step) if (!rb_world_step(&world,controls,true,label)) break;
            double right=(4.0/3)*(clear ? 8 : 4);
            current_contained=world.snapshot.ball.x+.4<=right;
        }
        else
        {
            float target=clear ? 6.0f : 2.0f;
            for (int step=0;step<600;++step)
            {
                controls[0]=-1; if (!rb_world_step(&world,controls,true,label)) break;
                if (world.frame[32]<=target) { current_contained=true; break; }
            }
            controls[0]=0; for (int step=0;step<40;++step) if (!rb_world_step(&world,controls,true,label)) break;
            dango_ball_geometry geometry={0}; dango_ball_geometry_result contact={0};
            int code=dango_round_ball_test_world_geometry(world.handle,world.dango_id,0,-.32,.4,&geometry,&contact);
            rb_check(code==0,label,"read-current-real-rig-containment",code,0);
            double left=clear ? -8 : -4,right=clear ? 8 : 4;
            for (int node=0;node<16;++node)
                current_contained=current_contained && geometry.nodes[2*node]-.045>=left-.005 &&
                    geometry.nodes[2*node]+.045<=right+.005;
        }
        rb_check(current_contained,label,"current-bodies-fit-proposed-boundaries",current_contained,1);
        dango_ball_snapshot safe=world.snapshot,after={0}; float safe_frame[42]={0},after_frame[42]={0};
        int code=dango_round_ball_test_dango_frame(world.handle,world.dango_id,safe_frame,42);
        rb_check(code==0,label,"capture-current-safe-frame",code,0);
        int mutation=clear ? dango_physics_world_clear_horizontal_bounds(world.handle) :
            dango_physics_world_set_horizontal_bounds(world.handle,-4,4);
        rb_check(mutation!=0,label,"bounds-that-invalidate-joint-birth-are-rejected",mutation,-410);
        dango_round_ball_test_read(world.handle,&after);
        dango_round_ball_test_dango_frame(world.handle,world.dango_id,after_frame,42);
        rb_check(memcmp(&safe,&after,sizeof safe)==0 && memcmp(safe_frame,after_frame,sizeof safe_frame)==0,
            label,"birth-boundary-rejection-is-atomic",(double)after.scene_revision,(double)safe.scene_revision);
        code=dango_physics_world_reset(world.handle);
        rb_check(code==0,label,"original-wide-boundary-keeps-joint-Reset-usable",code,0);
        dango_round_ball_test_read(world.handle,&after);
        rb_check(after.ball.enabled && after.ball.id==world.ball_id,label,"joint-Reset-retains-ball-ID",after.ball.id,world.ball_id);
        rb_close(after.ball.x,birth_x,1e-10,0,label,"joint-Reset-restores-unchanged-ball-birth");
        float birth_frame[42]={0}; dango_round_ball_test_dango_frame(world.handle,world.dango_id,birth_frame,42);
        rb_close(birth_frame[32],dango_birth ? 12 : -2,1e-6,0,label,"joint-Reset-restores-unchanged-Dango-birth");
        printf("SCENE %s current_Dango_x=%.17g current_ball_x=%.17g ball_birth_x=%.17g bounds_return=%d Reset_return=%d\n",
            label,(double)safe_frame[32],safe.ball.x,birth_x,mutation,code);
        ++rb_cases; rb_world_destroy(&world,label);
    }
}

static void rb_removed_ball_platform_reset_contract(void)
{
    const char* label="I7 removed-ball original-platform Reset contact freshness";
    rb_test_world world; if (!rb_world_create(&world,0,2.5f,label)) { rb_world_destroy(&world,label); return; }
    if (!rb_world_add_ball(&world,rb_definition(3,-.32),label)) { rb_world_destroy(&world,label); return; }
    int code=dango_round_ball_test_remove(world.handle,world.ball_id);
    rb_check(code==0,label,"remove-ball-restores-platform-capability",code,0);
    code=dango_physics_world_set_static_platform(world.handle,0,1,4,.3f,.72f);
    rb_check(code==0,label,"enable-real-original-platform",code,0);
    float controls[6]={0},contact[6]={0}; bool supported=false;
    for (int step=0;step<300;++step)
    {
        if (!rb_world_step(&world,controls,true,label)) break;
        code=dango_physics_world_get_static_platform_contact(world.handle,world.dango_id,contact,6);
        rb_check(code==0,label,"read-real-original-platform-contact",code,0);
        if (contact[0]!=0 && contact[1]!=0 && contact[2]!=0) supported=true;
    }
    rb_check(supported && contact[2]!=0,label,"actual-platform-supported-contact-reached",contact[2],1);
    double supported_core_y=world.frame[33]; float before_contact[6]; memcpy(before_contact,contact,sizeof contact);
    code=dango_physics_world_reset(world.handle); rb_check(code==0,label,"Reset-after-real-platform-support",code,0);
    code=dango_physics_world_get_static_platform_contact(world.handle,world.dango_id,contact,6);
    rb_check(code==0,label,"read-immediate-Reset-platform-diagnostic",code,0);
    float frame[42]={0}; code=dango_round_ball_test_dango_frame(world.handle,world.dango_id,frame,42);
    rb_check(code==0,label,"read-immediate-published-Reset-frame",code,0);
    rb_close(frame[33],2.5+(double)(float).42,1e-6,0,label,"real-Reset-birth-height");
    rb_check(frame[33]>supported_core_y+.5,label,"actual-Reset-left-old-platform-pose",frame[33],supported_core_y+.5);
    dango_ball_geometry geometry={0}; dango_ball_geometry_result ignored={0};
    code=dango_round_ball_test_world_geometry(world.handle,world.dango_id,3,3,.4,&geometry,&ignored);
    rb_check(code==0,label,"read-actual-Reset-rig-for-independent-platform-gap",code,0);
    float positions[34]; rb_pack_geometry(&geometry,positions); rb_oracle_mesh mesh={0};
    bool valid=rb_o_mesh(positions,&mesh); rb_check(valid,label,"independent-Reset-display-mesh",valid,1);
    if (valid)
    {
        double minimum_y=HUGE_VAL;
        for (int node=0;node<16;++node) for (int sample=0;sample<8;++sample)
            minimum_y=fmin(minimum_y,rb_o_curve_point(&mesh,node,(double)sample/8).y);
        double expected_gap=minimum_y-(1+.5*(double)(float).3);
        rb_close(contact[5],expected_gap,1e-5,0,label,"platform-gap-matches-current-real-Reset-geometry");
    }
    rb_check(contact[0]!=0 && contact[1]==0 && contact[2]==0,label,"Reset-diagnostic-does-not-retain-old-supported-contact",contact[2],0);
    rb_check(memcmp(contact,before_contact,sizeof contact)!=0,label,"Reset-updates-original-platform-safe-diagnostic",contact[5],before_contact[5]);
    dango_ball_snapshot after={0}; dango_round_ball_test_read(world.handle,&after);
    rb_check(!after.ball.enabled,label,"platform-Reset-does-not-revive-removed-ball",after.ball.enabled,0);
    printf("SCENE %s old_supported=%g old_gap=%.17g Reset_supported=%g Reset_gap=%.17g actual_Reset_core_y=%.17g\n",
        label,before_contact[2],(double)before_contact[5],contact[2],(double)contact[5],(double)frame[33]);
    ++rb_cases; rb_world_destroy(&world,label);
}

int main(void)
{
    printf("Round-ball frozen admission: variant=%d; A4-A8 native math/geometry; A1-A3 consumer projection belongs to independent probe.\n",DANGO_ROUND_BALL_TEST_VARIANT);
    rb_analytic_contracts(); rb_floor_contracts(); rb_geometric_contracts(); rb_real_geometry_contracts();
    rb_push_and_fall_contracts(); rb_high_speed_contracts(); rb_real_rolling_contracts();
    rb_ball_top_jump_contracts(); rb_sleep_contracts(); rb_safe_snapshot_contracts(); rb_lifecycle_contracts();
    rb_sleeping_floor_boundary_contract(); rb_translated_floor_contracts();
    rb_joint_birth_reset_contract();
    rb_birth_boundary_contracts(); rb_removed_ball_platform_reset_contract();
    printf("Round-ball native A4-A8 and I1-I7 cases=%d checks=%d failures=%d\n",rb_cases,rb_checks,rb_failures);
    return rb_failures==0 ? 0 : 1;
}

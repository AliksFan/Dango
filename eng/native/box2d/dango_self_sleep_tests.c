// Isolated sleep experiment. Never linked into the production DLL.
#define _CRT_SECURE_NO_WARNINGS
#include <stdio.h>
#include "box2d/box2d.h"
// Pinned Box2D internals: isolated cache ablation only, never a production API.
#include "joint.h"
#include "dango_ground_contact_evaluation.h"
static bool ground_candidate;
static int ground_variant;
static b2ShapeId ground_candidate_shape;
static float ground_mu,ground_dt;
static b2Vec2 ground_previous[17];
static float ground_normal_impulses[16];
static float ground_tangent_impulses[16];
static float ground_max_velocity_energy_gain;
static bool ground_joint_solve(dango_constraint_state* state)
{
    if (!ground_candidate) return dango_constraint_solve(state);
    float lambda[16]={0},normal_extra[16]={0},tangent_impulse[16]={0};
    for (int iteration=0;iteration<96;++iteration)
    {
        float residual=ground_variant==1 ? dango_constraint_sweep_order(state,true,iteration%2!=0) :
            ground_geometry_sweep(state,normal_extra,iteration%2!=0);
        if (!isfinite(residual)) return false;
        float change=0,velocity_change=0;
        for (int i=0;i<16;++i)
        {
            if (state->positions[i].y-state->ground-state->radius>.005f) continue;
            change=fmaxf(change,ground_friction_project(&state->positions[i].x,ground_previous[i].x,
                state->inverse_mass[i],ground_normal_impulses[i]+normal_extra[i],ground_mu,ground_dt,&lambda[i]));
            if (ground_variant==2) velocity_change=fmaxf(velocity_change,ground_friction_velocity_project(&state->velocities[i].x,
                state->inverse_mass[i],ground_normal_impulses[i]+normal_extra[i],ground_mu,&tangent_impulse[i]));
        }
        if (dango_constraint_sweep(state,false)<=DANGO_CONSTRAINT_TOLERANCE && change/state->radial_length<=DANGO_CONSTRAINT_TOLERANCE && velocity_change<=.00001f)
        {
            if (ground_variant>=4)
            {
                float normals[16];
                for (int i=0;i<16;++i) normals[i]=ground_normal_impulses[i]+normal_extra[i];
                float before_energy=0,after_energy=0;
                for (int i=0;i<17;++i) before_energy+=.5f*b2LengthSquared(state->velocities[i])/state->inverse_mass[i];
                if (!ground_velocity_solve(state,normals,ground_mu,NULL,ground_variant==5 ? ground_tangent_impulses : NULL)) return false;
                for (int i=0;i<17;++i) after_energy+=.5f*b2LengthSquared(state->velocities[i])/state->inverse_mass[i];
                ground_max_velocity_energy_gain=fmaxf(ground_max_velocity_energy_gain,after_energy-before_energy);
                if (after_energy>before_energy+1e-6f) { fprintf(stderr,"ground velocity energy gain=%.9g\n",(double)(after_energy-before_energy)); return false; }
                return true;
            }
            if (ground_variant!=2) for (int i=0;i<16;++i) if (state->positions[i].y-state->ground-state->radius<=.005f)
                state->velocities[i].x=ground_friction_velocity(state->velocities[i].x,state->inverse_mass[i],
                    ground_normal_impulses[i]+normal_extra[i],ground_mu);
            return true;
        }
    }
    fprintf(stderr,"ground candidate constraint residual=%.9g\n",(double)dango_constraint_sweep(state,false));
    return false;
}
static bool preserve_joint_impulses;
static int diagnostic_cache_reset; // 0 unchanged, 1 spring only, 2 limit pair only
static bool radial_trace_enabled;
static b2JointId radial_trace_joints[16];
static b2Vec2 radial_world_axis[16];
static float radial_world_length[16],radial_world_speed[16],radial_world_impulse[16];
static bool radial_world_sampled;
static float diagnostic_dt;
static bool trace_enabled;
static b2BodyId trace_ids[17];
static double trace_force_x, trace_world_dx, trace_projection_dx;
static int trace_steps;
static double trace_velocity_px, trace_world_px;
static double pending_fx;
static bool diagnostic_balance;
static double trace_node_world_dx[17], trace_node_projection_dx[17];
static void trace_velocity(b2BodyId id, b2Vec2 velocity)
{
    if (trace_enabled) trace_velocity_px+=(double)b2Body_GetMass(id)*(velocity.x-b2Body_GetLinearVelocity(id).x);
    b2Body_SetLinearVelocity(id,velocity);
}
static void trace_apply_force(b2BodyId id, b2Vec2 force, bool wake)
{
    if (trace_enabled) trace_force_x+=force.x;
    pending_fx+=force.x;
    b2Body_ApplyForceToCenter(id,force,wake);
}
static void trace_transform(b2BodyId id, b2Vec2 position, b2Rot rotation)
{
    if (trace_enabled) trace_projection_dx+=(double)b2Body_GetMass(id)*(position.x-b2Body_GetPosition(id).x);
    if (trace_enabled) for (int i=0;i<17;++i) if (B2_ID_EQUALS(id,trace_ids[i]))
        trace_node_projection_dx[i]+=position.x-b2Body_GetPosition(id).x;
    b2Body_SetTransform(id,position,rotation);
}
static void trace_world_step(b2WorldId id, float dt, int substeps)
{
    if (ground_candidate)
    {
        ground_dt=dt;
        for (int i=0;i<17;++i) ground_previous[i]=b2Body_GetPosition(trace_ids[i]);
    }
    double before=0,after=0, momentum_before=0,momentum_after=0;
    float before_x[17]={0};
    if (trace_enabled) for (int i=0;i<17;++i) before_x[i]=b2Body_GetPosition(trace_ids[i]).x;
    if (diagnostic_balance) b2Body_ApplyForceToCenter(trace_ids[16],(b2Vec2){-(float)pending_fx,0},true);
    pending_fx=0;
    if (trace_enabled) for (int i=0;i<17;++i) before+=(double)b2Body_GetMass(trace_ids[i])*b2Body_GetPosition(trace_ids[i]).x;
    if (trace_enabled) for (int i=0;i<17;++i) momentum_before+=(double)b2Body_GetMass(trace_ids[i])*b2Body_GetLinearVelocity(trace_ids[i]).x;
    b2World_Step(id,dt,substeps);
    if (radial_trace_enabled)
    {
        radial_world_sampled=true;
        for (int i=0;i<16;++i)
        {
            b2Vec2 delta=b2Sub(b2Body_GetPosition(trace_ids[i]),b2Body_GetPosition(trace_ids[16]));
            radial_world_axis[i]=b2Normalize(delta);
            radial_world_length[i]=b2Length(delta);
            radial_world_speed[i]=b2Dot(radial_world_axis[i],b2Sub(b2Body_GetLinearVelocity(trace_ids[i]),b2Body_GetLinearVelocity(trace_ids[16])));
            radial_world_impulse[i]=b2GetJointSimCheckType(radial_trace_joints[i],b2_distanceJoint)->distanceJoint.impulse;
        }
    }
    if (ground_candidate) for (int i=0;i<16;++i)
    {
        ground_normal_impulses[i]=0;
        ground_tangent_impulses[i]=0;
        b2ContactData contacts[8];
        int count=b2Body_GetContactData(trace_ids[i],contacts,8);
        for (int j=0;j<count;++j) if (B2_ID_EQUALS(contacts[j].shapeIdA,ground_candidate_shape) || B2_ID_EQUALS(contacts[j].shapeIdB,ground_candidate_shape))
            for (int p=0;p<contacts[j].manifold.pointCount;++p)
            {
                ground_normal_impulses[i]+=contacts[j].manifold.points[p].normalImpulse;
                float sign=B2_ID_EQUALS(contacts[j].shapeIdA,ground_candidate_shape) ? 1.0f : -1.0f;
                ground_tangent_impulses[i]+=sign*contacts[j].manifold.normal.y*contacts[j].manifold.points[p].tangentImpulse;
            }
    }
    if (trace_enabled)
    {
        for (int i=0;i<17;++i) after+=(double)b2Body_GetMass(trace_ids[i])*b2Body_GetPosition(trace_ids[i]).x;
        trace_world_dx+=after-before; ++trace_steps;
        for (int i=0;i<17;++i) momentum_after+=(double)b2Body_GetMass(trace_ids[i])*b2Body_GetLinearVelocity(trace_ids[i]).x;
        trace_world_px+=momentum_after-momentum_before;
        for (int i=0;i<17;++i) trace_node_world_dx[i]+=b2Body_GetPosition(trace_ids[i]).x-before_x[i];
    }
}
static void evaluation_set_length_range(b2JointId id, float minimum, float maximum)
{
    if (!preserve_joint_impulses || b2DistanceJoint_GetMinLength(id)!=minimum || b2DistanceJoint_GetMaxLength(id)!=maximum)
        b2DistanceJoint_SetLengthRange(id,minimum,maximum);
    else if (ground_candidate && diagnostic_cache_reset)
    {
        b2DistanceJoint* joint=&b2GetJointSimCheckType(id,b2_distanceJoint)->distanceJoint;
        if (diagnostic_cache_reset==1) joint->impulse=0;
        else { joint->lowerImpulse=0; joint->upperImpulse=0; }
    }
}
#define b2DistanceJoint_SetLengthRange evaluation_set_length_range
#define b2Body_ApplyForceToCenter trace_apply_force
#define b2Body_SetTransform trace_transform
#define b2World_Step trace_world_step
#define b2Body_SetLinearVelocity trace_velocity
#define dango_constraint_solve ground_joint_solve
#include "dango_box2d_bridge.c"
#undef b2DistanceJoint_SetLengthRange
#undef b2Body_ApplyForceToCenter
#undef b2Body_SetTransform
#undef b2World_Step
#undef dango_constraint_solve
#undef b2Body_SetLinearVelocity
#include "dango_pair_ground_evaluation.h"
#define dango_self_solve_constraints pair_ground_solve
#include "dango_node_edge_evaluation.h"
#undef dango_self_solve_constraints
#define CHECK(c) do { if (!(c)) { fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #c); return 1; } } while (0)

static dango_self_physics_config configuration(void)
{
    return (dango_self_physics_config){
        .node_count=16, .substep_count=4, .radial_rest_length=.68f,
        .radial_minimum_ratio=.65f, .radial_maximum_ratio=1.22f,
        .perimeter_minimum_ratio=.73f, .perimeter_maximum_ratio=1.07f,
        .radial_hertz=5.2f, .perimeter_hertz=20.1f, .radial_damping=1.85f, .perimeter_damping=1.85f,
        .area_strength=4.8f, .ground_deformation_strength=3.5f,
        .repulsion_distance=.09f, .repulsion_strength=.3f, .repulsion_maximum_force=3.5f,
        .core_exclusion_ratio=.25f, .angle_strength=10, .local_angle_balance_strength=1.7f,
        .gravity=9.8f, .core_mass=.12f, .node_mass=.015f, .drop_height=.42f,
        .ground_height=-.72f, .node_radius=.045f, .friction=.72f, .restitution=.02f,
        .contact_hertz=28, .linear_damping=1.8f, .drag_stiffness=10, .drag_damping=2.2f,
        .drag_maximum_force=8, .horizontal_force=2.4f, .maximum_horizontal_speed=3.2f,
        .air_control_ratio=.35f, .downward_force=2.2f, .jump_impulse=.42f,
        .jump_cooldown_seconds=.45f, .maximum_speed=12, .minimum_area=.42f, .top_inward_force=.5f
    };
}

static b2BodyId rig_id(dango_self_soft_body* b, int i)
{
    return i == 16 ? b->core_body_id : b->node_body_ids[i];
}

static int awake_count(dango_self_soft_body* b)
{
    int count=0;
    for (int i=0; i<17; ++i) count += b2Body_IsAwake(rig_id(b,i)) ? 1 : 0;
    return count;
}

static float extra_downward;
static int extra_jump;
static bool interactive_targets;
static float interactive_target_x,interactive_target_y;

static int active_step(dango_self_soft_body* b, const dango_self_physics_config* c, float x, float y,
    float horizontal, int drag, float dt)
{
    float points[32], area, cx, cy, px, py, speed;
    ground_mu=c->friction;
    if (diagnostic_dt>0) dt=diagnostic_dt;
    int count, contacts, grounded, selected;
    return dango_box2d_self_soft_body_step(b, dt, c, x, y, horizontal, extra_downward, extra_jump, drag,
        interactive_targets ? interactive_target_x : x-1, interactive_targets ? interactive_target_y : 0,
        points, 16, &count, &contacts, &area, &cx, &cy, &px, &py, &speed, &grounded, &selected);
}

typedef struct sleep_probe
{
    int stable_frames;
    bool sleeping;
    b2Vec2 minimum[17], maximum[17], relative[16];
    float observed_speed, observed_range, observed_shape;
    int longest_stable;
    float speed_threshold;
    bool failed;
    bool configured;
    dango_self_physics_config last_config;
} sleep_probe;

// Read-only validation: never project an illegal shape to grant sleep eligibility.
static bool legal(dango_self_soft_body* b, const dango_self_physics_config* c)
{
    dango_constraint_state s={0};
    s.radial_length=b->radial_rest_length; s.perimeter_length=b->perimeter_rest_length;
    s.radial_min=c->radial_minimum_ratio; s.radial_max=c->radial_maximum_ratio;
    s.perimeter_min=c->perimeter_minimum_ratio; s.perimeter_max=c->perimeter_maximum_ratio;
    s.radius=c->node_radius; s.ground=c->ground_height;
    float display_area=0;
    b2Vec2 display[16];
    for (int i=0; i<17; ++i)
    {
        s.positions[i]=b2Body_GetPosition(rig_id(b,i));
        s.velocities[i]=b2Body_GetLinearVelocity(rig_id(b,i));
        if (!isfinite(s.positions[i].x) || !isfinite(s.positions[i].y) ||
            !isfinite(s.velocities[i].x) || !isfinite(s.velocities[i].y)) return false;
        s.inverse_mass[i]=1.0f/b2Body_GetMass(rig_id(b,i));
    }
    if (dango_constraint_sweep(&s,false)>DANGO_CONSTRAINT_TOLERANCE || dango_self_has_crossing(b)) return false;
    for (int i=0; i<16; ++i)
    {
        b2Vec2 r=b2Sub(s.positions[i],s.positions[16]);
        display[i]=b2MulSV(1+c->node_radius/b2Length(r),r);
    }
    for (int i=0; i<16; ++i) display_area+=b2Cross(display[i],display[(i+1)%16])*.5f;
    return isfinite(display_area) && display_area>=c->minimum_area;
}

static void wake_rig(dango_self_soft_body* b, sleep_probe* p)
{
    p->sleeping=false;
    p->stable_frames=0;
    for (int i=0; i<17; ++i) b2Body_SetAwake(rig_id(b,i),true);
}

static bool consider_sleep(dango_self_soft_body* b, const dango_self_physics_config* c, sleep_probe* p, bool input)
{
    if (!legal(b,c)) return false;
    float speed=0, range=0, shape=0;
    b2Vec2 core=b2Body_GetPosition(b->core_body_id);
    bool supported=false;
    for (int i=0; i<17; ++i)
    {
        b2Vec2 pos=b2Body_GetPosition(rig_id(b,i));
        speed=fmaxf(speed,b2Length(b2Body_GetLinearVelocity(rig_id(b,i))));
        if (i<16 && fabsf(pos.y-c->node_radius-c->ground_height)<.005f) supported=true;
        if (p->stable_frames==0)
        {
            p->minimum[i]=p->maximum[i]=pos;
            if (i<16) p->relative[i]=b2Sub(pos,core);
        }
        p->minimum[i]=b2Min(p->minimum[i],pos);
        p->maximum[i]=b2Max(p->maximum[i],pos);
        range=fmaxf(range,b2Distance(p->minimum[i],p->maximum[i]));
        if (i<16) shape=fmaxf(shape,b2Distance(p->relative[i],b2Sub(pos,core)));
    }
    p->observed_speed=speed; p->observed_range=range; p->observed_shape=shape;
    if (input || !supported || speed>p->speed_threshold || range>.003f || shape>.001f)
    {
        p->stable_frames=0;
        return true;
    }
    ++p->stable_frames;
    if (p->stable_frames>p->longest_stable) p->longest_stable=p->stable_frames;
    if (p->stable_frames>=120)
    {
        b2Vec2 before[17];
        for (int i=0; i<17; ++i) before[i]=b2Body_GetPosition(rig_id(b,i));
        for (int i=0; i<17; ++i) b2Body_SetAwake(rig_id(b,i),false);
        for (int i=0; i<17; ++i)
            if (b2Distance(before[i],b2Body_GetPosition(rig_id(b,i)))!=0) return false;
        p->sleeping=awake_count(b)==0;
    }
    return true;
}

static bool tick(dango_self_soft_body* b, const dango_self_physics_config* c, sleep_probe* p,
    float x, float y, float horizontal, int drag, float dt)
{
    if (p->failed) return false;
    bool changed=p->configured && memcmp(&p->last_config,c,sizeof(*c))!=0;
    p->last_config=*c;
    p->configured=true;
    bool input=horizontal!=0 || drag!=0 || extra_downward!=0 || extra_jump!=0;
    if (p->sleeping && (changed || input || awake_count(b)!=0)) wake_rig(b,p);
    if (p->sleeping)
    {
        // The shared world keeps advancing; a real incoming collider can wake the island.
        if (!legal(b,c)) { p->failed=true; return false; }
        if (ground_candidate) trace_world_step(b->world_id,dt,4);
        else b2World_Step(b->world_id,dt,4);
        if (awake_count(b)!=0)
        {
            wake_rig(b,p);
            if (!dango_self_solve_constraints(b,c) || !dango_self_limit_speed(b,c->maximum_speed))
            { p->failed=true; return false; }
        }
        if (!legal(b,c)) { p->failed=true; return false; }
        return true;
    }
    if (active_step(b,c,x,y,horizontal,drag,dt)!=0 || !consider_sleep(b,c,p,input || changed))
    { p->failed=true; return false; }
    return true;
}

#include "dango_dynamic_review_protocol.h"

static int settle(dango_self_soft_body* b, dango_self_physics_config* c, sleep_probe* p, float x)
{
    b2Vec2 minimum[17],maximum[17];
    p->longest_stable=0;
    trace_force_x=trace_world_dx=trace_projection_dx=0; trace_steps=0;
    trace_velocity_px=trace_world_px=0; pending_fx=0;
    for (int i=0;i<17;++i) trace_ids[i]=rig_id(b,i);
    memset(trace_node_world_dx,0,sizeof(trace_node_world_dx));
    memset(trace_node_projection_dx,0,sizeof(trace_node_projection_dx));
    const char* intervention=getenv("DANGO_SLEEP_DIAGNOSTIC_INTERVENTION");
    diagnostic_balance=intervention && strcmp(intervention,"balance")==0;
    if (intervention && strcmp(intervention,"cold")==0) b2World_EnableWarmStarting(b->world_id,false);
    for (int frame=0; frame<6000 && !p->sleeping; ++frame)
    {
        trace_enabled=frame>=5400;
        CHECK(tick(b,c,p,x,0,0,0,1.0f/60.0f));
        if (frame>=5400) for (int i=0; i<17; ++i)
        {
            b2Vec2 pos=b2Body_GetPosition(rig_id(b,i));
            if (frame==5400) minimum[i]=maximum[i]=pos;
            minimum[i]=b2Min(minimum[i],pos); maximum[i]=b2Max(maximum[i],pos);
        }
    }
    trace_enabled=false;
    diagnostic_balance=false;
    if (!p->sleeping)
    {
        float drift=0;
        for (int i=0; i<17; ++i) drift=fmaxf(drift,b2Distance(minimum[i],maximum[i]));
        printf("resettle_failed speed=%.9g range=%.9g shape=%.9g longest_stable=%d tail600_drift=%.9g core_dx=%.9g\n",
            (double)p->observed_speed,(double)p->observed_range,(double)p->observed_shape,p->longest_stable,
            (double)drift,(double)(maximum[16].x-minimum[16].x));
        printf("trace steps=%d mean_internal_fx=%.9g com_world_dx=%.9g com_projection_dx=%.9g dt=%.9g\n",
            trace_steps,trace_steps ? trace_force_x/trace_steps : 0,trace_world_dx/.36,trace_projection_dx/.36,
            (double)(diagnostic_dt>0 ? diagnostic_dt : 1.0f/60.0f));
        printf("momentum world_px=%.9g constraint_px=%.9g intervention=%s\n",trace_world_px,trace_velocity_px,
            intervention ? intervention : "none");
        for (int i=0;i<17;++i)
        {
            b2Vec2 pos=b2Body_GetPosition(rig_id(b,i)),v=b2Body_GetLinearVelocity(rig_id(b,i));
            printf("node_trace i=%d gap=%.9g vx=%.9g world_dx=%.9g projection_dx=%.9g contacts=%d\n",i,
                (double)(pos.y-c->node_radius-c->ground_height),(double)v.x,trace_node_world_dx[i],trace_node_projection_dx[i],
                b2Body_GetContactCapacity(rig_id(b,i)));
        }
    }
    CHECK(p->sleeping && awake_count(b)==0);
    return 0;
}

static bool pair_near(dango_self_soft_body** bodies, float skin)
{
    for (int k=0; k<2; ++k)
        for (int i=0; i<16; ++i)
            if (evaluate_node_edge(bodies[k],i,bodies[1-k],skin+.005f,false)>0) return true;
    return false;
}

static int pair_run(float skin)
{
    dango_self_physics_config config=configuration();
    void* handle=NULL;
    CHECK(dango_box2d_self_soft_body_create(&config,&handle)==0);
    dango_self_soft_body* lower=handle;
    dango_self_soft_body upper={0};
    upper.world_id=lower->world_id; upper.ground_body_id=lower->ground_body_id; upper.ground_shape_id=lower->ground_shape_id;
    dango_self_create_rig(&upper,&config);
    dango_self_soft_body* bodies[2]={lower,&upper};
    if (ground_candidate) b2Shape_SetFriction(lower->ground_shape_id,0);
    for (int i=0; i<17; ++i) b2Body_SetTransform(rig_id(&upper,i),
        b2Add(b2Body_GetPosition(rig_id(&upper,i)),(b2Vec2){.15f,1.65f}),b2Rot_identity);
    sleep_probe probes[2]={{0},{0}};
    probes[0].speed_threshold=probes[1].speed_threshold=.02f;
    b2Vec2 minimum[2][17],maximum[2][17];
    int slept_at[2]={-1,-1};
    bool both_slept=false;
    bool incoming_woke=false;
    b2Vec2 incoming_before={0};
    int projections_at_rest=0;
    for (int frame=0; frame<36120; ++frame)
    {
        if (frame==36000)
        {
            both_slept=awake_count(lower)==0 && awake_count(&upper)==0;
            incoming_before=b2Body_GetPosition(upper.core_body_id);
            projections_at_rest=evaluation_projection_count;
        }
        bool had_contact=false;
        for (int sub=0; sub<4; ++sub)
        {
            bool near=pair_near(bodies,skin);
            had_contact |= near;
            if (near) for (int k=0; k<2; ++k) wake_rig(bodies[k],&probes[k]);
            for (int k=0; k<2; ++k) if (!probes[k].sleeping)
            {
                dango_self_update_joint_parameters(bodies[k],&config);
                dango_self_apply_internal_forces(bodies[k],&config);
            }
            if (frame>=36000 && frame<36060)
            {
                wake_rig(lower,&probes[0]);
                float direction=incoming_before.x>b2Body_GetPosition(lower->core_body_id).x ? 1.0f : -1.0f;
                b2Body_ApplyForceToCenter(lower->core_body_id,(b2Vec2){direction*1.2f,0},true);
            }
            pair_ground_step(bodies,1.0f/240.0f);
            near=pair_near(bodies,skin);
            had_contact |= near;
            if (near) for (int k=0; k<2; ++k) wake_rig(bodies[k],&probes[k]);
            if (frame>=36000 && near && awake_count(&upper)==17) incoming_woke=true;
            if (!probes[0].sleeping && !probes[1].sleeping)
            {
                CHECK(evaluate_continuous_pair(bodies,&config,skin));
            }
            else for (int k=0; k<2; ++k) if (!probes[k].sleeping) CHECK(pair_ground_solve(bodies[k],&config));
            for (int k=0; k<2; ++k)
            {
                CHECK(dango_self_limit_speed(bodies[k],config.maximum_speed));
                CHECK(legal(bodies[k],&config));
                for (int i=0; i<16; ++i)
                {
                    CHECK(evaluate_node_edge(bodies[k],i,bodies[1-k],skin,false)<=1e-5f);
                    CHECK(b2Distance(b2Body_GetPosition(rig_id(bodies[k],i)),b2Body_GetPosition(bodies[1-k]->core_body_id))>=.17f);
                    for (int j=0; j<16; ++j)
                    {
                        b2Vec2 a=b2Body_GetPosition(rig_id(bodies[k],i));
                        b2Vec2 b=b2Body_GetPosition(rig_id(bodies[k],(i+1)%16));
                        b2Vec2 c=b2Body_GetPosition(rig_id(bodies[1-k],j));
                        b2Vec2 d=b2Body_GetPosition(rig_id(bodies[1-k],(j+1)%16));
                        CHECK(!(b2Cross(b2Sub(b,a),b2Sub(c,a))*b2Cross(b2Sub(b,a),b2Sub(d,a)) < -1e-10f &&
                            b2Cross(b2Sub(d,c),b2Sub(a,c))*b2Cross(b2Sub(d,c),b2Sub(b,c)) < -1e-10f));
                    }
                }
            }
        }
        for (int k=0; k<2; ++k)
        {
            if (!probes[k].sleeping)
            {
                CHECK(consider_sleep(bodies[k],&config,&probes[k],had_contact || frame>=36000));
                if (probes[k].sleeping) slept_at[k]=frame;
            }
            if (frame>=35400 && frame<36000) for (int i=0; i<17; ++i)
            {
                b2Vec2 p=b2Body_GetPosition(rig_id(bodies[k],i));
                if (frame==35400) minimum[k][i]=maximum[k][i]=p;
                minimum[k][i]=b2Min(minimum[k][i],p); maximum[k][i]=b2Max(maximum[k][i],p);
            }
        }
    }
    float drift=0;
    for (int k=0; k<2; ++k) for (int i=0; i<17; ++i) drift=fmaxf(drift,b2Distance(minimum[k][i],maximum[k][i]));
    printf("pair skin=%.3f sleep_frames=%d,%d awake=%d,%d drift=%.9g projections=%d momentum_error=%.9g energy_gain=%.9g\n",
        (double)skin,slept_at[0],slept_at[1],awake_count(lower),awake_count(&upper),(double)drift,
        evaluation_projection_count,(double)evaluation_max_momentum_error,(double)evaluation_max_energy_gain);
    CHECK(drift<.003f && both_slept);
    printf("pair_incoming woke=%d projections=%d displacement=%.9g\n",incoming_woke,
        evaluation_projection_count-projections_at_rest,(double)b2Distance(incoming_before,b2Body_GetPosition(upper.core_body_id)));
    CHECK(incoming_woke && evaluation_projection_count>projections_at_rest);
    CHECK(b2Distance(incoming_before,b2Body_GetPosition(upper.core_body_id))>.01f);
    CHECK(evaluation_projection_count>0 && evaluation_max_momentum_error<1e-5f && evaluation_max_energy_gain<1e-5f);
    int bytes;
    CHECK(dango_box2d_self_soft_body_destroy(handle,&bytes)==0 && bytes==0);
    return 0;
}

static b2Vec2 contour_sample(const b2Vec2* nodes,int sample)
{
    int i=sample/32;
    float t=(float)(sample%32)/32,u=1-t;
    b2Vec2 control[3];
    for (int k=0;k<3;++k)
    {
        b2Vec2 p=nodes[(i+15+k)%16];
        control[k]=b2MulSV(1+.045f/b2Length(p),p);
    }
    return b2Add(b2Add(b2MulSV(.5f*u*u,control[0]),b2MulSV(.5f+t-t*t,control[1])),b2MulSV(.5f*t*t,control[2]));
}

static float contour_distance(const b2Vec2* first,const b2Vec2* second)
{
    b2Vec2 a[512],b[512];
    for (int i=0;i<512;++i) { a[i]=contour_sample(first,i); b[i]=contour_sample(second,i); }
    float maximum=0;
    for (int pass=0;pass<2;++pass) for (int i=0;i<512;++i)
    {
        b2Vec2* from=pass==0 ? a : b;
        b2Vec2* to=pass==0 ? b : a;
        float nearest=INFINITY;
        for (int j=0;j<512;++j)
        {
            b2Vec2 e=b2Sub(to[(j+1)%512],to[j]);
            float t=fmaxf(0,fminf(1,b2Dot(b2Sub(from[i],to[j]),e)/b2LengthSquared(e)));
            nearest=fminf(nearest,b2Distance(from[i],b2MulAdd(to[j],t,e)));
        }
        maximum=fmaxf(maximum,nearest);
    }
    return maximum;
}

static void contour_metrics(const b2Vec2* nodes,float* width,float* height,float* area)
{
    b2Vec2 minimum={INFINITY,INFINITY},maximum={-INFINITY,-INFINITY};
    *area=0;
    for (int i=0;i<512;++i)
    {
        b2Vec2 p=contour_sample(nodes,i),q=contour_sample(nodes,(i+1)%512);
        minimum=b2Min(minimum,p); maximum=b2Max(maximum,p); *area+=.5f*b2Cross(p,q);
    }
    *width=maximum.x-minimum.x; *height=maximum.y-minimum.y;
}

static bool contour_gate(const b2Vec2* baseline,const b2Vec2* candidate,bool report)
{
    for (int i=0;i<16;++i)
        if (!isfinite(baseline[i].x) || !isfinite(baseline[i].y) ||
            !isfinite(candidate[i].x) || !isfinite(candidate[i].y) ||
            b2LengthSquared(baseline[i])<=0 || b2LengthSquared(candidate[i])<=0) return false;
    float bw,bh,ba,cw,ch,ca;
    contour_metrics(baseline,&bw,&bh,&ba); contour_metrics(candidate,&cw,&ch,&ca);
    float distance=contour_distance(baseline,candidate);
    float width=fabsf(cw/bw-1),height=fabsf(ch/bh-1),area=fabsf(ca/ba-1);
    bool pass=isfinite(distance) && isfinite(width) && isfinite(height) && isfinite(area) &&
        ba>0 && ca>0 && distance<=.003f && width<=.005f && height<=.005f && area<=.01f;
    if (report) printf("contour_gate distance=%.9g width_relative=%.9g height_relative=%.9g area_relative=%.9g pass=%d\n",
        (double)distance,(double)width,(double)height,(double)area,pass);
    return pass;
}

static int shape_metric_tests(void)
{
    b2Vec2 baseline[16],candidate[16];
    for (int i=0;i<16;++i)
    {
        float angle=6.28318530718f*(float)i/16;
        baseline[i]=(b2Vec2){.68f*cosf(angle),.60f*sinf(angle)};
    }
    CHECK(contour_gate(baseline,baseline,false));
    for (int i=0;i<16;++i) candidate[i]=baseline[(i+7)%16];
    CHECK(contour_gate(baseline,candidate,false));
    for (int i=0;i<16;++i) candidate[i]=(b2Vec2){baseline[i].x*1.02f,baseline[i].y};
    CHECK(!contour_gate(baseline,candidate,false));
    for (int i=0;i<16;++i) candidate[i]=b2MulSV(1.02f,baseline[i]);
    CHECK(!contour_gate(baseline,candidate,false));
    for (int i=0;i<16;++i) candidate[i]=baseline[i];
    candidate[4].y-=.08f;
    CHECK(!contour_gate(baseline,candidate,false));
    candidate[4].y=NAN;
    CHECK(!contour_gate(baseline,candidate,false));
    puts("PASS: contour identity/cyclic relabel; reject width stretch, scale and local dent");
    return 0;
}

static int shape_isolation(bool enforce_contour,bool inspect_radial)
{
    const int frames[4]={899,2084,5999,35999};
    const char* names[4]={"production","cached_sleep","ground_awake","ground_sleep"};
    b2Vec2 samples[4][4][16];
    for (int pass=0;pass<4;++pass)
    {
        ground_variant=pass>=2 ? 4 : 0; ground_candidate=pass>=2; preserve_joint_impulses=pass!=0;
        dango_self_physics_config config=configuration();
        void* handle=NULL;
        CHECK(dango_box2d_self_soft_body_create(&config,&handle)==0);
        dango_self_soft_body* body=handle;
        if (inspect_radial)
        {
            for (int i=0;i<17;++i) trace_ids[i]=rig_id(body,i);
            for (int i=0;i<16;++i) radial_trace_joints[i]=body->radial_joint_ids[i];
        }
        if (ground_candidate)
        {
            for (int i=0;i<17;++i) trace_ids[i]=rig_id(body,i);
            ground_candidate_shape=body->ground_shape_id; b2Shape_SetFriction(ground_candidate_shape,0);
        }
        sleep_probe probe={0}; probe.speed_threshold=.02f;
        for (int frame=0;frame<36000;++frame)
        {
            radial_trace_enabled=inspect_radial && (frame==899 || frame==2084 || frame==5999 || frame==35999);
            radial_world_sampled=false;
            bool success=(pass==0 || pass==2) ? active_step(body,&config,0,0,0,0,1.0f/60.0f)==0 :
                tick(body,&config,&probe,0,0,0,0,1.0f/60.0f);
            if (!success) { fprintf(stderr,"shape isolation failed pass=%s frame=%d\n",names[pass],frame); return 1; }
            CHECK(legal(body,&config));
            if (radial_trace_enabled)
            {
                printf("radial_trace pass=%s frame=%d sampled=%d sleeping=%d\n",names[pass],frame+1,radial_world_sampled,probe.sleeping);
                if (radial_world_sampled) for (int i=0;i<16;++i)
                {
                    b2Vec2 delta=b2Sub(b2Body_GetPosition(body->node_body_ids[i]),b2Body_GetPosition(body->core_body_id));
                    b2Vec2 axis=b2Normalize(delta);
                    float speed=b2Dot(axis,b2Sub(b2Body_GetLinearVelocity(body->node_body_ids[i]),b2Body_GetLinearVelocity(body->core_body_id)));
                    printf("radial_node pass=%s frame=%d index=%d length_before=%.9g length_after=%.9g speed_before=%.9g speed_after=%.9g impulse=%.9g axis_change=%.9g\n",
                        names[pass],frame+1,i,(double)radial_world_length[i],(double)b2Length(delta),
                        (double)radial_world_speed[i],(double)speed,(double)radial_world_impulse[i],(double)b2Distance(axis,radial_world_axis[i]));
                }
            }
            for (int slot=0;slot<4;++slot) if (frame==frames[slot])
            {
                b2Vec2 core=b2Body_GetPosition(body->core_body_id);
                for (int i=0;i<16;++i) samples[pass][slot][i]=b2Sub(b2Body_GetPosition(body->node_body_ids[i]),core);
                float width,height,area;
                contour_metrics(samples[pass][slot],&width,&height,&area);
                printf("shape_stage pass=%s frame=%d width=%.9g height=%.9g area=%.9g sleeping=%d\n",
                    names[pass],frame+1,(double)width,(double)height,(double)area,probe.sleeping);
                // Export every checkpoint for the isolated WPF shape review.
                if (pass==0 || pass==3)
                    for (int i=0;i<16;++i)
                        printf("shape_node pass=%s frame=%d index=%d x=%.9g y=%.9g\n",
                            names[pass],frame+1,i,(double)samples[pass][slot][i].x,(double)samples[pass][slot][i].y);
            }
        }
        int bytes;
        radial_trace_enabled=false;
        CHECK(dango_box2d_self_soft_body_destroy(handle,&bytes)==0 && bytes==0);
    }
    for (int pass=0;pass<4;++pass) for (int slot=0;slot<4;++slot)
    {
        printf("shape_isolation pass=%s frame=%d vs_same_time_production=%.9g vs_own_900=%.9g\n",names[pass],frames[slot]+1,
            (double)contour_distance(samples[pass][slot],samples[0][slot]),(double)contour_distance(samples[pass][slot],samples[pass][0]));
    }
    printf("shape_isolation final_ground_vs_cached_sleep=%.9g final_ground_sleep_vs_awake=%.9g\n",
        (double)contour_distance(samples[3][3],samples[1][3]),(double)contour_distance(samples[3][3],samples[2][3]));
    if (enforce_contour)
    {
        bool passed=true;
        for (int slot=0;slot<4;++slot)
        {
            printf("contour_stage frame=%d ",frames[slot]+1);
            if (!contour_gate(samples[0][slot],samples[3][slot],true)) passed=false;
        }
        puts("Sampled contour engineering screen only; desktop acceptance remains required");
        CHECK(passed);
        return 0;
    }
    puts("DIAGNOSTIC ONLY: no shape acceptance changed");
    return 0;
}

static int shape_comparison(int variant,bool cached_joints)
{
    b2Vec2 baseline[16],candidate[16];
    float maximum=0;
    for (int pass=0;pass<2;++pass)
    {
        ground_variant=pass==0 ? 0 : variant; ground_candidate=pass!=0; preserve_joint_impulses=pass!=0 && cached_joints;
        dango_self_physics_config config=configuration();
        void* handle=NULL;
        CHECK(dango_box2d_self_soft_body_create(&config,&handle)==0);
        dango_self_soft_body* body=handle;
        if (ground_candidate)
        {
            for (int i=0;i<17;++i) trace_ids[i]=rig_id(body,i);
            ground_candidate_shape=body->ground_shape_id;
            if (ground_variant!=5) b2Shape_SetFriction(ground_candidate_shape,0);
        }
        sleep_probe probe={0}; probe.speed_threshold=.02f;
        for (int frame=0;frame<36000;++frame)
        {
            if (pass==0) CHECK(active_step(body,&config,0,0,0,0,1.0f/60.0f)==0);
            else CHECK(tick(body,&config,&probe,0,0,0,0,1.0f/60.0f));
            CHECK(legal(body,&config));
        }
        b2Vec2 core=b2Body_GetPosition(body->core_body_id);
        for (int i=0;i<16;++i)
        {
            b2Vec2 local=b2Sub(b2Body_GetPosition(body->node_body_ids[i]),core);
            if (pass==0) baseline[i]=local;
            else
            {
                candidate[i]=local;
                float difference=b2Distance(local,baseline[i]);
                maximum=fmaxf(maximum,difference);
                printf("shape_compare node=%d difference=%.9g\n",i,(double)difference);
            }
        }
        int bytes;
        CHECK(dango_box2d_self_soft_body_destroy(handle,&bytes)==0 && bytes==0);
    }
    printf("shape_compare maximum=%.9g limit=0.003\n",(double)maximum);
    float cyclic=INFINITY;
    int best_shift=-1;
    for (int shift=0;shift<16;++shift)
    {
        float difference=0;
        for (int i=0;i<16;++i) difference=fmaxf(difference,b2Distance(candidate[i],baseline[(i+shift)%16]));
        if (difference<cyclic) { cyclic=difference; best_shift=shift; }
    }
    printf("shape_compare cyclic_shift=%d cyclic_maximum=%.9g (diagnostic only, raw gate retained)\n",best_shift,(double)cyclic);
    printf("shape_compare quadratic_contour_distance=%.9g (diagnostic sampled polyline)\n",(double)contour_distance(baseline,candidate));
    CHECK(maximum<=.003f);
    return 0;
}

#include "dango_stack_evaluation.h"

int main(int argc, char** argv)
{
    if (argc==2 && strcmp(argv[1],"contact-friction-tests")==0) return contact_friction_tests();
    if ((argc==4 || argc==5) &&
        (strcmp(argv[1],"stack-diagnostic")==0 || strcmp(argv[1],"stack-check")==0))
    {
        char* end=NULL;
        float ratio=argc==5 ? strtof(argv[4],&end) : 0;
        if (argc==5 && (end==argv[4] || *end!='\0')) return 9;
        return stack_diagnostic(atoi(argv[2]),(float)atof(argv[3]),strcmp(argv[1],"stack-check")==0,ratio);
    }
    if (argc==2 && strcmp(argv[1],"interactive-review")==0) return dynamic_review(false);
    if (argc==2 && strcmp(argv[1],"interactive-review-test")==0) return dynamic_review(true);
    const char* cache_reset=getenv("DANGO_DIAGNOSTIC_CACHE_RESET");
    if (cache_reset)
    {
        if (strcmp(cache_reset,"spring")==0) diagnostic_cache_reset=1;
        else if (strcmp(cache_reset,"limits")==0) diagnostic_cache_reset=2;
        else return 9;
        printf("DIAGNOSTIC ONLY: ground candidate cache reset=%s\n",cache_reset);
    }
    if (argc==2 && strcmp(argv[1],"shape-metric-tests")==0) return shape_metric_tests();
    if (argc==2 && strcmp(argv[1],"shape-cache-trace")==0) return shape_isolation(false,true);
    if (argc==2 && strcmp(argv[1],"shape-contour")==0) return shape_isolation(true,false);
    if (argc==2 && strcmp(argv[1],"shape-isolation")==0) return shape_isolation(false,false);
    if (argc==2 && strcmp(argv[1],"shape")==0) return shape_comparison(4,true);
    if (argc==2 && strcmp(argv[1],"shape5")==0) return shape_comparison(5,true);
    if (argc==2 && strcmp(argv[1],"shape-original-joints")==0) return shape_comparison(4,false);
    const char* ground_text=getenv("DANGO_GROUND_CONTACT_CANDIDATE");
    ground_variant=ground_text ? atoi(ground_text) : 0;
    if (ground_variant<0 || ground_variant>5) return 9;
    ground_candidate=ground_variant!=0;
    if (ground_candidate && argc>5 && ground_variant!=4) return 9; // Only the accepted candidate is paired.
    if (argc>5) { preserve_joint_impulses=true; return pair_run((float)atof(argv[5])); }
    float x=argc>1 ? (float)atof(argv[1]) : 0;
    dango_self_physics_config config=configuration();
    bool pressure=argc>3 && atof(argv[3])>=0;
    preserve_joint_impulses=argc>4 && atoi(argv[4])!=0;
    if (pressure) { config.angle_strength=0; config.drag_stiffness=40; config.drag_maximum_force=30; config.top_inward_force=(float)atof(argv[3]); }
    void* handle=NULL;
    CHECK(dango_box2d_self_soft_body_create(&config, &handle)==0);
    dango_self_soft_body* body=handle;
    if (ground_candidate)
    {
        for (int i=0;i<17;++i) trace_ids[i]=rig_id(body,i);
        ground_candidate_shape=body->ground_shape_id;
        if (ground_variant!=5) b2Shape_SetFriction(ground_candidate_shape,0);
    }
    sleep_probe probe={0};
    probe.speed_threshold=argc>2 ? (float)atof(argv[2]) : .02f;
    int sleep_frame=-1;
    b2Vec2 asleep_positions[17];
    for (int frame=0; frame<36000; ++frame)
    {
        if (!probe.sleeping)
        {
            int drag=pressure && frame>=300 && frame<420;
            CHECK(tick(body,&config,&probe,x,pressure ? 1.6666667f : 0,0,drag,pressure ? .0166667f : 1.0f/60.0f));
            if (probe.sleeping)
            {
                sleep_frame=frame;
                for (int i=0; i<17; ++i) asleep_positions[i]=b2Body_GetPosition(rig_id(body,i));
            }
        }
        else
        {
            if (pressure && frame==300) { wake_rig(body,&probe); --frame; continue; }
            CHECK(tick(body,&config,&probe,x,0,0,0,1.0f/60.0f));
            CHECK(legal(body,&config));
            CHECK(awake_count(body)==0);
            for (int i=0; i<17; ++i) CHECK(b2Distance(asleep_positions[i],b2Body_GetPosition(rig_id(body,i)))==0);
        }
    }
    printf("candidate x=%.3f sleep_frame=%d awake=%d/17 longest_stable=%d speed=%.9g range=%.9g shape=%.9g\n",
        (double)x,sleep_frame,awake_count(body),probe.longest_stable,(double)probe.observed_speed,
        (double)probe.observed_range,(double)probe.observed_shape);
    CHECK(awake_count(body)==0);
    const char* dt_text=getenv("DANGO_SLEEP_DIAGNOSTIC_DT");
    if (dt_text) diagnostic_dt=(float)atof(dt_text);
    b2Vec2 before=b2Body_GetPosition(body->core_body_id);
    for (int i=0; i<30; ++i) { CHECK(tick(body,&config,&probe,x,0,1,0,1.0f/60.0f)); CHECK(awake_count(body)==17); CHECK(legal(body,&config)); }
    CHECK(b2Body_GetPosition(body->core_body_id).x-before.x>.01f);
    printf("force_wake dx=%.9g\n", (double)(b2Body_GetPosition(body->core_body_id).x-before.x));
    CHECK(settle(body,&config,&probe,x)==0);

    // Parameter changes must wake without rebuilding or snapping the rig.
    config.top_inward_force=config.top_inward_force==.5f ? .4f : .5f;
    CHECK(tick(body,&config,&probe,x,0,0,0,1.0f/60.0f));
    CHECK(!probe.sleeping && awake_count(body)==17);
    CHECK(settle(body,&config,&probe,x)==0);

    // Real Box2D collider, not an injected awake flag or scripted rig displacement.
    float right=-INFINITY;
    b2Vec2 core=b2Body_GetPosition(body->core_body_id);
    for (int i=0; i<16; ++i) right=fmaxf(right,b2Body_GetPosition(body->node_body_ids[i]).x);
    b2BodyDef projectile_def=b2DefaultBodyDef();
    projectile_def.type=b2_dynamicBody; projectile_def.gravityScale=0; projectile_def.fixedRotation=true;
    projectile_def.position=(b2Vec2){right+.35f,core.y}; projectile_def.linearVelocity=(b2Vec2){-1,0};
    b2BodyId projectile=b2CreateBody(body->world_id,&projectile_def);
    b2ShapeDef projectile_shape=b2DefaultShapeDef();
    projectile_shape.filter.categoryBits=1; projectile_shape.filter.maskBits=2;
    projectile_shape.density=3;
    b2Circle circle={{0,0},.15f};
    b2CreateCircleShape(projectile,&projectile_shape,&circle);
    CHECK(awake_count(body)==0);
    int woke_at=-1;
    for (int frame=0; frame<120; ++frame)
    {
        CHECK(tick(body,&config,&probe,x,0,0,0,1.0f/60.0f));
        if (woke_at<0 && awake_count(body)>0) { woke_at=frame; CHECK(awake_count(body)==17); }
    }
    CHECK(woke_at>=0);
    CHECK(b2Distance(core,b2Body_GetPosition(body->core_body_id))>.001f);
    printf("collision_wake frame=%d displacement=%.9g\n",woke_at,(double)b2Distance(core,b2Body_GetPosition(body->core_body_id)));
    b2DestroyBody(projectile);
    CHECK(settle(body,&config,&probe,x)==0);

    // Lower both the actual floor and the self solver floor; no hidden old support.
    before=b2Body_GetPosition(body->core_body_id);
    config.ground_height-=1;
    b2Body_SetTransform(body->ground_body_id,(b2Vec2){0,config.ground_height-.08f},b2Rot_identity);
    CHECK(tick(body,&config,&probe,x,0,0,0,1.0f/60.0f));
    CHECK(awake_count(body)==17 && !probe.sleeping);
    for (int i=0; i<30; ++i) CHECK(tick(body,&config,&probe,x,0,0,0,1.0f/60.0f));
    CHECK(before.y-b2Body_GetPosition(body->core_body_id).y>.1f);
    printf("support_removed fall=%.9g\n",(double)(before.y-b2Body_GetPosition(body->core_body_id).y));
    CHECK(settle(body,&config,&probe,x)==0);

    // A sleeping invalid rig must fault rather than qualify, project, or respawn.
    if (ground_variant>=4)
    {
        before=b2Body_GetPosition(body->core_body_id);
        extra_jump=1;
        CHECK(tick(body,&config,&probe,x,0,0,0,1.0f/60.0f));
        extra_jump=0;
        CHECK(awake_count(body)==17 && !probe.sleeping);
        float peak=0;
        for (int i=0;i<60;++i)
        {
            CHECK(tick(body,&config,&probe,x,0,0,0,1.0f/60.0f));
            peak=fmaxf(peak,b2Body_GetPosition(body->core_body_id).y-before.y);
        }
        CHECK(peak>.05f);
        CHECK(settle(body,&config,&probe,x)==0);
        before=b2Body_GetPosition(body->core_body_id);
        extra_downward=1;
        for (int i=0;i<30;++i) CHECK(tick(body,&config,&probe,x,0,0,0,1.0f/60.0f));
        extra_downward=0;
        float compression=before.y-b2Body_GetPosition(body->core_body_id).y;
        CHECK(compression>.002f);
        CHECK(settle(body,&config,&probe,x)==0);
        before=b2Body_GetPosition(body->core_body_id);
        for (int i=0;i<60;++i) CHECK(tick(body,&config,&probe,x,0,0,1,1.0f/60.0f));
        float drag_distance=b2Distance(before,b2Body_GetPosition(body->core_body_id));
        CHECK(drag_distance>.1f && !probe.sleeping);
        CHECK(settle(body,&config,&probe,x)==0);
        printf("input_release jump_peak=%.9g down_compression=%.9g drag_distance=%.9g\n",(double)peak,(double)compression,(double)drag_distance);
    }
    b2Vec2 original=b2Body_GetPosition(body->node_body_ids[0]);
    b2Body_SetTransform(body->node_body_ids[0],b2Body_GetPosition(body->core_body_id),b2Rot_identity);
    CHECK(!tick(body,&config,&probe,x,0,0,0,1.0f/60.0f));
    CHECK(probe.failed);
    b2Body_SetTransform(body->node_body_ids[0],original,b2Rot_identity);
    CHECK(!tick(body,&config,&probe,x,0,1,0,1.0f/60.0f));
    puts("PASS sleep/no-snap/force/parameter/collision/support-removal/invalid-latch");
    if (ground_candidate) printf("ground variant=%d maximum_velocity_energy_gain=%.9g\n",ground_variant,(double)ground_max_velocity_energy_gain);
    int bytes;
    CHECK(dango_box2d_self_soft_body_destroy(handle,&bytes)==0 && bytes==0);
    return 0;
}

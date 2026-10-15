// Isolated shared-world experiment. Does not replace dango_box2d.dll.
#include <math.h>
#include <stddef.h>
#include <stdlib.h>
#include <string.h>
#include "dango_self_physics_internal.h"
#include "dango_physics_module.h"
#include "dango_display_contact.h"
#if defined(DANGO_MATERIAL_IDLE_TRACE)
#include <stddef.h>
#include "dango_material_idle_trace_format.h"
static void dango_it_mode_event(const void*, bool, bool);
static void dango_it_state_event(int, const void*);
#define DANGO_IDLE_TRACE_MODE_EVENT(report, built, solved) dango_it_mode_event(report, built, solved)
#define DANGO_IDLE_TRACE_STATE(index, state) dango_it_state_event(index, state)
#endif
#if defined(DANGO_MATERIAL_CONTACT_CANDIDATE)
#include "dango_material_contact_adapter.h"
#endif
#if defined(DANGO_MATERIAL_IDLE_TRACE)
#include "dango_material_idle_trace.h"
#endif
#include "dango_static_platform.h"
#include "dango_round_ball.h"
#include "dango_furniture_collection_types.h"
#if defined(DANGO_REST_SLEEP)
#include "dango_rest_sleep.h"
#define COLLISION_RESTING(p,r) dango_rest_is(&(p)->rest,(r))
#define COLLISION_REST_WAKE_ALL(p) ((p)->rest.wake_all=true)
#else
#define COLLISION_RESTING(p,r) false
#define COLLISION_REST_WAKE_ALL(p) ((void)0)
#endif

_Static_assert(sizeof(dango_ball_definition)==64,"Round-ball definition ABI layout");
_Static_assert(sizeof(dango_ball_state)==88,"Round-ball state ABI layout");
_Static_assert(sizeof(dango_ball_contact)==104,"Round-ball contact ABI layout");
_Static_assert(sizeof(dango_ball_snapshot)==312,"Round-ball snapshot ABI layout");
_Static_assert(offsetof(dango_round_ball_frame,definition)==312,"Round-ball definition offset");
_Static_assert(offsetof(dango_round_ball_frame,error_code)==376,"Round-ball error offset");
_Static_assert(offsetof(dango_round_ball_frame,entity_ids)==392,"Round-ball IDs offset");
_Static_assert(offsetof(dango_round_ball_frame,states)==404,"Round-ball states offset");
_Static_assert(sizeof(dango_round_ball_frame)==912,"Round-ball whole-frame ABI layout");
_Static_assert(sizeof(dango_furniture_probe)==32,"Furniture probe ABI layout");
_Static_assert(sizeof(dango_furniture_token)==32,"Furniture token ABI layout");
_Static_assert(sizeof(dango_furniture_frame)==1936,"Furniture frame ABI layout");

typedef struct dango_native_ball
{
    dango_ball_definition definition;
    dango_ball_state state;
    double sleep_seconds;
    double initial_vx,initial_vy,initial_omega;
    bool floor_touch,dango_touch,wall_touch[2];
} dango_native_ball;

typedef struct dango_collection_item
{
    uint64_t instance;
    dango_furniture_definition definition;
    dango_native_ball ball;
    dango_ball_contact dango_contact,surface_contact;
    bool platform_touch[DANGO_FURNITURE_COLLECTION_MAX];
} dango_collection_item;

#if defined(DANGO_PHYSICS_LAB)
#include "dango_physics_lab.h"
#endif

typedef struct collision_review
{
    dango_self_soft_body* bodies[DANGO_PHYSICS_MAX_BODIES];
    dango_self_soft_body additional[DANGO_PHYSICS_MAX_BODIES-1];
    dango_self_physics_config config;
    int count, capacity, error, contact_steps, disable_contact_for_test, disable_damping_for_test, legacy_interaction_flags;
    int active[DANGO_PHYSICS_MAX_BODIES], entity_ids[DANGO_PHYSICS_MAX_BODIES], next_entity_id;
    float anchors[DANGO_PHYSICS_MAX_BODIES*2];
    float snapshot[DANGO_PHYSICS_MAX_BODIES*42];
    b2ShapeId horizontal_walls[2];
    bool horizontal_bounds_enabled;
    float horizontal_left, horizontal_right;
    float preview_jump_speed_scale;
#if defined(DANGO_MATERIAL_CONTACT_CANDIDATE)
    dango_material_contact_diagnostic candidate_diagnostic;
#endif
#if defined(DANGO_MATERIAL_IDLE_TRACE)
    dango_it_context idle_trace;
#endif
    bool platform_enabled;
    dango_static_platform platform;
    uint64_t world_generation,platform_instance,next_platform_instance,next_furniture_token,last_applied_furniture_token;
    bool furniture_prepared;
    int prepared_kind;
    dango_furniture_token prepared_token;
    dango_static_platform prepared_platform;
    float prepared_platform_contacts[DANGO_PHYSICS_MAX_BODIES][6];
    bool prepared_grounded[DANGO_PHYSICS_MAX_BODIES];
    float platform_contacts[DANGO_PHYSICS_MAX_BODIES][6];
    float platform_snapshot[DANGO_PHYSICS_MAX_BODIES][6];
    bool ball_scene;
    int next_ball_id;
    uint64_t scene_revision,physics_step;
    dango_native_ball ball;
    dango_round_ball_frame frame_snapshot;
    dango_ball_contact ball_dango_contact,ball_floor_contact;
    dango_ball_geometry ball_birth_geometry[DANGO_PHYSICS_MAX_BODIES];
    bool collection_mode,collection_prepared;
    int collection_count,collection_prepared_kind,collection_prepared_slot;
    uint64_t collection_next_instance;
    dango_furniture_token collection_token;
    dango_collection_item collection[DANGO_FURNITURE_COLLECTION_MAX],collection_candidate;
    bool collection_prepared_grounded[DANGO_PHYSICS_MAX_BODIES];
    bool collection_pair_touch[DANGO_FURNITURE_COLLECTION_MAX][DANGO_FURNITURE_COLLECTION_MAX];
    float collection_support_friction[DANGO_PHYSICS_MAX_BODIES];
    dango_furniture_collection_frame collection_snapshot;
#if defined(DANGO_REST_SLEEP)
    dango_rest_state rest;
#endif
#ifdef DANGO_ROUND_BALL_CONTRACT_TESTS
    int test_ball_fault_mode;
#endif
#ifdef DANGO_STATIC_PLATFORM_CONTRACT_TESTS
    int test_platform_collapse_substep;
#endif
#if defined(DANGO_PHYSICS_LAB)
    bool lab_call;
    dango_lab_state lab[DANGO_PHYSICS_MAX_BODIES];
#endif
    dango_self_physics_config body_config[DANGO_PHYSICS_MAX_BODIES];
} collision_review;

static bool collection_refresh(collision_review* p);
static bool collection_substep(collision_review* p,double dt,int* touched);
static void collection_publish(collision_review* p);
static int collection_reset(collision_review* p);
static void collection_begin(collision_review* p);
static void collision_furniture_read_core(const collision_review* p,dango_furniture_frame* frame);

#ifdef DANGO_ROUND_BALL_CONTRACT_TESTS
static int round_ball_owner_count;
#endif

// Configuration of the rig that owns body; world-level fields are equal in every copy.
static const dango_self_physics_config* collision_body_config(const collision_review* p,const dango_self_soft_body* body)
{
    for (int r=0;r<p->capacity;++r) if (p->bodies[r]==body) return &p->body_config[r];
    return &p->config;
}

static void collision_ball_geometry(const collision_review* p,int slot,
    double x,double y,double radius,dango_ball_geometry* geometry)
{
    *geometry=(dango_ball_geometry){.node_radius=p->body_config[slot].node_radius,.ball_x=x,.ball_y=y,.ball_radius=radius};
    b2Vec2 core=b2Body_GetPosition(p->bodies[slot]->core_body_id);
    geometry->core[0]=core.x; geometry->core[1]=core.y;
    for (int n=0;n<16;++n)
    {
        b2Vec2 point=b2Body_GetPosition(p->bodies[slot]->node_body_ids[n]);
        geometry->nodes[2*n]=point.x; geometry->nodes[2*n+1]=point.y;
    }
}

static void collision_ball_floor_range(const collision_review* p,double* left,double* right)
{
    double center=b2Body_GetPosition(p->bodies[0]->ground_body_id).x;
    *left=(4.0/3)*(p->horizontal_bounds_enabled ? p->horizontal_left : center-8);
    *right=(4.0/3)*(p->horizontal_bounds_enabled ? p->horizontal_right : center+8);
}

static bool collision_ball_circle_contained(const collision_review* p,double x,double y,double radius,
    double left,double right)
{
    return isfinite(x) && isfinite(y) && x-radius>=left && x+radius<=right &&
        y-radius>=p->config.ground_height;
}

static bool collision_ball_rig_contained(const collision_review* p,const dango_ball_geometry* geometry,
    double left,double right)
{
    /* Match the existing raw-node bounds check and pinned Box2D linear slop.
       This is world containment, separate from the visible circle contact. */
    double slop=.005*b2GetLengthUnitsPerMeter();
    left*=.75; right*=.75;
    for (int n=0;n<16;++n)
    {
        double x=geometry->nodes[2*n],y=geometry->nodes[2*n+1];
        if (!isfinite(x) || !isfinite(y) || x-geometry->node_radius<left-slop ||
            x+geometry->node_radius>right+slop || y-geometry->node_radius<p->config.ground_height-slop) return false;
    }
    return true;
}

static bool collision_ball_births_contained(const collision_review* p,double left,double right,bool current)
{
    if (p->ball.state.enabled &&
        (!collision_ball_circle_contained(p,p->ball.definition.x,p->ball.definition.y,p->ball.definition.radius,left,right) ||
         (current && !collision_ball_circle_contained(p,p->ball.state.x,p->ball.state.y,p->ball.definition.radius,left,right)))) return false;
    for (int r=0;r<p->capacity;++r) if (p->active[r])
    {
        if (!collision_ball_rig_contained(p,&p->ball_birth_geometry[r],left,right)) return false;
        if (current)
        {
            dango_ball_geometry geometry;
            collision_ball_geometry(p,r,0,0,.4,&geometry);
            if (!collision_ball_rig_contained(p,&geometry,left,right)) return false;
        }
    }
    return true;
}

static bool collision_ball_reset_geometry_valid(const collision_review* p,bool births)
{
    double left,right; collision_ball_floor_range(p,&left,&right);
    if (!collision_ball_births_contained(p,left,right,!births)) return false;
    if (!p->ball.state.enabled) return true;
    for (int r=0;r<p->capacity;++r) if (p->active[r])
    {
        dango_ball_geometry geometry;
        if (births) geometry=p->ball_birth_geometry[r];
        else collision_ball_geometry(p,r,0,0,.4,&geometry);
        geometry.ball_x=p->ball.definition.x; geometry.ball_y=p->ball.definition.y;
        geometry.ball_radius=p->ball.definition.radius;
        dango_ball_geometry_result contact;
        if (!dango_ball_geometry_contact(&geometry,&contact) || contact.gap<0) return false;
    }
    return true;
}

static bool collision_ball_valid(const collision_review* p)
{
    const dango_ball_state* s=&p->ball.state;
    return !s->enabled || (isfinite(s->x) && isfinite(s->y) && isfinite(s->vx) && isfinite(s->vy) &&
        isfinite(s->angle) && isfinite(s->omega) && fabs(s->x)<=1024 && fabs(s->y)<=1024 &&
        hypot(s->vx,s->vy)<=12 && fabs(s->omega)<=40);
}

static void collision_ball_publish(collision_review* p)
{
    if (p->collection_prepared && (p->collection_token.scene_revision!=p->scene_revision ||
        p->collection_token.physics_step!=p->physics_step)) p->collection_prepared=false;
    if (p->furniture_prepared && (p->prepared_token.scene_revision!=p->scene_revision ||
        p->prepared_token.physics_step!=p->physics_step)) p->furniture_prepared=false;
    p->ball.state.scene_revision=p->scene_revision; p->ball.state.step=p->physics_step;
    p->ball_dango_contact.scene_revision=p->scene_revision; p->ball_dango_contact.step=p->physics_step;
    p->ball_floor_contact.scene_revision=p->scene_revision; p->ball_floor_contact.step=p->physics_step;
    memset(&p->frame_snapshot,0,sizeof p->frame_snapshot);
    p->frame_snapshot.snapshot=(dango_ball_snapshot){.scene_revision=p->scene_revision,.step=p->physics_step,
        .ball=p->ball.state,.dango_contact=p->ball_dango_contact,.floor_contact=p->ball_floor_contact};
    if (p->ball.state.enabled) p->frame_snapshot.definition=p->ball.definition;
    p->frame_snapshot.error_code=p->error; p->frame_snapshot.contact_steps=p->contact_steps;
    p->frame_snapshot.body_count=p->count;
    int output=0;
    for (int r=0;r<p->capacity;++r) if (p->active[r])
    {
        p->frame_snapshot.entity_ids[output]=p->entity_ids[r];
        memcpy(p->frame_snapshot.states+42*output,p->snapshot+42*r,42*sizeof(float));
        // Scene mutations can refresh grounding without stepping or changing
        // the legacy snapshot. Publish that current eligibility in this DTO.
        p->frame_snapshot.states[42*output+39]=p->bodies[r]->grounded ? 1.0f : 0.0f;
        ++output;
    }
    if (p->collection_mode) collection_publish(p);
}

static inline void collision_ball_wake(collision_review* p)
{ p->ball.state.sleeping=0; p->ball.sleep_seconds=0; }

/* Fixed-boundary point contact. The lever arm is -radius*n, and t=(-ny,nx),
   so a positive tangent impulse changes omega by -radius*Jt/I. */
static bool collision_ball_boundary(collision_review* p,double nx,double ny,double gap,
    bool* was_touching,dango_ball_contact* diagnostics)
{
    dango_ball_state* state=&p->ball.state;
    const dango_ball_definition* definition=&p->ball.definition;
    bool touching=gap<=.001;
    if (diagnostics) { diagnostics->touching=touching ? 1 : 0; diagnostics->nx=nx; diagnostics->ny=ny; diagnostics->gap=gap; }
    if (!touching) { *was_touching=false; return true; }
    double tx=-ny,ty=nx,inverse_mass=1/definition->mass;
    double inertia=.5*definition->mass*definition->radius*definition->radius;
    double vn=nx*state->vx+ny*state->vy;
    double speed_before=hypot(state->vx,state->vy);
    double vt=tx*state->vx+ty*state->vy-definition->radius*state->omega;
    double target=dango_ball_restitution_target(-vn,definition->floor_restitution,!*was_touching);
    *was_touching=true;
    dango_ball_impulse impulse;
    if (!dango_ball_contact_impulse(inverse_mass,0,inverse_mass+definition->radius*definition->radius/inertia,
        vn,vt,target,definition->floor_friction,&impulse)) return false;
    state->vx+=inverse_mass*(nx*impulse.normal+tx*impulse.tangent);
    state->vy+=inverse_mass*(ny*impulse.normal+ty*impulse.tangent);
    state->omega-=definition->radius*impulse.tangent/inertia;
    double correction=fmax(0,.0001-gap);
    state->x+=nx*correction; state->y+=ny*correction;
    if (diagnostics)
    {
        if (diagnostics->normal_impulse==0 && diagnostics->tangent_impulse==0 &&
            (impulse.normal>0 || impulse.tangent!=0))
        { diagnostics->normal_speed_before=vn; diagnostics->tangent_speed_before=vt; diagnostics->ball_speed_before=speed_before; }
        diagnostics->touching=1; diagnostics->nx=nx; diagnostics->ny=ny; diagnostics->gap=gap;
        diagnostics->normal_impulse+=impulse.normal; diagnostics->tangent_impulse+=impulse.tangent;
    }
    return collision_ball_valid(p);
}

static bool collision_ball_boundaries(collision_review* p)
{
    dango_ball_state* s=&p->ball.state; double radius=p->ball.definition.radius;
    double left,right; collision_ball_floor_range(p,&left,&right);
    if (s->x>=left && s->x<=right)
    {
        if (!collision_ball_boundary(p,0,1,s->y-radius-p->config.ground_height,
            &p->ball.floor_touch,&p->ball_floor_contact)) return false;
    }
    else { p->ball.floor_touch=false; p->ball_floor_contact.touching=0; }
    if (p->horizontal_bounds_enabled)
    {
        if (!collision_ball_boundary(p,1,0,s->x-radius-left,&p->ball.wall_touch[0],NULL) ||
            !collision_ball_boundary(p,-1,0,right-s->x-radius,&p->ball.wall_touch[1],NULL)) return false;
    }
    return true;
}

static bool collision_ball_dango(collision_review* p,int slot,int* touched)
{
    dango_ball_state* state=&p->ball.state;
    const dango_ball_definition* definition=&p->ball.definition;
    dango_ball_geometry geometry; dango_ball_geometry_result contact;
    collision_ball_geometry(p,slot,state->x,state->y,definition->radius,&geometry);
    if (!dango_ball_geometry_contact(&geometry,&contact)) return false;
    p->ball_dango_contact.object_id=p->entity_ids[slot];
    p->ball_dango_contact.nx=contact.nx; p->ball_dango_contact.ny=contact.ny;
    p->ball_dango_contact.gap=contact.gap; p->ball_dango_contact.sample=contact.sample;
    p->ball_dango_contact.fraction=contact.fraction;
    p->ball_dango_contact.touching=contact.gap<=.001 ? 1 : 0;
    if (contact.gap>.001) { p->ball.dango_touch=false; return true; }
    p->ball_dango_contact.touching=1;
#if DANGO_ROUND_BALL_TEST_VARIANT == 1
    (void)touched;
    return true;
#else
    double nx=contact.nx,ny=contact.ny,tx=-ny,ty=nx;
    double inertia=.5*definition->mass*definition->radius*definition->radius;
    double knn=1/definition->mass,knt=0,ktt=1/definition->mass+definition->radius*definition->radius/inertia;
    double vn=nx*state->vx+ny*state->vy;
    double vt=tx*state->vx+ty*state->vy-definition->radius*state->omega;
    for (int n=0;n<17;++n)
    {
        b2BodyId id=display_id(p->bodies[slot],n); double inverse_mass=1/b2Body_GetMass(id);
        b2Vec2 velocity=b2Body_GetLinearVelocity(id);
        double gx=contact.normal_gradient[2*n],gy=contact.normal_gradient[2*n+1];
        double hx=contact.tangent_gradient[2*n],hy=contact.tangent_gradient[2*n+1];
        knn+=inverse_mass*(gx*gx+gy*gy); knt+=inverse_mass*(gx*hx+gy*hy); ktt+=inverse_mass*(hx*hx+hy*hy);
        vn-=gx*velocity.x+gy*velocity.y; vt-=hx*velocity.x+hy*velocity.y;
    }
    if (state->sleeping && (vn<-1e-4 || contact.gap<-.0001)) collision_ball_wake(p);
    double target=dango_ball_restitution_target(-vn,definition->dango_restitution,!p->ball.dango_touch);
    p->ball.dango_touch=true;
    dango_ball_impulse impulse;
    if (!dango_ball_contact_impulse(knn,knt,ktt,vn,vt,target,definition->dango_friction,&impulse)) return false;
    if (state->sleeping && (impulse.normal>1e-8 || fabs(impulse.tangent)>1e-8)) collision_ball_wake(p);
    if (impulse.normal>0 || impulse.tangent!=0 || contact.gap<0) *touched=1;
    if (p->ball_dango_contact.normal_impulse==0 && p->ball_dango_contact.tangent_impulse==0 &&
        (impulse.normal>0 || impulse.tangent!=0))
    { p->ball_dango_contact.normal_speed_before=vn; p->ball_dango_contact.tangent_speed_before=vt;
      p->ball_dango_contact.ball_speed_before=hypot(state->vx,state->vy); }
    p->ball_dango_contact.normal_impulse+=impulse.normal;
    p->ball_dango_contact.tangent_impulse+=impulse.tangent;
    state->vx+=(nx*impulse.normal+tx*impulse.tangent)/definition->mass;
    state->vy+=(ny*impulse.normal+ty*impulse.tangent)/definition->mass;
    state->omega-=definition->radius*impulse.tangent/inertia;
    double correction=fmin(.01,fmax(0,.0001-contact.gap))/knn;
    state->x+=nx*correction/definition->mass; state->y+=ny*correction/definition->mass;
#if DANGO_ROUND_BALL_TEST_VARIANT != 2
    for (int n=0;n<17;++n)
    {
        b2BodyId id=display_id(p->bodies[slot],n); double inverse_mass=1/b2Body_GetMass(id);
        b2Vec2 velocity=b2Body_GetLinearVelocity(id),position=b2Body_GetPosition(id);
        velocity.x-=(float)(inverse_mass*(contact.normal_gradient[2*n]*impulse.normal+contact.tangent_gradient[2*n]*impulse.tangent));
        velocity.y-=(float)(inverse_mass*(contact.normal_gradient[2*n+1]*impulse.normal+contact.tangent_gradient[2*n+1]*impulse.tangent));
        position.x-=(float)(inverse_mass*correction*contact.normal_gradient[2*n]);
        position.y-=(float)(inverse_mass*correction*contact.normal_gradient[2*n+1]);
        b2Body_SetLinearVelocity(id,velocity); b2Body_SetTransform(id,position,b2Rot_identity);
    }
#endif
    if (correction>0 && !dango_self_solve_constraints(p->bodies[slot],&p->body_config[slot])) return false;
    return collision_ball_valid(p);
#endif
}

static bool collision_ball_substep(collision_review* p,double dt,int* touched)
{
    if (!p->ball.state.enabled) return true;
    dango_ball_state* s=&p->ball.state;
    double floor_normal_start=p->ball_floor_contact.normal_impulse;
    double floor_tangent_start=p->ball_floor_contact.tangent_impulse;
    bool integrate=!s->sleeping;
#if DANGO_ROUND_BALL_TEST_VARIANT == 7
    integrate=true; /* Deliberately report sleep while continuing integration. */
#endif
    if (integrate)
    {
        s->vy-=DANGO_ROUND_BALL_GRAVITY_SCALE*p->config.gravity*dt;
        s->x+=s->vx*dt; s->y+=s->vy*dt;
        s->angle+=s->omega*dt; ++s->integrations;
    }
    if (!collision_ball_valid(p)) return false;
    for (int iteration=0;iteration<48;++iteration)
    {
        if (!collision_ball_boundaries(p)) return false;
        for (int slot=0;slot<p->capacity;++slot) if (p->active[slot] && !COLLISION_RESTING(p,slot))
            if (!collision_ball_dango(p,slot,touched)) return false;
        double floor_gap=s->y-p->ball.definition.radius-p->config.ground_height;
        if (p->ball_dango_contact.gap>=-1e-5 && floor_gap>=-1e-5) break;
        if (iteration==47) return false;
    }
    if (!collision_ball_boundaries(p)) return false;
    if (!s->sleeping && p->ball.floor_touch)
    {
        /* Only rolling resistance; there is no free-flight drag or second
           angular damping mechanism. The bounded torque cannot cross zero. */
        double inertia=.5*p->ball.definition.mass*p->ball.definition.radius*p->ball.definition.radius;
        double radius=p->ball.definition.radius,mass=p->ball.definition.mass;
        double limit=.02*mass*DANGO_ROUND_BALL_GRAVITY_SCALE*p->config.gravity*radius*dt/(inertia+mass*radius*radius);
        if (p->ball.definition.floor_friction>0 && fabs(s->vx+radius*s->omega)<.002)
        {
            /* Static floor friction transmits the rolling torque through the
               constrained inertia I+mR². Respect its Coulomb force budget. */
            double normal_support=p->ball_floor_contact.normal_impulse-floor_normal_start;
            double tangent_used=p->ball_floor_contact.tangent_impulse-floor_tangent_start;
            double available=fmax(0,p->ball.definition.floor_friction*normal_support-fabs(tangent_used));
            limit=fmin(limit,available/(mass*radius));
            double delta=fmax(-limit,fmin(limit,s->omega));
            s->omega-=delta; s->vx+=radius*delta;
            p->ball_floor_contact.tangent_impulse-=mass*radius*delta;
        }
        double gap=s->y-p->ball.definition.radius-p->config.ground_height;
        if (hypot(s->vx,s->vy)<=.02 && fabs(s->omega)<=.05 && fabs(gap)<=.001 && !*touched)
        {
            p->ball.sleep_seconds+=dt;
            if (p->ball.sleep_seconds>=.5) { s->sleeping=1; s->vx=0; s->vy=0; s->omega=0; }
        }
        else p->ball.sleep_seconds=0;
    }
#if DANGO_ROUND_BALL_TEST_VARIANT == 6
    /* Deliberately invent rigid rolling even for the real frictionless floor.
       These assignments exist only in the isolated negative executable. */
    s->omega=-s->vx/p->ball.definition.radius;
    s->angle=-(s->x-p->ball.definition.x)/p->ball.definition.radius;
#endif
    return collision_ball_valid(p);
}

static bool collision_platform_floor(const collision_review* p,const dango_self_soft_body* body)
{
    for (int n=0;n<body->node_count;++n)
    {
        b2Vec2 position=b2Body_GetPosition(body->node_body_ids[n]);
        if ((!p->horizontal_bounds_enabled || (position.x>=p->horizontal_left && position.x<=p->horizontal_right)) &&
            position.y-collision_body_config(p,body)->node_radius<=p->config.ground_height+.035f) return true;
    }
    return false;
}

static bool collision_platform_refresh(collision_review* p,int slot)
{
    if (!p->platform_enabled) return true;
    dango_self_soft_body* body=p->bodies[slot];
    display_mesh mesh;
    if (!display_build(body,p->body_config[slot].node_radius,&mesh)) return false;
    dango_platform_contact contact=platform_separation(&mesh,&p->platform);
    if (!isfinite(contact.gap)) return false;
    b2Vec2 core=b2Body_GetPosition(body->core_body_id);
    b2Vec2 velocity=b2Body_GetLinearVelocity(body->core_body_id);
    bool support=platform_support_eligible(&contact,core.y,velocity.y,&p->platform);
    float* values=p->platform_contacts[slot];
    values[0]=1; values[1]=contact.gap<=.008f ? 1.0f : 0.0f; values[2]=support ? 1.0f : 0.0f;
    values[3]=contact.normal.x; values[4]=contact.normal.y; values[5]=contact.gap;
    // Only the real floor and top-facing platform contact provide ground
    // control. Raw wall contacts cannot grant launch eligibility.
    body->grounded=collision_platform_floor(p,body) || support;
    return true;
}

DANGO_API void dango_collision_test_interaction_flags(void* handle,int flags)
{
    if (handle) ((collision_review*)handle)->legacy_interaction_flags=flags&255;
}

static bool collision_support_eligible(float gap,float upward_normal,b2Vec2 upper,b2Vec2 lower,b2Vec2 uv,b2Vec2 lv)
{
    return gap<=.008f && upward_normal>=.7f && upper.y>lower.y+.4f && uv.y-lv.y<.5f;
}

static int collision_support_slot(collision_review* p,int r)
{
    if (p->disable_contact_for_test || (p->legacy_interaction_flags&8)) return -1;
    for (int other=0;other<p->capacity;++other)
    {
        if (other==r || !p->active[other]) continue;
        int a=r<other ? r : other,b=r<other ? other : r;
        display_mesh m[2]; b2Vec2 normal; int ia,ib;
        if (!display_build(p->bodies[a],p->body_config[a].node_radius,&m[0]) || !display_build(p->bodies[b],p->body_config[b].node_radius,&m[1])) continue;
        float gap=display_separation(&m[0],&m[1],&normal,&ia,&ib,.008f);
        float upward=r==a ? -normal.y : normal.y;
        if (gap>.008f || upward<.7f) continue;
        b2Vec2 upper=b2Body_GetPosition(p->bodies[r]->core_body_id),lower=b2Body_GetPosition(p->bodies[other]->core_body_id);
        b2Vec2 uv=b2Body_GetLinearVelocity(p->bodies[r]->core_body_id),lv=b2Body_GetLinearVelocity(p->bodies[other]->core_body_id);
        if (collision_support_eligible(gap,upward,upper,lower,uv,lv)) return other;
    }
    return -1;
}

static void collision_launch(collision_review* p,int r,bool supported,int other)
{
    float impulse=0,other_mass=0,speed=supported ? 6.0f : 7.7f;
    // A single-body editor may opt into a lower real launch. The default
    // branch retains the accepted arithmetic and every ordinary-world bit.
    if (p->preview_jump_speed_scale!=1.0f) speed*=p->preview_jump_speed_scale;
    b2Vec2 direction={0,1},point={0}; float weights[17]={0},weight_sum=0;
    if (supported)
    {
        float relative=b2Body_GetLinearVelocity(p->bodies[r]->core_body_id).y-b2Body_GetLinearVelocity(p->bodies[other]->core_body_id).y;
        speed=fmaxf(0,speed-relative);
        int a=r<other ? r : other,b=r<other ? other : r;
        display_mesh m[2]; b2Vec2 normal; int ia,ib;
        if (!(p->legacy_interaction_flags&32) && display_build(p->bodies[a],p->body_config[a].node_radius,&m[0]) && display_build(p->bodies[b],p->body_config[b].node_radius,&m[1]))
        {
            (void)display_separation(&m[0],&m[1],&normal,&ia,&ib,.008f);
            // Contact location distributes support pressure, never launch direction.
            point=r==b ? m[0].points[ia] : m[1].points[ib];
        }
        for (int n=0;n<17;++n)
        {
            b2BodyId id=display_id(p->bodies[other],n); float mass=b2Body_GetMass(id);
            float distance=b2DistanceSquared(b2Body_GetPosition(id),point);
            weights[n]=mass*expf(-distance/(.55f*.55f)); weight_sum+=weights[n]; other_mass+=mass;
        }
    }
    for (int n=0;n<17;++n)
    {
        b2BodyId id=display_id(p->bodies[r],n); float kick=b2Body_GetMass(id)*speed;
        b2Body_ApplyLinearImpulseToCenter(id,b2MulSV(kick,direction),true); impulse+=kick;
    }
    // Equal opposite impulse; the original deformable support absorbs it against the floor.
    if (supported) for (int n=0;n<17;++n)
    {
        b2BodyId id=display_id(p->bodies[other],n);
        float fraction=b2Body_GetMass(id)/other_mass;
        if (!(p->legacy_interaction_flags&32) && weight_sum>1e-8f) fraction=.5f*fraction+.5f*weights[n]/weight_sum;
        b2Body_ApplyLinearImpulseToCenter(id,b2MulSV(-impulse*fraction,direction),true);
    }
    p->bodies[r]->grounded=false; p->bodies[r]->seconds_since_jump=0;
}

DANGO_API void dango_collision_disable_damping_for_test(void* handle)
{
    if (handle) ((collision_review*)handle)->disable_damping_for_test=1;
}

DANGO_API int dango_collision_contract_tests(void* handle,int broken)
{
    collision_review* p=handle; if (!p) return -1;
    int failures=0;
    // Analytic unequal ellipses: full-direction, scale-covariant smaller-body rule.
    for (int scale_index=0;scale_index<3;++scale_index) for (int degree=0;degree<360;degree+=5)
    {
        float scale=scale_index==0 ? .5f : scale_index==1 ? 1 : 2;
        float angle=degree*B2_PI/180;
        b2Vec2 normal={cosf(angle),sinf(angle)};
        display_mesh a={0},b={0}; float low=INFINITY,high=-INFINITY;
        for (int i=0;i<DISPLAY_SAMPLES;++i)
        {
            float t=2*B2_PI*i/DISPLAY_SAMPLES;
            b2Vec2 point={scale*cosf(t),.6f*scale*sinf(t)};
            a.points[i]=point; b.points[i]=b2MulSV(1.7f,point);
            float value=b2Dot(normal,point); low=fminf(low,value); high=fmaxf(high,value);
        }
        float margin=display_contact_margin(&a,&b,normal,true);
        if (broken) margin=-.03f; // Fixed world length must fail across sizes/directions.
        if (fabsf(-margin/(high-low)-.03f)>1e-6f) ++failures;
        if (fabsf(display_contact_margin(&b,&a,normal,true)-display_contact_margin(&a,&b,normal,true))>1e-6f) ++failures;
        if (fabsf(display_contact_margin(&a,&b,normal,false)-.001f)>1e-6f) ++failures;
    }
    b2Vec2 high={0,1},low={0,0},still={0,0},departing={0,1};
    if (!collision_support_eligible(.001f,1,high,low,still,still)) ++failures;
    if (collision_support_eligible(.001f,0,high,low,still,still) ||
        collision_support_eligible(.02f,1,high,low,still,still) ||
        collision_support_eligible(.001f,1,low,high,still,still) ||
        collision_support_eligible(.001f,1,high,low,departing,still)) ++failures;
    if (p->count==2)
    {
        // A sloped support must never steer the launch impulse sideways.
        b2Vec2 saved_positions[17],saved_speeds[2][17];
        for (int n=0;n<17;++n) saved_positions[n]=b2Body_GetPosition(display_id(p->bodies[1],n));
        for (int r=0;r<2;++r) for (int n=0;n<17;++n) saved_speeds[r][n]=b2Body_GetLinearVelocity(display_id(p->bodies[r],n));
        for (int side=-1;side<=1;side+=2)
        {
            b2Vec2 origin=b2Body_GetPosition(p->bodies[0]->core_body_id);
            b2Vec2 delta=b2Sub(b2Add(origin,(b2Vec2){side*.35f,1.65f}),saved_positions[16]);
            for (int n=0;n<17;++n) b2Body_SetTransform(display_id(p->bodies[1],n),b2Add(saved_positions[n],delta),b2Rot_identity);
            collision_launch(p,1,true,0);
            for (int r=0;r<2;++r) for (int n=0;n<17;++n)
            {
                b2BodyId id=display_id(p->bodies[r],n);
                if (fabsf(b2Body_GetLinearVelocity(id).x-saved_speeds[r][n].x)>1e-6f) ++failures;
                b2Body_SetLinearVelocity(id,saved_speeds[r][n]);
            }
        }
        for (int n=0;n<17;++n) b2Body_SetTransform(display_id(p->bodies[1],n),saved_positions[n],b2Rot_identity);
        b2Vec2 saved_velocity[2][17],before_total={0},after_total={0};
        for (int r=0;r<2;++r) for (int n=0;n<17;++n)
        {
            b2BodyId id=display_id(p->bodies[r],n); saved_velocity[r][n]=b2Body_GetLinearVelocity(id);
            before_total=b2MulAdd(before_total,b2Body_GetMass(id),saved_velocity[r][n]);
        }
        collision_launch(p,1,true,0);
        for (int r=0;r<2;++r) for (int n=0;n<17;++n)
        {
            b2BodyId id=display_id(p->bodies[r],n);
            after_total=b2MulAdd(after_total,b2Body_GetMass(id),b2Body_GetLinearVelocity(id));
            b2Body_SetLinearVelocity(id,saved_velocity[r][n]);
        }
        if (b2Distance(before_total,after_total)>1e-5f) ++failures;
    }
    b2Vec2 positions[17],before[17],after[17]; float masses[17];
    for (int n=0;n<17;++n)
    {
        float angle=2*B2_PI*(float)n/16;
        positions[n]=n==16 ? (b2Vec2){0,0} : (b2Vec2){cosf(angle),sinf(angle)};
        masses[n]=n==16 ? .12f : .015f;
        before[n]=(b2Vec2){2-3*positions[n].y+.3f*positions[n].x,-1+3*positions[n].x+.3f*positions[n].y};
    }
    for (int ground=0;ground<2;++ground) for (int active=0;active<2;++active)
    {
        memcpy(after,before,sizeof after);
        if (!broken) passive_spin(positions,after,masses,.1f,ground!=0,active!=0);
        b2Vec2 old_momentum={0},new_momentum={0}; double e0=0,e1=0;
        for (int n=0;n<17;++n)
        {
            float omega=ground && !active ? 3*expf(-12.0f) : 3;
            b2Vec2 expected={2-omega*positions[n].y+.3f*positions[n].x,-1+omega*positions[n].x+.3f*positions[n].y};
            if (b2Distance(after[n],expected)>2e-6f) ++failures;
            if ((!ground || active) && memcmp(&before[n],&after[n],sizeof(b2Vec2))) ++failures;
            old_momentum=b2MulAdd(old_momentum,masses[n],before[n]); new_momentum=b2MulAdd(new_momentum,masses[n],after[n]);
            e0+=masses[n]*b2Dot(before[n],before[n]); e1+=masses[n]*b2Dot(after[n],after[n]);
        }
        if (b2Distance(old_momentum,new_momentum)>1e-6f || e1-e0>1e-6) ++failures;
    }
    dango_self_soft_body* body=p->bodies[0];
    b2Vec2 saved[16];
    for (int n=0;n<16;++n) saved[n]=b2Body_GetPosition(body->node_body_ids[n]);
    for (int clipped=0;clipped<2;++clipped)
    {
        if (clipped) for (int n=0;n<16;++n)
        {
            b2Vec2 core=b2Body_GetPosition(body->core_body_id),local=b2Sub(saved[n],core);
            b2Body_SetTransform(body->node_body_ids[n],b2Add(core,(b2Vec2){2*local.x+.15f,1.7f*local.y}),b2Rot_identity);
        }
        display_mesh mesh; b2Vec2 g[17],normal={.6f,.8f};
        if (!display_build(body,p->body_config[0].node_radius,&mesh))
        {
            ++failures;
            for (int n=0;n<16;++n) b2Body_SetTransform(body->node_body_ids[n],saved[n],b2Rot_identity);
            continue;
        }
        display_gradient(&mesh,5,normal,g);
        for (int n=0;n<17;++n) for (int axis=0;axis<2;++axis)
        {
            b2BodyId id=display_id(body,n); b2Vec2 pos=b2Body_GetPosition(id),delta=axis ? (b2Vec2){0,.0005f} : (b2Vec2){.0005f,0};
            display_mesh plus,minus;
            b2Body_SetTransform(id,b2Add(pos,delta),b2Rot_identity); bool ok=display_build(body,p->body_config[0].node_radius,&plus);
            b2Body_SetTransform(id,b2Sub(pos,delta),b2Rot_identity); ok=display_build(body,p->body_config[0].node_radius,&minus) && ok;
            b2Body_SetTransform(id,pos,b2Rot_identity);
            if (!ok || fabsf(b2Dot(normal,b2Sub(plus.points[5],minus.points[5]))/.001f-(axis ? g[n].y : g[n].x))>.001f) ++failures;
        }
        for (int n=0;n<16;++n) b2Body_SetTransform(body->node_body_ids[n],saved[n],b2Rot_identity);
    }
    return failures;
}
#if defined(DANGO_MATERIAL_EXPERIMENT_MODE)
static bool collision_material_passive_spin_enabled(void)
{
    // Private fixed diagnostic only; all other damping and integration remain.
    return (DANGO_MATERIAL_EXPERIMENT_MODE & 8) == 0;
}
static bool collision_material_idle_friction_override_enabled(void)
{
    return (DANGO_MATERIAL_EXPERIMENT_MODE & 16) == 0;
}
#endif

static void collision_damp(collision_review* p,int r,float dt,const float* control)
{
    if (p->disable_damping_for_test) return;
    b2Vec2 positions[17],velocities[17]; float masses[17]; bool ground=false;
    for (int n=0;n<17;++n)
    {
        b2BodyId id=display_id(p->bodies[r],n);
        positions[n]=b2Body_GetPosition(id); velocities[n]=b2Body_GetLinearVelocity(id); masses[n]=b2Body_GetMass(id);
        if (n<16 && positions[n].y-p->body_config[r].node_radius<=p->config.ground_height+.001f) ground=true;
    }
    bool active=control[0]!=0 || control[1]!=0 || control[2]!=0 || control[3]!=0;
    if (p->platform_enabled && p->platform_contacts[r][2]!=0) ground=true;
    bool collection_support=p->collection_count && p->collection_support_friction[r]>=0;
    if (collection_support) ground=true;
#if defined(DANGO_MATERIAL_EXPERIMENT_MODE)
    if (collision_material_passive_spin_enabled())
#endif
        passive_spin(positions,velocities,masses,dt,ground,active);
    if (ground && !active)
    {
        float mass=0,horizontal=0;
        for (int n=0;n<17;++n) { mass+=masses[n]; horizontal+=masses[n]*velocities[n].x; }
        // Finite dry friction on whole-body translation; no position anchor or vertical braking.
        float friction_scale=p->platform_enabled && p->platform_contacts[r][2]!=0 ? p->platform.friction/.72f : 1.0f;
        if (collection_support) friction_scale=p->collection_support_friction[r]/.72f;
        float delta=b2ClampFloat(horizontal/mass,-35*friction_scale*dt,35*friction_scale*dt);
        for (int n=0;n<17;++n) velocities[n].x=(velocities[n].x-delta)*expf(-60*dt);
    }
    for (int n=0;n<17;++n) b2Body_SetLinearVelocity(display_id(p->bodies[r],n),velocities[n]);
}

DANGO_API void dango_collision_disable_for_test(void* handle)
{
    if (handle) ((collision_review*)handle)->disable_contact_for_test=1;
}

static bool collision_solve_pairs(collision_review* p,const bool* static_x,int* touched)
{
    bool relative=!(p->legacy_interaction_flags&64);
    for (int sweep=0;sweep<8;++sweep)
    {
        for (int a=0;a<p->capacity;++a) for (int b=a+1;b<p->capacity;++b)
        {
            if (!p->active[a] || !p->active[b] || COLLISION_RESTING(p,a) || COLLISION_RESTING(p,b)) continue;
            dango_self_soft_body* pair[2]={p->bodies[a],p->bodies[b]};
            const dango_self_physics_config* pair_config[2]={&p->body_config[a],&p->body_config[b]};
            bool fixed_x[2]={static_x[a],static_x[b]};
            if (!display_contact_solve(pair,pair_config,fixed_x,relative,touched)) return false;
        }
        if (p->count<=2) return true;
        bool valid=true;
        for (int a=0;a<p->capacity;++a) for (int b=a+1;b<p->capacity;++b)
        {
            if (!p->active[a] || !p->active[b]) continue;
            display_mesh m[2]; b2Vec2 normal; int ia,ib;
            if (!display_build(p->bodies[a],p->body_config[a].node_radius,&m[0]) || !display_build(p->bodies[b],p->body_config[b].node_radius,&m[1])) return false;
            float gap=display_separation(&m[0],&m[1],&normal,&ia,&ib,.001f);
            if (!isfinite(gap)) return false;
            if (gap<display_contact_margin(&m[0],&m[1],normal,relative)-1e-5f) valid=false;
        }
        if (valid) return true;
    }
    return false;
}

static int collision_local_floor_contacts(collision_review* p,dango_self_soft_body* body,int* grounded)
{
    int count=0;
    bool near_floor=false;
    for (int n=0;n<body->node_count;++n)
    {
        b2Vec2 position=b2Body_GetPosition(body->node_body_ids[n]);
        if (position.x>=p->horizontal_left && position.x<=p->horizontal_right &&
            position.y-collision_body_config(p,body)->node_radius<=p->config.ground_height+.035f) near_floor=true;
        b2ContactData contacts[4];
        int capacity=b2Body_GetContactCapacity(body->node_body_ids[n]);
        if (capacity<=0) continue;
        int touching=b2Body_GetContactData(body->node_body_ids[n],contacts,capacity<4 ? capacity : 4);
        for (int c=0;c<touching;++c)
        {
            b2ContactData* contact=&contacts[c];
            // The normal is A->B. Only the configured floor supporting a node
            // from below counts; walls and lateral floor edges cannot grant jumps.
            float upward=B2_ID_EQUALS(contact->shapeIdA,body->ground_shape_id) ? contact->manifold.normal.y :
                B2_ID_EQUALS(contact->shapeIdB,body->ground_shape_id) ? -contact->manifold.normal.y : 0;
            if (contact->manifold.pointCount>0 && upward>=.7f) ++count;
        }
    }
    body->grounded=count>0 || near_floor;
    *grounded=body->grounded ? 1 : 0;
    return count;
}

static int collision_read(collision_review* p, float* output)
{
    if (p->collection_count && !collection_refresh(p)) return -402;
    for (int r=0;r<p->capacity;++r)
    {
        if (!p->active[r]) continue;
        float* row=output+42*r; int points, contacts, grounded, selected;
        int error=dango_self_read_frame(p->bodies[r],&p->body_config[r],row,&points,&contacts,
            row+34,row+35,row+36,row+32,row+33,row+37,&grounded,&selected);
        if (error) return error;
        if (p->horizontal_bounds_enabled) contacts=collision_local_floor_contacts(p,p->bodies[r],&grounded);
        if (p->platform_enabled)
        {
            if (!collision_platform_refresh(p,r)) return -402;
            grounded=p->bodies[r]->grounded ? 1 : 0;
        }
        if (p->collection_count)
        {
            if (!collection_refresh(p)) return -402;
            grounded=p->bodies[r]->grounded ? 1 : 0;
        }
        row[38]=(float)contacts; row[39]=(float)grounded; row[40]=(float)selected; row[41]=(float)points;
    }
    return 0;
}

DANGO_API int dango_collision_create(const dango_self_physics_config* config, int count, int layout, void** handle)
{
    if (!dango_self_config_valid(config) || !handle || count<1 || count>2 || layout<0 || layout>4) return -400;
    *handle=NULL;
    collision_review* p=calloc(1,sizeof *p); if (!p) return -401;
    p->config=*config; p->count=count; p->capacity=count; p->next_entity_id=1;
    for (int r=0;r<DANGO_PHYSICS_MAX_BODIES;++r) p->body_config[r]=*config;
    p->preview_jump_speed_scale=1.0f;
    void* first=NULL;
    int error=dango_box2d_self_soft_body_create(config,&first);
    if (error) { free(p); return error; }
    p->bodies[0]=first;
    // Windows x64 owner identity plus Box2D's recycling generation. Distinct
    // loaded candidate DLL copies may each have Box2D world index one.
    p->world_generation=((uint64_t)p->bodies[0]->world_id.generation<<48) |
        ((uint64_t)(uintptr_t)p & UINT64_C(0x0000FFFFFFFFFFFF));
    if (count==2)
    {
        p->bodies[1]=&p->additional[0];
        p->additional[0].world_id=p->bodies[0]->world_id;
        p->additional[0].ground_body_id=p->bodies[0]->ground_body_id;
        p->additional[0].ground_shape_id=p->bodies[0]->ground_shape_id;
        dango_self_create_rig(&p->additional[0],config);
    }
    for (int r=0;r<count;++r)
    {
        p->active[r]=1; p->entity_ids[r]=p->next_entity_id++;
        dango_self_initialize_at(p->bodies[r],config,r==0 ? 0 : layout==1 ? 0 : layout==2 ? 4.0f : layout==3 ? -.35f : layout==4 ? .35f : 2.8f,
            r==1 && (layout==1 || layout>=3) ? 1.65f : 0);
        p->anchors[2*r]=r==0 ? 0 : layout==1 ? 0 : layout==2 ? 4.0f : layout==3 ? -.35f : layout==4 ? .35f : 2.8f;
        p->anchors[2*r+1]=r==1 && (layout==1 || layout>=3) ? 1.65f : 0;
        collision_ball_geometry(p,r,0,0,.4,&p->ball_birth_geometry[r]);
    }
    *handle=p;
    // Initial falling shape is real state, not a settled-pose replacement.
    p->error=collision_read(p,p->snapshot);
    if (!p->error) { ++p->scene_revision; collision_ball_publish(p); }
    return 0;
}

#if defined(DANGO_REST_SLEEP)
static bool collision_rest_ball_near(const collision_review* p,const dango_native_ball* ball,int r)
{
    if (!ball->state.enabled) return false;
    b2Vec2 core=b2Body_GetPosition(p->bodies[r]->core_body_id);
    // Same reach as the ball contact broad phase, plus the rest margin.
    return hypot(ball->state.x-(4.0/3)*core.x,ball->state.y-core.y)<=ball->definition.radius+1.7+DANGO_REST_MARGIN;
}

static void collision_rest_inputs(collision_review* p,bool* near,bool* supported)
{
    for (int r=0;r<p->capacity;++r)
    {
        near[r]=false; if (supported) supported[r]=false;
        if (!p->active[r]) continue;
        near[r]=collision_rest_ball_near(p,&p->ball,r);
        for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i)
            if (p->collection[i].instance && p->collection[i].definition.kind==2 && collision_rest_ball_near(p,&p->collection[i].ball,r)) near[r]=true;
        if (!supported) continue;
        for (int n=0;n<16;++n)
            if (b2Body_GetPosition(p->bodies[r]->node_body_ids[n]).y-p->body_config[r].node_radius<=p->config.ground_height+.035f) supported[r]=true;
        if (p->platform_enabled && p->platform_contacts[r][2]!=0) supported[r]=true;
        if (p->collection_count && p->collection_support_friction[r]>=0) supported[r]=true;
        if (collision_support_slot(p,r)>=0) supported[r]=true;
    }
}

// Grouping reach uses the largest node radius present, so groups are never narrower than any member needs.
static float collision_rest_radius(const collision_review* p)
{
    float radius=0;
    for (int r=0;r<p->capacity;++r) if (p->active[r]) radius=fmaxf(radius,p->body_config[r].node_radius);
    return radius;
}

static bool collision_rest_has_ball(const collision_review* p)
{
    if (p->ball.state.enabled) return true;
    for (int i=0;i<DANGO_FURNITURE_COLLECTION_MAX;++i)
        if (p->collection[i].instance && p->collection[i].definition.kind==2) return true;
    return false;
}
#endif

DANGO_API int dango_collision_step(void* handle, float dt, const float* controls, float* output, int capacity, int* contact_steps)
{
    collision_review* p=handle;
    if (!p || !controls || !output || capacity<p->capacity*42 || !contact_steps || !isfinite(dt) || dt<=0 || dt>.1f) return -400;
    for (int r=0;r<p->capacity;++r)
    {
        if (!p->active[r]) continue;
        const float* c=controls+6*r;
        for (int i=0;i<6;++i) if (!isfinite(c[i])) return -400;
        if (fabsf(c[0])>1 || c[1]<0 || c[1]>1 || (c[2]!=0 && c[2]!=1) || (c[3]!=0 && c[3]!=1)) return -400;
    }
    int safe_platform_contact_steps=p->contact_steps;
#if defined(DANGO_REST_SLEEP)
    bool rest_near[DANGO_PHYSICS_MAX_BODIES],rest_supported[DANGO_PHYSICS_MAX_BODIES];
    bool rest_frozen=false;
    if (!p->error)
    {
        collision_rest_inputs(p,rest_near,NULL);
        dango_rest_check_wake(&p->rest,p->bodies,p->active,p->capacity,controls,rest_near,collision_rest_radius(p));
        // Whole world at rest and nothing else moving: the frame is an exact no-op.
        rest_frozen=dango_rest_all(&p->rest,p->active,p->capacity) && !collision_rest_has_ball(p);
    }
    if (!p->error && !rest_frozen)
#else
    if (!p->error)
#endif
    {
        if (p->collection_count) collection_begin(p);
        if (p->ball_scene)
        {
            p->ball_dango_contact=(dango_ball_contact){0};
            p->ball_floor_contact=(dango_ball_contact){.object_id=-1};
        }
        int small_steps=(int)ceilf(dt*(p->disable_damping_for_test ? 240.0f : 960.0f));
        if (small_steps<p->config.substep_count) small_steps=p->config.substep_count;
        float small_dt=dt/(float)small_steps;
#if defined(DANGO_MATERIAL_IDLE_TRACE)
        if (p->idle_trace.file) ++p->idle_trace.frame;
#endif
        for (int sub=0;sub<small_steps && !p->error;++sub)
        {
#if defined(DANGO_MATERIAL_IDLE_TRACE)
            dango_it_start(&p->idle_trace,p->bodies[0],(uint32_t)sub,small_dt);
#endif
#if defined(DANGO_REST_SLEEP)
            if (sub>0)
            {
                collision_rest_inputs(p,rest_near,NULL);
                dango_rest_check_wake(&p->rest,p->bodies,p->active,p->capacity,NULL,rest_near,collision_rest_radius(p));
            }
#endif
            int supported[DANGO_PHYSICS_MAX_BODIES];
            for (int r=0;r<p->capacity;++r) supported[r]=-1;
            for (int r=0;r<p->capacity;++r) if (p->active[r]) supported[r]=collision_support_slot(p,r);
            for (int r=0;r<p->capacity;++r)
            {
                if (!p->active[r] || COLLISION_RESTING(p,r)) continue;
                const float* c=controls+6*r;
                dango_self_soft_body* body=p->bodies[r];
#ifdef DANGO_ROUND_BALL_CONTRACT_TESTS
                if (p->ball_scene && p->test_ball_fault_mode && sub==4)
                {
                    int mode=p->test_ball_fault_mode; p->test_ball_fault_mode=0;
                    if (mode==1 && p->ball.state.enabled) p->ball.state.vx=NAN;
                    else
                    {
                        b2Vec2 core=b2Body_GetPosition(body->core_body_id);
                        for (int n=0;n<16;++n) b2Body_SetTransform(body->node_body_ids[n],core,b2Rot_identity);
                        dango_ball_geometry damaged; dango_ball_geometry_result result;
                        collision_ball_geometry(p,r,p->ball.state.x,p->ball.state.y,.4,&damaged);
                        if (!dango_ball_geometry_contact(&damaged,&result)) { p->error=-403; break; }
                    }
                }
#endif
#ifdef DANGO_STATIC_PLATFORM_CONTRACT_TESTS
                if (p->platform_enabled && p->test_platform_collapse_substep>0 && sub==p->test_platform_collapse_substep)
                {
                    p->test_platform_collapse_substep=0;
                    b2Vec2 core=b2Body_GetPosition(body->core_body_id);
                    for (int n=0;n<16;++n) b2Body_SetTransform(body->node_body_ids[n],core,b2Rot_identity);
                }
#endif
                if (p->platform_enabled && !collision_platform_refresh(p,r)) { p->error=-402; break; }
                if (p->collection_count && !collection_refresh(p)) { p->error=-402; break; }
#if DANGO_ROUND_BALL_TEST_VARIANT == 4
                if (p->ball.state.enabled)
                {
                    dango_ball_geometry geometry; dango_ball_geometry_result contact;
                    collision_ball_geometry(p,r,p->ball.state.x,p->ball.state.y,p->ball.definition.radius,&geometry);
                    if (dango_ball_geometry_contact(&geometry,&contact) && contact.gap<=.008 && contact.ny<-.7)
                        body->grounded=true; /* Deliberate invalid ball-surface support. */
                }
#endif
                bool idle=c[0]==0 && c[1]==0 && c[2]==0 && c[3]==0;
                float friction=!p->disable_damping_for_test && idle ? 16.0f : p->body_config[r].friction;
#if defined(DANGO_MATERIAL_EXPERIMENT_MODE)
                if (!collision_material_idle_friction_override_enabled()) friction=p->body_config[r].friction;
#endif
                for (int n=0;n<16;++n) b2Shape_SetFriction(body->node_shape_ids[n],friction);
                float horizontal=c[0],downward=c[1];
#if defined(DANGO_PHYSICS_LAB)
                if(p->lab_call) {
                    if(sub==0) p->lab[r].jump_accepted=c[2]!=0 &&
                        (body->grounded || (!(p->legacy_interaction_flags&1) && supported[r]>=0)) &&
                        body->seconds_since_jump+small_dt>=p->body_config[r].jump_cooldown_seconds;
                    if(p->lab[r].jump_accepted) downward=0;
                    p->error=dango_lab_forces(body,&p->body_config[r],&p->lab[r],small_dt,c);
                    if(p->error) break;
                    if(p->lab[r].mode!=0 && !(p->lab[r].mode==3 && p->lab[r].bias==0)) horizontal=0;
                }
#endif
                p->error=dango_self_apply_step_forces(body,&p->body_config[r],small_dt,sub,horizontal,downward,
                    (p->legacy_interaction_flags&1) ? (int)c[2] : 0,(int)c[3],c[4],c[5]);
                if (p->error) break;
                // Borrow only the accepted ground spreading force; do not change
                // actual grounding, air control, friction, or the support launch height.
                if (!(p->legacy_interaction_flags&128) && supported[r]>=0 && !body->grounded && c[2]==0 && c[3]==0)
                {
                    body->grounded=true;
                    dango_self_apply_ground_deformation_force(body,&p->body_config[r]);
                    body->grounded=false;
                }
                if (!(p->legacy_interaction_flags&1) && sub==0 && c[2]!=0 && (body->grounded || supported[r]>=0) && body->seconds_since_jump>=p->body_config[r].jump_cooldown_seconds)
                {
                    collision_launch(p,r,!body->grounded && supported[r]>=0,supported[r]);
                }
            }
            if (p->error) break;
#if defined(DANGO_MATERIAL_IDLE_TRACE)
            dango_it_pre_world(&p->idle_trace,p->bodies[0]);
#endif
            b2World_Step(p->bodies[0]->world_id,small_dt,1);
#if defined(DANGO_MATERIAL_CONTACT_CANDIDATE)
            if (p->capacity!=1 || p->count!=1 || p->disable_damping_for_test) { p->error=-410; break; }
            // This private single-body path performs position, original damping,
            // final velocity and original global speed cap before one writeback.
            p->error=dango_material_contact_substep(p->bodies[0],&p->config,small_dt,controls,&p->candidate_diagnostic
                );
#if defined(DANGO_MATERIAL_IDLE_TRACE)
            dango_it_after(&p->idle_trace,p->bodies[0],&p->candidate_diagnostic);
#endif
            if (p->error) break;
            (void)collision_damp;
#else
            for (int r=0;r<p->capacity;++r)
                if (p->active[r] && !COLLISION_RESTING(p,r))
                if (!dango_self_solve_constraints(p->bodies[r],&p->body_config[r])) p->error=-91;
            if (p->error) break;
            if (p->collection_count)
            {
                int touched=0;
                if (!collection_substep(p,small_dt,&touched)) { p->error=-403; break; }
                p->contact_steps+=touched;
            }
            if (p->ball.state.enabled)
            {
                int touched=0;
                if (!collision_ball_substep(p,small_dt,&touched)) { p->error=-403; break; }
                p->contact_steps+=touched;
            }
            if (p->platform_enabled)
            {
                for (int r=0;r<p->capacity;++r) if (p->active[r] && !COLLISION_RESTING(p,r))
                {
                    int touched=0;
                    if (!platform_contact_solve(p->bodies[r],&p->body_config[r],&p->platform,&touched) ||
                        !collision_platform_refresh(p,r)) { p->error=-402; break; }
                    p->contact_steps+=touched;
                }
                if (p->error) break;
            }
            if (p->count>=2 && !p->disable_contact_for_test)
            {
                int touched=0;
                bool static_x[DANGO_PHYSICS_MAX_BODIES]={false};
                if (!p->disable_damping_for_test) for (int r=0;r<p->capacity;++r)
                {
                    if (!p->active[r] || COLLISION_RESTING(p,r)) continue;
                    const float* c=controls+6*r;
                    bool ground=false; float mass=0,horizontal=0;
                    for (int n=0;n<17;++n)
                    {
                        b2BodyId id=display_id(p->bodies[r],n); float m=b2Body_GetMass(id);
                        mass+=m; horizontal+=m*b2Body_GetLinearVelocity(id).x;
                        if (n<16 && b2Body_GetPosition(id).y-p->body_config[r].node_radius<=p->config.ground_height+.001f) ground=true;
                    }
                    static_x[r]=ground && c[0]==0 && c[1]==0 && c[2]==0 && c[3]==0 && fabsf(horizontal/mass)<=35*small_dt;
                }
                if (!collision_solve_pairs(p,static_x,&touched)) p->error=-402;
                p->contact_steps+=touched;
            }
            for (int r=0;r<p->capacity;++r) if (p->active[r] && !COLLISION_RESTING(p,r)) collision_damp(p,r,small_dt,controls+6*r);
            for (int r=0;r<p->capacity;++r)
                if (p->active[r] && !COLLISION_RESTING(p,r))
                if (!dango_self_limit_speed(p->bodies[r],p->body_config[r].maximum_speed)) p->error=-87;
#endif
            // Display convex-hull separation is checked by the contact solve; raw rig overlap is not visible penetration.
        }
        float candidate[DANGO_PHYSICS_MAX_BODIES*42]={0};
        if (!p->error) p->error=collision_read(p,candidate);
        if (!p->error)
        {
            memcpy(p->snapshot,candidate,sizeof candidate);
            if (p->platform_enabled) memcpy(p->platform_snapshot,p->platform_contacts,sizeof p->platform_snapshot);
            ++p->physics_step;
            collision_ball_publish(p);
#if defined(DANGO_REST_SLEEP)
            collision_rest_inputs(p,rest_near,rest_supported);
            dango_rest_frame_end(&p->rest,p->bodies,p->active,p->capacity,controls,rest_supported,rest_near,collision_rest_radius(p),dt);
#endif
        }
#if defined(DANGO_MATERIAL_IDLE_TRACE)
        dango_it_publish(&p->idle_trace,p->snapshot,p->error,p->contact_steps);
#endif
    }
#if defined(DANGO_REST_SLEEP)
    if (rest_frozen) for (int r=0;r<p->capacity;++r) if (p->active[r]) p->rest.slot[r].rest_seconds+=dt;
#endif
    if ((p->platform_enabled || p->ball_scene || p->collection_count) && p->error) p->contact_steps=safe_platform_contact_steps;
    memcpy(output,p->snapshot,(size_t)p->capacity*42*sizeof(float)); *contact_steps=p->contact_steps;
    return p->error;
}

DANGO_API int dango_collision_destroy(void* handle)
{
    collision_review* p=handle; if (!p) return -400;
#if defined(DANGO_MATERIAL_IDLE_TRACE)
    if (p->idle_trace.file) { (void)fclose(p->idle_trace.file); p->idle_trace.file=NULL; }
    if (dango_it_current==&p->idle_trace) dango_it_current=NULL;
#endif
    int bytes=0; int error=dango_box2d_self_soft_body_destroy(p->bodies[0],&bytes);
    free(p); return error;
}

static bool collision_rig_valid(const dango_self_soft_body* body)
{
    if (!b2Body_IsValid(body->core_body_id) || !b2Shape_IsValid(body->core_shape_id)) return false;
    for (int n=0;n<body->node_count;++n)
        if (!b2Body_IsValid(body->node_body_ids[n]) || !b2Shape_IsValid(body->node_shape_ids[n])) return false;
    return true;
}

DANGO_API int dango_physics_get_abi_version(void)
{
    return DANGO_PHYSICS_MODULE_ABI_VERSION;
}

DANGO_API int dango_physics_get_body_material_model_version(void)
{
    return 1;
}

#if defined(DANGO_MATERIAL_IDLE_TRACE)
DANGO_API int dango_material_idle_trace_layout(dango_it_layout* output)
{
    if (!output) return -500;
    *output=(dango_it_layout){1,sizeof(dango_it_header),sizeof(dango_it_step),sizeof(dango_it_mode),sizeof(dango_it_frame),
        sizeof(dango_material_contact_diagnostic),sizeof(dango_self_physics_config),sizeof(dango_it_cost),dango_it_worst_bytes(),
        offsetof(dango_material_contact_diagnostic,input),offsetof(dango_material_contact_diagnostic,positioned),
        offsetof(dango_material_contact_diagnostic,damped),offsetof(dango_material_contact_diagnostic,uncapped),
        offsetof(dango_material_contact_diagnostic,output),offsetof(dango_constraint_state,positions),
        offsetof(dango_constraint_state,velocities),offsetof(dango_constraint_state,inverse_mass)};
    return output->worst_case_bytes <= DANGO_IT_LIMIT ? 0 : -501;
}
DANGO_API int dango_material_idle_trace_begin(void* handle,const char* path,int enabled)
{
    collision_review* p=handle;
    if (!p || !path || (enabled!=0 && enabled!=1) || p->capacity!=1 || p->count!=1 ||
        p->candidate_diagnostic.substeps || p->idle_trace.file || dango_it_worst_bytes()>DANGO_IT_LIMIT) return -500;
    dango_it_context* trace=&p->idle_trace;
    memset(trace,0,sizeof(*trace)); trace->enabled=enabled; trace->end.enabled=(uint32_t)enabled;
    trace->file=fopen(path,"wbx"); if (!trace->file) return -502;
    dango_it_header header={0}; memcpy(header.magic,"DGTRACE1",8);
    header.schema=1; header.enabled=(uint32_t)enabled; header.header_bytes=sizeof(header);
    header.step_bytes=sizeof(dango_it_step); header.mode_bytes=sizeof(dango_it_mode); header.frame_bytes=sizeof(dango_it_frame);
    header.diagnostic_bytes=sizeof(dango_material_contact_diagnostic); header.config_bytes=sizeof(p->config); header.maximum_bytes=DANGO_IT_LIMIT;
    _Static_assert(sizeof(dango_self_physics_config)==164,"private config layout");
    memcpy(header.config,&p->config,sizeof(p->config));
    dango_it_body(p->bodies[0],header.initial,header.masses);
    memcpy(trace->previous,header.initial,sizeof(trace->previous));
    memcpy(trace->end.final,header.initial,sizeof(trace->end.final));
    (void)dango_it_write(trace,&header,sizeof(header));
    return trace->error;
}
DANGO_API int dango_material_idle_trace_status(void* handle)
{
    return handle ? ((collision_review*)handle)->idle_trace.error : -500;
}
DANGO_API int dango_material_idle_trace_finish(void* handle)
{
    collision_review* p=handle;
    if (!p || !p->idle_trace.file) return -500;
    dango_it_context* trace=&p->idle_trace;
    if (dango_it_current==trace) dango_it_current=NULL;
    dango_it_body(p->bodies[0],trace->end.final,NULL);
    trace->end.error=trace->error;
    dango_it_end end=trace->end;
    end.bytes+=sizeof(dango_it_prefix)+sizeof(end);
    (void)dango_it_record(trace,DANGO_IT_END,&end,sizeof(end));
    if (fclose(trace->file)!=0 && !trace->error) trace->error=-502;
    trace->file=NULL; return trace->error;
}
#endif

DANGO_API int dango_physics_world_create(const dango_self_physics_config* config,int capacity,void** handle)
{
#if defined(DANGO_MATERIAL_CONTACT_CANDIDATE)
    if (capacity!=1 || !config || config->node_count!=16) return -410;
#endif
    if (!handle || !dango_self_config_valid(config) || capacity<1 || capacity>DANGO_PHYSICS_MAX_BODIES) return -410;
    *handle=NULL;
    collision_review* p=calloc(1,sizeof *p); if (!p) return -401;
    p->config=*config; p->capacity=capacity; p->next_entity_id=1;
    for (int r=0;r<DANGO_PHYSICS_MAX_BODIES;++r) p->body_config[r]=*config;
    p->preview_jump_speed_scale=1.0f;
    int error=dango_self_world_create(config,&p->bodies[0]);
    if (error) { free(p); return error; }
    p->world_generation=((uint64_t)p->bodies[0]->world_id.generation<<48) |
        ((uint64_t)(uintptr_t)p & UINT64_C(0x0000FFFFFFFFFFFF));
    collision_ball_publish(p);
#ifdef DANGO_ROUND_BALL_CONTRACT_TESTS
    ++round_ball_owner_count;
#endif
    *handle=p;
    return 0;
}

#if defined(DANGO_MATERIAL_CONTACT_CANDIDATE)
// Private candidate telemetry only; never compiled into the default module.
DANGO_API int dango_material_contact_get_diagnostic(void* handle,dango_material_contact_diagnostic* output)
{
    if (!handle || !output) return -410;
    *output=((collision_review*)handle)->candidate_diagnostic;
    return 0;
}
#endif

static int collision_add(collision_review* p,const dango_self_physics_config* config,float anchor_x,float anchor_y,int* entity_id);

DANGO_API int dango_physics_world_add(void* handle,float anchor_x,float anchor_y,int* entity_id)
{
    collision_review* p=handle;
    return collision_add(p,p ? &p->config : NULL,anchor_x,anchor_y,entity_id);
}

// Adds a body with its own shape. World-level fields (node count, substeps, gravity, ground)
// must equal the world configuration; every other field belongs to the body.
DANGO_API int dango_physics_world_add_configured(void* handle,const dango_self_physics_config* config,float anchor_x,float anchor_y,int* entity_id)
{
    collision_review* p=handle;
    if (!p || !config || !dango_self_config_valid(config) || config->node_count!=p->config.node_count ||
        config->substep_count!=p->config.substep_count || config->gravity!=p->config.gravity ||
        config->ground_height!=p->config.ground_height) return -410;
    return collision_add(p,config,anchor_x,anchor_y,entity_id);
}

static int collision_add(collision_review* p,const dango_self_physics_config* config,float anchor_x,float anchor_y,int* entity_id)
{
    if (p) COLLISION_REST_WAKE_ALL(p);
    if (!p || !entity_id || !isfinite(anchor_x) || !isfinite(anchor_y)) return -410;
    if (p->preview_jump_speed_scale!=1.0f && p->count!=0) return -410;
    if (p->collection_count && (p->error || p->count!=0)) return -410;
    if (p->platform_enabled && (p->error || p->count!=0)) return -410;
    if (p->ball_scene && p->error) return -410;
    if (p->ball.state.enabled && p->count!=0) return -410;
    if (p->count>=p->capacity) return -411;
    int slot=-1;
    for (int i=0;i<p->capacity;++i) if (!p->active[i]) { slot=i; break; }
    if (slot<0) return -411;
    p->body_config[slot]=*config;
    if (slot>0)
    {
        p->bodies[slot]=&p->additional[slot-1];
        p->bodies[slot]->world_id=p->bodies[0]->world_id;
        p->bodies[slot]->ground_body_id=p->bodies[0]->ground_body_id;
        p->bodies[slot]->ground_shape_id=p->bodies[0]->ground_shape_id;
    }
    dango_self_create_rig(p->bodies[slot],&p->body_config[slot]);
    if (!collision_rig_valid(p->bodies[slot]))
    {
        dango_self_destroy_rig(p->bodies[slot]);
        return -401;
    }
    dango_self_initialize_at(p->bodies[slot],&p->body_config[slot],anchor_x,anchor_y);
    dango_ball_geometry birth_geometry;
    collision_ball_geometry(p,slot,0,0,.4,&birth_geometry);
    if (p->platform_enabled)
    {
        display_mesh mesh;
        if (!display_build(p->bodies[slot],p->body_config[slot].node_radius,&mesh) ||
            platform_separation(&mesh,&p->platform).gap<0)
        {
            dango_self_destroy_rig(p->bodies[slot]);
            return -410;
        }
    }
    if (p->ball.state.enabled)
    {
        dango_ball_geometry geometry; dango_ball_geometry_result contact;
        collision_ball_geometry(p,slot,p->ball.state.x,p->ball.state.y,p->ball.definition.radius,&geometry);
        if (!dango_ball_geometry_contact(&geometry,&contact) || contact.gap<0)
        { dango_self_destroy_rig(p->bodies[slot]); return -410; }
        geometry=birth_geometry; geometry.ball_x=p->ball.definition.x; geometry.ball_y=p->ball.definition.y;
        geometry.ball_radius=p->ball.definition.radius;
        double left,right; collision_ball_floor_range(p,&left,&right);
        if (!dango_ball_geometry_contact(&geometry,&contact) || contact.gap<0 ||
            !collision_ball_rig_contained(p,&birth_geometry,left,right))
        { dango_self_destroy_rig(p->bodies[slot]); return -410; }
    }
    p->active[slot]=1; p->entity_ids[slot]=p->next_entity_id++;
#if defined(DANGO_REST_SLEEP)
    dango_rest_clear(&p->rest,slot);
#endif
    p->anchors[2*slot]=anchor_x; p->anchors[2*slot+1]=anchor_y; ++p->count;
    memset(p->snapshot+42*slot,0,42*sizeof(float));
    int error=collision_read(p,p->snapshot);
    if (error)
    {
        dango_self_destroy_rig(p->bodies[slot]); p->active[slot]=0; p->entity_ids[slot]=0; --p->count;
        return error;
    }
    *entity_id=p->entity_ids[slot];
    p->ball_birth_geometry[slot]=birth_geometry;
    if (p->platform_enabled) memcpy(p->platform_snapshot,p->platform_contacts,sizeof p->platform_snapshot);
    if (!p->error) { ++p->scene_revision; collision_ball_publish(p); }
    return 0;
}

DANGO_API int dango_physics_world_remove(void* handle,int entity_id)
{
    collision_review* p=handle;
    if (p) COLLISION_REST_WAKE_ALL(p);
    if (!p || entity_id<=0 || p->collection_count) return -410;
    if ((p->platform_enabled || p->ball_scene) && p->error) return -410;
    int slot=-1;
    for (int i=0;i<p->capacity;++i) if (p->active[i] && p->entity_ids[i]==entity_id) { slot=i; break; }
    if (slot<0) return -412;
    dango_self_destroy_rig(p->bodies[slot]);
#if defined(DANGO_PHYSICS_LAB)
    memset(&p->lab[slot],0,sizeof p->lab[slot]);
#endif
    p->active[slot]=0; p->entity_ids[slot]=0; --p->count;
#if defined(DANGO_REST_SLEEP)
    dango_rest_clear(&p->rest,slot);
#endif
    memset(p->snapshot+42*slot,0,42*sizeof(float));
    memset(p->platform_contacts[slot],0,sizeof p->platform_contacts[slot]);
    memset(p->platform_snapshot[slot],0,sizeof p->platform_snapshot[slot]);
    p->ball_birth_geometry[slot]=(dango_ball_geometry){0};
    if (p->ball_scene)
    {
        p->ball.dango_touch=false; p->ball_dango_contact=(dango_ball_contact){0};
    }
    if (!p->error) { ++p->scene_revision; collision_ball_publish(p); }
    return 0;
}

#if defined(DANGO_BODY_DRIVE_REVIEW)
// Read-only D1 diagnostics, absent from normal product modules. Each point is
// x/y, vx/vy, actual mass, Box2D contact-data count; core precedes ring nodes.
DANGO_API int dango_physics_world_get_drive_review(void* handle, int entity_id, float* samples, int capacity)
{
    collision_review* p = handle;
    if (!p || !samples) return -410;
    int slot = -1;
    for (int i = 0; i < p->capacity; ++i)
        if (p->active[i] && p->entity_ids[i] == entity_id) { slot = i; break; }
    if (slot < 0) return -412;
    const dango_self_soft_body* body = p->bodies[slot];
    if (capacity < (body->node_count + 1) * 6) return -410;
    for (int i = 0; i <= body->node_count; ++i)
    {
        b2BodyId id = i == 0 ? body->core_body_id : body->node_body_ids[i - 1];
        b2Vec2 position = b2Body_GetPosition(id), velocity = b2Body_GetLinearVelocity(id);
        b2ContactData data[16];
        int contacts = b2Body_GetContactData(id, data, 16);
        samples[6 * i] = position.x; samples[6 * i + 1] = position.y;
        samples[6 * i + 2] = velocity.x; samples[6 * i + 3] = velocity.y;
        samples[6 * i + 4] = b2Body_GetMass(id); samples[6 * i + 5] = (float)contacts;
    }
    return 0;
}
#endif

static int collision_world_step_impl(void* handle,float time_step,const int* entity_ids,const float* controls,
    int control_count,int* state_ids,float* states,int state_capacity,int* state_count,int* contact_steps,int* latched_error)
{
    collision_review* p=handle;
    if (!p || control_count!=p->count || (control_count>0 && (!entity_ids || !controls)) || !state_ids || !states ||
        state_capacity<p->count*DANGO_PHYSICS_STATE_FLOATS || !state_count || !contact_steps || !latched_error) return -410;
    if (p->count==0 && !p->ball.state.enabled)
    {
        if (!isfinite(time_step) || time_step<=0 || time_step>.1f) return -410;
        p->furniture_prepared=false;
        *state_count=0; *contact_steps=p->contact_steps; *latched_error=p->error;
        if (!p->error) { ++p->physics_step; collision_ball_publish(p); }
        return 0;
    }
    float slot_controls[DANGO_PHYSICS_MAX_BODIES*DANGO_PHYSICS_CONTROL_FLOATS]={0};
    int seen[DANGO_PHYSICS_MAX_BODIES]={0};
    for (int index=0;index<control_count;++index)
    {
        int slot=-1;
        for (int i=0;i<p->capacity;++i) if (p->active[i] && p->entity_ids[i]==entity_ids[index]) { slot=i; break; }
        if (slot<0 || seen[slot]) return -412;
        seen[slot]=1; memcpy(slot_controls+slot*6,controls+index*6,6*sizeof(float));
    }
    float slot_states[DANGO_PHYSICS_MAX_BODIES*DANGO_PHYSICS_STATE_FLOATS]={0};
    p->furniture_prepared=false;
    p->collection_prepared=false;
    int error=dango_collision_step(p,time_step,slot_controls,slot_states,DANGO_PHYSICS_MAX_BODIES*42,contact_steps);
    if (error==-400) return -410;
    int output=0;
    for (int slot=0;slot<p->capacity;++slot) if (p->active[slot])
    {
        state_ids[output]=p->entity_ids[slot];
        memcpy(states+output*42,slot_states+slot*42,42*sizeof(float));
        ++output;
    }
    *state_count=output; *latched_error=p->error;
    return 0;
}

DANGO_API int dango_physics_world_step(void* handle,float dt,const int* ids,const float* controls,
    int count,int* state_ids,float* states,int capacity,int* out_count,int* contacts,int* error)
{
#if defined(DANGO_PHYSICS_LAB)
    collision_review* p=handle;
    if(p) { p->lab_call=false; memset(p->lab,0,sizeof p->lab); }
#endif
    return collision_world_step_impl(handle,dt,ids,controls,count,state_ids,states,capacity,out_count,contacts,error);
}
#if defined(DANGO_PHYSICS_LAB)
DANGO_API int dango_physics_lab_version(void) { return 1; }
DANGO_API int dango_physics_get_lab_version(void) { return 1; }
DANGO_API int dango_physics_world_get_lab_stats(void* handle,int id,float* values,int capacity)
{
    collision_review* p=handle;
    if(!p || !values || capacity<16) return -410;
    for(int i=0;i<p->capacity;++i) if(p->active[i] && p->entity_ids[i]==id) {
        memcpy(values,p->lab[i].stats,16*sizeof(float)); return 0;
    }
    return -412;
}
/* controls9: legacy six, stretch, real input occupancy, immediate cancel. */
DANGO_API int dango_physics_world_step_lab(void* handle,float dt,const int* ids,const float* controls,
    int count,int mode,float bias,int* state_ids,float* states,int capacity,int* out_count,int* contacts,int* error)
{
    collision_review* p=handle;
    if(!p || count!=p->count || (count && (!ids || !controls)) || !state_ids || !states ||
       capacity<p->count*42 || !out_count || !contacts || !error || !isfinite(dt) || dt<=0 || dt>.1f ||
       mode<0 || mode>3 || !isfinite(bias) || fabsf(bias)>1) return -410;
    float old[DANGO_PHYSICS_MAX_BODIES*6]={0}; int slots[DANGO_PHYSICS_MAX_BODIES],seen[DANGO_PHYSICS_MAX_BODIES]={0};
    for(int i=0;i<count;++i) {
        int slot=-1;
        for(int j=0;j<p->capacity;++j) if(p->active[j] && p->entity_ids[j]==ids[i]) { slot=j; break; }
        if(slot<0 || seen[slot]) return -412;
        seen[slot]=1; slots[i]=slot;
        const float* c=controls+9*i;
        for(int j=0;j<9;++j) if(!isfinite(c[j])) return -410;
        if(fabsf(c[0])>1 || c[1]<0 || c[1]>1 || (c[2]!=0 && c[2]!=1) || (c[3]!=0 && c[3]!=1) ||
           c[6]<0 || c[6]>1 || (c[7]!=0 && c[7]!=1) || (c[8]!=0 && c[8]!=1)) return -410;
        memcpy(old+6*i,c,6*sizeof(float));
    }
    for(int i=0;i<count;++i) {
        dango_lab_state* s=&p->lab[slots[i]]; const float* c=controls+9*i;
        s->mode=mode; s->bias=bias; s->stretch=c[6]; s->active=c[7]; s->cancel=c[8];
    }
    p->lab_call=true;
    int result=collision_world_step_impl(handle,dt,ids,old,count,state_ids,states,capacity,out_count,contacts,error);
    p->lab_call=false;
    if(p->error) memset(p->lab,0,sizeof p->lab);
    return result;
}
#endif

DANGO_API int dango_physics_world_reset(void* handle)
{
    collision_review* p=handle;
#if defined(DANGO_PHYSICS_LAB)
    if(p) { p->lab_call=false; memset(p->lab,0,sizeof p->lab); }
#endif
    if (p) COLLISION_REST_WAKE_ALL(p);
    if (!p) return -410;
#if defined(DANGO_REST_SLEEP)
    for (int slot=0;slot<p->capacity;++slot) dango_rest_clear(&p->rest,slot);
#endif
    p->furniture_prepared=false;
    p->collection_prepared=false;
    /* Preserve all valid birth definitions, rather than publishing a Reset
       state that is already outside its scene or overlapping the circle. */
    if (p->ball.state.enabled && !collision_ball_reset_geometry_valid(p,true)) { p->error=-403; return p->error; }
    for (int slot=0;slot<p->capacity;++slot) if (p->active[slot]) dango_self_destroy_rig(p->bodies[slot]);
    for (int slot=0;slot<p->capacity;++slot) if (p->active[slot])
    {
        dango_self_create_rig(p->bodies[slot],&p->body_config[slot]);
        if (!collision_rig_valid(p->bodies[slot])) return -401;
        dango_self_initialize_at(p->bodies[slot],&p->body_config[slot],p->anchors[2*slot],p->anchors[2*slot+1]);
    }
    if (p->collection_count) return collection_reset(p);
    if (p->ball_scene)
    {
        p->error=0; p->contact_steps=0;
        if (p->ball.state.enabled)
        {
            int id=p->ball.state.id;
            p->ball.state=(dango_ball_state){.enabled=1,.id=id,.x=p->ball.definition.x,.y=p->ball.definition.y,
                .vx=p->ball.initial_vx,.vy=p->ball.initial_vy,.omega=p->ball.initial_omega};
            p->ball.sleep_seconds=0; p->ball.floor_touch=false; p->ball.dango_touch=false;
            p->ball.wall_touch[0]=false; p->ball.wall_touch[1]=false;
        }
        p->ball_dango_contact=(dango_ball_contact){0}; p->ball_floor_contact=(dango_ball_contact){.object_id=-1};
#ifdef DANGO_ROUND_BALL_CONTRACT_TESTS
        p->test_ball_fault_mode=0;
#endif
    }
    if (p->platform_enabled)
    {
        p->error=0; p->contact_steps=0;
        float candidate[DANGO_PHYSICS_MAX_BODIES*42]={0};
        for (int r=0;r<p->capacity;++r) if (p->active[r])
        {
            int touched=0;
            if (!platform_contact_solve(p->bodies[r],&p->body_config[r],&p->platform,&touched)) { p->error=-402; return p->error; }
        }
        p->error=collision_read(p,candidate);
        if (!p->error)
        {
            memcpy(p->snapshot,candidate,sizeof candidate);
            memcpy(p->platform_snapshot,p->platform_contacts,sizeof p->platform_snapshot);
            ++p->scene_revision; collision_ball_publish(p);
        }
        return p->error;
    }
    if (p->ball_scene)
    {
        p->error=0; p->contact_steps=0;
        float candidate[DANGO_PHYSICS_MAX_BODIES*42]={0};
        p->error=collision_read(p,candidate);
        if (!p->error && !collision_ball_valid(p)) p->error=-403;
        if (!p->error && p->ball.state.enabled && !collision_ball_reset_geometry_valid(p,false)) p->error=-403;
        if (!p->error)
        {
            memcpy(p->snapshot,candidate,sizeof candidate);
            ++p->scene_revision; collision_ball_publish(p);
        }
        return p->error;
    }
    p->error=0; p->contact_steps=0; memset(p->snapshot,0,sizeof p->snapshot);
#ifdef DANGO_MATERIAL_CONTACT_CANDIDATE
    memset(&p->candidate_diagnostic,0,sizeof p->candidate_diagnostic);
#endif
    int result=collision_read(p,p->snapshot);
    if (!result) { ++p->scene_revision; collision_ball_publish(p); }
    return result;
}

// Additive ABI-1 capability. This changes only future launch impulses, never
// the existing rig, current velocities, cooldown, configuration or step count.
// Restoring the default remains legal after a latched fault for host cleanup.
DANGO_API int dango_physics_world_set_preview_jump_speed_scale(void* handle,float scale)
{
    collision_review* p=handle;
    if (p) COLLISION_REST_WAKE_ALL(p);
    if (!p || !isfinite(scale) || scale<.1f || scale>1.0f ||
        (scale!=1.0f && (p->error || p->count!=1))) return -410;
    if (p->preview_jump_speed_scale!=scale)
    {
        p->preview_jump_speed_scale=scale;
        if (!p->error) { ++p->scene_revision; collision_ball_publish(p); }
    }
    return 0;
}

// Opt-in local scene geometry. Existing worlds retain their original floor and
// exact stepping path unless these additive ABI-1 exports are explicitly used.
DANGO_API int dango_physics_world_set_horizontal_bounds(void* handle,float left,float right)
{
    collision_review* p=handle;
    if (p) COLLISION_REST_WAKE_ALL(p);
    if (!p || p->error || !isfinite(left) || !isfinite(right) ||
        fabsf(left)>1024 || fabsf(right)>1024 ||
        !isfinite(right-left) || right-left<2*(p->config.radial_rest_length+p->config.node_radius)) return -410;
    if (p->horizontal_bounds_enabled && left==p->horizontal_left && right==p->horizontal_right) return 0;
    if (p->collection_count) return -410;
    if (p->ball.state.enabled && !collision_ball_births_contained(p,(4.0/3)*left,(4.0/3)*right,true)) return -410;
    if (p->ball.state.enabled && (p->ball.state.x-p->ball.definition.radius<(4.0/3)*left ||
        p->ball.state.x+p->ball.definition.radius>(4.0/3)*right)) return -410;
    // Reject a shrinking floor that would strand an existing rig; no teleport or
    // partial geometry mutation is used to make invalid bounds appear successful.
    // Pinned Box2D 3.1.1 constants.h defines LINEAR_SLOP as .005 meters. A legal
    // resting contact may overlap by this solver tolerance; .001 incorrectly
    // rejected the unchanged wall after recentering a real contact pose.
    float containment_slop=.005f*b2GetLengthUnitsPerMeter();
    for (int r=0;r<p->capacity;++r) if (p->active[r])
        for (int n=0;n<16;++n)
        {
            float x=b2Body_GetPosition(p->bodies[r]->node_body_ids[n]).x;
            if (x-p->body_config[r].node_radius<left-containment_slop || x+p->body_config[r].node_radius>right+containment_slop) return -410;
        }
    dango_self_soft_body* first=p->bodies[0];
    b2ShapeDef wall_def=b2DefaultShapeDef();
    wall_def.material.friction=p->config.friction;
    wall_def.material.restitution=p->config.restitution;
    wall_def.filter.categoryBits=0x00000001;
    wall_def.filter.maskBits=0x00000002;
    b2Polygon walls[2]={
        b2MakeOffsetBox(.08f,32.0f,(b2Vec2){left-.08f,32.08f},b2Rot_identity),
        b2MakeOffsetBox(.08f,32.0f,(b2Vec2){right+.08f,32.08f},b2Rot_identity)};
    bool created[2]={false,false};
    for (int i=0;i<2;++i)
    {
        if (b2Shape_IsValid(p->horizontal_walls[i])) b2Shape_SetPolygon(p->horizontal_walls[i],&walls[i]);
        else
        {
            p->horizontal_walls[i]=b2CreatePolygonShape(first->ground_body_id,&wall_def,&walls[i]);
            created[i]=b2Shape_IsValid(p->horizontal_walls[i]);
        }
        if (!b2Shape_IsValid(p->horizontal_walls[i]))
        {
            for (int j=0;j<2;++j) if (created[j])
            {
                b2DestroyShape(p->horizontal_walls[j],false);
                p->horizontal_walls[j]=b2_nullShapeId;
            }
            // A partial allocation is not usable geometry. Preserve the last
            // safe snapshot and require explicit recovery rather than stepping.
            p->error=-401;
            return p->error;
        }
    }
    b2Body_SetTransform(first->ground_body_id,(b2Vec2){0,p->config.ground_height-.08f},b2Rot_identity);
    b2Polygon floor=b2MakeOffsetBox((right-left)/2,.08f,(b2Vec2){left+(right-left)/2,0},b2Rot_identity);
    b2Shape_SetPolygon(first->ground_shape_id,&floor);
    p->horizontal_left=left; p->horizontal_right=right; p->horizontal_bounds_enabled=true;
    ++p->scene_revision; collision_ball_publish(p);
    return 0;
}

DANGO_API int dango_physics_world_clear_horizontal_bounds(void* handle)
{
    collision_review* p=handle;
    if (p) COLLISION_REST_WAKE_ALL(p);
    if (!p || p->error || p->collection_count) return -410;
    dango_self_soft_body* first=p->bodies[0];
    if (!p->horizontal_bounds_enabled && b2Body_GetPosition(first->ground_body_id).x==0 &&
        !b2Shape_IsValid(p->horizontal_walls[0]) && !b2Shape_IsValid(p->horizontal_walls[1])) return 0;
    if (p->ball.state.enabled && !collision_ball_births_contained(p,-32.0/3,32.0/3,true)) return -410;
    for (int i=0;i<2;++i)
        if (b2Shape_IsValid(p->horizontal_walls[i]))
        {
            b2DestroyShape(p->horizontal_walls[i],false);
            p->horizontal_walls[i]=b2_nullShapeId;
        }
    b2Body_SetTransform(first->ground_body_id,(b2Vec2){0,p->config.ground_height-.08f},b2Rot_identity);
    b2Polygon floor=b2MakeBox(8.0f,.08f);
    b2Shape_SetPolygon(first->ground_shape_id,&floor);
    p->horizontal_bounds_enabled=false;
    ++p->scene_revision; collision_ball_publish(p);
    return 0;
}

DANGO_API int dango_physics_world_translate_horizontal(void* handle,float translation)
{
    collision_review* p=handle;
    if (p) COLLISION_REST_WAKE_ALL(p);
    if (!p || p->error || !isfinite(translation) || fabsf(translation)>1024) return -410;
    if (p->platform_enabled || p->ball_scene || p->collection_count) return -410;
    if (translation==0) return 0;
    if (fabsf(b2Body_GetPosition(p->bodies[0]->ground_body_id).x+translation)>1024 ||
        (p->horizontal_bounds_enabled && (fabsf(p->horizontal_left+translation)>1024 ||
            fabsf(p->horizontal_right+translation)>1024))) return -410;
    for (int r=0;r<p->capacity;++r) if (p->active[r])
        for (int n=0;n<17;++n)
        {
            b2Vec2 position=b2Body_GetPosition(display_id(p->bodies[r],n));
            if (!isfinite(position.x+translation) || fabsf(position.x+translation)>1024) return -410;
        }
    // All nodes, the core and floor move together. Velocities, entity IDs,
    // relative shape, joints, cooldowns and the reset anchors are not recreated.
    for (int r=0;r<p->capacity;++r) if (p->active[r])
        for (int n=0;n<17;++n)
        {
            b2BodyId id=display_id(p->bodies[r],n);
            b2Vec2 position=b2Body_GetPosition(id); position.x+=translation;
            b2Body_SetTransform(id,position,b2Body_GetRotation(id));
        }
    b2BodyId ground=p->bodies[0]->ground_body_id;
    b2Vec2 position=b2Body_GetPosition(ground); position.x+=translation;
    b2Body_SetTransform(ground,position,b2Body_GetRotation(ground));
    if (p->horizontal_bounds_enabled)
    {
        p->horizontal_left+=translation;
        p->horizontal_right+=translation;
    }
    int result=collision_read(p,p->snapshot);
    if (!result) { ++p->scene_revision; collision_ball_publish(p); }
    return result;
}

DANGO_API int dango_physics_world_destroy(void* handle)
{
    collision_review* p=handle;
    if (!p || !p->bodies[0]) return -410;
#if defined(DANGO_MATERIAL_IDLE_TRACE)
    if (p->idle_trace.file) { (void)fclose(p->idle_trace.file); p->idle_trace.file=NULL; }
    if (dango_it_current==&p->idle_trace) dango_it_current=NULL;
#endif
    if (b2World_IsValid(p->bodies[0]->world_id)) b2DestroyWorld(p->bodies[0]->world_id);
#ifdef DANGO_ROUND_BALL_CONTRACT_TESTS
    --round_ball_owner_count;
#endif
    free(p->bodies[0]); free(p);
    return 0;
}

DANGO_API int dango_physics_world_set_static_platform(void* handle,
    float center_x,float center_y,float width,float height,float friction)
{
    collision_review* p=handle;
    if (p) COLLISION_REST_WAKE_ALL(p);
    dango_static_platform definition={center_x,center_y,width,height,friction};
    if (!p || p->error || p->collection_count || p->ball.state.enabled || p->count>1 || !platform_valid(&definition)) return -410;
    if (p->platform_enabled)
        return p->platform.x==definition.x && p->platform.y==definition.y &&
            p->platform.width==definition.width && p->platform.height==definition.height &&
            p->platform.friction==definition.friction ? 0 : -410;
    for (int r=0;r<p->capacity;++r) if (p->active[r])
    {
        display_mesh mesh;
        if (!display_build(p->bodies[r],p->body_config[r].node_radius,&mesh) || platform_separation(&mesh,&definition).gap<0) return -410;
    }
    p->platform=definition; p->platform_enabled=true;
    for (int r=0;r<p->capacity;++r) if (p->active[r])
        if (!collision_platform_refresh(p,r)) { p->platform_enabled=false; return -410; }
    memcpy(p->platform_snapshot,p->platform_contacts,sizeof p->platform_snapshot);
    p->platform_instance=++p->next_platform_instance;
    ++p->scene_revision; collision_ball_publish(p);
    return 0;
}

DANGO_API int dango_physics_world_clear_static_platform(void* handle)
{
    collision_review* p=handle;
    if (p) COLLISION_REST_WAKE_ALL(p);
    if (!p || p->error || p->collection_count) return -410;
    bool changed=p->platform_enabled;
    if (p->platform_enabled)
        for (int r=0;r<p->capacity;++r) if (p->active[r])
            p->bodies[r]->grounded=collision_platform_floor(p,p->bodies[r]);
    p->platform_enabled=false; memset(&p->platform,0,sizeof p->platform);
    p->platform_instance=0;
    memset(p->platform_contacts,0,sizeof p->platform_contacts);
    memset(p->platform_snapshot,0,sizeof p->platform_snapshot);
    if (changed) { ++p->scene_revision; collision_ball_publish(p); }
    return 0;
}

DANGO_API int dango_physics_world_get_static_platform_contact(void* handle,int entity_id,float* values,int capacity)
{
    collision_review* p=handle;
    if (!p || !values || capacity<6) return -410;
    for (int r=0;r<p->capacity;++r) if (p->active[r] && p->entity_ids[r]==entity_id)
    {
        memcpy(values,p->platform_snapshot[r],6*sizeof(float)); return 0;
    }
    return -412;
}

// Admission is read-only. Preparing reserves only metadata; no collider,
// body transform, velocity, grounding, control or published frame is touched.
static void collision_furniture_probe(collision_review* p,int kind,uint64_t target,uint64_t expected_revision,
    const dango_static_platform* candidate,dango_furniture_probe* result,
    float contacts[DANGO_PHYSICS_MAX_BODIES][6],bool grounded[DANGO_PHYSICS_MAX_BODIES])
{
    *result=(dango_furniture_probe){.world_generation=p->world_generation,.scene_revision=p->scene_revision,
        .physics_step=p->physics_step,.minimum_gap=1024};
    memset(contacts,0,sizeof(float)*DANGO_PHYSICS_MAX_BODIES*6);
    memset(grounded,0,sizeof(bool)*DANGO_PHYSICS_MAX_BODIES);
    if (p->error) { result->rejection=8; return; }
    if (p->collection_count || p->ball.state.enabled || p->count>1) { result->rejection=2; return; }
    if (expected_revision!=p->scene_revision) { result->rejection=3; return; }
    if (kind<1 || kind>3 || (kind==1 && (target!=0 || p->platform_enabled)) ||
        (kind!=1 && (!p->platform_enabled || target==0 || target!=p->platform_instance)))
    { result->rejection=4; return; }
    if (kind!=3)
    {
        if (!candidate || !platform_valid(candidate)) { result->rejection=1; return; }
        if (!p->horizontal_bounds_enabled) { result->rejection=5; return; }
        if (candidate->x-candidate->width*.5f<p->horizontal_left ||
            candidate->x+candidate->width*.5f>p->horizontal_right ||
            candidate->y-candidate->height*.5f<p->config.ground_height ||
            candidate->y+candidate->height*.5f>32)
        { result->rejection=6; return; }
    }
    for (int r=0;r<p->capacity;++r) if (p->active[r])
    {
        bool floor=collision_platform_floor(p,p->bodies[r]);
        grounded[r]=floor;
        if (kind==3) continue;
        display_mesh mesh;
        if (!display_build(p->bodies[r],p->body_config[r].node_radius,&mesh)) { result->rejection=8; return; }
        dango_platform_contact contact=platform_separation(&mesh,candidate);
        if (!isfinite(contact.gap)) { result->rejection=8; return; }
        result->minimum_gap=fminf(result->minimum_gap,contact.gap);
        if (contact.gap<-DANGO_FURNITURE_ADMISSION_EPSILON) { result->rejection=7; return; }
        b2Vec2 core=b2Body_GetPosition(p->bodies[r]->core_body_id);
        b2Vec2 velocity=b2Body_GetLinearVelocity(p->bodies[r]->core_body_id);
        bool support=platform_support_eligible(&contact,core.y,velocity.y,candidate);
        contacts[r][0]=1; contacts[r][1]=contact.gap<=.008f ? 1.0f : 0.0f;
        contacts[r][2]=support ? 1.0f : 0.0f;
        contacts[r][3]=contact.normal.x; contacts[r][4]=contact.normal.y; contacts[r][5]=contact.gap;
        grounded[r]=floor || support;
    }
}

DANGO_API int dango_physics_get_furniture_abi_version(void) { return 1; }
DANGO_API int dango_physics_get_furniture_frame_size(void) { return sizeof(dango_furniture_frame); }

DANGO_API int dango_physics_world_probe_static_platform_change(void* handle,int kind,uint64_t target,
    uint64_t expected_revision,const dango_static_platform* candidate,dango_furniture_probe* result,int size)
{
    collision_review* p=handle;
    if (!p || !result || size!=sizeof *result) return -410;
    float contacts[DANGO_PHYSICS_MAX_BODIES][6]; bool grounded[DANGO_PHYSICS_MAX_BODIES];
    collision_furniture_probe(p,kind,target,expected_revision,candidate,result,contacts,grounded);
    return 0;
}

DANGO_API int dango_physics_world_prepare_static_platform_change(void* handle,int kind,uint64_t target,
    uint64_t expected_revision,const dango_static_platform* candidate,dango_furniture_probe* result,int size,
    dango_furniture_token* token)
{
    collision_review* p=handle;
    if (!p || !result || !token || size!=sizeof *result) return -410;
    *token=(dango_furniture_token){0};
    float contacts[DANGO_PHYSICS_MAX_BODIES][6]; bool grounded[DANGO_PHYSICS_MAX_BODIES];
    collision_furniture_probe(p,kind,target,expected_revision,candidate,result,contacts,grounded);
    if (result->rejection) return 0;
    p->prepared_kind=kind;
    p->prepared_platform=kind==3 ? (dango_static_platform){0} : *candidate;
    memcpy(p->prepared_platform_contacts,contacts,sizeof contacts);
    memcpy(p->prepared_grounded,grounded,sizeof grounded);
    p->prepared_token=(dango_furniture_token){.world_generation=p->world_generation,
        .token_id=++p->next_furniture_token,.scene_revision=p->scene_revision,.physics_step=p->physics_step};
    p->furniture_prepared=true; *token=p->prepared_token;
    return 0;
}

static bool collision_furniture_token_valid(const collision_review* p,const dango_furniture_token* token)
{
    return token && p->furniture_prepared && !p->error && token->world_generation==p->world_generation &&
        token->token_id!=0 && token->token_id==p->prepared_token.token_id &&
        token->scene_revision==p->scene_revision && token->physics_step==p->physics_step &&
        token->scene_revision==p->prepared_token.scene_revision && token->physics_step==p->prepared_token.physics_step;
}

DANGO_API int dango_physics_world_apply_prepared_static_platform_change(void* handle,const dango_furniture_token* token)
{
    collision_review* p=handle;
    if (p) COLLISION_REST_WAKE_ALL(p);
    if (!p || !collision_furniture_token_valid(p,token)) return -410;
    // No allocation or fallible solve after file commit. Apply only prepared
    // geometry and support data at this same physics boundary, exactly once.
    bool inserted=p->prepared_kind==1;
    p->platform_enabled=p->prepared_kind!=3; p->platform=p->prepared_platform;
    if (inserted) p->platform_instance=++p->next_platform_instance;
    if (!p->platform_enabled) p->platform_instance=0;
    memcpy(p->platform_contacts,p->prepared_platform_contacts,sizeof p->platform_contacts);
    memcpy(p->platform_snapshot,p->prepared_platform_contacts,sizeof p->platform_snapshot);
    for (int r=0;r<p->capacity;++r) if (p->active[r]) p->bodies[r]->grounded=p->prepared_grounded[r];
    p->furniture_prepared=false; p->last_applied_furniture_token=token->token_id;
    ++p->scene_revision; collision_ball_publish(p);
    return 0;
}

DANGO_API int dango_physics_world_cancel_prepared_static_platform_change(void* handle,const dango_furniture_token* token)
{
    collision_review* p=handle;
    if (!p || !token || token->world_generation!=p->world_generation) return -410;
    // A delayed cancellation must not cancel a newer operation.
    if (p->furniture_prepared && token->token_id==p->prepared_token.token_id) p->furniture_prepared=false;
    return 0;
}

DANGO_API int dango_physics_world_get_furniture_frame(void* handle,dango_furniture_frame* frame,int size)
{
    const collision_review* p=handle;
    if (!p || !frame || size!=sizeof *frame || p->collection_count) return -410;
    collision_furniture_read_core(p,frame);
    return 0;
}

static void collision_furniture_read_core(const collision_review* p,dango_furniture_frame* frame)
{
    memset(frame,0,sizeof *frame);
    frame->world_generation=p->world_generation; frame->scene_revision=p->frame_snapshot.snapshot.scene_revision;
    frame->physics_step=p->frame_snapshot.snapshot.step; frame->platform_instance=p->platform_instance;
    frame->last_applied_token=p->last_applied_furniture_token; frame->platform_enabled=p->platform_enabled ? 1 : 0;
    if (p->platform_enabled) frame->platform=p->platform;
    // Faults retain the last published safe body frame; report live health
    // separately so stale healthy snapshots cannot authorize more mutations.
    frame->error_code=p->error; frame->contact_steps=p->frame_snapshot.contact_steps;
    frame->body_count=p->frame_snapshot.body_count;
    memcpy(frame->entity_ids,p->frame_snapshot.entity_ids,sizeof frame->entity_ids);
    memcpy(frame->states,p->frame_snapshot.states,sizeof frame->states);
    int output=0;
    for (int r=0;r<p->capacity;++r) if (p->active[r])
    {
        memcpy(frame->contacts+6*output,p->platform_snapshot[r],6*sizeof(float));
        const dango_self_soft_body* body=p->bodies[r];
        for (int n=0;n<17;++n)
        {
            b2BodyId id=n==16 ? body->core_body_id : body->node_body_ids[n];
            frame->raw_body_ids[17*output+n]=b2StoreBodyId(id);
            if (!b2Body_IsValid(id)) { frame->error_code=-402; continue; }
            b2Vec2 position=b2Body_GetPosition(id),velocity=b2Body_GetLinearVelocity(id);
            int offset=68*output+4*n;
            frame->raw_positions_velocities[offset]=position.x; frame->raw_positions_velocities[offset+1]=position.y;
            frame->raw_positions_velocities[offset+2]=velocity.x; frame->raw_positions_velocities[offset+3]=velocity.y;
        }
        frame->seconds_since_jump[output]=body->seconds_since_jump;
        frame->drag_active[output]=body->drag_active ? 1 : 0; frame->grounded[output]=body->grounded ? 1 : 0;
        ++output;
    }
}

DANGO_API int dango_physics_get_allocated_bytes(void) { return b2GetByteCount(); }

static int collision_ball_add(void* handle,const dango_ball_definition* definition,int* id)
{
    collision_review* p=handle;
    if (!p || !definition || !id || p->error || p->collection_count || p->platform_enabled || p->ball.state.enabled || p->count>1 ||
        !isfinite(definition->x) || !isfinite(definition->y) || fabs(definition->x)>1024 || fabs(definition->y)>1024 ||
        !isfinite(definition->radius) || definition->radius<.05 || definition->radius>4 ||
        !isfinite(definition->mass) || definition->mass<.001 || definition->mass>10 ||
        !isfinite(definition->floor_friction) || definition->floor_friction<0 || definition->floor_friction>1.5 ||
        !isfinite(definition->dango_friction) || definition->dango_friction<0 || definition->dango_friction>1.5 ||
        !isfinite(definition->floor_restitution) || definition->floor_restitution<0 || definition->floor_restitution>1 ||
        !isfinite(definition->dango_restitution) || definition->dango_restitution<0 || definition->dango_restitution>1 ||
        definition->y-definition->radius<p->config.ground_height) return -410;
    double left,right; collision_ball_floor_range(p,&left,&right);
    if (definition->x-definition->radius<left || definition->x+definition->radius>right) return -410;
    for (int r=0;r<p->capacity;++r) if (p->active[r])
    {
        dango_ball_geometry geometry; dango_ball_geometry_result contact;
        collision_ball_geometry(p,r,definition->x,definition->y,definition->radius,&geometry);
        if (!dango_ball_geometry_contact(&geometry,&contact) || contact.gap<0) return -410;
        geometry=p->ball_birth_geometry[r]; geometry.ball_x=definition->x; geometry.ball_y=definition->y;
        geometry.ball_radius=definition->radius;
        if (!collision_ball_rig_contained(p,&geometry,left,right) ||
            !dango_ball_geometry_contact(&geometry,&contact) || contact.gap<0) return -410;
    }
    float candidate[DANGO_PHYSICS_MAX_BODIES*42]={0};
    int error=collision_read(p,candidate); if (error) return error;
    p->ball=(dango_native_ball){.definition=*definition,
        .state={.enabled=1,.id=++p->next_ball_id,.x=definition->x,.y=definition->y}};
    p->ball_scene=true; ++p->scene_revision;
    p->ball_dango_contact=(dango_ball_contact){0}; p->ball_floor_contact=(dango_ball_contact){.object_id=-1};
    memcpy(p->snapshot,candidate,sizeof candidate); collision_ball_publish(p); *id=p->ball.state.id;
    return 0;
}

static int collision_ball_remove(void* handle,int id)
{
    collision_review* p=handle;
    if (!p || p->error) return -410;
    if (!p->ball.state.enabled || id!=p->ball.state.id) return -412;
    float candidate[DANGO_PHYSICS_MAX_BODIES*42]={0};
    int error=collision_read(p,candidate); if (error) return error;
    p->ball=(dango_native_ball){0}; ++p->scene_revision;
    p->ball_dango_contact=(dango_ball_contact){0}; p->ball_floor_contact=(dango_ball_contact){0};
    memcpy(p->snapshot,candidate,sizeof candidate); collision_ball_publish(p);
    return 0;
}

DANGO_API int dango_physics_get_round_ball_abi_version(void)
{ return DANGO_ROUND_BALL_ABI_VERSION; }
DANGO_API int dango_physics_get_round_ball_frame_size(void)
{ return (int)sizeof(dango_round_ball_frame); }
DANGO_API double dango_physics_get_round_ball_gravity_scale(void)
{ return DANGO_ROUND_BALL_GRAVITY_SCALE; }
#if defined(DANGO_REST_SLEEP)
// Read-only presentation/test hook: 1 for each resting entity, in slot order.
DANGO_API int dango_physics_world_get_rest_state(void* handle,int* entity_ids,int* resting,int capacity,int* count)
{
    collision_review* p=handle;
    if (!p || !entity_ids || !resting || !count || capacity<p->count) return -410;
    int output=0;
    for (int r=0;r<p->capacity;++r) if (p->active[r])
    { entity_ids[output]=p->entity_ids[r]; resting[output]=p->rest.slot[r].resting ? 1 : 0; ++output; }
    *count=output;
    return 0;
}
#endif

DANGO_API int dango_physics_world_add_round_ball(void* handle,const dango_ball_definition* definition,int* id)
{ return collision_ball_add(handle,definition,id); }
DANGO_API int dango_physics_world_remove_round_ball(void* handle,int id)
{ return collision_ball_remove(handle,id); }
DANGO_API int dango_physics_world_get_round_ball_frame(void* handle,dango_round_ball_frame* frame,int frame_size)
{
    collision_review* p=handle;
    if (!p || !frame || frame_size!=(int)sizeof(dango_round_ball_frame) || p->collection_count) return -410;
    *frame=p->frame_snapshot;
    frame->error_code=p->error;
    return 0;
}

#include "dango_furniture_collection.h"

#ifdef DANGO_ROUND_BALL_CONTRACT_TESTS
int dango_round_ball_test_point(const dango_ball_point_input* input,dango_ball_point_result* result)
{
    if (!input || !result || !isfinite(input->nx) || !isfinite(input->ny) ||
        fabs(hypot(input->nx,input->ny)-1)>1e-8 || !isfinite(input->dango_mass) || input->dango_mass<=0 ||
        !isfinite(input->ball_mass) || input->ball_mass<=0 || !isfinite(input->radius) || input->radius<=0 ||
        !isfinite(input->inertia) || input->inertia<=0 || !isfinite(input->dvx) || !isfinite(input->dvy) ||
        !isfinite(input->bvx) || !isfinite(input->bvy) || !isfinite(input->omega) ||
        !isfinite(input->restitution) || input->restitution<0 || input->restitution>1) return -410;
    double nx=input->nx,ny=input->ny,tx=-ny,ty=nx;
#if DANGO_ROUND_BALL_TEST_VARIANT == 3
    double gx=nx,gy=ny,hx=tx,hy=ty;
#else
    double gx=(4.0/3)*nx,gy=ny,hx=(4.0/3)*tx,hy=ty;
#endif
    *result=(dango_ball_point_result){0};
    result->knn=(gx*gx+gy*gy)/input->dango_mass+1/input->ball_mass;
    result->knt=(gx*hx+gy*hy)/input->dango_mass;
    result->ktt=(hx*hx+hy*hy)/input->dango_mass+1/input->ball_mass+input->radius*input->radius/input->inertia;
    result->vn_before=nx*input->bvx+ny*input->bvy-gx*input->dvx-gy*input->dvy;
    result->vt_before=tx*input->bvx+ty*input->bvy-input->radius*input->omega-hx*input->dvx-hy*input->dvy;
    result->target=dango_ball_restitution_target(-result->vn_before,input->restitution,true);
    dango_ball_impulse impulse;
    if (!dango_ball_contact_impulse(result->knn,result->knt,result->ktt,
        result->vn_before,result->vt_before,result->target,input->friction,&impulse)) return -410;
#if DANGO_ROUND_BALL_TEST_VARIANT == 1
    impulse=(dango_ball_impulse){0};
#endif
    result->jn=impulse.normal; result->jt=impulse.tangent;
#if DANGO_ROUND_BALL_TEST_VARIANT == 2
    result->dvx=input->dvx; result->dvy=input->dvy;
#else
    result->dvx=input->dvx-(gx*impulse.normal+hx*impulse.tangent)/input->dango_mass;
    result->dvy=input->dvy-(gy*impulse.normal+hy*impulse.tangent)/input->dango_mass;
#endif
    result->bvx=input->bvx+(nx*impulse.normal+tx*impulse.tangent)/input->ball_mass;
    result->bvy=input->bvy+(ny*impulse.normal+ty*impulse.tangent)/input->ball_mass;
    result->omega=input->omega-input->radius*impulse.tangent/input->inertia;
    result->vn_after=nx*result->bvx+ny*result->bvy-gx*result->dvx-gy*result->dvy;
    result->vt_after=tx*result->bvx+ty*result->bvy-input->radius*result->omega-hx*result->dvx-hy*result->dvy;
    return 0;
}

int dango_round_ball_test_floor(const dango_ball_point_input* input,dango_ball_point_result* result)
{
    if (!input || !result || !isfinite(input->nx) || !isfinite(input->ny) ||
        fabs(hypot(input->nx,input->ny)-1)>1e-8 || !isfinite(input->ball_mass) || input->ball_mass<=0 ||
        !isfinite(input->radius) || input->radius<=0 || !isfinite(input->inertia) || input->inertia<=0 ||
        !isfinite(input->bvx) || !isfinite(input->bvy) || !isfinite(input->omega) ||
        !isfinite(input->restitution) || input->restitution<0 || input->restitution>1) return -410;
    double nx=input->nx,ny=input->ny,tx=-ny,ty=nx;
    *result=(dango_ball_point_result){.knn=1/input->ball_mass,
        .ktt=1/input->ball_mass+input->radius*input->radius/input->inertia};
    result->vn_before=nx*input->bvx+ny*input->bvy;
    result->vt_before=tx*input->bvx+ty*input->bvy-input->radius*input->omega;
    result->target=dango_ball_restitution_target(-result->vn_before,input->restitution,true);
    dango_ball_impulse impulse;
    if (!dango_ball_contact_impulse(result->knn,0,result->ktt,result->vn_before,result->vt_before,
        result->target,input->friction,&impulse)) return -410;
    result->jn=impulse.normal; result->jt=impulse.tangent;
    result->dvx=input->dvx; result->dvy=input->dvy;
    result->bvx=input->bvx+(nx*impulse.normal+tx*impulse.tangent)/input->ball_mass;
    result->bvy=input->bvy+(ny*impulse.normal+ty*impulse.tangent)/input->ball_mass;
    result->omega=input->omega-input->radius*impulse.tangent/input->inertia;
    result->vn_after=nx*result->bvx+ny*result->bvy;
    result->vt_after=tx*result->bvx+ty*result->bvy-input->radius*result->omega;
    return 0;
}

int dango_round_ball_test_geometry(const dango_ball_geometry* input,dango_ball_geometry_result* result)
{ return dango_ball_geometry_contact(input,result) ? 0 : -410; }

int dango_round_ball_test_world_geometry(void* handle,int entity_id,double x,double y,double radius,
    dango_ball_geometry* geometry,dango_ball_geometry_result* result)
{
    collision_review* p=handle;
    if (!p || !geometry || !result) return -410;
    for (int r=0;r<p->capacity;++r) if (p->active[r] && p->entity_ids[r]==entity_id)
    { collision_ball_geometry(p,r,x,y,radius,geometry); return dango_round_ball_test_geometry(geometry,result); }
    return -412;
}

int dango_round_ball_test_add(void* handle,const dango_ball_definition* definition,int* id)
{ return collision_ball_add(handle,definition,id); }
int dango_round_ball_test_remove(void* handle,int id)
{ return collision_ball_remove(handle,id); }

int dango_round_ball_test_read(void* handle,dango_ball_snapshot* snapshot)
{
    collision_review* p=handle; if (!p || !snapshot) return -410;
    *snapshot=p->frame_snapshot.snapshot; return 0;
}

int dango_round_ball_test_dango_frame(void* handle,int entity_id,float* values,int capacity)
{
    collision_review* p=handle;
    if (!p || !values || capacity<42) return -410;
    for (int r=0;r<p->frame_snapshot.body_count;++r) if (p->frame_snapshot.entity_ids[r]==entity_id)
    { memcpy(values,p->frame_snapshot.states+42*r,42*sizeof(float)); return 0; }
    return -412;
}

int dango_round_ball_test_initial_velocity(void* handle,double vx,double vy,double omega)
{
    collision_review* p=handle;
    if (!p || p->error || !p->ball.state.enabled || p->physics_step!=0 || !isfinite(vx) || !isfinite(vy) ||
        !isfinite(omega) || hypot(vx,vy)>12 || fabs(omega)>40) return -410;
    p->ball.state.vx=vx; p->ball.state.vy=vy; p->ball.state.omega=omega;
    p->ball.initial_vx=vx; p->ball.initial_vy=vy; p->ball.initial_omega=omega;
    ++p->scene_revision; collision_ball_publish(p); return 0;
}
int dango_round_ball_test_fault(void* handle,int mode)
{
    collision_review* p=handle;
    if (!p || p->error || !p->ball_scene || p->count!=1 || mode<1 || mode>2 ||
        (mode==1 && !p->ball.state.enabled)) return -410;
    p->test_ball_fault_mode=mode; return 0;
}
int dango_round_ball_test_owner_count(void) { return round_ball_owner_count; }
#endif

#ifdef DANGO_STATIC_PLATFORM_CONTRACT_TESTS
int dango_static_platform_contract_tests(void)
{
    dango_static_platform p={0,1,4,.3f,.72f};
    dango_platform_contact top={.normal={0,1},.gap=.001f,.sample=0};
    int failures=0;
    if (!platform_support_eligible(&top,2,.499999f,&p)) ++failures;
    if (platform_support_eligible(&top,2,.5f,&p)) ++failures;
    if (platform_support_eligible(&top,2,.500001f,&p)) ++failures;
    top.normal=(b2Vec2){1,0};
    if (platform_support_eligible(&top,2,0,&p)) ++failures;
    top.normal=(b2Vec2){0,-1};
    if (platform_support_eligible(&top,0,0,&p)) ++failures;
    top.normal=(b2Vec2){0,1}; top.gap=.009f;
    if (platform_support_eligible(&top,2,0,&p)) ++failures;
    return failures;
}

int dango_static_platform_failure_tests(void)
{
    const dango_self_physics_config config={
        .node_count=16,.substep_count=4,.radial_rest_length=.68f,.radial_minimum_ratio=.65f,.radial_maximum_ratio=1.22f,
        .perimeter_minimum_ratio=.73f,.perimeter_maximum_ratio=1.07f,.radial_hertz=5.2f,.perimeter_hertz=20.1f,
        .radial_damping=1.85f,.perimeter_damping=1.85f,.area_strength=4.8f,.ground_deformation_strength=3.5f,
        .repulsion_distance=.09f,.repulsion_strength=.3f,.repulsion_maximum_force=3.5f,.core_exclusion_ratio=.25f,
        .angle_strength=10,.local_angle_balance_strength=1.7f,.top_inward_force=.5f,.gravity=9.8f,.core_mass=.12f,.node_mass=.015f,
        .drop_height=.42f,.ground_height=-.72f,.node_radius=.045f,.friction=.72f,.restitution=.02f,.contact_hertz=28,
        .linear_damping=1.8f,.drag_stiffness=10,.drag_damping=2.2f,.drag_maximum_force=8,.horizontal_force=2.4f,
        .maximum_horizontal_speed=3.2f,.air_control_ratio=.35f,.downward_force=2.2f,.jump_impulse=.42f,
        .jump_cooldown_seconds=.45f,.maximum_speed=12,.minimum_area=.42f
    };
    int failures=0,initial_bytes=b2GetByteCount(),id=0,state_id=0,count=0,contacts=0,error=0;
    float controls[6]={0},states[42]={0},safe[42],safe_contact[6],contact[6];
    void* handle=NULL;
    if (dango_physics_world_create(&config,1,&handle)!=0) return 1;
    if (dango_physics_world_set_static_platform(handle,0,1,4,.3f,.72f)!=0 ||
        dango_physics_world_add(handle,0,2.5f,&id)!=0) { dango_physics_world_destroy(handle); return 1; }
    for (int step=0;step<240;++step)
        if (dango_physics_world_step(handle,1.0f/60,&id,controls,1,&state_id,states,42,&count,&contacts,&error)!=0 || error) ++failures;
    memcpy(safe,states,sizeof safe);
    dango_physics_world_get_static_platform_contact(handle,id,safe_contact,6);
    int safe_contacts=contacts;
    collision_review* world=handle;
    // The optional editor must preserve both publication and raw live state
    // when preparing/cancelling or refusing, and cannot reuse a faulted token.
    dango_furniture_frame old_frame,new_frame;
    dango_furniture_probe probe; dango_furniture_token token;
    if (dango_physics_world_get_furniture_frame(handle,&old_frame,sizeof old_frame)!=0) ++failures;
    int prepared_bytes=b2GetByteCount();
    if (dango_physics_world_prepare_static_platform_change(handle,3,world->platform_instance,world->scene_revision,
        NULL,&probe,sizeof probe,&token)!=0 || probe.rejection || !token.token_id) ++failures;
    if (dango_physics_world_get_furniture_frame(handle,&new_frame,sizeof new_frame)!=0 ||
        memcmp(&old_frame,&new_frame,sizeof old_frame) || b2GetByteCount()!=prepared_bytes) ++failures;
    dango_furniture_token cancelled=token;
    if (dango_physics_world_cancel_prepared_static_platform_change(handle,&cancelled)!=0 ||
        dango_physics_world_apply_prepared_static_platform_change(handle,&cancelled)!=-410) ++failures;
    if (dango_physics_world_get_furniture_frame(handle,&new_frame,sizeof new_frame)!=0 ||
        memcmp(&old_frame,&new_frame,sizeof old_frame)) ++failures;
    if (dango_physics_world_prepare_static_platform_change(handle,3,world->platform_instance,world->scene_revision,
        NULL,&probe,sizeof probe,&token)!=0 || probe.rejection) ++failures;
    world->test_platform_collapse_substep=4; // Corrupt real geometry after four actual supported substeps.
    if (dango_physics_world_step(handle,1.0f/60,&id,controls,1,&state_id,states,42,&count,&contacts,&error)!=0 || error!=-402) ++failures;
    if (state_id!=id || count!=1 || contacts!=safe_contacts || memcmp(states,safe,sizeof safe)) ++failures;
    if (dango_physics_world_apply_prepared_static_platform_change(handle,&token)!=-410 ||
        dango_physics_world_get_furniture_frame(handle,&new_frame,sizeof new_frame)!=0 || new_frame.error_code!=-402 ||
        new_frame.scene_revision!=old_frame.scene_revision || new_frame.physics_step!=old_frame.physics_step ||
        memcmp(new_frame.states,old_frame.states,sizeof old_frame.states)) ++failures;
    if (dango_physics_world_prepare_static_platform_change(handle,3,world->platform_instance,world->scene_revision,
        NULL,&probe,sizeof probe,&token)!=0 || probe.rejection!=8 || token.token_id) ++failures;
    dango_physics_world_get_static_platform_contact(handle,id,contact,6);
    if (memcmp(contact,safe_contact,sizeof contact)) ++failures;
    if (dango_physics_world_remove(handle,id)!=-410 || dango_physics_world_clear_static_platform(handle)!=-410 ||
        dango_physics_world_set_static_platform(handle,0,1,4,.3f,.72f)!=-410) ++failures;
    if (dango_physics_world_step(handle,1.0f/60,&id,controls,1,&state_id,states,42,&count,&contacts,&error)!=0 ||
        error!=-402 || memcmp(states,safe,sizeof safe) || contacts!=safe_contacts) ++failures;
    if (dango_physics_world_reset(handle)!=0) ++failures;
    for (int step=0;step<240;++step)
        if (dango_physics_world_step(handle,1.0f/60,&id,controls,1,&state_id,states,42,&count,&contacts,&error)!=0 || error) ++failures;
    dango_physics_world_get_static_platform_contact(handle,id,contact,6);
    if (state_id!=id || !contact[0] || !contact[2]) ++failures;
    if (dango_physics_world_destroy(handle)!=0 || b2GetByteCount()!=initial_bytes) ++failures;
    return failures;
}
#endif

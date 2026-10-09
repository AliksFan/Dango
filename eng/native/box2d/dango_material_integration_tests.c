// Isolated source-equivalent integration checks. No accepted DLL is loaded,
// rebuilt, replaced, or instrumented by this executable.
#include <math.h>
#include <stdio.h>
#include <string.h>
#include "box2d/box2d.h"
#include "dango_self_physics_internal.h"

static int integration_material_mode;
static unsigned long long integration_world_steps;
static unsigned int integration_checks;
static void integration_world_step(b2WorldId world, float dt, int substeps);

#define DANGO_MATERIAL_EXPERIMENT_MODE integration_material_mode
#define b2World_Step integration_world_step
#include "dango_box2d_bridge.c"
#include "dango_collision_review.c"
#undef b2World_Step
#undef DANGO_MATERIAL_EXPERIMENT_MODE

static void integration_world_step(b2WorldId world, float dt, int substeps)
{
    ++integration_world_steps;
    b2World_Step(world, dt, substeps);
}

// Fixed behavioral contract: an invalid material geometry must fail before
// integration, preserve the last legal outer-frame snapshot, and remain
// latched across later controls until the caller explicitly resets the rig.
#define INTEGRATION_REQUIRE(c) do { \
    ++integration_checks; \
    if (!(c)) { \
        fprintf(stderr, "FAIL mode=%d candidate=%d fixture=%d line=%d: %s\n", \
            mode, candidate ? 1 : 0, fixture, __LINE__, #c); \
        failed = 1; goto cleanup; \
    } \
} while (0)

static dango_self_physics_config integration_defaults(bool candidate)
{
    dango_self_physics_config config = {
        .node_count = 16, .substep_count = 4, .radial_rest_length = .68f,
        .radial_minimum_ratio = .65f, .radial_maximum_ratio = 1.22f,
        .perimeter_minimum_ratio = .73f, .perimeter_maximum_ratio = 1.07f,
        .radial_hertz = 5.2f, .perimeter_hertz = 20.1f,
        .radial_damping = 1.85f, .perimeter_damping = 1.85f,
        .area_strength = 4.8f, .ground_deformation_strength = 3.5f,
        .repulsion_distance = .09f, .repulsion_strength = .3f,
        .repulsion_maximum_force = 3.5f, .core_exclusion_ratio = .25f,
        .angle_strength = 10, .local_angle_balance_strength = 1.7f,
        .top_inward_force = .5f, .gravity = 9.8f,
        .core_mass = .12f, .node_mass = .015f, .drop_height = .42f,
        .ground_height = -.72f, .node_radius = .045f, .friction = .72f,
        .restitution = .02f, .contact_hertz = 28, .linear_damping = 1.8f,
        .drag_stiffness = 10, .drag_damping = 2.2f, .drag_maximum_force = 8,
        .horizontal_force = 2.4f, .maximum_horizontal_speed = 3.2f,
        .air_control_ratio = .35f, .downward_force = 2.2f,
        .jump_impulse = .42f, .jump_cooldown_seconds = .45f,
        .maximum_speed = 12, .minimum_area = .42f
    };
    if (candidate)
    {
        config.radial_hertz = 1.82f;
        config.perimeter_hertz = 15.075f;
        config.top_inward_force = 0;
        config.ground_deformation_strength = 0;
    }
    return config;
}

static int integration_step(void* handle, int entity_id, const float* controls,
    float* row, int* state_id, int* count, int* contacts, int* latch)
{
    return dango_physics_world_step(handle, .0166666f, &entity_id, controls, 1,
        state_id, row, DANGO_PHYSICS_STATE_FLOATS, count, contacts, latch);
}

static bool integration_same_rig(const dango_self_soft_body* a,
    const dango_self_soft_body* b)
{
    return a->node_count == b->node_count &&
        !memcmp(&a->world_id, &b->world_id, sizeof a->world_id) &&
        !memcmp(&a->core_body_id, &b->core_body_id, sizeof a->core_body_id) &&
        !memcmp(&a->core_shape_id, &b->core_shape_id, sizeof a->core_shape_id) &&
        !memcmp(a->node_body_ids, b->node_body_ids, sizeof a->node_body_ids) &&
        !memcmp(a->node_shape_ids, b->node_shape_ids, sizeof a->node_shape_ids) &&
        !memcmp(a->radial_joint_ids, b->radial_joint_ids, sizeof a->radial_joint_ids) &&
        !memcmp(a->perimeter_joint_ids, b->perimeter_joint_ids, sizeof a->perimeter_joint_ids);
}

static void integration_capture(const dango_self_soft_body* body,
    b2Vec2* positions, b2Vec2* velocities)
{
    for (int i = 0; i <= body->node_count; ++i)
    {
        b2BodyId id = i < body->node_count ? body->node_body_ids[i] : body->core_body_id;
        positions[i] = b2Body_GetPosition(id);
        velocities[i] = b2Body_GetLinearVelocity(id);
    }
}

static int integration_healthy_case(int mode, bool candidate)
{
    const int fixture = -1;
    int failed = 0, entity_id = 0, state_id = 0, count = 0, contacts = 0, latch = 0;
    void* handle = NULL;
    dango_self_physics_config config = integration_defaults(candidate);
    float controls[6] = {0}, row[42] = {0};
    bool reached_ground = false;
    unsigned long long before = integration_world_steps;
    integration_material_mode = mode;
    INTEGRATION_REQUIRE(dango_physics_world_create(&config, 1, &handle) == 0);
    INTEGRATION_REQUIRE(dango_physics_world_add(handle, 0, 0, &entity_id) == 0);
    collision_review* world = handle;
    for (int i = 0; i < 16; ++i)
    {
        INTEGRATION_REQUIRE(b2DistanceJoint_IsSpringEnabled(world->bodies[0]->radial_joint_ids[i]));
        INTEGRATION_REQUIRE(b2DistanceJoint_IsLimitEnabled(world->bodies[0]->radial_joint_ids[i]) == ((mode & 32) == 0));
        INTEGRATION_REQUIRE(b2DistanceJoint_IsLimitEnabled(world->bodies[0]->perimeter_joint_ids[i]));
    }
    const float start_y = world->snapshot[33];
    for (int frame = 0; frame < 120; ++frame)
    {
        controls[0] = frame >= 60 && frame < 90 ? .25f : 0;
        INTEGRATION_REQUIRE(integration_step(handle, entity_id, controls, row,
            &state_id, &count, &contacts, &latch) == 0);
        INTEGRATION_REQUIRE(latch == 0 && world->error == 0);
        INTEGRATION_REQUIRE(count == 1 && state_id == entity_id);
        for (int i = 0; i < 42; ++i) INTEGRATION_REQUIRE(isfinite(row[i]));
        INTEGRATION_REQUIRE(row[34] >= config.minimum_area && row[37] <= config.maximum_speed);
        INTEGRATION_REQUIRE(row[41] == 16 && collision_rig_valid(world->bodies[0]));
        reached_ground = reached_ground || row[39] == 1;
    }
    INTEGRATION_REQUIRE(integration_world_steps >= before + 120);
    INTEGRATION_REQUIRE(reached_ground && row[33] < start_y - .05f);
    printf("healthy mode=%d candidate=%d frames=120 steps=%llu\n", mode,
        candidate ? 1 : 0, integration_world_steps - before);
cleanup:
    if (handle && dango_physics_world_destroy(handle) != 0)
    {
        fprintf(stderr, "FAIL destroy mode=%d candidate=%d\n", mode, candidate ? 1 : 0);
        failed = 1;
    }
    return failed;
}

static double integration_orientation(b2Vec2 a, b2Vec2 b, b2Vec2 c)
{
    return ((double)b.x - a.x) * ((double)c.y - a.y) -
        ((double)b.y - a.y) * ((double)c.x - a.x);
}

static int integration_fault_case(int mode, bool candidate, int fixture)
{
    const int expected = mode == 2 ? -95 : -94;
    int failed = 0, entity_id = 0, state_id = 0, count = 0, contacts = 0, latch = 0;
    void* handle = NULL;
    dango_self_physics_config config = integration_defaults(candidate);
    float controls[6] = {0}, row[42] = {0}, legal[42];
    b2Vec2 points[16], positions[17], velocities[17], after_positions[17], after_velocities[17];
    integration_material_mode = mode;
    INTEGRATION_REQUIRE(dango_physics_world_create(&config, 1, &handle) == 0);
    INTEGRATION_REQUIRE(dango_physics_world_add(handle, 0, 0, &entity_id) == 0);
    collision_review* world = handle;
    dango_self_soft_body* body = world->bodies[0];
    for (int frame = 0; frame < 6; ++frame)
    {
        INTEGRATION_REQUIRE(integration_step(handle, entity_id, controls, row,
            &state_id, &count, &contacts, &latch) == 0);
        INTEGRATION_REQUIRE(latch == 0 && count == 1 && state_id == entity_id);
    }
    memcpy(legal, row, sizeof legal);
    INTEGRATION_REQUIRE(!memcmp(legal, world->snapshot, sizeof legal));
    const dango_self_soft_body original_rig = *body;
    const b2Vec2 center = b2Body_GetPosition(body->core_body_id);
    for (int i = 0; i < 16; ++i)
    {
        const float angle = (float)(6.28318530717958647692 * i / 16);
        points[i] = (b2Vec2){center.x + body->radial_rest_length * cosf(angle),
            center.y + body->radial_rest_length * sinf(angle)};
    }
    if (fixture == 0)
    {
        points[1] = points[0];
        INTEGRATION_REQUIRE(!memcmp(&points[0], &points[1], sizeof points[0]));
    }
    else
    {
        b2Vec2 swap = points[2]; points[2] = points[3]; points[3] = swap;
        // Independent orientation oracle: nonadjacent edges (1,2) and (3,4)
        // cross strictly in their interiors; this is not a helper self-check.
        INTEGRATION_REQUIRE(integration_orientation(points[1], points[2], points[3]) *
            integration_orientation(points[1], points[2], points[4]) < 0);
        INTEGRATION_REQUIRE(integration_orientation(points[3], points[4], points[1]) *
            integration_orientation(points[3], points[4], points[2]) < 0);
    }
    for (int i = 0; i < 16; ++i)
        b2Body_SetTransform(body->node_body_ids[i], points[i], b2Rot_identity);
    integration_capture(body, positions, velocities);
    const unsigned long long before = integration_world_steps;
    const int prior_contacts = world->contact_steps;
    INTEGRATION_REQUIRE(integration_step(handle, entity_id, controls, row,
        &state_id, &count, &contacts, &latch) == 0);
    INTEGRATION_REQUIRE(latch == expected && world->error == expected);
    INTEGRATION_REQUIRE(integration_world_steps == before);
    INTEGRATION_REQUIRE(count == 1 && state_id == entity_id && contacts == prior_contacts);
    INTEGRATION_REQUIRE(!memcmp(legal, row, sizeof legal));
    INTEGRATION_REQUIRE(!memcmp(legal, world->snapshot, sizeof legal));
    INTEGRATION_REQUIRE(world->bodies[0] == body && integration_same_rig(body, &original_rig));
    integration_capture(body, after_positions, after_velocities);
    INTEGRATION_REQUIRE(!memcmp(positions, after_positions, sizeof positions));
    INTEGRATION_REQUIRE(!memcmp(velocities, after_velocities, sizeof velocities));
    // Even later active controls cannot clear the latch or integrate/rebuild.
    const float active_controls[6] = {.75f, .2f, 1, 1, .25f, 1.25f};
    for (int repeat = 0; repeat < 3; ++repeat)
    {
        memset(row, 0, sizeof row); latch = 0;
        INTEGRATION_REQUIRE(integration_step(handle, entity_id, active_controls, row,
            &state_id, &count, &contacts, &latch) == 0);
        INTEGRATION_REQUIRE(latch == expected && world->error == expected);
        INTEGRATION_REQUIRE(integration_world_steps == before);
        INTEGRATION_REQUIRE(count == 1 && state_id == entity_id && contacts == prior_contacts);
        INTEGRATION_REQUIRE(!memcmp(legal, row, sizeof legal));
        INTEGRATION_REQUIRE(!memcmp(legal, world->snapshot, sizeof legal));
        INTEGRATION_REQUIRE(world->bodies[0] == body && integration_same_rig(body, &original_rig));
        integration_capture(body, after_positions, after_velocities);
        INTEGRATION_REQUIRE(!memcmp(positions, after_positions, sizeof positions));
        INTEGRATION_REQUIRE(!memcmp(velocities, after_velocities, sizeof velocities));
    }
    INTEGRATION_REQUIRE(dango_physics_world_reset(handle) == 0);
    INTEGRATION_REQUIRE(world->error == 0 && world->count == 1 && world->entity_ids[0] == entity_id);
    INTEGRATION_REQUIRE(collision_rig_valid(world->bodies[0]));
    INTEGRATION_REQUIRE(!memcmp(&original_rig.world_id, &world->bodies[0]->world_id,
        sizeof original_rig.world_id));
    for (int frame = 0; frame < 60; ++frame)
    {
        INTEGRATION_REQUIRE(integration_step(handle, entity_id, controls, row,
            &state_id, &count, &contacts, &latch) == 0);
        INTEGRATION_REQUIRE(latch == 0 && count == 1 && state_id == entity_id);
        for (int i = 0; i < 42; ++i) INTEGRATION_REQUIRE(isfinite(row[i]));
    }
    INTEGRATION_REQUIRE(integration_world_steps > before);
    printf("fault mode=%d candidate=%d fixture=%s latch=%d frozen_steps=0 reset_frames=60\n",
        mode, candidate ? 1 : 0, fixture == 0 ? "collapsed-edge" : "self-cross", expected);
cleanup:
    if (handle && dango_physics_world_destroy(handle) != 0)
    {
        fprintf(stderr, "FAIL destroy mode=%d candidate=%d fixture=%d\n", mode,
            candidate ? 1 : 0, fixture);
        failed = 1;
    }
    return failed;
}

int main(void)
{
    int failed = 0;
    const int modes[] = {0, 1, 2, 3, 7, 15, 23, 39};
    const int mode_count = (int)(sizeof modes / sizeof modes[0]);
    for (int candidate = 0; candidate < 2; ++candidate)
    {
        for (int i = 0; i < mode_count; ++i)
            failed |= integration_healthy_case(modes[i], candidate != 0);
        for (int i = 1; i < mode_count; ++i)
            for (int fixture = 0; fixture < 2; ++fixture)
                failed |= integration_fault_case(modes[i], candidate != 0, fixture);
    }
    printf("material integration: checks=%u healthy_cases=%d fault_cases=%d result=%s\n",
        integration_checks, 2 * mode_count, 4 * (mode_count - 1), failed ? "FAIL" : "PASS");
    return failed ? 1 : 0;
}

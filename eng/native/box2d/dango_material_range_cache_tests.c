// Real pinned-Box2D setter regression. Both the repair and legacy targets run
// these identical behavioral assertions; the legacy target must fail the
// unchanged-range assertion after an actual spring has generated a reaction.
// This executable neither loads nor replaces a production DLL.
#include <math.h>
#include <stdio.h>
#include <string.h>
#include "dango_box2d_bridge.c"

static unsigned int range_checks;

#define RANGE_REQUIRE(condition) do { \
    ++range_checks; \
    if (!(condition)) { \
        fprintf(stderr, "FAIL range-cache line=%d: %s\n", __LINE__, #condition); \
        failed = 1; goto cleanup; \
    } \
} while (0)

static dango_self_physics_config range_configuration(void)
{
    dango_self_physics_config config = {
        .node_count = 16, .substep_count = 4, .radial_rest_length = .68f,
        .radial_minimum_ratio = .65f, .radial_maximum_ratio = 1.22f,
        .perimeter_minimum_ratio = .73f, .perimeter_maximum_ratio = 1.07f,
        .radial_hertz = 1.82f, .perimeter_hertz = 15.075f,
        .radial_damping = 1.85f, .perimeter_damping = 1.85f,
        .area_strength = 4.8f, .ground_deformation_strength = 0,
        .repulsion_distance = .09f, .repulsion_strength = .3f,
        .repulsion_maximum_force = 3.5f, .core_exclusion_ratio = .25f,
        .angle_strength = 10, .local_angle_balance_strength = 1.7f,
        .top_inward_force = 0, .gravity = 9.8f,
        .core_mass = .12f, .node_mass = .015f, .drop_height = .42f,
        .ground_height = -.72f, .node_radius = .045f, .friction = .72f,
        .restitution = .02f, .contact_hertz = 28, .linear_damping = 1.8f,
        .drag_stiffness = 10, .drag_damping = 2.2f, .drag_maximum_force = 8,
        .horizontal_force = 2.4f, .maximum_horizontal_speed = 3.2f,
        .air_control_ratio = .35f, .downward_force = 2.2f,
        .jump_impulse = .42f, .jump_cooldown_seconds = .45f,
        .maximum_speed = 12, .minimum_area = .42f
    };
    return config;
}

static bool range_same_force(b2Vec2 first, b2Vec2 second)
{
    // An unchanged-range parameter refresh performs no integration. Thus the
    // already solved reaction must survive exactly, not merely within a gate.
    return memcmp(&first.x, &second.x, sizeof(float)) == 0 &&
        memcmp(&first.y, &second.y, sizeof(float)) == 0;
}

static void range_load_and_step(dango_self_soft_body* body, b2Vec2 direction)
{
    // Equal/opposite forces keep this excitation internal to the 17-body rig.
    // The selected radial joint stays strictly inside its lower/upper limits,
    // so its nonzero public constraint force comes from the real spring.
    b2Vec2 force = b2MulSV(.08f, direction);
    b2Body_ApplyForceToCenter(body->node_body_ids[0], force, true);
    b2Body_ApplyForceToCenter(body->core_body_id, b2Neg(force), true);
    b2World_Step(body->world_id, 1.0f / 240.0f, 1);
}

int main(void)
{
    int failed = 0, outstanding = -1;
    void* handle = NULL;
    b2WorldId created_world = b2_nullWorldId;
    dango_self_physics_config config = range_configuration();
    b2Vec2 before = b2Vec2_zero, after = b2Vec2_zero;
    float first_reaction = 0, changed_reaction = 0;

    RANGE_REQUIRE(b2GetByteCount() == 0);
    RANGE_REQUIRE(dango_box2d_self_soft_body_create(&config, &handle) == 0);
    dango_self_soft_body* body = handle;
    created_world = body->world_id;
    RANGE_REQUIRE(b2World_IsValid(created_world));
    RANGE_REQUIRE(body->node_count == 16);
    b2Counters counters = b2World_GetCounters(created_world);
    RANGE_REQUIRE(counters.bodyCount == 18); // 17 dynamic rig bodies plus ground.
    RANGE_REQUIRE(counters.jointCount == 32);
    for (int n = 0; n < 17; ++n)
    {
        b2BodyId id = n < 16 ? body->node_body_ids[n] : body->core_body_id;
        RANGE_REQUIRE(b2Body_IsValid(id));
        RANGE_REQUIRE(b2Body_GetType(id) == b2_dynamicBody);
        RANGE_REQUIRE(isfinite(b2Body_GetMass(id)) && b2Body_GetMass(id) > 0);
    }
    b2World_SetGravity(created_world, b2Vec2_zero);
    b2World_EnableSleeping(created_world, false);
    b2Vec2 core = b2Body_GetPosition(body->core_body_id);
    b2Vec2 node = b2Body_GetPosition(body->node_body_ids[0]);
    b2Vec2 radial = b2Sub(node, core);
    float length = b2Length(radial);
    RANGE_REQUIRE(isfinite(length) && length > 0);
    b2Vec2 direction = b2MulSV(1.0f / length, radial);
    b2Body_SetTransform(body->node_body_ids[0], b2MulAdd(node, .04f, direction), b2Rot_identity);
    b2JointId selected = body->radial_joint_ids[0];
    RANGE_REQUIRE(b2DistanceJoint_IsSpringEnabled(selected));
    RANGE_REQUIRE(b2DistanceJoint_IsLimitEnabled(selected));
    RANGE_REQUIRE(b2DistanceJoint_GetCurrentLength(selected) > b2DistanceJoint_GetMinLength(selected));
    RANGE_REQUIRE(b2DistanceJoint_GetCurrentLength(selected) < b2DistanceJoint_GetMaxLength(selected));

    range_load_and_step(body, direction);
    before = b2Joint_GetConstraintForce(selected);
    first_reaction = b2Length(before);
    RANGE_REQUIRE(isfinite(first_reaction) && first_reaction > .0001f);
    RANGE_REQUIRE(b2DistanceJoint_GetCurrentLength(selected) > b2DistanceJoint_GetMinLength(selected));
    RANGE_REQUIRE(b2DistanceJoint_GetCurrentLength(selected) < b2DistanceJoint_GetMaxLength(selected));
    for (int refresh = 0; refresh < 8; ++refresh)
    {
        dango_self_update_joint_parameters(body, &config);
        after = b2Joint_GetConstraintForce(selected);
        // The same assertion deliberately fails with the original bridge:
        // SetLengthRange has erased the measured spring reaction to zero.
        RANGE_REQUIRE(range_same_force(before, after));
    }

    // Change one endpoint at a time. A broken "both endpoints changed" guard
    // must not pass merely because the fixture always edits them together.
    for (int change = 0; change < 2; ++change)
    {
        if (change == 0) config.radial_minimum_ratio = .68f;
        else config.radial_maximum_ratio = 1.18f;
        RANGE_REQUIRE(dango_self_config_valid(&config));
        dango_self_update_joint_parameters(body, &config);
        for (int n = 0; n < 16; ++n)
        {
            b2JointId joint = body->radial_joint_ids[n];
            RANGE_REQUIRE(b2DistanceJoint_GetMinLength(joint) == body->radial_rest_length * config.radial_minimum_ratio);
            RANGE_REQUIRE(b2DistanceJoint_GetMaxLength(joint) == body->radial_rest_length * config.radial_maximum_ratio);
            b2Vec2 reset = b2Joint_GetConstraintForce(joint);
            RANGE_REQUIRE(reset.x == 0 && reset.y == 0);
        }
        range_load_and_step(body, direction);
        before = b2Joint_GetConstraintForce(selected);
        changed_reaction = b2Length(before);
        RANGE_REQUIRE(isfinite(changed_reaction) && changed_reaction > .0001f);
        for (int refresh = 0; refresh < 8; ++refresh)
        {
            dango_self_update_joint_parameters(body, &config);
            after = b2Joint_GetConstraintForce(selected);
            RANGE_REQUIRE(range_same_force(before, after));
        }
    }

cleanup:
    if (handle != NULL)
    {
        int destroyed = dango_box2d_self_soft_body_destroy(handle, &outstanding);
        handle = NULL;
        ++range_checks;
        if (destroyed != 0 || outstanding != 0 || b2World_IsValid(created_world))
        {
            fprintf(stderr, "FAIL range-cache cleanup destroy=%d bytes=%d valid=%d\n",
                destroyed, outstanding, b2World_IsValid(created_world) ? 1 : 0);
            failed = 1;
        }
    }
    ++range_checks;
    if (b2GetByteCount() != 0)
    {
        fprintf(stderr, "FAIL range-cache cleanup global bytes=%d\n", b2GetByteCount());
        failed = 1;
    }
    printf("range_cache checks=%u first_reaction=%.9g changed_reaction=%.9g refreshed_reaction=%.9g cleanup_bytes=%d passed=%d\n",
        range_checks, (double)first_reaction, (double)changed_reaction, (double)b2Length(after),
        b2GetByteCount(), failed ? 0 : 1);
    return failed ? 1 : 0;
}

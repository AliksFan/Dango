#include <box2d/box2d.h>
#include <math.h>
#include <stdlib.h>
#include "dango_self_constraints.h"
#if defined(DANGO_MATERIAL_EXPERIMENT_MODE)
#include "dango_material_forces.h"
#endif

#if defined(_WIN32)
#define DANGO_API __declspec(dllexport)
#else
#define DANGO_API __attribute__((visibility("default")))
#endif

#define DANGO_BOX2D_ABI_VERSION 13
#define DANGO_SOFT_BODY_MAX_NODES 16
#define DANGO_EYE_COUNT 2
#define DANGO_EYE_JOINT_COUNT 3
#define DANGO_HAT_JOINT_COUNT 2
#define DANGO_SOFT_BODY_MIN_AREA 0.45f
#define DANGO_SOFT_BODY_MAX_RADIUS 0.98f
#define DANGO_SOFT_BODY_MAX_SPEED 8.0f
#define DANGO_SOFT_BODY_MAX_CENTROID_OFFSET 0.25f

typedef struct dango_soft_body
{
    b2WorldId world_id;
    b2BodyId core_body_id;
    b2BodyId ground_body_id;
    b2BodyId node_body_ids[DANGO_SOFT_BODY_MAX_NODES];
    b2ShapeId node_shape_ids[DANGO_SOFT_BODY_MAX_NODES];
    b2JointId radial_joint_ids[DANGO_SOFT_BODY_MAX_NODES];
    b2JointId edge_joint_ids[DANGO_SOFT_BODY_MAX_NODES];
    b2BodyId eye_body_ids[DANGO_EYE_COUNT];
    b2BodyId hat_body_id;
    b2JointId eye_joint_ids[DANGO_EYE_COUNT][DANGO_EYE_JOINT_COUNT];
    b2JointId hat_joint_ids[DANGO_HAT_JOINT_COUNT];
    b2JointId drag_joint_id;
    float angles[DANGO_SOFT_BODY_MAX_NODES];
    int node_count;
    int substep_count;
    int initial_box2d_bytes;
    float rest_width;
    float rest_height;
    float top_roundness;
    float lower_bulge;
    float bottom_flatness;
    float ground_compression;
    float drop_height;
    float impact_speed;
    float rebound_strength;
    float lift_off_limit;
    float settle_time;
    float maximum_compression;
    float lateral_expansion;
    float radial_hertz;
    float edge_hertz;
    float radial_damping;
    float edge_damping;
    float maximum_radius;
    float maximum_speed;
    float maximum_centroid_offset;
    float minimum_area;
    float grounded_blend;
    float applied_grounded_blend;
    float ground_height;
    float node_radius;
    float applied_facing;
    bool drag_active_applied;
} dango_soft_body;

typedef struct dango_soft_body_config
{
    int node_count;
    int substep_count;
    float rest_width;
    float rest_height;
    float top_roundness;
    float lower_bulge;
    float bottom_flatness;
    float ground_compression;
    float drop_height;
    float impact_speed;
    float rebound_strength;
    float lift_off_limit;
    float settle_time;
    float maximum_compression;
    float lateral_expansion;
    float radial_hertz;
    float edge_hertz;
    float radial_damping;
    float edge_damping;
    float linear_damping;
    float node_mass;
    float ground_height;
    float node_radius;
    float friction;
    float restitution;
    float contact_hertz;
    float maximum_radius;
    float maximum_speed;
    float maximum_centroid_offset;
    float minimum_area;
} dango_soft_body_config;

static dango_soft_body_config dango_default_soft_body_config(int node_count, int substep_count)
{
    dango_soft_body_config value = {0};
    value.node_count = node_count; value.substep_count = substep_count;
    value.rest_width = 1.44f; value.rest_height = 1.64f; value.top_roundness = 1.0f;
    value.radial_hertz = 5.5f; value.edge_hertz = 8.0f;
    value.radial_damping = 0.9f; value.edge_damping = 0.85f; value.linear_damping = 1.6f;
    value.node_mass = 0.01f; value.ground_height = -0.90f; value.node_radius = 0.055f;
    value.friction = 0.35f; value.contact_hertz = 30.0f;
    value.maximum_radius = DANGO_SOFT_BODY_MAX_RADIUS; value.maximum_speed = DANGO_SOFT_BODY_MAX_SPEED;
    value.maximum_centroid_offset = DANGO_SOFT_BODY_MAX_CENTROID_OFFSET; value.minimum_area = DANGO_SOFT_BODY_MIN_AREA;
    return value;
}

static b2Vec2 dango_target_point(const dango_soft_body* body, int index, float blend)
{
    float angle = body->angles[index];
    float impact_wave = 4.0f * blend * (1.0f - blend);
    float air_radius = 0.72f;
    float x = 0.5f * body->rest_width * cosf(angle);
    float y = 0.5f * body->rest_height * sinf(angle);
    float lower = fmaxf(0.0f, -sinf(angle));
    x *= 1.0f + body->lower_bulge * lower + body->lateral_expansion * impact_wave * lower;
    float top = fmaxf(0.0f, sinf(angle));
    x *= 1.0f - (1.0f - body->top_roundness) * top * 0.22f;
    if (sinf(angle) < -0.35f)
    {
        float bottom_y = -0.5f * body->rest_height * (1.0f - body->ground_compression - body->maximum_compression * impact_wave);
        y = y + (bottom_y - y) * body->bottom_flatness;
    }
    b2Vec2 air = {air_radius * cosf(angle), air_radius * sinf(angle)};
    b2Vec2 rest = {x, y};
    return (b2Vec2){air.x + (rest.x - air.x) * blend, air.y + (rest.y - air.y) * blend};
}

static void dango_destroy_soft_body_internal(dango_soft_body* soft_body)
{
    if (soft_body == NULL)
    {
        return;
    }

    if (b2World_IsValid(soft_body->world_id))
    {
        b2DestroyWorld(soft_body->world_id);
    }
    free(soft_body);
}

static bool dango_is_supported_soft_body_configuration(int node_count, int substep_count)
{
    return (node_count == 12 || node_count == 16) && (substep_count == 2 || substep_count == 4);
}

static b2JointId dango_create_spring(
    b2WorldId world_id,
    b2BodyId body_a,
    b2BodyId body_b,
    float length,
    float hertz,
    float damping_ratio)
{
    b2DistanceJointDef joint_def = b2DefaultDistanceJointDef();
    joint_def.bodyIdA = body_a;
    joint_def.bodyIdB = body_b;
    joint_def.length = length;
    joint_def.enableSpring = true;
    joint_def.hertz = hertz;
    joint_def.dampingRatio = damping_ratio;
    joint_def.collideConnected = false;
    return b2CreateDistanceJoint(world_id, &joint_def);
}

static b2JointId dango_create_anchored_spring(
    b2WorldId world_id,
    b2BodyId body_a,
    b2BodyId body_b,
    b2Vec2 local_anchor_a,
    b2Vec2 local_anchor_b,
    float length,
    float hertz,
    float damping_ratio)
{
    b2DistanceJointDef joint_def = b2DefaultDistanceJointDef();
    joint_def.bodyIdA = body_a;
    joint_def.bodyIdB = body_b;
    joint_def.localAnchorA = local_anchor_a;
    joint_def.localAnchorB = local_anchor_b;
    joint_def.length = fmaxf(length, 0.005f);
    joint_def.enableSpring = true;
    joint_def.hertz = hertz;
    joint_def.dampingRatio = damping_ratio;
    joint_def.collideConnected = false;
    return b2CreateDistanceJoint(world_id, &joint_def);
}

static float dango_distance(b2Vec2 a, b2Vec2 b)
{
    float dx = b.x - a.x;
    float dy = b.y - a.y;
    return sqrtf(dx * dx + dy * dy);
}

DANGO_API int dango_box2d_get_abi_version(void)
{
    return DANGO_BOX2D_ABI_VERSION;
}

DANGO_API int dango_box2d_get_version_major(void)
{
    return b2GetVersion().major;
}

DANGO_API int dango_box2d_get_version_minor(void)
{
    return b2GetVersion().minor;
}

DANGO_API int dango_box2d_get_version_revision(void)
{
    return b2GetVersion().revision;
}

DANGO_API int dango_box2d_run_lifecycle_probe(int step_count, int* outstanding_bytes)
{
    if (outstanding_bytes == NULL)
    {
        return -1;
    }

    *outstanding_bytes = -1;
    if (step_count <= 0 || step_count > 10000)
    {
        return -2;
    }

    const int initial_bytes = b2GetByteCount();
    int result = 0;
    b2WorldDef world_def = b2DefaultWorldDef();
    world_def.gravity = (b2Vec2){0.0f, -10.0f};
    b2WorldId world_id = b2CreateWorld(&world_def);
    if (!b2World_IsValid(world_id))
    {
        return -3;
    }

    b2BodyDef body_def_a = b2DefaultBodyDef();
    body_def_a.type = b2_dynamicBody;
    body_def_a.position = (b2Vec2){0.0f, 2.0f};
    b2BodyId body_a = b2CreateBody(world_id, &body_def_a);

    b2BodyDef body_def_b = b2DefaultBodyDef();
    body_def_b.type = b2_dynamicBody;
    body_def_b.position = (b2Vec2){1.0f, 2.0f};
    b2BodyId body_b = b2CreateBody(world_id, &body_def_b);
    if (!b2Body_IsValid(body_a) || !b2Body_IsValid(body_b))
    {
        result = -4;
        goto cleanup_bodies;
    }

    b2DistanceJointDef joint_def = b2DefaultDistanceJointDef();
    joint_def.bodyIdA = body_a;
    joint_def.bodyIdB = body_b;
    joint_def.length = 1.0f;
    joint_def.enableSpring = true;
    joint_def.hertz = 4.0f;
    joint_def.dampingRatio = 0.7f;
    b2JointId joint_id = b2CreateDistanceJoint(world_id, &joint_def);
    if (!b2Joint_IsValid(joint_id))
    {
        result = -5;
        goto cleanup_bodies;
    }

    for (int index = 0; index < step_count; ++index)
    {
        b2World_Step(world_id, 1.0f / 60.0f, 4);
    }

    b2DestroyJoint(joint_id);

cleanup_bodies:
    if (b2Body_IsValid(body_b))
    {
        b2DestroyBody(body_b);
    }
    if (b2Body_IsValid(body_a))
    {
        b2DestroyBody(body_a);
    }
    b2DestroyWorld(world_id);

    *outstanding_bytes = b2GetByteCount() - initial_bytes;
    if (result == 0 && *outstanding_bytes != 0)
    {
        result = -6;
    }

    return result;
}

DANGO_API int dango_box2d_soft_body_create_configured(
    const dango_soft_body_config* config,
    void** soft_body_handle)
{
    if (soft_body_handle == NULL)
    {
        return -10;
    }
    *soft_body_handle = NULL;
    if (config == NULL || !dango_is_supported_soft_body_configuration(config->node_count, config->substep_count))
    {
        return -11;
    }

    dango_soft_body* soft_body = calloc(1, sizeof(dango_soft_body));
    if (soft_body == NULL)
    {
        return -12;
    }

    soft_body->world_id = b2_nullWorldId;
    soft_body->initial_box2d_bytes = b2GetByteCount();
    soft_body->node_count = config->node_count;
    soft_body->substep_count = config->substep_count;
    soft_body->rest_width = config->rest_width; soft_body->rest_height = config->rest_height;
    soft_body->top_roundness = config->top_roundness; soft_body->lower_bulge = config->lower_bulge;
    soft_body->bottom_flatness = config->bottom_flatness; soft_body->ground_compression = config->ground_compression;
    soft_body->drop_height = config->drop_height; soft_body->impact_speed = config->impact_speed;
    soft_body->rebound_strength = config->rebound_strength; soft_body->lift_off_limit = config->lift_off_limit;
    soft_body->settle_time = config->settle_time; soft_body->ground_height = config->ground_height; soft_body->node_radius = config->node_radius;
    soft_body->maximum_compression = config->maximum_compression; soft_body->lateral_expansion = config->lateral_expansion;
    soft_body->radial_hertz = config->radial_hertz; soft_body->edge_hertz = config->edge_hertz;
    soft_body->radial_damping = config->radial_damping; soft_body->edge_damping = config->edge_damping;
    soft_body->maximum_radius = config->maximum_radius; soft_body->maximum_speed = config->maximum_speed;
    soft_body->maximum_centroid_offset = config->maximum_centroid_offset; soft_body->minimum_area = config->minimum_area;
    soft_body->applied_grounded_blend = -1.0f;

    b2WorldDef world_def = b2DefaultWorldDef();
    world_def.gravity = (b2Vec2){0.0f, config->drop_height > 0.0f ? -9.8f : 0.0f};
    soft_body->world_id = b2CreateWorld(&world_def);
    if (!b2World_IsValid(soft_body->world_id))
    {
        dango_destroy_soft_body_internal(soft_body);
        return -13;
    }
    b2World_SetContactTuning(soft_body->world_id, config->contact_hertz, 10.0f, 3.0f);
    b2World_EnableSleeping(soft_body->world_id, true);

    b2BodyDef core_def = b2DefaultBodyDef();
    core_def.type = b2_kinematicBody;
    core_def.position = (b2Vec2){0.0f, config->drop_height};
    soft_body->core_body_id = b2CreateBody(soft_body->world_id, &core_def);
    if (!b2Body_IsValid(soft_body->core_body_id))
    {
        dango_destroy_soft_body_internal(soft_body);
        return -14;
    }

    b2BodyDef ground_def = b2DefaultBodyDef();
    ground_def.type = b2_staticBody;
    ground_def.position = (b2Vec2){0.0f, config->ground_height - 0.08f};
    soft_body->ground_body_id = b2CreateBody(soft_body->world_id, &ground_def);
    b2ShapeDef ground_shape_def = b2DefaultShapeDef();
    ground_shape_def.filter.categoryBits = 0x00000001;
    ground_shape_def.filter.maskBits = 0x00000002;
    b2Polygon ground_box = b2MakeBox(1.5f, 0.08f);
    b2ShapeId ground_shape_id = b2CreatePolygonShape(
        soft_body->ground_body_id,
        &ground_shape_def,
        &ground_box);
    if (!b2Body_IsValid(soft_body->ground_body_id) || !b2Shape_IsValid(ground_shape_id))
    {
        dango_destroy_soft_body_internal(soft_body);
        return -15;
    }

    const float two_pi = 6.28318530717958647692f;
    b2ShapeDef node_shape_def = b2DefaultShapeDef();
    node_shape_def.density = config->node_mass / (3.14159265f * config->node_radius * config->node_radius);
    node_shape_def.material.friction = config->friction;
    node_shape_def.material.restitution = fminf(0.5f, config->restitution * (1.0f + config->rebound_strength));
    node_shape_def.filter.categoryBits = 0x00000002;
    node_shape_def.filter.maskBits = 0x00000001;
    b2Circle node_circle = {{0.0f, 0.0f}, config->node_radius};

    for (int index = 0; index < soft_body->node_count; ++index)
    {
        float angle = two_pi * (float)index / (float)soft_body->node_count;
        soft_body->angles[index] = angle;
        b2Vec2 position = dango_target_point(soft_body, index, 0.0f);
        position.y += config->drop_height;
        b2BodyDef node_def = b2DefaultBodyDef();
        node_def.type = b2_dynamicBody;
        node_def.position = position;
        node_def.linearDamping = config->linear_damping;
        node_def.angularDamping = 1.0f;
        node_def.sleepThreshold = 0.025f;
        node_def.fixedRotation = true;
        soft_body->node_body_ids[index] = b2CreateBody(soft_body->world_id, &node_def);
        b2Body_SetLinearVelocity(soft_body->node_body_ids[index], (b2Vec2){0.0f, -config->impact_speed});
        soft_body->node_shape_ids[index] = b2CreateCircleShape(
            soft_body->node_body_ids[index],
            &node_shape_def,
            &node_circle);
        if (!b2Body_IsValid(soft_body->node_body_ids[index]) || !b2Shape_IsValid(soft_body->node_shape_ids[index]))
        {
            dango_destroy_soft_body_internal(soft_body);
            return -16;
        }

        float radial_length = sqrtf(position.x * position.x + position.y * position.y);
        soft_body->radial_joint_ids[index] = dango_create_spring(
            soft_body->world_id,
            soft_body->core_body_id,
            soft_body->node_body_ids[index],
            radial_length,
            config->radial_hertz,
            config->radial_damping);
        if (!b2Joint_IsValid(soft_body->radial_joint_ids[index]))
        {
            dango_destroy_soft_body_internal(soft_body);
            return -17;
        }
    }

    for (int index = 0; index < soft_body->node_count; ++index)
    {
        int next_index = (index + 1) % soft_body->node_count;
        b2Vec2 position = b2Body_GetPosition(soft_body->node_body_ids[index]);
        b2Vec2 next_position = b2Body_GetPosition(soft_body->node_body_ids[next_index]);
        float delta_x = next_position.x - position.x;
        float delta_y = next_position.y - position.y;
        float edge_length = sqrtf(delta_x * delta_x + delta_y * delta_y);
        soft_body->edge_joint_ids[index] = dango_create_spring(
            soft_body->world_id,
            soft_body->node_body_ids[index],
            soft_body->node_body_ids[next_index],
            edge_length,
            config->edge_hertz,
            config->edge_damping);
        if (!b2Joint_IsValid(soft_body->edge_joint_ids[index]))
        {
            dango_destroy_soft_body_internal(soft_body);
            return -18;
        }
    }

    b2ShapeDef accessory_shape_def = b2DefaultShapeDef();
    accessory_shape_def.density = 0.25f;
    accessory_shape_def.filter.categoryBits = 0x00000004;
    accessory_shape_def.filter.maskBits = 0;

    b2Circle eye_circle = {{0.0f, 0.0f}, 0.04f};
    const int eye_node_indices[DANGO_EYE_JOINT_COUNT] =
    {
        0,
        soft_body->node_count / 2,
        soft_body->node_count / 4
    };
    for (int eye_index = 0; eye_index < DANGO_EYE_COUNT; ++eye_index)
    {
        b2BodyDef eye_def = b2DefaultBodyDef();
        eye_def.type = b2_dynamicBody;
        eye_def.position = (b2Vec2)
        {
            eye_index == 0 ? -0.2638889f : 0.2638889f,
            config->drop_height - 0.05f
        };
        eye_def.linearDamping = 3.0f;
        eye_def.angularDamping = 4.0f;
        eye_def.gravityScale = 0.0f;
        eye_def.fixedRotation = true;
        eye_def.sleepThreshold = 0.01f;
        soft_body->eye_body_ids[eye_index] = b2CreateBody(soft_body->world_id, &eye_def);
        b2ShapeId eye_shape_id = b2CreateCircleShape(
            soft_body->eye_body_ids[eye_index],
            &accessory_shape_def,
            &eye_circle);
        if (!b2Body_IsValid(soft_body->eye_body_ids[eye_index]) || !b2Shape_IsValid(eye_shape_id))
        {
            dango_destroy_soft_body_internal(soft_body);
            return -19;
        }

        for (int joint_index = 0; joint_index < DANGO_EYE_JOINT_COUNT; ++joint_index)
        {
            b2Vec2 node_position = b2Body_GetPosition(soft_body->node_body_ids[eye_node_indices[joint_index]]);
            soft_body->eye_joint_ids[eye_index][joint_index] = dango_create_spring(
                soft_body->world_id,
                soft_body->node_body_ids[eye_node_indices[joint_index]],
                soft_body->eye_body_ids[eye_index],
                dango_distance(node_position, eye_def.position),
                5.0f,
                0.82f);
            if (!b2Joint_IsValid(soft_body->eye_joint_ids[eye_index][joint_index]))
            {
                dango_destroy_soft_body_internal(soft_body);
                return -19;
            }
        }
    }

    const int top_index = soft_body->node_count / 4;
    const int hat_node_indices[DANGO_HAT_JOINT_COUNT] =
    {
        (top_index - 1 + soft_body->node_count) % soft_body->node_count,
        (top_index + 1) % soft_body->node_count
    };
    b2Vec2 top_position = b2Body_GetPosition(soft_body->node_body_ids[top_index]);
    b2BodyDef hat_def = b2DefaultBodyDef();
    hat_def.type = b2_dynamicBody;
    hat_def.position = top_position;
    hat_def.linearDamping = 1.5f;
    hat_def.angularDamping = 1.4f;
    hat_def.gravityScale = 0.0f;
    hat_def.sleepThreshold = 0.01f;
    soft_body->hat_body_id = b2CreateBody(soft_body->world_id, &hat_def);
    b2Polygon hat_box = b2MakeBox(0.18f, 0.04f);
    b2ShapeId hat_shape_id = b2CreatePolygonShape(
        soft_body->hat_body_id,
        &accessory_shape_def,
        &hat_box);
    if (!b2Body_IsValid(soft_body->hat_body_id) || !b2Shape_IsValid(hat_shape_id))
    {
        dango_destroy_soft_body_internal(soft_body);
        return -19;
    }

    const b2Vec2 hat_local_anchors[DANGO_HAT_JOINT_COUNT] =
    {
        {-0.18f, 0.0f},
        {0.18f, 0.0f}
    };
    for (int index = 0; index < DANGO_HAT_JOINT_COUNT; ++index)
    {
        b2Vec2 node_position = b2Body_GetPosition(soft_body->node_body_ids[hat_node_indices[index]]);
        b2Vec2 hat_anchor =
        {
            hat_def.position.x + hat_local_anchors[index].x,
            hat_def.position.y
        };
        soft_body->hat_joint_ids[index] = dango_create_anchored_spring(
            soft_body->world_id,
            soft_body->node_body_ids[hat_node_indices[index]],
            soft_body->hat_body_id,
            (b2Vec2){0.0f, 0.0f},
            hat_local_anchors[index],
            dango_distance(node_position, hat_anchor),
            2.8f,
            0.55f);
        if (!b2Joint_IsValid(soft_body->hat_joint_ids[index]))
        {
            dango_destroy_soft_body_internal(soft_body);
            return -19;
        }
    }

    *soft_body_handle = soft_body;
    return 0;
}

DANGO_API int dango_box2d_soft_body_create(int node_count, int substep_count, void** soft_body_handle)
{
    dango_soft_body_config config = dango_default_soft_body_config(node_count, substep_count);
    return dango_box2d_soft_body_create_configured(&config, soft_body_handle);
}

DANGO_API int dango_box2d_soft_body_step(
    void* soft_body_handle,
    float time_step,
    int cue,
    float anchor_x,
    float anchor_y,
    float strength,
    float facing,
    int drag_active,
    float drag_grab_x,
    float drag_grab_y,
    float* points,
    int point_capacity,
    int* point_count,
    float* signed_area,
    float* centroid_x,
    float* centroid_y,
    float* maximum_radius,
    float* maximum_speed,
    float* energy_proxy,
    int* sleeping,
    float* left_eye_x,
    float* left_eye_y,
    float* right_eye_x,
    float* right_eye_y,
    float* hat_x,
    float* hat_y,
    float* hat_angle)
{
    dango_soft_body* soft_body = soft_body_handle;
    if (soft_body == NULL || points == NULL || point_count == NULL || signed_area == NULL ||
        centroid_x == NULL || centroid_y == NULL || maximum_radius == NULL ||
        maximum_speed == NULL || energy_proxy == NULL || sleeping == NULL ||
        left_eye_x == NULL || left_eye_y == NULL || right_eye_x == NULL || right_eye_y == NULL ||
        hat_x == NULL || hat_y == NULL || hat_angle == NULL)
    {
        return -20;
    }
    if (!b2World_IsValid(soft_body->world_id) || point_capacity < soft_body->node_count ||
        !isfinite(time_step) || time_step <= 0.0f || time_step > 0.1f ||
        !isfinite(anchor_x) || !isfinite(anchor_y) || !isfinite(strength) || !isfinite(facing) ||
        !isfinite(drag_grab_x) || !isfinite(drag_grab_y) ||
        strength < 0.0f || strength > 1.0f || facing < -1.0f || facing > 1.0f ||
        drag_active < 0 || drag_active > 1 ||
        drag_grab_x < -1.0f || drag_grab_x > 1.0f ||
        drag_grab_y < -1.0f || drag_grab_y > 1.0f || cue < 0 || cue > 3)
    {
        return -21;
    }

    if (anchor_x < -0.35f)
    {
        anchor_x = -0.35f;
    }
    else if (anchor_x > 0.35f)
    {
        anchor_x = 0.35f;
    }
    float maximum_anchor_y = soft_body->drop_height > 0.0f ? soft_body->drop_height : 0.2f;
    if (anchor_y < -0.2f)
    {
        anchor_y = -0.2f;
    }
    else if (anchor_y > maximum_anchor_y)
    {
        anchor_y = maximum_anchor_y;
    }

    b2Body_SetTransform(soft_body->core_body_id, (b2Vec2){anchor_x, anchor_y}, b2Rot_identity);

    if (drag_active != 0)
    {
        b2Vec2 drag_target = {anchor_x + drag_grab_x, anchor_y + drag_grab_y};
        if (!b2Joint_IsValid(soft_body->drag_joint_id))
        {
            int closest_index = 0;
            float closest_distance_squared = 1000.0f;
            for (int index = 0; index < soft_body->node_count; ++index)
            {
                b2Vec2 position = b2Body_GetPosition(soft_body->node_body_ids[index]);
                float dx = position.x - drag_target.x;
                float dy = position.y - drag_target.y;
                float distance_squared = dx * dx + dy * dy;
                if (distance_squared < closest_distance_squared)
                {
                    closest_distance_squared = distance_squared;
                    closest_index = index;
                }
            }

            b2MouseJointDef drag_joint_def = b2DefaultMouseJointDef();
            drag_joint_def.bodyIdA = soft_body->ground_body_id;
            drag_joint_def.bodyIdB = soft_body->node_body_ids[closest_index];
            drag_joint_def.target = drag_target;
            drag_joint_def.hertz = 4.5f;
            drag_joint_def.dampingRatio = 0.9f;
            drag_joint_def.maxForce = 2.0f;
            soft_body->drag_joint_id = b2CreateMouseJoint(soft_body->world_id, &drag_joint_def);
            if (!b2Joint_IsValid(soft_body->drag_joint_id))
            {
                return -29;
            }
        }
        else
        {
            b2MouseJoint_SetTarget(soft_body->drag_joint_id, drag_target);
        }

        if (!soft_body->drag_active_applied)
        {
            for (int index = 0; index < soft_body->node_count; ++index)
            {
                b2DistanceJoint_SetSpringHertz(
                    soft_body->radial_joint_ids[index],
                    fmaxf(2.8f, soft_body->radial_hertz * 0.64f));
            }
            soft_body->drag_active_applied = true;
        }

        for (int index = 0; index < soft_body->node_count; ++index)
        {
            b2Vec2 position = b2Body_GetPosition(soft_body->node_body_ids[index]);
            float local_y = position.y - anchor_y;
            float below_grab = fminf(1.0f, fmaxf(0.0f, (drag_grab_y - local_y) / 1.6f));
            float downward_force = -0.42f * (0.2f + 0.8f * below_grab);
            b2Body_ApplyForceToCenter(
                soft_body->node_body_ids[index],
                (b2Vec2){0.0f, downward_force},
                true);
        }
    }
    else if (soft_body->drag_active_applied)
    {
        if (b2Joint_IsValid(soft_body->drag_joint_id))
        {
            b2DestroyJoint(soft_body->drag_joint_id);
            soft_body->drag_joint_id = b2_nullJointId;
        }
        for (int index = 0; index < soft_body->node_count; ++index)
        {
            b2DistanceJoint_SetSpringHertz(
                soft_body->radial_joint_ids[index],
                soft_body->radial_hertz);
        }
        soft_body->drag_active_applied = false;
    }

    if (fabsf(facing - soft_body->applied_facing) > 0.0001f)
    {
        b2Vec2 accessory_centroid = {0.0f, 0.0f};
        for (int index = 0; index < soft_body->node_count; ++index)
        {
            b2Vec2 position = b2Body_GetPosition(soft_body->node_body_ids[index]);
            accessory_centroid.x += position.x;
            accessory_centroid.y += position.y;
        }
        accessory_centroid.x /= (float)soft_body->node_count;
        accessory_centroid.y /= (float)soft_body->node_count;

        const int eye_node_indices[DANGO_EYE_JOINT_COUNT] =
        {
            0,
            soft_body->node_count / 2,
            soft_body->node_count / 4
        };
        float eye_half_distance = fabsf(facing) > 0.5f ? 0.2361111f : 0.2638889f;
        for (int eye_index = 0; eye_index < DANGO_EYE_COUNT; ++eye_index)
        {
            b2Vec2 eye_target =
            {
                accessory_centroid.x + 0.075f * facing + (eye_index == 0 ? -eye_half_distance : eye_half_distance),
                accessory_centroid.y - 0.05f
            };
            for (int joint_index = 0; joint_index < DANGO_EYE_JOINT_COUNT; ++joint_index)
            {
                b2Vec2 node_position = b2Body_GetPosition(soft_body->node_body_ids[eye_node_indices[joint_index]]);
                b2DistanceJoint_SetLength(
                    soft_body->eye_joint_ids[eye_index][joint_index],
                    fmaxf(dango_distance(node_position, eye_target), 0.005f));
            }
            b2Body_SetAwake(soft_body->eye_body_ids[eye_index], true);
        }

        const int top_index = soft_body->node_count / 4;
        const int hat_node_indices[DANGO_HAT_JOINT_COUNT] =
        {
            (top_index - 1 + soft_body->node_count) % soft_body->node_count,
            (top_index + 1) % soft_body->node_count
        };
        const b2Vec2 hat_local_anchors[DANGO_HAT_JOINT_COUNT] =
        {
            {-0.18f, 0.0f},
            {0.18f, 0.0f}
        };
        b2Vec2 top_position = b2Body_GetPosition(soft_body->node_body_ids[top_index]);
        b2Vec2 hat_target = {top_position.x + 0.05f * facing, top_position.y};
        for (int index = 0; index < DANGO_HAT_JOINT_COUNT; ++index)
        {
            b2Vec2 node_position = b2Body_GetPosition(soft_body->node_body_ids[hat_node_indices[index]]);
            b2Vec2 target_anchor =
            {
                hat_target.x + hat_local_anchors[index].x,
                hat_target.y
            };
            b2DistanceJoint_SetLength(
                soft_body->hat_joint_ids[index],
                fmaxf(dango_distance(node_position, target_anchor), 0.005f));
        }
        b2Body_SetAwake(soft_body->hat_body_id, true);
        soft_body->applied_facing = facing;
    }
    if (soft_body->drop_height > 0.0f)
    {
        float lowest = 1000.0f;
        for (int index = 0; index < soft_body->node_count; ++index)
        {
            lowest = fminf(lowest, b2Body_GetPosition(soft_body->node_body_ids[index]).y);
        }
        float lowest_surface = lowest - soft_body->node_radius;
        float contact_limit = soft_body->grounded_blend > 0.5f
            ? soft_body->ground_height + soft_body->lift_off_limit
            : soft_body->ground_height + 0.01f;
        float target_blend = lowest_surface <= contact_limit ? 1.0f : 0.0f;
        soft_body->grounded_blend += (target_blend - soft_body->grounded_blend) * fminf(1.0f, time_step * (6.0f / fmaxf(0.2f, soft_body->settle_time)));
        if (fabsf(soft_body->grounded_blend - soft_body->applied_grounded_blend) > 0.0001f)
        {
            for (int index = 0; index < soft_body->node_count; ++index)
            {
                int next_index = (index + 1) % soft_body->node_count;
                b2Vec2 target = dango_target_point(soft_body, index, soft_body->grounded_blend);
                b2Vec2 next_target = dango_target_point(soft_body, next_index, soft_body->grounded_blend);
                b2DistanceJoint_SetLength(soft_body->radial_joint_ids[index], sqrtf(target.x * target.x + target.y * target.y));
                b2DistanceJoint_SetLength(soft_body->edge_joint_ids[index], sqrtf((next_target.x-target.x)*(next_target.x-target.x)+(next_target.y-target.y)*(next_target.y-target.y)));
            }
            soft_body->applied_grounded_blend = soft_body->grounded_blend;
        }
    }
    if (cue == 1)
    {
        for (int index = 0; index < soft_body->node_count; ++index)
        {
            b2Body_ApplyLinearImpulseToCenter(
                soft_body->node_body_ids[index],
                (b2Vec2){0.0f, -0.008f * strength},
                true);
        }
    }
    else if (cue == 2)
    {
        for (int index = 0; index < soft_body->node_count; ++index)
        {
            b2Body_SetAwake(soft_body->node_body_ids[index], true);
        }
    }
    else if (cue == 3 && drag_active == 0)
    {
        float recovery_blend = fminf(0.16f, time_step * 7.5f);
        for (int index = 0; index < soft_body->node_count; ++index)
        {
            b2Vec2 current = b2Body_GetPosition(soft_body->node_body_ids[index]);
            b2Vec2 target = dango_target_point(soft_body, index, soft_body->grounded_blend);
            target.x += anchor_x;
            target.y += anchor_y;
            b2Vec2 recovered =
            {
                current.x + ((target.x - current.x) * recovery_blend),
                current.y + ((target.y - current.y) * recovery_blend)
            };
            b2Vec2 velocity = b2Body_GetLinearVelocity(soft_body->node_body_ids[index]);
            b2Body_SetTransform(soft_body->node_body_ids[index], recovered, b2Rot_identity);
            b2Body_SetLinearVelocity(
                soft_body->node_body_ids[index],
                (b2Vec2){velocity.x * 0.35f, velocity.y * 0.35f});
            b2Body_SetAwake(soft_body->node_body_ids[index], true);
        }
        for (int eye_index = 0; eye_index < DANGO_EYE_COUNT; ++eye_index)
        {
            b2Vec2 velocity = b2Body_GetLinearVelocity(soft_body->eye_body_ids[eye_index]);
            b2Body_SetLinearVelocity(
                soft_body->eye_body_ids[eye_index],
                (b2Vec2){velocity.x * 0.5f, velocity.y * 0.5f});
            b2Body_SetAwake(soft_body->eye_body_ids[eye_index], true);
        }
        b2Vec2 hat_velocity = b2Body_GetLinearVelocity(soft_body->hat_body_id);
        b2Body_SetLinearVelocity(
            soft_body->hat_body_id,
            (b2Vec2){hat_velocity.x * 0.5f, hat_velocity.y * 0.5f});
        b2Body_SetAngularVelocity(
            soft_body->hat_body_id,
            b2Body_GetAngularVelocity(soft_body->hat_body_id) * 0.5f);
        b2Body_SetAwake(soft_body->hat_body_id, true);
    }

    b2World_Step(soft_body->world_id, time_step, soft_body->substep_count);

    if (soft_body->drop_height > 0.0f && soft_body->grounded_blend > 0.5f)
    {
        float post_step_lowest = 1000.0f;
        for (int index = 0; index < soft_body->node_count; ++index)
        {
            post_step_lowest = fminf(post_step_lowest, b2Body_GetPosition(soft_body->node_body_ids[index]).y - soft_body->node_radius);
        }
        float excess_lift = post_step_lowest - (soft_body->ground_height + soft_body->lift_off_limit);
        if (excess_lift > 0.003f)
        {
            for (int index = 0; index < soft_body->node_count; ++index)
            {
                b2Vec2 position = b2Body_GetPosition(soft_body->node_body_ids[index]);
                b2Body_SetTransform(soft_body->node_body_ids[index], (b2Vec2){position.x, position.y - excess_lift}, b2Rot_identity);
            }
        }
    }

    b2Vec2 core_position = b2Body_GetPosition(soft_body->core_body_id);
    float area_twice = 0.0f;
    float center_x_sum = 0.0f;
    float center_y_sum = 0.0f;
    float max_radius_value = 0.0f;
    float max_speed_value = 0.0f;
    float energy_value = 0.0f;
    bool all_sleeping = true;
    for (int index = 0; index < soft_body->node_count; ++index)
    {
        int next_index = (index + 1) % soft_body->node_count;
        b2Vec2 position = b2Body_GetPosition(soft_body->node_body_ids[index]);
        b2Vec2 next_position = b2Body_GetPosition(soft_body->node_body_ids[next_index]);
        b2Vec2 velocity = b2Body_GetLinearVelocity(soft_body->node_body_ids[index]);
        float local_x = position.x - core_position.x;
        float local_y = position.y - core_position.y;
        float next_local_x = next_position.x - core_position.x;
        float next_local_y = next_position.y - core_position.y;
        float radius = sqrtf(local_x * local_x + local_y * local_y);
        float speed = sqrtf(velocity.x * velocity.x + velocity.y * velocity.y);
        if (!isfinite(local_x) || !isfinite(local_y) || !isfinite(speed))
        {
            return -22;
        }

        points[index * 2] = local_x;
        points[index * 2 + 1] = local_y;
        center_x_sum += local_x;
        center_y_sum += local_y;
        area_twice += local_x * next_local_y - next_local_x * local_y;
        max_radius_value = fmaxf(max_radius_value, radius);
        max_speed_value = fmaxf(max_speed_value, speed);
        energy_value += speed * speed;
        all_sleeping = all_sleeping && !b2Body_IsAwake(soft_body->node_body_ids[index]);
    }

    float area = 0.5f * area_twice;
    if (!isfinite(area) || area < soft_body->minimum_area)
    {
        return -23;
    }
    if (max_radius_value > soft_body->maximum_radius)
    {
        return -24;
    }
    if (max_speed_value > soft_body->maximum_speed)
    {
        return -25;
    }

    float center_x = center_x_sum / (float)soft_body->node_count;
    float center_y = center_y_sum / (float)soft_body->node_count;
    float centroid_offset = sqrtf(center_x * center_x + center_y * center_y);
    if (!isfinite(centroid_offset) || centroid_offset > soft_body->maximum_centroid_offset)
    {
        return -26;
    }
    if (!isfinite(energy_value) ||
        energy_value > (float)soft_body->node_count * DANGO_SOFT_BODY_MAX_SPEED * DANGO_SOFT_BODY_MAX_SPEED)
    {
        return -27;
    }

    *point_count = soft_body->node_count;
    *signed_area = area;
    *centroid_x = center_x;
    *centroid_y = center_y;
    *maximum_radius = max_radius_value;
    *maximum_speed = max_speed_value;
    *energy_proxy = energy_value;
    *sleeping = all_sleeping ? 1 : 0;
    b2Vec2 left_eye_position = b2Body_GetPosition(soft_body->eye_body_ids[0]);
    b2Vec2 right_eye_position = b2Body_GetPosition(soft_body->eye_body_ids[1]);
    b2Vec2 hat_position = b2Body_GetPosition(soft_body->hat_body_id);
    *left_eye_x = left_eye_position.x - core_position.x;
    *left_eye_y = left_eye_position.y - core_position.y;
    *right_eye_x = right_eye_position.x - core_position.x;
    *right_eye_y = right_eye_position.y - core_position.y;
    *hat_x = hat_position.x - core_position.x;
    *hat_y = hat_position.y - core_position.y;
    *hat_angle = b2Rot_GetAngle(b2Body_GetRotation(soft_body->hat_body_id));
    if (!isfinite(*left_eye_x) || !isfinite(*left_eye_y) ||
        !isfinite(*right_eye_x) || !isfinite(*right_eye_y) ||
        !isfinite(*hat_x) || !isfinite(*hat_y) || !isfinite(*hat_angle) ||
        fabsf(*left_eye_x) > 2.0f || fabsf(*left_eye_y) > 2.0f ||
        fabsf(*right_eye_x) > 2.0f || fabsf(*right_eye_y) > 2.0f ||
        fabsf(*hat_x) > 2.0f || fabsf(*hat_y) > 2.0f)
    {
        return -28;
    }
    return 0;
}

DANGO_API int dango_box2d_soft_body_destroy(void* soft_body_handle, int* outstanding_bytes)
{
    dango_soft_body* soft_body = soft_body_handle;
    if (soft_body == NULL || outstanding_bytes == NULL)
    {
        return -30;
    }

    int initial_bytes = soft_body->initial_box2d_bytes;
    dango_destroy_soft_body_internal(soft_body);
    *outstanding_bytes = b2GetByteCount() - initial_bytes;
    return *outstanding_bytes == 0 ? 0 : -31;
}

typedef struct dango_paired_soft_bodies
{
    b2WorldId world_id;
    b2BodyId ground_body_id;
    b2ShapeId ground_shape_id;
    b2BodyId core_body_ids[2];
    b2ShapeId core_proxy_shape_ids[2];
    b2BodyId node_body_ids[2][DANGO_SOFT_BODY_MAX_NODES];
    b2ShapeId node_shape_ids[2][DANGO_SOFT_BODY_MAX_NODES];
    b2JointId radial_joint_ids[2][DANGO_SOFT_BODY_MAX_NODES];
    b2JointId edge_joint_ids[2][DANGO_SOFT_BODY_MAX_NODES];
    int node_count;
    int substep_count;
    int initial_box2d_bytes;
    bool contact_active_applied;
    bool anchors_initialized;
    dango_soft_body_config config;
} dango_paired_soft_bodies;

static void dango_destroy_paired_internal(dango_paired_soft_bodies* pair)
{
    if (pair == NULL)
    {
        return;
    }
    if (b2World_IsValid(pair->world_id))
    {
        b2DestroyWorld(pair->world_id);
    }
    free(pair);
}

static b2Vec2 dango_paired_target_point(const dango_paired_soft_bodies* pair, int index)
{
    float angle = 6.28318530717958647692f * (float)index / (float)pair->node_count;
    float sine = sinf(angle);
    float x = 0.5f * pair->config.rest_width * cosf(angle);
    float y = 0.5f * pair->config.rest_height * sine;
    float lower = fmaxf(0.0f, -sine);
    float top = fmaxf(0.0f, sine);
    x *= 1.0f + pair->config.lower_bulge * lower;
    x *= 1.0f - (1.0f - pair->config.top_roundness) * top * 0.22f;
    if (sine < -0.35f)
    {
        float bottom_y = -0.5f * pair->config.rest_height * (1.0f - pair->config.ground_compression);
        y += (bottom_y - y) * pair->config.bottom_flatness;
    }
    return (b2Vec2){x, y};
}

static int dango_create_paired_ring(dango_paired_soft_bodies* pair, int participant, float center_x)
{
    b2BodyDef core_def = b2DefaultBodyDef();
    core_def.type = b2_kinematicBody;
    core_def.position = (b2Vec2){center_x, 0.0f};
    pair->core_body_ids[participant] = b2CreateBody(pair->world_id, &core_def);
    if (!b2Body_IsValid(pair->core_body_ids[participant]))
    {
        return -42;
    }

    b2ShapeDef proxy_shape_def = b2DefaultShapeDef();
    proxy_shape_def.filter.categoryBits = participant == 0 ? 0x00000040 : 0x00000080;
    proxy_shape_def.filter.maskBits = 0;
    float proxy_radius = fmaxf(0.2f, 0.5f * pair->config.rest_width - pair->config.node_radius);
    b2Circle proxy_circle = {{0.0f, 0.0f}, proxy_radius};
    pair->core_proxy_shape_ids[participant] = b2CreateCircleShape(
        pair->core_body_ids[participant], &proxy_shape_def, &proxy_circle);
    if (!b2Shape_IsValid(pair->core_proxy_shape_ids[participant]))
    {
        return -42;
    }

    const uint64_t category = participant == 0 ? 0x00000010 : 0x00000020;
    const float collision_radius = pair->config.node_radius * 0.10f;
    b2ShapeDef node_shape_def = b2DefaultShapeDef();
    node_shape_def.density = pair->config.node_mass /
        (3.14159265f * collision_radius * collision_radius);
    node_shape_def.material.friction = pair->config.friction;
    node_shape_def.material.restitution = pair->config.restitution;
    node_shape_def.filter.categoryBits = category;
    node_shape_def.filter.maskBits = 0x00000001;
    b2Circle node_circle = {{0.0f, 0.0f}, collision_radius};

    for (int index = 0; index < pair->node_count; ++index)
    {
        b2Vec2 local = dango_paired_target_point(pair, index);
        b2BodyDef node_def = b2DefaultBodyDef();
        node_def.type = b2_dynamicBody;
        node_def.position = (b2Vec2){center_x + local.x, local.y};
        node_def.linearDamping = pair->config.linear_damping;
        node_def.angularDamping = 2.0f;
        node_def.gravityScale = 0.0f;
        node_def.fixedRotation = true;
        node_def.sleepThreshold = 0.02f;
        pair->node_body_ids[participant][index] = b2CreateBody(pair->world_id, &node_def);
        pair->node_shape_ids[participant][index] = b2CreateCircleShape(
            pair->node_body_ids[participant][index], &node_shape_def, &node_circle);
        if (!b2Body_IsValid(pair->node_body_ids[participant][index]) ||
            !b2Shape_IsValid(pair->node_shape_ids[participant][index]))
        {
            return -43;
        }
        pair->radial_joint_ids[participant][index] = dango_create_spring(
            pair->world_id,
            pair->core_body_ids[participant],
            pair->node_body_ids[participant][index],
            sqrtf(local.x * local.x + local.y * local.y),
            pair->config.radial_hertz,
            pair->config.radial_damping);
        if (!b2Joint_IsValid(pair->radial_joint_ids[participant][index]))
        {
            return -44;
        }
    }

    for (int index = 0; index < pair->node_count; ++index)
    {
        int next = (index + 1) % pair->node_count;
        b2Vec2 position = b2Body_GetPosition(pair->node_body_ids[participant][index]);
        b2Vec2 next_position = b2Body_GetPosition(pair->node_body_ids[participant][next]);
        pair->edge_joint_ids[participant][index] = dango_create_spring(
            pair->world_id,
            pair->node_body_ids[participant][index],
            pair->node_body_ids[participant][next],
            dango_distance(position, next_position),
            pair->config.edge_hertz,
            pair->config.edge_damping);
        if (!b2Joint_IsValid(pair->edge_joint_ids[participant][index]))
        {
            return -45;
        }
    }
    return 0;
}

static void dango_apply_paired_contact_compliance(
    dango_paired_soft_bodies* pair,
    int participant,
    float normal_x,
    float normal_y,
    float strength,
    int contact_active)
{
    float direction_x = participant == 0 ? normal_x : -normal_x;
    float direction_y = participant == 0 ? normal_y : -normal_y;
    for (int index = 0; index < pair->node_count; ++index)
    {
        b2Vec2 target = dango_paired_target_point(pair, index);
        float target_length = sqrtf(target.x * target.x + target.y * target.y);
        float alignment = target_length > 0.0001f
            ? (target.x * direction_x + target.y * direction_y) / target_length
            : 0.0f;
        float near_weight = fmaxf(0.0f, alignment);
        float far_weight = fmaxf(0.0f, -alignment);
        float radial_factor = contact_active != 0
            ? 1.0f - 0.55f * strength * near_weight + 1.50f * strength * far_weight
            : 1.0f;
        b2DistanceJoint_SetSpringHertz(
            pair->radial_joint_ids[participant][index],
            pair->config.radial_hertz * radial_factor);
        if (contact_active != 0 && far_weight > 0.0f)
        {
            b2Vec2 core = b2Body_GetPosition(pair->core_body_ids[participant]);
            b2Vec2 position = b2Body_GetPosition(pair->node_body_ids[participant][index]);
            float angular_frequency = 6.28318530717958647692f * pair->config.radial_hertz;
            float lateral_stiffness = pair->config.node_mass * angular_frequency * angular_frequency;
            float lateral_force = (core.x + target.x - position.x) *
                lateral_stiffness * strength * far_weight;
            lateral_force = fmaxf(-2.5f, fminf(2.5f, lateral_force));
            b2Body_ApplyForceToCenter(
                pair->node_body_ids[participant][index],
                (b2Vec2){lateral_force, 0.0f},
                true);
        }

        int next = (index + 1) % pair->node_count;
        b2Vec2 next_target = dango_paired_target_point(pair, next);
        float edge_x = target.x + next_target.x;
        float edge_y = target.y + next_target.y;
        float edge_length = sqrtf(edge_x * edge_x + edge_y * edge_y);
        float edge_alignment = edge_length > 0.0001f
            ? (edge_x * direction_x + edge_y * direction_y) / edge_length
            : 0.0f;
        float edge_near_weight = fmaxf(0.0f, edge_alignment);
        float edge_far_weight = fmaxf(0.0f, -edge_alignment);
        float edge_factor = contact_active != 0
            ? 1.0f - 0.65f * strength * edge_near_weight + 1.00f * strength * edge_far_weight
            : 1.0f;
        b2DistanceJoint_SetSpringHertz(
            pair->edge_joint_ids[participant][index],
            pair->config.edge_hertz * edge_factor);
    }
}

DANGO_API int dango_box2d_paired_soft_body_create_configured(
    const dango_soft_body_config* config,
    void** paired_handle)
{
    if (paired_handle == NULL)
    {
        return -40;
    }
    *paired_handle = NULL;
    if (config == NULL ||
        !dango_is_supported_soft_body_configuration(config->node_count, config->substep_count))
    {
        return -41;
    }

    dango_paired_soft_bodies* pair = calloc(1, sizeof(dango_paired_soft_bodies));
    if (pair == NULL)
    {
        return -42;
    }
    pair->world_id = b2_nullWorldId;
    pair->initial_box2d_bytes = b2GetByteCount();
    pair->node_count = config->node_count;
    pair->substep_count = config->substep_count;
    pair->config = *config;

    b2WorldDef world_def = b2DefaultWorldDef();
    world_def.gravity = (b2Vec2){0.0f, -9.8f};
    pair->world_id = b2CreateWorld(&world_def);
    if (!b2World_IsValid(pair->world_id))
    {
        dango_destroy_paired_internal(pair);
        return -42;
    }
    b2World_SetContactTuning(pair->world_id, config->contact_hertz, 10.0f, 3.0f);
    b2World_EnableSleeping(pair->world_id, true);

    b2BodyDef ground_def = b2DefaultBodyDef();
    ground_def.type = b2_staticBody;
    ground_def.position = (b2Vec2){0.0f, config->ground_height - 0.08f};
    pair->ground_body_id = b2CreateBody(pair->world_id, &ground_def);
    b2ShapeDef ground_shape_def = b2DefaultShapeDef();
    ground_shape_def.material.friction = config->friction;
    ground_shape_def.filter.categoryBits = 0x00000001;
    ground_shape_def.filter.maskBits = 0x00000030;
    b2Polygon ground_box = b2MakeBox(5.0f, 0.08f);
    pair->ground_shape_id = b2CreatePolygonShape(
        pair->ground_body_id, &ground_shape_def, &ground_box);
    if (!b2Body_IsValid(pair->ground_body_id) || !b2Shape_IsValid(pair->ground_shape_id))
    {
        dango_destroy_paired_internal(pair);
        return -42;
    }

    int result = dango_create_paired_ring(pair, 0, 0.0f);
    if (result == 0)
    {
        result = dango_create_paired_ring(pair, 1, 2.5f);
    }
    if (result != 0)
    {
        dango_destroy_paired_internal(pair);
        return result;
    }

    *paired_handle = pair;
    return 0;
}

DANGO_API int dango_box2d_paired_soft_body_create(int node_count, int substep_count, void** paired_handle)
{
    dango_soft_body_config config = dango_default_soft_body_config(node_count, substep_count);
    return dango_box2d_paired_soft_body_create_configured(&config, paired_handle);
}

static int dango_write_paired_ring(
    dango_paired_soft_bodies* pair,
    int participant,
    float* points,
    float* signed_area,
    float* centroid_x,
    float* centroid_y,
    float* maximum_speed)
{
    b2Vec2 core = b2Body_GetPosition(pair->core_body_ids[participant]);
    float area_twice = 0.0f;
    float center_x = 0.0f;
    float center_y = 0.0f;
    for (int index = 0; index < pair->node_count; ++index)
    {
        int next = (index + 1) % pair->node_count;
        b2Vec2 position = b2Body_GetPosition(pair->node_body_ids[participant][index]);
        b2Vec2 next_position = b2Body_GetPosition(pair->node_body_ids[participant][next]);
        b2Vec2 velocity = b2Body_GetLinearVelocity(pair->node_body_ids[participant][index]);
        float local_x = position.x - core.x;
        float local_y = position.y - core.y;
        float next_x = next_position.x - core.x;
        float next_y = next_position.y - core.y;
        float local_length = sqrtf(local_x * local_x + local_y * local_y);
        float next_length = sqrtf(next_x * next_x + next_y * next_y);
        if (local_length > 0.0001f)
        {
            local_x += pair->config.node_radius * local_x / local_length;
            local_y += pair->config.node_radius * local_y / local_length;
        }
        if (next_length > 0.0001f)
        {
            next_x += pair->config.node_radius * next_x / next_length;
            next_y += pair->config.node_radius * next_y / next_length;
        }
        float speed = sqrtf(velocity.x * velocity.x + velocity.y * velocity.y);
        if (!isfinite(local_x) || !isfinite(local_y) || !isfinite(speed) ||
            fabsf(local_x) > 1.0f || fabsf(local_y) > 1.0f || speed > 10.0f)
        {
            return -48;
        }
        points[index * 2] = local_x;
        points[index * 2 + 1] = local_y;
        center_x += local_x;
        center_y += local_y;
        area_twice += local_x * next_y - next_x * local_y;
        *maximum_speed = fmaxf(*maximum_speed, speed);
    }
    *signed_area = 0.5f * area_twice;
    *centroid_x = center_x / (float)pair->node_count;
    *centroid_y = center_y / (float)pair->node_count;
    return !isfinite(*signed_area) || *signed_area < 0.35f ? -49 : 0;
}

DANGO_API int dango_box2d_paired_soft_body_step(
    void* paired_handle,
    float time_step,
    float first_anchor_x,
    float first_anchor_y,
    float second_anchor_x,
    float second_anchor_y,
    float normal_x,
    float normal_y,
    float strength,
    int contact_active,
    float* first_points,
    float* second_points,
    int point_capacity,
    int* point_count,
    int* contact_count,
    float* first_signed_area,
    float* first_centroid_x,
    float* first_centroid_y,
    float* second_signed_area,
    float* second_centroid_x,
    float* second_centroid_y,
    float* maximum_speed)
{
    dango_paired_soft_bodies* pair = paired_handle;
    if (pair == NULL || first_points == NULL || second_points == NULL || point_count == NULL ||
        contact_count == NULL || first_signed_area == NULL || first_centroid_x == NULL ||
        first_centroid_y == NULL || second_signed_area == NULL || second_centroid_x == NULL ||
        second_centroid_y == NULL || maximum_speed == NULL)
    {
        return -46;
    }
    float normal_length = sqrtf(normal_x * normal_x + normal_y * normal_y);
    if (!b2World_IsValid(pair->world_id) || point_capacity < pair->node_count ||
        !isfinite(time_step) || time_step <= 0.0f || time_step > 0.1f ||
        !isfinite(first_anchor_x) || !isfinite(first_anchor_y) ||
        !isfinite(second_anchor_x) || !isfinite(second_anchor_y) ||
        !isfinite(normal_x) || !isfinite(normal_y) || !isfinite(strength) ||
        strength < 0.0f || strength > 1.0f || contact_active < 0 || contact_active > 1 ||
        (contact_active != 0 && normal_length < 0.9f))
    {
        return -47;
    }

    const b2Vec2 requested_anchors[2] =
    {
        {first_anchor_x, first_anchor_y},
        {second_anchor_x, second_anchor_y}
    };
    if (!pair->anchors_initialized)
    {
        for (int participant = 0; participant < 2; ++participant)
        {
            b2Vec2 current_anchor = b2Body_GetPosition(pair->core_body_ids[participant]);
            b2Vec2 delta =
            {
                requested_anchors[participant].x - current_anchor.x,
                requested_anchors[participant].y - current_anchor.y
            };
            b2Body_SetTransform(pair->core_body_ids[participant], requested_anchors[participant], b2Rot_identity);
            for (int index = 0; index < pair->node_count; ++index)
            {
                b2Vec2 position = b2Body_GetPosition(pair->node_body_ids[participant][index]);
                b2Body_SetTransform(
                    pair->node_body_ids[participant][index],
                    (b2Vec2){position.x + delta.x, position.y + delta.y},
                    b2Rot_identity);
                b2Body_SetLinearVelocity(pair->node_body_ids[participant][index], (b2Vec2){0.0f, 0.0f});
            }
        }
        pair->anchors_initialized = true;
    }
    else
    {
        b2Body_SetTransform(pair->core_body_ids[0], requested_anchors[0], b2Rot_identity);
        b2Body_SetTransform(pair->core_body_ids[1], requested_anchors[1], b2Rot_identity);
    }

    if (contact_active != 0)
    {
        normal_x /= normal_length;
        normal_y /= normal_length;
    }

    for (int participant = 0; participant < 2; ++participant)
    {
        dango_apply_paired_contact_compliance(
            pair,
            participant,
            normal_x,
            normal_y,
            strength,
            contact_active);
        for (int index = 0; index < pair->node_count; ++index)
        {
            if ((contact_active != 0) != pair->contact_active_applied)
            {
                b2Filter filter = b2Shape_GetFilter(pair->node_shape_ids[participant][index]);
                filter.maskBits = contact_active != 0
                    ? (participant == 0 ? 0x000000A1 : 0x00000051)
                    : 0x00000001;
                b2Shape_SetFilter(pair->node_shape_ids[participant][index], filter);
                b2Body_SetAwake(pair->node_body_ids[participant][index], true);
            }
        }
        if ((contact_active != 0) != pair->contact_active_applied)
        {
            b2Filter proxy_filter = b2Shape_GetFilter(pair->core_proxy_shape_ids[participant]);
            proxy_filter.maskBits = contact_active != 0
                ? (participant == 0 ? 0x00000020 : 0x00000010)
                : 0;
            b2Shape_SetFilter(pair->core_proxy_shape_ids[participant], proxy_filter);
        }
    }
    pair->contact_active_applied = contact_active != 0;

    b2World_Step(pair->world_id, time_step, pair->substep_count);
    *point_count = pair->node_count;
    *contact_count = 0;
    *maximum_speed = 0.0f;
    for (int index = 0; index < pair->node_count; ++index)
    {
        b2ContactData contacts[8];
        int capacity = b2Body_GetContactCapacity(pair->node_body_ids[0][index]);
        capacity = capacity < 8 ? capacity : 8;
        if (capacity > 0)
        {
            int count = b2Body_GetContactData(pair->node_body_ids[0][index], contacts, capacity);
            for (int contact_index = 0; contact_index < count; ++contact_index)
            {
                uint64_t category_a = b2Shape_GetFilter(contacts[contact_index].shapeIdA).categoryBits;
                uint64_t category_b = b2Shape_GetFilter(contacts[contact_index].shapeIdB).categoryBits;
                if (((category_a | category_b) & 0x000000A0) != 0)
                {
                    *contact_count += 1;
                }
            }
        }
    }
    int result = dango_write_paired_ring(pair, 0, first_points, first_signed_area, first_centroid_x, first_centroid_y, maximum_speed);
    if (result == 0)
    {
        result = dango_write_paired_ring(pair, 1, second_points, second_signed_area, second_centroid_x, second_centroid_y, maximum_speed);
    }
    return result;
}

DANGO_API int dango_box2d_paired_soft_body_destroy(void* paired_handle, int* outstanding_bytes)
{
    dango_paired_soft_bodies* pair = paired_handle;
    if (pair == NULL || outstanding_bytes == NULL)
    {
        return -50;
    }
    int initial_bytes = pair->initial_box2d_bytes;
    dango_destroy_paired_internal(pair);
    *outstanding_bytes = b2GetByteCount() - initial_bytes;
    return *outstanding_bytes == 0 ? 0 : -51;
}

#define DANGO_STACKED_BODY_COUNT 3

typedef struct dango_stacked_soft_bodies
{
    b2WorldId world_id;
    b2BodyId ground_body_id;
    b2ShapeId ground_shape_id;
    b2BodyId core_body_ids[DANGO_STACKED_BODY_COUNT];
    b2ShapeId core_proxy_shape_ids[DANGO_STACKED_BODY_COUNT];
    b2BodyId node_body_ids[DANGO_STACKED_BODY_COUNT][DANGO_SOFT_BODY_MAX_NODES];
    b2ShapeId node_shape_ids[DANGO_STACKED_BODY_COUNT][DANGO_SOFT_BODY_MAX_NODES];
    b2JointId radial_joint_ids[DANGO_STACKED_BODY_COUNT][DANGO_SOFT_BODY_MAX_NODES];
    b2JointId edge_joint_ids[DANGO_STACKED_BODY_COUNT][DANGO_SOFT_BODY_MAX_NODES];
    int node_count;
    int substep_count;
    int initial_box2d_bytes;
    int active_count;
    bool anchors_initialized;
    dango_soft_body_config config;
} dango_stacked_soft_bodies;

static void dango_destroy_stacked_internal(dango_stacked_soft_bodies* stack)
{
    if (stack == NULL)
    {
        return;
    }
    if (b2World_IsValid(stack->world_id))
    {
        b2DestroyWorld(stack->world_id);
    }
    free(stack);
}

static b2Vec2 dango_stacked_target_point(const dango_stacked_soft_bodies* stack, int index)
{
    float angle = 6.28318530717958647692f * (float)index / (float)stack->node_count;
    float sine = sinf(angle);
    float x = 0.5f * stack->config.rest_width * cosf(angle);
    float y = 0.5f * stack->config.rest_height * sine;
    float lower = fmaxf(0.0f, -sine);
    float top = fmaxf(0.0f, sine);
    x *= 1.0f + stack->config.lower_bulge * lower;
    x *= 1.0f - (1.0f - stack->config.top_roundness) * top * 0.22f;
    if (sine < -0.35f)
    {
        float bottom_y = -0.5f * stack->config.rest_height * (1.0f - stack->config.ground_compression);
        y += (bottom_y - y) * stack->config.bottom_flatness;
    }
    return (b2Vec2){x, y};
}

static uint64_t dango_stacked_node_category(int participant)
{
    return (uint64_t)0x00000010 << participant;
}

static uint64_t dango_stacked_proxy_category(int participant)
{
    return (uint64_t)0x00000100 << participant;
}

static int dango_create_stacked_ring(dango_stacked_soft_bodies* stack, int participant, float center_x)
{
    b2BodyDef core_def = b2DefaultBodyDef();
    core_def.type = b2_dynamicBody;
    core_def.position = (b2Vec2){center_x, 0.0f};
    core_def.linearDamping = 2.4f;
    core_def.angularDamping = 2.0f;
    core_def.gravityScale = 1.0f;
    core_def.fixedRotation = true;
    core_def.sleepThreshold = 0.025f;
    stack->core_body_ids[participant] = b2CreateBody(stack->world_id, &core_def);
    if (!b2Body_IsValid(stack->core_body_ids[participant]))
    {
        return -62;
    }

    uint64_t own_node_category = dango_stacked_node_category(participant);
    uint64_t own_proxy_category = dango_stacked_proxy_category(participant);
    uint64_t all_node_categories = 0;
    uint64_t all_proxy_categories = 0;
    for (int body = 0; body < DANGO_STACKED_BODY_COUNT; ++body)
    {
        all_node_categories |= dango_stacked_node_category(body);
        all_proxy_categories |= dango_stacked_proxy_category(body);
    }

    b2ShapeDef proxy_shape_def = b2DefaultShapeDef();
    proxy_shape_def.density = 0.55f;
    proxy_shape_def.material.friction = 0.0f;
    proxy_shape_def.material.restitution = stack->config.restitution;
    proxy_shape_def.filter.categoryBits = own_proxy_category;
    proxy_shape_def.filter.maskBits = all_node_categories & ~own_node_category;
    float proxy_half_width = fmaxf(0.2f, 0.38f * stack->config.rest_width);
    float proxy_half_height = fmaxf(0.2f, 0.54f * stack->config.rest_height);
    b2Polygon proxy_shape = b2MakeRoundedBox(proxy_half_width, proxy_half_height, 0.12f);
    stack->core_proxy_shape_ids[participant] = b2CreatePolygonShape(
        stack->core_body_ids[participant], &proxy_shape_def, &proxy_shape);
    if (!b2Shape_IsValid(stack->core_proxy_shape_ids[participant]))
    {
        return -62;
    }

    const float collision_radius = stack->config.node_radius * 0.10f;
    b2ShapeDef node_shape_def = b2DefaultShapeDef();
    node_shape_def.density = stack->config.node_mass /
        (3.14159265f * collision_radius * collision_radius);
    node_shape_def.material.friction = stack->config.friction;
    node_shape_def.material.restitution = stack->config.restitution;
    node_shape_def.filter.categoryBits = own_node_category;
    node_shape_def.filter.maskBits = 0x00000001 |
        (all_node_categories & ~own_node_category) |
        (all_proxy_categories & ~own_proxy_category);
    b2Circle node_circle = {{0.0f, 0.0f}, collision_radius};

    for (int index = 0; index < stack->node_count; ++index)
    {
        b2Vec2 local = dango_stacked_target_point(stack, index);
        b2BodyDef node_def = b2DefaultBodyDef();
        node_def.type = b2_dynamicBody;
        node_def.position = (b2Vec2){center_x + local.x, local.y};
        node_def.linearDamping = stack->config.linear_damping;
        node_def.angularDamping = 2.0f;
        node_def.gravityScale = 1.0f;
        node_def.fixedRotation = true;
        node_def.sleepThreshold = 0.025f;
        stack->node_body_ids[participant][index] = b2CreateBody(stack->world_id, &node_def);
        stack->node_shape_ids[participant][index] = b2CreateCircleShape(
            stack->node_body_ids[participant][index], &node_shape_def, &node_circle);
        if (!b2Body_IsValid(stack->node_body_ids[participant][index]) ||
            !b2Shape_IsValid(stack->node_shape_ids[participant][index]))
        {
            return -63;
        }
        stack->radial_joint_ids[participant][index] = dango_create_spring(
            stack->world_id,
            stack->core_body_ids[participant],
            stack->node_body_ids[participant][index],
            sqrtf(local.x * local.x + local.y * local.y),
            stack->config.radial_hertz,
            stack->config.radial_damping);
        if (!b2Joint_IsValid(stack->radial_joint_ids[participant][index]))
        {
            return -64;
        }
    }

    for (int index = 0; index < stack->node_count; ++index)
    {
        int next = (index + 1) % stack->node_count;
        b2Vec2 position = b2Body_GetPosition(stack->node_body_ids[participant][index]);
        b2Vec2 next_position = b2Body_GetPosition(stack->node_body_ids[participant][next]);
        stack->edge_joint_ids[participant][index] = dango_create_spring(
            stack->world_id,
            stack->node_body_ids[participant][index],
            stack->node_body_ids[participant][next],
            dango_distance(position, next_position),
            stack->config.edge_hertz,
            stack->config.edge_damping);
        if (!b2Joint_IsValid(stack->edge_joint_ids[participant][index]))
        {
            return -65;
        }
    }
    return 0;
}

DANGO_API int dango_box2d_stacked_soft_body_create_configured(
    const dango_soft_body_config* config,
    void** stacked_handle)
{
    if (stacked_handle == NULL)
    {
        return -60;
    }
    *stacked_handle = NULL;
    if (config == NULL ||
        !dango_is_supported_soft_body_configuration(config->node_count, config->substep_count))
    {
        return -61;
    }

    dango_stacked_soft_bodies* stack = calloc(1, sizeof(dango_stacked_soft_bodies));
    if (stack == NULL)
    {
        return -62;
    }
    stack->world_id = b2_nullWorldId;
    stack->initial_box2d_bytes = b2GetByteCount();
    stack->node_count = config->node_count;
    stack->substep_count = config->substep_count;
    stack->config = *config;

    b2WorldDef world_def = b2DefaultWorldDef();
    world_def.gravity = (b2Vec2){0.0f, -9.8f};
    stack->world_id = b2CreateWorld(&world_def);
    if (!b2World_IsValid(stack->world_id))
    {
        dango_destroy_stacked_internal(stack);
        return -62;
    }
    b2World_SetContactTuning(stack->world_id, config->contact_hertz, 10.0f, 3.0f);
    b2World_EnableSleeping(stack->world_id, true);

    b2BodyDef ground_def = b2DefaultBodyDef();
    ground_def.type = b2_staticBody;
    ground_def.position = (b2Vec2){0.0f, config->ground_height - 0.08f};
    stack->ground_body_id = b2CreateBody(stack->world_id, &ground_def);
    b2ShapeDef ground_shape_def = b2DefaultShapeDef();
    ground_shape_def.material.friction = fmaxf(config->friction, 0.75f);
    ground_shape_def.filter.categoryBits = 0x00000001;
    ground_shape_def.filter.maskBits = 0x00000070;
    b2Polygon ground_box = b2MakeBox(8.0f, 0.08f);
    stack->ground_shape_id = b2CreatePolygonShape(
        stack->ground_body_id, &ground_shape_def, &ground_box);
    if (!b2Body_IsValid(stack->ground_body_id) || !b2Shape_IsValid(stack->ground_shape_id))
    {
        dango_destroy_stacked_internal(stack);
        return -62;
    }
    b2Polygon side_wall = b2MakeBox(0.08f, 5.0f);
    b2ShapeDef wall_shape_def = ground_shape_def;
    b2Transform left_wall_transform = {{-5.5f, 5.0f}, b2Rot_identity};
    b2Transform right_wall_transform = {{5.5f, 5.0f}, b2Rot_identity};
    b2Polygon left_wall = b2TransformPolygon(left_wall_transform, &side_wall);
    b2Polygon right_wall = b2TransformPolygon(right_wall_transform, &side_wall);
    b2CreatePolygonShape(stack->ground_body_id, &wall_shape_def, &left_wall);
    b2CreatePolygonShape(stack->ground_body_id, &wall_shape_def, &right_wall);

    int result = 0;
    for (int participant = 0; participant < DANGO_STACKED_BODY_COUNT && result == 0; ++participant)
    {
        result = dango_create_stacked_ring(stack, participant, (float)participant * 3.0f);
    }
    if (result != 0)
    {
        dango_destroy_stacked_internal(stack);
        return result;
    }

    for (int participant = 0; participant < DANGO_STACKED_BODY_COUNT; ++participant)
    {
        b2Body_Disable(stack->core_body_ids[participant]);
        for (int index = 0; index < stack->node_count; ++index)
        {
            b2Body_Disable(stack->node_body_ids[participant][index]);
        }
    }

    *stacked_handle = stack;
    return 0;
}

static int dango_write_stacked_ring(
    dango_stacked_soft_bodies* stack,
    int participant,
    float* points,
    float* signed_area,
    float* centroid_x,
    float* centroid_y,
    float* maximum_speed)
{
    b2Vec2 core = b2Body_GetPosition(stack->core_body_ids[participant]);
    float area_twice = 0.0f;
    float center_x = 0.0f;
    float center_y = 0.0f;
    for (int index = 0; index < stack->node_count; ++index)
    {
        int next = (index + 1) % stack->node_count;
        b2Vec2 position = b2Body_GetPosition(stack->node_body_ids[participant][index]);
        b2Vec2 next_position = b2Body_GetPosition(stack->node_body_ids[participant][next]);
        b2Vec2 velocity = b2Body_GetLinearVelocity(stack->node_body_ids[participant][index]);
        float local_x = position.x - core.x;
        float local_y = position.y - core.y;
        float next_x = next_position.x - core.x;
        float next_y = next_position.y - core.y;
        float local_length = sqrtf(local_x * local_x + local_y * local_y);
        float next_length = sqrtf(next_x * next_x + next_y * next_y);
        if (local_length > 0.0001f)
        {
            local_x += stack->config.node_radius * local_x / local_length;
            local_y += stack->config.node_radius * local_y / local_length;
        }
        if (next_length > 0.0001f)
        {
            next_x += stack->config.node_radius * next_x / next_length;
            next_y += stack->config.node_radius * next_y / next_length;
        }
        float speed = sqrtf(velocity.x * velocity.x + velocity.y * velocity.y);
        if (!isfinite(local_x) || !isfinite(local_y) || !isfinite(speed))
        {
            return -68;
        }
        if (fabsf(local_x) > 1.2f || fabsf(local_y) > 1.2f)
        {
            return -72;
        }
        if (speed > 10.0f)
        {
            return -73;
        }
        points[index * 2] = local_x;
        points[index * 2 + 1] = local_y;
        center_x += local_x;
        center_y += local_y;
        area_twice += local_x * next_y - next_x * local_y;
        *maximum_speed = fmaxf(*maximum_speed, speed);
    }
    *signed_area = 0.5f * area_twice;
    *centroid_x = center_x / (float)stack->node_count;
    *centroid_y = center_y / (float)stack->node_count;
    return !isfinite(*signed_area) || *signed_area < 0.30f ? -69 : 0;
}

DANGO_API int dango_box2d_stacked_soft_body_step(
    void* stacked_handle,
    float time_step,
    const float* anchors,
    int anchor_count,
    float control_x,
    float control_y,
    float* first_points,
    float* second_points,
    float* third_points,
    int point_capacity,
    int* point_count,
    int* contact_count,
    int* first_second_contact,
    int* second_third_contact,
    float* signed_areas,
    float* centroid_xs,
    float* centroid_ys,
    float* core_xs,
    float* core_ys,
    float* maximum_speed)
{
    dango_stacked_soft_bodies* stack = stacked_handle;
    if (stack == NULL || anchors == NULL || first_points == NULL || second_points == NULL ||
        third_points == NULL || point_count == NULL || contact_count == NULL ||
        first_second_contact == NULL || second_third_contact == NULL || signed_areas == NULL ||
        centroid_xs == NULL || centroid_ys == NULL || core_xs == NULL || core_ys == NULL ||
        maximum_speed == NULL)
    {
        return -66;
    }
    if (!b2World_IsValid(stack->world_id) || anchor_count < 1 || anchor_count > DANGO_STACKED_BODY_COUNT ||
        point_capacity < stack->node_count || !isfinite(time_step) || time_step <= 0.0f || time_step > 0.1f)
    {
        return -67;
    }
    if (!isfinite(control_x) || !isfinite(control_y) ||
        control_x < -1.0f || control_x > 1.0f || control_y < -1.0f || control_y > 1.0f)
    {
        return -67;
    }
    for (int participant = 0; participant < DANGO_STACKED_BODY_COUNT; ++participant)
    {
        if (participant >= anchor_count)
        {
            b2Body_Disable(stack->core_body_ids[participant]);
            for (int index = 0; index < stack->node_count; ++index)
            {
                b2Body_Disable(stack->node_body_ids[participant][index]);
            }
            continue;
        }
        if (!isfinite(anchors[participant * 2]) || !isfinite(anchors[participant * 2 + 1]))
        {
            return -67;
        }
        b2Vec2 requested_anchor = {anchors[participant * 2], anchors[participant * 2 + 1]};
        if (participant >= stack->active_count)
        {
            b2Body_Enable(stack->core_body_ids[participant]);
            for (int index = 0; index < stack->node_count; ++index)
            {
                b2Body_Enable(stack->node_body_ids[participant][index]);
            }
            b2Vec2 current_anchor = b2Body_GetPosition(stack->core_body_ids[participant]);
            b2Vec2 delta = {requested_anchor.x - current_anchor.x, requested_anchor.y - current_anchor.y};
            b2Body_SetTransform(stack->core_body_ids[participant], requested_anchor, b2Rot_identity);
            for (int index = 0; index < stack->node_count; ++index)
            {
                b2Vec2 position = b2Body_GetPosition(stack->node_body_ids[participant][index]);
                b2Body_SetTransform(
                    stack->node_body_ids[participant][index],
                    (b2Vec2){position.x + delta.x, position.y + delta.y},
                    b2Rot_identity);
                b2Body_SetLinearVelocity(stack->node_body_ids[participant][index], (b2Vec2){0.0f, 0.0f});
            }
        }
        else if (participant == 0)
        {
            b2Body_SetTransform(stack->core_body_ids[participant], requested_anchor, b2Rot_identity);
            b2Body_SetLinearVelocity(stack->core_body_ids[participant], (b2Vec2){0.0f, 0.0f});
            b2Body_SetAwake(stack->core_body_ids[participant], true);
        }
    }
    stack->active_count = anchor_count;
    stack->anchors_initialized = true;

    b2World_Step(stack->world_id, time_step, stack->substep_count);
    *point_count = stack->node_count;
    *contact_count = 0;
    *first_second_contact = 0;
    *second_third_contact = 0;
    *maximum_speed = 0.0f;
    for (int participant = 0; participant < anchor_count; ++participant)
    {
        uint64_t participant_category = dango_stacked_node_category(participant);
        for (int index = 0; index < stack->node_count; ++index)
        {
            b2ContactData contacts[12];
            int capacity = b2Body_GetContactCapacity(stack->node_body_ids[participant][index]);
            capacity = capacity < 12 ? capacity : 12;
            if (capacity <= 0)
            {
                continue;
            }
            int count = b2Body_GetContactData(stack->node_body_ids[participant][index], contacts, capacity);
            for (int contact_index = 0; contact_index < count; ++contact_index)
            {
                uint64_t category_a = b2Shape_GetFilter(contacts[contact_index].shapeIdA).categoryBits;
                uint64_t category_b = b2Shape_GetFilter(contacts[contact_index].shapeIdB).categoryBits;
                uint64_t other_category = category_a == participant_category ? category_b : category_a;
                for (int other = participant + 1; other < anchor_count; ++other)
                {
                    uint64_t other_node = dango_stacked_node_category(other);
                    uint64_t other_proxy = dango_stacked_proxy_category(other);
                    if (other_category == other_node || other_category == other_proxy)
                    {
                        *contact_count += 1;
                        if (participant == 0 && other == 1)
                        {
                            *first_second_contact = 1;
                        }
                        if (participant == 1 && other == 2)
                        {
                            *second_third_contact = 1;
                        }
                    }
                }
            }
        }
        b2ContactData core_contacts[8];
        int core_capacity = b2Body_GetContactCapacity(stack->core_body_ids[participant]);
        core_capacity = core_capacity < 8 ? core_capacity : 8;
        if (core_capacity > 0)
        {
            int core_count = b2Body_GetContactData(
                stack->core_body_ids[participant], core_contacts, core_capacity);
            uint64_t participant_proxy = dango_stacked_proxy_category(participant);
            for (int contact_index = 0; contact_index < core_count; ++contact_index)
            {
                uint64_t category_a = b2Shape_GetFilter(core_contacts[contact_index].shapeIdA).categoryBits;
                uint64_t category_b = b2Shape_GetFilter(core_contacts[contact_index].shapeIdB).categoryBits;
                uint64_t other_category = category_a == participant_proxy ? category_b : category_a;
                for (int other = participant + 1; other < anchor_count; ++other)
                {
                    if (other_category == dango_stacked_proxy_category(other) ||
                        other_category == dango_stacked_node_category(other))
                    {
                        *contact_count += 1;
                        if (participant == 0 && other == 1)
                        {
                            *first_second_contact = 1;
                        }
                        if (participant == 1 && other == 2)
                        {
                            *second_third_contact = 1;
                        }
                    }
                }
            }
        }
    }

    float* point_sets[DANGO_STACKED_BODY_COUNT] = {first_points, second_points, third_points};
    int result = 0;
    for (int participant = 0; participant < anchor_count && result == 0; ++participant)
    {
        b2Vec2 core_position = b2Body_GetPosition(stack->core_body_ids[participant]);
        core_xs[participant] = core_position.x;
        core_ys[participant] = core_position.y;
        result = dango_write_stacked_ring(
            stack,
            participant,
            point_sets[participant],
            &signed_areas[participant],
            &centroid_xs[participant],
            &centroid_ys[participant],
            maximum_speed);
    }
    return result;
}

DANGO_API int dango_box2d_stacked_soft_body_destroy(void* stacked_handle, int* outstanding_bytes)
{
    dango_stacked_soft_bodies* stack = stacked_handle;
    if (stack == NULL || outstanding_bytes == NULL)
    {
        return -70;
    }
    int initial_bytes = stack->initial_box2d_bytes;
    dango_destroy_stacked_internal(stack);
    *outstanding_bytes = b2GetByteCount() - initial_bytes;
    return *outstanding_bytes == 0 ? 0 : -71;
}

#include "dango_self_physics_internal.h"

bool dango_self_config_valid(const dango_self_physics_config* config)
{
    return config != NULL && config->node_count == 16 &&
        (config->substep_count == 2 || config->substep_count == 4) &&
        isfinite(config->radial_rest_length) && config->radial_rest_length > 0.0f &&
        isfinite(config->radial_minimum_ratio) && isfinite(config->radial_maximum_ratio) &&
        config->radial_minimum_ratio > 0.0f && config->radial_minimum_ratio <= 1.0f &&
        config->radial_maximum_ratio >= 1.0f &&
        isfinite(config->perimeter_minimum_ratio) && isfinite(config->perimeter_maximum_ratio) &&
        config->perimeter_minimum_ratio > 0.0f && config->perimeter_minimum_ratio <= 1.0f &&
        config->perimeter_maximum_ratio >= 1.0f &&
        isfinite(config->ground_deformation_strength) && config->ground_deformation_strength >= 0.0f &&
        isfinite(config->local_angle_balance_strength) && config->local_angle_balance_strength >= 0.0f &&
        isfinite(config->top_inward_force) && config->top_inward_force >= 0.0f && config->top_inward_force <= 1.0f &&
        isfinite(config->gravity) && config->gravity > 0.0f &&
        isfinite(config->node_radius) && config->node_radius > 0.0f &&
        isfinite(config->maximum_speed) && config->maximum_speed > 0.0f;
}

static void dango_destroy_self_internal(dango_self_soft_body* body)
{
    if (body == NULL)
    {
        return;
    }
    if (b2World_IsValid(body->world_id))
    {
        b2DestroyWorld(body->world_id);
    }
    free(body);
}

static float dango_clamp_force(float value, float maximum)
{
    return fmaxf(-maximum, fminf(maximum, value));
}

static void dango_self_update_joint_parameters(
    dango_self_soft_body* body,
    const dango_self_physics_config* config)
{
    for (int index = 0; index < body->node_count; ++index)
    {
#if defined(DANGO_MATERIAL_RANGE_CACHE_CANDIDATE)
        // SetLengthRange clears spring and limit impulses even for equal values.
        // This private causal variant updates only an actually changed range.
        float minimum = body->radial_rest_length * config->radial_minimum_ratio;
        float maximum = body->radial_rest_length * config->radial_maximum_ratio;
        if (b2DistanceJoint_GetMinLength(body->radial_joint_ids[index]) != minimum ||
            b2DistanceJoint_GetMaxLength(body->radial_joint_ids[index]) != maximum)
            b2DistanceJoint_SetLengthRange(body->radial_joint_ids[index], minimum, maximum);
#else
        b2DistanceJoint_SetLengthRange(body->radial_joint_ids[index],
            body->radial_rest_length * config->radial_minimum_ratio,
            body->radial_rest_length * config->radial_maximum_ratio);
#endif
        b2DistanceJoint_SetSpringHertz(body->radial_joint_ids[index], config->radial_hertz);
        b2DistanceJoint_SetSpringDampingRatio(body->radial_joint_ids[index], config->radial_damping);
        b2DistanceJoint_SetSpringHertz(body->perimeter_joint_ids[index], config->perimeter_hertz);
        b2DistanceJoint_SetSpringDampingRatio(body->perimeter_joint_ids[index], config->perimeter_damping);
    }
}

static float dango_self_signed_area(dango_self_soft_body* body)
{
    float twice_area = 0.0f;
    for (int index = 0; index < body->node_count; ++index)
    {
        int next = (index + 1) % body->node_count;
        b2Vec2 first = b2Body_GetPosition(body->node_body_ids[index]);
        b2Vec2 second = b2Body_GetPosition(body->node_body_ids[next]);
        twice_area += first.x * second.y - second.x * first.y;
    }
    return 0.5f * twice_area;
}

#if defined(DANGO_MATERIAL_EXPERIMENT_MODE)
// Explicit isolated-build mode: bit 1 is area, bit 2 is bending, bit 4 is
// half-node initial phase; bit 8 omits only extra passive spin damping.
// Bit 16 retains configured contact friction during idle.
// Bit 32 leaves radial bounds to the unchanged post-step constraint solver.
// No ABI field,
// material-slider heuristic or production default selects this candidate.
static int dango_self_material_mode(void)
{
    return DANGO_MATERIAL_EXPERIMENT_MODE;
}

static int dango_self_material_force_values(dango_self_soft_body* body,
    const dango_self_physics_config* config, bool bending, b2Vec2* values)
{
    if (body->node_count < 3 || body->node_count > DANGO_SOFT_BODY_MAX_NODES)
        return bending ? -95 : -94;
    dango_material_vec2 points[DANGO_SOFT_BODY_MAX_NODES], forces[DANGO_SOFT_BODY_MAX_NODES];
    for (int i = 0; i < body->node_count; ++i)
    {
        b2Vec2 p = b2Body_GetPosition(body->node_body_ids[i]);
        points[i] = (dango_material_vec2){p.x, p.y};
    }
    double energy, cap_scale;
    // At a natural regular ring, match the old center radial contribution.
    // This is a fixed dimensional mapping, not full old-force equivalence.
    double k = config->local_angle_balance_strength * (double)body->radial_rest_length *
        tan(3.14159265358979323846 / body->node_count) * 0.5;
    int status = bending ? dango_material_bending(points, body->node_count, k, &energy, forces) :
        dango_material_area(points, body->node_count, body->target_area,
            body->radial_rest_length, config->area_strength, &energy, forces);
    if (status != DANGO_MATERIAL_OK) return bending ? -95 : -94;
    if (dango_material_cap(forces, body->node_count, config->repulsion_maximum_force, &cap_scale) != DANGO_MATERIAL_OK)
        return bending ? -95 : -94;
    // Validate every float before any candidate force is applied.
    for (int i = 0; i < body->node_count; ++i)
    {
        values[i] = (b2Vec2){(float)forces[i].x, (float)forces[i].y};
        if (!isfinite(values[i].x) || !isfinite(values[i].y)) return bending ? -95 : -94;
    }
    return 0;
}
#endif

static int dango_self_apply_area_force(
    dango_self_soft_body* body,
    const dango_self_physics_config* config)
{
#if defined(DANGO_MATERIAL_EXPERIMENT_MODE)
    if (dango_self_material_mode() & 1)
    {
        b2Vec2 forces[DANGO_SOFT_BODY_MAX_NODES];
        int error = dango_self_material_force_values(body, config, false, forces);
        if (error) return error;
        for (int i = 0; i < body->node_count; ++i)
            b2Body_ApplyForceToCenter(body->node_body_ids[i], forces[i], true);
        return 0;
    }
#endif
    float area = dango_self_signed_area(body);
    if (!isfinite(area) || area >= body->target_area || config->area_strength <= 0.0f)
    {
        return 0;
    }
    float pressure = config->area_strength * (body->target_area - area) / body->target_area;
    pressure = fminf(pressure, config->repulsion_maximum_force);
    b2Vec2 core = b2Body_GetPosition(body->core_body_id);
    for (int index = 0; index < body->node_count; ++index)
    {
        b2Vec2 position = b2Body_GetPosition(body->node_body_ids[index]);
        b2Vec2 direction = {position.x - core.x, position.y - core.y};
        float length = sqrtf(direction.x * direction.x + direction.y * direction.y);
        if (length > 0.0001f)
        {
            b2Body_ApplyForceToCenter(
                body->node_body_ids[index],
                (b2Vec2){pressure * direction.x / length, pressure * direction.y / length},
                true);
        }
    }
    return 0;
}

static b2Vec2 dango_self_top_force(b2Vec2 radial, float strength)
{
    float length = b2Length(radial);
    if (length <= 0.0001f || radial.y <= 0.0f || strength <= 0.0f)
    {
        return (b2Vec2){0.0f, 0.0f};
    }
    float upward = radial.y / length;
    return b2MulSV(-strength * upward * upward / length, radial);
}

static void dango_self_apply_top_pressure(
    dango_self_soft_body* body,
    const dango_self_physics_config* config)
{
    if (config->top_inward_force <= 0.0f)
    {
        return;
    }

    b2Vec2 core = b2Body_GetPosition(body->core_body_id);
    b2Vec2 reaction = {0.0f, 0.0f};
    for (int index = 0; index < body->node_count; ++index)
    {
        b2Vec2 node = b2Body_GetPosition(body->node_body_ids[index]);
        b2Vec2 radial = {node.x - core.x, node.y - core.y};
        b2Vec2 force = dango_self_top_force(radial, config->top_inward_force);
        b2Body_ApplyForceToCenter(body->node_body_ids[index], force, true);
        reaction.x -= force.x;
        reaction.y -= force.y;
    }
    b2Body_ApplyForceToCenter(body->core_body_id, reaction, true);
}

void dango_self_apply_ground_deformation_force(
    dango_self_soft_body* body,
    const dango_self_physics_config* config)
{
    if (!body->grounded || config->ground_deformation_strength <= 0.0f)
    {
        return;
    }

    b2Vec2 core = b2Body_GetPosition(body->core_body_id);
    float total_force_x = 0.0f;
    for (int index = 0; index < body->node_count; ++index)
    {
        b2Vec2 position = b2Body_GetPosition(body->node_body_ids[index]);
        float local_x = position.x - core.x;
        float local_y = position.y - core.y;
        float horizontal_weight = fminf(1.0f, fabsf(local_x) / body->radial_rest_length);
        float vertical_bias = fmaxf(0.0f, fminf(1.0f, -local_y / body->radial_rest_length));
        float force_x = config->ground_deformation_strength * vertical_bias * horizontal_weight;
        force_x = local_x < 0.0f ? -force_x : force_x;
        b2Body_ApplyForceToCenter(body->node_body_ids[index], (b2Vec2){force_x, 0.0f}, true);
        total_force_x += force_x;
    }
    b2Body_ApplyForceToCenter(body->core_body_id, (b2Vec2){-total_force_x, 0.0f}, true);
}

static void dango_self_apply_radial_repulsion(
    dango_self_soft_body* body,
    const dango_self_physics_config* config)
{
    if (config->repulsion_strength <= 0.0f)
    {
        return;
    }
    b2Vec2 core = b2Body_GetPosition(body->core_body_id);
    float sample = fmaxf(0.05f, fminf(0.95f, config->core_exclusion_ratio));
    for (int first = 0; first < body->node_count; ++first)
    {
        b2Vec2 first_position = b2Body_GetPosition(body->node_body_ids[first]);
        b2Vec2 first_sample =
        {
            core.x + (first_position.x - core.x) * sample,
            core.y + (first_position.y - core.y) * sample
        };
        for (int second = first + 1; second < body->node_count; ++second)
        {
            b2Vec2 second_position = b2Body_GetPosition(body->node_body_ids[second]);
            b2Vec2 second_sample =
            {
                core.x + (second_position.x - core.x) * sample,
                core.y + (second_position.y - core.y) * sample
            };
            b2Vec2 delta = {second_sample.x - first_sample.x, second_sample.y - first_sample.y};
            float distance = sqrtf(delta.x * delta.x + delta.y * delta.y);
            if (distance >= config->repulsion_distance || distance < 0.0001f)
            {
                continue;
            }
            float magnitude = config->repulsion_strength *
                (config->repulsion_distance - distance) / config->repulsion_distance;
            magnitude = fminf(magnitude, config->repulsion_maximum_force);
            b2Vec2 force = {magnitude * delta.x / distance, magnitude * delta.y / distance};
            b2Body_ApplyForceToCenter(body->node_body_ids[first], (b2Vec2){-force.x, -force.y}, true);
            b2Body_ApplyForceToCenter(body->node_body_ids[second], force, true);
        }
    }
}

static void dango_self_apply_angle_barrier(
    dango_self_soft_body* body,
    const dango_self_physics_config* config)
{
    if (config->angle_strength <= 0.0f)
    {
        return;
    }
    for (int index = 0; index < body->node_count; ++index)
    {
        int previous = (index - 1 + body->node_count) % body->node_count;
        int next = (index + 1) % body->node_count;
        b2Vec2 center = b2Body_GetPosition(body->node_body_ids[index]);
        b2Vec2 previous_position = b2Body_GetPosition(body->node_body_ids[previous]);
        b2Vec2 next_position = b2Body_GetPosition(body->node_body_ids[next]);
        b2Vec2 first = {previous_position.x - center.x, previous_position.y - center.y};
        b2Vec2 second = {next_position.x - center.x, next_position.y - center.y};
        float first_length = sqrtf(first.x * first.x + first.y * first.y);
        float second_length = sqrtf(second.x * second.x + second.y * second.y);
        if (first_length < 0.0001f || second_length < 0.0001f)
        {
            continue;
        }
        float cosine = (first.x * second.x + first.y * second.y) / (first_length * second_length);
        if (cosine <= 0.0f)
        {
            continue;
        }
        b2Vec2 separation = {next_position.x - previous_position.x, next_position.y - previous_position.y};
        float separation_length = sqrtf(separation.x * separation.x + separation.y * separation.y);
        if (separation_length < 0.0001f)
        {
            continue;
        }
        float magnitude = fminf(config->angle_strength * cosine, config->repulsion_maximum_force);
        b2Vec2 force =
        {
            magnitude * separation.x / separation_length,
            magnitude * separation.y / separation_length
        };
        b2Body_ApplyForceToCenter(body->node_body_ids[previous], (b2Vec2){-force.x, -force.y}, true);
        b2Body_ApplyForceToCenter(body->node_body_ids[next], force, true);
    }
}

static void dango_self_apply_convexity_barrier(
    dango_self_soft_body* body,
    const dango_self_physics_config* config)
{
    if (config->angle_strength <= 0.0f)
    {
        return;
    }

    for (int index = 0; index < body->node_count; ++index)
    {
        int previous = (index - 1 + body->node_count) % body->node_count;
        int next = (index + 1) % body->node_count;
        b2Vec2 p = b2Body_GetPosition(body->node_body_ids[previous]);
        b2Vec2 c = b2Body_GetPosition(body->node_body_ids[index]);
        b2Vec2 n = b2Body_GetPosition(body->node_body_ids[next]);
        b2Vec2 incoming = {c.x - p.x, c.y - p.y};
        b2Vec2 outgoing = {n.x - c.x, n.y - c.y};
        float incoming_length = sqrtf(incoming.x * incoming.x + incoming.y * incoming.y);
        float outgoing_length = sqrtf(outgoing.x * outgoing.x + outgoing.y * outgoing.y);
        if (incoming_length < 0.0001f || outgoing_length < 0.0001f)
        {
            continue;
        }

        float normalized_cross =
            (incoming.x * outgoing.y - incoming.y * outgoing.x) /
            (incoming_length * outgoing_length);
        const float activation_threshold = 0.12f;
        if (normalized_cross >= activation_threshold)
        {
            continue;
        }

        b2Vec2 previous_gradient = {c.y - n.y, n.x - c.x};
        b2Vec2 center_gradient = {n.y - p.y, p.x - n.x};
        b2Vec2 next_gradient = {p.y - c.y, c.x - p.x};
        float previous_gradient_length = sqrtf(
            previous_gradient.x * previous_gradient.x + previous_gradient.y * previous_gradient.y);
        float center_gradient_length = sqrtf(
            center_gradient.x * center_gradient.x + center_gradient.y * center_gradient.y);
        float next_gradient_length = sqrtf(
            next_gradient.x * next_gradient.x + next_gradient.y * next_gradient.y);
        float maximum_gradient_length = fmaxf(
            previous_gradient_length,
            fmaxf(center_gradient_length, next_gradient_length));
        if (maximum_gradient_length < 0.0001f)
        {
            continue;
        }

        float penetration = fminf(1.0f, activation_threshold - normalized_cross);
        float scale = fminf(
            config->angle_strength * penetration,
            config->repulsion_maximum_force) / maximum_gradient_length;
        b2Body_ApplyForceToCenter(
            body->node_body_ids[previous],
            (b2Vec2){previous_gradient.x * scale, previous_gradient.y * scale},
            true);
        b2Body_ApplyForceToCenter(
            body->node_body_ids[index],
            (b2Vec2){center_gradient.x * scale, center_gradient.y * scale},
            true);
        b2Body_ApplyForceToCenter(
            body->node_body_ids[next],
            (b2Vec2){next_gradient.x * scale, next_gradient.y * scale},
            true);
    }
}

static float dango_self_interior_angle(dango_self_soft_body* body, int index)
{
    int previous = (index - 1 + body->node_count) % body->node_count;
    int next = (index + 1) % body->node_count;
    b2Vec2 p = b2Body_GetPosition(body->node_body_ids[previous]);
    b2Vec2 c = b2Body_GetPosition(body->node_body_ids[index]);
    b2Vec2 n = b2Body_GetPosition(body->node_body_ids[next]);
    b2Vec2 incoming = {c.x - p.x, c.y - p.y};
    b2Vec2 outgoing = {n.x - c.x, n.y - c.y};
    float cross = incoming.x * outgoing.y - incoming.y * outgoing.x;
    float dot = incoming.x * outgoing.x + incoming.y * outgoing.y;
    return 3.14159265358979323846f - atan2f(cross, dot);
}

static int dango_self_apply_local_angle_balance_force(
    dango_self_soft_body* body,
    const dango_self_physics_config* config)
{
#if defined(DANGO_MATERIAL_EXPERIMENT_MODE)
    if (dango_self_material_mode() & 2)
    {
        b2Vec2 forces[DANGO_SOFT_BODY_MAX_NODES];
        int error = dango_self_material_force_values(body, config, true, forces);
        if (error) return error;
        for (int i = 0; i < body->node_count; ++i)
            b2Body_ApplyForceToCenter(body->node_body_ids[i], forces[i], true);
        return 0;
    }
#endif
    if (config->local_angle_balance_strength <= 0.0f)
    {
        return 0;
    }

    for (int index = 0; index < body->node_count; ++index)
    {
        int previous = (index - 1 + body->node_count) % body->node_count;
        int next = (index + 1) % body->node_count;
        float current_angle = dango_self_interior_angle(body, index);
        float neighbor_average = 0.5f *
            (dango_self_interior_angle(body, previous) + dango_self_interior_angle(body, next));
        float angle_difference = current_angle - neighbor_average;
        if (fabsf(angle_difference) < 0.001f)
        {
            continue;
        }

        b2Vec2 previous_position = b2Body_GetPosition(body->node_body_ids[previous]);
        b2Vec2 next_position = b2Body_GetPosition(body->node_body_ids[next]);
        b2Vec2 chord =
        {
            next_position.x - previous_position.x,
            next_position.y - previous_position.y
        };
        float chord_length = sqrtf(chord.x * chord.x + chord.y * chord.y);
        if (chord_length < 0.0001f)
        {
            continue;
        }

        float magnitude = dango_clamp_force(
            config->local_angle_balance_strength * angle_difference,
            config->repulsion_maximum_force);
        b2Vec2 center_force =
        {
            magnitude * chord.y / chord_length,
            -magnitude * chord.x / chord_length
        };
        b2Body_ApplyForceToCenter(body->node_body_ids[index], center_force, true);
        b2Vec2 neighbor_force = {-0.5f * center_force.x, -0.5f * center_force.y};
        b2Body_ApplyForceToCenter(body->node_body_ids[previous], neighbor_force, true);
        b2Body_ApplyForceToCenter(body->node_body_ids[next], neighbor_force, true);
    }
    return 0;
}

static float dango_orientation(b2Vec2 first, b2Vec2 second, b2Vec2 third)
{
    return (second.x - first.x) * (third.y - first.y) -
        (second.y - first.y) * (third.x - first.x);
}

static bool dango_self_has_crossing(dango_self_soft_body* body)
{
    for (int first = 0; first < body->node_count; ++first)
    {
        int first_next = (first + 1) % body->node_count;
        b2Vec2 a = b2Body_GetPosition(body->node_body_ids[first]);
        b2Vec2 b = b2Body_GetPosition(body->node_body_ids[first_next]);
        for (int second = first + 2; second < body->node_count; ++second)
        {
            int second_next = (second + 1) % body->node_count;
            if (first == second_next || first_next == second)
            {
                continue;
            }
            b2Vec2 c = b2Body_GetPosition(body->node_body_ids[second]);
            b2Vec2 d = b2Body_GetPosition(body->node_body_ids[second_next]);
            float ab_c = dango_orientation(a, b, c);
            float ab_d = dango_orientation(a, b, d);
            float cd_a = dango_orientation(c, d, a);
            float cd_b = dango_orientation(c, d, b);
            if (ab_c * ab_d < 0.0f && cd_a * cd_b < 0.0f)
            {
                return true;
            }
        }
    }
    return false;
}

bool dango_self_limit_speed(dango_self_soft_body* body, float limit)
{
    float maximum = 0.0f;
    for (int i = 0; i <= body->node_count; ++i)
    {
        b2BodyId id = i == body->node_count ? body->core_body_id : body->node_body_ids[i];
        float speed = b2Length(b2Body_GetLinearVelocity(id));
        if (!isfinite(speed))
        {
            return false;
        }
        maximum = fmaxf(maximum, speed);
    }
    if (maximum > limit)
    {
        // One positive scale preserves relative-velocity constraint directions
        // and only removes energy. Leave positions and ordinary motion untouched.
        float scale = (limit * 0.99999f) / maximum;
        for (int i = 0; i <= body->node_count; ++i)
        {
            b2BodyId id = i == body->node_count ? body->core_body_id : body->node_body_ids[i];
            b2Body_SetLinearVelocity(id, b2MulSV(scale, b2Body_GetLinearVelocity(id)));
        }
    }
    return true;
}

bool dango_self_solve_constraints(dango_self_soft_body* body,
    const dango_self_physics_config* config)
{
    dango_constraint_state state = {0};
    state.radial_length = body->radial_rest_length;
    state.perimeter_length = body->perimeter_rest_length;
    state.radial_min = config->radial_minimum_ratio;
    state.radial_max = config->radial_maximum_ratio;
    state.perimeter_min = config->perimeter_minimum_ratio;
    state.perimeter_max = config->perimeter_maximum_ratio;
    state.radius = config->node_radius;
    state.ground = config->ground_height;
#if defined(DANGO_LAB_ENVELOPE)
    state.envelope_friction=config->friction; state.envelope_stats=body->envelope_stats;
#endif
    for (int i = 0; i <= body->node_count; ++i)
    {
        b2BodyId id = i == body->node_count ? body->core_body_id : body->node_body_ids[i];
        state.positions[i] = b2Body_GetPosition(id);
        state.velocities[i] = b2Body_GetLinearVelocity(id);
        state.inverse_mass[i] = 1.0f / b2Body_GetMass(id);
    }
    if (!dango_constraint_solve(&state))
    {
#if defined(DANGO_LAB_ENVELOPE)
        body->envelope_stats[30]=1;
#endif
        return false;
    }
    body->grounded = false;
    for (int i = 0; i <= body->node_count; ++i)
    {
        b2BodyId id = i == body->node_count ? body->core_body_id : body->node_body_ids[i];
        b2Vec2 old = b2Body_GetPosition(id);
        if (b2DistanceSquared(old, state.positions[i]) > 0.0f)
        {
            b2Body_SetTransform(id, state.positions[i], b2Rot_identity);
        }
        b2Body_SetLinearVelocity(id, state.velocities[i]);
        if (i < body->node_count && state.positions[i].y - config->node_radius <= config->ground_height + 0.035f)
        {
            body->grounded = true;
        }
    }
#if defined(DANGO_LAB_ENVELOPE)
    dango_envelope_point foot; if(!dango_envelope_geometry(state.positions,state.radius,&foot)) return false;
    body->grounded=foot.point.y-config->ground_height<=.035f;
    body->envelope_stats[20]=fmaxf(body->envelope_stats[20],fmaxf(0,config->ground_height-foot.point.y));
#endif
    return !dango_self_has_crossing(body) && dango_self_signed_area(body) > 0.0f;
}

void dango_self_create_rig(dango_self_soft_body* body,
    const dango_self_physics_config* config)
{
    body->node_count = config->node_count;
    body->substep_count = config->substep_count;
    body->selected_drag_node = -1;
    body->seconds_since_jump = 1000.0f;
    body->radial_rest_length = config->radial_rest_length;
    body->perimeter_rest_length = 2.0f * config->radial_rest_length *
        sinf(3.14159265358979323846f / (float)config->node_count);
    body->target_area = 0.5f * (float)config->node_count *
        config->radial_rest_length * config->radial_rest_length *
        sinf(6.28318530717958647692f / (float)config->node_count);

    b2BodyDef core_def = b2DefaultBodyDef();
    core_def.type = b2_dynamicBody;
    core_def.position = (b2Vec2){0.0f, config->drop_height};
    core_def.linearDamping = config->linear_damping;
    core_def.angularDamping = 2.0f;
    core_def.gravityScale = 1.0f;
    core_def.fixedRotation = true;
    body->core_body_id = b2CreateBody(body->world_id, &core_def);
    b2ShapeDef core_shape_def = b2DefaultShapeDef();
    const float core_radius = 0.12f;
    core_shape_def.density = config->core_mass / (3.14159265f * core_radius * core_radius);
    core_shape_def.filter.categoryBits = 0x00000004;
    core_shape_def.filter.maskBits = 0;
    b2Circle core_circle = {{0.0f, 0.0f}, core_radius};
    body->core_shape_id = b2CreateCircleShape(body->core_body_id, &core_shape_def, &core_circle);

    b2ShapeDef node_shape_def = b2DefaultShapeDef();
    node_shape_def.density = config->node_mass /
        (3.14159265f * config->node_radius * config->node_radius);
    node_shape_def.material.friction = config->friction;
    node_shape_def.material.restitution = config->restitution;
    node_shape_def.filter.categoryBits = 0x00000002;
    node_shape_def.filter.maskBits = 0x00000001;
    b2Circle node_circle = {{0.0f, 0.0f}, config->node_radius};
    for (int index = 0; index < body->node_count; ++index)
    {
        float angle = 6.28318530717958647692f * (float)index / (float)body->node_count;
#if defined(DANGO_MATERIAL_EXPERIMENT_MODE)
        // A fixed natural-circle orientation experiment, never a grounded pose.
        if (dango_self_material_mode() & 4)
        {
            angle += 3.14159265358979323846f / (float)body->node_count;
        }
#endif
        b2Vec2 local =
        {
            body->radial_rest_length * cosf(angle),
            body->radial_rest_length * sinf(angle)
        };
        b2BodyDef node_def = b2DefaultBodyDef();
        node_def.type = b2_dynamicBody;
        node_def.position = (b2Vec2){local.x, config->drop_height + local.y};
        node_def.linearDamping = config->linear_damping;
        node_def.angularDamping = 2.0f;
        node_def.gravityScale = 1.0f;
        node_def.fixedRotation = true;
        body->node_body_ids[index] = b2CreateBody(body->world_id, &node_def);
        body->node_shape_ids[index] = b2CreateCircleShape(
            body->node_body_ids[index], &node_shape_def, &node_circle);
        body->radial_joint_ids[index] = dango_create_spring(
            body->world_id,
            body->core_body_id,
            body->node_body_ids[index],
            body->radial_rest_length,
            config->radial_hertz,
            config->radial_damping);
        b2DistanceJoint_EnableLimit(body->radial_joint_ids[index], true);
#if defined(DANGO_MATERIAL_EXPERIMENT_MODE)
        if (dango_self_material_mode() & 32)
        {
            b2DistanceJoint_EnableLimit(body->radial_joint_ids[index], false);
        }
#endif
        b2DistanceJoint_SetLengthRange(
            body->radial_joint_ids[index],
            body->radial_rest_length * config->radial_minimum_ratio,
            body->radial_rest_length * config->radial_maximum_ratio);
    }
    for (int index = 0; index < body->node_count; ++index)
    {
        int next = (index + 1) % body->node_count;
        body->perimeter_joint_ids[index] = dango_create_spring(
            body->world_id,
            body->node_body_ids[index],
            body->node_body_ids[next],
            body->perimeter_rest_length,
            config->perimeter_hertz,
            config->perimeter_damping);
        b2DistanceJoint_EnableLimit(body->perimeter_joint_ids[index], true);
        b2DistanceJoint_SetLengthRange(
            body->perimeter_joint_ids[index],
            body->perimeter_rest_length * config->perimeter_minimum_ratio,
            body->perimeter_rest_length * config->perimeter_maximum_ratio);
    }
}

void dango_self_destroy_rig(dango_self_soft_body* body)
{
    if (body == NULL)
    {
        return;
    }
    for (int index = 0; index < body->node_count; ++index)
    {
        if (b2Body_IsValid(body->node_body_ids[index]))
        {
            b2DestroyBody(body->node_body_ids[index]);
        }
        body->node_body_ids[index] = b2_nullBodyId;
        body->node_shape_ids[index] = b2_nullShapeId;
        body->radial_joint_ids[index] = b2_nullJointId;
        body->perimeter_joint_ids[index] = b2_nullJointId;
    }
    if (b2Body_IsValid(body->core_body_id))
    {
        b2DestroyBody(body->core_body_id);
    }
    body->core_body_id = b2_nullBodyId;
    body->core_shape_id = b2_nullShapeId;
    body->node_count = 0;
    body->initialized = false;
    body->drag_active = false;
    body->grounded = false;
    body->selected_drag_node = -1;
}

static int dango_self_apply_internal_forces(dango_self_soft_body* body,
    const dango_self_physics_config* config)
{
    int error = dango_self_apply_area_force(body, config);
    if (error) return error;
    dango_self_apply_top_pressure(body, config);
    dango_self_apply_ground_deformation_force(body, config);
    dango_self_apply_radial_repulsion(body, config);
    dango_self_apply_angle_barrier(body, config);
    dango_self_apply_convexity_barrier(body, config);
    return dango_self_apply_local_angle_balance_force(body, config);
}

int dango_self_world_create(const dango_self_physics_config* config,dango_self_soft_body** self_handle)
{
    if (self_handle == NULL)
    {
        return -80;
    }
    *self_handle = NULL;
    if (!dango_self_config_valid(config))
    {
        return -81;
    }
    dango_self_soft_body* body = calloc(1, sizeof(dango_self_soft_body));
    if (body == NULL)
    {
        return -82;
    }
    body->world_id = b2_nullWorldId;
    body->initial_box2d_bytes = b2GetByteCount();

    b2WorldDef world_def = b2DefaultWorldDef();
    world_def.gravity = (b2Vec2){0.0f, -config->gravity};
    body->world_id = b2CreateWorld(&world_def);
    if (!b2World_IsValid(body->world_id))
    {
        dango_destroy_self_internal(body);
        return -82;
    }
    b2World_SetContactTuning(body->world_id, config->contact_hertz, 10.0f, 3.0f);
    b2World_EnableSleeping(body->world_id, true);

    b2BodyDef ground_def = b2DefaultBodyDef();
    ground_def.type = b2_staticBody;
    ground_def.position = (b2Vec2){0.0f, config->ground_height - 0.08f};
    body->ground_body_id = b2CreateBody(body->world_id, &ground_def);
    b2ShapeDef ground_shape_def = b2DefaultShapeDef();
    ground_shape_def.material.friction = config->friction;
    ground_shape_def.material.restitution = config->restitution;
    ground_shape_def.filter.categoryBits = 0x00000001;
    ground_shape_def.filter.maskBits = 0x00000002;
#if defined(DANGO_MATERIAL_CONTACT_WIDE_GROUND_CANDIDATE)
    // The isolated sample's permitted throw trajectory crosses the old +/-8
    // corner, where a vertical-plane contact adapter must reject angled normals.
    b2Polygon ground_box = b2MakeBox(64.0f, 0.08f);
#else
    b2Polygon ground_box = b2MakeBox(8.0f, 0.08f);
#endif
#if defined(DANGO_LAB_ENVELOPE)
    ground_shape_def.filter.maskBits=0;
#endif
    body->ground_shape_id = b2CreatePolygonShape(body->ground_body_id, &ground_shape_def, &ground_box);

    if (!b2Body_IsValid(body->ground_body_id) || !b2Shape_IsValid(body->ground_shape_id))
    {
        dango_destroy_self_internal(body);
        return -83;
    }
    *self_handle = body;
    return 0;
}

DANGO_API int dango_box2d_self_soft_body_create(
    const dango_self_physics_config* config,
    void** self_handle)
{
    dango_self_soft_body* body=NULL;
    int error=dango_self_world_create(config,&body);
    if (error) return error;
    dango_self_create_rig(body,config);
    if (!b2Body_IsValid(body->core_body_id) || !b2Shape_IsValid(body->core_shape_id))
    {
        dango_destroy_self_internal(body);
        return -83;
    }
    *self_handle=body;
    return 0;
}

void dango_self_initialize_at(dango_self_soft_body* body, const dango_self_physics_config* config, float anchor_x, float anchor_y)
{
    if (!body->initialized)
    {
        b2Vec2 current = b2Body_GetPosition(body->core_body_id);
        b2Vec2 desired = {anchor_x, anchor_y + config->drop_height};
        b2Vec2 delta = {desired.x - current.x, desired.y - current.y};
        b2Body_SetTransform(body->core_body_id, desired, b2Rot_identity);
        for (int index = 0; index < body->node_count; ++index)
        {
            b2Vec2 position = b2Body_GetPosition(body->node_body_ids[index]);
            b2Body_SetTransform(
                body->node_body_ids[index],
                (b2Vec2){position.x + delta.x, position.y + delta.y},
                b2Rot_identity);
        }
        body->initialized = true;
    }

}

#if defined(DANGO_BODY_DISTRIBUTED_DRIVE)
#include "dango_body_drive.h"
#endif

int dango_self_apply_step_forces(dango_self_soft_body* body, const dango_self_physics_config* config,
    float small_time_step, int small_step, float horizontal_control, float downward_control,
    int jump_pressed, int drag_active, float drag_target_x, float drag_target_y)
{
    dango_self_update_joint_parameters(body, config);
    body->seconds_since_jump += small_time_step;
    b2Vec2 core_velocity = b2Body_GetLinearVelocity(body->core_body_id);
    float control_scale = body->grounded ? 1.0f : config->air_control_ratio;
    if ((horizontal_control < 0.0f && core_velocity.x > -config->maximum_horizontal_speed) ||
        (horizontal_control > 0.0f && core_velocity.x < config->maximum_horizontal_speed))
    {
#if defined(DANGO_BODY_DISTRIBUTED_DRIVE)
        int drive_error = dango_body_apply_mass_drive(body,
            horizontal_control * config->horizontal_force * control_scale);
        if (drive_error != 0) return drive_error;
#else
        b2Body_ApplyForceToCenter(
            body->core_body_id,
            (b2Vec2){horizontal_control * config->horizontal_force * control_scale, 0.0f},
            true);
#endif
    }
    if (downward_control > 0.0f)
    {
        b2Body_ApplyForceToCenter(
            body->core_body_id,
            (b2Vec2){0.0f, -downward_control * config->downward_force},
            true);
    }
    if (small_step == 0 && jump_pressed != 0 && body->grounded && body->seconds_since_jump >= config->jump_cooldown_seconds)
    {
        b2Body_ApplyLinearImpulseToCenter(
            body->core_body_id,
            (b2Vec2){0.0f, config->jump_impulse},
            true);
        body->grounded = false;
        body->seconds_since_jump = 0.0f;
    }

    if (drag_active != 0 && !body->drag_active)
    {
        float highest = -INFINITY;
        body->selected_drag_node = 0;
        for (int index = 0; index < body->node_count; ++index)
        {
            b2Vec2 position = b2Body_GetPosition(body->node_body_ids[index]);
            if (position.y > highest)
            {
                highest = position.y;
                body->selected_drag_node = index;
            }
        }
    }
    body->drag_active = drag_active != 0;
    if (body->drag_active && body->selected_drag_node >= 0)
    {
        b2BodyId dragged = body->node_body_ids[body->selected_drag_node];
        b2Vec2 position = b2Body_GetPosition(dragged);
        b2Vec2 velocity = b2Body_GetLinearVelocity(dragged);
        b2Vec2 force =
        {
            config->drag_stiffness * (drag_target_x - position.x) - config->drag_damping * velocity.x,
            config->drag_stiffness * (drag_target_y - position.y) - config->drag_damping * velocity.y
        };
        float magnitude = sqrtf(force.x * force.x + force.y * force.y);
        if (magnitude > config->drag_maximum_force && magnitude > 0.0001f)
        {
            force.x *= config->drag_maximum_force / magnitude;
            force.y *= config->drag_maximum_force / magnitude;
        }
        b2Body_ApplyForceToCenter(dragged, force, true);
    }
    else
    {
        body->selected_drag_node = -1;
    }

    return dango_self_apply_internal_forces(body, config);
}

int dango_self_read_frame(dango_self_soft_body* body, const dango_self_physics_config* config,
    float* points, int* point_count, int* ground_contact_count, float* signed_area,
    float* centroid_x, float* centroid_y, float* core_x, float* core_y, float* maximum_speed,
    int* grounded, int* selected_drag_node)
{
    if (dango_self_has_crossing(body))
    {
        return -86;
    }
    b2Vec2 core = b2Body_GetPosition(body->core_body_id);
    b2Vec2 core_velocity = b2Body_GetLinearVelocity(body->core_body_id);
    *point_count = body->node_count;
    *ground_contact_count = 0;
    *signed_area = 0.0f;
    *centroid_x = 0.0f;
    *centroid_y = 0.0f;
    *maximum_speed = sqrtf(core_velocity.x * core_velocity.x + core_velocity.y * core_velocity.y);
    float lowest = INFINITY;
    for (int index = 0; index < body->node_count; ++index)
    {
        int next = (index + 1) % body->node_count;
        b2Vec2 position = b2Body_GetPosition(body->node_body_ids[index]);
        b2Vec2 next_position = b2Body_GetPosition(body->node_body_ids[next]);
        b2Vec2 velocity = b2Body_GetLinearVelocity(body->node_body_ids[index]);
        float local_x = position.x - core.x;
        float local_y = position.y - core.y;
        float local_length = sqrtf(local_x * local_x + local_y * local_y);
        float next_x = next_position.x - core.x;
        float next_y = next_position.y - core.y;
        float next_length = sqrtf(next_x * next_x + next_y * next_y);
        if (next_length > 0.0001f)
        {
            next_x += config->node_radius * next_x / next_length;
            next_y += config->node_radius * next_y / next_length;
        }
        if (local_length > 0.0001f)
        {
            local_x += config->node_radius * local_x / local_length;
            local_y += config->node_radius * local_y / local_length;
        }
        float speed = sqrtf(velocity.x * velocity.x + velocity.y * velocity.y);
        if (!isfinite(local_x) || !isfinite(local_y) || !isfinite(speed) || speed > config->maximum_speed)
        {
            return -87;
        }
        points[index * 2] = local_x;
        points[index * 2 + 1] = local_y;
        *centroid_x += local_x;
        *centroid_y += local_y;
        *signed_area += local_x * next_y - next_x * local_y;
        *maximum_speed = fmaxf(*maximum_speed, speed);
        lowest = fminf(lowest, position.y - config->node_radius);
        int capacity = b2Body_GetContactCapacity(body->node_body_ids[index]);
        if (capacity > 0)
        {
            b2ContactData contacts[4];
            capacity = capacity < 4 ? capacity : 4;
            *ground_contact_count += b2Body_GetContactData(body->node_body_ids[index], contacts, capacity);
        }
    }
    *signed_area *= 0.5f;
    *centroid_x /= (float)body->node_count;
    *centroid_y /= (float)body->node_count;
    body->grounded = *ground_contact_count > 0 || lowest <= config->ground_height + 0.035f;
#if defined(DANGO_LAB_ENVELOPE)
    b2Vec2 envelope_positions[17]; for(int n=0;n<16;++n)envelope_positions[n]=b2Body_GetPosition(body->node_body_ids[n]); envelope_positions[16]=core;
    dango_envelope_point foot; if(!dango_envelope_geometry(envelope_positions,config->node_radius,&foot))return -87;
    float gap=foot.point.y-config->ground_height; *ground_contact_count=gap<=DANGO_CONSTRAINT_TOLERANCE*body->radial_rest_length ? 1 : 0;
    body->grounded=gap<=.035f; body->envelope_stats[0]=foot.point.x;body->envelope_stats[1]=foot.point.y;body->envelope_stats[2]=gap;
    body->envelope_stats[9]=body->grounded ? 1.0f : 0.0f;body->envelope_stats[10]=(float)*ground_contact_count;body->envelope_stats[28]=foot.maximum_y;body->envelope_stats[31]=1;
#endif
    if (!isfinite(*signed_area) || *signed_area < config->minimum_area)
    {
        return -88;
    }
    *core_x = core.x;
    *core_y = core.y;
    *grounded = body->grounded ? 1 : 0;
    *selected_drag_node = body->selected_drag_node;
    return 0;
}

DANGO_API int dango_box2d_self_soft_body_step(
    void* self_handle,
    float time_step,
    const dango_self_physics_config* config,
    float anchor_x,
    float anchor_y,
    float horizontal_control,
    float downward_control,
    int jump_pressed,
    int drag_active,
    float drag_target_x,
    float drag_target_y,
    float* points,
    int point_capacity,
    int* point_count,
    int* ground_contact_count,
    float* signed_area,
    float* centroid_x,
    float* centroid_y,
    float* core_x,
    float* core_y,
    float* maximum_speed,
    int* grounded,
    int* selected_drag_node)
{
    dango_self_soft_body* body = self_handle;
    if (body == NULL || !dango_self_config_valid(config) || points == NULL || point_count == NULL ||
        ground_contact_count == NULL || signed_area == NULL || centroid_x == NULL || centroid_y == NULL ||
        core_x == NULL || core_y == NULL || maximum_speed == NULL || grounded == NULL || selected_drag_node == NULL)
    {
        return -84;
    }
    if (!b2World_IsValid(body->world_id) || point_capacity < body->node_count ||
        !isfinite(time_step) || time_step <= 0.0f || time_step > 0.1f ||
        !isfinite(anchor_x) || !isfinite(anchor_y) || !isfinite(horizontal_control) ||
        !isfinite(downward_control) || horizontal_control < -1.0f || horizontal_control > 1.0f ||
        downward_control < 0.0f || downward_control > 1.0f || jump_pressed < 0 || jump_pressed > 1 ||
        drag_active < 0 || drag_active > 1 || !isfinite(drag_target_x) || !isfinite(drag_target_y))
    {
        return -85;
    }
    dango_self_initialize_at(body, config, anchor_x, anchor_y);
    int small_steps = (int)ceilf(time_step * 240.0f);
    small_steps = small_steps < body->substep_count ? body->substep_count : small_steps;
    float small_time_step = time_step / (float)small_steps;
    for (int small_step = 0; small_step < small_steps; ++small_step)
    {
        int material_error = dango_self_apply_step_forces(body, config, small_time_step, small_step,
            horizontal_control, downward_control, jump_pressed, drag_active, drag_target_x, drag_target_y);
        if (material_error) return material_error;
        b2World_Step(body->world_id, small_time_step, 1);
        if (!dango_self_solve_constraints(body, config))
        {
            return -91;
        }
        if (!dango_self_limit_speed(body, config->maximum_speed))
        {
            return -87;
        }
    }

    return dango_self_read_frame(body, config, points, point_count, ground_contact_count, signed_area,
        centroid_x, centroid_y, core_x, core_y, maximum_speed, grounded, selected_drag_node);
}

DANGO_API int dango_box2d_self_soft_body_destroy(void* self_handle, int* outstanding_bytes)
{
    dango_self_soft_body* body = self_handle;
    if (body == NULL || outstanding_bytes == NULL)
    {
        return -89;
    }
    int initial_bytes = body->initial_box2d_bytes;
    dango_destroy_self_internal(body);
    *outstanding_bytes = b2GetByteCount() - initial_bytes;
    return *outstanding_bytes == 0 ? 0 : -90;
}

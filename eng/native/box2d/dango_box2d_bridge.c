#include <box2d/box2d.h>
#include <math.h>
#include <stdlib.h>

#if defined(_WIN32)
#define DANGO_API __declspec(dllexport)
#else
#define DANGO_API __attribute__((visibility("default")))
#endif

#define DANGO_BOX2D_ABI_VERSION 5
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

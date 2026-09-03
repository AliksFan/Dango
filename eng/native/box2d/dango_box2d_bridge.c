#include <box2d/box2d.h>
#include <math.h>
#include <stdlib.h>

#if defined(_WIN32)
#define DANGO_API __declspec(dllexport)
#else
#define DANGO_API __attribute__((visibility("default")))
#endif

#define DANGO_BOX2D_ABI_VERSION 1
#define DANGO_SOFT_BODY_MAX_NODES 16
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
    int node_count;
    int substep_count;
    int initial_box2d_bytes;
} dango_soft_body;

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

DANGO_API int dango_box2d_soft_body_create(
    int node_count,
    int substep_count,
    void** soft_body_handle)
{
    if (soft_body_handle == NULL)
    {
        return -10;
    }
    *soft_body_handle = NULL;
    if (!dango_is_supported_soft_body_configuration(node_count, substep_count))
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
    soft_body->node_count = node_count;
    soft_body->substep_count = substep_count;

    b2WorldDef world_def = b2DefaultWorldDef();
    world_def.gravity = (b2Vec2){0.0f, 0.0f};
    soft_body->world_id = b2CreateWorld(&world_def);
    if (!b2World_IsValid(soft_body->world_id))
    {
        dango_destroy_soft_body_internal(soft_body);
        return -13;
    }
    b2World_EnableSleeping(soft_body->world_id, true);

    b2BodyDef core_def = b2DefaultBodyDef();
    core_def.type = b2_kinematicBody;
    core_def.position = (b2Vec2){0.0f, 0.0f};
    soft_body->core_body_id = b2CreateBody(soft_body->world_id, &core_def);
    if (!b2Body_IsValid(soft_body->core_body_id))
    {
        dango_destroy_soft_body_internal(soft_body);
        return -14;
    }

    b2BodyDef ground_def = b2DefaultBodyDef();
    ground_def.type = b2_staticBody;
    ground_def.position = (b2Vec2){0.0f, -0.98f};
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
    node_shape_def.density = 1.0f;
    node_shape_def.material.friction = 0.35f;
    node_shape_def.filter.categoryBits = 0x00000002;
    node_shape_def.filter.maskBits = 0x00000001;
    b2Circle node_circle = {{0.0f, 0.0f}, 0.055f};

    for (int index = 0; index < node_count; ++index)
    {
        float angle = two_pi * (float)index / (float)node_count;
        b2Vec2 position = {0.72f * cosf(angle), 0.82f * sinf(angle)};
        b2BodyDef node_def = b2DefaultBodyDef();
        node_def.type = b2_dynamicBody;
        node_def.position = position;
        node_def.linearDamping = 1.6f;
        node_def.angularDamping = 1.0f;
        node_def.sleepThreshold = 0.025f;
        node_def.fixedRotation = true;
        soft_body->node_body_ids[index] = b2CreateBody(soft_body->world_id, &node_def);
        b2ShapeId node_shape_id = b2CreateCircleShape(
            soft_body->node_body_ids[index],
            &node_shape_def,
            &node_circle);
        if (!b2Body_IsValid(soft_body->node_body_ids[index]) || !b2Shape_IsValid(node_shape_id))
        {
            dango_destroy_soft_body_internal(soft_body);
            return -16;
        }

        float radial_length = sqrtf(position.x * position.x + position.y * position.y);
        b2JointId radial_joint_id = dango_create_spring(
            soft_body->world_id,
            soft_body->core_body_id,
            soft_body->node_body_ids[index],
            radial_length,
            5.5f,
            0.9f);
        if (!b2Joint_IsValid(radial_joint_id))
        {
            dango_destroy_soft_body_internal(soft_body);
            return -17;
        }
    }

    for (int index = 0; index < node_count; ++index)
    {
        int next_index = (index + 1) % node_count;
        b2Vec2 position = b2Body_GetPosition(soft_body->node_body_ids[index]);
        b2Vec2 next_position = b2Body_GetPosition(soft_body->node_body_ids[next_index]);
        float delta_x = next_position.x - position.x;
        float delta_y = next_position.y - position.y;
        float edge_length = sqrtf(delta_x * delta_x + delta_y * delta_y);
        b2JointId edge_joint_id = dango_create_spring(
            soft_body->world_id,
            soft_body->node_body_ids[index],
            soft_body->node_body_ids[next_index],
            edge_length,
            8.0f,
            0.85f);
        if (!b2Joint_IsValid(edge_joint_id))
        {
            dango_destroy_soft_body_internal(soft_body);
            return -18;
        }
    }

    *soft_body_handle = soft_body;
    return 0;
}

DANGO_API int dango_box2d_soft_body_step(
    void* soft_body_handle,
    float time_step,
    int cue,
    float anchor_x,
    float anchor_y,
    float strength,
    float* points,
    int point_capacity,
    int* point_count,
    float* signed_area,
    float* centroid_x,
    float* centroid_y,
    float* maximum_radius,
    float* maximum_speed,
    float* energy_proxy,
    int* sleeping)
{
    dango_soft_body* soft_body = soft_body_handle;
    if (soft_body == NULL || points == NULL || point_count == NULL || signed_area == NULL ||
        centroid_x == NULL || centroid_y == NULL || maximum_radius == NULL ||
        maximum_speed == NULL || energy_proxy == NULL || sleeping == NULL)
    {
        return -20;
    }
    if (!b2World_IsValid(soft_body->world_id) || point_capacity < soft_body->node_count ||
        !isfinite(time_step) || time_step <= 0.0f || time_step > 0.1f ||
        !isfinite(anchor_x) || !isfinite(anchor_y) || !isfinite(strength) ||
        strength < 0.0f || strength > 1.0f || cue < 0 || cue > 2)
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
    if (anchor_y < -0.2f)
    {
        anchor_y = -0.2f;
    }
    else if (anchor_y > 0.2f)
    {
        anchor_y = 0.2f;
    }

    b2Body_SetTransform(soft_body->core_body_id, (b2Vec2){anchor_x, anchor_y}, b2Rot_identity);
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

    b2World_Step(soft_body->world_id, time_step, soft_body->substep_count);

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
    if (!isfinite(area) || area < DANGO_SOFT_BODY_MIN_AREA)
    {
        return -23;
    }
    if (max_radius_value > DANGO_SOFT_BODY_MAX_RADIUS)
    {
        return -24;
    }
    if (max_speed_value > DANGO_SOFT_BODY_MAX_SPEED)
    {
        return -25;
    }

    float center_x = center_x_sum / (float)soft_body->node_count;
    float center_y = center_y_sum / (float)soft_body->node_count;
    float centroid_offset = sqrtf(center_x * center_x + center_y * center_y);
    if (!isfinite(centroid_offset) || centroid_offset > DANGO_SOFT_BODY_MAX_CENTROID_OFFSET)
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

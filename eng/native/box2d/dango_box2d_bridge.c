#include <box2d/box2d.h>

#if defined(_WIN32)
#define DANGO_API __declspec(dllexport)
#else
#define DANGO_API __attribute__((visibility("default")))
#endif

#define DANGO_BOX2D_ABI_VERSION 1

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

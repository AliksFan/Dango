#ifndef DANGO_PHYSICS_MODULE_H
#define DANGO_PHYSICS_MODULE_H

#include "dango_self_physics_internal.h"

#define DANGO_PHYSICS_MODULE_ABI_VERSION 1
#define DANGO_PHYSICS_MAX_BODIES 3
#define DANGO_PHYSICS_CONTROL_FLOATS 6
#define DANGO_PHYSICS_STATE_FLOATS 42

DANGO_API int dango_physics_get_abi_version(void);
DANGO_API int dango_physics_world_create(
    const dango_self_physics_config* config,
    int capacity,
    void** handle);
DANGO_API int dango_physics_world_add(
    void* handle,
    float anchor_x,
    float anchor_y,
    int* entity_id);
DANGO_API int dango_physics_world_remove(void* handle, int entity_id);
DANGO_API int dango_physics_world_step(
    void* handle,
    float time_step,
    const int* entity_ids,
    const float* controls,
    int control_count,
    int* state_ids,
    float* states,
    int state_capacity,
    int* state_count,
    int* contact_steps,
    int* latched_error);
DANGO_API int dango_physics_world_reset(void* handle);
DANGO_API int dango_physics_world_set_preview_jump_speed_scale(void* handle, float scale);
DANGO_API int dango_physics_world_set_horizontal_bounds(void* handle, float left, float right);
DANGO_API int dango_physics_world_clear_horizontal_bounds(void* handle);
DANGO_API int dango_physics_world_translate_horizontal(void* handle, float translation);
DANGO_API int dango_physics_world_destroy(void* handle);

#endif

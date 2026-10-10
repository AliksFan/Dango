#ifndef DANGO_PHYSICS_MODULE_H
#define DANGO_PHYSICS_MODULE_H

#include "dango_self_physics_internal.h"
#include "dango_round_ball_types.h"
#include "dango_authority_types.h"
#include "dango_prescribed_types.h"

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
// Optional ABI-1 capability. A single static rectangle with one active Dango.
DANGO_API int dango_physics_world_set_static_platform(void* handle,
    float center_x,float center_y,float width,float height,float friction);
DANGO_API int dango_physics_world_clear_static_platform(void* handle);
// Six floats: enabled, touching, supported, outward normal X/Y, signed gap.
DANGO_API int dango_physics_world_get_static_platform_contact(void* handle,int entity_id,float* values,int capacity);
DANGO_API int dango_physics_get_allocated_bytes(void);

// Additive semantic authority ABI. Explicit enable must precede stepping.
DANGO_API int dango_physics_get_authority_abi_version(void);
DANGO_API int dango_physics_get_authority_frame_size(void);
DANGO_API int dango_physics_get_authority_capabilities(void);
DANGO_API int dango_physics_world_enable_authority(void* handle);
DANGO_API int dango_physics_world_get_authority_frame(void* handle,dango_authority_frame* frame,int frame_size);
// Complete solver recovery is intentionally unavailable in this candidate.
DANGO_API int dango_physics_world_export_checkpoint(void* handle,void* buffer,int capacity,int* written);
DANGO_API int dango_physics_world_import_checkpoint(void* handle,const void* buffer,int size);
DANGO_API int dango_physics_get_presentation_target_size(void);
DANGO_API int dango_physics_world_apply_presentation_targets(void* handle,
    const dango_presentation_target* targets,int count,int target_size);
DANGO_API int dango_physics_get_prescribed_abi_version(void);
DANGO_API int dango_physics_get_prescribed_step_size(void);
DANGO_API int dango_physics_get_prescribed_trace_size(void);
DANGO_API int dango_physics_world_bind_prescribed_epoch(void* handle,uint64_t epoch);
DANGO_API int dango_physics_world_step_prescribed(void* handle,const dango_prescribed_step* input,int input_size,
    int* state_ids,float* states,int capacity,int* state_count,int* contact_steps,dango_prescribed_trace* trace,int trace_size);

// Optional round-ball ABI. Get checks exact frame_size before any output write.
DANGO_API int dango_physics_get_round_ball_abi_version(void);
DANGO_API int dango_physics_get_round_ball_frame_size(void);
// Optional official-ball material capability; existing frame/definition stay ABI-1.
DANGO_API double dango_physics_get_round_ball_gravity_scale(void);
DANGO_API int dango_physics_world_add_round_ball(void* handle,const dango_ball_definition* definition,int* ball_id);
DANGO_API int dango_physics_world_remove_round_ball(void* handle,int ball_id);
DANGO_API int dango_physics_world_get_round_ball_frame(void* handle,dango_round_ball_frame* frame,int frame_size);

#endif

#ifndef DANGO_AUTHORITY_TYPES_H
#define DANGO_AUTHORITY_TYPES_H

#include <stdint.h>

#define DANGO_AUTHORITY_ABI_VERSION 1
#define DANGO_AUTHORITY_SEMANTIC_FRAMES 1
#define DANGO_AUTHORITY_COMPLETE_CHECKPOINT 2
#define DANGO_AUTHORITY_UNSUPPORTED -420
#define DANGO_AUTHORITY_MODE_CONFLICT -421
#define DANGO_AUTHORITY_INVALID_OUTPUT -422

enum dango_authority_body_flags
{
    DANGO_AUTHORITY_FLOOR_GROUNDED=1,
    DANGO_AUTHORITY_DANGO_SUPPORTED=2,
    DANGO_AUTHORITY_CAN_JUMP=4
};
enum dango_authority_jump_result
{
    DANGO_AUTHORITY_JUMP_NONE=0,
    DANGO_AUTHORITY_JUMP_APPLIED_FLOOR=1,
    DANGO_AUTHORITY_JUMP_APPLIED_SUPPORT=2,
    DANGO_AUTHORITY_JUMP_REJECTED_AIRBORNE=3,
    DANGO_AUTHORITY_JUMP_REJECTED_COOLDOWN=4
};
enum dango_authority_pair_flags
{
    DANGO_AUTHORITY_PAIR_TOUCHING=1,
    DANGO_AUTHORITY_PAIR_SUPPORTS_A=2,
    DANGO_AUTHORITY_PAIR_SUPPORTS_B=4
};

/* Positions, gaps and velocities retain the original p coordinate system.
   This optional semantic frame never contains contours, native IDs or pointers. */
typedef struct dango_authority_body
{
    int32_t entity_id,support_entity_id;
    uint32_t flags,jump_result;
    float core_x,core_y,core_vx,core_vy;
    float support_nx,support_ny,support_gap,cooldown_remaining;
} dango_authority_body;

typedef struct dango_authority_pair
{
    int32_t entity_a,entity_b;
    uint32_t flags,reserved;
    float nx,ny,gap,minimum_projected_width;
} dango_authority_pair;

typedef struct dango_authority_frame
{
    uint32_t abi_version,frame_size;
    uint64_t tick,scene_revision;
    int32_t error_code,body_count,pair_count,reserved;
    dango_authority_body bodies[3];
    dango_authority_pair pairs[3];
} dango_authority_frame;

/* Local presentation targets are a separate, mutually exclusive world role.
   No input, support verdict, body contour or player identity is accepted. */
typedef struct dango_presentation_target
{
    int32_t entity_id;
    float core_x,core_y,core_vx,core_vy;
} dango_presentation_target;

#endif

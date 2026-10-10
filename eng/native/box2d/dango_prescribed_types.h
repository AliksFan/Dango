#ifndef DANGO_PRESCRIBED_TYPES_H
#define DANGO_PRESCRIBED_TYPES_H
#include "dango_authority_types.h"
#define DANGO_PRESCRIBED_ABI_VERSION 1
#define DANGO_PRESCRIBED_MAX_SAMPLES 97
typedef struct dango_prescribed_body
{
    int32_t entity_id;
    uint32_t start_floor,end_floor;
    int32_t start_support,end_support,reserved;
    float start_x,start_y,start_vx,start_vy,end_x,end_y,end_vx,end_vy;
} dango_prescribed_body;
typedef struct dango_prescribed_step
{
    uint32_t abi_version,frame_size;
    uint64_t epoch,ordinal;
    double start_time,end_time;
    uint64_t start_revision,end_revision;
    int32_t body_count,start_pair_count,end_pair_count,reserved;
    dango_prescribed_body bodies[2];
    dango_authority_pair start_pairs[1],end_pairs[1];
} dango_prescribed_step;
typedef struct dango_prescribed_sample
{
    float fraction,actual_x,actual_y,target_x,target_y;
} dango_prescribed_sample;
typedef struct dango_prescribed_trace
{
    uint32_t abi_version,frame_size;
    uint64_t epoch,ordinal;
    int32_t error_code,body_count,substep_count,sample_count,entity_ids[2];
    float maximum_core_errors[2];
    dango_prescribed_sample samples[2][DANGO_PRESCRIBED_MAX_SAMPLES];
} dango_prescribed_trace;
#endif

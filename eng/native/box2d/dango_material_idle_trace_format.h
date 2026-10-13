#ifndef DANGO_MATERIAL_IDLE_TRACE_FORMAT_H
#define DANGO_MATERIAL_IDLE_TRACE_FORMAT_H
#include <stdint.h>

// Private, little-endian x64 observation file. No public ABI changes.
#define DANGO_IT_FRAMES 6301
#define DANGO_IT_STEPS 100816
#define DANGO_IT_LIMIT (UINT64_C(512) * 1024 * 1024)
#define DANGO_IT_COSTS 12
#define DANGO_IT_STAGES 8
enum { DANGO_IT_STEP = 1, DANGO_IT_FRAME = 2, DANGO_IT_WINDOW = 3, DANGO_IT_END = 4, DANGO_IT_OFF_STEP = 5 };
// states: begin, before Box2D, adapter input, positioned, damped,
//         contact float, cap output, actual writeback. Components x,y,vx,vy.
// costs: position iterations, linear solves, full checks, float checks, modes,
//        pivot searches/eliminations, certificate calls/coordinate/coefficient/residual/row visits.
#pragma pack(push, 1)
typedef struct dango_it_header {
    char magic[8];
    uint32_t schema, enabled, header_bytes, step_bytes, mode_bytes, frame_bytes, diagnostic_bytes, config_bytes;
    uint64_t maximum_bytes;
    float masses[17], initial[17][4];
    unsigned char config[164];
} dango_it_header;
typedef struct dango_it_prefix { uint32_t kind, bytes; } dango_it_prefix;
typedef struct dango_it_mode {
    uint32_t index;
    int32_t status;
    uint32_t flags; // 1 system built,2 solve passed,4 factor available,8 selection available,16 used,32 selection passed
    uint8_t rank, nullity, dimension, active_count;
    double alpha; // missing iff !(flags&8); never infer alpha from zero storage
    uint64_t mode_bits; // four bits per active column
} dango_it_mode;
typedef struct dango_it_cost {
    uint64_t sequence;
    uint32_t frame, substep;
    int32_t phase, error;
    uint64_t costs[DANGO_IT_COSTS];
} dango_it_cost;
typedef struct dango_it_receipt { int32_t node; double friction, normal, tangent, total_normal; } dango_it_receipt;
typedef struct dango_it_step {
    dango_it_cost meta;
    uint32_t stage_mask, receipt_count, event_count, active_count;
    float dt, cap_scale;
    int32_t active_ids[16];
    float masses[17], states[DANGO_IT_STAGES][17][4];
    double contact_velocity[17][2];
    dango_it_receipt receipts[16];
    // Variable event_count*dango_it_mode bytes follow, at most32.
} dango_it_step;
typedef struct dango_it_frame {
    uint32_t frame, published;
    int32_t error, contacts;
    float row[42];
} dango_it_frame;
typedef struct dango_it_window { uint64_t sequence; uint32_t frame, substep, reason, diagnostic_bytes; } dango_it_window;
typedef struct dango_it_end {
    uint64_t steps, frames, bytes, windows;
    int32_t error;
    uint32_t enabled;
    uint64_t costs[DANGO_IT_COSTS];
    // stage0: previous actual end -> begin; stages1..7: adjacent stored states.
    double displacement[8][17][2], absolute_displacement[8][17][2];
    float final[17][4];
} dango_it_end;
typedef struct dango_it_layout {
    uint32_t schema, header_bytes, step_bytes, mode_bytes, frame_bytes, diagnostic_bytes, config_bytes, cost_bytes;
    uint64_t worst_case_bytes;
    uint32_t input_offset, positioned_offset, damped_offset, uncapped_offset, output_offset;
    uint32_t positions_offset, velocities_offset, inverse_mass_offset;
} dango_it_layout;
#pragma pack(pop)
_Static_assert(sizeof(dango_it_mode) == 32, "compact mode record");
#endif

#ifndef DANGO_RECEIPT_HISTORY_CAPTURE_H
#define DANGO_RECEIPT_HISTORY_CAPTURE_H
#include <box2d/box2d.h>
#include <stdint.h>

// Candidate-only, per-world live provenance. No userData or process singleton.
typedef struct dango_rh_point
{
    int contact, point, point_id, node, shape_a, shape_b, body_a, body_b;
    uint16_t shape_gen_a, shape_gen_b, body_gen_a, body_gen_b;
    uint64_t sequence, step;
    float nx, ny, friction, friction_n, friction_t, rest_n, rest_t, delta_n, delta_t, stored_n, stored_t;
    int stage, delta_count;
    bool invalid;
} dango_rh_point;
typedef struct dango_rh_capture
{
    b2WorldId world;
    uint64_t sequence, step;
    b2ShapeId ground, nodes[16];
    b2BodyId bodies[16];
    dango_rh_point points[16];
    bool bound, invalid;
} dango_rh_capture;
typedef struct b2World b2World;
typedef struct b2ContactSim b2ContactSim;
bool dango_rh_begin(dango_rh_capture*, b2WorldId, b2ShapeId, const b2ShapeId[16], const b2BodyId[16]);
dango_rh_capture* dango_rh_get(b2WorldId);
void dango_rh_end(dango_rh_capture*);
void dango_rh_event(b2World*, const b2ContactSim*, int, int, float, float, float, float, float, float, float);
bool dango_rh_live(const dango_rh_capture*, const dango_rh_point*);
#endif

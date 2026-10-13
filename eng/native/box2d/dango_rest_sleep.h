#ifndef DANGO_REST_SLEEP_H
#define DANGO_REST_SLEEP_H

// Idle rest sleep for contact-connected groups of soft bodies.
// See docs/planning/2026-10-13-idle-rest-sleep-design.md.
//
// A group of bodies whose display bounds are within DANGO_REST_MARGIN of each
// other rests only when every member has been quiet for DANGO_REST_FRAMES
// frames. A resting group is frozen: its Box2D island sleeps and every custom
// force, constraint, contact and damping pass skips it, so its state stays
// bit-identical. Any interaction wakes the whole group before the next substep.

#include <math.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>
#include "box2d/box2d.h"
#include "dango_self_physics_internal.h"
#include "dango_physics_module.h"

#define DANGO_REST_FRAMES 30          /* 0.5 s at 60 Hz */
#define DANGO_REST_SPEED 0.05f        /* world/s, about 5 px/s; only guards fast oscillation. Contact-cancelled gravity
                                         leaves up to ~0.02 world/s on grounded nodes with no displacement. */
#define DANGO_REST_DRIFT 0.002f       /* world, about 0.2 px over the quiet window */
#define DANGO_REST_MARGIN 0.15f       /* world; far above one substep of travel at maximum speed */
#define DANGO_REST_POINTS 17

typedef struct dango_rest_slot
{
    bool resting;
    int quiet_frames;
    double rest_seconds;
    b2Vec2 reference[DANGO_REST_POINTS];
} dango_rest_slot;

typedef struct dango_rest_state
{
    dango_rest_slot slot[DANGO_PHYSICS_MAX_BODIES];
    bool wake_all;
    uint64_t entries, wakes;
} dango_rest_state;

static inline b2BodyId dango_rest_point(const dango_self_soft_body* body, int n)
{
    return n < 16 ? body->node_body_ids[n] : body->core_body_id;
}

static inline bool dango_rest_is(const dango_rest_state* s, int r) { return s->slot[r].resting; }

static inline bool dango_rest_all(const dango_rest_state* s, const int* active, int capacity)
{
    bool any = false;
    for (int r = 0; r < capacity; ++r)
    {
        if (!active[r]) continue;
        if (!s->slot[r].resting) return false;
        any = true;
    }
    return any;
}

static inline void dango_rest_clear(dango_rest_state* s, int r)
{
    memset(&s->slot[r], 0, sizeof s->slot[r]);
}

static inline void dango_rest_capture(dango_rest_slot* slot, const dango_self_soft_body* body)
{
    for (int n = 0; n < DANGO_REST_POINTS; ++n) slot->reference[n] = b2Body_GetPosition(dango_rest_point(body, n));
}

static inline void dango_rest_wake(dango_rest_state* s, dango_self_soft_body* body, int r)
{
    dango_rest_slot* slot = &s->slot[r];
    if (slot->resting)
    {
        b2Body_SetAwake(body->core_body_id, true);
        // Rest freezes the body clock; credit it so wake never delays a jump.
        body->seconds_since_jump += (float)slot->rest_seconds;
        ++s->wakes;
    }
    dango_rest_clear(s, r);
    dango_rest_capture(slot, body);
}

static inline void dango_rest_bounds(const dango_self_soft_body* body, float radius, float* box)
{
    box[0] = box[1] = INFINITY; box[2] = box[3] = -INFINITY;
    for (int n = 0; n < 16; ++n)
    {
        b2Vec2 p = b2Body_GetPosition(body->node_body_ids[n]);
        box[0] = fminf(box[0], p.x - radius); box[1] = fminf(box[1], p.y - radius);
        box[2] = fmaxf(box[2], p.x + radius); box[3] = fmaxf(box[3], p.y + radius);
    }
}

static inline bool dango_rest_near(const float* a, const float* b)
{
    return a[0] - DANGO_REST_MARGIN <= b[2] && b[0] - DANGO_REST_MARGIN <= a[2] &&
        a[1] - DANGO_REST_MARGIN <= b[3] && b[1] - DANGO_REST_MARGIN <= a[3];
}

// Called before the first substep (controls != NULL) and before every substep
// (controls == NULL). near_obstacle[r] marks a dynamic non-dango object such as
// a ball that can reach body r; it always wakes body r.
static inline void dango_rest_check_wake(dango_rest_state* s, dango_self_soft_body* const* bodies,
    const int* active, int capacity, const float* controls, const bool* near_obstacle, float radius)
{
    for (int r = 0; r < capacity; ++r)
    {
        if (!active[r] || !s->slot[r].resting) continue;
        const float* c = controls ? controls + 6 * r : NULL;
        bool input = c && (c[0] != 0 || c[1] != 0 || c[2] != 0 || c[3] != 0);
        bool box2d_woke = b2Body_IsAwake(bodies[r]->core_body_id);
        if (s->wake_all || input || (near_obstacle && near_obstacle[r]) || box2d_woke) dango_rest_wake(s, bodies[r], r);
    }
    s->wake_all = false;
    // A resting body near an awake body joins its group: wake transitively.
    float box[DANGO_PHYSICS_MAX_BODIES][4];
    for (int r = 0; r < capacity; ++r) if (active[r]) dango_rest_bounds(bodies[r], radius, box[r]);
    for (bool changed = true; changed;)
    {
        changed = false;
        for (int a = 0; a < capacity; ++a)
        {
            if (!active[a] || s->slot[a].resting) continue;
            for (int b = 0; b < capacity; ++b)
                if (b != a && active[b] && s->slot[b].resting && dango_rest_near(box[a], box[b]))
                { dango_rest_wake(s, bodies[b], b); changed = true; }
        }
    }
}

// Called after a successful frame. supported[r] means grounded or standing on
// a platform or another body; near_obstacle[r] blocks rest beside balls.
static inline void dango_rest_frame_end(dango_rest_state* s, dango_self_soft_body* const* bodies,
    const int* active, int capacity, const float* controls, const bool* supported, const bool* near_obstacle,
    float radius, float dt)
{
    bool candidate[DANGO_PHYSICS_MAX_BODIES] = {false};
    for (int r = 0; r < capacity; ++r)
    {
        if (!active[r]) continue;
        dango_rest_slot* slot = &s->slot[r];
        if (slot->resting) { slot->rest_seconds += dt; continue; }
        const float* c = controls + 6 * r;
        bool quiet = c[0] == 0 && c[1] == 0 && c[2] == 0 && c[3] == 0 && supported[r] && !near_obstacle[r];
        for (int n = 0; quiet && n < DANGO_REST_POINTS; ++n)
        {
            b2BodyId id = dango_rest_point(bodies[r], n);
            b2Vec2 p = b2Body_GetPosition(id), v = b2Body_GetLinearVelocity(id);
            if (b2Length(v) >= DANGO_REST_SPEED || b2Distance(p, slot->reference[n]) >= DANGO_REST_DRIFT) quiet = false;
        }
        if (!quiet) { slot->quiet_frames = 0; dango_rest_capture(slot, bodies[r]); continue; }
        if (++slot->quiet_frames >= DANGO_REST_FRAMES) candidate[r] = true;
    }
    // Group by proximity; a group rests only when all its members are candidates.
    float box[DANGO_PHYSICS_MAX_BODIES][4];
    int group[DANGO_PHYSICS_MAX_BODIES];
    for (int r = 0; r < capacity; ++r) { group[r] = r; if (active[r]) dango_rest_bounds(bodies[r], radius, box[r]); }
    for (bool changed = true; changed;)
    {
        changed = false;
        for (int a = 0; a < capacity; ++a) for (int b = a + 1; b < capacity; ++b)
            if (active[a] && active[b] && group[a] != group[b] && dango_rest_near(box[a], box[b]))
            {
                int to = group[a] < group[b] ? group[a] : group[b];
                int from = group[a] < group[b] ? group[b] : group[a];
                for (int r = 0; r < capacity; ++r) if (group[r] == from) group[r] = to;
                changed = true;
            }
    }
    for (int g = 0; g < capacity; ++g)
    {
        bool members = false, ready = true;
        for (int r = 0; r < capacity; ++r)
            if (active[r] && group[r] == g) { members = true; if (!candidate[r] && !s->slot[r].resting) ready = false; }
        if (!members || !ready) continue;
        for (int r = 0; r < capacity; ++r)
        {
            if (!active[r] || group[r] != g || s->slot[r].resting) continue;
            for (int n = 0; n < DANGO_REST_POINTS; ++n)
            {
                b2Body_SetLinearVelocity(dango_rest_point(bodies[r], n), (b2Vec2){0.0f, 0.0f});
                b2Body_SetAngularVelocity(dango_rest_point(bodies[r], n), 0.0f);
            }
            b2Body_SetAwake(bodies[r]->core_body_id, false);
            bool asleep = !b2Body_IsAwake(bodies[r]->core_body_id);
            for (int n = 0; asleep && n < 16; ++n) if (b2Body_IsAwake(bodies[r]->node_body_ids[n])) asleep = false;
            if (!asleep) { dango_rest_wake(s, bodies[r], r); continue; }
            s->slot[r].resting = true; s->slot[r].rest_seconds = 0; ++s->entries;
        }
    }
}

#endif

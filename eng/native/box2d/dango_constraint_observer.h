#ifndef DANGO_CONSTRAINT_OBSERVER_H
#define DANGO_CONSTRAINT_OBSERVER_H

#include <box2d/box2d.h>

// Diagnostic copies only. This header does not own solver state or a callback.
enum dango_constraint_observer_kind
{
    DANGO_OBSERVER_UNKNOWN = 0,
    DANGO_OBSERVER_RADIAL = 1,
    DANGO_OBSERVER_PERIMETER = 2,
    DANGO_OBSERVER_RAW_ANGLE = 3,
    DANGO_OBSERVER_DISPLAY_ANGLE = 4,
    DANGO_OBSERVER_GROUND = 5
};
enum dango_constraint_observer_bound
{
    DANGO_OBSERVER_LOWER = 1,
    DANGO_OBSERVER_UPPER = 2
};
enum dango_constraint_observer_event_kind
{
    DANGO_OBSERVER_EVALUATION = 0,
    DANGO_OBSERVER_CORRECTION = 1,
    DANGO_OBSERVER_INVALID_GEOMETRY = 2,
    DANGO_OBSERVER_BLOCKED_DENOMINATOR = 3
};

typedef struct dango_constraint_observation
{
    int kind, node, bound, event_kind;
    int mode, count, ids[4];
    bool correct, value_available, gradient_available, solve_available, multipliers_available;
    bool touching, violated, clipped, position_applied, velocity_applied;
    bool ground_direct_clamp;
    float measured, bound_value, signed_gap, error, touch_tolerance;
    float denominator, rate, raw_lambda, lambda, impulse, maximum_move;
    b2Vec2 gradients[4];
    float inverse_mass[4];
    b2Vec2 positions_before[4], positions_after[4];
    b2Vec2 velocities_before[4], velocities_after[4];
} dango_constraint_observation;

#endif

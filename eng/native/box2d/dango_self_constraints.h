#ifndef DANGO_SELF_CONSTRAINTS_H
#define DANGO_SELF_CONSTRAINTS_H

#include <box2d/box2d.h>
#include <math.h>
#if defined(DANGO_CONSTRAINT_TEST_DIAGNOSTICS)
#include <stdio.h>
#endif

#define DANGO_CONSTRAINT_NODES 16
#define DANGO_CONSTRAINT_CORE 16
#define DANGO_CONSTRAINT_TOLERANCE 0.00001f

#if defined(DANGO_JOINT_BASELINE_COUNTERS)
static unsigned long long dango_constraint_sweep_count;
static unsigned long long dango_constraint_row_visit_count;
#endif

typedef struct dango_constraint_state
{
    b2Vec2 positions[17];
    b2Vec2 velocities[17];
    float inverse_mass[17];
    float radial_length;
    float perimeter_length;
    float radial_min;
    float radial_max;
    float perimeter_min;
    float perimeter_max;
    float radius;
    float ground;
#if defined(DANGO_LAB_ENVELOPE)
    float envelope_friction, envelope_normal, envelope_tangent_used;
    float* envelope_stats;
#endif
#if defined(DANGO_LAB_ARCS)
    float arc_normal[32],arc_tangent[32];
    float* arc_stats;
#endif
} dango_constraint_state;

typedef enum dango_constraint_correction_mode
{
    DANGO_CONSTRAINT_CORRECT_BOTH = 0,
    DANGO_CONSTRAINT_CORRECT_POSITION = 1,
    DANGO_CONSTRAINT_CORRECT_VELOCITY = 2
} dango_constraint_correction_mode;

#if defined(DANGO_LAB_ENVELOPE)
#include "dango_lab_envelope.h"
#endif

#if defined(DANGO_CONSTRAINT_OBSERVER)
#include "dango_constraint_observer.h"
#ifndef DANGO_CONSTRAINT_OBSERVER_EMIT
#error Define DANGO_CONSTRAINT_OBSERVER_EMIT to a read-only diagnostic callback.
#endif
#ifndef DANGO_CONSTRAINT_OBSERVER_ENABLED
#define DANGO_CONSTRAINT_OBSERVER_ENABLED() true
#endif

static void dango_observer_identity(dango_constraint_observation* event, const int* ids, int count)
{
    event->kind = DANGO_OBSERVER_UNKNOWN; event->node = -1;
    if (count == 2 && (ids[0] == 16 || ids[1] == 16))
    {
        event->kind = DANGO_OBSERVER_RADIAL; event->node = ids[0] == 16 ? ids[1] : ids[0];
    }
    else if (count == 2 && ids[1] == (ids[0] + 1) % 16)
    {
        event->kind = DANGO_OBSERVER_PERIMETER; event->node = ids[0];
    }
    else if ((count == 3 || count == 4) && ids[0] == (ids[1] + 15) % 16 &&
        ids[2] == (ids[1] + 1) % 16 && (count == 3 || ids[3] == 16))
    {
        event->kind = count == 3 ? DANGO_OBSERVER_RAW_ANGLE : DANGO_OBSERVER_DISPLAY_ANGLE;
        event->node = ids[1];
    }
}

static dango_constraint_observation dango_observer_capture(const dango_constraint_state* s,
    const int* ids, int count, bool correct, dango_constraint_correction_mode mode)
{
    dango_constraint_observation event = {0};
    event.count = count; event.correct = correct; event.mode = (int)mode;
    dango_observer_identity(&event, ids, count);
    for (int i = 0; i < count && i < 4; ++i)
    {
        event.ids[i] = ids[i]; event.inverse_mass[i] = s->inverse_mass[ids[i]];
        event.positions_before[i] = event.positions_after[i] = s->positions[ids[i]];
        event.velocities_before[i] = event.velocities_after[i] = s->velocities[ids[i]];
    }
    return event;
}

static void dango_observer_after(dango_constraint_observation* event, const dango_constraint_state* s)
{
    for (int i = 0; i < event->count && i < 4; ++i)
    {
        event->positions_after[i] = s->positions[event->ids[i]];
        event->velocities_after[i] = s->velocities[event->ids[i]];
    }
}

static void dango_observer_boundaries(const dango_constraint_state* s, int kind, int node,
    const int* ids, int count, float measured, float minimum, float maximum,
    bool correct, dango_constraint_correction_mode mode, bool valid)
{
    if (!DANGO_CONSTRAINT_OBSERVER_ENABLED()) return;
    for (int bound = DANGO_OBSERVER_LOWER; bound <= DANGO_OBSERVER_UPPER; ++bound)
    {
        dango_constraint_observation event = dango_observer_capture(s, ids, count, correct, mode);
        event.kind = kind; event.node = node; event.bound = bound;
        event.event_kind = valid ? DANGO_OBSERVER_EVALUATION : DANGO_OBSERVER_INVALID_GEOMETRY;
        event.value_available = valid; event.measured = measured;
        event.bound_value = bound == DANGO_OBSERVER_LOWER ? minimum : maximum;
        event.signed_gap = measured - event.bound_value;
        event.error = bound == DANGO_OBSERVER_LOWER ? fminf(0.0f, event.signed_gap) : fmaxf(0.0f, event.signed_gap);
        event.touch_tolerance = (kind == DANGO_OBSERVER_RAW_ANGLE || kind == DANGO_OBSERVER_DISPLAY_ANGLE) ?
            DANGO_CONSTRAINT_TOLERANCE : DANGO_CONSTRAINT_TOLERANCE * s->radial_length;
        event.touching = valid && fabsf(event.signed_gap) <= event.touch_tolerance;
        event.violated = valid && event.error != 0.0f;
        DANGO_CONSTRAINT_OBSERVER_EMIT(&event);
    }
}
#endif

// Internal corrections are inverse-mass weighted; velocity projection only
// removes constraint-closing motion, never converts position error to energy.
static void dango_constraint_correct_mode(dango_constraint_state* s, const int* ids,
    const b2Vec2* gradients, int count, float error, bool lower, bool correct,
    dango_constraint_correction_mode mode)
{
    if (!correct)
    {
        return;
    }
#if defined(DANGO_CONSTRAINT_OBSERVER)
    bool observing = DANGO_CONSTRAINT_OBSERVER_ENABLED();
    dango_constraint_observation event;
    if (observing)
    {
        event = dango_observer_capture(s, ids, count, correct, mode);
        event.bound = lower ? DANGO_OBSERVER_LOWER : DANGO_OBSERVER_UPPER;
        event.event_kind = DANGO_OBSERVER_CORRECTION; event.error = error;
        event.violated = error != 0.0f; event.gradient_available = true;
        for (int i = 0; i < count && i < 4; ++i) event.gradients[i] = gradients[i];
    }
#endif
    float denominator = 0.0f;
    float rate = 0.0f;
    for (int i = 0; i < count; ++i)
    {
        denominator += s->inverse_mass[ids[i]] * b2Dot(gradients[i], gradients[i]);
        rate += b2Dot(gradients[i], s->velocities[ids[i]]);
    }
    if (denominator < 0.000001f)
    {
#if defined(DANGO_CONSTRAINT_OBSERVER)
        if (observing)
        {
            event.denominator = denominator; event.rate = rate; event.solve_available = true;
            event.event_kind = DANGO_OBSERVER_BLOCKED_DENOMINATOR;
            DANGO_CONSTRAINT_OBSERVER_EMIT(&event);
        }
#endif
        return;
    }
    float lambda = mode == DANGO_CONSTRAINT_CORRECT_VELOCITY ? 0.0f : -error / denominator;
#if defined(DANGO_CONSTRAINT_OBSERVER)
    if (observing) event.raw_lambda = lambda;
#endif
    // Each sweep is bounded; later sweeps re-evaluate nonlinear gradients.
    float maximum_move = 0.0f;
    for (int i = 0; i < count; ++i)
    {
        maximum_move = fmaxf(maximum_move,
            fabsf(lambda) * s->inverse_mass[ids[i]] * b2Length(gradients[i]));
    }
    if (maximum_move > 0.02f * s->radial_length)
    {
        lambda *= 0.02f * s->radial_length / maximum_move;
#if defined(DANGO_CONSTRAINT_OBSERVER)
        if (observing) event.clipped = true;
#endif
    }
    float impulse = mode == DANGO_CONSTRAINT_CORRECT_POSITION ? 0.0f :
        ((lower ? rate < 0.0f : rate > 0.0f) ? -rate / denominator : 0.0f);
    for (int i = 0; i < count; ++i)
    {
        int id = ids[i];
        if (mode != DANGO_CONSTRAINT_CORRECT_VELOCITY)
        {
            s->positions[id] = b2MulAdd(s->positions[id], lambda * s->inverse_mass[id], gradients[i]);
        }
        if (mode != DANGO_CONSTRAINT_CORRECT_POSITION)
        {
            s->velocities[id] = b2MulAdd(s->velocities[id], impulse * s->inverse_mass[id], gradients[i]);
        }
    }
#if defined(DANGO_CONSTRAINT_OBSERVER)
    if (observing)
    {
        event.denominator = denominator; event.rate = rate; event.lambda = lambda;
        event.impulse = impulse; event.maximum_move = maximum_move;
        event.solve_available = true; event.multipliers_available = true;
        event.position_applied = mode != DANGO_CONSTRAINT_CORRECT_VELOCITY;
        event.velocity_applied = mode != DANGO_CONSTRAINT_CORRECT_POSITION;
        dango_observer_after(&event, s); DANGO_CONSTRAINT_OBSERVER_EMIT(&event);
    }
#endif
}

static void dango_constraint_correct(dango_constraint_state* s, const int* ids,
    const b2Vec2* gradients, int count, float error, bool lower, bool correct)
{
    dango_constraint_correct_mode(s, ids, gradients, count, error, lower, correct,
        DANGO_CONSTRAINT_CORRECT_BOTH);
}

static float dango_constraint_distance_mode(dango_constraint_state* s, int a, int b,
    float minimum, float maximum, bool correct, dango_constraint_correction_mode mode)
{
    b2Vec2 delta = b2Sub(s->positions[b], s->positions[a]);
    float length = b2Length(delta);
#if defined(DANGO_CONSTRAINT_OBSERVER)
    if (DANGO_CONSTRAINT_OBSERVER_ENABLED())
    {
        int observer_ids[2] = {a, b};
        dango_constraint_observation identity = {0}; dango_observer_identity(&identity, observer_ids, 2);
        dango_observer_boundaries(s, identity.kind, identity.node, observer_ids, 2, length, minimum, maximum,
            correct, mode, isfinite(length) && length >= 0.00001f);
    }
#endif
    if (!isfinite(length) || length < 0.00001f)
    {
        return INFINITY;
    }
    float error = length < minimum ? length - minimum : fmaxf(0.0f, length - maximum);
    if (error != 0.0f)
    {
        b2Vec2 direction = b2MulSV(1.0f / length, delta);
        int ids[2] = {a, b};
        b2Vec2 gradients[2] = {b2Neg(direction), direction};
        dango_constraint_correct_mode(s, ids, gradients, 2, error, error < 0.0f, correct, mode);
    }
    return fabsf(error) / s->radial_length;
}

static float dango_constraint_distance(dango_constraint_state* s, int a, int b,
    float minimum, float maximum, bool correct)
{
    return dango_constraint_distance_mode(s, a, b, minimum, maximum, correct,
        DANGO_CONSTRAINT_CORRECT_BOTH);
}

static b2Vec2 dango_constraint_display_point(const dango_constraint_state* s, int id)
{
    b2Vec2 radial = b2Sub(s->positions[id], s->positions[DANGO_CONSTRAINT_CORE]);
    float length = b2Length(radial);
    return length > 0.00001f ? b2MulAdd(s->positions[id], s->radius / length, radial) : s->positions[id];
}

static float dango_constraint_angle_mode(dango_constraint_state* s, int index, bool display, bool correct,
    dango_constraint_correction_mode mode)
{
    int ids[4] = {(index + 15) % 16, index, (index + 1) % 16, DANGO_CONSTRAINT_CORE};
    b2Vec2 p[3];
    for (int i = 0; i < 3; ++i)
    {
        p[i] = display ? dango_constraint_display_point(s, ids[i]) : s->positions[ids[i]];
    }
    b2Vec2 e = b2Sub(p[1], p[0]);
    b2Vec2 f = b2Sub(p[2], p[1]);
    float e2 = b2Dot(e, e);
    float f2 = b2Dot(f, f);
    if (e2 < 1e-10f || f2 < 1e-10f || !isfinite(e2 + f2))
    {
#if defined(DANGO_CONSTRAINT_OBSERVER)
        dango_observer_boundaries(s, display ? DANGO_OBSERVER_DISPLAY_ANGLE : DANGO_OBSERVER_RAW_ANGLE,
            index, ids, display ? 4 : 3, INFINITY, 0.002f, 1.5687963268f, correct, mode, false);
#endif
        return INFINITY;
    }
    float turn = atan2f(b2Cross(e, f), b2Dot(e, f));
    // Safety margin absorbs floating point and output mapping round-off.
    const float minimum = 0.002f;
    const float maximum = 1.5687963268f;
#if defined(DANGO_CONSTRAINT_OBSERVER)
    dango_observer_boundaries(s, display ? DANGO_OBSERVER_DISPLAY_ANGLE : DANGO_OBSERVER_RAW_ANGLE,
        index, ids, display ? 4 : 3, turn, minimum, maximum, correct, mode, true);
#endif
    float error = turn < minimum ? turn - minimum : fmaxf(0.0f, turn - maximum);
    if (error == 0.0f)
    {
        return 0.0f;
    }
    b2Vec2 gradients[4] = {{-e.y / e2, e.x / e2}, {0, 0}, {-f.y / f2, f.x / f2}, {0, 0}};
    gradients[1] = b2Neg(b2Add(gradients[0], gradients[2]));
    if (display)
    {
        for (int i = 0; i < 3; ++i)
        {
            b2Vec2 radial = b2Sub(s->positions[ids[i]], s->positions[DANGO_CONSTRAINT_CORE]);
            float length = b2Length(radial);
            if (length < 0.00001f)
            {
#if defined(DANGO_CONSTRAINT_OBSERVER)
                dango_observer_boundaries(s, DANGO_OBSERVER_DISPLAY_ANGLE, index, ids, 4,
                    turn, minimum, maximum, correct, mode, false);
#endif
                return INFINITY;
            }
            b2Vec2 unit = b2MulSV(1.0f / length, radial);
            b2Vec2 tangent_gradient = b2MulSub(gradients[i], b2Dot(unit, gradients[i]), unit);
            b2Vec2 extra = b2MulSV(s->radius / length, tangent_gradient);
            gradients[i] = b2Add(gradients[i], extra);
            gradients[3] = b2Sub(gradients[3], extra);
        }
    }
    dango_constraint_correct_mode(s, ids, gradients, display ? 4 : 3, error, error < 0.0f, correct, mode);
    return fabsf(error);
}

static float dango_constraint_angle(dango_constraint_state* s, int index, bool display, bool correct)
{
    return dango_constraint_angle_mode(s, index, display, correct, DANGO_CONSTRAINT_CORRECT_BOTH);
}

static float dango_constraint_sweep_order_mode(dango_constraint_state* s, bool correct, bool reverse,
    dango_constraint_correction_mode mode)
{
#if defined(DANGO_LAB_ENVELOPE)
    b2Vec2 before_shape[17]; for(int n=0;n<17;++n)before_shape[n]=s->positions[n];
    float before_energy=dango_envelope_energy(s);
#endif
#if defined(DANGO_JOINT_BASELINE_COUNTERS)
    ++dango_constraint_sweep_count;
#endif
    float residual = 0.0f;
    for (int ordinal = 0; ordinal < 16; ++ordinal)
    {
#if defined(DANGO_JOINT_BASELINE_COUNTERS)
        dango_constraint_row_visit_count += 4;
#endif
        int i = reverse ? 15 - ordinal : ordinal;
        b2Vec2 radial = b2Sub(s->positions[i], s->positions[16]);
        float length = b2Length(radial);
        if (!isfinite(length) || length < 0.00001f)
        {
#if defined(DANGO_CONSTRAINT_OBSERVER)
            if (DANGO_CONSTRAINT_OBSERVER_ENABLED())
            {
                int observer_ids[2] = {16, i};
                dango_observer_boundaries(s, DANGO_OBSERVER_RADIAL, i, observer_ids, 2, length,
                    s->radial_length * s->radial_min, s->radial_length * s->radial_max, correct, mode, false);
            }
#endif
            return INFINITY;
        }
        residual = fmaxf(residual, dango_constraint_distance_mode(s, 16, i,
            s->radial_length * s->radial_min, s->radial_length * s->radial_max, correct, mode));
#if !defined(DANGO_LAB_ARCS)
        residual = fmaxf(residual, dango_constraint_distance_mode(s, i, (i + 1) % 16,
            s->perimeter_length * s->perimeter_min, s->perimeter_length * s->perimeter_max, correct, mode));
        residual = fmaxf(residual, dango_constraint_angle_mode(s, i, false, correct, mode));
        residual = fmaxf(residual, dango_constraint_angle_mode(s, i, true, correct, mode));
#endif
    }
    #if defined(DANGO_LAB_ENVELOPE)
    if(correct && s->envelope_stats) {s->envelope_stats[29]+=dango_envelope_energy(s)-before_energy;
        for(int n=0;n<17;++n)s->envelope_stats[18]=fmaxf(s->envelope_stats[18],b2Distance(before_shape[n],s->positions[n])); }
#if defined(DANGO_LAB_ARCS)
    if(correct && s->arc_stats) {
        s->arc_stats[19]+=dango_envelope_energy(s)-before_energy;
        for(int n=0;n<17;++n)s->arc_stats[13]=fmaxf(s->arc_stats[13],b2Distance(before_shape[n],s->positions[n]));
    }
    residual = fmaxf(residual, dango_arc_contact(s, correct, mode));
#else
    residual = fmaxf(residual, dango_envelope_contact(s, correct, mode));
#endif
    #else
    for (int i = 0; i < 16; ++i)
    {
#if defined(DANGO_JOINT_BASELINE_COUNTERS)
        ++dango_constraint_row_visit_count;
#endif
        float penetration = fmaxf(0.0f, s->ground + s->radius - s->positions[i].y);
        residual = fmaxf(residual, penetration / s->radial_length);
#if defined(DANGO_CONSTRAINT_OBSERVER)
        bool observing_ground = DANGO_CONSTRAINT_OBSERVER_ENABLED();
        bool observed_ground_correction = false;
        dango_constraint_observation ground_event;
        if (observing_ground)
        {
            int observer_ids[1] = {i};
            ground_event = dango_observer_capture(s, observer_ids, 1, correct, mode);
            ground_event.kind = DANGO_OBSERVER_GROUND; ground_event.node = i;
            ground_event.bound = DANGO_OBSERVER_LOWER; ground_event.event_kind = DANGO_OBSERVER_EVALUATION;
            ground_event.value_available = true; ground_event.measured = s->positions[i].y;
            ground_event.bound_value = s->ground + s->radius;
            ground_event.signed_gap = ground_event.measured - ground_event.bound_value;
            ground_event.error = -penetration; ground_event.touch_tolerance = DANGO_CONSTRAINT_TOLERANCE * s->radial_length;
            ground_event.touching = fabsf(ground_event.signed_gap) <= ground_event.touch_tolerance;
            ground_event.violated = penetration > 0.0f; ground_event.gradient_available = true;
            ground_event.gradients[0] = (b2Vec2){0.0f, 1.0f}; ground_event.ground_direct_clamp = true;
            DANGO_CONSTRAINT_OBSERVER_EMIT(&ground_event);
        }
#endif
        if (correct && penetration > 0.0f && mode != DANGO_CONSTRAINT_CORRECT_VELOCITY)
        {
            s->positions[i].y += penetration;
#if defined(DANGO_CONSTRAINT_OBSERVER)
            if (observing_ground) { observed_ground_correction = true; ground_event.position_applied = true; }
#endif
            if (mode == DANGO_CONSTRAINT_CORRECT_BOTH)
            {
                s->velocities[i].y = fmaxf(0.0f, s->velocities[i].y);
#if defined(DANGO_CONSTRAINT_OBSERVER)
                if (observing_ground) ground_event.velocity_applied = true;
#endif
            }
        }
        else if (correct && mode == DANGO_CONSTRAINT_CORRECT_VELOCITY &&
            s->positions[i].y <= s->ground + s->radius + DANGO_CONSTRAINT_TOLERANCE * s->radial_length)
        {
            s->velocities[i].y = fmaxf(0.0f, s->velocities[i].y);
#if defined(DANGO_CONSTRAINT_OBSERVER)
            if (observing_ground) { observed_ground_correction = true; ground_event.velocity_applied = true; }
#endif
        }
#if defined(DANGO_CONSTRAINT_OBSERVER)
        if (observing_ground && observed_ground_correction)
        {
            ground_event.event_kind = DANGO_OBSERVER_CORRECTION;
            dango_observer_after(&ground_event, s); DANGO_CONSTRAINT_OBSERVER_EMIT(&ground_event);
        }
#endif
    }
    #endif
    return residual;
}

static float dango_constraint_sweep_order(dango_constraint_state* s, bool correct, bool reverse)
{
    return dango_constraint_sweep_order_mode(s, correct, reverse, DANGO_CONSTRAINT_CORRECT_BOTH);
}

static float dango_constraint_sweep(dango_constraint_state* s, bool correct)
{
    return dango_constraint_sweep_order(s, correct, false);
}

static bool dango_constraint_solve(dango_constraint_state* s)
{
#if defined(DANGO_LAB_ARCS)
    (void)dango_constraint_sweep;(void)dango_constraint_angle;(void)dango_constraint_distance;
    /* Position repair never becomes velocity. Freeze final geometry before
       the accumulated unilateral velocity/contact solve. */
    bool converged=false;
    for(int iteration=0;iteration<384;++iteration) {
        float r=dango_constraint_sweep_order_mode(s,true,iteration%2!=0,DANGO_CONSTRAINT_CORRECT_POSITION);
        if(s->envelope_stats)++s->envelope_stats[8];
        if(!isfinite(r)){if(s->arc_stats)s->arc_stats[26]=1;return false;}
        if(dango_constraint_sweep_order_mode(s,false,false,DANGO_CONSTRAINT_CORRECT_POSITION)<=DANGO_CONSTRAINT_TOLERANCE){converged=true;break;}
    }
    if(!converged){if(s->arc_stats)s->arc_stats[26]=2;return false;}
    float before=dango_envelope_energy(s);
    for(int iteration=0;iteration<32;++iteration)
        if(!isfinite(dango_constraint_sweep_order_mode(s,true,iteration%2!=0,DANGO_CONSTRAINT_CORRECT_VELOCITY))){if(s->arc_stats)s->arc_stats[26]=3;return false;}
    float normal=0,tangent=0;for(int row=0;row<32;++row){normal+=s->arc_normal[row];tangent+=fabsf(s->arc_tangent[row]);}
    if(s->envelope_stats){s->envelope_stats[3]+=normal;s->envelope_stats[4]+=tangent;}
    dango_envelope_point final_foot;if(!dango_envelope_geometry(s->positions,s->radius,&final_foot))return false;
    if(s->arc_stats)s->arc_stats[8]=fmaxf(0,s->ground-final_foot.point.y);
    return dango_envelope_energy(s)<=before+fmaxf(1e-7f,1e-5f*before);
#else
#if defined(DANGO_CONSTRAINT_TEST_DIAGNOSTICS)
    dango_constraint_state before = *s;
#endif
    // Soft rigs under changing drag can still be converging after 96 sweeps.
    // Healthy steps retain the same early exit and tolerance; difficult contact
    // configurations receive a bounded extra budget before a genuine fault latches.
    for (int iteration = 0; iteration < 384; ++iteration)
    {
        float residual = dango_constraint_sweep_order(s, true, iteration % 2 != 0);
#if defined(DANGO_LAB_ENVELOPE)
        if(s->envelope_stats)++s->envelope_stats[8];
#endif
        if (!isfinite(residual))
        {
            return false;
        }
        if (dango_constraint_sweep(s, false) <= DANGO_CONSTRAINT_TOLERANCE)
        {
#if defined(DANGO_CONSTRAINT_TEST_DIAGNOSTICS)
            if (iteration >= 96) fprintf(stderr, "Recovered constraint after %d sweeps\n", iteration + 1);
#endif
            return true;
        }
    }
#if defined(DANGO_CONSTRAINT_TEST_DIAGNOSTICS)
    fprintf(stderr, "Constraint residual: %.9g\n", (double)dango_constraint_sweep(s, false));
    for (int i = 0; i < 17; ++i)
    {
        fprintf(stderr, "input %d: {%.9gf, %.9gf}, velocity {%.9gf, %.9gf}, inverse mass %.9gf\n", i,
            (double)before.positions[i].x, (double)before.positions[i].y,
            (double)before.velocities[i].x, (double)before.velocities[i].y, (double)before.inverse_mass[i]);
    }
    for (int i = 0; i < 16; ++i)
    {
        fprintf(stderr, "node %d: raw angle residual %.9g, display %.9g, y %.9g\n", i,
            (double)dango_constraint_angle(s, i, false, false),
            (double)dango_constraint_angle(s, i, true, false), (double)s->positions[i].y);
    }
#endif
    return false;
#endif
}

#endif

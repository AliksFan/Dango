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
} dango_constraint_state;

typedef enum dango_constraint_correction_mode
{
    DANGO_CONSTRAINT_CORRECT_BOTH = 0,
    DANGO_CONSTRAINT_CORRECT_POSITION = 1,
    DANGO_CONSTRAINT_CORRECT_VELOCITY = 2
} dango_constraint_correction_mode;

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
    float denominator = 0.0f;
    float rate = 0.0f;
    for (int i = 0; i < count; ++i)
    {
        denominator += s->inverse_mass[ids[i]] * b2Dot(gradients[i], gradients[i]);
        rate += b2Dot(gradients[i], s->velocities[ids[i]]);
    }
    if (denominator < 0.000001f)
    {
        return;
    }
    float lambda = mode == DANGO_CONSTRAINT_CORRECT_VELOCITY ? 0.0f : -error / denominator;
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
        return INFINITY;
    }
    float turn = atan2f(b2Cross(e, f), b2Dot(e, f));
    // Safety margin absorbs floating point and output mapping round-off.
    const float minimum = 0.002f;
    const float maximum = 1.5687963268f;
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
            return INFINITY;
        }
        residual = fmaxf(residual, dango_constraint_distance_mode(s, 16, i,
            s->radial_length * s->radial_min, s->radial_length * s->radial_max, correct, mode));
        residual = fmaxf(residual, dango_constraint_distance_mode(s, i, (i + 1) % 16,
            s->perimeter_length * s->perimeter_min, s->perimeter_length * s->perimeter_max, correct, mode));
        residual = fmaxf(residual, dango_constraint_angle_mode(s, i, false, correct, mode));
        residual = fmaxf(residual, dango_constraint_angle_mode(s, i, true, correct, mode));
    }
    for (int i = 0; i < 16; ++i)
    {
#if defined(DANGO_JOINT_BASELINE_COUNTERS)
        ++dango_constraint_row_visit_count;
#endif
        float penetration = fmaxf(0.0f, s->ground + s->radius - s->positions[i].y);
        residual = fmaxf(residual, penetration / s->radial_length);
        if (correct && penetration > 0.0f && mode != DANGO_CONSTRAINT_CORRECT_VELOCITY)
        {
            s->positions[i].y += penetration;
            if (mode == DANGO_CONSTRAINT_CORRECT_BOTH)
            {
                s->velocities[i].y = fmaxf(0.0f, s->velocities[i].y);
            }
        }
        else if (correct && mode == DANGO_CONSTRAINT_CORRECT_VELOCITY &&
            s->positions[i].y <= s->ground + s->radius + DANGO_CONSTRAINT_TOLERANCE * s->radial_length)
        {
            s->velocities[i].y = fmaxf(0.0f, s->velocities[i].y);
        }
    }
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
#if defined(DANGO_CONSTRAINT_TEST_DIAGNOSTICS)
    dango_constraint_state before = *s;
#endif
    // Soft rigs under changing drag can still be converging after 96 sweeps.
    // Healthy steps retain the same early exit and tolerance; difficult contact
    // configurations receive a bounded extra budget before a genuine fault latches.
    for (int iteration = 0; iteration < 384; ++iteration)
    {
        float residual = dango_constraint_sweep_order(s, true, iteration % 2 != 0);
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
}

#endif

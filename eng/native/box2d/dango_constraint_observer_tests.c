#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include "dango_constraint_observer.h"

#if !defined(DANGO_CONSTRAINT_OBSERVER_TEST_DISABLED)
static bool trace_enabled = true;
static dango_constraint_observation events[8192];
static int event_count, overflow;
static void record_event(const dango_constraint_observation* event)
{
    if (event_count >= (int)(sizeof events / sizeof events[0])) { overflow = 1; return; }
    events[event_count++] = *event;
}
#define DANGO_CONSTRAINT_OBSERVER
#define DANGO_CONSTRAINT_OBSERVER_EMIT record_event
#define DANGO_CONSTRAINT_OBSERVER_ENABLED() trace_enabled
#endif
#include "dango_self_constraints.h"

static int checks;
#define CHECK(x) do { ++checks; if (!(x)) { fprintf(stderr, "observer check failed line %d: %s\n", __LINE__, #x); return 1; } } while (0)

static dango_constraint_state natural_ring(void)
{
    dango_constraint_state s = {0};
    s.radial_length = .68f; s.perimeter_length = 2 * s.radial_length * sinf(3.14159265359f / 16);
    s.radial_min = .65f; s.radial_max = 1.22f; s.perimeter_min = .73f; s.perimeter_max = 1.07f;
    s.radius = .045f; s.ground = -10;
    for (int i = 0; i < 16; ++i)
    {
        float a = 6.28318530718f * i / 16;
        s.positions[i] = (b2Vec2){s.radial_length * cosf(a), s.radial_length * sinf(a)};
        s.inverse_mass[i] = 1.0f / (.012f + .001f * i);
        s.velocities[i] = (b2Vec2){.03f * i - .2f, .04f * i - .3f};
    }
    s.inverse_mass[16] = 1.0f / .12f;
    s.velocities[16] = (b2Vec2){.13f, -.17f};
    return s;
}

#if !defined(DANGO_CONSTRAINT_OBSERVER_TEST_DISABLED)
typedef struct point_d { double x, y; } point_d;
static bool close_d(double actual, double expected, double absolute, double relative)
{
    return isfinite(actual) && isfinite(expected) && fabs(actual - expected) <= absolute + relative * fabs(expected);
}
static double scalar_value(const dango_constraint_state* s, int kind, int node)
{
    if (kind == DANGO_OBSERVER_GROUND) return s->positions[node].y;
    if (kind == DANGO_OBSERVER_RADIAL || kind == DANGO_OBSERVER_PERIMETER)
    {
        int a = kind == DANGO_OBSERVER_RADIAL ? 16 : node;
        int b = kind == DANGO_OBSERVER_RADIAL ? node : (node + 1) % 16;
        double x = (double)s->positions[b].x - s->positions[a].x;
        double y = (double)s->positions[b].y - s->positions[a].y;
        return sqrt(x * x + y * y);
    }
    point_d p[3]; int ids[3] = {(node + 15) % 16, node, (node + 1) % 16};
    for (int i = 0; i < 3; ++i)
    {
        p[i] = (point_d){s->positions[ids[i]].x, s->positions[ids[i]].y};
        if (kind == DANGO_OBSERVER_DISPLAY_ANGLE)
        {
            double x = p[i].x - s->positions[16].x, y = p[i].y - s->positions[16].y;
            double r = sqrt(x * x + y * y);
            p[i].x += s->radius * x / r; p[i].y += s->radius * y / r;
        }
    }
    double ex = p[1].x - p[0].x, ey = p[1].y - p[0].y;
    double fx = p[2].x - p[1].x, fy = p[2].y - p[1].y;
    return atan2(ex * fy - ey * fx, ex * fx + ey * fy);
}
// Independent double finite differences; no solver gradient helper is reused.
static double gradient_component(const dango_constraint_state* s, int kind, int node, int id, bool y)
{
    const double h = .0001;
    dango_constraint_state plus = *s, minus = *s;
    float old = y ? s->positions[id].y : s->positions[id].x;
    float upper = (float)((double)old + h), lower = (float)((double)old - h);
    if (y) { plus.positions[id].y = upper; minus.positions[id].y = lower; }
    else { plus.positions[id].x = upper; minus.positions[id].x = lower; }
    return (scalar_value(&plus, kind, node) - scalar_value(&minus, kind, node)) / ((double)upper - lower);
}
static double boundary_value(const dango_constraint_state* s, int kind, int bound)
{
    if (kind == DANGO_OBSERVER_GROUND) return (float)(s->ground + s->radius);
    if (kind == DANGO_OBSERVER_RADIAL)
        return (float)(s->radial_length * (bound == DANGO_OBSERVER_LOWER ? s->radial_min : s->radial_max));
    if (kind == DANGO_OBSERVER_PERIMETER)
        return (float)(s->perimeter_length * (bound == DANGO_OBSERVER_LOWER ? s->perimeter_min : s->perimeter_max));
    return bound == DANGO_OBSERVER_LOWER ? (double).002f : (double)1.5687963268f;
}
static bool row_identity(const dango_constraint_observation* e)
{
    if (e->node < 0 || e->node >= 16 || e->bound < 1 || e->bound > 2) return false;
    if (e->kind == DANGO_OBSERVER_RADIAL) return e->count == 2 && e->ids[0] == 16 && e->ids[1] == e->node;
    if (e->kind == DANGO_OBSERVER_PERIMETER) return e->count == 2 && e->ids[0] == e->node && e->ids[1] == (e->node + 1) % 16;
    if (e->kind == DANGO_OBSERVER_RAW_ANGLE || e->kind == DANGO_OBSERVER_DISPLAY_ANGLE)
        return e->count == (e->kind == DANGO_OBSERVER_RAW_ANGLE ? 3 : 4) &&
            e->ids[0] == (e->node + 15) % 16 && e->ids[1] == e->node && e->ids[2] == (e->node + 1) % 16 &&
            (e->count == 3 || e->ids[3] == 16);
    return e->kind == DANGO_OBSERVER_GROUND && e->count == 1 && e->ids[0] == e->node && e->bound == 1;
}
static bool verify_event(const dango_constraint_observation* e, dango_constraint_state* current)
{
    if (!row_identity(e)) return false;
    for (int i = 0; i < e->count; ++i)
    {
        int id = e->ids[i];
        if (memcmp(&e->positions_before[i], &current->positions[id], sizeof(b2Vec2)) ||
            memcmp(&e->velocities_before[i], &current->velocities[id], sizeof(b2Vec2)) ||
            e->inverse_mass[i] != current->inverse_mass[id]) return false;
    }
    if (e->event_kind == DANGO_OBSERVER_INVALID_GEOMETRY) return !e->value_available;
    double measured = scalar_value(current, e->kind, e->node);
    double boundary = boundary_value(current, e->kind, e->bound);
    double gap = measured - boundary;
    double error = e->bound == DANGO_OBSERVER_LOWER ? fmin(0, gap) : fmax(0, gap);
    if (e->event_kind == DANGO_OBSERVER_EVALUATION)
    {
        if (!e->value_available || !close_d(e->measured, measured, 2e-6, 2e-6) ||
            !close_d(e->bound_value, boundary, 1e-7, 1e-7) || !close_d(e->error, error, 2e-6, 2e-6)) return false;
        if (e->touching != (fabs(e->signed_gap) <= e->touch_tolerance) || e->violated != (e->error != 0)) return false;
        for (int i = 0; i < e->count; ++i)
            if (memcmp(&e->positions_before[i], &e->positions_after[i], sizeof(b2Vec2)) ||
                memcmp(&e->velocities_before[i], &e->velocities_after[i], sizeof(b2Vec2))) return false;
        return !e->solve_available && !e->multipliers_available && !e->position_applied && !e->velocity_applied;
    }
    if (!e->correct || !close_d(e->error, error, 2e-6, 2e-6)) return false;
    if (e->kind == DANGO_OBSERVER_GROUND)
    {
        if (!e->ground_direct_clamp || e->multipliers_available || e->solve_available) return false;
        double py = e->position_applied ? (float)(e->positions_before[0].y - e->error) : e->positions_before[0].y;
        double vy = e->velocity_applied ? fmax(0, e->velocities_before[0].y) : e->velocities_before[0].y;
        if (!close_d(e->positions_after[0].y, py, 1e-7, 1e-7) || e->velocities_after[0].y != vy ||
            e->positions_after[0].x != e->positions_before[0].x || e->velocities_after[0].x != e->velocities_before[0].x) return false;
    }
    else
    {
        if (!e->gradient_available || !e->solve_available) return false;
        double denominator = 0, rate = 0;
        for (int i = 0; i < e->count; ++i)
        {
            double gx = gradient_component(current, e->kind, e->node, e->ids[i], false);
            double gy = gradient_component(current, e->kind, e->node, e->ids[i], true);
            if (!close_d(e->gradients[i].x, gx, 1e-5, 2e-5) || !close_d(e->gradients[i].y, gy, 1e-5, 2e-5)) return false;
            denominator += e->inverse_mass[i] * (gx * gx + gy * gy);
            rate += gx * e->velocities_before[i].x + gy * e->velocities_before[i].y;
        }
        if (!close_d(e->denominator, denominator, 1e-5, 5e-5) || !close_d(e->rate, rate, 2e-5, 5e-5)) return false;
        if (e->event_kind == DANGO_OBSERVER_BLOCKED_DENOMINATOR)
            return e->denominator < .000001f && !e->multipliers_available;
        if (!e->multipliers_available) return false;
        double raw_lambda = e->mode == DANGO_CONSTRAINT_CORRECT_VELOCITY ? 0 : -(double)e->error / e->denominator;
        double maximum_move = 0;
        for (int i = 0; i < e->count; ++i)
            maximum_move = fmax(maximum_move, fabs(raw_lambda) * e->inverse_mass[i] *
                sqrt((double)e->gradients[i].x * e->gradients[i].x + (double)e->gradients[i].y * e->gradients[i].y));
        double cap = .02 * current->radial_length;
        double lambda = maximum_move > cap ? raw_lambda * cap / maximum_move : raw_lambda;
        double impulse = e->mode == DANGO_CONSTRAINT_CORRECT_POSITION ? 0 :
            ((e->bound == DANGO_OBSERVER_LOWER ? e->rate < 0 : e->rate > 0) ? -(double)e->rate / e->denominator : 0);
        if (!close_d(e->raw_lambda, raw_lambda, 1e-8, 2e-6) || !close_d(e->lambda, lambda, 1e-8, 2e-6) ||
            !close_d(e->impulse, impulse, 1e-8, 2e-6) || !close_d(e->maximum_move, maximum_move, 1e-7, 2e-6) ||
            e->clipped != (maximum_move > cap) || e->position_applied != (e->mode != DANGO_CONSTRAINT_CORRECT_VELOCITY) ||
            e->velocity_applied != (e->mode != DANGO_CONSTRAINT_CORRECT_POSITION)) return false;
        for (int i = 0; i < e->count; ++i)
        {
            double px = e->positions_before[i].x + lambda * e->inverse_mass[i] * e->gradients[i].x;
            double py = e->positions_before[i].y + lambda * e->inverse_mass[i] * e->gradients[i].y;
            double vx = e->velocities_before[i].x + impulse * e->inverse_mass[i] * e->gradients[i].x;
            double vy = e->velocities_before[i].y + impulse * e->inverse_mass[i] * e->gradients[i].y;
            if (!close_d(e->positions_after[i].x, px, 2e-6, 2e-6) || !close_d(e->positions_after[i].y, py, 2e-6, 2e-6) ||
                !close_d(e->velocities_after[i].x, vx, 2e-6, 2e-6) || !close_d(e->velocities_after[i].y, vy, 2e-6, 2e-6)) return false;
        }
        double center_x = 0, center_y = 0, momentum_x = 0, momentum_y = 0, energy_change = 0;
        for (int i = 0; i < e->count; ++i)
        {
            if (e->inverse_mass[i] <= 0) continue;
            double mass = 1.0 / e->inverse_mass[i];
            center_x += mass * ((double)e->positions_after[i].x - e->positions_before[i].x);
            center_y += mass * ((double)e->positions_after[i].y - e->positions_before[i].y);
            momentum_x += mass * ((double)e->velocities_after[i].x - e->velocities_before[i].x);
            momentum_y += mass * ((double)e->velocities_after[i].y - e->velocities_before[i].y);
            energy_change += .5 * mass * ((double)e->velocities_after[i].x * e->velocities_after[i].x +
                (double)e->velocities_after[i].y * e->velocities_after[i].y -
                (double)e->velocities_before[i].x * e->velocities_before[i].x -
                (double)e->velocities_before[i].y * e->velocities_before[i].y);
        }
        double expected_energy_change = e->impulse == 0 ? 0 : -.5 * (double)e->rate * e->rate / e->denominator;
        if (fabs(center_x) > 5e-7 || fabs(center_y) > 5e-7 || fabs(momentum_x) > 5e-7 || fabs(momentum_y) > 5e-7 ||
            !close_d(energy_change, expected_energy_change, 2e-6, 2e-6) || energy_change > 2e-6) return false;
    }
    for (int i = 0; i < e->count; ++i)
    {
        current->positions[e->ids[i]] = e->positions_after[i];
        current->velocities[e->ids[i]] = e->velocities_after[i];
    }
    return true;
}
static bool verify_trace(const dango_constraint_state* before, const dango_constraint_state* after)
{
    dango_constraint_state current = *before;
    if (overflow) return false;
    for (int i = 0; i < event_count; ++i)
        if (!verify_event(&events[i], &current)) { fprintf(stderr, "trace rejects event %d kind %d node %d event %d\n", i, events[i].kind, events[i].node, events[i].event_kind); return false; }
    return memcmp(&current, after, sizeof current) == 0;
}
static bool evaluation_order(int start, int rows, bool reverse, bool correct, int mode)
{
    int ordinal = 0;
    for (int i = 0; i < event_count; ++i)
    {
        const dango_constraint_observation* e = &events[i];
        if (e->event_kind != DANGO_OBSERVER_EVALUATION) continue;
        if (ordinal < start) { ++ordinal; continue; }
        int row = ordinal - start;
        if (row >= rows) break;
        int node, kind, bound;
        if (rows == 2) { node = events[0].node; kind = events[0].kind; bound = row + 1; }
        else if (row < 128)
        {
            node = reverse ? 15 - row / 8 : row / 8;
            kind = 1 + row % 8 / 2; bound = row % 2 + 1;
        }
        else { node = row - 128; kind = DANGO_OBSERVER_GROUND; bound = DANGO_OBSERVER_LOWER; }
        if (e->node != node || e->kind != kind || e->bound != bound || e->correct != correct || e->mode != mode) return false;
        ++ordinal;
    }
    return ordinal >= start + rows;
}
#endif

static float exercise(dango_constraint_state* s, int scenario, dango_constraint_correction_mode mode)
{
    if (scenario < 4)
    {
        int a = scenario < 2 ? 16 : 0, b = scenario < 2 ? 0 : 1;
        float rest = scenario < 2 ? s->radial_length : s->perimeter_length;
        float minimum = rest * (scenario < 2 ? s->radial_min : s->perimeter_min);
        float maximum = rest * (scenario < 2 ? s->radial_max : s->perimeter_max);
        return dango_constraint_distance_mode(s, a, b, minimum, maximum, true, mode);
    }
    if (scenario < 8) return dango_constraint_angle_mode(s, 0, scenario >= 6, true, mode);
    if (scenario == 12) return dango_constraint_sweep_order_mode(s, false, true, mode);
    if (scenario == 13) return dango_constraint_sweep_order_mode(s, false, false, mode);
    if (scenario == 14) return dango_constraint_solve(s) ? 0 : INFINITY;
    return dango_constraint_sweep_order_mode(s, true, false, mode);
}
static dango_constraint_state fixture(int scenario)
{
    dango_constraint_state s = natural_ring();
    if (scenario == 0) { s.positions[0].x = .35f; s.velocities[0].x = -2; }
    if (scenario == 1) { s.positions[0].x = 1; s.velocities[0].x = 2; }
    if (scenario == 2) s.positions[1] = (b2Vec2){.75f, .03f};
    if (scenario == 3) s.positions[1].x += .35f;
    if (scenario >= 4 && scenario < 8)
    {
        s.positions[15] = (b2Vec2){-.2f, 0}; s.positions[0] = (b2Vec2){0, 0};
        s.positions[1] = (scenario % 2) ? (b2Vec2){-.15f, .10f} : (b2Vec2){.2f, -.1f};
        s.positions[16] = (b2Vec2){-.3f, -.3f};
    }
    if (scenario == 8) { s.ground = -.60f; s.velocities[12].y = -2; }
    if (scenario == 9) { s.ground = s.positions[12].y - s.radius; s.velocities[12].y = -2; }
    if (scenario == 10) { s.ground = s.positions[12].y - s.radius - .000003f; s.velocities[12].y = -2; }
    if (scenario == 11) { s.ground = -.60f; s.inverse_mass[12] = 0; s.velocities[12].y = -2; }
    return s;
}

int main(int argc, char** argv)
{
    if (argc > 2) return 2;
    FILE* dump = argc == 2 ? fopen(argv[1], "wbx") : NULL;
    if (argc == 2 && !dump) { fprintf(stderr, "Cannot create exclusive fixture dump.\n"); return 2; }
    for (int scenario = 0; scenario < 15; ++scenario)
    for (int mode = 0; mode < 3; ++mode)
    {
        dango_constraint_state before = fixture(scenario), after = before;
#if !defined(DANGO_CONSTRAINT_OBSERVER_TEST_DISABLED)
        event_count = overflow = 0; trace_enabled = true;
#endif
        float residual = exercise(&after, scenario, (dango_constraint_correction_mode)mode);
        CHECK(isfinite(residual));
#if !defined(DANGO_CONSTRAINT_OBSERVER_TEST_DISABLED)
        CHECK(event_count > 0); CHECK(verify_trace(&before, &after));
        if (scenario < 8) CHECK(evaluation_order(0, 2, false, true, mode));
        else if (scenario < 14) CHECK(evaluation_order(0, 144, scenario == 12, scenario < 12, mode));
        else
        {
            CHECK(evaluation_order(0, 144, false, true, DANGO_CONSTRAINT_CORRECT_BOTH));
            CHECK(evaluation_order(144, 144, false, false, DANGO_CONSTRAINT_CORRECT_BOTH));
        }
        dango_constraint_state disabled = before; event_count = 0; trace_enabled = false;
        float disabled_residual = exercise(&disabled, scenario, (dango_constraint_correction_mode)mode);
        CHECK(event_count == 0); CHECK(memcmp(&after, &disabled, sizeof after) == 0);
        CHECK(memcmp(&residual, &disabled_residual, sizeof residual) == 0); trace_enabled = true;
#endif
        if (dump) { CHECK(fwrite(&after, sizeof after, 1, dump) == 1); CHECK(fwrite(&residual, sizeof residual, 1, dump) == 1); }
    }
#if !defined(DANGO_CONSTRAINT_OBSERVER_TEST_DISABLED)
    // A corrupted stable identity, gradient, denominator, multiplier, missing
    // correction, or unreported nonparticipant write must be rejected.
    dango_constraint_state before = fixture(1), after = before;
    event_count = overflow = 0; exercise(&after, 1, DANGO_CONSTRAINT_CORRECT_BOTH);
    CHECK(event_count == 3); dango_constraint_observation original = events[2];
    dango_constraint_state current = before;
    events[2].node = 2; CHECK(!verify_event(&events[2], &current)); events[2] = original;
    events[2].gradients[0].y += .1f; current = before; CHECK(!verify_event(&events[2], &current)); events[2] = original;
    events[2].denominator += 1; current = before; CHECK(!verify_event(&events[2], &current)); events[2] = original;
    events[2].lambda += .01f; current = before; CHECK(!verify_event(&events[2], &current)); events[2] = original;
    events[2].impulse += .01f; current = before; CHECK(!verify_event(&events[2], &current)); events[2] = original;
    dango_constraint_observation first = events[0]; events[0] = events[1];
    CHECK(!evaluation_order(0, 2, false, true, DANGO_CONSTRAINT_CORRECT_BOTH)); events[0] = first;
    --event_count; CHECK(!verify_trace(&before, &after)); ++event_count;
    after.positions[5].x += .01f; CHECK(!verify_trace(&before, &after));
    // Zero effective inverse mass must report the actual early return.
    before = fixture(1); before.inverse_mass[0] = before.inverse_mass[16] = 0; after = before;
    event_count = overflow = 0; exercise(&after, 1, DANGO_CONSTRAINT_CORRECT_BOTH);
    CHECK(event_count == 3 && events[2].event_kind == DANGO_OBSERVER_BLOCKED_DENOMINATOR);
    CHECK(verify_trace(&before, &after));
    // Genuine collapsed edges retain failure and do not mutate the state.
    before = natural_ring(); before.positions[1] = before.positions[0]; after = before;
    event_count = overflow = 0;
    CHECK(isinf(dango_constraint_distance_mode(&after, 0, 1, .1f, .4f, true, DANGO_CONSTRAINT_CORRECT_BOTH)));
    CHECK(event_count == 2 && events[0].event_kind == DANGO_OBSERVER_INVALID_GEOMETRY);
    CHECK(memcmp(&before, &after, sizeof before) == 0);
#endif
    if (dump) CHECK(fclose(dump) == 0);
    printf("constraint observer: checks=%d scenarios=45 result=PASS\n", checks);
    return 0;
}

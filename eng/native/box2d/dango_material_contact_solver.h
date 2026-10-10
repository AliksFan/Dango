#ifndef DANGO_MATERIAL_CONTACT_SOLVER_H
#define DANGO_MATERIAL_CONTACT_SOLVER_H

// Isolated, fixed-geometry velocity candidate. No world/body API calls, position
// corrections, material forces, damping, or cross-step warm starts live here.
// The caller first completes the unchanged 384-sweep POSITION stage, applies
// the original damping once, then builds this stage from that final geometry.
#include <float.h>
#include <stdint.h>
#include <string.h>
#include "dango_self_constraints.h"

#define DANGO_MATERIAL_CONTACT_SHAPE_ROWS 128
#define DANGO_MATERIAL_CONTACT_ROWS 160
#define DANGO_MATERIAL_CONTACT_SWEEPS 384
#define DANGO_MATERIAL_CONTACT_RATE_TOLERANCE 0.00001

typedef struct dango_material_contact_vec2 { double x, y; } dango_material_contact_vec2;

typedef enum dango_material_contact_status
{
    DANGO_MATERIAL_CONTACT_OK = 0,
    DANGO_MATERIAL_CONTACT_INVALID_INPUT,
    DANGO_MATERIAL_CONTACT_INVALID_GEOMETRY,
    DANGO_MATERIAL_CONTACT_CAPACITY,
    DANGO_MATERIAL_CONTACT_RECEIPT,
    DANGO_MATERIAL_CONTACT_BUDGET,
    DANGO_MATERIAL_CONTACT_FINAL_RATE,
    DANGO_MATERIAL_CONTACT_COMPLEMENTARITY,
    DANGO_MATERIAL_CONTACT_FRICTION,
    DANGO_MATERIAL_CONTACT_ENERGY,
    DANGO_MATERIAL_CONTACT_LEDGER,
    DANGO_MATERIAL_CONTACT_SPEED_CAP
} dango_material_contact_status;

typedef enum dango_material_contact_kind
{
    DANGO_MATERIAL_CONTACT_RADIAL = 1,
    DANGO_MATERIAL_CONTACT_PERIMETER,
    DANGO_MATERIAL_CONTACT_RAW_ANGLE,
    DANGO_MATERIAL_CONTACT_DISPLAY_ANGLE,
    DANGO_MATERIAL_CONTACT_NORMAL,
    DANGO_MATERIAL_CONTACT_TANGENT
} dango_material_contact_kind;

// One canonical, summed actual ground receipt per node. Input velocities already
// include these impulses. The adapter must reject capture overflow/duplicates,
// orient normal to +Y and tangent to +X, and supply the actual mixed friction
// sqrt(node_shape_friction * ground_shape_friction). totalNormalImpulse is
// diagnostic telemetry, never the support budget. Neither base impulse is replayed.
typedef struct dango_material_ground_receipt
{
    int node;
    double normal_impulse;
    double tangent_impulse;
    double friction;
    double total_normal_impulse;
} dango_material_ground_receipt;

typedef struct dango_material_contact_row
{
    int id, kind, node, bound, count, ids[4];
    bool active;
    double gap, effective_inverse_mass, multiplier;
    dango_material_contact_vec2 gradients[4];
} dango_material_contact_row;

typedef struct dango_material_contact_support
{
    bool receipt_present, touching;
    double base_normal, base_tangent, friction, total_normal_telemetry;
    double receipt_cone_slack;
} dango_material_contact_support;

typedef struct dango_material_contact_report
{
    dango_material_contact_status status;
    int sweeps, active_shape_rows, active_normal_rows, active_tangent_rows;
    uint64_t row_visits, cone_retractions;
    double geometry_residual;
    // Linear rates are world-length/s; relative linear rates are radial-length/s.
    // Angular rates are radians/s. Never merge angular and linear quantities.
    double maximum_linear_closing, maximum_relative_linear_closing;
    double maximum_angular_closing;
    double maximum_relative_linear_complementarity, maximum_angular_complementarity;
    double maximum_relative_friction_rate, maximum_friction_cone_excess;
    double maximum_receipt_cone_slack;
    double energy_before, energy_after, energy_allowance;
    dango_material_contact_vec2 momentum_before, momentum_after, external_impulse;
    double momentum_residual, momentum_allowance;
    double angular_momentum_before, angular_momentum_after, external_torque;
    double angular_momentum_residual, angular_momentum_allowance;
    double maximum_relative_velocity_ledger_residual, maximum_relative_velocity_ledger_allowance;
    bool float_output_checked;
    // True only when the ledger values belong to the inspected velocities.
    bool ledger_evaluated;
} dango_material_contact_report;

typedef struct dango_material_contact_cap_report
{
    dango_material_contact_status status;
    float scale, maximum_before, maximum_after;
    double energy_before, energy_after, energy_allowance;
    dango_material_contact_vec2 cap_momentum_change;
    double cap_angular_momentum_change;
    bool contact_output_checked, exact_common_scale_checked;
    dango_material_contact_report final_rows;
} dango_material_contact_cap_report;

typedef struct dango_material_contact_solver
{
    dango_constraint_state geometry;
    dango_material_ground_receipt receipts[16];
    int receipt_count;
    double step_seconds;
    dango_material_contact_row rows[DANGO_MATERIAL_CONTACT_ROWS];
    dango_material_contact_support support[16];
    dango_material_contact_vec2 input_velocities[17], velocities[17];
    dango_material_contact_report report;
} dango_material_contact_solver;

static inline dango_material_contact_vec2 dango_mc_v(double x, double y)
{ dango_material_contact_vec2 v = {x, y}; return v; }
static inline dango_material_contact_vec2 dango_mc_add(dango_material_contact_vec2 a, dango_material_contact_vec2 b)
{ return dango_mc_v(a.x + b.x, a.y + b.y); }
static inline dango_material_contact_vec2 dango_mc_sub(dango_material_contact_vec2 a, dango_material_contact_vec2 b)
{ return dango_mc_v(a.x - b.x, a.y - b.y); }
static inline dango_material_contact_vec2 dango_mc_mul(double s, dango_material_contact_vec2 v)
{ return dango_mc_v(s * v.x, s * v.y); }
static inline double dango_mc_dot(dango_material_contact_vec2 a, dango_material_contact_vec2 b)
{ return a.x * b.x + a.y * b.y; }
static inline double dango_mc_cross(dango_material_contact_vec2 a, dango_material_contact_vec2 b)
{ return a.x * b.y - a.y * b.x; }
static inline double dango_mc_length(dango_material_contact_vec2 v)
{ return hypot(v.x, v.y); }
static inline dango_material_contact_vec2 dango_mc_position(const dango_constraint_state* s, int node)
{ return dango_mc_v(s->positions[node].x, s->positions[node].y); }
static inline bool dango_mc_finite(dango_material_contact_vec2 v)
{ return isfinite(v.x) && isfinite(v.y); }
static inline double dango_mc_clamp(double x, double low, double high)
{ return fmin(high, fmax(low, x)); }

static inline bool dango_mc_fail(dango_material_contact_report* report, dango_material_contact_status status)
{ if (report) report->status = status; return false; }

static inline bool dango_mc_state_valid(const dango_constraint_state* s)
{
    if (!s || !isfinite(s->radial_length) || s->radial_length <= 0 ||
        !isfinite(s->perimeter_length) || s->perimeter_length <= 0 ||
        !isfinite(s->radial_min) || s->radial_min <= 0 ||
        !isfinite(s->radial_max) || s->radial_max <= s->radial_min ||
        !isfinite(s->perimeter_min) || s->perimeter_min <= 0 ||
        !isfinite(s->perimeter_max) || s->perimeter_max <= s->perimeter_min ||
        !isfinite(s->radius) || s->radius <= 0 || !isfinite(s->ground)) return false;
    for (int i = 0; i < 17; ++i)
    {
        // The present single-body rig has 17 dynamic bodies. A zero inverse mass
        // is rejected explicitly rather than silently inventing a finite mass.
        if (!isfinite(s->inverse_mass[i]) || s->inverse_mass[i] <= 0 ||
            !isfinite(s->positions[i].x) || !isfinite(s->positions[i].y) ||
            !isfinite(s->velocities[i].x) || !isfinite(s->velocities[i].y)) return false;
    }
    return true;
}

static inline void dango_mc_row(dango_material_contact_solver* solver, int row_id,
    int kind, int node, int bound, int count, const int* ids,
    const dango_material_contact_vec2* gradients, double gap, double touch_tolerance)
{
    dango_material_contact_row* row = &solver->rows[row_id];
    row->id = row_id; row->kind = kind; row->node = node; row->bound = bound;
    row->count = count; row->gap = gap; row->active = gap <= touch_tolerance;
    for (int i = 0; i < count; ++i)
    {
        row->ids[i] = ids[i]; row->gradients[i] = gradients[i];
        row->effective_inverse_mass += solver->geometry.inverse_mass[ids[i]] * dango_mc_dot(gradients[i], gradients[i]);
    }
}

static inline bool dango_mc_distance_rows(dango_material_contact_solver* solver,
    int offset, int kind, int node, int a, int b, double minimum, double maximum)
{
    dango_material_contact_vec2 delta = dango_mc_sub(dango_mc_position(&solver->geometry, b), dango_mc_position(&solver->geometry, a));
    double length = dango_mc_length(delta);
    if (!isfinite(length) || length < 0.00001) return false;
    dango_material_contact_vec2 direction = dango_mc_mul(1.0 / length, delta);
    dango_material_contact_vec2 lower[2] = {dango_mc_mul(-1, direction), direction};
    dango_material_contact_vec2 upper[2] = {direction, dango_mc_mul(-1, direction)};
    int ids[2] = {a, b};
    double tolerance = DANGO_CONSTRAINT_TOLERANCE * (double)solver->geometry.radial_length;
    dango_mc_row(solver, offset, kind, node, 1, 2, ids, lower, length - minimum, tolerance);
    dango_mc_row(solver, offset + 1, kind, node, 2, 2, ids, upper, maximum - length, tolerance);
    return true;
}

static inline bool dango_mc_angle_rows(dango_material_contact_solver* solver, int offset, int node, bool display)
{
    const dango_constraint_state* s = &solver->geometry;
    int ids[4] = {(node + 15) % 16, node, (node + 1) % 16, 16};
    dango_material_contact_vec2 p[3];
    double radial_lengths[3] = {0};
    dango_material_contact_vec2 units[3];
    for (int i = 0; i < 3; ++i)
    {
        p[i] = dango_mc_position(s, ids[i]);
        if (display)
        {
            dango_material_contact_vec2 radial = dango_mc_sub(p[i], dango_mc_position(s, 16));
            radial_lengths[i] = dango_mc_length(radial);
            if (!isfinite(radial_lengths[i]) || radial_lengths[i] < 0.00001) return false;
            units[i] = dango_mc_mul(1.0 / radial_lengths[i], radial);
            p[i] = dango_mc_add(p[i], dango_mc_mul(s->radius, units[i]));
        }
    }
    dango_material_contact_vec2 e = dango_mc_sub(p[1], p[0]), f = dango_mc_sub(p[2], p[1]);
    double e2 = dango_mc_dot(e, e), f2 = dango_mc_dot(f, f);
    if (!isfinite(e2 + f2) || e2 < 1e-10 || f2 < 1e-10) return false;
    double turn = atan2(dango_mc_cross(e, f), dango_mc_dot(e, f));
    dango_material_contact_vec2 gradients[4] = {
        {-e.y / e2, e.x / e2}, {0, 0}, {-f.y / f2, f.x / f2}, {0, 0}};
    gradients[1] = dango_mc_mul(-1, dango_mc_add(gradients[0], gradients[2]));
    if (display)
    {
        for (int i = 0; i < 3; ++i)
        {
            dango_material_contact_vec2 tangent = dango_mc_sub(gradients[i], dango_mc_mul(dango_mc_dot(units[i], gradients[i]), units[i]));
            dango_material_contact_vec2 extra = dango_mc_mul(s->radius / radial_lengths[i], tangent);
            gradients[i] = dango_mc_add(gradients[i], extra);
            gradients[3] = dango_mc_sub(gradients[3], extra);
        }
    }
    int count = display ? 4 : 3;
    // Exactly the existing float bounds, promoted without changing their values.
    double minimum = (double)0.002f, maximum = (double)1.5687963268f;
    dango_mc_row(solver, offset, display ? DANGO_MATERIAL_CONTACT_DISPLAY_ANGLE : DANGO_MATERIAL_CONTACT_RAW_ANGLE,
        node, 1, count, ids, gradients, turn - minimum, DANGO_CONSTRAINT_TOLERANCE);
    for (int i = 0; i < count; ++i) gradients[i] = dango_mc_mul(-1, gradients[i]);
    dango_mc_row(solver, offset + 1, display ? DANGO_MATERIAL_CONTACT_DISPLAY_ANGLE : DANGO_MATERIAL_CONTACT_RAW_ANGLE,
        node, 2, count, ids, gradients, maximum - turn, DANGO_CONSTRAINT_TOLERANCE);
    return true;
}

// Builds into a temporary; failed build leaves solver unchanged. Original
// float geometry checks and bounds remain the gate for the position stage.
static inline bool dango_material_contact_build(const dango_constraint_state* state,
    const dango_material_ground_receipt* receipts, int receipt_count, double step_seconds,
    dango_material_contact_solver* solver, dango_material_contact_report* report)
{
    if (report) memset(report, 0, sizeof(*report));
    if (!solver || !dango_mc_state_valid(state) || !isfinite(step_seconds) || step_seconds <= 0 ||
        receipt_count < 0 || (receipt_count > 0 && !receipts)) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_INVALID_INPUT);
    if (receipt_count > 16) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_CAPACITY);
    dango_material_contact_solver candidate = {0};
    candidate.geometry = *state; candidate.step_seconds = step_seconds;
    candidate.receipt_count = receipt_count;
    dango_constraint_state geometric_check = *state;
    double residual = dango_constraint_sweep_order_mode(&geometric_check, false, false, DANGO_CONSTRAINT_CORRECT_POSITION);
    candidate.report.geometry_residual = residual;
    if (!isfinite(residual) || residual > DANGO_CONSTRAINT_TOLERANCE)
    {
        if (report) report->geometry_residual = residual;
        return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_INVALID_GEOMETRY);
    }
    for (int i = 0; i < 17; ++i)
        candidate.velocities[i] = candidate.input_velocities[i] = dango_mc_v(state->velocities[i].x, state->velocities[i].y);
    for (int i = 0; i < receipt_count; ++i)
    {
        const dango_material_ground_receipt* receipt = &receipts[i];
        if (receipt->node < 0 || receipt->node >= 16 ||
            candidate.support[receipt->node].receipt_present ||
            !isfinite(receipt->normal_impulse) || receipt->normal_impulse < 0 ||
            !isfinite(receipt->tangent_impulse) || !isfinite(receipt->friction) || receipt->friction < 0 ||
            !isfinite(receipt->total_normal_impulse) || receipt->total_normal_impulse < 0)
            return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_RECEIPT);
        double bound = receipt->friction * receipt->normal_impulse;
        double receipt_roundoff = 8 * FLT_EPSILON * fmax(bound, fabs(receipt->tangent_impulse));
        if (!isfinite(bound) || fabs(receipt->tangent_impulse) > bound + receipt_roundoff)
            return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_RECEIPT);
        dango_material_contact_support* support = &candidate.support[receipt->node];
        support->receipt_present = true;
        support->base_normal = receipt->normal_impulse; support->base_tangent = receipt->tangent_impulse;
        support->friction = receipt->friction; support->total_normal_telemetry = receipt->total_normal_impulse;
        // Preserve only excess that already occurred in the float receipt and
        // passed the fixed 8*FLT_EPSILON input gate. Do not grant its full gate
        // allowance as fresh friction budget or rewrite any imported value.
        support->receipt_cone_slack = fmax(0, fabs(receipt->tangent_impulse) - bound);
        candidate.report.maximum_receipt_cone_slack = fmax(candidate.report.maximum_receipt_cone_slack, support->receipt_cone_slack);
        candidate.receipts[i] = *receipt;
    }
    for (int node = 0; node < 16; ++node)
    {
        if (!dango_mc_distance_rows(&candidate, 2 * node, DANGO_MATERIAL_CONTACT_RADIAL, node, 16, node,
            (double)(state->radial_length * state->radial_min), (double)(state->radial_length * state->radial_max)) ||
            !dango_mc_distance_rows(&candidate, 32 + 2 * node, DANGO_MATERIAL_CONTACT_PERIMETER, node, node, (node + 1) % 16,
            (double)(state->perimeter_length * state->perimeter_min), (double)(state->perimeter_length * state->perimeter_max)) ||
            !dango_mc_angle_rows(&candidate, 64 + 2 * node, node, false) ||
            !dango_mc_angle_rows(&candidate, 96 + 2 * node, node, true))
            return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_INVALID_GEOMETRY);
        int ids[1] = {node};
        dango_material_contact_vec2 normal[1] = {{0, 1}}, tangent[1] = {{1, 0}};
        double gap = (double)state->positions[node].y - (double)(state->ground + state->radius);
        double tolerance = DANGO_CONSTRAINT_TOLERANCE * (double)state->radial_length;
        dango_mc_row(&candidate, 128 + node, DANGO_MATERIAL_CONTACT_NORMAL, node, 1, 1, ids, normal, gap, tolerance);
        candidate.support[node].touching = candidate.rows[128 + node].active;
        dango_mc_row(&candidate, 144 + node, DANGO_MATERIAL_CONTACT_TANGENT, node, 0, 1, ids, tangent, gap, tolerance);
        // Geometric touching without an actual receipt can create an independent
        // unilateral normal, but cannot invent a ground material/friction receipt.
        candidate.rows[144 + node].active = candidate.support[node].touching && candidate.support[node].receipt_present;
    }
    for (int i = 0; i < DANGO_MATERIAL_CONTACT_ROWS; ++i)
    {
        dango_material_contact_row* row = &candidate.rows[i];
        if (!isfinite(row->gap) || !isfinite(row->effective_inverse_mass) || row->effective_inverse_mass <= 0)
            return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_INVALID_GEOMETRY);
        if (row->active)
        {
            if (i < 128) ++candidate.report.active_shape_rows;
            else if (i < 144) ++candidate.report.active_normal_rows;
            else ++candidate.report.active_tangent_rows;
        }
    }
    *solver = candidate;
    if (report) *report = candidate.report;
    return true;
}

static inline double dango_mc_rate(const dango_material_contact_row* row, const dango_material_contact_vec2* velocities)
{
    double rate = 0;
    for (int i = 0; i < row->count; ++i) rate += dango_mc_dot(row->gradients[i], velocities[row->ids[i]]);
    return rate;
}

static inline void dango_mc_apply(dango_material_contact_solver* solver, dango_material_contact_row* row, double multiplier)
{
    double delta = multiplier - row->multiplier;
    row->multiplier = multiplier;
    for (int i = 0; i < row->count; ++i)
    {
        int id = row->ids[i];
        solver->velocities[id] = dango_mc_add(solver->velocities[id],
            dango_mc_mul(delta * solver->geometry.inverse_mass[id], row->gradients[i]));
    }
}

static inline void dango_mc_unilateral(dango_material_contact_solver* solver, dango_material_contact_row* row)
{
    ++solver->report.row_visits;
    double rate = dango_mc_rate(row, solver->velocities);
    dango_mc_apply(solver, row, fmax(0, row->multiplier - rate / row->effective_inverse_mass));
}

static inline double dango_mc_friction_bound(const dango_material_contact_solver* solver, int node)
{
    const dango_material_contact_support* support = &solver->support[node];
    if (!support->receipt_present || !support->touching) return 0;
    return support->friction * (support->base_normal + solver->rows[128 + node].multiplier) + support->receipt_cone_slack;
}

static inline void dango_mc_contact_pair(dango_material_contact_solver* solver, int node)
{
    dango_material_contact_row* normal = &solver->rows[128 + node];
    dango_material_contact_row* tangent = &solver->rows[144 + node];
    if (normal->active) dango_mc_unilateral(solver, normal);
    if (!tangent->active) return;
    double bound = dango_mc_friction_bound(solver, node);
    double base = solver->support[node].base_tangent;
    // A smaller extra normal immediately retracts only our own tangent delta.
    // Clamp TOTAL tangent, never grant Box2D a second friction budget.
    double current_total = base + tangent->multiplier;
    double legal_total = dango_mc_clamp(current_total, -bound, bound);
    if (legal_total != current_total)
    {
        ++solver->report.cone_retractions;
        dango_mc_apply(solver, tangent, legal_total - base);
    }
    ++solver->report.row_visits;
    double rate = dango_mc_rate(tangent, solver->velocities);
    double total = base + tangent->multiplier - rate / tangent->effective_inverse_mass;
    dango_mc_apply(solver, tangent, dango_mc_clamp(total, -bound, bound) - base);
}

static inline bool dango_mc_is_angle(int kind)
{ return kind == DANGO_MATERIAL_CONTACT_RAW_ANGLE || kind == DANGO_MATERIAL_CONTACT_DISPLAY_ANGLE; }

// Independent inspection uses freshly rebuilt geometry rows, not the solver's
// last visited row, cached rate, active flags, gradient, or update size.
static inline bool dango_mc_inspect_rows(const dango_material_contact_solver* solver,
    const dango_material_contact_solver* reference, const dango_material_contact_vec2* velocities,
    dango_material_contact_report* report)
{
    bool rate_ok = true, complementarity_ok = true, friction_ok = true;
    report->maximum_linear_closing = report->maximum_relative_linear_closing = report->maximum_angular_closing = 0;
    report->maximum_relative_linear_complementarity = report->maximum_angular_complementarity = 0;
    report->maximum_relative_friction_rate = report->maximum_friction_cone_excess = 0;
    double scale = solver->geometry.radial_length;
    for (int i = 0; i < DANGO_MATERIAL_CONTACT_ROWS; ++i)
    {
        const dango_material_contact_row* row = &reference->rows[i];
        double multiplier = solver->rows[i].multiplier;
        if (!isfinite(multiplier) || (!row->active && multiplier != 0) ||
            (i < 144 && multiplier < 0)) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_COMPLEMENTARITY);
        if (!row->active) continue;
        double rate = dango_mc_rate(row, velocities);
        if (!isfinite(rate)) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_INVALID_INPUT);
        if (i < 144)
        {
            bool angular = dango_mc_is_angle(row->kind);
            double normalized = angular ? rate : rate / scale;
            double closing = fmax(0, -normalized);
            if (angular) report->maximum_angular_closing = fmax(report->maximum_angular_closing, closing);
            else
            {
                report->maximum_linear_closing = fmax(report->maximum_linear_closing, fmax(0, -rate));
                report->maximum_relative_linear_closing = fmax(report->maximum_relative_linear_closing, closing);
            }
            if (closing > DANGO_MATERIAL_CONTACT_RATE_TOLERANCE) rate_ok = false;
            double complementary = multiplier > 0 ? fmax(0, normalized) : 0;
            if (angular) report->maximum_angular_complementarity = fmax(report->maximum_angular_complementarity, complementary);
            else report->maximum_relative_linear_complementarity = fmax(report->maximum_relative_linear_complementarity, complementary);
            if (complementary > DANGO_MATERIAL_CONTACT_RATE_TOLERANCE) complementarity_ok = false;
        }
        else
        {
            int node = i - 144;
            double base = reference->support[node].base_tangent;
            double normal = reference->support[node].base_normal + solver->rows[128 + node].multiplier;
            double physical_bound = reference->support[node].friction * normal;
            double slack = reference->support[node].receipt_cone_slack;
            double bound = physical_bound + slack;
            double total = base + multiplier;
            double cone_epsilon = 32 * DBL_EPSILON * fmax(bound, fabs(total));
            double excess = fmax(0, fabs(total) - physical_bound);
            report->maximum_friction_cone_excess = fmax(report->maximum_friction_cone_excess, excess);
            if (!isfinite(bound) || bound < 0 || excess > slack + cone_epsilon) friction_ok = false;
            double violation = 0;
            if (bound > 0)
            {
                if (total <= -bound + cone_epsilon) violation = fmax(0, -rate);
                else if (total >= bound - cone_epsilon) violation = fmax(0, rate);
                else violation = fabs(rate);
            }
            report->maximum_relative_friction_rate = fmax(report->maximum_relative_friction_rate, violation / scale);
            if (violation / scale > DANGO_MATERIAL_CONTACT_RATE_TOLERANCE) friction_ok = false;
        }
    }
    if (!rate_ok) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_FINAL_RATE);
    if (!complementarity_ok) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_COMPLEMENTARITY);
    if (!friction_ok) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_FRICTION);
    return true;
}

static inline bool dango_mc_inspect_ledger(const dango_material_contact_solver* solver,
    const dango_material_contact_solver* reference, const dango_material_contact_vec2* velocities,
    bool float_output, dango_material_contact_report* report)
{
    double e0 = 0, e1 = 0, mass_sum = 0, impulse_scale = 0, torque_scale = 0;
    dango_material_contact_vec2 p0 = {0, 0}, p1 = {0, 0}, external = {0, 0};
    double a0 = 0, a1 = 0, external_torque = 0;
    dango_material_contact_vec2 origin = dango_mc_position(&solver->geometry, 16);
    dango_material_contact_vec2 expected[17];
    double velocity_scales[17];
    for (int i = 0; i < 17; ++i)
    {
        expected[i] = reference->input_velocities[i];
        velocity_scales[i] = dango_mc_length(expected[i]);
    }
    for (int i = 0; i < DANGO_MATERIAL_CONTACT_ROWS; ++i)
    {
        const dango_material_contact_row* row = &reference->rows[i];
        if (!row->active) continue;
        for (int j = 0; j < row->count; ++j)
        {
            int node = row->ids[j];
            dango_material_contact_vec2 delta = dango_mc_mul(solver->rows[i].multiplier *
                reference->geometry.inverse_mass[node], row->gradients[j]);
            expected[node] = dango_mc_add(expected[node], delta);
            velocity_scales[node] += dango_mc_length(delta);
        }
    }
    for (int i = 0; i < 17; ++i)
    {
        if (!dango_mc_finite(velocities[i])) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_INVALID_INPUT);
        double mass = 1.0 / solver->geometry.inverse_mass[i];
        mass_sum += mass;
        dango_material_contact_vec2 before = dango_mc_mul(mass, reference->input_velocities[i]);
        dango_material_contact_vec2 after = dango_mc_mul(mass, velocities[i]);
        dango_material_contact_vec2 position = dango_mc_sub(dango_mc_position(&solver->geometry, i), origin);
        p0 = dango_mc_add(p0, before); p1 = dango_mc_add(p1, after);
        e0 += 0.5 * mass * dango_mc_dot(reference->input_velocities[i], reference->input_velocities[i]);
        e1 += 0.5 * mass * dango_mc_dot(velocities[i], velocities[i]);
        a0 += dango_mc_cross(position, before); a1 += dango_mc_cross(position, after);
        impulse_scale += dango_mc_length(before) + dango_mc_length(after);
        torque_scale += dango_mc_length(position) * (dango_mc_length(before) + dango_mc_length(after));
        if (i < 16)
        {
            double n = reference->rows[128 + i].active ? solver->rows[128 + i].multiplier : 0;
            double t = reference->rows[144 + i].active ? solver->rows[144 + i].multiplier : 0;
            dango_material_contact_vec2 impulse = {t, n};
            external = dango_mc_add(external, impulse);
            external_torque += dango_mc_cross(position, impulse);
            impulse_scale += dango_mc_length(impulse);
            torque_scale += dango_mc_length(position) * dango_mc_length(impulse);
        }
    }
    double rounding = float_output ? 8 * FLT_EPSILON : 128 * DBL_EPSILON;
    double length = solver->geometry.radial_length;
    bool velocity_ledger_ok = true;
    report->maximum_relative_velocity_ledger_residual = report->maximum_relative_velocity_ledger_allowance = 0;
    for (int i = 0; i < 17; ++i)
    {
        double residual = dango_mc_length(dango_mc_sub(velocities[i], expected[i]));
        double allowance = rounding * fmax(velocity_scales[i], length * DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
        report->maximum_relative_velocity_ledger_residual = fmax(report->maximum_relative_velocity_ledger_residual, residual / length);
        report->maximum_relative_velocity_ledger_allowance = fmax(report->maximum_relative_velocity_ledger_allowance, allowance / length);
        if (!isfinite(residual) || residual > allowance) velocity_ledger_ok = false;
    }
    // Fixed relative numerical allowances; their small floor is tied to the
    // declared rate tolerance, not an arbitrary unit-energy absolute allowance.
    double energy_floor = mass_sum * length * length * DANGO_MATERIAL_CONTACT_RATE_TOLERANCE * DANGO_MATERIAL_CONTACT_RATE_TOLERANCE;
    report->energy_before = e0; report->energy_after = e1;
    report->ledger_evaluated = true;
    report->energy_allowance = rounding * fmax(e0, energy_floor);
    report->momentum_before = p0; report->momentum_after = p1; report->external_impulse = external;
    report->momentum_residual = dango_mc_length(dango_mc_sub(dango_mc_sub(p1, p0), external));
    report->momentum_allowance = rounding * fmax(impulse_scale, mass_sum * length * DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
    report->angular_momentum_before = a0; report->angular_momentum_after = a1; report->external_torque = external_torque;
    report->angular_momentum_residual = fabs(a1 - a0 - external_torque);
    report->angular_momentum_allowance = rounding * fmax(torque_scale, mass_sum * length * length * DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
    if (!isfinite(e0 + e1) || !isfinite(report->momentum_residual) || !isfinite(report->angular_momentum_residual))
        return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_INVALID_INPUT);
    if (e1 > e0 + report->energy_allowance) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_ENERGY);
    if (!velocity_ledger_ok || report->momentum_residual > report->momentum_allowance ||
        report->angular_momentum_residual > report->angular_momentum_allowance)
        return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_LEDGER);
    return true;
}

static inline bool dango_mc_same_geometry(const dango_constraint_state* a, const dango_constraint_state* b)
{
    if (a->radial_length != b->radial_length || a->perimeter_length != b->perimeter_length ||
        a->radial_min != b->radial_min || a->radial_max != b->radial_max ||
        a->perimeter_min != b->perimeter_min || a->perimeter_max != b->perimeter_max ||
        a->radius != b->radius || a->ground != b->ground) return false;
    for (int i = 0; i < 17; ++i)
        if (a->positions[i].x != b->positions[i].x || a->positions[i].y != b->positions[i].y ||
            a->inverse_mass[i] != b->inverse_mass[i]) return false;
    return true;
}

// Public independent final check. It rebuilds all rows from saved positions and
// original receipts, then checks the complete float state a caller will publish.
// Call again if any later operation (including damping) changes that state.
static inline bool dango_material_contact_validate(const dango_material_contact_solver* solver,
    const dango_constraint_state* final_state, dango_material_contact_report* report)
{
    if (report) { report->ledger_evaluated = false; report->float_output_checked = false; }
    if (!solver || !final_state || !report || !dango_mc_state_valid(final_state))
        return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_INVALID_INPUT);
    *report = solver->report;
    report->ledger_evaluated = false;
    report->float_output_checked = false;
    if (!dango_mc_same_geometry(&solver->geometry, final_state))
        return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_INVALID_GEOMETRY);
    dango_material_contact_solver reference;
    dango_material_contact_report build_report;
    if (!dango_material_contact_build(&solver->geometry, solver->receipts, solver->receipt_count,
        solver->step_seconds, &reference, &build_report))
        return dango_mc_fail(report, build_report.status);
    dango_material_contact_vec2 velocities[17];
    for (int i = 0; i < 17; ++i) velocities[i] = dango_mc_v(final_state->velocities[i].x, final_state->velocities[i].y);
    report->geometry_residual = build_report.geometry_residual; report->float_output_checked = true;
    if (!dango_mc_inspect_rows(solver, &reference, velocities, report) ||
        !dango_mc_inspect_ledger(solver, &reference, velocities, true, report)) return false;
    report->status = DANGO_MATERIAL_CONTACT_OK;
    return true;
}

// The unchanged global positive speed limiter must run AFTER adjudication:
// dissipative internal projection can still increase an individual node's speed.
// Its momentum loss is a separate cap ledger, never a ground/Coulomb impulse.
static inline bool dango_material_contact_validate_after_uniform_scale(
    const dango_material_contact_solver* solver, const dango_constraint_state* contact_output,
    const dango_constraint_state* final_state, float scale, float maximum_speed,
    dango_material_contact_cap_report* report)
{
    if (!report) return false;
    memset(report, 0, sizeof(*report));
    report->scale = scale;
    if (!solver || !contact_output || !final_state || !isfinite(scale) || scale <= 0 || scale > 1 ||
        !isfinite(maximum_speed) || maximum_speed <= 0 || !dango_mc_state_valid(final_state))
    { report->status = DANGO_MATERIAL_CONTACT_INVALID_INPUT; return false; }
    if (!dango_material_contact_validate(solver, contact_output, &report->final_rows))
    { report->status = report->final_rows.status; return false; }
    report->contact_output_checked = true;
    if (!dango_mc_same_geometry(contact_output, final_state))
    { report->status = DANGO_MATERIAL_CONTACT_INVALID_GEOMETRY; return false; }
    for (int i = 0; i < 17; ++i)
    {
        float speed = b2Length(contact_output->velocities[i]);
        if (!isfinite(speed)) { report->status = DANGO_MATERIAL_CONTACT_INVALID_INPUT; return false; }
        report->maximum_before = fmaxf(report->maximum_before, speed);
    }
    float expected_scale = report->maximum_before > maximum_speed ?
        (maximum_speed * 0.99999f) / report->maximum_before : 1.0f;
    if (scale != expected_scale)
    { report->status = DANGO_MATERIAL_CONTACT_SPEED_CAP; return false; }
    dango_material_contact_vec2 final_velocities[17];
    dango_material_contact_vec2 origin = dango_mc_position(&solver->geometry, 16);
    double mass_sum = 0;
    for (int i = 0; i < 17; ++i)
    {
        b2Vec2 expected = b2MulSV(scale, contact_output->velocities[i]);
        if (expected.x != final_state->velocities[i].x || expected.y != final_state->velocities[i].y)
        { report->status = DANGO_MATERIAL_CONTACT_SPEED_CAP; return false; }
        float speed = b2Length(final_state->velocities[i]);
        if (!isfinite(speed)) { report->status = DANGO_MATERIAL_CONTACT_INVALID_INPUT; return false; }
        report->maximum_after = fmaxf(report->maximum_after, speed);
        double mass = 1.0 / final_state->inverse_mass[i]; mass_sum += mass;
        dango_material_contact_vec2 before = dango_mc_v(contact_output->velocities[i].x, contact_output->velocities[i].y);
        final_velocities[i] = dango_mc_v(final_state->velocities[i].x, final_state->velocities[i].y);
        report->energy_before += .5 * mass * dango_mc_dot(before, before);
        report->energy_after += .5 * mass * dango_mc_dot(final_velocities[i], final_velocities[i]);
        dango_material_contact_vec2 change = dango_mc_mul(mass, dango_mc_sub(final_velocities[i], before));
        report->cap_momentum_change = dango_mc_add(report->cap_momentum_change, change);
        report->cap_angular_momentum_change += dango_mc_cross(dango_mc_sub(dango_mc_position(final_state, i), origin), change);
    }
    report->exact_common_scale_checked = true;
    if (report->maximum_after > maximum_speed)
    { report->status = DANGO_MATERIAL_CONTACT_SPEED_CAP; return false; }
    double length = solver->geometry.radial_length;
    double energy_floor = mass_sum * length * length * DANGO_MATERIAL_CONTACT_RATE_TOLERANCE * DANGO_MATERIAL_CONTACT_RATE_TOLERANCE;
    report->energy_allowance = 8 * FLT_EPSILON * fmax(report->energy_before, energy_floor);
    if (!isfinite(report->energy_before + report->energy_after) ||
        report->energy_after > report->energy_before + report->energy_allowance)
    { report->status = DANGO_MATERIAL_CONTACT_ENERGY; return false; }
    dango_material_contact_solver reference;
    dango_material_contact_report build_report;
    if (!dango_material_contact_build(&solver->geometry, solver->receipts, solver->receipt_count,
        solver->step_seconds, &reference, &build_report))
    { report->status = build_report.status; return false; }
    if (!dango_mc_inspect_rows(solver, &reference, final_velocities, &report->final_rows))
    { report->status = report->final_rows.status; return false; }
    report->status = DANGO_MATERIAL_CONTACT_OK;
    return true;
}

// At most 384 alternating sweeps. Failure leaves output untouched, preserving
// the caller's explicit error latch / last legal snapshot contract. This solver
// never substitutes a tiny-update criterion for the independent final gate.
static inline bool dango_material_contact_solve(dango_material_contact_solver* solver,
    dango_constraint_state* output, dango_material_contact_report* report)
{
    if (report) { report->ledger_evaluated = false; report->float_output_checked = false; }
    if (!solver || !output || !report) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_INVALID_INPUT);
    dango_material_contact_solver reference;
    dango_material_contact_report built;
    if (!dango_material_contact_build(&solver->geometry, solver->receipts, solver->receipt_count,
        solver->step_seconds, &reference, &built)) return dango_mc_fail(report, built.status);
    // Every invocation begins a new declared velocity stage. Reusing a solved
    // context without a fresh build would reset neither impulses nor input state.
    if (solver->report.sweeps != 0) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_INVALID_INPUT);
    for (int sweep = 0; sweep < DANGO_MATERIAL_CONTACT_SWEEPS; ++sweep)
    {
        bool reverse = (sweep & 1) != 0;
        for (int ordinal = 0; ordinal < DANGO_MATERIAL_CONTACT_SHAPE_ROWS; ++ordinal)
        {
            int i = reverse ? 127 - ordinal : ordinal;
            if (solver->rows[i].active) dango_mc_unilateral(solver, &solver->rows[i]);
        }
        for (int ordinal = 0; ordinal < 16; ++ordinal)
            dango_mc_contact_pair(solver, reverse ? 15 - ordinal : ordinal);
        solver->report.sweeps = sweep + 1;
        dango_material_contact_report checked = solver->report;
        checked.ledger_evaluated = false;
        if (dango_mc_inspect_rows(solver, &reference, solver->velocities, &checked))
        {
            if (!dango_mc_inspect_ledger(solver, &reference, solver->velocities, false, &checked))
            { solver->report = checked; *report = checked; return false; }
            dango_constraint_state candidate = solver->geometry;
            for (int i = 0; i < 17; ++i)
                candidate.velocities[i] = (b2Vec2){(float)solver->velocities[i].x, (float)solver->velocities[i].y};
            solver->report = checked;
            if (!dango_material_contact_validate(solver, &candidate, &checked))
            {
                solver->report = checked;
                // Float publication is part of the SAME declared budget. A
                // rounded active rate/cone may need another alternating sweep;
                // it never triggers a fresh budget or a relaxed threshold.
                if (checked.status == DANGO_MATERIAL_CONTACT_FINAL_RATE ||
                    checked.status == DANGO_MATERIAL_CONTACT_COMPLEMENTARITY || checked.status == DANGO_MATERIAL_CONTACT_FRICTION)
                    continue;
                *report = checked; return false;
            }
            solver->report = checked; *report = checked; *output = candidate;
            return true;
        }
        solver->report = checked;
        if (checked.status != DANGO_MATERIAL_CONTACT_FINAL_RATE &&
            checked.status != DANGO_MATERIAL_CONTACT_COMPLEMENTARITY && checked.status != DANGO_MATERIAL_CONTACT_FRICTION)
        { *report = checked; return false; }
    }
    solver->report.status = DANGO_MATERIAL_CONTACT_BUDGET;
    *report = solver->report;
    return false;
}

#endif

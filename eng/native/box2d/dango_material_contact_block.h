#ifndef DANGO_MATERIAL_CONTACT_BLOCK_H
#define DANGO_MATERIAL_CONTACT_BLOCK_H

// Fixed-geometry experimental block. This header never steps the world, changes
// material/positions, damps, caps, or silently delegates to the scalar solver.
#include <time.h>
#include "dango_material_contact_solver.h"
#ifdef DANGO_MATERIAL_CONTACT_NULL_REACTION_CANDIDATE
#include "dango_material_contact_null_reaction.h"
#endif

#ifdef DANGO_MATERIAL_CONTACT_EXTENDED_CAPACITY_CANDIDATE
#define DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY 32
#else
#define DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY 16
#endif
#define DANGO_MATERIAL_CONTACT_BLOCK_SOLVES 32

typedef enum dango_material_contact_block_status
{
    DANGO_MATERIAL_CONTACT_BLOCK_OK = 0,
    DANGO_MATERIAL_CONTACT_BLOCK_INVALID_INPUT,
    DANGO_MATERIAL_CONTACT_BLOCK_BUILD,
    DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY_FAILURE,
    DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE,
    DANGO_MATERIAL_CONTACT_BLOCK_RANK,
    DANGO_MATERIAL_CONTACT_BLOCK_REPEATED_MODE,
    DANGO_MATERIAL_CONTACT_BLOCK_BUDGET,
    DANGO_MATERIAL_CONTACT_BLOCK_FINAL
#ifdef DANGO_MATERIAL_CONTACT_NULL_REACTION_CANDIDATE
    , DANGO_MATERIAL_CONTACT_BLOCK_NULL_REACTION
#endif
} dango_material_contact_block_status;

typedef enum dango_material_contact_block_mode
{
    DANGO_MATERIAL_CONTACT_BLOCK_ENABLED = 0,
    DANGO_MATERIAL_CONTACT_BLOCK_RELEASED,
    DANGO_MATERIAL_CONTACT_BLOCK_STICK,
    DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE,
    DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE,
    DANGO_MATERIAL_CONTACT_BLOCK_ZERO
} dango_material_contact_block_mode;

typedef struct dango_material_contact_block_report
{
    dango_material_contact_report final_rows;
    dango_material_contact_block_status status;
    int capacity, solve_limit, active_count, matrix_dimension;
    int linear_solves, full_checks, float_checks, mode_history_count;
    uint64_t pivot_searches, pivot_eliminations;
    double elapsed_seconds;
    bool time_available;
    int active_row_ids[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY];
    dango_material_contact_block_mode modes[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY];
    dango_material_contact_block_mode mode_history[DANGO_MATERIAL_CONTACT_BLOCK_SOLVES][DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY];
    double raw_matrix[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY][DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY];
    double scaled_matrix[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY][DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY];
    double raw_rhs[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY], scaled_rhs[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY];
    double scaling[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY];
    double linear_solution[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY];
    double raw_multipliers[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY];
    double matrix_infinity_norm, pivot_threshold;
    // Each retained diagnostic belongs to an evaluated history entry. The
    // current modes may already be a pending vector rejected by the guard.
    bool matrix_available, linear_solution_available, row_rates_available;
    int matrix_mode_index, solution_mode_index, rates_mode_index;
    // Most recent complete double traversal; final_rows can subsequently refer
    // to the independently inspected float publication.
    double all_row_rates[DANGO_MATERIAL_CONTACT_ROWS];
#ifdef DANGO_MATERIAL_CONTACT_NULL_REACTION_CANDIDATE
    dango_material_contact_null_report null_reaction;
#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
    // Actual certificate traversal cost across every evaluated mode, including
    // a mode whose numerical or subsequent physical gate rejected publication.
    uint64_t operator_certificate_calls, operator_coordinate_visits, operator_coefficient_visits;
    uint64_t operator_residual_visits, operator_row_visits;
    double operator_certificate_elapsed_seconds;
    bool operator_certificate_time_available;
#endif
#endif
} dango_material_contact_block_report;

typedef struct dango_material_contact_block
{
    dango_material_contact_solver contact;
    dango_material_contact_block_report report;
    bool built, attempted;
} dango_material_contact_block;

static inline bool dango_mcb_fail(dango_material_contact_block_report* report,
    dango_material_contact_block_status status)
{ if (report) report->status = status; return false; }

static inline void dango_mcb_report_init(dango_material_contact_block_report* report)
{
    memset(report, 0, sizeof(*report));
    report->capacity = DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY;
    report->solve_limit = DANGO_MATERIAL_CONTACT_BLOCK_SOLVES;
    report->matrix_mode_index = report->solution_mode_index = report->rates_mode_index = -1;
    for (int i = 0; i < DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY; ++i) report->active_row_ids[i] = -1;
#ifdef DANGO_MATERIAL_CONTACT_NULL_REACTION_CANDIDATE
    dango_mcn_report_init(&report->null_reaction);
#endif
}

// The same fixed guard is used by the actual solve loop. Only evaluated mode
// vectors are recorded; a pending 33rd distinct vector does not obtain a solve.
static inline bool dango_mcb_record_modes(dango_material_contact_block_report* report)
{
    if (!report || report->active_count < 0 || report->active_count > DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY ||
        report->mode_history_count < 0 || report->mode_history_count > DANGO_MATERIAL_CONTACT_BLOCK_SOLVES)
        return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_INVALID_INPUT);
    for (int previous = 0; previous < report->mode_history_count; ++previous)
    {
        bool same = true;
        for (int i = 0; i < report->active_count; ++i)
            if (report->modes[i] != report->mode_history[previous][i]) same = false;
        if (same) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_REPEATED_MODE);
    }
    if (report->mode_history_count == DANGO_MATERIAL_CONTACT_BLOCK_SOLVES)
        return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_BUDGET);
    for (int i = 0; i < report->active_count; ++i)
        report->mode_history[report->mode_history_count][i] = report->modes[i];
    ++report->mode_history_count;
    return true;
}

// General complete-pivot elimination of the already normalized system. Original
// row/column identities travel through every permutation, including tie breaks.
static inline bool dango_mcb_linear_solve(int n, const int* row_ids,
    const double matrix[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY][DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY],
    const double* rhs, double* solution, dango_material_contact_block_report* report)
{
    if (!report || !row_ids || !matrix || !rhs || !solution || n <= 0 || n > DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY)
        return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_INVALID_INPUT);
    double a[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY][DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY] = {{0}};
    double b[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY] = {0}, x[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY] = {0};
    int row_permutation[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY], column_permutation[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY];
    double norm = 0;
    for (int i = 0; i < n; ++i)
    {
        row_permutation[i] = column_permutation[i] = i;
        if (!isfinite(rhs[i])) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE);
        b[i] = rhs[i];
        double sum = 0;
        for (int j = 0; j < n; ++j)
        {
            if (!isfinite(matrix[i][j])) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE);
            a[i][j] = matrix[i][j]; sum += fabs(matrix[i][j]);
        }
        if (!isfinite(sum)) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE);
        norm = fmax(norm, sum);
    }
    report->matrix_infinity_norm = norm;
    report->pivot_threshold = 128.0 * n * DBL_EPSILON * norm;
    if (!isfinite(report->pivot_threshold)) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE);
    for (int k = 0; k < n; ++k)
    {
        int pivot_row = -1, pivot_column = -1;
        double largest = -1;
        for (int i = k; i < n; ++i)
            for (int j = k; j < n; ++j)
            {
                ++report->pivot_searches;
                double value = fabs(a[i][j]);
                if (!isfinite(value)) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE);
                bool tie_precedes = pivot_row >= 0 &&
                    (row_ids[row_permutation[i]] < row_ids[row_permutation[pivot_row]] ||
                     (row_ids[row_permutation[i]] == row_ids[row_permutation[pivot_row]] &&
                      row_ids[column_permutation[j]] < row_ids[column_permutation[pivot_column]]));
                if (value > largest || (value == largest && tie_precedes))
                { largest = value; pivot_row = i; pivot_column = j; }
            }
        if (largest <= report->pivot_threshold) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_RANK);
        if (pivot_row != k)
        {
            for (int j = 0; j < n; ++j)
            { double temporary = a[k][j]; a[k][j] = a[pivot_row][j]; a[pivot_row][j] = temporary; }
            double temporary = b[k]; b[k] = b[pivot_row]; b[pivot_row] = temporary;
            int identity = row_permutation[k]; row_permutation[k] = row_permutation[pivot_row]; row_permutation[pivot_row] = identity;
        }
        if (pivot_column != k)
        {
            for (int i = 0; i < n; ++i)
            { double temporary = a[i][k]; a[i][k] = a[i][pivot_column]; a[i][pivot_column] = temporary; }
            int identity = column_permutation[k]; column_permutation[k] = column_permutation[pivot_column]; column_permutation[pivot_column] = identity;
        }
        ++report->pivot_eliminations;
        for (int i = k + 1; i < n; ++i)
        {
            double factor = a[i][k] / a[k][k];
            if (!isfinite(factor)) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE);
            a[i][k] = 0;
            for (int j = k + 1; j < n; ++j)
            {
                a[i][j] -= factor * a[k][j];
                if (!isfinite(a[i][j])) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE);
            }
            b[i] -= factor * b[k];
            if (!isfinite(b[i])) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE);
        }
    }
    for (int i = n - 1; i >= 0; --i)
    {
        double value = b[i];
        for (int j = i + 1; j < n; ++j) value -= a[i][j] * x[j];
        x[i] = value / a[i][i];
        if (!isfinite(x[i])) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE);
    }
    for (int i = 0; i < n; ++i) solution[column_permutation[i]] = x[i];
    return true;
}

static inline double dango_mcb_gram(const dango_material_contact_solver* reference, int a, int b)
{
    const dango_material_contact_row* left = &reference->rows[a];
    const dango_material_contact_row* right = &reference->rows[b];
    double value = 0;
    for (int i = 0; i < left->count; ++i)
        for (int j = 0; j < right->count; ++j)
            if (left->ids[i] == right->ids[j])
                value += reference->geometry.inverse_mass[left->ids[i]] * dango_mc_dot(left->gradients[i], right->gradients[j]);
    return value;
}

static inline bool dango_material_contact_block_build(const dango_constraint_state* state,
    const dango_material_ground_receipt* receipts, int receipt_count, double step_seconds,
    dango_material_contact_block* block, dango_material_contact_block_report* report)
{
    if (report) dango_mcb_report_init(report);
    if (!block || !report) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_INVALID_INPUT);
    memset(block, 0, sizeof(*block)); dango_mcb_report_init(&block->report);
    // Preserve the supplied input even when the shared build rejects it. Receipt
    // storage is fixed; an overflow count is retained without an out-of-bounds copy.
    if (state) block->contact.geometry = *state;
    block->contact.receipt_count = receipt_count; block->contact.step_seconds = step_seconds;
    if (receipts && receipt_count > 0)
        for (int i = 0; i < receipt_count && i < 16; ++i) block->contact.receipts[i] = receipts[i];
    dango_material_contact_report built = {0};
    if (!dango_material_contact_build(state, receipts, receipt_count, step_seconds, &block->contact, &built))
    {
        block->report.final_rows = built; block->report.status = DANGO_MATERIAL_CONTACT_BLOCK_BUILD;
        *report = block->report; return false;
    }
    block->report.final_rows = block->contact.report;
    int count = 0;
    for (int id = 0; id < DANGO_MATERIAL_CONTACT_ROWS; ++id)
        if (block->contact.rows[id].active)
        {
            if (count < DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY) block->report.active_row_ids[count] = id;
            ++count;
        }
    block->report.active_count = count; block->report.matrix_dimension = count;
    if (count > DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY)
    {
        block->report.status = DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY_FAILURE;
        *report = block->report; return false;
    }
    for (int i = 0; i < count; ++i)
    {
        int id = block->report.active_row_ids[i];
        double inverse = block->contact.rows[id].effective_inverse_mass;
        double scale = 1.0 / sqrt(inverse);
        if (!isfinite(inverse) || inverse <= 0 || !isfinite(scale) || scale <= 0)
        {
            block->report.status = DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE;
            *report = block->report; return false;
        }
        block->report.scaling[i] = scale;
        block->report.modes[i] = DANGO_MATERIAL_CONTACT_BLOCK_ENABLED;
        if (id >= 144)
            block->report.modes[i] = dango_mc_friction_bound(&block->contact, id - 144) > 0 ?
                DANGO_MATERIAL_CONTACT_BLOCK_STICK : DANGO_MATERIAL_CONTACT_BLOCK_ZERO;
    }
    block->built = true; *report = block->report; return true;
}

static inline bool dango_mcb_system(const dango_material_contact_solver* reference,
    dango_material_contact_block_report* report)
{
    int n = report->active_count;
#ifdef DANGO_MATERIAL_CONTACT_NULL_REACTION_CANDIDATE
    dango_mcn_report_init(&report->null_reaction);
    report->null_reaction.mode_index = report->mode_history_count - 1;
#endif
    report->matrix_available = report->linear_solution_available = false;
    report->matrix_mode_index = report->solution_mode_index = -1;
    memset(report->raw_matrix, 0, sizeof(report->raw_matrix));
    memset(report->scaled_matrix, 0, sizeof(report->scaled_matrix));
    memset(report->raw_rhs, 0, sizeof(report->raw_rhs));
    memset(report->scaled_rhs, 0, sizeof(report->scaled_rhs));
    memset(report->linear_solution, 0, sizeof(report->linear_solution));
    memset(report->raw_multipliers, 0, sizeof(report->raw_multipliers));
    for (int i = 0; i < n; ++i)
    {
        int id = report->active_row_ids[i];
        dango_material_contact_block_mode mode = report->modes[i];
        bool rate_equation = mode == DANGO_MATERIAL_CONTACT_BLOCK_ENABLED || mode == DANGO_MATERIAL_CONTACT_BLOCK_STICK;
        if (rate_equation)
        {
            for (int j = 0; j < n; ++j) report->raw_matrix[i][j] = dango_mcb_gram(reference, id, report->active_row_ids[j]);
            report->raw_rhs[i] = -dango_mc_rate(&reference->rows[id], reference->input_velocities);
        }
        else
        {
            report->raw_matrix[i][i] = 1;
            if (mode == DANGO_MATERIAL_CONTACT_BLOCK_ZERO) report->raw_rhs[i] = -reference->support[id - 144].base_tangent;
            else if (mode == DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE || mode == DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE)
            {
                int node = id - 144, normal_column = -1;
                for (int j = 0; j < n; ++j) if (report->active_row_ids[j] == 128 + node) normal_column = j;
                if (normal_column < 0) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_INVALID_INPUT);
                double sign = mode == DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE ? 1 : -1;
                const dango_material_contact_support* support = &reference->support[node];
                report->raw_matrix[i][normal_column] = -sign * support->friction;
                report->raw_rhs[i] = sign * (support->friction * support->base_normal + support->receipt_cone_slack) - support->base_tangent;
            }
            else if (mode != DANGO_MATERIAL_CONTACT_BLOCK_RELEASED)
                return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_INVALID_INPUT);
        }
        double equation_scale = rate_equation ? report->scaling[i] : 1;
        double largest = 0;
        for (int j = 0; j < n; ++j)
        {
            report->scaled_matrix[i][j] = equation_scale * report->raw_matrix[i][j] * report->scaling[j];
            if (!isfinite(report->scaled_matrix[i][j])) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE);
            largest = fmax(largest, fabs(report->scaled_matrix[i][j]));
        }
        report->scaled_rhs[i] = equation_scale * report->raw_rhs[i];
        if (!isfinite(report->raw_rhs[i]) || !isfinite(report->scaled_rhs[i]))
            return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE);
        if (largest <= 0) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_RANK);
        for (int j = 0; j < n; ++j) report->scaled_matrix[i][j] /= largest;
        report->scaled_rhs[i] /= largest;
        if (!isfinite(report->scaled_rhs[i])) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE);
    }
    report->matrix_available = true;
    report->matrix_mode_index = report->mode_history_count - 1;
    return true;
}

#ifdef DANGO_MATERIAL_CONTACT_NULL_REACTION_CANDIDATE
typedef enum dango_material_contact_block_null_constraint_kind
{
    DANGO_MATERIAL_CONTACT_BLOCK_NULL_SIGN = 1,
    DANGO_MATERIAL_CONTACT_BLOCK_NULL_FIXED_UPPER,
    DANGO_MATERIAL_CONTACT_BLOCK_NULL_FIXED_LOWER,
    DANGO_MATERIAL_CONTACT_BLOCK_NULL_CUMULATIVE_NORMAL,
    DANGO_MATERIAL_CONTACT_BLOCK_NULL_CONE_POSITIVE,
    DANGO_MATERIAL_CONTACT_BLOCK_NULL_CONE_NEGATIVE
} dango_material_contact_block_null_constraint_kind;

// Fixed physical interval assembly only; selection is a separate, single step.
// The report/reference may be synthetic in unit fixtures, never a world result.
static inline bool dango_mcb_null_interval(const dango_material_contact_solver* reference,
    dango_material_contact_block_report* report)
{
    if (!report) return false;
    dango_material_contact_null_report* null_report = &report->null_reaction;
    if (null_report->status != DANGO_MATERIAL_CONTACT_NULL_OK) return false;
    if (!reference || report->active_count <= 0 || report->active_count > DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY ||
        report->active_count != null_report->dimension)
        return dango_mcn_fail(null_report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
    if (!dango_mcn_interval_init(null_report)) return false;
    int n = report->active_count;
    for (int i = 0; i < n; ++i)
    {
        int id = report->active_row_ids[i];
        dango_material_contact_block_mode mode = report->modes[i];
        if (id < 0 || id >= DANGO_MATERIAL_CONTACT_ROWS || id != null_report->original_row_ids[i] ||
            (i > 0 && id <= report->active_row_ids[i - 1]))
            return dango_mcn_fail(null_report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
        double p = null_report->effective_lambda_particular[i], k = null_report->effective_lambda_direction[i];
        if (id < 144)
        {
            if (mode == DANGO_MATERIAL_CONTACT_BLOCK_ENABLED)
            {
                if (!dango_mcn_interval_add(null_report, id, DANGO_MATERIAL_CONTACT_BLOCK_NULL_SIGN, -k, -p)) return false;
            }
            else if (mode == DANGO_MATERIAL_CONTACT_BLOCK_RELEASED)
            {
                if (!dango_mcn_interval_add(null_report, id, DANGO_MATERIAL_CONTACT_BLOCK_NULL_FIXED_UPPER, k, p) ||
                    !dango_mcn_interval_add(null_report, id, DANGO_MATERIAL_CONTACT_BLOCK_NULL_FIXED_LOWER, -k, -p)) return false;
            }
            else return dango_mcn_fail(null_report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
            if (id >= 128 && !dango_mcn_interval_add(null_report, id, DANGO_MATERIAL_CONTACT_BLOCK_NULL_CUMULATIVE_NORMAL,
                -k, -(reference->support[id - 128].base_normal + p))) return false;
        }
        else
        {
            int node = id - 144, normal_column = -1;
            for (int j = 0; j < n; ++j) if (report->active_row_ids[j] == 128 + node) normal_column = j;
            if (normal_column < 0) return dango_mcn_fail(null_report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
            const dango_material_contact_support* support = &reference->support[node];
            if (mode == DANGO_MATERIAL_CONTACT_BLOCK_ZERO)
            {
                double fixed = support->base_tangent + p;
                if (!dango_mcn_interval_add(null_report, id, DANGO_MATERIAL_CONTACT_BLOCK_NULL_FIXED_UPPER, k, fixed) ||
                    !dango_mcn_interval_add(null_report, id, DANGO_MATERIAL_CONTACT_BLOCK_NULL_FIXED_LOWER, -k, -fixed)) return false;
            }
            else if (mode != DANGO_MATERIAL_CONTACT_BLOCK_STICK && mode != DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE &&
                mode != DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE)
                return dango_mcn_fail(null_report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
            double normal_p = support->base_normal + null_report->effective_lambda_particular[normal_column];
            double normal_k = null_report->effective_lambda_direction[normal_column];
            double bound_p = support->friction * normal_p + support->receipt_cone_slack;
            double bound_k = support->friction * normal_k;
            double total_p = support->base_tangent + p;
            if (!dango_mcn_interval_add(null_report, id, DANGO_MATERIAL_CONTACT_BLOCK_NULL_CONE_POSITIVE, k - bound_k, total_p - bound_p) ||
                !dango_mcn_interval_add(null_report, id, DANGO_MATERIAL_CONTACT_BLOCK_NULL_CONE_NEGATIVE, -k - bound_k, -total_p - bound_p)) return false;
        }
    }
    return true;
}

static inline bool dango_mcb_null_failure(dango_material_contact_block_report* report)
{
    dango_material_contact_null_status status = report->null_reaction.status;
    if (status == DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT)
        return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_INVALID_INPUT);
    if (status == DANGO_MATERIAL_CONTACT_NULL_NONFINITE)
        return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE);
    if (status == DANGO_MATERIAL_CONTACT_NULL_NULLITY)
        return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_RANK);
    return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_NULL_REACTION);
}

#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
static inline void dango_mcb_operator_record(dango_material_contact_block_report* report)
{
    const dango_material_contact_null_report* mode = &report->null_reaction;
    if (mode->operator_certificate_calls == 0) return;
    bool first = report->operator_certificate_calls == 0;
    report->operator_certificate_calls += mode->operator_certificate_calls;
    report->operator_coordinate_visits += mode->operator_coordinate_visits;
    report->operator_coefficient_visits += mode->operator_coefficient_visits;
    report->operator_residual_visits += mode->operator_residual_visits;
    report->operator_row_visits += mode->operator_row_visits;
    report->operator_certificate_elapsed_seconds += mode->operator_certificate_elapsed_seconds;
    report->operator_certificate_time_available =
        (first || report->operator_certificate_time_available) && mode->operator_certificate_time_available &&
        isfinite(report->operator_certificate_elapsed_seconds);
}
#endif

// One factorization per already-budgeted mode. The retained full-rank operations
// are used directly; a raw particular never becomes the block's solved result.
static inline bool dango_mcb_null_solve(const dango_material_contact_solver* reference,
    dango_material_contact_block_report* report)
{
    if (!report) return false;
    int n = report->active_count, mode_index = report->matrix_mode_index;
    if (!reference || !report->matrix_available || n <= 0 || n > DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY)
        return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_INVALID_INPUT);
    dango_material_contact_null_report* null_report = &report->null_reaction;
    bool factored = dango_mcn_factor(n, report->active_row_ids, report->scaled_matrix,
        report->scaled_rhs, report->linear_solution, null_report);
    null_report->mode_index = mode_index;
    if (null_report->factor_available) null_report->factor_mode_index = mode_index;
    if (null_report->raw_pair_available) null_report->raw_pair_mode_index = mode_index;
    report->pivot_searches += null_report->pivot_searches;
    report->pivot_eliminations += null_report->pivot_eliminations;
    report->matrix_infinity_norm = null_report->matrix_infinity_norm;
    report->pivot_threshold = null_report->pivot_threshold;
    if (!factored) return dango_mcb_null_failure(report);
    if (null_report->nullity == 0) return true;
    bool fixed[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY] = {false}; double fixed_values[DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY] = {0};
    for (int i = 0; i < n; ++i)
    {
        if (report->modes[i] == DANGO_MATERIAL_CONTACT_BLOCK_RELEASED) fixed[i] = true;
        else if (report->modes[i] == DANGO_MATERIAL_CONTACT_BLOCK_ZERO)
        {
            int id = report->active_row_ids[i];
            if (id < 144 || id >= 160)
            { dango_mcn_fail(null_report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT); return dango_mcb_null_failure(report); }
            fixed[i] = true; fixed_values[i] = -reference->support[id - 144].base_tangent;
        }
    }
#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
    bool solved = dango_mcn_effective_pair(n, report->active_row_ids, report->scaling, fixed, fixed_values,
        report->scaled_matrix, report->scaled_rhs, null_report) &&
        dango_mcn_reaction_null(reference, n, report->active_row_ids, null_report) &&
        dango_mcb_null_interval(reference, report) && dango_mcn_interval_select(null_report) &&
        dango_mcn_selected(n, report->scaling, report->scaled_matrix, report->scaled_rhs, null_report,
            report->raw_multipliers, report->linear_solution);
    dango_mcb_operator_record(report);
    if (!solved) return dango_mcb_null_failure(report);
#else
    if (!dango_mcn_effective_pair(n, report->active_row_ids, report->scaling, fixed, fixed_values,
        report->scaled_matrix, report->scaled_rhs, null_report) ||
        !dango_mcn_reaction_null(reference, n, report->active_row_ids, null_report) ||
        !dango_mcb_null_interval(reference, report) || !dango_mcn_interval_select(null_report) ||
        !dango_mcn_selected(n, report->scaling, report->scaled_matrix, report->scaled_rhs, null_report,
            report->raw_multipliers, report->linear_solution)) return dango_mcb_null_failure(report);
#endif
    return true;
}
#endif

// Complete traversal uses the exact shared physical gates, including strict
// multiplier signs. Unlike the scalar helper, a negative multiplier does not
// stop the traversal before the other 159 rows have been inspected.
static inline bool dango_mcb_full_rows(const dango_material_contact_solver* contact,
    const dango_material_contact_solver* reference, dango_material_contact_block_report* block_report)
{
    dango_material_contact_report* report = &block_report->final_rows;
    *report = reference->report;
    report->ledger_evaluated = report->float_output_checked = false;
    bool finite = true, rate_ok = true, complementarity_ok = true, friction_ok = true;
    double scale = reference->geometry.radial_length;
    for (int id = 0; id < DANGO_MATERIAL_CONTACT_ROWS; ++id)
    {
        const dango_material_contact_row* row = &reference->rows[id];
        double multiplier = contact->rows[id].multiplier;
        double rate = dango_mc_rate(row, contact->velocities);
        block_report->all_row_rates[id] = rate;
        if (!isfinite(multiplier) || !isfinite(rate)) finite = false;
        if ((!row->active && multiplier != 0) || (id < 144 && multiplier < 0)) complementarity_ok = false;
        if (!row->active || !isfinite(rate) || !isfinite(multiplier)) continue;
        if (id < 144)
        {
            bool angular = dango_mc_is_angle(row->kind);
            double normalized = angular ? rate : rate / scale;
            double closing = fmax(0, -normalized), complement = multiplier > 0 ? fmax(0, normalized) : 0;
            if (angular)
            {
                report->maximum_angular_closing = fmax(report->maximum_angular_closing, closing);
                report->maximum_angular_complementarity = fmax(report->maximum_angular_complementarity, complement);
            }
            else
            {
                report->maximum_linear_closing = fmax(report->maximum_linear_closing, fmax(0, -rate));
                report->maximum_relative_linear_closing = fmax(report->maximum_relative_linear_closing, closing);
                report->maximum_relative_linear_complementarity = fmax(report->maximum_relative_linear_complementarity, complement);
            }
            if (closing > DANGO_MATERIAL_CONTACT_RATE_TOLERANCE) rate_ok = false;
            if (complement > DANGO_MATERIAL_CONTACT_RATE_TOLERANCE) complementarity_ok = false;
        }
        else
        {
            int node = id - 144;
            const dango_material_contact_support* support = &reference->support[node];
            double physical = support->friction * (support->base_normal + contact->rows[128 + node].multiplier);
            double bound = physical + support->receipt_cone_slack, total = support->base_tangent + multiplier;
            double epsilon = 32 * DBL_EPSILON * fmax(bound, fabs(total));
            double excess = fmax(0, fabs(total) - physical);
            report->maximum_friction_cone_excess = fmax(report->maximum_friction_cone_excess, excess);
            if (!isfinite(bound) || !isfinite(total)) finite = false;
            if (!isfinite(bound) || bound < 0 || excess > support->receipt_cone_slack + epsilon) friction_ok = false;
            double violation = 0;
            if (bound > 0)
            {
                if (total <= -bound + epsilon) violation = fmax(0, -rate);
                else if (total >= bound - epsilon) violation = fmax(0, rate);
                else violation = fabs(rate);
            }
            report->maximum_relative_friction_rate = fmax(report->maximum_relative_friction_rate, violation / scale);
            if (violation / scale > DANGO_MATERIAL_CONTACT_RATE_TOLERANCE) friction_ok = false;
        }
    }
    ++block_report->full_checks;
    block_report->row_rates_available = true;
    block_report->rates_mode_index = block_report->mode_history_count - 1;
    if (!finite) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_INVALID_INPUT);
    if (!rate_ok) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_FINAL_RATE);
    if (!complementarity_ok) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_COMPLEMENTARITY);
    if (!friction_ok) return dango_mc_fail(report, DANGO_MATERIAL_CONTACT_FRICTION);
    report->status = DANGO_MATERIAL_CONTACT_OK; return true;
}

#ifdef DANGO_MATERIAL_CONTACT_FRICTION_MODE_PRIORITY_CANDIDATE
static inline bool dango_mcb_trial_seen(const dango_material_contact_block_report* report,
    int change, dango_material_contact_block_mode mode)
{
    if (!report || report->active_count < 0 || report->active_count > DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY ||
        change < 0 || change >= report->active_count ||
        report->mode_history_count < 0 || report->mode_history_count > DANGO_MATERIAL_CONTACT_BLOCK_SOLVES) return true;
    for (int previous = 0; previous < report->mode_history_count; ++previous)
    {
        bool same = true;
        for (int i = 0; i < report->active_count; ++i)
            if ((i == change ? mode : report->modes[i]) != report->mode_history[previous][i]) same = false;
        if (same) return true;
    }
    return false;
}
#endif

// Geometry/IDs are immutable. Each trial changes one mode and rebuilds the
// system, including the tangent budget affected by its paired normal.
static inline int dango_mcb_next_mode(const dango_material_contact_solver* contact,
    const dango_material_contact_solver* reference, const dango_material_contact_block_report* report,
    dango_material_contact_block_mode* next_mode)
{
#ifdef DANGO_MATERIAL_CONTACT_FRICTION_MODE_PRIORITY_CANDIDATE
#ifdef DANGO_MATERIAL_CONTACT_CONE_ACTIVATION_STICK_CANDIDATE
    // A tangent leaving a zero cone can choose a sliding equation whose coupled
    // normal reaction makes its own cone negative. Try the unseen stick mode
    // before unrelated negative-reaction releases erase that causal transition.
    if (report->mode_history_count >= 2)
        for (int i = 0; i < report->active_count; ++i)
        {
            int id = report->active_row_ids[i];
            dango_material_contact_block_mode mode = report->modes[i];
            if (id < 144 || (mode != DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE &&
                mode != DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE) ||
                !(dango_mc_friction_bound(contact, id - 144) < 0) ||
                report->mode_history[report->mode_history_count - 2][i] != DANGO_MATERIAL_CONTACT_BLOCK_ZERO ||
                dango_mcb_trial_seen(report, i, DANGO_MATERIAL_CONTACT_BLOCK_STICK)) continue;
            bool sole_transition = true;
            for (int j = 0; j < report->active_count; ++j)
                if (j != i && report->mode_history[report->mode_history_count - 2][j] != report->modes[j]) sole_transition = false;
            if (sole_transition) { *next_mode = DANGO_MATERIAL_CONTACT_BLOCK_STICK; return i; }
        }
#endif
    // Repair infeasible compressive reactions before allowing a previously
    // released row to reactivate and recreate the same impossible mode.
    int negative = -1; double negative_scaled = 0;
    for (int i = 0; i < report->active_count; ++i)
    {
        int id = report->active_row_ids[i];
        if (id < 144 && report->modes[i] == DANGO_MATERIAL_CONTACT_BLOCK_ENABLED &&
            contact->rows[id].multiplier < 0)
        {
            double scaled = contact->rows[id].multiplier / report->scaling[i];
            if (scaled < negative_scaled) { negative = i; negative_scaled = scaled; }
        }
    }
    if (negative >= 0) { *next_mode = DANGO_MATERIAL_CONTACT_BLOCK_RELEASED; return negative; }
    // Bring tangent modes into the current normal cone before reactivating
    // unilateral rows. In particular a released normal can leave a stale stick
    // equation with a zero cone; reactivating shape rows first cycles that mode.
    for (int i = 0; i < report->active_count; ++i)
    {
        int id = report->active_row_ids[i];
        if (id < 144) continue;
        int node = id - 144;
        dango_material_contact_block_mode mode = report->modes[i];
        double bound = dango_mc_friction_bound(contact, node);
        double total = reference->support[node].base_tangent + contact->rows[id].multiplier;
        double epsilon = 32 * DBL_EPSILON * fmax(bound, fabs(total));
        if (bound == 0 && mode != DANGO_MATERIAL_CONTACT_BLOCK_ZERO)
        { *next_mode = DANGO_MATERIAL_CONTACT_BLOCK_ZERO; return i; }
        if (bound > 0 && mode == DANGO_MATERIAL_CONTACT_BLOCK_ZERO)
        {
#ifdef DANGO_MATERIAL_CONTACT_ZERO_TO_SLIDE_CANDIDATE
            double rate = dango_mc_rate(&reference->rows[id], contact->velocities);
            double normalized = rate / reference->geometry.radial_length;
            *next_mode = normalized > DANGO_MATERIAL_CONTACT_RATE_TOLERANCE ? DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE :
                (normalized < -DANGO_MATERIAL_CONTACT_RATE_TOLERANCE ? DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE : DANGO_MATERIAL_CONTACT_BLOCK_STICK);
#else
            *next_mode = DANGO_MATERIAL_CONTACT_BLOCK_STICK;
#endif
            return i;
        }
        if (mode == DANGO_MATERIAL_CONTACT_BLOCK_STICK && fabs(total) > bound + epsilon)
        {
            *next_mode = total > 0 ? DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE : DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE;
#ifdef DANGO_MATERIAL_CONTACT_OUTCONE_FRESH_TRIAL_CANDIDATE
            dango_material_contact_block_mode opposite = total > 0 ?
                DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE : DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE;
            if (dango_mcb_trial_seen(report, i, *next_mode) && !dango_mcb_trial_seen(report, i, opposite)) *next_mode = opposite;
#endif
            return i;
        }
    }
    // Resolve every cone violation before undoing a slide trial: another
    // contact's stale stick equation can reverse the first trial's slip rate.
    for (int i = 0; i < report->active_count; ++i)
    {
        int id = report->active_row_ids[i];
        if (id < 144) continue;
        dango_material_contact_block_mode mode = report->modes[i];
        double sign = mode == DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE ? 1 :
            (mode == DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE ? -1 : 0);
        double rate = dango_mc_rate(&reference->rows[id], contact->velocities);
        if (sign != 0 && sign * rate / reference->geometry.radial_length > DANGO_MATERIAL_CONTACT_RATE_TOLERANCE)
        {
            // The coupled normal cone can make the stick extrapolation choose
            // the wrong sliding branch. If stick already failed in this exact
            // other-row mode, trial the opposite branch once under the same
            // history/budget and complete physical checks.
#ifdef DANGO_MATERIAL_CONTACT_FRESH_TRIAL_CANDIDATE
            dango_material_contact_block_mode opposite = sign > 0 ?
                DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE : DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE;
            *next_mode = dango_mcb_trial_seen(report, i, DANGO_MATERIAL_CONTACT_BLOCK_STICK) &&
                !dango_mcb_trial_seen(report, i, opposite) ? opposite : DANGO_MATERIAL_CONTACT_BLOCK_STICK;
#else
            *next_mode = DANGO_MATERIAL_CONTACT_BLOCK_STICK;
#endif
            return i;
        }
    }
    int closing = -1; double closing_rate = -DANGO_MATERIAL_CONTACT_RATE_TOLERANCE;
    for (int i = 0; i < report->active_count; ++i)
    {
        int id = report->active_row_ids[i];
        if (id >= 144 || report->modes[i] != DANGO_MATERIAL_CONTACT_BLOCK_RELEASED) continue;
        double rate = dango_mc_rate(&reference->rows[id], contact->velocities);
        double normalized = dango_mc_is_angle(reference->rows[id].kind) ? rate : rate / reference->geometry.radial_length;
        if (normalized < closing_rate) { closing = i; closing_rate = normalized; }
    }
    if (closing >= 0) { *next_mode = DANGO_MATERIAL_CONTACT_BLOCK_ENABLED; return closing; }
#endif
    for (int i = 0; i < report->active_count; ++i)
    {
        int id = report->active_row_ids[i];
        dango_material_contact_block_mode mode = report->modes[i];
        double multiplier = contact->rows[id].multiplier;
        double rate = dango_mc_rate(&reference->rows[id], contact->velocities);
        if (id < 144)
        {
            double normalized = dango_mc_is_angle(reference->rows[id].kind) ? rate : rate / reference->geometry.radial_length;
            if (mode == DANGO_MATERIAL_CONTACT_BLOCK_ENABLED && multiplier < 0)
            { *next_mode = DANGO_MATERIAL_CONTACT_BLOCK_RELEASED; return i; }
            if (mode == DANGO_MATERIAL_CONTACT_BLOCK_RELEASED && normalized < -DANGO_MATERIAL_CONTACT_RATE_TOLERANCE)
            { *next_mode = DANGO_MATERIAL_CONTACT_BLOCK_ENABLED; return i; }
        }
        else
        {
            int node = id - 144;
            double bound = dango_mc_friction_bound(contact, node);
            double total = reference->support[node].base_tangent + multiplier;
            double epsilon = 32 * DBL_EPSILON * fmax(bound, fabs(total));
            if (bound == 0 && mode != DANGO_MATERIAL_CONTACT_BLOCK_ZERO)
            { *next_mode = DANGO_MATERIAL_CONTACT_BLOCK_ZERO; return i; }
            if (bound > 0 && mode == DANGO_MATERIAL_CONTACT_BLOCK_ZERO)
            { *next_mode = DANGO_MATERIAL_CONTACT_BLOCK_STICK; return i; }
            if (mode == DANGO_MATERIAL_CONTACT_BLOCK_STICK && fabs(total) > bound + epsilon)
            { *next_mode = total > 0 ? DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE : DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE; return i; }
            double sign = mode == DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE ? 1 :
                (mode == DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE ? -1 : 0);
            if (sign != 0 && sign * rate / reference->geometry.radial_length > DANGO_MATERIAL_CONTACT_RATE_TOLERANCE)
            { *next_mode = DANGO_MATERIAL_CONTACT_BLOCK_STICK; return i; }
        }
    }
    return -1;
}

static inline bool dango_mcb_modes_valid(const dango_material_contact_solver* contact,
    const dango_material_contact_solver* reference, const dango_material_contact_vec2* velocities,
    dango_material_contact_block_report* report)
{
    for (int i = 0; i < report->active_count; ++i)
    {
        int id = report->active_row_ids[i];
        dango_material_contact_block_mode mode = report->modes[i];
        double multiplier = contact->rows[id].multiplier, rate = dango_mc_rate(&reference->rows[id], velocities);
        if (id < 144)
        {
            double normalized = dango_mc_is_angle(reference->rows[id].kind) ? rate : rate / reference->geometry.radial_length;
            if ((mode == DANGO_MATERIAL_CONTACT_BLOCK_ENABLED && (multiplier < 0 || fabs(normalized) > DANGO_MATERIAL_CONTACT_RATE_TOLERANCE)) ||
                (mode == DANGO_MATERIAL_CONTACT_BLOCK_RELEASED && (multiplier != 0 || normalized < -DANGO_MATERIAL_CONTACT_RATE_TOLERANCE)))
                return dango_mc_fail(&report->final_rows, DANGO_MATERIAL_CONTACT_COMPLEMENTARITY);
        }
        else
        {
            int node = id - 144;
            double bound = dango_mc_friction_bound(contact, node), total = reference->support[node].base_tangent + multiplier;
            double epsilon = 32 * DBL_EPSILON * fmax(bound, fabs(total));
            if (mode == DANGO_MATERIAL_CONTACT_BLOCK_ZERO && (bound != 0 || total != 0))
                return dango_mc_fail(&report->final_rows, DANGO_MATERIAL_CONTACT_FRICTION);
            if (mode == DANGO_MATERIAL_CONTACT_BLOCK_STICK && (bound <= 0 || fabs(rate / reference->geometry.radial_length) > DANGO_MATERIAL_CONTACT_RATE_TOLERANCE))
                return dango_mc_fail(&report->final_rows, DANGO_MATERIAL_CONTACT_FRICTION);
            double sign = mode == DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE ? 1 :
                (mode == DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE ? -1 : 0);
            if (sign != 0 && (bound <= 0 || fabs(total - sign * bound) > epsilon ||
                sign * rate / reference->geometry.radial_length > DANGO_MATERIAL_CONTACT_RATE_TOLERANCE))
                return dango_mc_fail(&report->final_rows, DANGO_MATERIAL_CONTACT_FRICTION);
        }
    }
    return true;
}

#if defined(DANGO_MATERIAL_CONTACT_MODE_TRANSITION_CANDIDATE)
// An empty reaction interval rejects the current active mode, not necessarily
// the physical problem. Only a fully certified particular may identify the next
// mode. This temporary state is never a selected solution or published output.
static inline bool dango_mcb_null_next_mode(const dango_material_contact_solver* reference,
    dango_material_contact_block_report* report)
{
    if (!reference || !report || report->active_count <= 0 ||
        report->active_count > DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY) return false;
    const dango_material_contact_null_report* nr = &report->null_reaction;
    if (nr->status != DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY ||
        !nr->factor_passed || !nr->pair_checks_passed || !nr->reaction_checks_passed ||
        !nr->factor_available || !nr->raw_pair_available || !nr->effective_pair_available ||
        !nr->pair_checks_available || !nr->reaction_checks_available ||
        !nr->interval_available || nr->interval_passed || nr->selected_pair_available ||
        nr->nullity != 1 || nr->dimension != report->active_count ||
        nr->mode_index != report->matrix_mode_index ||
        nr->factor_mode_index != nr->mode_index || nr->raw_pair_mode_index != nr->mode_index ||
        nr->effective_pair_mode_index != nr->mode_index || nr->pair_checks_mode_index != nr->mode_index ||
        nr->reaction_mode_index != nr->mode_index || nr->interval_mode_index != nr->mode_index ||
        nr->used || nr->selection_available)
        return false;
    dango_material_contact_solver probe = *reference;
    for (int i = 0; i < report->active_count; ++i)
    {
        int id = report->active_row_ids[i];
        if (id < 0 || id >= DANGO_MATERIAL_CONTACT_ROWS || !reference->rows[id].active ||
            nr->original_row_ids[i] != id || !isfinite(nr->effective_lambda_particular[i])) return false;
        probe.rows[id].multiplier = nr->effective_lambda_particular[i];
    }
    for (int id = 0; id < DANGO_MATERIAL_CONTACT_ROWS; ++id)
    {
        const dango_material_contact_row* row = &probe.rows[id];
        if (!row->active) continue;
        for (int j = 0; j < row->count; ++j)
        {
            int node = row->ids[j];
            dango_material_contact_vec2 delta = dango_mc_mul(reference->geometry.inverse_mass[node] * row->multiplier, row->gradients[j]);
            probe.velocities[node] = dango_mc_add(probe.velocities[node], delta);
            if (!dango_mc_finite(probe.velocities[node])) return false;
        }
    }
    dango_material_contact_block_mode next = DANGO_MATERIAL_CONTACT_BLOCK_ENABLED;
    int change = dango_mcb_next_mode(&probe, reference, report, &next);
    if (change < 0) return false;
    report->modes[change] = next;
    return true;
}
#endif

#if defined(DANGO_MATERIAL_CONTACT_RANK_MODE_CANDIDATE)
// A dependent enabled shape equality can be removed from the current mode,
// while its unilateral physical row remains active and is checked in full.
// This is a bounded mode trial, never acceptance of an inconsistent solution.
static inline bool dango_mcb_rank_next_mode(const dango_material_contact_solver* reference,
    dango_material_contact_block_report* report)
{
    if (!reference || !report || report->active_count <= 0 ||
        report->active_count > DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY) return false;
    const dango_material_contact_null_report* nr = &report->null_reaction;
    if ((nr->status != DANGO_MATERIAL_CONTACT_NULL_NULLITY && nr->status != DANGO_MATERIAL_CONTACT_NULL_INCONSISTENT &&
        nr->status != DANGO_MATERIAL_CONTACT_NULL_REACTION) ||
        !report->matrix_available || !nr->factor_available || nr->rank < 0 || nr->rank >= nr->dimension ||
        nr->dimension != report->active_count || nr->nullity != nr->dimension - nr->rank ||
        nr->factor_mode_index != report->matrix_mode_index || nr->mode_index != report->matrix_mode_index ||
        nr->used || nr->selection_available || nr->selected_pair_available) return false;
    for (int j = nr->rank; j < nr->dimension; ++j)
    {
        int id = nr->column_permutation[j];
        if (id < 0 || id >= 128 || !reference->rows[id].active) continue;
        for (int i = 0; i < report->active_count; ++i)
            if (report->active_row_ids[i] == id && report->modes[i] == DANGO_MATERIAL_CONTACT_BLOCK_ENABLED)
            {
#ifdef DANGO_MATERIAL_CONTACT_FRESH_TRIAL_CANDIDATE
                if (dango_mcb_trial_seen(report, i, DANGO_MATERIAL_CONTACT_BLOCK_RELEASED)) continue;
#endif
                report->modes[i] = DANGO_MATERIAL_CONTACT_BLOCK_RELEASED;
                return true;
            }
    }
    // Complete pivoting may retain the dependent shape in the basis and leave
    // a tangent column free. Try the first enabled unilateral shape instead;
    // the unchanged final gates decide whether that mode is physically legal.
    for (int i = 0; i < report->active_count; ++i)
    {
        int id = report->active_row_ids[i];
        if (id >= 0 && id < 128 && reference->rows[id].active &&
            report->modes[i] == DANGO_MATERIAL_CONTACT_BLOCK_ENABLED)
        {
#ifdef DANGO_MATERIAL_CONTACT_FRESH_TRIAL_CANDIDATE
            if (dango_mcb_trial_seen(report, i, DANGO_MATERIAL_CONTACT_BLOCK_RELEASED)) continue;
#endif
            report->modes[i] = DANGO_MATERIAL_CONTACT_BLOCK_RELEASED;
            return true;
        }
    }
    return false;
}
#endif

static inline bool dango_mcb_finish(dango_material_contact_block* block,
    dango_material_contact_block_report* report, const struct timespec* started, bool timed, bool result)
{
    struct timespec finished;
    if (timed && timespec_get(&finished, TIME_UTC) == TIME_UTC)
    {
        double elapsed = (double)(finished.tv_sec - started->tv_sec) + 1e-9 * (finished.tv_nsec - started->tv_nsec);
        if (isfinite(elapsed) && elapsed >= 0)
        { block->report.elapsed_seconds = elapsed; block->report.time_available = true; }
    }
    block->contact.report = block->report.final_rows;
    *report = block->report; return result;
}

static inline bool dango_material_contact_block_solve(dango_material_contact_block* block,
    dango_constraint_state* output, dango_material_contact_block_report* report)
{
    if (report)
    {
        report->final_rows.ledger_evaluated = false; report->final_rows.float_output_checked = false;
        report->time_available = false; report->elapsed_seconds = 0;
    }
    if (!block || !output || !report) return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_INVALID_INPUT);
    if (!block->built || block->attempted)
    {
        *report = block->report; report->final_rows.ledger_evaluated = report->final_rows.float_output_checked = false;
        report->time_available = false; report->elapsed_seconds = 0;
        return dango_mcb_fail(report, DANGO_MATERIAL_CONTACT_BLOCK_INVALID_INPUT);
    }
    block->attempted = true;
    struct timespec started = {0}; bool timed = timespec_get(&started, TIME_UTC) == TIME_UTC;
    block->report.final_rows.ledger_evaluated = block->report.final_rows.float_output_checked = false;
    dango_material_contact_solver reference;
    dango_material_contact_report built = {0};
    if (!dango_material_contact_build(&block->contact.geometry, block->contact.receipts, block->contact.receipt_count,
        block->contact.step_seconds, &reference, &built))
    {
        block->report.final_rows = built; block->report.status = DANGO_MATERIAL_CONTACT_BLOCK_BUILD;
        return dango_mcb_finish(block, report, &started, timed, false);
    }
    // A caller cannot seed a partial block or a cross-step mode/impulse warm
    // start by changing diagnostic fields after build. The complete membership
    // and natural initial modes must still match the independently rebuilt input.
    int expected_count = 0;
    bool membership_ok = block->report.active_count >= 0 &&
        block->report.active_count <= DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY &&
        block->report.mode_history_count == 0 && block->report.linear_solves == 0;
    for (int id = 0; id < DANGO_MATERIAL_CONTACT_ROWS; ++id)
        if (reference.rows[id].active)
        {
            if (expected_count >= DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY) membership_ok = false;
            else
            {
                dango_material_contact_block_mode initial = id < 144 ? DANGO_MATERIAL_CONTACT_BLOCK_ENABLED :
                    (dango_mc_friction_bound(&reference, id - 144) > 0 ? DANGO_MATERIAL_CONTACT_BLOCK_STICK : DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
                if (block->report.active_row_ids[expected_count] != id || block->report.modes[expected_count] != initial ||
                    block->report.scaling[expected_count] != 1.0 / sqrt(reference.rows[id].effective_inverse_mass)) membership_ok = false;
            }
            ++expected_count;
        }
    if (!membership_ok || expected_count != block->report.active_count)
    {
        block->report.status = DANGO_MATERIAL_CONTACT_BLOCK_INVALID_INPUT;
        return dango_mcb_finish(block, report, &started, timed, false);
    }
    block->contact = reference;
    for (;;)
    {
        int n = block->report.active_count;
        if (n > 0)
        {
            if (!dango_mcb_record_modes(&block->report)) return dango_mcb_finish(block, report, &started, timed, false);
            ++block->report.linear_solves;
#ifdef DANGO_MATERIAL_CONTACT_NULL_REACTION_CANDIDATE
#ifdef DANGO_IDLE_TRACE_MODE_EVENT
            bool trace_built = dango_mcb_system(&reference, &block->report);
            bool trace_solved = trace_built && dango_mcb_null_solve(&reference, &block->report);
            DANGO_IDLE_TRACE_MODE_EVENT(&block->report, trace_built, trace_solved);
            if (!trace_solved)
#else
            if (!dango_mcb_system(&reference, &block->report) || !dango_mcb_null_solve(&reference, &block->report))
#endif
#else
            if (!dango_mcb_system(&reference, &block->report) ||
                !dango_mcb_linear_solve(n, block->report.active_row_ids, block->report.scaled_matrix,
                    block->report.scaled_rhs, block->report.linear_solution, &block->report))
#endif
            {
#if defined(DANGO_MATERIAL_CONTACT_MODE_TRANSITION_CANDIDATE)
                if (dango_mcb_null_next_mode(&reference, &block->report)) continue;
#endif
#if defined(DANGO_MATERIAL_CONTACT_RANK_MODE_CANDIDATE)
                if (dango_mcb_rank_next_mode(&reference, &block->report)) continue;
#endif
                return dango_mcb_finish(block, report, &started, timed, false);
            }
            block->report.linear_solution_available = true;
            block->report.solution_mode_index = block->report.mode_history_count - 1;
            for (int id = 0; id < DANGO_MATERIAL_CONTACT_ROWS; ++id) block->contact.rows[id].multiplier = 0;
            for (int i = 0; i < n; ++i)
            {
                int id = block->report.active_row_ids[i];
#ifdef DANGO_MATERIAL_CONTACT_NULL_REACTION_CANDIDATE
                double multiplier = block->report.null_reaction.used ? block->report.null_reaction.selected_lambda[i] :
                    block->report.scaling[i] * block->report.linear_solution[i];
#else
                double multiplier = block->report.scaling[i] * block->report.linear_solution[i];
#endif
                block->report.raw_multipliers[i] = multiplier;
                if (!isfinite(multiplier))
                { block->report.status = DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE; return dango_mcb_finish(block, report, &started, timed, false); }
                // RELEASED and ZERO prescribe fixed unknowns in their identity
                // equations. Reconstruct those exact values, retaining the raw
                // solve above. This is not clipping a negative reaction or a
                // stick solution: nonfixed multipliers are never changed, and
                // the actual identities/velocities face every independent gate.
                if (block->report.modes[i] == DANGO_MATERIAL_CONTACT_BLOCK_RELEASED) multiplier = 0;
                else if (block->report.modes[i] == DANGO_MATERIAL_CONTACT_BLOCK_ZERO) multiplier = -reference.support[id - 144].base_tangent;
                block->contact.rows[id].multiplier = multiplier;
            }
        }
        for (int node = 0; node < 17; ++node) block->contact.velocities[node] = reference.input_velocities[node];
        for (int id = 0; id < DANGO_MATERIAL_CONTACT_ROWS; ++id)
        {
            const dango_material_contact_row* row = &reference.rows[id];
            if (!row->active) continue;
            for (int j = 0; j < row->count; ++j)
            {
                int node = row->ids[j];
                dango_material_contact_vec2 delta = dango_mc_mul(reference.geometry.inverse_mass[node] * block->contact.rows[id].multiplier, row->gradients[j]);
                block->contact.velocities[node] = dango_mc_add(block->contact.velocities[node], delta);
            }
        }
        // Rebuild the complete reference on every successful linear adjudication.
        // It never inherits solver modes, multipliers, gradients, or cached rates.
        dango_material_contact_solver rebuilt;
        if (!dango_material_contact_build(&reference.geometry, reference.receipts, reference.receipt_count,
            reference.step_seconds, &rebuilt, &built))
        {
            block->report.final_rows = built; block->report.status = DANGO_MATERIAL_CONTACT_BLOCK_BUILD;
            return dango_mcb_finish(block, report, &started, timed, false);
        }
        bool rows_ok = dango_mcb_full_rows(&block->contact, &rebuilt, &block->report);
        if (block->report.final_rows.status == DANGO_MATERIAL_CONTACT_INVALID_INPUT)
        {
            block->report.status = DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE;
            return dango_mcb_finish(block, report, &started, timed, false);
        }
        dango_material_contact_block_mode next_mode = DANGO_MATERIAL_CONTACT_BLOCK_ENABLED;
        int change = dango_mcb_next_mode(&block->contact, &rebuilt, &block->report, &next_mode);
        if (change >= 0) { block->report.modes[change] = next_mode; continue; }
        if (!rows_ok || !dango_mcb_modes_valid(&block->contact, &rebuilt, block->contact.velocities, &block->report) ||
            !dango_mc_inspect_ledger(&block->contact, &rebuilt, block->contact.velocities, false, &block->report.final_rows))
        {
            block->report.status = DANGO_MATERIAL_CONTACT_BLOCK_FINAL;
            return dango_mcb_finish(block, report, &started, timed, false);
        }
        dango_constraint_state candidate = reference.geometry;
        for (int node = 0; node < 17; ++node)
            candidate.velocities[node] = (b2Vec2){(float)block->contact.velocities[node].x, (float)block->contact.velocities[node].y};
        block->contact.report = block->report.final_rows;
        ++block->report.float_checks;
        if (!dango_material_contact_validate(&block->contact, &candidate, &block->report.final_rows))
        {
            block->report.status = DANGO_MATERIAL_CONTACT_BLOCK_FINAL;
            return dango_mcb_finish(block, report, &started, timed, false);
        }
        dango_material_contact_vec2 published[17];
        for (int node = 0; node < 17; ++node) published[node] = dango_mc_v(candidate.velocities[node].x, candidate.velocities[node].y);
        if (!dango_mcb_modes_valid(&block->contact, &rebuilt, published, &block->report))
        {
            block->report.status = DANGO_MATERIAL_CONTACT_BLOCK_FINAL;
            return dango_mcb_finish(block, report, &started, timed, false);
        }
        block->report.status = DANGO_MATERIAL_CONTACT_BLOCK_OK; *output = candidate;
        return dango_mcb_finish(block, report, &started, timed, true);
    }
}

// Validate the actual capped float publication against the same block modes.
// The original cap ledger still describes the unscaled contact reactions;
// this extra rate check must not replace or mutate that reaction ledger.
static inline bool dango_material_contact_block_validate_after_uniform_scale(
    const dango_material_contact_block* block, const dango_constraint_state* contact_output,
    const dango_constraint_state* final_state, float scale, float maximum_speed,
    dango_material_contact_cap_report* report)
{
    if (!report) return false;
    if (!block || !block->attempted || block->report.status != DANGO_MATERIAL_CONTACT_BLOCK_OK)
    {
        memset(report, 0, sizeof(*report)); report->status = DANGO_MATERIAL_CONTACT_INVALID_INPUT;
        return false;
    }
    if (!dango_material_contact_validate_after_uniform_scale(&block->contact, contact_output,
        final_state, scale, maximum_speed, report)) return false;
    dango_material_contact_solver reference;
    dango_material_contact_report built;
    if (!dango_material_contact_build(&block->contact.geometry, block->contact.receipts,
        block->contact.receipt_count, block->contact.step_seconds, &reference, &built))
    { report->status = report->final_rows.status = built.status; return false; }
    dango_material_contact_vec2 published[17];
    for (int node = 0; node < 17; ++node)
        published[node] = dango_mc_v(final_state->velocities[node].x, final_state->velocities[node].y);
    dango_material_contact_block_report mode_report = block->report;
    if (!dango_mcb_modes_valid(&block->contact, &reference, published, &mode_report))
    { report->status = report->final_rows.status = mode_report.final_rows.status; return false; }
    return true;
}

#endif

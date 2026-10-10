#ifndef DANGO_MATERIAL_CONTACT_NULL_REACTION_H
#define DANGO_MATERIAL_CONTACT_NULL_REACTION_H

// Private, bounded one-dimensional redundant-reaction candidate. It never
// removes a physical row, regularizes a matrix, searches modes, or steps a world.
#include "dango_material_contact_solver.h"

#define DANGO_MATERIAL_CONTACT_NULL_CAPACITY 16
#define DANGO_MATERIAL_CONTACT_NULL_CONSTRAINTS 64

typedef enum dango_material_contact_null_status
{
    DANGO_MATERIAL_CONTACT_NULL_OK = 0,
    DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT,
    DANGO_MATERIAL_CONTACT_NULL_NONFINITE,
    DANGO_MATERIAL_CONTACT_NULL_NULLITY,
    DANGO_MATERIAL_CONTACT_NULL_INCONSISTENT,
    DANGO_MATERIAL_CONTACT_NULL_REACTION,
    DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY,
    DANGO_MATERIAL_CONTACT_NULL_SELECTED_RESIDUAL
} dango_material_contact_null_status;

typedef struct dango_material_contact_null_constraint
{
    int row_id, kind;
    double a, b; // a*alpha+b <= 0, with no interval tolerance.
    bool has_endpoint;
    double endpoint, lower_after, upper_after;
    bool lower_bounded_after, upper_bounded_after;
} dango_material_contact_null_constraint;

typedef struct dango_material_contact_null_report
{
    dango_material_contact_null_status status;
    int mode_index, dimension, rank, nullity, free_column_id;
    // A selected reaction was supplied to the current internal block solve.
    // This is not public output success; later physical/float/cap gates may fail.
    bool used;
    uint64_t pivot_searches, pivot_eliminations;
    double matrix_infinity_norm, pivot_threshold;
    int original_row_ids[16], row_permutation[16], column_permutation[16];
    bool factor_available, raw_pair_available, effective_pair_available;
    bool pair_checks_available, reaction_checks_available, interval_available;
    bool selection_available, selected_pair_available;
    // Availability means diagnostic data exists, including a rejected stage.
    // Passing latches are independent and are required by the next stage.
    bool factor_passed, pair_checks_passed, reaction_checks_passed;
    bool interval_passed, selection_passed, selected_checks_passed;
    int factor_mode_index, raw_pair_mode_index, effective_pair_mode_index;
    int pair_checks_mode_index, reaction_mode_index, interval_mode_index;
    int selection_mode_index, selected_mode_index;
    double raw_z_particular[16], raw_z_direction[16];
    double raw_lambda_particular[16], raw_lambda_direction[16];
    double effective_z_particular[16], effective_z_direction[16];
    double effective_lambda_particular[16], effective_lambda_direction[16];
    double direction_normalization;
    double particular_residual[16], particular_allowance[16];
    double direction_residual[16], direction_allowance[16];
    int first_particular_row_id, first_direction_row_id, first_reaction_body;
    dango_material_contact_vec2 reaction_delta[17];
    double reaction_scale[17], reaction_residual[17], reaction_allowance[17];
    int constraint_count, empty_row_id;
    dango_material_contact_null_constraint constraints[DANGO_MATERIAL_CONTACT_NULL_CONSTRAINTS];
    bool lower_bounded, upper_bounded;
    double lower, upper, selected_alpha;
    double selected_lambda[16], selected_z[16];
    double selected_residual[16], selected_allowance[16];
    int first_selected_row_id;
} dango_material_contact_null_report;

static inline void dango_mcn_report_init(dango_material_contact_null_report* report)
{
    memset(report, 0, sizeof(*report));
    report->mode_index = report->free_column_id = report->empty_row_id = -1;
    report->factor_mode_index = report->raw_pair_mode_index = report->effective_pair_mode_index = -1;
    report->pair_checks_mode_index = report->reaction_mode_index = report->interval_mode_index = -1;
    report->selection_mode_index = report->selected_mode_index = -1;
    report->first_particular_row_id = report->first_direction_row_id = -1;
    report->first_reaction_body = report->first_selected_row_id = -1;
}

static inline bool dango_mcn_fail(dango_material_contact_null_report* report,
    dango_material_contact_null_status status)
{
    if (report)
    {
        if (report->status == DANGO_MATERIAL_CONTACT_NULL_OK) report->status = status;
        report->used = false;
    }
    return false;
}

static inline void dango_mcn_clear_selected(dango_material_contact_null_report* report)
{
    report->used = report->selected_pair_available = report->selected_checks_passed = false;
    report->selected_mode_index = report->first_selected_row_id = -1;
    memset(report->selected_lambda, 0, sizeof(report->selected_lambda));
    memset(report->selected_z, 0, sizeof(report->selected_z));
    memset(report->selected_residual, 0, sizeof(report->selected_residual));
    memset(report->selected_allowance, 0, sizeof(report->selected_allowance));
}

static inline void dango_mcn_clear_selection(dango_material_contact_null_report* report)
{
    dango_mcn_clear_selected(report);
    report->selection_available = report->selection_passed = false;
    report->selection_mode_index = -1; report->selected_alpha = 0;
}

static inline void dango_mcn_clear_interval(dango_material_contact_null_report* report)
{
    dango_mcn_clear_selection(report);
    report->interval_available = report->interval_passed = false;
    report->interval_mode_index = report->empty_row_id = -1; report->constraint_count = 0;
    report->lower_bounded = report->upper_bounded = false;
    report->lower = report->upper = 0;
    memset(report->constraints, 0, sizeof(report->constraints));
}

static inline void dango_mcn_clear_reaction(dango_material_contact_null_report* report)
{
    dango_mcn_clear_interval(report);
    report->reaction_checks_available = report->reaction_checks_passed = false;
    report->reaction_mode_index = report->first_reaction_body = -1;
    memset(report->reaction_delta, 0, sizeof(report->reaction_delta));
    memset(report->reaction_scale, 0, sizeof(report->reaction_scale));
    memset(report->reaction_residual, 0, sizeof(report->reaction_residual));
    memset(report->reaction_allowance, 0, sizeof(report->reaction_allowance));
}

static inline void dango_mcn_clear_effective(dango_material_contact_null_report* report)
{
    dango_mcn_clear_reaction(report);
    report->effective_pair_available = report->pair_checks_available = report->pair_checks_passed = false;
    report->effective_pair_mode_index = report->pair_checks_mode_index = -1;
    report->first_particular_row_id = report->first_direction_row_id = -1;
    report->direction_normalization = 0;
    memset(report->raw_lambda_particular, 0, sizeof(report->raw_lambda_particular));
    memset(report->raw_lambda_direction, 0, sizeof(report->raw_lambda_direction));
    memset(report->effective_lambda_particular, 0, sizeof(report->effective_lambda_particular));
    memset(report->effective_lambda_direction, 0, sizeof(report->effective_lambda_direction));
    memset(report->effective_z_particular, 0, sizeof(report->effective_z_particular));
    memset(report->effective_z_direction, 0, sizeof(report->effective_z_direction));
    memset(report->particular_residual, 0, sizeof(report->particular_residual));
    memset(report->particular_allowance, 0, sizeof(report->particular_allowance));
    memset(report->direction_residual, 0, sizeof(report->direction_residual));
    memset(report->direction_allowance, 0, sizeof(report->direction_allowance));
}

// One factorization only. Full rank uses exactly the scalar operations, complete
// pivot selection and output publication of the retained block linear routine.
// Rank n-1 retains raw z_p (free=0), q (free=1); it does not publish a solution.
static inline bool dango_mcn_factor(int n, const int* row_ids,
    const double matrix[16][16], const double* rhs, double* solution,
    dango_material_contact_null_report* report)
{
    if (!report) return false;
    dango_mcn_report_init(report);
    if (!row_ids || !matrix || !rhs || !solution || n <= 0 || n > 16)
        return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
    report->dimension = n;
    double a[16][16] = {{0}}, b[16] = {0}, x[16] = {0}, q[16] = {0};
    int rows[16], columns[16];
    double norm = 0;
    for (int i = 0; i < n; ++i)
    {
        report->original_row_ids[i] = row_ids[i];
        rows[i] = columns[i] = i;
        if (!isfinite(rhs[i])) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
        b[i] = rhs[i];
        double sum = 0;
        for (int j = 0; j < n; ++j)
        {
            if (!isfinite(matrix[i][j])) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
            a[i][j] = matrix[i][j]; sum += fabs(matrix[i][j]);
        }
        if (!isfinite(sum)) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
        norm = fmax(norm, sum);
    }
    report->matrix_infinity_norm = norm;
    report->pivot_threshold = 128.0 * n * DBL_EPSILON * norm;
    if (!isfinite(report->pivot_threshold)) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
    int rank = n;
    for (int k = 0; k < n; ++k)
    {
        int pivot_row = -1, pivot_column = -1;
        double largest = -1;
        for (int i = k; i < n; ++i)
            for (int j = k; j < n; ++j)
            {
                ++report->pivot_searches;
                double value = fabs(a[i][j]);
                if (!isfinite(value)) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
                bool tie_precedes = pivot_row >= 0 &&
                    (row_ids[rows[i]] < row_ids[rows[pivot_row]] ||
                     (row_ids[rows[i]] == row_ids[rows[pivot_row]] && row_ids[columns[j]] < row_ids[columns[pivot_column]]));
                if (value > largest || (value == largest && tie_precedes))
                { largest = value; pivot_row = i; pivot_column = j; }
            }
        if (largest <= report->pivot_threshold) { rank = k; break; }
        if (pivot_row != k)
        {
            for (int j = 0; j < n; ++j)
            { double temporary = a[k][j]; a[k][j] = a[pivot_row][j]; a[pivot_row][j] = temporary; }
            double temporary = b[k]; b[k] = b[pivot_row]; b[pivot_row] = temporary;
            int identity = rows[k]; rows[k] = rows[pivot_row]; rows[pivot_row] = identity;
        }
        if (pivot_column != k)
        {
            for (int i = 0; i < n; ++i)
            { double temporary = a[i][k]; a[i][k] = a[i][pivot_column]; a[i][pivot_column] = temporary; }
            int identity = columns[k]; columns[k] = columns[pivot_column]; columns[pivot_column] = identity;
        }
        ++report->pivot_eliminations;
        for (int i = k + 1; i < n; ++i)
        {
            double factor = a[i][k] / a[k][k];
            if (!isfinite(factor)) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
            a[i][k] = 0;
            for (int j = k + 1; j < n; ++j)
            {
                a[i][j] -= factor * a[k][j];
                if (!isfinite(a[i][j])) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
            }
            b[i] -= factor * b[k];
            if (!isfinite(b[i])) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
        }
    }
    report->rank = rank; report->nullity = n - rank; report->factor_available = true;
    report->factor_mode_index = report->mode_index;
    for (int i = 0; i < n; ++i)
    { report->row_permutation[i] = row_ids[rows[i]]; report->column_permutation[i] = row_ids[columns[i]]; }
    if (n - rank > 1) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NULLITY);
    if (rank < n) { report->free_column_id = row_ids[columns[rank]]; q[rank] = 1; }
    for (int i = rank - 1; i >= 0; --i)
    {
        double value = b[i];
        for (int j = i + 1; j < n; ++j) value -= a[i][j] * x[j];
        x[i] = value / a[i][i];
        if (!isfinite(x[i])) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
        if (rank < n)
        {
            double direction = 0;
            for (int j = i + 1; j < n; ++j) direction -= a[i][j] * q[j];
            q[i] = direction / a[i][i];
            if (!isfinite(q[i])) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
        }
    }
    if (rank == n)
    {
        for (int i = 0; i < n; ++i) solution[columns[i]] = x[i];
        report->factor_passed = true;
        return true;
    }
    for (int i = 0; i < n; ++i)
    { report->raw_z_particular[columns[i]] = x[i]; report->raw_z_direction[columns[i]] = q[i]; }
    report->raw_pair_available = report->factor_passed = true;
    report->raw_pair_mode_index = report->mode_index; return true;
}

static inline bool dango_mcn_row_residuals(int n, const double matrix[16][16],
    const double* rhs, const double* z, bool homogeneous, double* residuals, double* allowances)
{
    if (!matrix || !rhs || !z || !residuals || !allowances || n <= 0 || n > 16) return false;
    bool valid = true;
    for (int i = 0; i < n; ++i)
    {
        double sum = 0, scale = homogeneous ? 0 : fabs(rhs[i]);
        for (int j = 0; j < n; ++j)
        { double term = matrix[i][j] * z[j]; sum += term; scale += fabs(term); }
        residuals[i] = sum - (homogeneous ? 0 : rhs[i]);
        allowances[i] = 128.0 * n * DBL_EPSILON * scale;
        if (!isfinite(sum) || !isfinite(scale) || !isfinite(residuals[i]) || !isfinite(allowances[i]) ||
            fabs(residuals[i]) > allowances[i]) valid = false;
    }
    return valid;
}

static inline bool dango_mcn_effective_pair(int n, const int* row_ids, const double* scaling,
    const bool* fixed, const double* fixed_values, const double matrix[16][16], const double* rhs,
    dango_material_contact_null_report* report)
{
    if (!report) return false;
    if (report->status != DANGO_MATERIAL_CONTACT_NULL_OK) return false;
    dango_mcn_clear_effective(report);
    if (!row_ids || !scaling || !fixed || !fixed_values || !matrix || !rhs || n <= 0 || n > 16 ||
        n != report->dimension || !report->factor_passed || !report->raw_pair_available || report->nullity != 1 ||
        report->factor_mode_index != report->mode_index || report->raw_pair_mode_index != report->mode_index)
        return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
    double largest = 0;
    int first = -1;
    for (int i = 0; i < n; ++i)
    {
        if (row_ids[i] != report->original_row_ids[i])
            return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
        if (!isfinite(scaling[i]) || scaling[i] <= 0) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
        double particular = scaling[i] * report->raw_z_particular[i];
        double direction = scaling[i] * report->raw_z_direction[i];
        report->raw_lambda_particular[i] = particular; report->raw_lambda_direction[i] = direction;
        if (!isfinite(particular) || !isfinite(direction) || (fixed[i] && !isfinite(fixed_values[i])))
            return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
        if (fixed[i]) { particular = fixed_values[i]; direction = 0; }
        report->effective_lambda_particular[i] = particular; report->effective_lambda_direction[i] = direction;
        largest = fmax(largest, fabs(direction));
        if (direction != 0 && (first < 0 || row_ids[i] < row_ids[first])) first = i;
    }
    if (largest <= 0 || first < 0) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_REACTION);
    double signed_norm = report->effective_lambda_direction[first] > 0 ? largest : -largest;
    report->direction_normalization = signed_norm;
    for (int i = 0; i < n; ++i)
    {
        report->effective_lambda_direction[i] /= signed_norm;
        report->effective_z_particular[i] = report->effective_lambda_particular[i] / scaling[i];
        report->effective_z_direction[i] = report->effective_lambda_direction[i] / scaling[i];
        if (!isfinite(report->effective_z_particular[i]) || !isfinite(report->effective_z_direction[i]))
            return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
    }
    report->effective_pair_available = true; report->effective_pair_mode_index = report->mode_index;
    bool particular_ok = dango_mcn_row_residuals(n, matrix, rhs, report->effective_z_particular, false,
        report->particular_residual, report->particular_allowance);
    bool direction_ok = dango_mcn_row_residuals(n, matrix, rhs, report->effective_z_direction, true,
        report->direction_residual, report->direction_allowance);
    report->pair_checks_available = true; report->pair_checks_mode_index = report->mode_index;
    for (int i = 0; i < n; ++i)
    {
        if (report->first_particular_row_id < 0 && fabs(report->particular_residual[i]) > report->particular_allowance[i])
            report->first_particular_row_id = row_ids[i];
        if (report->first_direction_row_id < 0 && fabs(report->direction_residual[i]) > report->direction_allowance[i])
            report->first_direction_row_id = row_ids[i];
    }
    if (!particular_ok || !direction_ok) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INCONSISTENT);
    report->pair_checks_passed = true;
    return true;
}

static inline bool dango_mcn_reaction_null(const dango_material_contact_solver* reference,
    int n, const int* row_ids, dango_material_contact_null_report* report)
{
    if (!report) return false;
    if (report->status != DANGO_MATERIAL_CONTACT_NULL_OK) return false;
    dango_mcn_clear_reaction(report);
    if (!reference || !row_ids || n <= 0 || n > 16 || n != report->dimension ||
        !report->pair_checks_passed || !report->effective_pair_available ||
        report->effective_pair_mode_index != report->mode_index || report->pair_checks_mode_index != report->mode_index)
        return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
    bool valid = true;
    for (int body = 0; body < 17; ++body)
    { report->reaction_delta[body] = dango_mc_v(0, 0); report->reaction_scale[body] = 0; }
    for (int i = 0; i < n; ++i)
    {
        int id = row_ids[i];
        if (id != report->original_row_ids[i] || id < 0 || id >= DANGO_MATERIAL_CONTACT_ROWS)
            return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
        const dango_material_contact_row* row = &reference->rows[id];
        if (row->count < 0 || row->count > 4) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
        for (int j = 0; j < row->count; ++j)
        {
            int body = row->ids[j];
            if (body < 0 || body >= 17) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
            dango_material_contact_vec2 contribution = dango_mc_mul(reference->geometry.inverse_mass[body] *
                report->effective_lambda_direction[i], row->gradients[j]);
            report->reaction_delta[body] = dango_mc_add(report->reaction_delta[body], contribution);
            report->reaction_scale[body] += dango_mc_length(contribution);
        }
    }
    for (int body = 0; body < 17; ++body)
    {
        report->reaction_residual[body] = dango_mc_length(report->reaction_delta[body]);
        report->reaction_allowance[body] = 128.0 * n * DBL_EPSILON * report->reaction_scale[body];
        if (!isfinite(report->reaction_residual[body]) || !isfinite(report->reaction_allowance[body]) ||
            report->reaction_residual[body] > report->reaction_allowance[body])
        { valid = false; if (report->first_reaction_body < 0) report->first_reaction_body = body; }
    }
    report->reaction_checks_available = true; report->reaction_mode_index = report->mode_index;
    if (!valid) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_REACTION);
    report->reaction_checks_passed = true;
    return true;
}

static inline bool dango_mcn_interval_init(dango_material_contact_null_report* report)
{
    if (!report) return false;
    if (report->status != DANGO_MATERIAL_CONTACT_NULL_OK) return false;
    dango_mcn_clear_interval(report);
    if (!report->reaction_checks_passed || report->reaction_mode_index != report->mode_index)
        return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
    report->interval_available = true; report->interval_mode_index = report->mode_index;
    report->interval_passed = true; return true;
}

static inline bool dango_mcn_interval_add(dango_material_contact_null_report* report,
    int row_id, int kind, double a, double b)
{
    if (!report) return false;
    if (report->status != DANGO_MATERIAL_CONTACT_NULL_OK) return false;
    dango_mcn_clear_selection(report);
    if (!report->interval_available || !report->interval_passed || !report->reaction_checks_passed ||
        report->interval_mode_index != report->mode_index || report->reaction_mode_index != report->mode_index ||
        report->constraint_count < 0 || report->constraint_count >= DANGO_MATERIAL_CONTACT_NULL_CONSTRAINTS)
        return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
    dango_material_contact_null_constraint* constraint = &report->constraints[report->constraint_count++];
    report->interval_passed = false;
    memset(constraint, 0, sizeof(*constraint)); constraint->row_id = row_id; constraint->kind = kind;
    constraint->a = a; constraint->b = b;
    if (!isfinite(a) || !isfinite(b)) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
    if (a == 0)
    {
        if (b > 0) { report->empty_row_id = row_id; return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY); }
    }
    else
    {
        double endpoint = -b / a;
        if (!isfinite(endpoint)) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
        constraint->has_endpoint = true; constraint->endpoint = endpoint;
        if (a > 0)
        {
            if (!report->upper_bounded || endpoint < report->upper) report->upper = endpoint;
            report->upper_bounded = true;
        }
        else
        {
            if (!report->lower_bounded || endpoint > report->lower) report->lower = endpoint;
            report->lower_bounded = true;
        }
    }
    constraint->lower_after = report->lower; constraint->upper_after = report->upper;
    constraint->lower_bounded_after = report->lower_bounded; constraint->upper_bounded_after = report->upper_bounded;
    if (report->lower_bounded && report->upper_bounded && report->lower > report->upper)
    { report->empty_row_id = row_id; return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY); }
    report->interval_passed = true;
    return true;
}

static inline bool dango_mcn_interval_select(dango_material_contact_null_report* report)
{
    if (!report) return false;
    if (report->status != DANGO_MATERIAL_CONTACT_NULL_OK) return false;
    dango_mcn_clear_selection(report);
    if (!report->interval_available || !report->interval_passed || !report->reaction_checks_passed ||
        report->interval_mode_index != report->mode_index || report->reaction_mode_index != report->mode_index ||
        report->constraint_count < 0 || report->constraint_count > DANGO_MATERIAL_CONTACT_NULL_CONSTRAINTS)
        return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
    if (report->lower_bounded && report->upper_bounded && report->lower > report->upper)
        return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY);
    double alpha = 0;
    if (report->lower_bounded && alpha < report->lower) alpha = report->lower;
    else if (report->upper_bounded && alpha > report->upper) alpha = report->upper;
    if (!isfinite(alpha)) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
    report->selected_alpha = alpha; report->selection_available = true; report->selection_mode_index = report->mode_index;
    // Arithmetic at a selected closed endpoint may still fail. There is exactly
    // one selection: never move the endpoint or grant a general cone allowance.
    for (int i = 0; i < report->constraint_count; ++i)
    {
        double residual = report->constraints[i].a * alpha + report->constraints[i].b;
        if (!isfinite(residual)) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
        if (residual > 0) { report->empty_row_id = report->constraints[i].row_id; return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY); }
    }
    report->selection_passed = true;
    return true;
}

static inline bool dango_mcn_selected(int n, const double* scaling, const double matrix[16][16],
    const double* rhs, dango_material_contact_null_report* report, double* multipliers, double* solution)
{
    if (!report) return false;
    if (report->status != DANGO_MATERIAL_CONTACT_NULL_OK) return false;
    dango_mcn_clear_selected(report);
    if (!scaling || !matrix || !rhs || !multipliers || !solution || n <= 0 || n > 16 ||
        n != report->dimension || !report->selection_available || !report->selection_passed ||
        !report->pair_checks_passed || !report->reaction_checks_passed || !report->interval_passed ||
        report->selection_mode_index != report->mode_index || report->pair_checks_mode_index != report->mode_index ||
        report->reaction_mode_index != report->mode_index || report->interval_mode_index != report->mode_index ||
        !report->effective_pair_available)
        return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
    for (int i = 0; i < n; ++i)
    {
        if (!isfinite(scaling[i]) || scaling[i] <= 0) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
        report->selected_lambda[i] = report->effective_lambda_particular[i] + report->selected_alpha * report->effective_lambda_direction[i];
        report->selected_z[i] = report->selected_lambda[i] / scaling[i];
        if (!isfinite(report->selected_lambda[i]) || !isfinite(report->selected_z[i]))
            return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
    }
    report->selected_pair_available = true; report->selected_mode_index = report->mode_index;
    if (!dango_mcn_row_residuals(n, matrix, rhs, report->selected_z, false, report->selected_residual, report->selected_allowance))
    {
        for (int i = 0; i < n; ++i)
            if (fabs(report->selected_residual[i]) > report->selected_allowance[i])
            { report->first_selected_row_id = report->original_row_ids[i]; break; }
        return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_SELECTED_RESIDUAL);
    }
    for (int i = 0; i < n; ++i) { multipliers[i] = report->selected_lambda[i]; solution[i] = report->selected_z[i]; }
    report->used = report->selected_checks_passed = true; return true;
}

#endif

#ifndef DANGO_MATERIAL_CONTACT_NULL_REACTION_H
#define DANGO_MATERIAL_CONTACT_NULL_REACTION_H

// Private, bounded one-dimensional redundant-reaction candidate. It never
// removes a physical row, regularizes a matrix, searches modes, or steps a world.
#include "dango_material_contact_solver.h"
#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
#include <time.h>
#endif

#ifdef DANGO_MATERIAL_CONTACT_EXTENDED_CAPACITY_CANDIDATE
#define DANGO_MATERIAL_CONTACT_NULL_CAPACITY 32
#define DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY 32
#else
#define DANGO_MATERIAL_CONTACT_NULL_CAPACITY 16
#define DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY 17
#endif
#ifdef DANGO_MATERIAL_CONTACT_EXTENDED_CAPACITY_CANDIDATE
#define DANGO_MATERIAL_CONTACT_NULL_CONSTRAINTS 128
#else
#define DANGO_MATERIAL_CONTACT_NULL_CONSTRAINTS 64
#endif

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

#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
typedef struct dango_material_contact_operator_certificate
{
    bool available, finite, passed;
    int mode_index, dimension;
    uint64_t coordinate_visits, operator_visits, residual_visits, rows_checked;
    double elapsed_seconds;
    bool time_available;
    // Matrix stages use n entries; the reaction stage uses all 17 bodies.
    double operator_scale[DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY], coordinate_scale[DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY], rhs_scale[DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY], scale[DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY];
    bool evaluated[DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY], operator_finite[DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY], coordinate_finite[DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY], rhs_finite[DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY], scale_finite[DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY];
    bool zero_operator[DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY], zero_coordinate[DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY], zero_scale[DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY];
} dango_material_contact_operator_certificate;

static inline void dango_mcn_operator_certificate_init(dango_material_contact_operator_certificate* certificate)
{ memset(certificate, 0, sizeof(*certificate)); certificate->mode_index = -1; }

// Time is optional telemetry, never a certificate or publication gate.
static inline void dango_mcn_operator_certificate_finish(dango_material_contact_operator_certificate* certificate,
    const struct timespec* started, bool timed)
{
    struct timespec finished;
    if (timed && timespec_get(&finished, TIME_UTC) == TIME_UTC)
    {
        double elapsed = (double)(finished.tv_sec - started->tv_sec) + 1e-9 * (finished.tv_nsec - started->tv_nsec);
        if (isfinite(elapsed) && elapsed >= 0)
        { certificate->elapsed_seconds = elapsed; certificate->time_available = true; }
    }
}
#endif

typedef struct dango_material_contact_null_report
{
    dango_material_contact_null_status status;
    int mode_index, dimension, rank, nullity, free_column_id;
    // A selected reaction was supplied to the current internal block solve.
    // This is not public output success; later physical/float/cap gates may fail.
    bool used;
    uint64_t pivot_searches, pivot_eliminations;
    double matrix_infinity_norm, pivot_threshold;
    int original_row_ids[DANGO_MATERIAL_CONTACT_NULL_CAPACITY], row_permutation[DANGO_MATERIAL_CONTACT_NULL_CAPACITY], column_permutation[DANGO_MATERIAL_CONTACT_NULL_CAPACITY];
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
    double raw_z_particular[DANGO_MATERIAL_CONTACT_NULL_CAPACITY], raw_z_direction[DANGO_MATERIAL_CONTACT_NULL_CAPACITY];
    double raw_lambda_particular[DANGO_MATERIAL_CONTACT_NULL_CAPACITY], raw_lambda_direction[DANGO_MATERIAL_CONTACT_NULL_CAPACITY];
    double effective_z_particular[DANGO_MATERIAL_CONTACT_NULL_CAPACITY], effective_z_direction[DANGO_MATERIAL_CONTACT_NULL_CAPACITY];
    double effective_lambda_particular[DANGO_MATERIAL_CONTACT_NULL_CAPACITY], effective_lambda_direction[DANGO_MATERIAL_CONTACT_NULL_CAPACITY];
    double direction_normalization;
    double particular_residual[DANGO_MATERIAL_CONTACT_NULL_CAPACITY], particular_allowance[DANGO_MATERIAL_CONTACT_NULL_CAPACITY];
    double direction_residual[DANGO_MATERIAL_CONTACT_NULL_CAPACITY], direction_allowance[DANGO_MATERIAL_CONTACT_NULL_CAPACITY];
    int first_particular_row_id, first_direction_row_id, first_reaction_body;
    dango_material_contact_vec2 reaction_delta[17];
    double reaction_scale[DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY], reaction_residual[17], reaction_allowance[17];
    int constraint_count, empty_row_id;
    dango_material_contact_null_constraint constraints[DANGO_MATERIAL_CONTACT_NULL_CONSTRAINTS];
    bool lower_bounded, upper_bounded;
    double lower, upper, selected_alpha;
    double selected_lambda[DANGO_MATERIAL_CONTACT_NULL_CAPACITY], selected_z[DANGO_MATERIAL_CONTACT_NULL_CAPACITY];
    double selected_residual[DANGO_MATERIAL_CONTACT_NULL_CAPACITY], selected_allowance[DANGO_MATERIAL_CONTACT_NULL_CAPACITY];
    int first_selected_row_id;
#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
    dango_material_contact_operator_certificate particular_certificate, direction_certificate;
    dango_material_contact_operator_certificate reaction_certificate, selected_certificate;
    // Actual work across all certificate invocations in this evaluated mode,
    // including a rejected stage or a legitimate stage re-evaluation.
    uint64_t operator_certificate_calls, operator_coordinate_visits, operator_coefficient_visits;
    uint64_t operator_residual_visits, operator_row_visits;
    double operator_certificate_elapsed_seconds;
    bool operator_certificate_time_available;
#endif
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
#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
    dango_mcn_operator_certificate_init(&report->particular_certificate);
    dango_mcn_operator_certificate_init(&report->direction_certificate);
    dango_mcn_operator_certificate_init(&report->reaction_certificate);
    dango_mcn_operator_certificate_init(&report->selected_certificate);
#endif
}

#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
static inline void dango_mcn_operator_record(dango_material_contact_null_report* report,
    const dango_material_contact_operator_certificate* certificate)
{
    if (!certificate->available) return;
    bool first = report->operator_certificate_calls == 0;
    ++report->operator_certificate_calls;
    report->operator_coordinate_visits += certificate->coordinate_visits;
    report->operator_coefficient_visits += certificate->operator_visits;
    report->operator_residual_visits += certificate->residual_visits;
    report->operator_row_visits += certificate->rows_checked;
    report->operator_certificate_elapsed_seconds += certificate->elapsed_seconds;
    report->operator_certificate_time_available =
        (first || report->operator_certificate_time_available) && certificate->time_available &&
        isfinite(report->operator_certificate_elapsed_seconds);
}
#endif

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
#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
    dango_mcn_operator_certificate_init(&report->selected_certificate);
#endif
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
#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
    dango_mcn_operator_certificate_init(&report->reaction_certificate);
#endif
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
#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
    dango_mcn_operator_certificate_init(&report->particular_certificate);
    dango_mcn_operator_certificate_init(&report->direction_certificate);
#endif
}

// One factorization only. Full rank uses exactly the scalar operations, complete
// pivot selection and output publication of the retained block linear routine.
// Rank n-1 retains raw z_p (free=0), q (free=1); it does not publish a solution.
static inline bool dango_mcn_factor(int n, const int* row_ids,
    const double matrix[DANGO_MATERIAL_CONTACT_NULL_CAPACITY][DANGO_MATERIAL_CONTACT_NULL_CAPACITY], const double* rhs, double* solution,
    dango_material_contact_null_report* report)
{
    if (!report) return false;
    dango_mcn_report_init(report);
    if (!row_ids || !matrix || !rhs || !solution || n <= 0 || n > DANGO_MATERIAL_CONTACT_NULL_CAPACITY)
        return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
    report->dimension = n;
    double a[DANGO_MATERIAL_CONTACT_NULL_CAPACITY][DANGO_MATERIAL_CONTACT_NULL_CAPACITY] = {{0}}, b[DANGO_MATERIAL_CONTACT_NULL_CAPACITY] = {0}, x[DANGO_MATERIAL_CONTACT_NULL_CAPACITY] = {0}, q[DANGO_MATERIAL_CONTACT_NULL_CAPACITY] = {0};
    int rows[DANGO_MATERIAL_CONTACT_NULL_CAPACITY], columns[DANGO_MATERIAL_CONTACT_NULL_CAPACITY];
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

static inline bool dango_mcn_row_residuals(int n, const double matrix[DANGO_MATERIAL_CONTACT_NULL_CAPACITY][DANGO_MATERIAL_CONTACT_NULL_CAPACITY],
    const double* rhs, const double* z, bool homogeneous, double* residuals, double* allowances)
{
    if (!matrix || !rhs || !z || !residuals || !allowances || n <= 0 || n > DANGO_MATERIAL_CONTACT_NULL_CAPACITY) return false;
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

#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
// Independent fixed operator certificate. No coordinate is clipped, and the
// unmultiplied finite scale must exist before the epsilon coefficient is used.
static inline bool dango_mcn_operator_row_residuals(int n, const double matrix[DANGO_MATERIAL_CONTACT_NULL_CAPACITY][DANGO_MATERIAL_CONTACT_NULL_CAPACITY],
    const double* rhs, const double* z, bool homogeneous, double* residuals, double* allowances,
    dango_material_contact_operator_certificate* certificate, int mode_index)
{
    if (!certificate) return false;
    dango_mcn_operator_certificate_init(certificate);
    if (!matrix || !rhs || !z || !residuals || !allowances || n <= 0 || n > DANGO_MATERIAL_CONTACT_NULL_CAPACITY) return false;
    certificate->available = certificate->finite = true;
    certificate->mode_index = mode_index; certificate->dimension = n;
    struct timespec started = {0}; bool timed = timespec_get(&started, TIME_UTC) == TIME_UTC;
    double coordinate = 0;
    bool coordinate_finite = true, valid = true;
    for (int j = 0; j < n; ++j)
    {
        ++certificate->coordinate_visits;
        if (!isfinite(z[j])) coordinate_finite = false;
        coordinate = fmax(coordinate, fabs(z[j]));
    }
    if (!coordinate_finite || !isfinite(coordinate)) certificate->finite = false;
    for (int i = 0; i < n; ++i)
    {
        certificate->evaluated[i] = true;
        ++certificate->rows_checked;
        double sum = 0, operator_scale = 0;
        double rhs_scale = homogeneous ? 0 : fabs(rhs[i]);
        bool operator_finite = true, residual_finite = true;
        bool rhs_finite = isfinite(rhs[i]);
        for (int j = 0; j < n; ++j)
        {
            ++certificate->operator_visits;
            if (!isfinite(matrix[i][j])) operator_finite = false;
            operator_scale += fabs(matrix[i][j]);
            if (!isfinite(operator_scale)) operator_finite = false;
            ++certificate->residual_visits;
            double term = matrix[i][j] * z[j];
            if (!isfinite(term)) residual_finite = false;
            sum += term;
            if (!isfinite(sum)) residual_finite = false;
        }
        double product = operator_scale * coordinate;
        double scale = product + rhs_scale;
        bool scale_finite = operator_finite && coordinate_finite && rhs_finite &&
            isfinite(product) && isfinite(scale);
        certificate->operator_scale[i] = operator_scale;
        certificate->coordinate_scale[i] = coordinate;
        certificate->rhs_scale[i] = rhs_scale;
        certificate->scale[i] = scale;
        certificate->operator_finite[i] = operator_finite;
        certificate->coordinate_finite[i] = coordinate_finite && isfinite(coordinate);
        certificate->rhs_finite[i] = rhs_finite;
        certificate->scale_finite[i] = scale_finite;
        certificate->zero_operator[i] = operator_finite && operator_scale == 0;
        certificate->zero_coordinate[i] = coordinate_finite && coordinate == 0;
        certificate->zero_scale[i] = scale_finite && scale == 0;
        residuals[i] = sum - (homogeneous ? 0 : rhs[i]);
        allowances[i] = 128.0 * n * DBL_EPSILON * scale;
        bool finite = scale_finite && residual_finite && isfinite(residuals[i]) && isfinite(allowances[i]);
        if (!finite) certificate->finite = false;
        if (!finite || fabs(residuals[i]) > allowances[i]) valid = false;
    }
    certificate->passed = valid;
    dango_mcn_operator_certificate_finish(certificate, &started, timed);
    return valid;
}
#endif

static inline bool dango_mcn_effective_pair(int n, const int* row_ids, const double* scaling,
    const bool* fixed, const double* fixed_values, const double matrix[DANGO_MATERIAL_CONTACT_NULL_CAPACITY][DANGO_MATERIAL_CONTACT_NULL_CAPACITY], const double* rhs,
    dango_material_contact_null_report* report)
{
    if (!report) return false;
    if (report->status != DANGO_MATERIAL_CONTACT_NULL_OK) return false;
    dango_mcn_clear_effective(report);
    if (!row_ids || !scaling || !fixed || !fixed_values || !matrix || !rhs || n <= 0 || n > DANGO_MATERIAL_CONTACT_NULL_CAPACITY ||
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
#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
    bool particular_ok = dango_mcn_operator_row_residuals(n, matrix, rhs, report->effective_z_particular, false,
        report->particular_residual, report->particular_allowance, &report->particular_certificate, report->mode_index);
    dango_mcn_operator_record(report, &report->particular_certificate);
    bool direction_ok = dango_mcn_operator_row_residuals(n, matrix, rhs, report->effective_z_direction, true,
        report->direction_residual, report->direction_allowance, &report->direction_certificate, report->mode_index);
    dango_mcn_operator_record(report, &report->direction_certificate);
#else
    bool particular_ok = dango_mcn_row_residuals(n, matrix, rhs, report->effective_z_particular, false,
        report->particular_residual, report->particular_allowance);
    bool direction_ok = dango_mcn_row_residuals(n, matrix, rhs, report->effective_z_direction, true,
        report->direction_residual, report->direction_allowance);
#endif
    report->pair_checks_available = true; report->pair_checks_mode_index = report->mode_index;
    for (int i = 0; i < n; ++i)
    {
        if (report->first_particular_row_id < 0 && fabs(report->particular_residual[i]) > report->particular_allowance[i])
            report->first_particular_row_id = row_ids[i];
        if (report->first_direction_row_id < 0 && fabs(report->direction_residual[i]) > report->direction_allowance[i])
            report->first_direction_row_id = row_ids[i];
    }
#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
    if (!report->particular_certificate.finite || !report->direction_certificate.finite)
        return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
#endif
    if (!particular_ok || !direction_ok) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INCONSISTENT);
    report->pair_checks_passed = true;
    return true;
}

#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
// Called only after the shared stage/ID/dimension guards. The actual reaction
// delta retains the original accumulation; only its certificate scale changes.
static inline bool dango_mcn_operator_reaction_checks(const dango_material_contact_solver* reference,
    int n, const int* row_ids, dango_material_contact_null_report* report)
{
    dango_material_contact_operator_certificate* certificate = &report->reaction_certificate;
    dango_mcn_operator_certificate_init(certificate);
    certificate->available = certificate->finite = true;
    certificate->mode_index = report->mode_index; certificate->dimension = 17;
    struct timespec started = {0}; bool timed = timespec_get(&started, TIME_UTC) == TIME_UTC;
    double coordinate = 0, gradient_sum[17] = {0};
    bool coordinate_finite = true, operator_finite[DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY], residual_finite[17];
    for (int body = 0; body < 17; ++body)
    { operator_finite[body] = residual_finite[body] = true; }
    for (int i = 0; i < n; ++i)
    {
        ++certificate->coordinate_visits;
        if (!isfinite(report->effective_lambda_direction[i])) coordinate_finite = false;
        coordinate = fmax(coordinate, fabs(report->effective_lambda_direction[i]));
    }
    for (int i = 0; i < n; ++i)
    {
        int id = row_ids[i];
        if (id != report->original_row_ids[i] || id < 0 || id >= DANGO_MATERIAL_CONTACT_ROWS ||
            reference->rows[id].count < 0 || reference->rows[id].count > 4)
        {
            dango_mcn_operator_certificate_finish(certificate, &started, timed);
            dango_mcn_operator_record(report, certificate);
            return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
        }
        const dango_material_contact_row* row = &reference->rows[id];
        for (int j = 0; j < row->count; ++j)
        {
            int body = row->ids[j];
            if (body < 0 || body >= 17)
            {
                dango_mcn_operator_certificate_finish(certificate, &started, timed);
                dango_mcn_operator_record(report, certificate);
                return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
            }
            ++certificate->operator_visits;
            // hypot avoids square overflow/underflow for finite gradients.
            double gradient_length = dango_mc_length(row->gradients[j]);
            if (!isfinite(row->gradients[j].x) || !isfinite(row->gradients[j].y) || !isfinite(gradient_length))
                operator_finite[body] = false;
            gradient_sum[body] += gradient_length;
            if (!isfinite(gradient_sum[body])) operator_finite[body] = false;
            ++certificate->residual_visits;
            double weight = reference->geometry.inverse_mass[body] * report->effective_lambda_direction[i];
            dango_material_contact_vec2 contribution = dango_mc_mul(weight, row->gradients[j]);
            if (!isfinite(weight) || !isfinite(contribution.x) || !isfinite(contribution.y)) residual_finite[body] = false;
            report->reaction_delta[body] = dango_mc_add(report->reaction_delta[body], contribution);
            if (!isfinite(report->reaction_delta[body].x) || !isfinite(report->reaction_delta[body].y)) residual_finite[body] = false;
        }
    }
    bool valid = true;
    for (int body = 0; body < 17; ++body)
    {
        certificate->evaluated[body] = true;
        ++certificate->rows_checked;
        double inverse_mass = reference->geometry.inverse_mass[body];
        double operator_scale = inverse_mass * gradient_sum[body];
        bool body_operator_finite = operator_finite[body] && isfinite(inverse_mass) && inverse_mass >= 0 && isfinite(operator_scale);
        double scale = operator_scale * coordinate;
        bool scale_finite = body_operator_finite && coordinate_finite && isfinite(coordinate) && isfinite(scale);
        certificate->operator_scale[body] = operator_scale;
        certificate->coordinate_scale[body] = coordinate;
        certificate->rhs_scale[body] = 0;
        certificate->scale[body] = report->reaction_scale[body] = scale;
        certificate->operator_finite[body] = body_operator_finite;
        certificate->coordinate_finite[body] = coordinate_finite && isfinite(coordinate);
        certificate->rhs_finite[body] = true;
        certificate->scale_finite[body] = scale_finite;
        certificate->zero_operator[body] = body_operator_finite && operator_scale == 0;
        certificate->zero_coordinate[body] = coordinate_finite && coordinate == 0;
        certificate->zero_scale[body] = scale_finite && scale == 0;
        report->reaction_residual[body] = dango_mc_length(report->reaction_delta[body]);
        report->reaction_allowance[body] = 128.0 * n * DBL_EPSILON * scale;
        bool finite = scale_finite && residual_finite[body] && isfinite(report->reaction_residual[body]) &&
            isfinite(report->reaction_allowance[body]);
        if (!finite) certificate->finite = false;
        if (!finite || report->reaction_residual[body] > report->reaction_allowance[body])
        { valid = false; if (report->first_reaction_body < 0) report->first_reaction_body = body; }
    }
    certificate->passed = valid;
    dango_mcn_operator_certificate_finish(certificate, &started, timed);
    dango_mcn_operator_record(report, certificate);
    report->reaction_checks_available = true; report->reaction_mode_index = report->mode_index;
    if (!certificate->finite) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
    if (!valid) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_REACTION);
    report->reaction_checks_passed = true; return true;
}
#endif

static inline bool dango_mcn_reaction_null(const dango_material_contact_solver* reference,
    int n, const int* row_ids, dango_material_contact_null_report* report)
{
    if (!report) return false;
    if (report->status != DANGO_MATERIAL_CONTACT_NULL_OK) return false;
    dango_mcn_clear_reaction(report);
    if (!reference || !row_ids || n <= 0 || n > DANGO_MATERIAL_CONTACT_NULL_CAPACITY || n != report->dimension ||
        !report->pair_checks_passed || !report->effective_pair_available ||
        report->effective_pair_mode_index != report->mode_index || report->pair_checks_mode_index != report->mode_index)
        return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT);
#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
    return dango_mcn_operator_reaction_checks(reference, n, row_ids, report);
#else
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
#endif
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

static inline bool dango_mcn_selected(int n, const double* scaling, const double matrix[DANGO_MATERIAL_CONTACT_NULL_CAPACITY][DANGO_MATERIAL_CONTACT_NULL_CAPACITY],
    const double* rhs, dango_material_contact_null_report* report, double* multipliers, double* solution)
{
    if (!report) return false;
    if (report->status != DANGO_MATERIAL_CONTACT_NULL_OK) return false;
    dango_mcn_clear_selected(report);
    if (!scaling || !matrix || !rhs || !multipliers || !solution || n <= 0 || n > DANGO_MATERIAL_CONTACT_NULL_CAPACITY ||
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
#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
    bool selected_ok = dango_mcn_operator_row_residuals(n, matrix, rhs, report->selected_z, false,
        report->selected_residual, report->selected_allowance, &report->selected_certificate, report->mode_index);
    dango_mcn_operator_record(report, &report->selected_certificate);
    if (!report->selected_certificate.finite) return dango_mcn_fail(report, DANGO_MATERIAL_CONTACT_NULL_NONFINITE);
    if (!selected_ok)
#else
    if (!dango_mcn_row_residuals(n, matrix, rhs, report->selected_z, false, report->selected_residual, report->selected_allowance))
#endif
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

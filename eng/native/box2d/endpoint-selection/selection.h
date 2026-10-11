#ifndef DANGO_ENDPOINT_SELECTION_H
#define DANGO_ENDPOINT_SELECTION_H

// Included only in the generated private null header, after the unchanged
// original selection function. The shared source is never edited.
typedef struct dango_endpoint_trace
{
    bool original_failure_available, neighbor_constraints_passed;
    int original_failures, neighbor_attempts;
    double neighbor_alpha;
    dango_material_contact_null_report original_failure;
} dango_endpoint_trace;

typedef struct dango_endpoint_context
{
    dango_endpoint_trace* trace;
    dango_material_contact_null_report* report;
} dango_endpoint_context;

#if defined(_MSC_VER)
static __declspec(thread) dango_endpoint_context dango_endpoint_current;
#else
static _Thread_local dango_endpoint_context dango_endpoint_current;
#endif

static inline bool dango_endpoint_begin(dango_endpoint_trace* trace,
    dango_material_contact_null_report* report)
{
    if (!trace || !report || dango_endpoint_current.trace) return false;
    memset(trace, 0, sizeof(*trace));
    dango_endpoint_current.trace = trace; dango_endpoint_current.report = report;
    return true;
}

static inline void dango_endpoint_end(void)
{ dango_endpoint_current.trace = NULL; dango_endpoint_current.report = NULL; }

static inline bool dango_endpoint_provenance(const dango_material_contact_null_report* r)
{
    if (!r || !r->factor_available || !r->factor_passed || !r->raw_pair_available ||
        !r->effective_pair_available || !r->pair_checks_available || !r->pair_checks_passed ||
        !r->reaction_checks_available || !r->reaction_checks_passed ||
        !r->interval_available || !r->interval_passed || !r->selection_available ||
        r->selection_passed || r->mode_index < 0 || r->dimension < 1 ||
        r->dimension > DANGO_MATERIAL_CONTACT_NULL_CAPACITY || r->nullity != 1 ||
        r->rank != r->dimension - 1 || r->constraint_count < 1 ||
        r->constraint_count > DANGO_MATERIAL_CONTACT_NULL_CONSTRAINTS ||
        r->factor_mode_index != r->mode_index || r->raw_pair_mode_index != r->mode_index ||
        r->effective_pair_mode_index != r->mode_index || r->pair_checks_mode_index != r->mode_index ||
        r->reaction_mode_index != r->mode_index || r->interval_mode_index != r->mode_index ||
        r->selection_mode_index != r->mode_index) return false;
    if ((r->lower_bounded && !isfinite(r->lower)) ||
        (r->upper_bounded && !isfinite(r->upper)) ||
        (r->lower_bounded && r->upper_bounded && r->lower >= r->upper)) return false;
    for (int i = 0; i < r->constraint_count; ++i)
        if (!isfinite(r->constraints[i].a) || !isfinite(r->constraints[i].b)) return false;
    for (int i = 0; i < r->dimension; ++i)
    {
        if (!isfinite(r->particular_residual[i]) || !isfinite(r->particular_allowance[i]) ||
            !isfinite(r->direction_residual[i]) || !isfinite(r->direction_allowance[i]) ||
            r->particular_allowance[i] < 0 || r->direction_allowance[i] < 0 ||
            fabs(r->particular_residual[i]) > r->particular_allowance[i] ||
            fabs(r->direction_residual[i]) > r->direction_allowance[i]) return false;
    }
    for (int i = 0; i < 17; ++i)
        if (!isfinite(r->reaction_residual[i]) || !isfinite(r->reaction_allowance[i]) ||
            r->reaction_allowance[i] < 0 || fabs(r->reaction_residual[i]) > r->reaction_allowance[i]) return false;
    return true;
}

static inline bool dango_mcn_interval_select(dango_material_contact_null_report* report)
{
    // The full ordinary path and all of its original rejection states remain.
    bool fresh_stage = report && report->status == DANGO_MATERIAL_CONTACT_NULL_OK;
    if (dango_endpoint_original_select(report)) return true;
    dango_endpoint_trace* trace = dango_endpoint_current.trace;
    if (!report || !trace || dango_endpoint_current.report != report) return false;
    ++trace->original_failures;
    if (!trace->original_failure_available)
    { trace->original_failure = *report; trace->original_failure_available = true; }
    if (!fresh_stage || report->status != DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY ||
        trace->neighbor_attempts != 0 || !dango_endpoint_provenance(report) ||
        !isfinite(report->selected_alpha)) return false;
    double direction;
    if (report->lower_bounded && report->selected_alpha == report->lower) direction = INFINITY;
    else if (report->upper_bounded && report->selected_alpha == report->upper) direction = -INFINITY;
    else return false;
    // Exactly one representable neighbor for this entire block invocation.
    ++trace->neighbor_attempts;
    double alpha = nextafter(report->selected_alpha, direction);
    trace->neighbor_alpha = alpha;
    if (!isfinite(alpha) ||
        (report->lower_bounded && alpha < report->lower) ||
        (report->upper_bounded && alpha > report->upper)) return false;
    for (int i = 0; i < report->constraint_count; ++i)
    {
        double residual = report->constraints[i].a * alpha + report->constraints[i].b;
        if (!isfinite(residual) || residual > 0) return false;
    }
    report->selected_alpha = alpha; report->selection_passed = true;
    report->status = DANGO_MATERIAL_CONTACT_NULL_OK;
    trace->neighbor_constraints_passed = true;
    // Every existing selected-residual, block, double/float, cone, ledger, cap
    // and publication check still follows in the unchanged caller.
    return true;
}
#endif

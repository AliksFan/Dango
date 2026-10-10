#ifndef DANGO_MATERIAL_CONTACT_NULL_DIAGNOSTICS_H
#define DANGO_MATERIAL_CONTACT_NULL_DIAGNOSTICS_H

// Private diagnostic serialization only; no world state or solver decisions.
#include <stdio.h>
#include "dango_material_contact_null_reaction.h"

typedef struct dango_mcn_json_writer
{
    FILE* file;
    unsigned nonfinite_count;
} dango_mcn_json_writer;

static inline void dango_mcn_json_number(dango_mcn_json_writer* w, double value)
{
    if (isfinite(value)) fprintf(w->file, "%.17g", value);
    else { fputs("null", w->file); ++w->nonfinite_count; }
}

static inline void dango_mcn_json_field(dango_mcn_json_writer* w, const char* name, double value, bool available)
{
    fprintf(w->file, ",\"%s\":", name);
    if (available) dango_mcn_json_number(w, value); else fputs("null", w->file);
}

static inline void dango_mcn_json_array(dango_mcn_json_writer* w, const double* values, int n, bool available)
{
    if (!available) { fputs("null", w->file); return; }
    fputc('[', w->file);
    for (int i = 0; i < n; ++i)
    { if (i) fputc(',', w->file); dango_mcn_json_number(w, values[i]); }
    fputc(']', w->file);
}

static inline void dango_mcn_json_ints(FILE* file, const int* values, int n, bool available)
{
    if (!available) { fputs("null", file); return; }
    fputc('[', file);
    for (int i = 0; i < n; ++i) fprintf(file, "%s%d", i ? "," : "", values[i]);
    fputc(']', file);
}

#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
static inline void dango_mcn_json_certificate(dango_mcn_json_writer* w,
    const dango_material_contact_operator_certificate* c, const double* residuals, const double* allowances)
{
    if (!c->available) { fputs("null", w->file); return; }
    fprintf(w->file, "{\"available\":true,\"finite\":%s,\"passed\":%s,\"mode_index\":%d,\"dimension\":%d,\"coordinate_visits\":%llu,\"operator_visits\":%llu,\"residual_visits\":%llu,\"rows_checked\":%llu,\"time_available\":%s",
        c->finite ? "true" : "false", c->passed ? "true" : "false", c->mode_index, c->dimension,
        (unsigned long long)c->coordinate_visits, (unsigned long long)c->operator_visits,
        (unsigned long long)c->residual_visits, (unsigned long long)c->rows_checked, c->time_available ? "true" : "false");
    dango_mcn_json_field(w, "elapsed_seconds", c->elapsed_seconds, c->time_available);
    fputs(",\"rows\":[", w->file);
    int n = c->dimension < 17 ? c->dimension : 17;
    for (int i = 0; i < n; ++i)
    {
        fprintf(w->file, "%s{\"index\":%d,\"evaluated\":%s", i ? "," : "", i, c->evaluated[i] ? "true" : "false");
        bool checked = c->evaluated[i];
        dango_mcn_json_field(w, "operator_scale", c->operator_scale[i], checked);
        dango_mcn_json_field(w, "coordinate_scale", c->coordinate_scale[i], checked);
        dango_mcn_json_field(w, "rhs_scale", c->rhs_scale[i], checked);
        dango_mcn_json_field(w, "scale", c->scale[i], checked);
        dango_mcn_json_field(w, "residual", residuals[i], checked);
        dango_mcn_json_field(w, "allowance", allowances[i], checked);
#define MCN_JSON_CERT_BOOL(field) fprintf(w->file, ",\"" #field "\":%s", checked ? (c->field[i] ? "true" : "false") : "null")
        MCN_JSON_CERT_BOOL(operator_finite); MCN_JSON_CERT_BOOL(coordinate_finite);
        MCN_JSON_CERT_BOOL(rhs_finite); MCN_JSON_CERT_BOOL(scale_finite);
        MCN_JSON_CERT_BOOL(zero_operator); MCN_JSON_CERT_BOOL(zero_coordinate); MCN_JSON_CERT_BOOL(zero_scale);
#undef MCN_JSON_CERT_BOOL
        fprintf(w->file, ",\"residual_finite\":%s,\"allowance_finite\":%s}",
            checked ? (isfinite(residuals[i]) ? "true" : "false") : "null",
            checked ? (isfinite(allowances[i]) ? "true" : "false") : "null");
    }
    fputs("]}", w->file);
}
#endif

static inline void dango_mcn_json_report(FILE* file, const dango_material_contact_null_report* r)
{
    dango_mcn_json_writer writer = {file, 0}; dango_mcn_json_writer* w = &writer;
    int n = r->dimension < 16 ? r->dimension : 16;
    if (n < 0) n = 0;
    fprintf(file, "{\"schema\":1,\"status\":%d,\"mode_index\":%d,\"dimension\":%d,\"rank\":%d,\"nullity\":%d,\"free_column_id\":%d,\"used\":%s,\"used_scope\":\"Internal reaction choice; final block, float, cap and native publication are separate gates.\",\"pivot_searches\":%llu,\"pivot_eliminations\":%llu",
        r->status, r->mode_index, r->dimension, r->rank, r->nullity, r->free_column_id, r->used ? "true" : "false",
        (unsigned long long)r->pivot_searches, (unsigned long long)r->pivot_eliminations);
#define MCN_JSON_BOOL(field) fprintf(file, ",\"" #field "\":%s", r->field ? "true" : "false")
#define MCN_JSON_INT(field) fprintf(file, ",\"" #field "\":%d", r->field)
#define MCN_JSON_ARRAY(field, available) do { fputs(",\"" #field "\":", file); dango_mcn_json_array(w, r->field, n, available); } while (0)
    MCN_JSON_BOOL(factor_available); MCN_JSON_BOOL(raw_pair_available); MCN_JSON_BOOL(effective_pair_available);
    MCN_JSON_BOOL(pair_checks_available); MCN_JSON_BOOL(reaction_checks_available); MCN_JSON_BOOL(interval_available);
    MCN_JSON_BOOL(selection_available); MCN_JSON_BOOL(selected_pair_available);
    MCN_JSON_BOOL(factor_passed); MCN_JSON_BOOL(pair_checks_passed); MCN_JSON_BOOL(reaction_checks_passed);
    MCN_JSON_BOOL(interval_passed); MCN_JSON_BOOL(selection_passed); MCN_JSON_BOOL(selected_checks_passed);
    MCN_JSON_INT(factor_mode_index); MCN_JSON_INT(raw_pair_mode_index); MCN_JSON_INT(effective_pair_mode_index);
    MCN_JSON_INT(pair_checks_mode_index); MCN_JSON_INT(reaction_mode_index); MCN_JSON_INT(interval_mode_index);
    MCN_JSON_INT(selection_mode_index); MCN_JSON_INT(selected_mode_index);
    dango_mcn_json_field(w, "matrix_infinity_norm", r->matrix_infinity_norm, r->factor_available);
    dango_mcn_json_field(w, "pivot_threshold", r->pivot_threshold, r->factor_available);
    dango_mcn_json_field(w, "direction_normalization", r->direction_normalization, r->effective_pair_available);
    fputs(",\"original_row_ids\":", file); dango_mcn_json_ints(file, r->original_row_ids, n, r->factor_available);
    fputs(",\"row_permutation\":", file); dango_mcn_json_ints(file, r->row_permutation, n, r->factor_available);
    fputs(",\"column_permutation\":", file); dango_mcn_json_ints(file, r->column_permutation, n, r->factor_available);
    MCN_JSON_ARRAY(raw_z_particular, r->raw_pair_available); MCN_JSON_ARRAY(raw_z_direction, r->raw_pair_available);
    MCN_JSON_ARRAY(raw_lambda_particular, r->effective_pair_available); MCN_JSON_ARRAY(raw_lambda_direction, r->effective_pair_available);
    MCN_JSON_ARRAY(effective_z_particular, r->effective_pair_available); MCN_JSON_ARRAY(effective_z_direction, r->effective_pair_available);
    MCN_JSON_ARRAY(effective_lambda_particular, r->effective_pair_available); MCN_JSON_ARRAY(effective_lambda_direction, r->effective_pair_available);
    MCN_JSON_ARRAY(particular_residual, r->pair_checks_available); MCN_JSON_ARRAY(particular_allowance, r->pair_checks_available);
    MCN_JSON_ARRAY(direction_residual, r->pair_checks_available); MCN_JSON_ARRAY(direction_allowance, r->pair_checks_available);
    MCN_JSON_INT(first_particular_row_id); MCN_JSON_INT(first_direction_row_id); MCN_JSON_INT(first_reaction_body); MCN_JSON_INT(first_selected_row_id);
    fputs(",\"reaction_bodies\":", file);
    if (!r->reaction_checks_available) fputs("null", file);
    else
    {
        fputc('[', file);
        for (int i = 0; i < 17; ++i)
        {
            fprintf(file, "%s{\"id\":%d,\"delta\":[", i ? "," : "", i);
            dango_mcn_json_number(w, r->reaction_delta[i].x); fputc(',', file); dango_mcn_json_number(w, r->reaction_delta[i].y);
            fputc(']', file);
            dango_mcn_json_field(w, "scale", r->reaction_scale[i], true);
            dango_mcn_json_field(w, "residual", r->reaction_residual[i], true);
            dango_mcn_json_field(w, "allowance", r->reaction_allowance[i], true); fputc('}', file);
        }
        fputc(']', file);
    }
    dango_mcn_json_field(w, "constraint_count", r->constraint_count, r->interval_available);
    dango_mcn_json_field(w, "empty_row_id", r->empty_row_id, r->interval_available);
    fprintf(file, ",\"lower_bounded\":%s,\"upper_bounded\":%s",
        r->interval_available ? (r->lower_bounded ? "true" : "false") : "null",
        r->interval_available ? (r->upper_bounded ? "true" : "false") : "null");
    dango_mcn_json_field(w, "lower", r->lower, r->interval_available && r->lower_bounded);
    dango_mcn_json_field(w, "upper", r->upper, r->interval_available && r->upper_bounded);
    fputs(",\"constraints\":", file);
    if (!r->interval_available) fputs("null", file);
    else
    {
        fputc('[', file);
        for (int i = 0; i < r->constraint_count && i < DANGO_MATERIAL_CONTACT_NULL_CONSTRAINTS; ++i)
        {
            const dango_material_contact_null_constraint* c = &r->constraints[i];
            fprintf(file, "%s{\"row_id\":%d,\"kind\":%d,\"has_endpoint\":%s", i ? "," : "", c->row_id, c->kind, c->has_endpoint ? "true" : "false");
            dango_mcn_json_field(w, "a", c->a, true); dango_mcn_json_field(w, "b", c->b, true);
            dango_mcn_json_field(w, "endpoint", c->endpoint, c->has_endpoint);
            fprintf(file, ",\"lower_bounded_after\":%s,\"upper_bounded_after\":%s", c->lower_bounded_after ? "true" : "false", c->upper_bounded_after ? "true" : "false");
            dango_mcn_json_field(w, "lower_after", c->lower_after, c->lower_bounded_after);
            dango_mcn_json_field(w, "upper_after", c->upper_after, c->upper_bounded_after); fputc('}', file);
        }
        fputc(']', file);
    }
    dango_mcn_json_field(w, "selected_alpha", r->selected_alpha, r->selection_available);
    MCN_JSON_ARRAY(selected_lambda, r->selected_pair_available); MCN_JSON_ARRAY(selected_z, r->selected_pair_available);
    MCN_JSON_ARRAY(selected_residual, r->selected_pair_available); MCN_JSON_ARRAY(selected_allowance, r->selected_pair_available);
#ifdef DANGO_MATERIAL_CONTACT_OPERATOR_CERTIFICATE_CANDIDATE
#define MCN_JSON_CERT(name, residual, allowance) do { fputs(",\"" #name "\":", file); dango_mcn_json_certificate(w, &r->name, r->residual, r->allowance); } while (0)
    MCN_JSON_CERT(particular_certificate, particular_residual, particular_allowance);
    MCN_JSON_CERT(direction_certificate, direction_residual, direction_allowance);
    MCN_JSON_CERT(reaction_certificate, reaction_residual, reaction_allowance);
    MCN_JSON_CERT(selected_certificate, selected_residual, selected_allowance);
#undef MCN_JSON_CERT
    fprintf(file, ",\"operator_certificate_calls\":%llu,\"operator_coordinate_visits\":%llu,\"operator_coefficient_visits\":%llu,\"operator_residual_visits\":%llu,\"operator_row_visits\":%llu,\"operator_certificate_time_available\":%s",
        (unsigned long long)r->operator_certificate_calls, (unsigned long long)r->operator_coordinate_visits,
        (unsigned long long)r->operator_coefficient_visits, (unsigned long long)r->operator_residual_visits,
        (unsigned long long)r->operator_row_visits, r->operator_certificate_time_available ? "true" : "false");
    dango_mcn_json_field(w, "operator_certificate_elapsed_seconds", r->operator_certificate_elapsed_seconds, r->operator_certificate_time_available);
#endif
    fprintf(file, ",\"nonfinite_encoding\":\"null\",\"nonfinite_value_count\":%u}", w->nonfinite_count);
#undef MCN_JSON_BOOL
#undef MCN_JSON_INT
#undef MCN_JSON_ARRAY
}

#endif

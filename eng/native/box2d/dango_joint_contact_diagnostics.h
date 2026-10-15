// Read-only observations shared by J2.5 tests and future J3R scan hooks.
#ifndef DANGO_JOINT_CONTACT_DIAGNOSTICS_H
#define DANGO_JOINT_CONTACT_DIAGNOSTICS_H
#include "dango_joint_contact_registry.h"

typedef struct joint_residual_report
{
    // radial, perimeter, raw angle, display angle, ground, position friction, cross contact
    float maximum[7];
    int rig[7], node[7];
    joint_contact_key cross_key;
    int row_reads[7];
} joint_residual_report;

static inline void joint_residual_record(joint_residual_report* report, int kind, int rig, int node, float value)
{
    ++report->row_reads[kind];
    if (!isfinite(value) || value > report->maximum[kind])
    {
        report->maximum[kind] = value;
        report->rig[kind] = rig;
        report->node[kind] = node;
    }
}

static inline int joint_measure(const dango_joint_state* state, const joint_contact_snapshot* contacts,
    joint_residual_report* output)
{
    if (!state || !contacts || !output || !dango_joint_state_valid(state) ||
        contacts->registry.count < 0 || contacts->registry.count > 144) return DANGO_JOINT_ERROR_INVALID_STATE;
    joint_residual_report report = {0};
    for (int k = 0; k < 7; ++k) report.rig[k] = report.node[k] = -1;
    // Shared legacy primitives accept mutable arguments: use a copy even with correct=false.
    dango_joint_state copy = *state;
    for (int r = 0; r < state->body_count; ++r)
    {
        dango_constraint_state* s = &copy.rigs[r];
        for (int n = 0; n < 16; ++n)
        {
            if (!isfinite(state->ground_position_lambda[r][n])) return DANGO_JOINT_ERROR_CONTACT_GEOMETRY;
            joint_residual_record(&report, 0, r, n, dango_constraint_distance_mode(s, 16, n,
                s->radial_length * s->radial_min, s->radial_length * s->radial_max, false, DANGO_CONSTRAINT_CORRECT_POSITION));
            joint_residual_record(&report, 1, r, n, dango_constraint_distance_mode(s, n, (n + 1) % 16,
                s->perimeter_length * s->perimeter_min, s->perimeter_length * s->perimeter_max, false, DANGO_CONSTRAINT_CORRECT_POSITION));
            joint_residual_record(&report, 2, r, n, dango_constraint_angle_mode(s, n, false, false, DANGO_CONSTRAINT_CORRECT_POSITION));
            joint_residual_record(&report, 3, r, n, dango_constraint_angle_mode(s, n, true, false, DANGO_CONSTRAINT_CORRECT_POSITION));
            joint_residual_record(&report, 4, r, n, fmaxf(0, s->ground + s->radius - s->positions[n].y) / s->radial_length);
            float x = s->positions[n].x, lambda = state->ground_position_lambda[r][n];
            if (s->positions[n].y - s->radius - s->ground <= .005f)
                ground_friction_project(&x, state->previous[r][n].x, s->inverse_mass[n],
                    state->ground_normal_impulse[r][n], state->ground_friction, state->time_step, &lambda);
            joint_residual_record(&report, 5, r, n, fabsf(x - s->positions[n].x));
        }
    }
    for (int i = 0; i < contacts->registry.count; ++i)
    {
        const joint_contact_row* row = &contacts->registry.rows[i];
        if (row->key.kind == JOINT_GROUND) continue;
        float residual = fmaxf(0, state->skin - row->gap);
        if (residual > report.maximum[6]) report.cross_key = row->key;
        joint_residual_record(&report, 6, row->key.point_rig, row->key.point_node, residual);
    }
    for (int k = 0; k < 7; ++k) if (!isfinite(report.maximum[k])) return DANGO_JOINT_ERROR_CONTACT_GEOMETRY;
    *output = report;
    return 0;
}

typedef struct joint_group_observation
{
    joint_residual_report before, after;
    joint_contact_changes changes;
    int before_candidates, after_candidates, unique, duplicates;
    int generation_rebuilds, final_rebuilds, diagnostic_rebuilds;
    unsigned long long corrected_rows[7];
    float maximum_move;
} joint_group_observation;

static inline int joint_observe_group(const dango_joint_state* before, const dango_joint_state* after,
    const unsigned long long corrected_rows[7], joint_group_observation* output)
{
    if (!before || !after || !corrected_rows || !output || before->body_count != after->body_count)
        return DANGO_JOINT_ERROR_INVALID_STATE;
    joint_contact_snapshot a, b;
    joint_group_observation report = {0};
    int error = joint_build_contacts(before, &a);
    if (!error) error = joint_build_contacts(after, &b);
    if (!error) error = joint_measure(before, &a, &report.before);
    if (!error) error = joint_measure(after, &b, &report.after);
    if (error) return error;
    report.changes = joint_compare_contacts(&a, &b);
    report.before_candidates = a.candidates;
    report.after_candidates = b.candidates;
    report.unique = b.registry.count;
    report.duplicates = b.registry.duplicates;
    // Observation never generates solver rows or performs the solver's final check.
    report.diagnostic_rebuilds = a.candidates + b.candidates;
    for (int k = 0; k < 7; ++k) report.corrected_rows[k] = corrected_rows[k];
    for (int r = 0; r < before->body_count; ++r) for (int n = 0; n < 17; ++n)
        report.maximum_move = fmaxf(report.maximum_move, b2Distance(before->rigs[r].positions[n], after->rigs[r].positions[n]));
    *output = report;
    return 0;
}
#endif

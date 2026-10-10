// Full fixed private-candidate trajectories and first-failure solver input.
#define main candidate_unused_idle_main
#include "dango_material_idle_diagnostics.c"
#undef main

static void candidate_write_state(FILE* file, const dango_constraint_state* state)
{
    fprintf(file, "{\"radialLength\":%.9g,\"perimeterLength\":%.9g,\"radialMin\":%.9g,\"radialMax\":%.9g,\"perimeterMin\":%.9g,\"perimeterMax\":%.9g,\"radius\":%.9g,\"ground\":%.9g,\"bodies\":[",
        (double)state->radial_length, (double)state->perimeter_length, (double)state->radial_min, (double)state->radial_max,
        (double)state->perimeter_min, (double)state->perimeter_max, (double)state->radius, (double)state->ground);
    for (int n = 0; n < 17; ++n)
        fprintf(file, "%s{\"position\":[%.9g,%.9g],\"velocity\":[%.9g,%.9g],\"inverseMass\":%.9g}", n ? "," : "",
            (double)state->positions[n].x, (double)state->positions[n].y, (double)state->velocities[n].x, (double)state->velocities[n].y, (double)state->inverse_mass[n]);
    fputs("]}", file);
}

static void candidate_write_available_state(FILE* file, const dango_constraint_state* state, bool available)
{
    if (available) candidate_write_state(file, state); else fputs("null", file);
}

static void candidate_write_available_number(FILE* file, const char* name, double value, bool available)
{
    fprintf(file, ",\"%s\":", name);
    if (available) fprintf(file, "%.17g", value); else fputs("null", file);
}

static void candidate_write_diagnostic(FILE* file, const dango_material_contact_diagnostic* d)
{
    const bool output_available = d->phase >= 5 && d->final_speed_scale > 0;
    fprintf(file, "{\"phase\":%d,\"adapterError\":%d,\"reportValid\":%s,\"totalSubsteps\":%llu,\"positionIterations\":%d,\"receipts\":[",
        d->phase, d->error, d->report_valid ? "true" : "false", d->substeps, d->position_iterations);
    for (int n = 0; n < d->receipt_count; ++n)
    {
        const dango_material_ground_receipt* r = &d->receipts[n];
        fprintf(file, "%s{\"node\":%d,\"normal\":%.17g,\"tangent\":%.17g,\"friction\":%.17g,\"totalNormalTelemetry\":%.17g}",
            n ? "," : "", r->node, r->normal_impulse, r->tangent_impulse, r->friction, r->total_normal_impulse);
    }
    fputs("],\"input\":", file); candidate_write_available_state(file, &d->input, d->phase >= 1);
    fputs(",\"positioned\":", file); candidate_write_available_state(file, &d->positioned, d->phase >= 2);
    fputs(",\"damped\":", file); candidate_write_available_state(file, &d->damped, d->phase >= 4);
    fputs(",\"uncapped\":", file); candidate_write_available_state(file, &d->uncapped, d->phase >= 5);
    fputs(",\"output\":", file); candidate_write_available_state(file, &d->output, output_available);
    fprintf(file, ",\"solverStepSeconds\":%.17g,\"solverDoubleVelocities\":[", d->solver.step_seconds);
    for (int n = 0; n < 17; ++n) fprintf(file, "%s[%.17g,%.17g]", n ? "," : "", d->solver.velocities[n].x, d->solver.velocities[n].y);
    fputc(']', file);
    const dango_material_contact_report* r = &d->report;
    fprintf(file, ",\"velocityReport\":{\"status\":%d,\"sweeps\":%d,\"rowVisits\":%llu,\"coneRetractions\":%llu,\"activeShapeRows\":%d,\"activeNormalRows\":%d,\"activeTangentRows\":%d,\"geometryResidual\":%.17g,\"relativeLinearClosing\":%.17g,\"angularClosing\":%.17g,\"relativeLinearComplementarity\":%.17g,\"angularComplementarity\":%.17g,\"relativeFrictionRate\":%.17g,\"frictionConeExcess\":%.17g,\"receiptConeSlack\":%.17g,\"ledgerEvaluated\":%s",
        r->status, r->sweeps, (unsigned long long)r->row_visits, (unsigned long long)r->cone_retractions, r->active_shape_rows, r->active_normal_rows, r->active_tangent_rows,
        r->geometry_residual, r->maximum_relative_linear_closing, r->maximum_angular_closing, r->maximum_relative_linear_complementarity, r->maximum_angular_complementarity,
        r->maximum_relative_friction_rate, r->maximum_friction_cone_excess, r->maximum_receipt_cone_slack, r->ledger_evaluated ? "true" : "false");
    candidate_write_available_number(file, "energyBefore", r->energy_before, r->ledger_evaluated);
    candidate_write_available_number(file, "energyAfter", r->energy_after, r->ledger_evaluated);
    candidate_write_available_number(file, "energyAllowance", r->energy_allowance, r->ledger_evaluated);
    candidate_write_available_number(file, "momentumResidual", r->momentum_residual, r->ledger_evaluated);
    candidate_write_available_number(file, "momentumAllowance", r->momentum_allowance, r->ledger_evaluated);
    candidate_write_available_number(file, "angularMomentumResidual", r->angular_momentum_residual, r->ledger_evaluated);
    candidate_write_available_number(file, "angularMomentumAllowance", r->angular_momentum_allowance, r->ledger_evaluated);
    candidate_write_available_number(file, "relativeVelocityLedgerResidual", r->maximum_relative_velocity_ledger_residual, r->ledger_evaluated);
    candidate_write_available_number(file, "relativeVelocityLedgerAllowance", r->maximum_relative_velocity_ledger_allowance, r->ledger_evaluated);
    fprintf(file, ",\"floatOutputChecked\":%s}", r->float_output_checked ? "true" : "false");
    candidate_write_available_number(file, "finalSpeedScale", d->final_speed_scale, output_available);
    fputs(",\"rows\":[", file);
    for (int n = 0; n < DANGO_MATERIAL_CONTACT_ROWS; ++n)
    {
        const dango_material_contact_row* row = &d->solver.rows[n];
        fprintf(file, "%s{\"id\":%d,\"kind\":%d,\"node\":%d,\"bound\":%d,\"active\":%s,\"gap\":%.17g,\"effectiveInverseMass\":%.17g,\"multiplier\":%.17g,\"bodies\":[",
            n ? "," : "", row->id, row->kind, row->node, row->bound, row->active ? "true" : "false", row->gap, row->effective_inverse_mass, row->multiplier);
        for (int i = 0; i < row->count; ++i) fprintf(file, "%s{\"id\":%d,\"gradient\":[%.17g,%.17g]}", i ? "," : "", row->ids[i], row->gradients[i].x, row->gradients[i].y);
        fputs("]}", file);
    }
    fputs("],\"speedCapReport\":", file);
    if (!output_available) { fputs("null}", file); return; }
    fprintf(file, "{\"status\":%d,\"scale\":%.9g,\"maximumBefore\":%.9g,\"maximumAfter\":%.9g,\"energyBefore\":%.17g,\"energyAfter\":%.17g,\"energyAllowance\":%.17g,\"momentumChange\":[%.17g,%.17g],\"angularMomentumChange\":%.17g,\"contactOutputChecked\":%s,\"exactCommonScaleChecked\":%s}}",
        d->cap_report.status, (double)d->cap_report.scale, (double)d->cap_report.maximum_before, (double)d->cap_report.maximum_after,
        d->cap_report.energy_before, d->cap_report.energy_after, d->cap_report.energy_allowance, d->cap_report.cap_momentum_change.x,
        d->cap_report.cap_momentum_change.y, d->cap_report.cap_angular_momentum_change, d->cap_report.contact_output_checked ? "true" : "false",
        d->cap_report.exact_common_scale_checked ? "true" : "false");
}

int main(int argc, char** argv)
{
    if (argc != 3 || (strcmp(argv[2], "default") && strcmp(argv[2], "forward") && strcmp(argv[2], "mirror"))) return 2;
    FILE* output = fopen(argv[1], "wx"); if (!output) return 2;
    bool default_case = !strcmp(argv[2], "default"), mirror = !strcmp(argv[2], "mirror");
    int requested = default_case ? 36000 : 42000;
    idle_config = idle_defaults(true); idle_material_mode = 7;
    void* handle = NULL; int id = 0, error = dango_physics_world_create(&idle_config, 1, &handle);
    if (!error) error = dango_physics_world_add(handle, 0, 0, &id);
    if (error) { fprintf(output, "{\"creationError\":%d}\n", error); fclose(output); if (handle) dango_physics_world_destroy(handle); return 1; }
    int completed = 0, attempted = 0, count = 0, state_id = 0, contacts = 0, latched = 0;
    float controls[6] = {0}, row[42] = {0};
    double low[4] = {INFINITY,INFINITY,INFINITY,INFINITY}, high[4] = {-INFINITY,-INFINITY,-INFINITY,-INFINITY};
    fputs("{\"schema\":1,\"privateCandidate\":true,\"snapshots\":[", output); bool first = true;
    for (attempted = 1; attempted <= requested; ++attempted)
    {
        memset(controls, 0, sizeof controls);
        if (!default_case && attempted <= 6000)
        {
            int i = attempted - 1; controls[3] = i >= 600 && i % 600 < 480 ? 1.0f : 0.0f;
            controls[4] = (float)((mirror ? -1 : 1) * 2.4 * sin(i * .12)); controls[5] = (float)(.4 + 1.5 * cos(i * .17));
        }
        error = dango_physics_world_step(handle, .0166666f, &id, controls, 1, &state_id, row, 42, &count, &contacts, &latched);
        if (error || latched || count != 1 || state_id != id) break;
        ++completed; int idle_frame = default_case ? attempted : attempted - 6000, index = idle_frame - 1;
        if (index >= 600 && (index % 60 == 0 || index >= 35400))
        {
            double min_x = INFINITY, min_y = INFINITY, max_x = -INFINITY, max_y = -INFINITY;
            for (int n = 0; n < 16; ++n) { min_x = fmin(min_x,row[2*n]); max_x = fmax(max_x,row[2*n]); min_y = fmin(min_y,row[2*n+1]); max_y = fmax(max_y,row[2*n+1]); }
            double values[4] = {row[32],row[33],max_x-min_x,max_y-min_y};
            for (int n = 0; n < 4; ++n) { low[n] = fmin(low[n],values[n]); high[n] = fmax(high[n],values[n]); }
        }
        if (idle_checkpoint(idle_frame))
        {
            fprintf(output, "%s{\"worldFrame\":%d,\"nativeRow\":[", first ? "" : ",", attempted); first = false;
            for (int n = 0; n < 42; ++n) fprintf(output, "%s%.9g", n ? "," : "", (double)row[n]); fputs("]}", output);
        }
    }
    fprintf(output, "],\"profile\":\"%s\",\"completedFrames\":%d,\"attemptedFrame\":%d,\"error\":%d,\"latchedError\":%d,\"lastLegalRow\":[", argv[2], completed, completed == requested ? requested : attempted, error, latched);
    for (int n = 0; n < 42; ++n) fprintf(output, "%s%.9g", n ? "," : "", (double)row[n]); fputs("],\"spans\":[", output);
    for (int n = 0; n < 4; ++n) fprintf(output, "%s%.17g", n ? "," : "", isfinite(low[n]) ? high[n]-low[n] : 0);
    fputs("],\"lastSubstep\":", output); candidate_write_diagnostic(output, &((collision_review*)handle)->candidate_diagnostic);
    fputs("}\n", output); int io = fclose(output), destroyed = dango_physics_world_destroy(handle);
    printf("%s frames=%d attempted=%d error=%d latch=%d\n", argv[2], completed, completed == requested ? requested : attempted, error, latched);
    return error || latched || io || destroyed || completed != requested ? 1 : 0;
}

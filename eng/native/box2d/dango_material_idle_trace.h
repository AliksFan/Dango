#ifndef DANGO_MATERIAL_IDLE_TRACE_H
#define DANGO_MATERIAL_IDLE_TRACE_H
#include <stdio.h>
#include "dango_material_idle_trace_format.h"

typedef struct dango_it_context {
    FILE* file;
    int error, enabled;
    uint32_t frame;
    uint64_t sequence;
    bool first_alpha, first_error;
    dango_it_step step;
    dango_it_mode modes[32];
    dango_it_end end;
    float previous[17][4];
} dango_it_context;
#if defined(_MSC_VER)
static __declspec(thread) dango_it_context* dango_it_current;
#else
static _Thread_local dango_it_context* dango_it_current;
#endif

static uint64_t dango_it_worst_bytes(void)
{
    // Both roles, maximum32events,256windows+2exceptions, manifests/sparse tests.
    return 2 * sizeof(dango_it_header) +
        (uint64_t)DANGO_IT_STEPS * (sizeof(dango_it_prefix) + sizeof(dango_it_step) + 32 * sizeof(dango_it_mode)) +
        (uint64_t)DANGO_IT_STEPS * (sizeof(dango_it_prefix) + sizeof(dango_it_cost)) +
        2 * (uint64_t)DANGO_IT_FRAMES * (sizeof(dango_it_prefix) + sizeof(dango_it_frame)) +
        2 * 258 * (sizeof(dango_it_prefix) + sizeof(dango_it_window) + sizeof(dango_material_contact_diagnostic)) +
        2 * (sizeof(dango_it_prefix) + sizeof(dango_it_end)) + UINT64_C(2097152);
}

static bool dango_it_write(dango_it_context* trace, const void* data, size_t bytes)
{
    if (!trace->file || trace->error) return false;
    if (bytes > DANGO_IT_LIMIT - trace->end.bytes) { trace->error = -501; return false; }
    if (fwrite(data, 1, bytes, trace->file) != bytes) { trace->error = -502; return false; }
    trace->end.bytes += bytes; return true;
}
static bool dango_it_record(dango_it_context* trace, uint32_t kind, const void* data, uint32_t bytes)
{
    dango_it_prefix prefix = {kind, bytes};
    return dango_it_write(trace, &prefix, sizeof prefix) && dango_it_write(trace, data, bytes);
}
static void dango_it_body(dango_self_soft_body* body, float state[17][4], float* masses)
{
    for (int n = 0; n < 17; ++n) {
        b2BodyId id = dango_mca_id(body, n);
        b2Vec2 p = b2Body_GetPosition(id), v = b2Body_GetLinearVelocity(id);
        state[n][0] = p.x; state[n][1] = p.y; state[n][2] = v.x; state[n][3] = v.y;
        if (masses) masses[n] = b2Body_GetMass(id);
    }
}
static void dango_it_state(float destination[17][4], const dango_constraint_state* state)
{
    for (int n = 0; n < 17; ++n) {
        destination[n][0] = state->positions[n].x; destination[n][1] = state->positions[n].y;
        destination[n][2] = state->velocities[n].x; destination[n][3] = state->velocities[n].y;
    }
}

static void dango_it_mode_event(const void* raw, bool built, bool solved)
{
    dango_it_context* trace = dango_it_current;
    if (!trace || !trace->enabled) return;
    if (trace->step.event_count >= 32) { trace->error = -503; return; }
    const dango_material_contact_block_report* report = raw;
    const dango_material_contact_null_report* r = &report->null_reaction;
    dango_it_mode* event = &trace->modes[trace->step.event_count++];
    memset(event, 0, sizeof(*event));
    event->index = (uint32_t)(report->mode_history_count - 1); event->status = r->status;
    event->flags = (built ? 1u : 0u) | (solved ? 2u : 0u) | (r->factor_available ? 4u : 0u) |
        (r->selection_available ? 8u : 0u) | (r->used ? 16u : 0u) | (r->selection_passed ? 32u : 0u);
    event->rank = (uint8_t)r->rank; event->nullity = (uint8_t)r->nullity;
    event->dimension = (uint8_t)r->dimension; event->active_count = (uint8_t)report->active_count;
    event->alpha = r->selection_available ? r->selected_alpha : 0;
    for (int i = 0; i < report->active_count && i < 16; ++i)
        event->mode_bits |= (uint64_t)report->modes[i] << (4 * i);
}
static void dango_it_state_event(int stage, const void* raw)
{
    dango_it_context* trace = dango_it_current;
    if (!trace || !trace->enabled || trace->error) return;
    if (stage < 2 || stage > 6) { trace->error = -504; return; }
    dango_it_state(trace->step.states[stage], raw);
    trace->step.stage_mask |= 1u << stage;
}

static void dango_it_start(dango_it_context* trace, dango_self_soft_body* body, uint32_t substep, float dt)
{
    if (!trace->file || trace->error) return;
    memset(&trace->step, 0, sizeof(trace->step));
    trace->step.meta.sequence = ++trace->sequence; trace->step.meta.frame = trace->frame;
    trace->step.meta.substep = substep; trace->step.dt = dt;
    if (trace->enabled) {
        dango_it_body(body, trace->step.states[0], trace->step.masses);
        trace->step.stage_mask = 1;
        dango_it_current = trace;
    }
}
static void dango_it_pre_world(dango_it_context* trace, dango_self_soft_body* body)
{
    if (trace->file && trace->enabled && !trace->error) {
        dango_it_body(body, trace->step.states[1], NULL); trace->step.stage_mask |= 2;
    }
}
static void dango_it_window_record(dango_it_context* trace, const dango_material_contact_diagnostic* d, uint32_t reason)
{
    dango_it_window window = {trace->sequence, trace->frame, trace->step.meta.substep, reason, sizeof(*d)};
    dango_it_prefix prefix = {DANGO_IT_WINDOW, (uint32_t)(sizeof(window) + sizeof(*d))};
    if (dango_it_write(trace, &prefix, sizeof prefix) && dango_it_write(trace, &window, sizeof window) &&
        dango_it_write(trace, d, sizeof(*d))) ++trace->end.windows;
}
static void dango_it_after(dango_it_context* trace, dango_self_soft_body* body, const dango_material_contact_diagnostic* d)
{
    dango_it_current = NULL;
    if (!trace->file || trace->error) return;
    dango_it_step* step = &trace->step;
    step->meta.phase = d->phase; step->meta.error = d->error;
    const dango_material_contact_block_report* r = &d->block_report;
    uint64_t costs[DANGO_IT_COSTS] = {(uint64_t)d->position_iterations, (uint64_t)r->linear_solves,
        (uint64_t)r->full_checks, (uint64_t)r->float_checks, (uint64_t)r->mode_history_count,
        r->pivot_searches, r->pivot_eliminations, r->operator_certificate_calls, r->operator_coordinate_visits,
        r->operator_coefficient_visits, r->operator_residual_visits, r->operator_row_visits};
    memcpy(step->meta.costs, costs, sizeof costs);
    for (int i = 0; i < DANGO_IT_COSTS; ++i) trace->end.costs[i] += costs[i];
    if (!trace->enabled) {
        (void)dango_it_record(trace, DANGO_IT_OFF_STEP, &step->meta, sizeof step->meta);
        ++trace->end.steps; return;
    }
    // Availability was marked at actual assignment, never inferred from phase.
    if (step->stage_mask & 32) {
        for (int n = 0; n < 17; ++n) { step->contact_velocity[n][0] = d->block.contact.velocities[n].x; step->contact_velocity[n][1] = d->block.contact.velocities[n].y; }
    }
    dango_it_body(body, step->states[7], NULL); step->stage_mask |= 128;
    step->receipt_count = (uint32_t)d->receipt_count; step->cap_scale = d->final_speed_scale;
    step->active_count = (uint32_t)r->active_count;
    memcpy(step->active_ids, r->active_row_ids, sizeof step->active_ids);
    for (int i = 0; i < d->receipt_count && i < 16; ++i) {
        const dango_material_ground_receipt* receipt = &d->receipts[i];
        step->receipts[i] = (dango_it_receipt){receipt->node, receipt->friction, receipt->normal_impulse, receipt->tangent_impulse, receipt->total_normal_impulse};
    }
    if (step->stage_mask == 255 && !d->error) {
        for (int stage = 0; stage < 8; ++stage)
            for (int n = 0; n < 17; ++n) for (int axis = 0; axis < 2; ++axis) {
                double before = stage ? step->states[stage-1][n][axis] : trace->previous[n][axis];
                double delta = (double)step->states[stage][n][axis] - before;
                trace->end.displacement[stage][n][axis] += delta;
                trace->end.absolute_displacement[stage][n][axis] += fabs(delta);
            }
    }
    memcpy(trace->previous, step->states[7], sizeof trace->previous);
    memcpy(trace->end.final, step->states[7], sizeof trace->end.final);
    dango_it_prefix prefix = {DANGO_IT_STEP, (uint32_t)(sizeof(*step) + step->event_count * sizeof(dango_it_mode))};
    if (!dango_it_write(trace, &prefix, sizeof prefix) || !dango_it_write(trace, step, sizeof(*step)) ||
        !dango_it_write(trace, trace->modes, step->event_count * sizeof(dango_it_mode))) return;
    ++trace->end.steps;
    bool fixed = trace->frame <= 4 || (trace->frame >= 597 && trace->frame <= 604) || trace->frame >= 6298;
    bool nonzero = false;
    for (uint32_t i = 0; i < step->event_count; ++i)
        if ((trace->modes[i].flags & 8) && trace->modes[i].alpha != 0) nonzero = true;
    uint32_t reason = fixed ? 1u : 0u;
    if (!trace->first_alpha && nonzero) { reason |= 2; trace->first_alpha = true; }
    if (!trace->first_error && d->error) { reason |= 4; trace->first_error = true; }
    if (reason) dango_it_window_record(trace, d, reason);
}
static void dango_it_publish(dango_it_context* trace, const float* row, int error, int contacts)
{
    if (!trace->file || trace->error) return;
    dango_it_frame frame = {trace->frame, error == 0, error, contacts, {0}};
    memcpy(frame.row, row, sizeof frame.row);
    if (dango_it_record(trace, DANGO_IT_FRAME, &frame, sizeof frame)) ++trace->end.frames;
}
#endif

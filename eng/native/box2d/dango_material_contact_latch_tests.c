// Failure-contract regression for the actual private candidate, which currently
// fails natural landing at frame 29. This is not a healthy-physics acceptance.
#ifndef DANGO_MATERIAL_CONTACT_CANDIDATE
#define DANGO_MATERIAL_CONTACT_CANDIDATE
#endif
#define main contact_latch_unused_idle_main
#include "dango_material_idle_diagnostics.c"
#undef main

typedef struct contact_latch_raw_state
{
    b2BodyId ids[17];
    b2Vec2 positions[17], velocities[17];
} contact_latch_raw_state;

static contact_latch_raw_state contact_latch_capture(collision_review* world)
{
    contact_latch_raw_state state;
    for (int n = 0; n < 17; ++n)
    {
        state.ids[n] = n == 16 ? world->bodies[0]->core_body_id : world->bodies[0]->node_body_ids[n];
        state.positions[n] = b2Body_GetPosition(state.ids[n]);
        state.velocities[n] = b2Body_GetLinearVelocity(state.ids[n]);
    }
    return state;
}

static bool contact_latch_same_raw(const contact_latch_raw_state* a, const contact_latch_raw_state* b)
{
    for (int n = 0; n < 17; ++n)
        if (!B2_ID_EQUALS(a->ids[n], b->ids[n]) ||
            memcmp(&a->positions[n], &b->positions[n], sizeof(b2Vec2)) ||
            memcmp(&a->velocities[n], &b->velocities[n], sizeof(b2Vec2))) return false;
    return true;
}

static bool contact_latch_finite_row(const float* row)
{
    for (int n = 0; n < 42; ++n) if (!isfinite(row[n])) return false;
    return row[41] == 16 && row[34] > 0;
}

#define LATCH_CHECK(condition) do { ++checks; if (!(condition)) { \
    fprintf(stderr, "FAIL latch %d: %s\n", __LINE__, #condition); result = 1; goto cleanup; } } while (0)

int main(void)
{
    int result = 0, checks = 0, id = 0, state_id = 0, count = 0, contacts = 0, latched = 0;
    void* handle = NULL;
    const float dt = .0166666f;
    float controls[6] = {0}, row[42] = {0}, initial[42], last_legal[42];
    idle_config = idle_defaults(true); idle_material_mode = 7;
    LATCH_CHECK(dango_physics_world_create(&idle_config, 1, &handle) == 0 && handle != NULL);
    LATCH_CHECK(dango_physics_world_add(handle, 0, 0, &id) == 0 && id > 0);
    collision_review* world = handle;
    memcpy(initial, world->snapshot, sizeof initial);
    LATCH_CHECK(contact_latch_finite_row(initial));

    for (int frame = 1; frame <= 28; ++frame)
    {
        LATCH_CHECK(dango_physics_world_step(handle, dt, &id, controls, 1,
            &state_id, row, 42, &count, &contacts, &latched) == 0);
        LATCH_CHECK(latched == 0 && world->error == 0 && count == 1 && state_id == id);
        LATCH_CHECK(contact_latch_finite_row(row));
        LATCH_CHECK(memcmp(row, world->snapshot, sizeof row) == 0);
    }
    memcpy(last_legal, row, sizeof last_legal);
    contact_latch_raw_state before_fault = contact_latch_capture(world);

    // Frame 29 advances some real world substeps and then fails the velocity
    // budget. Its raw world is not a rollback to frame 28; publication is.
    LATCH_CHECK(dango_physics_world_step(handle, dt, &id, controls, 1,
        &state_id, row, 42, &count, &contacts, &latched) == 0);
    LATCH_CHECK(latched == -96 && world->error == -96 && count == 1 && state_id == id);
    LATCH_CHECK(memcmp(row, last_legal, sizeof row) == 0);
    LATCH_CHECK(memcmp(world->snapshot, last_legal, sizeof row) == 0);
    dango_material_contact_diagnostic diagnostic;
    LATCH_CHECK(dango_material_contact_get_diagnostic(handle, &diagnostic) == 0);
    LATCH_CHECK(diagnostic.phase == 4 && diagnostic.error == -96 && diagnostic.report_valid);
    LATCH_CHECK(diagnostic.substeps == 460 && diagnostic.position_iterations == 21);
    LATCH_CHECK(diagnostic.report.status == DANGO_MATERIAL_CONTACT_BUDGET && diagnostic.report.sweeps == 384);
    LATCH_CHECK(diagnostic.report.maximum_angular_closing > DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
    LATCH_CHECK(!diagnostic.report.ledger_evaluated && !diagnostic.report.float_output_checked &&
        !diagnostic.cap_report.contact_output_checked);
    contact_latch_raw_state failed_raw = contact_latch_capture(world);
    for (int n = 0; n < 17; ++n)
    {
        LATCH_CHECK(B2_ID_EQUALS(before_fault.ids[n], failed_raw.ids[n]));
        // No scratch position/damping/solver update was written on failure.
        LATCH_CHECK(memcmp(&failed_raw.positions[n], &diagnostic.input.positions[n], sizeof(b2Vec2)) == 0);
        LATCH_CHECK(memcmp(&failed_raw.velocities[n], &diagnostic.input.velocities[n], sizeof(b2Vec2)) == 0);
    }
    const int held_contacts = contacts;
    const float active_controls[3][6] = {
        {1, 0, 0, 0, 0, 0},
        {0, 1, 1, 0, 0, 0},
        {0, 0, 0, 1, 2.4f, 1.9f}
    };
    for (int trial = 0; trial < 3; ++trial)
    {
        LATCH_CHECK(dango_physics_world_step(handle, dt, &id, active_controls[trial], 1,
            &state_id, row, 42, &count, &contacts, &latched) == 0);
        LATCH_CHECK(latched == -96 && world->error == -96 && count == 1 && state_id == id && contacts == held_contacts);
        LATCH_CHECK(memcmp(row, last_legal, sizeof row) == 0 && memcmp(world->snapshot, last_legal, sizeof row) == 0);
        contact_latch_raw_state held = contact_latch_capture(world);
        LATCH_CHECK(contact_latch_same_raw(&failed_raw, &held));
        dango_material_contact_diagnostic held_diagnostic;
        LATCH_CHECK(dango_material_contact_get_diagnostic(handle, &held_diagnostic) == 0);
        LATCH_CHECK(held_diagnostic.substeps == diagnostic.substeps && held_diagnostic.phase == diagnostic.phase &&
            held_diagnostic.error == diagnostic.error && held_diagnostic.report.status == diagnostic.report.status &&
            held_diagnostic.report.sweeps == diagnostic.report.sweeps);
    }

    LATCH_CHECK(dango_physics_world_reset(handle) == 0);
    LATCH_CHECK(world->error == 0 && world->contact_steps == 0 && world->count == 1 && world->entity_ids[0] == id);
    LATCH_CHECK(memcmp(world->snapshot, initial, sizeof initial) == 0);
    dango_material_contact_diagnostic reset_diagnostic;
    LATCH_CHECK(dango_material_contact_get_diagnostic(handle, &reset_diagnostic) == 0);
    LATCH_CHECK(reset_diagnostic.phase == 0 && reset_diagnostic.error == 0 && reset_diagnostic.substeps == 0 &&
        !reset_diagnostic.report_valid && !reset_diagnostic.report.ledger_evaluated);
    for (int frame = 1; frame <= 28; ++frame)
    {
        LATCH_CHECK(dango_physics_world_step(handle, dt, &id, controls, 1,
            &state_id, row, 42, &count, &contacts, &latched) == 0);
        LATCH_CHECK(latched == 0 && world->error == 0 && count == 1 && state_id == id);
        LATCH_CHECK(contact_latch_finite_row(row));
        LATCH_CHECK(memcmp(row, world->snapshot, sizeof row) == 0);
    }
    // Explicit recovery makes the first 28 frames available again; it does not
    // repair the candidate. Preserve the repeated real failure at frame 29.
    memcpy(last_legal, row, sizeof last_legal);
    LATCH_CHECK(dango_physics_world_step(handle, dt, &id, controls, 1,
        &state_id, row, 42, &count, &contacts, &latched) == 0);
    LATCH_CHECK(latched == -96 && world->error == -96 && count == 1 && state_id == id);
    LATCH_CHECK(memcmp(row, last_legal, sizeof row) == 0);
    LATCH_CHECK(world->candidate_diagnostic.report.status == DANGO_MATERIAL_CONTACT_BUDGET &&
        world->candidate_diagnostic.report.sweeps == 384);

cleanup:
    if (handle && dango_physics_world_destroy(handle) != 0)
    {
        fprintf(stderr, "FAIL latch: destroy did not release the test world\n");
        result = 1;
    }
    if (!result) printf("PASS candidate latch: %d checks; natural frame29 budget fault; 3 active inputs held; explicit reset recovered28 frames and faulted29 again. Candidate physics remains failed.\n", checks);
    return result;
}

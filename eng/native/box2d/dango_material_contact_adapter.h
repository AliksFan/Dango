#ifndef DANGO_MATERIAL_CONTACT_ADAPTER_H
#define DANGO_MATERIAL_CONTACT_ADAPTER_H

// Private single-body experiment only. Included after passive_spin is defined.
#include "dango_material_contact_solver.h"
#include "dango_material_forces.h"
#if defined(DANGO_MATERIAL_CONTACT_BLOCK_CANDIDATE)
#include "dango_material_contact_block.h"
#endif

typedef struct dango_material_contact_diagnostic
{
    int phase, error, position_iterations, receipt_count;
    bool report_valid;
    float final_speed_scale;
    unsigned long long substeps;
    dango_constraint_state input, positioned, damped, uncapped, output;
    dango_material_ground_receipt receipts[16];
    dango_material_contact_solver solver;
    dango_material_contact_report report;
    dango_material_contact_cap_report cap_report;
#if defined(DANGO_MATERIAL_CONTACT_BLOCK_CANDIDATE)
    dango_material_contact_block block;
    dango_material_contact_block_report block_report;
#endif
} dango_material_contact_diagnostic;

static b2BodyId dango_mca_id(dango_self_soft_body* body, int n)
{ return n == 16 ? body->core_body_id : body->node_body_ids[n]; }

static int dango_mca_failure(dango_material_contact_diagnostic* diagnostic, int error)
{ diagnostic->error = error; return error; }

static bool dango_mca_receipts(dango_self_soft_body* body, dango_material_contact_diagnostic* diagnostic)
{
    diagnostic->receipt_count = 0;
    for (int n = 0; n < 16; ++n)
    {
        int capacity = b2Body_GetContactCapacity(body->node_body_ids[n]);
        if (capacity < 0 || (size_t)capacity > SIZE_MAX / sizeof(b2ContactData)) return false;
        b2ContactData* contacts = capacity ? malloc((size_t)capacity * sizeof(*contacts)) : NULL;
        if (capacity && !contacts) return false;
        int count = capacity ? b2Body_GetContactData(body->node_body_ids[n], contacts, capacity) : 0;
        bool valid = count >= 0 && count <= capacity;
        dango_material_ground_receipt receipt = {0}; receipt.node = n;
        receipt.friction = (double)sqrtf(b2Shape_GetFriction(body->node_shape_ids[n]) * b2Shape_GetFriction(body->ground_shape_id));
        int ground_manifolds = 0;
        for (int i = 0; valid && i < count; ++i)
        {
            b2ContactData* c = &contacts[i];
            bool ground_a = B2_ID_EQUALS(c->shapeIdA, body->ground_shape_id), ground_b = B2_ID_EQUALS(c->shapeIdB, body->ground_shape_id);
            bool node_a = B2_ID_EQUALS(c->shapeIdA, body->node_shape_ids[n]), node_b = B2_ID_EQUALS(c->shapeIdB, body->node_shape_ids[n]);
            if (!(ground_a || ground_b)) continue;
            if (!(node_a || node_b) || ground_a == ground_b || ++ground_manifolds > 1 || c->manifold.pointCount < 1 || c->manifold.pointCount > 2) { valid = false; break; }
            b2Vec2 normal = b2MulSV(node_a ? -1.0f : 1.0f, c->manifold.normal);
            if (!isfinite(normal.x) || !isfinite(normal.y) || fabsf(normal.x) > 1e-6f || fabsf(normal.y - 1) > 1e-6f) { valid = false; break; }
            b2Vec2 tangent = b2RightPerp(normal);
            for (int p = 0; p < c->manifold.pointCount; ++p)
            {
                b2ManifoldPoint* point = &c->manifold.points[p];
                if (!isfinite(point->normalImpulse) || !isfinite(point->tangentImpulse) || !isfinite(point->totalNormalImpulse) || point->normalImpulse < 0 || point->totalNormalImpulse < 0 ||
                    (p && point->id == c->manifold.points[0].id)) { valid = false; break; }
                receipt.normal_impulse += point->normalImpulse * (double)normal.y;
                receipt.tangent_impulse += point->tangentImpulse * (double)tangent.x;
                receipt.total_normal_impulse += point->totalNormalImpulse;
            }
        }
        free(contacts);
        if (!valid) return false;
        if (ground_manifolds) diagnostic->receipts[diagnostic->receipt_count++] = receipt;
    }
    return true;
}

static bool dango_mca_geometry(const dango_constraint_state* state)
{
    dango_material_vec2 points[16], forces[16]; double energy;
    for (int n = 0; n < 16; ++n) points[n] = (dango_material_vec2){state->positions[n].x, state->positions[n].y};
    // The independent material validator rejects crossings/degenerate CCW area.
    if (dango_material_area(points, 16, 1, state->radial_length, 0, &energy, forces)) return false;
    dango_constraint_state copy = *state;
    return dango_constraint_sweep_order_mode(&copy, false, false, DANGO_CONSTRAINT_CORRECT_POSITION) <= DANGO_CONSTRAINT_TOLERANCE;
}

static int dango_material_contact_substep(dango_self_soft_body* body, const dango_self_physics_config* config,
    float dt, const float* controls, dango_material_contact_diagnostic* diagnostic)
{
    unsigned long long substeps = diagnostic->substeps + 1;
    memset(diagnostic, 0, sizeof(*diagnostic)); diagnostic->substeps = substeps; diagnostic->phase = 1;
    dango_constraint_state state = {0};
    state.radial_length = body->radial_rest_length; state.perimeter_length = body->perimeter_rest_length;
    state.radial_min = config->radial_minimum_ratio; state.radial_max = config->radial_maximum_ratio;
    state.perimeter_min = config->perimeter_minimum_ratio; state.perimeter_max = config->perimeter_maximum_ratio;
    state.radius = config->node_radius; state.ground = config->ground_height;
    float masses[17];
    for (int n = 0; n < 17; ++n)
    {
        b2BodyId id = dango_mca_id(body, n); masses[n] = b2Body_GetMass(id);
        state.positions[n] = b2Body_GetPosition(id); state.velocities[n] = b2Body_GetLinearVelocity(id);
        state.inverse_mass[n] = 1 / masses[n];
    }
    diagnostic->input = state;
#ifdef DANGO_IDLE_TRACE_STATE
    DANGO_IDLE_TRACE_STATE(2, &state);
#endif
    if (!dango_mca_receipts(body, diagnostic)) { diagnostic->report.status = DANGO_MATERIAL_CONTACT_RECEIPT; return dango_mca_failure(diagnostic, -96); }
    diagnostic->phase = 2; bool position_valid = false;
    for (int iteration = 0; iteration < 384; ++iteration)
    {
        diagnostic->position_iterations = iteration + 1;
        float residual = dango_constraint_sweep_order_mode(&state, true, (iteration & 1) != 0, DANGO_CONSTRAINT_CORRECT_POSITION);
        if (!isfinite(residual)) break;
        if (dango_constraint_sweep_order_mode(&state, false, false, DANGO_CONSTRAINT_CORRECT_POSITION) <= DANGO_CONSTRAINT_TOLERANCE) { position_valid = true; break; }
    }
    diagnostic->positioned = state;
#ifdef DANGO_IDLE_TRACE_STATE
    DANGO_IDLE_TRACE_STATE(3, &state);
#endif
    if (!position_valid || !dango_mca_geometry(&state)) return dango_mca_failure(diagnostic, -91);
    diagnostic->phase = 3;
    bool near_ground = false, active = controls[0] != 0 || controls[1] != 0 || controls[2] != 0 || controls[3] != 0;
    for (int n = 0; n < 16; ++n) if (state.positions[n].y - config->node_radius <= config->ground_height + .001f) near_ground = true;
    passive_spin(state.positions, state.velocities, masses, dt, near_ground, active);
    if (near_ground && !active)
    {
        float mass = 0, horizontal = 0;
        for (int n = 0; n < 17; ++n) { mass += masses[n]; horizontal += masses[n] * state.velocities[n].x; }
        float delta = b2ClampFloat(horizontal / mass, -35 * dt, 35 * dt);
        for (int n = 0; n < 17; ++n) state.velocities[n].x = (state.velocities[n].x - delta) * expf(-60 * dt);
    }
    diagnostic->damped = state; diagnostic->phase = 4;
#ifdef DANGO_IDLE_TRACE_STATE
    DANGO_IDLE_TRACE_STATE(4, &state);
#endif
    diagnostic->report_valid = true;
#if defined(DANGO_MATERIAL_CONTACT_BLOCK_CANDIDATE)
    bool block_built = dango_material_contact_block_build(&state, diagnostic->receipts, diagnostic->receipt_count,
        dt, &diagnostic->block, &diagnostic->block_report);
    bool block_solved = block_built && dango_material_contact_block_solve(&diagnostic->block,
        &diagnostic->uncapped, &diagnostic->block_report);
    diagnostic->solver = diagnostic->block.contact;
    diagnostic->report = diagnostic->block_report.final_rows;
    if (!block_solved) return dango_mca_failure(diagnostic, -96);
#else
    if (!dango_material_contact_build(&state, diagnostic->receipts, diagnostic->receipt_count, dt, &diagnostic->solver, &diagnostic->report)) return dango_mca_failure(diagnostic, -96);
    if (!dango_material_contact_solve(&diagnostic->solver, &diagnostic->uncapped, &diagnostic->report)) return dango_mca_failure(diagnostic, -96);
#endif
    state = diagnostic->uncapped; diagnostic->phase = 5;
#ifdef DANGO_IDLE_TRACE_STATE
    DANGO_IDLE_TRACE_STATE(5, &state);
#endif
    if (!dango_mca_geometry(&state)) return dango_mca_failure(diagnostic, -91);
    // A common positive scale preserves every homogeneous rate inequality.
    float maximum = 0;
    for (int n = 0; n < 17; ++n) maximum = fmaxf(maximum, b2Length(state.velocities[n]));
    if (!isfinite(maximum)) return dango_mca_failure(diagnostic, -87);
    diagnostic->final_speed_scale = maximum > config->maximum_speed ? (config->maximum_speed * .99999f) / maximum : 1.0f;
    if (diagnostic->final_speed_scale < 1)
        for (int n = 0; n < 17; ++n) state.velocities[n] = b2MulSV(diagnostic->final_speed_scale, state.velocities[n]);
    diagnostic->output = state;
#ifdef DANGO_IDLE_TRACE_STATE
    DANGO_IDLE_TRACE_STATE(6, &state);
#endif
#if defined(DANGO_MATERIAL_CONTACT_BLOCK_CANDIDATE)
    if (!dango_material_contact_block_validate_after_uniform_scale(&diagnostic->block, &diagnostic->uncapped,
        &state, diagnostic->final_speed_scale, config->maximum_speed, &diagnostic->cap_report)) return dango_mca_failure(diagnostic, -87);
#else
    if (!dango_material_contact_validate_after_uniform_scale(&diagnostic->solver, &diagnostic->uncapped,
        &state, diagnostic->final_speed_scale, config->maximum_speed, &diagnostic->cap_report)) return dango_mca_failure(diagnostic, -87);
#endif
    // No candidate position/velocity is written before all stages pass.
    body->grounded = false;
    for (int n = 0; n < 17; ++n)
    {
        b2BodyId id = dango_mca_id(body, n);
        if (b2DistanceSquared(b2Body_GetPosition(id), state.positions[n]) > 0) b2Body_SetTransform(id, state.positions[n], b2Rot_identity);
        b2Body_SetLinearVelocity(id, state.velocities[n]);
        if (n < 16 && state.positions[n].y - config->node_radius <= config->ground_height + .035f) body->grounded = true;
    }
    diagnostic->phase = 6; return 0;
}

#endif

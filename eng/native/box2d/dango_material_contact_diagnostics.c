// Source-equivalent single-body capture. No world/cache replay is implied.
#define _CRT_SECURE_NO_WARNINGS
#include <stdio.h>
#include <stdlib.h>
#include <stddef.h>
#include "dango_constraint_observer.h"

struct dango_constraint_state;
struct idle_state;
static void contact_before_constraints(const struct dango_constraint_state* state);
static void contact_after_constraints(bool valid);
static void contact_after_integration(const struct idle_state* before, float dt);
static void contact_after_speed(const struct idle_state* before, const struct idle_state* after);
static void contact_observe(const dango_constraint_observation* event);
static bool contact_observer_enabled;
#define DANGO_CONTACT_TRACE
#if defined(DANGO_CONTACT_OBSERVER_BUILD)
#define DANGO_CONSTRAINT_OBSERVER
#define DANGO_CONSTRAINT_OBSERVER_EMIT contact_observe
#define DANGO_CONSTRAINT_OBSERVER_ENABLED() contact_observer_enabled
#endif
#define main contact_unused_idle_main
#include "dango_material_idle_diagnostics.c"
#undef main

typedef struct contact_record
{
    int frame, small_step;
    unsigned long long sweep;
    dango_constraint_observation row;
} contact_record;

static FILE* contact_trace;
static FILE* contact_events;
static FILE* contact_stages;
static int contact_frame, contact_small_step;
static float contact_controls[6];
static bool contact_selected, contact_capture_failed;
static unsigned long long contact_event_count, contact_event_start, contact_sweep_start;
static double contact_dp[17][2], contact_dv[17][2];
static idle_state contact_input;
static idle_state contact_before_world;
static float contact_small_dt;
static double contact_max_reconstruction_position, contact_max_reconstruction_velocity;

static bool contact_select(int idle_frame)
{
    // Includes the first sample and the first mirrored .003 crossing.
    return idle_frame == 1 || idle_frame == 600 || idle_frame == 601 ||
        idle_frame == 721 || idle_frame == 781 || idle_frame == 1800 ||
        idle_frame == 1801 || idle_frame == 3600 || idle_frame == 3601 || idle_frame == 36000;
}

static void contact_vector(FILE* file, b2Vec2 v)
{
    fprintf(file, "[%.9g,%.9g]", (double)v.x, (double)v.y);
}

static void contact_energy(FILE* file, const idle_state* state)
{
    double gravity = 0, springs = 0, area = NAN, bending = NAN;
    dango_material_vec2 points[16], forces[16];
    for (int n = 0; n < 17; ++n) gravity += idle_masses[n] * idle_config.gravity * state->positions[n].y;
    for (int n = 0; n < 16; ++n)
    {
        points[n] = (dango_material_vec2){state->positions[n].x, state->positions[n].y};
        double radial = b2Distance(state->positions[n], state->positions[16]) - idle_body->radial_rest_length;
        double perimeter = b2Distance(state->positions[n], state->positions[(n + 1) % 16]) - idle_body->perimeter_rest_length;
        double radial_mass = (double)idle_masses[n] * idle_masses[16] / (idle_masses[n] + idle_masses[16]);
        double perimeter_mass = (double)idle_masses[n] * idle_masses[(n + 1) % 16] / (idle_masses[n] + idle_masses[(n + 1) % 16]);
        double wr = 6.28318530717958647692 * idle_config.radial_hertz;
        double wp = 6.28318530717958647692 * idle_config.perimeter_hertz;
        springs += .5 * (radial_mass * wr * wr * radial * radial + perimeter_mass * wp * wp * perimeter * perimeter);
    }
    double k = idle_config.local_angle_balance_strength * (double)idle_body->radial_rest_length * tan(3.14159265358979323846 / 16) * .5;
    int area_status = dango_material_area(points, 16, idle_body->target_area, idle_body->radial_rest_length, idle_config.area_strength, &area, forces);
    int bending_status = dango_material_bending(points, 16, k, &bending, forces);
    if (area_status || bending_status) contact_capture_failed = true;
    fprintf(file, "{\"kinetic\":%.17g,\"gravityPotential\":%.17g,\"springEstimate\":%.17g,\"areaStatus\":%d,\"area\":%.17g,\"bendingStatus\":%d,\"bending\":%.17g}",
        state->energy, gravity, springs, area_status, area_status ? 0 : area, bending_status, bending_status ? 0 : bending);
}

static void contact_state(FILE* file, const dango_constraint_state* state)
{
    fprintf(file, "{\"radialLength\":%.9g,\"perimeterLength\":%.9g,\"radialMin\":%.9g,\"radialMax\":%.9g,\"perimeterMin\":%.9g,\"perimeterMax\":%.9g,\"radius\":%.9g,\"ground\":%.9g,\"bodies\":[",
        (double)state->radial_length, (double)state->perimeter_length, (double)state->radial_min, (double)state->radial_max,
        (double)state->perimeter_min, (double)state->perimeter_max, (double)state->radius, (double)state->ground);
    for (int n = 0; n < 17; ++n)
    {
        fprintf(file, "%s{\"position\":", n ? "," : ""); contact_vector(file, state->positions[n]);
        fputs(",\"velocity\":", file); contact_vector(file, state->velocities[n]);
        fprintf(file, ",\"inverseMass\":%.9g}", (double)state->inverse_mass[n]);
    }
    fputs("]}", file);
}

static void contact_contacts(FILE* file)
{
    fputs("[", file); bool first = true;
    for (int n = 0; n < 17; ++n)
    {
        int capacity = b2Body_GetContactCapacity(idle_id(n));
        if (capacity < 0 || (size_t)capacity > SIZE_MAX / sizeof(b2ContactData)) { contact_capture_failed = true; break; }
        b2ContactData* data = capacity ? malloc((size_t)capacity * sizeof(*data)) : NULL;
        if (capacity && !data) { contact_capture_failed = true; break; }
        int count = capacity ? b2Body_GetContactData(idle_id(n), data, capacity) : 0;
        if (count < 0 || count > capacity) { contact_capture_failed = true; free(data); break; }
        for (int i = 0; i < count; ++i)
        {
            b2ContactData* c = &data[i];
            bool node_a = n < 16 ? B2_ID_EQUALS(c->shapeIdA, idle_body->node_shape_ids[n]) : B2_ID_EQUALS(c->shapeIdA, idle_body->core_shape_id);
            bool ground_a = B2_ID_EQUALS(c->shapeIdA, idle_body->ground_shape_id);
            bool ground_b = B2_ID_EQUALS(c->shapeIdB, idle_body->ground_shape_id);
            if (c->manifold.pointCount < 0 || c->manifold.pointCount > 2) { contact_capture_failed = true; continue; }
            b2Vec2 normal = b2MulSV(node_a ? -1.0f : 1.0f, c->manifold.normal);
            fprintf(file, "%s{\"body\":%d,\"capacity\":%d,\"returnedCount\":%d,\"nodeIsA\":%s,\"groundIsA\":%s,\"groundIsB\":%s,\"rawNormal\":",
                first ? "" : ",", n, capacity, count, node_a ? "true" : "false", ground_a ? "true" : "false", ground_b ? "true" : "false"); first = false;
            contact_vector(file, c->manifold.normal); fputs(",\"normalOnNode\":", file); contact_vector(file, normal);
            fputs(",\"rightTangentOnNode\":", file); contact_vector(file, b2RightPerp(normal));
            fprintf(file, ",\"rollingImpulse\":%.9g,\"points\":[", (double)c->manifold.rollingImpulse);
            for (int p = 0; p < c->manifold.pointCount; ++p)
            {
                b2ManifoldPoint* point = &c->manifold.points[p];
                fprintf(file, "%s{\"id\":%u,\"persisted\":%s,\"separation\":%.9g,\"normalImpulse\":%.9g,\"tangentImpulse\":%.9g,\"totalNormalImpulse\":%.9g,\"normalVelocity\":%.9g,\"point\":",
                    p ? "," : "", point->id, point->persisted ? "true" : "false", (double)point->separation, (double)point->normalImpulse,
                    (double)point->tangentImpulse, (double)point->totalNormalImpulse, (double)point->normalVelocity);
                contact_vector(file, point->point); fputs(",\"anchorA\":", file); contact_vector(file, point->anchorA);
                fputs(",\"anchorB\":", file); contact_vector(file, point->anchorB); fputc('}', file);
            }
            fputs("]}", file);
        }
        free(data);
    }
    fputc(']', file);
}

static void contact_before_constraints(const dango_constraint_state* state)
{
    ++contact_small_step;
    if (!contact_selected) return;
    contact_input = idle_capture(); contact_event_start = contact_event_count; contact_sweep_start = dango_constraint_sweep_count;
    memset(contact_dp, 0, sizeof contact_dp); memset(contact_dv, 0, sizeof contact_dv);
    fprintf(contact_trace, "{\"worldFrame\":%d,\"idleFrame\":%d,\"smallStep\":%d,\"smallDt\":%.17g,\"targetArea\":%.9g,\"areaStrength\":%.9g,\"bendingStrength\":%.9g,\"radialHertz\":%.9g,\"perimeterHertz\":%.9g,\"radialDamping\":%.9g,\"perimeterDamping\":%.9g,\"nodeFriction\":%.9g,\"floorFriction\":%.9g,\"gravity\":%.9g,\"groundedFlagBeforeProjection\":%s,\"selectedDragNode\":%d,\"controls\":[", contact_frame, contact_frame - 6000, contact_small_step, (double)contact_small_dt, (double)idle_body->target_area, (double)idle_config.area_strength, (double)idle_config.local_angle_balance_strength, (double)idle_config.radial_hertz, (double)idle_config.perimeter_hertz, (double)idle_config.radial_damping, (double)idle_config.perimeter_damping, (double)b2Shape_GetFriction(idle_body->node_shape_ids[0]), (double)b2Shape_GetFriction(idle_body->ground_shape_id), (double)idle_config.gravity, idle_body->grounded ? "true" : "false", idle_body->selected_drag_node);
    for (int i = 0; i < 6; ++i) fprintf(contact_trace, "%s%.9g", i ? "," : "", (double)contact_controls[i]);
    fputs("],\"input\":", contact_trace); contact_state(contact_trace, state);
    dango_constraint_state world_before = idle_constraint_state(&contact_before_world);
    fputs(",\"beforeWorldStep\":", contact_trace); contact_state(contact_trace, &world_before);
    fputs(",\"beforeWorldEnergy\":", contact_trace); contact_energy(contact_trace, &contact_before_world);
    fputs(",\"inputEnergy\":", contact_trace); contact_energy(contact_trace, &contact_input);
    fputs(",\"contactsAfterWorldStep\":", contact_trace); contact_contacts(contact_trace);
    contact_observer_enabled = true;
}

static void contact_after_constraints(bool valid)
{
    contact_observer_enabled = false;
    if (!contact_selected) return;
    idle_state after = idle_capture(); dango_constraint_state state = idle_constraint_state(&after);
    double position_error = 0, velocity_error = 0;
    for (int n = 0; n < 17; ++n)
    {
        position_error = fmax(position_error, fabs(((double)after.positions[n].x - contact_input.positions[n].x) - contact_dp[n][0]));
        position_error = fmax(position_error, fabs(((double)after.positions[n].y - contact_input.positions[n].y) - contact_dp[n][1]));
        velocity_error = fmax(velocity_error, fabs(((double)after.velocities[n].x - contact_input.velocities[n].x) - contact_dv[n][0]));
        velocity_error = fmax(velocity_error, fabs(((double)after.velocities[n].y - contact_input.velocities[n].y) - contact_dv[n][1]));
    }
    contact_max_reconstruction_position = fmax(contact_max_reconstruction_position, position_error);
    contact_max_reconstruction_velocity = fmax(contact_max_reconstruction_velocity, velocity_error);
    fprintf(contact_trace, ",\"valid\":%s,\"sweeps\":%llu,\"eventOffset\":%llu,\"eventCount\":%llu,\"positionReconstructionError\":%.17g,\"velocityReconstructionError\":%.17g,\"output\":",
        valid ? "true" : "false", dango_constraint_sweep_count - contact_sweep_start, contact_event_start, contact_event_count - contact_event_start, position_error, velocity_error);
    contact_state(contact_trace, &state); fputs(",\"outputEnergy\":", contact_trace); contact_energy(contact_trace, &after);
    fputs("}\n", contact_trace);
}

static void contact_after_integration(const idle_state* before, float dt)
{
    if (!contact_selected) return;
    contact_before_world = *before; contact_small_dt = dt;
}

static void contact_after_speed(const idle_state* before, const idle_state* after)
{
    if (!contact_selected) return;
    fprintf(contact_stages, "{\"worldFrame\":%d,\"smallStep\":%d,\"afterConstraintsEnergy\":", contact_frame, contact_small_step);
    contact_energy(contact_stages, &idle_after_constraints);
    fputs(",\"afterGroundDampingEnergy\":", contact_stages); contact_energy(contact_stages, before);
    fputs(",\"afterSpeedLimitEnergy\":", contact_stages); contact_energy(contact_stages, after);
    dango_constraint_state state = idle_constraint_state(before);
    fputs(",\"afterGroundDamping\":", contact_stages); contact_state(contact_stages, &state);
    state = idle_constraint_state(after); fputs(",\"afterSpeedLimit\":", contact_stages); contact_state(contact_stages, &state);
    fputs("}\n", contact_stages);
}

static void contact_observe(const dango_constraint_observation* event)
{
    contact_record record = {0}; record.frame = contact_frame; record.small_step = contact_small_step;
    record.sweep = dango_constraint_sweep_count - contact_sweep_start; record.row = *event;
    if (fwrite(&record, sizeof record, 1, contact_events) != 1) contact_capture_failed = true;
    ++contact_event_count;
    for (int i = 0; i < event->count; ++i)
    {
        int n = event->ids[i]; if (n < 0 || n >= 17) { contact_capture_failed = true; continue; }
        contact_dp[n][0] += (double)event->positions_after[i].x - event->positions_before[i].x;
        contact_dp[n][1] += (double)event->positions_after[i].y - event->positions_before[i].y;
        contact_dv[n][0] += (double)event->velocities_after[i].x - event->velocities_before[i].x;
        contact_dv[n][1] += (double)event->velocities_after[i].y - event->velocities_before[i].y;
    }
}

static FILE* contact_new_file(const char* prefix, const char* suffix, const char* mode)
{
    char path[1024]; if (snprintf(path, sizeof path, "%s%s", prefix, suffix) < 0 || strlen(prefix) + strlen(suffix) >= sizeof path) return NULL;
    FILE* existing = fopen(path, "rb"); if (existing) { fclose(existing); return NULL; }
    return fopen(path, mode);
}

int main(int argc, char** argv)
{
    if (argc != 3 || (strcmp(argv[2], "forward") && strcmp(argv[2], "mirror"))) { fprintf(stderr, "Usage: capture output-prefix forward|mirror\n"); return 2; }
    // Keep both symbols used in the plain compilation, while its solver has no observer hooks.
    (void)contact_observe;
    FILE* rows = contact_new_file(argv[1], ".frames.bin", "wb");
    FILE* counts = contact_new_file(argv[1], ".cost.bin", "wb");
    FILE* report = contact_new_file(argv[1], ".json", "w");
    contact_trace = contact_new_file(argv[1], ".inputs.jsonl", "w");
    contact_events = contact_new_file(argv[1], ".events.bin", "wb");
    contact_stages = contact_new_file(argv[1], ".stages.jsonl", "w");
    if (!rows || !counts || !report || !contact_trace || !contact_events || !contact_stages) { fprintf(stderr, "Unable to create new capture files; existing evidence is never overwritten.\n"); return 2; }
    idle_variant = IDLE_GRADIENTS_HALF_PHASE; idle_material_mode = 7; idle_config = idle_defaults(true);
    void* handle = NULL; int id = 0;
    int error = dango_physics_world_create(&idle_config, 1, &handle);
    if (!error) error = dango_physics_world_add(handle, 0, 0, &id);
    if (error) return 1;
    idle_body = ((collision_review*)handle)->bodies[0];
    for (int n = 0; n < 17; ++n) idle_masses[n] = b2Body_GetMass(idle_id(n));
    idle_cached = idle_capture();
    bool mirror = !strcmp(argv[2], "mirror"); int state_id = 0, count = 0, contacts = 0, latched = 0, completed = 0;
    float row[42] = {0}; double low[4] = {INFINITY, INFINITY, INFINITY, INFINITY}, high[4] = {-INFINITY, -INFINITY, -INFINITY, -INFINITY};
    for (contact_frame = 1; contact_frame <= 42000; ++contact_frame)
    {
        memset(contact_controls, 0, sizeof contact_controls); contact_small_step = 0;
        if (contact_frame <= 6000)
        {
            int i = contact_frame - 1; contact_controls[3] = i >= 600 && i % 600 < 480 ? 1.0f : 0.0f;
            contact_controls[4] = (float)((mirror ? -1 : 1) * 2.4 * sin(i * .12));
            contact_controls[5] = (float)(.4 + 1.5 * cos(i * .17));
        }
        contact_selected = contact_select(contact_frame - 6000);
        unsigned long long before[2] = {dango_constraint_sweep_count, dango_constraint_row_visit_count};
        error = dango_physics_world_step(handle, .0166666f, &id, contact_controls, 1, &state_id, row, 42, &count, &contacts, &latched);
        unsigned long long cost[2] = {dango_constraint_sweep_count - before[0], dango_constraint_row_visit_count - before[1]};
        if (fwrite(row, sizeof row, 1, rows) != 1 || fwrite(cost, sizeof cost, 1, counts) != 1) contact_capture_failed = true;
        if (error || latched || count != 1 || state_id != id) break;
        ++completed;
        int index = contact_frame - 6001;
        if (index >= 600 && (index % 60 == 0 || index >= 35400))
        {
            double min_x = INFINITY, min_y = INFINITY, max_x = -INFINITY, max_y = -INFINITY;
            for (int n = 0; n < 16; ++n) { min_x = fmin(min_x, row[2*n]); max_x = fmax(max_x, row[2*n]); min_y = fmin(min_y, row[2*n+1]); max_y = fmax(max_y, row[2*n+1]); }
            double values[4] = {row[32], row[33], max_x-min_x, max_y-min_y};
            for (int n = 0; n < 4; ++n) { low[n] = fmin(low[n], values[n]); high[n] = fmax(high[n], values[n]); }
        }
        if (contact_frame % 6000 == 0) printf("%s frame=%d events=%llu\n", argv[2], contact_frame, contact_event_count);
    }
    fprintf(report, "{\"schema\":1,\"materialMode\":7,\"mirroredX\":%s,\"completedFrames\":%d,\"error\":%d,\"latchedError\":%d,\"captureFailed\":%s,\"recordBytes\":%zu,\"rowOffset\":%zu,\"observationBytes\":%zu,\"events\":%llu,\"reconstructionMaxPosition\":%.17g,\"reconstructionMaxVelocity\":%.17g,\"observerCompiled\":%s,\"spans\":[",
        mirror ? "true" : "false", completed, error, latched, contact_capture_failed ? "true" : "false", sizeof(contact_record), offsetof(contact_record,row), sizeof(dango_constraint_observation), contact_event_count, contact_max_reconstruction_position, contact_max_reconstruction_velocity,
#if defined(DANGO_CONTACT_OBSERVER_BUILD)
        "true"
#else
        "false"
#endif
    );
    for (int n = 0; n < 4; ++n) fprintf(report, "%s%.17g", n ? "," : "", isfinite(low[n]) ? high[n] - low[n] : 0);
    fputs("],\"scope\":\"Solver input copies and row events only; no Box2D world/cache checkpoint. Frame stream is all 42 native floats; cost stream is uint64 sweep and row-visit counts. Spring energy is a linear center-anchor estimate, excludes damping/contact/limit energy; material energies are uncapped potentials.\"}\n", report);
    idle_body = NULL; int destroyed = dango_physics_world_destroy(handle);
    int io = fclose(rows) | fclose(counts) | fclose(report) | fclose(contact_trace) | fclose(contact_events) | fclose(contact_stages);
    return error || latched || destroyed || io || contact_capture_failed || completed != 42000 ? 1 : 0;
}

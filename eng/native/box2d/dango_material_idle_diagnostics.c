// Standalone diagnostic only. The included production sources are unchanged.
// Baselines forward every intercepted physics call exactly once.
#define _CRT_SECURE_NO_WARNINGS
#include <stdio.h>
#include <string.h>
#include <time.h>
#include "box2d/box2d.h"
#include "dango_self_physics_internal.h"

static int idle_material_mode;
#define DANGO_MATERIAL_EXPERIMENT_MODE idle_material_mode

static void idle_force(const char* caller, b2BodyId id, b2Vec2 force, bool wake);
static void idle_range(b2JointId id, float minimum, float maximum);
static void idle_world_step(b2WorldId world, float dt, int substeps);
static bool idle_constraints(dango_self_soft_body* body, const dango_self_physics_config* config);
static bool idle_limit(dango_self_soft_body* body, float limit);

#define DANGO_JOINT_BASELINE_COUNTERS
#define b2Body_ApplyForceToCenter(...) idle_force(__func__, __VA_ARGS__)
#define b2DistanceJoint_SetLengthRange idle_range
#define b2World_Step idle_world_step
#define dango_self_solve_constraints idle_original_constraints
#define dango_self_limit_speed idle_original_limit
#include "dango_box2d_bridge.c"
#undef dango_self_solve_constraints
#undef dango_self_limit_speed
#define dango_self_solve_constraints idle_constraints
#define dango_self_limit_speed idle_limit
#include "dango_collision_review.c"
#undef dango_self_solve_constraints
#undef dango_self_limit_speed
#undef b2Body_ApplyForceToCenter
#undef b2DistanceJoint_SetLengthRange
#undef b2World_Step

enum { IDLE_AREA, IDLE_TOP, IDLE_GROUND, IDLE_REPULSION, IDLE_ANGLE, IDLE_CONVEXITY, IDLE_BALANCE, IDLE_FORCE_COUNT };
enum { IDLE_INTEGRATION, IDLE_CONSTRAINTS, IDLE_DAMPING, IDLE_SPEED, IDLE_STAGE_COUNT };
enum { IDLE_BASELINE, IDLE_AREA_REACTION, IDLE_NO_BALANCE, IDLE_CACHED_RANGE,
    IDLE_AREA_GRADIENT, IDLE_BENDING_GRADIENT, IDLE_MATERIAL_GRADIENTS, IDLE_GRADIENTS_CACHED,
    IDLE_GRADIENTS_HALF_PHASE, IDLE_GRADIENTS_HALF_PHASE_NO_SPIN,
    IDLE_GRADIENTS_HALF_PHASE_CONFIGURED_FRICTION, IDLE_GRADIENTS_HALF_PHASE_PROJECTED_RADIAL };
static const char* force_names[] = {"area", "top", "ground_spreading", "repulsion", "angle_barrier", "convexity", "local_balance"};
static const char* stage_names[] = {"box2d_integration", "hard_constraints", "ground_damping", "speed_limit"};
static const char* touch_names[] = {"radial_lower", "radial_upper", "perimeter_lower", "perimeter_upper", "raw_angle_lower", "raw_angle_upper", "display_angle_lower", "display_angle_upper", "ground"};

typedef struct idle_state
{
    b2Vec2 positions[17], velocities[17];
    double mass, center_x, center_y, momentum_x, momentum_y, angular_momentum, energy;
    double raw_width, raw_height, display_width, display_height, raw_area;
} idle_state;

typedef struct idle_force_total
{
    double fx, fy, torque_com, torque_core, power, absolute_force;
    unsigned long long calls;
} idle_force_total;

typedef struct idle_stage_total
{
    double center_x, center_y, momentum_x, momentum_y, angular_momentum, energy;
    double position_norm, velocity_norm, max_position, max_velocity;
    double raw_width, raw_height, display_width, display_height;
    double node_position[17][2], node_velocity[17][2];
} idle_stage_total;

typedef struct idle_window
{
    double duration;
    unsigned long long substeps, sweeps, max_sweeps, range_resets, range_skips;
    idle_force_total forces[IDLE_FORCE_COUNT];
    idle_stage_total stages[IDLE_STAGE_COUNT];
    unsigned long long touches[9][16];
    double max_pre_residual, max_post_residual;
} idle_window;

static dango_self_soft_body* idle_body;
static dango_self_physics_config idle_config;
static float idle_masses[17];
static idle_state idle_cached, idle_after_constraints;
static idle_window idle_stats;
static b2Vec2 idle_step_forces[IDLE_FORCE_COUNT][17];
static unsigned long long idle_step_calls[IDLE_FORCE_COUNT];
static double idle_step_absolute[IDLE_FORCE_COUNT];
static int idle_variant;

static b2BodyId idle_id(int node)
{
    return node == 16 ? idle_body->core_body_id : idle_body->node_body_ids[node];
}

static idle_state idle_capture(void)
{
    idle_state value = {0};
    double min_x = INFINITY, min_y = INFINITY, max_x = -INFINITY, max_y = -INFINITY;
    double display_min_x = INFINITY, display_min_y = INFINITY, display_max_x = -INFINITY, display_max_y = -INFINITY;
    for (int n = 0; n < 17; ++n)
    {
        value.positions[n] = b2Body_GetPosition(idle_id(n));
        value.velocities[n] = b2Body_GetLinearVelocity(idle_id(n));
        double mass = idle_masses[n];
        value.mass += mass;
        value.center_x += mass * value.positions[n].x;
        value.center_y += mass * value.positions[n].y;
        value.momentum_x += mass * value.velocities[n].x;
        value.momentum_y += mass * value.velocities[n].y;
        value.energy += .5 * mass * b2Dot(value.velocities[n], value.velocities[n]);
    }
    value.center_x /= value.mass; value.center_y /= value.mass;
    for (int n = 0; n < 17; ++n)
    {
        double x = value.positions[n].x - value.center_x, y = value.positions[n].y - value.center_y;
        value.angular_momentum += idle_masses[n] * (x * value.velocities[n].y - y * value.velocities[n].x);
        if (n == 16) continue;
        b2Vec2 position = value.positions[n], next = value.positions[(n + 1) % 16];
        min_x = fmin(min_x, position.x); max_x = fmax(max_x, position.x);
        min_y = fmin(min_y, position.y); max_y = fmax(max_y, position.y);
        b2Vec2 radial = b2Sub(position, value.positions[16]);
        float length = b2Length(radial);
        b2Vec2 display = length > .0001f ? b2MulAdd(position, idle_config.node_radius / length, radial) : position;
        display_min_x = fmin(display_min_x, display.x); display_max_x = fmax(display_max_x, display.x);
        display_min_y = fmin(display_min_y, display.y); display_max_y = fmax(display_max_y, display.y);
        value.raw_area += .5 * ((double)position.x * next.y - (double)next.x * position.y);
    }
    value.raw_width = max_x - min_x; value.raw_height = max_y - min_y;
    value.display_width = display_max_x - display_min_x; value.display_height = display_max_y - display_min_y;
    return value;
}

static void idle_delta(int stage, const idle_state* before, const idle_state* after)
{
    idle_stage_total* total = &idle_stats.stages[stage];
    total->center_x += after->center_x - before->center_x; total->center_y += after->center_y - before->center_y;
    total->momentum_x += after->momentum_x - before->momentum_x; total->momentum_y += after->momentum_y - before->momentum_y;
    total->angular_momentum += after->angular_momentum - before->angular_momentum; total->energy += after->energy - before->energy;
    total->raw_width += after->raw_width - before->raw_width; total->raw_height += after->raw_height - before->raw_height;
    total->display_width += after->display_width - before->display_width; total->display_height += after->display_height - before->display_height;
    for (int n = 0; n < 17; ++n)
    {
        b2Vec2 dp = b2Sub(after->positions[n], before->positions[n]), dv = b2Sub(after->velocities[n], before->velocities[n]);
        double position = b2Length(dp), velocity = b2Length(dv);
        total->position_norm += position; total->velocity_norm += velocity;
        total->max_position = fmax(total->max_position, position); total->max_velocity = fmax(total->max_velocity, velocity);
        total->node_position[n][0] += dp.x; total->node_position[n][1] += dp.y;
        total->node_velocity[n][0] += dv.x; total->node_velocity[n][1] += dv.y;
    }
}

static int idle_force_category(const char* caller)
{
    static const char* callers[] = {"dango_self_apply_area_force", "dango_self_apply_top_pressure", "dango_self_apply_ground_deformation_force", "dango_self_apply_radial_repulsion", "dango_self_apply_angle_barrier", "dango_self_apply_convexity_barrier", "dango_self_apply_local_angle_balance_force"};
    for (int i = 0; i < IDLE_FORCE_COUNT; ++i) if (strcmp(caller, callers[i]) == 0) return i;
    return -1;
}

static void idle_record_force(int category, int node, b2Vec2 force)
{
    idle_step_forces[category][node] = b2Add(idle_step_forces[category][node], force);
    idle_step_absolute[category] += b2Length(force); ++idle_step_calls[category];
}

static void idle_force(const char* caller, b2BodyId id, b2Vec2 force, bool wake)
{
    if (idle_body)
    {
        int category = idle_force_category(caller);
        if (category >= 0)
        {
            if (idle_variant == IDLE_NO_BALANCE && category == IDLE_BALANCE) return;
            for (int n = 0; n < 17; ++n) if (B2_ID_EQUALS(id, idle_id(n))) { idle_record_force(category, n, force); break; }
        }
    }
    b2Body_ApplyForceToCenter(id, force, wake);
}

static void idle_range(b2JointId id, float minimum, float maximum)
{
    if (idle_body)
    {
        if ((idle_variant == IDLE_CACHED_RANGE || idle_variant == IDLE_GRADIENTS_CACHED) &&
            minimum == b2DistanceJoint_GetMinLength(id) && maximum == b2DistanceJoint_GetMaxLength(id))
        {
            ++idle_stats.range_skips; return;
        }
        ++idle_stats.range_resets;
    }
    b2DistanceJoint_SetLengthRange(id, minimum, maximum);
}

static void idle_world_step(b2WorldId world, float dt, int substeps)
{
    if (!idle_body) { b2World_Step(world, dt, substeps); return; }
    if (idle_variant == IDLE_AREA_REACTION)
    {
        b2Vec2 reaction = {0};
        for (int n = 0; n < 16; ++n) reaction = b2Sub(reaction, idle_step_forces[IDLE_AREA][n]);
        idle_record_force(IDLE_AREA, 16, reaction);
        b2Body_ApplyForceToCenter(idle_body->core_body_id, reaction, true);
    }
    for (int category = 0; category < IDLE_FORCE_COUNT; ++category)
    {
        idle_force_total* total = &idle_stats.forces[category];
        total->calls += idle_step_calls[category]; total->absolute_force += dt * idle_step_absolute[category];
        for (int n = 0; n < 17; ++n)
        {
            b2Vec2 force = idle_step_forces[category][n], position = idle_cached.positions[n], velocity = idle_cached.velocities[n];
            total->fx += dt * force.x; total->fy += dt * force.y;
            total->torque_com += dt * ((position.x - idle_cached.center_x) * force.y - (position.y - idle_cached.center_y) * force.x);
            total->torque_core += dt * ((double)(position.x - idle_cached.positions[16].x) * force.y - (double)(position.y - idle_cached.positions[16].y) * force.x);
            total->power += dt * ((double)force.x * velocity.x + (double)force.y * velocity.y);
        }
    }
    memset(idle_step_forces, 0, sizeof idle_step_forces); memset(idle_step_calls, 0, sizeof idle_step_calls); memset(idle_step_absolute, 0, sizeof idle_step_absolute);
    idle_state before = idle_capture();
    b2World_Step(world, dt, substeps);
    idle_state after = idle_capture(); idle_delta(IDLE_INTEGRATION, &before, &after);
#if defined(DANGO_CONTACT_TRACE)
    contact_after_integration(&before, dt);
#endif
    idle_stats.duration += dt; ++idle_stats.substeps;
}

static dango_constraint_state idle_constraint_state(const idle_state* value)
{
    dango_constraint_state state = {0};
    state.radial_length = idle_body->radial_rest_length; state.perimeter_length = idle_body->perimeter_rest_length;
    state.radial_min = idle_config.radial_minimum_ratio; state.radial_max = idle_config.radial_maximum_ratio;
    state.perimeter_min = idle_config.perimeter_minimum_ratio; state.perimeter_max = idle_config.perimeter_maximum_ratio;
    state.radius = idle_config.node_radius; state.ground = idle_config.ground_height;
    for (int n = 0; n < 17; ++n) { state.positions[n] = value->positions[n]; state.velocities[n] = value->velocities[n]; state.inverse_mass[n] = 1 / idle_masses[n]; }
    return state;
}

static void idle_touches(const idle_state* value)
{
    dango_constraint_state state = idle_constraint_state(value);
    const float tolerance = DANGO_CONSTRAINT_TOLERANCE;
    for (int n = 0; n < 16; ++n)
    {
        float radial = b2Distance(state.positions[n], state.positions[16]) / state.radial_length;
        float perimeter = b2Distance(state.positions[n], state.positions[(n + 1) % 16]) / state.perimeter_length;
        idle_stats.touches[0][n] += radial <= state.radial_min + tolerance;
        idle_stats.touches[1][n] += radial >= state.radial_max - tolerance;
        idle_stats.touches[2][n] += perimeter <= state.perimeter_min + tolerance;
        idle_stats.touches[3][n] += perimeter >= state.perimeter_max - tolerance;
        for (int display = 0; display < 2; ++display)
        {
            b2Vec2 a = display ? dango_constraint_display_point(&state, (n + 15) % 16) : state.positions[(n + 15) % 16];
            b2Vec2 b = display ? dango_constraint_display_point(&state, n) : state.positions[n];
            b2Vec2 c = display ? dango_constraint_display_point(&state, (n + 1) % 16) : state.positions[(n + 1) % 16];
            b2Vec2 e = b2Sub(b, a), f = b2Sub(c, b);
            float turn = atan2f(b2Cross(e, f), b2Dot(e, f));
            idle_stats.touches[4 + 2 * display][n] += turn <= .002f + tolerance;
            idle_stats.touches[5 + 2 * display][n] += turn >= 1.5687963268f - tolerance;
        }
        idle_stats.touches[8][n] += state.positions[n].y <= state.ground + state.radius + tolerance * state.radial_length;
    }
}

static bool idle_constraints(dango_self_soft_body* body, const dango_self_physics_config* config)
{
    if (!idle_body) return idle_original_constraints(body, config);
    idle_state before = idle_capture();
    dango_constraint_state state = idle_constraint_state(&before);
    idle_stats.max_pre_residual = fmax(idle_stats.max_pre_residual, dango_constraint_sweep(&state, false));
    unsigned long long count = dango_constraint_sweep_count;
#if defined(DANGO_CONTACT_TRACE)
    contact_before_constraints(&state);
#endif
    bool valid = idle_original_constraints(body, config);
#if defined(DANGO_CONTACT_TRACE)
    contact_after_constraints(valid);
#endif
    unsigned long long sweeps = dango_constraint_sweep_count - count;
    idle_stats.sweeps += sweeps; if (sweeps > idle_stats.max_sweeps) idle_stats.max_sweeps = sweeps;
    idle_after_constraints = idle_capture(); idle_delta(IDLE_CONSTRAINTS, &before, &idle_after_constraints);
    state = idle_constraint_state(&idle_after_constraints);
    idle_stats.max_post_residual = fmax(idle_stats.max_post_residual, dango_constraint_sweep(&state, false));
    idle_touches(&idle_after_constraints);
    return valid;
}

static bool idle_limit(dango_self_soft_body* body, float limit)
{
    if (!idle_body) return idle_original_limit(body, limit);
    idle_state before = idle_capture(); idle_delta(IDLE_DAMPING, &idle_after_constraints, &before);
    bool valid = idle_original_limit(body, limit);
    idle_cached = idle_capture(); idle_delta(IDLE_SPEED, &before, &idle_cached);
#if defined(DANGO_CONTACT_TRACE)
    contact_after_speed(&before, &idle_cached);
#endif
    return valid;
}

static void idle_write_state(FILE* output, const idle_state* state)
{
    fprintf(output, "\"massCenter\":[%.17g,%.17g],\"momentum\":[%.17g,%.17g],\"angularMomentum\":%.17g,\"kineticEnergy\":%.17g,\"rawWidth\":%.17g,\"rawHeight\":%.17g,\"displayWidth\":%.17g,\"displayHeight\":%.17g,\"rawArea\":%.17g",
        state->center_x, state->center_y, state->momentum_x, state->momentum_y, state->angular_momentum, state->energy,
        state->raw_width, state->raw_height, state->display_width, state->display_height, state->raw_area);
}

static void idle_write_window(FILE* output, int frame, const float* row)
{
    double duration = idle_stats.duration;
    double min_x = INFINITY, min_y = INFINITY, max_x = -INFINITY, max_y = -INFINITY;
    for (int n = 0; n < 16; ++n)
    {
        min_x = fmin(min_x, row[2 * n]); max_x = fmax(max_x, row[2 * n]);
        min_y = fmin(min_y, row[2 * n + 1]); max_y = fmax(max_y, row[2 * n + 1]);
    }
    fprintf(output, "{\"frame\":%d,\"duration\":%.17g,\"substeps\":%llu,\"solverSweeps\":%llu,\"maxSolverSweeps\":%llu,\"rangeResets\":%llu,\"rangeSkips\":%llu,\"maxPreResidual\":%.17g,\"maxPostResidual\":%.17g,",
        frame, duration, idle_stats.substeps, idle_stats.sweeps, idle_stats.max_sweeps, idle_stats.range_resets, idle_stats.range_skips, idle_stats.max_pre_residual, idle_stats.max_post_residual);
    idle_write_state(output, &idle_cached);
    fprintf(output, ",\"core\":[%.9g,%.9g],\"outputWidth\":%.17g,\"outputHeight\":%.17g,\"forces\":[", (double)row[32], (double)row[33],
        max_x - min_x, max_y - min_y);
    for (int c = 0; c < IDLE_FORCE_COUNT; ++c)
    {
        idle_force_total* t = &idle_stats.forces[c];
        fprintf(output, "%s{\"name\":\"%s\",\"meanForce\":[%.17g,%.17g],\"meanTorqueCom\":%.17g,\"meanTorqueCore\":%.17g,\"meanPower\":%.17g,\"meanAbsoluteAppliedForce\":%.17g,\"calls\":%llu}",
            c ? "," : "", force_names[c], t->fx / duration, t->fy / duration, t->torque_com / duration, t->torque_core / duration, t->power / duration, t->absolute_force / duration, t->calls);
    }
    fputs("],\"stages\":[", output);
    for (int c = 0; c < IDLE_STAGE_COUNT; ++c)
    {
        idle_stage_total* t = &idle_stats.stages[c];
        fprintf(output, "%s{\"name\":\"%s\",\"deltaCenter\":[%.17g,%.17g],\"deltaMomentum\":[%.17g,%.17g],\"deltaAngularMomentum\":%.17g,\"deltaEnergy\":%.17g,\"sumNodePositionNorm\":%.17g,\"sumNodeVelocityNorm\":%.17g,\"maxNodePosition\":%.17g,\"maxNodeVelocity\":%.17g,\"deltaRawSize\":[%.17g,%.17g],\"deltaDisplaySize\":[%.17g,%.17g],\"nodeDeltaPosition\":[",
            c ? "," : "", stage_names[c], t->center_x, t->center_y, t->momentum_x, t->momentum_y, t->angular_momentum, t->energy,
            t->position_norm, t->velocity_norm, t->max_position, t->max_velocity, t->raw_width, t->raw_height, t->display_width, t->display_height);
        for (int n = 0; n < 17; ++n) fprintf(output, "%s[%.17g,%.17g]", n ? "," : "", t->node_position[n][0], t->node_position[n][1]);
        fputs("],\"nodeDeltaVelocity\":[", output);
        for (int n = 0; n < 17; ++n) fprintf(output, "%s[%.17g,%.17g]", n ? "," : "", t->node_velocity[n][0], t->node_velocity[n][1]);
        fputs("]}", output);
    }
    fputs("],\"touchCounts\":{", output);
    for (int c = 0; c < 9; ++c)
    {
        fprintf(output, "%s\"%s\":[", c ? "," : "", touch_names[c]);
        for (int n = 0; n < 16; ++n) fprintf(output, "%s%llu", n ? "," : "", idle_stats.touches[c][n]);
        fputc(']', output);
    }
    fputs("}}", output);
}

static dango_self_physics_config idle_defaults(bool candidate)
{
    dango_self_physics_config config = {
        .node_count = 16, .substep_count = 4, .radial_rest_length = .68f,
        .radial_minimum_ratio = .65f, .radial_maximum_ratio = 1.22f, .perimeter_minimum_ratio = .73f, .perimeter_maximum_ratio = 1.07f,
        .radial_hertz = 5.2f, .perimeter_hertz = 20.1f, .radial_damping = 1.85f, .perimeter_damping = 1.85f,
        .area_strength = 4.8f, .ground_deformation_strength = 3.5f, .repulsion_distance = .09f, .repulsion_strength = .3f,
        .repulsion_maximum_force = 3.5f, .core_exclusion_ratio = .25f, .angle_strength = 10, .local_angle_balance_strength = 1.7f,
        .top_inward_force = .5f, .gravity = 9.8f, .core_mass = .12f, .node_mass = .015f, .drop_height = .42f,
        .ground_height = -.72f, .node_radius = .045f, .friction = .72f, .restitution = .02f, .contact_hertz = 28,
        .linear_damping = 1.8f, .drag_stiffness = 10, .drag_damping = 2.2f, .drag_maximum_force = 8,
        .horizontal_force = 2.4f, .maximum_horizontal_speed = 3.2f, .air_control_ratio = .35f, .downward_force = 2.2f,
        .jump_impulse = .42f, .jump_cooldown_seconds = .45f, .maximum_speed = 12, .minimum_area = .42f
    };
    if (candidate) { config.radial_hertz = 1.82f; config.perimeter_hertz = 15.075f; config.top_inward_force = 0; config.ground_deformation_strength = 0; }
    return config;
}

static bool idle_checkpoint(int frame)
{
    return frame == 600 || frame == 1800 || frame == 3600 || frame == 7200 || frame == 18000 || frame == 36000;
}

static int idle_run(FILE* output, bool candidate, int variant)
{
    const int requested = 36000;
    const float dt = .0166666f;
    idle_variant = variant; idle_body = NULL; idle_config = idle_defaults(candidate);
    idle_material_mode = variant == IDLE_AREA_GRADIENT ? 1 :
        variant == IDLE_BENDING_GRADIENT ? 2 :
        (variant == IDLE_MATERIAL_GRADIENTS || variant == IDLE_GRADIENTS_CACHED) ? 3 :
        variant == IDLE_GRADIENTS_HALF_PHASE ? 7 :
        variant == IDLE_GRADIENTS_HALF_PHASE_NO_SPIN ? 15 :
        variant == IDLE_GRADIENTS_HALF_PHASE_CONFIGURED_FRICTION ? 23 :
        variant == IDLE_GRADIENTS_HALF_PHASE_PROJECTED_RADIAL ? 39 : 0;
    memset(&idle_stats, 0, sizeof idle_stats); memset(idle_step_forces, 0, sizeof idle_step_forces);
    memset(idle_step_calls, 0, sizeof idle_step_calls); memset(idle_step_absolute, 0, sizeof idle_step_absolute);
    void* handle = NULL; int id = 0;
    int error = dango_physics_world_create(&idle_config, 1, &handle);
    if (!error) error = dango_physics_world_add(handle, 0, 0, &id);
    if (error)
    {
        fprintf(output, "{\"name\":\"%s\",\"variant\":%d,\"creationError\":%d}", candidate ? "candidate-default" : "original-default", variant, error);
        if (handle) dango_physics_world_destroy(handle);
        return 1;
    }
    idle_body = ((collision_review*)handle)->bodies[0];
    for (int n = 0; n < 17; ++n) idle_masses[n] = b2Body_GetMass(idle_id(n));
    idle_cached = idle_capture();
    fprintf(output, "{\"name\":\"%s\",\"variant\":%d,\"radialHertz\":%.9g,\"perimeterHertz\":%.9g,\"samples\":[", candidate ? "candidate-default" : "original-default", variant, (double)idle_config.radial_hertz, (double)idle_config.perimeter_hertz);
    float controls[6] = {0}, row[42] = {0}; int state_id, count, contacts, latched = 0, frame;
    bool first_sample = true, first_checkpoint = true;
    // Checkpoints are buffered so samples can stream without retaining 600 windows.
    idle_state checkpoints[6]; float checkpoint_rows[6][42]; int checkpoint_frames[6], checkpoint_count = 0;
    double low[4] = {INFINITY, INFINITY, INFINITY, INFINITY}, high[4] = {-INFINITY, -INFINITY, -INFINITY, -INFINITY};
    clock_t started = clock();
    for (frame = 1; frame <= requested; ++frame)
    {
        error = dango_physics_world_step(handle, dt, &id, controls, 1, &state_id, row, 42, &count, &contacts, &latched);
        if (error || latched || count != 1 || state_id != id) break;
        int index = frame - 1;
        if (index >= 600 && (index % 60 == 0 || index >= 35400))
        {
            double min_x = INFINITY, min_y = INFINITY, max_x = -INFINITY, max_y = -INFINITY;
            for (int n = 0; n < 16; ++n) { min_x = fmin(min_x, row[2 * n]); max_x = fmax(max_x, row[2 * n]); min_y = fmin(min_y, row[2 * n + 1]); max_y = fmax(max_y, row[2 * n + 1]); }
            double values[4] = {row[32], row[33], max_x - min_x, max_y - min_y};
            for (int n = 0; n < 4; ++n) { low[n] = fmin(low[n], values[n]); high[n] = fmax(high[n], values[n]); }
        }
        if (idle_checkpoint(frame)) { checkpoints[checkpoint_count] = idle_cached; memcpy(checkpoint_rows[checkpoint_count], row, sizeof row); checkpoint_frames[checkpoint_count++] = frame; }
        if (frame % 60 == 0)
        {
            if (!first_sample) fputc(',', output); first_sample = false;
            idle_write_window(output, frame, row); memset(&idle_stats, 0, sizeof idle_stats);
        }
    }
    fputs("],\"checkpoints\":[", output);
    for (int c = 0; c < checkpoint_count; ++c)
    {
        fprintf(output, "%s{\"frame\":%d,", first_checkpoint ? "" : ",", checkpoint_frames[c]); first_checkpoint = false;
        idle_write_state(output, &checkpoints[c]); fputs(",\"nativeRow\":[", output);
        for (int n = 0; n < 42; ++n) fprintf(output, "%s%.9g", n ? "," : "", (double)checkpoint_rows[c][n]);
        fputs("],\"nodesAndCore\":[", output);
        for (int n = 0; n < 17; ++n) fprintf(output, "%s{\"position\":[%.9g,%.9g],\"velocity\":[%.9g,%.9g],\"mass\":%.9g}", n ? "," : "", (double)checkpoints[c].positions[n].x, (double)checkpoints[c].positions[n].y, (double)checkpoints[c].velocities[n].x, (double)checkpoints[c].velocities[n].y, (double)idle_masses[n]);
        fputs("]}", output);
    }
    double drift = 0;
    fputs("],\"spans\":[", output);
    for (int n = 0; n < 4; ++n) { double span = isfinite(low[n]) ? high[n] - low[n] : 0; drift = fmax(drift, span); fprintf(output, "%s%.17g", n ? "," : "", span); }
    fprintf(output, "],\"frames\":%d,\"error\":%d,\"latchedError\":%d,\"spanThreshold\":0.003,\"drift\":%.17g,\"spanPass\":%s,\"elapsedSeconds\":%.6f}",
        frame > requested ? requested : frame, error, latched, drift, frame > requested && !error && !latched && drift < .003 ? "true" : "false", (double)(clock() - started) / CLOCKS_PER_SEC);
    printf("%s variant=%d frames=%d drift=%.9g error=%d latch=%d\n", candidate ? "candidate-default" : "original-default", variant, frame > requested ? requested : frame, drift, error, latched);
    idle_body = NULL; int destroyed = dango_physics_world_destroy(handle);
    return error || latched || destroyed || frame <= requested ? 1 : 0;
}

int main(int argc, char** argv)
{
    if (argc < 2 || argc > 3) { fprintf(stderr, "Usage: diagnostic output.json [baseline|area-reaction|no-local-balance|cached-range|area-gradient|bending-gradient|material-gradients|material-gradients-cached|material-gradients-half-phase|material-gradients-half-phase-no-spin|material-gradients-half-phase-configured-friction|material-gradients-half-phase-projected-radial]\n"); return 2; }
    int variant = IDLE_BASELINE;
    if (argc == 3)
    {
        if (!strcmp(argv[2], "area-reaction")) variant = IDLE_AREA_REACTION;
        else if (!strcmp(argv[2], "no-local-balance")) variant = IDLE_NO_BALANCE;
        else if (!strcmp(argv[2], "cached-range")) variant = IDLE_CACHED_RANGE;
        else if (!strcmp(argv[2], "area-gradient")) variant = IDLE_AREA_GRADIENT;
        else if (!strcmp(argv[2], "bending-gradient")) variant = IDLE_BENDING_GRADIENT;
        else if (!strcmp(argv[2], "material-gradients")) variant = IDLE_MATERIAL_GRADIENTS;
        else if (!strcmp(argv[2], "material-gradients-cached")) variant = IDLE_GRADIENTS_CACHED;
        else if (!strcmp(argv[2], "material-gradients-half-phase")) variant = IDLE_GRADIENTS_HALF_PHASE;
        else if (!strcmp(argv[2], "material-gradients-half-phase-no-spin")) variant = IDLE_GRADIENTS_HALF_PHASE_NO_SPIN;
        else if (!strcmp(argv[2], "material-gradients-half-phase-configured-friction")) variant = IDLE_GRADIENTS_HALF_PHASE_CONFIGURED_FRICTION;
        else if (!strcmp(argv[2], "material-gradients-half-phase-projected-radial")) variant = IDLE_GRADIENTS_HALF_PHASE_PROJECTED_RADIAL;
        else if (strcmp(argv[2], "baseline")) return 2;
    }
    FILE* existing = fopen(argv[1], "r");
    if (existing) { fclose(existing); fprintf(stderr, "Refusing to overwrite an existing diagnostic report.\n"); return 2; }
    FILE* output = fopen(argv[1], "w"); if (!output) return 2;
    fputs("{\"schema\":1,\"diagnosticOnly\":true,\"requestedFrames\":36000,\"dt\":", output);
    const char* variant_name = variant == IDLE_AREA_REACTION ? "area-reaction" : variant == IDLE_NO_BALANCE ? "no-local-balance" :
        variant == IDLE_CACHED_RANGE ? "cached-range" : variant == IDLE_AREA_GRADIENT ? "area-gradient" :
        variant == IDLE_BENDING_GRADIENT ? "bending-gradient" : variant == IDLE_MATERIAL_GRADIENTS ? "material-gradients" :
        variant == IDLE_GRADIENTS_CACHED ? "material-gradients-cached" :
        variant == IDLE_GRADIENTS_HALF_PHASE ? "material-gradients-half-phase" :
        variant == IDLE_GRADIENTS_HALF_PHASE_NO_SPIN ? "material-gradients-half-phase-no-spin" :
        variant == IDLE_GRADIENTS_HALF_PHASE_CONFIGURED_FRICTION ? "material-gradients-half-phase-configured-friction" :
        variant == IDLE_GRADIENTS_HALF_PHASE_PROJECTED_RADIAL ? "material-gradients-half-phase-projected-radial" : "baseline";
    fprintf(output, "%.17g,\"variantName\":\"%s\",\"notes\":\"Source-equivalent standalone executable, not a probe inside the DLL. Forces are time-weighted means. Stage deltas are window sums, with final net projection per small step; no per-row projection trace. SolverSweeps includes correction and validation sweeps (normally two per iteration). Integration includes gravity, Box2D linear damping, springs and contact. Display sizes are expanded control-point envelopes, not painted spline bounds. Touch counts are post-projection proximity, not contact impulses. Span-only pass is not full acceptance.\",\"cases\":[", (double).0166666f, variant_name);
    int result = 0;
    if (variant == IDLE_BASELINE) { result |= idle_run(output, false, variant); fputc(',', output); }
    result |= idle_run(output, true, variant);
    fputs("]}\n", output); if (fclose(output)) result = 1;
    return result;
}

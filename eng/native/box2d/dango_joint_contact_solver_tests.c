// Isolated joint-solver evaluation. Never linked into the production DLL.
#define DANGO_JOINT_BASELINE_COUNTERS
#define main dango_legacy_self_sleep_main
#include "dango_self_sleep_tests.c"
#undef main

#include "dango_joint_contact_solver.h"
#include "dango_joint_contact_fixtures.h"
#include <direct.h>
#include <errno.h>
#include "dango_joint_contact_registry.h"
#include "dango_joint_contact_diagnostics.h"
static int contact_extended_tests(void);

static int contact_identity_tests(void)
{
    joint_contact_row a = {{JOINT_EDGE, 0, 3, 1, 7, 0}, 1.0f, .01f, {1, 0}};
    joint_contact_row b = {{JOINT_EDGE, 1, 8, 0, 3, 0}, 0.0f, .01f, {-1, 0}};
    joint_contact_registry registry = {0};
    int failures = 0;
    if (joint_normalize(&a) != 0 || joint_normalize(&b) != 0 ||
        a.key.kind != JOINT_VERTEX || !joint_key_equal(a.key, b.key))
    {
        fprintf(stderr, "FAIL endpoint canonical identity\n");
        ++failures;
    }
    if (joint_register(&registry, a) != 0 || joint_register(&registry, b) != 0 ||
        registry.count != 1 || registry.duplicates != 1)
    {
        fprintf(stderr, "FAIL bidirectional duplicate support\n");
        ++failures;
    }
    printf("J2.5 contact-identity failures=%d\n", failures);
    int extended = contact_extended_tests();
    return failures != 0 || extended != 0;
}

static b2BodyId joint_fixture_body_id(dango_self_soft_body* body, int index)
{
    return index == DANGO_CONSTRAINT_CORE ? body->core_body_id : body->node_body_ids[index];
}

static void joint_read_solver_state(dango_joint_state* state, dango_self_soft_body** bodies,
    int count, const dango_self_physics_config* config, float skin, float time_step)
{
    memset(state, 0, sizeof *state);
    state->body_count = count;
    state->skin = skin;
    state->ground_friction = config->friction;
    state->inter_body_friction = .3f;
    state->time_step = time_step;
    for (int rig = 0; rig < count; ++rig)
    {
        dango_constraint_state* target = &state->rigs[rig];
        target->radial_length = bodies[rig]->radial_rest_length;
        target->perimeter_length = bodies[rig]->perimeter_rest_length;
        target->radial_min = config->radial_minimum_ratio;
        target->radial_max = config->radial_maximum_ratio;
        target->perimeter_min = config->perimeter_minimum_ratio;
        target->perimeter_max = config->perimeter_maximum_ratio;
        target->radius = config->node_radius;
        target->ground = config->ground_height;
        for (int body = 0; body < DANGO_JOINT_BODIES_PER_RIG; ++body)
        {
            b2BodyId id = joint_fixture_body_id(bodies[rig], body);
            target->positions[body] = b2Body_GetPosition(id);
            target->velocities[body] = b2Body_GetLinearVelocity(id);
            target->inverse_mass[body] = 1.0f / b2Body_GetMass(id);
            state->previous[rig][body] = pair_ground[rig].previous[body];
            if (body < DANGO_CONSTRAINT_NODES)
            {
                state->ground_normal_impulse[rig][body] = pair_ground[rig].normals[body];
                state->ground_tangent_impulse[rig][body] = pair_ground[rig].tangents[body];
            }
        }
    }
}

static int natural_fixture(float skin, dango_joint_state* state)
{
    ground_candidate = true;
    ground_variant = 4;
    preserve_joint_impulses = true;
    dango_self_physics_config config = configuration();
    void* handle = NULL;
    if (dango_box2d_self_soft_body_create(&config, &handle) != 0)
    {
        return 2;
    }
    dango_self_soft_body extra[2] = {{0}, {0}};
    dango_self_soft_body* bodies[3] = {handle, &extra[0], &extra[1]};
    for (int rig = 1; rig < 3; ++rig)
    {
        bodies[rig]->world_id = bodies[0]->world_id;
        bodies[rig]->ground_body_id = bodies[0]->ground_body_id;
        bodies[rig]->ground_shape_id = bodies[0]->ground_shape_id;
        dango_self_create_rig(bodies[rig], &config);
        for (int body = 0; body < DANGO_JOINT_BODIES_PER_RIG; ++body)
        {
            b2BodyId id = joint_fixture_body_id(bodies[rig], body);
            b2Body_SetTransform(id, b2Add(b2Body_GetPosition(id), (b2Vec2){0.0f, 1.65f * rig}), b2Rot_identity);
        }
    }
    b2Shape_SetFriction(bodies[0]->ground_shape_id, 0.0f);
    int target_frame = skin == .045f ? 46 : 47;
    int result = 0;
    bool captured = false;
    for (int frame = 0; frame <= target_frame && result == 0 && !captured; ++frame)
    {
        for (int sub = 0; sub < 4; ++sub)
        {
            for (int rig = 0; rig < 3; ++rig)
            {
                dango_self_update_joint_parameters(bodies[rig], &config);
                dango_self_apply_internal_forces(bodies[rig], &config);
            }
            group_ground_step(bodies, 3, 1.0f / 240.0f);
            if (frame == target_frame && sub == 2)
            {
                joint_read_solver_state(state, bodies, 3, &config, skin, 1.0f / 240.0f);
                captured = true;
                break;
            }
            if (!stack_solve(bodies, 3, &config, skin))
            {
                result = 3;
                break;
            }
            for (int rig = 0; rig < 3; ++rig)
            {
                if (!dango_self_limit_speed(bodies[rig], config.maximum_speed))
                {
                    result = 4;
                }
            }
        }
    }
    int bytes = -1;
    if (dango_box2d_self_soft_body_destroy(handle, &bytes) != 0 || bytes != 0)
    {
        result = 5;
    }
    if (!captured && result == 0)
    {
        result = 6;
    }
    return result;
}

static int position_fixture_test(float skin)
{
    dango_joint_state state;
    int fixture_result = natural_fixture(skin, &state);
    if (fixture_result != 0)
    {
        fprintf(stderr, "FAIL natural fixture skin=%.3f code=%d\n", (double)skin, fixture_result);
        return 1;
    }
    if (!dango_joint_state_valid(&state))
    {
        fprintf(stderr, "FAIL invalid captured solver state skin=%.3f\n", (double)skin);
        return 1;
    }
    b2Vec2 velocities_before[DANGO_JOINT_MAX_RIGS][DANGO_JOINT_BODIES_PER_RIG];
    for (int rig = 0; rig < state.body_count; ++rig)
    {
        memcpy(velocities_before[rig], state.rigs[rig].velocities, sizeof velocities_before[rig]);
    }
    dango_joint_solver_result result = dango_joint_solve_positions(&state);
    float maximum_velocity_change = 0.0f;
    for (int rig = 0; rig < state.body_count; ++rig)
    {
        for (int body = 0; body < DANGO_JOINT_BODIES_PER_RIG; ++body)
        {
            maximum_velocity_change = fmaxf(maximum_velocity_change,
                b2Distance(velocities_before[rig][body], state.rigs[rig].velocities[body]));
        }
    }
    printf("position_fixture skin=%.3f success=%d error=%d scans=%d self_residual=%.9g contact_residual=%.9g row_visits=%llu contact_rebuilds=%llu velocity_change=%.9g\n",
        (double)skin, result.succeeded, result.error_code, result.position_scans,
        (double)result.maximum_self_residual, (double)result.maximum_contact_residual,
        result.position_row_visits, result.contact_rebuilds, (double)maximum_velocity_change);
    if (!result.succeeded || maximum_velocity_change != 0.0f)
    {
        return 1;
    }
    return 0;
}

static unsigned long long fixture_checksum(const void* data, size_t size)
{
    const unsigned char* bytes = data;
    unsigned long long hash = 1469598103934665603ULL;
    for (size_t i = 0; i < size; ++i)
    {
        hash ^= bytes[i];
        hash *= 1099511628211ULL;
    }
    return hash;
}

static int fixture_replay_one(float skin)
{
    dango_joint_fixture_record record = {0};
    record.schema_version = DANGO_JOINT_FIXTURE_SCHEMA_VERSION;
    record.target_frame = skin == .045f ? 46U : 47U;
    record.target_substep = 2U;
    int result = natural_fixture(skin, &record.state);
    if (result != 0 || !dango_joint_state_valid(&record.state))
    {
        fprintf(stderr, "FAIL fixture capture skin=%.3f code=%d\n", (double)skin, result);
        return 1;
    }
    if (_mkdir("artifacts\\p2d-007c\\joint-solver") != 0 && errno != EEXIST)
    {
        fprintf(stderr, "FAIL fixture directory errno=%d\n", errno);
        return 1;
    }
    const char* path = skin == .045f
        ? "artifacts\\p2d-007c\\joint-solver\\solver-boundary-.045-v1.bin"
        : "artifacts\\p2d-007c\\joint-solver\\solver-boundary-.020-v1.bin";
    FILE* output = fopen(path, "wb");
    if (output == NULL)
    {
        fprintf(stderr, "FAIL write fixture %s\n", path);
        return 1;
    }
    bool write_ok = fwrite(&record, sizeof record, 1, output) == 1;
    write_ok = fclose(output) == 0 && write_ok;
    if (!write_ok)
    {
        fprintf(stderr, "FAIL write fixture %s\n", path);
        return 1;
    }
    dango_joint_fixture_record replay = {0};
    FILE* input = fopen(path, "rb");
    if (input == NULL)
    {
        fprintf(stderr, "FAIL read fixture %s\n", path);
        return 1;
    }
    bool read_ok = fread(&replay, sizeof replay, 1, input) == 1;
    read_ok = fclose(input) == 0 && read_ok;
    if (!read_ok)
    {
        fprintf(stderr, "FAIL read fixture %s\n", path);
        return 1;
    }
    if (memcmp(&record, &replay, sizeof record) != 0 ||
        replay.schema_version != DANGO_JOINT_FIXTURE_SCHEMA_VERSION ||
        !dango_joint_state_valid(&replay.state))
    {
        fprintf(stderr, "FAIL replay mismatch %s\n", path);
        return 1;
    }
    printf("fixture_replay path=%s schema=%u bytes=%zu checksum=%llu frame=%u substep=%u bodies=%d\n",
        path, replay.schema_version, sizeof replay, fixture_checksum(&replay, sizeof replay),
        replay.target_frame, replay.target_substep, replay.state.body_count);
    return 0;
}

static int red_baseline(float skin)
{
    dango_constraint_sweep_count = 0;
    dango_constraint_row_visit_count = 0;
    evaluation_contact_rebuild_count = 0;
    _putenv_s("DANGO_STACK_FRICTION", "0.3");
    int legacy_result = stack_diagnostic(3, skin, true, 0.0f);
    _putenv_s("DANGO_STACK_FRICTION", "");
    printf("legacy_cost constraint_sweeps=%llu constraint_row_visits=%llu contact_rebuilds=%llu\n",
        dango_constraint_sweep_count,
        dango_constraint_row_visit_count,
        evaluation_contact_rebuild_count);
    if (legacy_result != 1)
    {
        fprintf(stderr, "FAIL expected legacy centered three-rig scenario to exit 1, got %d\n", legacy_result);
        return 2;
    }
    printf("EXPECTED RED legacy centered three-rig skin=%.3f reached the approved failure gate\n", (double)skin);
    dango_joint_solver_result candidate = dango_joint_solver_not_implemented();
    if (!candidate.succeeded)
    {
        fprintf(stderr, "EXPECTED RED joint solver is not implemented error=%d\n", candidate.error_code);
        return 1;
    }
    return 0;
}

#include "dango_joint_contact_registry_tests.h"
#include "dango_joint_position_replay.h"
#include "dango_joint_position_replay_tests.h"
#include "dango_joint_velocity.h"
#include "dango_joint_velocity_tests.h"
#include "dango_joint_preview.h"

int main(int argc, char** argv)
{
    if (argc == 2 && strcmp(argv[1], "preview-host") == 0) return joint_preview_host();
    if (argc == 2 && strcmp(argv[1], "preview-smoke") == 0) return joint_preview_smoke();
    if (argc == 2 && strcmp(argv[1], "velocity-tests") == 0) return joint_velocity_tests(false);
    if (argc == 2 && strcmp(argv[1], "velocity-tests-broken") == 0) return joint_velocity_tests(true);
    if (argc == 2 && strcmp(argv[1], "position-contract") == 0) return joint_r_contract_tests();
    if (argc == 3 && strcmp(argv[1], "position-r") == 0) return joint_r_fixture_tests(argv[2]);
    if (argc == 3 && strcmp(argv[1], "contact-diagnostics") == 0)
    {
        return contact_diagnostics_tests(argv[2]);
    }
    if (argc == 2 && strcmp(argv[1], "contact-identity") == 0)
    {
        return contact_identity_tests();
    }
    if (argc == 2 && strcmp(argv[1], "red-baseline") == 0)
    {
        return red_baseline(.045f);
    }
    if (argc == 2 && strcmp(argv[1], "red-baseline-.020") == 0)
    {
        return red_baseline(.020f);
    }
    if (argc == 2 && strcmp(argv[1], "position") == 0)
    {
        int first = position_fixture_test(.020f);
        int second = position_fixture_test(.045f);
        return first != 0 || second != 0 ? 1 : 0;
    }
    if (argc == 2 && strcmp(argv[1], "fixture-replay") == 0)
    {
        int first = fixture_replay_one(.020f);
        int second = fixture_replay_one(.045f);
        return first != 0 || second != 0 ? 1 : 0;
    }
    fprintf(stderr, "usage: dango_joint_contact_solver_tests red-baseline|red-baseline-.020|fixture-replay|position|contact-identity|contact-diagnostics NEW_LOG_PATH\n");
    return 9;
}

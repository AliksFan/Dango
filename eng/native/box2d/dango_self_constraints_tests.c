#include <stdio.h>
#include <time.h>
#define DANGO_CONSTRAINT_TEST_DIAGNOSTICS
#include "dango_box2d_bridge.c"

#define CHECK(condition) do { if (!(condition)) { fprintf(stderr, "Failed line %d: %s\n", __LINE__, #condition); return 1; } } while (0)

static dango_constraint_state ring(void)
{
    dango_constraint_state s = {0};
    s.radial_length = 0.68f;
    s.perimeter_length = 2.0f * s.radial_length * sinf(3.14159265359f / 16.0f);
    s.radial_min = 0.65f;
    s.radial_max = 1.22f;
    s.perimeter_min = 0.73f;
    s.perimeter_max = 1.07f;
    s.radius = 0.045f;
    s.ground = -10.0f;
    for (int i = 0; i < 16; ++i)
    {
        float a = 6.28318530718f * (float)i / 16.0f;
        s.positions[i] = (b2Vec2){s.radial_length * cosf(a), s.radial_length * sinf(a)};
        s.inverse_mass[i] = 1.0f / 0.015f;
    }
    s.inverse_mass[16] = 1.0f / 0.12f;
    return s;
}

static b2Vec2 weighted_sum(const dango_constraint_state* s, bool velocity)
{
    b2Vec2 result = {0};
    for (int i = 0; i < 17; ++i)
    {
        result = b2MulAdd(result, 1.0f / s->inverse_mass[i], velocity ? s->velocities[i] : s->positions[i]);
    }
    return result;
}

static float energy(const dango_constraint_state* s)
{
    float result = 0;
    for (int i = 0; i < 17; ++i)
    {
        result += 0.5f * b2Dot(s->velocities[i], s->velocities[i]) / s->inverse_mass[i];
    }
    return result;
}

static int split_row_checks(void)
{
    dango_constraint_state combined = ring();
    int ids[3] = {0, 1, 16};
    b2Vec2 gradients[3] = {{1.0f, -0.25f}, {-0.4f, 0.75f}, {-0.6f, -0.5f}};
    combined.velocities[0] = (b2Vec2){-1.0f, 0.25f};
    combined.velocities[1] = (b2Vec2){0.2f, -0.8f};
    combined.velocities[16] = (b2Vec2){0.1f, 0.3f};
    dango_constraint_state split = combined;
    dango_constraint_correct_mode(&combined, ids, gradients, 3, -0.08f, true, true,
        DANGO_CONSTRAINT_CORRECT_BOTH);
    dango_constraint_correct_mode(&split, ids, gradients, 3, -0.08f, true, true,
        DANGO_CONSTRAINT_CORRECT_POSITION);
    dango_constraint_correct_mode(&split, ids, gradients, 3, -0.08f, true, true,
        DANGO_CONSTRAINT_CORRECT_VELOCITY);
    for (int i = 0; i < 17; ++i)
    {
        CHECK(b2Distance(combined.positions[i], split.positions[i]) < 1e-7f);
        CHECK(b2Distance(combined.velocities[i], split.velocities[i]) < 1e-7f);
    }

    dango_constraint_state position_only = ring();
    position_only.positions[0].x += 0.30f;
    position_only.velocities[0] = (b2Vec2){-2.0f, 1.0f};
    b2Vec2 velocities_before[17];
    memcpy(velocities_before, position_only.velocities, sizeof velocities_before);
    CHECK(dango_constraint_distance_mode(&position_only, 16, 0,
        position_only.radial_length * position_only.radial_min,
        position_only.radial_length * position_only.radial_max, true,
        DANGO_CONSTRAINT_CORRECT_POSITION) > 0.0f);
    CHECK(dango_constraint_angle_mode(&position_only, 1, false, true,
        DANGO_CONSTRAINT_CORRECT_POSITION) >= 0.0f);
    for (int i = 0; i < 17; ++i)
    {
        CHECK(b2Distance(position_only.velocities[i], velocities_before[i]) == 0.0f);
    }

    dango_constraint_state velocity_only = ring();
    velocity_only.positions[0].x += 0.30f;
    velocity_only.velocities[0] = (b2Vec2){2.0f, 0.0f};
    b2Vec2 positions_before[17];
    memcpy(positions_before, velocity_only.positions, sizeof positions_before);
    CHECK(dango_constraint_distance_mode(&velocity_only, 16, 0,
        velocity_only.radial_length * velocity_only.radial_min,
        velocity_only.radial_length * velocity_only.radial_max, true,
        DANGO_CONSTRAINT_CORRECT_VELOCITY) > 0.0f);
    for (int i = 0; i < 17; ++i)
    {
        CHECK(b2Distance(velocity_only.positions[i], positions_before[i]) == 0.0f);
    }

    dango_constraint_state ground = ring();
    ground.ground = -0.60f;
    memcpy(velocities_before, ground.velocities, sizeof velocities_before);
    CHECK(dango_constraint_sweep_order_mode(&ground, true, false,
        DANGO_CONSTRAINT_CORRECT_POSITION) > 0.0f);
    for (int i = 0; i < 17; ++i)
    {
        CHECK(b2Distance(ground.velocities[i], velocities_before[i]) == 0.0f);
    }
    puts("PASS: split row equivalence and position/velocity isolation");
    return 0;
}

static int runtime_checks(float inward_force, bool defaults, float anchor_x)
{
    dango_self_physics_config config = {
        .node_count = 16, .substep_count = 4, .radial_rest_length = 0.68f,
        .radial_minimum_ratio = 0.65f, .radial_maximum_ratio = 1.22f,
        .perimeter_minimum_ratio = 0.73f, .perimeter_maximum_ratio = 1.07f,
        .radial_hertz = 5.2f, .perimeter_hertz = 20.1f,
        .radial_damping = 1.85f, .perimeter_damping = 1.85f,
        .area_strength = 4.8f, .ground_deformation_strength = 3.5f,
        .repulsion_distance = 0.09f, .repulsion_strength = 0.3f, .repulsion_maximum_force = 3.5f,
        .core_exclusion_ratio = 0.25f, .angle_strength = 0, .local_angle_balance_strength = 1.7f,
        .gravity = 9.8f, .core_mass = 0.12f, .node_mass = 0.015f, .drop_height = 0.42f,
        .ground_height = -0.72f, .node_radius = 0.045f, .friction = 0.72f, .restitution = 0.02f,
        .contact_hertz = 28, .linear_damping = 1.8f, .drag_stiffness = 40,
        .drag_damping = 2.2f, .drag_maximum_force = 30, .horizontal_force = 2.4f,
        .maximum_horizontal_speed = 3.2f, .air_control_ratio = 0.35f, .downward_force = 2.2f,
        .jump_impulse = 0.42f, .jump_cooldown_seconds = 0.45f, .maximum_speed = 12, .minimum_area = 0.42f
    };
    config.top_inward_force = inward_force;
    if (defaults)
    {
        config.angle_strength = 10;
        config.drag_stiffness = 10;
        config.drag_maximum_force = 8;
    }
    void* handle = NULL;
    CHECK(dango_box2d_self_soft_body_create(&config, &handle) == 0);
    dango_self_soft_body* speed_body = handle;
    b2Vec2 saved_position = b2Body_GetPosition(speed_body->node_body_ids[0]);
    b2Body_SetLinearVelocity(speed_body->node_body_ids[0], (b2Vec2){18, 24});
    b2Body_SetLinearVelocity(speed_body->core_body_id, (b2Vec2){3, 4});
    CHECK(dango_self_limit_speed(speed_body, 12));
    b2Vec2 limited = b2Body_GetLinearVelocity(speed_body->node_body_ids[0]);
    b2Vec2 limited_core = b2Body_GetLinearVelocity(speed_body->core_body_id);
    CHECK(b2Length(limited) <= 12 && b2Length(limited) > 11.99f);
    CHECK(fabsf(limited.x / 18 - limited.y / 24) < 1e-6f);
    CHECK(fabsf(limited.x / 18 - limited_core.x / 3) < 1e-6f);
    CHECK(b2Distance(saved_position, b2Body_GetPosition(speed_body->node_body_ids[0])) == 0);
    CHECK(dango_self_limit_speed(speed_body, 12));
    CHECK(b2Distance(limited, b2Body_GetLinearVelocity(speed_body->node_body_ids[0])) == 0);
    b2Body_SetLinearVelocity(speed_body->node_body_ids[0], (b2Vec2){0, 0});
    b2Body_SetLinearVelocity(speed_body->core_body_id, (b2Vec2){0, 0});
    b2Vec2 rest_min[17], rest_max[17];
    clock_t started = clock();
    for (int step = 0; step < 36000; ++step)
    {
        float points[32], area, cx, cy, x, y, speed;
        int count, contacts, grounded, selected;
        int code = dango_box2d_self_soft_body_step(handle, defaults ? 1.0f/60.0f : 0.0166667f, &config, anchor_x, defaults ? 0 : 1.6666667f,
            0, 0, 0, !defaults && step >= 300 && step < 420, anchor_x-1, 0,
            points, 16, &count, &contacts, &area, &cx, &cy, &x, &y, &speed, &grounded, &selected);
        if (code != 0)
        {
            fprintf(stderr, "Runtime failure step %d code %d\n", step, code);
            int bytes;
            dango_box2d_self_soft_body_destroy(handle, &bytes);
            return 1;
        }
        dango_self_soft_body* body = handle;
        b2Vec2 core = b2Body_GetPosition(body->core_body_id);
        CHECK(!dango_self_has_crossing(body));
        for (int i = 0; i < 16; ++i)
        {
            b2Vec2 p = b2Body_GetPosition(body->node_body_ids[i]);
            b2Vec2 n = b2Body_GetPosition(body->node_body_ids[(i + 1) % 16]);
            float radial_length = b2Distance(p, core);
            float perimeter_length = b2Distance(p, n);
            CHECK(radial_length >= body->radial_rest_length * config.radial_minimum_ratio - 1e-5f);
            CHECK(radial_length <= body->radial_rest_length * config.radial_maximum_ratio + 1e-5f);
            CHECK(perimeter_length >= body->perimeter_rest_length * config.perimeter_minimum_ratio - 1e-5f);
            CHECK(perimeter_length <= body->perimeter_rest_length * config.perimeter_maximum_ratio + 1e-5f);
            CHECK(p.y >= config.ground_height + config.node_radius - 1e-5f);
            float angle = dango_self_interior_angle(body, i);
            CHECK(angle >= 1.5707963268f && angle <= 3.1415926536f);
        }
        if (step >= 35400)
        {
            for (int i = 0; i < 17; ++i)
            {
                b2Vec2 p = b2Body_GetPosition(i == 16 ? body->core_body_id : body->node_body_ids[i]);
                if (step == 35400)
                {
                    rest_min[i] = rest_max[i] = p;
                }
                rest_min[i] = b2Min(rest_min[i], p);
                rest_max[i] = b2Max(rest_max[i], p);
            }
        }
    }
    float rest_range = 0;
    for (int i = 0; i < 17; ++i)
    {
        rest_range = fmaxf(rest_range, b2Distance(rest_min[i], rest_max[i]));
    }
    printf("defaults=%d anchor_x=%.3f Inward force %.2f; rest range %.9g; 36000 native frames in %.3f seconds\n", defaults, (double)anchor_x, (double)inward_force, (double)rest_range,
        (double)(clock() - started) / CLOCKS_PER_SEC);
    CHECK(rest_range < 0.003f);
    int bytes;
    CHECK(dango_box2d_self_soft_body_destroy(handle, &bytes) == 0 && bytes == 0);
    puts("PASS: native runtime 36000 frames, lengths, angles, ground, no latch, settled drift/jitter");
    return 0;
}

static int material_drag_convergence_fixture(void)
{
    // Actual pre-solve state at frame 764 of the material ground-swing regression.
    // The old 96-sweep solve rejected it at residual 1.1973083e-5.
    dango_constraint_state s = ring();
    s.ground = -0.72f;
    const b2Vec2 positions[17] = {
        {1.63938594f, -0.167212605f},
        {1.43453729f, 0.029319521f},
        {1.1697464f, 0.131729439f},
        {0.89168936f, 0.189259633f},
        {0.613773763f, 0.130101979f},
        {0.37193954f, 0.0680535287f},
        {0.143069044f, -0.0129827168f},
        {0.022876488f, -0.22409451f},
        {0.0518832617f, -0.469970345f},
        {0.174628794f, -0.672543705f},
        {0.420204222f, -0.674899817f},
        {0.693453074f, -0.675055683f},
        {0.977380276f, -0.674843907f},
        {1.25009179f, -0.674187303f},
        {1.51739597f, -0.672710478f},
        {1.65899992f, -0.443776667f},
        {0.852042139f, -0.250959545f}
    };
    const b2Vec2 velocities[17] = {
        {-0.485797077f, -3.34102798f},
        {-1.20711493f, -4.08524609f},
        {-0.402778924f, -2.00555587f},
        {-0.215977103f, -0.439290285f},
        {-0.302019387f, -0.0350740552f},
        {-0.635758638f, -0.712477744f},
        {-0.609500647f, -1.85219479f},
        {-0.213652492f, -1.83757973f},
        {0.141279295f, -1.3837235f},
        {0.816277564f, 0.0132856201f},
        {0.532199979f, -0.0964706764f},
        {0.74970752f, -0.00206584623f},
        {0.750761747f, -0.150417626f},
        {0.704937756f, -0.361570954f},
        {0.0f, 0.06245327f},
        {-0.751273453f, -3.07569599f},
        {-0.167033717f, -0.4069888f}
    };
    memcpy(s.positions, positions, sizeof positions);
    memcpy(s.velocities, velocities, sizeof velocities);
    float energy_before = energy(&s);
    CHECK(dango_constraint_solve(&s));
    CHECK(dango_constraint_sweep(&s, false) <= DANGO_CONSTRAINT_TOLERANCE);
    CHECK(energy(&s) <= energy_before + 1e-6f);
    puts("PASS: captured soft-material drag converges without relaxing tolerance or adding energy");
    return 0;
}

int main(int argc, char** argv)
{
    if (argc > 1) return runtime_checks(0.5f, true, (float)atof(argv[1]));
    CHECK(split_row_checks() == 0);
    CHECK(material_drag_convergence_fixture() == 0);
    b2Vec2 top = dango_self_top_force((b2Vec2){0, 1}, 0.5f);
    b2Vec2 shoulder = dango_self_top_force((b2Vec2){1, 1}, 0.5f);
    CHECK(fabsf(top.y + 0.5f) < 1e-6f && top.x == 0);
    CHECK(fabsf(b2Length(shoulder) - 0.25f) < 1e-6f);
    CHECK(shoulder.x < 0 && shoulder.y < 0);
    CHECK(b2Length(dango_self_top_force((b2Vec2){1, 0}, 0.5f)) == 0);
    CHECK(b2Length(dango_self_top_force((b2Vec2){0, -1}, 0.5f)) == 0);
    CHECK(b2Length(dango_self_top_force((b2Vec2){0, 0}, 0.5f)) == 0);
    CHECK(b2Length(dango_self_top_force((b2Vec2){0, 1}, 0)) == 0);
    dango_constraint_state s = ring();
    dango_constraint_state initial = s;
    CHECK(dango_constraint_solve(&s));
    for (int i = 0; i < 17; ++i)
    {
        CHECK(b2Distance(s.positions[i], initial.positions[i]) < 1e-7f);
    }

    for (int fixture = 0; fixture < 2; ++fixture)
    {
        s = ring();
        s.positions[4].y = fixture == 0 ? 0.50f : 1.0f;
        s.velocities[4] = (b2Vec2){0.4f, fixture == 0 ? -2.0f : 2.0f};
        CHECK(dango_constraint_angle(&s, 4, false, false) > 0.01f);
        b2Vec2 center_before = weighted_sum(&s, false);
        b2Vec2 momentum_before = weighted_sum(&s, true);
        float energy_before = energy(&s);
        CHECK(dango_constraint_solve(&s));
        CHECK(dango_constraint_sweep(&s, false) <= DANGO_CONSTRAINT_TOLERANCE);
        CHECK(b2Distance(center_before, weighted_sum(&s, false)) < 1e-6f);
        CHECK(b2Distance(momentum_before, weighted_sum(&s, true)) < 1e-6f);
        CHECK(energy(&s) <= energy_before + 1e-6f);
    }

    s = ring();
    s.ground = -0.60f;
    CHECK(dango_constraint_solve(&s));
    for (int i = 0; i < 16; ++i)
    {
        CHECK(s.positions[i].y >= s.ground + s.radius - 1e-5f);
    }

    s = ring();
    s.positions[5] = s.positions[4];
    CHECK(!dango_constraint_solve(&s));
    puts("PASS: legal ring, acute/concave recovery, raw/display angles, lengths, mass center, momentum, energy, ground, degenerate rejection");
    CHECK(runtime_checks(0.0f, false, 0) == 0);
    CHECK(runtime_checks(0.5f, false, 0) == 0);
    return runtime_checks(1.0f, false, 0);
}

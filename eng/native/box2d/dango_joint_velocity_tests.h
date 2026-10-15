static int joint_velocity_tests(bool broken)
{
    int checks = 0, failures = 0;
#define VCHECK(e) do { ++checks; if (!(e)) { ++failures; fprintf(stderr, "FAIL velocity line=%d %s\n", __LINE__, #e); } } while (0)
    for (int mode = 0; mode < 4; ++mode)
    {
        dango_joint_state state = joint_polygon_fixture(); state.body_count = 1;
        state.rigs[0].velocities[0] = (b2Vec2){10, mode == 2 ? 0.0f : -1.0f};
        dango_joint_state original = state;
        joint_v_system sys = {0}; joint_v_row row = {0}; row.count = 1; row.normal = -1; row.ids[0] = 0; row.ground = true;
        row.gradients[0] = (b2Vec2){0, 1};
        float mu = mode == 1 ? 0 : .3f;
        VCHECK(joint_v_contact(&sys, &state, row, mu, 0) == 0);
        for (int i = 0; i < 8; ++i)
        {
            joint_v_project(&state, &sys, 0);
            if (broken) sys.rows[1].lambda = 0; // Deliberately reproduce the old per-visit budget reset.
            joint_v_project(&state, &sys, 1);
        }
        VCHECK(fabsf(state.rigs[0].velocities[0].x - 10) <= mu * sys.rows[0].lambda + 1e-5f);
        VCHECK(fabsf(sys.rows[1].lambda) <= mu * sys.rows[0].lambda + 1e-6f);
        VCHECK(joint_v_energy(&state) <= joint_v_energy(&original) + 1e-6);
        VCHECK(memcmp(state.rigs[0].positions, original.rigs[0].positions, sizeof state.rigs[0].positions) == 0);
        if (mode == 3)
        {
            sys.rows[0].lambda = 0; joint_v_project(&state, &sys, 1);
            VCHECK(sys.rows[1].lambda == 0 && fabsf(state.rigs[0].velocities[0].x - 10) < 1e-5f);
        }
    }
    for (int endpoint = 0; endpoint < 3; ++endpoint)
    {
        dango_joint_state state = joint_polygon_fixture();
        state.rigs[0].velocities[0] = (b2Vec2){-1, 2}; state.rigs[0].inverse_mass[0] = 2;
        joint_v_system sys = {0}; joint_v_row row = {0}; row.count = 3; row.normal = -1;
        float t = .5f * endpoint; row.ids[0] = 0; row.ids[1] = 17; row.ids[2] = 18;
        row.gradients[0] = (b2Vec2){1, 0}; row.gradients[1] = (b2Vec2){-(1-t), 0}; row.gradients[2] = (b2Vec2){-t, 0};
        VCHECK(joint_v_contact(&sys, &state, row, .3f, 0) == 0);
        dango_joint_state before = state;
        joint_v_result result = joint_v_solve(&state, &sys);
        VCHECK(result.error == 0 && result.energy_gain <= 1e-6);
        VCHECK(b2Distance(joint_v_momentum(&state), joint_v_momentum(&before)) < 1e-5f);
        for (int r = 0; r < 3; ++r) VCHECK(memcmp(state.rigs[r].positions, before.rigs[r].positions, sizeof state.rigs[r].positions) == 0);
    }
    dango_joint_state state = joint_polygon_fixture(); joint_v_system sys;
    VCHECK(joint_v_build(&state, &sys) == 0 && sys.count <= 672);
    joint_v_result result = joint_v_solve(&state, &sys);
    VCHECK(result.error == 0);
    printf("velocity-tests broken=%d checks=%d failures=%d\n", broken, checks, failures);
#undef VCHECK
    return failures != 0;
}

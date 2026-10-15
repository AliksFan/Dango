static int joint_r_contract_tests(void)
{
    int failures = 0, checks = 0;
#define R_CHECK(e) do { ++checks; if (!(e)) { ++failures; fprintf(stderr, "FAIL J3R line=%d %s\n", __LINE__, #e); } } while (0)
    dango_joint_state good = joint_polygon_fixture(); good.skin = .01f;
    joint_contact_snapshot contacts;
    joint_residual_report report;
    R_CHECK(joint_r_final(&good, .42f, &report, &contacts));
    dango_joint_state bad = good;
    for (int n = 0; n < 17; ++n) bad.rigs[1].positions[n].x -= .04f;
    R_CHECK(!joint_r_final(&bad, .42f, &report, &contacts));
    R_CHECK(report.maximum[6] > 1e-5f && contacts.registry.count > 0);
    bad = good; bad.rigs[0].positions[0].x += .5f;
    R_CHECK(!joint_r_final(&bad, .42f, &report, &contacts));
    bad = good; bad.rigs[0].positions[12].y -= .1f;
    R_CHECK(!joint_r_final(&bad, .42f, &report, &contacts));
    bad = good; bad.rigs[0].positions[0].x = NAN;
    R_CHECK(!joint_r_final(&bad, .42f, &report, &contacts));
    bad = good;
    b2Vec2 temp = bad.rigs[0].positions[0]; bad.rigs[0].positions[0] = bad.rigs[0].positions[8]; bad.rigs[0].positions[8] = temp;
    R_CHECK(!joint_r_topology(&bad, .42f));
    bad = good;
    for (int n = 0; n < 17; ++n) bad.rigs[1].positions[n].x -= 1;
    R_CHECK(!joint_r_topology(&bad, .42f));
    bad = good; bad.rigs[1].positions[16] = bad.rigs[0].positions[0];
    R_CHECK(!joint_r_topology(&bad, .42f));
    bad = good;
    for (int n = 0; n < 16; ++n) bad.rigs[0].positions[n] = b2MulSV(.1f, bad.rigs[0].positions[n]);
    R_CHECK(!joint_r_topology(&bad, .42f));
    // Two-body vertex row: inverse-mass weighted correction, no velocity changes.
    dango_joint_state state = joint_polygon_fixture(), before = state;
    joint_contact_row vertex = {{JOINT_VERTEX, 0, 0, 1, 8, 0}, 0, .02f, {-1, 0}};
    float move = 0;
    R_CHECK(joint_r_project(&state, &vertex, &move) == 0);
    R_CHECK(move > 0 && state.rigs[0].positions[0].x < before.rigs[0].positions[0].x);
    R_CHECK(fabsf((state.rigs[0].positions[0].x - before.rigs[0].positions[0].x) +
        (state.rigs[1].positions[8].x - before.rigs[1].positions[8].x)) < 1e-6f);
    for (int r = 0; r < 3; ++r) R_CHECK(memcmp(state.rigs[r].velocities, before.rigs[r].velocities, sizeof state.rigs[r].velocities) == 0);
    // Fixed edge identity with changing interpolation and unequal inverse masses.
    state = joint_polygon_fixture();
    state.rigs[0].positions[3] = (b2Vec2){1.5f, -.02f};
    state.rigs[1].positions[0] = (b2Vec2){0, 0}; state.rigs[1].positions[1] = (b2Vec2){2, 0};
    state.rigs[0].inverse_mass[3] = 2; state.rigs[1].inverse_mass[0] = 1; state.rigs[1].inverse_mass[1] = 3;
    before = state;
    joint_contact_row edge = {{JOINT_EDGE, 0, 3, 1, 0, 0}, .25f, .02f, {0, -1}}, saved_edge = edge;
    R_CHECK(joint_r_project(&state, &edge, &move) == 0 && memcmp(&edge, &saved_edge, sizeof edge) == 0);
    float lambda = .01f / (2 + .25f * .25f + 3 * .75f * .75f);
    R_CHECK(fabsf(state.rigs[0].positions[3].y - (before.rigs[0].positions[3].y - 2 * lambda)) < 1e-6f);
    R_CHECK(fabsf(state.rigs[1].positions[0].y - .25f * lambda) < 1e-6f);
    R_CHECK(fabsf(state.rigs[1].positions[1].y - 2.25f * lambda) < 1e-6f);
    for (int r = 0; r < 3; ++r) for (int n = 0; n < 17; ++n)
        if (!(r == 0 && n == 3) && !(r == 1 && (n == 0 || n == 1)))
            R_CHECK(memcmp(&state.rigs[r].positions[n], &before.rigs[r].positions[n], sizeof(b2Vec2)) == 0);
    for (int r = 0; r < 3; ++r) R_CHECK(memcmp(state.rigs[r].velocities, before.rigs[r].velocities, sizeof state.rigs[r].velocities) == 0);
    // A genuinely legal separated state must have a successful path too.
    state = good;
    joint_r_result result = joint_r_solve(&state, .42f, NULL);
    R_CHECK(result.success && result.scans <= 96 && result.topology);
    for (int r = 0; r < 3; ++r) R_CHECK(memcmp(state.rigs[r].velocities, good.rigs[r].velocities, sizeof state.rigs[r].velocities) == 0);
    printf("J3R contract checks=%d failures=%d\n", checks, failures);
#undef R_CHECK
    return failures != 0;
}

static int joint_r_fixture_tests(const char* prefix)
{
    int failures = 0;
    for (int i = 0; i < 2; ++i)
    {
        const char* source = i == 0 ? "artifacts/p2d-007c/joint-solver/solver-boundary-.020-v1.bin" :
            "artifacts/p2d-007c/joint-solver/solver-boundary-.045-v1.bin";
        dango_joint_fixture_record record;
        FILE* input = fopen(source, "rb");
        if (!input) return 2;
        bool read = fread(&record, sizeof record, 1, input) == 1 && fgetc(input) == EOF;
        fclose(input);
        unsigned long long expected = i == 0 ? 18364184990579871796ULL : 9091279660744721202ULL;
        if (!read || record.schema_version != 1 || fixture_checksum(&record, sizeof record) != expected) return 2;
        char path[1024], state_path[1024];
        if (snprintf(path, sizeof path, "%s-%s.csv", prefix, i == 0 ? "020" : "045") >= (int)sizeof path ||
            snprintf(state_path, sizeof state_path, "%s-%s-final.bin", prefix, i == 0 ? "020" : "045") >= (int)sizeof state_path) return 2;
        FILE* log = fopen(path, "wx");
        if (!log) return 2;
        FILE* final = fopen(state_path, "wbx");
        if (!final) { fclose(log); return 2; }
        fprintf(log, "J3R,input_checksum,%llu,minimum_display_area,0.42,scan_budget,96\n", expected);
        dango_joint_state state = record.state;
        joint_r_result result = joint_r_solve(&state, configuration().minimum_area, log);
        bool unchanged = true;
        for (int r = 0; r < 3; ++r)
            if (memcmp(state.rigs[r].velocities, record.state.rigs[r].velocities, sizeof state.rigs[r].velocities)) unchanged = false;
        record.state = state;
        bool wrote = fwrite(&record, sizeof record, 1, final) == 1;
        wrote = fclose(final) == 0 && wrote;
        if (ferror(log)) wrote = false;
        wrote = fclose(log) == 0 && wrote;
        float self = 0;
        for (int k = 0; k < 5; ++k) self = fmaxf(self, result.residual.maximum[k]);
        unsigned long long visits = 0; for (int k = 0; k < 7; ++k) visits += result.visits[k];
        printf("J3R skin=%.3f success=%d error=%d scans=%d self=%.9g contact=%.9g topology=%d velocities_unchanged=%d visits=%llu cross_visits=%llu generation=%llu final_checks=%llu diagnostic=%llu max_row_move=%.9g ms=%.3f peak=%d saved=%d\n",
            (double)state.skin, result.success, result.error, result.scans, (double)self, (double)result.residual.maximum[6],
            result.topology, unchanged, visits, result.visits[6], result.generation, result.final_checks, result.diagnostic,
            (double)result.maximum_row_move, result.milliseconds, result.peak_contacts, wrote);
        if (!result.success || !unchanged || !wrote) ++failures;
    }
    return failures ? 1 : 0;
}

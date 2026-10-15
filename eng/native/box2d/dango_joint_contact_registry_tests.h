// J2.5 directed geometry, capacity, live adapter and observation checks.
static int joint_checks, joint_failures;
#define JOINT_CHECK(expression) do { ++joint_checks; if (!(expression)) { \
    ++joint_failures; fprintf(stderr, "FAIL J2.5 line=%d %s\n", __LINE__, #expression); } } while (0)

static dango_joint_state joint_polygon_fixture(void)
{
    dango_joint_state s = {0};
    s.body_count = 3; s.skin = .045f; s.time_step = 1.0f / 240; s.ground_friction = .72f;
    for (int r = 0; r < 3; ++r)
    {
        dango_constraint_state* p = &s.rigs[r];
        p->radial_length = 1; p->perimeter_length = 2 * sinf(B2_PI / 16);
        p->radial_min = .9f; p->radial_max = 1.1f;
        p->perimeter_min = .9f; p->perimeter_max = 1.1f; p->ground = -1.1f; p->radius = .1f;
        p->positions[16] = (b2Vec2){2.02f * r, 0};
        for (int n = 0; n < 16; ++n)
        {
            float angle = 2 * B2_PI * n / 16;
            p->positions[n] = b2Add(p->positions[16], (b2Vec2){cosf(angle), sinf(angle)});
        }
        for (int n = 0; n < 17; ++n) { p->inverse_mass[n] = 1; s.previous[r][n] = p->positions[n]; }
    }
    return s;
}

static void joint_key_tests(void)
{
    joint_contact_row endpoint = {{JOINT_EDGE, 0, 3, 1, 7, 0}, 1, .02f, {1, 0}};
    joint_contact_row adjacent = {{JOINT_EDGE, 0, 3, 1, 8, 0}, 0, .02f, {1, 0}};
    JOINT_CHECK(joint_normalize(&endpoint) == 0);
    JOINT_CHECK(joint_normalize(&adjacent) == 0 && joint_key_equal(endpoint.key, adjacent.key));
    joint_contact_row again = endpoint;
    JOINT_CHECK(joint_normalize(&again) == 0 && memcmp(&again, &endpoint, sizeof again) == 0);
    joint_contact_registry a = {0}, b = {0};
    JOINT_CHECK(joint_register(&a, endpoint) == 0 && joint_register(&a, adjacent) == 0 && a.count == 1);
    JOINT_CHECK(joint_register(&b, adjacent) == 0 && joint_register(&b, endpoint) == 0 && b.count == 1);
    JOINT_CHECK(joint_key_equal(a.rows[0].key, b.rows[0].key));
    joint_contact_row middle = {{JOINT_EDGE, 0, 3, 1, 7, 0}, .5f, .02f, {1, 0}};
    JOINT_CHECK(joint_register(&a, middle) == 0 && a.count == 2);
    middle.key.feature = 8;
    JOINT_CHECK(joint_register(&a, middle) == 0 && a.count == 3);
    middle.t = 1e-7f;
    JOINT_CHECK(joint_normalize(&middle) == 0 && middle.key.kind == JOINT_EDGE);
    joint_contact_registry saved = a;
    endpoint.gap += .01f;
    JOINT_CHECK(joint_register(&a, endpoint) == DANGO_JOINT_ERROR_CONTACT_CONFLICT && memcmp(&a, &saved, sizeof a) == 0);
    endpoint.key.point_node = 16;
    JOINT_CHECK(joint_register(&a, endpoint) == DANGO_JOINT_ERROR_CONTACT_INDEX);
    endpoint.key.point_node = 3; endpoint.gap = NAN;
    JOINT_CHECK(joint_register(&a, endpoint) == DANGO_JOINT_ERROR_CONTACT_GEOMETRY);
    endpoint.gap = 0; endpoint.normal.x = INFINITY;
    JOINT_CHECK(joint_register(&a, endpoint) == DANGO_JOINT_ERROR_CONTACT_GEOMETRY);
}

static void joint_capacity_tests(void)
{
    joint_contact_registry registry = {0};
    for (int r = 0; r < 3; ++r) for (int t = 0; t < 3; ++t) if (r != t)
        for (int n = 0; n < 16; ++n)
        {
            joint_contact_row row = {{JOINT_EDGE, r, n, t, n, 0}, .5f, 0, {1, 0}};
            JOINT_CHECK(joint_register(&registry, row) == 0);
        }
    JOINT_CHECK(registry.count == 96 && registry.raw_cross == 96);
    joint_contact_registry saved = registry;
    joint_contact_row cross = {{JOINT_EDGE, 0, 0, 1, 1, 0}, .5f, 0, {1, 0}};
    JOINT_CHECK(joint_register(&registry, cross) == DANGO_JOINT_ERROR_CONTACT_CAPACITY && memcmp(&saved, &registry, sizeof saved) == 0);
    for (int r = 0; r < 3; ++r) for (int n = 0; n < 16; ++n)
    {
        joint_contact_row ground = {{JOINT_GROUND, r, n, -1, -1, 1}, 0, 0, {0, 1}};
        JOINT_CHECK(joint_register(&registry, ground) == 0);
    }
    JOINT_CHECK(registry.count == 144 && registry.raw_ground == 48);
    saved = registry;
    joint_contact_row extra = {{JOINT_GROUND, 0, 0, -1, -1, 2}, 0, 0, {0, 1}};
    JOINT_CHECK(joint_register(&registry, extra) == DANGO_JOINT_ERROR_CONTACT_CAPACITY && memcmp(&saved, &registry, sizeof saved) == 0);
    joint_contact_registry floor_only = {0};
    for (int i = 96; i < 144; ++i) JOINT_CHECK(joint_register(&floor_only, registry.rows[i]) == 0);
    JOINT_CHECK(joint_register(&floor_only, extra) == DANGO_JOINT_ERROR_CONTACT_CAPACITY);
    printf("J2.5 artificial capacity cross=%d ground=%d total=%d overflow=rejected\n", registry.raw_cross, registry.raw_ground, registry.count);
}

static void joint_geometry_tests(void)
{
    dango_joint_state state = joint_polygon_fixture(), original = state;
    joint_contact_snapshot contacts, separated, restored;
    JOINT_CHECK(joint_build_contacts(&state, &contacts) == 0);
    JOINT_CHECK(contacts.candidates == 96 && contacts.registry.count > 0);
    JOINT_CHECK(memcmp(&state, &original, sizeof state) == 0);
    // Every active directed candidate must be covered by exactly one canonical row.
    for (int r = 0; r < 3; ++r) for (int t = 0; t < 3; ++t) if (r != t)
        for (int n = 0; n < 16; ++n)
        {
            joint_contact_row row;
            JOINT_CHECK(joint_nearest(&state, r, n, t, &row) == 0);
            float legacy = dango_joint_node_edge_position(&state, r, n, t, false);
            JOINT_CHECK(fabsf(legacy - fmaxf(0, state.skin - row.gap)) < 1e-6f);
            int found = 0;
            for (int i = 0; i < contacts.registry.count; ++i) if (joint_key_equal(row.key, contacts.registry.rows[i].key)) ++found;
            if (row.gap <= state.skin) JOINT_CHECK(found == 1);
        }
    for (int n = 0; n < 17; ++n) state.rigs[1].positions[n].y += 10;
    JOINT_CHECK(joint_build_contacts(&state, &separated) == 0);
    joint_contact_changes changes = joint_compare_contacts(&contacts, &separated);
    JOINT_CHECK(changes.removed > 0 && changes.switches > 0 && separated.registry.count == 0);
    state = original;
    JOINT_CHECK(joint_build_contacts(&state, &restored) == 0 && memcmp(&contacts, &restored, sizeof contacts) == 0);
    changes = joint_compare_contacts(&separated, &restored);
    JOINT_CHECK(changes.added > 0 && changes.switches > 0);
    // Mirror x and reverse node winding to keep the convex polygon CCW.
    for (int r = 0; r < 3; ++r) for (int n = 0; n < 16; ++n)
    {
        b2Vec2 p = original.rigs[r].positions[(16 - n) % 16];
        state.rigs[r].positions[n] = (b2Vec2){-p.x, p.y};
    }
    for (int r = 0; r < 3; ++r) state.rigs[r].positions[16].x *= -1;
    JOINT_CHECK(joint_build_contacts(&state, &restored) == 0 && restored.registry.count == contacts.registry.count);
    for (int i = 0; i < restored.registry.count; ++i)
    {
        joint_contact_row mirrored = restored.registry.rows[i];
        mirrored.key.point_node = (16 - mirrored.key.point_node) % 16;
        mirrored.key.feature = (16 - mirrored.key.feature - (mirrored.key.kind == JOINT_EDGE ? 1 : 0)) % 16;
        int found = 0;
        for (int j = 0; j < contacts.registry.count; ++j)
            if (joint_key_equal(mirrored.key, contacts.registry.rows[j].key) && fabsf(mirrored.gap - contacts.registry.rows[j].gap) < 1e-6f) ++found;
        JOINT_CHECK(found == 1);
    }
    state = original; state.rigs[1].positions[1] = state.rigs[1].positions[0];
    joint_contact_snapshot saved = restored;
    JOINT_CHECK(joint_build_contacts(&state, &restored) == DANGO_JOINT_ERROR_CONTACT_GEOMETRY && memcmp(&saved, &restored, sizeof saved) == 0);
    state = original; state.rigs[1].positions[0].x = NAN;
    JOINT_CHECK(joint_build_contacts(&state, &restored) != 0);
    joint_contact_row row;
    JOINT_CHECK(joint_nearest(&original, 0, 16, 1, &row) == DANGO_JOINT_ERROR_CONTACT_INDEX);
}

static void joint_live_ground_tests(void)
{
    int initial_bytes = b2GetByteCount();
    b2WorldDef wd = b2DefaultWorldDef(); wd.gravity = (b2Vec2){0, 0};
    b2WorldId world = b2CreateWorld(&wd);
    b2BodyDef bd = b2DefaultBodyDef(); bd.type = b2_dynamicBody;
    b2BodyId node = b2CreateBody(world, &bd);
    b2ShapeDef sd = b2DefaultShapeDef();
    b2Circle circle = {{0, 0}, 1};
    b2CreateCircleShape(node, &sd, &circle);
    bd.type = b2_staticBody;
    b2ShapeId floor = b2_nullShapeId, unrelated = b2_nullShapeId;
    for (int i = 0; i < 12; ++i)
    {
        float angle = 2 * B2_PI * i / 12;
        bd.position = (b2Vec2){1.99f * cosf(angle), 1.99f * sinf(angle)};
        b2BodyId obstacle = b2CreateBody(world, &bd);
        b2ShapeId shape = b2CreateCircleShape(obstacle, &sd, &circle);
        if (i == 9) floor = shape;
    }
    bd.position = (b2Vec2){100, 100};
    unrelated = b2CreateCircleShape(b2CreateBody(world, &bd), &sd, &circle);
    b2World_Step(world, 1.0f / 240, 1);
    joint_ground_sample sample = {0}, absent = {0};
    JOINT_CHECK(joint_read_ground(node, floor, &sample) == 0);
    JOINT_CHECK(sample.capacity > 8 && sample.returned > 8 && sample.matching == 1);
    JOINT_CHECK(joint_read_ground(node, unrelated, &absent) == 0 && absent.matching == 0 && absent.normal == 0 && absent.tangent == 0);
    JOINT_CHECK(B2_ID_EQUALS(sample.shape, floor));
    joint_contact_registry registry = {0};
    JOINT_CHECK(joint_register_ground(&registry, 0, 0, -.01f, &sample) == 0 && registry.count == 1);
    JOINT_CHECK(registry.rows[0].key.shape == b2StoreShapeId(floor));
    JOINT_CHECK(joint_register_ground(&registry, 0, 1, -.01f, &absent) == 0 && registry.count == 1);
    printf("J2.5 live-ground capacity=%d returned=%d matching=%d\n", sample.capacity, sample.returned, sample.matching);
    b2DestroyWorld(world);
    JOINT_CHECK(joint_read_ground(node, floor, &sample) == DANGO_JOINT_ERROR_CONTACT_READ);
    JOINT_CHECK(b2GetByteCount() == initial_bytes);
    // Actual nonzero support: a falling dynamic circle on one physical floor.
    wd = b2DefaultWorldDef(); world = b2CreateWorld(&wd);
    bd = b2DefaultBodyDef();
    b2BodyId floor_body = b2CreateBody(world, &bd);
    b2Polygon box = b2MakeBox(10, .1f);
    floor = b2CreatePolygonShape(floor_body, &sd, &box);
    bd.type = b2_dynamicBody; bd.position = (b2Vec2){0, 1.1f}; bd.enableSleep = false;
    node = b2CreateBody(world, &bd);
    b2CreateCircleShape(node, &sd, &circle);
    for (int i = 0; i < 60; ++i) b2World_Step(world, 1.0f / 240, 1);
    b2Vec2 position = b2Body_GetPosition(node), velocity = b2Body_GetLinearVelocity(node);
    JOINT_CHECK(joint_read_ground(node, floor, &sample) == 0 && sample.matching == 1 && sample.normal > 0);
    b2Vec2 position_after = b2Body_GetPosition(node), velocity_after = b2Body_GetLinearVelocity(node);
    JOINT_CHECK(memcmp(&position, &position_after, sizeof position) == 0 && memcmp(&velocity, &velocity_after, sizeof velocity) == 0);
    printf("J2.5 live support normal=%.9g tangent=%.9g\n", (double)sample.normal, (double)sample.tangent);
    b2DestroyWorld(world);
    JOINT_CHECK(b2GetByteCount() == initial_bytes);
}

static void joint_observation_tests(void)
{
    dango_joint_state before = joint_polygon_fixture(), after = before;
    after.rigs[1].positions[0].y += .1f;
    after.ground_position_lambda[0][12] = .001f;
    dango_joint_state saved_before = before, saved_after = after;
    unsigned long long visits[7] = {0};
    joint_group_observation report;
    JOINT_CHECK(joint_observe_group(&before, &after, visits, &report) == 0);
    JOINT_CHECK(memcmp(&before, &saved_before, sizeof before) == 0 && memcmp(&after, &saved_after, sizeof after) == 0);
    JOINT_CHECK(report.maximum_move > .099f && report.diagnostic_rebuilds == 192 && report.generation_rebuilds == 0 && report.final_rebuilds == 0);
    JOINT_CHECK(report.after.maximum[2] > 0 || report.after.maximum[3] > 0 || report.after.maximum[1] > 0);
    for (int k = 0; k < 6; ++k) JOINT_CHECK(report.after.row_reads[k] == 48);
    for (int i = 0; i < 2; ++i)
    {
        const char* path = i == 0 ? "artifacts/p2d-007c/joint-solver/solver-boundary-.020-v1.bin" :
            "artifacts/p2d-007c/joint-solver/solver-boundary-.045-v1.bin";
        dango_joint_fixture_record record;
        FILE* file = fopen(path, "rb");
        JOINT_CHECK(file != NULL);
        if (!file) continue;
        bool read = fread(&record, sizeof record, 1, file) == 1 && fgetc(file) == EOF;
        fclose(file);
        JOINT_CHECK(read && record.schema_version == 1);
        if (!read) continue;
        saved_before = record.state;
        joint_contact_snapshot contacts;
        int error = joint_build_contacts(&record.state, &contacts);
        JOINT_CHECK(error == 0);
        if (error) { printf("J2.5 natural fixture error=%d\n", error); continue; }
        joint_residual_report residual;
        JOINT_CHECK(joint_measure(&record.state, &contacts, &residual) == 0);
        JOINT_CHECK(memcmp(&saved_before, &record.state, sizeof saved_before) == 0);
        printf("J2.5 natural skin=%.3f candidates=%d active=%d unique=%d duplicate=%d geometric_ground=%d contact_residual=%.9g checksum=%llu\n",
            (double)record.state.skin, contacts.candidates, contacts.active_cross, contacts.registry.count,
            contacts.registry.duplicates, contacts.ground_geometric, (double)residual.maximum[6], fixture_checksum(&record, sizeof record));
    }
}

static int contact_extended_tests(void)
{
    joint_key_tests(); joint_capacity_tests(); joint_geometry_tests(); joint_live_ground_tests(); joint_observation_tests();
    printf("J2.5 assertions=%d failures=%d\n", joint_checks, joint_failures);
    return joint_failures != 0;
}

static FILE* joint_trace_output;
static dango_joint_state joint_trace_before;
static joint_contact_snapshot joint_trace_previous;
static bool joint_trace_has_previous;
static int joint_trace_errors, joint_trace_switches, joint_trace_duplicates, joint_trace_groups;
static unsigned long long joint_trace_diagnostic_rebuilds;

static void joint_trace_observer(const dango_joint_state* state, int scan, int phase, int rig, bool before)
{
    if (phase == 3)
    {
        joint_contact_snapshot snapshot;
        int error = joint_build_contacts(state, &snapshot);
        if (error) { ++joint_trace_errors; return; }
        joint_trace_diagnostic_rebuilds += snapshot.candidates;
        joint_contact_changes changes = {0};
        if (joint_trace_has_previous) changes = joint_compare_contacts(&joint_trace_previous, &snapshot);
        joint_trace_switches += changes.switches;
        joint_trace_duplicates += snapshot.registry.duplicates;
        fprintf(joint_trace_output, "scan,%d,candidates,%d,active,%d,unique,%d,duplicates,%d,added,%d,removed,%d,switches,%d\n",
            scan, snapshot.candidates, snapshot.active_cross, snapshot.registry.count,
            snapshot.registry.duplicates, changes.added, changes.removed, changes.switches);
        joint_trace_previous = snapshot;
        joint_trace_has_previous = true;
        return;
    }
    if (before) { joint_trace_before = *state; return; }
    unsigned long long visits[7] = {0};
    if (phase == 0) { for (int k = 0; k < 5; ++k) visits[k] = 16; }
    else if (phase == 1)
    {
        for (int n = 0; n < 16; ++n)
            if (joint_trace_before.rigs[rig].positions[n].y - joint_trace_before.rigs[rig].radius -
                joint_trace_before.rigs[rig].ground <= .005f) ++visits[5];
    }
    else visits[6] = (unsigned long long)state->body_count * (state->body_count - 1) * 16;
    joint_group_observation report;
    int error = joint_observe_group(&joint_trace_before, state, visits, &report);
    if (error) { ++joint_trace_errors; fprintf(joint_trace_output, "error,%d,scan,%d,phase,%d\n", error, scan, phase); return; }
    ++joint_trace_groups;
    joint_trace_diagnostic_rebuilds += report.diagnostic_rebuilds;
    fprintf(joint_trace_output, "group,%d,phase,%d,rig,%d,max_group_displacement,%.9g,diagnostic_rebuilds,%d\n",
        scan, phase, rig, (double)report.maximum_move, report.diagnostic_rebuilds);
    for (int k = 0; k < 7; ++k)
        fprintf(joint_trace_output, "residual,%d,phase,%d,rig,%d,kind,%d,before,%.9g,after,%.9g,max_rig,%d,max_node,%d,visits,%llu,reads,%d\n",
            scan, phase, rig, k, (double)report.before.maximum[k], (double)report.after.maximum[k],
            report.after.rig[k], report.after.node[k], report.corrected_rows[k], report.after.row_reads[k]);
    joint_contact_key key = report.after.cross_key;
    fprintf(joint_trace_output, "cross_key,%d,%d,%d,%d,%d\n", key.kind, key.point_rig, key.point_node, key.target_rig, key.feature);
}

static int contact_diagnostics_tests(const char* path)
{
    // Exclusive creation preserves prior diagnostics as well as the old red logs.
    joint_trace_output = fopen(path, "wx");
    if (!joint_trace_output) { fprintf(stderr, "Cannot create new diagnostic file: %s\n", path); return 1; }
    int failures = 0;
    for (int i = 0; i < 2; ++i)
    {
        const char* input = i == 0 ? "artifacts/p2d-007c/joint-solver/solver-boundary-.020-v1.bin" :
            "artifacts/p2d-007c/joint-solver/solver-boundary-.045-v1.bin";
        FILE* file = fopen(input, "rb");
        dango_joint_fixture_record record;
        if (!file) { ++failures; continue; }
        bool read = fread(&record, sizeof record, 1, file) == 1 && fgetc(file) == EOF;
        fclose(file);
        if (!read || record.schema_version != 1) { ++failures; continue; }
        dango_joint_state observed = record.state, control = record.state;
        joint_trace_errors = joint_trace_switches = joint_trace_duplicates = joint_trace_groups = 0;
        joint_trace_diagnostic_rebuilds = 0;
        joint_trace_has_previous = false;
        fprintf(joint_trace_output, "fixture,%.3f,algorithm,legacy_J3_observation_only\n", (double)record.state.skin);
        dango_joint_position_observer = joint_trace_observer;
        dango_joint_solver_result result = dango_joint_solve_positions(&observed);
        dango_joint_position_observer = NULL;
        dango_joint_solver_result plain = dango_joint_solve_positions(&control);
        bool identical = memcmp(&observed, &control, sizeof observed) == 0 && memcmp(&result, &plain, sizeof result) == 0;
        if (!identical || joint_trace_errors || result.succeeded || result.position_scans != 96 || joint_trace_groups != 672) ++failures;
        printf("J2.5 trace skin=%.3f scans=%d groups=%d feature_switches=%d duplicate_sum=%d observer_errors=%d identical=%d self=%.9g cross=%.9g solver_rebuilds=%llu diagnostic_rebuilds=%llu\n",
            (double)record.state.skin, result.position_scans, joint_trace_groups, joint_trace_switches,
            joint_trace_duplicates, joint_trace_errors, identical, (double)result.maximum_self_residual,
            (double)result.maximum_contact_residual, result.contact_rebuilds, joint_trace_diagnostic_rebuilds);
    }
    if (ferror(joint_trace_output)) ++failures;
    if (fclose(joint_trace_output) != 0) ++failures;
    joint_trace_output = NULL;
    printf("J2.5 trace failures=%d file=%s\n", failures, path);
    return failures != 0;
}
#undef JOINT_CHECK

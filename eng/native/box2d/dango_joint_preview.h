// Stdio preview host: fictional physics state only, never production ABI.
#ifndef DANGO_JOINT_PREVIEW_H
#define DANGO_JOINT_PREVIEW_H
typedef struct joint_preview
{
    dango_self_soft_body* bodies[3];
    dango_self_soft_body extra[2];
    dango_self_physics_config config;
    dango_joint_state published;
    int count, frame, error, warnings, position_scans, velocity_scans;
    float skin, offset, self_residual, contact_residual, velocity_residual;
    double energy_gain;
} joint_preview;
static int joint_preview_close(joint_preview* preview)
{
    int bytes = 0;
    if (preview->bodies[0]) dango_box2d_self_soft_body_destroy(preview->bodies[0], &bytes);
    memset(preview, 0, sizeof *preview);
    return bytes;
}
static int joint_preview_read(joint_preview* p, dango_joint_state* state)
{
    for (int r = 0; r < p->count; ++r) for (int n = 0; n < 16; ++n)
    {
        joint_ground_sample sample;
        int error = joint_read_ground(p->bodies[r]->node_body_ids[n], p->bodies[r]->ground_shape_id, &sample);
        if (error) return error;
        pair_ground[r].normals[n] = sample.normal; pair_ground[r].tangents[n] = sample.tangent;
    }
    joint_read_solver_state(state, p->bodies, p->count, &p->config, p->skin, 1.0f / 240);
    return 0;
}
static int joint_preview_reset(joint_preview* p, int count, float skin, float offset)
{
    if (count < 1 || count > 3 || (skin != .020f && skin != .045f) || !isfinite(offset) || fabsf(offset) > .1f) return -301;
    int bytes = joint_preview_close(p); if (bytes) return -302;
    p->config = configuration(); p->count = count; p->skin = skin; p->offset = offset;
    ground_candidate = false;
    void* handle = NULL; int error = dango_box2d_self_soft_body_create(&p->config, &handle);
    if (error) return error;
    p->bodies[0] = handle;
    for (int r = 1; r < count; ++r)
    {
        p->bodies[r] = &p->extra[r - 1];
        p->bodies[r]->world_id = p->bodies[0]->world_id;
        p->bodies[r]->ground_body_id = p->bodies[0]->ground_body_id;
        p->bodies[r]->ground_shape_id = p->bodies[0]->ground_shape_id;
        dango_self_create_rig(p->bodies[r], &p->config);
        for (int n = 0; n < 17; ++n)
        {
            b2BodyId id = joint_fixture_body_id(p->bodies[r], n);
            b2Vec2 delta = {offset * 2 * (p->bodies[0]->radial_rest_length + p->config.node_radius) * r, 1.65f * r};
            b2Body_SetTransform(id, b2Add(b2Body_GetPosition(id), delta), b2Rot_identity);
        }
    }
    b2Shape_SetFriction(p->bodies[0]->ground_shape_id, 0);
    for (int r = 0; r < count; ++r) for (int n = 0; n < 17; ++n)
        pair_ground[r].previous[n] = b2Body_GetPosition(joint_fixture_body_id(p->bodies[r], n));
    p->error = joint_preview_read(p, &p->published);
    return p->error;
}
static void joint_preview_step(joint_preview* p)
{
    if (p->error || !p->bodies[0]) return;
    for (int sub = 0; sub < 4; ++sub)
    {
        for (int r = 0; r < p->count; ++r)
        {
            for (int n = 0; n < 17; ++n) pair_ground[r].previous[n] = b2Body_GetPosition(joint_fixture_body_id(p->bodies[r], n));
            dango_self_update_joint_parameters(p->bodies[r], &p->config);
            dango_self_apply_internal_forces(p->bodies[r], &p->config);
        }
        b2World_Step(p->bodies[0]->world_id, 1.0f / 240, 1);
        dango_joint_state state;
        p->error = joint_preview_read(p, &state); if (p->error) return;
        joint_r_result position = joint_r_solve(&state, p->config.minimum_area, NULL);
        p->position_scans = position.scans; p->self_residual = 0;
        for (int k = 0; k < 5; ++k) p->self_residual = fmaxf(p->self_residual, position.residual.maximum[k]);
        p->contact_residual = position.residual.maximum[6];
        if (!position.success)
        {
            if (position.error != -102 || !position.topology || p->self_residual > .001f || p->contact_residual > .001f)
            { p->error = position.error ? position.error : -303; return; }
            ++p->warnings;
        }
        joint_v_system velocities;
        p->error = joint_v_build(&state, &velocities); if (p->error) return;
        joint_v_result velocity = joint_v_solve(&state, &velocities);
        p->velocity_scans = velocity.scans; p->velocity_residual = velocity.residual; p->energy_gain = velocity.energy_gain;
        if (velocity.error) { p->error = velocity.error; return; }
        float maximum_speed = 0;
        for (int r = 0; r < p->count; ++r) for (int n = 0; n < 17; ++n)
        {
            if (!b2IsValidVec2(state.rigs[r].velocities[n])) { p->error = -202; return; }
            maximum_speed = fmaxf(maximum_speed, b2Length(state.rigs[r].velocities[n]));
        }
        float scale = maximum_speed > p->config.maximum_speed ? p->config.maximum_speed / maximum_speed : 1;
        for (int r = 0; r < p->count; ++r) for (int n = 0; n < 17; ++n)
        {
            b2BodyId id = joint_fixture_body_id(p->bodies[r], n);
            state.rigs[r].velocities[n] = b2MulSV(scale, state.rigs[r].velocities[n]);
            b2Body_SetTransform(id, state.rigs[r].positions[n], b2Rot_identity);
            b2Body_SetLinearVelocity(id, state.rigs[r].velocities[n]);
        }
        p->published = state;
    }
    ++p->frame;
}
static void joint_preview_remove_support(joint_preview* p)
{
    if (p->error || p->count < 2) return;
    for (int n = 0; n < 17; ++n)
    {
        b2BodyId id = joint_fixture_body_id(p->bodies[0], n);
        b2Body_SetTransform(id, b2Add(b2Body_GetPosition(id), (b2Vec2){-3, 0}), b2Rot_identity);
    }
}
static void joint_preview_print(const joint_preview* p)
{
    printf("{\"frame\":%d,\"count\":%d,\"error\":%d,\"warnings\":%d,\"self\":%.9g,\"contact\":%.9g,\"velocity\":%.9g,\"energy\":%.9g,\"ground\":%.9g,\"radius\":%.9g,\"nodes\":[",
        p->frame, p->count, p->error, p->warnings, (double)p->self_residual, (double)p->contact_residual,
        (double)p->velocity_residual, p->energy_gain, (double)p->config.ground_height, (double)p->config.node_radius);
    for (int r = 0; r < p->count; ++r) for (int n = 0; n < 17; ++n)
        printf("%s[%.9g,%.9g]", r || n ? "," : "", (double)p->published.rigs[r].positions[n].x, (double)p->published.rigs[r].positions[n].y);
    printf("]}\n"); fflush(stdout);
}
static int joint_preview_host(void)
{
    joint_preview preview = {0}; char line[128];
    while (fgets(line, sizeof line, stdin))
    {
        if (strncmp(line, "quit", 4) == 0) break;
        if (strncmp(line, "reset ", 6) == 0)
        {
            int count; float skin, offset;
            if (sscanf(line + 6, "%d %f %f", &count, &skin, &offset) != 3) preview.error = -301;
            else preview.error = joint_preview_reset(&preview, count, skin, offset);
        }
        else if (strncmp(line, "step", 4) == 0) joint_preview_step(&preview);
        else if (strncmp(line, "remove", 6) == 0) joint_preview_remove_support(&preview);
        else preview.error = -301;
        joint_preview_print(&preview);
    }
    return joint_preview_close(&preview) == 0 ? 0 : 1;
}
static int joint_preview_smoke(void)
{
    int failures = 0;
    for (int count = 1; count <= 3; ++count)
    {
        joint_preview p = {0}; p.error = joint_preview_reset(&p, count, .045f, 0);
        while (!p.error && p.frame < 600) joint_preview_step(&p);
        printf("preview-smoke count=%d frame=%d error=%d warnings=%d self=%.9g contact=%.9g velocity=%.9g energy=%.9g\n",
            count, p.frame, p.error, p.warnings, (double)p.self_residual, (double)p.contact_residual, (double)p.velocity_residual, p.energy_gain);
        if (p.error) ++failures;
        if (joint_preview_close(&p) != 0) ++failures;
    }
    return failures ? 1 : 0;
}
#endif

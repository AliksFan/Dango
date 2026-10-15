// Isolated preview velocity constraints, all geometry fixed for the substep.
#ifndef DANGO_JOINT_VELOCITY_H
#define DANGO_JOINT_VELOCITY_H
typedef struct joint_v_row
{
    int count, ids[4], normal;
    b2Vec2 gradients[4];
    float k, lambda, friction;
    bool ground;
} joint_v_row;
typedef struct joint_v_system
{
    joint_v_row rows[672];
    int count, shape_count, contact_count;
    b2Vec2 ground_delta;
} joint_v_system;
typedef struct joint_v_result { int error, scans, rows; float residual; double energy_gain; } joint_v_result;

static inline b2Vec2* joint_v_velocity(dango_joint_state* state, int id) { return &state->rigs[id / 17].velocities[id % 17]; }
static inline float joint_v_mass(const dango_joint_state* state, int id) { return state->rigs[id / 17].inverse_mass[id % 17]; }
static inline float joint_v_rate(dango_joint_state* state, const joint_v_row* row)
{
    float value = 0; for (int i = 0; i < row->count; ++i) value += b2Dot(row->gradients[i], *joint_v_velocity(state, row->ids[i]));
    return value;
}
static inline int joint_v_add(joint_v_system* sys, const dango_joint_state* state, joint_v_row row, bool shape)
{
    if (sys->count >= 672 || (shape ? sys->shape_count >= 384 : sys->contact_count >= 288)) return -201;
    row.k = 0;
    for (int i = 0; i < row.count; ++i)
    {
        if (row.ids[i] < 0 || row.ids[i] >= state->body_count * 17 || !b2IsValidVec2(row.gradients[i])) return -202;
        row.k += joint_v_mass(state, row.ids[i]) * b2LengthSquared(row.gradients[i]);
    }
    if (!isfinite(row.k) || row.k <= 1e-6f) return -202;
    sys->rows[sys->count++] = row;
    if (shape) ++sys->shape_count; else ++sys->contact_count;
    return 0;
}
static inline void joint_v_project(dango_joint_state* state, joint_v_system* sys, int index)
{
    joint_v_row* row = &sys->rows[index];
    float next = row->lambda - joint_v_rate(state, row) / row->k;
    if (row->normal < 0) next = fmaxf(0, next);
    else { float limit = row->friction * sys->rows[row->normal].lambda; next = fmaxf(-limit, fminf(limit, next)); }
    float delta = next - row->lambda; row->lambda = next;
    for (int i = 0; i < row->count; ++i)
    {
        b2Vec2 impulse = b2MulSV(delta, row->gradients[i]);
        b2Vec2* v = joint_v_velocity(state, row->ids[i]);
        *v = b2MulAdd(*v, joint_v_mass(state, row->ids[i]), impulse);
        if (row->ground) sys->ground_delta = b2Add(sys->ground_delta, impulse);
    }
}
static inline int joint_v_distance(joint_v_system* sys, const dango_joint_state* state, int rig, int a, int b, float low, float high)
{
    const dango_constraint_state* s = &state->rigs[rig];
    b2Vec2 d = b2Sub(s->positions[b], s->positions[a]); float length = b2Length(d);
    if (!isfinite(length) || length < 1e-5f) return -202;
    float margin = 4 * DANGO_CONSTRAINT_TOLERANCE * s->radial_length;
    for (int bound = 0; bound < 2; ++bound)
    {
        if (bound ? length < high - margin : length > low + margin) continue;
        b2Vec2 unit = b2MulSV((bound ? -1.0f : 1.0f) / length, d);
        joint_v_row row = {0}; row.count = 2; row.normal = -1;
        row.ids[0] = rig * 17 + a; row.ids[1] = rig * 17 + b;
        row.gradients[0] = b2Neg(unit); row.gradients[1] = unit;
        int error = joint_v_add(sys, state, row, true); if (error) return error;
    }
    return 0;
}
static inline int joint_v_angle(joint_v_system* sys, const dango_joint_state* state, int rig, int node, bool display)
{
    const dango_constraint_state* s = &state->rigs[rig];
    int ids[4] = {(node + 15) % 16, node, (node + 1) % 16, 16};
    b2Vec2 p[3]; for (int i = 0; i < 3; ++i) p[i] = display ? dango_constraint_display_point(s, ids[i]) : s->positions[ids[i]];
    b2Vec2 e = b2Sub(p[1], p[0]), f = b2Sub(p[2], p[1]); float e2 = b2LengthSquared(e), f2 = b2LengthSquared(f);
    if (e2 < 1e-10f || f2 < 1e-10f) return -202;
    float turn = atan2f(b2Cross(e, f), b2Dot(e, f));
    if (!isfinite(turn)) return -202;
    bool lower = turn <= .002f + 4e-5f, upper = turn >= 1.5687963268f - 4e-5f;
    if (!lower && !upper) return 0;
    joint_v_row row = {0}; row.count = display ? 4 : 3; row.normal = -1;
    row.gradients[0] = (b2Vec2){-e.y / e2, e.x / e2}; row.gradients[2] = (b2Vec2){-f.y / f2, f.x / f2};
    row.gradients[1] = b2Neg(b2Add(row.gradients[0], row.gradients[2]));
    if (display) for (int i = 0; i < 3; ++i)
    {
        b2Vec2 radial = b2Sub(s->positions[ids[i]], s->positions[16]); float length = b2Length(radial);
        if (length < 1e-5f) return -202;
        b2Vec2 unit = b2MulSV(1 / length, radial);
        b2Vec2 extra = b2MulSV(s->radius / length, b2MulSub(row.gradients[i], b2Dot(unit, row.gradients[i]), unit));
        row.gradients[i] = b2Add(row.gradients[i], extra); row.gradients[3] = b2Sub(row.gradients[3], extra);
    }
    for (int i = 0; i < row.count; ++i) { row.ids[i] = rig * 17 + ids[i]; if (upper) row.gradients[i] = b2Neg(row.gradients[i]); }
    return joint_v_add(sys, state, row, true);
}
static inline int joint_v_contact(joint_v_system* sys, const dango_joint_state* state, joint_v_row normal, float mu, float tangent)
{
    int index = sys->count;
    int error = joint_v_add(sys, state, normal, false); if (error) return error;
    normal.normal = index; normal.friction = mu; normal.lambda = tangent;
    for (int i = 0; i < normal.count; ++i) normal.gradients[i] = (b2Vec2){normal.gradients[i].y, -normal.gradients[i].x};
    return joint_v_add(sys, state, normal, false);
}
static int joint_v_build(const dango_joint_state* state, joint_v_system* output)
{
    if (!dango_joint_state_valid(state)) return -202;
    joint_v_system sys = {0};
    for (int r = 0; r < state->body_count; ++r) for (int n = 0; n < 16; ++n)
    {
        const dango_constraint_state* s = &state->rigs[r];
        int error = joint_v_distance(&sys, state, r, 16, n, s->radial_length * s->radial_min, s->radial_length * s->radial_max);
        if (!error) error = joint_v_distance(&sys, state, r, n, (n + 1) % 16, s->perimeter_length * s->perimeter_min, s->perimeter_length * s->perimeter_max);
        if (!error) error = joint_v_angle(&sys, state, r, n, false);
        if (!error) error = joint_v_angle(&sys, state, r, n, true);
        if (error) return error;
        if (s->positions[n].y - s->radius - s->ground <= 1e-5f)
        {
            joint_v_row row = {0}; row.count = 1; row.ids[0] = r * 17 + n; row.normal = -1;
            row.gradients[0] = (b2Vec2){0, 1}; row.ground = true; row.lambda = state->ground_normal_impulse[r][n];
            error = joint_v_contact(&sys, state, row, state->ground_friction, state->ground_tangent_impulse[r][n]);
            if (error) return error;
        }
    }
    joint_contact_registry registry = {0};
    for (int r = 0; r < state->body_count; ++r) for (int t = 0; t < state->body_count; ++t) if (r != t)
        for (int n = 0; n < 16; ++n)
        {
            joint_contact_row row; int error = joint_nearest(state, r, n, t, &row);
            if (error) return error;
            if (row.gap <= state->skin + 1e-5f) { error = joint_register(&registry, row); if (error) return error; }
        }
    for (int i = 0; i < registry.count; ++i)
    {
        joint_contact_row* contact = &registry.rows[i]; joint_v_row row = {0};
        row.count = contact->key.kind == JOINT_VERTEX ? 2 : 3; row.normal = -1;
        row.ids[0] = contact->key.point_rig * 17 + contact->key.point_node;
        row.ids[1] = contact->key.target_rig * 17 + contact->key.feature;
        row.ids[2] = contact->key.target_rig * 17 + (contact->key.feature + 1) % 16;
        row.gradients[0] = contact->normal;
        row.gradients[1] = b2MulSV(-(1 - contact->t), contact->normal);
        row.gradients[2] = b2MulSV(-contact->t, contact->normal);
        int error = joint_v_contact(&sys, state, row, state->inter_body_friction, 0); if (error) return error;
    }
    *output = sys; return 0;
}
static inline double joint_v_energy(const dango_joint_state* state)
{
    double energy = 0; for (int r = 0; r < state->body_count; ++r) for (int n = 0; n < 17; ++n)
        energy += .5 * b2LengthSquared(state->rigs[r].velocities[n]) / state->rigs[r].inverse_mass[n];
    return energy;
}
static inline b2Vec2 joint_v_momentum(const dango_joint_state* state)
{
    b2Vec2 p = {0}; for (int r = 0; r < state->body_count; ++r) for (int n = 0; n < 17; ++n)
        p = b2MulAdd(p, 1 / state->rigs[r].inverse_mass[n], state->rigs[r].velocities[n]);
    return p;
}
static joint_v_result joint_v_solve(dango_joint_state* state, joint_v_system* sys)
{
    joint_v_result result = {0}; result.rows = sys->count;
    double energy = joint_v_energy(state); b2Vec2 momentum = joint_v_momentum(state);
    for (int scan = 0; scan < 96; ++scan)
    {
        result.scans = scan + 1;
        for (int i = 0; i < sys->count; ++i) joint_v_project(state, sys, i);
        result.residual = 0;
        for (int i = 0; i < sys->count; ++i)
        {
            joint_v_row* row = &sys->rows[i]; float rate = joint_v_rate(state, row);
            if (!isfinite(rate) || !isfinite(row->lambda)) { result.error = -202; return result; }
            float residual;
            if (row->normal < 0) residual = row->lambda > 0 ? fabsf(rate) : fmaxf(0, -rate);
            else
            {
                float limit = row->friction * sys->rows[row->normal].lambda;
                float projected = fmaxf(-limit, fminf(limit, row->lambda - rate / row->k));
                residual = fabsf(projected - row->lambda) * row->k;
            }
            result.residual = fmaxf(result.residual, residual);
        }
        if (result.residual <= 1e-5f) break;
    }
    result.energy_gain = joint_v_energy(state) - energy;
    if (!isfinite(result.energy_gain) || result.energy_gain > 1e-6) result.error = -206;
    else if (b2Distance(b2Sub(joint_v_momentum(state), momentum), sys->ground_delta) > 1e-5f) result.error = -207;
    else if (result.residual > 1e-5f) result.error = -205;
    return result;
}
#endif

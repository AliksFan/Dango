// Test-only joint shape/contact/friction solver candidate.
// This header is never included by the production DLL.
#ifndef DANGO_JOINT_CONTACT_SOLVER_H
#define DANGO_JOINT_CONTACT_SOLVER_H

#define DANGO_JOINT_MAX_RIGS 3
#define DANGO_JOINT_BODIES_PER_RIG 17
#define DANGO_JOINT_MAX_BODIES 51
#define DANGO_JOINT_MAX_CONTACTS 144
#define DANGO_JOINT_MAX_SINGLE_ROWS 384
#define DANGO_JOINT_MAX_CONTACT_ROWS 288
#define DANGO_JOINT_MAX_VELOCITY_ROWS 672
#define DANGO_JOINT_ERROR_INVALID_STATE -101
#define DANGO_JOINT_ERROR_POSITION_DID_NOT_CONVERGE -102

typedef struct dango_joint_state
{
    int body_count;
    dango_constraint_state rigs[DANGO_JOINT_MAX_RIGS];
    b2Vec2 previous[DANGO_JOINT_MAX_RIGS][DANGO_JOINT_BODIES_PER_RIG];
    float ground_normal_impulse[DANGO_JOINT_MAX_RIGS][DANGO_CONSTRAINT_NODES];
    float ground_tangent_impulse[DANGO_JOINT_MAX_RIGS][DANGO_CONSTRAINT_NODES];
    float ground_position_lambda[DANGO_JOINT_MAX_RIGS][DANGO_CONSTRAINT_NODES];
    float skin;
    float ground_friction;
    float inter_body_friction;
    float time_step;
} dango_joint_state;

// Optional read-only J2.5 observation of the unchanged historical J3 algorithm.
// phase 0: shared shape/ground sweep; 1: ground friction; 2: cross; 3: scan start.
typedef void (*dango_joint_observer)(const dango_joint_state*, int, int, int, bool);
static dango_joint_observer dango_joint_position_observer;

typedef struct dango_joint_solver_result
{
    bool succeeded;
    int error_code;
    int position_scans;
    int velocity_scans;
    unsigned long long position_row_visits;
    unsigned long long velocity_row_visits;
    unsigned long long contact_rebuilds;
    float maximum_self_residual;
    float maximum_contact_residual;
} dango_joint_solver_result;

static dango_joint_solver_result dango_joint_solver_not_implemented(void)
{
    dango_joint_solver_result result = {0};
    result.error_code = -1;
    return result;
}

static bool dango_joint_state_valid(const dango_joint_state* state)
{
    if (state == NULL || state->body_count < 1 || state->body_count > DANGO_JOINT_MAX_RIGS ||
        !isfinite(state->skin) || state->skin <= 0.0f ||
        !isfinite(state->ground_friction) || state->ground_friction < 0.0f ||
        !isfinite(state->inter_body_friction) || state->inter_body_friction < 0.0f ||
        !isfinite(state->time_step) || state->time_step <= 0.0f)
    {
        return false;
    }
    for (int rig = 0; rig < state->body_count; ++rig)
    {
        const dango_constraint_state* s = &state->rigs[rig];
        if (!isfinite(s->radial_length) || s->radial_length <= 0.0f ||
            !isfinite(s->perimeter_length) || s->perimeter_length <= 0.0f)
        {
            return false;
        }
        for (int body = 0; body < DANGO_JOINT_BODIES_PER_RIG; ++body)
        {
            if (!b2IsValidVec2(s->positions[body]) || !b2IsValidVec2(s->velocities[body]) ||
                !b2IsValidVec2(state->previous[rig][body]) ||
                !isfinite(s->inverse_mass[body]) || s->inverse_mass[body] <= 0.0f)
            {
                return false;
            }
        }
        for (int node = 0; node < DANGO_CONSTRAINT_NODES; ++node)
        {
            if (!isfinite(state->ground_normal_impulse[rig][node]) ||
                state->ground_normal_impulse[rig][node] < 0.0f ||
                !isfinite(state->ground_tangent_impulse[rig][node]))
            {
                return false;
            }
        }
    }
    return true;
}

static float dango_joint_node_edge_position(dango_joint_state* state,
    int point_rig, int point_node, int edge_rig, bool correct)
{
    dango_constraint_state* point_state = &state->rigs[point_rig];
    dango_constraint_state* edge_state = &state->rigs[edge_rig];
    b2Vec2 point = point_state->positions[point_node];
    bool inside = true;
    float best_distance = INFINITY;
    float best_t = 0.0f;
    int best_edge = -1;
    b2Vec2 best_point = {0.0f, 0.0f};
    b2Vec2 best_outward = {0.0f, 0.0f};
    for (int edge = 0; edge < DANGO_CONSTRAINT_NODES; ++edge)
    {
        b2Vec2 a = edge_state->positions[edge];
        b2Vec2 b = edge_state->positions[(edge + 1) % DANGO_CONSTRAINT_NODES];
        b2Vec2 segment = b2Sub(b, a);
        float length_squared = b2Dot(segment, segment);
        if (!isfinite(length_squared) || length_squared < 1e-10f)
        {
            return INFINITY;
        }
        if (b2Cross(segment, b2Sub(point, a)) < 0.0f)
        {
            inside = false;
        }
        float t = fmaxf(0.0f, fminf(1.0f,
            b2Dot(b2Sub(point, a), segment) / length_squared));
        b2Vec2 nearest = b2MulAdd(a, t, segment);
        float distance = b2Distance(point, nearest);
        if (distance < best_distance)
        {
            best_distance = distance;
            best_t = t;
            best_edge = edge;
            best_point = nearest;
            best_outward = b2MulSV(1.0f / sqrtf(length_squared),
                (b2Vec2){segment.y, -segment.x});
        }
    }
    if (best_edge < 0 || !isfinite(best_distance))
    {
        return INFINITY;
    }
    float error = state->skin - (inside ? -best_distance : best_distance);
    if (error <= 0.0f || !correct)
    {
        return fmaxf(0.0f, error);
    }
    b2Vec2 normal = best_distance > 1e-7f
        ? b2MulSV((inside ? -1.0f : 1.0f) / best_distance, b2Sub(point, best_point))
        : best_outward;
    int next = (best_edge + 1) % DANGO_CONSTRAINT_NODES;
    float weights[3] = {1.0f, -(1.0f - best_t), -best_t};
    float inverse_mass[3] = {
        point_state->inverse_mass[point_node],
        edge_state->inverse_mass[best_edge],
        edge_state->inverse_mass[next]
    };
    float denominator = 0.0f;
    for (int i = 0; i < 3; ++i)
    {
        denominator += inverse_mass[i] * weights[i] * weights[i];
    }
    if (!isfinite(denominator) || denominator < 0.000001f)
    {
        return INFINITY;
    }
    float lambda = fminf(error, 0.01f) / denominator;
    point_state->positions[point_node] = b2MulAdd(point_state->positions[point_node],
        lambda * inverse_mass[0] * weights[0], normal);
    edge_state->positions[best_edge] = b2MulAdd(edge_state->positions[best_edge],
        lambda * inverse_mass[1] * weights[1], normal);
    edge_state->positions[next] = b2MulAdd(edge_state->positions[next],
        lambda * inverse_mass[2] * weights[2], normal);
    return error;
}

static void dango_joint_position_residuals(dango_joint_state* state,
    float* maximum_self_residual, float* maximum_contact_residual,
    unsigned long long* contact_rebuilds)
{
    *maximum_self_residual = 0.0f;
    *maximum_contact_residual = 0.0f;
    for (int rig = 0; rig < state->body_count; ++rig)
    {
        *maximum_self_residual = fmaxf(*maximum_self_residual,
            dango_constraint_sweep_order_mode(&state->rigs[rig], false, false,
                DANGO_CONSTRAINT_CORRECT_POSITION));
    }
    for (int first = 0; first < state->body_count; ++first)
    {
        for (int second = first + 1; second < state->body_count; ++second)
        {
            for (int direction = 0; direction < 2; ++direction)
            {
                int point_rig = direction == 0 ? first : second;
                int edge_rig = direction == 0 ? second : first;
                for (int node = 0; node < DANGO_CONSTRAINT_NODES; ++node)
                {
                    ++*contact_rebuilds;
                    *maximum_contact_residual = fmaxf(*maximum_contact_residual,
                        dango_joint_node_edge_position(state, point_rig, node, edge_rig, false));
                }
            }
        }
    }
}

static dango_joint_solver_result dango_joint_solve_positions(dango_joint_state* state)
{
    dango_joint_solver_result result = {0};
    if (!dango_joint_state_valid(state))
    {
        result.error_code = DANGO_JOINT_ERROR_INVALID_STATE;
        return result;
    }
    for (int scan = 0; scan < 96; ++scan)
    {
        if (dango_joint_position_observer) dango_joint_position_observer(state, scan, 3, -1, true);
        bool reverse = (scan & 1) != 0;
        result.position_scans = scan + 1;
        for (int ordinal = 0; ordinal < state->body_count; ++ordinal)
        {
            int rig = reverse ? state->body_count - 1 - ordinal : ordinal;
            if (dango_joint_position_observer) dango_joint_position_observer(state, scan, 0, rig, true);
            float residual = dango_constraint_sweep_order_mode(&state->rigs[rig], true, reverse,
                DANGO_CONSTRAINT_CORRECT_POSITION);
            if (dango_joint_position_observer) dango_joint_position_observer(state, scan, 0, rig, false);
            result.position_row_visits += 80;
            if (!isfinite(residual))
            {
                result.error_code = DANGO_JOINT_ERROR_INVALID_STATE;
                return result;
            }
            if (dango_joint_position_observer) dango_joint_position_observer(state, scan, 1, rig, true);
            for (int node = 0; node < DANGO_CONSTRAINT_NODES; ++node)
            {
                if (state->rigs[rig].positions[node].y - state->rigs[rig].radius -
                    state->rigs[rig].ground <= 0.005f)
                {
                    ground_friction_project(&state->rigs[rig].positions[node].x,
                        state->previous[rig][node].x, state->rigs[rig].inverse_mass[node],
                        state->ground_normal_impulse[rig][node], state->ground_friction,
                        state->time_step, &state->ground_position_lambda[rig][node]);
                    ++result.position_row_visits;
                }
            }
            if (dango_joint_position_observer) dango_joint_position_observer(state, scan, 1, rig, false);
        }
        if (dango_joint_position_observer) dango_joint_position_observer(state, scan, 2, -1, true);
        for (int first_ordinal = 0; first_ordinal < state->body_count; ++first_ordinal)
        {
            int first = reverse ? state->body_count - 1 - first_ordinal : first_ordinal;
            for (int second_ordinal = first_ordinal + 1; second_ordinal < state->body_count; ++second_ordinal)
            {
                int second = reverse ? state->body_count - 1 - second_ordinal : second_ordinal;
                for (int direction = 0; direction < 2; ++direction)
                {
                    int point_rig = direction == 0 ? first : second;
                    int edge_rig = direction == 0 ? second : first;
                    for (int node_ordinal = 0; node_ordinal < DANGO_CONSTRAINT_NODES; ++node_ordinal)
                    {
                        int node = reverse ? DANGO_CONSTRAINT_NODES - 1 - node_ordinal : node_ordinal;
                        ++result.contact_rebuilds;
                        ++result.position_row_visits;
                        float residual = dango_joint_node_edge_position(state,
                            point_rig, node, edge_rig, true);
                        if (!isfinite(residual))
                        {
                            result.error_code = DANGO_JOINT_ERROR_INVALID_STATE;
                            return result;
                        }
                    }
                }
            }
        }
        if (dango_joint_position_observer) dango_joint_position_observer(state, scan, 2, -1, false);
        dango_joint_position_residuals(state, &result.maximum_self_residual,
            &result.maximum_contact_residual, &result.contact_rebuilds);
        if (!isfinite(result.maximum_self_residual) || !isfinite(result.maximum_contact_residual))
        {
            result.error_code = DANGO_JOINT_ERROR_INVALID_STATE;
            return result;
        }
        if (result.maximum_self_residual <= DANGO_CONSTRAINT_TOLERANCE &&
            result.maximum_contact_residual <= DANGO_CONSTRAINT_TOLERANCE)
        {
            result.succeeded = true;
            return result;
        }
    }
    result.error_code = DANGO_JOINT_ERROR_POSITION_DID_NOT_CONVERGE;
    return result;
}

#endif

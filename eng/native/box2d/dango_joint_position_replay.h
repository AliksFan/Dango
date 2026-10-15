// Isolated J3R candidate. Historical J3 remains available unchanged.
#ifndef DANGO_JOINT_POSITION_REPLAY_H
#define DANGO_JOINT_POSITION_REPLAY_H
#include "dango_joint_contact_diagnostics.h"
#include <time.h>

static inline bool joint_r_cross(b2Vec2 a, b2Vec2 b, b2Vec2 c, b2Vec2 d, float epsilon)
{
    return b2Cross(b2Sub(b, a), b2Sub(c, a)) * b2Cross(b2Sub(b, a), b2Sub(d, a)) < -epsilon &&
        b2Cross(b2Sub(d, c), b2Sub(a, c)) * b2Cross(b2Sub(d, c), b2Sub(b, c)) < -epsilon;
}

static inline bool joint_r_topology(const dango_joint_state* state, float minimum_area)
{
    if (!dango_joint_state_valid(state) || !isfinite(minimum_area) || minimum_area <= 0) return false;
    for (int r = 0; r < state->body_count; ++r)
    {
        const dango_constraint_state* s = &state->rigs[r];
        float area = 0, display_area = 0;
        for (int n = 0; n < 16; ++n)
        {
            int next = (n + 1) % 16;
            b2Vec2 a = s->positions[n], b = s->positions[next], core = s->positions[16];
            if (b2Distance(a, core) < 1e-5f) return false;
            area += .5f * b2Cross(b2Sub(a, core), b2Sub(b, core));
            display_area += .5f * b2Cross(b2Sub(dango_constraint_display_point(s, n), core),
                b2Sub(dango_constraint_display_point(s, next), core));
            for (int j = n + 2; j < 16; ++j)
                if ((j + 1) % 16 != n && joint_r_cross(a, b, s->positions[j], s->positions[(j + 1) % 16], 0)) return false;
            for (int t = 0; t < state->body_count; ++t)
            {
                if (t == r) continue;
                const dango_constraint_state* other = &state->rigs[t];
                // Same authored threshold as the existing stack_geometry gate.
                if (b2Distance(a, other->positions[16]) < .17f) return false;
                for (int j = 0; j < 16; ++j)
                    if (joint_r_cross(a, b, other->positions[j], other->positions[(j + 1) % 16], 1e-10f)) return false;
            }
        }
        if (!isfinite(area) || area <= 0 || !isfinite(display_area) || display_area < minimum_area) return false;
    }
    return true;
}

static inline bool joint_r_final(const dango_joint_state* state, float minimum_area,
    joint_residual_report* report, joint_contact_snapshot* contacts)
{
    if (joint_build_contacts(state, contacts) != 0 || joint_measure(state, contacts, report) != 0) return false;
    for (int k = 0; k < 5; ++k) if (report->maximum[k] > DANGO_CONSTRAINT_TOLERANCE) return false;
    return report->maximum[6] <= DANGO_CONSTRAINT_TOLERANCE && joint_r_topology(state, minimum_area);
}

static inline int joint_r_project(dango_joint_state* state, const joint_contact_row* row, float* move)
{
    if (!state || !row || !move) return DANGO_JOINT_ERROR_INVALID_STATE;
    *move = 0;
    joint_contact_row check = *row;
    int error = joint_normalize(&check);
    if (error) return error;
    if (!joint_key_equal(check.key, row->key) || row->key.kind == JOINT_GROUND ||
        row->key.point_rig >= state->body_count || row->key.target_rig >= state->body_count)
        return DANGO_JOINT_ERROR_CONTACT_INDEX;
    const joint_contact_key* key = &row->key;
    dango_constraint_state* point = &state->rigs[key->point_rig];
    dango_constraint_state* target = &state->rigs[key->target_rig];
    b2Vec2 p = point->positions[key->point_node], a = target->positions[key->feature];
    int next = (key->feature + 1) % 16;
    b2Vec2 edge = b2Sub(target->positions[next], a);
    float length2 = b2LengthSquared(edge);
    if (!b2IsValidVec2(p) || !b2IsValidVec2(a) || !isfinite(length2) || length2 < 1e-10f)
        return DANGO_JOINT_ERROR_CONTACT_GEOMETRY;
    float t = key->kind == JOINT_VERTEX ? 0 : fmaxf(0, fminf(1, b2Dot(b2Sub(p, a), edge) / length2));
    b2Vec2 q = b2MulAdd(a, t, edge);
    float distance = b2Distance(p, q);
    bool inside = true;
    for (int n = 0; n < 16; ++n)
        if (b2Cross(b2Sub(target->positions[(n + 1) % 16], target->positions[n]), b2Sub(p, target->positions[n])) < 0) inside = false;
    float residual = state->skin - (inside ? -distance : distance);
    if (!isfinite(residual)) return DANGO_JOINT_ERROR_CONTACT_GEOMETRY;
    if (residual <= 0) return 0;
    b2Vec2 normal = distance > 1e-7f ? b2MulSV((inside ? -1.0f : 1.0f) / distance, b2Sub(p, q)) :
        b2MulSV(1 / sqrtf(length2), (b2Vec2){edge.y, -edge.x});
    float weights[3] = {1, -(1 - t), -t};
    float masses[3] = {point->inverse_mass[key->point_node], target->inverse_mass[key->feature], target->inverse_mass[next]};
    b2Vec2* positions[3] = {&point->positions[key->point_node], &target->positions[key->feature], &target->positions[next]};
    float denominator = 0;
    for (int i = 0; i < 3; ++i) denominator += masses[i] * weights[i] * weights[i];
    if (!isfinite(denominator) || denominator < 1e-6f) return DANGO_JOINT_ERROR_CONTACT_GEOMETRY;
    float lambda = fminf(residual, .01f) / denominator;
    for (int i = 0; i < 3; ++i)
    {
        b2Vec2 previous = *positions[i];
        *positions[i] = b2MulAdd(previous, lambda * masses[i] * weights[i], normal);
        *move = fmaxf(*move, b2Distance(previous, *positions[i]));
    }
    return 0;
}

typedef struct joint_r_result
{
    bool success, topology;
    int error, scans, peak_contacts;
    unsigned long long visits[7], generation, final_checks, diagnostic;
    float maximum_row_move;
    joint_residual_report residual;
    double milliseconds;
} joint_r_result;

static inline void joint_r_log_group(FILE* log, int scan, int phase, int rig,
    const dango_joint_state* before, const dango_joint_state* after,
    const unsigned long long visits[7], joint_r_result* result)
{
    if (!log) return;
    joint_group_observation report;
    int error = joint_observe_group(before, after, visits, &report);
    if (error) { result->error = error; return; }
    result->diagnostic += report.diagnostic_rebuilds;
    fprintf(log, "group,%d,phase,%d,rig,%d,max_group_displacement,%.9g,added,%d,removed,%d,switches,%d\n",
        scan, phase, rig, (double)report.maximum_move, report.changes.added, report.changes.removed, report.changes.switches);
    for (int k = 0; k < 7; ++k)
        fprintf(log, "residual,%d,phase,%d,rig,%d,kind,%d,before,%.9g,after,%.9g,worst_rig,%d,worst_node,%d,visits,%llu\n",
            scan, phase, rig, k, (double)report.before.maximum[k], (double)report.after.maximum[k],
            report.after.rig[k], report.after.node[k], visits[k]);
}

static inline void joint_r_track_move(const b2Vec2 before[17], const dango_constraint_state* after, joint_r_result* result)
{
    for (int n = 0; n < 17; ++n) result->maximum_row_move = fmaxf(result->maximum_row_move, b2Distance(before[n], after->positions[n]));
}

static joint_r_result joint_r_solve(dango_joint_state* state, float minimum_area, FILE* log)
{
    joint_r_result result = {0};
    clock_t start = clock();
    joint_contact_snapshot previous = {0};
    for (int scan = 0; scan < 96; ++scan)
    {
        joint_contact_snapshot contacts;
        int error = joint_build_contacts(state, &contacts);
        if (error) { result.error = error; break; }
        result.generation += contacts.candidates;
        result.scans = scan + 1;
        if (contacts.registry.count > result.peak_contacts) result.peak_contacts = contacts.registry.count;
        joint_contact_changes changes = joint_compare_contacts(&previous, &contacts);
        if (log) fprintf(log, "scan,%d,candidates,%d,active,%d,unique,%d,duplicate,%d,added,%d,removed,%d,switches,%d\n",
            scan, contacts.candidates, contacts.active_cross, contacts.registry.count, contacts.registry.duplicates,
            changes.added, changes.removed, changes.switches);
        previous = contacts;
        bool reverse = (scan & 1) != 0;
        for (int ordinal = 0; ordinal < state->body_count; ++ordinal)
        {
            int r = reverse ? state->body_count - 1 - ordinal : ordinal;
            dango_constraint_state* s = &state->rigs[r];
            dango_joint_state before = *state;
            unsigned long long visits[7] = {16, 16, 16, 16, 16, 0, 0};
            // Exactly the legacy primitive order and formulas; no inner solve loop.
            for (int i = 0; i < 16; ++i)
            {
                int n = reverse ? 15 - i : i;
                for (int k = 0; k < 4; ++k)
                {
                    b2Vec2 saved[17]; memcpy(saved, s->positions, sizeof saved);
                    float residual;
                    if (k == 0) residual = dango_constraint_distance_mode(s, 16, n, s->radial_length * s->radial_min,
                        s->radial_length * s->radial_max, true, DANGO_CONSTRAINT_CORRECT_POSITION);
                    else if (k == 1) residual = dango_constraint_distance_mode(s, n, (n + 1) % 16,
                        s->perimeter_length * s->perimeter_min, s->perimeter_length * s->perimeter_max, true, DANGO_CONSTRAINT_CORRECT_POSITION);
                    else residual = dango_constraint_angle_mode(s, n, k == 3, true, DANGO_CONSTRAINT_CORRECT_POSITION);
                    ++result.visits[k];
                    joint_r_track_move(saved, s, &result);
                    if (!isfinite(residual)) result.error = DANGO_JOINT_ERROR_CONTACT_GEOMETRY;
                }
            }
            for (int n = 0; n < 16; ++n)
            {
                float penetration = fmaxf(0, s->ground + s->radius - s->positions[n].y);
                s->positions[n].y += penetration;
                result.maximum_row_move = fmaxf(result.maximum_row_move, penetration);
                ++result.visits[4];
            }
            joint_r_log_group(log, scan, 0, r, &before, state, visits, &result);
            before = *state; memset(visits, 0, sizeof visits);
            for (int n = 0; n < 16; ++n)
            {
                if (s->positions[n].y - s->radius - s->ground > .005f) continue;
                float x = s->positions[n].x;
                ground_friction_project(&s->positions[n].x, state->previous[r][n].x, s->inverse_mass[n],
                    state->ground_normal_impulse[r][n], state->ground_friction, state->time_step, &state->ground_position_lambda[r][n]);
                ++visits[5]; ++result.visits[5];
                result.maximum_row_move = fmaxf(result.maximum_row_move, fabsf(s->positions[n].x - x));
            }
            joint_r_log_group(log, scan, 1, r, &before, state, visits, &result);
        }
        if (result.error) break;
        dango_joint_state before = *state;
        unsigned long long visits[7] = {0};
        bool visited[144] = {false};
        // Preserve legacy pair/direction/node order; execute the first canonical occurrence once.
        for (int a = 0; a < state->body_count; ++a) for (int b = a + 1; b < state->body_count; ++b)
            for (int direction = 0; direction < 2; ++direction) for (int i = 0; i < 16; ++i)
            {
                int first = reverse ? state->body_count - 1 - a : a;
                int second = reverse ? state->body_count - 1 - b : b;
                int point = direction ? second : first, target = direction ? first : second;
                int node = reverse ? 15 - i : i;
                joint_contact_key key = contacts.source_keys[point][node][target];
                for (int j = 0; j < contacts.registry.count; ++j)
                {
                    if (visited[j] || !joint_key_equal(key, contacts.registry.rows[j].key)) continue;
                    visited[j] = true; ++visits[6]; ++result.visits[6];
                    float move;
                    error = joint_r_project(state, &contacts.registry.rows[j], &move);
                    if (error) result.error = error;
                    result.maximum_row_move = fmaxf(result.maximum_row_move, move);
                }
            }
        joint_r_log_group(log, scan, 2, -1, &before, state, visits, &result);
        if (result.error) break;
        joint_contact_snapshot final;
        result.success = joint_r_final(state, minimum_area, &result.residual, &final);
        result.final_checks += (unsigned long long)state->body_count * (state->body_count - 1) * 16;
        result.topology = joint_r_topology(state, minimum_area);
        if (log) fprintf(log, "final,%d,self,%.9g,cross,%.9g,topology,%d,success,%d,max_row_move,%.9g\n", scan,
            (double)fmaxf(fmaxf(result.residual.maximum[0], result.residual.maximum[1]), fmaxf(result.residual.maximum[2],
                fmaxf(result.residual.maximum[3], result.residual.maximum[4]))),
            (double)result.residual.maximum[6], result.topology, result.success, (double)result.maximum_row_move);
        if (!result.topology) { result.error = -108; break; }
        if (result.success) break;
    }
    if (!result.success && !result.error) result.error = DANGO_JOINT_ERROR_POSITION_DID_NOT_CONVERGE;
    result.milliseconds = 1000.0 * (clock() - start) / CLOCKS_PER_SEC;
    return result;
}
#endif

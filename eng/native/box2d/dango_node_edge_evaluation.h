// Evaluation only. Not included in the production DLL.
#ifndef DANGO_NODE_EDGE_EVALUATION_H
#define DANGO_NODE_EDGE_EVALUATION_H

static int evaluation_projection_count;
static float evaluation_max_momentum_error;
static float evaluation_max_energy_gain;
static float evaluation_contact_mu;
static float evaluation_position_relaxation=1;
static float evaluation_max_friction_budget_error;
static int evaluation_friction_calls,evaluation_friction_saturated;
static double evaluation_tangent_speed_before,evaluation_tangent_speed_after;
#if defined(DANGO_JOINT_BASELINE_COUNTERS)
static unsigned long long evaluation_contact_rebuild_count;
#endif
#include "dango_contact_friction_evaluation.h"

static float evaluate_node_edge(dango_self_soft_body* point_body, int point_index,
    dango_self_soft_body* edge_body, float skin, bool correct)
{
#if defined(DANGO_JOINT_BASELINE_COUNTERS)
    ++evaluation_contact_rebuild_count;
#endif
    b2BodyId point_id = point_body->node_body_ids[point_index];
    b2Vec2 p = b2Body_GetPosition(point_id);
    bool inside = true;
    float best_distance = INFINITY, best_t = 0;
    int best_edge = 0;
    b2Vec2 best_q = {0}, best_outward = {0};
    for (int j = 0; j < 16; ++j)
    {
        b2Vec2 a = b2Body_GetPosition(edge_body->node_body_ids[j]);
        b2Vec2 b = b2Body_GetPosition(edge_body->node_body_ids[(j + 1) % 16]);
        b2Vec2 e = b2Sub(b, a);
        float length2 = b2Dot(e, e);
        if (length2 < 1e-10f) return INFINITY;
        if (b2Cross(e, b2Sub(p, a)) < 0) inside = false;
        float t = fmaxf(0, fminf(1, b2Dot(b2Sub(p, a), e) / length2));
        b2Vec2 q = b2MulAdd(a, t, e);
        float distance = b2Distance(p, q);
        if (distance < best_distance)
        {
            best_distance = distance;
            best_t = t;
            best_edge = j;
            best_q = q;
            best_outward = b2MulSV(1 / sqrtf(length2), (b2Vec2){e.y, -e.x});
        }
    }
    float error = skin - (inside ? -best_distance : best_distance);
    if (error <= 0 || !correct) return fmaxf(0, error);
    b2Vec2 normal = best_distance > 1e-7f
        ? b2MulSV((inside ? -1.0f : 1.0f) / best_distance, b2Sub(p, best_q))
        : best_outward;
    b2BodyId ids[3] = {point_id, edge_body->node_body_ids[best_edge],
        edge_body->node_body_ids[(best_edge + 1) % 16]};
    float weights[3] = {1, -(1-best_t), -best_t};
    float inverse_mass[3], denominator = 0, speed = 0;
    for (int i = 0; i < 3; ++i)
    {
        inverse_mass[i] = 1 / b2Body_GetMass(ids[i]);
        denominator += inverse_mass[i] * weights[i] * weights[i];
        speed += weights[i] * b2Dot(b2Body_GetLinearVelocity(ids[i]), normal);
    }
    float correction_error=evaluation_position_relaxation==1 ? error : error*evaluation_position_relaxation;
    float lambda = fminf(correction_error, 0.01f) / denominator;
    float impulse = fmaxf(0, -speed) / denominator;
    b2Vec2 momentum_before = {0}, momentum_after = {0};
    float energy_before = 0, energy_after = 0;
    for (int i = 0; i < 3; ++i)
    {
        b2Vec2 velocity = b2Body_GetLinearVelocity(ids[i]);
        momentum_before = b2MulAdd(momentum_before, 1 / inverse_mass[i], velocity);
        energy_before += 0.5f / inverse_mass[i] * b2Dot(velocity, velocity);
    }
    for (int i = 0; i < 3; ++i)
    {
        b2Body_SetTransform(ids[i], b2MulAdd(b2Body_GetPosition(ids[i]),
            lambda * inverse_mass[i] * weights[i], normal), b2Rot_identity);
        b2Body_SetLinearVelocity(ids[i], b2MulAdd(b2Body_GetLinearVelocity(ids[i]),
            impulse * inverse_mass[i] * weights[i], normal));
    }
    if (evaluation_contact_mu>0 && impulse>0)
    {
        b2Vec2 velocities[3];
        for (int i=0;i<3;++i) velocities[i]=b2Body_GetLinearVelocity(ids[i]);
        b2Vec2 tangent={-normal.y,normal.x};
        float slip_before=0,slip_after=0;
        for (int i=0;i<3;++i) slip_before+=weights[i]*b2Dot(velocities[i],tangent);
        float tangent_impulse=contact_friction_apply(velocities,inverse_mass,weights,
            tangent,impulse,evaluation_contact_mu);
        for (int i=0;i<3;++i) slip_after+=weights[i]*b2Dot(velocities[i],tangent);
        ++evaluation_friction_calls;
        if (fabsf(slip_before)/denominator>evaluation_contact_mu*impulse) ++evaluation_friction_saturated;
        evaluation_tangent_speed_before+=fabsf(slip_before);
        evaluation_tangent_speed_after+=fabsf(slip_after);
        evaluation_max_friction_budget_error=fmaxf(evaluation_max_friction_budget_error,
            fabsf(tangent_impulse)-evaluation_contact_mu*impulse);
        for (int i=0;i<3;++i) b2Body_SetLinearVelocity(ids[i],velocities[i]);
    }
    for (int i=0;i<3;++i)
    {
        b2Vec2 velocity = b2Body_GetLinearVelocity(ids[i]);
        momentum_after = b2MulAdd(momentum_after, 1 / inverse_mass[i], velocity);
        energy_after += 0.5f / inverse_mass[i] * b2Dot(velocity, velocity);
    }
    evaluation_projection_count++;
    evaluation_max_momentum_error = fmaxf(evaluation_max_momentum_error, b2Distance(momentum_before, momentum_after));
    evaluation_max_energy_gain = fmaxf(evaluation_max_energy_gain, energy_after-energy_before);
    return error;
}

static bool evaluate_continuous_pair(dango_self_soft_body** bodies,
    const dango_self_physics_config* config, float skin)
{
    for (int iteration = 0; iteration < 96; ++iteration)
    {
        for (int k = 0; k < 2; ++k)
            for (int i = 0; i < 16; ++i)
                evaluate_node_edge(bodies[k], i, bodies[1-k], skin, true);
        for (int k = 0; k < 2; ++k)
            if (!dango_self_solve_constraints(bodies[k], config)) return false;
        float residual = 0;
        for (int k = 0; k < 2; ++k)
            for (int i = 0; i < 16; ++i)
                residual = fmaxf(residual, evaluate_node_edge(bodies[k], i, bodies[1-k], skin, false));
        if (residual <= 1e-5f) return true;
    }
    return false;
}
#endif

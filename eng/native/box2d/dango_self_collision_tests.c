// Native-only feasibility gate: same self rig, shared world, no desktop replacement.
#include <stdio.h>
#include <stdlib.h>
#include "dango_box2d_bridge.c"
#include "dango_node_edge_evaluation.h"

static bool segments_cross(b2Vec2 a, b2Vec2 b, b2Vec2 c, b2Vec2 d)
{
    float ab_c = b2Cross(b2Sub(b, a), b2Sub(c, a));
    float ab_d = b2Cross(b2Sub(b, a), b2Sub(d, a));
    float cd_a = b2Cross(b2Sub(d, c), b2Sub(a, c));
    float cd_b = b2Cross(b2Sub(d, c), b2Sub(b, c));
    return ab_c * ab_d < -1e-10f && cd_a * cd_b < -1e-10f;
}

static int probe(float radius, float offset, bool continuous, int frames, int disable_after, bool balance_probe, float origin_shift)
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

    config.angle_strength = 10;
    config.drag_stiffness = 10;
    config.drag_maximum_force = 8;
    config.top_inward_force = 0.5f;
    int baseline = b2GetByteCount();
    void* first = NULL;
    if (dango_box2d_self_soft_body_create(&config, &first) != 0) return 2;
    dango_self_soft_body* lower = first;
    dango_self_soft_body upper = {0};
    upper.world_id = lower->world_id;
    upper.ground_body_id = lower->ground_body_id;
    upper.ground_shape_id = lower->ground_shape_id;
    dango_self_create_rig(&upper, &config);
    dango_self_soft_body* bodies[2] = {lower, &upper};
    for (int k = 0; k < 2; ++k)
    {
        dango_self_soft_body* body = bodies[k];
        for (int i = 0; i <= 16; ++i)
        {
            b2BodyId id = i == 16 ? body->core_body_id : body->node_body_ids[i];
            b2Vec2 p = b2Body_GetPosition(id);
            p.x += k == 1 ? offset : 0;
            p.y += k == 1 ? 1.65f : 0;
            b2Body_SetTransform(id, p, b2Rot_identity);
            if (i < 16 && !continuous)
            {
                b2ShapeDef shape = b2DefaultShapeDef();
                shape.density = 0;
                shape.material.friction = config.friction;
                shape.material.restitution = config.restitution;
                shape.filter.categoryBits = 8;
                shape.filter.maskBits = 8;
                shape.filter.groupIndex = -(k + 1);
                b2Circle circle = {{0, 0}, radius};
                b2CreateCircleShape(id, &shape, &circle);
                if (fabsf(b2Body_GetMass(id) - config.node_mass) > 1e-6f) return 3;
            }
        }
    }
    int failure = 0;
    int first_crossing = -1;
    float nearest_core = 100;
    b2Vec2 rest_min[2][17], rest_max[2][17];
    b2Vec2 local_min[2][16], local_max[2][16];
    int last_contact_frame = -1;
    double step_dx[2] = {0}, correction_dx[2] = {0};
    double area_force_x_sum[2] = {0};
    for (int frame = 0; frame < frames && failure == 0; ++frame)
    {
        if (frame == 600 && origin_shift != 0)
            for (int k = 0; k < 2; ++k)
                for (int i = 0; i <= 16; ++i)
                {
                    b2BodyId id = i == 16 ? bodies[k]->core_body_id : bodies[k]->node_body_ids[i];
                    b2Vec2 p = b2Body_GetPosition(id);
                    p.x += origin_shift;
                    b2Body_SetTransform(id, p, b2Rot_identity);
                }
        for (int sub = 0; sub < 4 && failure == 0; ++sub)
        {
            bool solve_contacts = continuous && (disable_after < 0 || frame < disable_after);
            int projections_before = evaluation_projection_count;
            float before_x[2], stepped_x[2];
            for (int k = 0; k < 2; ++k) before_x[k] = b2Body_GetPosition(bodies[k]->core_body_id).x;
            for (int k = 0; k < 2; ++k)
            {
                dango_self_apply_internal_forces(bodies[k], &config);
                float area = dango_self_signed_area(bodies[k]);
                float pressure = fminf(config.repulsion_maximum_force,
                    config.area_strength * fmaxf(0, bodies[k]->target_area-area) / bodies[k]->target_area);
                float force_x = 0;
                b2Vec2 core = b2Body_GetPosition(bodies[k]->core_body_id);
                for (int i = 0; i < 16; ++i)
                {
                    b2Vec2 d = b2Sub(b2Body_GetPosition(bodies[k]->node_body_ids[i]), core);
                    force_x += pressure * d.x / b2Length(d);
                }
                if (frame >= frames - 600) area_force_x_sum[k] += force_x;
                // Diagnostic intervention only: isolate horizontal internal-force imbalance.
                if (balance_probe && frame >= 600)
                    b2Body_ApplyForceToCenter(bodies[k]->core_body_id, (b2Vec2){-force_x, 0}, true);
            }
            b2World_Step(lower->world_id, 1.0f / 240.0f, 1);
            for (int k = 0; k < 2; ++k) stepped_x[k] = b2Body_GetPosition(bodies[k]->core_body_id).x;
            if (solve_contacts && !evaluate_continuous_pair(bodies, &config, radius))
            {
                printf("FAIL continuous solver frame=%d sub=%d\n", frame, sub);
                failure = 7;
            }
            if (evaluation_projection_count != projections_before) last_contact_frame = frame;
            for (int k = 0; k < 2 && !solve_contacts; ++k)
            {
                if (!dango_self_solve_constraints(bodies[k], &config))
                {
                    printf("FAIL radius=%.3f offset=%.3f frame=%d sub=%d constraint body=%d\n",
                        (double)radius, (double)offset, frame, sub, k);
                    failure = 4;
                    break;
                }
            }
            if (frame >= frames - 600)
                for (int k = 0; k < 2; ++k)
                {
                    step_dx[k] += (double)stepped_x[k] - before_x[k];
                    correction_dx[k] += (double)b2Body_GetPosition(bodies[k]->core_body_id).x - stepped_x[k];
                }
            float maximum = 0;
            for (int k = 0; k < 2; ++k)
                for (int i = 0; i <= 16; ++i)
                    maximum = fmaxf(maximum, b2Length(b2Body_GetLinearVelocity(
                        i == 16 ? bodies[k]->core_body_id : bodies[k]->node_body_ids[i])));
            if (maximum > 12)
                for (int k = 0; k < 2; ++k)
                    for (int i = 0; i <= 16; ++i)
                    {
                        b2BodyId id = i == 16 ? bodies[k]->core_body_id : bodies[k]->node_body_ids[i];
                        b2Body_SetLinearVelocity(id, b2MulSV(11.99988f / maximum, b2Body_GetLinearVelocity(id)));
                    }
            for (int i = 0; i < 16 && failure == 0; ++i)
                for (int j = 0; j < 16; ++j)
                    if (segments_cross(
                        b2Body_GetPosition(lower->node_body_ids[i]),
                        b2Body_GetPosition(lower->node_body_ids[(i + 1) % 16]),
                        b2Body_GetPosition(upper.node_body_ids[j]),
                        b2Body_GetPosition(upper.node_body_ids[(j + 1) % 16])))
                    {
                        if (first_crossing < 0)
                        {
                            printf("CROSS radius=%.3f offset=%.3f frame=%d sub=%d edges=%d,%d\n",
                                (double)radius, (double)offset, frame, sub, i, j);
                            first_crossing = frame;
                        }
                        break;
                    }
            for (int k = 0; k < 2; ++k)
                for (int i = 0; i < 16; ++i)
                {
                    float distance = b2Distance(b2Body_GetPosition(bodies[k]->node_body_ids[i]),
                        b2Body_GetPosition(bodies[1-k]->core_body_id));
                    nearest_core = fminf(nearest_core, distance);
                    if (distance < config.radial_rest_length * config.core_exclusion_ratio)
                    {
                        printf("FAIL radius=%.3f offset=%.3f frame=%d sub=%d node=%d body=%d entered opposite core zone distance=%.9g\n",
                            (double)radius, (double)offset, frame, sub, i, k, (double)distance);
                        failure = 5;
                        break;
                    }
                }
        }
        if (frame >= frames - 600)
            for (int k = 0; k < 2; ++k)
                for (int i = 0; i <= 16; ++i)
                {
                    b2Vec2 p = b2Body_GetPosition(i == 16 ? bodies[k]->core_body_id : bodies[k]->node_body_ids[i]);
                    if (frame == frames - 600) rest_min[k][i] = rest_max[k][i] = p;
                    rest_min[k][i] = b2Min(rest_min[k][i], p);
                    rest_max[k][i] = b2Max(rest_max[k][i], p);
                    if (i < 16)
                    {
                        b2Vec2 local = b2Sub(p, b2Body_GetPosition(bodies[k]->core_body_id));
                        if (frame == frames - 600) local_min[k][i] = local_max[k][i] = local;
                        local_min[k][i] = b2Min(local_min[k][i], local);
                        local_max[k][i] = b2Max(local_max[k][i], local);
                    }
                }
        if (failure != 0)
            for (int k = 0; k < 2; ++k)
                for (int i = 0; i <= 16; ++i)
                {
                    b2Vec2 p = b2Body_GetPosition(i == 16 ? bodies[k]->core_body_id : bodies[k]->node_body_ids[i]);
                    printf("body=%d node=%d x=%.9g y=%.9g\n", k, i, (double)p.x, (double)p.y);
                }
    }
    if (failure == 0 && first_crossing >= 0)
    {
        printf("FAIL: perimeter crossing first frame=%d; minimum opposite core distance=%.9g\n",
            first_crossing, (double)nearest_core);
        failure = 5;
    }
    if (failure == 0)
    {
        float drift = 0;
        for (int k = 0; k < 2; ++k)
            for (int i = 0; i <= 16; ++i)
                drift = fmaxf(drift, b2Distance(rest_min[k][i], rest_max[k][i]));
        printf("MEASURE drift=%.9g projections=%d momentum_error=%.9g energy_gain=%.9g\n",
            (double)drift, evaluation_projection_count, (double)evaluation_max_momentum_error,
            (double)evaluation_max_energy_gain);
        printf("LAST_CONTACT frame=%d disable_after=%d\n", last_contact_frame, disable_after);
        for (int k = 0; k < 2; ++k)
        {
            float local_drift = 0;
            for (int i = 0; i < 16; ++i)
                local_drift = fmaxf(local_drift, b2Distance(local_min[k][i], local_max[k][i]));
            printf("BODY %d core_dx=%.9g core_dy=%.9g local_drift=%.9g core_x=%.9g\n", k,
                (double)(rest_max[k][16].x-rest_min[k][16].x),
                (double)(rest_max[k][16].y-rest_min[k][16].y), (double)local_drift,
                (double)b2Body_GetPosition(bodies[k]->core_body_id).x);
            printf("DRIFT_SOURCE body=%d world_step_dx=%.9g self_correction_dx=%.9g\n", k, step_dx[k], correction_dx[k]);
            printf("AREA_FORCE body=%d mean_x=%.9g balance_probe=%d\n", k, area_force_x_sum[k]/2400, balance_probe);
        }
        if (continuous && ((fabsf(offset) < 2 && evaluation_projection_count == 0) || evaluation_max_momentum_error > 1e-5f ||
            evaluation_max_energy_gain > 1e-5f || (frames >= 36000 && drift >= 0.003f))) failure = 8;
    }
    if (failure == 0)
        printf("PASS radius=%.3f offset=%.3f frames=%d continuous=%d cores_y=%.6f,%.6f\n",
            (double)radius, (double)offset, frames, continuous,
            (double)b2Body_GetPosition(lower->core_body_id).y,
            (double)b2Body_GetPosition(upper.core_body_id).y);
    int bytes = -1;
    dango_box2d_self_soft_body_destroy(first, &bytes);
    if (b2GetByteCount() != baseline) return 6;
    return failure;
}

int main(int argc, char** argv)
{
    float radius = argc > 1 ? (float)atof(argv[1]) : 0.045f;
    float offset = argc > 2 ? (float)atof(argv[2]) : 0.15f;
    bool continuous = argc > 3 && atoi(argv[3]) != 0;
    int frames = argc > 4 ? atoi(argv[4]) : 1200;
    int disable_after = argc > 5 ? atoi(argv[5]) : -1;
    if (frames < 600 || !isfinite(radius) || radius <= 0 || !isfinite(offset)) return 9;
    bool balance_probe = argc > 6 && atoi(argv[6]) != 0;
    float origin_shift = argc > 7 ? (float)atof(argv[7]) : 0;
    return probe(radius, offset, continuous, frames, disable_after, balance_probe, origin_shift);
}

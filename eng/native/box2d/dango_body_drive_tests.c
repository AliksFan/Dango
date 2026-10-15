// Force-capture contract: real Box2D masses/positions, no physical world steps.
#include <math.h>
#include <stdio.h>
#include <string.h>
#include "box2d/box2d.h"
#include "dango_self_physics_internal.h"

static b2BodyId ids[17];
static b2Vec2 captured[17];
static int calls[17], capture_error;
static void drive_capture(b2BodyId id, b2Vec2 force, bool wake)
{
    for (int i = 0; i < 17; ++i)
        if (memcmp(&id, &ids[i], sizeof(id)) == 0)
        { captured[i] = force; ++calls[i]; if (!wake) capture_error = 1; return; }
    capture_error = 1;
}
#define b2Body_ApplyForceToCenter drive_capture
#include "dango_body_drive.h"
#undef b2Body_ApplyForceToCenter

#define CHECK(c) do { if (!(c)) { fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #c); return 1; } } while (0)

static bool force_contract(float requested, int count)
{
    double mass = 0, cx = 0, cy = 0;
    for (int i = 0; i < count; ++i)
    {
        double m = b2Body_GetMass(ids[i]); b2Vec2 p = b2Body_GetPosition(ids[i]);
        mass += m; cx += m * p.x; cy += m * p.y;
    }
    cx /= mass; cy /= mass;
    double fx = 0, fy = 0, torque = 0, radius = 0;
    for (int i = 0; i < count; ++i)
    {
        b2Vec2 p = b2Body_GetPosition(ids[i]);
        double x = p.x - cx, y = p.y - cy, m = b2Body_GetMass(ids[i]);
        fx += captured[i].x; fy += captured[i].y;
        torque += x * captured[i].y - y * captured[i].x;
        radius = fmax(radius, hypot(x, y));
        if (fabs(captured[i].x / m - requested / mass) > 1e-5 * fmax(1, fabs(requested / mass))) return false;
        if (calls[i] != 1 || captured[i].y != 0) return false;
    }
    double scale = fmax(1e-12, fabs(requested));
    return fabs(fx - requested) / scale <= 1e-5 && fabs(fy) / scale <= 1e-5 &&
        fabs(torque) / (scale * fmax(radius, 1e-12)) <= 1e-5 && !capture_error;
}

int main(void)
{
    int fixtures = 0, rejected_errors = 0;
    const float controls[] = {-2.4f, -.84f, .84f, 2.4f};
    for (int size = 0; size < 3; ++size)
    {
        float scale = size == 0 ? .8f : size == 1 ? 1.0f : 1.2f;
        b2WorldDef world_def = b2DefaultWorldDef(); world_def.gravity = (b2Vec2){0, 0};
        b2WorldId world = b2CreateWorld(&world_def);
        dango_self_soft_body body = {0}; body.node_count = 16;
        for (int i = 0; i < 17; ++i)
        {
            double angle = i * .39;
            b2BodyDef def = b2DefaultBodyDef(); def.type = b2_dynamicBody;
            // Irregular and translated: core, node average and true COM differ.
            def.position = (b2Vec2){(float)(3 + scale * cos(angle) * (i == 0 ? .2 : .7)),
                (float)(-4 + scale * sin(angle) * (i == 0 ? .1 : .5))};
            ids[i] = b2CreateBody(world, &def);
            b2ShapeDef shape = b2DefaultShapeDef();
            float mass = scale * scale * (i == 0 ? .11f : .013f * (1 + .03f * (float)i));
            shape.density = mass / (.01f * 3.14159265358979323846f);
            b2Circle circle = {{0, 0}, .1f}; b2CreateCircleShape(ids[i], &shape, &circle);
            if (i == 0) body.core_body_id = ids[i]; else body.node_body_ids[i - 1] = ids[i];
        }
        for (int c = 0; c < 4; ++c)
        {
            memset(calls, 0, sizeof calls); memset(captured, 0, sizeof captured); capture_error = 0;
            CHECK(dango_body_apply_mass_drive(&body, controls[c]) == 0);
            CHECK(force_contract(controls[c], 17)); ++fixtures;
            // The oracle must reject force doubling, omitted core and wrong mass weights.
            b2Vec2 saved[17]; memcpy(saved, captured, sizeof saved);
            for (int i = 0; i < 17; ++i) captured[i].x *= 2;
            CHECK(!force_contract(controls[c], 17)); ++rejected_errors;
            memcpy(captured, saved, sizeof saved); captured[0].x = 0;
            CHECK(!force_contract(controls[c], 17)); ++rejected_errors;
            memcpy(captured, saved, sizeof saved);
            for (int i = 0; i < 17; ++i) captured[i].x = controls[c] / 17;
            CHECK(!force_contract(controls[c], 17)); ++rejected_errors;
        }
        memset(calls, 0, sizeof calls);
        CHECK(dango_body_apply_mass_drive(&body, 0) == 0);
        for (int i = 0; i < 17; ++i) CHECK(calls[i] == 0);
        CHECK(dango_body_apply_mass_drive(&body, NAN) == -497);
        body.node_count = 0; CHECK(dango_body_apply_mass_drive(&body, 1) == -497);
        b2DestroyWorld(world);
    }
    printf("PASS %d mass/force/torque fixtures; %d deliberately wrong force sets rejected; zero/invalid input passed\n",
        fixtures, rejected_errors);
    return 0;
}

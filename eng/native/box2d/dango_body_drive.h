#ifndef DANGO_BODY_DRIVE_H
#define DANGO_BODY_DRIVE_H

#include <math.h>
#include "dango_self_physics_internal.h"

// D1 candidate only: preserve the requested total force and give every physical
// point the same control acceleration. Contact and internal forces are unchanged.
static int dango_body_apply_mass_drive(dango_self_soft_body* body, float horizontal_force)
{
    if (!body || body->node_count < 1 || body->node_count > DANGO_SOFT_BODY_MAX_NODES ||
        !isfinite(horizontal_force)) return -497;
    b2BodyId point_ids[DANGO_SOFT_BODY_MAX_NODES + 1];
    double masses[DANGO_SOFT_BODY_MAX_NODES + 1];
    point_ids[0] = body->core_body_id;
    double total_mass = 0;
    for (int i = 0; i <= body->node_count; ++i)
    {
        if (i > 0) point_ids[i] = body->node_body_ids[i - 1];
        if (!b2Body_IsValid(point_ids[i])) return -497;
        masses[i] = b2Body_GetMass(point_ids[i]);
        if (!isfinite(masses[i]) || masses[i] <= 0) return -497;
        total_mass += masses[i];
    }
    if (!isfinite(total_mass) || total_mass <= 0) return -497;
    // Validate all points before applying any force, including zero-input calls.
    if (horizontal_force == 0) return 0;
    for (int i = 0; i <= body->node_count; ++i)
    {
        float force = (float)((double)horizontal_force * masses[i] / total_mass);
        b2Body_ApplyForceToCenter(point_ids[i], (b2Vec2){force, 0}, true);
    }
    return 0;
}

#endif

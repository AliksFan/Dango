// Test-only J2.5 geometry registry. No position or velocity corrections.
#ifndef DANGO_JOINT_CONTACT_REGISTRY_H
#define DANGO_JOINT_CONTACT_REGISTRY_H
#include <stdint.h>
#include <stdlib.h>

#define DANGO_JOINT_ERROR_CONTACT_INDEX -103
#define DANGO_JOINT_ERROR_CONTACT_GEOMETRY -104
#define DANGO_JOINT_ERROR_CONTACT_CAPACITY -105
#define DANGO_JOINT_ERROR_CONTACT_CONFLICT -106
#define DANGO_JOINT_ERROR_CONTACT_READ -107

enum { JOINT_EDGE, JOINT_VERTEX, JOINT_GROUND };
typedef struct joint_contact_key
{
    int kind, point_rig, point_node, target_rig, feature;
    uint64_t shape;
} joint_contact_key;

typedef struct joint_contact_row
{
    joint_contact_key key;
    float t, gap;
    b2Vec2 normal;
} joint_contact_row;

typedef struct joint_contact_registry
{
    joint_contact_row rows[144];
    int count, raw_cross, raw_ground, duplicates;
} joint_contact_registry;

static inline bool joint_key_equal(joint_contact_key a, joint_contact_key b)
{
    return a.kind == b.kind && a.point_rig == b.point_rig && a.point_node == b.point_node &&
        a.target_rig == b.target_rig && a.feature == b.feature && a.shape == b.shape;
}

static inline int joint_normalize(joint_contact_row* row)
{
    if (row == NULL) return DANGO_JOINT_ERROR_CONTACT_INDEX;
    joint_contact_key* k = &row->key;
    if (k->point_rig < 0 || k->point_rig >= 3 || k->point_node < 0 || k->point_node >= 16)
        return DANGO_JOINT_ERROR_CONTACT_INDEX;
    if (!isfinite(row->gap) || !isfinite(row->t) || !b2IsValidVec2(row->normal) ||
        fabsf(b2Length(row->normal) - 1.0f) > 1e-5f)
        return DANGO_JOINT_ERROR_CONTACT_GEOMETRY;
    if (k->kind == JOINT_GROUND)
        return k->shape != 0 && k->target_rig == -1 && k->feature == -1
            ? 0 : DANGO_JOINT_ERROR_CONTACT_INDEX;
    if ((k->kind != JOINT_EDGE && k->kind != JOINT_VERTEX) || k->target_rig < 0 ||
        k->target_rig >= 3 || k->target_rig == k->point_rig || k->feature < 0 ||
        k->feature >= 16 || k->shape != 0 || row->t < 0 || row->t > 1)
        return DANGO_JOINT_ERROR_CONTACT_INDEX;
    // Only exact clamped endpoints become vertices; nearby interior points stay edges.
    if (k->kind == JOINT_EDGE && (row->t == 0 || row->t == 1))
    {
        k->feature = (k->feature + (row->t == 1 ? 1 : 0)) % 16;
        k->kind = JOINT_VERTEX;
    }
    if (k->kind == JOINT_VERTEX)
    {
        row->t = 0;
        if (k->point_rig > k->target_rig)
        {
            int rig = k->point_rig, node = k->point_node;
            k->point_rig = k->target_rig;
            k->point_node = k->feature;
            k->target_rig = rig;
            k->feature = node;
            row->normal = b2Neg(row->normal);
        }
    }
    return 0;
}

static inline int joint_register(joint_contact_registry* registry, joint_contact_row row)
{
    if (registry == NULL) return DANGO_JOINT_ERROR_CONTACT_INDEX;
    int error = joint_normalize(&row);
    if (error) return error;
    bool ground = row.key.kind == JOINT_GROUND;
    if (registry->count < 0 || registry->count > 144 || registry->raw_cross < 0 ||
        registry->raw_ground < 0 || registry->raw_cross > 96 || registry->raw_ground > 48 ||
        (ground ? registry->raw_ground == 48 : registry->raw_cross == 96))
        return DANGO_JOINT_ERROR_CONTACT_CAPACITY;
    for (int i = 0; i < registry->count; ++i)
    {
        if (!joint_key_equal(registry->rows[i].key, row.key)) continue;
        const joint_contact_row* old = &registry->rows[i];
        if (fabsf(old->gap - row.gap) > 1e-5f || fabsf(old->t - row.t) > 1e-5f ||
            b2Distance(old->normal, row.normal) > 1e-5f)
            return DANGO_JOINT_ERROR_CONTACT_CONFLICT;
        if (ground) ++registry->raw_ground; else ++registry->raw_cross;
        ++registry->duplicates;
        return 0;
    }
    if (registry->count == 144) return DANGO_JOINT_ERROR_CONTACT_CAPACITY;
    if (ground) ++registry->raw_ground; else ++registry->raw_cross;
    registry->rows[registry->count++] = row;
    return 0;
}

// Geometric candidate from a snapshot. The source identity is retained by the caller.
static inline int joint_nearest(const dango_joint_state* state, int rig, int node,
    int target, joint_contact_row* result)
{
    if (!state || !result || rig < 0 || target < 0 || rig >= state->body_count ||
        target >= state->body_count || rig == target || node < 0 || node >= 16)
        return DANGO_JOINT_ERROR_CONTACT_INDEX;
    b2Vec2 p = state->rigs[rig].positions[node];
    if (!b2IsValidVec2(p)) return DANGO_JOINT_ERROR_CONTACT_GEOMETRY;
    float best = INFINITY;
    bool inside = true;
    joint_contact_row row = {{JOINT_EDGE, rig, node, target, -1, 0}, 0, 0, {0, 0}};
    b2Vec2 nearest = {0, 0}, outward = {0, 0};
    for (int edge = 0; edge < 16; ++edge)
    {
        b2Vec2 a = state->rigs[target].positions[edge];
        b2Vec2 b = state->rigs[target].positions[(edge + 1) % 16];
        b2Vec2 delta = b2Sub(b, a);
        float length2 = b2LengthSquared(delta);
        if (!b2IsValidVec2(a) || !b2IsValidVec2(b) || !isfinite(length2) || length2 < 1e-10f)
            return DANGO_JOINT_ERROR_CONTACT_GEOMETRY;
        if (b2Cross(delta, b2Sub(p, a)) < 0) inside = false;
        float projection = b2Dot(b2Sub(p, a), delta) / length2;
        if (!isfinite(projection)) return DANGO_JOINT_ERROR_CONTACT_GEOMETRY;
        float t = fmaxf(0, fminf(1, projection));
        b2Vec2 q = b2MulAdd(a, t, delta);
        float distance = b2Distance(p, q);
        if (!isfinite(distance)) return DANGO_JOINT_ERROR_CONTACT_GEOMETRY;
        if (distance < best)
        {
            best = distance;
            row.key.feature = edge;
            row.t = t;
            nearest = q;
            outward = b2MulSV(1.0f / sqrtf(length2), (b2Vec2){delta.y, -delta.x});
        }
    }
    row.gap = inside ? -best : best;
    row.normal = best > 1e-7f ? b2MulSV((inside ? -1.0f : 1.0f) / best, b2Sub(p, nearest)) : outward;
    int error = joint_normalize(&row);
    if (!error) *result = row;
    return error;
}

typedef struct joint_contact_snapshot
{
    joint_contact_registry registry;
    joint_contact_key source_keys[3][16][3];
    bool source_present[3][16][3];
    int candidates, active_cross, ground_geometric;
} joint_contact_snapshot;

static inline int joint_key_compare(joint_contact_key a, joint_contact_key b)
{
    int av[5] = {a.kind, a.point_rig, a.point_node, a.target_rig, a.feature};
    int bv[5] = {b.kind, b.point_rig, b.point_node, b.target_rig, b.feature};
    for (int i = 0; i < 5; ++i) if (av[i] != bv[i]) return av[i] < bv[i] ? -1 : 1;
    return a.shape == b.shape ? 0 : (a.shape < b.shape ? -1 : 1);
}

static inline int joint_build_contacts(const dango_joint_state* state, joint_contact_snapshot* output)
{
    if (!state || !output || !dango_joint_state_valid(state)) return DANGO_JOINT_ERROR_INVALID_STATE;
    joint_contact_snapshot next = {0};
    for (int rig = 0; rig < state->body_count; ++rig)
    {
        const dango_constraint_state* s = &state->rigs[rig];
        if (!isfinite(s->ground) || !isfinite(s->radius) || s->radius < 0 ||
            !isfinite(s->radial_min) || !isfinite(s->radial_max) || s->radial_min <= 0 || s->radial_max < s->radial_min ||
            !isfinite(s->perimeter_min) || !isfinite(s->perimeter_max) || s->perimeter_min <= 0 || s->perimeter_max < s->perimeter_min)
            return DANGO_JOINT_ERROR_CONTACT_GEOMETRY;
        for (int node = 0; node < 16; ++node)
        {
            if (state->rigs[rig].positions[node].y <= state->rigs[rig].ground + state->rigs[rig].radius)
                ++next.ground_geometric;
            for (int target = 0; target < state->body_count; ++target)
            {
                if (target == rig) continue;
                joint_contact_row row;
                int error = joint_nearest(state, rig, node, target, &row);
                if (error) return error;
                if (++next.candidates > 96) return DANGO_JOINT_ERROR_CONTACT_CAPACITY;
                next.source_keys[rig][node][target] = row.key;
                next.source_present[rig][node][target] = true;
                if (row.gap <= state->skin)
                {
                    ++next.active_cross;
                    error = joint_register(&next.registry, row);
                    if (error) return error;
                }
            }
        }
    }
    // Stable canonical storage. The solver may apply its own declared visit order.
    for (int i = 1; i < next.registry.count; ++i)
    {
        joint_contact_row row = next.registry.rows[i];
        int j = i;
        while (j > 0 && joint_key_compare(row.key, next.registry.rows[j - 1].key) < 0)
        {
            next.registry.rows[j] = next.registry.rows[j - 1];
            --j;
        }
        next.registry.rows[j] = row;
    }
    *output = next;
    return 0;
}

typedef struct joint_contact_changes { int added, removed, switches; } joint_contact_changes;
static inline joint_contact_changes joint_compare_contacts(const joint_contact_snapshot* before,
    const joint_contact_snapshot* after)
{
    joint_contact_changes changes = {0};
    for (int side = 0; side < 2; ++side)
    {
        const joint_contact_registry* a = side ? &before->registry : &after->registry;
        const joint_contact_registry* b = side ? &after->registry : &before->registry;
        for (int i = 0; i < a->count; ++i)
        {
            bool found = false;
            for (int j = 0; j < b->count; ++j) if (joint_key_equal(a->rows[i].key, b->rows[j].key)) found = true;
            if (!found) { if (side) ++changes.removed; else ++changes.added; }
        }
    }
    for (int r = 0; r < 3; ++r) for (int n = 0; n < 16; ++n) for (int t = 0; t < 3; ++t)
        if (before->source_present[r][n][t] && after->source_present[r][n][t] &&
            !joint_key_equal(before->source_keys[r][n][t], after->source_keys[r][n][t])) ++changes.switches;
    return changes;
}

typedef struct joint_ground_sample
{
    b2ShapeId shape;
    int capacity, returned, matching;
    float normal, tangent;
} joint_ground_sample;

static inline int joint_read_ground(b2BodyId body, b2ShapeId ground, joint_ground_sample* output)
{
    if (!output || !b2Body_IsValid(body) || !b2Shape_IsValid(ground)) return DANGO_JOINT_ERROR_CONTACT_READ;
    int capacity = b2Body_GetContactCapacity(body);
    if (capacity < 0 || (size_t)capacity > SIZE_MAX / sizeof(b2ContactData)) return DANGO_JOINT_ERROR_CONTACT_CAPACITY;
    b2ContactData* data = capacity ? malloc((size_t)capacity * sizeof *data) : NULL;
    if (capacity && !data) return DANGO_JOINT_ERROR_CONTACT_READ;
    joint_ground_sample sample = {0};
    sample.shape = ground;
    sample.capacity = capacity;
    sample.returned = capacity ? b2Body_GetContactData(body, data, capacity) : 0;
    int error = 0;
    if (sample.returned < 0 || sample.returned > capacity || b2Body_GetContactCapacity(body) != capacity)
        error = DANGO_JOINT_ERROR_CONTACT_READ;
    for (int i = 0; !error && i < sample.returned; ++i)
    {
        bool a = B2_ID_EQUALS(data[i].shapeIdA, ground), b = B2_ID_EQUALS(data[i].shapeIdB, ground);
        if (!a && !b) continue;
        ++sample.matching;
        if (data[i].manifold.pointCount < 0 || data[i].manifold.pointCount > 2 ||
            !b2IsValidVec2(data[i].manifold.normal)) { error = DANGO_JOINT_ERROR_CONTACT_GEOMETRY; break; }
        for (int p = 0; p < data[i].manifold.pointCount; ++p)
        {
            float n = data[i].manifold.points[p].normalImpulse;
            float t = data[i].manifold.points[p].tangentImpulse;
            if (!isfinite(n) || n < 0 || !isfinite(t)) { error = DANGO_JOINT_ERROR_CONTACT_GEOMETRY; break; }
            sample.normal += n;
            sample.tangent += (a ? 1.0f : -1.0f) * data[i].manifold.normal.y * t;
        }
    }
    free(data);
    if (!isfinite(sample.normal) || !isfinite(sample.tangent)) error = DANGO_JOINT_ERROR_CONTACT_GEOMETRY;
    if (!error) *output = sample;
    return error;
}

static inline int joint_register_ground(joint_contact_registry* registry, int rig, int node,
    float gap, const joint_ground_sample* sample)
{
    if (!sample || sample->matching < 0 || sample->matching > sample->returned ||
        !isfinite(sample->normal) || sample->normal < 0 || !isfinite(sample->tangent))
        return DANGO_JOINT_ERROR_CONTACT_READ;
    if (!sample->matching) return 0;
    joint_contact_row row = {{JOINT_GROUND, rig, node, -1, -1, b2StoreShapeId(sample->shape)}, 0, gap, {0, 1}};
    return joint_register(registry, row);
}
#endif

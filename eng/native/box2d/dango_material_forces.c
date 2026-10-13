#include "dango_material_forces.h"
#include <math.h>
#include <stddef.h>
#include <string.h>

static const double dango_material_pi = 3.14159265358979323846;

static double cross(dango_material_vec2 a, dango_material_vec2 b)
{
    return a.x * b.y - a.y * b.x;
}

static dango_material_vec2 subtract(dango_material_vec2 a, dango_material_vec2 b)
{
    return (dango_material_vec2){a.x - b.x, a.y - b.y};
}

static int valid_count(int count)
{
    return count >= 3 && count <= DANGO_MATERIAL_MAX_NODES;
}

static void reset_output(int count, double* energy, dango_material_vec2* forces)
{
    if (energy != NULL) *energy = NAN;
    if (forces != NULL && valid_count(count))
        memset(forces, 0, (size_t)count * sizeof(*forces));
}

static double orientation(dango_material_vec2 a, dango_material_vec2 b, dango_material_vec2 c)
{
    return cross(subtract(b, a), subtract(c, a));
}

static int on_segment(dango_material_vec2 a, dango_material_vec2 b, dango_material_vec2 p)
{
    const double tolerance = 1e-14;
    return fabs(orientation(a, b, p)) <= tolerance &&
        p.x >= fmin(a.x, b.x) - tolerance && p.x <= fmax(a.x, b.x) + tolerance &&
        p.y >= fmin(a.y, b.y) - tolerance && p.y <= fmax(a.y, b.y) + tolerance;
}

static int segments_intersect(dango_material_vec2 a, dango_material_vec2 b,
    dango_material_vec2 c, dango_material_vec2 d)
{
    double ab_c = orientation(a, b, c), ab_d = orientation(a, b, d);
    double cd_a = orientation(c, d, a), cd_b = orientation(c, d, b);
    if (((ab_c > 0 && ab_d < 0) || (ab_c < 0 && ab_d > 0)) &&
        ((cd_a > 0 && cd_b < 0) || (cd_a < 0 && cd_b > 0))) return 1;
    return on_segment(a, b, c) || on_segment(a, b, d) ||
        on_segment(c, d, a) || on_segment(c, d, b);
}

// Normalize around a vertex before area/angle evaluation. This avoids the
// cancellation caused by an arbitrary world origin and defines scale-relative
// degeneracy thresholds without consulting the material's stiffness.
static int validate_polygon(const dango_material_vec2* points, int count,
    dango_material_vec2* normalized, double* scale, double* area)
{
    if (points == NULL || !valid_count(count)) return DANGO_MATERIAL_INVALID;
    double min_x = points[0].x, max_x = min_x, min_y = points[0].y, max_y = min_y;
    for (int i = 0; i < count; ++i)
    {
        if (!isfinite(points[i].x) || !isfinite(points[i].y)) return DANGO_MATERIAL_NONFINITE;
        min_x = fmin(min_x, points[i].x); max_x = fmax(max_x, points[i].x);
        min_y = fmin(min_y, points[i].y); max_y = fmax(max_y, points[i].y);
    }
    *scale = hypot(max_x - min_x, max_y - min_y);
    if (!isfinite(*scale)) return DANGO_MATERIAL_NONFINITE;
    if (*scale <= 0) return DANGO_MATERIAL_DEGENERATE;
    for (int i = 0; i < count; ++i)
    {
        normalized[i] = (dango_material_vec2){
            (points[i].x - points[0].x) / *scale,
            (points[i].y - points[0].y) / *scale};
    }
    double twice_area = 0;
    for (int i = 0; i < count; ++i)
    {
        int next = (i + 1) % count;
        dango_material_vec2 edge = subtract(normalized[next], normalized[i]);
        if (hypot(edge.x, edge.y) <= 1e-10) return DANGO_MATERIAL_DEGENERATE;
        twice_area += cross(normalized[i], normalized[next]);
    }
    // A non-positive signed area is not a valid CCW material contour.
    if (twice_area <= 2e-12) return DANGO_MATERIAL_DEGENERATE;
    for (int i = 0; i < count; ++i)
    {
        int next_i = (i + 1) % count;
        for (int j = i + 1; j < count; ++j)
        {
            int next_j = (j + 1) % count;
            if (j == next_i || next_j == i) continue;
            if (segments_intersect(normalized[i], normalized[next_i], normalized[j], normalized[next_j]))
                return DANGO_MATERIAL_DEGENERATE;
        }
    }
    *area = (0.5 * twice_area * *scale) * *scale;
    return isfinite(*area) && *area > 0 ? DANGO_MATERIAL_OK : DANGO_MATERIAL_NONFINITE;
}

static int copy_result(int count, double value, const dango_material_vec2* result,
    double* energy, dango_material_vec2* forces)
{
    if (!isfinite(value)) return DANGO_MATERIAL_NONFINITE;
    for (int i = 0; i < count; ++i)
        if (!isfinite(result[i].x) || !isfinite(result[i].y)) return DANGO_MATERIAL_NONFINITE;
    *energy = value;
    memcpy(forces, result, (size_t)count * sizeof(*forces));
    return DANGO_MATERIAL_OK;
}

int dango_material_area(const dango_material_vec2* points, int count,
    double target_area, double natural_radius, double strength,
    double* energy, dango_material_vec2* forces)
{
    // Aliasing violates the API precondition; do not overwrite the input.
    if (points == forces && points != NULL)
    {
        if (energy != NULL) *energy = NAN;
        return DANGO_MATERIAL_INVALID;
    }
    reset_output(count, energy, forces);
    if (energy == NULL || forces == NULL) return DANGO_MATERIAL_INVALID;
    if (!isfinite(target_area) || !isfinite(natural_radius) || !isfinite(strength))
        return DANGO_MATERIAL_NONFINITE;
    if (target_area <= 0 || natural_radius <= 0 || strength < 0) return DANGO_MATERIAL_INVALID;
    dango_material_vec2 normalized[DANGO_MATERIAL_MAX_NODES];
    double scale, area;
    int status = validate_polygon(points, count, normalized, &scale, &area);
    if (status != DANGO_MATERIAL_OK) return status;
    if (area >= target_area || strength == 0)
    {
        *energy = 0;
        return DANGO_MATERIAL_OK;
    }
    double natural_gradient = natural_radius * sin(2 * dango_material_pi / count);
    double coefficient = (strength / target_area) / natural_gradient;
    // Positive stiffness must not silently disappear through underflow in an
    // intermediate coefficient (large compression could amplify it again).
    if (!isfinite(coefficient) || coefficient <= 0) return DANGO_MATERIAL_NONFINITE;
    double compression = target_area - area;
    double pressure = coefficient * compression;
    double value = 0.5 * pressure * compression;
    dango_material_vec2 result[DANGO_MATERIAL_MAX_NODES];
    for (int i = 0; i < count; ++i)
    {
        int previous = (i + count - 1) % count, next = (i + 1) % count;
        // Negative gradient of the one-sided area compression energy.
        result[i] = (dango_material_vec2){
            0.5 * pressure * (points[next].y - points[previous].y),
            0.5 * pressure * (points[previous].x - points[next].x)};
    }
    return copy_result(count, value, result, energy, forces);
}

int dango_material_bending(const dango_material_vec2* points, int count,
    double stiffness, double* energy, dango_material_vec2* forces)
{
    if (points == forces && points != NULL)
    {
        if (energy != NULL) *energy = NAN;
        return DANGO_MATERIAL_INVALID;
    }
    reset_output(count, energy, forces);
    if (energy == NULL || forces == NULL) return DANGO_MATERIAL_INVALID;
    if (!isfinite(stiffness)) return DANGO_MATERIAL_NONFINITE;
    if (stiffness < 0) return DANGO_MATERIAL_INVALID;
    dango_material_vec2 normalized[DANGO_MATERIAL_MAX_NODES];
    dango_material_vec2 previous_gradient[DANGO_MATERIAL_MAX_NODES];
    dango_material_vec2 next_gradient[DANGO_MATERIAL_MAX_NODES];
    double turning[DANGO_MATERIAL_MAX_NODES], scale, area;
    int status = validate_polygon(points, count, normalized, &scale, &area);
    if (status != DANGO_MATERIAL_OK) return status;
    for (int i = 0; i < count; ++i)
    {
        int previous = (i + count - 1) % count, next = (i + 1) % count;
        dango_material_vec2 e = subtract(normalized[i], normalized[previous]);
        dango_material_vec2 f = subtract(normalized[next], normalized[i]);
        double e_squared = e.x * e.x + e.y * e.y, f_squared = f.x * f.x + f.y * f.y;
        turning[i] = atan2(cross(e, f), e.x * f.x + e.y * f.y);
        if (dango_material_pi - fabs(turning[i]) <= 1e-10) return DANGO_MATERIAL_DEGENERATE;
        previous_gradient[i] = (dango_material_vec2){(-e.y / e_squared) / scale, (e.x / e_squared) / scale};
        next_gradient[i] = (dango_material_vec2){(-f.y / f_squared) / scale, (f.x / f_squared) / scale};
    }
    dango_material_vec2 result[DANGO_MATERIAL_MAX_NODES] = {{0, 0}};
    double value = 0;
    for (int i = 0; i < count; ++i)
    {
        int previous = (i + count - 1) % count, next = (i + 1) % count;
        double difference = turning[next] - turning[i];
        value += 0.5 * stiffness * difference * difference;
        double derivative = stiffness * (2 * turning[i] - turning[previous] - turning[next]);
        dango_material_vec2 p = previous_gradient[i], n = next_gradient[i];
        result[previous].x -= derivative * p.x; result[previous].y -= derivative * p.y;
        result[next].x -= derivative * n.x; result[next].y -= derivative * n.y;
        result[i].x += derivative * (p.x + n.x); result[i].y += derivative * (p.y + n.y);
    }
    return copy_result(count, value, result, energy, forces);
}

int dango_material_cap(dango_material_vec2* forces, int count,
    double maximum_force, double* scale)
{
    if (scale != NULL) *scale = NAN;
    if (forces == NULL || scale == NULL || count < 1 || count > DANGO_MATERIAL_MAX_NODES)
        return DANGO_MATERIAL_INVALID;
    if (!isfinite(maximum_force)) return DANGO_MATERIAL_NONFINITE;
    if (maximum_force < 0) return DANGO_MATERIAL_INVALID;
    double largest = 0;
    for (int i = 0; i < count; ++i)
    {
        if (!isfinite(forces[i].x) || !isfinite(forces[i].y)) return DANGO_MATERIAL_NONFINITE;
        double length = hypot(forces[i].x, forces[i].y);
        if (!isfinite(length)) return DANGO_MATERIAL_NONFINITE;
        largest = fmax(largest, length);
    }
    *scale = largest > maximum_force ? maximum_force / largest : 1;
    for (int i = 0; i < count; ++i)
    {
        forces[i].x *= *scale;
        forces[i].y *= *scale;
    }
    return DANGO_MATERIAL_OK;
}

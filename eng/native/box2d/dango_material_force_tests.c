// Fixed algorithm fixtures only; this executable never steps a physical world.
#include <float.h>
#include <math.h>
#include <stdio.h>
#include <string.h>
#include "dango_material_forces.h"
#include "box2d/box2d.h"
#include "dango_self_physics_internal.h"

// Capture the actual old static force functions, without applying their forces.
static void material_legacy_capture(b2BodyId id, b2Vec2 force, bool wake);
#define b2Body_ApplyForceToCenter material_legacy_capture
#include "dango_box2d_bridge.c"
#undef b2Body_ApplyForceToCenter

// Fixed before implementation: do not derive thresholds from observed errors.
static const double force_relative_tolerance = 1e-11;
static const double gradient_relative_tolerance = 3e-7;
static const double gradient_absolute_tolerance = 1e-8;
static const double difference_step_ratio = 1e-6;
static const double material_pi = 3.1415926535897932384626433832795;
#define MATERIAL_CHECK(condition) do { if (!(condition)) { fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #condition); return 1; } } while (0)

typedef struct material_fixture
{
    int count;
    double natural_radius, target_area;
    dango_material_vec2 points[DANGO_MATERIAL_MAX_NODES];
} material_fixture;

static dango_self_soft_body* legacy_body;
static dango_material_vec2 legacy_forces[DANGO_MATERIAL_MAX_NODES];
static double maximum_force_residual, maximum_torque_residual, maximum_gradient_error;
static int positive_fixtures;

static double material_area_value(const dango_material_vec2* points, int count)
{
    // Subtract one vertex so translations do not cancel large world products.
    double twice = 0;
    for (int i = 0; i < count; ++i)
    {
        int next = (i + 1) % count;
        double x = points[i].x - points[0].x, y = points[i].y - points[0].y;
        double nx = points[next].x - points[0].x, ny = points[next].y - points[0].y;
        twice += x * ny - nx * y;
    }
    return .5 * twice;
}

static double material_scale(const dango_material_vec2* points, int count)
{
    double x0 = points[0].x, x1 = x0, y0 = points[0].y, y1 = y0;
    for (int i = 1; i < count; ++i)
    {
        x0 = fmin(x0, points[i].x); x1 = fmax(x1, points[i].x);
        y0 = fmin(y0, points[i].y); y1 = fmax(y1, points[i].y);
    }
    return hypot(x1 - x0, y1 - y0);
}

static material_fixture material_irregular(double size, double rotation, double tx, double ty, bool concave)
{
    material_fixture fixture = {0};
    fixture.count = 16; fixture.natural_radius = .68 * size;
    fixture.target_area = .5 * fixture.count * fixture.natural_radius * fixture.natural_radius * sin(2 * material_pi / fixture.count);
    for (int i = 0; i < fixture.count; ++i)
    {
        double phase = 2 * material_pi * i / fixture.count;
        double angle = phase + .13 * sin(phase) + .04 * sin(3 * phase);
        double radius = .68 * (.82 + .08 * cos(2 * phase) + .04 * sin(3 * phase));
        if (concave && i == 5) radius *= .68;
        double x = size * (radius * cos(angle) + .035 * sin(angle));
        double y = size * (.85 * radius * sin(angle) + .017 * cos(3 * phase));
        fixture.points[i] = (dango_material_vec2){tx + cos(rotation) * x - sin(rotation) * y, ty + sin(rotation) * x + cos(rotation) * y};
    }
    return fixture;
}

static material_fixture material_regular(int count, double radius)
{
    material_fixture fixture = {0};
    fixture.count = count; fixture.natural_radius = radius;
    fixture.target_area = .5 * count * radius * radius * sin(2 * material_pi / count);
    for (int i = 0; i < count; ++i)
    {
        double angle = 2 * material_pi * i / count;
        fixture.points[i] = (dango_material_vec2){radius * cos(angle), radius * sin(angle)};
    }
    return fixture;
}

static bool material_closed(const material_fixture* fixture, const dango_material_vec2* forces, bool measure)
{
    double cx = 0, cy = 0, fx = 0, fy = 0, torque = 0, absolute = 0;
    for (int i = 0; i < fixture->count; ++i) { cx += fixture->points[i].x; cy += fixture->points[i].y; }
    cx /= fixture->count; cy /= fixture->count;
    double scale = material_scale(fixture->points, fixture->count);
    for (int i = 0; i < fixture->count; ++i)
    {
        if (!isfinite(forces[i].x) || !isfinite(forces[i].y)) return false;
        fx += forces[i].x; fy += forces[i].y; absolute += hypot(forces[i].x, forces[i].y);
        torque += (fixture->points[i].x - cx) * forces[i].y - (fixture->points[i].y - cy) * forces[i].x;
    }
    // Absolute floor permits double addition roundoff when the group is zero.
    double floor = 32 * DBL_EPSILON * fmax(1, absolute);
    bool valid = hypot(fx, fy) <= force_relative_tolerance * absolute + floor &&
        fabs(torque) <= (force_relative_tolerance * absolute + floor) * scale;
    if (measure)
    {
        maximum_force_residual = fmax(maximum_force_residual, hypot(fx, fy) / fmax(1, absolute));
        maximum_torque_residual = fmax(maximum_torque_residual, fabs(torque) / fmax(1, absolute * scale));
    }
    return valid;
}

static int material_evaluate(const material_fixture* fixture, bool bending, const dango_material_vec2* points, double* energy, dango_material_vec2* forces)
{
    return bending ? dango_material_bending(points, fixture->count, 1.7, energy, forces) :
        dango_material_area(points, fixture->count, fixture->target_area, fixture->natural_radius, 4.8, energy, forces);
}

static double material_reference_energy(const material_fixture* fixture, bool bending)
{
    if (!bending)
    {
        double deficit = fmax(0, fixture->target_area - material_area_value(fixture->points, fixture->count));
        return .5 * 4.8 * deficit * deficit / (fixture->target_area * fixture->natural_radius * sin(2 * material_pi / fixture->count));
    }
    double turns[DANGO_MATERIAL_MAX_NODES], energy = 0;
    for (int i = 0; i < fixture->count; ++i)
    {
        int previous = (i + fixture->count - 1) % fixture->count, next = (i + 1) % fixture->count;
        double ex = fixture->points[i].x - fixture->points[previous].x, ey = fixture->points[i].y - fixture->points[previous].y;
        double fx = fixture->points[next].x - fixture->points[i].x, fy = fixture->points[next].y - fixture->points[i].y;
        turns[i] = atan2(ex * fy - ey * fx, ex * fx + ey * fy);
    }
    for (int i = 0; i < fixture->count; ++i)
    {
        double difference = turns[(i + 1) % fixture->count] - turns[i];
        energy += .5 * 1.7 * difference * difference;
    }
    return energy;
}

static bool material_gradient_matches(const material_fixture* fixture, bool bending, const dango_material_vec2* forces, bool measure)
{
    dango_material_vec2 points[DANGO_MATERIAL_MAX_NODES], ignored[DANGO_MATERIAL_MAX_NODES];
    memcpy(points, fixture->points, sizeof points);
    double h = difference_step_ratio * material_scale(points, fixture->count);
    for (int i = 0; i < fixture->count; ++i) for (int axis = 0; axis < 2; ++axis)
    {
        double* coordinate = axis ? &points[i].y : &points[i].x;
        double original = *coordinate, plus, minus;
        *coordinate = original + h;
        if (material_evaluate(fixture, bending, points, &plus, ignored) != DANGO_MATERIAL_OK) return false;
        *coordinate = original - h;
        if (material_evaluate(fixture, bending, points, &minus, ignored) != DANGO_MATERIAL_OK) return false;
        *coordinate = original;
        double numerical = -(plus - minus) / (2 * h), analytic = axis ? forces[i].y : forces[i].x;
        double error = fabs(analytic - numerical);
        if (measure) maximum_gradient_error = fmax(maximum_gradient_error, error);
        if (error > gradient_absolute_tolerance + gradient_relative_tolerance * fmax(fabs(analytic), fabs(numerical))) return false;
    }
    return true;
}

static int material_positive_fixtures(void)
{
    const double sizes[] = {.8, 1, 1.2};
    for (int concave = 0; concave < 2; ++concave)
    for (int size = 0; size < 3; ++size)
    for (int moved = 0; moved < 2; ++moved)
    for (int bending = 0; bending < 2; ++bending)
    {
        material_fixture fixture = material_irregular(sizes[size], moved ? .713 : 0, moved ? -2.75 : 0, moved ? 4.125 : 0, concave != 0);
        dango_material_vec2 forces[DANGO_MATERIAL_MAX_NODES]; double energy = NAN;
        MATERIAL_CHECK(material_evaluate(&fixture, bending != 0, fixture.points, &energy, forces) == DANGO_MATERIAL_OK);
        double expected = material_reference_energy(&fixture, bending != 0);
        MATERIAL_CHECK(fabs(energy - expected) <= 1e-12 * fmax(1, fabs(expected)));
        MATERIAL_CHECK(material_closed(&fixture, forces, true));
        MATERIAL_CHECK(material_gradient_matches(&fixture, bending != 0, forces, true));
        ++positive_fixtures;
    }
    puts("PASS: 24 asymmetric/nonuniform/concave size and transform fixtures, independent energies, closure, numerical gradients");
    return 0;
}

static int material_covariance(void)
{
    for (int bending = 0; bending < 2; ++bending)
    {
        material_fixture base = material_irregular(1, 0, 0, 0, false);
        dango_material_vec2 original[DANGO_MATERIAL_MAX_NODES]; double energy;
        MATERIAL_CHECK(material_evaluate(&base, bending != 0, base.points, &energy, original) == DANGO_MATERIAL_OK);
        const double sizes[] = {.8, 1, 1.2};
        for (int size = 0; size < 3; ++size)
        {
            const double angle = .713;
            material_fixture moved = material_irregular(sizes[size], angle, -2.75, 4.125, false);
            dango_material_vec2 forces[DANGO_MATERIAL_MAX_NODES]; double moved_energy;
            MATERIAL_CHECK(material_evaluate(&moved, bending != 0, moved.points, &moved_energy, forces) == DANGO_MATERIAL_OK);
            double expected_energy = energy * (bending ? 1 : sizes[size]);
            MATERIAL_CHECK(fabs(moved_energy - expected_energy) <= 1e-11 * fmax(1, fabs(expected_energy)));
            for (int i = 0; i < base.count; ++i)
            {
                double divisor = bending ? sizes[size] : 1;
                double fx = (cos(angle) * original[i].x - sin(angle) * original[i].y) / divisor;
                double fy = (sin(angle) * original[i].x + cos(angle) * original[i].y) / divisor;
                MATERIAL_CHECK(hypot(forces[i].x - fx, forces[i].y - fy) <= 1e-10 * fmax(1, hypot(fx, fy)));
            }
        }
    }
    puts("PASS: translation/rotation covariance and fixed-natural-scale dimensional behavior");
    return 0;
}

static int material_zero_and_regular(void)
{
    const int counts[] = {3, 8, 16, 64};
    for (int c = 0; c < 4; ++c)
    {
        material_fixture fixture = material_regular(counts[c], .68);
        dango_material_vec2 forces[DANGO_MATERIAL_MAX_NODES]; double energy;
        MATERIAL_CHECK(dango_material_bending(fixture.points, fixture.count, 1.7, &energy, forces) == DANGO_MATERIAL_OK);
        MATERIAL_CHECK(energy <= 1e-25);
        for (int i = 0; i < fixture.count; ++i) MATERIAL_CHECK(hypot(forces[i].x, forces[i].y) < 1e-10);
        // Strictly uncompressed and the C1 boundary are separate from central differences.
        for (int boundary = 0; boundary < 2; ++boundary)
        {
            double area = material_area_value(fixture.points, fixture.count);
            MATERIAL_CHECK(dango_material_area(fixture.points, fixture.count, area * (boundary ? 1 : .8), fixture.natural_radius, 4.8, &energy, forces) == DANGO_MATERIAL_OK);
            MATERIAL_CHECK(fabs(energy) < 1e-25);
            for (int i = 0; i < fixture.count; ++i) MATERIAL_CHECK(hypot(forces[i].x, forces[i].y) < 1e-12);
        }
    }
    material_fixture fixture = material_irregular(1, 0, 0, 0, false);
    dango_material_vec2 forces[DANGO_MATERIAL_MAX_NODES]; double energy;
    MATERIAL_CHECK(dango_material_area(fixture.points, fixture.count, fixture.target_area, fixture.natural_radius, 0, &energy, forces) == DANGO_MATERIAL_OK);
    MATERIAL_CHECK(energy == 0);
    for (int i = 0; i < fixture.count; ++i) MATERIAL_CHECK(forces[i].x == 0 && forces[i].y == 0);
    MATERIAL_CHECK(dango_material_bending(fixture.points, fixture.count, 0, &energy, forces) == DANGO_MATERIAL_OK);
    MATERIAL_CHECK(energy == 0);
    for (int i = 0; i < fixture.count; ++i) MATERIAL_CHECK(forces[i].x == 0 && forces[i].y == 0);
    // Nonzero pressure calibration is exact at natural regular radius, not at compressed radius.
    fixture = material_regular(16, .68); fixture.target_area *= 1.2;
    MATERIAL_CHECK(material_evaluate(&fixture, false, fixture.points, &energy, forces) == DANGO_MATERIAL_OK);
    double pressure = 4.8 * (fixture.target_area - material_area_value(fixture.points, fixture.count)) / fixture.target_area;
    for (int i = 0; i < fixture.count; ++i)
    {
        double fx = pressure * fixture.points[i].x / fixture.natural_radius, fy = pressure * fixture.points[i].y / fixture.natural_radius;
        MATERIAL_CHECK(hypot(forces[i].x - fx, forces[i].y - fy) < 1e-12);
    }
    puts("PASS: regular polygons, inactive area, area boundary, zero stiffness, natural-ring pressure calibration");
    return 0;
}

static bool material_zeroed(const dango_material_vec2* forces, int count)
{
    for (int i = 0; i < count; ++i) if (forces[i].x != 0 || forces[i].y != 0) return false;
    return true;
}

static void material_sentinels(dango_material_vec2* forces)
{
    for (int i = 0; i < DANGO_MATERIAL_MAX_NODES; ++i) forces[i] = (dango_material_vec2){17, -31};
}

static int material_rejected_geometry(const material_fixture* fixture, int expected)
{
    for (int bending = 0; bending < 2; ++bending)
    {
        dango_material_vec2 forces[DANGO_MATERIAL_MAX_NODES]; material_sentinels(forces); double energy = 42;
        MATERIAL_CHECK(material_evaluate(fixture, bending != 0, fixture->points, &energy, forces) == expected);
        MATERIAL_CHECK(isnan(energy)); MATERIAL_CHECK(material_zeroed(forces, fixture->count));
    }
    return 0;
}

static int material_failures(void)
{
    material_fixture base = material_irregular(1, 0, 0, 0, false), invalid;
    dango_material_vec2 forces[DANGO_MATERIAL_MAX_NODES]; double energy;
    invalid = base; invalid.points[2].x = NAN; MATERIAL_CHECK(material_rejected_geometry(&invalid, DANGO_MATERIAL_NONFINITE) == 0);
    invalid = base; invalid.points[2].y = INFINITY; MATERIAL_CHECK(material_rejected_geometry(&invalid, DANGO_MATERIAL_NONFINITE) == 0);
    invalid = base; invalid.points[1] = invalid.points[0]; MATERIAL_CHECK(material_rejected_geometry(&invalid, DANGO_MATERIAL_DEGENERATE) == 0);
    invalid = base; invalid.points[1] = (dango_material_vec2){invalid.points[0].x + 1e-12, invalid.points[0].y}; MATERIAL_CHECK(material_rejected_geometry(&invalid, DANGO_MATERIAL_DEGENERATE) == 0);
    invalid = material_regular(3, .68); invalid.points[0] = (dango_material_vec2){0, 0}; invalid.points[1] = (dango_material_vec2){1, 0}; invalid.points[2] = (dango_material_vec2){2, 0}; MATERIAL_CHECK(material_rejected_geometry(&invalid, DANGO_MATERIAL_DEGENERATE) == 0);
    invalid = base; for (int i = 0; i < base.count; ++i) invalid.points[i] = base.points[base.count - 1 - i]; MATERIAL_CHECK(material_rejected_geometry(&invalid, DANGO_MATERIAL_DEGENERATE) == 0);
    invalid = base; invalid.count = 4; invalid.points[0] = (dango_material_vec2){0, 0}; invalid.points[1] = (dango_material_vec2){3, 0}; invalid.points[2] = (dango_material_vec2){0, 2}; invalid.points[3] = (dango_material_vec2){2, 2}; MATERIAL_CHECK(material_area_value(invalid.points, invalid.count) > 0); MATERIAL_CHECK(material_rejected_geometry(&invalid, DANGO_MATERIAL_DEGENERATE) == 0);
    // A nonadjacent vertex touches the first edge, with positive signed area.
    invalid = base; invalid.count = 5; invalid.points[0] = (dango_material_vec2){0, 0}; invalid.points[1] = (dango_material_vec2){2, 0}; invalid.points[2] = (dango_material_vec2){2, 2}; invalid.points[3] = (dango_material_vec2){1, 0}; invalid.points[4] = (dango_material_vec2){0, 2};
    MATERIAL_CHECK(material_area_value(invalid.points, invalid.count) > 0);
    MATERIAL_CHECK(material_rejected_geometry(&invalid, DANGO_MATERIAL_DEGENERATE) == 0);
    // Only bending has the atan2 reversal cut. This simple CCW polygon remains
    // valid for area; its tiny self-contact margin excludes a difference probe.
    invalid = base; invalid.count = 5; invalid.points[0] = (dango_material_vec2){-1, 0}; invalid.points[1] = (dango_material_vec2){1, 0}; invalid.points[2] = (dango_material_vec2){0, 1e-11}; invalid.points[3] = (dango_material_vec2){1, 1}; invalid.points[4] = (dango_material_vec2){-1, 1};
    material_sentinels(forces); energy = 42;
    MATERIAL_CHECK(dango_material_area(invalid.points, invalid.count, 2, invalid.natural_radius, 4.8, &energy, forces) == DANGO_MATERIAL_OK);
    MATERIAL_CHECK(isfinite(energy) && energy > 0 && material_closed(&invalid, forces, true));
    material_sentinels(forces); energy = 42;
    MATERIAL_CHECK(dango_material_bending(invalid.points, invalid.count, 1.7, &energy, forces) == DANGO_MATERIAL_DEGENERATE);
    MATERIAL_CHECK(isnan(energy) && material_zeroed(forces, invalid.count));
    invalid = base;
    for (int i = 0; i < invalid.count; ++i) { invalid.points[i].x *= 1e-200; invalid.points[i].y *= 1e-200; }
    MATERIAL_CHECK(material_rejected_geometry(&invalid, DANGO_MATERIAL_NONFINITE) == 0);
    invalid = base; invalid.count = 4;
    invalid.points[0] = (dango_material_vec2){0, 0}; invalid.points[1] = (dango_material_vec2){1, 0};
    invalid.points[2] = (dango_material_vec2){1, 1}; invalid.points[3] = (dango_material_vec2){0, 1};
    material_sentinels(forces); energy = 42;
    MATERIAL_CHECK(dango_material_area(invalid.points, invalid.count, 1e300, 1e-300, 1e-100, &energy, forces) == DANGO_MATERIAL_NONFINITE);
    MATERIAL_CHECK(isnan(energy) && material_zeroed(forces, invalid.count));
    for (int field = 0; field < 3; ++field) for (int nonfinite = 0; nonfinite < 2; ++nonfinite)
    {
        double target = base.target_area, radius = base.natural_radius, strength = 4.8;
        double value = nonfinite ? NAN : -1;
        if (field == 0) target = value; else if (field == 1) radius = value; else strength = value;
        material_sentinels(forces); energy = 42;
        MATERIAL_CHECK(dango_material_area(base.points, base.count, target, radius, strength, &energy, forces) == (nonfinite ? DANGO_MATERIAL_NONFINITE : DANGO_MATERIAL_INVALID));
        MATERIAL_CHECK(isnan(energy) && material_zeroed(forces, base.count));
    }
    for (int nonfinite = 0; nonfinite < 2; ++nonfinite)
    {
        material_sentinels(forces); energy = 42;
        MATERIAL_CHECK(dango_material_bending(base.points, base.count, nonfinite ? INFINITY : -1, &energy, forces) == (nonfinite ? DANGO_MATERIAL_NONFINITE : DANGO_MATERIAL_INVALID));
        MATERIAL_CHECK(isnan(energy) && material_zeroed(forces, base.count));
    }
    material_sentinels(forces); energy = 42;
    MATERIAL_CHECK(dango_material_area(NULL, base.count, base.target_area, base.natural_radius, 4.8, &energy, forces) == DANGO_MATERIAL_INVALID);
    MATERIAL_CHECK(isnan(energy) && material_zeroed(forces, base.count));
    material_sentinels(forces); energy = 42;
    MATERIAL_CHECK(dango_material_bending(NULL, base.count, 1.7, &energy, forces) == DANGO_MATERIAL_INVALID);
    MATERIAL_CHECK(isnan(energy) && material_zeroed(forces, base.count));
    // Invalid counts must fail with otherwise valid non-null storage. Null
    // output would reject independently and could conceal a missing count gate.
    dango_material_vec2 count_points[DANGO_MATERIAL_MAX_NODES + 1];
    dango_material_vec2 count_forces[DANGO_MATERIAL_MAX_NODES + 1];
    for (int i = 0; i <= DANGO_MATERIAL_MAX_NODES; ++i)
    {
        double angle = 2 * material_pi * i / (DANGO_MATERIAL_MAX_NODES + 1);
        count_points[i] = (dango_material_vec2){cos(angle), sin(angle)};
        count_forces[i] = (dango_material_vec2){17, -31};
    }
    const int invalid_counts[] = {0, 1, 2, DANGO_MATERIAL_MAX_NODES + 1};
    for (int i = 0; i < 4; ++i)
    {
        energy = 42;
        MATERIAL_CHECK(dango_material_area(count_points, invalid_counts[i], base.target_area, base.natural_radius, 4.8, &energy, count_forces) == DANGO_MATERIAL_INVALID && isnan(energy));
        energy = 42;
        MATERIAL_CHECK(dango_material_bending(count_points, invalid_counts[i], 1.7, &energy, count_forces) == DANGO_MATERIAL_INVALID && isnan(energy));
    }
    MATERIAL_CHECK(dango_material_area(base.points, 2, base.target_area, base.natural_radius, 4.8, &energy, NULL) == DANGO_MATERIAL_INVALID && isnan(energy));
    MATERIAL_CHECK(dango_material_bending(base.points, 65, 1.7, &energy, NULL) == DANGO_MATERIAL_INVALID && isnan(energy));
    material_sentinels(forces); MATERIAL_CHECK(dango_material_area(base.points, base.count, base.target_area, base.natural_radius, 4.8, NULL, forces) == DANGO_MATERIAL_INVALID && material_zeroed(forces, base.count));
    material_sentinels(forces); MATERIAL_CHECK(dango_material_bending(base.points, base.count, 1.7, NULL, forces) == DANGO_MATERIAL_INVALID && material_zeroed(forces, base.count));
    MATERIAL_CHECK(dango_material_area(base.points, base.count, base.target_area, base.natural_radius, 4.8, &energy, NULL) == DANGO_MATERIAL_INVALID && isnan(energy));
    MATERIAL_CHECK(dango_material_bending(base.points, base.count, 1.7, &energy, NULL) == DANGO_MATERIAL_INVALID && isnan(energy));
    invalid = base;
    MATERIAL_CHECK(dango_material_area(invalid.points, invalid.count, invalid.target_area, invalid.natural_radius, 4.8, &energy, invalid.points) == DANGO_MATERIAL_INVALID);
    MATERIAL_CHECK(isnan(energy) && memcmp(invalid.points, base.points, sizeof base.points) == 0);
    MATERIAL_CHECK(dango_material_bending(invalid.points, invalid.count, 1.7, &energy, invalid.points) == DANGO_MATERIAL_INVALID);
    MATERIAL_CHECK(isnan(energy) && memcmp(invalid.points, base.points, sizeof base.points) == 0);
    puts("PASS: input/count/alias and degenerate/range rejection; documented failure outputs");
    return 0;
}

static int material_caps(void)
{
    material_fixture fixture = material_irregular(1, .713, -2.75, 4.125, false);
    dango_material_vec2 original[DANGO_MATERIAL_MAX_NODES], capped[DANGO_MATERIAL_MAX_NODES]; double energy;
    MATERIAL_CHECK(material_evaluate(&fixture, false, fixture.points, &energy, original) == DANGO_MATERIAL_OK);
    double maximum = 0;
    for (int i = 0; i < fixture.count; ++i) maximum = fmax(maximum, hypot(original[i].x, original[i].y));
    for (int mode = 0; mode < 3; ++mode)
    {
        memcpy(capped, original, sizeof capped); double cap = mode == 0 ? 0 : mode == 1 ? maximum * .55 : maximum * 2, scale = NAN;
        MATERIAL_CHECK(dango_material_cap(capped, fixture.count, cap, &scale) == DANGO_MATERIAL_OK);
        double expected = mode == 0 ? 0 : mode == 1 ? .55 : 1;
        MATERIAL_CHECK(fabs(scale - expected) < 1e-14);
        for (int i = 0; i < fixture.count; ++i)
        {
            MATERIAL_CHECK(hypot(capped[i].x, capped[i].y) <= cap + 1e-12);
            MATERIAL_CHECK(hypot(capped[i].x - original[i].x * scale, capped[i].y - original[i].y * scale) < 1e-12);
        }
        MATERIAL_CHECK(material_closed(&fixture, capped, true));
    }
    const int counts[] = {1, 2, 3, 16, 64};
    for (int c = 0; c < 5; ++c)
    {
        material_sentinels(original); memcpy(capped, original, sizeof capped); double scale;
        MATERIAL_CHECK(dango_material_cap(capped, counts[c], 2, &scale) == DANGO_MATERIAL_OK);
        for (int i = 0; i < counts[c]; ++i) MATERIAL_CHECK(fabs(hypot(capped[i].x, capped[i].y) - 2) < 1e-12);
    }
    memset(original, 0, sizeof original); double scale;
    MATERIAL_CHECK(dango_material_cap(original, 16, 0, &scale) == DANGO_MATERIAL_OK && scale == 1 && material_zeroed(original, 16));
    for (int mode = 0; mode < 4; ++mode)
    {
        material_sentinels(original);
        if (mode == 2) original[7].x = NAN;
        if (mode == 3) original[7].y = INFINITY;
        memcpy(capped, original, sizeof capped); scale = 42;
        double cap = mode == 0 ? -1 : mode == 1 ? NAN : 2;
        MATERIAL_CHECK(dango_material_cap(capped, 16, cap, &scale) == (mode == 0 ? DANGO_MATERIAL_INVALID : DANGO_MATERIAL_NONFINITE));
        MATERIAL_CHECK(isnan(scale) && memcmp(capped, original, sizeof capped) == 0);
    }
    material_sentinels(original); memcpy(capped, original, sizeof capped);
    MATERIAL_CHECK(dango_material_cap(capped, 0, 1, &scale) == DANGO_MATERIAL_INVALID && isnan(scale) && memcmp(capped, original, sizeof capped) == 0);
    MATERIAL_CHECK(dango_material_cap(capped, 65, 1, &scale) == DANGO_MATERIAL_INVALID && isnan(scale) && memcmp(capped, original, sizeof capped) == 0);
    MATERIAL_CHECK(dango_material_cap(NULL, 16, 1, &scale) == DANGO_MATERIAL_INVALID && isnan(scale));
    MATERIAL_CHECK(dango_material_cap(capped, 16, 1, NULL) == DANGO_MATERIAL_INVALID && memcmp(capped, original, sizeof capped) == 0);
    puts("PASS: common cap scale, cap/closure, zero group, 1..64 force groups, unchanged failure output");
    return 0;
}

static void material_legacy_capture(b2BodyId id, b2Vec2 force, bool wake)
{
    if (legacy_body)
    {
        for (int i = 0; i < legacy_body->node_count; ++i) if (B2_ID_EQUALS(id, legacy_body->node_body_ids[i]))
        {
            legacy_forces[i].x += force.x; legacy_forces[i].y += force.y; return;
        }
        return;
    }
    b2Body_ApplyForceToCenter(id, force, wake);
}

static bool material_old_force(const material_fixture* fixture, bool bending, dango_material_vec2* forces)
{
    dango_self_physics_config config = {
        .node_count = 16, .substep_count = 4, .radial_rest_length = .68f, .radial_minimum_ratio = .65f, .radial_maximum_ratio = 1.22f,
        .perimeter_minimum_ratio = .73f, .perimeter_maximum_ratio = 1.07f, .radial_hertz = 5.2f, .perimeter_hertz = 20.1f,
        .radial_damping = 1.85f, .perimeter_damping = 1.85f, .area_strength = 4.8f, .local_angle_balance_strength = 1.7f,
        .repulsion_maximum_force = 3.5f, .gravity = 9.8f, .core_mass = .12f, .node_mass = .015f,
        .ground_height = -10, .node_radius = .045f, .friction = .72f, .contact_hertz = 28, .maximum_speed = 12
    };
    void* handle = NULL;
    if (dango_box2d_self_soft_body_create(&config, &handle)) return false;
    legacy_body = handle; legacy_body->target_area = (float)fixture->target_area;
    b2Body_SetTransform(legacy_body->core_body_id, b2Vec2_zero, b2Rot_identity);
    for (int i = 0; i < fixture->count; ++i) b2Body_SetTransform(legacy_body->node_body_ids[i], (b2Vec2){(float)fixture->points[i].x, (float)fixture->points[i].y}, b2Rot_identity);
    memset(legacy_forces, 0, sizeof legacy_forces);
    if (bending) dango_self_apply_local_angle_balance_force(legacy_body, &config);
    else dango_self_apply_area_force(legacy_body, &config);
    memcpy(forces, legacy_forces, sizeof legacy_forces); legacy_body = NULL;
    int outstanding = 0;
    return dango_box2d_self_soft_body_destroy(handle, &outstanding) == 0 && outstanding == 0;
}

static bool material_negative_rejected(const char* mode)
{
    material_fixture fixture = material_irregular(1, 0, 0, 0, false);
    dango_material_vec2 forces[DANGO_MATERIAL_MAX_NODES]; double energy;
    if (!strcmp(mode, "--negative-area") || !strcmp(mode, "--negative-bending"))
    {
        if (!material_old_force(&fixture, !strcmp(mode, "--negative-bending"), forces)) return false;
        double fx = 0, fy = 0, torque = 0, absolute = 0, cx = 0, cy = 0;
        for (int i = 0; i < fixture.count; ++i) { cx += fixture.points[i].x; cy += fixture.points[i].y; }
        cx /= fixture.count; cy /= fixture.count;
        for (int i = 0; i < fixture.count; ++i)
        {
            fx += forces[i].x; fy += forces[i].y; absolute += hypot(forces[i].x, forces[i].y);
            torque += (fixture.points[i].x - cx) * forces[i].y - (fixture.points[i].y - cy) * forces[i].x;
        }
        // A float implementation is not a negative merely for missing a double
        // roundoff threshold. Require structural force/torque far above noise.
        double normalized = !strcmp(mode, "--negative-bending") ? fabs(torque) / (material_scale(fixture.points, fixture.count) * absolute) : hypot(fx, fy) / absolute;
        bool rejected = normalized > 1e-5 && !material_closed(&fixture, forces, false);
        printf("negative=%s old_real_static_function net_force=%.9g net_torque_com=%.9g structural_relative=%.9g rejected=%d\n", mode, hypot(fx, fy), torque, normalized, rejected);
        return rejected;
    }
    if (material_evaluate(&fixture, false, fixture.points, &energy, forces) != DANGO_MATERIAL_OK) return false;
    if (!strcmp(mode, "--negative-point-cap"))
    {
        double maximum = 0;
        for (int i = 0; i < fixture.count; ++i) maximum = fmax(maximum, hypot(forces[i].x, forces[i].y));
        for (int i = 0; i < fixture.count; ++i)
        {
            double length = hypot(forces[i].x, forces[i].y), factor = fmin(1, maximum * .55 / length);
            forces[i].x *= factor; forces[i].y *= factor;
        }
        bool rejected = !material_closed(&fixture, forces, false);
        printf("negative=%s independent_node_caps rejected=%d\n", mode, rejected); return rejected;
    }
    if (!strcmp(mode, "--negative-sign"))
    {
        for (int i = 0; i < fixture.count; ++i) { forces[i].x = -forces[i].x; forces[i].y = -forces[i].y; }
        bool rejected = !material_gradient_matches(&fixture, false, forces, false);
        printf("negative=%s reversed_gradient rejected=%d\n", mode, rejected); return rejected;
    }
    return false;
}

int main(int argc, char** argv)
{
    const char* negative_modes[] = {"--negative-area", "--negative-bending", "--negative-point-cap", "--negative-sign"};
    if (argc == 2)
    {
        for (int i = 0; i < 4; ++i) if (!strcmp(argv[1], negative_modes[i]))
        {
            // Run the real gate on the wrong implementation. Its expected result
            // is failure; the normal suite separately asserts this rejection.
            return material_negative_rejected(argv[1]) ? 1 : 0;
        }
        fprintf(stderr, "Unknown negative fixture mode.\n"); return 2;
    }
    if (argc != 1) return 2;
    MATERIAL_CHECK(material_positive_fixtures() == 0);
    MATERIAL_CHECK(material_covariance() == 0);
    MATERIAL_CHECK(material_zero_and_regular() == 0);
    MATERIAL_CHECK(material_failures() == 0);
    MATERIAL_CHECK(material_caps() == 0);
    for (int i = 0; i < 4; ++i) MATERIAL_CHECK(material_negative_rejected(negative_modes[i]));
    printf("PASS: fixed material gates; fixtures=%d max_normalized_net_force=%.9g max_normalized_net_torque=%.9g max_absolute_gradient_error=%.9g\n",
        positive_fixtures, maximum_force_residual, maximum_torque_residual, maximum_gradient_error);
    return 0;
}

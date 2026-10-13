// Independent, fixed algorithm fixtures. Never steps a Box2D world or changes
// a DLL. All thresholds are declared before running any fixture.
#include <stdio.h>
#include "dango_material_contact_solver.h"

#define MC_CHECK(c) do { ++assertions; if (!(c)) { fprintf(stderr, "FAIL %d: %s\n", __LINE__, #c); return 1; } } while (0)
static const double expected_float_tolerance = 0.000002;
static const double gradient_relative_tolerance = 0.0000003;
static const double gradient_absolute_tolerance = 0.0000002;
static const double fixture_pi = 3.1415926535897932384626433832795;
static int assertions, positive_fixtures, rejected_negative_fixtures;
static double largest_gradient_error;

static dango_constraint_state mc_ring(void)
{
    dango_constraint_state s = {0};
    s.radial_length = 1; s.perimeter_length = (float)(2 * sin(fixture_pi / 16));
    s.radial_min = .9f; s.radial_max = 1.1f;
    s.perimeter_min = .1f; s.perimeter_max = 3;
    s.radius = .045f; s.ground = -10;
    for (int i = 0; i < 16; ++i)
    {
        double phase = 2 * fixture_pi * i / 16;
        s.positions[i] = (b2Vec2){(float)cos(phase), (float)sin(phase)};
        s.inverse_mass[i] = 1;
    }
    s.positions[0] = (b2Vec2){1, 0}; s.positions[4] = (b2Vec2){0, 1};
    s.positions[8] = (b2Vec2){-1, 0}; s.positions[12] = (b2Vec2){0, -1};
    s.inverse_mass[16] = 1;
    return s;
}

static void mc_ground_at(dango_constraint_state* s, int node)
{ s->ground = s->positions[node].y - s->radius; }

static bool mc_near(double a, double b)
{ return fabs(a - b) <= expected_float_tolerance * fmax(1, fmax(fabs(a), fabs(b))); }

static int mc_success(dango_constraint_state* input, const dango_material_ground_receipt* receipts,
    int count, dango_material_contact_solver* solver, dango_constraint_state* output,
    dango_material_contact_report* report)
{
    dango_constraint_state before = *input;
    MC_CHECK(dango_material_contact_build(input, receipts, count, 1.0 / 600, solver, report));
    MC_CHECK(dango_material_contact_solve(solver, output, report));
    MC_CHECK(report->status == DANGO_MATERIAL_CONTACT_OK && report->float_output_checked);
    MC_CHECK(report->ledger_evaluated);
    MC_CHECK(report->sweeps <= 384 && report->row_visits <= 384 * 160);
    MC_CHECK(memcmp(input, &before, sizeof before) == 0);
    MC_CHECK(dango_mc_same_geometry(input, output));
    MC_CHECK(dango_material_contact_validate(solver, output, report));
    MC_CHECK(report->energy_after <= report->energy_before + report->energy_allowance);
    ++positive_fixtures;
    return 0;
}

static int distance_analytical_checks(void)
{
    for (int upper = 0; upper <= 1; ++upper)
    {
        dango_constraint_state s = mc_ring(), output;
        s.radial_min = .98f; s.radial_max = 1.02f;
        s.positions[0].x = upper ? s.radial_max : s.radial_min;
        s.inverse_mass[0] = .5f; s.inverse_mass[16] = 1.0f / 3;
        s.velocities[0].x = upper ? 2.0f : -2.0f; s.velocities[16].x = upper ? -1.0f : 1.0f;
        dango_material_contact_solver solver;
        dango_material_contact_report report;
        MC_CHECK(mc_success(&s, NULL, 0, &solver, &output, &report) == 0);
        MC_CHECK(report.active_shape_rows == 1 && report.active_normal_rows == 0);
        // Common velocity = (-4+3)/5, or its mirror; unequal masses matter.
        double expected = upper ? .2 : -.2;
        MC_CHECK(mc_near(output.velocities[0].x, expected));
        MC_CHECK(mc_near(output.velocities[16].x, expected));
        MC_CHECK(mc_near(solver.rows[upper].multiplier, 3.6));
        MC_CHECK(report.momentum_residual <= report.momentum_allowance);
        MC_CHECK(report.angular_momentum_residual <= report.angular_momentum_allowance);
        for (int i = 1; i < 16; ++i) MC_CHECK(output.velocities[i].x == 0 && output.velocities[i].y == 0);

        // The actual old implementation checks only geometrically violated rows.
        dango_constraint_state old = s;
        MC_CHECK(dango_constraint_distance_mode(&old, 16, 0, s.radial_min, s.radial_max,
            true, DANGO_CONSTRAINT_CORRECT_VELOCITY) == 0);
        MC_CHECK(!dango_material_contact_validate(&solver, &old, &report));
        MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_FINAL_RATE);
        ++rejected_negative_fixtures;
    }
    // Coupled lower bounds share the dynamic core. First-row correction changes
    // the second row and vice versa; the analytical final common velocity is 0.
    dango_constraint_state s = mc_ring(), output;
    s.radial_min = .98f;
    s.positions[0].x = s.radial_min; s.positions[8].x = -s.radial_min;
    s.inverse_mass[16] = .5f; s.velocities[0].x = -1; s.velocities[8].x = 1;
    dango_material_contact_solver solver;
    dango_material_contact_report report;
    MC_CHECK(mc_success(&s, NULL, 0, &solver, &output, &report) == 0);
    MC_CHECK(report.active_shape_rows == 2 && report.sweeps > 1);
    MC_CHECK(fabs(output.velocities[0].x) < 0.00002 && fabs(output.velocities[8].x) < 0.00002);
    MC_CHECK(fabs(output.velocities[16].x) < 0.00002);
    MC_CHECK(fabs(solver.rows[0].multiplier - 1) < .00002 && fabs(solver.rows[16].multiplier - 1) < .00002);
    return 0;
}

// The value function is independent of the solver's gradient construction.
static double mc_angle_value(const dango_material_contact_vec2* positions, double radius, int node, bool display)
{
    int ids[3] = {(node + 15) % 16, node, (node + 1) % 16};
    dango_material_contact_vec2 p[3];
    for (int i = 0; i < 3; ++i)
    {
        p[i] = positions[ids[i]];
        if (display)
        {
            double dx = p[i].x - positions[16].x, dy = p[i].y - positions[16].y;
            double factor = radius / hypot(dx, dy);
            p[i].x += factor * dx; p[i].y += factor * dy;
        }
    }
    double ax = p[1].x - p[0].x, ay = p[1].y - p[0].y;
    double bx = p[2].x - p[1].x, by = p[2].y - p[1].y;
    return atan2(ax * by - ay * bx, ax * bx + ay * by);
}

static int angle_gradient_checks(void)
{
    const double scales[3] = {.65, 1, 1.35}, rotations[3] = {-.31, 0, .47};
    const double translations[3][2] = {{-2, 3}, {0, 0}, {1.5, -2}};
    for (int fixture = 0; fixture < 3; ++fixture)
    {
        dango_constraint_state s = mc_ring();
        s.radial_length = (float)scales[fixture]; s.perimeter_length *= (float)scales[fixture]; s.radius *= (float)scales[fixture];
        s.ground = -10;
        for (int i = 0; i < 17; ++i)
        {
            double x = s.positions[i].x, y = s.positions[i].y;
            if (i < 16) { x *= .97 + .02 * sin(2 * fixture_pi * i / 16); y *= .98; }
            s.positions[i].x = (float)(translations[fixture][0] + scales[fixture] * (cos(rotations[fixture]) * x - sin(rotations[fixture]) * y));
            s.positions[i].y = (float)(translations[fixture][1] + scales[fixture] * (sin(rotations[fixture]) * x + cos(rotations[fixture]) * y));
        }
        dango_material_contact_solver solver;
        dango_material_contact_report report;
        MC_CHECK(dango_material_contact_build(&s, NULL, 0, 1.0 / 600, &solver, &report));
        dango_material_contact_vec2 points[17];
        for (int i = 0; i < 17; ++i) points[i] = dango_mc_position(&s, i);
        for (int display = 0; display <= 1; ++display) for (int node = 0; node < 16; ++node)
        {
            const dango_material_contact_row* row = &solver.rows[(display ? 96 : 64) + 2 * node];
            double step = 0.000001 * scales[fixture];
            dango_material_contact_vec2 sum = {0, 0}; double torque = 0;
            for (int j = 0; j < row->count; ++j)
            {
                int id = row->ids[j];
                sum = dango_mc_add(sum, row->gradients[j]);
                torque += dango_mc_cross(dango_mc_sub(points[id], points[16]), row->gradients[j]);
                for (int axis = 0; axis < 2; ++axis)
                {
                    double saved = axis ? points[id].y : points[id].x;
                    if (axis) points[id].y = saved + step; else points[id].x = saved + step;
                    double plus = mc_angle_value(points, s.radius, node, display != 0);
                    if (axis) points[id].y = saved - step; else points[id].x = saved - step;
                    double minus = mc_angle_value(points, s.radius, node, display != 0);
                    if (axis) points[id].y = saved; else points[id].x = saved;
                    double difference = (plus - minus) / (2 * step);
                    double analytical = axis ? row->gradients[j].y : row->gradients[j].x;
                    double error = fabs(difference - analytical);
                    largest_gradient_error = fmax(largest_gradient_error, error);
                    MC_CHECK(error <= gradient_absolute_tolerance / scales[fixture] + gradient_relative_tolerance * fabs(analytical));
                }
            }
            MC_CHECK(dango_mc_length(sum) < 0.000000000001 / scales[fixture]);
            MC_CHECK(fabs(torque) < 0.000000000001);
        }
        ++positive_fixtures;
    }
    // An exactly legal, almost-flat raw corner must project its closing rate.
    dango_constraint_state s = mc_ring(), output;
    s.positions[0].x = (float)(s.positions[1].x + s.positions[1].y * tan((double)0.002f / 2));
    s.velocities[0].x = -1;
    dango_material_contact_solver solver;
    dango_material_contact_report report;
    MC_CHECK(dango_material_contact_build(&s, NULL, 0, 1.0 / 600, &solver, &report));
    MC_CHECK(solver.rows[64].active && report.active_shape_rows == 1);
    double lambda = -dango_mc_rate(&solver.rows[64], solver.velocities) / solver.rows[64].effective_inverse_mass;
    MC_CHECK(dango_material_contact_solve(&solver, &output, &report));
    MC_CHECK(mc_near(solver.rows[64].multiplier, lambda));
    MC_CHECK(report.maximum_angular_closing <= 0.00001 && report.maximum_relative_linear_closing == 0);
    MC_CHECK(report.energy_after < report.energy_before);
    ++positive_fixtures;

    // Uneven circular sampling creates a legal near-90-degree corner. Uniform
    // radial expansion preserves its turn, activating BOTH raw and display
    // upper rows. The independent derivatives above test their different core
    // mapping on nonuniform radii; this case tests actual display-row solving.
    s = mc_ring(); s.perimeter_max = 4;
    double corner = (double)1.5687963268f;
    for (int i = 1; i < 16; ++i)
    {
        double phase = corner + (i - 1) * (2 * fixture_pi - 2 * corner) / 14;
        s.positions[i] = (b2Vec2){(float)cos(phase), (float)sin(phase)};
    }
    s.velocities[0].x = 1;
    MC_CHECK(dango_material_contact_build(&s, NULL, 0, 1.0 / 600, &solver, &report));
    MC_CHECK(solver.rows[65].active && solver.rows[97].active && report.active_shape_rows == 2);
    MC_CHECK(dango_material_contact_solve(&solver, &output, &report));
    MC_CHECK(report.maximum_angular_closing <= 0.00001 && report.energy_after < report.energy_before);
    MC_CHECK(dango_material_contact_validate(&solver, &output, &report));
    ++positive_fixtures;
    return 0;
}

static int friction_and_support_checks(void)
{
    dango_constraint_state s = mc_ring(), output;
    mc_ground_at(&s, 12);
    s.velocities[12] = (b2Vec2){-2, 0};
    dango_material_ground_receipt receipt = {12, 1, .8, 1, 999};
    dango_material_contact_solver solver;
    dango_material_contact_report report;
    MC_CHECK(mc_success(&s, &receipt, 1, &solver, &output, &report) == 0);
    MC_CHECK(mc_near(output.velocities[12].x, -1.8));
    MC_CHECK(mc_near(solver.rows[156].multiplier, .2));
    MC_CHECK(solver.rows[140].multiplier == 0);
    MC_CHECK(mc_near(report.external_impulse.x, .2) && report.external_impulse.y == 0);
    MC_CHECK(report.active_tangent_rows == 1);
    // Replay a reset-accumulator bug: repeated +.2 impulses produce an apparently
    // stopped node with a legal stored cone, but the final impulse ledger rejects.
    dango_constraint_state reset_bug = output;
    reset_bug.velocities[12].x = 0;
    MC_CHECK(!dango_material_contact_validate(&solver, &reset_bug, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_LEDGER);
    ++rejected_negative_fixtures;
    // Ignore imported tangent and spend an extra full unit: total 1.8 > 1.
    solver.rows[156].multiplier = 1;
    dango_constraint_state second_budget = s; second_budget.velocities[12].x = -1;
    MC_CHECK(!dango_material_contact_validate(&solver, &second_budget, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_FRICTION && report.maximum_friction_cone_excess > .79);
    ++rejected_negative_fixtures;

    s.velocities[12] = (b2Vec2){10, -2};
    receipt = (dango_material_ground_receipt){12, 2, -.5, .5, 400};
    MC_CHECK(mc_success(&s, &receipt, 1, &solver, &output, &report) == 0);
    MC_CHECK(mc_near(output.velocities[12].x, 8.5) && output.velocities[12].y == 0);
    MC_CHECK(mc_near(solver.rows[140].multiplier, 2) && mc_near(solver.rows[156].multiplier, -1.5));
    MC_CHECK(mc_near(report.external_impulse.y, 2)); // imported Jn0=2 never replayed
    dango_constraint_state receipt_replayed = output; receipt_replayed.velocities[12].y += 2;
    MC_CHECK(!dango_material_contact_validate(&solver, &receipt_replayed, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_COMPLEMENTARITY);
    ++rejected_negative_fixtures;

    // Touching without an actual contact still receives the geometric unilateral
    // normal safety row, but receives no fabricated friction material/receipt.
    MC_CHECK(mc_success(&s, NULL, 0, &solver, &output, &report) == 0);
    MC_CHECK(output.velocities[12].x == 10 && output.velocities[12].y == 0);
    MC_CHECK(report.active_normal_rows == 1 && report.active_tangent_rows == 0);

    // The receipt exists, but final legal geometry is separated: no support or
    // friction budget survives from that historic Box2D impulse.
    s.ground -= .1f;
    MC_CHECK(mc_success(&s, &receipt, 1, &solver, &output, &report) == 0);
    MC_CHECK(report.active_normal_rows == 0 && report.active_tangent_rows == 0);
    MC_CHECK(memcmp(output.velocities, s.velocities, sizeof s.velocities) == 0);
    MC_CHECK(report.external_impulse.x == 0 && report.external_impulse.y == 0);

    mc_ground_at(&s, 12);
    receipt = (dango_material_ground_receipt){12, 1, 0, 0, 100};
    MC_CHECK(mc_success(&s, &receipt, 1, &solver, &output, &report) == 0);
    MC_CHECK(output.velocities[12].x == 10 && output.velocities[12].y == 0);
    MC_CHECK(solver.rows[156].multiplier == 0);

    // Actual float saturation can be a few double ulps beyond mu*Jn. Accept
    // only that immutable quantization slack; telemetry remains diagnostic.
    s.velocities[12] = (b2Vec2){-.1f, 0};
    receipt = (dango_material_ground_receipt){12, .009999999776482582,
        .007200000341981649, .7200000286102295, 500};
    MC_CHECK(mc_success(&s, &receipt, 1, &solver, &output, &report) == 0);
    double known_slack = fabs(receipt.tangent_impulse) - receipt.friction * receipt.normal_impulse;
    MC_CHECK(known_slack > 0 && known_slack < .000000001);
    MC_CHECK(report.maximum_receipt_cone_slack == known_slack);
    MC_CHECK(mc_near(report.maximum_friction_cone_excess, known_slack));
    MC_CHECK(solver.rows[156].multiplier == 0 && output.velocities[12].x == s.velocities[12].x);
    receipt.tangent_impulse += .00000001;
    MC_CHECK(!dango_material_contact_build(&s, &receipt, 1, 1.0 / 600, &solver, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_RECEIPT);
    ++rejected_negative_fixtures;
    // The actual idle mix sqrtf(16*.72) has the same capture issue at larger
    // support. Zero input velocity must remain exactly zero, with zero energy.
    s.velocities[12] = (b2Vec2){0, 0};
    receipt = (dango_material_ground_receipt){12, 1.2999999523162842,
        4.412346363067627, 3.3941125869750977, 1000};
    MC_CHECK(mc_success(&s, &receipt, 1, &solver, &output, &report) == 0);
    known_slack = fabs(receipt.tangent_impulse) - receipt.friction * receipt.normal_impulse;
    MC_CHECK(known_slack > .0000001 && known_slack < .0000002);
    MC_CHECK(report.maximum_receipt_cone_slack == known_slack && report.maximum_friction_cone_excess == known_slack);
    MC_CHECK(output.velocities[12].x == 0 && output.velocities[12].y == 0);
    MC_CHECK(report.energy_before == 0 && report.energy_after == 0);
    MC_CHECK(solver.rows[140].multiplier == 0 && solver.rows[156].multiplier == 0);
    receipt.tangent_impulse += .00001;
    MC_CHECK(!dango_material_contact_build(&s, &receipt, 1, 1.0 / 600, &solver, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_RECEIPT);
    ++rejected_negative_fixtures;
    return 0;
}

static int coupled_complementarity_and_rollback_checks(void)
{
    dango_constraint_state s = mc_ring(), output;
    s.radial_max = 1.02f; s.positions[12].y = -s.radial_max; mc_ground_at(&s, 12);
    s.velocities[12].y = -3; s.velocities[16].y = -1;
    dango_material_contact_solver solver;
    dango_material_contact_report report;
    MC_CHECK(dango_material_contact_build(&s, NULL, 0, 1.0 / 600, &solver, &report));
    // First natural shape->normal sweep: all rates are nonnegative, but the
    // shape's positive lambda at opening rate 2 is not complementary.
    dango_mc_unilateral(&solver, &solver.rows[25]);
    dango_mc_contact_pair(&solver, 12);
    dango_constraint_state first = s;
    first.velocities[12] = (b2Vec2){0, (float)solver.velocities[12].y};
    first.velocities[16] = (b2Vec2){0, (float)solver.velocities[16].y};
    MC_CHECK(first.velocities[12].y == 0 && first.velocities[16].y == -2);
    MC_CHECK(!dango_material_contact_validate(&solver, &first, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_COMPLEMENTARITY && report.maximum_relative_linear_closing == 0);
    ++rejected_negative_fixtures;
    MC_CHECK(dango_material_contact_solve(&solver, &output, &report));
    MC_CHECK(output.velocities[12].y == 0 && output.velocities[16].y == -1);
    MC_CHECK(solver.rows[25].multiplier == 0 && solver.rows[140].multiplier == 3);
    ++positive_fixtures;

    s.velocities[12] = (b2Vec2){2, -1}; s.velocities[16].y = 3;
    dango_material_ground_receipt receipt = {12, 1, 0, 1, 1};
    MC_CHECK(dango_material_contact_build(&s, &receipt, 1, 1.0 / 600, &solver, &report));
    // Deliberately exercise N->T->shape once before the normal natural order.
    // At this point all nonpenetration rates pass, but extra normal is excessive.
    dango_mc_contact_pair(&solver, 12);
    dango_mc_unilateral(&solver, &solver.rows[25]);
    MC_CHECK(solver.rows[140].multiplier == 1 && solver.rows[156].multiplier == -2);
    MC_CHECK(solver.velocities[12].x == 0 && solver.velocities[12].y == 1.5 && solver.velocities[16].y == 1.5);
    first = s; first.velocities[12] = (b2Vec2){0, 1.5f}; first.velocities[16].y = 1.5f;
    MC_CHECK(!dango_material_contact_validate(&solver, &first, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_COMPLEMENTARITY);
    ++rejected_negative_fixtures;
    double energy_before_rollback = .5 * (solver.velocities[12].x * solver.velocities[12].x +
        solver.velocities[12].y * solver.velocities[12].y + solver.velocities[16].y * solver.velocities[16].y);
    dango_mc_contact_pair(&solver, 12);
    MC_CHECK(solver.rows[140].multiplier == 0 && solver.rows[156].multiplier == -1);
    MC_CHECK(solver.report.cone_retractions == 1 && solver.velocities[12].x == 1);
    // The isolated tangent rollback adds .5 kinetic energy. This is permissible
    // inside the iteration; only the entire final stage must dissipate.
    double tangent_rollback_energy = .5 * solver.velocities[12].x * solver.velocities[12].x;
    MC_CHECK(tangent_rollback_energy == .5 && energy_before_rollback == 2.25);
    MC_CHECK(dango_material_contact_solve(&solver, &output, &report));
    MC_CHECK(output.velocities[12].x == 1 && output.velocities[12].y == 1 && output.velocities[16].y == 1);
    MC_CHECK(report.energy_before == 7 && report.energy_after == 1.5);
    MC_CHECK(report.external_impulse.x == -1 && report.external_impulse.y == 0);
    // Imported Jn0=1 can coexist with separating final vn=1: normal
    // complementarity applies to extra lambda, not the immutable historical base.
    MC_CHECK(solver.rows[140].multiplier == 0 && receipt.normal_impulse == 1);
    ++positive_fixtures;

    // Natural shape->normal->tangent ordering also retracts normal and tangent
    // together. The convex lower arc is y=yA+.05(x-xA)^2; its unique lowest
    // corner A has a diagonal radial upper gradient, the core is B.
    s = mc_ring(); s.radial_min = .4f; s.radial_max = 1.02f;
    double coordinate = -(double)s.radial_max / sqrt(2);
    s.positions[10] = (b2Vec2){(float)coordinate, (float)coordinate};
    for (int i = 11; i <= 14; ++i)
    {
        double phase = 2 * fixture_pi * i / 16, cx = cos(phase), sy = sin(phase);
        double a = .05 * cx * cx, b = -.1 * cx * coordinate - sy;
        double c = .05 * coordinate * coordinate + coordinate;
        double radius = -2 * c / (b + sqrt(b * b - 4 * a * c));
        s.positions[i] = (b2Vec2){(float)(radius * cx), (float)(radius * sy)};
    }
    mc_ground_at(&s, 10); s.velocities[10] = (b2Vec2){2, -1}; s.velocities[16].y = 1;
    receipt = (dango_material_ground_receipt){10, 1, 0, 1, 1};
    MC_CHECK(mc_success(&s, &receipt, 1, &solver, &output, &report) == 0);
    MC_CHECK(report.active_shape_rows == 1 && report.cone_retractions > 0);
    MC_CHECK(mc_near(output.velocities[10].x, .5) && fabs(output.velocities[10].y) < .00002);
    MC_CHECK(mc_near(output.velocities[16].x, -.25) && mc_near(output.velocities[16].y, .75));
    MC_CHECK(mc_near(solver.rows[21].multiplier, 1 / (2 * sqrt(2))));
    MC_CHECK(mc_near(solver.rows[138].multiplier, .75) && mc_near(solver.rows[154].multiplier, -1.75));
    MC_CHECK(mc_near(report.energy_before, 3) && mc_near(report.energy_after, .4375));

    // Unequal-mass lower radial + ground has the exact all-rest solution:
    // lambda_shape=6, lambda_normal=7. Ground's last correction must not hide
    // a still-closing shape row.
    s = mc_ring(); s.radial_min = .98f; s.positions[12].y = -s.radial_min; mc_ground_at(&s, 12);
    s.inverse_mass[16] = .5f; s.velocities[12].y = -1; s.velocities[16].y = -3;
    MC_CHECK(mc_success(&s, NULL, 0, &solver, &output, &report) == 0);
    MC_CHECK(fabs(output.velocities[12].y) < .00002 && fabs(output.velocities[16].y) < .00002);
    MC_CHECK(fabs(solver.rows[24].multiplier - 6) < .00004 && fabs(solver.rows[140].multiplier - 7) < .00004);
    return 0;
}

static int uniform_speed_cap_checks(void)
{
    dango_constraint_state s = mc_ring(), contact, final;
    s.radial_min = .98f; s.positions[0].x = s.radial_min;
    s.velocities[0] = (b2Vec2){0, 12}; s.velocities[16] = (b2Vec2){12, 0};
    dango_material_contact_solver solver;
    dango_material_contact_report report;
    MC_CHECK(mc_success(&s, NULL, 0, &solver, &contact, &report) == 0);
    MC_CHECK(contact.velocities[0].x == 6 && contact.velocities[0].y == 12);
    MC_CHECK(contact.velocities[16].x == 6 && contact.velocities[16].y == 0);
    MC_CHECK(report.energy_before == 144 && report.energy_after == 108);
    float maximum = 0;
    for (int i = 0; i < 17; ++i) maximum = fmaxf(maximum, b2Length(contact.velocities[i]));
    MC_CHECK(maximum > 13.4f); // legal projection increased one body's peak speed
    float scale = (12.0f * 0.99999f) / maximum;
    final = contact;
    for (int i = 0; i < 17; ++i) final.velocities[i] = b2MulSV(scale, contact.velocities[i]);
    dango_material_contact_cap_report cap;
    MC_CHECK(dango_material_contact_validate_after_uniform_scale(&solver, &contact, &final, scale, 12, &cap));
    MC_CHECK(cap.status == DANGO_MATERIAL_CONTACT_OK && cap.contact_output_checked && cap.exact_common_scale_checked);
    MC_CHECK(cap.maximum_before == maximum && cap.maximum_after <= 12 && cap.energy_after < cap.energy_before);
    MC_CHECK(cap.cap_momentum_change.x < 0 && cap.cap_momentum_change.y < 0);
    MC_CHECK(cap.final_rows.maximum_relative_linear_closing <= .00001);
    ++positive_fixtures;
    // An in-place cap must not lose the uncapped contact output needed to verify
    // all applied contact impulses. Passing final as both snapshots is rejected.
    MC_CHECK(!dango_material_contact_validate_after_uniform_scale(&solver, &final, &final, scale, 12, &cap));
    MC_CHECK(cap.status == DANGO_MATERIAL_CONTACT_LEDGER);
    ++rejected_negative_fixtures;
    // Individual node clamping would change a legal relative velocity direction.
    dango_constraint_state individual_cap = final; individual_cap.velocities[16] = contact.velocities[16];
    MC_CHECK(!dango_material_contact_validate_after_uniform_scale(&solver, &contact, &individual_cap, scale, 12, &cap));
    MC_CHECK(cap.status == DANGO_MATERIAL_CONTACT_SPEED_CAP);
    ++rejected_negative_fixtures;
    MC_CHECK(!dango_material_contact_validate_after_uniform_scale(&solver, &contact, &final, scale * .99f, 12, &cap));
    MC_CHECK(cap.status == DANGO_MATERIAL_CONTACT_SPEED_CAP);
    ++rejected_negative_fixtures;
    MC_CHECK(!dango_material_contact_validate_after_uniform_scale(&solver, &contact, &final, 0, 12, &cap));
    MC_CHECK(cap.status == DANGO_MATERIAL_CONTACT_INVALID_INPUT);
    ++rejected_negative_fixtures;

    // No cap required: exact identity and the same independent final rows.
    s = mc_ring(); s.velocities[3] = (b2Vec2){1, 2};
    MC_CHECK(mc_success(&s, NULL, 0, &solver, &contact, &report) == 0);
    MC_CHECK(dango_material_contact_validate_after_uniform_scale(&solver, &contact, &contact, 1, 12, &cap));
    MC_CHECK(cap.maximum_before == cap.maximum_after && cap.cap_momentum_change.x == 0 && cap.cap_momentum_change.y == 0);
    ++positive_fixtures;

    s = mc_ring(); mc_ground_at(&s, 12); s.velocities[12] = (b2Vec2){10, -2};
    dango_material_ground_receipt receipt = {12, 2, -.5, .5, 300};
    MC_CHECK(mc_success(&s, &receipt, 1, &solver, &contact, &report) == 0);
    MC_CHECK(contact.velocities[12].x == 8.5f && contact.velocities[12].y == 0);
    scale = (4.0f * 0.99999f) / 8.5f; final = contact;
    for (int i = 0; i < 17; ++i) final.velocities[i] = b2MulSV(scale, contact.velocities[i]);
    MC_CHECK(dango_material_contact_validate_after_uniform_scale(&solver, &contact, &final, scale, 4, &cap));
    MC_CHECK(cap.final_rows.external_impulse.x == -1.5 && cap.final_rows.external_impulse.y == 2);
    MC_CHECK(cap.cap_momentum_change.x < -4.49 && cap.cap_momentum_change.y == 0);
    MC_CHECK(cap.final_rows.maximum_relative_friction_rate == 0 && cap.maximum_after <= 4);
    ++positive_fixtures;
    return 0;
}

static int damping_order_and_float_checks(void)
{
    dango_constraint_state s = mc_ring(), output;
    // A 45-degree lower radial row with initially tangent relative velocity.
    s.radial_min = .98f;
    double k = (double)s.radial_min / sqrt(2);
    s.positions[2] = (b2Vec2){(float)k, (float)k};
    s.velocities[2] = (b2Vec2){1, -1};
    dango_material_contact_solver solver;
    dango_material_contact_report report;
    MC_CHECK(mc_success(&s, NULL, 0, &solver, &output, &report) == 0);
    // The actual original horizontal damping formula, applied once AFTER
    // velocity adjudication, changes the tangent motion into closing motion.
    float dry_delta = 35.0f / 600.0f, decay = expf(-60.0f / 600.0f);
    for (int i = 0; i < 17; ++i) output.velocities[i].x = (output.velocities[i].x - dry_delta) * decay;
    MC_CHECK(!dango_material_contact_validate(&solver, &output, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_FINAL_RATE && report.maximum_relative_linear_closing > .06);
    ++rejected_negative_fixtures;
    // Apply that same damping once BEFORE building the final velocity stage.
    for (int i = 0; i < 17; ++i) s.velocities[i].x = (s.velocities[i].x - dry_delta) * decay;
    MC_CHECK(mc_success(&s, NULL, 0, &solver, &output, &report) == 0);

    dango_constraint_state changed_position = output; changed_position.positions[2].x += .000001f;
    MC_CHECK(!dango_material_contact_validate(&solver, &changed_position, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_INVALID_GEOMETRY);
    MC_CHECK(!report.ledger_evaluated && !report.float_output_checked);
    ++rejected_negative_fixtures;
    dango_constraint_state energy_injection = output;
    energy_injection.velocities[7].x += 10;
    MC_CHECK(!dango_material_contact_validate(&solver, &energy_injection, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_ENERGY);
    ++rejected_negative_fixtures;
    return 0;
}

static int budget_and_invalid_checks(void)
{
    dango_constraint_state s = mc_ring(), output = mc_ring(), preserved = output;
    s.radial_min = .98f; s.positions[12].y = -s.radial_min; mc_ground_at(&s, 12);
    s.inverse_mass[16] = 1e-8f; s.velocities[12].y = -1; s.velocities[16].y = -3;
    dango_material_contact_solver solver;
    dango_material_contact_report report;
    MC_CHECK(dango_material_contact_build(&s, NULL, 0, 1.0 / 600, &solver, &report));
    MC_CHECK(!dango_material_contact_solve(&solver, &output, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_BUDGET && report.sweeps == 384);
    MC_CHECK(report.maximum_relative_linear_closing > 2.99);
    MC_CHECK(fabs(solver.velocities[16].y + 3) < .00002); // tiny updates, large residual
    MC_CHECK(memcmp(&output, &preserved, sizeof output) == 0);
    ++rejected_negative_fixtures;

    // Float publication is stricter than the internal double solution. This
    // small but nondegenerate geometry has a 22.5-degree active lower row; its
    // double projection passes but rounding the two body velocities leaves a
    // substantial closing/complementarity rate relative to the fixed length.
    // The solver must use its remaining SAME budget, not return at first cast.
    // The exact power-of-two division preserves the .01-circle float direction
    // while exposing its known 1.20852e-8 world-rate cast residual at this scale.
    const float fixture_scale = .01f / 16.0f;
    s = mc_ring(); s.radial_length = fixture_scale; s.perimeter_length *= fixture_scale; s.radius *= fixture_scale; s.radial_min = .98f;
    for (int i = 0; i < 16; ++i) s.positions[i] = b2MulSV(fixture_scale, s.positions[i]);
    s.positions[1] = b2MulSV(s.radial_min, s.positions[1]);
    s.velocities[1] = (b2Vec2){0, 12}; s.velocities[16] = (b2Vec2){12, 0};
    MC_CHECK(dango_material_contact_build(&s, NULL, 0, 1.0 / 600, &solver, &report));
    MC_CHECK(solver.rows[2].active && report.active_shape_rows == 1);
    dango_mc_unilateral(&solver, &solver.rows[2]);
    dango_material_contact_solver reference;
    MC_CHECK(dango_material_contact_build(&s, NULL, 0, 1.0 / 600, &reference, &report));
    MC_CHECK(dango_mc_inspect_rows(&solver, &reference, solver.velocities, &report));
    dango_constraint_state rounded = s;
    for (int i = 0; i < 17; ++i) rounded.velocities[i] = (b2Vec2){(float)solver.velocities[i].x, (float)solver.velocities[i].y};
    MC_CHECK(!dango_material_contact_validate(&solver, &rounded, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_FINAL_RATE || report.status == DANGO_MATERIAL_CONTACT_COMPLEMENTARITY);
    MC_CHECK(!dango_material_contact_solve(&solver, &output, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_BUDGET && report.sweeps == 384 && report.float_output_checked);
    MC_CHECK(memcmp(&output, &preserved, sizeof output) == 0);
    ++rejected_negative_fixtures;

    dango_material_contact_solver preserved_solver = solver;
    s = mc_ring(); s.positions[0] = s.positions[16];
    MC_CHECK(!dango_material_contact_build(&s, NULL, 0, 1.0 / 600, &solver, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_INVALID_GEOMETRY);
    MC_CHECK(memcmp(&solver, &preserved_solver, sizeof solver) == 0);
    ++rejected_negative_fixtures;
    s = mc_ring(); s.inverse_mass[7] = 0;
    MC_CHECK(!dango_material_contact_build(&s, NULL, 0, 1.0 / 600, &solver, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_INVALID_INPUT);
    ++rejected_negative_fixtures;
    s = mc_ring(); s.velocities[3].x = NAN;
    MC_CHECK(!dango_material_contact_build(&s, NULL, 0, 1.0 / 600, &solver, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_INVALID_INPUT);
    ++rejected_negative_fixtures;
    s = mc_ring();
    MC_CHECK(!dango_material_contact_build(&s, NULL, 0, 0, &solver, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_INVALID_INPUT);
    ++rejected_negative_fixtures;
    dango_material_ground_receipt receipts[17] = {{12, 1, 0, 1, 1}, {12, 1, 0, 1, 1}};
    MC_CHECK(!dango_material_contact_build(&s, receipts, 17, 1.0 / 600, &solver, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_CAPACITY);
    ++rejected_negative_fixtures;
    MC_CHECK(!dango_material_contact_build(&s, receipts, 2, 1.0 / 600, &solver, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_RECEIPT);
    ++rejected_negative_fixtures;
    receipts[0].friction = -1;
    MC_CHECK(!dango_material_contact_build(&s, receipts, 1, 1.0 / 600, &solver, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_RECEIPT);
    ++rejected_negative_fixtures;
    receipts[0] = (dango_material_ground_receipt){12, 1, 2, 1, 1};
    MC_CHECK(!dango_material_contact_build(&s, receipts, 1, 1.0 / 600, &solver, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_RECEIPT);
    ++rejected_negative_fixtures;
    MC_CHECK(memcmp(&solver, &preserved_solver, sizeof solver) == 0);
    return 0;
}

static int report_availability_checks(void)
{
    dango_constraint_state s = mc_ring(), output;
    s.velocities[3] = (b2Vec2){1, 2};
    dango_material_contact_solver solver;
    dango_material_contact_report report;
    MC_CHECK(mc_success(&s, NULL, 0, &solver, &output, &report) == 0);
    MC_CHECK(report.ledger_evaluated && report.float_output_checked);
    // Reusing a successful report must not claim a later invalid invocation
    // evaluated energy/impulse ledgers or checked float output.
    MC_CHECK(!dango_material_contact_validate(NULL, &output, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_INVALID_INPUT && !report.ledger_evaluated && !report.float_output_checked);
    ++rejected_negative_fixtures;
    MC_CHECK(dango_material_contact_validate(&solver, &output, &report));
    MC_CHECK(report.ledger_evaluated && report.float_output_checked);
    MC_CHECK(!dango_material_contact_validate(&solver, NULL, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_INVALID_INPUT && !report.ledger_evaluated && !report.float_output_checked);
    ++rejected_negative_fixtures;
    MC_CHECK(dango_material_contact_validate(&solver, &output, &report));
    MC_CHECK(report.ledger_evaluated && report.float_output_checked);
    dango_constraint_state preserved = output;
    MC_CHECK(!dango_material_contact_solve(NULL, &output, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_INVALID_INPUT && !report.ledger_evaluated && !report.float_output_checked);
    MC_CHECK(memcmp(&output, &preserved, sizeof output) == 0);
    ++rejected_negative_fixtures;
    MC_CHECK(dango_material_contact_validate(&solver, &output, &report));
    MC_CHECK(report.ledger_evaluated && report.float_output_checked);
    MC_CHECK(!dango_material_contact_solve(&solver, NULL, &report));
    MC_CHECK(report.status == DANGO_MATERIAL_CONTACT_INVALID_INPUT && !report.ledger_evaluated && !report.float_output_checked);
    ++rejected_negative_fixtures;
    return 0;
}

int main(void)
{
    if (distance_analytical_checks() || angle_gradient_checks() || friction_and_support_checks() ||
        coupled_complementarity_and_rollback_checks() || damping_order_and_float_checks() ||
        uniform_speed_cap_checks() || budget_and_invalid_checks() || report_availability_checks()) return 1;
    printf("PASS contact solver: %d assertions; %d fixed positive fixtures; %d rejected negatives; gradient error %.12g; fixed budget 384\n",
        assertions, positive_fixtures, rejected_negative_fixtures, largest_gradient_error);
    return 0;
}

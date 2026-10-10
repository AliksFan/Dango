// Fixed independent fixtures for the approved 16-row / 32-mode candidate.
// No Box2D world, DLL, tunable budget, or parameter scan is used here.
#include <stdio.h>
#include "dango_material_contact_block.h"
#include "dango_material_contact_block_frame29.h"

#define BC_CHECK(c) do { ++bc_assertions; if (!(c)) { fprintf(stderr, "FAIL block %d: %s\n", __LINE__, #c); return 1; } } while (0)
static int bc_assertions, bc_positives, bc_negatives;
static const double bc_pi = 3.1415926535897932384626433832795;

static dango_constraint_state bc_ring(void)
{
    dango_constraint_state s = {0};
    s.radial_length = 1; s.perimeter_length = (float)(2 * sin(bc_pi / 16));
    s.radial_min = .9f; s.radial_max = 1.1f;
    s.perimeter_min = .1f; s.perimeter_max = 3;
    s.radius = .045f; s.ground = -10;
    for (int i = 0; i < 16; ++i)
    {
        double angle = 2 * bc_pi * i / 16;
        s.positions[i] = (b2Vec2){(float)cos(angle), (float)sin(angle)};
        s.inverse_mass[i] = 1;
    }
    s.positions[0] = (b2Vec2){1, 0}; s.positions[4] = (b2Vec2){0, 1};
    s.positions[8] = (b2Vec2){-1, 0}; s.positions[12] = (b2Vec2){0, -1};
    s.inverse_mass[16] = 1;
    return s;
}

static void bc_ground(dango_constraint_state* s, int node)
{ s->ground = s->positions[node].y - s->radius; }

static bool bc_near(double a, double b)
{ return fabs(a - b) <= .000002 * fmax(1, fmax(fabs(a), fabs(b))); }

// Independent directional derivatives of physical boundary values. Gradients
// are never read from the candidate when recomputing rates or body impulses.
static void bc_display(const dango_constraint_state* s, const double v[17][2], int node,
    bool display, double p[2], double u[2])
{
    p[0] = s->positions[node].x; p[1] = s->positions[node].y;
    u[0] = v[node][0]; u[1] = v[node][1];
    if (!display) return;
    double x = p[0] - s->positions[16].x, y = p[1] - s->positions[16].y;
    double vx = u[0] - v[16][0], vy = u[1] - v[16][1];
    double length = hypot(x, y), contraction = (x * vx + y * vy) / (length * length);
    p[0] += s->radius * x / length; p[1] += s->radius * y / length;
    u[0] += s->radius * (vx - x * contraction) / length;
    u[1] += s->radius * (vy - y * contraction) / length;
}

static double bc_boundary_rate(const dango_constraint_state* s, const double v[17][2],
    int row, double* gap)
{
    if (row < 64)
    {
        int node = row < 32 ? row / 2 : (row - 32) / 2;
        int a = row < 32 ? 16 : node, b = row < 32 ? node : (node + 1) % 16;
        double x = (double)s->positions[b].x - s->positions[a].x;
        double y = (double)s->positions[b].y - s->positions[a].y, length = hypot(x, y);
        double rate = (x * (v[b][0] - v[a][0]) + y * (v[b][1] - v[a][1])) / length;
        double low = row < 32 ? (double)(s->radial_length * s->radial_min) : (double)(s->perimeter_length * s->perimeter_min);
        double high = row < 32 ? (double)(s->radial_length * s->radial_max) : (double)(s->perimeter_length * s->perimeter_max);
        *gap = row % 2 ? high - length : length - low;
        return row % 2 ? -rate : rate;
    }
    if (row < 128)
    {
        int node = row < 96 ? (row - 64) / 2 : (row - 96) / 2;
        double p[3][2], u[3][2];
        for (int i = 0; i < 3; ++i) bc_display(s, v, (node + 15 + i) % 16, row >= 96, p[i], u[i]);
        double ex = p[1][0] - p[0][0], ey = p[1][1] - p[0][1];
        double fx = p[2][0] - p[1][0], fy = p[2][1] - p[1][1];
        double dex = u[1][0] - u[0][0], dey = u[1][1] - u[0][1];
        double dfx = u[2][0] - u[1][0], dfy = u[2][1] - u[1][1];
        double angle = atan2(ex * fy - ey * fx, ex * fx + ey * fy);
        double rate = (fx * dfy - fy * dfx) / (fx * fx + fy * fy) - (ex * dey - ey * dex) / (ex * ex + ey * ey);
        *gap = row % 2 ? (double)1.5687963268f - angle : angle - (double).002f;
        return row % 2 ? -rate : rate;
    }
    int node = row < 144 ? row - 128 : row - 144;
    *gap = (double)s->positions[node].y - (double)(s->ground + s->radius);
    return v[node][row < 144 ? 1 : 0];
}

static int bc_independent_final(const dango_material_contact_block* block,
    const dango_constraint_state* published, bool float_state)
{
    const dango_material_contact_solver* contact = &block->contact;
    const dango_constraint_state* s = &contact->geometry;
    double v[17][2], delta[17][2] = {{0}}, impulse_scale[17] = {0};
    double origin[2] = {s->positions[16].x, s->positions[16].y};
    double energy0 = 0, energy1 = 0, pchange[2] = {0}, external[2] = {0}, angular = 0, torque = 0;
    double mass_sum = 0, total_impulse_scale = 0, torque_scale = 0;
    for (int n = 0; n < 17; ++n)
    {
        v[n][0] = float_state ? published->velocities[n].x : contact->velocities[n].x;
        v[n][1] = float_state ? published->velocities[n].y : contact->velocities[n].y;
        impulse_scale[n] = hypot(contact->input_velocities[n].x, contact->input_velocities[n].y);
    }
    for (int row = 0; row < 160; ++row)
    {
        double gap, rate = bc_boundary_rate(s, v, row, &gap);
        double tolerance = row >= 64 && row < 128 ? DANGO_CONSTRAINT_TOLERANCE : DANGO_CONSTRAINT_TOLERANCE * s->radial_length;
        bool active = gap <= tolerance;
        int node = row - 144;
        const dango_material_contact_support* support = node >= 0 ? &contact->support[node] : NULL;
        if (row >= 144) active = active && support->receipt_present;
        double lambda = contact->rows[row].multiplier;
        BC_CHECK(isfinite(gap) && isfinite(rate) && isfinite(lambda));
        BC_CHECK(active == contact->rows[row].active);
        BC_CHECK(fabs(gap - contact->rows[row].gap) <= 2e-12 * fmax(1, fabs(gap)));
        if (!active) { BC_CHECK(lambda == 0); continue; }
        if (!float_state)
            BC_CHECK(fabs(rate - block->report.all_row_rates[row]) <= 2e-12 * fmax(1, fabs(rate)));
        if (row < 144)
        {
            double normalized = row >= 64 && row < 128 ? rate : rate / s->radial_length;
            BC_CHECK(lambda >= 0);
            BC_CHECK(normalized >= -DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
            if (lambda > 0) BC_CHECK(normalized <= DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
        }
        else
        {
            double physical = support->friction * (support->base_normal + contact->rows[128 + node].multiplier);
            double budget = physical + support->receipt_cone_slack, total = support->base_tangent + lambda;
            double eps = 32 * DBL_EPSILON * fmax(budget, fabs(total));
            BC_CHECK(budget >= 0 && fabs(total) <= budget + eps);
            if (budget > 0)
            {
                if (total <= -budget + eps) BC_CHECK(rate / s->radial_length >= -DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
                else if (total >= budget - eps) BC_CHECK(rate / s->radial_length <= DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
                else BC_CHECK(fabs(rate) / s->radial_length <= DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
            }
        }
        // Evaluate each unit direction through the independent boundary value.
        // This reconstructs Ji without reading cached candidate gradients.
        for (int n = 0; n < 17; ++n)
        {
            double gradient[2];
            for (int axis = 0; axis < 2; ++axis)
            {
                double basis[17][2] = {{0}}, ignored;
                basis[n][axis] = 1; gradient[axis] = bc_boundary_rate(s, basis, row, &ignored);
                delta[n][axis] += lambda * s->inverse_mass[n] * gradient[axis];
            }
            impulse_scale[n] += fabs(lambda) * s->inverse_mass[n] * hypot(gradient[0], gradient[1]);
        }
    }
    double rounding = float_state ? 8 * FLT_EPSILON : 128 * DBL_EPSILON;
    for (int n = 0; n < 17; ++n)
    {
        double mass = 1.0 / s->inverse_mass[n], x = s->positions[n].x - origin[0], y = s->positions[n].y - origin[1];
        double vx0 = contact->input_velocities[n].x, vy0 = contact->input_velocities[n].y;
        double rx = v[n][0] - vx0 - delta[n][0], ry = v[n][1] - vy0 - delta[n][1];
        double allowance = rounding * fmax(impulse_scale[n], s->radial_length * DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
        // Extra 2e-13 only bounds our independently ordered arithmetic. The
        // unchanged original validator below enforces the original allowance.
        BC_CHECK(hypot(rx, ry) <= allowance + 2e-13 * fmax(1, impulse_scale[n]));
        energy0 += .5 * mass * (vx0 * vx0 + vy0 * vy0);
        energy1 += .5 * mass * (v[n][0] * v[n][0] + v[n][1] * v[n][1]);
        double px = mass * (v[n][0] - vx0), py = mass * (v[n][1] - vy0);
        pchange[0] += px; pchange[1] += py; angular += x * py - y * px;
        mass_sum += mass;
        double sum = mass * (hypot(vx0, vy0) + hypot(v[n][0], v[n][1]));
        total_impulse_scale += sum; torque_scale += hypot(x, y) * sum;
        if (n < 16)
        {
            double ix = contact->rows[144 + n].multiplier, iy = contact->rows[128 + n].multiplier;
            external[0] += ix; external[1] += iy; torque += x * iy - y * ix;
            total_impulse_scale += hypot(ix, iy); torque_scale += hypot(x, y) * hypot(ix, iy);
        }
    }
    double floor = mass_sum * s->radial_length * s->radial_length * 1e-10;
    BC_CHECK(energy1 <= energy0 + rounding * fmax(energy0, floor));
    BC_CHECK(hypot(pchange[0] - external[0], pchange[1] - external[1]) <= rounding * fmax(total_impulse_scale, mass_sum * s->radial_length * 1e-5) + 2e-13);
    BC_CHECK(fabs(angular - torque) <= rounding * fmax(torque_scale, mass_sum * s->radial_length * s->radial_length * 1e-5) + 2e-13);
    return 0;
}
static int bc_success(const dango_constraint_state* input, const dango_material_ground_receipt* receipts,
    int count, double dt, dango_material_contact_block* block, dango_constraint_state* output)
{
    dango_constraint_state saved = *input;
    dango_material_contact_block_report report;
    BC_CHECK(dango_material_contact_block_build(input, receipts, count, dt, block, &report));
    BC_CHECK(report.capacity == 16 && report.solve_limit == 32 && report.active_count <= 16);
    BC_CHECK(dango_material_contact_block_solve(block, output, &report));
    BC_CHECK(report.status == DANGO_MATERIAL_CONTACT_BLOCK_OK);
    BC_CHECK(memcmp(input, &saved, sizeof(saved)) == 0);
    BC_CHECK(report.linear_solves <= 32 && report.float_checks == 1);
    BC_CHECK(report.mode_history_count == report.linear_solves);
    BC_CHECK(report.full_checks == (report.active_count ? report.linear_solves : 1));
    BC_CHECK(report.matrix_dimension == report.active_count);
    BC_CHECK(report.row_rates_available && report.rates_mode_index == report.mode_history_count - 1);
    BC_CHECK(report.matrix_available == (report.active_count > 0));
    BC_CHECK(report.linear_solution_available == (report.active_count > 0));
    if (report.active_count > 0)
    {
        BC_CHECK(report.matrix_mode_index == report.mode_history_count - 1);
        BC_CHECK(report.solution_mode_index == report.mode_history_count - 1);
    }
    uint64_t n = (uint64_t)report.active_count;
    BC_CHECK(report.pivot_searches == (uint64_t)report.linear_solves * n * (n+1) * (2*n+1) / 6);
    BC_CHECK(report.pivot_eliminations == (uint64_t)report.linear_solves * n);
    BC_CHECK(report.final_rows.ledger_evaluated && report.final_rows.float_output_checked);
    BC_CHECK(report.time_available && isfinite(report.elapsed_seconds) && report.elapsed_seconds >= 0);
    BC_CHECK(bc_independent_final(block, output, false) == 0);
    BC_CHECK(bc_independent_final(block, output, true) == 0);
    dango_material_contact_report checked;
    BC_CHECK(dango_material_contact_validate(&block->contact, output, &checked));
    ++bc_positives;
    return 0;
}

static int bc_mode_index(const dango_material_contact_block_report* report, int row)
{
    for (int i = 0; i < report->active_count; ++i) if (report->active_row_ids[i] == row) return i;
    return -1;
}

static int bc_cap(const dango_material_contact_block* block, const dango_constraint_state* output, float maximum_speed)
{
    float maximum = 0;
    for (int n = 0; n < 17; ++n) maximum = fmaxf(maximum, b2Length(output->velocities[n]));
    float scale = maximum > maximum_speed ? (maximum_speed * .99999f) / maximum : 1;
    dango_constraint_state limited = *output;
    for (int n = 0; n < 17; ++n) limited.velocities[n] = b2MulSV(scale, output->velocities[n]);
    dango_material_contact_cap_report cap;
    BC_CHECK(dango_material_contact_validate_after_uniform_scale(&block->contact, output, &limited, scale, maximum_speed, &cap));
    BC_CHECK(dango_material_contact_block_validate_after_uniform_scale(block, output, &limited, scale, maximum_speed, &cap));
    BC_CHECK(cap.maximum_after <= maximum_speed && cap.exact_common_scale_checked && cap.contact_output_checked);
    BC_CHECK(cap.energy_after <= cap.energy_before + cap.energy_allowance);
    return 0;
}

static int bc_final_cap_modes(void)
{
    // Fixed audit counterexample. The unscaled lower diagonal rate is exactly
    // zero with lambda=0. Float common scaling opens the enabled row beyond
    // 1e-5 /s; old unilateral complementarity alone permits that opening.
    dango_constraint_state s=bc_ring(),output;
    const float length=.01f/16;
    s.radial_length=length; s.perimeter_length*=length; s.radius*=length; s.radial_min=.98f;
    for (int n=0;n<16;++n) s.positions[n]=b2MulSV(length,s.positions[n]);
    s.positions[2]=b2MulSV(s.radial_min,s.positions[2]);
    s.velocities[2]=(b2Vec2){10,0}; s.velocities[16]=(b2Vec2){2,8};
    dango_material_contact_block block,saved;
    BC_CHECK(bc_success(&s,NULL,0,1.0/600,&block,&output)==0);
    BC_CHECK(block.report.active_count==1 && block.report.active_row_ids[0]==4);
    BC_CHECK(block.report.modes[0]==DANGO_MATERIAL_CONTACT_BLOCK_ENABLED && block.contact.rows[4].multiplier==0);
    memcpy(&saved,&block,sizeof saved);
    const float scale=(1.0f*.99999f)/10.0f;
    dango_constraint_state limited=output;
    double v[17][2];
    for (int n=0;n<17;++n)
    {
        limited.velocities[n]=b2MulSV(scale,output.velocities[n]);
        v[n][0]=limited.velocities[n].x; v[n][1]=limited.velocities[n].y;
    }
    double gap,opening=bc_boundary_rate(&s,v,4,&gap)/length;
    BC_CHECK(opening>DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
    dango_constraint_state saved_output,saved_limited;
    memcpy(&saved_output,&output,sizeof output); memcpy(&saved_limited,&limited,sizeof limited);
    dango_material_contact_cap_report cap;
    BC_CHECK(dango_material_contact_validate_after_uniform_scale(&block.contact,&output,&limited,scale,1,&cap));
    BC_CHECK(!dango_material_contact_block_validate_after_uniform_scale(&block,&output,&limited,scale,1,&cap));
    BC_CHECK(cap.status==DANGO_MATERIAL_CONTACT_COMPLEMENTARITY && cap.final_rows.status==DANGO_MATERIAL_CONTACT_COMPLEMENTARITY);
    BC_CHECK(memcmp(&block,&saved,sizeof block)==0); ++bc_negatives;
    BC_CHECK(memcmp(&output,&saved_output,sizeof output)==0 && memcmp(&limited,&saved_limited,sizeof limited)==0);
    printf("fixed cap mode counterexample: enabled radial row4 lambda=0; opening/L=%.17g; old accepted / block rejected\n",opening);

    // A real nontrivial common scale with an axis-aligned enabled row stays
    // exactly rate-zero after publication and must pass the full block gate.
    s=bc_ring(); s.radial_min=.98f; s.positions[0].x=s.radial_min;
    s.velocities[0]=(b2Vec2){0,12}; s.velocities[16]=(b2Vec2){12,0};
    BC_CHECK(bc_success(&s,NULL,0,1.0/600,&block,&output)==0);
    BC_CHECK(output.velocities[0].x==6 && output.velocities[0].y==12 && output.velocities[16].x==6);
    float maximum=0;
    for (int n=0;n<17;++n) maximum=fmaxf(maximum,b2Length(output.velocities[n]));
    float legal_scale=(12.0f*.99999f)/maximum; BC_CHECK(legal_scale>0 && legal_scale<1);
    limited=output;
    for (int n=0;n<17;++n) limited.velocities[n]=b2MulSV(legal_scale,output.velocities[n]);
    memcpy(&saved,&block,sizeof saved);
    memcpy(&saved_output,&output,sizeof output); memcpy(&saved_limited,&limited,sizeof limited);
    BC_CHECK(dango_material_contact_block_validate_after_uniform_scale(&block,&output,&limited,legal_scale,12,&cap));
    BC_CHECK(cap.maximum_after<=12 && cap.exact_common_scale_checked && cap.contact_output_checked);
    BC_CHECK(memcmp(&block,&saved,sizeof block)==0); ++bc_positives;
    BC_CHECK(memcmp(&output,&saved_output,sizeof output)==0 && memcmp(&limited,&saved_limited,sizeof limited)==0);
    // A plausible but noncanonical common factor is rejected without editing
    // the original block state or its unscaled energy/momentum ledger.
    float wrong_scale=legal_scale*.99f;
    for (int n=0;n<17;++n) limited.velocities[n]=b2MulSV(wrong_scale,output.velocities[n]);
    memcpy(&saved_limited,&limited,sizeof limited);
    BC_CHECK(!dango_material_contact_block_validate_after_uniform_scale(&block,&output,&limited,wrong_scale,12,&cap));
    BC_CHECK(cap.status==DANGO_MATERIAL_CONTACT_SPEED_CAP);
    BC_CHECK(memcmp(&block,&saved,sizeof block)==0); ++bc_negatives;
    BC_CHECK(memcmp(&output,&saved_output,sizeof output)==0 && memcmp(&limited,&saved_limited,sizeof limited)==0);
    return 0;
}

static int bc_frame29(void)
{
    dango_constraint_state s = dango_material_contact_frame29_state(), output = bc_ring(), saved = output;
    dango_material_contact_solver legacy;
    dango_material_contact_report old_report;
    BC_CHECK(dango_material_contact_build(&s, dango_material_contact_frame29_receipts, 4, dango_material_contact_frame29_step_seconds, &legacy, &old_report));
    BC_CHECK(!dango_material_contact_solve(&legacy, &output, &old_report));
    BC_CHECK(old_report.status == DANGO_MATERIAL_CONTACT_BUDGET && old_report.sweeps == 384);
    BC_CHECK(old_report.maximum_angular_closing > 1e-5 && !old_report.ledger_evaluated);
    BC_CHECK(memcmp(&output, &saved, sizeof output) == 0); ++bc_negatives;
    dango_material_contact_block block;
    BC_CHECK(bc_success(&s, dango_material_contact_frame29_receipts, 4, dango_material_contact_frame29_step_seconds, &block, &output) == 0);
    const int expected[8] = {22,24,86,88,139,140,155,156};
    BC_CHECK(block.report.active_count == 8);
    for (int i = 0; i < 8; ++i) BC_CHECK(block.report.active_row_ids[i] == expected[i]);
    BC_CHECK(block.contact.receipt_count == 4);
    BC_CHECK(memcmp(block.contact.receipts, dango_material_contact_frame29_receipts, sizeof dango_material_contact_frame29_receipts) == 0);
    BC_CHECK(bc_cap(&block, &output, 12) == 0);
    printf("frame29 block n=%d solves=%d full=%d float=%d angular=%.17g complementarity=%.17g energy=%.17g->%.17g pivots=%llu elapsed=%.9g; old384 rejected\n",
        block.report.active_count,block.report.linear_solves,block.report.full_checks,block.report.float_checks,
        block.report.final_rows.maximum_angular_closing,block.report.final_rows.maximum_angular_complementarity,
        block.report.final_rows.energy_before,block.report.final_rows.energy_after,
        (unsigned long long)block.report.pivot_eliminations,block.report.elapsed_seconds);
    return 0;
}

static int bc_radial_modes(void)
{
    for (int upper = 0; upper < 2; ++upper)
    {
        dango_constraint_state s = bc_ring(), output;
        s.radial_min = .98f; s.radial_max = 1.02f; s.positions[0].x = upper ? s.radial_max : s.radial_min;
        s.inverse_mass[0] = .5f; s.inverse_mass[16] = 1.0f / 3;
        s.velocities[0].x = upper ? 2.0f : -2.0f; s.velocities[16].x = upper ? -1.0f : 1.0f;
        dango_material_contact_block block;
        BC_CHECK(bc_success(&s, NULL, 0, 1.0 / 600, &block, &output) == 0);
        BC_CHECK(bc_near(output.velocities[0].x, upper ? .2 : -.2));
        BC_CHECK(bc_near(output.velocities[16].x, upper ? .2 : -.2));
        BC_CHECK(bc_near(block.contact.rows[upper].multiplier, 3.6));
        // Opening boundary must release instead of pulling the bodies together.
        s.velocities[0].x = -s.velocities[0].x; s.velocities[16].x = -s.velocities[16].x;
        BC_CHECK(bc_success(&s, NULL, 0, 1.0 / 600, &block, &output) == 0);
        BC_CHECK(block.contact.rows[upper].multiplier == 0);
        BC_CHECK(memcmp(output.velocities, s.velocities, sizeof s.velocities) == 0);
        BC_CHECK(block.report.linear_solves == 2 && block.report.modes[0] == DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
    }
    // Gii=2, Gij=cos(theta_i-theta_j), q=(-2,-4,2). Independent
    // linear algebra predicts EEE -> REE -> RER -> EER; row 0 restarts.
    dango_constraint_state s = bc_ring(), output;
    const int nodes[3] = {0,2,5}; const double q[3] = {-2,-4,2}; s.radial_min = .98f;
    for (int i = 0; i < 3; ++i)
    {
        int n = nodes[i]; double x = s.positions[n].x, y = s.positions[n].y, length = hypot(x,y);
        s.positions[n] = (b2Vec2){(float)(s.radial_min*x/length),(float)(s.radial_min*y/length)};
        s.velocities[n] = (b2Vec2){(float)(q[i]*x/length),(float)(q[i]*y/length)};
    }
    dango_material_contact_block block;
    BC_CHECK(bc_success(&s, NULL, 0, 1.0 / 600, &block, &output) == 0);
    BC_CHECK(block.report.active_count == 3 && block.report.linear_solves == 4);
    int zero = bc_mode_index(&block.report,0), five = bc_mode_index(&block.report,10);
    BC_CHECK(zero >= 0 && five >= 0);
    BC_CHECK(block.report.mode_history[0][zero] == DANGO_MATERIAL_CONTACT_BLOCK_ENABLED);
    BC_CHECK(block.report.mode_history[1][zero] == DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
    BC_CHECK(block.report.mode_history[2][five] == DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
    BC_CHECK(block.report.mode_history[3][zero] == DANGO_MATERIAL_CONTACT_BLOCK_ENABLED);
    BC_CHECK(block.contact.rows[10].multiplier == 0 && block.contact.rows[0].multiplier > 0);
    return 0;
}

static int bc_friction_modes(void)
{
    for (int sign = -1; sign <= 1; sign += 2)
    {
        dango_constraint_state s = bc_ring(), output; bc_ground(&s,12);
        s.velocities[12] = (b2Vec2){(float)(sign * 10),-2};
        dango_material_ground_receipt receipt = {12,2,sign * .5,.5,400};
        dango_material_contact_block block;
        BC_CHECK(bc_success(&s,&receipt,1,1.0/600,&block,&output) == 0);
        // The velocity already includes N0=2,T0=+/-.5. Only deltas apply:
        // extra N=2, total T=-/+2, extra T=-/+2.5, |vx|=7.5.
        BC_CHECK(bc_near(output.velocities[12].x,sign * 7.5) && output.velocities[12].y == 0);
        BC_CHECK(bc_near(block.contact.rows[140].multiplier,2));
        BC_CHECK(bc_near(block.contact.rows[156].multiplier,-sign * 2.5));
        int t = bc_mode_index(&block.report,156); BC_CHECK(t >= 0);
        BC_CHECK(block.report.modes[t] == (sign > 0 ? DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE : DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE));
        BC_CHECK(bc_cap(&block,&output,4) == 0);
        // A legal-looking final cone cannot disguise replaying the receipt.
        dango_constraint_state replay = output; replay.velocities[12].y += 2;
        dango_material_contact_report reject;
        BC_CHECK(!dango_material_contact_validate(&block.contact,&replay,&reject)); ++bc_negatives;
    }
    // Exact cone boundary at vt=0 is legal; a small unsaturated speed sticks.
    for (int boundary = 0; boundary <= 1; ++boundary)
    {
        dango_constraint_state s = bc_ring(), output; bc_ground(&s,12);
        s.velocities[12] = (b2Vec2){boundary ? -1.0f : -.25f,0};
        dango_material_ground_receipt receipt = {12,1,0,1,99};
        dango_material_contact_block block;
        BC_CHECK(bc_success(&s,&receipt,1,1.0/600,&block,&output) == 0);
        BC_CHECK(output.velocities[12].x == 0 && output.velocities[12].y == 0);
        BC_CHECK(block.contact.rows[140].multiplier == 0);
        BC_CHECK(bc_near(block.contact.rows[156].multiplier,boundary ? 1 : .25));
    }
    // μ=0 always has zero cumulative tangent budget. N0=0 with μ>0
    // starts ZERO, but downward velocity creates extra support and enables T.
    for (int kind = 0; kind < 3; ++kind)
    {
        dango_constraint_state s = bc_ring(), output; bc_ground(&s,12);
        s.velocities[12] = (b2Vec2){2,kind == 1 ? -2.0f : 0};
        dango_material_ground_receipt receipt = {12,kind == 0 ? 1 : 0,0,kind == 0 ? 0 : 1,100};
        dango_material_contact_block block;
        BC_CHECK(bc_success(&s,&receipt,1,1.0/600,&block,&output) == 0);
        int t = bc_mode_index(&block.report,156); BC_CHECK(t >= 0);
        BC_CHECK(block.report.mode_history[0][t] == DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
        if (kind == 1)
        {
            BC_CHECK(output.velocities[12].x == 0 && output.velocities[12].y == 0);
            BC_CHECK(block.contact.rows[140].multiplier == 2 && block.contact.rows[156].multiplier == -2);
            BC_CHECK(block.report.modes[t] == DANGO_MATERIAL_CONTACT_BLOCK_STICK);
        }
        else
        {
            BC_CHECK(output.velocities[12].x == 2 && block.contact.rows[156].multiplier == 0);
            BC_CHECK(block.report.modes[t] == DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
        }
    }
    // Current receipt contract rejects any nonzero T when μ=0.
    dango_constraint_state s = bc_ring(), output = s, saved = output; bc_ground(&s,12);
    dango_material_ground_receipt receipt = {12,1,.001,0,100};
    dango_material_contact_block block;
    dango_material_contact_block_report report;
    BC_CHECK(!dango_material_contact_block_build(&s,&receipt,1,1.0/600,&block,&report));
    BC_CHECK(report.status == DANGO_MATERIAL_CONTACT_BLOCK_BUILD && report.final_rows.status == DANGO_MATERIAL_CONTACT_RECEIPT); ++bc_negatives;
    BC_CHECK(memcmp(&output,&saved,sizeof output) == 0);

    // Captured float receipt has immutable cone surplus, not a fresh allowance.
    s.velocities[12] = (b2Vec2){0,0};
    receipt = (dango_material_ground_receipt){12,1.2999999523162842,4.412346363067627,3.3941125869750977,1000};
    BC_CHECK(bc_success(&s,&receipt,1,1.0/600,&block,&output) == 0);
    double slack = fabs(receipt.tangent_impulse) - receipt.friction * receipt.normal_impulse;
    BC_CHECK(slack > 1e-7 && slack < 2e-7);
    BC_CHECK(block.contact.support[12].receipt_cone_slack == slack);
    BC_CHECK(output.velocities[12].x == 0 && output.velocities[12].y == 0);
    BC_CHECK(block.contact.rows[140].multiplier == 0 && block.contact.rows[156].multiplier == 0);
    receipt.tangent_impulse += .00001;
    BC_CHECK(!dango_material_contact_block_build(&s,&receipt,1,1.0/600,&block,&report));
    BC_CHECK(report.final_rows.status == DANGO_MATERIAL_CONTACT_RECEIPT); ++bc_negatives;

    // A historical receipt does not grant current separated geometry support.
    s.velocities[12] = (b2Vec2){4,-2}; s.ground -= .1f;
    receipt = (dango_material_ground_receipt){12,1,.5,1,1000};
    BC_CHECK(bc_success(&s,&receipt,1,1.0/600,&block,&output) == 0);
    BC_CHECK(block.report.active_count == 0 && block.report.linear_solves == 0);
    BC_CHECK(memcmp(output.velocities,s.velocities,sizeof s.velocities) == 0);
    BC_CHECK(block.contact.rows[140].multiplier == 0 && block.contact.rows[156].multiplier == 0);
    // Pure geometric touching has only N, no invented friction material.
    bc_ground(&s,12);
    BC_CHECK(bc_success(&s,NULL,0,1.0/600,&block,&output) == 0);
    BC_CHECK(output.velocities[12].x == 4 && output.velocities[12].y == 0);
    BC_CHECK(block.report.active_count == 1 && !block.contact.rows[156].active);
    return 0;
}

static int bc_coupled_contact(void)
{
    // Opening radial upper row and ground initially both enabled. The exact
    // solution must release radial reaction instead of pulling the core up.
    dango_constraint_state s = bc_ring(), output;
    s.radial_max = 1.02f; s.positions[12].y = -s.radial_max; bc_ground(&s,12);
    s.velocities[12].y = -3; s.velocities[16].y = -1;
    dango_material_contact_block block;
    BC_CHECK(bc_success(&s,NULL,0,1.0/600,&block,&output) == 0);
    BC_CHECK(output.velocities[12].y == 0 && output.velocities[16].y == -1);
    BC_CHECK(block.contact.rows[25].multiplier == 0 && block.contact.rows[140].multiplier == 3);
    // Imported normal remains historical even when final normal separates.
    s.velocities[12] = (b2Vec2){2,-1}; s.velocities[16].y = 3;
    dango_material_ground_receipt receipt = {12,1,0,1,1};
    BC_CHECK(bc_success(&s,&receipt,1,1.0/600,&block,&output) == 0);
    BC_CHECK(output.velocities[12].x == 1 && output.velocities[12].y == 1 && output.velocities[16].y == 1);
    BC_CHECK(block.contact.rows[140].multiplier == 0 && block.contact.rows[156].multiplier == -1);
    BC_CHECK(block.report.final_rows.energy_before == 7 && block.report.final_rows.energy_after == 1.5);
    return 0;
}

static int bc_guard_failures(void)
{
    // These are the identical guards invoked before every real linear solve.
    // We feed distinct actual vectors; no count/limit is forced or modified.
    dango_material_contact_block_report report = {0}; report.active_count = 6;
    for (int i = 0; i < 6; ++i) report.modes[i] = DANGO_MATERIAL_CONTACT_BLOCK_ENABLED;
    BC_CHECK(dango_mcb_record_modes(&report));
    BC_CHECK(report.mode_history_count == 1);
    BC_CHECK(!dango_mcb_record_modes(&report));
    BC_CHECK(report.status == DANGO_MATERIAL_CONTACT_BLOCK_REPEATED_MODE && report.mode_history_count == 1); ++bc_negatives;
    memset(&report,0,sizeof report); report.active_count = 6;
    for (int vector = 0; vector < 32; ++vector)
    {
        for (int i = 0; i < 6; ++i) report.modes[i] = vector & (1 << i) ? DANGO_MATERIAL_CONTACT_BLOCK_RELEASED : DANGO_MATERIAL_CONTACT_BLOCK_ENABLED;
        BC_CHECK(dango_mcb_record_modes(&report)); BC_CHECK(report.mode_history_count == vector + 1);
        for (int i = 0; i < 6; ++i) BC_CHECK(report.mode_history[vector][i] == report.modes[i]);
    }
    for (int i = 0; i < 6; ++i) report.modes[i] = 32 & (1 << i) ? DANGO_MATERIAL_CONTACT_BLOCK_RELEASED : DANGO_MATERIAL_CONTACT_BLOCK_ENABLED;
    BC_CHECK(!dango_mcb_record_modes(&report));
    BC_CHECK(report.status == DANGO_MATERIAL_CONTACT_BLOCK_BUDGET && report.mode_history_count == 32); ++bc_negatives;
    return 0;
}

static int bc_physical_repeat(void)
{
    // One diagonal upper radial row, N, and T. The bottom arc is convex and
    // leaves only node 10 touching. With N0=0, μ=1, vA=(2,-.5), vCore=(0,2.125),
    // independent 3-row algebra gives ZERO -> STICK -> release N -> ZERO ->
    // re-enable N, revisiting the initial vector. This is an actual physical
    // repeated-mode failure, including both directions of the zero-B switch.
    dango_constraint_state s = bc_ring(), output = s, saved = output;
    s.radial_min = .4f; s.radial_max = 1.02f;
    double corner = -(double)s.radial_max / sqrt(2);
    s.positions[10] = (b2Vec2){(float)corner,(float)corner};
    for (int n = 11; n <= 14; ++n)
    {
        double phase = 2 * bc_pi * n / 16, cx = cos(phase), sy = sin(phase);
        double a = .05 * cx * cx, b = -.1 * cx * corner - sy;
        double c = .05 * corner * corner + corner;
        double radius = -2 * c / (b + sqrt(b*b-4*a*c));
        s.positions[n] = (b2Vec2){(float)(radius*cx),(float)(radius*sy)};
    }
    bc_ground(&s,10); s.velocities[10] = (b2Vec2){2,-.5f}; s.velocities[16].y = 2.125f;
    dango_material_ground_receipt receipt = {10,0,0,1,0};
    dango_material_contact_block block; dango_material_contact_block_report report;
    BC_CHECK(dango_material_contact_block_build(&s,&receipt,1,1.0/600,&block,&report));
    BC_CHECK(report.active_count == 3);
    BC_CHECK(!dango_material_contact_block_solve(&block,&output,&report));
    BC_CHECK(report.status == DANGO_MATERIAL_CONTACT_BLOCK_REPEATED_MODE);
    BC_CHECK(memcmp(&output,&saved,sizeof output) == 0);
    BC_CHECK(report.linear_solves == 4 && report.full_checks == 4 && report.float_checks == 0);
    BC_CHECK(!report.final_rows.ledger_evaluated && !report.final_rows.float_output_checked);
    BC_CHECK(report.matrix_available && report.linear_solution_available && report.row_rates_available);
    BC_CHECK(report.matrix_mode_index == 3 && report.solution_mode_index == 3 && report.rates_mode_index == 3);
    int normal = bc_mode_index(&report,138), tangent = bc_mode_index(&report,154);
    BC_CHECK(normal >= 0 && tangent >= 0);
    BC_CHECK(report.mode_history[0][tangent] == DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
    BC_CHECK(report.mode_history[1][tangent] == DANGO_MATERIAL_CONTACT_BLOCK_STICK);
    BC_CHECK(report.mode_history[2][normal] == DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
    BC_CHECK(report.mode_history[3][tangent] == DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
    ++bc_negatives;
    return 0;
}

static int bc_matrix_checks(void)
{
    const int ids[16] = {9,2,17,4};
    double matrix[16][16] = {{0}}, rhs[16] = {0}, solution[16] = {0};
    dango_material_contact_block_report report = {0};
    // Nonsymmetric, legal negative pivot. A Cholesky-only implementation fails.
    matrix[0][1] = 1; matrix[1][0] = -1; rhs[0] = 2; rhs[1] = -3;
    BC_CHECK(dango_mcb_linear_solve(2,ids,matrix,rhs,solution,&report));
    BC_CHECK(bc_near(solution[0],3) && bc_near(solution[1],2));
    BC_CHECK(report.pivot_searches == 5 && report.pivot_eliminations == 2);
    for (int i = 0; i < 2; ++i)
        BC_CHECK(fabs(matrix[i][0]*solution[0]+matrix[i][1]*solution[1]-rhs[i]) < 1e-13);
    ++bc_positives;
    dango_material_contact_block_report repeat = {0}; double repeated[16] = {0};
    BC_CHECK(dango_mcb_linear_solve(2,ids,matrix,rhs,repeated,&repeat));
    BC_CHECK(memcmp(solution,repeated,sizeof solution) == 0);
    BC_CHECK(report.pivot_searches == repeat.pivot_searches && report.pivot_eliminations == repeat.pivot_eliminations);
    // Exact rank deficiency: no diagonal regularization or pseudo-inverse.
    matrix[0][0] = 1; matrix[0][1] = 1; matrix[1][0] = 1; matrix[1][1] = 1;
    double saved[16]; memcpy(saved,solution,sizeof saved); memset(&report,0,sizeof report);
    BC_CHECK(!dango_mcb_linear_solve(2,ids,matrix,rhs,solution,&report));
    BC_CHECK(report.status == DANGO_MATERIAL_CONTACT_BLOCK_RANK);
    BC_CHECK(memcmp(solution,saved,sizeof saved) == 0); ++bc_negatives;
    // Already row-normalized near dependence, on each side of the fixed gate.
    matrix[1][0] = 1 / (1 + 1e-14); matrix[1][1] = 1; memset(&report,0,sizeof report);
    BC_CHECK(!dango_mcb_linear_solve(2,ids,matrix,rhs,solution,&report));
    BC_CHECK(report.status == DANGO_MATERIAL_CONTACT_BLOCK_RANK); ++bc_negatives;
    matrix[1][0] = 1 / (1 + 1e-11); rhs[0] = 3; rhs[1] = matrix[1][0] + 2; memset(&report,0,sizeof report);
    BC_CHECK(dango_mcb_linear_solve(2,ids,matrix,rhs,solution,&report));
    BC_CHECK(fabs(matrix[0][0]*solution[0]+matrix[0][1]*solution[1]-rhs[0]) < 1e-12);
    BC_CHECK(fabs(matrix[1][0]*solution[0]+matrix[1][1]*solution[1]-rhs[1]) < 1e-12); ++bc_positives;
    matrix[0][0] = NAN; memset(&report,0,sizeof report);
    memcpy(saved,solution,sizeof saved);
    BC_CHECK(!dango_mcb_linear_solve(2,ids,matrix,rhs,solution,&report));
    BC_CHECK(report.status == DANGO_MATERIAL_CONTACT_BLOCK_NONFINITE);
    BC_CHECK(memcmp(solution,saved,sizeof saved) == 0); ++bc_negatives;
    return 0;
}

static int bc_mode_decision_units(void)
{
    // Direct unit coverage of the exact decision routine used after each solve.
    // These rate/mode states are not claimed to be full physical trajectories.
    for (int sign = -1; sign <= 1; sign += 2)
    {
        dango_constraint_state s = bc_ring(); bc_ground(&s,12);
        dango_material_ground_receipt receipt = {12,1,0,1,1};
        dango_material_contact_block block; dango_material_contact_block_report report;
        BC_CHECK(dango_material_contact_block_build(&s,&receipt,1,1.0/600,&block,&report));
        dango_material_contact_solver reference = block.contact;
        int t = bc_mode_index(&report,156); BC_CHECK(t >= 0);
        report.modes[t] = sign > 0 ? DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE : DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE;
        block.contact.rows[156].multiplier = sign;
        block.contact.velocities[12].x = sign;
        dango_material_contact_block_mode next = DANGO_MATERIAL_CONTACT_BLOCK_ZERO;
        BC_CHECK(dango_mcb_next_mode(&block.contact,&reference,&report,&next) == t);
        BC_CHECK(next == DANGO_MATERIAL_CONTACT_BLOCK_STICK); ++bc_positives;
        // Lowest row identity wins when both N and sliding direction violate.
        int normal = bc_mode_index(&report,140); BC_CHECK(normal >= 0);
        block.contact.rows[140].multiplier = -.1;
        BC_CHECK(dango_mcb_next_mode(&block.contact,&reference,&report,&next) == normal);
        BC_CHECK(next == DANGO_MATERIAL_CONTACT_BLOCK_RELEASED); ++bc_positives;
    }
    return 0;
}

static int bc_failure_preservation(void)
{
    dango_constraint_state s = bc_ring(), output = s, saved = output;
    // Exactly 16 independent radial rows are supported, including zero loads.
    s.radial_min = .98f;
    for (int n = 0; n < 16; ++n) s.positions[n] = b2MulSV(s.radial_min,s.positions[n]);
    dango_material_contact_block block;
    dango_material_contact_block_report report;
    BC_CHECK(bc_success(&s,NULL,0,1.0/600,&block,&output) == 0);
    BC_CHECK(block.report.active_count == 16 && block.report.linear_solves == 1);
    saved = output;
    // One additional ground row must fail rather than silently choosing 16.
    bc_ground(&s,12);
    BC_CHECK(!dango_material_contact_block_build(&s,NULL,0,1.0/600,&block,&report));
    BC_CHECK(report.status == DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY_FAILURE && report.active_count == 17);
    BC_CHECK(!dango_material_contact_block_solve(&block,&output,&report));
    BC_CHECK(memcmp(&output,&saved,sizeof output) == 0); ++bc_negatives;
    // Small legal geometry exposes float rate failure after double passes.
    // The new algorithm must stop, with no old sweep fallback or fresh budget.
    s = bc_ring(); const float scale = .01f / 16;
    s.radial_length = scale; s.perimeter_length *= scale; s.radius *= scale; s.radial_min = .98f;
    for (int n = 0; n < 16; ++n) s.positions[n] = b2MulSV(scale,s.positions[n]);
    s.positions[1] = b2MulSV(s.radial_min,s.positions[1]);
    s.velocities[1] = (b2Vec2){0,12}; s.velocities[16] = (b2Vec2){12,0};
    BC_CHECK(dango_material_contact_block_build(&s,NULL,0,1.0/600,&block,&report));
    BC_CHECK(!dango_material_contact_block_solve(&block,&output,&report));
    BC_CHECK(report.status == DANGO_MATERIAL_CONTACT_BLOCK_FINAL && report.float_checks == 1);
    BC_CHECK(report.final_rows.status == DANGO_MATERIAL_CONTACT_FINAL_RATE || report.final_rows.status == DANGO_MATERIAL_CONTACT_COMPLEMENTARITY);
    BC_CHECK(report.linear_solves == 1 && report.full_checks == 1);
    BC_CHECK(report.matrix_available && report.linear_solution_available && report.row_rates_available);
    BC_CHECK(report.matrix_mode_index == 0 && report.solution_mode_index == 0 && report.rates_mode_index == 0);
    BC_CHECK(memcmp(&output,&saved,sizeof output) == 0); ++bc_negatives;
    // A built context is single-use. A second solve cannot replay its deltas.
    s = bc_ring(); s.velocities[3].x = 1;
    BC_CHECK(bc_success(&s,NULL,0,1.0/600,&block,&output) == 0); saved = output;
    BC_CHECK(!dango_material_contact_block_solve(&block,&output,&report));
    BC_CHECK(report.status == DANGO_MATERIAL_CONTACT_BLOCK_INVALID_INPUT);
    BC_CHECK(memcmp(&output,&saved,sizeof output) == 0); ++bc_negatives;
    return 0;
}

int main(void)
{
    if (bc_frame29() || bc_radial_modes() || bc_friction_modes() || bc_coupled_contact() ||
        bc_guard_failures() || bc_physical_repeat() || bc_matrix_checks() || bc_mode_decision_units() || bc_failure_preservation() || bc_final_cap_modes()) return 1;
    printf("PASS block: %d assertions; %d fixed positive fixtures; %d rejected negatives; capacity 16 / mode budget 32\n",
        bc_assertions,bc_positives,bc_negatives);
    return 0;
}

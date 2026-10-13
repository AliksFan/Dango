// Fixed capacity and captured-state tests. No world, DLL or parameter search.
// All matrix fixtures use the extended stride; no retained 16-stride test is included.
#include <stdio.h>
#include "dango_material_contact_block.h"
#include "dango_material_contact_frame905.h"

_Static_assert(DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY == 32, "extended block fixture");
_Static_assert(DANGO_MATERIAL_CONTACT_NULL_CAPACITY == 32, "extended null fixture");
_Static_assert(DANGO_MATERIAL_CONTACT_CERTIFICATE_CAPACITY == 32, "extended certificate fixture");
_Static_assert(DANGO_MATERIAL_CONTACT_NULL_CONSTRAINTS == 128, "extended interval fixture");
_Static_assert(DANGO_MATERIAL_CONTACT_BLOCK_SOLVES == 32, "unchanged mode budget");

#define CA_CHECK(c) do { ++ca_assertions; if (!(c)) { fprintf(stderr, "FAIL capacity %d: %s\n", __LINE__, #c); return 1; } } while (0)
static int ca_assertions, ca_positives, ca_negatives;
static const uint64_t ca_canary = UINT64_C(0x6294B5F317CE08AD);

typedef struct ca_guarded_vector { uint64_t before[4]; double values[32]; uint64_t after[4]; } ca_guarded_vector;
typedef struct ca_guarded_state { uint64_t before[4]; dango_constraint_state value; uint64_t after[4]; } ca_guarded_state;
typedef struct ca_guarded_null { uint64_t before[4]; dango_material_contact_null_report value; uint64_t after[4]; } ca_guarded_null;

static void ca_guard_set(uint64_t* before, uint64_t* after)
{ for (int i = 0; i < 4; ++i) before[i] = after[i] = ca_canary; }
static bool ca_guard_ok(const uint64_t* before, const uint64_t* after)
{ for (int i = 0; i < 4; ++i) if (before[i] != ca_canary || after[i] != ca_canary) return false; return true; }

static int ca_fixture_bits(void)
{
    for (int stage = 0; stage < 2; ++stage)
    {
        dango_constraint_state s = dango_material_contact_frame905_state(stage != 0);
        float values[93] = {s.radial_length,s.perimeter_length,s.radial_min,s.radial_max,
            s.perimeter_min,s.perimeter_max,s.radius,s.ground};
        int count = 8;
        for (int n = 0; n < 17; ++n)
        {
            values[count++] = s.positions[n].x; values[count++] = s.positions[n].y;
            values[count++] = s.velocities[n].x; values[count++] = s.velocities[n].y;
            values[count++] = s.inverse_mass[n];
        }
        CA_CHECK(count == 93);
        for (int i = 0; i < count; ++i)
        { uint32_t bits; memcpy(&bits,&values[i],sizeof bits); CA_CHECK(bits == dango_material_contact_frame905_float_bits[stage][i]); }
    }
    uint64_t step_bits;
    memcpy(&step_bits,&dango_material_contact_frame905_step_seconds,sizeof step_bits);
    CA_CHECK(step_bits == dango_material_contact_frame905_step_bits);
    const int nodes[3] = {0,14,15};
    for (int i = 0; i < 3; ++i)
    {
        const dango_material_ground_receipt* r = &dango_material_contact_frame905_receipts[i];
        const double values[4] = {r->normal_impulse,r->tangent_impulse,r->friction,r->total_normal_impulse};
        CA_CHECK(r->node == nodes[i]);
        for (int j = 0; j < 4; ++j)
        { uint64_t bits; memcpy(&bits,&values[j],sizeof bits); CA_CHECK(bits == dango_material_contact_frame905_receipt_bits[i][j]); }
    }
    dango_constraint_state positioned = dango_material_contact_frame905_state(false), damped = dango_material_contact_frame905_state(true);
    CA_CHECK(memcmp(positioned.positions,damped.positions,sizeof positioned.positions) == 0);
    CA_CHECK(memcmp(positioned.inverse_mass,damped.inverse_mass,sizeof positioned.inverse_mass) == 0);
    // This active fling substep applied no additional passive damping; both
    // captured stage velocities are identical, rather than fabricated variants.
    CA_CHECK(memcmp(positioned.velocities,damped.velocities,sizeof positioned.velocities) == 0);
    return 0;
}

// Independently differentiate each physical boundary value. Candidate gradients
// are never read to obtain the rates, Gram entries or reconstructed impulses.
static void ca_display(const dango_constraint_state* s, const double v[17][2], int node,
    bool display, double p[2], double u[2])
{
    p[0] = s->positions[node].x; p[1] = s->positions[node].y;
    u[0] = v[node][0]; u[1] = v[node][1];
    if (!display) return;
    double x = p[0] - s->positions[16].x, y = p[1] - s->positions[16].y;
    double vx = u[0] - v[16][0], vy = u[1] - v[16][1];
    double length = hypot(x,y), contraction = (x*vx + y*vy)/(length*length);
    p[0] += s->radius*x/length; p[1] += s->radius*y/length;
    u[0] += s->radius*(vx-x*contraction)/length;
    u[1] += s->radius*(vy-y*contraction)/length;
}

static double ca_boundary_rate(const dango_constraint_state* s, const double v[17][2], int row, double* gap)
{
    if (row < 64)
    {
        int node = row < 32 ? row/2 : (row-32)/2;
        int a = row < 32 ? 16 : node, b = row < 32 ? node : (node+1)%16;
        double x = (double)s->positions[b].x-s->positions[a].x, y = (double)s->positions[b].y-s->positions[a].y;
        double length = hypot(x,y), rate = (x*(v[b][0]-v[a][0])+y*(v[b][1]-v[a][1]))/length;
        double low = row < 32 ? (double)(s->radial_length*s->radial_min) : (double)(s->perimeter_length*s->perimeter_min);
        double high = row < 32 ? (double)(s->radial_length*s->radial_max) : (double)(s->perimeter_length*s->perimeter_max);
        *gap = row%2 ? high-length : length-low;
        return row%2 ? -rate : rate;
    }
    if (row < 128)
    {
        int node = row < 96 ? (row-64)/2 : (row-96)/2;
        double p[3][2], u[3][2];
        for (int i = 0; i < 3; ++i) ca_display(s,v,(node+15+i)%16,row >= 96,p[i],u[i]);
        double ex=p[1][0]-p[0][0], ey=p[1][1]-p[0][1], fx=p[2][0]-p[1][0], fy=p[2][1]-p[1][1];
        double dex=u[1][0]-u[0][0], dey=u[1][1]-u[0][1], dfx=u[2][0]-u[1][0], dfy=u[2][1]-u[1][1];
        double angle=atan2(ex*fy-ey*fx,ex*fx+ey*fy);
        double rate=(fx*dfy-fy*dfx)/(fx*fx+fy*fy)-(ex*dey-ey*dex)/(ex*ex+ey*ey);
        *gap = row%2 ? (double)1.5687963268f-angle : angle-(double).002f;
        return row%2 ? -rate : rate;
    }
    int node = row < 144 ? row-128 : row-144;
    *gap = (double)s->positions[node].y-(double)(s->ground+s->radius);
    return v[node][row < 144 ? 1 : 0];
}

static int ca_independent_final(const dango_material_contact_block* block,
    const dango_constraint_state* published, bool float_state)
{
    const dango_material_contact_solver* contact = &block->contact;
    const dango_constraint_state* s = &contact->geometry;
    double v[17][2], delta[17][2]={{0}}, impulse_scale[17]={0};
    double origin[2]={s->positions[16].x,s->positions[16].y};
    double energy0=0,energy1=0,pchange[2]={0},external[2]={0},angular=0,torque=0;
    double mass_sum=0,total_impulse_scale=0,torque_scale=0;
    for (int n=0;n<17;++n)
    {
        v[n][0]=float_state ? published->velocities[n].x : contact->velocities[n].x;
        v[n][1]=float_state ? published->velocities[n].y : contact->velocities[n].y;
        impulse_scale[n]=hypot(contact->input_velocities[n].x,contact->input_velocities[n].y);
    }
    for (int row=0;row<160;++row)
    {
        double gap,rate=ca_boundary_rate(s,v,row,&gap);
        double tolerance=row>=64 && row<128 ? DANGO_CONSTRAINT_TOLERANCE : DANGO_CONSTRAINT_TOLERANCE*s->radial_length;
        bool active=gap<=tolerance;
        int node=row-144;
        const dango_material_contact_support* support=node>=0 ? &contact->support[node] : NULL;
        if (row>=144) active=active && support->receipt_present;
        double lambda=contact->rows[row].multiplier;
        CA_CHECK(isfinite(gap) && isfinite(rate) && isfinite(lambda));
        CA_CHECK(gap>=-tolerance);
        CA_CHECK(active==contact->rows[row].active);
        CA_CHECK(fabs(gap-contact->rows[row].gap)<=2e-12*fmax(1,fabs(gap)));
        if (!float_state) CA_CHECK(fabs(rate-block->report.all_row_rates[row])<=2e-12*fmax(1,fabs(rate)));
        if (!active) { CA_CHECK(lambda==0); continue; }
        if (row<144)
        {
            double normalized=row>=64 && row<128 ? rate : rate/s->radial_length;
            CA_CHECK(lambda>=0);
            CA_CHECK(normalized>=-DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
            if (lambda>0) CA_CHECK(normalized<=DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
        }
        else
        {
            double budget=support->friction*(support->base_normal+contact->rows[128+node].multiplier)+support->receipt_cone_slack;
            double total=support->base_tangent+lambda,eps=32*DBL_EPSILON*fmax(budget,fabs(total));
            CA_CHECK(budget>=0 && fabs(total)<=budget+eps);
            if (budget>0)
            {
                if (total<=-budget+eps) CA_CHECK(rate/s->radial_length>=-DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
                else if (total>=budget-eps) CA_CHECK(rate/s->radial_length<=DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
                else CA_CHECK(fabs(rate)/s->radial_length<=DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
            }
        }
        for (int n=0;n<17;++n)
        {
            double gradient[2];
            for (int axis=0;axis<2;++axis)
            {
                double basis[17][2]={{0}},ignored;
                basis[n][axis]=1; gradient[axis]=ca_boundary_rate(s,basis,row,&ignored);
                delta[n][axis]+=lambda*s->inverse_mass[n]*gradient[axis];
            }
            impulse_scale[n]+=fabs(lambda)*s->inverse_mass[n]*hypot(gradient[0],gradient[1]);
        }
    }
    double rounding=float_state ? 8*FLT_EPSILON : 128*DBL_EPSILON;
    for (int n=0;n<17;++n)
    {
        double mass=1.0/s->inverse_mass[n],x=s->positions[n].x-origin[0],y=s->positions[n].y-origin[1];
        double vx0=contact->input_velocities[n].x,vy0=contact->input_velocities[n].y;
        double rx=v[n][0]-vx0-delta[n][0],ry=v[n][1]-vy0-delta[n][1];
        double allowance=rounding*fmax(impulse_scale[n],s->radial_length*DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
        // Bounds independent arithmetic order only. Original validator below
        // also has to pass its unchanged allowance without this small bound.
        CA_CHECK(hypot(rx,ry)<=allowance+2e-13*fmax(1,impulse_scale[n]));
        energy0+=.5*mass*(vx0*vx0+vy0*vy0); energy1+=.5*mass*(v[n][0]*v[n][0]+v[n][1]*v[n][1]);
        double px=mass*(v[n][0]-vx0),py=mass*(v[n][1]-vy0);
        pchange[0]+=px;pchange[1]+=py;angular+=x*py-y*px;mass_sum+=mass;
        double sum=mass*(hypot(vx0,vy0)+hypot(v[n][0],v[n][1]));
        total_impulse_scale+=sum;torque_scale+=hypot(x,y)*sum;
        if (n<16)
        {
            double ix=contact->rows[144+n].multiplier,iy=contact->rows[128+n].multiplier;
            external[0]+=ix;external[1]+=iy;torque+=x*iy-y*ix;
            total_impulse_scale+=hypot(ix,iy);torque_scale+=hypot(x,y)*hypot(ix,iy);
        }
    }
    double floor=mass_sum*s->radial_length*s->radial_length*1e-10;
    CA_CHECK(energy1<=energy0+rounding*fmax(energy0,floor));
    CA_CHECK(hypot(pchange[0]-external[0],pchange[1]-external[1])<=rounding*fmax(total_impulse_scale,mass_sum*s->radial_length*1e-5)+2e-13);
    CA_CHECK(fabs(angular-torque)<=rounding*fmax(torque_scale,mass_sum*s->radial_length*s->radial_length*1e-5)+2e-13);
    return 0;
}

static int ca_matrix_boundary(void)
{
    int ids[32]; double matrix[32][32]={{0}},rhs[32]={0},expected[32];
    for (int i=0;i<32;++i)
    {
        ids[i]=159-i; expected[i]=.25*(i%7-3); matrix[i][i]=4+i*.03125;
        if (i) matrix[i][i-1]=-.25;
        if (i<31) matrix[i][i+1]=-.25;
    }
    for (int i=0;i<32;++i) for (int j=0;j<32;++j) rhs[i]+=matrix[i][j]*expected[j];
    ca_guarded_vector output={0};ca_guard_set(output.before,output.after);
    dango_material_contact_block_report block_report;dango_mcb_report_init(&block_report);
    CA_CHECK(dango_mcb_linear_solve(32,ids,matrix,rhs,output.values,&block_report));
    CA_CHECK(block_report.pivot_eliminations==32 && block_report.pivot_threshold>0);
    for (int i=0;i<32;++i) CA_CHECK(fabs(output.values[i]-expected[i])<1e-14);
    CA_CHECK(ca_guard_ok(output.before,output.after));
    dango_material_contact_null_report report;
    CA_CHECK(dango_mcn_factor(32,ids,matrix,rhs,output.values,&report));
    CA_CHECK(report.rank==32 && report.nullity==0 && report.factor_passed);
    for (int i=0;i<32;++i) CA_CHECK(fabs(output.values[i]-expected[i])<1e-14);
    double residual[32]={0},allowance[32]={0};
    dango_material_contact_operator_certificate certificate;
    CA_CHECK(dango_mcn_operator_row_residuals(32,matrix,rhs,output.values,false,residual,allowance,&certificate,0));
    CA_CHECK(certificate.dimension==32 && certificate.rows_checked==32 && certificate.evaluated[31] && certificate.passed);
    output.values[31]+=.125;
    CA_CHECK(!dango_mcn_operator_row_residuals(32,matrix,rhs,output.values,false,residual,allowance,&certificate,0));
    CA_CHECK(fabs(residual[31])>allowance[31]);++ca_negatives;
    for (int i=0;i<32;++i) output.values[i]=900+i;
    double saved[32];memcpy(saved,output.values,sizeof saved);
    CA_CHECK(!dango_mcb_linear_solve(33,ids,matrix,rhs,output.values,&block_report));
    CA_CHECK(block_report.status==DANGO_MATERIAL_CONTACT_BLOCK_INVALID_INPUT);
    CA_CHECK(!dango_mcn_factor(33,ids,matrix,rhs,output.values,&report));
    CA_CHECK(report.status==DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT && !report.factor_available);
    CA_CHECK(memcmp(output.values,saved,sizeof saved)==0 && ca_guard_ok(output.before,output.after));
    CA_CHECK(!dango_mcn_operator_row_residuals(33,matrix,rhs,output.values,false,residual,allowance,&certificate,0));
    CA_CHECK(!certificate.available);++ca_negatives;++ca_positives;
    printf("PASS 32-matrix boundary, last-coordinate certificate rejection, 33-dimension output guards\n");
    return 0;
}

static int ca_interval_boundary(void)
{
    // Synthetic helper-storage fixture, not a captured physical reaction.
    ca_guarded_null guarded={0};ca_guard_set(guarded.before,guarded.after);
    dango_material_contact_null_report* r=&guarded.value;dango_mcn_report_init(r);
    r->mode_index=7;r->reaction_mode_index=7;r->reaction_checks_passed=true;
    CA_CHECK(dango_mcn_interval_init(r));
    for (int i=0;i<128;++i)
        CA_CHECK(dango_mcn_interval_add(r,i,i%4,i%2 ? -1 : 1,-2));
    CA_CHECK(r->constraint_count==128 && r->interval_passed && r->constraints[127].row_id==127);
    CA_CHECK(r->lower_bounded && r->upper_bounded && r->lower==-2 && r->upper==2);
    CA_CHECK(dango_mcn_interval_select(r) && r->selected_alpha==0 && r->selection_passed);
    dango_material_contact_null_constraint saved[128];memcpy(saved,r->constraints,sizeof saved);
    CA_CHECK(!dango_mcn_interval_add(r,159,4,1,-1));
    CA_CHECK(r->status==DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT && r->constraint_count==128);
    CA_CHECK(!r->used && !r->selection_available && !r->selected_pair_available);
    CA_CHECK(memcmp(saved,r->constraints,sizeof saved)==0 && r->lower==-2 && r->upper==2);
    CA_CHECK(ca_guard_ok(guarded.before,guarded.after));++ca_positives;++ca_negatives;
    printf("PASS 128-interval boundary, 129th inequality rejection, constraint storage guards\n");
    return 0;
}

static dango_constraint_state ca_ring32(void)
{
    const double pi=3.1415926535897932384626433832795;
    dango_constraint_state s={0};s.radial_length=1;s.perimeter_length=(float)(2*sin(pi/16));
    s.radial_min=.98f;s.radial_max=1.1f;s.perimeter_min=.1f;s.perimeter_max=.98f;
    s.radius=.045f;s.ground=-10;
    for (int i=0;i<16;++i)
    { double angle=2*pi*i/16;s.positions[i]=(b2Vec2){s.radial_min*(float)cos(angle),s.radial_min*(float)sin(angle)};s.inverse_mass[i]=1; }
    s.positions[0]=(b2Vec2){s.radial_min,0};s.positions[4]=(b2Vec2){0,s.radial_min};
    s.positions[8]=(b2Vec2){-s.radial_min,0};s.positions[12]=(b2Vec2){0,-s.radial_min};s.inverse_mass[16]=1;
    return s;
}

static int ca_active_boundary(void)
{
    dango_constraint_state input=ca_ring32(),saved=input;
    ca_guarded_state output={0};ca_guard_set(output.before,output.after);
    dango_material_contact_block block;dango_material_contact_block_report report;
    CA_CHECK(dango_material_contact_block_build(&input,NULL,0,1.0/600,&block,&report));
    CA_CHECK(report.capacity==32 && report.active_count==32 && report.matrix_dimension==32 && report.solve_limit==32);
    for (int i=0;i<16;++i) { CA_CHECK(report.active_row_ids[i]==2*i);CA_CHECK(report.active_row_ids[16+i]==33+2*i); }
    CA_CHECK(dango_material_contact_block_solve(&block,&output.value,&report));
    CA_CHECK(report.status==DANGO_MATERIAL_CONTACT_BLOCK_OK && report.final_rows.ledger_evaluated && report.final_rows.float_output_checked);
    CA_CHECK(report.mode_history_count==report.linear_solves && report.linear_solves<=32 && report.float_checks==1);
    CA_CHECK(ca_independent_final(&block,&output.value,false)==0 && ca_independent_final(&block,&output.value,true)==0);
    CA_CHECK(ca_guard_ok(output.before,output.after) && memcmp(&input,&saved,sizeof input)==0);
    dango_material_contact_report checked;
    CA_CHECK(dango_material_contact_validate(&block.contact,&output.value,&checked));++ca_positives;
    input.ground=input.positions[12].y-input.radius;saved=input;
    memset(&output.value,0x5a,sizeof output.value);dango_constraint_state sentinel=output.value;
    CA_CHECK(!dango_material_contact_block_build(&input,NULL,0,1.0/600,&block,&report));
    CA_CHECK(report.status==DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY_FAILURE && report.active_count==33 && report.matrix_dimension==33);
    CA_CHECK(!report.matrix_available && !report.linear_solution_available && report.mode_history_count==0);
    CA_CHECK(!block.built && !dango_material_contact_block_solve(&block,&output.value,&report));
    CA_CHECK(memcmp(&output.value,&sentinel,sizeof sentinel)==0 && memcmp(&input,&saved,sizeof input)==0);
    CA_CHECK(ca_guard_ok(output.before,output.after));++ca_negatives;
    // Width is 32, but mode history remains 32 distinct evaluated vectors.
    dango_mcb_report_init(&report);report.active_count=32;
    for (int mode=0;mode<32;++mode)
    { report.modes[0]=(dango_material_contact_block_mode)(mode/6);report.modes[31]=(dango_material_contact_block_mode)(mode%6);CA_CHECK(dango_mcb_record_modes(&report)); }
    CA_CHECK(report.mode_history_count==32);
    CA_CHECK(!dango_mcb_record_modes(&report) && report.status==DANGO_MATERIAL_CONTACT_BLOCK_REPEATED_MODE);++ca_negatives;
    report.modes[0]=(dango_material_contact_block_mode)(32/6);report.modes[31]=(dango_material_contact_block_mode)(32%6);
    CA_CHECK(!dango_mcb_record_modes(&report) && report.status==DANGO_MATERIAL_CONTACT_BLOCK_BUDGET && report.mode_history_count==32);++ca_negatives;
    printf("PASS actual 32-active build/solve, 33-active capacity rejection, unchanged 32-mode budget\n");
    return 0;
}

static int ca_mode_helper_capacity(void)
{
    // Synthetic diagnostic units for helper trust boundaries. No certificate
    // flags here are represented as a real physical solve or publication.
    dango_constraint_state input=ca_ring32();
    dango_material_contact_block block;dango_material_contact_block_report report;
    CA_CHECK(dango_material_contact_block_build(&input,NULL,0,1.0/600,&block,&report));
    dango_material_contact_solver reference=block.contact,saved_reference=reference;
    report.matrix_available=true;report.matrix_mode_index=0;
    dango_material_contact_null_report* nr=&report.null_reaction;
    nr->status=DANGO_MATERIAL_CONTACT_NULL_NULLITY;nr->factor_available=true;
    nr->rank=0;nr->dimension=32;nr->nullity=32;
    nr->mode_index=nr->factor_mode_index=0;nr->column_permutation[0]=0;
    dango_material_contact_block_report positive=report;
    CA_CHECK(dango_mcb_rank_next_mode(&reference,&positive));
    CA_CHECK(positive.modes[0]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
    // Without the new width guard this same first free column would be chosen
    // immediately, before any other member has to be read.
    report.active_count=nr->dimension=33;nr->nullity=33;
    dango_material_contact_block_report saved_report=report;
    CA_CHECK(!dango_mcb_rank_next_mode(&reference,&report));
    CA_CHECK(memcmp(&report,&saved_report,sizeof report)==0);++ca_negatives;
    nr->status=DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY;nr->nullity=1;
    nr->factor_passed=nr->pair_checks_passed=nr->reaction_checks_passed=true;
    nr->raw_pair_available=nr->effective_pair_available=nr->pair_checks_available=true;
    nr->reaction_checks_available=nr->interval_available=true;
    nr->raw_pair_mode_index=nr->effective_pair_mode_index=nr->pair_checks_mode_index=0;
    nr->reaction_mode_index=nr->interval_mode_index=0;
    saved_report=report;
    CA_CHECK(!dango_mcb_null_next_mode(&reference,&report));
    CA_CHECK(memcmp(&report,&saved_report,sizeof report)==0);
    CA_CHECK(memcmp(&reference,&saved_reference,sizeof reference)==0);++ca_negatives;
    printf("PASS rank and empty-interval helper 33-width rejection without mutation\n");
    return 0;
}

static int ca_frame905(void)
{
    static const int ids[17]={3,41,43,45,47,49,51,53,55,57,59,61,63,128,143,144,159};
    dango_constraint_state input=dango_material_contact_frame905_state(true),saved=input;
    ca_guarded_state output={0};ca_guard_set(output.before,output.after);
    dango_material_contact_block block;dango_material_contact_block_report report;
    CA_CHECK(dango_material_contact_block_build(&input,dango_material_contact_frame905_receipts,3,
        dango_material_contact_frame905_step_seconds,&block,&report));
    CA_CHECK(report.active_count==17 && report.matrix_dimension==17 && report.capacity==32 && report.solve_limit==32);
    for (int i=0;i<17;++i) CA_CHECK(report.active_row_ids[i]==ids[i]);
    // Independently reconstruct the complete initial Gram matrix from physical
    // boundary directional derivatives, including the formerly truncated row159.
    dango_material_contact_block probe=block;
    CA_CHECK(dango_mcb_record_modes(&probe.report) && dango_mcb_system(&probe.contact,&probe.report));
    double independent[17][17][2]={{{0}}};
    for (int i=0;i<17;++i) for (int n=0;n<17;++n) for (int axis=0;axis<2;++axis)
    { double basis[17][2]={{0}},gap;basis[n][axis]=1;independent[i][n][axis]=ca_boundary_rate(&input,basis,ids[i],&gap); }
    for (int i=0;i<17;++i) for (int j=0;j<17;++j)
    {
        double gram=0;
        for (int n=0;n<17;++n) gram+=input.inverse_mass[n]*(independent[i][n][0]*independent[j][n][0]+independent[i][n][1]*independent[j][n][1]);
        CA_CHECK(fabs(gram-probe.report.raw_matrix[i][j])<=2e-12*fmax(1,fabs(gram)));
    }
    ca_guarded_vector z={0};ca_guard_set(z.before,z.after);
    dango_material_contact_null_report factor;
    CA_CHECK(dango_mcn_factor(17,ids,probe.report.scaled_matrix,probe.report.scaled_rhs,z.values,&factor));
    CA_CHECK(factor.rank==16 && factor.nullity==1 && factor.factor_passed && factor.raw_pair_available);
    CA_CHECK(ca_guard_ok(z.before,z.after));
    CA_CHECK(dango_material_contact_block_solve(&block,&output.value,&report));
    CA_CHECK(report.status==DANGO_MATERIAL_CONTACT_BLOCK_OK && report.linear_solves>0 && report.linear_solves<=32);
    CA_CHECK(report.mode_history_count==report.linear_solves && report.float_checks==1 && report.full_checks>=1);
    CA_CHECK(report.final_rows.ledger_evaluated && report.final_rows.float_output_checked && report.row_rates_available);
    CA_CHECK(report.matrix_mode_index==report.mode_history_count-1 && report.solution_mode_index==report.matrix_mode_index && report.rates_mode_index==report.matrix_mode_index);
    CA_CHECK(memcmp(&input,&saved,sizeof input)==0 && ca_guard_ok(output.before,output.after));
    CA_CHECK(memcmp(output.value.positions,input.positions,sizeof input.positions)==0);
    CA_CHECK(memcmp(output.value.inverse_mass,input.inverse_mass,sizeof input.inverse_mass)==0);
    CA_CHECK(ca_independent_final(&block,&output.value,false)==0 && ca_independent_final(&block,&output.value,true)==0);
    dango_material_contact_report checked;
    CA_CHECK(dango_material_contact_validate(&block.contact,&output.value,&checked));
    CA_CHECK(checked.ledger_evaluated && checked.float_output_checked);
    float maximum=0;for (int n=0;n<17;++n) maximum=fmaxf(maximum,b2Length(output.value.velocities[n]));
    float scale=maximum>12 ? (12*.99999f)/maximum : 1;
    dango_constraint_state limited=output.value;
    for (int n=0;n<17;++n) limited.velocities[n]=b2MulSV(scale,limited.velocities[n]);
    dango_material_contact_cap_report cap;
    CA_CHECK(dango_material_contact_block_validate_after_uniform_scale(&block,&output.value,&limited,scale,12,&cap));
    CA_CHECK(cap.contact_output_checked && cap.exact_common_scale_checked && cap.maximum_after<=12 && cap.final_rows.ledger_evaluated);
    dango_constraint_state corrupt=output.value;corrupt.velocities[15].y=-1;
    CA_CHECK(!dango_material_contact_validate(&block.contact,&corrupt,&checked));++ca_negatives;++ca_positives;
    printf("PASS captured frame905: 17 rows, initial rank16/nullity1, %d modes; independent double/float geometry/rates/cone/energy/momentum/angular/velocity ledger and original cap\n",report.linear_solves);
    return 0;
}

int main(void)
{
    if (ca_fixture_bits() || ca_matrix_boundary() || ca_interval_boundary() || ca_active_boundary() || ca_mode_helper_capacity() || ca_frame905()) return 1;
    printf("PASS capacity suite: %d assertions; %d positives; %d rejected negatives\n",ca_assertions,ca_positives,ca_negatives);
    return 0;
}

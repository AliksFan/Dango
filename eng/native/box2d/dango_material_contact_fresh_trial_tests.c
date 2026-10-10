// Frozen-state trial regression. No world, DLL, parameter search or seeded solution.
#include <stdio.h>
#include "dango_material_contact_block.h"
#include "dango_material_contact_frame2647.h"

_Static_assert(DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY == 32, "extended block fixture");
_Static_assert(DANGO_MATERIAL_CONTACT_BLOCK_SOLVES == 32, "unchanged mode budget");
#if !defined(DANGO_MATERIAL_CONTACT_MODE_TRANSITION_CANDIDATE) || !defined(DANGO_MATERIAL_CONTACT_RANK_MODE_CANDIDATE) || !defined(DANGO_MATERIAL_CONTACT_FRICTION_MODE_PRIORITY_CANDIDATE)
#error Fresh trial regression requires the preceding transition, rank and priority candidates.
#endif

#define FT_CHECK(c) do { ++ft_assertions; if (!(c)) { fprintf(stderr, "FAIL fresh trial %d: %s\n", __LINE__, #c); return 1; } } while (0)
static int ft_assertions, ft_positives, ft_negatives;
static const uint64_t ft_canary = UINT64_C(0x81B206AF43E75C9D);
typedef struct ft_guarded_state { uint64_t before[4]; dango_constraint_state value; uint64_t after[4]; } ft_guarded_state;

static void ft_guard_set(uint64_t* before, uint64_t* after)
{ for (int i = 0; i < 4; ++i) before[i] = after[i] = ft_canary; }
static bool ft_guard_ok(const uint64_t* before, const uint64_t* after)
{ for (int i = 0; i < 4; ++i) if (before[i] != ft_canary || after[i] != ft_canary) return false; return true; }

static int ft_fixture_bits(void)
{
    for (int stage = 0; stage < 2; ++stage)
    {
        dango_constraint_state s = dango_material_contact_frame2647_state(stage != 0);
        float values[93] = {s.radial_length,s.perimeter_length,s.radial_min,s.radial_max,
            s.perimeter_min,s.perimeter_max,s.radius,s.ground};
        int count = 8;
        for (int n = 0; n < 17; ++n)
        {
            values[count++] = s.positions[n].x; values[count++] = s.positions[n].y;
            values[count++] = s.velocities[n].x; values[count++] = s.velocities[n].y;
            values[count++] = s.inverse_mass[n];
        }
        FT_CHECK(count == 93);
        for (int i = 0; i < count; ++i)
        { uint32_t bits; memcpy(&bits,&values[i],sizeof bits); FT_CHECK(bits == dango_material_contact_frame2647_float_bits[stage][i]); }
    }
    uint64_t step_bits;
    memcpy(&step_bits,&dango_material_contact_frame2647_step_seconds,sizeof step_bits);
    FT_CHECK(step_bits == dango_material_contact_frame2647_step_bits);
    for (int i = 0; i < 5; ++i)
    {
        const dango_material_ground_receipt* r = &dango_material_contact_frame2647_receipts[i];
        const double values[4] = {r->normal_impulse,r->tangent_impulse,r->friction,r->total_normal_impulse};
        FT_CHECK(r->node == 11+i);
        for (int j = 0; j < 4; ++j)
        { uint64_t bits; memcpy(&bits,&values[j],sizeof bits); FT_CHECK(bits == dango_material_contact_frame2647_receipt_bits[i][j]); }
    }
    dango_constraint_state positioned = dango_material_contact_frame2647_state(false), damped = dango_material_contact_frame2647_state(true);
    FT_CHECK(memcmp(positioned.positions,damped.positions,sizeof positioned.positions) == 0);
    FT_CHECK(memcmp(positioned.inverse_mass,damped.inverse_mass,sizeof positioned.inverse_mass) == 0);
    // The captured active fling did not add passive damping at this substep.
    FT_CHECK(memcmp(positioned.velocities,damped.velocities,sizeof positioned.velocities) == 0);
    return 0;
}

// Independently differentiate each physical boundary value. Candidate gradients
// are never read to obtain the rates, Gram entries or reconstructed impulses.
static void ft_display(const dango_constraint_state* s, const double v[17][2], int node,
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

static double ft_boundary_rate(const dango_constraint_state* s, const double v[17][2], int row, double* gap)
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
        for (int i = 0; i < 3; ++i) ft_display(s,v,(node+15+i)%16,row >= 96,p[i],u[i]);
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

static int ft_independent_final(const dango_material_contact_block* block,
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
        double gap,rate=ft_boundary_rate(s,v,row,&gap);
        double tolerance=row>=64 && row<128 ? DANGO_CONSTRAINT_TOLERANCE : DANGO_CONSTRAINT_TOLERANCE*s->radial_length;
        bool active=gap<=tolerance;
        int node=row-144;
        const dango_material_contact_support* support=node>=0 ? &contact->support[node] : NULL;
        if (row>=144) active=active && support->receipt_present;
        double lambda=contact->rows[row].multiplier;
        FT_CHECK(isfinite(gap) && isfinite(rate) && isfinite(lambda));
        FT_CHECK(gap>=-tolerance);
        FT_CHECK(active==contact->rows[row].active);
        FT_CHECK(fabs(gap-contact->rows[row].gap)<=2e-12*fmax(1,fabs(gap)));
        if (!float_state) FT_CHECK(fabs(rate-block->report.all_row_rates[row])<=2e-12*fmax(1,fabs(rate)));
        if (!active) { FT_CHECK(lambda==0); continue; }
        if (row<144)
        {
            double normalized=row>=64 && row<128 ? rate : rate/s->radial_length;
            FT_CHECK(lambda>=0);
            FT_CHECK(normalized>=-DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
            if (lambda>0) FT_CHECK(normalized<=DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
        }
        else
        {
            double budget=support->friction*(support->base_normal+contact->rows[128+node].multiplier)+support->receipt_cone_slack;
            double total=support->base_tangent+lambda,eps=32*DBL_EPSILON*fmax(budget,fabs(total));
            FT_CHECK(budget>=0 && fabs(total)<=budget+eps);
            if (budget>0)
            {
                if (total<=-budget+eps) FT_CHECK(rate/s->radial_length>=-DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
                else if (total>=budget-eps) FT_CHECK(rate/s->radial_length<=DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
                else FT_CHECK(fabs(rate)/s->radial_length<=DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
            }
        }
        for (int n=0;n<17;++n)
        {
            double gradient[2];
            for (int axis=0;axis<2;++axis)
            {
                double basis[17][2]={{0}},ignored;
                basis[n][axis]=1; gradient[axis]=ft_boundary_rate(s,basis,row,&ignored);
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
        FT_CHECK(hypot(rx,ry)<=allowance+2e-13*fmax(1,impulse_scale[n]));
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
    FT_CHECK(energy1<=energy0+rounding*fmax(energy0,floor));
    FT_CHECK(hypot(pchange[0]-external[0],pchange[1]-external[1])<=rounding*fmax(total_impulse_scale,mass_sum*s->radial_length*1e-5)+2e-13);
    FT_CHECK(fabs(angular-torque)<=rounding*fmax(torque_scale,mass_sum*s->radial_length*s->radial_length*1e-5)+2e-13);
    return 0;
}



static int ft_frame2647(bool legacy)
{
    static const int ids[12]={1,8,26,35,37,39,90,124,141,142,157,158};
    static const dango_material_contact_block_mode initial[12]={0,0,0,0,0,0,0,0,0,0,2,2};
    dango_constraint_state input=dango_material_contact_frame2647_state(true),saved=input;
    dango_material_ground_receipt receipts[5],saved_receipts[5];
    memcpy(receipts,dango_material_contact_frame2647_receipts,sizeof receipts);
    memcpy(saved_receipts,receipts,sizeof receipts);
    ft_guarded_state output={0};ft_guard_set(output.before,output.after);
    memset(&output.value,0x5a,sizeof output.value);dango_constraint_state sentinel=output.value;
    dango_material_contact_block block;dango_material_contact_block_report report;
    FT_CHECK(dango_material_contact_block_build(&input,receipts,5,dango_material_contact_frame2647_step_seconds,&block,&report));
    FT_CHECK(report.active_count==12 && report.matrix_dimension==12 && report.capacity==32 && report.solve_limit==32);
    FT_CHECK(report.mode_history_count==0 && report.linear_solves==0);
    for (int i=0;i<12;++i) FT_CHECK(report.active_row_ids[i]==ids[i] && report.modes[i]==initial[i]);
    dango_material_contact_solver reference=block.contact;
    // Independent initial Gram assembly, including both ground tangent rows.
    dango_material_contact_block probe=block;
    FT_CHECK(dango_mcb_record_modes(&probe.report) && dango_mcb_system(&probe.contact,&probe.report));
    double independent[12][17][2]={{{0}}};
    for (int i=0;i<12;++i) for (int n=0;n<17;++n) for (int axis=0;axis<2;++axis)
    { double basis[17][2]={{0}},gap;basis[n][axis]=1;independent[i][n][axis]=ft_boundary_rate(&input,basis,ids[i],&gap); }
    for (int i=0;i<12;++i) for (int j=0;j<12;++j)
    {
        double gram=0;
        for (int n=0;n<17;++n) gram+=input.inverse_mass[n]*(independent[i][n][0]*independent[j][n][0]+independent[i][n][1]*independent[j][n][1]);
        FT_CHECK(fabs(gram-probe.report.raw_matrix[i][j])<=2e-12*fmax(1,fabs(gram)));
    }
    // Natural initial solve; the known legal final mode is never written in.
    bool solved=dango_material_contact_block_solve(&block,&output.value,&report);
    FT_CHECK(memcmp(&input,&saved,sizeof input)==0 && memcmp(receipts,saved_receipts,sizeof receipts)==0);
    FT_CHECK(memcmp(&block.contact.geometry,&reference.geometry,sizeof reference.geometry)==0);
    FT_CHECK(memcmp(block.contact.input_velocities,reference.input_velocities,sizeof reference.input_velocities)==0);
    FT_CHECK(memcmp(block.contact.support,reference.support,sizeof reference.support)==0);
    FT_CHECK(memcmp(block.contact.receipts,reference.receipts,sizeof reference.receipts)==0);
    FT_CHECK(block.contact.step_seconds==reference.step_seconds && block.contact.receipt_count==reference.receipt_count);
    FT_CHECK(ft_guard_ok(output.before,output.after));
    for (int i=0;i<12;++i) FT_CHECK(report.active_row_ids[i]==ids[i]);
    if (legacy)
    {
        FT_CHECK(!solved && report.status==DANGO_MATERIAL_CONTACT_BLOCK_REPEATED_MODE);
        FT_CHECK(report.mode_history_count==3 && report.linear_solves==3 && report.full_checks==3 && report.float_checks==0);
        FT_CHECK(memcmp(&output.value,&sentinel,sizeof sentinel)==0);
        FT_CHECK(report.mode_history[1][6]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
        FT_CHECK(report.mode_history[2][10]==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE);
        FT_CHECK(report.modes[10]==DANGO_MATERIAL_CONTACT_BLOCK_STICK);
        ++ft_negatives;
        printf("PASS frame2647 control without fresh trial: original 3-mode repetition rejected; no publication\n");
        return 0;
    }
    if (!solved) fprintf(stderr,"frame2647 natural failure: status=%d solves=%d full=%d float=%d\n",report.status,report.linear_solves,report.full_checks,report.float_checks);
    FT_CHECK(solved && report.status==DANGO_MATERIAL_CONTACT_BLOCK_OK);
    FT_CHECK(report.mode_history_count==8 && report.linear_solves==8 && report.full_checks==8 && report.float_checks==1);
    FT_CHECK(report.mode_history[2][10]==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE);
    FT_CHECK(report.mode_history[3][10]==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE);
    FT_CHECK(report.mode_history[4][8]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
    FT_CHECK(report.mode_history[5][10]==DANGO_MATERIAL_CONTACT_BLOCK_STICK);
    FT_CHECK(report.mode_history[6][10]==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE);
    FT_CHECK(report.mode_history[7][6]==DANGO_MATERIAL_CONTACT_BLOCK_ENABLED);
    static const dango_material_contact_block_mode expected[12]={0,0,0,0,0,0,0,0,1,0,4,2};
    for (int i=0;i<12;++i) FT_CHECK(report.modes[i]==expected[i]);
    for (int i=0;i<report.mode_history_count;++i) for (int j=0;j<i;++j)
        FT_CHECK(memcmp(report.mode_history[i],report.mode_history[j],12*sizeof report.modes[0])!=0);
    FT_CHECK(report.final_rows.ledger_evaluated && report.final_rows.float_output_checked && report.row_rates_available);
    FT_CHECK(report.matrix_mode_index==7 && report.solution_mode_index==7 && report.rates_mode_index==7);
    FT_CHECK(memcmp(output.value.positions,input.positions,sizeof input.positions)==0);
    FT_CHECK(memcmp(output.value.inverse_mass,input.inverse_mass,sizeof input.inverse_mass)==0);
    FT_CHECK(ft_independent_final(&block,&output.value,false)==0 && ft_independent_final(&block,&output.value,true)==0);
    dango_material_contact_report checked;
    FT_CHECK(dango_material_contact_validate(&block.contact,&output.value,&checked));
    FT_CHECK(checked.ledger_evaluated && checked.float_output_checked);
    float maximum=0;for (int n=0;n<17;++n) maximum=fmaxf(maximum,b2Length(output.value.velocities[n]));
    float scale=maximum>12 ? (12*.99999f)/maximum : 1;
    dango_constraint_state limited=output.value;
    for (int n=0;n<17;++n) limited.velocities[n]=b2MulSV(scale,limited.velocities[n]);
    dango_material_contact_cap_report cap;
    FT_CHECK(dango_material_contact_block_validate_after_uniform_scale(&block,&output.value,&limited,scale,12,&cap));
    FT_CHECK(cap.contact_output_checked && cap.exact_common_scale_checked && cap.maximum_after<=12 && cap.final_rows.ledger_evaluated);
    dango_constraint_state corrupt=output.value;corrupt.velocities[14].y=-1;
    FT_CHECK(!dango_material_contact_validate(&block.contact,&corrupt,&checked));++ft_negatives;++ft_positives;
    printf("PASS frame2647: 12 original rows, 8 distinct modes; independent all160 geometry/rates/cone/energy/momentum/angular/velocity ledger, double/float and original cap\n");
    return 0;
}

#ifdef DANGO_MATERIAL_CONTACT_FRESH_TRIAL_CANDIDATE
static int ft_history_guards(void)
{
    dango_material_contact_block_report report;dango_mcb_report_init(&report);report.active_count=12;
    for (int i=0;i<12;++i) report.modes[i]=i<10 ? DANGO_MATERIAL_CONTACT_BLOCK_ENABLED : DANGO_MATERIAL_CONTACT_BLOCK_STICK;
    report.modes[6]=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED;
    FT_CHECK(dango_mcb_record_modes(&report));
    report.modes[10]=DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE;
    FT_CHECK(dango_mcb_trial_seen(&report,10,DANGO_MATERIAL_CONTACT_BLOCK_STICK));
    FT_CHECK(!dango_mcb_trial_seen(&report,10,DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE));
    dango_material_contact_block_report saved=report;
    FT_CHECK(dango_mcb_trial_seen(&report,-1,DANGO_MATERIAL_CONTACT_BLOCK_STICK));
    FT_CHECK(memcmp(&report,&saved,sizeof report)==0);
    report.modes[8]=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED;
    FT_CHECK(!dango_mcb_trial_seen(&report,10,DANGO_MATERIAL_CONTACT_BLOCK_STICK));
    FT_CHECK(report.mode_history_count==1);
    report.active_count=33;saved=report;
    FT_CHECK(dango_mcb_trial_seen(&report,10,DANGO_MATERIAL_CONTACT_BLOCK_STICK));
    FT_CHECK(memcmp(&report,&saved,sizeof report)==0);++ft_negatives;
    printf("PASS whole-vector history comparison and 33-width rejection without history mutation\n");
    return 0;
}
#endif

int main(int argc,char** argv)
{
    bool legacy=argc==2 && strcmp(argv[1],"--negative-fresh")==0;
    if (argc>2 || (argc==2 && !legacy)) return 2;
#ifdef DANGO_MATERIAL_CONTACT_FRESH_TRIAL_CANDIDATE
    if (legacy) { fprintf(stderr,"negative control requires fresh trial macro disabled\n"); return 2; }
#else
    if (!legacy) { fprintf(stderr,"positive requires fresh trial macro; this unchanged initial state must fail without it\n"); return 1; }
#endif
    if (ft_fixture_bits() || ft_frame2647(legacy)) return 1;
#ifdef DANGO_MATERIAL_CONTACT_FRESH_TRIAL_CANDIDATE
    if (ft_history_guards()) return 1;
#endif
    printf("PASS fresh trial suite: %d assertions; %d positives; %d rejected negatives\n",ft_assertions,ft_positives,ft_negatives);
    return 0;
}

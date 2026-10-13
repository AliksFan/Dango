// Frozen captured-state regression. No world, parameter search or seeded mode.
#define main zs_retained_fresh_trial_main
#include "dango_material_contact_fresh_trial_tests.c"
#undef main
#include "dango_material_contact_zero_slide_frames.h"

#define ZS_CHECK(c) do { ++zs_assertions; if (!(c)) { fprintf(stderr,"FAIL zero slide %d: %s\n",__LINE__,#c); return 1; } } while(0)
static int zs_assertions,zs_positives,zs_negatives;

static int zs_bits(const dango_constraint_state* s,const uint32_t recorded[93],
    const dango_material_ground_receipt* receipts,int receipt_count,
    const uint64_t receipt_bits[][4],double step,uint64_t step_bits)
{
    const float scalars[8]={s->radial_length,s->perimeter_length,s->radial_min,s->radial_max,
        s->perimeter_min,s->perimeter_max,s->radius,s->ground};
    int index=0;
    for(int i=0;i<8;++i) { uint32_t bits;memcpy(&bits,&scalars[i],sizeof bits);ZS_CHECK(bits==recorded[index++]); }
    for(int n=0;n<17;++n)
    {
        const float values[5]={s->positions[n].x,s->positions[n].y,s->velocities[n].x,s->velocities[n].y,s->inverse_mass[n]};
        for(int i=0;i<5;++i) { uint32_t bits;memcpy(&bits,&values[i],sizeof bits);ZS_CHECK(bits==recorded[index++]); }
    }
    ZS_CHECK(index==93);
    uint64_t bits;memcpy(&bits,&step,sizeof bits);ZS_CHECK(bits==step_bits);
    for(int n=0;n<receipt_count;++n)
    {
        const dango_material_ground_receipt* r=&receipts[n];
        const double values[4]={r->normal_impulse,r->tangent_impulse,r->friction,r->total_normal_impulse};
        for(int i=0;i<4;++i) { memcpy(&bits,&values[i],sizeof bits);ZS_CHECK(bits==receipt_bits[n][i]); }
    }
    return 0;
}

static int zs_modes(const dango_material_contact_block* block,const dango_constraint_state* output,bool published)
{
    const dango_material_contact_solver* contact=&block->contact;
    double velocities[17][2];
    for(int n=0;n<17;++n)
    {
        velocities[n][0]=published?output->velocities[n].x:contact->velocities[n].x;
        velocities[n][1]=published?output->velocities[n].y:contact->velocities[n].y;
    }
    for(int i=0;i<block->report.active_count;++i)
    {
        int row=block->report.active_row_ids[i];double gap,rate=ft_boundary_rate(&contact->geometry,velocities,row,&gap);
        double normalized=rate/(row>=64&&row<128?1:contact->geometry.radial_length);
        double lambda=contact->rows[row].multiplier;
        dango_material_contact_block_mode mode=block->report.modes[i];
        if(row<144)
        {
            if(mode==DANGO_MATERIAL_CONTACT_BLOCK_ENABLED) ZS_CHECK(fabs(normalized)<=DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
            else ZS_CHECK(mode==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED&&lambda==0&&normalized>=-DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
        }
        else
        {
            int node=row-144;const dango_material_contact_support* support=&contact->support[node];
            double bound=support->friction*(support->base_normal+contact->rows[128+node].multiplier)+support->receipt_cone_slack;
            double total=support->base_tangent+lambda,epsilon=32*DBL_EPSILON*fmax(bound,fabs(total));
            if(mode==DANGO_MATERIAL_CONTACT_BLOCK_ZERO) ZS_CHECK(bound==0&&total==0);
            else if(mode==DANGO_MATERIAL_CONTACT_BLOCK_STICK) ZS_CHECK(bound>0&&fabs(normalized)<=DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
            else
            {
                double sign=mode==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE?1:-1;
                ZS_CHECK(mode==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE||mode==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE);
                ZS_CHECK(fabs(total-sign*bound)<=epsilon&&sign*normalized<=DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
            }
        }
    }
    return 0;
}

static int zs_fixture(const char* name,dango_constraint_state input,
    const dango_material_ground_receipt* receipts,int receipt_count,double step,
    const int* ids,const dango_material_contact_block_mode* initial,const dango_material_contact_block_mode* expected,
    int count,int solves,bool negative)
{
    dango_constraint_state saved=input,output,sentinel;memset(&output,0x5a,sizeof output);sentinel=output;
    dango_material_ground_receipt receipt_copy[16],receipt_saved[16];
    memcpy(receipt_copy,receipts,receipt_count*sizeof receipts[0]);memcpy(receipt_saved,receipt_copy,receipt_count*sizeof receipts[0]);
    dango_material_contact_block block;dango_material_contact_block_report report;
    ZS_CHECK(dango_material_contact_block_build(&input,receipt_copy,receipt_count,step,&block,&report));
    ZS_CHECK(report.active_count==count&&report.capacity==32&&report.solve_limit==32);
    ZS_CHECK(report.mode_history_count==0&&report.linear_solves==0);
    for(int i=0;i<count;++i) ZS_CHECK(report.active_row_ids[i]==ids[i]&&report.modes[i]==initial[i]);
    dango_material_contact_solver reference=block.contact;
    dango_material_contact_block probe=block;
    ZS_CHECK(dango_mcb_record_modes(&probe.report)&&dango_mcb_system(&probe.contact,&probe.report));
    double independent[32][17][2]={{{0}}};
    for(int i=0;i<count;++i) for(int n=0;n<17;++n) for(int axis=0;axis<2;++axis)
    { double basis[17][2]={{0}},gap;basis[n][axis]=1;independent[i][n][axis]=ft_boundary_rate(&input,basis,ids[i],&gap); }
    for(int i=0;i<count;++i) for(int j=0;j<count;++j)
    {
        double gram=0;
        for(int n=0;n<17;++n) gram+=input.inverse_mass[n]*(independent[i][n][0]*independent[j][n][0]+independent[i][n][1]*independent[j][n][1]);
        if(i==j) ZS_CHECK(fabs(probe.report.scaling[i]-1/sqrt(gram))<=2e-12*fmax(1,fabs(probe.report.scaling[i])));
        double equation=initial[i]==DANGO_MATERIAL_CONTACT_BLOCK_ZERO?(i==j?1:0):gram;
        ZS_CHECK(fabs(equation-probe.report.raw_matrix[i][j])<=2e-12*fmax(1,fabs(equation)));
    }
    // Only the natural initial modes are present at call entry.
    bool solved=dango_material_contact_block_solve(&block,&output,&report);
    ZS_CHECK(memcmp(&input,&saved,sizeof input)==0&&memcmp(receipt_copy,receipt_saved,receipt_count*sizeof receipts[0])==0);
    ZS_CHECK(memcmp(&block.contact.geometry,&reference.geometry,sizeof reference.geometry)==0);
    ZS_CHECK(memcmp(block.contact.input_velocities,reference.input_velocities,sizeof reference.input_velocities)==0);
    ZS_CHECK(memcmp(block.contact.support,reference.support,sizeof reference.support)==0);
    ZS_CHECK(memcmp(block.contact.receipts,reference.receipts,sizeof reference.receipts)==0);
    ZS_CHECK(block.contact.step_seconds==reference.step_seconds&&block.contact.receipt_count==reference.receipt_count);
    if(negative)
    {
        ZS_CHECK(!solved&&report.status==DANGO_MATERIAL_CONTACT_BLOCK_REPEATED_MODE);
        ZS_CHECK(report.mode_history_count==solves&&report.linear_solves==solves&&report.full_checks==solves&&report.float_checks==0);
        ZS_CHECK(memcmp(&output,&sentinel,sizeof output)==0);
        ZS_CHECK(!report.final_rows.ledger_evaluated&&!report.final_rows.float_output_checked);
        if(count==6||count==8)
        {
            int normal=count==6?2:6,tangent=count==6?4:7;
            ZS_CHECK(report.mode_history[0][normal]==DANGO_MATERIAL_CONTACT_BLOCK_ENABLED&&report.mode_history[0][tangent]==DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
            ZS_CHECK(report.mode_history[1][tangent]==DANGO_MATERIAL_CONTACT_BLOCK_STICK);
            ZS_CHECK(report.mode_history[2][normal]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED&&report.mode_history[2][tangent]==DANGO_MATERIAL_CONTACT_BLOCK_STICK);
            ZS_CHECK(report.mode_history[3][normal]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED&&report.mode_history[3][tangent]==DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
            ZS_CHECK(report.modes[normal]==DANGO_MATERIAL_CONTACT_BLOCK_ENABLED&&report.modes[tangent]==DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
        }
        else if(count==10)
        {
            ZS_CHECK(report.mode_history[0][8]==DANGO_MATERIAL_CONTACT_BLOCK_STICK&&report.mode_history[0][9]==DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
            ZS_CHECK(report.mode_history[1][8]==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE&&report.mode_history[1][9]==DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
            ZS_CHECK(report.mode_history[2][9]==DANGO_MATERIAL_CONTACT_BLOCK_STICK);
            ZS_CHECK(report.mode_history[3][7]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED&&report.mode_history[3][9]==DANGO_MATERIAL_CONTACT_BLOCK_STICK);
            ZS_CHECK(report.mode_history[4][7]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED&&report.mode_history[4][9]==DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
            ZS_CHECK(report.modes[7]==DANGO_MATERIAL_CONTACT_BLOCK_ENABLED&&report.modes[8]==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE&&report.modes[9]==DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
        }
        else if(count==13)
        {
            ZS_CHECK(report.mode_history[0][11]==DANGO_MATERIAL_CONTACT_BLOCK_STICK&&report.mode_history[0][12]==DANGO_MATERIAL_CONTACT_BLOCK_STICK);
            ZS_CHECK(report.mode_history[1][11]==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE&&report.mode_history[1][12]==DANGO_MATERIAL_CONTACT_BLOCK_STICK);
            ZS_CHECK(report.mode_history[2][11]==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE&&report.mode_history[2][12]==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE);
            ZS_CHECK(report.mode_history[3][11]==DANGO_MATERIAL_CONTACT_BLOCK_STICK&&report.mode_history[3][12]==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE);
            ZS_CHECK(report.modes[11]==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE&&report.modes[12]==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE);
        }
        else if(count==15)
        {
            ZS_CHECK(report.mode_history[0][11]==DANGO_MATERIAL_CONTACT_BLOCK_ENABLED&&report.mode_history[0][14]==DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
            ZS_CHECK(report.mode_history[1][11]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED&&report.mode_history[1][14]==DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
            ZS_CHECK(report.mode_history[2][14]==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE);
            ZS_CHECK(report.mode_history[3][5]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
            ZS_CHECK(report.mode_history[4][12]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
            ZS_CHECK(report.mode_history[5][14]==DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
            ZS_CHECK(report.mode_history[6][5]==DANGO_MATERIAL_CONTACT_BLOCK_ENABLED&&report.mode_history[6][12]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
            ZS_CHECK(report.modes[11]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED&&report.modes[12]==DANGO_MATERIAL_CONTACT_BLOCK_ENABLED&&report.modes[14]==DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
        }
        ++zs_negatives;printf("PASS %s old controller: %d-mode repetition; no publication\n",name,solves);return 0;
    }
    if(!solved) fprintf(stderr,"%s status=%d solves=%d full=%d float=%d\n",name,report.status,report.linear_solves,report.full_checks,report.float_checks);
    ZS_CHECK(solved&&report.status==DANGO_MATERIAL_CONTACT_BLOCK_OK);
    ZS_CHECK(report.mode_history_count==solves&&report.linear_solves==solves&&report.linear_solves<=32&&report.float_checks==1);
    for(int i=0;i<count;++i) ZS_CHECK(report.active_row_ids[i]==ids[i]&&report.modes[i]==expected[i]);
    for(int i=0;i<solves;++i) for(int j=0;j<i;++j) ZS_CHECK(memcmp(report.mode_history[i],report.mode_history[j],count*sizeof report.modes[0])!=0);
    ZS_CHECK(report.final_rows.ledger_evaluated&&report.final_rows.float_output_checked&&report.row_rates_available);
    ZS_CHECK(report.matrix_mode_index==solves-1&&report.solution_mode_index==solves-1&&report.rates_mode_index==solves-1);
    ZS_CHECK(memcmp(output.positions,input.positions,sizeof input.positions)==0&&memcmp(output.inverse_mass,input.inverse_mass,sizeof input.inverse_mass)==0);
    ZS_CHECK(ft_independent_final(&block,&output,false)==0&&ft_independent_final(&block,&output,true)==0);
    ZS_CHECK(zs_modes(&block,&output,false)==0&&zs_modes(&block,&output,true)==0);
    dango_material_contact_report checked;ZS_CHECK(dango_material_contact_validate(&block.contact,&output,&checked));
    ZS_CHECK(checked.ledger_evaluated&&checked.float_output_checked);
    float maximum=0;for(int n=0;n<17;++n) maximum=fmaxf(maximum,b2Length(output.velocities[n]));
    float scale=maximum>12?(12*.99999f)/maximum:1;
    dango_constraint_state limited=output;for(int n=0;n<17;++n) limited.velocities[n]=b2MulSV(scale,limited.velocities[n]);
    dango_material_contact_cap_report cap;ZS_CHECK(dango_material_contact_block_validate_after_uniform_scale(&block,&output,&limited,scale,12,&cap));
    ZS_CHECK(cap.contact_output_checked&&cap.exact_common_scale_checked&&cap.maximum_after<=12&&cap.final_rows.ledger_evaluated);
    // A downward corruption at an actual support must still be rejected.
    dango_constraint_state corrupt=output;int ground=0;while(ids[ground]<128||ids[ground]>=144) ++ground;
    corrupt.velocities[ids[ground]-128].y=-1;
    ZS_CHECK(!dango_material_contact_validate(&block.contact,&corrupt,&checked));++zs_negatives;++zs_positives;
    printf("PASS %s: %d natural distinct modes; all160 independent double/float physical/mode/energy/momentum/angular/velocity ledger and original cap\n",name,solves);return 0;
}

#define ZS_BITS(name) ZS_CHECK(zs_bits(&s,dango_material_contact_##name##_float_bits,dango_material_contact_##name##_receipts,(int)(sizeof dango_material_contact_##name##_receipts/sizeof dango_material_contact_##name##_receipts[0]),dango_material_contact_##name##_receipt_bits,dango_material_contact_##name##_step,dango_material_contact_##name##_step_bits)==0)
#define ZS_RUN(name,count,solves,negative) ZS_CHECK(zs_fixture(#name,s,dango_material_contact_##name##_receipts,(int)(sizeof dango_material_contact_##name##_receipts/sizeof dango_material_contact_##name##_receipts[0]),dango_material_contact_##name##_step,ids,initial,expected,count,solves,negative)==0)

#ifndef DANGO_MATERIAL_ZERO_SLIDE_NO_MAIN
int main(int argc,char** argv)
{
    bool negative_zero=argc==2&&strcmp(argv[1],"--negative-zero")==0;
    bool negative_cone=argc==2&&strcmp(argv[1],"--negative-cone")==0;
    if(argc>2||(argc==2&&!negative_zero&&!negative_cone)) return 2;
#ifdef DANGO_MATERIAL_CONTACT_ZERO_TO_SLIDE_CANDIDATE
    if(negative_zero) return 2;
#else
    if(!negative_zero) return 2;
#endif
#ifdef DANGO_MATERIAL_CONTACT_OUTCONE_FRESH_TRIAL_CANDIDATE
    if(negative_cone) return 2;
#else
    if(!negative_zero&&!negative_cone) return 2;
#endif
    {
        dango_constraint_state s=dango_material_contact_horizontal2886_state();ZS_BITS(horizontal2886);
        const int ids[6]={51,53,139,140,155,156};const dango_material_contact_block_mode initial[6]={0,0,0,0,5,2},expected[6]={0,0,0,0,3,2};
        ZS_RUN(horizontal2886,6,negative_zero?4:2,negative_zero);
    }
    {
        dango_constraint_state s=dango_material_contact_changing4896_state();ZS_BITS(changing4896);
        const int ids[10]={1,24,53,55,57,90,137,138,153,154};const dango_material_contact_block_mode initial[10]={0,0,0,0,0,0,0,0,2,5},expected[10]={0,0,0,0,0,0,0,0,4,4};
        ZS_RUN(changing4896,10,negative_zero?5:3,negative_zero);
    }
    {
        dango_constraint_state s=dango_material_contact_relaxed2255_state();ZS_BITS(relaxed2255);
        const int ids[8]={33,35,37,59,61,63,131,147};const dango_material_contact_block_mode initial[8]={0,0,0,0,0,0,0,5},expected[8]={0,0,0,0,0,0,0,3};
        ZS_RUN(relaxed2255,8,negative_zero?4:2,negative_zero);
    }
    if(!negative_zero)
    {
        dango_constraint_state s=dango_material_contact_coupled3461_state();ZS_BITS(coupled3461);
        const int ids[13]={0,14,33,35,37,39,63,78,80,135,136,151,152};const dango_material_contact_block_mode initial[13]={0,0,0,0,0,0,0,0,0,0,0,2,2},expected[13]={0,0,0,0,0,0,0,0,0,1,0,3,3};
        ZS_RUN(coupled3461,13,negative_cone?4:10,negative_cone);
    }
    printf("PASS zero-slide suite: assertions=%d independent=%d positives=%d negatives=%d\n",zs_assertions,ft_assertions,zs_positives,zs_negatives);return 0;
}
#endif

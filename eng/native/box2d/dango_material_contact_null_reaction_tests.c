// Approved nullity-one reaction fixtures. Mathematical helper inputs are
// explicitly unit fixtures; no edited mode/report is called a world trajectory.
#ifndef DANGO_MATERIAL_CONTACT_NULL_REACTION_CANDIDATE
#define DANGO_MATERIAL_CONTACT_NULL_REACTION_CANDIDATE
#endif
#define main null_reaction_retained_block_main
#include "dango_material_contact_block_tests.c"
#undef main
#include "dango_material_contact_null_reaction.h"
#include "dango_material_contact_block_frame147.h"

#define NR_CHECK(c) do { ++bc_assertions; if (!(c)) { fprintf(stderr,"FAIL null reaction %d: %s\n",__LINE__,#c); return 1; } } while (0)

static int nr_block_stage_indices(const dango_material_contact_block_report* report)
{
    const dango_material_contact_null_report* nr=&report->null_reaction;
    const bool available[8]={nr->factor_available,nr->raw_pair_available,nr->effective_pair_available,nr->pair_checks_available,
        nr->reaction_checks_available,nr->interval_available,nr->selection_available,nr->selected_pair_available};
    const int indices[8]={nr->factor_mode_index,nr->raw_pair_mode_index,nr->effective_pair_mode_index,nr->pair_checks_mode_index,
        nr->reaction_mode_index,nr->interval_mode_index,nr->selection_mode_index,nr->selected_mode_index};
    NR_CHECK(nr->mode_index==report->matrix_mode_index);
    for (int i=0;i<8;++i) NR_CHECK(indices[i]==(available[i]?nr->mode_index:-1));
    NR_CHECK(!nr->factor_passed || nr->factor_available);
    NR_CHECK(!nr->pair_checks_passed || nr->pair_checks_available);
    NR_CHECK(!nr->reaction_checks_passed || nr->reaction_checks_available);
    NR_CHECK(!nr->interval_passed || nr->interval_available);
    NR_CHECK(!nr->selection_passed || nr->selection_available);
    NR_CHECK(!nr->selected_checks_passed || nr->selected_pair_available);
    NR_CHECK(!nr->used || nr->selected_checks_passed);
    return 0;
}

static int nr_success(const dango_constraint_state* input,const dango_material_ground_receipt* receipts,
    int count,double dt,dango_material_contact_block* block,dango_constraint_state* output)
{
    dango_constraint_state saved; memcpy(&saved,input,sizeof saved);
    dango_material_contact_block_report report;
    NR_CHECK(dango_material_contact_block_build(input,receipts,count,dt,block,&report));
    memset(output,0x5a,sizeof(*output)); dango_constraint_state output_before; memcpy(&output_before,output,sizeof output_before);
    bool solved=dango_material_contact_block_solve(block,output,&report);
    NR_CHECK(nr_block_stage_indices(&report)==0);
    if (!solved)
    {
        const dango_material_contact_null_report* nr=&report.null_reaction;
        printf("real null candidate failure: block=%d null=%d rank=%d nullity=%d solves=%d full=%d float=%d mode=%d; availability factor=%d raw=%d effective=%d pair=%d reaction=%d interval=%d selection=%d selected=%d; pivot=%llu/%llu\n",
            (int)report.status,(int)nr->status,nr->rank,nr->nullity,report.linear_solves,report.full_checks,report.float_checks,nr->mode_index,
            nr->factor_available,nr->raw_pair_available,nr->effective_pair_available,nr->pair_checks_available,nr->reaction_checks_available,
            nr->interval_available,nr->selection_available,nr->selected_pair_available,(unsigned long long)report.pivot_searches,(unsigned long long)report.pivot_eliminations);
        for (int i=0;i<report.active_count;++i)
            printf("  row%d: p=%.17g allowance=%.17g q=%.17g allowance=%.17g rawp=%.17g rawq=%.17g effectivep=%.17g effectivek=%.17g\n",
                report.active_row_ids[i],nr->particular_residual[i],nr->particular_allowance[i],nr->direction_residual[i],nr->direction_allowance[i],
                nr->raw_z_particular[i],nr->raw_z_direction[i],nr->effective_lambda_particular[i],nr->effective_lambda_direction[i]);
        NR_CHECK(memcmp(output,&output_before,sizeof output_before)==0);
        NR_CHECK(!report.linear_solution_available && !nr->used);
        if (nr->status==DANGO_MATERIAL_CONTACT_NULL_INCONSISTENT)
        {
            NR_CHECK(nr->factor_passed && nr->pair_checks_available && !nr->pair_checks_passed);
            NR_CHECK(!nr->reaction_checks_available && !nr->reaction_checks_passed && !nr->interval_available && !nr->interval_passed);
            NR_CHECK(!nr->selection_available && !nr->selection_passed && !nr->selected_pair_available && !nr->selected_checks_passed);
        }
    }
    NR_CHECK(memcmp(input,&saved,sizeof saved)==0);
    NR_CHECK(solved); // Required real-frame positive stays failure if unsupported.
    NR_CHECK(report.status==DANGO_MATERIAL_CONTACT_BLOCK_OK && report.capacity==16 && report.solve_limit==32);
    NR_CHECK(report.linear_solves<=32 && report.mode_history_count==report.linear_solves);
    NR_CHECK(report.full_checks==report.linear_solves && report.float_checks==1);
    NR_CHECK(report.matrix_available && report.linear_solution_available && report.row_rates_available);
    NR_CHECK(report.final_rows.ledger_evaluated && report.final_rows.float_output_checked);
    NR_CHECK(memcmp(input,&saved,sizeof saved)==0);
    NR_CHECK(bc_independent_final(block,output,false)==0);
    NR_CHECK(bc_independent_final(block,output,true)==0);
    dango_material_contact_report final_report;
    NR_CHECK(dango_material_contact_validate(&block->contact,output,&final_report));
    NR_CHECK(bc_cap(block,output,12)==0);
    ++bc_positives;
    return 0;
}

static int nr_frame147(void)
{
    dango_constraint_state s=dango_material_contact_frame147_state(),output;
    // Independent directional derivative identity over every physical DOF.
    for (int n=0;n<17;++n) for (int axis=0;axis<2;++axis)
    {
        double basis[17][2]={{0}},gap; basis[n][axis]=1;
        double perimeter=bc_boundary_rate(&s,basis,55,&gap);
        double left=bc_boundary_rate(&s,basis,155,&gap),right=bc_boundary_rate(&s,basis,156,&gap);
        NR_CHECK(perimeter-left+right==0);
    }
    dango_material_contact_block initial,block;
    dango_material_contact_block_report report;
    NR_CHECK(dango_material_contact_block_build(&s,dango_material_contact_frame147_receipts,4,
        dango_material_contact_frame147_step_seconds,&initial,&report));
    NR_CHECK(report.active_count==9);
    // Retained scalar-factor routine rejects this identical first-mode matrix.
    // The old whole-block executable/latch is also run separately by root.
    NR_CHECK(dango_mcb_system(&initial.contact,&report));
    double old_solution[16]={0},saved_solution[16]; memcpy(saved_solution,old_solution,sizeof old_solution);
    NR_CHECK(!dango_mcb_linear_solve(9,report.active_row_ids,report.scaled_matrix,
        report.scaled_rhs,old_solution,&report));
    NR_CHECK(report.status==DANGO_MATERIAL_CONTACT_BLOCK_RANK);
    NR_CHECK(report.pivot_searches==285 && report.pivot_eliminations==8);
    NR_CHECK(memcmp(old_solution,saved_solution,sizeof old_solution)==0); ++bc_negatives;
    NR_CHECK(nr_success(&s,dango_material_contact_frame147_receipts,4,
        dango_material_contact_frame147_step_seconds,&block,&output)==0);
    const int expected[9]={22,24,55,86,88,139,140,155,156};
    NR_CHECK(block.report.active_count==9 && block.report.linear_solves==1);
    for (int i=0;i<9;++i) NR_CHECK(block.report.active_row_ids[i]==expected[i]);
    NR_CHECK(block.contact.receipt_count==4 && memcmp(block.contact.receipts,dango_material_contact_frame147_receipts,
        sizeof dango_material_contact_frame147_receipts)==0);
    NR_CHECK(block.report.pivot_searches==285 && block.report.pivot_eliminations==8);
    printf("frame147 null reaction: n=%d solves=%d full=%d float=%d angular=%.17g energy=%.17g->%.17g; old matrix rejected\n",
        block.report.active_count,block.report.linear_solves,block.report.full_checks,block.report.float_checks,
        block.report.final_rows.maximum_angular_closing,block.report.final_rows.energy_before,block.report.final_rows.energy_after);
    return 0;
}

static void nr_report_numbers(const dango_material_contact_report* r,double numbers[31])
{
    const double values[31]={r->geometry_residual,r->maximum_linear_closing,r->maximum_relative_linear_closing,
        r->maximum_angular_closing,r->maximum_relative_linear_complementarity,r->maximum_angular_complementarity,
        r->maximum_relative_friction_rate,r->maximum_friction_cone_excess,r->maximum_receipt_cone_slack,
        r->energy_before,r->energy_after,r->energy_allowance,
        r->momentum_before.x,r->momentum_before.y,r->momentum_after.x,r->momentum_after.y,
        r->external_impulse.x,r->external_impulse.y,r->momentum_residual,r->momentum_allowance,
        r->angular_momentum_before,r->angular_momentum_after,r->external_torque,r->angular_momentum_residual,
        r->angular_momentum_allowance,r->maximum_relative_velocity_ledger_residual,r->maximum_relative_velocity_ledger_allowance,
        (double)r->active_shape_rows,(double)r->active_normal_rows,(double)r->active_tangent_rows,(double)r->status};
    memcpy(numbers,values,sizeof values);
}

static int nr_full_rank_exact(void)
{
    dango_constraint_state s=dango_material_contact_frame29_state(),output,old_output;
    dango_material_contact_block block,baseline;
    dango_material_contact_block_report old_cost;
    NR_CHECK(dango_material_contact_block_build(&s,dango_material_contact_frame29_receipts,4,
        dango_material_contact_frame29_step_seconds,&baseline,&old_cost));
    NR_CHECK(dango_mcb_system(&baseline.contact,&old_cost));
    double old_z[16]={0};
    NR_CHECK(dango_mcb_linear_solve(8,old_cost.active_row_ids,old_cost.scaled_matrix,old_cost.scaled_rhs,old_z,&old_cost));
    double direct_z[16]={0}; dango_material_contact_null_report direct;
    NR_CHECK(dango_mcn_factor(8,old_cost.active_row_ids,old_cost.scaled_matrix,old_cost.scaled_rhs,direct_z,&direct));
    NR_CHECK(direct.status==DANGO_MATERIAL_CONTACT_NULL_OK && direct.factor_available && direct.rank==8 && direct.nullity==0);
    NR_CHECK(!direct.raw_pair_available && !direct.effective_pair_available && !direct.selection_available && !direct.used);
    NR_CHECK(memcmp(direct_z,old_z,sizeof old_z)==0);
    NR_CHECK(direct.pivot_searches==old_cost.pivot_searches && direct.pivot_eliminations==old_cost.pivot_eliminations);
    NR_CHECK(direct.matrix_infinity_norm==old_cost.matrix_infinity_norm && direct.pivot_threshold==old_cost.pivot_threshold);
    dango_material_contact_solver reference=baseline.contact;
    for (int i=0;i<8;++i)
        baseline.contact.rows[old_cost.active_row_ids[i]].multiplier=old_cost.scaling[i]*old_z[i];
    for (int n=0;n<17;++n) baseline.contact.velocities[n]=reference.input_velocities[n];
    for (int row=0;row<160;++row)
    {
        const dango_material_contact_row* r=&reference.rows[row];
        if (!r->active) continue;
        for (int j=0;j<r->count;++j)
        {
            int n=r->ids[j];
            dango_material_contact_vec2 delta=dango_mc_mul(reference.geometry.inverse_mass[n]*baseline.contact.rows[row].multiplier,r->gradients[j]);
            baseline.contact.velocities[n]=dango_mc_add(baseline.contact.velocities[n],delta);
        }
    }
    old_output=s;
    for (int n=0;n<17;++n) old_output.velocities[n]=(b2Vec2){(float)baseline.contact.velocities[n].x,(float)baseline.contact.velocities[n].y};
    dango_material_contact_report old_final=reference.report;
    NR_CHECK(dango_mc_inspect_rows(&baseline.contact,&reference,baseline.contact.velocities,&old_final));
    NR_CHECK(dango_mc_inspect_ledger(&baseline.contact,&reference,baseline.contact.velocities,false,&old_final));
    baseline.contact.report=old_final;
    NR_CHECK(dango_material_contact_validate(&baseline.contact,&old_output,&old_final));
    NR_CHECK(nr_success(&s,dango_material_contact_frame29_receipts,4,
        dango_material_contact_frame29_step_seconds,&block,&output)==0);
    NR_CHECK(memcmp(block.contact.velocities,baseline.contact.velocities,sizeof baseline.contact.velocities)==0);
    NR_CHECK(memcmp(&output,&old_output,sizeof output)==0);
    NR_CHECK(memcmp(block.report.linear_solution,old_z,8*sizeof(double))==0);
    NR_CHECK(block.report.pivot_searches==old_cost.pivot_searches && block.report.pivot_eliminations==old_cost.pivot_eliminations);
    double old_numbers[31],new_numbers[31];
    nr_report_numbers(&old_final,old_numbers); nr_report_numbers(&block.report.final_rows,new_numbers);
    NR_CHECK(memcmp(old_numbers,new_numbers,sizeof old_numbers)==0);
    NR_CHECK(old_final.float_output_checked==block.report.final_rows.float_output_checked &&
        old_final.ledger_evaluated==block.report.final_rows.ledger_evaluated);
    printf("frame29 retained full-rank path: double/float/31 physical fields/solution/pivot costs bit exact\n");
    return 0;
}

// Exact two-row mathematical unit: M has one redundant coordinate and p=(-1,0),
// k=(1,1). Two opposite per-body gradients make k a pure reaction. The rows
// below are synthetic; this is not a captured state or a Box2D world trajectory.
static int nr_pair_unit(dango_material_contact_null_report* report,dango_material_contact_solver* reference)
{
    const int ids[2]={22,55}; const double matrix[16][16]={{1,-1},{-1,1}},rhs[16]={-1,1};
    const double scaling[16]={1,1},fixed_values[16]={0}; const bool fixed[16]={false};
    double untouched[16],saved[16]; for (int i=0;i<16;++i) untouched[i]=1234+i; memcpy(saved,untouched,sizeof saved);
    NR_CHECK(dango_mcn_factor(2,ids,matrix,rhs,untouched,report));
    NR_CHECK(report->status==DANGO_MATERIAL_CONTACT_NULL_OK && report->rank==1 && report->nullity==1 && report->free_column_id==55);
    NR_CHECK(report->factor_available && report->raw_pair_available && !report->effective_pair_available);
    NR_CHECK(report->dimension==2 && report->factor_passed && !report->pair_checks_passed && report->factor_mode_index==-1 && report->raw_pair_mode_index==-1);
    NR_CHECK(report->pivot_searches==5 && report->pivot_eliminations==1 && memcmp(untouched,saved,sizeof saved)==0);
    NR_CHECK(report->raw_z_particular[0]==-1 && report->raw_z_particular[1]==0 && report->raw_z_direction[0]==1 && report->raw_z_direction[1]==1);
    NR_CHECK(dango_mcn_effective_pair(2,ids,scaling,fixed,fixed_values,matrix,rhs,report));
    NR_CHECK(report->effective_pair_available && report->pair_checks_available && report->direction_normalization==1);
    NR_CHECK(report->pair_checks_passed && report->effective_pair_mode_index==-1 && report->pair_checks_mode_index==-1);
    for (int i=0;i<2;++i) NR_CHECK(report->particular_residual[i]==0 && report->direction_residual[i]==0);
    memset(reference,0,sizeof(*reference));
    reference->geometry.inverse_mass[0]=.25f; reference->geometry.inverse_mass[1]=2;
    for (int i=0;i<2;++i)
    {
        dango_material_contact_row* row=&reference->rows[ids[i]]; row->id=ids[i]; row->count=2;
        row->ids[0]=0; row->ids[1]=1; row->gradients[0]=dango_mc_v(i?1:-1,0); row->gradients[1]=dango_mc_v(i?-1:1,0);
    }
    NR_CHECK(dango_mcn_reaction_null(reference,2,ids,report));
    NR_CHECK(report->reaction_checks_available && !report->interval_available && !report->used);
    NR_CHECK(report->reaction_checks_passed && report->reaction_mode_index==-1 && !report->interval_passed && !report->selection_passed && !report->selected_checks_passed);
    for (int b=0;b<17;++b) NR_CHECK(report->reaction_residual[b]==0);
    NR_CHECK(report->reaction_scale[0]==.5 && report->reaction_scale[1]==4);
    return 0;
}

static int nr_sign_repair(void)
{
    dango_material_contact_null_report report; dango_material_contact_solver reference;
    NR_CHECK(nr_pair_unit(&report,&reference)==0);
    const int ids[2]={22,55}; const double matrix[16][16]={{1,-1},{-1,1}},rhs[16]={-1,1},scaling[16]={1,1};
    dango_material_contact_solver saved=reference;
    NR_CHECK(dango_mcn_interval_init(&report));
    // Independent nonnegative physical reactions: -(p_i+alpha*k_i)<=0.
    NR_CHECK(dango_mcn_interval_add(&report,22,0,-1,1));
    NR_CHECK(dango_mcn_interval_add(&report,55,0,-1,0));
    NR_CHECK(report.lower_bounded && report.lower==1 && !report.upper_bounded);
    NR_CHECK(dango_mcn_interval_select(&report) && report.selected_alpha==1);
    double lambda[16]={0},z[16]={0};
    NR_CHECK(dango_mcn_selected(2,scaling,matrix,rhs,&report,lambda,z));
    NR_CHECK(report.used && report.selected_pair_available && lambda[0]==0 && lambda[1]==1 && z[0]==0 && z[1]==1);
    NR_CHECK(report.constraint_count==2 && report.pivot_searches==5 && report.pivot_eliminations==1);
    for (int i=0;i<2;++i) NR_CHECK(report.selected_residual[i]==0 && -lambda[i]<=0);
    // Selection changes no body velocity relative to the raw particular.
    for (int b=0;b<17;++b)
    {
        double before[2]={0},after[2]={0};
        for (int i=0;i<2;++i) for (int j=0;j<reference.rows[ids[i]].count;++j)
            if (reference.rows[ids[i]].ids[j]==b)
            {
                before[0]+=reference.geometry.inverse_mass[b]*reference.rows[ids[i]].gradients[j].x*report.effective_lambda_particular[i];
                before[1]+=reference.geometry.inverse_mass[b]*reference.rows[ids[i]].gradients[j].y*report.effective_lambda_particular[i];
                after[0]+=reference.geometry.inverse_mass[b]*reference.rows[ids[i]].gradients[j].x*lambda[i];
                after[1]+=reference.geometry.inverse_mass[b]*reference.rows[ids[i]].gradients[j].y*lambda[i];
            }
        NR_CHECK(before[0]==after[0] && before[1]==after[1]);
    }
    NR_CHECK(memcmp(&reference,&saved,sizeof saved)==0); ++bc_positives;
    return 0;
}

static int nr_interval_begin(dango_material_contact_null_report* r)
{
    dango_material_contact_solver reference;
    NR_CHECK(nr_pair_unit(r,&reference)==0);
    NR_CHECK(dango_mcn_interval_init(r));
    return 0;
}

static int nr_intervals(void)
{
    dango_material_contact_null_report r;
    NR_CHECK(nr_interval_begin(&r)==0);
    NR_CHECK(dango_mcn_interval_select(&r) && r.selected_alpha==0 && !r.lower_bounded && !r.upper_bounded); ++bc_positives;
    NR_CHECK(nr_interval_begin(&r)==0);
    NR_CHECK(dango_mcn_interval_add(&r,22,0,-1,2));
    NR_CHECK(dango_mcn_interval_add(&r,55,0,1,-4));
    NR_CHECK(dango_mcn_interval_select(&r) && r.selected_alpha==2 && r.lower==2 && r.upper==4); ++bc_positives;
    NR_CHECK(nr_interval_begin(&r)==0);
    NR_CHECK(dango_mcn_interval_add(&r,22,0,1,2));
    NR_CHECK(dango_mcn_interval_select(&r) && r.selected_alpha==-2 && !r.lower_bounded && r.upper_bounded); ++bc_positives;
    NR_CHECK(nr_interval_begin(&r)==0);
    NR_CHECK(dango_mcn_interval_add(&r,22,0,-1,-1));
    NR_CHECK(dango_mcn_interval_add(&r,55,0,1,-1));
    NR_CHECK(dango_mcn_interval_select(&r) && r.selected_alpha==0); ++bc_positives;
    NR_CHECK(nr_interval_begin(&r)==0);
    NR_CHECK(dango_mcn_interval_add(&r,22,0,-1,3));
    NR_CHECK(dango_mcn_interval_add(&r,55,0,1,-3));
    NR_CHECK(dango_mcn_interval_select(&r) && r.lower==r.upper && r.selected_alpha==3);
    NR_CHECK(r.constraints[0].has_endpoint && r.constraints[0].lower_bounded_after && !r.constraints[0].upper_bounded_after);
    NR_CHECK(r.constraints[1].upper_bounded_after && r.constraint_count==2); ++bc_positives;
    NR_CHECK(nr_interval_begin(&r)==0);
    NR_CHECK(dango_mcn_interval_add(&r,22,0,-1,3));
    NR_CHECK(!dango_mcn_interval_add(&r,55,0,1,-2));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY && r.empty_row_id==55); ++bc_negatives;
    NR_CHECK(nr_interval_begin(&r)==0);
    NR_CHECK(dango_mcn_interval_add(&r,22,0,0,0));
    NR_CHECK(dango_mcn_interval_add(&r,24,0,0,-1));
    NR_CHECK(!dango_mcn_interval_add(&r,55,0,0,DBL_MIN));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY && r.empty_row_id==55 && !r.constraints[2].has_endpoint); ++bc_negatives;
    NR_CHECK(nr_interval_begin(&r)==0);
    NR_CHECK(!dango_mcn_interval_add(&r,55,0,DBL_MIN,DBL_MAX));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_NONFINITE && !r.lower_bounded && !r.upper_bounded); ++bc_negatives;
    NR_CHECK(nr_interval_begin(&r)==0);
    NR_CHECK(!dango_mcn_interval_add(&r,55,0,INFINITY,1));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_NONFINITE); ++bc_negatives;
    NR_CHECK(nr_interval_begin(&r)==0);
    NR_CHECK(!dango_mcn_interval_add(&r,55,0,1,NAN));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_NONFINITE); ++bc_negatives;
    // Tiny finite coefficients are real finite endpoints, not an unbounded end.
    NR_CHECK(nr_interval_begin(&r)==0);
    NR_CHECK(dango_mcn_interval_add(&r,55,0,-DBL_MIN,DBL_MIN));
    NR_CHECK(dango_mcn_interval_select(&r) && r.selected_alpha==1 && r.lower_bounded && r.lower==1); ++bc_positives;
    NR_CHECK(nr_interval_begin(&r)==0);
    for (int i=0;i<64;++i) NR_CHECK(dango_mcn_interval_add(&r,22,0,0,0));
    NR_CHECK(r.constraint_count==64 && r.interval_passed);
    NR_CHECK(!dango_mcn_interval_add(&r,22,0,0,0));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT && r.constraint_count==64 && !r.selection_available && !r.used); ++bc_negatives;
    return 0;
}

static int nr_algebra_rejections(void)
{
    const int ids[3]={22,24,55}; double output[16],saved[16];
    for (int i=0;i<16;++i) output[i]=5678+i; memcpy(saved,output,sizeof saved);
    dango_material_contact_null_report r;
    const double two_null[16][16]={{1}},zero_rhs[16]={0};
    NR_CHECK(!dango_mcn_factor(3,ids,two_null,zero_rhs,output,&r));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_NULLITY && r.rank==1 && r.nullity==2 && r.factor_available && !r.raw_pair_available);
    NR_CHECK(memcmp(output,saved,sizeof saved)==0); ++bc_negatives;
    const double incompatible[16][16]={{1}},tiny_rhs[16]={0,1e-20},scaling[16]={1,1},fixed_values[16]={0};
    const bool fixed[16]={false};
    NR_CHECK(dango_mcn_factor(2,ids,incompatible,tiny_rhs,output,&r));
    NR_CHECK(!dango_mcn_effective_pair(2,ids,scaling,fixed,fixed_values,incompatible,tiny_rhs,&r));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_INCONSISTENT && r.pair_checks_available);
    NR_CHECK(r.particular_residual[1]==-1e-20 && r.particular_allowance[1]==128*2*DBL_EPSILON*1e-20);
    NR_CHECK(fabs(r.particular_residual[1])>r.particular_allowance[1]);
    NR_CHECK(!r.selection_available && !r.selected_pair_available && !r.used && memcmp(output,saved,sizeof saved)==0); ++bc_negatives;
    const double nonfinite[16][16]={{NAN}};
    NR_CHECK(!dango_mcn_factor(1,ids,nonfinite,zero_rhs,output,&r));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_NONFINITE && !r.factor_available && !r.raw_pair_available);
    NR_CHECK(memcmp(output,saved,sizeof saved)==0); ++bc_negatives;
    NR_CHECK(!dango_mcn_factor(2,NULL,incompatible,zero_rhs,output,&r));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT && !r.factor_available); ++bc_negatives;
    return 0;
}

static int nr_nonreaction(void)
{
    dango_material_contact_null_report r; dango_material_contact_solver reference;
    NR_CHECK(nr_pair_unit(&r,&reference)==0);
    const int ids[2]={22,55};
    // Globally balanced equal/opposite impulses along horizontal and vertical
    // pairs have zero total linear/angular impulse, but change each body.
    memset(&reference,0,sizeof reference);
    for (int i=0;i<4;++i) reference.geometry.inverse_mass[i]=(float)(1<<i);
    reference.geometry.positions[0]=(b2Vec2){1,0}; reference.geometry.positions[1]=(b2Vec2){-1,0};
    reference.geometry.positions[2]=(b2Vec2){0,1}; reference.geometry.positions[3]=(b2Vec2){0,-1};
    for (int i=0;i<2;++i)
    {
        dango_material_contact_row* row=&reference.rows[ids[i]]; row->count=2; row->ids[0]=2*i; row->ids[1]=2*i+1;
        row->gradients[0]=i?dango_mc_v(0,1):dango_mc_v(1,0); row->gradients[1]=dango_mc_mul(-1,row->gradients[0]);
    }
    double momentum[2]={0},angular=0;
    for (int i=0;i<2;++i) for (int j=0;j<2;++j)
    {
        int body=reference.rows[ids[i]].ids[j]; dango_material_contact_vec2 impulse=reference.rows[ids[i]].gradients[j];
        momentum[0]+=impulse.x; momentum[1]+=impulse.y;
        angular+=reference.geometry.positions[body].x*impulse.y-reference.geometry.positions[body].y*impulse.x;
    }
    NR_CHECK(momentum[0]==0 && momentum[1]==0 && angular==0);
    NR_CHECK(!dango_mcn_reaction_null(&reference,2,ids,&r));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_REACTION && r.reaction_checks_available && r.reaction_residual[0]==1);
    NR_CHECK(r.reaction_residual[0]>r.reaction_allowance[0] && !r.used); ++bc_negatives;
    // A near-dependent Gram matrix meets the unchanged rank threshold and
    // original-M backward error, while its physical direction changes velocity.
    const double near_matrix[16][16]={{1,1},{1,1+1e-14}},rhs[16]={0},scaling[16]={1,1},fixed_values[16]={0};
    const bool fixed[16]={false}; double z[16]={0};
    NR_CHECK(dango_mcn_factor(2,ids,near_matrix,rhs,z,&r) && r.rank==1);
    NR_CHECK(dango_mcn_effective_pair(2,ids,scaling,fixed,fixed_values,near_matrix,rhs,&r));
    NR_CHECK(r.effective_lambda_direction[0]==1 && r.effective_lambda_direction[1]<0 &&
        fmax(fabs(r.effective_lambda_direction[0]),fabs(r.effective_lambda_direction[1]))==1);
    memset(&reference,0,sizeof reference); reference.geometry.inverse_mass[0]=1;
    for (int i=0;i<2;++i) { reference.rows[ids[i]].count=1; reference.rows[ids[i]].ids[0]=0; }
    reference.rows[22].gradients[0]=dango_mc_v(1,0); reference.rows[55].gradients[0]=dango_mc_v(1,1e-7);
    NR_CHECK(!dango_mcn_reaction_null(&reference,2,ids,&r));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_REACTION && r.reaction_residual[0]>r.reaction_allowance[0]); ++bc_negatives;
    return 0;
}

static int nr_fixed_identity(void)
{
    const int ids[4]={22,55,144,25}; const double matrix[16][16]={{1,-1},{-1,1},{0,0,1},{0,0,0,1}},rhs[16]={-1,1,-.75,0};
    const double scaling[16]={2,1,4,8},fixed_values[16]={0,0,-3,0}; const bool fixed[16]={false,false,true,true};
    dango_material_contact_null_report r; double z[16]={0};
    NR_CHECK(dango_mcn_factor(4,ids,matrix,rhs,z,&r));
    NR_CHECK(dango_mcn_effective_pair(4,ids,scaling,fixed,fixed_values,matrix,rhs,&r));
    NR_CHECK(r.effective_lambda_particular[0]==-2 && r.effective_lambda_particular[2]==-3 && r.effective_lambda_particular[3]==0);
    NR_CHECK(r.effective_lambda_direction[0]==1 && r.effective_lambda_direction[1]==.5 && r.effective_lambda_direction[2]==0 && r.effective_lambda_direction[3]==0);
    NR_CHECK(r.direction_normalization==2);
    dango_material_contact_solver reference={0}; reference.geometry.inverse_mass[0]=.25f;
    for (int i=0;i<2;++i) { reference.rows[ids[i]].count=1; reference.rows[ids[i]].ids[0]=0; }
    reference.rows[22].gradients[0]=dango_mc_v(-.5,0); reference.rows[55].gradients[0]=dango_mc_v(1,0);
    NR_CHECK(dango_mcn_reaction_null(&reference,4,ids,&r));
    NR_CHECK(dango_mcn_interval_init(&r)); NR_CHECK(dango_mcn_interval_add(&r,22,0,-1,2)); NR_CHECK(dango_mcn_interval_add(&r,55,0,-.5,0));
    NR_CHECK(dango_mcn_interval_select(&r) && r.selected_alpha==2);
    double lambda[16]={0}; NR_CHECK(dango_mcn_selected(4,scaling,matrix,rhs,&r,lambda,z));
    NR_CHECK(lambda[0]==0 && lambda[1]==1 && lambda[2]==-3 && lambda[3]==0 && z[2]==-.75 && z[3]==0); ++bc_positives;
    // Identity assignments must recheck BOTH original particular and direction.
    dango_material_contact_null_report wrong; NR_CHECK(dango_mcn_factor(4,ids,matrix,rhs,z,&wrong));
    const double bad_values[16]={0,0,-2,0};
    NR_CHECK(!dango_mcn_effective_pair(4,ids,scaling,fixed,bad_values,matrix,rhs,&wrong));
    NR_CHECK(wrong.status==DANGO_MATERIAL_CONTACT_NULL_INCONSISTENT && wrong.particular_residual[2]==.25 && !wrong.used); ++bc_negatives;
    NR_CHECK(dango_mcn_factor(4,ids,matrix,rhs,z,&wrong));
    const bool altered_direction[16]={true,false,true,true}; const double released_value[16]={-2,0,-3,0};
    NR_CHECK(!dango_mcn_effective_pair(4,ids,scaling,altered_direction,released_value,matrix,rhs,&wrong));
    NR_CHECK(wrong.status==DANGO_MATERIAL_CONTACT_NULL_INCONSISTENT && fabs(wrong.direction_residual[0])>wrong.direction_allowance[0]); ++bc_negatives;
    return 0;
}

static int nr_selected_failure(void)
{
    dango_material_contact_null_report r; dango_material_contact_solver reference;
    NR_CHECK(nr_pair_unit(&r,&reference)==0);
    const double matrix[16][16]={{1,-1},{-1,1}},bad_rhs[16]={-1,2},scaling[16]={1,1};
    NR_CHECK(dango_mcn_interval_init(&r)); NR_CHECK(dango_mcn_interval_add(&r,22,0,-1,1)); NR_CHECK(dango_mcn_interval_select(&r));
    double lambda[16],z[16],saved_l[16],saved_z[16];
    for (int i=0;i<16;++i) { lambda[i]=333+i; z[i]=777+i; }
    memcpy(saved_l,lambda,sizeof lambda); memcpy(saved_z,z,sizeof z);
    NR_CHECK(!dango_mcn_selected(2,scaling,matrix,bad_rhs,&r,lambda,z));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_SELECTED_RESIDUAL && r.selected_pair_available && !r.used);
    NR_CHECK(r.selected_residual[1]==-1 && memcmp(lambda,saved_l,sizeof lambda)==0 && memcmp(z,saved_z,sizeof z)==0); ++bc_negatives;
    dango_mcn_report_init(&r);
    NR_CHECK(!dango_mcn_selected(2,scaling,matrix,bad_rhs,&r,lambda,z));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT && !r.selected_pair_available && !r.used);
    NR_CHECK(memcmp(lambda,saved_l,sizeof lambda)==0 && memcmp(z,saved_z,sizeof z)==0); ++bc_negatives;
    return 0;
}

static int nr_stage_contract(void)
{
    const int ids[2]={22,55}; const double matrix[16][16]={{1,-1},{-1,1}},rhs[16]={-1,1},scaling[16]={1,1};
    dango_material_contact_null_report r; dango_material_contact_solver reference;
    double lambda[16],z[16],saved_l[16],saved_z[16];
    for (int i=0;i<16;++i) { lambda[i]=111+i; z[i]=222+i; }
    memcpy(saved_l,lambda,sizeof lambda); memcpy(saved_z,z,sizeof z);
    // Availability denotes recorded diagnostics, not a proof. A manually set
    // report must not fabricate an accepted physical pair or final solution.
    dango_mcn_report_init(&r); r.dimension=2; r.effective_pair_available=true; r.selection_available=true;
    NR_CHECK(!dango_mcn_selected(2,scaling,matrix,rhs,&r,lambda,z));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT && !r.selected_pair_available && !r.used);
    NR_CHECK(memcmp(lambda,saved_l,sizeof lambda)==0 && memcmp(z,saved_z,sizeof z)==0); ++bc_negatives;
    // A failed pure-reaction certificate cannot be bypassed by invoking later
    // stages or by setting their availability fields after the failure.
    NR_CHECK(nr_pair_unit(&r,&reference)==0);
    reference.rows[55].gradients[0].x=0;
    NR_CHECK(!dango_mcn_reaction_null(&reference,2,ids,&r));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_REACTION && r.reaction_checks_available && !r.reaction_checks_passed);
    NR_CHECK(!dango_mcn_interval_init(&r));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_REACTION && !r.interval_available);
    r.selection_available=true;
    NR_CHECK(!dango_mcn_selected(2,scaling,matrix,rhs,&r,lambda,z));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_REACTION && !r.used && !r.selected_pair_available);
    NR_CHECK(memcmp(lambda,saved_l,sizeof lambda)==0 && memcmp(z,saved_z,sizeof z)==0); ++bc_negatives;
    // The exact a==0 contradiction records its failed interval and preserves
    // that earliest status; later selection/publication are not permitted.
    NR_CHECK(nr_pair_unit(&r,&reference)==0); NR_CHECK(dango_mcn_interval_init(&r));
    NR_CHECK(!dango_mcn_interval_add(&r,22,0,0,1));
    NR_CHECK(!dango_mcn_interval_select(&r));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY && !r.selection_available);
    NR_CHECK(!dango_mcn_selected(2,scaling,matrix,rhs,&r,lambda,z));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY && !r.selected_pair_available && !r.used);
    NR_CHECK(memcmp(lambda,saved_l,sizeof lambda)==0 && memcmp(z,saved_z,sizeof z)==0); ++bc_negatives;
    // Successful stage reentry invalidates every prior downstream certificate.
    NR_CHECK(nr_pair_unit(&r,&reference)==0); NR_CHECK(dango_mcn_interval_init(&r));
    NR_CHECK(dango_mcn_interval_add(&r,22,0,-1,1)); NR_CHECK(dango_mcn_interval_select(&r));
    NR_CHECK(dango_mcn_selected(2,scaling,matrix,rhs,&r,lambda,z) && r.used && r.selected_checks_passed);
    NR_CHECK(dango_mcn_reaction_null(&reference,2,ids,&r));
    NR_CHECK(r.reaction_checks_available && r.reaction_checks_passed && !r.interval_available && !r.interval_passed &&
        !r.selection_available && !r.selection_passed && !r.selected_pair_available && !r.selected_checks_passed && !r.used);
    NR_CHECK(!dango_mcn_selected(2,scaling,matrix,rhs,&r,lambda,z));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT && !r.used); ++bc_negatives;
    NR_CHECK(nr_pair_unit(&r,&reference)==0); r.mode_index=7;
    NR_CHECK(!dango_mcn_reaction_null(&reference,2,ids,&r));
    NR_CHECK(r.status==DANGO_MATERIAL_CONTACT_NULL_INVALID_INPUT && !r.reaction_checks_available && !r.reaction_checks_passed && !r.used); ++bc_negatives;
    return 0;
}

static int nr_total_cone_units(void)
{
    // Original builder supplies the immutable measured receipt slack. Only the
    // three Jacobian rows are synthetic mathematical inputs for this unit;
    // their resulting state is never presented as a geometric trajectory.
    dango_constraint_state s=bc_ring(); bc_ground(&s,12);
    dango_material_ground_receipt receipt={12,.125,(double)nextafterf(.25f,INFINITY),2,0};
    double measured_slack=receipt.tangent_impulse-.25;
    dango_material_contact_solver reference; dango_material_contact_report built;
    NR_CHECK(dango_material_contact_build(&s,&receipt,1,1.0/600,&reference,&built));
    NR_CHECK(reference.support[12].base_normal==.125 && reference.support[12].base_tangent==receipt.tangent_impulse &&
        reference.support[12].receipt_cone_slack==measured_slack && measured_slack>0 && measured_slack==0x1p-25);
    memset(reference.rows,0,sizeof reference.rows); memset(reference.input_velocities,0,sizeof reference.input_velocities);
    reference.input_velocities[12]=dango_mc_v(-.75,-.125);
    const int ids[3]={22,140,156}; const dango_material_contact_vec2 gradients[3]={{-1,-1},{0,1},{1,0}};
    dango_material_contact_block_report report; dango_mcb_report_init(&report); report.active_count=report.matrix_dimension=3;
    for (int i=0;i<3;++i)
    {
        reference.rows[ids[i]].id=ids[i]; reference.rows[ids[i]].active=true; reference.rows[ids[i]].count=1;
        reference.rows[ids[i]].ids[0]=12; reference.rows[ids[i]].gradients[0]=gradients[i];
        report.active_row_ids[i]=ids[i]; report.scaling[i]=1;
        report.modes[i]=i==2?DANGO_MATERIAL_CONTACT_BLOCK_STICK:DANGO_MATERIAL_CONTACT_BLOCK_ENABLED;
    }
    dango_material_contact_solver saved=reference;
    NR_CHECK(dango_mcb_system(&reference,&report));
    double lambda[16]={0},z[16]={0}; const bool fixed[16]={false}; const double fixed_values[16]={0};
    NR_CHECK(dango_mcn_factor(3,ids,report.scaled_matrix,report.scaled_rhs,z,&report.null_reaction));
    NR_CHECK(dango_mcn_effective_pair(3,ids,report.scaling,fixed,fixed_values,report.scaled_matrix,report.scaled_rhs,&report.null_reaction));
    NR_CHECK(dango_mcn_reaction_null(&reference,3,ids,&report.null_reaction));
    NR_CHECK(report.null_reaction.effective_lambda_particular[0]==-.75 && report.null_reaction.effective_lambda_particular[1]==-.625 && report.null_reaction.effective_lambda_particular[2]==0);
    NR_CHECK(dango_mcb_null_interval(&reference,&report));
    NR_CHECK(dango_mcn_interval_select(&report.null_reaction));
    NR_CHECK(report.null_reaction.selected_alpha==1.25 && report.null_reaction.lower==1.25 && !report.null_reaction.upper_bounded);
    NR_CHECK(dango_mcn_selected(3,report.scaling,report.scaled_matrix,report.scaled_rhs,&report.null_reaction,lambda,z));
    NR_CHECK(lambda[0]==.5 && lambda[1]==.625 && lambda[2]==1.25);
    // TOTAL impulse, not new tangent alone, uses measured slack exactly once.
    double total=receipt.tangent_impulse+lambda[2],physical=2*(.125+lambda[1]),budget=physical+measured_slack;
    NR_CHECK(total==1.5+measured_slack && budget==1.5+measured_slack && total<=budget && -total<=budget);
    NR_CHECK(memcmp(&reference,&saved,sizeof saved)==0); ++bc_positives;
    // Both original slide equations keep N0,T0 and the measured receipt slack.
    for (int sign=-1;sign<=1;sign+=2)
    {
        report.modes[2]=sign>0?DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE:DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE;
        NR_CHECK(dango_mcb_system(&reference,&report));
        double expected_rhs=sign*(.25+measured_slack)-receipt.tangent_impulse;
        NR_CHECK(report.raw_matrix[2][1]==-sign*2 && report.raw_matrix[2][2]==1 && report.raw_rhs[2]==expected_rhs);
        dango_material_contact_block_report old=report;
        double old_z[16]={0},new_z[16]={0}; dango_material_contact_null_report nr;
        NR_CHECK(dango_mcb_linear_solve(3,ids,report.scaled_matrix,report.scaled_rhs,old_z,&old));
        NR_CHECK(dango_mcn_factor(3,ids,report.scaled_matrix,report.scaled_rhs,new_z,&nr));
        NR_CHECK(nr.nullity==0 && memcmp(old_z,new_z,sizeof old_z)==0);
        NR_CHECK(new_z[2]-sign*2*new_z[1]==expected_rhs); ++bc_positives;
    }
    report.modes[0]=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED; report.modes[2]=DANGO_MATERIAL_CONTACT_BLOCK_ZERO;
    NR_CHECK(dango_mcb_system(&reference,&report));
    NR_CHECK(report.raw_matrix[0][0]==1 && report.raw_rhs[0]==0 && report.raw_matrix[2][2]==1 && report.raw_rhs[2]==-receipt.tangent_impulse);
    // Zero-mode identity alone does not imply a zero current budget. Original
    // single-mode decision moves to STICK, then ZERO when B becomes exactly 0.
    dango_material_contact_solver contact=reference; memset(contact.velocities,0,sizeof contact.velocities);
    dango_material_contact_block_mode next=DANGO_MATERIAL_CONTACT_BLOCK_ENABLED;
    NR_CHECK(dango_mcb_next_mode(&contact,&reference,&report,&next)==2 && next==DANGO_MATERIAL_CONTACT_BLOCK_STICK);
    report.modes[0]=DANGO_MATERIAL_CONTACT_BLOCK_ENABLED; report.modes[2]=DANGO_MATERIAL_CONTACT_BLOCK_STICK;
    contact.support[12].base_normal=0; contact.support[12].base_tangent=0; contact.support[12].receipt_cone_slack=0;
    NR_CHECK(dango_mcb_next_mode(&contact,&contact,&report,&next)==2 && next==DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
    report.modes[2]=DANGO_MATERIAL_CONTACT_BLOCK_ZERO; contact.rows[140].multiplier=.25;
    NR_CHECK(dango_mcb_next_mode(&contact,&contact,&report,&next)==2 && next==DANGO_MATERIAL_CONTACT_BLOCK_STICK);
    contact.rows[140].multiplier=-.25;
    NR_CHECK(dango_mcb_next_mode(&contact,&contact,&report,&next)==1 && next==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED); ++bc_positives;
    return 0;
}

int main(void)
{
    if (null_reaction_retained_block_main() || nr_full_rank_exact() || nr_sign_repair() || nr_intervals() ||
        nr_algebra_rejections() || nr_nonreaction() || nr_fixed_identity() || nr_selected_failure() || nr_stage_contract() || nr_total_cone_units()) return 1;
    printf("PASS isolated null guards: %d assertions; %d positives; %d rejected negatives; real frame147 acceptance follows\n",bc_assertions,bc_positives,bc_negatives);
    if (nr_frame147()) return 1;
    printf("PASS null reaction: %d assertions; %d positives; %d rejected negatives (includes retained block regressions)\n",
        bc_assertions,bc_positives,bc_negatives);
    return 0;
}

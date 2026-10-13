// Independent fixed control-entry fixture and fail-closed transition guards.
// No Box2D world, parameter scan, DLL, or altered publication tolerance.
#ifndef DANGO_MATERIAL_CONTACT_NULL_REACTION_CANDIDATE
#define DANGO_MATERIAL_CONTACT_NULL_REACTION_CANDIDATE
#endif
#define main mt_retained_block_main
#include "dango_material_contact_block_tests.c"
#undef main
#include "dango_material_contact_frame601.h"

#define MT_CHECK(c) do { ++bc_assertions; if (!(c)) { fprintf(stderr,"FAIL mode transition %d: %s\n",__LINE__,#c); return 1; } } while (0)

static int mt_one_fixture_bits(const dango_constraint_state* state,const uint32_t recorded[93])
{
    dango_constraint_state s=*state;
    const float scalars[8]={s.radial_length,s.perimeter_length,s.radial_min,s.radial_max,
        s.perimeter_min,s.perimeter_max,s.radius,s.ground};
    int index=0;
    for(int i=0;i<8;++i)
    {
        uint32_t bits; memcpy(&bits,&scalars[i],sizeof bits);
        MT_CHECK(bits==recorded[index++]);
    }
    for(int n=0;n<17;++n)
    {
        const float values[5]={s.positions[n].x,s.positions[n].y,s.velocities[n].x,
            s.velocities[n].y,s.inverse_mass[n]};
        for(int i=0;i<5;++i)
        {
            uint32_t bits; memcpy(&bits,&values[i],sizeof bits);
            MT_CHECK(bits==recorded[index++]);
        }
    }
    MT_CHECK(index==93);
    return 0;
}

static int mt_fixture_bits(void)
{
    dango_constraint_state s=dango_material_contact_frame601_state();
    MT_CHECK(mt_one_fixture_bits(&s,dango_material_contact_frame601_float_bits)==0);
    s=dango_material_contact_frame2720_state();
    MT_CHECK(mt_one_fixture_bits(&s,dango_material_contact_frame2720_float_bits)==0);
    s=dango_material_contact_frame3322_state();
    MT_CHECK(mt_one_fixture_bits(&s,dango_material_contact_frame3322_float_bits)==0);
    s=dango_material_contact_frame1711_state();
    MT_CHECK(mt_one_fixture_bits(&s,dango_material_contact_frame1711_float_bits)==0);
    return 0;
}

#if defined(DANGO_MATERIAL_CONTACT_MODE_TRANSITION_CANDIDATE)
static int mt_first_mode(dango_material_contact_block* block,
    dango_material_contact_block_report* report)
{
    dango_constraint_state s=dango_material_contact_frame601_state();
    MT_CHECK(dango_material_contact_block_build(&s,dango_material_contact_frame601_receipts,4,
        dango_material_contact_frame601_step_seconds,block,report));
    const int expected[9]={22,24,55,86,88,139,140,155,156};
    MT_CHECK(report->active_count==9 && report->mode_history_count==0);
    for(int i=0;i<9;++i) MT_CHECK(report->active_row_ids[i]==expected[i]);
    MT_CHECK(dango_mcb_record_modes(report));
    MT_CHECK(dango_mcb_system(&block->contact,report));
    MT_CHECK(!dango_mcb_null_solve(&block->contact,report));
    const dango_material_contact_null_report* nr=&report->null_reaction;
    MT_CHECK(report->status==DANGO_MATERIAL_CONTACT_BLOCK_NULL_REACTION);
    MT_CHECK(nr->status==DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY && nr->rank==8 && nr->nullity==1);
    MT_CHECK(nr->factor_passed && nr->pair_checks_passed && nr->reaction_checks_passed);
    MT_CHECK(nr->effective_pair_available && nr->interval_available && !nr->interval_passed);
    MT_CHECK(!nr->used && !nr->selection_available && !nr->selected_pair_available);
    MT_CHECK(nr->effective_lambda_particular[0]<-1e-5 && fabs(nr->effective_lambda_direction[0])<1e-12);
    MT_CHECK(nr->empty_row_id==55 && nr->lower>1e10 && nr->upper==0);
    MT_CHECK(!report->linear_solution_available && !report->row_rates_available);
    MT_CHECK(report->full_checks==0 && report->float_checks==0);
    return 0;
}
#endif

static int mt_stage_contract(const dango_material_contact_block_report* report)
{
    // Same availability/index contract as the retained nr helper, without its
    // old one-full-check-per-factorization assumption for rejected probes.
    const dango_material_contact_null_report* nr=&report->null_reaction;
    const bool available[8]={nr->factor_available,nr->raw_pair_available,nr->effective_pair_available,
        nr->pair_checks_available,nr->reaction_checks_available,nr->interval_available,
        nr->selection_available,nr->selected_pair_available};
    const int indices[8]={nr->factor_mode_index,nr->raw_pair_mode_index,nr->effective_pair_mode_index,
        nr->pair_checks_mode_index,nr->reaction_mode_index,nr->interval_mode_index,
        nr->selection_mode_index,nr->selected_mode_index};
    MT_CHECK(nr->mode_index==report->matrix_mode_index);
    for(int i=0;i<8;++i) MT_CHECK(indices[i]==(available[i]?nr->mode_index:-1));
    MT_CHECK(!nr->used || (nr->selected_pair_available && nr->selected_checks_passed));
    return 0;
}

static int mt_frame601(bool legacy)
{
    dango_constraint_state s=dango_material_contact_frame601_state(),saved=s,output,sentinel;
    memset(&output,0x5a,sizeof output); memcpy(&sentinel,&output,sizeof sentinel);
    dango_material_contact_block block;
    dango_material_contact_block_report report;
    MT_CHECK(dango_material_contact_block_build(&s,dango_material_contact_frame601_receipts,4,
        dango_material_contact_frame601_step_seconds,&block,&report));
    bool solved=dango_material_contact_block_solve(&block,&output,&report);
    MT_CHECK(memcmp(&s,&saved,sizeof s)==0);
    if(legacy)
    {
        MT_CHECK(!solved && report.status==DANGO_MATERIAL_CONTACT_BLOCK_NULL_REACTION);
        MT_CHECK(report.null_reaction.status==DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY);
        MT_CHECK(report.linear_solves==1 && report.mode_history_count==1);
        MT_CHECK(report.full_checks==0 && report.float_checks==0);
        MT_CHECK(!report.final_rows.ledger_evaluated && !report.final_rows.float_output_checked);
        MT_CHECK(!report.null_reaction.selection_available && !report.null_reaction.used);
        MT_CHECK(memcmp(&output,&sentinel,sizeof output)==0); ++bc_negatives;
        printf("PASS legacy frame601 rejection: empty interval; output untouched\n");
        return 0;
    }
    MT_CHECK(solved && report.status==DANGO_MATERIAL_CONTACT_BLOCK_OK);
    MT_CHECK(report.capacity==16 && report.solve_limit==32);
    MT_CHECK(report.linear_solves>=2 && report.linear_solves<=32);
    MT_CHECK(report.mode_history_count==report.linear_solves);
    MT_CHECK(report.mode_history[0][0]==DANGO_MATERIAL_CONTACT_BLOCK_ENABLED);
    MT_CHECK(report.mode_history[1][0]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
    MT_CHECK(report.full_checks>=1 && report.full_checks<report.linear_solves && report.float_checks==1);
    MT_CHECK(report.final_rows.ledger_evaluated && report.final_rows.float_output_checked);
    MT_CHECK(report.matrix_available && report.linear_solution_available && report.row_rates_available);
    MT_CHECK(report.rates_mode_index==report.mode_history_count-1);
    MT_CHECK(mt_stage_contract(&report)==0);
    MT_CHECK(bc_independent_final(&block,&output,false)==0);
    MT_CHECK(bc_independent_final(&block,&output,true)==0);
    dango_material_contact_report checked;
    MT_CHECK(dango_material_contact_validate(&block.contact,&output,&checked));
    MT_CHECK(checked.ledger_evaluated && checked.float_output_checked);
    MT_CHECK(bc_cap(&block,&output,12)==0);
    // Invalid public output still faces the original validator after repair.
    dango_constraint_state corrupt=output; corrupt.velocities[11].y=-1;
    MT_CHECK(!dango_material_contact_validate(&block.contact,&corrupt,&checked)); ++bc_negatives;
    ++bc_positives;
    printf("PASS frame601: modes=%d full=%d float=%d; independent double/float/energy/momentum/angular/velocity ledger/cap\n",
        report.mode_history_count,report.full_checks,report.float_checks);
    return 0;
}

#if defined(DANGO_MATERIAL_CONTACT_MODE_TRANSITION_CANDIDATE)
static int mt_transition_guards(void)
{
    dango_material_contact_block block;
    dango_material_contact_block_report initial;
    MT_CHECK(mt_first_mode(&block,&initial)==0);
    dango_material_contact_solver reference=block.contact,saved_reference=reference;
    dango_material_contact_block_report report=initial,expected=initial;
    MT_CHECK(dango_mcb_null_next_mode(&reference,&report));
    expected.modes[0]=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED;
    MT_CHECK(memcmp(&report,&expected,sizeof report)==0);
    MT_CHECK(memcmp(&reference,&saved_reference,sizeof reference)==0);
    MT_CHECK(!report.null_reaction.used && !report.null_reaction.selection_available);
    ++bc_positives;
    // Explicit synthetic diagnostic mutations exercise helper trust boundaries;
    // none are represented as captured or native-world trajectories.
    for(int mutation=0;mutation<25;++mutation)
    {
        report=initial;
        switch(mutation)
        {
            case 0: report.null_reaction.status=DANGO_MATERIAL_CONTACT_NULL_NONFINITE; break;
            case 1: report.null_reaction.status=DANGO_MATERIAL_CONTACT_NULL_INCONSISTENT; break;
            case 2: report.null_reaction.factor_passed=false; break;
            case 3: report.null_reaction.pair_checks_passed=false; break;
            case 4: report.null_reaction.reaction_checks_passed=false; break;
            case 5: report.null_reaction.effective_pair_available=false; break;
            case 6: report.null_reaction.interval_available=false; break;
            case 7: report.null_reaction.interval_passed=true; break;
            case 8: report.null_reaction.used=true; break;
            case 9: report.null_reaction.selection_available=true; break;
            case 10: report.null_reaction.nullity=2; break;
            case 11: report.null_reaction.mode_index+=1; break;
            case 12: report.null_reaction.original_row_ids[0]=24; break;
            case 13: report.null_reaction.effective_lambda_particular[0]=NAN; break;
            case 14: report.null_reaction.factor_available=false; break;
            case 15: report.null_reaction.raw_pair_available=false; break;
            case 16: report.null_reaction.pair_checks_available=false; break;
            case 17: report.null_reaction.reaction_checks_available=false; break;
            case 18: report.null_reaction.factor_mode_index+=1; break;
            case 19: report.null_reaction.raw_pair_mode_index+=1; break;
            case 20: report.null_reaction.effective_pair_mode_index+=1; break;
            case 21: report.null_reaction.pair_checks_mode_index+=1; break;
            case 22: report.null_reaction.reaction_mode_index+=1; break;
            case 23: report.null_reaction.selected_pair_available=true; break;
            default: report.null_reaction.status=DANGO_MATERIAL_CONTACT_NULL_REACTION; break;
        }
        expected=report;
        MT_CHECK(!dango_mcb_null_next_mode(&reference,&report));
        MT_CHECK(memcmp(&report,&expected,sizeof report)==0);
        MT_CHECK(memcmp(&reference,&saved_reference,sizeof reference)==0); ++bc_negatives;
    }
    // A synthetic EMPTY diagnostic with no next-mode violation must remain
    // rejected. The helper cannot invent a release or turn a probe into output.
    report=initial;
    dango_material_contact_solver no_next=reference;
    memset(no_next.velocities,0,sizeof no_next.velocities);
    memset(no_next.input_velocities,0,sizeof no_next.input_velocities);
    memset(report.null_reaction.effective_lambda_particular,0,sizeof report.null_reaction.effective_lambda_particular);
    expected=report;
    MT_CHECK(!dango_mcb_null_next_mode(&no_next,&report));
    MT_CHECK(memcmp(&report,&expected,sizeof report)==0); ++bc_negatives;
    // Repeated-mode and 32-mode budget guards remain the original hard gates.
    report=initial; MT_CHECK(dango_mcb_null_next_mode(&reference,&report));
    MT_CHECK(dango_mcb_record_modes(&report));
    MT_CHECK(!dango_mcb_record_modes(&report) && report.status==DANGO_MATERIAL_CONTACT_BLOCK_REPEATED_MODE);
    ++bc_negatives;
    report=initial; report.mode_history_count=32;
    for(int i=0;i<32;++i) for(int n=0;n<report.active_count;++n)
        report.mode_history[i][n]=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED;
    MT_CHECK(!dango_mcb_record_modes(&report) && report.status==DANGO_MATERIAL_CONTACT_BLOCK_BUDGET);
    ++bc_negatives;
    printf("PASS transition guards: certificates, no-next-mode, immutability, repeats, fixed budget\n");
    return 0;
}
#endif

#if defined(DANGO_MATERIAL_CONTACT_MODE_TRANSITION_CANDIDATE)
static int mt_frame2720(bool legacy)
{
    dango_constraint_state s=dango_material_contact_frame2720_state(),saved=s,output,sentinel;
    memset(&output,0x5a,sizeof output); memcpy(&sentinel,&output,sizeof sentinel);
    dango_material_contact_block block;
    dango_material_contact_block_report report;
    MT_CHECK(dango_material_contact_block_build(&s,dango_material_contact_frame2720_receipts,2,
        dango_material_contact_frame2720_step_seconds,&block,&report));
    bool solved=dango_material_contact_block_solve(&block,&output,&report);
    MT_CHECK(memcmp(&s,&saved,sizeof s)==0);
    if(legacy)
    {
        MT_CHECK(!solved && report.status==DANGO_MATERIAL_CONTACT_BLOCK_NULL_REACTION);
        MT_CHECK(report.null_reaction.status==DANGO_MATERIAL_CONTACT_NULL_INCONSISTENT);
        MT_CHECK(report.null_reaction.factor_passed && report.null_reaction.pair_checks_available);
        MT_CHECK(!report.null_reaction.pair_checks_passed && !report.null_reaction.used);
        MT_CHECK(report.linear_solves==2 && report.mode_history_count==2 && report.float_checks==0);
        MT_CHECK(!report.final_rows.ledger_evaluated && !report.final_rows.float_output_checked);
        MT_CHECK(!report.linear_solution_available && !report.null_reaction.selected_pair_available);
        MT_CHECK(memcmp(&output,&sentinel,sizeof output)==0); ++bc_negatives;
        printf("PASS pre-rank frame2720 rejection: inconsistent pair; output untouched\n");
        return 0;
    }
    MT_CHECK(solved && report.status==DANGO_MATERIAL_CONTACT_BLOCK_OK);
    MT_CHECK(report.capacity==16 && report.solve_limit==32 && report.active_count==6);
    MT_CHECK(report.linear_solves>=3 && report.linear_solves<=32 && report.mode_history_count==report.linear_solves);
#if !defined(DANGO_MATERIAL_CONTACT_FRICTION_MODE_PRIORITY_CANDIDATE)
    MT_CHECK(report.mode_history[1][2]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
    MT_CHECK(report.mode_history[2][0]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
#endif
    MT_CHECK(report.full_checks>=1 && report.full_checks<report.linear_solves && report.float_checks==1);
    MT_CHECK(report.final_rows.ledger_evaluated && report.final_rows.float_output_checked);
    MT_CHECK(report.linear_solution_available && report.row_rates_available);
    MT_CHECK(mt_stage_contract(&report)==0);
    MT_CHECK(bc_independent_final(&block,&output,false)==0);
    MT_CHECK(bc_independent_final(&block,&output,true)==0);
    dango_material_contact_report checked;
    MT_CHECK(dango_material_contact_validate(&block.contact,&output,&checked));
    MT_CHECK(bc_cap(&block,&output,12)==0); ++bc_positives;
    printf("PASS frame2720: modes=%d full=%d float=%d; retained physical rows and complete independent final gates\n",
        report.mode_history_count,report.full_checks,report.float_checks);
    return 0;
}
#endif

#if defined(DANGO_MATERIAL_CONTACT_RANK_MODE_CANDIDATE)
static int mt_rank_guards(void)
{
    dango_constraint_state s=dango_material_contact_frame2720_state();
    dango_material_contact_block block;
    dango_material_contact_block_report initial;
    MT_CHECK(dango_material_contact_block_build(&s,dango_material_contact_frame2720_receipts,2,
        dango_material_contact_frame2720_step_seconds,&block,&initial));
    const int expected_ids[6]={49,51,136,137,152,153};
    MT_CHECK(initial.active_count==6);
    for(int i=0;i<6;++i) MT_CHECK(initial.active_row_ids[i]==expected_ids[i]);
    // Replay only the captured fixed mathematical mode vector; no world state,
    // velocity, geometry, or native cache is fabricated by this helper fixture.
    MT_CHECK(dango_mcb_record_modes(&initial));
    initial.modes[2]=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED;
    MT_CHECK(dango_mcb_record_modes(&initial));
    MT_CHECK(dango_mcb_system(&block.contact,&initial));
    MT_CHECK(!dango_mcb_null_solve(&block.contact,&initial));
    MT_CHECK(initial.null_reaction.status==DANGO_MATERIAL_CONTACT_NULL_INCONSISTENT);
    MT_CHECK(initial.null_reaction.rank==5 && initial.null_reaction.nullity==1);
    MT_CHECK(initial.null_reaction.factor_passed && initial.null_reaction.pair_checks_available);
    MT_CHECK(!initial.null_reaction.pair_checks_passed && !initial.null_reaction.reaction_checks_available);
    MT_CHECK(initial.null_reaction.column_permutation[5]==153);
    dango_material_contact_solver reference=block.contact,saved_reference=reference;
    dango_material_contact_block_report report=initial,expected=initial;
    MT_CHECK(dango_mcb_rank_next_mode(&reference,&report));
    expected.modes[0]=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED;
    MT_CHECK(memcmp(&report,&expected,sizeof report)==0);
    MT_CHECK(memcmp(&reference,&saved_reference,sizeof reference)==0);
    MT_CHECK(!report.null_reaction.used && !report.linear_solution_available && !report.row_rates_available);
    MT_CHECK(!report.null_reaction.pair_checks_passed && !report.null_reaction.selected_pair_available);
    ++bc_positives;
    for(int mutation=0;mutation<13;++mutation)
    {
        report=initial;
        switch(mutation)
        {
            case 0: report.null_reaction.status=DANGO_MATERIAL_CONTACT_NULL_INTERVAL_EMPTY; break;
            case 1: report.null_reaction.status=DANGO_MATERIAL_CONTACT_NULL_NONFINITE; break;
            case 2: report.null_reaction.factor_available=false; break;
            case 3: report.matrix_available=false; break;
            case 4: report.null_reaction.rank=report.null_reaction.dimension; break;
            case 5: report.null_reaction.rank=-1; break;
            case 6: report.null_reaction.dimension+=1; break;
            case 7: report.null_reaction.nullity+=1; break;
            case 8: report.null_reaction.factor_mode_index+=1; break;
            case 9: report.null_reaction.mode_index+=1; break;
            case 10: report.null_reaction.used=true; break;
            case 11: report.null_reaction.selection_available=true; break;
            default: report.null_reaction.selected_pair_available=true; break;
        }
        expected=report;
        MT_CHECK(!dango_mcb_rank_next_mode(&reference,&report));
        MT_CHECK(memcmp(&report,&expected,sizeof report)==0);
        MT_CHECK(memcmp(&reference,&saved_reference,sizeof reference)==0); ++bc_negatives;
    }
    report=initial;
    report.modes[0]=report.modes[1]=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED;
    expected=report;
    MT_CHECK(!dango_mcb_rank_next_mode(&reference,&report));
    MT_CHECK(memcmp(&report,&expected,sizeof report)==0); ++bc_negatives;
    // Synthetic NULLITY branch: a fresh rejected factor can identify a bounded
    // mode trial, but creates neither a velocity nor a passing certificate.
    report=initial; report.null_reaction.status=DANGO_MATERIAL_CONTACT_NULL_NULLITY;
    report.null_reaction.rank=4; report.null_reaction.nullity=2;
    report.null_reaction.factor_passed=false;
    MT_CHECK(dango_mcb_rank_next_mode(&reference,&report));
    MT_CHECK(report.modes[0]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
    MT_CHECK(!report.null_reaction.factor_passed && !report.null_reaction.pair_checks_passed);
    MT_CHECK(!report.linear_solution_available && !report.row_rates_available && !report.null_reaction.used);
    MT_CHECK(memcmp(&reference,&saved_reference,sizeof reference)==0); ++bc_positives;
    // Separate synthetic REACTION branch: this policy retries a shape mode
    // without accepting the failed reaction certificate or using its vectors.
    report=initial; report.null_reaction.status=DANGO_MATERIAL_CONTACT_NULL_REACTION;
    report.null_reaction.pair_checks_passed=true;
    report.null_reaction.reaction_checks_available=true;
    report.null_reaction.reaction_checks_passed=false;
    expected=report; expected.modes[0]=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED;
    MT_CHECK(dango_mcb_rank_next_mode(&reference,&report));
    MT_CHECK(memcmp(&report,&expected,sizeof report)==0);
    MT_CHECK(!report.null_reaction.reaction_checks_passed && !report.null_reaction.selected_pair_available);
    MT_CHECK(!report.linear_solution_available && !report.row_rates_available && !report.null_reaction.used);
    MT_CHECK(memcmp(&reference,&saved_reference,sizeof reference)==0); ++bc_positives;
    printf("PASS rank mode guards: failed certificates never become velocities/output; stale/invalid factors/no shape rejected\n");
    return 0;
}
#endif

#if defined(DANGO_MATERIAL_CONTACT_RANK_MODE_CANDIDATE)
static int mt_frame3322(bool legacy)
{
    dango_constraint_state s=dango_material_contact_frame3322_state(),saved=s,output,sentinel;
    dango_material_ground_receipt receipts[3],saved_receipts[3];
    memcpy(receipts,dango_material_contact_frame3322_receipts,sizeof receipts);
    memcpy(saved_receipts,receipts,sizeof receipts);
    memset(&output,0x5a,sizeof output); memcpy(&sentinel,&output,sizeof sentinel);
    dango_material_contact_block block;
    dango_material_contact_block_report report;
    MT_CHECK(dango_material_contact_block_build(&s,receipts,3,
        dango_material_contact_frame3322_step_seconds,&block,&report));
    const int expected_ids[11]={2,27,33,35,37,39,63,128,143,144,159};
    MT_CHECK(report.capacity==16 && report.solve_limit==32 && report.active_count==11);
    MT_CHECK(report.mode_history_count==0 && report.linear_solves==0);
    for(int i=0;i<11;++i)
    {
        MT_CHECK(report.active_row_ids[i]==expected_ids[i]);
        MT_CHECK(report.modes[i]==(i<9?DANGO_MATERIAL_CONTACT_BLOCK_ENABLED:DANGO_MATERIAL_CONTACT_BLOCK_STICK));
    }
    dango_material_contact_solver reference=block.contact;
    // Solve directly from the natural initial modes. The independently found
    // legal vector is deliberately not injected into the candidate.
    bool solved=dango_material_contact_block_solve(&block,&output,&report);
    MT_CHECK(memcmp(&s,&saved,sizeof s)==0);
    MT_CHECK(memcmp(receipts,saved_receipts,sizeof receipts)==0);
    MT_CHECK(memcmp(&block.contact.geometry,&reference.geometry,sizeof reference.geometry)==0);
    MT_CHECK(memcmp(block.contact.input_velocities,reference.input_velocities,sizeof reference.input_velocities)==0);
    MT_CHECK(memcmp(block.contact.receipts,reference.receipts,sizeof reference.receipts)==0);
    MT_CHECK(memcmp(block.contact.support,reference.support,sizeof reference.support)==0);
    MT_CHECK(block.contact.receipt_count==reference.receipt_count && block.contact.step_seconds==reference.step_seconds);
    for(int i=0;i<11;++i) MT_CHECK(report.active_row_ids[i]==expected_ids[i]);
    if(legacy)
    {
        const dango_material_contact_block_mode history[3][11]={
            {0,0,0,0,0,0,0,0,0,2,2},
            {0,1,0,0,0,0,0,0,0,2,2},
            {0,1,0,0,0,0,0,1,0,2,2}
        };
        MT_CHECK(!solved && report.status==DANGO_MATERIAL_CONTACT_BLOCK_REPEATED_MODE);
        MT_CHECK(report.linear_solves==3 && report.mode_history_count==3);
        MT_CHECK(report.full_checks==1 && report.float_checks==0);
        MT_CHECK(report.matrix_available && report.linear_solution_available && report.row_rates_available);
        MT_CHECK(report.matrix_mode_index==2 && report.solution_mode_index==2 && report.rates_mode_index==2);
        for(int mode=0;mode<3;++mode) for(int i=0;i<11;++i)
            MT_CHECK(report.mode_history[mode][i]==history[mode][i]);
        for(int i=0;i<11;++i) MT_CHECK(report.modes[i]==history[1][i]);
        MT_CHECK(!report.final_rows.ledger_evaluated && !report.final_rows.float_output_checked);
        MT_CHECK(memcmp(&output,&sentinel,sizeof output)==0); ++bc_negatives;
        printf("PASS pre-priority frame3322 rejection: repeated mode; retained diagnostics; output untouched\n");
        return 0;
    }
    if(!solved)
        fprintf(stderr,"frame3322 natural priority failure: status=%d modes=%d full=%d float=%d\n",
            report.status,report.mode_history_count,report.full_checks,report.float_checks);
    MT_CHECK(solved && report.status==DANGO_MATERIAL_CONTACT_BLOCK_OK);
    MT_CHECK(report.active_count==11 && report.linear_solves>1 && report.linear_solves<=32);
    MT_CHECK(report.mode_history_count==report.linear_solves);
    MT_CHECK(report.full_checks>=1 && report.full_checks<=report.linear_solves && report.float_checks==1);
    MT_CHECK(report.final_rows.ledger_evaluated && report.final_rows.float_output_checked);
    MT_CHECK(report.matrix_available && report.linear_solution_available && report.row_rates_available);
    MT_CHECK(report.matrix_mode_index==report.mode_history_count-1);
    MT_CHECK(report.solution_mode_index==report.mode_history_count-1 && report.rates_mode_index==report.mode_history_count-1);
    MT_CHECK(mt_stage_contract(&report)==0);
    MT_CHECK(bc_independent_final(&block,&output,false)==0);
    MT_CHECK(bc_independent_final(&block,&output,true)==0);
    dango_material_contact_report checked;
    MT_CHECK(dango_material_contact_validate(&block.contact,&output,&checked));
    MT_CHECK(checked.ledger_evaluated && checked.float_output_checked);
    MT_CHECK(bc_cap(&block,&output,12)==0); ++bc_positives;
    printf("PASS frame3322: modes=%d full=%d float=%d; natural final modes=",
        report.mode_history_count,report.full_checks,report.float_checks);
    for(int i=0;i<11;++i) printf("%s%d",i?",":"",report.modes[i]);
    printf("; original double/float/energy/momentum/angular/velocity ledger/cap\n");
    return 0;
}
#endif

#if defined(DANGO_MATERIAL_CONTACT_RANK_MODE_CANDIDATE)
static int mt_frame1711(bool legacy)
{
    dango_constraint_state s=dango_material_contact_frame1711_state(),saved=s,output;
    dango_material_ground_receipt receipts[5],saved_receipts[5];
    memcpy(receipts,dango_material_contact_frame1711_receipts,sizeof receipts);
    memcpy(saved_receipts,receipts,sizeof receipts);
    memset(&output,0x5a,sizeof output);
    dango_material_contact_block block;
    dango_material_contact_block_report report;
    MT_CHECK(dango_material_contact_block_build(&s,receipts,5,
        dango_material_contact_frame1711_step_seconds,&block,&report));
    const int expected_ids[6]={70,72,74,106,132,148};
    MT_CHECK(report.capacity==16 && report.solve_limit==32 && report.active_count==6);
    MT_CHECK(report.mode_history_count==0 && report.linear_solves==0);
    for(int i=0;i<6;++i)
    {
        MT_CHECK(report.active_row_ids[i]==expected_ids[i]);
        MT_CHECK(report.modes[i]==(i<5?DANGO_MATERIAL_CONTACT_BLOCK_ENABLED:DANGO_MATERIAL_CONTACT_BLOCK_STICK));
    }
    dango_material_contact_solver reference=block.contact;
    // A zero additional ground reaction does not remove the receipt's positive
    // normal. This captured regression is not a zero-Coulomb-cone fixture.
    MT_CHECK(reference.rows[132].multiplier==0 && reference.support[4].base_normal>0);
    MT_CHECK(dango_mc_friction_bound(&reference,4)>0);
    // No known legal mode is seeded; all six original physical rows remain.
    bool solved=dango_material_contact_block_solve(&block,&output,&report);
    MT_CHECK(memcmp(&s,&saved,sizeof s)==0);
    MT_CHECK(memcmp(receipts,saved_receipts,sizeof receipts)==0);
    MT_CHECK(memcmp(&block.contact.geometry,&reference.geometry,sizeof reference.geometry)==0);
    MT_CHECK(memcmp(block.contact.input_velocities,reference.input_velocities,sizeof reference.input_velocities)==0);
    MT_CHECK(memcmp(block.contact.receipts,reference.receipts,sizeof reference.receipts)==0);
    MT_CHECK(memcmp(block.contact.support,reference.support,sizeof reference.support)==0);
    MT_CHECK(block.contact.receipt_count==reference.receipt_count && block.contact.step_seconds==reference.step_seconds);
    for(int i=0;i<6;++i) MT_CHECK(report.active_row_ids[i]==expected_ids[i]);
    if(!solved)
        fprintf(stderr,"frame1711 natural solver failure: status=%d modes=%d full=%d float=%d\n",
            report.status,report.mode_history_count,report.full_checks,report.float_checks);
    MT_CHECK(solved && report.status==DANGO_MATERIAL_CONTACT_BLOCK_OK);
    MT_CHECK(report.active_count==6 && report.linear_solves>1 && report.linear_solves<=32);
    // The no-priority control also has a legal solution. Only the new policy
    // promises to release the most negative scaled reaction (row 74) first.
    if(!legacy) MT_CHECK(report.mode_history[1][2]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
    MT_CHECK(report.mode_history_count==report.linear_solves);
    MT_CHECK(report.full_checks>=1 && report.full_checks<=report.linear_solves && report.float_checks==1);
    MT_CHECK(report.final_rows.ledger_evaluated && report.final_rows.float_output_checked);
    MT_CHECK(report.matrix_available && report.linear_solution_available && report.row_rates_available);
    MT_CHECK(report.matrix_mode_index==report.mode_history_count-1);
    MT_CHECK(report.solution_mode_index==report.mode_history_count-1 && report.rates_mode_index==report.mode_history_count-1);
    MT_CHECK(mt_stage_contract(&report)==0);
    MT_CHECK(bc_independent_final(&block,&output,false)==0);
    MT_CHECK(bc_independent_final(&block,&output,true)==0);
    dango_material_contact_report checked;
    MT_CHECK(dango_material_contact_validate(&block.contact,&output,&checked));
    MT_CHECK(checked.ledger_evaluated && checked.float_output_checked);
    MT_CHECK(bc_cap(&block,&output,12)==0); ++bc_positives;
    printf("PASS frame1711: policy=%s modes=%d full=%d float=%d; natural final modes=",
        legacy?"no-priority":"priority",report.mode_history_count,report.full_checks,report.float_checks);
    for(int i=0;i<6;++i) printf("%s%d",i?",":"",report.modes[i]);
    printf("; original double/float/energy/momentum/angular/velocity ledger/cap\n");
    return 0;
}
#endif

int main(int argc,char** argv)
{
    bool legacy=argc==2 && strcmp(argv[1],"--negative-legacy")==0;
    bool rank_legacy=argc==2 && strcmp(argv[1],"--negative-rank")==0;
    bool priority_legacy=argc==2 && strcmp(argv[1],"--negative-priority")==0;
    if(argc>2 || (argc==2 && !legacy && !rank_legacy && !priority_legacy)) return 2;
#if defined(DANGO_MATERIAL_CONTACT_MODE_TRANSITION_CANDIDATE)
    if(legacy) { fprintf(stderr,"legacy rejection requires build without transition macro\n"); return 2; }
#else
    if(!legacy) { fprintf(stderr,"positive requires transition macro\n"); return 2; }
#endif
#if defined(DANGO_MATERIAL_CONTACT_RANK_MODE_CANDIDATE)
    if(rank_legacy) { fprintf(stderr,"rank rejection requires build without rank macro\n"); return 2; }
#else
    if(priority_legacy) { fprintf(stderr,"priority rejection requires rank macro\n"); return 2; }
#endif
#if defined(DANGO_MATERIAL_CONTACT_FRICTION_MODE_PRIORITY_CANDIDATE)
    if(priority_legacy) { fprintf(stderr,"priority rejection requires build without priority macro\n"); return 2; }
#endif
    if(mt_fixture_bits() || mt_frame601(legacy)) return 1;
#if defined(DANGO_MATERIAL_CONTACT_MODE_TRANSITION_CANDIDATE)
    if(mt_transition_guards()) return 1;
    if(rank_legacy && mt_frame2720(true)) return 1;
#endif
#if defined(DANGO_MATERIAL_CONTACT_RANK_MODE_CANDIDATE)
    if(mt_frame2720(false) || mt_rank_guards()) return 1;
    if(priority_legacy && mt_frame3322(true)) return 1;
#if !defined(DANGO_MATERIAL_CONTACT_FRICTION_MODE_PRIORITY_CANDIDATE)
    if(mt_frame1711(true)) return 1;
#endif
#endif
#if defined(DANGO_MATERIAL_CONTACT_FRICTION_MODE_PRIORITY_CANDIDATE)
    if(mt_frame3322(false) || mt_frame1711(false)) return 1;
#endif
    printf("PASS mode transition suite: %d assertions; %d positives; %d rejected negatives\n",
        bc_assertions,bc_positives,bc_negatives);
    return 0;
}

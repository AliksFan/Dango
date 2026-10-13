// Exact fresh ZERO -> sliding activation with a negative paired normal cone.
// Natural initial modes only; no alternate normal/shape release seed.
static void ac_capture_evaluated_report(const void* report);
#define DANGO_IDLE_TRACE_MODE_EVENT(report,built,solved) do { if((built)&&(solved)) ac_capture_evaluated_report(report); } while(0)
#define DANGO_MATERIAL_ZERO_SLIDE_NO_MAIN
#include "dango_material_contact_zero_slide_tests.c"

#if !defined(DANGO_MATERIAL_CONTACT_ZERO_TO_SLIDE_CANDIDATE) || !defined(DANGO_MATERIAL_CONTACT_OUTCONE_FRESH_TRIAL_CANDIDATE) || !defined(DANGO_MATERIAL_CONTACT_FRESH_TRIAL_CANDIDATE)
#error Activation regression requires the earlier zero, outcone and fresh trials.
#endif

static dango_material_contact_block_report ac_evaluated;
static bool ac_captured;

static void ac_capture_evaluated_report(const void* value)
{
    const dango_material_contact_block_report* report=value;
    if(report->matrix_mode_index==2)
    {
        ac_evaluated=*report;
        ac_captured=true;
    }
}

static int ac_guard_replays(void)
{
    ZS_CHECK(ac_captured&&ac_evaluated.active_count==15&&ac_evaluated.mode_history_count==3);
    ZS_CHECK(ac_evaluated.null_reaction.rank==15&&ac_evaluated.null_reaction.nullity==0);
    dango_constraint_state input=dango_material_contact_soft1687_state();
    dango_material_contact_block built;dango_material_contact_block_report initial;
    ZS_CHECK(dango_material_contact_block_build(&input,dango_material_contact_soft1687_receipts,3,
        dango_material_contact_soft1687_step,&built,&initial));
    dango_material_contact_solver reference=built.contact,contact=reference;
    // The hook captures the third naturally evaluated factorization before
    // next_mode. Reconstruct its impulse publication with independent boundary
    // derivatives; the fixture, saved history and production inputs stay fixed.
    for(int i=0;i<ac_evaluated.active_count;++i)
    {
        int row=ac_evaluated.active_row_ids[i];
        double lambda=ac_evaluated.scaling[i]*ac_evaluated.linear_solution[i];
        if(ac_evaluated.modes[i]==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED) lambda=0;
        contact.rows[row].multiplier=lambda;
        for(int n=0;n<17;++n) for(int axis=0;axis<2;++axis)
        {
            double basis[17][2]={{0}},gap;basis[n][axis]=1;
            double impulse=lambda*input.inverse_mass[n]*ft_boundary_rate(&input,basis,row,&gap);
            if(axis==0) contact.velocities[n].x+=impulse;
            else contact.velocities[n].y+=impulse;
        }
    }
    ZS_CHECK(ac_evaluated.modes[14]==DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE);
    ZS_CHECK(ac_evaluated.mode_history[1][14]==DANGO_MATERIAL_CONTACT_BLOCK_ZERO);
    for(int i=0;i<14;++i) ZS_CHECK(ac_evaluated.mode_history[1][i]==ac_evaluated.modes[i]);
    ZS_CHECK(contact.rows[42].multiplier<0&&dango_mc_friction_bound(&contact,5)<0);
    ZS_CHECK(!dango_mcb_trial_seen(&ac_evaluated,14,DANGO_MATERIAL_CONTACT_BLOCK_STICK));
    dango_material_contact_solver saved_contact=contact,saved_reference=reference;
    dango_material_contact_block_report saved_report=ac_evaluated;
    dango_material_contact_block_mode next=DANGO_MATERIAL_CONTACT_BLOCK_ZERO;
    int change=dango_mcb_next_mode(&contact,&reference,&ac_evaluated,&next);
#ifdef DANGO_MATERIAL_CONTACT_CONE_ACTIVATION_STICK_CANDIDATE
    ZS_CHECK(change==14&&next==DANGO_MATERIAL_CONTACT_BLOCK_STICK);
#else
    ZS_CHECK(change==5&&next==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
#endif
    ZS_CHECK(memcmp(&contact,&saved_contact,sizeof contact)==0&&memcmp(&reference,&saved_reference,sizeof reference)==0);
    ZS_CHECK(memcmp(&ac_evaluated,&saved_report,sizeof ac_evaluated)==0);
    ++zs_positives;
    // Explicit synthetic diagnostic replays of that evaluated stage. These
    // local mutations are helper trust-boundary tests, not captured trajectories.
    const char* names[7]={"previous tangent not ZERO","previous other row differs",
        "whole stick vector already seen","cone zero","cone positive","mode not slide","insufficient history"};
    for(int mutation=0;mutation<7;++mutation)
    {
        dango_material_contact_solver trial=contact;
        dango_material_contact_block_report report=ac_evaluated;
        if(mutation==0) report.mode_history[1][14]=DANGO_MATERIAL_CONTACT_BLOCK_STICK;
        else if(mutation==1) report.mode_history[1][0]=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED;
        else if(mutation==2)
        {
            memcpy(report.mode_history[3],ac_evaluated.mode_history[2],sizeof report.mode_history[3]);
            memcpy(report.mode_history[2],ac_evaluated.mode_history[1],sizeof report.mode_history[2]);
            memcpy(report.mode_history[1],report.modes,sizeof report.mode_history[1]);
            report.mode_history[1][14]=DANGO_MATERIAL_CONTACT_BLOCK_STICK;
            report.mode_history_count=4;
            ZS_CHECK(dango_mcb_trial_seen(&report,14,DANGO_MATERIAL_CONTACT_BLOCK_STICK));
        }
        else if(mutation==3) trial.rows[133].multiplier=0;
        else if(mutation==4) trial.rows[133].multiplier=.0001;
        else if(mutation==5) report.modes[14]=DANGO_MATERIAL_CONTACT_BLOCK_STICK;
        else report.mode_history_count=1;
        dango_material_contact_solver trial_saved=trial;
        dango_material_contact_block_report report_saved=report;
        next=DANGO_MATERIAL_CONTACT_BLOCK_ZERO;
        change=dango_mcb_next_mode(&trial,&reference,&report,&next);
        // Original priority chooses shape row 42, the most negative scaled
        // compressive reaction. Every inactive-guard replay must retain that.
        ZS_CHECK(change==5&&report.active_row_ids[change]==42&&next==DANGO_MATERIAL_CONTACT_BLOCK_RELEASED);
        ZS_CHECK(memcmp(&trial,&trial_saved,sizeof trial)==0&&memcmp(&report,&report_saved,sizeof report)==0);
        ZS_CHECK(memcmp(&reference,&saved_reference,sizeof reference)==0);
        ++zs_negatives;printf("PASS inactive activation guard: %s; original row42 release unchanged\n",names[mutation]);
    }
    ZS_CHECK(memcmp(&ac_evaluated,&saved_report,sizeof ac_evaluated)==0);
    return 0;
}

int main(int argc,char** argv)
{
    bool negative=argc==2&&strcmp(argv[1],"--negative-activation")==0;
    if(argc>2||(argc==2&&!negative)) return 2;
#ifdef DANGO_MATERIAL_CONTACT_CONE_ACTIVATION_STICK_CANDIDATE
    if(negative) return 2;
#else
    if(!negative) return 2;
#endif
    dango_constraint_state s=dango_material_contact_soft1687_state();ZS_BITS(soft1687);
    const int ids[15]={13,33,35,37,39,42,55,57,59,61,63,132,133,148,149};
    const dango_material_contact_block_mode initial[15]={0,0,0,0,0,0,0,0,0,0,0,0,0,2,5};
    const dango_material_contact_block_mode expected[15]={0,0,0,0,0,0,0,0,0,0,0,1,0,2,2};
    ZS_RUN(soft1687,15,negative?7:4,negative);
    ZS_CHECK(ac_guard_replays()==0);
    printf("PASS activation suite: assertions=%d independent=%d positives=%d negatives=%d\n",zs_assertions,ft_assertions,zs_positives,zs_negatives);return 0;
}

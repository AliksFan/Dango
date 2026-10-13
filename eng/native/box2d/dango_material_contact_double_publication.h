#ifndef DANGO_MATERIAL_CONTACT_DOUBLE_PUBLICATION_H
#define DANGO_MATERIAL_CONTACT_DOUBLE_PUBLICATION_H

// Private numerical candidate: the fixed positive global limiter publishes
// directly from the certified double contact velocities, rounding only once.
// The original float-times-float publication/validator remains unchanged.
#include "dango_material_contact_block.h"

#ifdef DANGO_MATERIAL_CONTACT_SINGLE_ROUND_PUBLICATION_CANDIDATE
static inline bool dango_mdp_fail(dango_material_contact_cap_report* report,
    dango_material_contact_status status)
{ if(report) report->status=status; return false; }

static inline bool dango_material_contact_block_validate_after_double_scale(
    const dango_material_contact_block* block, const dango_constraint_state* contact_output,
    const dango_constraint_state* final_state, float scale, float maximum_speed,
    dango_material_contact_cap_report* report)
{
    if(!report) return false;
    memset(report,0,sizeof *report); report->scale=scale;
    if(!block || !contact_output || !final_state || !block->built || !block->attempted ||
        block->report.status!=DANGO_MATERIAL_CONTACT_BLOCK_OK ||
        block->report.active_count<0 || block->report.active_count>DANGO_MATERIAL_CONTACT_BLOCK_CAPACITY ||
        !isfinite(scale) || scale<=0 || scale>1 || !isfinite(maximum_speed) || maximum_speed<=0 ||
        !dango_mc_state_valid(contact_output) || !dango_mc_state_valid(final_state))
        return dango_mdp_fail(report,DANGO_MATERIAL_CONTACT_INVALID_INPUT);
    const dango_material_contact_solver* solver=&block->contact;
    if(!dango_mc_same_geometry(&solver->geometry,contact_output) ||
        !dango_mc_same_geometry(contact_output,final_state))
        return dango_mdp_fail(report,DANGO_MATERIAL_CONTACT_INVALID_GEOMETRY);
    dango_material_contact_solver reference;
    dango_material_contact_report built;
    if(!dango_material_contact_build(&solver->geometry,solver->receipts,solver->receipt_count,
        solver->step_seconds,&reference,&built)) return dango_mdp_fail(report,built.status);
    // Report membership is not an authority for omitting a physical row. Check
    // all active IDs and admissible mode types against freshly rebuilt geometry.
    int active=0;
    for(int id=0;id<DANGO_MATERIAL_CONTACT_ROWS;++id)
    {
        if(!reference.rows[id].active) continue;
        if(active>=block->report.active_count || block->report.active_row_ids[active]!=id)
            return dango_mdp_fail(report,DANGO_MATERIAL_CONTACT_INVALID_INPUT);
        dango_material_contact_block_mode mode=block->report.modes[active++];
        if((id<144 && mode!=DANGO_MATERIAL_CONTACT_BLOCK_ENABLED && mode!=DANGO_MATERIAL_CONTACT_BLOCK_RELEASED) ||
            (id>=144 && mode!=DANGO_MATERIAL_CONTACT_BLOCK_STICK && mode!=DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_POSITIVE &&
             mode!=DANGO_MATERIAL_CONTACT_BLOCK_SLIDE_NEGATIVE && mode!=DANGO_MATERIAL_CONTACT_BLOCK_ZERO))
            return dango_mdp_fail(report,DANGO_MATERIAL_CONTACT_INVALID_INPUT);
    }
    if(active!=block->report.active_count) return dango_mdp_fail(report,DANGO_MATERIAL_CONTACT_INVALID_INPUT);
    for(int n=0;n<17;++n)
    {
        if(!dango_mc_finite(solver->velocities[n]) ||
            solver->input_velocities[n].x!=reference.input_velocities[n].x ||
            solver->input_velocities[n].y!=reference.input_velocities[n].y)
            return dango_mdp_fail(report,DANGO_MATERIAL_CONTACT_INVALID_INPUT);
        if(contact_output->velocities[n].x!=(float)solver->velocities[n].x ||
            contact_output->velocities[n].y!=(float)solver->velocities[n].y)
            return dango_mdp_fail(report,DANGO_MATERIAL_CONTACT_SPEED_CAP);
    }
    // Never trust a stale success flag as a certificate for the double basis.
    dango_material_contact_report double_checked=built;
    if(!dango_mc_inspect_rows(solver,&reference,solver->velocities,&double_checked) ||
        !dango_mc_inspect_ledger(solver,&reference,solver->velocities,false,&double_checked))
    { report->final_rows=double_checked; return dango_mdp_fail(report,double_checked.status); }
    dango_material_contact_block_report mode_checked=block->report;
    if(!dango_mcb_modes_valid(solver,&reference,solver->velocities,&mode_checked))
    { report->final_rows=mode_checked.final_rows; return dango_mdp_fail(report,mode_checked.final_rows.status); }
    if(!dango_material_contact_validate(solver,contact_output,&report->final_rows))
        return dango_mdp_fail(report,report->final_rows.status);
    report->contact_output_checked=true;
    // The original canonical ratio is still defined by the unscaled contact
    // float output. No nextafter selection, ratio search, or altered safety factor.
    for(int n=0;n<17;++n)
    {
        float speed=b2Length(contact_output->velocities[n]);
        if(!isfinite(speed)) return dango_mdp_fail(report,DANGO_MATERIAL_CONTACT_INVALID_INPUT);
        report->maximum_before=fmaxf(report->maximum_before,speed);
    }
    float expected_scale=report->maximum_before>maximum_speed ?
        (maximum_speed*.99999f)/report->maximum_before : 1.0f;
    if(scale!=expected_scale) return dango_mdp_fail(report,DANGO_MATERIAL_CONTACT_SPEED_CAP);

    dango_material_contact_vec2 published[17];
    dango_material_contact_vec2 origin=dango_mc_position(&solver->geometry,16);
    dango_material_contact_vec2 momentum_before={0,0},momentum_after={0,0};
    double angular_before=0,angular_after=0,mass_sum=0,impulse_scale=0,torque_scale=0;
    for(int n=0;n<17;++n)
    {
        b2Vec2 expected={(float)(solver->velocities[n].x*(double)scale),
            (float)(solver->velocities[n].y*(double)scale)};
        if(final_state->velocities[n].x!=expected.x || final_state->velocities[n].y!=expected.y)
            return dango_mdp_fail(report,DANGO_MATERIAL_CONTACT_SPEED_CAP);
        float speed=b2Length(final_state->velocities[n]);
        if(!isfinite(speed)) return dango_mdp_fail(report,DANGO_MATERIAL_CONTACT_INVALID_INPUT);
        report->maximum_after=fmaxf(report->maximum_after,speed);
        double mass=1.0/final_state->inverse_mass[n]; mass_sum+=mass;
        dango_material_contact_vec2 before=dango_mc_v(contact_output->velocities[n].x,contact_output->velocities[n].y);
        published[n]=dango_mc_v(final_state->velocities[n].x,final_state->velocities[n].y);
        report->energy_before+=.5*mass*dango_mc_dot(before,before);
        report->energy_after+=.5*mass*dango_mc_dot(published[n],published[n]);
        dango_material_contact_vec2 change=dango_mc_mul(mass,dango_mc_sub(published[n],before));
        dango_material_contact_vec2 position=dango_mc_sub(dango_mc_position(final_state,n),origin);
        report->cap_momentum_change=dango_mc_add(report->cap_momentum_change,change);
        report->cap_angular_momentum_change+=dango_mc_cross(position,change);
        dango_material_contact_vec2 p0=dango_mc_mul(mass,before),p1=dango_mc_mul(mass,published[n]);
        momentum_before=dango_mc_add(momentum_before,p0); momentum_after=dango_mc_add(momentum_after,p1);
        angular_before+=dango_mc_cross(position,p0); angular_after+=dango_mc_cross(position,p1);
        double magnitude=dango_mc_length(p0)+dango_mc_length(p1)+dango_mc_length(change);
        impulse_scale+=magnitude; torque_scale+=dango_mc_length(position)*magnitude;
    }
    report->exact_common_scale_checked=true;
    if(report->maximum_after>maximum_speed) return dango_mdp_fail(report,DANGO_MATERIAL_CONTACT_SPEED_CAP);
    double length=solver->geometry.radial_length;
    double floor=mass_sum*length*length*DANGO_MATERIAL_CONTACT_RATE_TOLERANCE*DANGO_MATERIAL_CONTACT_RATE_TOLERANCE;
    report->energy_allowance=8*FLT_EPSILON*fmax(report->energy_before,floor);
    if(!isfinite(report->energy_before+report->energy_after) ||
        report->energy_after>report->energy_before+report->energy_allowance)
        return dango_mdp_fail(report,DANGO_MATERIAL_CONTACT_ENERGY);
    // Cap losses are accounted separately, never presented as contact impulse.
    // Reconcile independently summed before/after momenta with the per-node cap
    // changes using the existing double relative ledger rounding factor.
    double momentum_residual=dango_mc_length(dango_mc_sub(dango_mc_sub(momentum_after,momentum_before),report->cap_momentum_change));
    double angular_residual=fabs(angular_after-angular_before-report->cap_angular_momentum_change);
    double momentum_allowance=128*DBL_EPSILON*fmax(impulse_scale,mass_sum*length*DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
    double angular_allowance=128*DBL_EPSILON*fmax(torque_scale,mass_sum*length*length*DANGO_MATERIAL_CONTACT_RATE_TOLERANCE);
    if(!isfinite(momentum_residual+angular_residual) || momentum_residual>momentum_allowance || angular_residual>angular_allowance)
        return dango_mdp_fail(report,DANGO_MATERIAL_CONTACT_LEDGER);
    report->final_rows=built;
    report->final_rows.float_output_checked=true;
    report->final_rows.ledger_evaluated=false; // The original contact ledger belongs to the unscaled stage.
    if(!dango_mc_inspect_rows(solver,&reference,published,&report->final_rows))
        return dango_mdp_fail(report,report->final_rows.status);
    mode_checked=block->report;
    if(!dango_mcb_modes_valid(solver,&reference,published,&mode_checked))
    { report->final_rows.status=mode_checked.final_rows.status; return dango_mdp_fail(report,mode_checked.final_rows.status); }
    report->status=report->final_rows.status=DANGO_MATERIAL_CONTACT_OK;
    return true;
}
#endif
#endif

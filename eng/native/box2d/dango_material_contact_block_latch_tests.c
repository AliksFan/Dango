// Real private-world publication/latch/reset contract. Default, forward and
// mirror profiles independently captured this natural frame-147 rank failure.
// A contract pass is separate from successful candidate physics acceptance.
#ifndef DANGO_MATERIAL_CONTACT_CANDIDATE
#define DANGO_MATERIAL_CONTACT_CANDIDATE
#endif
#ifndef DANGO_MATERIAL_CONTACT_BLOCK_CANDIDATE
#define DANGO_MATERIAL_CONTACT_BLOCK_CANDIDATE
#endif
#define main block_latch_unused_idle_main
#include "dango_material_idle_diagnostics.c"
#undef main

typedef struct block_latch_raw
{
    b2BodyId ids[17];
    b2Vec2 positions[17], velocities[17];
} block_latch_raw;

static block_latch_raw block_latch_capture(collision_review* world)
{
    block_latch_raw raw;
    for (int n = 0; n < 17; ++n)
    {
        raw.ids[n] = n == 16 ? world->bodies[0]->core_body_id : world->bodies[0]->node_body_ids[n];
        raw.positions[n] = b2Body_GetPosition(raw.ids[n]);
        raw.velocities[n] = b2Body_GetLinearVelocity(raw.ids[n]);
    }
    return raw;
}

static bool block_latch_same_raw(const block_latch_raw* a, const block_latch_raw* b)
{
    for (int n = 0; n < 17; ++n)
        if (!B2_ID_EQUALS(a->ids[n],b->ids[n]) ||
            memcmp(&a->positions[n],&b->positions[n],sizeof(b2Vec2)) ||
            memcmp(&a->velocities[n],&b->velocities[n],sizeof(b2Vec2))) return false;
    return true;
}

static bool block_latch_same_motion(const block_latch_raw* a, const block_latch_raw* b)
{
    for (int n = 0; n < 17; ++n)
        if (memcmp(&a->positions[n],&b->positions[n],sizeof(b2Vec2)) ||
            memcmp(&a->velocities[n],&b->velocities[n],sizeof(b2Vec2))) return false;
    return true;
}

static bool block_latch_finite(const float* row)
{
    for (int n = 0; n < 42; ++n) if (!isfinite(row[n])) return false;
    return row[41] == 16 && row[34] > 0;
}

#define BLOCK_LATCH_CHECK(c) do { ++checks; if (!(c)) { fprintf(stderr,"FAIL block latch %d: %s\n",__LINE__,#c); result=1; goto cleanup; } } while (0)

int main(void)
{
    const int profile_limit = 147;
    const float dt = .0166666f;
    int result=0,checks=0,id=0,state_id=0,count=0,contacts=0,latched=0;
    int first_fault=0,first_error=0,completed=0;
    void* handle=NULL;
    float controls[6]={0},row[42]={0},initial[42],last_legal[42],fault_snapshot[42];
    idle_config=idle_defaults(true); idle_material_mode=7;
    BLOCK_LATCH_CHECK(dango_physics_world_create(&idle_config,1,&handle)==0 && handle!=NULL);
    BLOCK_LATCH_CHECK(dango_physics_world_add(handle,0,0,&id)==0 && id>0);
    collision_review* world=handle;
    memcpy(initial,world->snapshot,sizeof initial); memcpy(last_legal,initial,sizeof initial);
    BLOCK_LATCH_CHECK(block_latch_finite(initial));
    block_latch_raw initial_raw=block_latch_capture(world),before_fault=initial_raw;
    for (int frame=1;frame<=profile_limit;++frame)
    {
        before_fault=block_latch_capture(world);
        BLOCK_LATCH_CHECK(dango_physics_world_step(handle,dt,&id,controls,1,
            &state_id,row,42,&count,&contacts,&latched)==0);
        BLOCK_LATCH_CHECK(count==1 && state_id==id);
        if (latched)
        {
            first_fault=frame; first_error=latched;
            BLOCK_LATCH_CHECK(world->error==first_error);
            BLOCK_LATCH_CHECK(memcmp(row,last_legal,sizeof row)==0);
            BLOCK_LATCH_CHECK(memcmp(world->snapshot,last_legal,sizeof row)==0);
            break;
        }
        ++completed;
        BLOCK_LATCH_CHECK(world->error==0 && block_latch_finite(row));
        BLOCK_LATCH_CHECK(memcmp(row,world->snapshot,sizeof row)==0);
        memcpy(last_legal,row,sizeof row);
    }
    // A missing/earlier/different fault is a regression failure. Healthy frames
    // cannot silently skip the captured fault/latch/reset contract.
    BLOCK_LATCH_CHECK(first_fault==147 && completed==146 && first_error==-96);
    memcpy(fault_snapshot,last_legal,sizeof fault_snapshot);
    dango_material_contact_diagnostic diagnostic;
    BLOCK_LATCH_CHECK(dango_material_contact_get_diagnostic(handle,&diagnostic)==0);
    BLOCK_LATCH_CHECK(diagnostic.error==-96 && diagnostic.phase==4 && diagnostic.substeps==2345 && diagnostic.report_valid);
    BLOCK_LATCH_CHECK(diagnostic.block_report.status==DANGO_MATERIAL_CONTACT_BLOCK_RANK && diagnostic.block_report.active_count==9);
    const int active_rows[9]={22,24,55,86,88,139,140,155,156};
    for (int i=0;i<9;++i) BLOCK_LATCH_CHECK(diagnostic.block_report.active_row_ids[i]==active_rows[i]);
    BLOCK_LATCH_CHECK(diagnostic.block_report.linear_solves==1 && diagnostic.block_report.full_checks==0 && diagnostic.block_report.float_checks==0);
    BLOCK_LATCH_CHECK(diagnostic.block_report.mode_history_count==1);
    BLOCK_LATCH_CHECK(diagnostic.block_report.pivot_searches==285 && diagnostic.block_report.pivot_eliminations==8);
    BLOCK_LATCH_CHECK(diagnostic.block_report.matrix_available && diagnostic.block_report.matrix_mode_index==0);
    BLOCK_LATCH_CHECK(!diagnostic.block_report.linear_solution_available && diagnostic.block_report.solution_mode_index==-1);
    BLOCK_LATCH_CHECK(!diagnostic.block_report.row_rates_available && diagnostic.block_report.rates_mode_index==-1);
    BLOCK_LATCH_CHECK(!diagnostic.block_report.final_rows.ledger_evaluated && !diagnostic.block_report.final_rows.float_output_checked);
    BLOCK_LATCH_CHECK(!diagnostic.report.ledger_evaluated && !diagnostic.report.float_output_checked && !diagnostic.cap_report.contact_output_checked);
    block_latch_raw failed=block_latch_capture(world);
    for (int n=0;n<17;++n)
    {
        BLOCK_LATCH_CHECK(B2_ID_EQUALS(before_fault.ids[n],failed.ids[n]));
        // World integration/cache changes are not rolled back. No failed
        // scratch position/damping/block/cap state may have been written back.
        BLOCK_LATCH_CHECK(memcmp(&failed.positions[n],&diagnostic.input.positions[n],sizeof(b2Vec2))==0);
        BLOCK_LATCH_CHECK(memcmp(&failed.velocities[n],&diagnostic.input.velocities[n],sizeof(b2Vec2))==0);
    }
    const int held_contacts=contacts;
    const float active[3][6]={{1,0,0,0,0,0},{0,1,1,0,0,0},{0,0,0,1,2.4f,1.9f}};
    for (int trial=0;trial<3;++trial)
    {
        BLOCK_LATCH_CHECK(dango_physics_world_step(handle,dt,&id,active[trial],1,
            &state_id,row,42,&count,&contacts,&latched)==0);
        BLOCK_LATCH_CHECK(latched==first_error && world->error==first_error && count==1 && state_id==id && contacts==held_contacts);
        BLOCK_LATCH_CHECK(memcmp(row,fault_snapshot,sizeof row)==0 && memcmp(world->snapshot,fault_snapshot,sizeof row)==0);
        block_latch_raw held=block_latch_capture(world);
        BLOCK_LATCH_CHECK(block_latch_same_raw(&failed,&held));
        dango_material_contact_diagnostic held_diagnostic;
        BLOCK_LATCH_CHECK(dango_material_contact_get_diagnostic(handle,&held_diagnostic)==0);
        BLOCK_LATCH_CHECK(memcmp(&held_diagnostic,&diagnostic,sizeof diagnostic)==0);
    }
    BLOCK_LATCH_CHECK(dango_physics_world_reset(handle)==0);
    BLOCK_LATCH_CHECK(world->error==0 && world->contact_steps==0 && world->count==1 && world->entity_ids[0]==id);
    BLOCK_LATCH_CHECK(memcmp(world->snapshot,initial,sizeof initial)==0);
    block_latch_raw reset_raw=block_latch_capture(world);
    // The existing reset destroys and recreates the rig. Business entity ID
    // stays fixed; Box2D body generations must become new valid identities.
    BLOCK_LATCH_CHECK(block_latch_same_motion(&initial_raw,&reset_raw));
    for (int n=0;n<17;++n)
    {
        BLOCK_LATCH_CHECK(!b2Body_IsValid(initial_raw.ids[n]));
        BLOCK_LATCH_CHECK(b2Body_IsValid(reset_raw.ids[n]));
        BLOCK_LATCH_CHECK(!B2_ID_EQUALS(initial_raw.ids[n],reset_raw.ids[n]));
    }
    dango_material_contact_diagnostic reset_diagnostic,empty_diagnostic;
    memset(&empty_diagnostic,0,sizeof empty_diagnostic);
    BLOCK_LATCH_CHECK(dango_material_contact_get_diagnostic(handle,&reset_diagnostic)==0);
    BLOCK_LATCH_CHECK(memcmp(&reset_diagnostic,&empty_diagnostic,sizeof reset_diagnostic)==0);
    memcpy(last_legal,initial,sizeof initial);
    for (int frame=1;frame<=first_fault;++frame)
    {
        BLOCK_LATCH_CHECK(dango_physics_world_step(handle,dt,&id,controls,1,
            &state_id,row,42,&count,&contacts,&latched)==0);
        BLOCK_LATCH_CHECK(count==1 && state_id==id);
        if (frame<first_fault)
        {
            BLOCK_LATCH_CHECK(latched==0 && world->error==0 && block_latch_finite(row));
            BLOCK_LATCH_CHECK(memcmp(row,world->snapshot,sizeof row)==0);
            memcpy(last_legal,row,sizeof row);
        }
        else
        {
            BLOCK_LATCH_CHECK(latched==first_error && world->error==first_error);
            BLOCK_LATCH_CHECK(memcmp(row,last_legal,sizeof row)==0 && memcmp(row,fault_snapshot,sizeof row)==0);
            BLOCK_LATCH_CHECK(memcmp(world->snapshot,fault_snapshot,sizeof row)==0);
        }
    }
    dango_material_contact_diagnostic repeated;
    BLOCK_LATCH_CHECK(dango_material_contact_get_diagnostic(handle,&repeated)==0);
    BLOCK_LATCH_CHECK(repeated.phase==diagnostic.phase && repeated.error==diagnostic.error &&
        repeated.substeps==diagnostic.substeps && repeated.position_iterations==diagnostic.position_iterations);
    BLOCK_LATCH_CHECK(repeated.block_report.status==diagnostic.block_report.status &&
        repeated.block_report.linear_solves==diagnostic.block_report.linear_solves &&
        repeated.block_report.full_checks==diagnostic.block_report.full_checks &&
        repeated.block_report.float_checks==diagnostic.block_report.float_checks &&
        repeated.block_report.active_count==diagnostic.block_report.active_count);
    BLOCK_LATCH_CHECK(repeated.block_report.pivot_searches==285 && repeated.block_report.pivot_eliminations==8 &&
        repeated.block_report.matrix_available && repeated.block_report.matrix_mode_index==0 &&
        !repeated.block_report.linear_solution_available && repeated.block_report.solution_mode_index==-1 &&
        !repeated.block_report.row_rates_available && repeated.block_report.rates_mode_index==-1);
    BLOCK_LATCH_CHECK(memcmp(repeated.block_report.active_row_ids,diagnostic.block_report.active_row_ids,sizeof diagnostic.block_report.active_row_ids)==0);
    BLOCK_LATCH_CHECK(memcmp(repeated.block_report.raw_matrix,diagnostic.block_report.raw_matrix,sizeof diagnostic.block_report.raw_matrix)==0);
    BLOCK_LATCH_CHECK(memcmp(repeated.block_report.scaled_matrix,diagnostic.block_report.scaled_matrix,sizeof diagnostic.block_report.scaled_matrix)==0);
    BLOCK_LATCH_CHECK(memcmp(repeated.block_report.raw_rhs,diagnostic.block_report.raw_rhs,sizeof diagnostic.block_report.raw_rhs)==0);
    BLOCK_LATCH_CHECK(memcmp(repeated.block_report.scaled_rhs,diagnostic.block_report.scaled_rhs,sizeof diagnostic.block_report.scaled_rhs)==0);
    block_latch_raw repeated_raw=block_latch_capture(world);
    BLOCK_LATCH_CHECK(block_latch_same_motion(&failed,&repeated_raw));
    for (int n=0;n<17;++n)
        BLOCK_LATCH_CHECK(B2_ID_EQUALS(reset_raw.ids[n],repeated_raw.ids[n]) && b2Body_IsValid(repeated_raw.ids[n]));
    printf("block latch natural fault frame=%d legal=%d error=%d phase=%d status=%d substeps=%llu active=%d solves=%d full=%d float=%d\n",
        first_fault,completed,first_error,diagnostic.phase,diagnostic.block_report.status,diagnostic.substeps,
        diagnostic.block_report.active_count,diagnostic.block_report.linear_solves,
        diagnostic.block_report.full_checks,diagnostic.block_report.float_checks);
cleanup:
    if (handle && dango_physics_world_destroy(handle)!=0)
    {
        fprintf(stderr,"FAIL block latch: test world destruction failed\n"); result=1;
    }
    if (!result)
    {
        printf("PASS block latch: %d checks; natural frame147 rank fault; three active inputs held; explicit reset recovered146 frames and faulted147 again. Latch contract only; candidate physics remains failed.\n",checks);
    }
    return result;
}
